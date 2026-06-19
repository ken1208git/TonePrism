using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) ゲーム追加/編集フォームの WPF bindable state（add/edit 共通コア）。<see cref="GameInfo"/> の各フィールドを
    /// INotifyPropertyChanged プロパティとして公開し、XAML から two-way バインドする。
    ///
    /// 設計の肝: 人数/難易度/時間/年を <c>int?</c> で直持ちすることで、WinForms 版 (EditGameForm) の「NumericUpDown が
    /// null を表現できない」ための null 保護スナップショット glue（<c>_versionMinPlayersWasNullOnLoad</c> 系のフラグ群）が
    /// 丸ごと不要になる。入力検証は <see cref="INotifyDataErrorInfo"/>（gameId/title）。保存オーケストレーション
    /// (旧 btnOK_Click 相当) は EditGamePage 側に置き抽出済み service を呼ぶ（CLAUDE.md「UI は薄く、ロジックは外へ」+
    /// 既存 GameListPage の code-behind 流）。版管理は <c>EditViewModel</c> が継承して足す。
    /// </summary>
    public class GameFormViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        // ===== 共通フィールド (GameInfo に 1:1) =====
        private string _gameId;
        public string GameId { get => _gameId; set { if (SetField(ref _gameId, value)) ValidateGameId(); } }

        private string _title;
        public string Title { get => _title; set { if (SetField(ref _title, value)) ValidateTitle(); } }

        private string _description;
        public string Description { get => _description; set => SetField(ref _description, value); }

        private string _arguments;
        public string Arguments { get => _arguments; set => SetField(ref _arguments, value); }

        private int? _releaseYear;
        public int? ReleaseYear { get => _releaseYear; set => SetField(ref _releaseYear, value); }

        private bool _releaseYearUnknown;
        public bool ReleaseYearUnknown { get => _releaseYearUnknown; set => SetField(ref _releaseYearUnknown, value); }

        // 人数: min<=max を「相手を押し上げ/押し下げ」で保つ（範囲スライダー的・[[feedback_minmax_coupled_clamp]]）。
        // 片方 null の間はクランプしない。_clamping で相手側 setter の再入クランプを抑止。
        // _suppressClamp は load 時専用: 連動クランプを止めて破損 (min>max) を温存し、保存時に検証でブロックさせる
        // (閲覧しただけで silent heal せず、SemVer 不正版と同じ「直させる」方針に揃える)。対話入力時はクランプ有効。
        private bool _clamping;
        private bool _suppressClamp;
        private int? _minPlayers;
        public int? MinPlayers
        {
            get => _minPlayers;
            set
            {
                if (!SetField(ref _minPlayers, value) || _clamping || _suppressClamp) return;
                if (_minPlayers.HasValue && _maxPlayers.HasValue && _minPlayers.Value > _maxPlayers.Value)
                {
                    _clamping = true;
                    try { MaxPlayers = _minPlayers; }   // 上限を押し上げ
                    finally { _clamping = false; }      // binding 更新が万一 throw しても guard を残さない
                }
            }
        }

        private int? _maxPlayers;
        public int? MaxPlayers
        {
            get => _maxPlayers;
            set
            {
                if (!SetField(ref _maxPlayers, value) || _clamping || _suppressClamp) return;
                if (_minPlayers.HasValue && _maxPlayers.HasValue && _maxPlayers.Value < _minPlayers.Value)
                {
                    _clamping = true;
                    try { MinPlayers = _maxPlayers; }   // 下限を押し下げ
                    finally { _clamping = false; }      // binding 更新が万一 throw しても guard を残さない
                }
            }
        }

        /// <summary>load 時専用: 連動クランプを抑止して min/max をそのまま設定する。破損 (min&gt;max) を温存し、保存時に
        /// <see cref="GameVersionSetValidator"/> の PlayerCountViolations でブロック → ユーザーに直させる
        /// (SemVer 不正版と同方針)。範囲 [1,99] クランプ・null→1 コアースは呼び出し側で済ませてから渡す。</summary>
        protected void SetPlayerCountsForLoad(int? min, int? max)
        {
            _suppressClamp = true;
            try { MinPlayers = min; MaxPlayers = max; }
            finally { _suppressClamp = false; }
        }

        private int? _difficulty;
        public int? Difficulty { get => _difficulty; set => SetField(ref _difficulty, value); }

        private int? _playTime;
        public int? PlayTime { get => _playTime; set => SetField(ref _playTime, value); }

        private int _supportedConnection;
        public int SupportedConnection { get => _supportedConnection; set => SetField(ref _supportedConnection, value); }

        private bool _controllerSupport;
        public bool ControllerSupport { get => _controllerSupport; set => SetField(ref _controllerSupport, value); }

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

        public ObservableCollection<string> SelectedGenres { get; } = new ObservableCollection<string>();

        private string _thumbnailPath;
        public string ThumbnailPath { get => _thumbnailPath; set => SetField(ref _thumbnailPath, value); }

        private string _backgroundPath;
        public string BackgroundPath { get => _backgroundPath; set => SetField(ref _backgroundPath, value); }

        private string _executablePath;
        public string ExecutablePath { get => _executablePath; set => SetField(ref _executablePath, value); }

        // (#324) 製作者はカードからインライン編集するため DeveloperEditViewModel で保持 (Grade 文字列 ↔ 数値+不明/教員)。
        public ObservableCollection<DeveloperEditViewModel> Developers { get; } = new ObservableCollection<DeveloperEditViewModel>();

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        protected void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ===== INotifyDataErrorInfo (gameId / title) =====
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        public bool HasErrors => _errors.Count > 0;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public IEnumerable GetErrors(string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName) && _errors.TryGetValue(propertyName, out var msg))
                return new[] { msg };
            return Array.Empty<string>();
        }

        protected void SetError(string prop, string msg)
        {
            bool changed;
            if (msg == null)
            {
                changed = _errors.Remove(prop);
            }
            else
            {
                changed = !_errors.TryGetValue(prop, out var cur) || cur != msg;
                _errors[prop] = msg;
            }
            if (changed) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
        }

        private void ValidateGameId()
            => SetError(nameof(GameId), GameFormHelper.IsValidGameId(GameId ?? "", out var err) ? null : err);

        private void ValidateTitle()
            => SetError(nameof(Title), string.IsNullOrWhiteSpace(Title) ? "ゲームタイトルを入力してください。" : null);

        /// <summary>全項目を検証してエラー状態を更新する（保存直前に呼ぶ）。派生は base を呼んでから版固有検証を足す。</summary>
        public virtual void ValidateAll()
        {
            ValidateGameId();
            ValidateTitle();
        }
    }
}
