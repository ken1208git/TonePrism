using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// バックアップ履歴をファイルシステムから導出するカタログサービス (DB v19 で `backup_log` テーブルを
    /// 廃止した置き換え)。
    ///
    /// 設計意図: 旧実装は履歴メタデータを「バックアップ対象である toneprism.db の中」に持っていたため、
    /// 復元で DB が丸ごと置き換わると履歴とディスク上の実ファイルが恒常的にズレ、そのズレを埋める
    /// reconcile / register 系の後付けコードがバグの温床になっていた。本サービスは `backups/` フォルダの
    /// **ファイル名 + フォルダ位置 + FileInfo** だけから履歴を導出するため、ズレの概念ごと消え、
    /// プロジェクト移動耐性も (常に現在の保存先を走査するので) 自動的に得られる。
    ///
    /// 種類はフォルダ位置で確定: `&lt;保存先&gt;/auto/`→auto、`&lt;保存先&gt;/manual/`→manual、
    /// `&lt;dbDir&gt;/backups/safety/`→safety、保存先直下の旧 `toneprism_*.db`→manual (種類不明は安全側)。
    ///
    /// フォルダパスは <see cref="BackupService"/> から取得して二重実装 (特に M17 危険パス guard) を避ける。
    /// 整合性チェック (PRAGMA quick_check) は走査時には行わない — バックアップ成功確定前に
    /// <see cref="BackupService"/> が既に検証済みで、SMB 越し × 全件 open は重いため。
    /// </summary>
    public class BackupCatalogService
    {
        private readonly BackupService _backupService;

        // host 対応の regex (suffix は 1 グループに集約)。`auto_<yyyyMMdd>_<HHmmss>[_<rest>].db` 形式。
        // <rest> は host / host_<衝突連番> / 旧形式の数値連番 のいずれか (ExtractHost で解釈)。
        private static readonly Regex AutoRegex = new Regex(@"^auto_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);
        private static readonly Regex ManualRegex = new Regex(@"^manual_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);
        private static readonly Regex LegacyRegex = new Regex(@"^toneprism_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);
        // #168 ブランド改名前の旧接頭辞 `prism_` (= toneprism_ より前)。旧 folder-scan は拾わなかったが、
        // 履歴可視性のため走査対象に含める (種類不明なので安全側 manual)。
        private static readonly Regex PrismLegacyRegex = new Regex(@"^prism_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);
        private static readonly Regex SafetyRegex = new Regex(@"^safety_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);
        private static readonly Regex SafetyLegacyRegex = new Regex(@"^safety_before_restore_(\d{8})_(\d{6})(?:_(.+))?\.db$", RegexOptions.IgnoreCase);

        // host 末尾の `_<数値>` (= 同 host 同秒の衝突連番) を切り出す。host 自体は SanitizeHostForFileName で
        // `_` を含まないよう正規化されるため、末尾の `_<数値>` は連番と一意に解釈できる。
        private static readonly Regex TrailingNumericSuffix = new Regex(@"^(.*)_(\d+)$");

        public BackupCatalogService(BackupService backupService)
        {
            _backupService = backupService;
        }

        /// <summary>
        /// auto / manual / 旧 toneprism / (任意で) safety を全て走査し、ファイル名タイムスタンプ降順で返す。
        /// </summary>
        public List<BackupCatalogEntry> ScanAll(bool includeSafety = true)
        {
            var list = new List<BackupCatalogEntry>();
            string root = _backupService.GetEffectiveDestinationDirectory();

            // 保存先直下の旧レイアウト (種類不明 → 安全側で manual)。#168 前の `prism_` 接頭辞も拾う。
            ScanFolder(list, root, "toneprism_*.db", "manual", LegacyRegex);
            ScanFolder(list, root, "prism_*.db", "manual", PrismLegacyRegex);
            // 新レイアウト (フォルダ位置で種類確定)
            ScanFolder(list, Path.Combine(root, "auto"), "auto_*.db", "auto", AutoRegex);
            ScanFolder(list, Path.Combine(root, "manual"), "manual_*.db", "manual", ManualRegex);

            if (includeSafety)
            {
                // safety は保存先設定に関わらず常に <dbDir>/backups/safety/ (BackupService / RestoreService と同じ規約)。
                // 新形式 (safety_<ts>.db) と旧形式 (safety_before_restore_<ts>.db) を 1 回の *.db 走査で両対応。
                ScanFolder(list, _backupService.GetSafetyDirectory(), "*.db", "safety", SafetyRegex, SafetyLegacyRegex);
            }

            return SortNewestFirst(list);
        }

        /// <summary>auto のみ走査 (retention 件数判定 / 削除後の last_backup_at 巻き戻し用)。</summary>
        public List<BackupCatalogEntry> ScanAuto()
        {
            var list = new List<BackupCatalogEntry>();
            string root = _backupService.GetEffectiveDestinationDirectory();
            ScanFolder(list, Path.Combine(root, "auto"), "auto_*.db", "auto", AutoRegex);
            return SortNewestFirst(list);
        }

        /// <summary>最終バックアップ表示用。auto / manual / safety を含めた最新 1 件 (無ければ null)。</summary>
        public BackupCatalogEntry GetLastSuccess()
        {
            return ScanAll(includeSafety: true).FirstOrDefault();
        }

        /// <summary>削除後の last_backup_at 巻き戻し用。最新の auto バックアップ 1 件 (無ければ null)。</summary>
        public BackupCatalogEntry GetLastAuto()
        {
            return ScanAuto().FirstOrDefault();
        }

        private static List<BackupCatalogEntry> SortNewestFirst(List<BackupCatalogEntry> list)
        {
            // ファイル名タイムスタンプ降順。同一秒は FilePath で安定二次ソート。
            // 旧 backup_log の `id DESC` (単調 INSERT 順 = wall-clock 非依存) は失うが、file-scan には id 相当が
            // 無く、タイムスタンプはファイル名そのものなので表示列とも一致する (clock-skew の数件逆転は受容)。
            return list
                .OrderByDescending(e => e.StartedAt)
                .ThenByDescending(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void ScanFolder(List<BackupCatalogEntry> list, string folder, string glob, string trigger, params Regex[] regexes)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, glob);
            }
            catch (Exception ex)
            {
                Logger.Warn("[BackupCatalogService] フォルダ走査に失敗 (skip): " + folder + ": " + ex.Message);
                return;
            }

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                Match match = null;
                foreach (var rx in regexes)
                {
                    var m = rx.Match(name);
                    if (m.Success) { match = m; break; }
                }
                if (match == null) continue;

                if (!TryParseTimestamp(match.Groups[1].Value, match.Groups[2].Value, out long startedAt))
                    continue;

                string rest = match.Groups[3].Success ? match.Groups[3].Value : "";
                string host = ExtractHost(rest);

                long size;
                try { size = new FileInfo(file).Length; }
                catch { continue; }

                string fullPath = file;
                try { fullPath = Path.GetFullPath(file); } catch { /* 走査由来の生 path を identity に使う */ }

                list.Add(new BackupCatalogEntry
                {
                    FilePath = fullPath,
                    TriggerType = trigger,
                    StartedAt = startedAt,
                    PcName = host,
                    FileSizeBytes = size
                });
            }
        }

        private static bool TryParseTimestamp(string dateStr, string timeStr, out long startedAt)
        {
            startedAt = 0;
            if (!DateTime.TryParseExact(dateStr + "_" + timeStr, "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime local))
            {
                return false;
            }
            startedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUnixTimeSeconds();
            return true;
        }

        /// <summary>
        /// ファイル名の suffix 部 (`auto_<ts>` の後ろ) から host を取り出す。
        ///   - 空 → "" (旧 host なし形式)
        ///   - 末尾 `_<数値>` は衝突連番として剥がす (新形式 `host_2` / 旧 LAN 形式 `pcname_1`)
        ///   - 剥がした残りが純数値 → "" (旧 `_2` の host なし衝突連番のみ)
        ///   - それ以外 → host
        /// </summary>
        private static string ExtractHost(string rest)
        {
            if (string.IsNullOrEmpty(rest)) return "";
            string candidate = rest;
            var m = TrailingNumericSuffix.Match(rest);
            if (m.Success) candidate = m.Groups[1].Value;
            if (candidate.Length == 0) return "";
            return candidate.All(char.IsDigit) ? "" : candidate;
        }
    }
}
