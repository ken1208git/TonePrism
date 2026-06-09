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

        // (#212) ソース combo の選択肢はタイプで絞る (厳選枠=手動＋ランキング系のみ)。そのため combo の「表示 index」と
        // canonical な「ソース ID (0-12)」がズレる。AllSources が全ソースの (ラベル, canonical ID) のマスタで、
        // _sourceMap が「現在 combo に並んでいる表示 index → canonical ID」の対応表。フォーム内のロジックは
        // 表示 index ではなく SelectedSource (canonical) を見る。
        // 並び順 = ドロップダウンの表示順。canonical ID (Source) とは独立なので、ここを並べ替えても DB 解釈は不変。
        // 「制作年」(ID=12) は release_year つながりで「新作」(ID=2) の隣に置く。
        private static readonly (string Label, int Source)[] AllSources = new[]
        {
            // (#297) 「人気ランキング」(1) /「最近プレイ」(3) は実ランキング未実装の placeholder
            //   (人気=タイトル順を返すだけ / 最近プレイ=0行で自動非表示) で来場者/スタッフに誤解を与えるため、
            //   ドロップダウンの選択肢から一時的に外す。canonical mapping (GetCanonicalFromSource 等) は残すので
            //   既存セクションの読み込みは壊れない。**PR2 で responses 集計の実ランキングを実装したら、ここに
            //   ("人気ランキング", 1), ("最近プレイ", 3) を戻すこと**（戻し忘れ防止）。
            ("手動", 0), ("新作", 2), ("制作年", 12),
            ("ジャンル指定", 4), ("プレイ人数(以上)", 5), ("プレイ人数(以下)", 6),
            ("難易度", 7), ("プレイ時間", 8), ("通信プレイ", 9), ("ランダム", 10),
            ("コントローラー", 11),
        };
        private readonly List<int> _sourceMap = new List<int>();
        private bool _suppressSourceEvent = false;

        // 現在選択中の canonical なソース ID (0=手動 … 12=制作年)。未構築/未選択は 0。
        private int SelectedSource
            => (cmbSectionSource.SelectedIndex >= 0 && cmbSectionSource.SelectedIndex < _sourceMap.Count)
                ? _sourceMap[cmbSectionSource.SelectedIndex] : 0;

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

            // セクションソースの選択肢はタイプに応じて RebuildSourceCombo (SetSourceControls 内) で動的構築する (#212)。

            // ジャンルComboBox
            foreach (var genre in GenreList.AvailableGenres)
            {
                cmbGenre.Items.Add(genre);
            }

            // 値を設定
            txtTitle.Text = _section.Title ?? "";
            // (レビュー) section_type が schema 外 (0-2 以外) の legacy 異常値でも SelectedIndex 代入で例外にならないよう
            // クランプ (同 load 経路の nudMaxDisplayCount #211 クラッシュ修正と一貫させる)。
            cmbSectionType.SelectedIndex = Math.Max(0, Math.Min(cmbSectionType.Items.Count - 1, _section.SectionType));
            // (#211 fix) 手動/フィルター系は max_display_count=0 で保存されるが nudMaxDisplayCount.Minimum=1 のため
            // 0 をそのまま代入すると例外になる。グレーアウト時の表示値は保存に影響しない (btnOK で 0 を書く) ので
            // Min/Max にクランプして読み込む。
            nudMaxDisplayCount.Value = ClampToNud(nudMaxDisplayCount, _section.MaxDisplayCount);
            chkIsVisible.Checked = _section.IsVisible;

            // ソースの設定 (タイプに応じた combo 構築を含む)。legacy の許可外ソース×厳選枠は手動に落ち、案内する。
            bool loadCoerced = SetSourceControls(_section.SectionSource ?? "manual");

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

            // ソース由来の表示更新 (値コントロールの可視性 / 最大表示数の有効・無効 / ゲーム割当の可視性) は
            // SetSourceControls → RebuildSourceCombo → OnSourceChanged で既に反映済み。
            // legacy の許可外ソース×厳選枠を手動に落とした場合だけここで案内する。
            if (loadCoerced) ShowShowcaseCoerceDialog();
        }

        // ソース文字列 (DB値) を (1) canonical ソース ID に解釈してタイプに応じた combo を構築・選択し、
        // (2) ジャンル/数値/難易度等の値コントロールに保存値を反映する。厳選枠で許可外ソースだった場合は
        // combo に出ないため手動に落ちる。戻り値: 許可外で手動に coerce されたか。
        private bool SetSourceControls(string source)
        {
            int canon = CanonSourceFromString(source);
            // (レビュー) 認識できないソース文字列は manual として扱う (= GetSourceString が "manual" を書き元の値を破棄する)。
            // 現状すべてのソースを網羅しているので通常は起きないが、将来 Launcher 側にだけソースを追加して Manager の
            // 解釈を更新し忘れた場合などに silent でデータが書き換わるのを防ぐため、最低限ログに残す (CLAUDE.md §3.6)。
            if (canon == 0 && !string.IsNullOrEmpty(source) && source != "manual")
                Logger.Warn("StoreSectionForm: 未知のソース文字列 '" + source + "' を manual として解釈しました (このまま保存すると元の値は失われます)。");

            // タイプに応じて combo を構築 + 該当ソースを選択 (許可外なら手動に落ちる)。OnSourceChanged が走り
            // 値コントロールはいったん既定化されるので、保存値の反映はこの後に行う。
            bool coerced = RebuildSourceCombo(cmbSectionType.SelectedIndex, canon);
            if (coerced) return true; // 手動に落ちたので保存値 (ジャンル/数値等) は反映しない

            // 保存値を値コントロールへ反映
            if (canon == 4) // genre:
            {
                int gi = cmbGenre.Items.IndexOf(source.Substring(6));
                if (gi >= 0) cmbGenre.SelectedIndex = gi;
            }
            else if (canon == 5 || canon == 6) // players_min/max:
            {
                if (int.TryParse(source.Substring(source.IndexOf(':') + 1), out int val))
                    nudSourceValue.Value = ClampToNud(nudSourceValue, val);
            }
            else if (canon == 7) // difficulty:
            {
                if (int.TryParse(source.Substring(11), out int dval) && dval >= 1 && dval <= cmbSourceValue.Items.Count)
                    cmbSourceValue.SelectedIndex = dval - 1;
            }
            else if (canon == 8) // play_time:
            {
                if (int.TryParse(source.Substring(10), out int pval) && pval >= 1 && pval <= cmbSourceValue.Items.Count)
                    cmbSourceValue.SelectedIndex = pval - 1;
            }
            else if (canon == 12) // release_year:
            {
                if (int.TryParse(source.Substring(13), out int year))
                    nudSourceValue.Value = ClampToNud(nudSourceValue, year);
            }
            return false;
        }

        // ソース文字列 → canonical ソース ID (0-12)。AllSources の並びと一致。
        private static int CanonSourceFromString(string source)
        {
            if (source == "popular") return 1;
            if (source == "recent") return 2;
            if (source == "recently_played") return 3;
            if (source.StartsWith("genre:")) return 4;
            if (source.StartsWith("players_min:")) return 5;
            if (source.StartsWith("players_max:")) return 6;
            if (source.StartsWith("difficulty:")) return 7;
            if (source.StartsWith("play_time:")) return 8;
            if (source == "online") return 9;
            if (source == "random") return 10;
            if (source == "controller") return 11;
            if (source.StartsWith("release_year:")) return 12;
            return 0; // manual / 不明
        }

        // NumericUpDown の Min/Max にクランプ (範囲外代入の例外回避)。
        private static decimal ClampToNud(NumericUpDown nud, int value)
            => Math.Max(nud.Minimum, Math.Min(nud.Maximum, value));

        private string GetSourceString()
        {
            int idx = SelectedSource;
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
            // RebuildSourceCombo が combo を作り直している最中は OnSourceChanged を別途 1 回呼ぶので、ここは無視。
            if (_suppressSourceEvent) return;
            OnSourceChanged();
        }

        // ソース選択が変わったとき (ユーザー操作 / タイプ変更で combo 再構築) の表示更新をまとめて行う。
        private void OnSourceChanged()
        {
            int src = SelectedSource;
            // (#290 review) 難易度(7)/プレイ時間(8) のラベル付き選択肢を入れ直す。
            PopulateSourceValueCombo(src);
            // (#291 / レビュー) nud (プレイ人数 / 制作年) は共有コントロールなので、ソース種別に対して値が不自然なら
            // 既定へ補正する。制作年へ切替時は年らしくなければ今年に。プレイ人数へ切替時に年(>=1000)が残っていたら 1 に。
            // (= 制作年 2026 のままプレイ人数へ切替えて players_min:2026 と保存されるのを防ぐ。load 時は直後に保存値で上書き。)
            if (src == 12 && nudSourceValue.Value < 2000)
                nudSourceValue.Value = System.DateTime.Now.Year;
            else if ((src == 5 || src == 6) && nudSourceValue.Value >= 1000)
                nudSourceValue.Value = 1;
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
            // (#212) タイプに応じてソース combo の選択肢を絞り直す。厳選枠 (スライドショー/タイルグリッド) は
            // 手動＋ランキング系のみ出す。現在のソースが許可外なら手動に落とし、案内ダイアログを出す。
            int prevSource = SelectedSource;
            bool coerced = RebuildSourceCombo(cmbSectionType.SelectedIndex, prevSource);
            if (coerced) ShowShowcaseCoerceDialog();

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

        // (#212 改) 厳選枠で許可するソース: 手動(0) ＋ ランキング系 (= IsRankingSource: 人気/最近プレイ/ランダム)。
        // 厳選枠は「少数を大きく見せる」枠なので、表示数をコントロールできるソースだけが合う:
        //   - 手動       = 割り当てで枚数を決める
        //   - ランキング系 = max_display_count で TOP N を指定できる (背景画像/表示テキストはゲーム自身のものに
        //                    フォールバックするので自動でも崩れず、ローテーションするヒーロー枠と相性が良い)
        // 一方フィルター系 (genre/players_*/difficulty/play_time/online/controller/recent/release_year) は条件一致を
        // 「全件」表示で枚数を絞れず "厳選" にならないため不可。結果として「厳選枠で許可するソース集合」=
        // 「max_display_count が有効なソース集合 ∪ 手動」と一致する (1 つの原則で説明できる)。通常カテゴリ行(0) は全ソース可。
        private static bool TypeAllowsSource(int typeIndex, int sourceIndex)
            => !TypeIsShowcase(typeIndex) || sourceIndex == 0 || IsRankingSource(sourceIndex);

        // (#211) 最大表示数 (nud + ラベル) を、ランキング系ソースのときだけ有効化する。手動/フィルター系はグレーアウト。
        private void UpdateMaxDisplayCountEnabled()
        {
            bool enabled = IsRankingSource(SelectedSource);
            nudMaxDisplayCount.Enabled = enabled;
            lblMaxDisplayCount.Enabled = enabled;
        }

        // (#212) タイプに応じてソース combo の選択肢を作り直す。厳選枠 (スライドショー/タイルグリッド) は
        // 手動＋ランキング系のみ、通常カテゴリ行は全ソースを出す。desiredSource を選択するが、許可外なら手動(0)に
        // 落とす (= dropdown に出さないことで「選べない」を表現。選んでから戻す旧 coerce 方式より直感的)。
        // 構築中は _suppressSourceEvent で SelectedIndexChanged を抑止し、最後に OnSourceChanged を 1 回だけ呼ぶ。
        // 戻り値: desiredSource が許可外で手動に落ちたか (desiredSource==0 のときは false)。
        private bool RebuildSourceCombo(int typeIndex, int desiredSource)
        {
            _suppressSourceEvent = true;
            cmbSectionSource.Items.Clear();
            _sourceMap.Clear();
            int selectDisplay = 0;
            bool found = false;
            foreach (var entry in AllSources)
            {
                if (!TypeAllowsSource(typeIndex, entry.Source)) continue;
                if (entry.Source == desiredSource) { selectDisplay = _sourceMap.Count; found = true; }
                cmbSectionSource.Items.Add(entry.Label);
                _sourceMap.Add(entry.Source);
            }
            cmbSectionSource.SelectedIndex = (cmbSectionSource.Items.Count > 0) ? selectDisplay : -1;
            _suppressSourceEvent = false;
            OnSourceChanged();
            return !found && desiredSource != 0;
        }

        // (#212) 厳選枠で許可外ソースを手動に落としたときの案内。
        private void ShowShowcaseCoerceDialog()
        {
            MessageBox.Show(
                "スライドショー / タイルグリッドのソースは「手動」か「ランキング系（人気ランキング・最近プレイ・ランダム）」のみ対応です。\n" +
                "（少数を大きく見せる厳選枠のため、ジャンル等の絞り込み系は表示数を絞れず不可）\n\n" +
                "ソースを「手動」に変更しました。",
                "ソースを変更", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateSourceParameterVisibility()
        {
            int idx = SelectedSource;
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
            bool isManual = SelectedSource == 0;
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
            _section.MaxDisplayCount = IsRankingSource(SelectedSource) ? (int)nudMaxDisplayCount.Value : 0;
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
