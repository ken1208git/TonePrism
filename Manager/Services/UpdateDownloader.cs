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
        /// (#175 Phase 4.1) manifest filename const。Release.ps1 側 `$script:ManifestRelativePath`
        /// と literal 一致が必要 (round 2 Low-3 で SoT 1 箇所統一)。filename を変えるときは両 SoT を
        /// 同期更新すること。
        /// </summary>
        internal const string ManifestFileName = "bundle_manifest.json";

        /// <summary>
        /// (#175 Phase 4.1 round 2 Low-2) `ResolveBundleRoot` の解決結果を staging dir ごとに cache。
        /// 旧実装は 1 update worker 実行中に `ValidateStaging` + `ValidateBundleVersion` +
        /// `RunUpdateWorker` Step 5 直前の 3 箇所で再計算され、それぞれ File.Exists + Logger.Info を
        /// 出していた log noise + 微小性能コスト。本 cache で worker 1 回あたり 1 回の解決に圧縮。
        /// staging dir は worker 1 回ごとに新規 (`%TEMP%/GCTonePrism_update_<ver>/`) なので、process
        /// が長時間生きても cache 肥大は最大数回 entries に留まる。
        /// OrdinalIgnoreCase で Windows path comparison に揃える。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> _bundleRootCache
            = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// (#175 Phase 4.1) staging dir 内の **bundle root** を解決する helper。
        ///
        /// 新 zip 構造 (v0.3.1+): zip 直下 = `Install.bat` / `INSTALL_README.txt`、それ以外
        /// (`Launcher.bat` / `Manager.bat` / `show_folder_dialog.ps1` / `bundle_manifest.json` /
        /// `files/`) は `bundle/` 配下に集約。caller は staging dir 内の path 参照を `bundleRoot`
        /// 経由 (`Path.Combine(bundleRoot, "files", ...)`) で行う。
        ///
        /// 後方互換: 旧 zip 構造 (v0.3.0) は zip 直下に全て配置されていた。本 helper は manifest
        /// (`<staging>/bundle/bundle_manifest.json`) の存在で新/旧を判定し:
        ///   - manifest あれば `bundleRoot = <staging>/bundle` (新構造)
        ///   - manifest 無ければ `bundleRoot = <staging>` (旧構造 = legacy fallback)
        ///
        /// (#175 Phase 4.1 round 1 Medium-2) **manifest 検出 logic の SoT 集約**: 旧実装は本 helper と
        /// `ValidateStaging` で同じ predicate (`File.Exists(<staging>/bundle/bundle_manifest.json)`)
        /// を literal duplication していたが、ValidateStaging も冒頭で本 helper を呼んで
        /// `bundleRoot != stagingDir` で経路分岐する形に集約済 (= manifest path 名 / 位置を変えた時に
        /// 片方だけ更新する drift bug の温床を closure)。
        ///
        /// (#175 Phase 4.1 round 1 Low-2) v0.3.0 install からの self-validate は正常 path のため log
        /// level を Warn → Info に降格 (旧実装は alarm が日常 self-update でも出ていた)。
        ///
        /// (#175 Phase 4.1 round 2 Low-2) 同 stagingDir に対する複数回呼出は cache から返却、
        /// File.Exists + Logger.Info の重複を回避。
        ///
        /// 注意: legacy fallback path (= v0.3.0 install の Manager から v0.3.0 zip を取得する path)
        /// は v0.3.1 以降の zip 構造変更には対応できない (= 旧 Manager が新 zip 構造を validate
        /// できず手動 install 必要)。Phase 4.1 (#175) の trade-off。
        /// </summary>
        public static string ResolveBundleRoot(string stagingDir)
        {
            string cached;
            lock (_bundleRootCache)
            {
                if (_bundleRootCache.TryGetValue(stagingDir, out cached)) return cached;
            }
            string manifestPath = Path.Combine(stagingDir, "bundle", ManifestFileName);
            string bundleRoot;
            if (File.Exists(manifestPath))
            {
                bundleRoot = Path.Combine(stagingDir, "bundle");
                Logger.Info("[UpdateDownloader] ResolveBundleRoot: manifest 検出 (新構造) → " + bundleRoot);
            }
            else
            {
                bundleRoot = stagingDir;
                Logger.Info("[UpdateDownloader] ResolveBundleRoot: manifest 不在 (旧構造 v0.3.0 fallback) → " + stagingDir);
            }
            lock (_bundleRootCache)
            {
                _bundleRootCache[stagingDir] = bundleRoot;
            }
            return bundleRoot;
        }

        /// <summary>
        /// 展開後の staging dir に期待ファイルが揃っているか検証する。
        /// 不足があれば missing list を返す (空 list = OK)。
        ///
        /// (#175 Phase 4.1) 3 経路で分岐:
        ///   (a) **新構造 (manifest 検出)** (v0.3.1+): `<staging>/bundle/bundle_manifest.json` を読み
        ///       「list 通り存在するか」を check。**zip ごとに新 manifest が新 file 構造を表現する**
        ///       ので、Manager 側 `ValidateStaging` の validate fence の drift を closure する
        ///       (= zip 構造変更時に Manager 側 hardcoded list を同期更新する規約が不要になる)。
        ///       ⚠ **apply 側 (`UpdateSectionPanel.RunUpdateWorker` Step 5-9) は依然 path hardcoded**
        ///       のため、将来 `bundle/files/Launcher/` を別 dir 名に変えると validate は通るが apply
        ///       で fail する partial-state を生む。「Manager コード変更ゼロで全 dir 構造変更を吸収」
        ///       は overstated、apply 側の同期は別途必要 (round 1 High-1、別 issue 化想定)。
        ///   (b) **broken release (manifest なしだが bundle/ あり)** (v0.3.1+ 想定だが zip 同梱漏れ /
        ///       manifest parse 失敗等): legacy fallback は staging 直下に `Launcher.bat` 等を期待
        ///       するため必ず 3 件 missing で fail、user 体験が悪い。明示 missing sentinel を返却
        ///       して「broken release 疑い、再 DL 推奨」を上位 UI に伝える (round 1 High-2 + Medium-3)。
        ///   (c) **legacy hardcoded list (manifest なし + bundle/ なし)** (v0.3.0): Phase 4 PR #161
        ///       で確立した hardcoded list で fallback。**path 変更には対応不可** (= v0.3.0 install
        ///       の Manager から v0.3.1 への update path で再発するため、Phase 4.1 release で
        ///       v0.3.0 install からの自動 update flow は破綻、手動 install が必要)。
        /// </summary>
        public static IReadOnlyList<string> ValidateStaging(string stagingDir, out BundleManifest manifest)
        {
            manifest = null;
            Logger.Info("[UpdateDownloader] ValidateStaging 開始: " + stagingDir);
            // (#175 Phase 4.1 round 1 Medium-2) manifest 検出 logic は ResolveBundleRoot に集約。
            // bundleRoot != stagingDir なら新構造 (manifest あり)、== なら manifest なし (旧構造 or broken)。
            string bundleRoot = ResolveBundleRoot(stagingDir);
            if (!string.Equals(bundleRoot, stagingDir, StringComparison.OrdinalIgnoreCase))
            {
                // 新構造 (manifest 検出済)、manifest 経由検証
                // (#177) parse 成功時の manifest object を out param で caller に流す。caller (UpdateSectionPanel)
                // が `manifest?.Layout` 経由で apply 側 path を解決する forward compat 機構。
                Logger.Info("[UpdateDownloader] ValidateStaging: manifest 経由 (新構造、forward compat path)");
                return ValidateStagingViaManifest(bundleRoot, out manifest);
            }
            // bundleRoot == stagingDir = manifest 不在。ただし `bundle/` dir だけある case は broken
            // release (v0.3.1+ zip で manifest 同梱漏れ等) として legacy fallback に流さず明示 abort。
            // (#175 Phase 4.1 round 1 High-2 + Medium-3)
            if (Directory.Exists(Path.Combine(stagingDir, "bundle")))
            {
                Logger.Error("[UpdateDownloader] ValidateStaging: bundle/ あり + manifest なし、broken release 疑い (再 DL 推奨)");
                // (#175 Phase 4.1 round 2 Low-5) Windows path separator 統一 (manifest 経路 missing と同じ
                // backslash 表記、user 視点で「同じ string 表記の path」感を保つ)。
                return new List<string>
                {
                    Path.Combine("bundle", ManifestFileName) + " (broken release 疑い: bundle/ dir はあるが manifest 不在、zip 同梱漏れの可能性。再 DL を試してください)",
                };
            }
            // (#175 Phase 4.1 round 1 Low-2) 旧構造 self-update は正常 path、Warn → Info 降格。
            // (#177) v0.3.0 legacy 経路は manifest 自体不在のため manifest = null で caller の null-coalesce
            // fallback に倒す (= hardcoded legacy path で apply、v0.3.0 から v0.3.1 への self-update と同型)。
            Logger.Info("[UpdateDownloader] ValidateStaging: 旧構造 v0.3.0 legacy fallback (forward compat 制限あり)");
            return ValidateStagingLegacy(stagingDir);
        }

        /// <summary>
        /// (#175 Phase 4.1 round 1 Medium-2 で抽出) manifest 経由検証の本体。`ValidateStaging` の
        /// 新構造分岐から呼ばれる。manifest parse 失敗 / 例外時は broken release 扱いで abort sentinel
        /// を返却し、legacy fallback には降格しない (= v0.3.1+ 構造で legacy が必ず fail する path を
        /// 物理的に避ける、round 1 High-2)。
        /// </summary>
        private static IReadOnlyList<string> ValidateStagingViaManifest(string bundleRoot, out BundleManifest manifest)
        {
            manifest = null;
            string manifestPath = Path.Combine(bundleRoot, "bundle_manifest.json");
            try
            {
                BundleManifest parsed = ReadBundleManifest(manifestPath);
                if (parsed == null || parsed.Files == null)
                {
                    // (#175 Phase 4.1 round 1 High-2) parse 失敗 = broken/corrupted manifest。
                    // legacy fallback は staging 直下 `Launcher.bat` を期待するが新構造では bundle/
                    // 配下にしかないため必ず 3 件 missing で fail。silent な誤判定を防ぐため明示 abort。
                    // (#175 Phase 4.1 round 2 Low-5) Windows path separator 統一 (`Path.Combine` 経由)。
                    Logger.Error("[UpdateDownloader] ValidateStaging: manifest parse 失敗、broken release 疑い (再 DL 推奨)");
                    return new List<string>
                    {
                        Path.Combine("bundle", ManifestFileName) + " (broken release 疑い: parse 失敗、zip 破損 / schema 不一致の可能性。再 DL を試してください)",
                    };
                }
                manifest = parsed;  // (#177) parse 成功時のみ out param に流す、caller の apply 側 layout 経由 path 解決に使う
                var missing = new List<string>();
                foreach (string rel in parsed.Files)
                {
                    // JSON 上は `/` separator で記録されているので Windows 用に変換
                    string relWin = rel.Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.Combine(bundleRoot, relWin);
                    // (#175 Phase 4.1 round 2 Low-4) schema_version 1 では manifest files は file path のみ
                    // (dir entry なし、`ReadBundleManifest` docstring 参照)。旧実装の `Directory.Exists`
                    // check は dead branch だったため削除して `File.Exists` 1 行に簡素化。将来 dir entry を
                    // 許容する場合は本 check + schema_version bump で再導入する設計。
                    if (!File.Exists(full))
                    {
                        missing.Add(Path.Combine("bundle", relWin));
                    }
                }
                if (missing.Count > 0)
                {
                    Logger.Warn("[UpdateDownloader] ValidateStaging 不足 " + missing.Count + " 件 (manifest 経由): " + string.Join(", ", missing));
                }
                else
                {
                    Logger.Info("[UpdateDownloader] ValidateStaging OK (manifest 経由、全 " + parsed.Files.Count + " ファイル存在)");
                }
                return missing;
            }
            catch (Exception ex)
            {
                // (#175 Phase 4.1 round 1 High-2) 例外時も broken release 扱いで abort sentinel
                // (#175 Phase 4.1 round 2 Low-5) Windows path separator 統一
                Logger.Error("[UpdateDownloader] ValidateStaging: manifest 検証で例外、broken release 疑い (再 DL 推奨): " + ex.Message);
                return new List<string>
                {
                    Path.Combine("bundle", ManifestFileName) + " (broken release 疑い: 検証中に例外、" + ex.GetType().Name + ": " + ex.Message + "。再 DL を試してください)",
                };
            }
        }

        /// <summary>
        /// (#175 Phase 4.1) Legacy 旧構造 (v0.3.0) 用の hardcoded list 検証 path。
        /// Phase 4 PR #161 round 1 C1 fix で確立した list をそのまま保持。新規 caller は使わず、
        /// `ValidateStaging` 内の manifest fallback 経路としてのみ呼ばれる。
        /// </summary>
        private static IReadOnlyList<string> ValidateStagingLegacy(string stagingDir)
        {
            var missing = new List<string>();
            // 旧 zip ルート (= stagingDir 直下、v0.3.0 構造)
            string[] rootExpected = new[]
            {
                "Install.bat",
                "INSTALL_README.txt",
                "Launcher.bat",
                "Manager.bat",
                "show_folder_dialog.ps1",
            };
            // 旧 files/ 配下 (v0.3.0 構造)
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
                Path.Combine("files", "CHANGELOG.md"),
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
                Logger.Warn("[UpdateDownloader] ValidateStagingLegacy 不足 " + missing.Count + " 件: " + string.Join(", ", missing));
            }
            else
            {
                Logger.Info("[UpdateDownloader] ValidateStagingLegacy OK (全 " + (rootExpected.Length + filesExpected.Length) + " ファイル存在)");
            }
            return missing;
        }

        /// <summary>
        /// (#175 Phase 4.1) `bundle/bundle_manifest.json` を読み込んで `BundleManifest` POCO に変換。
        /// schema_version 1 を想定。失敗時は null を返す (caller は legacy fallback に降格)。
        /// </summary>
        private const int SupportedManifestSchemaVersion = 1;

        private static BundleManifest ReadBundleManifest(string manifestPath)
        {
            try
            {
                string json = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dict = ser.DeserializeObject(json) as System.Collections.Generic.IDictionary<string, object>;
                if (dict == null) return null;
                var manifest = new BundleManifest();
                object bv;
                if (dict.TryGetValue("bundle_version", out bv) && bv != null) manifest.BundleVersion = bv.ToString();
                object ga;
                if (dict.TryGetValue("generated_at", out ga) && ga != null) manifest.GeneratedAt = ga.ToString();
                object sv;
                if (dict.TryGetValue("schema_version", out sv) && sv != null)
                {
                    int parsed;
                    if (int.TryParse(sv.ToString(), out parsed)) manifest.SchemaVersion = parsed;
                }
                // (#175 Phase 4.1 round 1 Medium-1) schema_version 検査。将来 schema_version 2 で
                // field semantics が変わった (例: files が {name, sha256} object 配列に拡張) 場合、
                // 旧 Manager は `as object[]` cast で偶然 null を取って silent な validate skip →
                // broken release 誤判定の path がある。unknown schema は明示的に parse 不可と扱い、
                // caller (ValidateStagingViaManifest) で broken sentinel 経路に倒す。
                if (manifest.SchemaVersion != SupportedManifestSchemaVersion)
                {
                    Logger.Warn("[UpdateDownloader] ReadBundleManifest: 未対応 schema_version=" + manifest.SchemaVersion +
                        " (本 Manager は v" + SupportedManifestSchemaVersion + " のみ対応)、parse 失敗扱いに降格");
                    return null;
                }
                object filesObj;
                if (dict.TryGetValue("files", out filesObj))
                {
                    var arr = filesObj as object[];
                    if (arr != null)
                    {
                        manifest.Files = new List<string>(arr.Length);
                        foreach (var item in arr)
                        {
                            if (item != null) manifest.Files.Add(item.ToString());
                        }
                    }
                }
                // (#177) layout (optional additive field、apply 側 path 解決用): v0.3.1 以前の manifest は
                // layout 不在 → manifest.Layout = null、caller は null-coalesce で hardcoded legacy path に
                // fallback。parse 失敗 (型不整合 / 部分 null 等) も silent 削除して null fallback、validate
                // path 全体を fail-soft に保つ。
                object layoutObj;
                if (dict.TryGetValue("layout", out layoutObj))
                {
                    var layoutDict = layoutObj as System.Collections.Generic.IDictionary<string, object>;
                    if (layoutDict != null)
                    {
                        manifest.Layout = new BundleLayout
                        {
                            LauncherDir   = TryGetLayoutString(layoutDict, "launcher_dir"),
                            ManagerDir    = TryGetLayoutString(layoutDict, "manager_dir"),
                            CompanionsDir = TryGetLayoutString(layoutDict, "companions_dir"),
                            UpdaterDir    = TryGetLayoutString(layoutDict, "updater_dir"),
                            LauncherBat   = TryGetLayoutString(layoutDict, "launcher_bat"),
                            ManagerBat    = TryGetLayoutString(layoutDict, "manager_bat"),
                            ChangelogMd   = TryGetLayoutString(layoutDict, "changelog_md"),
                        };
                    }
                }
                Logger.Info("[UpdateDownloader] ReadBundleManifest OK: bundle_version=" + (manifest.BundleVersion ?? "(null)") +
                    " schema_version=" + manifest.SchemaVersion + " files=" + (manifest.Files == null ? 0 : manifest.Files.Count) +
                    " layout=" + (manifest.Layout == null ? "null" : "present"));
                return manifest;
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateDownloader] ReadBundleManifest 失敗: " + manifestPath + " ex=" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// (#177) manifest layout dict から指定 key の string 値を抽出。値不在 / null は null 返却で
        /// caller の null-coalesce fallback path に倒す。
        /// </summary>
        private static string TryGetLayoutString(System.Collections.Generic.IDictionary<string, object> dict, string key)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                return val.ToString();
            }
            return null;
        }

        /// <summary>
        /// staging CHANGELOG.md から最新 Bundle entry を取得して target version と一致するか検証。
        /// 不一致は zip 改竄 / 取り違え疑い、abort 経路 (Manager UI が UpdateDownloader 呼出後に check)。
        /// (#108 Phase 4 round 5 L-5) `out Version stagingVer` で staging 側の parse 結果を caller に
        /// 返す (error message に含めて UI に staging version を表示)。
        /// (#108 Phase 4 round 7 L-3) `false` 返却 case は 2 種、`stagingVer` の値で識別可能:
        ///   (a) staging CHANGELOG 不在 / 読込失敗 / parse 失敗 → `stagingVer == null` + return false
        ///   (b) staging Bundle version != expectedVersion (zip 改竄 / 取り違え疑い) → `stagingVer != null`
        ///       (mismatch 値) + return false
        /// caller (UpdateSectionPanel) は (b) のみ UI に「staging: vX.Y.Z / 期待: vA.B.C」を表示、
        /// (a) は「CHANGELOG が見つかりません」固定文言に縮退する想定。
        /// </summary>
        public static bool ValidateBundleVersion(string stagingDir, Version expectedVersion, out Version stagingVer)
        {
            stagingVer = null;
            if (expectedVersion == null)
            {
                Logger.Warn("[UpdateDownloader] ValidateBundleVersion: expectedVersion=null");
                return false;
            }
            // (#108 Phase 4 C1 fix) Bundle SoT は `files/CHANGELOG.md` (= Release.ps1 `Copy-Templates`
            // と同期、SPEC §3.7.7)。旧 path `files/Manager/CHANGELOG.md` は drift 残骸。
            // (#175 Phase 4.1) bundleRoot 経由に変更 (新構造で `<staging>/bundle/files/CHANGELOG.md`、
            // 旧構造 fallback で `<staging>/files/CHANGELOG.md`)。
            string bundleRoot = ResolveBundleRoot(stagingDir);
            string changelogPath = Path.Combine(bundleRoot, "files", "CHANGELOG.md");
            Logger.Info("[UpdateDownloader] ValidateBundleVersion: expected=" + expectedVersion.ToString(3) + " changelog=" + changelogPath);
            BundleEntry latest = ChangelogParser.TryReadLatestFromFile(changelogPath);
            if (latest == null || latest.Version == null)
            {
                Logger.Warn("[UpdateDownloader] ValidateBundleVersion: CHANGELOG から Bundle entry parse 失敗 (path=" + changelogPath + ")");
                return false;
            }
            stagingVer = latest.Version;
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

    /// <summary>
    /// (#175 Phase 4.1) `bundle/bundle_manifest.json` の deserialize 結果 POCO。schema_version 1 を想定。
    ///
    /// **Schema 進化方針** (#177 で更新): 「全 field 追加で schema_version bump」は conservative すぎる
    /// ため、以下のように区別する:
    ///   - **既存 field の semantics 変更** (例: `Files` の型を `[string]` → `[{name, sha256}]` に拡張、
    ///     `BundleVersion` の format 変更、field 削除等) は **schema_version bump 必須**。旧 reader が
    ///     `as object[]` cast 等で偶然 null を取って silent な validate skip → broken release 誤判定の
    ///     path を防ぐため (Phase 4.1 round 1 Medium-1 で導入された schema_version != 1 reject fence の
    ///     原則)。
    ///   - **新 optional field の追加** (= 旧 reader が `TryGetValue` で無視できる additive change) は
    ///     **schema_version=1 のまま forward compat 維持**。`JavaScriptSerializer.DeserializeObject` は
    ///     `IDictionary&lt;string, object&gt;` に展開してから必要 field のみ `TryGetValue` で取り出すため、
    ///     未知 field は dict 内に残るが POCO 側で参照しなければ黙殺される標準的 JSON forward compat
    ///     pattern。本 PR (#177) の `Layout` 追加が初の事例で、v0.3.1 同梱 Manager (v0.9.1) も新 manifest
    ///     を silent に parse success できる (PowerShell 実機 verify 済、PR #180 round 1 Low-2 と同 pattern)。
    /// </summary>
    internal sealed class BundleManifest
    {
        /// <summary>manifest 生成時の Bundle version 文字列 (例: "0.3.1")。</summary>
        public string BundleVersion { get; set; }
        /// <summary>manifest 生成時刻 (ISO 8601 UTC、例: "2026-05-18T01:30:00Z")。</summary>
        public string GeneratedAt { get; set; }
        /// <summary>schema バージョン (現状 1、breaking change 時のみ bump、additive field 追加では bump しない)。</summary>
        public int SchemaVersion { get; set; }
        /// <summary>bundle/ からの相対 file path リスト (JSON 上は `/` separator)。</summary>
        public System.Collections.Generic.List<string> Files { get; set; }
        /// <summary>
        /// (#177) apply 側 forward compat の category → path mapping。`UpdateSectionPanel.RunUpdateWorker`
        /// が hardcoded path の代わりに本 layout 経由で path 解決する (Phase 4.1+ で導入)。null 許容:
        /// v0.3.1 manifest (本 PR 以前) は layout 不在のため null、caller は null-coalesce で hardcoded
        /// legacy path に fallback する設計。新規 component 追加時は本 POCO + Release.ps1
        /// `$script:BundleLayout` の両方を SoT 同期更新する (SPEC §3.7.8 チェックリスト参照)。
        /// </summary>
        public BundleLayout Layout { get; set; }
    }

    /// <summary>
    /// (#177) `bundle_manifest.json` の `layout` field の deserialize 結果。category 名 → zip 内
    /// 相対 path (`/` separator) の mapping。`BundleManifest.Layout` から参照される。
    ///
    /// **Serializer 切替時の注意**: 本 class は C# 慣例 PascalCase property を持ち、JSON wire format は
    /// snake_case (writer = Release.ps1 `$script:BundleLayout` の hashtable)。現状の
    /// `System.Web.Script.Serialization.JavaScriptSerializer` は case-insensitive deserialize で互換性が
    /// 成立しているが、将来 `System.Text.Json` 等の case-sensitive default serializer へ切替える場合、
    /// wire 名 mapping を別途設定する必要がある (例: `[JsonPropertyName("launcher_dir")]` 等)。
    /// 切替時は wire format との対応を再検証すること。
    ///
    /// **legacy compat**: 各 property の値が null の場合、caller は hardcoded legacy path に fallback
    /// する想定 (`manifest?.Layout?.LauncherDir ?? "files/Launcher"` 等の null-coalesce pattern)。
    /// </summary>
    internal sealed class BundleLayout
    {
        /// <summary>Launcher dir の zip 内相対 path (例: "files/Launcher")。</summary>
        public string LauncherDir { get; set; }
        /// <summary>Manager dir の zip 内相対 path (例: "files/Manager")。</summary>
        public string ManagerDir { get; set; }
        /// <summary>Companions root dir の zip 内相対 path (例: "files/Companions")。</summary>
        public string CompanionsDir { get; set; }
        /// <summary>Updater dir の zip 内相対 path (例: "files/Companions/Updater")。</summary>
        public string UpdaterDir { get; set; }
        /// <summary>Launcher.bat shortcut の zip 内相対 path (例: "Launcher.bat")。</summary>
        public string LauncherBat { get; set; }
        /// <summary>Manager.bat shortcut の zip 内相対 path (例: "Manager.bat")。</summary>
        public string ManagerBat { get; set; }
        /// <summary>CHANGELOG.md の zip 内相対 path (例: "files/CHANGELOG.md")。</summary>
        public string ChangelogMd { get; set; }
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
