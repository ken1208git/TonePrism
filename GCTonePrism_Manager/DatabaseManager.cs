using System;
using System.Collections.Generic;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Repositories;

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

        public DatabaseManager()
        {
            _conn = new DatabaseConnection();
            _schema = new SchemaManager(_conn);
            _devRepo = new DeveloperRepository(_conn);
            _gameRepo = new GameRepository(_conn, _devRepo);
            _versionRepo = new VersionRepository(_conn, _devRepo);
        }

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

        // --- 開発者 ---
        public List<DeveloperInfo> GetDeveloperInfosByGameId(string gameId) => _devRepo.GetByGameId(gameId);
        public List<DeveloperInfo> GetDeveloperInfosByGameIdAndVersionId(string gameId, int versionId, SQLiteConnection connection) => _devRepo.GetByGameIdAndVersionId(gameId, versionId, connection);

        // --- バージョン ---
        public void AddGameVersion(GameVersion version) => _versionRepo.Add(version);
        public void UpdateGameVersion(GameVersion version) => _versionRepo.Update(version);
        public List<GameVersion> GetGameVersions(string gameId) => _versionRepo.GetByGameId(gameId);
        public GameVersion GetLatestVersion(string gameId) => _versionRepo.GetLatest(gameId);

        // --- エラーメッセージ ---
        public static string GetUserFriendlyErrorMessage(SQLiteException ex) => DatabaseConnection.GetUserFriendlyErrorMessage(ex);
    }
}
