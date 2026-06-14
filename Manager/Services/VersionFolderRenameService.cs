using System;
using System.Collections.Generic;
using System.Linq;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#242 ゲーム登録フォーム WPF 化 ①) 版の番号変更に伴う <c>v{version}/</c> フォルダの安全なリネームを
    /// EditGameForm.btnOK_Click から抽出した service。元は god-method 内に直書きされていた重ロジック
    /// (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は完全保存。
    ///
    /// 2 フェーズ構成:
    ///   <see cref="BuildPlan"/> (UI/disk 副作用ほぼ無し、`dirExists` のみ) = 予約 slot 構築 + 衝突判定 +
    ///     topological sort で並べ替えた rename 計画を返す。衝突時は <see cref="BuildResult.CollisionMessage"/>。
    ///   <see cref="ExecutePlan"/> (disk Move + 進捗) = 計画通り Move 実行 → 版オブジェクトの相対パス prefix
    ///     書換 + snapshot 更新。Move 失敗時は完了済を逆順 rollback して <see cref="ExecuteResult.ErrorMessage"/>。
    ///   <see cref="Rollback"/> = DB 保存失敗時 (rename 後・UpdateVersionsAndGame 失敗) に caller から呼ぶ。
    ///
    /// disk 操作 (<c>Directory.Exists</c>/<c>Directory.Move</c>) は ctor で注入可能 (既定 = System.IO)。
    /// 単体テストで fake を差し込み、衝突判定・topological sort・rollback・prefix 書換を検証する。
    /// </summary>
    public class VersionFolderRenameService
    {
        private readonly Func<string, bool> _dirExists;
        private readonly Action<string, string> _move;

        public VersionFolderRenameService(Func<string, bool> dirExists = null, Action<string, string> move = null)
        {
            _dirExists = dirExists ?? System.IO.Directory.Exists;
            _move = move ?? System.IO.Directory.Move;
        }

        // (#158 CX-1) folder rename を 2-phase 化するための plan 単位 (BuildPlan で構築 / ExecutePlan で実行)。
        // Old{Executable,Thumbnail,Background}Path: in-memory state rollback 用に path 書き換え前の値を capture。
        // MoveDone: SourceExists=false 経路 (旧 folder 不在で Move skip) でも path/snapshot mutation は行うので
        // rollback 対象として記録しつつ disk Move 戻しは skip させる flag。Move を実行した entry のみ true。
        public class RenamePlan
        {
            public GameVersion Version;
            public string OldDir;
            public string NewDir;
            public string OriginalVer;
            public bool SourceExists;
            public bool MoveDone;
            public string OldExecutablePath;
            public string OldThumbnailPath;
            public string OldBackgroundPath;
        }

        /// <summary>BuildPlan の結果。<see cref="CollisionMessage"/> が非 null なら衝突 (caller は MessageBox + 中止)。</summary>
        public class BuildResult
        {
            public string CollisionMessage;
            public List<RenamePlan> OrderedPlan = new List<RenamePlan>();
            public bool HasCollision => CollisionMessage != null;
        }

        /// <summary>ExecutePlan の結果。<see cref="ErrorMessage"/> が非 null なら Move 失敗 (rollback 済、caller は中止)。
        /// 成功時 <see cref="CompletedRenames"/> は後続の DB 保存が失敗した場合に <see cref="Rollback"/> へ渡す。</summary>
        public class ExecuteResult
        {
            public string ErrorMessage;
            public List<RenamePlan> CompletedRenames = new List<RenamePlan>();
            public bool Failed => ErrorMessage != null;
        }

        public static string ToVersionLeaf(string ver)
        {
            if (ver == null) return "v";
            return "v" + ver.TrimStart('v', 'V');
        }

        /// <summary>
        /// (#158 Q3) 相対パス先頭の <c>v&lt;oldVer&gt;/</c> (or <c>\</c>) prefix を <c>v&lt;newVer&gt;/</c> に置換する。
        /// AddGameForm が「&lt;gameFolder&gt; 起点で相対化」する関係上、executable_path 等は「v&lt;version&gt;/main.exe」の
        /// 形で DB 保存されている。version rename 時にこれらも連動して書き換える。前方一致のみ (保守的)。
        /// </summary>
        public static string ReplaceVersionPrefix(string relPath, string oldVer, string newVer)
        {
            if (string.IsNullOrEmpty(relPath)) return relPath;
            string oldLeaf = ToVersionLeaf(oldVer);
            string newLeaf = ToVersionLeaf(newVer);
            string oldPrefixFwd = oldLeaf + "/";
            string newPrefixFwd = newLeaf + "/";
            string oldPrefixBack = oldLeaf + "\\";
            string newPrefixBack = newLeaf + "\\";
            if (relPath.StartsWith(oldPrefixFwd, StringComparison.OrdinalIgnoreCase))
                return newPrefixFwd + relPath.Substring(oldPrefixFwd.Length);
            if (relPath.StartsWith(oldPrefixBack, StringComparison.OrdinalIgnoreCase))
                return newPrefixBack + relPath.Substring(oldPrefixBack.Length);
            return relPath;
        }

        /// <summary>
        /// Phase 1: 衝突 check + 計画作成 + topological sort。<paramref name="versions"/> は編集中の全版、
        /// <paramref name="originalByDbId"/> は LoadVersions 時の DB-fetched version 文字列 (id→ver)。
        /// </summary>
        public BuildResult BuildPlan(string gameFolder, IEnumerable<GameVersion> versions, IReadOnlyDictionary<int, string> originalByDbId)
        {
            var versionList = versions.ToList();

            // (#158 round 6 M-2) Phase 1 衝突 check は disk 現在状態だけ見ると、同 OK 内で chained rename
            // (例: A→B + B→C) の場合に A→B 計画が「B が既存 disk にある」で abort される。実際 B は B→C 計画で
            // 空く予定 → 順序付ければ成立。全件の oldDir を「予約済み slot (rename で空く予定)」HashSet として
            // 除外してから衝突判定する。
            var reservedOldDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var vR in versionList)
            {
                // 防御: 現 caller は cmbVersionList.Items.OfType<GameVersion>() で null 除外済だが、param は
                // IEnumerable<GameVersion> ゆえ将来 OfType 以外の経路から渡される場合の保険 (害なし、下の v も同様)。
                if (vR == null) continue;
                if (!originalByDbId.TryGetValue(vR.Id, out string origR))
                {
                    // (#158 round 8 senior Med #3) 現状 snapshot は LoadVersions のみが populate するため到達は構造上
                    // ありえないが、将来 form 内に「version 追加」ボタン等が入ると silent drift する死角になるため defensive log。
                    Logger.Warn("[VersionFolderRenameService] (#158 round 8 Med #3) reservedOldDirs build: snapshot 不在 version id=" + vR.Id + " ('" + (vR.Version ?? "(null)") + "')、rename plan skip");
                    continue;
                }
                // (#221) raw 文字列比較ではなく leaf 正規化後で「実際に rename されるか」を判定する。v prefix 有無や
                // 大文字 V のみの差は raw 不一致だが ToVersionLeaf で同一 leaf に畳まれ rename されない = slot は空かない。
                string oldLeafR = ToVersionLeaf(origR);
                if (string.Equals(oldLeafR, ToVersionLeaf(vR.Version), StringComparison.OrdinalIgnoreCase)) continue;
                reservedOldDirs.Add(System.IO.Path.Combine(gameFolder, oldLeafR));
            }

            var renamePlan = new List<RenamePlan>();
            foreach (var v in versionList)
            {
                if (v == null) continue;
                if (!originalByDbId.TryGetValue(v.Id, out string originalVer))
                {
                    Logger.Warn("[VersionFolderRenameService] (#158 round 8 Med #3) renamePlan build: snapshot 不在 version id=" + v.Id + " ('" + (v.Version ?? "(null)") + "')、rename plan skip");
                    continue;
                }
                // (#158 round 3 H-2) case-only な差 (DB の "V1.2.3" が save で "v1.2.3" 正規化) は disk 上同フォルダなので
                // rename 不要、OrdinalIgnoreCase で skip して DB 側だけ normalized 値で書き戻す。
                if (string.Equals(originalVer, v.Version, StringComparison.OrdinalIgnoreCase)) continue;

                string oldLeaf = ToVersionLeaf(originalVer);
                string newLeaf = ToVersionLeaf(v.Version);
                string oldDir = System.IO.Path.Combine(gameFolder, oldLeaf);
                string newDir = System.IO.Path.Combine(gameFolder, newLeaf);

                // (#221) ToVersionLeaf 正規化後に old/new が同一フォルダになるケースは disk rename 不要 (self-rename の
                // Directory.Move "source == dest" 例外を回避)。下の UpdateGameVersion ループが DB を正規化値で書き戻す。
                if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)) continue;

                // (#158 round 6 M-2 / round 8 codex P1) reservedOldDirs に含まれる newDir は他 plan の oldDir =
                // rename 実行で空く予定なので衝突 skip。oldDir 不在 (= partial-commit recovery 経路) は Move 自体走らない
                // ため衝突対象外。両方存在 + 非予約のときのみ真の衝突として block する。
                bool srcExistsV = _dirExists(oldDir);
                if (srcExistsV && _dirExists(newDir) && !reservedOldDirs.Contains(newDir))
                {
                    return new BuildResult
                    {
                        CollisionMessage =
                            "バージョンフォルダのリネームに失敗しました:\n" +
                            "  " + oldDir + " → " + newDir + "\n\n" +
                            "  移動先フォルダが既に存在します (他の rename 計画でも空く予定なし)。" +
                            "別のバージョン番号を指定してください。"
                    };
                }

                renamePlan.Add(new RenamePlan
                {
                    Version = v,
                    OldDir = oldDir,
                    NewDir = newDir,
                    OriginalVer = originalVer,
                    SourceExists = srcExistsV,
                    OldExecutablePath = v.ExecutablePath,
                    OldThumbnailPath = v.ThumbnailPath,
                    OldBackgroundPath = v.BackgroundPath,
                });
            }

            // (#158 round 7 H-1) topological sort。chained rename (A→B + B→C) は「newDir が他 plan の oldDir でない」
            // plan を優先実行 (destination が空く plan を先に潰す)。cycle (A↔B) は pickIdx<0 で fall through、残りを
            // UI 順で append (ExecutePlan で先行 Move の newDir 衝突 → CX-1 rollback で安全)。
            var orderedPlan = new List<RenamePlan>();
            var pendingPlan = new List<RenamePlan>(renamePlan);
            while (pendingPlan.Count > 0)
            {
                var pendingOldDirs = new HashSet<string>(
                    pendingPlan.Select(pp => pp.OldDir), StringComparer.OrdinalIgnoreCase);
                int pickIdx = pendingPlan.FindIndex(pp => !pendingOldDirs.Contains(pp.NewDir));
                if (pickIdx < 0)
                {
                    orderedPlan.AddRange(pendingPlan);
                    break;
                }
                orderedPlan.Add(pendingPlan[pickIdx]);
                pendingPlan.RemoveAt(pickIdx);
            }

            return new BuildResult { OrderedPlan = orderedPlan };
        }

        /// <summary>
        /// Phase 2: 計画通り rename 実行。例外時は完了済を逆順 rollback して <see cref="ExecuteResult.ErrorMessage"/> を返す。
        /// 成功時は各版の相対パス prefix を新 leaf へ書換 + <paramref name="originalByDbId"/> snapshot を更新する。
        /// <paramref name="progress"/> は ProcessingDialog からの marquee 進捗 (null 可)。
        /// </summary>
        public ExecuteResult ExecutePlan(IReadOnlyList<RenamePlan> orderedPlan, IDictionary<int, string> originalByDbId, IProgress<ProgressInfo> progress)
        {
            var result = new ExecuteResult();
            var completedRenames = result.CompletedRenames;
            for (int i = 0; i < orderedPlan.Count; i++)
            {
                var p = orderedPlan[i];
                progress?.Report(new ProgressInfo(-1,
                    $"バージョンフォルダをリネーム中 ({i + 1}/{orderedPlan.Count})...",
                    $"{p.OldDir}\n  → {p.NewDir}"));

                if (!p.SourceExists)
                {
                    // 旧 folder 不在: DB のみ存在する version 等。DB 更新だけ続けて警告ログのみ。disk Move は skip するが
                    // path/snapshot mutation はやるため completedRenames に MoveDone=false で記録。
                    Logger.Warn("[VersionFolderRenameService] (#158 Q3) version '" + p.OriginalVer + "' のフォルダが見つかりません、rename skip: " + p.OldDir);
                    p.MoveDone = false;
                    completedRenames.Add(p);
                }
                else
                {
                    try
                    {
                        _move(p.OldDir, p.NewDir);
                        p.MoveDone = true;
                        completedRenames.Add(p);
                    }
                    catch (Exception ex)
                    {
                        // (#158 CX-1 + round 4 codex P1) rollback: 完了済 rename を逆順で disk Move 戻し + in-memory state
                        // (snapshot + path 群) も capture 前に復元。同 dialog で再 OK 押下時に diff check が正しく triggered
                        // されて rename retry できる状態に戻す。ここは DB write が一切走っていないため安全に呼べる。
                        progress?.Report(new ProgressInfo(-1, "ロールバック中...", "完了済の rename を元の名前に戻しています"));
                        int rolledBack, rollbackFailures;
                        Rollback(completedRenames, originalByDbId, out rolledBack, out rollbackFailures);
                        result.ErrorMessage =
                            "バージョンフォルダのリネームに失敗しました:\n" +
                            "  " + p.OldDir + "\n  → " + p.NewDir + "\n\n" +
                            "  " + ex.Message + "\n\n" +
                            "  完了済の rename " + rolledBack + " 件を元の名前に rollback しました" +
                            (rollbackFailures > 0 ? " (rollback 失敗 " + rollbackFailures + " 件、ログファイル参照)" : "") +
                            "。\n  DB は更新していないので OK 押下前の状態に戻ります。\n\n" +
                            "Launcher / 別プロセスが該当フォルダを使用していないか確認してください。";
                        return result;
                    }
                }

                // version 文字列を含む相対パス (`v<old>/...`) を新 prefix に置換。prefix を持たない path は no-op。
                p.Version.ExecutablePath = ReplaceVersionPrefix(p.Version.ExecutablePath, p.OriginalVer, p.Version.Version);
                p.Version.ThumbnailPath = ReplaceVersionPrefix(p.Version.ThumbnailPath, p.OriginalVer, p.Version.Version);
                p.Version.BackgroundPath = ReplaceVersionPrefix(p.Version.BackgroundPath, p.OriginalVer, p.Version.Version);

                // snapshot を最新化 (将来 LoadVersions を OK 内で呼び直す path への保険)。
                originalByDbId[p.Version.Id] = p.Version.Version;
            }
            return result;
        }

        /// <summary>
        /// (#158 round 4 codex P1 + round 5 L-5) rename rollback の共通処理: completedRenames を逆順に disk Move を
        /// 戻し (MoveDone=true のみ)、各エントリの in-memory state (snapshot + GameVersion の path 群) を capture 前へ
        /// restore する。ExecutePlan 内の Move 失敗・caller の DB 保存失敗 両経路から呼ばれる。
        ///
        /// **注意 (#158 round 5 codex P1)**: 本 method は「DB が一切 commit されていない」前提でのみ安全。
        /// commit 済 row があるまま呼ぶと commit 済 row が指す path/folder 名が disk rollback で消失して drift する。
        /// </summary>
        public void Rollback(List<RenamePlan> completedRenames, IDictionary<int, string> originalByDbId, out int rolledBack, out int rollbackFailures)
        {
            rolledBack = 0;
            rollbackFailures = 0;
            for (int i = completedRenames.Count - 1; i >= 0; i--)
            {
                var done = completedRenames[i];
                if (done.MoveDone)
                {
                    try
                    {
                        _move(done.NewDir, done.OldDir);
                        rolledBack++;
                    }
                    catch (Exception rbEx)
                    {
                        rollbackFailures++;
                        Logger.Error("[VersionFolderRenameService] (#158 rollback) disk rename 戻し失敗: " + done.NewDir + " → " + done.OldDir, rbEx);
                    }
                }
                // in-memory 復元 (disk Move 成否 / SourceExists=false に関わらず、UI/DB drift 最小化のため必ず実行)。
                originalByDbId[done.Version.Id] = done.OriginalVer;
                done.Version.ExecutablePath = done.OldExecutablePath;
                done.Version.ThumbnailPath = done.OldThumbnailPath;
                done.Version.BackgroundPath = done.OldBackgroundPath;
            }
        }
    }
}
