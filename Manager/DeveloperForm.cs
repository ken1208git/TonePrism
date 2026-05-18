using System;
using System.Windows.Forms;
using TonePrism.Manager.Models;

namespace TonePrism.Manager
{
    /// <summary>
    /// 製作者情報入力フォーム
    /// </summary>
    public partial class DeveloperForm : Form
    {
        private DeveloperInfo developer;

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
                
                // 期生の設定（0以上の数値として扱う）
                if (!string.IsNullOrEmpty(developer.Grade))
                {
                    int gradeValue;
                    if (int.TryParse(developer.Grade, out gradeValue) && gradeValue >= 0)
                    {
                        numGrade.Value = gradeValue;
                    }
                    else
                    {
                        // 数値に変換できない場合は1をデフォルトにする
                        numGrade.Value = 1;
                    }
                }
                else
                {
                    // 未入力の場合は1をデフォルトにする
                    numGrade.Value = 1;
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
                // 期生を取得（0以上、0の場合は「教員」として扱う）
                string grade = numGrade.Value.ToString();

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
        /// 入力値のバリデーション
        /// </summary>
        private bool ValidateInput()
        {
            // 姓は空欄でも可（姓が不明な場合に対応）
            // 名
            if (string.IsNullOrWhiteSpace(txtFirstName.Text))
            {
                MessageBox.Show("名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtFirstName.Focus();
                return false;
            }

            return true;
        }
    }
}

