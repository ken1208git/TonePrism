using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// `<install>/logs/updater/` 配下の Updater log を Manager log に **post-hoc filtered absorb** する。
    /// Manager 起動直後に 1 回呼ばれ、未 absorb な `updater_*.log` から Warn/Error + 主要 milestone のみを
    /// 抽出して Manager 自身の Logger 経由で記録する。
    ///
    /// 設計意図 (= SPEC §3.6 Companions ログ管理規約):
    /// - Updater は Manager のライフサイクルを跨ぐ (Phase B: Manager dead 期間) ため、リアルタイム pipe redirect
    ///   で親 log に流す pattern が使えない。新 Manager 起動時の post-hoc 読込が唯一の手段。
    /// - 全件取り込みは Manager log を verbose な file copy/verify 行で埋もれさせる UX 悪化を招くため、
    ///   level + milestone marker で絞る。verbose 詳細は Updater 自身の file (= `logs/updater/*.log`) に残る。
    /// - GUI で見れる log source は Launcher / Manager / Monitor の 3 component に収束させる方針
    ///   (= Companion log は親 log の一部として閲覧、Companion 用 tab は増やさない)。
    /// - 重複 absorb 防止は `<install>/logs/updater/.absorbed` text file (= 1 行 1 path) で管理。
    /// </summary>
    public static class UpdaterLogAbsorber
    {
        // Updater log の行 format: `[YYYY-MM-DD HH:mm:ss] [LEVEL] message` (= Manager Logger と同一)
        private static readonly Regex LineRegex = new Regex(
            @"^\[(?<ts>[^\]]+)\] \[(?<level>INFO|WARN|ERROR)\] (?<rest>.*)$",
            RegexOptions.Compiled);

        // INFO 行のうち absorb 対象とする milestone marker pattern (大小無視)。
        // 高レベル境界 (Step ヘッダ + 完了 marker) のみを拾う設計、内側 file replace の中間進捗
        // (= FileReplacer.cs の `[Replace 1/2]` / `[Replace 2/2]` rename 段 + copy 段) は intentionally 除外:
        //   - 「rename か copy か」の段階別失敗判別が必要な場合は ERROR/WARN 行で十分判別可能 (= 各段の失敗
        //     は Logger.Error / Logger.Warn で出る、Step 2/4 + 完了 marker + ERROR/WARN の組合せで原因特定可)
        //   - 成功 path で `[Replace N/M]` を absorb すると 1 update につき 2 行増えて Manager log を冗長化
        //   - 深掘り debug は Updater 自身 file (`logs/updater/*.log`) を直接読む運用 (3 component 収束方針)
        // 対象:
        //   - `Updater 起動` / `Updater 終了` (Logger 自身が出すセッション境界)
        //   - `[Step N/M]` Step 1/2/3/4 のヘッダ (Program.cs)
        //   - `Updater 全工程完了` (= 最終成功)
        //   - `Manager 起動完了` / `Manager 起動失敗` / `Manager spawn` (= Step 3 結果)
        //   - `Manager プロセス終了確認` / `Manager プロセスは既に終了済み` (= Step 1 完了)
        //   - `Manager dir 置換完了` (= Step 2 完了)
        //   - `rollback` (= 復旧 path、INFO/WARN/ERROR どれでも該当時は出る)
        //   - `FATAL` (= 致命的状態を示す ERROR の典型 prefix、ERROR で既に拾われるが念の為)
        private static readonly Regex MilestoneRegex = new Regex(
            @"Updater\s*(起動|終了|全工程完了)" +
            @"|^\[Step\s*\d+/\d+\]" +
            @"|Manager\s*(起動完了|起動失敗|spawn|プロセス終了確認|プロセスは既に終了済み|dir\s*置換完了)" +
            @"|rollback" +
            @"|FATAL",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string AbsorbedListFileName = ".absorbed";

        /// <summary>
        /// 未 absorb な Updater log を全て読み込み、抽出行を Manager log に書出。例外は内部で握り潰す
        /// (Manager 起動を阻害しない = SPEC §3.6 「Logger 自身の障害は握り潰す」と同じ defensive 規約)。
        /// </summary>
        public static void AbsorbPendingLogs()
        {
            try
            {
                string updaterLogDir = PathManager.UpdaterLogDir;
                if (!Directory.Exists(updaterLogDir)) return;

                string absorbedListPath = Path.Combine(updaterLogDir, AbsorbedListFileName);
                HashSet<string> absorbedSet = LoadAbsorbedSet(absorbedListPath);

                // 抽出 + write は dir scan の中で逐次行う。新規 absorb 完了したら .absorbed に append。
                var newlyAbsorbed = new List<string>();
                foreach (string filePath in Directory.EnumerateFiles(updaterLogDir, "updater_*.log"))
                {
                    string normalized = NormalizePath(filePath);
                    if (absorbedSet.Contains(normalized)) continue;

                    // 「Updater 終了」行が含まれているファイルだけ absorb 対象とする。含まれていなければ
                    // Updater がまだ書き込み中 (= Phase B/C 中の race) の可能性、次回 Manager 起動で再評価。
                    // これにより部分 absorb (= 終端行未到達のまま mark 済になる事故) を構造的に防止する。
                    string content;
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            content = sr.ReadToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[UpdaterLogAbsorber] read 失敗 (skip): {filePath} — {ex.Message}");
                        continue;
                    }

                    if (content.IndexOf("Updater 終了", StringComparison.Ordinal) < 0)
                    {
                        // 終端未達 = 次回 Manager 起動で再評価。今回は absorb しない (mark もしない)。
                        continue;
                    }

                    int extracted = AbsorbContent(content);
                    Logger.Info($"[UpdaterLogAbsorber] Updater log を absorb: {Path.GetFileName(filePath)} ({extracted} 件)");
                    newlyAbsorbed.Add(normalized);
                }

                if (newlyAbsorbed.Count > 0)
                {
                    AppendAbsorbedList(absorbedListPath, newlyAbsorbed);
                }

                // .absorbed 内の dead path (= Updater Logger の 30 日 retention 後に消えた file) を prune。
                // 失敗しても本流処理には影響なし、best-effort。
                if (absorbedSet.Count > 0)
                {
                    PruneDeadAbsorbedEntries(absorbedListPath, absorbedSet, newlyAbsorbed);
                }
            }
            catch (Exception ex)
            {
                // 絶対に Manager 起動を阻害しない
                try { Logger.Warn($"[UpdaterLogAbsorber] absorb 中に例外: {ex.Message}"); } catch { /* swallow */ }
            }
        }

        // file 内容を行レベルで parse、抽出行を Manager Logger に書出。書出件数を return。
        private static int AbsorbContent(string content)
        {
            int count = 0;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;

                var m = LineRegex.Match(line);
                if (!m.Success) continue;

                string level = m.Groups["level"].Value;
                string ts = m.Groups["ts"].Value;
                string rest = m.Groups["rest"].Value;

                bool keep = level == "WARN" || level == "ERROR" || MilestoneRegex.IsMatch(rest);
                if (!keep) continue;

                // Manager log への出力時、由来 + 元 timestamp を prefix で明示
                string payload = $"[Updater {ts}] {rest}";
                switch (level)
                {
                    case "ERROR": Logger.Error(payload); break;
                    case "WARN": Logger.Warn(payload); break;
                    default: Logger.Info(payload); break;
                }
                count++;
            }
            return count;
        }

        private static HashSet<string> LoadAbsorbedSet(string absorbedListPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(absorbedListPath)) return set;
            try
            {
                foreach (string line in File.ReadAllLines(absorbedListPath, Encoding.UTF8))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0) set.Add(trimmed);
                }
            }
            catch (Exception ex)
            {
                // 既読 list 読込失敗時は安全側で「空」扱い → 全 file が absorb 対象になる = 二重 absorb の
                // リスクを取るが、Manager 起動阻害は避ける方針。
                Logger.Warn($"[UpdaterLogAbsorber] .absorbed 読込失敗、全件未 absorb 扱いで継続: {ex.Message}");
            }
            return set;
        }

        private static void AppendAbsorbedList(string absorbedListPath, List<string> newPaths)
        {
            try
            {
                using (var sw = new StreamWriter(absorbedListPath, append: true, encoding: new UTF8Encoding(false)))
                {
                    foreach (string p in newPaths) sw.WriteLine(p);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[UpdaterLogAbsorber] .absorbed 追記失敗 (次回 Manager 起動で同 log が再 absorb される可能性): {ex.Message}");
            }
        }

        // Updater Logger の 30 日 retention で消えた file path を .absorbed から削除する best-effort 処理。
        // 既存 set + 今回追加分の和集合のうち、現存しない file path は除外して書き直す。
        private static void PruneDeadAbsorbedEntries(string absorbedListPath, HashSet<string> existingSet, List<string> newPaths)
        {
            try
            {
                var union = new HashSet<string>(existingSet, StringComparer.OrdinalIgnoreCase);
                foreach (var p in newPaths) union.Add(p);

                var alive = new List<string>();
                foreach (string p in union)
                {
                    if (File.Exists(p)) alive.Add(p);
                }
                if (alive.Count == union.Count) return; // 変化なし → 書き直さない

                // 全件書き直し (append でなく overwrite)
                using (var sw = new StreamWriter(absorbedListPath, append: false, encoding: new UTF8Encoding(false)))
                {
                    foreach (string p in alive) sw.WriteLine(p);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[UpdaterLogAbsorber] .absorbed prune 失敗 (next run で retry): {ex.Message}");
            }
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }
}
