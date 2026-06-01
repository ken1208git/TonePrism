using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// ストアセクション編集フォーム
    /// </summary>
    public partial class StoreSectionForm : Form
    {
        private readonly DatabaseManager _dbManager;
        private readonly StoreSectionInfo _section;
        private readonly bool _isNew;
        // ゲームID → 表示テキストの一時保持
        private readonly Dictionary<string, string> _displayTexts = new Dictionary<string, string>();
        private bool _suppressDisplayTextEvent = false;

        public StoreSectionForm(DatabaseManager dbManager, StoreSectionInfo section = null)
        {
            InitializeComponent();
            _dbManager = dbManager;

            if (section != null)
            {
                _section = section;
                _isNew = false;
            }
            else
            {
                _section = new StoreSectionInfo
                {
                    DisplayOrder = _dbManager.GetMaxSectionDisplayOrder() + 1
                };
                _isNew = true;
            }
        }

        private void StoreSectionForm_Load(object sender, EventArgs e)
        {
            // セクションタイプ
            cmbSectionType.Items.AddRange(new object[]
            {
                "通常カテゴリ行",
                "スライドショー",
                "タイルグリッド"
            });

            // セクションソース
            cmbSectionSource.Items.AddRange(new object[]
            {
                "手動",
                "人気ランキング",
                "新作",
                "最近プレイ",
                "ジャンル指定",
                "プレイ人数(以上)",
                "プレイ人数(以下)",
                "難易度",
                "プレイ時間",
                "通信プレイ",
                "ランダム",
                "コントローラー",
                "制作年指定"
            });

            // ジャンルComboBox
            foreach (var genre in GenreList.AvailableGenres)
            {
                cmbGenre.Items.Add(genre);
            }

            // 値を設定
            txtTitle.Text = _section.Title ?? "";
            cmbSectionType.SelectedIndex = _section.SectionType;
            nudMaxDisplayCount.Value = _section.MaxDisplayCount;
            chkIsVisible.Checked = _section.IsVisible;

            // ソースの設定
            SetSourceControls(_section.SectionSource ?? "manual");

            // ゲーム一覧を読み込み
            LoadGameLists();

            // 既存のdisplay_textを読み込み
            if (_section.GameDisplayTexts != null)
            {
                foreach (var kvp in _section.GameDisplayTexts)
                {
                    _displayTexts[kvp.Key] = kvp.Value;
                }
            }

            // ソース変更時のイベント
            cmbSectionSource.SelectedIndexChanged += CmbSectionSource_SelectedIndexChanged;
            // タイプ変更時のイベント
            cmbSectionType.SelectedIndexChanged += CmbSectionType_SelectedIndexChanged;
            // 割当済みリスト選択変更時
            lstAssigned.SelectedIndexChanged += LstAssigned_SelectedIndexChanged;

            UpdateSourceParameterVisibility();
            UpdateGameListVisibility();
            // (#212) タイプ→ソース制限を適用 (legacy のスライドショー/タイルグリッド×自動は手動へ coerce + 案内)。
            ApplyTypeSourceConstraint(informOnCoerce: true);
            // (#211) ソースに応じて最大表示数の有効/無効を初期反映 (coerce が走らなかった場合も明示的に)。
            UpdateMaxDisplayCountEnabled();
        }

        private void SetSourceControls(string source)
        {
            if (source == "manual") cmbSectionSource.SelectedIndex = 0;
            else if (source == "popular") cmbSectionSource.SelectedIndex = 1;
            else if (source == "recent") cmbSectionSource.SelectedIndex = 2;
            else if (source == "recently_played") cmbSectionSource.SelectedIndex = 3;
            else if (source.StartsWith("genre:"))
            {
                cmbSectionSource.SelectedIndex = 4;
                string genreName = source.Substring(6);
                int idx = cmbGenre.Items.IndexOf(genreName);
                if (idx >= 0) cmbGenre.SelectedIndex = idx;
            }
            else if (source.StartsWith("players_min:"))
            {
                cmbSectionSource.SelectedIndex = 5;
                if (int.TryParse(source.Substring(12), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source.StartsWith("players_max:"))
            {
                cmbSectionSource.SelectedIndex = 6;
                if (int.TryParse(source.Substring(12), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source.StartsWith("difficulty:"))
            {
                cmbSectionSource.SelectedIndex = 7;
                // (#290 review) 数字直打ちでなくゲーム編集と同じラベル付きドロップダウンで指定する。
                int? dv = int.TryParse(source.Substring(11), out int dval) ? dval : (int?)null;
                cmbSourceValue.Items.Clear();
                GameFormHelper.InitializeDifficultyCombo(cmbSourceValue, dv);
            }
            else if (source.StartsWith("play_time:"))
            {
                cmbSectionSource.SelectedIndex = 8;
                int? pv = int.TryParse(source.Substring(10), out int pval) ? pval : (int?)null;
                cmbSourceValue.Items.Clear();
                GameFormHelper.InitializePlayTimeCombo(cmbSourceValue, pv);
            }
            else if (source == "online") cmbSectionSource.SelectedIndex = 9;
            else if (source == "random") cmbSectionSource.SelectedIndex = 10;
            else if (source == "controller") cmbSectionSource.SelectedIndex = 11;
            else if (source.StartsWith("release_year:")) // (#291) 制作年指定
            {
                cmbSectionSource.SelectedIndex = 12;
                if (int.TryParse(source.Substring(13), out int year))
                    nudSourceValue.Value = year;
            }
            else cmbSectionSource.SelectedIndex = 0;
        }

        private string GetSourceString()
        {
            int idx = cmbSectionSource.SelectedIndex;
            switch (idx)
            {
                case 0: return "manual";
                case 1: return "popular";
                case 2: return "recent";
                case 3: return "recently_played";
                case 4:
                    string genre = cmbGenre.SelectedItem?.ToString() ?? "";
                    return string.IsNullOrEmpty(genre) ? "manual" : "genre:" + genre;
                case 5: return "players_min:" + (int)nudSourceValue.Value;
                case 6: return "players_max:" + (int)nudSourceValue.Value;
                // (#290 review) 難易度/プレイ時間はラベル付き cmbSourceValue (index+1 = 値 1..3)。
                case 7: return "difficulty:" + (cmbSourceValue.SelectedIndex >= 0 ? cmbSourceValue.SelectedIndex + 1 : 2);
                case 8: return "play_time:" + (cmbSourceValue.SelectedIndex >= 0 ? cmbSourceValue.SelectedIndex + 1 : 2);
                case 9: return "online";
                case 10: return "random";
                case 11: return "controller";
                case 12: return "release_year:" + (int)nudSourceValue.Value; // (#291)
                default: return "manual";
            }
        }

        private void CmbSectionSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            // (#212 改) 厳選枠 (スライドショー/タイルグリッド) で許可外ソースを選んだら手動へ戻す。戻した時点で
            // 本ハンドラが再帰的に走り index=0 用の更新が済むので、ここでは return して二重更新を避ける。
            if (ApplyTypeSourceConstraint(informOnCoerce: true)) return;

            // (#290 review) 難易度/プレイ時間のラベル付き選択肢を入れ直す (対話的にソースを変えたとき)。
            PopulateSourceValueCombo(cmbSectionSource.SelectedIndex);
            // (#291) 制作年指定に切り替えたとき nud が年らしくない値なら今年を初期値に。
            if (cmbSectionSource.SelectedIndex == 12 && nudSourceValue.Value < 2000)
                nudSourceValue.Value = System.DateTime.Now.Year;
            UpdateSourceParameterVisibility();
            UpdateGameListVisibility();
            UpdateMaxDisplayCountEnabled();
        }

        // (#290 review) 難易度(7)/プレイ時間(8) のときだけ cmbSourceValue にゲーム編集と同じラベル付き選択肢を入れる。
        private void PopulateSourceValueCombo(int sourceIndex)
        {
            cmbSourceValue.Items.Clear();
            if (sourceIndex == 7) GameFormHelper.InitializeDifficultyCombo(cmbSourceValue);
            else if (sourceIndex == 8) GameFormHelper.InitializePlayTimeCombo(cmbSourceValue);
        }

        private void CmbSectionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            // (#212) スライドショー/タイルグリッドは手動ソースのみに制限 (自動ソースだと背景画像/16:9/表示テキストが
            // 前提を満たせず破綻しやすい)。ユーザーがタイプを変えたタイミングで coerce + 案内する。
            ApplyTypeSourceConstraint(informOnCoerce: true);

            // タイルグリッドに変更された場合、割当済みが3件超えていたら警告
            if (cmbSectionType.SelectedIndex == 2 && lstAssigned.Items.Count > 3)
            {
                MessageBox.Show(
                    "タイルグリッドは最大3件までしか表示できません。\n" +
                    "現在 " + lstAssigned.Items.Count + " 件割り当てられています。\n" +
                    "4件目以降は表示されません。複数行にしたい場合はセクションを分けてください。",
                    "注意", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // (#211) 最大表示数の上限が意味を持つランキング系ソース: 人気=1 (play_count DESC) / 最近プレイ=3 (last_played DESC)
        // / ランダム=10 (RANDOM())。TOP5 のような特集枠が成立する。
        // **新作=2 は除外** (#290 review): 実装上は `release_year=今年` のフィルタを display_order 順で並べるだけで本物の
        // ランキングではない (=「新しい順 TOP N」にならない) ため、ジャンル等と同じフィルター系として全件表示 (max グレーアウト) 扱いにする。
        private static bool IsRankingSource(int sourceIndex)
            => sourceIndex == 1 || sourceIndex == 3 || sourceIndex == 10;

        // (#212) スライドショー(1) / タイルグリッド(2) は背景画像・表示テキストで魅せる厳選枠。
        private static bool TypeIsShowcase(int typeIndex)
            => typeIndex == 1 || typeIndex == 2;

        // (#212 改) 厳選枠で許可するソース: 手動(0) と ランダム(10) のみ。背景画像/表示テキストはゲーム自身の
        // ものにフォールバックするので random でも崩れず、4 秒ごとに切り替わるヒーロー枠と相性が良い。一方
        // popular/genre 等の「絞り込み」系は厳選の意図に合わない (大判ヒーローに不向き) ため不可。通常カテゴリ行(0)
        // は全ソース可。random=10 は IsRankingSource でもあるので、厳選枠×ランダムでは最大表示数も有効になる
        // (スライドショー=スライド枚数 / タイルグリッド=最大3枚の上限)。
        private static bool TypeAllowsSource(int typeIndex, int sourceIndex)
            => !TypeIsShowcase(typeIndex) || sourceIndex == 0 || sourceIndex == 10;

        // (#211) 最大表示数 (nud + ラベル) を、ランキング系ソースのときだけ有効化する。手動/フィルター系はグレーアウト。
        private void UpdateMaxDisplayCountEnabled()
        {
            bool enabled = IsRankingSource(cmbSectionSource.SelectedIndex);
            nudMaxDisplayCount.Enabled = enabled;
            lblMaxDisplayCount.Enabled = enabled;
        }

        // (#212 改) 厳選枠 (スライドショー/タイルグリッド) で許可外ソース (手動/ランダム以外) が選ばれていたら
        // 手動へ coerce し、informOnCoerce=true なら案内ダイアログを出す。legacy の「絞り込み系×厳選枠」データも
        // 開いた時点でここで手動化される (手動/ランダムならそのまま)。coerce で SelectedIndex を変えると
        // CmbSectionSource_SelectedIndexChanged 経由で nud/可視性/最大表示数も追従する。
        // 戻り値: coerce したか (= true なら呼び出し側の Source ハンドラは二重更新を避けて return してよい)。
        // ※ combo は無効化しない (手動/ランダムの 2 択を選べるように常に有効)。
        private bool ApplyTypeSourceConstraint(bool informOnCoerce)
        {
            if (TypeIsShowcase(cmbSectionType.SelectedIndex)
                && !TypeAllowsSource(cmbSectionType.SelectedIndex, cmbSectionSource.SelectedIndex))
            {
                cmbSectionSource.SelectedIndex = 0; // 手動へ
                if (informOnCoerce)
                {
                    MessageBox.Show(
                        "スライドショー / タイルグリッドのソースは「手動」または「ランダム」のみ対応です。\n" +
                        "（背景画像・表示テキストで魅せる厳選枠のため、絞り込み系の自動ソースは不可）\n\n" +
                        "ソースを「手動」に変更しました。",
                        "ソースを変更", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return true;
            }
            return false;
        }

        private void UpdateSourceParameterVisibility()
        {
            int idx = cmbSectionSource.SelectedIndex;
            // ジャンル選択: idx=4
            lblGenre.Visible = cmbGenre.Visible = (idx == 4);
            // (#290 review / #291) ラベル付きドロップダウン: 難易度=7 / プレイ時間=8。数値入力: プレイ人数=5,6 / 制作年=12。
            bool useValueCombo = (idx == 7 || idx == 8);
            bool useValueNud = (idx == 5 || idx == 6 || idx == 12);
            cmbSourceValue.Visible = useValueCombo;
            nudSourceValue.Visible = useValueNud;
            lblSourceValue.Visible = useValueCombo || useValueNud;

            // ラベル更新
            if (idx == 5 || idx == 6) lblSourceValue.Text = "人数:";
            else if (idx == 7) lblSourceValue.Text = "難易度:";
            else if (idx == 8) lblSourceValue.Text = "プレイ時間:";
            else if (idx == 12) lblSourceValue.Text = "制作年:";
        }

        private void UpdateGameListVisibility()
        {
            bool isManual = cmbSectionSource.SelectedIndex == 0;
            grpGameAssignment.Visible = isManual;
        }

        private void LoadGameLists()
        {
            lstAvailable.Items.Clear();
            lstAssigned.Items.Clear();

            var allGames = _dbManager.GetAllGames();
            var assignedGameIds = _section.Games?.Select(g => g.GameId).ToHashSet() ?? new HashSet<string>();

            // 割当済みゲーム（順序保持）
            if (_section.Games != null)
            {
                foreach (var game in _section.Games)
                {
                    lstAssigned.Items.Add(new GameListItem(game));
                }
            }

            // 未割当ゲーム
            foreach (var game in allGames)
            {
                if (!assignedGameIds.Contains(game.GameId))
                {
                    lstAvailable.Items.Add(new GameListItem(game));
                }
            }
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // タイルグリッドの場合、3件制限チェック
            if (cmbSectionType.SelectedIndex == 2)
            {
                int selectedCount = lstAvailable.SelectedItems.Count;
                int currentCount = lstAssigned.Items.Count;
                if (currentCount + selectedCount > 3)
                {
                    MessageBox.Show(
                        "タイルグリッドは最大3件までです。\n" +
                        "4件以上表示したい場合はセクションを複数作成してください。",
                        "追加できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            MoveSelectedItems(lstAvailable, lstAssigned);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            MoveSelectedItems(lstAssigned, lstAvailable);
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            MoveItem(lstAssigned, -1);
        }

        private void btnDown_Click(object sender, EventArgs e)
        {
            MoveItem(lstAssigned, 1);
        }

        private void MoveSelectedItems(ListBox from, ListBox to)
        {
            var selected = from.SelectedItems.Cast<GameListItem>().ToList();
            foreach (var item in selected)
            {
                from.Items.Remove(item);
                to.Items.Add(item);
            }
        }

        private void MoveItem(ListBox list, int direction)
        {
            int idx = list.SelectedIndex;
            if (idx < 0) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= list.Items.Count) return;

            var item = list.Items[idx];
            list.Items.RemoveAt(idx);
            list.Items.Insert(newIdx, item);
            list.SelectedIndex = newIdx;
        }

        private void LstAssigned_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = lstAssigned.SelectedItem as GameListItem;
            _suppressDisplayTextEvent = true;
            if (selected != null)
            {
                string gameId = selected.Game.GameId;
                txtDisplayText.Text = _displayTexts.ContainsKey(gameId) ? _displayTexts[gameId] : "";
                txtDisplayText.Enabled = true;
            }
            else
            {
                txtDisplayText.Text = "";
                txtDisplayText.Enabled = false;
            }
            _suppressDisplayTextEvent = false;
        }

        private void txtDisplayText_TextChanged(object sender, EventArgs e)
        {
            if (_suppressDisplayTextEvent) return;
            var selected = lstAssigned.SelectedItem as GameListItem;
            if (selected != null)
            {
                _displayTexts[selected.Game.GameId] = txtDisplayText.Text;
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _section.Title = txtTitle.Text.Trim();
            _section.SectionType = cmbSectionType.SelectedIndex;
            _section.SectionSource = GetSourceString();
            // (#211) 最大表示数が意味を持つのはランキング系 (人気/最近プレイ/ランダム) のみ。手動は「割り当てた
            // ゲームを全件表示」、フィルター系は「条件に合うものを全件表示」が基本なので 0 (= 上限なし、Launcher の
            // max_display_count<=0 解釈) で保存し、意図せず切られないようにする (nud はグレーアウト中で値も信頼しない)。
            _section.MaxDisplayCount = IsRankingSource(cmbSectionSource.SelectedIndex) ? (int)nudMaxDisplayCount.Value : 0;
            _section.IsVisible = chkIsVisible.Checked;

            // 割当済みゲーム
            _section.Games = lstAssigned.Items.Cast<GameListItem>()
                .Select(item => item.Game).ToList();
            _section.GameDisplayTexts = new Dictionary<string, string>(_displayTexts);

            // (#179 round 6 M-1 案 B) DB write 直前で他 PC session を再 check (race fence)。
            // SectionPanel 側 (`ShowDialog` 直前) で既に 1 回 check 済だが、user が編集画面を 5-10 分
            // 開きっぱなしにする間に他 PC が編集を始めると衝突しうるため二段 fence。Cancel 選択時は
            // **編集画面に戻る** (= `DialogResult.OK` を設定せず Form を閉じない、入力内容を保持)。
            string opLabel = _isNew ? "ストアセクション追加" : "ストアセクション編集";
            if (SessionConflictHelper.CheckBeforeWrite(this, opLabel) == DialogResult.Cancel)
            {
                return;
            }

            try
            {
                if (_isNew)
                {
                    _dbManager.AddSection(_section);
                }
                else
                {
                    _dbManager.UpdateSection(_section);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存に失敗しました: " + ex.Message, "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// ListBoxの表示用ラッパー
        /// </summary>
        private class GameListItem
        {
            public GameInfo Game { get; }

            public GameListItem(GameInfo game)
            {
                Game = game;
            }

            public override string ToString()
            {
                return Game.Title ?? Game.GameId;
            }
        }
    }
}
