using System;
using System.Collections.Generic;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Repositories;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// データベース操作のファサード。
    /// 既存の呼び出し元を壊さないよう、各Repositoryへ委譲する。
    /// </summary>
    public class DatabaseManager
    {
        private readonly DatabaseConnection _conn;
        private readonly SchemaManager _schema;
        private readonly GameRepository _gameRepo;
        private readonly VersionRepository _versionRepo;
        private readonly DeveloperRepository _devRepo;
        private readonly StoreSectionRepository _sectionRepo;
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupLogRepository _backupLogRepo;
        private readonly BackupService _backupService;
        private readonly RestoreService _restoreService;

        public DatabaseManager()
        {
            _conn = new DatabaseConnection();
            _schema = new SchemaManager(_conn);
            _devRepo = new DeveloperRepository(_conn);
            _gameRepo = new GameRepository(_conn, _devRepo);
            _versionRepo = new VersionRepository(_conn, _devRepo);
            _sectionRepo = new StoreSectionRepository(_conn);
            _settingsRepo = new SettingsRepository(_conn);
            _backupLogRepo = new BackupLogRepository(_conn);
            _backupService = new BackupService(_conn, _backupLogRepo, _settingsRepo);
            _restoreService = new RestoreService(_conn);
        }

        // --- バックアップ機能アクセサ ---
        public BackupService BackupService { get { return _backupService; } }
        public RestoreService RestoreService { get { return _restoreService; } }
        public BackupLogRepository BackupLogRepository { get { return _backupLogRepo; } }
        public SettingsRepository SettingsRepository { get { return _settingsRepo; } }

        // --- 接続・スキーマ ---
        public bool DatabaseExists() => _conn.DatabaseExists();
        public bool TablesExist() => _schema.TablesExist();
        public int GetTargetDatabaseVersion() => _schema.GetTargetDatabaseVersion();
        public int GetActualDatabaseVersion() => _schema.GetActualDatabaseVersion();
        public void InitializeDatabase() => _schema.InitializeDatabase();
        public void ResetDatabase() => _schema.ResetDatabase();

        // --- ゲーム ---
        public List<GameInfo> GetAllGames() => _gameRepo.GetAll();
        public GameInfo GetGameById(string gameId) => _gameRepo.GetById(gameId);
        public int GetMinDisplayOrder() => _gameRepo.GetMinDisplayOrder();
        public void AddGame(GameInfo game) => _gameRepo.Add(game);
        public void UpdateGame(GameInfo game) => _gameRepo.Update(game);
        public void DeleteGame(string gameId) => _gameRepo.Delete(gameId);
        public void UpdateGameId(string oldId, string newId) => _gameRepo.UpdateGameId(oldId, newId);

        // --- 開発者 ---
        public List<DeveloperInfo> GetDeveloperInfosByGameId(string gameId) => _devRepo.GetByGameId(gameId);
        public List<DeveloperInfo> GetDeveloperInfosByGameIdAndVersionId(string gameId, int versionId, SQLiteConnection connection) => _devRepo.GetByGameIdAndVersionId(gameId, versionId, connection);

        // --- バージョン ---
        public void AddGameVersion(GameVersion version) => _versionRepo.Add(version);
        public void UpdateGameVersion(GameVersion version) => _versionRepo.Update(version);
        public List<GameVersion> GetGameVersions(string gameId) => _versionRepo.GetByGameId(gameId);
        public GameVersion GetLatestVersion(string gameId) => _versionRepo.GetLatest(gameId);

        // --- ストアセクション ---
        public List<StoreSectionInfo> GetAllSections() => _sectionRepo.GetAll();
        public StoreSectionInfo GetSectionById(int sectionId) => _sectionRepo.GetById(sectionId);
        public int GetMaxSectionDisplayOrder() => _sectionRepo.GetMaxDisplayOrder();
        public void AddSection(StoreSectionInfo section) => _sectionRepo.Add(section);
        public void UpdateSection(StoreSectionInfo section) => _sectionRepo.Update(section);
        public void DeleteSection(int sectionId) => _sectionRepo.Delete(sectionId);

        // --- エラーメッセージ ---
        public static string GetUserFriendlyErrorMessage(SQLiteException ex) => DatabaseConnection.GetUserFriendlyErrorMessage(ex);
    }
}
