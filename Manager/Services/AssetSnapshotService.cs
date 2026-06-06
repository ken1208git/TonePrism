using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR1) DB バックアップと同時に `games/` + `guide/` を **共有プール方式 (CAS / SHA-256)** で控える service。
    ///
    /// 中身ごとに `asset_pool/<hash>` に実体 1 個だけ置き、各世代は「相対パス→ハッシュ」の小さな manifest にする。
    /// 同じ中身は (別ゲーム間・版違い間でも) 1 個に集約されるため、ファイルサイズを単純合計するどんな仕組み
    /// (エクスプローラー / SMB サーバーの容量計算 / quota) でも**実サイズしか出ない** (= 削減が見える)。
    /// SHA-256 なので「中身が違うのに同じハッシュ」は起こらず、名前が同じでも中身が違えば必ず別保存される。
    /// コピーベースなので SMB 越し / 別ボリュームでも有効 (ハードリンクと違う)。
    ///
    /// **best-effort**: 失敗・キャンセルは throw せず <see cref="SnapshotResult"/> で返し、DB バックアップの成否・
    /// last_backup_at を壊さない (完了済み DB バックアップを守る)。
    /// </summary>
    public class AssetSnapshotService
    {
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupService _backupService;

        private static readonly string[] SubFolders = { "games", "guide" };
        internal const string PoolDirName = "asset_pool"; // (#250 PR3a review #4) AssetRestoreService と共用 (pool ルート dir 名 SoT)
        private const string ManifestDirName = "asset_snapshots";
        private const string ManifestExt = ".manifest";
        internal const string MetaLinePrefix = "META"; // (#250 PR3) AssetRestoreService と共用 (manifest 形式 SoT)
        /// <summary>(レビュー M1) pool 物理サイズのキャッシュ。UI から pool 全列挙 (SMB で重い) を避けるため、
        /// バックアップ時にバックグラウンドスレッドで算出してここに書き、UI は即時読みする。</summary>
        private const string PoolSizeFileName = ".poolsize";
        /// <summary>GC で未参照 pool ファイルを消すときの猶予 (直近書込/並行 backup のレース回避)。テストで 0 に上書き可。
        /// (#295 round7 #2 の対応) 1h→**24h** に延長。多ホストが同一 SMB プールへ並行 backup/GC する際、片方の
        /// **初回フル ingest (~6GB)** が grace を超える間に他ホストの GC が「取得中だがまだ目録未記載の blob」を
        /// 未参照と誤判定して回収しうる窓 (= #250 round8 C2) を塞ぐため。書きたて blob を 24h 守れば、現実的な
        /// ingest 時間 (数十分〜1h 級、24h は超えない) は確実に保護される。コスト: 削除済ゲームの未参照 blob が
        /// 最大 24h 長く pool に残る (= 一時的な容量増のみ、~6GB 規模では誤差)。24h を超える ingest という非現実的
        /// ケースだけは残り、それは heartbeat-lease が要る #250 の多ホスト整合領域。</summary>
        internal TimeSpan GcGracePeriod = TimeSpan.FromHours(24);

        public AssetSnapshotService(DatabaseConnection conn, SettingsRepository settingsRepo, BackupService backupService)
        {
            _conn = conn;
            _settingsRepo = settingsRepo;
            _backupService = backupService;
        }

        public string GetSnapshotRootDirectory() => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), ManifestDirName);
        private string GetPoolRootDirectory() => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), PoolDirName);

        /// <summary>(round7 M-2) DB に登録された games 件数。games/ も guide/ も見えないときに「未登録の新規 install」と
        /// 「SMB 不達等の異常」を区別する権威ある判別軸 (DB は直前の DB バックアップ成功で到達済 = 「本来 games が
        /// あるはず」を知っている。到達不能共有では Directory.Exists も manifest 列挙も空に見え判別不能なため)。</summary>
        private int CountRegisteredGames()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var command = new SQLiteCommand("SELECT COUNT(*) FROM games", connection))
                        return Convert.ToInt32(command.ExecuteScalar());
                }
            });
        }

        /// <summary>games/ + guide/ を 1 世代取得する。DB バックアップ成功直後に同一 timestamp/trigger で best-effort 呼び出し。</summary>
        public SnapshotResult CreateSnapshot(string timestamp, string triggerType,
            IProgress<ProgressInfo> progress = null, CancellationToken token = default(CancellationToken),
            string replacedManifestPath = null)
        {
            string tmpManifest = null;
            try
            {
                if (!IsEnabled()) return SnapshotResult.Skipped("asset_snapshot_enabled=false");

                string baseInstallDir = Path.GetDirectoryName(_conn.DbPath);
                if (!SubFolders.Any(s => Directory.Exists(Path.Combine(baseInstallDir, s))))
                {
                    // (レビュー#1 / round7 M-2) games/ と guide/ の両方が見えない。「まだ登録が無い新規 install」と
                    // 「SMB 一過性不達等の異常」を区別する。到達不能な共有では Directory.Exists が false を返し、manifest
                    // 履歴も空に見える (EnumerateManifests は Directory.Exists ガードで throw せず空を返す) ため、manifest の
                    // 有無だけでは判別できない。DB は直前の DB バックアップ成功で到達済 = 「本来 games があるはず」を知って
                    // いるので、DB の games 件数を権威ある判別軸に加える: games 件数 > 0 (or 履歴あり) なのに games/ が
                    // 見えない → 異常 (silent Success にしない)。どちらも無い → 真に未登録の新規 install。
                    int gameCount = 0;
                    try { gameCount = CountRegisteredGames(); }
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] games 件数の取得に失敗 (判別軸の一つが欠落): " + ex.Message); }
                    bool hadHistory = false;
                    try { hadHistory = EnumerateManifests().Any(); }
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] manifest 列挙に失敗 (判別軸の一つが欠落): " + ex.Message); }
                    if (gameCount > 0 || hadHistory)
                    {
                        Logger.Warn("[AssetSnapshot] games/ も guide/ も見つかりません (DB games=" + gameCount + " / 履歴=" + hadHistory + " → SMB 不達等の異常の可能性)。今回の控えはスキップします。");
                        return SnapshotResult.SkippedAnomaly("ゲームファイルのフォルダが見つかりません (異常の可能性)");
                    }
                    Logger.Info("[AssetSnapshot] games/ も guide/ も無く DB にも games 未登録のため控えなし (新規 install と判断)。");
                    return SnapshotResult.Success(null, 0, 0, 0);
                }

                string poolRoot = GetPoolRootDirectory();
                Directory.CreateDirectory(poolRoot);
                string manifestTriggerDir = Path.Combine(GetSnapshotRootDirectory(), triggerType);
                Directory.CreateDirectory(manifestTriggerDir);

                var cache = LoadHashCache();                 // 直近 manifest から relpath→(size,mtime,hash)

                // (レビュー M1) games/ や guide/ の「非対称欠損」を検出する。以前は控え (直近 manifest) に当該 sub の
                // エントリがあったのに今 dir が無い (= SMB で games/ だけ不可視等) のに先へ進むと、games を含まない
                // guide-only manifest を Success で書いてしまい、retention 世代を跨いで games blob が GC される。
                // 異常として世代まるごとスキップ (既存の完全な manifest 群は温存)。
                // (レビュー round5 #3) 判定は **直近 manifest 1 件**基準。直近がたまたま当該 sub 0 件の瞬間だと検知漏れ
                // しうるが、本番 games/ は常に非空なので実害ほぼ無し。全 manifest 横断は SMB でコスト高のため採らない。
                foreach (var sub in SubFolders)
                {
                    bool existedBefore = cache.Keys.Any(k => k.StartsWith(sub + "/", StringComparison.Ordinal));
                    if (!existedBefore) continue;
                    string subPath = Path.Combine(baseInstallDir, sub);
                    if (!Directory.Exists(subPath))
                    {
                        Logger.Warn("[AssetSnapshot] " + sub + "/ が以前は控えにあったのに見つかりません (SMB 不達等の異常の可能性)。今回の控えはスキップします。");
                        return SnapshotResult.SkippedAnomaly(sub + "/ が見つからない (異常の可能性)");
                    }
                    // (round7 M-1) dir は存在するが root 列挙が失敗 = 「見えるが読めない」異常 (SMB 一過性 I/O / 権限等)。
                    // WalkTree はフォルダ列挙失敗を best-effort で skip して進むため、放置すると当該 sub がほぼ空の sparse
                    // manifest を Success で書き、auto なら直後の GC が将来 blob を刈りうる。不在検出と同じ安全側 = 世代まるごと
                    // Skip にする (深部サブフォルダの単発失敗は従来どおり best-effort skip。ここで見るのは sub の root のみ)。
                    try { Directory.GetFiles(ForceLong(subPath)); }
                    catch (Exception ex)
                    {
                        Logger.Warn("[AssetSnapshot] " + sub + "/ は存在するが列挙に失敗 (SMB 一過性 I/O / 権限等の異常の可能性)。今回の控えはスキップします: " + ex.Message);
                        return SnapshotResult.SkippedAnomaly(sub + "/ の列挙に失敗 (異常の可能性)");
                    }
                }

                var entries = new List<string>();
                var stats = new Stats();
                // (レビュー#2 / #250 M1) 進捗分母は概算。SafeCountFiles は WalkTree と同じく ExcludedFolders を除外せず
                // 数える (= 分母と走査数を一致させる。旧実装は除外ありで数えており分母過小 → pct が 100 に張り付いた)。
                // 深い木で CountFiles 側が PathTooLong すると partial になりうるが、超過分は下の pct で 100 に clamp。
                int total = SubFolders.Sum(s => SafeCountFiles(Path.Combine(baseInstallDir, s)));

                foreach (var sub in SubFolders)
                {
                    string src = Path.Combine(baseInstallDir, sub);
                    if (Directory.Exists(src))
                        WalkTree(src, sub, poolRoot, cache, entries, stats, progress, token, total);
                }
                token.ThrowIfCancellationRequested();

                // manifest を temp→rename で atomic 書き出し。(レビュー#2) 書込/rename も長パス対応 (深い backup_dest で
                // manifest 実パスが MAX_PATH 超になりうる。WriteManifest 内は EnsureLongPath 済だが Move/Delete も揃える)。
                // manifestPath は ResolveUniqueManifest が非存在を保証するので削除チェックは不要 (デッドコードを除去)。
                string host = BackupService.SanitizeHostForFileName(Environment.MachineName);
                string leaf = string.IsNullOrEmpty(host) ? timestamp : timestamp + "_" + host;
                string manifestPath = ResolveUniqueManifest(manifestTriggerDir, leaf);
                tmpManifest = manifestPath + ".tmp";
                WriteManifest(tmpManifest, timestamp, host, triggerType, stats, entries);
                File.Move(FileOperationService.EnsureLongPath(tmpManifest), FileOperationService.EnsureLongPath(manifestPath));
                tmpManifest = null;

                ApplyRetentionAndGc(triggerType, stats.NewBytes, replacedManifestPath);

                Logger.Info(string.Format("[AssetSnapshot] 控え完了: {0} ({1} files / 論理 {2:F2}GB / 新規コピー {3:F2}MB)",
                    Path.GetFileName(manifestPath), stats.FileCount, stats.Bytes / 1073741824.0, stats.NewBytes / 1048576.0));
                // (round8 C1) 深部フォルダの列挙失敗を best-effort で skip して完走した場合、この世代は部分的な控えの
                // 可能性がある。個別 Warn は出ているが、件数を集計して「部分取得」を明示し UI にも伝える (緑チェック=
                // 完全控えと誤認させない)。世代まるごとの異常 (SkippedAnomaly) ほどではないので Success のまま IsPartial で返す。
                if (stats.SkippedDirCount > 0 || stats.SkippedFileCount > 0)
                    Logger.Warn(string.Format("[AssetSnapshot] ⚠ フォルダ {0} 個 / ファイル {1} 個を控えられずスキップしました (SMB 一過性 I/O / 権限 / 並行編集での消失等)。この世代は部分的な控えの可能性があります ({2} files)。",
                        stats.SkippedDirCount, stats.SkippedFileCount, stats.FileCount));
                return SnapshotResult.Success(manifestPath, stats.FileCount, stats.Bytes, stats.NewBytes, stats.SkippedDirCount, stats.SkippedFileCount);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tmpManifest); // pool に途中コピーした実体は無参照のまま GC で回収される
                Logger.Info("[AssetSnapshot] 取得がキャンセルされました (DB バックアップは保持)");
                return SnapshotResult.Skipped("キャンセル");
            }
            catch (Exception ex)
            {
                TryDeleteFile(tmpManifest);
                Logger.Error("[AssetSnapshot] 取得に失敗しました (DB バックアップは保持)", ex);
                return SnapshotResult.Failed(ex.Message);
            }
        }

        /// <summary>最新の世代 1 件 (auto/manual 横断)。無ければ null。UI 用。</summary>
        public AssetSnapshotInfo GetLatestSnapshot()
        {
            try
            {
                var newest = EnumerateManifests().OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                return newest != null ? ReadManifestHeader(newest) : null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[AssetSnapshot] 最新世代の取得に失敗: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// (#250 PR3b) ある DB バックアップ世代とペアになる **アセット manifest** を解決する。DB の `.db` と
        /// アセット `.manifest` は別ファイルで「同一バックアップ操作」を厳密に紐づける ID を持たないため、**時刻**で
        /// ペアリングする: <paramref name="dbStartedLocal"/> (= 復元しようとする .db の作成時刻 T) **以下で最大時刻**の
        /// manifest を選ぶ。
        ///
        /// **なぜ「以下で最大」か (replace-in-session 対策)**: DB-only 操作 (replace-in-session) は `.db` をより新しい
        /// 時刻で書き直すが manifest は書き直さないので、`.db` 時刻が対のアセット manifest より **後** になりうる。
        /// 「T 以下で最大」なら、その操作の直前フル世代の manifest (= live アセットが一致する世代) を正しく拾う。
        /// 「T 以上で最小」だと存在しない/別世代を指してしまう。
        ///
        /// **host 優先**: 同時刻に複数 PC が控えた tie を分けるため、<paramref name="preferredHost"/> (= .db を作った PC)
        /// 一致を優先し、無ければ全体最新。tie-break はファイル名降順 (newest-wins と一致)。比較は両者の
        /// <see cref="AssetSnapshotInfo.StartedAtLocal"/> 同士の **local 比較** (DB 名・manifest 名とも同じ
        /// yyyyMMdd_HHmmss 由来＝秒粒度・同一壁時計、DST ズレ無し)。
        ///
        /// 該当世代が無い (= T 以前の manifest が無い)・SMB 不達等は **null** を返す (DBのみ復元を阻害しない best-effort)。
        /// </summary>
        public AssetSnapshotInfo FindSnapshotForBackup(DateTime dbStartedLocal, string preferredHost)
        {
            try
            {
                var candidates = EnumerateManifests()
                    .Select(ReadManifestHeader)
                    // 不正ヘッダ (Timestamp 解釈不能 = StartedAtLocal が MinValue) は時刻信頼不可なので除外。
                    .Where(m => m != null && m.StartedAtLocal != DateTime.MinValue)
                    // T 以下 (= .db 時刻以前) の世代だけがペア候補 (replace-in-session で .db>manifest を正しく拾う)。
                    .Where(m => m.StartedAtLocal <= dbStartedLocal)
                    // 新しい順 → 同時刻ならファイル名降順 (host 名込みで決定的、newest-wins 規約と一致)。
                    .OrderByDescending(m => m.StartedAtLocal)
                    .ThenByDescending(m => Path.GetFileName(m.ManifestPath), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (candidates.Count == 0) return null;

                // (review #1) host 一致は **同時刻 (= 最新候補と同秒) の tie を分ける用途に限定**する。games/+guide/ は SMB 上の
                // 単一共有ツリー (host 非依存) なので、「時点 T のツリー状態」の最良推定は **時間的に最も近い manifest** であって
                // host ではない。旧実装は host 一致を候補リスト全体から探したため、別 PC の T 直前 manifest より **同 PC の
                // 数週間前 manifest** を優先しうる degraded ケースがあった (exact pair 欠落時)。これだと古い世代で reconcile が
                // 走り、T 以降に増えたファイルを「余剰」削除する方向に効く。そこで host 優先は最新秒グループ内だけに絞り、
                // 同秒に host 一致が無ければ全体最新 (= 時間的に最も近い) へフォールバックする。
                if (!string.IsNullOrEmpty(preferredHost))
                {
                    DateTime newestTime = candidates[0].StartedAtLocal;
                    var hostMatch = candidates.FirstOrDefault(m =>
                        m.StartedAtLocal == newestTime &&
                        string.Equals(m.Host, preferredHost, StringComparison.OrdinalIgnoreCase));
                    if (hostMatch != null) return hostMatch;
                }
                return candidates[0];
            }
            catch (Exception ex)
            {
                Logger.Warn("[AssetSnapshot] DB 世代とのアセット manifest ペアリングに失敗 (DBのみ復元へフォールバック): " + ex.Message);
                return null;
            }
        }

        /// <summary>アセットプールが実際に使っているディスク量 (= 重複排除後の物理サイズ)。UI 用。
        /// (レビュー M1) pool 全列挙は SMB で重く UI スレッドをフリーズさせうるので、バックアップ時に算出済の
        /// `.poolsize` キャッシュを即時読みする。無ければ 0 (= まだ取得していない)。</summary>
        public long GetPoolPhysicalBytes()
        {
            try
            {
                string sizeFile = FileOperationService.EnsureLongPath(Path.Combine(GetPoolRootDirectory(), PoolSizeFileName));
                if (!File.Exists(sizeFile)) return 0;
                long v;
                return long.TryParse(File.ReadAllText(sizeFile).Trim(), out v) ? v : 0;
            }
            catch { return 0; }
        }

        private void WritePoolSizeCache(long bytes)
        {
            try
            {
                string pool = GetPoolRootDirectory();
                if (!Directory.Exists(pool)) return;
                File.WriteAllText(FileOperationService.EnsureLongPath(Path.Combine(pool, PoolSizeFileName)),
                    bytes.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] poolsize キャッシュ書込失敗 (無害): " + ex.Message); }
        }


        // ---- 内部 ----

        private bool IsEnabled()
        {
            string v = _settingsRepo.GetString(SettingsKeys.AssetSnapshotEnabled, "true");
            return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class CacheEntry { public long Size; public long MtimeTicks; public string Hash; }

        /// <summary>直近 manifest を読み「relpath → (size, mtime, hash)」のキャッシュを作る。SMB で不変ファイルを再ハッシュしないため。</summary>
        private Dictionary<string, CacheEntry> LoadHashCache()
        {
            var cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            try
            {
                var newest = EnumerateManifests().OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (newest == null) return cache;
                foreach (var line in File.ReadLines(FileOperationService.EnsureLongPath(newest)))
                {
                    if (!TryParseManifestEntryLine(line, out ManifestEntry e)) continue;
                    cache[e.RelPath] = new CacheEntry { Hash = e.Hash, Size = e.Size, MtimeTicks = e.MtimeTicks };
                }
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] ハッシュキャッシュ読込失敗 (全ハッシュし直す): " + ex.Message); }
            return cache;
        }

        private void WalkTree(string srcDir, string relPrefix, string poolRoot,
            Dictionary<string, CacheEntry> cache, List<string> entries, Stats stats,
            IProgress<ProgressInfo> progress, CancellationToken token, int total)
        {
            token.ThrowIfCancellationRequested();
            // (レビュー#3) 列挙対象 dir も \\?\ で長パス対応にする (per-file だけでなく enumerate も)。EnsureLongPath は
            // 240 字未満だと \\?\ を付けないため、短い srcDir 配下に MAX_PATH 超のファイルがあると GetFiles 自体が
            // PathTooLong を投げ世代まるごと Failed になる。ForceLong で常に長パス列挙にし、失敗時もそのフォルダだけ
            // スキップ (best-effort、世代全体は落とさない)。
            string[] files;
            try { files = Directory.GetFiles(ForceLong(srcDir)); }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] ファイル列挙に失敗 (このフォルダをスキップ): " + srcDir + " : " + ex.Message); files = new string[0]; stats.SkippedDirCount++; }
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                string safe = FileOperationService.EnsureLongPath(file);
                // (レビュー L3) ファイルの symlink/junction も辿らない (dir と同扱い、spec 整合)。リンク先を
                // ハッシュ/コピーすると意図せぬ実体を取り込むため。
                try { if ((File.GetAttributes(safe) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) { Logger.Warn("[AssetSnapshot] reparse file をスキップ: " + file); continue; } }
                catch { }
                string relpath = relPrefix + "/" + Path.GetFileName(file);
                long size = 0;
                long mtime = 0;
                string hash = null;
                // (#299 review C-1 / #4) 実ファイル I/O (size/mtime/ハッシュ/pool 配置) だけを try で囲む。非ブロッキング化で
                // バックアップ中もユーザーが games/ を編集できるため、走査中のファイルが並行操作 (ゲーム削除 / 版up) で
                // 消える / 掴まれると HashAndStore の FileStream.Open 等が FileNotFound / IOException を投げる。旧実装 (モーダル)
                // は並行編集が物理的に不可能で per-file 防御が無く、1 ファイルで世代まるごと Failed (=~6GB 再走査 + 警告チラつき)
                // になっていた。dir 列挙失敗 (上の GetFiles catch) と同じ best-effort 思想で当該ファイルだけ skip し、世代は
                // IsPartial Success に留める (消えたファイルは次のコアレス再走査で削除後ツリーとして整合)。
                // (#4) try は I/O のみに絞る — entries.Add / progress.Report まで包むと、それらの例外まで「ファイル消失」として
                // 誤計上 (entries に入りつつ skip もされ二重カウント) してしまうため、後続は try 外に出す。
                try
                {
                    size = SafeLen(file);
                    mtime = File.GetLastWriteTimeUtc(safe).Ticks;

                    // キャッシュ命中 (relpath+size+mtime 一致) かつ pool に実体があれば再ハッシュ・再読込しない。
                    CacheEntry c;
                    if (cache.TryGetValue(relpath, out c) && c.Size == size && c.MtimeTicks == mtime
                        && File.Exists(FileOperationService.EnsureLongPath(PoolPathFor(poolRoot, c.Hash))))
                    {
                        hash = c.Hash;
                    }
                    else
                    {
                        // (レビュー#4) ソースを 1 回だけ読み、ハッシュ計算と pool 配置を同時に行う (SMB の二重読込を回避)。
                        if (HashAndStore(safe, poolRoot, token, out hash)) stats.NewBytes += size;
                    }
                }
                catch (OperationCanceledException) { throw; } // キャンセルは best-effort skip にせず伝播させる
                catch (Exception ex)
                {
                    Logger.Warn("[AssetSnapshot] ファイルの控えに失敗 (このファイルをスキップ、並行編集で消えた可能性): " + file + " : " + ex.Message);
                    stats.SkippedFileCount++;
                    continue;
                }

                entries.Add(hash + "\t" + size.ToString(CultureInfo.InvariantCulture) + "\t"
                    + mtime.ToString(CultureInfo.InvariantCulture) + "\t" + relpath);
                stats.FileCount++;
                stats.Bytes += size;
                if (total > 0 && progress != null)
                {
                    int pct = (int)((double)stats.FileCount / total * 100);
                    progress.Report(new ProgressInfo(pct > 100 ? 100 : pct, "ゲームファイルをバックアップ中...", Path.GetFileName(file)));
                }
            }
            string[] dirs;
            try { dirs = Directory.GetDirectories(ForceLong(srcDir)); }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] サブフォルダ列挙に失敗 (スキップ): " + srcDir + " : " + ex.Message); dirs = new string[0]; stats.SkippedDirCount++; }
            foreach (string subDir in dirs)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attr;
                try { attr = File.GetAttributes(FileOperationService.EnsureLongPath(subDir)); } catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Logger.Warn("[AssetSnapshot] reparse point をスキップ: " + subDir);
                    continue;
                }
                // (round9 L2) ゲーム削除の retry 退避フォルダ games/{id}.pending-delete-{guid} は、物理削除を諦めた
                // (pendingGivenUp) 場合に games/ 配下へ残る。これは「削除途中のゴミ」であって控えるべきゲーム本体では
                // ないため snapshot 対象から除外する (削除したはずのゲーム実体が manifest に復活し、復元時に蘇る混乱を防ぐ)。
                // 削除が成功した通常経路ではフォルダは RunAfterOperation より前に消えているのでそもそも列挙されない。
                if (Path.GetFileName(subDir).IndexOf(".pending-delete-", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                WalkTree(subDir, relPrefix + "/" + Path.GetFileName(subDir), poolRoot, cache, entries, stats, progress, token, total);
            }
        }

        /// <summary>常に \\?\ を付けて長パス対応の列挙にする (EnsureLongPath は 240 字未満だと付けないため)。
        /// UNC 対応は FileOperationService.ForceLongPath に集約 (#250 C1: 旧実装は UNC で "\\?\\\server\..." という
        /// 構文不正パスを生成し、SMB 上の列挙が全件失敗 → 空の控えを Success 扱いにする欠陥があった)。</summary>
        private static string ForceLong(string path) => FileOperationService.ForceLongPath(path);

        /// <summary>(#250 PR3) manifest の 1 エントリ (relpath + hash + size/mtime)。snapshot 書き手と restore 読み手で共用。</summary>
        internal struct ManifestEntry { public string Hash; public long Size; public long MtimeTicks; public string RelPath; }

        // (#250 PR3) manifest 形式と pool パスの SoT。AssetRestoreService が同一アセンブリから再利用する
        // (重複定義を避け、書き手 WriteManifest と読み手を 1 箇所に固定。InternalsVisibleTo 済でテストからも可視)。
        internal static string PoolPathFor(string poolRoot, string hash)
            => Path.Combine(poolRoot, hash.Substring(0, 2), hash);

        /// <summary>(#250 PR3) manifest 1 行 `&lt;hash&gt;\t&lt;size&gt;\t&lt;mtime_ticks&gt;\t&lt;relpath&gt;` を解析する。
        /// META 行 / 空 / 不正は false。LoadHashCache / GarbageCollectPool / AssetRestoreService が共用する唯一の解釈点
        /// (タブ4フィールドの順序を 1 箇所に固定し、書き手 WriteManifest と乖離させない)。</summary>
        internal static bool TryParseManifestEntryLine(string line, out ManifestEntry entry)
        {
            entry = default(ManifestEntry);
            if (string.IsNullOrEmpty(line)) return false;
            if (line.StartsWith(MetaLinePrefix + "\t")) return false;
            var f = line.Split(new[] { '\t' }, 4);
            if (f.Length < 4) return false;
            if (f[0].Length != 64) return false; // (#250 PR3a review #3) hash は SHA-256 hex 64 桁。不正長は破損行扱い (restore の削除抑止に倒し、PoolPathFor の Substring crash も防ぐ)
            long size, mt;
            if (!long.TryParse(f[1], out size) || !long.TryParse(f[2], out mt)) return false;
            entry.Hash = f[0];
            entry.Size = size;
            entry.MtimeTicks = mt;
            entry.RelPath = f[3];
            return true;
        }

        /// <summary>(#250 PR3a) META 行から skipped 合計を読む。**8 フィールド形式 (skipped 列あり) のときだけ true** +
        /// skipped 合計を返す。旧 6 フィールド META (skipped 情報なし＝部分取得か判定不能) や非 META 行は false。
        /// (review #1) restore は「false なら完全性を断定せず余剰削除を抑止」に倒す＝旧世代を complete と誤断して live を
        /// 消さない安全側既定。META 形式の解釈点を WriteManifest と同じ AssetSnapshotService に固定 (SoT)。</summary>
        internal static bool TryReadMetaSkipped(string metaLine, out int skippedTotal)
        {
            skippedTotal = 0;
            if (string.IsNullOrEmpty(metaLine) || !metaLine.StartsWith(MetaLinePrefix + "\t")) return false;
            var f = metaLine.Split('\t');
            if (f.Length < 8) return false; // 旧形式 (skipped 列なし) = 完全性を断定不能
            int sd, sf;
            int.TryParse(f[6], out sd);
            int.TryParse(f[7], out sf);
            skippedTotal = sd + sf;
            return true;
        }

        /// <summary>
        /// (レビュー#4) ソースを 1 回だけ読み、SHA-256 を計算しつつ pool の temp に書き、内容ハッシュ名へ rename する。
        /// 既に同一中身が pool にあれば temp を捨てる (重複排除)。新規配置したら true (= NewBytes 計上)。チャンク読みで
        /// token を観測しキャンセルに反応できる。
        /// (レビュー#1) 配置直後に mtime を**配置時刻**に打つ。pool blob は content-addressed で mtime は内容と無関係。
        /// File.Copy だと元ファイルの古い mtime を継承し、GC の grace (直近書込保護) が常に無効化される (= 多ホスト並行
        /// backup で他ホストの取得中 blob を誤って GC しうる) ため、配置時刻を刻んで grace を機能させる。
        /// </summary>
        private bool HashAndStore(string safeSrc, string poolRoot, CancellationToken token, out string hash)
        {
            string tmp = Path.Combine(poolRoot, ".tmp_" + Guid.NewGuid().ToString("N"));
            string safeTmp = FileOperationService.EnsureLongPath(tmp);
            Directory.CreateDirectory(FileOperationService.EnsureLongPath(poolRoot));
            try
            {
                using (var sha = SHA256.Create())
                using (var src = new FileStream(safeSrc, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                using (var dst = new FileStream(safeTmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    byte[] buf = new byte[1 << 20];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        sha.TransformBlock(buf, 0, n, null, 0);
                        dst.Write(buf, 0, n);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    var sb = new StringBuilder(sha.Hash.Length * 2);
                    foreach (var b in sha.Hash) sb.Append(b.ToString("x2"));
                    hash = sb.ToString();
                }

                string final = PoolPathFor(poolRoot, hash);
                string safeFinal = FileOperationService.EnsureLongPath(final);
                if (File.Exists(safeFinal)) { TryDeleteFile(tmp); return false; } // 既存 = 重複排除
                Directory.CreateDirectory(FileOperationService.EnsureLongPath(Path.GetDirectoryName(final)));
                try { File.Move(safeTmp, safeFinal); }
                catch (IOException) { TryDeleteFile(tmp); if (File.Exists(safeFinal)) return false; throw; }
                // grace 用に配置時刻を刻む。(round7 M-3) 失敗を握り潰さず Warn を残す: これに失敗すると blob は元ファイルの
                // 古い mtime を継承し、GC の grace (直近書込保護) が静かに無効化される = 多ホスト並行 backup で他ホストの
                // 取得中 blob を誤 GC しうる。本番 SMB が mtime 変更を拒否しないかは実機検証 (F-2)、世代基準 GC での根治は PR2/PR3。
                try { File.SetLastWriteTimeUtc(safeFinal, DateTime.UtcNow); }
                catch (Exception ex) { Logger.Warn("[AssetSnapshot] pool blob の配置時刻スタンプに失敗 (GC grace が無効化される恐れ): " + final + " : " + ex.Message); }
                return true;
            }
            catch
            {
                TryDeleteFile(tmp);
                throw;
            }
        }

        private static string ResolveUniqueManifest(string dir, string leaf)
        {
            string candidate = Path.Combine(dir, leaf + ManifestExt);
            int suffix = 2;
            while (File.Exists(FileOperationService.EnsureLongPath(candidate)))
            {
                candidate = Path.Combine(dir, leaf + "_" + suffix + ManifestExt);
                if (++suffix > 99) throw new Exception("manifest 名の衝突回避に失敗 (同 1 秒に 100 件以上): " + dir);
            }
            return candidate;
        }

        private static void WriteManifest(string path, string timestamp, string host, string trigger, Stats stats, List<string> entries)
        {
            var sb = new StringBuilder();
            // (#250 PR3a review #1) META 行末に skippedDir/skippedFile を追記し「部分取得 (IsPartial)」を永続化する。
            // restore が partial 世代を復元元にするとき、取りこぼされた live を「余剰」と誤判定して削除しないため
            // (旧 6 フィールド META は skipped 情報が無く complete 扱い＝後方互換)。
            sb.Append(MetaLinePrefix).Append('\t').Append(timestamp).Append('\t').Append(host).Append('\t')
              .Append(trigger).Append('\t').Append(stats.FileCount).Append('\t').Append(stats.Bytes).Append('\t')
              .Append(stats.SkippedDirCount).Append('\t').Append(stats.SkippedFileCount).Append('\n');
            foreach (var e in entries) sb.Append(e).Append('\n');
            File.WriteAllText(FileOperationService.EnsureLongPath(path), sb.ToString(), new UTF8Encoding(false));
        }

        internal IEnumerable<string> EnumerateManifests()
        {
            string root = GetSnapshotRootDirectory();
            foreach (var trigger in new[] { "auto", "manual" })
            {
                string dir = Path.Combine(root, trigger);
                if (!Directory.Exists(dir)) continue;
                // (レビュー M3) 列挙も長パス対応 (backup_dest が深いと manifest 実パスが MAX_PATH 超になりうる)。
                foreach (var f in Directory.GetFiles(ForceLong(dir), "*" + ManifestExt)) yield return f;
            }
        }

        internal static AssetSnapshotInfo ReadManifestHeader(string manifestPath)
        {
            var info = new AssetSnapshotInfo { ManifestPath = manifestPath };
            using (var r = new StreamReader(FileOperationService.EnsureLongPath(manifestPath)))
            {
                string first = r.ReadLine();
                if (first != null && first.StartsWith(MetaLinePrefix + "\t"))
                {
                    var f = first.Split('\t');
                    if (f.Length >= 6)
                    {
                        info.Timestamp = f[1];
                        info.Host = f[2];
                        info.TriggerType = f[3];
                        int fc; int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out fc); info.FileCount = fc;
                        long lb; long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out lb); info.LogicalBytes = lb;
                    }
                }
            }
            DateTime local;
            info.StartedAtLocal = (info.Timestamp != null && DateTime.TryParseExact(info.Timestamp, "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out local)) ? local : DateTime.MinValue;
            return info;
        }

        /// <summary>(レビュー L2) 全体を try/catch で best-effort 化し、retention/GC の例外で「manifest 書込済なのに
        /// 控え全体が Failed」と誤報告されるのを防ぐ。(レビュー M3) **GC は auto に限定**: manual manifest は retention
        /// 対象外で未参照 blob を生まないので GC 不要、サイズキャッシュだけ更新する。
        /// (#295 round7 — stale コメント訂正) 旧 docstring は「auto は **lease で多ホスト排他されるため並行 GC が
        /// 起きない**」と書いていたが、その lease (`TryAcquireBackupLease`) は #295 で撤去済 (操作単位トリガ移行で
        /// auto を interval 毎・全ホスト 1 回に律速していた間接的排他が消えた)。操作単位化 + lease 撤去で多ホスト並行
        /// GC の窓自体は拡大した (旧: 全ホストで interval 毎 1 回 → 新: 全ホストが毎操作)。**現状の緩和は Gc​GracePeriod
        /// = 24h** (round7 #2 の対応で 1h から延長): 多ホストが並行 backup/GC する際、片方の **初回フル ingest (~6GB)**
        /// が走る間に他ホストの GC が「取得中だが目録未記載の blob」を未参照と誤判定して回収しうる窓 (= #250 round8 C2)
        /// を、書きたて blob を 24h 守ることで塞ぐ (現実的 ingest は数十分〜1h 級で 24h を超えない)。24h を超える非現実的
        /// ingest だけ残り、それは heartbeat-lease が要る #250 の多ホスト整合領域。なお同 PC 重複起動は Program.cs の
        /// Named Mutex で物理 block 済、別 PC 同時編集は SessionConflictHelper が警告するので、C2 に至るには警告を無視
        /// した別 PC 同時編集 + 24h 超 ingest の二重条件が要る。**被害も限定的**: GC は pool のみ対象で live games/ は
        /// 触らないため、壊れるのは当該バックアップ世代だけ (次回 backup で live から再コピーされ自己回復、live は無事)。</summary>
        private void ApplyRetentionAndGc(string triggerType, long newBytesCopied, string excludeManifestPath = null)
        {
            try
            {
                if (string.Equals(triggerType, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    // (round9) DB バックアップと同じ保持世代数に統一 (1 バックアップ = .db + アセット manifest をペア保持/削除)。
                    int count = _settingsRepo.GetInt32("backup_retention_count", 30);
                    if (count > 0)
                    {
                        string autoDir = Path.Combine(GetSnapshotRootDirectory(), "auto");
                        if (Directory.Exists(autoDir))
                        {
                            // (round6 High) replace-in-session で coordinator がこの直後に消す前 manifest を母数から除外する
                            // (DB 側 ApplyRetention と同型。含めると過去世代の manifest を 1 件余計に削り、その manifest だけが
                            // 参照していた pool blob まで直後の GarbageCollectPool で道連れに消える)。除外世代は coordinator が消す。
                            // 比較はファイル名で行う: Directory.GetFiles(ForceLong(...)) は `\\?\` prefix 付き path を返す一方、
                            // excludeManifestPath (CreateSnapshot の戻り) は prefix 無しなので full path 比較だと一致しない。
                            // manifest 名は auto dir 内で一意 (ResolveUniqueManifest 保証) なのでファイル名比較で過不足なし。
                            string excludeManifestName = string.IsNullOrEmpty(excludeManifestPath) ? null : Path.GetFileName(excludeManifestPath);
                            var manifests = Directory.GetFiles(ForceLong(autoDir), "*" + ManifestExt).AsEnumerable();
                            if (excludeManifestName != null)
                                manifests = manifests.Where(p => !string.Equals(Path.GetFileName(p), excludeManifestName, StringComparison.OrdinalIgnoreCase));
                            var stale = manifests
                                .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Skip(count).ToList();
                            foreach (var m in stale)
                            {
                                try { File.Delete(FileOperationService.EnsureLongPath(m)); Logger.Info("[AssetSnapshot] 古い世代(manifest)を削除: " + Path.GetFileName(m)); }
                                catch (Exception ex) { Logger.Warn("[AssetSnapshot] manifest 削除失敗: " + m + " : " + ex.Message); }
                            }
                        }
                    }
                    GarbageCollectPool(); // GC + .poolsize 更新
                }
                else
                {
                    // manual: GC しない (未参照 blob を生まない)。プールは削除されず単調増加するだけなので、
                    // (レビュー#4) プール全列挙でなく「既存キャッシュ + 今回の新規コピー分」の差分でサイズ更新する
                    // (SMB で毎回数千 blob を列挙するコストを回避)。初回 (.poolsize 不在) は 0 + 新規 = 実体合計。
                    WritePoolSizeCache(GetPoolPhysicalBytes() + newBytesCopied);
                }
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] retention/GC 失敗 (無害、控え自体は成功): " + ex.Message); }
        }

        /// <summary>残る全 manifest が参照する hash 集合に無い pool ファイルを削除 (直近書込は grace で残す)。</summary>
        private void GarbageCollectPool()
        {
            try
            {
                string pool = GetPoolRootDirectory();
                if (!Directory.Exists(pool)) return;
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (var manifest in EnumerateManifests())
                {
                    try
                    {
                        foreach (var line in File.ReadLines(FileOperationService.EnsureLongPath(manifest)))
                        {
                            if (TryParseManifestEntryLine(line, out ManifestEntry e)) { referenced.Add(e.Hash); continue; }
                            // (review #2) GC は「保護側に倒す」。strict parse に失敗した行でも META でなく先頭フィールドがあれば
                            // hash 候補として参照集合に入れる。これにより破損行 (size/mtime 欠け/非数値等) で実 hash が落ち、
                            // まだ参照中の blob を誤 GC するのを防ぐ (旧実装の寛容な参照抽出を GC でのみ維持。restore/cache は strict)。
                            if (!line.StartsWith(MetaLinePrefix + "\t"))
                            {
                                int tab = line.IndexOf('\t');
                                if (tab > 0) referenced.Add(line.Substring(0, tab));
                            }
                        }
                    }
                    // (レビュー L2) manifest を 1 件でも読めなければ参照集合が不完全 → 誤って参照中 blob を消しうるので
                    // GC 全体を保守的に中止する。この早期 return では WritePoolSizeCache に到達せず .poolsize は前回値の
                    // まま据え置き = サイズ表示が一時 stale になるが、過大表示はあれど過小表示はしない安全側。
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC: manifest 読込失敗のため全 GC を保守的に中止 (.poolsize 据え置き): " + manifest + " : " + ex.Message); return; }
                }
                DateTime cutoff = DateTime.UtcNow - GcGracePeriod;
                int removed = 0; long freed = 0; long surviving = 0;
                foreach (var f in Directory.EnumerateFiles(ForceLong(pool), "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (name == PoolSizeFileName) continue; // メタファイルは触らない・サイズにも数えない
                    bool isTmp = name.Contains(".tmp_");
                    long len = SafeLen(f);
                    // 参照されている blob は残す。進行中 temp は触らない。grace 超過の未参照/orphan-temp は削除。
                    if (!isTmp && referenced.Contains(name)) { surviving += len; continue; }
                    try
                    {
                        if (File.GetLastWriteTimeUtc(f) > cutoff) { if (!isTmp) surviving += len; continue; } // grace: 直近書込は残す
                        File.Delete(FileOperationService.EnsureLongPath(f));
                        removed++; freed += len;
                    }
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC: pool 削除失敗: " + f + " : " + ex.Message); if (!isTmp) surviving += len; }
                }
                if (removed > 0) Logger.Info(string.Format("[AssetSnapshot] GC: 未参照 {0} 件 / {1:F2}MB を解放", removed, freed / 1048576.0));
                WritePoolSizeCache(surviving); // (レビュー M1) UI 即時読み用にサイズを記録
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC 失敗 (無害、次回再試行): " + ex.Message); }
        }

        private static int SafeCountFiles(string dir)
        {
            // (#250 M1) WalkTree は ExcludedFolders を除外せず丸ごと控える (バックアップなので Electron 系の
            // node_modules 等の「実行時に必須」なフォルダも残す。除外すると復元データが壊れる)。進捗分母も
            // applyExclusions:false で walk と揃える。
            try { return Directory.Exists(dir) ? FileOperationService.CountFiles(dir, null, applyExclusions: false) : 0; }
            catch { return 0; }
        }

        private static long SafeLen(string path)
        {
            try { return new FileInfo(FileOperationService.EnsureLongPath(path)).Length; } catch { return 0; }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(FileOperationService.EnsureLongPath(path))) File.Delete(FileOperationService.EnsureLongPath(path)); }
            catch { }
        }

        private sealed class Stats
        {
            public int FileCount;
            public long Bytes;     // 論理合計
            public long NewBytes;  // 今回プールへ新規コピーした分
            public int SkippedDirCount; // (round8 C1) 列挙できず skip した深部フォルダ数 (= 部分取得の根拠)
            public int SkippedFileCount; // (#299 review C-1) 並行編集での消失 / ロック等で控えられず skip した個別ファイル数 (= 部分取得の根拠)
        }
    }
}
