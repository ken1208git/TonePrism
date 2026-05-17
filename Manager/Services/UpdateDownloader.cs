using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// アップデート zip の download + 展開 + 完全性検証。Phase 4 (#108)。
    ///
    /// SPEC §3.7.3 [5][6]: GitHub Releases から zip を DL → `%TEMP%/GCTonePrism_update_&lt;version&gt;/` に
    /// 展開 → 期待ファイル全揃いの検証。本 class は zip URL を受け取って HttpClient で stream DL し、
    /// progress を IProgress 経由で UI に通知する。
    ///
    /// 完全性検証:
    ///   - zip ルートに Install.bat / INSTALL_README.txt / Launcher.bat / Manager.bat / show_folder_dialog.ps1
    ///   - files/ 配下: Manager/GCTonePrism_Manager.exe / Launcher/GCTonePrism_Launcher.exe /
    ///                  Companions/Updater/GCTonePrism_Updater.exe / Manager/CHANGELOG.md (Phase 4 新規)
    /// SPEC §3.7.3 の検証ステップを Manager UI 側で再実装 (Release.ps1 の Assert-ExpectedFiles と論理同型)。
    /// </summary>
    internal static class UpdateDownloader
    {
        // HttpClient は GitHubReleaseChecker と共有しない (個別 timeout 設定が必要、long-running stream)
        private static readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(() =>
        {
            var c = new HttpClient
            {
                // zip は大きい (数十 MB)、download に分単位かかる場合があるので timeout を長く取る
                Timeout = TimeSpan.FromMinutes(10),
            };
            c.DefaultRequestHeaders.Add("User-Agent", "GCTonePrism-Manager-Download/1.0");
            return c;
        });

        /// <summary>
        /// zip を `targetPath` に download する。progress は (bytesDownloaded, totalBytesOrZero) のペアで通知。
        /// </summary>
        public static async Task DownloadAsync(string url, string targetPath, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            Logger.Info("[UpdateDownloader] DownloadAsync 開始: url=" + url + " target=" + targetPath);
            string targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetParent)) Directory.CreateDirectory(targetParent);

            try
            {
                using (var resp = await _client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    Logger.Info("[UpdateDownloader] HTTP response: status=" + (int)resp.StatusCode + " " + resp.StatusCode + " content-length=" + (resp.Content.Headers.ContentLength ?? -1L));
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? 0L;
                    using (var input = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                    {
                        var buffer = new byte[64 * 1024];
                        long downloaded = 0L;
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            downloaded += read;
                            if (progress != null)
                            {
                                progress.Report(new DownloadProgress { BytesDownloaded = downloaded, TotalBytes = total });
                            }
                        }
                        Logger.Info("[UpdateDownloader] DL 完了: " + downloaded + " bytes → " + targetPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[UpdateDownloader] DownloadAsync 失敗: url=" + url, ex);
                throw;
            }
        }

        /// <summary>zip を `extractDir` に展開する。事前に extractDir が存在する場合は削除する。</summary>
        public static void Extract(string zipPath, string extractDir)
        {
            Logger.Info("[UpdateDownloader] Extract 開始: zip=" + zipPath + " → " + extractDir);
            try
            {
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                Logger.Info("[UpdateDownloader] Extract 完了");
            }
            catch (Exception ex)
            {
                Logger.Error("[UpdateDownloader] Extract 失敗: zip=" + zipPath, ex);
                throw;
            }
        }

        /// <summary>
        /// 展開後の staging dir に期待ファイルが揃っているか検証する。
        /// 不足があれば missing list を返す (空 list = OK)。
        /// </summary>
        public static IReadOnlyList<string> ValidateStaging(string stagingDir)
        {
            Logger.Info("[UpdateDownloader] ValidateStaging 開始: " + stagingDir);
            var missing = new List<string>();
            // zip ルート (= stagingDir 直下)
            string[] rootExpected = new[]
            {
                "Install.bat",
                "INSTALL_README.txt",
                "Launcher.bat",
                "Manager.bat",
                "show_folder_dialog.ps1",
            };
            // files/ 配下
            string[] filesExpected = new[]
            {
                Path.Combine("files", "Launcher", "GCTonePrism_Launcher.exe"),
                Path.Combine("files", "Manager", "GCTonePrism_Manager.exe"),
                Path.Combine("files", "Manager", "CHANGELOG.md"),
                Path.Combine("files", "Companions", "Updater", "GCTonePrism_Updater.exe"),
            };

            foreach (var rel in rootExpected)
            {
                string full = Path.Combine(stagingDir, rel);
                if (!File.Exists(full)) missing.Add(rel);
            }
            foreach (var rel in filesExpected)
            {
                string full = Path.Combine(stagingDir, rel);
                if (!File.Exists(full)) missing.Add(rel);
            }
            if (missing.Count > 0)
            {
                Logger.Warn("[UpdateDownloader] ValidateStaging 不足 " + missing.Count + " 件: " + string.Join(", ", missing));
            }
            else
            {
                Logger.Info("[UpdateDownloader] ValidateStaging OK (全 " + (rootExpected.Length + filesExpected.Length) + " ファイル存在)");
            }
            return missing;
        }

        /// <summary>
        /// staging CHANGELOG.md から最新 Bundle entry を取得して target version と一致するか検証。
        /// 不一致は zip 改竄 / 取り違え疑い、abort 経路 (Manager UI が UpdateDownloader 呼出後に check)。
        /// </summary>
        public static bool ValidateBundleVersion(string stagingDir, Version expectedVersion)
        {
            if (expectedVersion == null)
            {
                Logger.Warn("[UpdateDownloader] ValidateBundleVersion: expectedVersion=null");
                return false;
            }
            string changelogPath = Path.Combine(stagingDir, "files", "Manager", "CHANGELOG.md");
            Logger.Info("[UpdateDownloader] ValidateBundleVersion: expected=" + expectedVersion.ToString(3) + " changelog=" + changelogPath);
            BundleEntry latest = ChangelogParser.TryReadLatestFromFile(changelogPath);
            if (latest == null || latest.Version == null)
            {
                Logger.Warn("[UpdateDownloader] ValidateBundleVersion: CHANGELOG から Bundle entry parse 失敗 (path=" + changelogPath + ")");
                return false;
            }
            bool match = latest.Version == expectedVersion;
            if (match)
            {
                Logger.Info("[UpdateDownloader] ValidateBundleVersion OK: staging=" + latest.Version.ToString(3));
            }
            else
            {
                Logger.Warn("[UpdateDownloader] ValidateBundleVersion 不一致: expected=" + expectedVersion.ToString(3) + " staging=" + latest.Version.ToString(3));
            }
            return match;
        }
    }

    internal sealed class DownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }

        public double Percent
        {
            get
            {
                if (TotalBytes <= 0L) return 0.0;
                return (double)BytesDownloaded / TotalBytes * 100.0;
            }
        }
    }
}
