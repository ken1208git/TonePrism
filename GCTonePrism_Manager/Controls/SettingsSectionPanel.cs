using System;
using System.Reflection;
using System.Windows.Forms;

namespace GCTonePrism.Manager.Controls
{
    public partial class SettingsSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;

        public event Action DatabaseReset;

        public SettingsSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            UpdateVersionInfo();
        }

        public void UpdateVersionInfo()
        {
            if (_dbManager == null) return;

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                AssemblyName assemblyName = assembly.GetName();
                Version version = assemblyName.Version;

                string productName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "GCTonePrism 管理ソフト";

                string versionStr = $"{version.Major}.{version.Minor}.{version.Build}";
                if (version.Revision > 0)
                    versionStr += $".{version.Revision}";

                int targetVersion = _dbManager.GetTargetDatabaseVersion();
                int actualVersion = _dbManager.GetActualDatabaseVersion();

                lblVersionInfo.Text =
                    $"製品名: {productName}\n" +
                    $"バージョン: {versionStr}\n" +
                    $"データベース構造: v{actualVersion} (ターゲット: v{targetVersion})";
            }
            catch
            {
                lblVersionInfo.Text = "バージョン情報の取得に失敗しました。";
            }
        }

        private void btnResetDatabase_Click(object sender, EventArgs e)
        {
            using (var confirmForm = new ResetDatabaseConfirmForm())
            {
                if (confirmForm.ShowDialog() != DialogResult.Yes) return;
            }

            // ResetDatabase は DBファイル削除 + games フォルダ再構築 + テーブル再作成 +
            // マイグレーション再実行を行う。共有フォルダ越しでは時間がかかるので進捗バー表示。
            // 戻り値は退避フォルダ物理削除に失敗した場合の警告メッセージ (null なら完全成功)。
            // 真に失敗した場合は例外が ProcessingDialog 内でハンドリングされ DialogResult.Abort になる。
            Exception caught = null;
            string warning = null;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                try
                {
                    progress?.Report(new ProgressInfo(-1, "データベースをリセット中...", "ファイル削除と再作成を実行しています"));
                    warning = _dbManager.ResetDatabase();
                }
                catch (Exception ex)
                {
                    caught = ex;
                    throw;
                }
            })
            {
                Text = "データベースリセット中",
                MarqueeMode = true,
                AllowCancel = false
            })
            {
                var dr = dialog.ShowDialog(this);
                if (dr != DialogResult.OK)
                {
                    return;
                }
            }

            // ここまで来た = DB / games は再構築済み。UI リフレッシュは warning の有無に関わらず実行する
            // (Codex P2 #121: 警告を例外で表現すると ProcessingDialog で Abort 扱いされて
            //  リフレッシュフックがスキップされ、UI が古いまま「失敗」と誤報告されていたため)
            UpdateVersionInfo();
            DatabaseReset?.Invoke();

            if (warning != null)
            {
                MessageBox.Show(this,
                    "データベースのリセットは完了しましたが、警告があります:\n\n" + warning,
                    "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this,
                    "データベースのリセットが完了しました。",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
