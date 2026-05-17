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
    ///                  Companions/Updater/GCTonePrism_Updater.exe / CHANGELOG.md (Phase 4 新規、
    ///                  Bundle SoT として `files/` 直下に配置、SPEC §3.7.7)
    /// SPEC §3.7.3 の検証ステップを Manager UI 側で再実装 (Release.ps1 の Assert-ExpectedFiles と論理同型)。
    ///
    /// **重要**: 本クラスの ExpectedFiles リストは `Release.ps1` の `Assert-ExpectedFiles` と
    /// **手動同期** する責務がある (M6)。release 側で追加 / 削除されたものは本クラスにも反映必須、
    /// drift すると C1 系の bug (= 存在しないファイルを要求して apply が永久 abort or 存在するのに
    /// missing 認識して通る) を生む。長期的には manifest (`expected_files.json` 同梱) で SoT 統一する。
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

        /// <summary>
        /// zip を `extractDir` に展開する。caller (= RunUpdateWorker) が事前に `extractDir` を
        /// clean state (既存 zombie 削除 + CreateDirectory) にしている前提なので本 method 内では
        /// dir 削除しない。
        ///
        /// **重要 (Phase 4 bugfix)**: 旧実装は冒頭で `Directory.Delete(extractDir, recursive: true)`
        /// していたが、zip ファイル自体が `extractDir` 内部 (= RunUpdateWorker の zipPath 設計) に
        /// DL されているため、dir 削除で zip も一緒に消え、直後の `ZipFile.ExtractToDirectory` が
        /// FileNotFoundException で落ちる自滅 path があった。caller の clean 責務に統一して解消。
        /// </summary>
        public static void Extract(string zipPath, string extractDir)
        {
            Logger.Info("[UpdateDownloader] Extract 開始: zip=" + zipPath + " → " + extractDir);
            try
            {
                if (!File.Exists(zipPath))
                {
                    throw new FileNotFoundException(
                        "zip ファイルが見つかりません: " + zipPath +
                        " (DL 直後に消失している場合、アンチウイルスの quarantine 疑い)。",
                        zipPath);
                }
                if (!Directory.Exists(extractDir))
                {
                    Directory.CreateDirectory(extractDir);
                }
                // (#108 Phase 4 round 3 M-3) zip-slip pre-extract 検査。`ZipFile.ExtractToDirectory` は
                // entry path を destination dir 直下に強制 normalize しないため、悪意ある zip
                // (entry name = `..\..\Windows\System32\foo.exe`) で extract dir 外を書き換えうる。
                // 信頼境界 (release は repo maintainer のみ) で実害確率は低いが、CI key 漏洩 / repo
                // takeover 時に物理的に Manager dir 外 / system dir まで書き換え可能になる single point
                // failure を、安価な pre-check で塞ぐ。
                string extractDirFull = Path.GetFullPath(extractDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // dir entry (FullName 末尾 `/`) は skip、file のみ check
                        if (string.IsNullOrEmpty(entry.FullName)) continue;
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                            || entry.FullName.EndsWith("\\", StringComparison.Ordinal)) continue;
                        string entryFull = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                        if (!entryFull.StartsWith(extractDirFull, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                "zip 内に extract dir 外を指す entry が含まれます (zip slip 疑い): " +
                                entry.FullName + " → " + entryFull);
                        }
                    }
                }
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
            // (#108 Phase 4 round 2 M7 / codex P1) Release.ps1 `Assert-ExpectedFiles` の `$expected` と
            // **literal 一致** に拡張 (旧版は minimal subset で、`.exe.config` / 各 DLL / SQLite.Interop /
            // version.gd 不在の broken release を素通しして apply 続行 → 新 install が起動失敗、の path
            // があった)。AGENTS.md / SPEC §3.7.8 の「同期必須」規約を文字通り実装、broken release を
            // pre-replacement で検出。
            string[] filesExpected = new[]
            {
                Path.Combine("files", "Launcher", "GCTonePrism_Launcher.exe"),
                Path.Combine("files", "Launcher", "version.gd"),
                Path.Combine("files", "Manager", "GCTonePrism_Manager.exe"),
                Path.Combine("files", "Manager", "GCTonePrism_Manager.exe.config"),
                Path.Combine("files", "Manager", "System.Data.SQLite.dll"),
                Path.Combine("files", "Manager", "Microsoft.WindowsAPICodePack.dll"),
                Path.Combine("files", "Manager", "Microsoft.WindowsAPICodePack.Shell.dll"),
                Path.Combine("files", "Manager", "x64", "SQLite.Interop.dll"),
                Path.Combine("files", "Manager", "x86", "SQLite.Interop.dll"),
                Path.Combine("files", "CHANGELOG.md"),  // C1 fix: `files/` 直下 (SPEC §3.7.7)
                Path.Combine("files", "Companions", "Updater", "GCTonePrism_Updater.exe"),
                Path.Combine("files", "Companions", "Updater", "GCTonePrism_Updater.exe.config"),
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
            // (#108 Phase 4 C1 fix) Bundle SoT は `files/CHANGELOG.md` (= Release.ps1 `Copy-Templates`
            // と同期、SPEC §3.7.7)。旧 path `files/Manager/CHANGELOG.md` は drift 残骸。
            string changelogPath = Path.Combine(stagingDir, "files", "CHANGELOG.md");
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
