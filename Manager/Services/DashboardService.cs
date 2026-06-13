using System;
using System.Collections.Generic;
using System.Linq;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>finding の重大度。Critical だけが総合ステータス盾を赤くする (= 本当に壊れてる)。</summary>
    public enum FindingSeverity { Critical, Recommended, Info }

    /// <summary>
    /// (ダッシュボード) 要対応チェックリストの 1 項目。<see cref="Id"/> は × (非表示) の永続キーになる安定識別子で、
    /// 同じ問題が残り続ける限り同 Id = 一度 × すれば再表示されない (settings に保存)。
    /// </summary>
    public sealed class DashboardFinding
    {
        public string Id { get; set; }
        public FindingSeverity Severity { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        /// <summary>
        /// Critical（起動不能 / スキーマ未完）は × で黙らせられない＝必ず表示。盾の存在意義（本当に壊れてる物の
        /// 表面化）を、誤クリックや恣意的な × で無効化させないための防御（PR #372 review #1）。
        /// </summary>
        public bool CanDismiss => Severity != FindingSeverity.Critical;
    }

    /// <summary>
    /// (ダッシュボード) Manager の概況 + 要対応チェックリストのスナップショット。構成 / データの **pull スナップショット**で
    /// 「準備は整ってるか・直すべき不備は無いか」を一目で示す。`Gather` 自体は取得時点の one-shot だが、`DashboardPage` が
    /// near-real-time で再取得する（<see cref="LauncherSessionCount"/> バッジ=3 秒 / 重い全体=約20 秒）。
    /// **Monitor (#91) とは別系統**: Monitor は push 型の継続稼働監視 + 通知 / 自動再起動を担うのに対し、本ダッシュボードは
    /// pull 型で「今ちゃんと動いてるか」の watchdog ではなく「準備が整ってるか」を示すに留める。各 field は取得失敗時に既定値 / null。
    /// </summary>
    public sealed class DashboardSnapshot
    {
        // 登録コンテンツ (タイル)
        public int GameCount { get; set; }
        public int VisibleGameCount { get; set; }
        public int StoreSectionCount { get; set; }
        public int IntroSlideCount { get; set; }

        // バックアップ (タイル)
        public DateTime? LastBackupAt { get; set; }
        public string LastBackupTrigger { get; set; }
        public int BackupCount { get; set; }
        public long BackupTotalBytes { get; set; }

        // システム: LAN 全体で稼働中のランチャー (取得時点の one-shot、stale 除外済)。0 = どのPCでも停止中。
        public int LauncherSessionCount { get; set; }
        public List<string> LauncherPcNames { get; set; } = new List<string>();

        // 要対応リスト (× で振り分け済)。ActiveFindings = 表示中 / DismissedFindings = 非表示にした項目。
        public List<DashboardFinding> ActiveFindings { get; set; } = new List<DashboardFinding>();
        public List<DashboardFinding> DismissedFindings { get; set; } = new List<DashboardFinding>();

        /// <summary>収集中に例外が出て一部が既定値のまま (UI で注意表示)。</summary>
        public bool Failed { get; set; }
    }

    /// <summary>
    /// (ダッシュボード) LAN-wide ランチャー稼働の軽量スナップショット。3 秒間隔のバッジ更新で重い全体 Gather を
    /// 回さずこれだけ取るために分離。
    /// </summary>
    public sealed class LauncherStatus
    {
        public int Count { get; set; }
        public List<string> PcNames { get; set; } = new List<string>();
    }

    /// <summary>
    /// 概況 + 要対応チェックリストを集める。SMB 越し I/O (backups/ 走査・games/ フォルダ突き合わせ) を含むため、
    /// **呼び出し側はバックグラウンドスレッドで実行**して UI を固めないこと (DashboardPage は Task.Run で呼ぶ)。
    /// repository は呼び出しごとに自前の接続を開くのでスレッド跨ぎでも安全。区画ごとに try/catch で fail-soft
    /// (片方が落ちても他は出す)。要対応リストは登録不備 (recon) のみを部員に分かるプレーン日本語で出す方針で、
    /// 開発者向けの生ログ (WARN/ERROR) はここには載せない (部員に意味不明なため。生ログは『ログ』タブが SoT)。
    /// </summary>
    public static class DashboardService
    {
        public static DashboardSnapshot Gather(DatabaseManager db, LauncherSessionService launcherSessions)
        {
            var snap = new DashboardSnapshot();
            if (db == null) { snap.Failed = true; return snap; }

            // games は概況の件数と「画像未設定」findings の両方で使うので try の外に出す。
            List<GameInfo> games = null;

            // ----- 概況 (タイル) -----
            try
            {
                games = db.GetAllGames();
                snap.GameCount = games.Count;
                snap.VisibleGameCount = games.Count(g => g.IsVisible);
                snap.StoreSectionCount = db.GetAllSections().Count;
                snap.IntroSlideCount = db.GetAllIntroSlides().Count;

                var backups = db.BackupCatalogService.ScanAll(includeSafety: true);
                snap.BackupCount = backups.Count;
                snap.BackupTotalBytes = backups.Sum(b => b.FileSizeBytes);
                var last = backups.FirstOrDefault();
                if (last != null)
                {
                    snap.LastBackupAt = DateTimeOffset.FromUnixTimeSeconds(last.StartedAt).LocalDateTime;
                    snap.LastBackupTrigger = last.TriggerType;
                }

                // LAN 全体で稼働中のランチャーを検出 (= 別PCのキオスク含む)。バッジの 3 秒間隔更新と同じ
                // DetectLauncher を共有して結果を一致させる。
                var launcher = DetectLauncher(launcherSessions);
                snap.LauncherSessionCount = launcher.Count;
                snap.LauncherPcNames = launcher.PcNames;
            }
            catch (Exception ex)
            {
                snap.Failed = true;
                Logger.Warn("[DashboardService] 概況収集で例外（一部 default）: " + ex.Message);
            }

            // ----- 要対応 findings -----
            var findings = new List<DashboardFinding>();

            // 登録不備: 復元整合チェック (現 DB↔games フォルダの突き合わせ) を流用。起動不能 exe / 画像欠落 /
            // 版フォルダ欠落 / 孤児フォルダ / スキーマ未完 を 1 回の Analyze で全部拾える。すべて部員に分かる文言。
            // 開発者向けの生ログ (WARN/ERROR) はここには出さない (部員に意味不明なため、『ログ』タブが SoT)。
            try
            {
                var recon = new RestoreReconciliationService(db).Analyze();
                // Analyze は GetAllGames 失敗等で**例外を投げず** AnalysisFailed=true + 空 findings を返す
                // (RestoreReconciliationService.cs)。これを拾わないと「チェックは失敗したのに 0 件＝all-clear」
                // という最悪の誤表示になる (チェックリストの存在意義は問題の表面化)。snap.Failed に反映する。
                if (recon.AnalysisFailed)
                {
                    snap.Failed = true;
                    Logger.Warn("[DashboardService] 登録不備チェックが完了できませんでした: " + recon.AnalysisError);
                }
                AddReconFindings(findings, recon);
            }
            catch (Exception ex)
            {
                snap.Failed = true;
                Logger.Warn("[DashboardService] 登録不備チェックで例外: " + ex.Message);
            }

            // 画像未設定: そもそもサムネ/背景が割当てられていないゲーム。recon の「設定済みだが欠落」とは別軸で、
            // 最も軽い Info 扱い (一応の気付き、意図的なら × で消せる)。
            try
            {
                AddUnsetImageFindings(findings, games);
            }
            catch (Exception ex)
            {
                Logger.Warn("[DashboardService] 画像未設定チェックで例外: " + ex.Message);
            }

            // × 振り分け (settings 永続)。dismissed に含まれる Id は非表示リストへ。
            HashSet<string> dismissed = LoadDismissed(db);
            foreach (var f in findings)
            {
                // Critical (CanDismiss=false) は × されても必ず active に残す。万一 settings に Critical の Id が
                // 残っていても黙らせない＝盾の偽グリーンを構造的に防ぐ (review #1)。
                if (f.CanDismiss && dismissed.Contains(f.Id)) snap.DismissedFindings.Add(f);
                else snap.ActiveFindings.Add(f);
            }

            return snap;
        }

        /// <summary>
        /// LAN 全体で稼働中のランチャーを検出 (= 別PCのキオスク含む)。編集前競合チェックと同じ
        /// LauncherSessionService (responses/launcher_sessions/*.json の heartbeat、stale 除外済) を流用。
        /// read-only ファイルスキャンで背景スレッド安全・内部 fail-soft。バッジ専用の軽量経路 (3 秒間隔) と
        /// 全体 Gather の両方から呼ぶ。
        /// </summary>
        public static LauncherStatus DetectLauncher(LauncherSessionService launcherSessions)
        {
            var st = new LauncherStatus();
            try
            {
                if (launcherSessions != null)
                {
                    var sessions = launcherSessions.DetectActiveLauncherSessions();
                    st.Count = sessions.Count;
                    st.PcNames = sessions
                        .Select(x => x.PcName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch (Exception ex) { Logger.Warn("[DashboardService] ランチャー検出失敗: " + ex.Message); }
            return st;
        }

        // RestoreReconciliationResult を finding 群へ変換する。重大度の線引きは「来場者が"今"プレイする物をブロックするか」:
        //   Critical = アクティブ版の起動不能 (BrokenGames) と DB スキーマ未完 (= 表示中のゲーム / DB が壊れてる)。
        //   Recommended = 非アクティブ版の不備 (別バージョン不備=BrokenVersions / 版フォルダ欠落)・画像欠落・スライド画像欠落
        //     (= 表示中の版は無事で、版切替時か見栄えにしか影響しない)。
        //   Info = 孤児フォルダ (DB に無い余分なフォルダ＝掃除対象)。
        // 注: recon の HasCriticalFindings は BrokenVersions も critical 扱いだが、ダッシュボードは「今ブロックするか」基準
        // なので非アクティブ版は Recommended に下げ、版フォルダ欠落と severity を揃える (PR #372 review)。
        private static void AddReconFindings(List<DashboardFinding> list, RestoreReconciliationResult r)
        {
            if (r == null) return;

            // 🔴 起動不能 (アクティブ版 exe 解決不可)。
            foreach (var g in r.BrokenGames)
            {
                list.Add(new DashboardFinding
                {
                    Id = "game-exe:" + g.GameId,
                    Severity = FindingSeverity.Critical,
                    Category = "起動不能",
                    Title = g.Title + " — 実行ファイルが見つかりません",
                    Detail = "アクティブ版 " + Dash(g.ActiveVersion) + "／期待: " + g.ExpectedExecutable
                });
            }

            // 🟡 非アクティブ版の exe 欠落。切替時しか影響しない (表示中の版は無事) ので Recommended、版フォルダ欠落と
            //    severity を揃える (review)。アクティブ版が壊れてるケースは上の BrokenGames=Critical で必ず出る。
            foreach (var v in r.BrokenVersions)
            {
                list.Add(new DashboardFinding
                {
                    Id = "ver-exe:" + v.GameId + ":" + v.Version,
                    Severity = FindingSeverity.Recommended,
                    Category = "別バージョン不備",
                    Title = v.Title + "（v" + v.Version + "）— 実行ファイルが見つかりません",
                    Detail = "期待: " + v.ExpectedExecutable
                });
            }

            // 🔴 DB スキーマ未完。
            if (r.SchemaIncomplete)
            {
                list.Add(new DashboardFinding
                {
                    Id = "schema-incomplete",
                    Severity = FindingSeverity.Critical,
                    Category = "データベース",
                    Title = "データベースの更新が未完です",
                    Detail = "v" + r.ActualSchemaVersion + " → v" + r.ExpectedSchemaVersion + "（再起動で再適用されます）"
                });
            }

            // 🟡 画像欠落 (サムネ/背景)。想定内のことも多いので任意 (× で恒久非表示)。
            foreach (var a in r.BrokenAssets)
            {
                list.Add(new DashboardFinding
                {
                    Id = "asset:" + a.GameId + ":" + Dash(a.Version) + ":" + a.AssetKind,
                    Severity = FindingSeverity.Recommended,
                    Category = a.AssetKind + "欠落",
                    Title = a.Title + " — " + a.AssetKind + "が見つかりません",
                    Detail = (string.IsNullOrEmpty(a.Version) ? "" : "v" + a.Version + "／") + a.ExpectedPath
                });
            }

            // 🟡 版フォルダ欠落 (DB に版があるがフォルダが無い)。
            foreach (var m in r.MissingVersionFolders)
            {
                list.Add(new DashboardFinding
                {
                    Id = "verfolder:" + m.GameId + ":" + m.Version,
                    Severity = FindingSeverity.Recommended,
                    Category = "版フォルダ欠落",
                    Title = m.Title + "（v" + m.Version + "）— 版フォルダがありません",
                    Detail = m.ExpectedFolder
                });
            }

            // 🟡 初回説明スライドの画像欠落。
            foreach (var s in r.BrokenIntroSlides)
            {
                list.Add(new DashboardFinding
                {
                    Id = "slide:" + s.SlideId,
                    Severity = FindingSeverity.Recommended,
                    Category = "スライド画像欠落",
                    Title = "初回説明スライド #" + s.DisplayOrder + " の画像が見つかりません",
                    Detail = s.ExpectedPath
                });
            }

            // ⚪ 孤児フォルダ (DB に無い余分なフォルダ)。参考情報。
            foreach (var o in r.OrphanFolders)
            {
                list.Add(new DashboardFinding
                {
                    Id = "orphan:" + o.Path,
                    Severity = FindingSeverity.Info,
                    Category = o.Kind == OrphanKind.Game ? "孤児ゲームフォルダ" : "孤児版フォルダ",
                    Title = "DB に対応しない余分なフォルダがあります",
                    Detail = o.Path
                });
            }
        }

        // そもそも画像 (サムネ/背景) が割当てられていないゲームを最も軽い Info で出す。recon の BrokenAssets
        // (= パス設定済みだが実ファイル欠落) とは別軸。意図的に未設定のことも多いので × で個別に黙らせられる。
        // 1 ゲーム = 1 finding (未設定の種類をまとめて列挙) でリストが流れないようにする。
        private static void AddUnsetImageFindings(List<DashboardFinding> list, List<GameInfo> games)
        {
            if (games == null) return;
            foreach (var g in games)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.GameId)) continue;
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(g.ThumbnailPath)) missing.Add("サムネイル");
                if (string.IsNullOrWhiteSpace(g.BackgroundPath)) missing.Add("背景画像");
                if (missing.Count == 0) continue;
                string title = string.IsNullOrWhiteSpace(g.Title) ? g.GameId : g.Title;
                list.Add(new DashboardFinding
                {
                    Id = "noimg:" + g.GameId,
                    Severity = FindingSeverity.Info,
                    Category = "画像未設定",
                    Title = title + " — " + string.Join("・", missing) + "が未設定です",
                    Detail = ""
                });
            }
        }

        // ----- dismiss 永続 (settings K/V、schema 変更不要) -----

        public static HashSet<string> LoadDismissed(DatabaseManager db)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string raw = db.SettingsRepository.GetString(SettingsKeys.DashboardDismissedFindings, "");
                if (!string.IsNullOrEmpty(raw))
                    foreach (var s in raw.Split('\n'))
                        if (s.Length > 0) set.Add(s);
            }
            catch (Exception ex) { Logger.Warn("[DashboardService] dismiss 読込失敗: " + ex.Message); }
            return set;
        }

        /// <summary>× した finding を非表示リストに追加して永続化 (背景スレッドから呼ぶ)。</summary>
        public static void Dismiss(DatabaseManager db, string id)
        {
            if (db == null || string.IsNullOrEmpty(id)) return;
            try
            {
                var set = LoadDismissed(db);
                if (set.Add(id))
                    db.SettingsRepository.SetString(SettingsKeys.DashboardDismissedFindings, string.Join("\n", set));
            }
            catch (Exception ex) { Logger.Warn("[DashboardService] dismiss 保存失敗: " + ex.Message); }
        }

        /// <summary>非表示にした finding を元に戻す。</summary>
        public static void Restore(DatabaseManager db, string id)
        {
            if (db == null || string.IsNullOrEmpty(id)) return;
            try
            {
                var set = LoadDismissed(db);
                if (set.Remove(id))
                    db.SettingsRepository.SetString(SettingsKeys.DashboardDismissedFindings, string.Join("\n", set));
            }
            catch (Exception ex) { Logger.Warn("[DashboardService] dismiss 復元失敗: " + ex.Message); }
        }

        private static string Dash(string s) => string.IsNullOrEmpty(s) ? "—" : s;
    }
}
