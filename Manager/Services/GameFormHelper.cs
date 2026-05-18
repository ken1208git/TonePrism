using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// ゲーム追加・編集・バージョンアップフォーム共通ヘルパー
    /// </summary>
    public static class GameFormHelper
    {
        public const string ARGUMENTS_PLACEHOLDER = "通常は空欄で構いません。\r\n特殊な起動オプションが必要な場合のみ記述してください。\r\n例: Unity製ゲームでフルスクリーン起動を強制する場合 -> -screen-fullscreen 1";

        /// <summary>
        /// 実行ファイル自動検出時の除外パターン
        /// </summary>
        private static readonly string[] ExcludedExePatterns = new[]
        {
            ".console.exe",
            "UnityCrashHandler64.exe",
            "UnityCrashHandler32.exe",
            "UnityCrashHandler.exe",
            "CrashHandler.exe",
            "CrashHandler64.exe",
            "CrashHandler32.exe"
        };

        #region ComboBox初期化

        /// <summary>
        /// 難易度コンボボックスを初期化
        /// </summary>
        public static void InitializeDifficultyCombo(ComboBox cmb, int? selectedValue = null)
        {
            cmb.Items.Add("1 - 易しい");
            cmb.Items.Add("2 - 普通");
            cmb.Items.Add("3 - 難しい");

            if (selectedValue.HasValue && selectedValue.Value >= 1 && selectedValue.Value <= 3)
                cmb.SelectedIndex = selectedValue.Value - 1;
            else
                cmb.SelectedIndex = 1; // デフォルト: 普通
        }

        /// <summary>
        /// プレイ時間コンボボックスを初期化
        /// </summary>
        public static void InitializePlayTimeCombo(ComboBox cmb, int? selectedValue = null)
        {
            cmb.Items.Add("1 - ～5分");
            cmb.Items.Add("2 - 5分～15分");
            cmb.Items.Add("3 - 15分以上");

            if (selectedValue.HasValue && selectedValue.Value >= 1 && selectedValue.Value <= 3)
                cmb.SelectedIndex = selectedValue.Value - 1;
            else
                cmb.SelectedIndex = 1; // デフォルト: 5分～15分
        }

        /// <summary>
        /// 通信プレイ対応コンボボックスを初期化
        /// </summary>
        public static void InitializeConnectionCombo(ComboBox cmb, int selectedValue = 0)
        {
            cmb.Items.Add("なし（1台で遊ぶ）");
            cmb.Items.Add("ローカル通信（部室のLANで対戦）");
            cmb.Items.Add("オンライン通信（インターネット対戦）");

            if (selectedValue >= 0 && selectedValue <= 2)
                cmb.SelectedIndex = selectedValue;
            else
                cmb.SelectedIndex = 0; // デフォルト: なし
        }

        #endregion

        #region ジャンル操作

        /// <summary>
        /// CheckedListBoxから選択されたジャンルを取得
        /// </summary>
        public static List<string> GetSelectedGenres(CheckedListBox clb)
        {
            var genres = new List<string>();
            for (int i = 0; i < clb.CheckedItems.Count; i++)
            {
                string genre = clb.CheckedItems[i].ToString();
                if (!string.IsNullOrEmpty(genre))
                {
                    genres.Add(genre);
                }
            }
            return genres;
        }

        /// <summary>
        /// CheckedListBoxにジャンルを設定
        /// </summary>
        public static void SetSelectedGenres(CheckedListBox clb, List<string> genres)
        {
            // まず全てのチェックを外す
            for (int i = 0; i < clb.Items.Count; i++)
            {
                clb.SetItemChecked(i, false);
            }

            if (genres == null) return;

            foreach (string genre in genres)
            {
                if (GenreList.IsValidGenre(genre))
                {
                    int index = clb.Items.IndexOf(genre);
                    if (index >= 0)
                    {
                        clb.SetItemChecked(index, true);
                    }
                }
            }
        }

        #endregion

        #region プレースホルダー

        /// <summary>
        /// 起動オプションのプレースホルダーラベルを作成・設定
        /// </summary>
        public static Label SetupArgumentsPlaceholder(TextBox txtArguments, Control parent)
        {
            var lbl = new Label();
            lbl.Text = ARGUMENTS_PLACEHOLDER;
            lbl.ForeColor = Color.Gray;
            lbl.BackColor = Color.White;
            lbl.AutoSize = false;
            lbl.Size = new Size(txtArguments.Width - 4, txtArguments.Height - 4);
            lbl.Location = new Point(txtArguments.Location.X + 2, txtArguments.Location.Y + 2);
            lbl.Font = txtArguments.Font;
            lbl.Cursor = Cursors.IBeam;
            lbl.Click += (s, ev) => txtArguments.Focus();
            parent.Controls.Add(lbl);
            lbl.BringToFront();

            txtArguments.TextChanged += (s, ev) => lbl.Visible = string.IsNullOrEmpty(txtArguments.Text);
            lbl.Visible = string.IsNullOrEmpty(txtArguments.Text);

            return lbl;
        }

        #endregion

        #region テスト起動

        /// <summary>
        /// ゲームをテスト起動
        /// </summary>
        /// <param name="exePath">実行ファイルパス</param>
        /// <param name="arguments">起動引数</param>
        /// <param name="baseFolder">相対パスの基準フォルダ（省略時はパスをそのまま使用）</param>
        public static void TestRunGame(string exePath, string arguments, string baseFolder = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    MessageBox.Show("実行ファイルが指定されていません。", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 相対パスの場合は基準フォルダと結合
                if (!Path.IsPathRooted(exePath) && !string.IsNullOrEmpty(baseFolder))
                {
                    exePath = Path.Combine(baseFolder, exePath);
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show($"実行ファイルが見つかりません:\n{exePath}", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments ?? "",
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"実行ファイルの起動に失敗しました:\n{ex.Message}", "テスト起動", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region 自動検出

        /// <summary>
        /// ゲームフォルダ内のファイルを自動検出
        /// </summary>
        /// <param name="folderPath">検索対象フォルダ</param>
        /// <returns>(実行ファイルパス, サムネイルパス, 背景パス) — 見つからない場合はnull</returns>
        public static (string ExecutablePath, string ThumbnailPath, string BackgroundPath) AutoDetectFiles(string folderPath)
        {
            string exePath = null;
            string thumbPath = null;
            string bgPath = null;

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return (exePath, thumbPath, bgPath);

            // 実行ファイルを自動検出
            var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length > 0)
            {
                var preferredExeFiles = exeFiles
                    .Where(file =>
                    {
                        string fileName = Path.GetFileName(file);
                        return !ExcludedExePatterns.Any(pattern =>
                            fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();

                exePath = preferredExeFiles.Count > 0 ? preferredExeFiles[0] : exeFiles[0];
            }

            // サムネイル画像を自動検出
            var thumbnailPatterns = new[] { "thumbnail.png", "thumb.png", "thumb.jpg", "icon.png", "icon.jpg" };
            foreach (var pattern in thumbnailPatterns)
            {
                var files = Directory.GetFiles(folderPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    thumbPath = files[0];
                    break;
                }
            }

            // 背景画像を自動検出
            var backgroundPatterns = new[] { "background.png", "background.jpg", "bg.png", "bg.jpg", "preview.png", "preview.jpg" };
            foreach (var pattern in backgroundPatterns)
            {
                var files = Directory.GetFiles(folderPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    bgPath = files[0];
                    break;
                }
            }

            return (exePath, thumbPath, bgPath);
        }

        #endregion

        /// <summary>
        /// ゲームIDが有効な文字列かチェック（半角英数字、ハイフン、アンダースコアのみ、最大64文字）
        /// </summary>
        public static bool IsValidGameId(string gameId)
        {
            return IsValidGameId(gameId, out _);
        }

        /// <summary>
        /// ゲームIDが有効な文字列かチェックし、NGの場合は理由を返す
        /// </summary>
        public static bool IsValidGameId(string gameId, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(gameId))
            {
                errorMessage = "ゲームIDを入力してください。";
                return false;
            }

            string trimmed = gameId.Trim();

            if (trimmed.Length > 64)
            {
                errorMessage = "ゲームIDは64文字以内で入力してください。";
                return false;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_-]+$"))
            {
                errorMessage = "ゲームIDは英数字、アンダースコア（_）、ハイフン（-）のみ使用できます。";
                return false;
            }

            // Windowsの予約デバイス名チェック
            string upper = trimmed.ToUpperInvariant();
            var reserved = new HashSet<string>
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            if (reserved.Contains(upper))
            {
                errorMessage = $"「{trimmed}」はWindowsの予約名のため使用できません。";
                return false;
            }

            return true;
        }
    }
}
