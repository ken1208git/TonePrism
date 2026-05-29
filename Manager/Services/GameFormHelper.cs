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
        /// (round 5 L4) 旧実装は `Directory.GetFiles(... SearchOption.AllDirectories)` が junction / symbolic link
        /// 先のファイルを拾った場合、戻り値 path が `Path.GetFullPath` 後に sourceFolder 外を指して
        /// 「絶対 path として `RelativeExecutablePath` に流入 → 後段 assert で永久 block」する経路があった。
        /// 各 path について sourceFolder 内 (IsPathInside) チェックを通し、外れていたら検出失敗扱いに倒す。
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
            var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                .Where(f => IsPathInsideSourceFolder(folderPath, f))
                .ToList();
            if (exeFiles.Count > 0)
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
                var files = Directory.GetFiles(folderPath, pattern, SearchOption.AllDirectories)
                    .Where(f => IsPathInsideSourceFolder(folderPath, f))
                    .ToList();
                if (files.Count > 0)
                {
                    thumbPath = files[0];
                    break;
                }
            }

            // 背景画像を自動検出
            var backgroundPatterns = new[] { "background.png", "background.jpg", "bg.png", "bg.jpg", "preview.png", "preview.jpg" };
            foreach (var pattern in backgroundPatterns)
            {
                var files = Directory.GetFiles(folderPath, pattern, SearchOption.AllDirectories)
                    .Where(f => IsPathInsideSourceFolder(folderPath, f))
                    .ToList();
                if (files.Count > 0)
                {
                    bgPath = files[0];
                    break;
                }
            }

            return (exePath, thumbPath, bgPath);
        }

        /// <summary>
        /// (round 5 L4) 自動検出された path が sourceFolder 配下に物理的に含まれるかチェック。
        /// junction / symbolic link 越しに sourceFolder 外を指す path を弾く目的。
        /// `Path.GetFullPath` で正規化 + 等値 or 区切り境界付き StartsWith で safe な前方一致判定。
        /// 例外時 (path 不正等) は false 倒し (= 自動検出から除外)。
        /// </summary>
        private static bool IsPathInsideSourceFolder(string sourceFolder, string candidatePath)
        {
            try
            {
                string sourceFull = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar);
                string candidateFull = Path.GetFullPath(candidatePath);
                if (string.Equals(candidateFull, sourceFull, StringComparison.OrdinalIgnoreCase)) return true;
                return candidateFull.StartsWith(sourceFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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

        #region (round 5 Phase D) パス textbox 入力支援

        /// <summary>
        /// (round 5 Phase D) 画像ファイルとして受け入れる拡張子一覧。
        /// </summary>
        public static readonly string[] ImageFileExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };

        /// <summary>
        /// (round 5 Phase D) 実行ファイルとして受け入れる拡張子一覧。
        /// </summary>
        public static readonly string[] ExecutableFileExtensions = new[] { ".exe" };

        /// <summary>
        /// (round 5 Phase D) ReadOnly 解除に伴う user 入力支援。`/` を `\` に正規化、cursor 位置を保持。
        /// TextChanged ハンドラから呼ばれることを想定 (= 再帰呼出しが起きるが 2 回目は `/` 無しで no-op)。
        /// </summary>
        public static void NormalizeSlashInPathTextBox(TextBox tb)
        {
            if (tb == null) return;
            string current = tb.Text;
            if (string.IsNullOrEmpty(current)) return;
            if (current.IndexOf('/') < 0) return;
            int caret = tb.SelectionStart;
            tb.Text = current.Replace('/', '\\');
            tb.SelectionStart = Math.Min(caret, tb.Text.Length);
        }

        /// <summary>
        /// (round 5 Phase D) ファイル path の validation 共通ヘルパ。
        /// 1. 空欄なら required=false で OK / required=true で「未入力」エラー
        /// 2. 拡張子 check (allowedExtensions)
        /// 3. 存在 check (相対なら baseFolder で絶対化)
        /// </summary>
        /// <param name="path">user 入力 path</param>
        /// <param name="baseFolder">相対 path 解決用の基準フォルダ (null 可)</param>
        /// <param name="allowedExtensions">受け入れる拡張子 (null = 任意)</param>
        /// <param name="required">空欄を NG とするか</param>
        /// <param name="fieldLabel">エラーメッセージに出す項目名</param>
        /// <param name="errorMessage">エラー時のメッセージ (OK なら null)</param>
        /// <returns>有効なら true</returns>
        public static bool ValidateFilePath(string path, string baseFolder, string[] allowedExtensions, bool required, string fieldLabel, out string errorMessage)
        {
            errorMessage = null;
            string trimmed = (path ?? "").Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                if (required)
                {
                    errorMessage = fieldLabel + "を入力してください。";
                    return false;
                }
                return true;
            }

            // 拡張子チェック
            if (allowedExtensions != null && allowedExtensions.Length > 0)
            {
                string ext = Path.GetExtension(trimmed);
                bool extOk = allowedExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
                if (!extOk)
                {
                    errorMessage = fieldLabel + "の拡張子は " + string.Join(" / ", allowedExtensions) + " のいずれかにしてください。\n\n  指定された拡張子: " + (string.IsNullOrEmpty(ext) ? "(なし)" : ext);
                    return false;
                }
            }

            // 存在チェック
            string resolved = trimmed;
            try
            {
                if (!Path.IsPathRooted(resolved) && !string.IsNullOrEmpty(baseFolder))
                {
                    resolved = Path.Combine(baseFolder, resolved);
                }
                resolved = Path.GetFullPath(resolved);
            }
            catch (Exception ex)
            {
                errorMessage = fieldLabel + "のパスが不正です:\n  " + trimmed + "\n\n  " + ex.Message;
                return false;
            }

            if (!File.Exists(resolved))
            {
                errorMessage = fieldLabel + "が見つかりません:\n  " + resolved + "\n\nパスを確認してください (相対パスの場合はゲームフォルダ基準で解決されます)。";
                return false;
            }

            return true;
        }

        #endregion

        /// <summary>
        /// (round 5 M6) NumericUpDown に DB 由来 int 値を安全に代入する。範囲外は clamp + Logger.Warn + false 返却。
        /// 範囲内なら true 返却 + そのまま代入。EditGameForm の round 2 M2 fix を helper 化し、DeveloperForm 等
        /// 他フォームからも利用可能にした共通実装。caller は false 戻り時に「NullOnLoad / 保存時の clamp 値書き戻し抑止」
        /// flag を立てる pattern。
        /// </summary>
        /// <param name="nud">対象 NumericUpDown</param>
        /// <param name="value">代入したい値 (DB 由来など、範囲外の可能性あり)</param>
        /// <param name="fieldName">log に出すフィールド名 (例: "MinPlayers (game)")</param>
        /// <param name="formName">log に出すフォーム名 (例: "EditGameForm")</param>
        /// <returns>範囲内で生代入できれば true、clamp 発生なら false</returns>
        public static bool SetClampedNumericValue(NumericUpDown nud, int value, string fieldName, string formName = "GameFormHelper")
        {
            decimal d = value;
            if (d < nud.Minimum)
            {
                Logger.Warn($"[{formName}] (round 5 M6) {fieldName}={value} は許容下限 {nud.Minimum} を下回るため clamp");
                nud.Value = nud.Minimum;
                return false;
            }
            if (d > nud.Maximum)
            {
                Logger.Warn($"[{formName}] (round 5 M6) {fieldName}={value} は許容上限 {nud.Maximum} を上回るため clamp");
                nud.Value = nud.Maximum;
                return false;
            }
            nud.Value = d;
            return true;
        }

        /// <summary>
        /// 最小プレイ人数 ≤ 最大プレイ人数 を検証する。Add / Edit / VersionUp の 3 フォーム共通。
        /// 旧実装はどのフォームも大小チェックを持たず、最小 &gt; 最大（例: 最小 4・最大 1）の
        /// ナンセンスな値をそのまま保存できていた（起動は壊れないがランチャー表示が破綻する
        /// データ品質欠陥）。3 フォームで drift しないよう helper に集約する。
        /// </summary>
        public static bool ValidatePlayerCount(int minPlayers, int maxPlayers, out string errorMessage)
        {
            errorMessage = null;
            if (minPlayers > maxPlayers)
            {
                errorMessage =
                    "最小プレイ人数が最大プレイ人数を上回っています。\n\n" +
                    "  最小プレイ人数: " + minPlayers + "\n" +
                    "  最大プレイ人数: " + maxPlayers + "\n\n" +
                    "最小 ≤ 最大 になるよう修正してください。";
                return false;
            }
            return true;
        }

        /// <summary>
        /// コピー元フォルダが games/ 配下（= Manager 管理下のゲーム本体・各版フォルダ）を指していないか
        /// 検証する。ゲーム追加 / バージョンアップ共通。
        ///
        /// games/ 配下をソースに選ぶと、コピー先が games/{id}/v.../ となりソースの内側に入るため、
        /// CopyDirectoryRecursive 冒頭の再帰ガードで「1 ファイルもコピーされない空コピー」になり破綻する。
        /// 旧実装は IsVersionFolder 名前ベース除外でこの誤操作を糊塗していたが、守りたいケース（ルート選択）
        /// は守れず、正当な v* 名フォルダを無言で誤除外する下しか無い保険だった。新ビルドは必ず games/ の外
        /// から取り込む規約を境界で強制し、誤操作はここで明示的に弾く。
        /// </summary>
        public static bool ValidateSourceNotInGamesFolder(string sourceFolder, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(sourceFolder)) return true; // 空欄は別 validation が扱う

            string gamesRoot;
            string src;
            try
            {
                gamesRoot = FileOperationService.NormalizePath(TonePrism.Manager.PathManager.GamesFolder);
                src = FileOperationService.NormalizePath(sourceFolder);
            }
            catch
            {
                // 正規化不能（= パス不正）なら Directory.Exists 等の他 validation に委ねる
                return true;
            }

            bool insideGames =
                string.Equals(src, gamesRoot, StringComparison.OrdinalIgnoreCase)
                || src.StartsWith(gamesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (insideGames)
            {
                errorMessage =
                    "コピー元として games フォルダ内のフォルダが選択されています:\n" +
                    "  " + sourceFolder + "\n\n" +
                    "games フォルダは Manager が管理するゲーム本体の置き場所です。\n" +
                    "取り込むのは、games フォルダの外にある新しいビルド（配布用フォルダ）を選んでください。";
                return false;
            }
            return true;
        }
    }
}
