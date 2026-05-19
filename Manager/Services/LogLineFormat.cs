using System.Text.RegularExpressions;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// Logger 出力 format `[YYYY-MM-DD HH:mm:ss] [LEVEL] message` の parse 用 SoT。
    ///
    /// 同 format は Manager `Services/Logger.cs` + Companions/Updater `Logger.cs` (= 将来 WindowProbe 等他
    /// Companion も同 format に揃える規約、SPEC §3.6) で共通。Manager UI 側 consumer (= LogSectionPanel
    /// + UpdaterLogAbsorber) が 2 callsite で同 regex を hardcode していたのを SoT 化、SPEC §3.6
    /// 「将来 DEBUG level 追加」等の format 拡張時に **1 ファイル更新で両 consumer が同期** する。
    ///
    /// 拡張時の注意:
    /// - DEBUG level を追加するなら `LineRegex` の `(?:INFO|WARN|ERROR)` を `(?:INFO|WARN|ERROR|DEBUG)` に。
    /// - level padding (`[INFO ]` 等) を導入するなら `\] \[` を `\]\s*\[` に + parse 側で trim を追加。
    /// - 行頭 anchor `^` 維持 (= continuation line を別途扱う前提、本 regex は header 行のみ判定する責務)。
    /// </summary>
    internal static class LogLineFormat
    {
        /// <summary>
        /// 行頭フォーマット: `[YYYY-MM-DD HH:mm:ss] [LEVEL] rest`
        /// - `ts`: timestamp 文字列 (parse は consumer 側責務、本 regex は str 切出のみ)
        /// - `level`: INFO / WARN / ERROR
        /// - `rest`: level 後の本文 (改行は含まない、行単位で split 済前提)
        /// </summary>
        public static readonly Regex LineRegex = new Regex(
            @"^\[(?<ts>[^\]]+)\] \[(?<level>INFO|WARN|ERROR)\] (?<rest>.*)$",
            RegexOptions.Compiled);
    }
}
