using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
{
    /// <summary>
    /// ログビューア (#129)。
    /// `<project_root>/logs/manager/` と `<project_root>/logs/launcher/` をスキャンし、
    /// セッション単位のログファイルを一覧表示 + 内容を行レベル別に色分けして表示。
    /// レベルフィルタ・全文検索を組み合わせ、現在のフィルタで「内容なし」になるファイルは
    /// 一覧で灰色化して「開いても何も出ない」と一目で分かるようにする。
    /// </summary>
    public partial class LogSectionPanel : UserControl
    {
        // ファイル名の規約: `{component}_{PCname}_{YYYY-MM-DD}_{HHmmss}.log`
        // 同秒衝突時は `_2`, `_3` が末尾に付くため正規表現は末尾オプショナルで吸収する。
        // monitor は将来 (= Monitor component 実装時) の readiness、現状 file 不在でも tab 切替時に grid 空表示が出るだけで害なし。
        private static readonly Regex FileNameRegex = new Regex(
            @"^(?<component>manager|launcher|monitor)_(?<pc>.+)_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{6})(?:_(?<seq>\d+))?\.log$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 行頭フォーマット: `[YYYY-MM-DD HH:mm:ss] [LEVEL] ...`
        private static readonly Regex LineRegex = new Regex(
            @"^\[(?<ts>[^\]]+)\] \[(?<level>INFO|WARN|ERROR)\] (?<rest>.*)$",
            RegexOptions.Compiled);

        private string _logsRoot;
        private List<LogFileEntry> _allEntries = new List<LogFileEntry>();
        private LogFileEntry _currentEntry; // 現在描画中のエントリ (フィルタ変更時の再描画用)
        // tab で選択中の component (`launcher` / `manager` / `monitor`)。TabPage.Name と一致させる。
        // 値は constructor で Designer の初期選択 tab から derive する (= Designer の TabPage 順序入替で
        // hidden drift が起きないよう、`SelectedIndex=0` の hardcode と field default の二重管理を避ける)。
        // 防御 fallback 値の `"launcher"` は Designer が壊れた場合の last resort、通常 path は通らない。
        private string _currentComponent = "launcher";

        public LogSectionPanel()
        {
            InitializeComponent();
            ConfigureGrid();
            // Designer 上の初期選択 tab を SoT として derive。tabComponent が null / SelectedTab 不在の
            // 異常 path は field default ("launcher") にフォールバック。
            if (tabComponent != null && tabComponent.SelectedTab != null)
            {
                _currentComponent = tabComponent.SelectedTab.Name;
            }
        }

        public void Initialize(string projectRoot)
        {
            _logsRoot = Path.Combine(projectRoot ?? "", "logs");
            RefreshDisplay();
        }

        private void ConfigureGrid()
        {
            gridFiles.AutoGenerateColumns = false;
            gridFiles.Columns.Clear();
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "component", HeaderText = "アプリ", Width = 80 });
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "pc", HeaderText = "PC", Width = 150 });
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "started", HeaderText = "開始日時", Width = 170 });
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "lastWrite", HeaderText = "最終更新", Width = 150 });
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "サイズ", Width = 90 });
            // ファイルパス列で残り幅を埋める (Fill モード)
            gridFiles.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "path",
                HeaderText = "ファイルパス",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
        }

        public void RefreshDisplay()
        {
            if (string.IsNullOrEmpty(_logsRoot)) return;

            // 更新前に選択されていた entry の FilePath を保持し、再表示後に同 file を再選択する。
            // (1) refresh で grid 全行を Clear → Add し直すと DataGridViewRow オブジェクトが入れ替わり、
            //     selection は失われる。デフォルト挙動で先頭が auto-selected になることはあるが、
            //     SelectionChanged event は「変化があった時だけ」発火する仕様で、Clear 直後の自動先頭
            //     選択時は発火しない path がある (= 内容描画されず空のまま、本 bug の原因)。
            // (2) 元々選択していた log がユーザーの意図と違う行に化けるとさらに混乱するため、
            //     FilePath で identify して同じ entry を再選択する。
            string prevSelectedPath = _currentEntry == null ? null : _currentEntry.FilePath;

            try
            {
                _allEntries = ScanLogFiles(_logsRoot, _currentComponent)
                    .OrderByDescending(e => e.StartedAt)
                    .ToList();

                gridFiles.Rows.Clear();
                foreach (var e in _allEntries)
                {
                    int row = gridFiles.Rows.Add(
                        e.Component,
                        e.PcName,
                        e.StartedAt.ToString("yyyy/MM/dd HH:mm:ss"),
                        e.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"),
                        FormatBytes(e.SizeBytes),
                        e.FilePath);
                    gridFiles.Rows[row].Tag = e;
                }

                // フィルタを適用して灰色化
                UpdateRowGreyout();
                UpdateFileCountLabel();

                // 元の選択 entry を FilePath で再 identify、見つからなければ先頭に fallback。
                int targetRow = -1;
                if (!string.IsNullOrEmpty(prevSelectedPath))
                {
                    for (int i = 0; i < gridFiles.Rows.Count; i++)
                    {
                        var e = gridFiles.Rows[i].Tag as LogFileEntry;
                        if (e != null && string.Equals(e.FilePath, prevSelectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            targetRow = i;
                            break;
                        }
                    }
                }
                if (targetRow == -1 && gridFiles.Rows.Count > 0) targetRow = 0;

                // SelectionChanged event は selection が変化しないと発火しない (= Rows.Clear 後の自動
                // 先頭選択 case で event 不発 → 内容空 path) ため、選択 + 直接 RenderContent 両方を行う。
                _currentEntry = null;
                txtContent.Clear();
                if (targetRow >= 0)
                {
                    gridFiles.ClearSelection();
                    gridFiles.Rows[targetRow].Selected = true;
                    _currentEntry = gridFiles.Rows[targetRow].Tag as LogFileEntry;
                    RenderContent();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログファイルの読み込みに失敗しました: {ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 指定 component の subdir のみ scan する (= tab で component が決まっているため、複数 subdir 横断は不要)。
        private static IEnumerable<LogFileEntry> ScanLogFiles(string logsRoot, string component)
        {
            string dir = Path.Combine(logsRoot, component);
            if (!Directory.Exists(dir)) yield break;
            foreach (string path in Directory.EnumerateFiles(dir, "*.log"))
            {
                var entry = TryParseFileName(path);
                if (entry != null)
                {
                    LoadAndParseContent(entry);
                    yield return entry;
                }
            }
        }

        private static LogFileEntry TryParseFileName(string path)
        {
            string name = Path.GetFileName(path);
            var m = FileNameRegex.Match(name);
            if (!m.Success) return null;

            string dateStr = m.Groups["date"].Value;
            string timeStr = m.Groups["time"].Value;
            if (!DateTime.TryParseExact(
                    $"{dateStr}_{timeStr}",
                    "yyyy-MM-dd_HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out DateTime started))
            {
                return null;
            }

            var fi = new FileInfo(path);
            string componentLower = m.Groups["component"].Value.ToLowerInvariant();
            string componentDisplay;
            switch (componentLower)
            {
                case "manager": componentDisplay = "Manager"; break;
                case "monitor": componentDisplay = "Monitor"; break;
                default: componentDisplay = "Launcher"; break;
            }
            return new LogFileEntry
            {
                FilePath = path,
                Component = componentDisplay,
                PcName = m.Groups["pc"].Value,
                StartedAt = started,
                LastWriteTime = fi.LastWriteTime,
                SizeBytes = fi.Length
            };
        }

        /// <summary>
        /// ファイルを読んで生テキスト + 行レベル付きパース結果をエントリに格納する。
        /// Logger が書き込み中のファイルもロックしないよう FileShare.ReadWrite で開く。
        /// </summary>
        private static void LoadAndParseContent(LogFileEntry entry)
        {
            try
            {
                using (var fs = new FileStream(entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    entry.RawContent = sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                entry.RawContent = $"[読み込み失敗] {ex.Message}";
            }

            entry.Lines = new List<ParsedLine>();
            string lastLevel = "INFO";
            foreach (string line in entry.RawContent.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                var m = LineRegex.Match(l);
                string level = m.Success ? m.Groups["level"].Value : lastLevel;
                entry.Lines.Add(new ParsedLine { Level = level, Text = l });
                lastLevel = level;
            }
        }

        private void gridFiles_SelectionChanged(object sender, EventArgs e)
        {
            if (gridFiles.SelectedRows.Count == 0) return;
            var entry = gridFiles.SelectedRows[0].Tag as LogFileEntry;
            if (entry == null) return;
            _currentEntry = entry;
            RenderContent();
        }

        private void RenderContent()
        {
            if (_currentEntry == null)
            {
                txtContent.Clear();
                return;
            }

            txtContent.SuspendLayout();
            try
            {
                txtContent.Clear();
                bool[] levelOn = { chkInfo.Checked, chkWarn.Checked, chkError.Checked };
                string search = txtSearch.Text ?? "";

                Color dimText = Color.FromArgb(180, 180, 180);
                Color normalText = SystemColors.ControlText;
                Color searchHighlight = Color.FromArgb(255, 240, 130); // 黄

                // 全行表示。マッチしない行は薄い灰色でディム、マッチ行はレベル別の背景色 + 黒文字。
                // 検索ヒットがあれば該当 substring に追加で黄色ハイライト。
                foreach (var pl in _currentEntry.Lines)
                {
                    bool matchesLevel = IsLevelOn(pl.Level, levelOn);
                    bool matchesSearch = ContainsSearch(pl.Text, search);
                    bool isMatch = matchesLevel && matchesSearch;

                    int start = txtContent.TextLength;
                    txtContent.AppendText(pl.Text + Environment.NewLine);
                    int end = txtContent.TextLength;

                    txtContent.Select(start, end - start);
                    txtContent.SelectionBackColor = isMatch ? GetLevelColor(pl.Level) : Color.White;
                    txtContent.SelectionColor = isMatch ? normalText : dimText;

                    // マッチ行内の検索 substring を追加でハイライト (大文字小文字無視)
                    if (isMatch && !string.IsNullOrEmpty(search))
                    {
                        int idx = 0;
                        while (idx <= pl.Text.Length - search.Length)
                        {
                            int found = pl.Text.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase);
                            if (found < 0) break;
                            txtContent.Select(start + found, search.Length);
                            txtContent.SelectionBackColor = searchHighlight;
                            idx = found + search.Length;
                        }
                    }
                }
                txtContent.Select(0, 0);
                txtContent.ScrollToCaret();
            }
            finally
            {
                txtContent.ResumeLayout();
            }
        }

        /// <summary>
        /// 全ファイル行を走査し、現在のフィルタで「表示するものがゼロ」なファイルを灰色化する。
        /// コンポーネントは tab で既に絞られているため、ここではレベル + 検索フィルタのみを行レベルで適用する。
        /// </summary>
        private void UpdateRowGreyout()
        {
            bool[] levelOn = { chkInfo.Checked, chkWarn.Checked, chkError.Checked };
            string search = txtSearch.Text ?? "";

            foreach (DataGridViewRow row in gridFiles.Rows)
            {
                var entry = row.Tag as LogFileEntry;
                if (entry == null) continue;
                bool hasMatch = HasAnyMatchingLine(entry, levelOn, search);
                ApplyRowAppearance(row, hasMatch);
            }
        }

        private void UpdateFileCountLabel()
        {
            bool[] levelOn = { chkInfo.Checked, chkWarn.Checked, chkError.Checked };
            string search = txtSearch.Text ?? "";
            int visible = _allEntries.Count(e => HasAnyMatchingLine(e, levelOn, search));
            lblFileCount.Text = $"ログファイル: {visible} 件";
        }

        private static bool HasAnyMatchingLine(LogFileEntry entry, bool[] levelOn, string search)
        {
            if (entry.Lines == null) return true; // 安全側: 不明なら表示扱い
            foreach (var pl in entry.Lines)
            {
                if (IsLevelOn(pl.Level, levelOn) && ContainsSearch(pl.Text, search))
                    return true;
            }
            return false;
        }

        private static bool IsLevelOn(string level, bool[] levelOn)
        {
            switch (level)
            {
                case "INFO": return levelOn[0];
                case "WARN": return levelOn[1];
                case "ERROR": return levelOn[2];
                default: return levelOn[0]; // 未分類は INFO 扱い
            }
        }

        private static bool ContainsSearch(string text, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            return text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ApplyRowAppearance(DataGridViewRow row, bool hasMatch)
        {
            if (hasMatch)
            {
                row.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                row.DefaultCellStyle.BackColor = SystemColors.Window;
            }
            else
            {
                // 「開いても何も出ない」状態を視覚化: 灰色の文字 + 薄い灰色背景
                row.DefaultCellStyle.ForeColor = Color.Gray;
                row.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            }
        }

        private static Color GetLevelColor(string level)
        {
            switch (level)
            {
                case "WARN": return Color.FromArgb(255, 250, 220);
                case "ERROR": return Color.FromArgb(255, 220, 220);
                default: return Color.White;
            }
        }

        private void chkLevelFilter_Changed(object sender, EventArgs e)
        {
            UpdateRowGreyout();
            UpdateFileCountLabel();
            RenderContent();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            UpdateRowGreyout();
            UpdateFileCountLabel();
            RenderContent();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshDisplay();
        }

        // tab 切替時に scan 対象 subdir が変わるため、grid と content view を full refresh する。
        // TabPage.Name は Designer 側で `launcher` / `manager` / `monitor` (= subdir 名と一致) を割当ててある。
        private void tabComponent_SelectedIndexChanged(object sender, EventArgs e)
        {
            var page = tabComponent.SelectedTab;
            if (page == null) return;
            _currentComponent = page.Name;
            RefreshDisplay();
        }

        /// <summary>
        /// `<install>/logs/` をエクスプローラで開く。部員がエラー発生時にログを zip して送る際の動線。
        /// logs/ dir が存在しない場合は親 dir (`<install>/`) を fallback として開く。失敗時は MessageBox。
        /// </summary>
        private void btnOpenLogFolder_Click(object sender, EventArgs e)
        {
            string target = _logsRoot;
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                // logs/ がまだ生成されていないケース、親 dir (= <install>/) を fallback で開く
                try
                {
                    target = Path.GetDirectoryName(_logsRoot);
                }
                catch
                {
                    target = null;
                }
                if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                {
                    MessageBox.Show("ログフォルダが見つかりません: " + (_logsRoot ?? "(未初期化)"),
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("エクスプローラを開けませんでした: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            return $"{mb:F2} MB";
        }

        private class LogFileEntry
        {
            public string FilePath;
            // TryParseFileName が返す値: "Manager" / "Launcher" / "Monitor"。
            // 現状の tab UI は Launcher / Manager の 2 tab のみで Monitor file は scan 対象に入らないが、
            // FileNameRegex 側は monitor を forward-compat で許容しているため値域は 3 値の可能性を持つ。
            public string Component;
            public string PcName;
            public DateTime StartedAt;
            public DateTime LastWriteTime;
            public long SizeBytes;
            public string RawContent;    // ファイル全文 (検索高速化のため事前ロード)
            public List<ParsedLine> Lines;
        }

        private class ParsedLine
        {
            public string Level; // INFO / WARN / ERROR (継続行は直前の Level を継承)
            public string Text;
        }
    }
}
