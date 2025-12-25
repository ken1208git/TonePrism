using System;
using System.Drawing;
using System.Windows.Forms;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// データベースリセット確認フォーム
    /// 安全機能付き（ランダム文字列入力、ボタンが逃げる）
    /// </summary>
    public partial class ResetDatabaseConfirmForm : Form
    {
        private string confirmationCode;
        private Random random;
        private Timer moveTimer;
        private bool isMoving;

        public ResetDatabaseConfirmForm()
        {
            InitializeComponent();
            random = new Random();
            isMoving = false;
        }

        private void ResetDatabaseConfirmForm_Load(object sender, EventArgs e)
        {
            // ランダムな確認コードを生成（4桁の英数字）
            confirmationCode = GenerateConfirmationCode();
            lblConfirmationCode.Text = $"確認コード: {confirmationCode}";
            
            // 初期位置を設定
            btnConfirm.Location = new Point(300, 185);
            
            // フォーム全体でマウスを監視
            this.MouseMove += ResetDatabaseConfirmForm_MouseMove;
        }

        /// <summary>
        /// フォーム全体のマウス移動イベント（ボタンが逃げる）
        /// </summary>
        private void ResetDatabaseConfirmForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMoving) return;
            
            // ボタンの中心位置を取得
            Point buttonCenter = new Point(
                btnConfirm.Location.X + btnConfirm.Width / 2,
                btnConfirm.Location.Y + btnConfirm.Height / 2);
            
            // マウス位置を取得
            Point mousePos = e.Location;
            
            // 距離を計算
            double distance = Math.Sqrt(
                Math.Pow(buttonCenter.X - mousePos.X, 2) + 
                Math.Pow(buttonCenter.Y - mousePos.Y, 2));
            
            // 一定距離以内（80ピクセル）に近づいたら逃げる
            if (distance < 80)
            {
                isMoving = true;
                MoveButtonAway();
            }
        }

        /// <summary>
        /// 確認コードを生成（4桁の英数字）
        /// </summary>
        private string GenerateConfirmationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 0, O, I, 1を除外
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
            {
                code[i] = chars[random.Next(chars.Length)];
            }
            return new string(code);
        }

        /// <summary>
        /// 確認ボタンマウス移動イベント（ボタンが逃げる）
        /// </summary>
        private void btnConfirm_MouseEnter(object sender, EventArgs e)
        {
            if (isMoving) return;
            
            isMoving = true;
            MoveButtonAway();
        }

        /// <summary>
        /// ボタンを逃がす（より積極的に）
        /// </summary>
        private void MoveButtonAway()
        {
            // ランダムな位置に移動（フォーム内に収まるように）
            int minX = 20;
            int maxX = Math.Max(minX + 1, this.ClientSize.Width - btnConfirm.Width - 20);
            int minY = 185;
            int maxY = Math.Max(minY + 1, this.ClientSize.Height - btnConfirm.Height - 20);
            
            // 有効な範囲を確保できている場合のみ移動
            if (maxX > minX && maxY > minY)
            {
                int newX = random.Next(minX, maxX);
                int newY = random.Next(minY, maxY);
                
                // 現在の位置から十分に離れた位置を選ぶ（最低80ピクセル）
                int attempts = 0;
                while (attempts < 20)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(newX - btnConfirm.Location.X, 2) + 
                        Math.Pow(newY - btnConfirm.Location.Y, 2));
                    
                    if (distance >= 80) break;
                    
                    newX = random.Next(minX, maxX);
                    newY = random.Next(minY, maxY);
                    attempts++;
                }
                
                btnConfirm.Location = new Point(newX, newY);
            }
            
            // 少し遅延してからisMovingをfalseに
            if (moveTimer == null)
            {
                moveTimer = new Timer();
                moveTimer.Interval = 100;
                moveTimer.Tick += (s, args) =>
                {
                    isMoving = false;
                    moveTimer.Stop();
                };
            }
            moveTimer.Start();
        }

        /// <summary>
        /// 確認ボタンクリック
        /// </summary>
        private void btnConfirm_Click(object sender, EventArgs e)
        {
            // 確認コードが正しいかチェック
            if (txtConfirmationCode.Text.Trim().ToUpper() != confirmationCode.ToUpper())
            {
                MessageBox.Show(
                    "確認コードが正しくありません。",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                
                // 新しい確認コードを生成
                confirmationCode = GenerateConfirmationCode();
                lblConfirmationCode.Text = $"確認コード: {confirmationCode}";
                txtConfirmationCode.Clear();
                
                return;
            }

            // 確認コードが正しい場合、ダイアログを閉じる
            DialogResult = DialogResult.Yes;
            Close();
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}

