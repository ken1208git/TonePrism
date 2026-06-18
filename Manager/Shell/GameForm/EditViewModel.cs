using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TonePrism.Manager;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) ゲーム編集の WPF bindable state。<see cref="GameFormViewModel"/>(共通コア) に版管理を足す。
    /// 旧 EditGameForm の LoadVersions / LoadGameDataForVersion / SaveGameDataToVersion を移植し、
    /// 「版切替で前版へ commit → 新版を load」を <see cref="SelectedVersion"/> setter に集約する。
    ///
    /// 移植の方針:
    /// - **アクティブ版フォールバック**(旧初期版行の空フィールドを games 行値で healing) は本物のドメインロジックなので保存。
    ///   active 判定は version 文字列比較でなく **DB id 比較** (rename 後も維持、#234/#158 round 7 M-2)。
    /// - 人数/難易度/時間/年を <c>int?</c> 直持ちにしたことで WinForms の null 保護スナップショット glue は不要に。
    /// - 範囲外の破損 DB 値 (人数 0/200 等) は load 時に [1,99] 等へ clamp。旧は「clamp 値を書き戻さない」snapshot 保護が
    ///   あったが、それを再現するとスナップショット復活で int? 化の意味が消えるため PR1 では簡素化 (= 破損は保存時に healing)。
    /// - 外部画像取込 (gameFolder 外の画像選択 → 保存時コピー) は **#324 PR4 送り**。PR1 は既存パスの表示↔相対化のみ扱う。
    /// 保存オーケストレーション(旧 btnOK_Click) は EditGamePage 側に置き、抽出済み service を呼ぶ。
    /// </summary>
    public class EditViewModel : GameFormViewModel
    {
        public const int PlayerMin = 1;
        public const int PlayerMax = 99;

        private readonly DatabaseManager _db;

        public GameInfo OriginalGame { get; }
        public string GameFolder { get; private set; }

        public ObservableCollection<GameVersion> Versions { get; } = new ObservableCollection<GameVersion>();

        /// <summary>初期選択 = 起動対象 (games.version 一致) 版の DB id。OK 時のアクティブ版切替検出 + load 時の healing 判定基準。</summary>
        public int? InitialSelectedVersionId { get; private set; }

        /// <summary>LoadVersions 時点の DB-fetched version 文字列 (id→ver)。版フォルダ rename 検出 / disk leaf 解決の SoT。</summary>
        public Dictionary<int, string> OriginalVersionByDbId { get; } = new Dictionary<int, string>();

        /// <summary>SemVer 不正な version (load 時に警告表示するため page が読む。"  - id=N: 'ver'" 形式)。</summary>
        public List<string> MalformedVersionsOnLoad { get; } = new List<string>();

        /// <summary>不正 version の集約警告を EditGamePage が一度だけ出すためのフラグ。VM 単位 (= 編集 1 回単位) で
        /// 持つことで、NavigationView がページを type 単位で cache・再利用しても別ゲーム編集で再警告できる
        /// (ページインスタンス固定フラグだと 2 ゲーム目以降で警告が抑止される)。</summary>
        public bool MalformedWarningShown { get; set; }

        // ===== 版固有フィールド (共通コアに無いもの) =====
        private string _versionName;   // semver 文字列 (SemverInput が parse/format)
        public string VersionName { get => _versionName; set => SetField(ref _versionName, value); }

        private string _versionUpdateNote;
        public string VersionUpdateNote { get => _versionUpdateNote; set => SetField(ref _versionUpdateNote, value); }

        private GameVersion _selectedVersion;
        public GameVersion SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (ReferenceEquals(_selectedVersion, value)) return;
                // 切替前に現版へ in-memory commit (旧 cmbVersionList_SelectedIndexChanged の commit→load)。
                if (_selectedVersion != null) CommitToVersion(_selectedVersion);
                _selectedVersion = value;
                Raise();
                if (_selectedVersion != null) LoadFromVersion(_selectedVersion);
            }
        }

        public EditViewModel(DatabaseManager db, GameInfo game)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            OriginalGame = game ?? throw new ArgumentNullException(nameof(game));
            GameFolder = PathManager.GetGameFolder(game.GameId);
            GameId = game.GameId;          // slug 手入力 (編集可)
            IsVisible = game.IsVisible;
            // 破損 DB の範囲外 release_year (手書き SQL / 旧 schema 由来。例 0 / 9999) は NumberBox の範囲 [1970,3000] と
            // ズレて「表示クランプ値 ≠ VM 生値」になり破損値を温存する。範囲外は「不明」に倒す (旧 EditGameForm 踏襲)。
            bool yearInRange = game.ReleaseYear.HasValue && game.ReleaseYear.Value >= 1970 && game.ReleaseYear.Value <= 3000;
            ReleaseYear = yearInRange ? game.ReleaseYear : (int?)null;
            ReleaseYearUnknown = !yearInRange;
            LoadVersions();
        }

        private bool IsActive(GameVersion v) => InitialSelectedVersionId.HasValue && v != null && v.Id == InitialSelectedVersionId.Value;

        /// <summary>gameId rename 成功後に in-memory 状態を新 ID へ追従させる (GameFolder + 全版の GameId)。
        /// 版フォルダ rename はこの新 GameFolder を base に走るため、gameId rename の効果を取り込む (#158 H1 と同趣旨)。</summary>
        public void ApplyGameIdRename(string newGameId, string newFolder)
        {
            GameFolder = newFolder;
            GameId = newGameId;
            foreach (var v in Versions) v.GameId = newGameId;
        }

        private void LoadVersions()
        {
            OriginalVersionByDbId.Clear();
            Versions.Clear();
            MalformedVersionsOnLoad.Clear();
            var versions = _db.GetGameVersions(OriginalGame.GameId);
            foreach (var v in versions)
            {
                Versions.Add(v);
                OriginalVersionByDbId[v.Id] = v.Version;
                if (!SemverInputControl.TryNormalize(v.Version ?? "", out _))
                    MalformedVersionsOnLoad.Add("  - id=" + v.Id + ": '" + (v.Version ?? "(null)") + "'");
            }

            // 起動対象版 = games.version と一致する版 (OrdinalIgnoreCase、rename normalize 揺れ吸収)。無ければ先頭 (最新)。
            GameVersion initial = null;
            if (OriginalGame.Version != null)
                initial = Versions.FirstOrDefault(v => string.Equals(v.Version, OriginalGame.Version, StringComparison.OrdinalIgnoreCase));
            if (initial == null && Versions.Count > 0) initial = Versions[0];

            // InitialSelectedVersionId は SelectedVersion 設定 (= LoadFromVersion) より前に確定させる
            // (旧 #158 H2: active 判定を初回 load から有効にして healing 非対称を防ぐ)。
            InitialSelectedVersionId = initial?.Id;
            if (initial != null) SelectedVersion = initial;   // setter が LoadFromVersion を呼ぶ
        }

        /// <summary>版 → VM フィールド (アクティブ版は空欄を games 行値でフォールバック healing)。</summary>
        private void LoadFromVersion(GameVersion v)
        {
            bool active = IsActive(v);
            VersionName = v.Version ?? "";

            Title = !string.IsNullOrWhiteSpace(v.Title) ? v.Title : (active ? (OriginalGame.Title ?? "") : "");
            Description = !string.IsNullOrWhiteSpace(v.Description) ? v.Description : (active ? OriginalGame.Description : null);
            Arguments = !string.IsNullOrWhiteSpace(v.Arguments) ? v.Arguments : (active ? OriginalGame.Arguments : null);
            VersionUpdateNote = v.UpdateNote ?? "";

            var genre = (v.Genre != null && v.Genre.Count > 0) ? v.Genre : (active ? OriginalGame.Genre : null);
            SelectedGenres.Clear();
            if (genre != null) foreach (var g in genre) SelectedGenres.Add(g);

            // プレイ人数は常に数値 (旧 WinForms の NumericUpDown 同様、不明=null 不可)。DB が null でも 1 にコアース。
            MinPlayers = ClampPlayer(v.MinPlayers ?? (active ? OriginalGame.MinPlayers : null)) ?? 1;
            MaxPlayers = ClampPlayer(v.MaxPlayers ?? (active ? OriginalGame.MaxPlayers : null)) ?? 1;
            Difficulty = ValidLevel(v.Difficulty) ?? (active ? ValidLevel(OriginalGame.Difficulty) : null);
            PlayTime = ValidLevel(v.PlayTime) ?? (active ? ValidLevel(OriginalGame.PlayTime) : null);
            // 通信/コントローラは非 nullable で「未設定」を判別できないため、アクティブ版は games 行 (mirror=真値) を採用。
            // 破損 DB の範囲外値 (手書き SQL / 旧 schema 由来) は 0-2 にクランプ。これが無いと ComboBox.SelectedIndex が
            // 範囲外 → WPF が -1 を書き戻し → 保存で -1 格納の恐れ (旧 LoadGameDataForVersion の connToShow クランプを踏襲)。
            SupportedConnection = ClampConnection(active ? OriginalGame.SupportedConnection : v.SupportedConnection);
            ControllerSupport = active ? OriginalGame.ControllerSupport : v.ControllerSupport;

            string exe = !string.IsNullOrEmpty(v.ExecutablePath) ? v.ExecutablePath : (active ? OriginalGame.ExecutablePath : null);
            string thumb = !string.IsNullOrEmpty(v.ThumbnailPath) ? v.ThumbnailPath : (active ? OriginalGame.ThumbnailPath : null);
            string bg = !string.IsNullOrEmpty(v.BackgroundPath) ? v.BackgroundPath : (active ? OriginalGame.BackgroundPath : null);
            ExecutablePath = string.IsNullOrEmpty(exe) ? "" : PathConversionHelper.ToAbsolutePath(GameFolder, exe);
            ThumbnailPath = string.IsNullOrEmpty(thumb) ? "" : PathConversionHelper.ToAbsolutePath(GameFolder, thumb);
            BackgroundPath = string.IsNullOrEmpty(bg) ? "" : PathConversionHelper.ToAbsolutePath(GameFolder, bg);

            // Developers (版が空ならアクティブ版に限り games の製作者をディープコピー。Id は引き継がない)。
            // カードでインライン編集するため DeveloperEditViewModel に wrap (Grade 文字列 ↔ 数値+不明/教員)。
            Developers.Clear();
            var devs = (v.Developers != null && v.Developers.Count > 0) ? v.Developers
                       : (active ? OriginalGame.Developers : null);
            if (devs != null)
                foreach (var d in devs)
                    Developers.Add(new DeveloperEditViewModel(d));
        }

        /// <summary>VM フィールド → 版 (deep-copy developers / 画像は gameFolder 基準で相対化)。版切替前・保存前に呼ぶ。</summary>
        public void CommitToVersion(GameVersion v)
        {
            if (v == null) return;
            v.Version = string.IsNullOrWhiteSpace(VersionName) ? v.Version : VersionName.Trim();
            v.Title = (Title ?? "").Trim();
            v.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            v.Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim();
            v.UpdateNote = string.IsNullOrWhiteSpace(VersionUpdateNote) ? null : VersionUpdateNote.Trim();
            v.Genre = SelectedGenres.ToList();
            v.MinPlayers = MinPlayers ?? 1;   // プレイ人数は常に数値で保存 (編集中に空にされても 1 に倒す)
            v.MaxPlayers = MaxPlayers ?? 1;
            v.Difficulty = Difficulty;
            v.PlayTime = PlayTime;
            v.ControllerSupport = ControllerSupport;
            v.SupportedConnection = SupportedConnection;
            v.ExecutablePath = ToRel(ExecutablePath);
            v.ThumbnailPath = ToRel(ThumbnailPath);
            v.BackgroundPath = ToRel(BackgroundPath);
            // 姓名どちらも空のカード (未入力で追加されただけ) は除外して保存。
            v.Developers = Developers
                .Where(d => !d.IsBlank)
                .Select(d => d.ToModel())
                .ToList();
        }

        // gameFolder 基準で相対化。gameFolder 外 / 空は null (= 外部画像取込は PR4 で対応、PR1 は null 格下げ)。
        private string ToRel(string abs)
        {
            if (string.IsNullOrWhiteSpace(abs)) return null;
            if (!PathConversionHelper.IsPathInside(GameFolder, abs)) return null;
            return PathConversionHelper.ToRelativePath(GameFolder, abs.Trim());
        }

        private static int? ClampPlayer(int? v) => v.HasValue ? Math.Max(PlayerMin, Math.Min(PlayerMax, v.Value)) : (int?)null;
        private static int? ValidLevel(int? v) => (v.HasValue && v.Value >= 1 && v.Value <= 3) ? v : (int?)null;
        private static int ClampConnection(int v) => (v >= 0 && v <= 2) ? v : 0;   // なし/ローカル/オンライン の 3 値
    }
}
