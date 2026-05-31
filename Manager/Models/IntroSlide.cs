namespace TonePrism.Manager.Models
{
    /// <summary>
    /// (#253) イントロガイドのスライド 1 枚。`intro_slides` テーブルの 1 行に対応する。
    ///
    /// スクリーンセーバー → ブラウズ間に表示する案内スライド (展示の説明 / 楽しみ方 / 注意事項 等)。
    /// 画像は `guide/` フォルダにファイル別管理し、`ImagePath` は相対パスのみ持つ (games のサムネと同流儀)。
    /// `BodyText` 空 = image-only スライド、`ImagePath` null = text-only スライド、の両方を許容する。
    /// </summary>
    public class IntroSlide
    {
        /// <summary>主キー (intro_slides.slide_id、AUTOINCREMENT)。新規は 0。</summary>
        public int SlideId { get; set; }

        /// <summary>表示順 (昇順)。</summary>
        public int DisplayOrder { get; set; }

        /// <summary>スライド本文 (空可 = image-only)。</summary>
        public string BodyText { get; set; } = "";

        /// <summary>画像の相対パス (`guide/` 基準)。null 可 = text-only。</summary>
        public string ImagePath { get; set; }

        /// <summary>自動送り秒数 (1-60、DB CHECK)。</summary>
        public int DurationSec { get; set; } = 5;

        /// <summary>表示 ON/OFF (削除せず一時非表示にできる)。</summary>
        public bool IsVisible { get; set; } = true;
    }
}
