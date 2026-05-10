using System;
using System.IO;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// バックアップ履歴 (BackupLogEntry) の絶対パスを解決するヘルパー (#126)。
    /// プロジェクト場所が移動した場合でも、relative_path が記録されているレコードは
    /// 現在の prism.db からの絶対パスを動的に再計算する。
    /// マイグレーション前の relative_path NULL レコードは file_path をそのまま使う
    /// (プロジェクト移動には追従できないが、後方互換性のため温存)。
    /// </summary>
    public static class BackupPathResolver
    {
        /// <summary>
        /// BackupLogEntry の絶対パスを解決する。
        /// </summary>
        /// <param name="entry">対象エントリ</param>
        /// <param name="dbPath">現在の prism.db のフルパス</param>
        /// <returns>解決された絶対パス。entry が null や情報不足なら空文字</returns>
        public static string ResolveAbsolutePath(BackupLogEntry entry, string dbPath)
        {
            if (entry == null) return string.Empty;

            if (!string.IsNullOrEmpty(entry.RelativePath) && !string.IsNullOrEmpty(dbPath))
            {
                try
                {
                    string dbDir = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrEmpty(dbDir))
                    {
                        return Path.GetFullPath(Path.Combine(dbDir, entry.RelativePath));
                    }
                }
                catch
                {
                    // 不正な相対パス等は無視して file_path フォールバックへ
                }
            }

            return entry.FilePath ?? string.Empty;
        }

        /// <summary>
        /// destination 絶対パスを dbDir からの相対パスに変換する。
        /// dbDir 配下に無い場合は null を返す (絶対パス保存のフォールバックを呼び出し側に委ねる)。
        /// .NET Framework 4.8 のため Path.GetRelativePath は使えない → Uri ベースで計算。
        /// </summary>
        public static string ToRelativeFromDbDir(string destinationPath, string dbDir)
        {
            if (string.IsNullOrEmpty(destinationPath) || string.IsNullOrEmpty(dbDir))
                return null;

            try
            {
                string normalizedDbDir = dbDir;
                if (!normalizedDbDir.EndsWith(Path.DirectorySeparatorChar.ToString())
                    && !normalizedDbDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    normalizedDbDir += Path.DirectorySeparatorChar;
                }

                Uri dbDirUri = new Uri(normalizedDbDir);
                Uri destinationUri = new Uri(destinationPath);

                if (!dbDirUri.IsBaseOf(destinationUri))
                    return null;

                string relative = Uri.UnescapeDataString(
                    dbDirUri.MakeRelativeUri(destinationUri).ToString());
                return relative.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }
    }
}
