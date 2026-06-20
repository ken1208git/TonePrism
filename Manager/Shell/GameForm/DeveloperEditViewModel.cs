using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 製作者 1 件のインライン編集 state。<see cref="DeveloperInfo"/> の <c>Grade</c> 文字列
    /// (""=不明 / "0"=教員 / "N"=N期生) を、カード上で扱いやすい「数値 + 不明/教員 の排他チェック」に分解する。
    /// 旧 WinForms の DeveloperForm と同じ意味論で、ダイアログを介さずカードから直接 姓/名/期生 を編集できるようにする。
    /// load 時に <see cref="DeveloperInfo"/> を wrap、保存時に <see cref="ToModel"/> で戻す。
    /// </summary>
    public class DeveloperEditViewModel : INotifyPropertyChanged
    {
        // developers.game_id。新規カードは null (旧 DeveloperForm 追加も GameId 未設定で、DB 層が付与する)。
        public string GameId { get; set; }

        private string _lastName;
        public string LastName { get => _lastName; set => SetField(ref _lastName, value); }

        private string _firstName;
        public string FirstName { get => _firstName; set => SetField(ref _firstName, value); }

        private int? _gradeNumber = 1;
        public int? GradeNumber { get => _gradeNumber; set => SetField(ref _gradeNumber, value); }

        private bool _isTeacher;
        public bool IsTeacher
        {
            get => _isTeacher;
            set { if (SetField(ref _isTeacher, value) && value) IsUnknown = false; RaiseGradeEnabled(); }
        }

        private bool _isUnknown;
        public bool IsUnknown
        {
            get => _isUnknown;
            set { if (SetField(ref _isUnknown, value) && value) IsTeacher = false; RaiseGradeEnabled(); }
        }

        /// <summary>期生の数値入力が有効か (不明/教員 のときは無効)。</summary>
        public bool GradeNumberEnabled => !_isTeacher && !_isUnknown;
        /// <summary>期生数値を視覚的にグレーアウトすべきか (不明/教員 のとき)。</summary>
        public bool GradeNumberDimmed => _isTeacher || _isUnknown;

        /// <summary>姓・名どちらも空 = 実体のないカード (保存時に除外する)。</summary>
        public bool IsBlank => string.IsNullOrWhiteSpace(LastName) && string.IsNullOrWhiteSpace(FirstName);

        public DeveloperEditViewModel() { }   // 新規追加カード (既定 1期生)

        public DeveloperEditViewModel(DeveloperInfo d)
        {
            GameId = d.GameId;
            _lastName = d.LastName;
            _firstName = d.FirstName;
            // Grade 解析は DeveloperForm と同じ防御 (空=不明 / 0(="00"," 0 "含む)=教員 / N>=1=N期生 / 異常値=1)。
            string g = d.Grade ?? "";
            if (string.IsNullOrWhiteSpace(g)) { _isUnknown = true; _gradeNumber = 1; }
            else if (int.TryParse(g, out int gv) && gv == 0) { _isTeacher = true; _gradeNumber = 1; }
            else if (int.TryParse(g, out int gv2) && gv2 >= 1)
            {
                // (レビュー #5) NumberBox 上限 999 超えは、bound 値の silent coerce に任せず明示 clamp + Warn ログ
                // (旧 DeveloperForm の SetClampedNumericValue 踏襲。grade>999 は非現実だが破損 DB への防御)。
                if (gv2 > 999) { Logger.Warn("[DeveloperEditViewModel] grade " + gv2 + " が上限 999 を超過、999 に clamp"); gv2 = 999; }
                _gradeNumber = gv2;
            }
            else { _gradeNumber = 1; }
        }

        /// <summary>編集 state → 保存用 <see cref="DeveloperInfo"/> (Id は引き継がない: 版の製作者は毎回入れ直し)。</summary>
        public DeveloperInfo ToModel() => new DeveloperInfo
        {
            GameId = GameId,
            LastName = (LastName ?? "").Trim(),
            FirstName = (FirstName ?? "").Trim(),
            Grade = IsUnknown ? "" : (IsTeacher ? "0" : (GradeNumber ?? 1).ToString())
        };

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void RaiseGradeEnabled()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradeNumberEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GradeNumberDimmed)));
        }
    }
}
