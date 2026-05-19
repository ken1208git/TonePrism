using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// `<install>/logs/updater/` 配下の Updater log を Manager log に **post-hoc filtered absorb** する。
    /// Manager 起動の session conflict check 通過後 (= `ContinueLoadAfterSessionCheck` 経由) に 1 回呼ばれ、
    /// 未 absorb な `updater_*.log` から Warn/Error + 主要 milestone のみを抽出して Manager 自身の Logger
    /// 経由で記録する。
    ///
    /// 設計意図 (= SPEC §3.6 Companions ログ管理規約):
    /// - Updater は Manager のライフサイクルを跨ぐ (Phase B: Manager dead 期間) ため、リアルタイム pipe redirect
    ///   で親 log に流す pattern が使えない。新 Manager 起動時の post-hoc 読込が唯一の手段。
    /// - 全件取り込みは Manager log を verbose な file copy/verify 行で埋もれさせる UX 悪化を招くため、
    ///   level + milestone marker で絞る。verbose 詳細は Updater 自身の file (= `logs/updater/*.log`) に残る。
    /// - GUI で見れる log source は Launcher / Manager / Monitor の 3 component に収束させる方針
    ///   (= Companion log は親 log の一部として閲覧、Companion 用 tab は増やさない)。
    /// - 重複 absorb 防止は `<install>/logs/updater/.absorbed` text file で管理 (= **order-insensitive set
    ///   persistence**、1 行 1 path、入れ替え順序は意味なし、prune 時に HashSet 反復順で書き直し)。
    ///
    /// 取込責務:
    /// - **多行 entry の継続行を保持** (R2 review H-1): `Logger.Error(string, Exception)` が出す stack trace
    ///   等の継続行 (= LineRegex の `[ts] [LEVEL]` ヘッダを持たない行) は直前 absorb 行の payload に
    ///   append、Manager log への書出も改行込みの単一 entry として記録する。
    /// - **crashed Updater の time-based fallback absorb** (R2 review M-1): 「Updater 終了」marker が
    ///   含まれない file は通常「まだ書込中 race window」として skip するが、`LastWriteTime` から 10 分以上
    ///   経過していれば process 終了確定として absorb 対象に含め、`[CRASHED?]` summary marker + WARN level
    ///   で Manager log に notice する。Updater segfault / kill / OOM 等で永久 skip される silent failure
    ///   path を閉じる。trade-off: Updater が 10 分以上正常稼働する case (= SMB 共有越し大量 file copy 等)
    ///   で誤 crash 判定する path はあるが、現状 Step 2 の rename + copy は数秒 〜 数十秒で完了する想定。
    /// </summary>
    public static class UpdaterLogAbsorber
    {
        // Updater log の行 format は Manager Logger と同一。parse SoT は `LogLineFormat.LineRegex`
        // (LogSectionPanel と共有、format 拡張時の同期 drift 防止)。
        private static readonly Regex LineRegex = LogLineFormat.LineRegex;

        // INFO 行のうち absorb 対象とする milestone marker pattern (大小無視)。
        // 高レベル境界 (Step ヘッダ + 完了 marker) のみを拾う設計、内側 file replace の中間進捗
        // (= FileReplacer.cs の `[Replace 1/2]` / `[Replace 2/2]` rename 段 + copy 段) は intentionally 除外:
        //   - 「rename か copy か」の段階別失敗判別が必要な場合は ERROR/WARN 行で十分判別可能 (= 各段の失敗
        //     は Logger.Error / Logger.Warn で出る、Step 2/4 + 完了 marker + ERROR/WARN の組合せで原因特定可)
        //   - 成功 path で `[Replace N/M]` を absorb すると 1 update につき 2 行増えて Manager log を冗長化
        //   - 深掘り debug は Updater 自身 file (`logs/updater/*.log`) を直接読む運用 (3 component 収束方針)
        //
        // 規約 (SPEC §3.6 INFO milestone success-path 限定):
        //   - **本 regex は success path の INFO milestone のみを対象**、failure event は WARN/ERROR レベル
        //     で出される前提。「rollback」「Manager spawn 失敗」等の failure 言及 INFO を将来追加してはならない
        //     (= 追加する場合は WARN/ERROR にする規約)。これにより本 regex に anchor なし broad alternation を
        //     置いても INFO false positive が構造的に発生しない契約となる。
        //
        // 対象:
        //   - `Updater 起動` / `Updater 終了` (Logger 自身が出すセッション境界)
        //   - `[Step N/M]` Step 1/2/3/4 のヘッダ (Program.cs)
        //   - `Updater 全工程完了` (= 最終成功)
        //   - `Manager 起動完了` / `Manager spawn` 成功 (= Step 3 結果、失敗 path は ERROR 経由で absorb)
        //   - `Manager プロセス終了確認` / `Manager プロセスは既に終了済み` (= Step 1 完了)
        //   - `Manager dir 置換完了` (= Step 2 完了)
        //   - `FATAL` (= 致命的状態を示す ERROR の典型 prefix、ERROR で既に拾われるが念の為)
        //
        // R2 review M-2 で `rollback` alternation を removal:
        //   - 現状 Updater の rollback 言及はすべて WARN/ERROR (FileReplacer.cs L101/L280/L288/L292、
        //     Program.cs L333/L342) で出ているため、WARN/ERROR 全件 absorb 経路で完全 cover、removal で
        //     coverage 損失なし。anchor なし `rollback` alternation が将来 INFO 行に偶然含まれた時の
        //     false positive を予防 (上記 SPEC 規約と整合)。
        private static readonly Regex MilestoneRegex = new Regex(
            @"Updater\s*(起動|終了|全工程完了)" +
            @"|^\[Step\s*\d+/\d+\]" +
            @"|Manager\s*(起動完了|spawn|プロセス終了確認|プロセスは既に終了済み|dir\s*置換完了)" +
            @"|FATAL",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string AbsorbedListFileName = ".absorbed";

        // crashed Updater fallback の閾値 (= file 終端 marker 不在 + LastWriteTime からこの時間以上経過で
        // process 終了確定として absorb 対象に含める)。10 分に設定。Updater は通常 数秒〜数分で完了する
        // 設計なので、10 分超は実質確実に terminated。
        private const int CrashFallbackMinutes = 10;

        // SPEC §3.6「Companion Shutdown marker は `[Logger] <Component> 終了` 行頭 prefix 固定」規約
        // に従う検出 string。Updater Logger.Shutdown が `[Logger] Updater 終了` を出力する specific phrase
        // と一致 (Companions/Updater/Logger.cs:108)、bare `Updater 終了` substring 一致 (R2 実装) より
        // 厳格化することで、将来 error message 中に偶然 "Updater 終了" を含む文字列が出ても誤発火しない
        // (R3 review M-1)。検出 phrase 自体は Logger 内部 prefix `[Logger]` を含むため、application code が
        // 無関係文脈で出す可能性が極めて低い。
        private const string UpdaterShutdownMarker = "[Logger] Updater 終了";

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

                    bool hasShutdownMarker = content.IndexOf(UpdaterShutdownMarker, StringComparison.Ordinal) >= 0;
                    bool crashedFallback = false;
                    if (!hasShutdownMarker)
                    {
                        // Shutdown marker 不在 — 通常 path では「まだ書込中 race window」として skip するが、
                        // LastWriteTime から CrashFallbackMinutes 以上経過していれば process 終了確定として
                        // absorb 対象に含める (R2 review M-1: crashed Updater が永久 skip される path 閉鎖)。
                        DateTime lastWrite;
                        try
                        {
                            lastWrite = new FileInfo(filePath).LastWriteTime;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[UpdaterLogAbsorber] LastWriteTime 取得失敗 (skip): {filePath} — {ex.Message}");
                            continue;
                        }
                        if ((DateTime.Now - lastWrite).TotalMinutes < CrashFallbackMinutes)
                        {
                            // 終端未達 + まだ書込中の可能性 = 次回 Manager 起動で再評価。
                            continue;
                        }
                        crashedFallback = true;
                    }

                    int extracted = AbsorbContent(content);
                    if (crashedFallback)
                    {
                        Logger.Warn(
                            $"[UpdaterLogAbsorber] Updater log を absorb [CRASHED?]: {Path.GetFileName(filePath)} " +
                            $"({extracted} 件、Shutdown marker 不在 + {CrashFallbackMinutes} 分以上書込なし → process 終了確定として absorb)");
                    }
                    else
                    {
                        Logger.Info($"[UpdaterLogAbsorber] Updater log を absorb: {Path.GetFileName(filePath)} ({extracted} 件)");
                    }
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
        //
        // 多行 entry の継続行サポート (R2 review H-1):
        //   `Logger.Error(string, Exception)` は exception 本体を message に埋込む形 (`$"{message}\n{ex}"`)
        //   で出力するため、stack trace 行は LineRegex の `[ts] [LEVEL] ...` ヘッダを持たない継続行となる。
        //   header 行を見たら直前の累積 entry を flush + 新 entry を開始、header 不一致行は active entry
        //   があれば payload に append (= 改行込みの単一 entry として Manager Logger に書出)、active entry が
        //   なければ noise として skip する state machine pattern。
        private static int AbsorbContent(string content)
        {
            int count = 0;
            string activeLevel = null;
            StringBuilder activePayload = null;

            foreach (string raw in content.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                Match m = line.Length == 0 ? null : LineRegex.Match(line);
                bool isHeader = m != null && m.Success;

                if (isHeader)
                {
                    // 新 header 検出 → 直前の active entry を flush
                    if (activeLevel != null)
                    {
                        WriteToManagerLogger(activeLevel, activePayload.ToString());
                        count++;
                        activeLevel = null;
                        activePayload = null;
                    }

                    string level = m.Groups["level"].Value;
                    string ts = m.Groups["ts"].Value;
                    string rest = m.Groups["rest"].Value;

                    bool keep = level == "WARN" || level == "ERROR" || MilestoneRegex.IsMatch(rest);
                    if (!keep) continue; // header だが absorb 対象外 → 後続継続行も accumulate しない

                    activeLevel = level;
                    activePayload = new StringBuilder();
                    activePayload.Append($"[Updater {ts}] {rest}");
                }
                else
                {
                    // header 不一致行 (= 継続行 or 空行)。
                    // active entry があれば payload に append (改行 + 本文)、なければ skip。
                    // R3 review M-3: Manager Logger は CRLF 出力 (UTF-8 BOM-less StreamWriter default
                    // NewLine = `Environment.NewLine` = CRLF on Windows)、entry 間と整合するため継続行
                    // 区切りも `Environment.NewLine` を使用 (= LF 単体だと 1 entry 内が LF / entry 間が
                    // CRLF と mixed line ending になり、log を外部 tool に渡した時の brittleness を生む)。
                    if (activeLevel != null)
                    {
                        activePayload.Append(Environment.NewLine);
                        if (line.Length > 0) activePayload.Append(line);
                    }
                }
            }

            // EOF 時の trailing entry を flush
            if (activeLevel != null)
            {
                WriteToManagerLogger(activeLevel, activePayload.ToString());
                count++;
            }
            return count;
        }

        private static void WriteToManagerLogger(string level, string payload)
        {
            switch (level)
            {
                case "ERROR": Logger.Error(payload); break;
                case "WARN": Logger.Warn(payload); break;
                default: Logger.Info(payload); break;
            }
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
