using System;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// 製作者情報入力フォーム
    /// </summary>
    public partial class DeveloperForm : Form
    {
        private DeveloperInfo developer;

        /// <summary>
        /// (round 5 M6) Grade DB 値が numGrade.Maximum (=999999) を超える場合、生代入で
        /// ArgumentOutOfRangeException が出てダイアログ自体が表示できない (= その製作者を編集する
        /// 経路が永久 block) 経路があった。手書き SQL / 旧 schema 復元で `grade="9999999"` (8 桁) 等が
        /// 残っている場合に発火。EditGameForm が round 2 M2 で同型 hazard を SetClampedNumericValue で
        /// 直した規約と非対称だったので、helper を GameFormHelper に昇格して DeveloperForm からも使う。
        /// 範囲外で clamp 発生したかどうかは戻り値 false で取れるが、本フォームは「flag による null 維持」
        /// パターンを持たない (Grade は text 列 + 再代入で常に numGrade.Value.ToString() 化される実装)
        /// ため flag は使わず、clamp された値で素直に save される。trail は Logger.Warn で残る。
        /// </summary>
        private static bool SetClampedNumericValue(NumericUpDown nud, int value, string fieldName)
            => GameFormHelper.SetClampedNumericValue(nud, value, fieldName, "DeveloperForm");

        /// <summary>
        /// 入力された製作者情報
        /// </summary>
        public DeveloperInfo Developer { get; private set; }

        /// <summary>
        /// 新規追加用コンストラクタ
        /// </summary>
        public DeveloperForm()
            : this(null)
        {
        }

        /// <summary>
        /// 編集用コンストラクタ
        /// </summary>
        public DeveloperForm(DeveloperInfo developer)
        {
            InitializeComponent();
            this.developer = developer;
            Developer = null;
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void DeveloperForm_Load(object sender, EventArgs e)
        {
            if (developer != null)
            {
                // 編集モード：既存の値を設定
                txtLastName.Text = developer.LastName ?? "";
                txtFirstName.Text = developer.FirstName ?? "";
                
                // 期生の設定（空欄=不明 / "0"=教員 / それ以外=N期生）。(#313) 「不明」「教員」はチェックボックス化。
                if (string.IsNullOrEmpty(developer.Grade))
                {
                    // DB 上 grade が空 = 不明。チェックして numGrade を無効化（仮値は1）。
                    // 旧実装は空でも numGrade=1 にして保存時 "1" へ coerce し「不明」を失っていた。
                    numGrade.Value = 1;
                    chkGradeUnknown.Checked = true;
                }
                else if (developer.Grade == "0")
                {
                    // "0" = 教員。チェックして numGrade を無効化。
                    numGrade.Value = 1;
                    chkGradeTeacher.Checked = true;
                }
                else
                {
                    int gradeValue;
                    if (int.TryParse(developer.Grade, out gradeValue) && gradeValue >= 1)
                    {
                        // (round 5 M6) 旧実装は `numGrade.Value = gradeValue;` 生代入で、numGrade.Maximum (=999999)
                        // を超える DB 値 (例: "9999999") があると ArgumentOutOfRangeException で dialog 自体が
                        // 開けない永久 block 経路があった。clamp helper 経由で安全に代入する。
                        SetClampedNumericValue(numGrade, gradeValue, "Grade");
                    }
                    else
                    {
                        // 数値に変換できない場合は1をデフォルトにする
                        numGrade.Value = 1;
                    }
                }

                this.Text = "製作者情報編集";
            }
            else
            {
                // 新規追加モード：デフォルト値は1期生
                numGrade.Value = 1;
                this.Text = "製作者情報追加";
            }
        }

        /// <summary>
        /// OKボタンクリック
        /// </summary>
        private void btnOK_Click(object sender, EventArgs e)
        {
            // バリデーション
            if (!ValidateInput())
            {
                return;
            }

            try
            {
                // 期生を取得（不明=空 / 教員="0" / それ以外=numGrade の N期生）
                string grade = chkGradeUnknown.Checked ? "" : (chkGradeTeacher.Checked ? "0" : numGrade.Value.ToString());

                // DeveloperInfoオブジェクトを作成
                Developer = new DeveloperInfo
                {
                    Id = developer?.Id, // 編集時は既存のIDを保持
                    LastName = txtLastName.Text.Trim(),
                    FirstName = txtFirstName.Text.Trim(),
                    Grade = grade
                };

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"製作者情報の保存に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// (#313) 「不明」「教員」は排他チェック。どちらかが ON なら期生入力を無効化
        /// （不明=空 / 教員="0" で保存）。
        /// </summary>
        private void chkGradeUnknown_CheckedChanged(object sender, EventArgs e)
        {
            if (chkGradeUnknown.Checked) chkGradeTeacher.Checked = false;
            UpdateGradeInputEnabled();
        }

        private void chkGradeTeacher_CheckedChanged(object sender, EventArgs e)
        {
            if (chkGradeTeacher.Checked) chkGradeUnknown.Checked = false;
            UpdateGradeInputEnabled();
        }

        private void UpdateGradeInputEnabled()
        {
            numGrade.Enabled = !(chkGradeUnknown.Checked || chkGradeTeacher.Checked);
        }

        /// <summary>
        /// 入力値のバリデーション
        /// </summary>
        private bool ValidateInput()
        {
            // 姓・名のどちらか一方でも入っていれば可 (姓だけ / 名だけ / 両方 いずれも許可)。
            // 旧実装は「名」必須だったが、姓のみ判明しているケース等に対応するため緩和。
            if (string.IsNullOrWhiteSpace(txtLastName.Text) && string.IsNullOrWhiteSpace(txtFirstName.Text))
            {
                MessageBox.Show("姓または名のいずれかを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtFirstName.Focus();
                return false;
            }

            return true;
        }
    }
}

