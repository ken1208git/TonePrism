using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using WinForms = System.Windows.Forms;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 ② ゲーム一覧 WPF 化) ゲーム一覧の WPF ネイティブページ。旧 GameHostPage (WinForms GameSectionPanel を
    /// WindowsFormsHost でホスト) を置換。一覧は Win11 設定アプリ風のカード型 ListBox、操作 (追加/編集/版up/削除) は
    /// 抽出済 service + 既存 WinForms フォームを可視シェル窓 HWND (<see cref="ShellOwner"/>) を owner にして開く。
    /// 重ロジックは service 側にあるので本ページは「選択検証 + 競合チェック + service 呼び出し + 再読込」の薄い配線
    /// (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は GameSectionPanel のボタンハンドラと同一。
    /// </summary>
    public partial class GameListPage : Page
    {
        private DatabaseManager Db => ShellWindow.SharedDb;

        // service は Db (= ShellWindow.SharedDb) 依存。Db は起動完了後に確定するため初回操作時に遅延生成。
        private GameVersionUpService _versionUpService;
        private GameDeletionService _deletionService;
        private GameVersionUpService VersionUpService => _versionUpService ??= new GameVersionUpService(Db);
        private GameDeletionService DeletionService => _deletionService ??= new GameDeletionService(Db);

        // owner = このページが載る可視シェル窓の HWND。WinForms ダイアログ / MessageBox をシェルにモーダルで開くため。
        private WinForms.IWin32Window Owner => ShellOwner.For(this);

        private GameInfo SelectedGame => (GamesList.SelectedItem as GameListItem)?.Game;

        // ジャンルフィルターのチェックボックス（master 一覧から ctor で一度だけ生成＝LoadGames で再生成しないので選択が保持される）。
        private readonly List<CheckBox> _genreChecks = new List<CheckBox>();
        // クリア等で複数のフィルター controls をまとめて変更する間、ApplyView の連続発火を抑える。
        private bool _suppressFilter;

        public GameListPage()
        {
            InitializeComponent();
            BuildGenreFilterChecks();
            // NumberBox の ValueChanged はコードで配線 (SettingsPage と同パターン、XAML の TypedEventHandler を避ける)。
            PlayerMinBox.ValueChanged += (s, e) => OnFilterChanged();
            PlayerMaxBox.ValueChanged += (s, e) => OnFilterChanged();
            // NavigationView の表示遷移ごとに最新化 (DashboardPage と同様、Loaded は表示ごとに発火)。
            Loaded += (_, _) => LoadGames();
        }

        private void BuildGenreFilterChecks()
        {
            var fg = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));
            foreach (var genre in GenreList.AvailableGenres)
            {
                var cb = new CheckBox
                {
                    Content = genre,
                    Foreground = fg,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 14, 7)
                };
                cb.Checked += FilterCheck_Changed;
                cb.Unchecked += FilterCheck_Changed;
                _genreChecks.Add(cb);
                GenrePanel.Children.Add(cb);
            }
        }

        // 全件 (検索/フィルター前)。表示は ApplyView で絞り込んで GamesList へ反映する。
        private List<GameListItem> _allItems;

        private void LoadGames()
        {
            if (Db == null) return;
            try
            {
                _allItems = Db.GetAllGames()
                    .Select(g => new GameListItem(g))
                    .ToList();
                ApplyView();                        // 現在の検索 / フィルター / 並べ替えを適用して表示
                _ = LoadThumbnailsAsync(_allItems); // サムネは背景で後追いロード (SMB でも UI を固めない)
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(Owner,
                    "ゲーム一覧の読み込みに失敗しました。\n\n" + ex.Message,
                    "エラー", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        // 検索語 / フィルター / 並べ替えを _allItems に適用して GamesList に反映。GameListItem インスタンスは
        // 再利用するためロード済サムネは保持される (選択は ItemsSource 差し替えでリセット＝絞り込み時は許容)。
        // 操作後の LoadGames からも呼ばれるので、コンボ/検索欄の現在値が操作をまたいで維持される。
        private void ApplyView()
        {
            if (_allItems == null) return;
            IEnumerable<GameListItem> q = _allItems;

            // ===== フィルター (フライアウトの各軸を AND で適用) =====
            // 表示状態
            switch (VisCombo?.SelectedIndex ?? 0)
            {
                case 1: q = q.Where(i => i.IsVisible); break;
                case 2: q = q.Where(i => !i.IsVisible); break;
            }
            // 通信プレイ (index 1..3 → SupportedConnection 0..2)
            int conn = ConnCombo?.SelectedIndex ?? 0;
            if (conn > 0) q = q.Where(i => i.Game.SupportedConnection == conn - 1);
            // プレイ人数: ゲームの対応範囲 [Min,Max] が指定 [下限,上限] と重なる (overlap) ものを表示。不明は 1人用扱い。
            int plo = (int)(PlayerMinBox?.Value ?? 1);
            int phi = (int)(PlayerMaxBox?.Value ?? 99);
            if (phi < plo) phi = plo;
            if (plo > 1 || phi < 99)
                q = q.Where(i => (i.Game.MinPlayers ?? 1) <= phi && (i.Game.MaxPlayers ?? 1) >= plo);
            // 難易度 / プレイ時間 (index 1..3 == 値 1..3)
            int diff = DiffCombo?.SelectedIndex ?? 0;
            if (diff > 0) q = q.Where(i => i.Game.Difficulty == diff);
            int ptime = TimeCombo?.SelectedIndex ?? 0;
            if (ptime > 0) q = q.Where(i => i.Game.PlayTime == ptime);
            // ジャンル (チェックされたいずれかを含む = OR)
            var genres = SelectedGenres();
            if (genres.Count > 0)
                q = q.Where(i => i.Game.Genre != null && i.Game.Genre.Any(g => genres.Contains(g)));

            // 検索 (名前 / ID / 製作者の部分一致・大小無視)
            string term = SearchBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(term))
                q = q.Where(i => i.Matches(term));

            // 並べ替え (未設定の数値は int.MaxValue で末尾へ寄せる。第2キーはタイトルで安定化)
            var byTitle = StringComparer.CurrentCulture;
            switch (SortCombo?.SelectedIndex ?? 0)
            {
                case 1: // 製作者
                    q = q.OrderBy(i => i.Game.DevelopersDisplay ?? "", byTitle).ThenBy(i => i.Game.Title, byTitle);
                    break;
                case 2: // リリース年（新しい順）
                    q = q.OrderByDescending(i => i.Game.ReleaseYear ?? int.MinValue).ThenBy(i => i.Game.Title, byTitle);
                    break;
                default: // タイトル
                    q = q.OrderBy(i => i.Game.Title, byTitle);
                    break;
            }

            var shown = q.ToList();
            GamesList.ItemsSource = shown;
            CountText.Text = shown.Count == _allItems.Count
                ? _allItems.Count + " 個のゲーム"
                : _allItems.Count + " 個中 " + shown.Count + " 個を表示";
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyView();
        private void View_Changed(object sender, SelectionChangedEventArgs e) => ApplyView();   // 並べ替え

        // ===== フィルター フライアウト =====
        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            // 開く操作のみ。閉じ/トグル/再オープン抑止はページ全体の Root_PreviewMouseDown + FlyoutDismiss が一律に担う。
            if (FilterPopup != null) FilterPopup.IsOpen = true;
        }

        private void FilterPopup_Opened(object sender, EventArgs e) => FlyoutDismiss.NotifyOpened(FilterPopup);
        private void FilterPopup_Closed(object sender, EventArgs e) => FlyoutDismiss.NotifyClosed();

        // ポップアップ上のホイールが裏のゲーム一覧へ抜けて爆速スクロールするのを止める。ジャンルの ScrollViewer は
        // 自分でホイールを処理 (より深いので先に Handled) するため、そこで未処理 (=コンボ/余白上) の分だけ Border で食う。
        private void FilterPopup_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => e.Handled = true;

        // ジャンル欄のホイール/トラックパッドスクロールが速すぎるので、delta 比例で控えめに自前スクロールする
        // (既定 ScrollViewer は 1 イベントで数行飛ぶ + トラックパッドは高頻度なので速くなる)。係数は体感調整値。
        private void GenreScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta * 0.3);
                e.Handled = true;
            }
        }

        // コンボ (SelectionChanged) とチェックボックス (Checked/Unchecked) でデリゲート型が違うので入口を2つに分け、共通処理へ。
        private void FilterCombo_Changed(object sender, SelectionChangedEventArgs e) => OnFilterChanged();
        private void FilterCheck_Changed(object sender, RoutedEventArgs e) => OnFilterChanged();

        private void OnFilterChanged()
        {
            if (_suppressFilter) return;
            ApplyView();
            UpdateFilterButtonText();
        }

        private void FilterClear_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilter = true;   // まとめてリセットする間 ApplyView を抑止 (最後に1回だけ流す)
            if (VisCombo != null) VisCombo.SelectedIndex = 0;
            if (ConnCombo != null) ConnCombo.SelectedIndex = 0;
            if (PlayerMinBox != null) PlayerMinBox.Value = 1;
            if (PlayerMaxBox != null) PlayerMaxBox.Value = 99;
            if (DiffCombo != null) DiffCombo.SelectedIndex = 0;
            if (TimeCombo != null) TimeCombo.SelectedIndex = 0;
            foreach (var cb in _genreChecks) cb.IsChecked = false;
            _suppressFilter = false;
            ApplyView();
            UpdateFilterButtonText();
        }

        private HashSet<string> SelectedGenres()
        {
            var set = new HashSet<string>();
            foreach (var cb in _genreChecks)
                if (cb.IsChecked == true && cb.Content is string g) set.Add(g);
            return set;
        }

        private int ActiveFilterCount()
        {
            int n = 0;
            if ((VisCombo?.SelectedIndex ?? 0) > 0) n++;
            if ((ConnCombo?.SelectedIndex ?? 0) > 0) n++;
            if (((int)(PlayerMinBox?.Value ?? 1)) > 1 || ((int)(PlayerMaxBox?.Value ?? 99)) < 99) n++;
            if ((DiffCombo?.SelectedIndex ?? 0) > 0) n++;
            if ((TimeCombo?.SelectedIndex ?? 0) > 0) n++;
            if (SelectedGenres().Count > 0) n++;
            return n;
        }

        // フィルターボタンに適用中の軸数バッジを出す（0 のときは素の「フィルター」）。
        private void UpdateFilterButtonText()
        {
            if (FilterButtonText == null) return;
            int n = ActiveFilterCount();
            FilterButtonText.Text = n > 0 ? "フィルター（" + n + "）" : "フィルター";
        }

        // サムネを背景で順次ロードし、決まったものから差し替える (SMB I/O で UI を固めない)。await 後は
        // 呼び出し元 (UI) スレッドに戻るので Thumbnail setter の PropertyChanged は安全。null の間はアイコン表示。
        private static async Task LoadThumbnailsAsync(List<GameListItem> items)
        {
            foreach (var item in items)
            {
                try
                {
                    var src = await Task.Run(() => GameListItem.LoadThumbnail(item.Game));
                    if (src != null) item.Thumbnail = src;
                }
                catch { /* 個別失敗は無視 (アイコンのまま) */ }
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (SessionConflictHelper.CheckBeforeWrite("ゲーム追加") == WinForms.DialogResult.Cancel) return;
            using (var form = new AddGameForm(Db))
            {
                if (form.ShowDialog(Owner) == WinForms.DialogResult.OK)
                {
                    LoadGames();
                    WinForms.MessageBox.Show(Owner,
                        "ゲーム「" + form.AddedGame.Title + "」を追加しました。",
                        "成功", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    // (#295) 成果確認 (MessageBox) を先に見せてから後追い best-effort バックアップ (版up/削除と順序統一)。
                    // ゲーム追加は games/ を変えるので DB + ゲーム本体を控える。
                    Db.SessionBackupCoordinator.RunAfterOperation(Owner, assetsChanged: true, "ゲーム追加");
                }
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す。
            var selected = SelectedGame;
            if (selected == null)
            {
                WinForms.MessageBox.Show(Owner, "編集するゲームを選択してください。", "情報",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            var game = Db.GetGameById(selected.GameId);
            if (game == null)
            {
                WinForms.MessageBox.Show(Owner, "選択されたゲームが見つかりません。", "エラー",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            if (SessionConflictHelper.CheckBeforeWrite("ゲーム編集") == WinForms.DialogResult.Cancel) return;

            using (var form = new EditGameForm(Db, game))
            {
                if (form.ShowDialog(Owner) == WinForms.DialogResult.OK)
                {
                    LoadGames();
                    WinForms.MessageBox.Show(Owner,
                        "ゲーム「" + form.EditedGame.Title + "」を更新しました。",
                        "成功", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    // (#295) ゲーム本体を変えた編集のときだけアセットも控える (form.AssetsChangedOnDisk)。
                    Db.SessionBackupCoordinator.RunAfterOperation(Owner, form.AssetsChangedOnDisk, "ゲーム編集");
                }
                else if (form.DataChangedOutsideOk)
                {
                    // (#209) バージョン即時削除は OK を介さず DB 確定するため、Cancel/×で閉じても一覧を再読込し、
                    // active 版付け替え後にメイン画面が削除済み版を出し続ける stale を防ぐ。
                    LoadGames();
                    Db.SessionBackupCoordinator.RunAfterOperation(Owner, form.AssetsChangedOnDisk, "バージョン削除");
                }
            }
        }

        private void VersionUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedGame;
            if (selected == null)
            {
                WinForms.MessageBox.Show(Owner, "バージョンアップするゲームを選択してください。", "情報",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }
            if (SessionConflictHelper.CheckBeforeWrite("ゲームのバージョンアップ") == WinForms.DialogResult.Cancel) return;

            var game = Db.GetGameById(selected.GameId);
            if (game == null)
            {
                WinForms.MessageBox.Show(Owner, "選択されたゲームが見つかりません。", "エラー",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }

            // 重い処理 (コピー / atomic move / DB / アクティブ化 / バックアップ) は GameVersionUpService に委譲。
            VersionUpService.Run(Owner, game, LoadGames);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedGame;
            if (selected == null)
            {
                WinForms.MessageBox.Show(Owner, "削除するゲームを選択してください。", "情報",
                    WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }
            if (SessionConflictHelper.CheckBeforeWrite("ゲーム削除") == WinForms.DialogResult.Cancel) return;

            // rename-rollback の多段削除フロー (退避 → DB 削除 → 物理削除) は GameDeletionService に委譲。
            DeletionService.Run(Owner, selected, LoadGames);
        }

        // ⋯ メニュー: クリックで開く (カードを選択してから)。閉じ/トグル/再オープン抑止は Root_PreviewMouseDown + FlyoutDismiss が担う。
        private void CardMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || fe.ContextMenu == null) return;
            if (fe.DataContext is GameListItem item) GamesList.SelectedItem = item;
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.IsOpen = true;
        }

        private void CardMenu_Opened(object sender, RoutedEventArgs e) => FlyoutDismiss.NotifyOpened((ContextMenu)sender);
        private void CardMenu_Closed(object sender, RoutedEventArgs e) => FlyoutDismiss.NotifyClosed();

        // ページ全体: フライアウト (フィルター Popup / ⋯ ContextMenu) が開いている間の本体側クリックを「閉じる」だけに
        // 消費し、その下の要素を再反応させない (= Win11 流・FlyoutDismiss に共通化)。他ページでも root に本ハンドラ +
        // 各フライアウトに Opened/Closed を張れば同じ挙動になる。
        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 中身クリック (コンボ/チェック/メニュー項目等) は触らせる。本体側クリックだけ閉じて消費する。
            if (FlyoutDismiss.IsInsideOpen(e.OriginalSource as DependencyObject)) return;
            if (FlyoutDismiss.DismissOpen()) e.Handled = true;
        }

        private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 行 (カード) ダブルクリックで編集 (GameSectionPanel の dgvGames_CellDoubleClick と同挙動)。選択時のみ。
            if (SelectedGame != null) Edit_Click(sender, e);
        }

        /// <summary>
        /// カード 1 枚分の表示用 view-model。<see cref="GameInfo"/> を表示文字列に射影しつつ元データを保持
        /// (操作は <see cref="Game"/> を介す)。サブタイトルは Win11 設定の「発行元 | 日付」に倣い
        /// 「ゲームID ・ 製作者 ・ リリース年」を中黒で連結。
        /// </summary>
        private sealed class GameListItem : INotifyPropertyChanged
        {
            public GameInfo Game { get; }
            public string Title => string.IsNullOrWhiteSpace(Game.Title) ? Game.GameId : Game.Title;
            public string Subtitle { get; }
            public string Version => string.IsNullOrWhiteSpace(Game.Version) ? "—" : Game.Version;
            public bool IsVisible => Game.IsVisible;

            // サムネは SMB I/O を含むため背景で後追いロードし、決まったら通知して ImageBrush を差し替える
            // (null の間はゲームアイコンのフォールバックが見える)。
            private ImageSource _thumbnail;
            public ImageSource Thumbnail
            {
                get => _thumbnail;
                set { _thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public GameListItem(GameInfo game)
            {
                Game = game;
                Subtitle = BuildSubtitle(game);
            }

            private static string BuildSubtitle(GameInfo g)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(g.GameId)) parts.Add(g.GameId);
                if (!string.IsNullOrWhiteSpace(g.DevelopersDisplay)) parts.Add(g.DevelopersDisplay);
                if (g.ReleaseYear.HasValue) parts.Add(g.ReleaseYear.Value + "年");
                return string.Join(" ｜ ", parts);
            }

            /// <summary>名前 / ゲームID / 製作者のいずれかに検索語を部分一致 (大小無視) で含むか。</summary>
            public bool Matches(string term)
                => ContainsCI(Game.Title, term) || ContainsCI(Game.GameId, term) || ContainsCI(Game.DevelopersDisplay, term);

            private static bool ContainsCI(string s, string term)
                => !string.IsNullOrEmpty(s) && s.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;

            /// <summary>
            /// アクティブ版のサムネイルを読み込む。解決は <see cref="RestoreReconciliationService"/> の ResolvesAsset と
            /// 同じ三段 (絶対 / gameFolder 基準 / install 基準) に合わせ、不備チェックが「在り」と判定する物と一致させる。
            /// 背景スレッドから呼ばれるため Frozen な BitmapImage を返す。欠落・失敗は null (= アイコンにフォールバック)。
            /// </summary>
            public static ImageSource LoadThumbnail(GameInfo g)
            {
                try
                {
                    string path = ResolveThumbnailPath(g);
                    if (path == null) return null;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;            // 全読込してファイルを掴まない
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = 96;                            // 42px アバター表示に十分 (高DPI 2x強)
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();                                         // 背景生成 → UI 使用のため凍結
                    return bmp;
                }
                catch (Exception ex)
                {
                    Logger.Warn("[GameListPage] サムネ読込失敗 (" + (g?.GameId ?? "?") + "): " + ex.Message);
                    return null;
                }
            }

            // 三段解決 (絶対 → gameFolder 基準 → install 基準)。RestoreReconciliationService.ResolvesExecutable と同形。
            private static string ResolveThumbnailPath(GameInfo g)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.ThumbnailPath) || string.IsNullOrWhiteSpace(g.GameId))
                    return null;
                string p = g.ThumbnailPath.Trim();
                try
                {
                    if (Path.IsPathRooted(p)) return File.Exists(p) ? p : null;
                    string c1 = Path.Combine(PathManager.GetGameFolder(g.GameId), p);
                    if (File.Exists(c1)) return c1;
                    string c2 = Path.Combine(PathManager.BaseDirectory, p);
                    if (File.Exists(c2)) return c2;
                }
                catch { return null; }
                return null;
            }
        }
    }
}
