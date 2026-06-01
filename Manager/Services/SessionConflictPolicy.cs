namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#278) セッション競合警告を出すべきかの**純粋な判定ロジック**。UI / セッション検出から切り離して
    /// 単体テスト可能にするためのポリシークラス（`MainForm.CheckSessionConflictBeforeWrite` から委譲）。
    ///
    /// 背景: 文化祭当日、Launcher を立てたまま Manager でゲーム情報等を編集したい。Launcher は DB を
    /// 読み取り専用でしか触らない (SELECT のみ、heartbeat は file 書き込み) ため、journal_mode=DELETE +
    /// busy_timeout の下で「Manager の行 write」と「Launcher の read」は SQLite が安全に調停する
    /// (write-write 競合なし・持続ロックなし)。したがって Launcher 単独稼働なら通常の行編集で警告は不要。
    /// ただし toneprism.db を**ファイルごと差し替える/再作成する**操作 (Restore / DB 初期化) は、Launcher が
    /// ストア表示中に DB ファイルハンドルを握っていると `File.Replace` / 再作成が衝突しうるため警告を維持する。
    /// 別 Manager 検出時は write-write の本当の危険なので操作種別を問わず常に警告。
    /// </summary>
    public static class SessionConflictPolicy
    {
        /// <summary>
        /// 競合警告ダイアログを出すべきか。
        /// </summary>
        /// <param name="otherManagerCount">自分以外に検出した稼働中 Manager セッション数。</param>
        /// <param name="launcherCount">検出した稼働中 Launcher セッション数。</param>
        /// <param name="operationDescription">操作ラベル (例: "ゲーム編集" / "バックアップ復元")。</param>
        /// <returns>true=警告を出す / false=警告不要 (操作続行)。</returns>
        public static bool ShouldWarn(int otherManagerCount, int launcherCount, string operationDescription)
        {
            // 誰も他にいない → 警告不要。
            if (otherManagerCount == 0 && launcherCount == 0) return false;

            // 別 Manager がいる → 操作種別を問わず常に警告 (両者 write での衝突・DB 破損リスク)。
            if (otherManagerCount > 0) return true;

            // ここに来るのは「別 Manager は無し・Launcher のみ稼働」。
            // DB ファイルを丸ごと差し替える操作だけ警告 (Launcher がファイルを開いていると衝突しうる)。
            // 通常の行編集は Launcher の read と安全に共存するため警告しない。
            return IsWholeDbReplacingOperation(operationDescription);
        }

        /// <summary>
        /// toneprism.db を**ファイルごと差し替える/再作成する**破壊的操作か。
        ///
        /// 注意: 操作種別を文字列ラベルで判定している (呼び出し API が operationDescription の string のみ
        /// 受けるため)。**新たに DB ファイルを置換/再作成する操作を追加したら必ずここに対応ラベルを足すこと。**
        /// 足し忘れると Launcher 単独稼働時に警告なしで実行され、ストア表示中だとファイル差し替えが衝突しうる。
        /// (将来 #278 ② で store_browse が DB ハンドルを握りっぱなしにしなくなれば、この例外自体を緩められる。)
        /// </summary>
        public static bool IsWholeDbReplacingOperation(string operationDescription)
        {
            return operationDescription == "バックアップ復元"      // RestoreService: File.Replace で toneprism.db を差し替え
                || operationDescription == "データベース初期化";    // ResetDatabase: DB + games/ をまっさらに再作成
        }
    }
}
