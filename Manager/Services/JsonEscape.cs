namespace TonePrism.Manager.Services
{
    /// <summary>
    /// 最小限の JSON string escape helper (`\` と `"` のみ escape)。
    ///
    /// **用途範囲**:
    /// - sentinel file / drop folder bridge 等の **小規模 ad-hoc JSON 書出**で使用
    ///   (例: `Program.WriteLogsRootMigrationSentinel`, `Services.LauncherLogsRootBridge.WriteCurrentLogsRoot`)
    /// - 値は Windows path 等の **U+0020..U+007E 範囲を超えない前提**で使用
    ///
    /// **未対応**:
    /// - U+0000..U+001F 制御文字の `\uXXXX` escape (= 仕様上は JSON で必要)
    /// - surrogate pair / `/` (= 任意 escape)
    /// 上記が必要な surface (例: user-typed free-form text) では `System.Web.Script.Serialization.JavaScriptSerializer`
    /// 等の proper serializer を使うこと。
    ///
    /// **重複定義防止**: R4 review M-1 で `Program.cs` + `LauncherLogsRootBridge.cs` の 2 callsite に
    /// copy-paste されていた同 helper を本 file に集約、将来 schema 拡張 (Unicode escape 等) で
    /// 片方だけ更新する silent drift を予防。
    /// </summary>
    internal static class JsonEscape
    {
        public static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
