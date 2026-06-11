using System.Collections.Generic;
using System.Text.Json;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#258) `JavaScriptSerializer` (System.Web.Extensions、net10 に存在しない) → `System.Text.Json` 移行の
    /// **挙動保存アダプター**。旧 serializer の振る舞いを保ち、各 call site の周辺コードを無改変にする。
    ///
    /// 旧 `JavaScriptSerializer` と揃えた点:
    /// - <see cref="Deserialize{T}"/>: **case-insensitive** property matching
    ///   (= camelCase wire ↔ PascalCase DTO 互換。System.Text.Json の default は case-sensitive のため明示)。
    /// - <see cref="DeserializeToObjectTree"/>: 旧 `DeserializeObject` / `Deserialize&lt;Dictionary&lt;string,object&gt;&gt;`
    ///   が返していた **動的オブジェクトツリー型** (object → <see cref="Dictionary{TKey,TValue}"/>、array → `object[]`、
    ///   string/bool/long/double/null) を再現。これにより既存の `As*` / `BuildFromDict` 等の動的走査コードが
    ///   System.Text.Json の `JsonElement` を意識せず無改変で動く。
    /// - <see cref="Serialize"/>: compact 出力 (旧 `Serialize` と同じく非整形)。匿名型 (camelCase) / DTO (PascalCase) の
    ///   property 名はそのまま wire に出るため wire format 不変。
    /// </summary>
    internal static class JsonCompat
    {
        // 旧 JavaScriptSerializer は property 名を case-insensitive に matching していた (camelCase wire を
        // PascalCase DTO が受理できた根拠) ため、System.Text.Json でも明示的に再現する。
        private static readonly JsonSerializerOptions DeserializeOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>型付き DTO への deserialize (case-insensitive、旧 <c>JavaScriptSerializer.Deserialize&lt;T&gt;</c> 互換)。</summary>
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, DeserializeOptions);
        }

        /// <summary>オブジェクトを compact JSON 文字列へ serialize (旧 <c>JavaScriptSerializer.Serialize</c> 互換)。</summary>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value);
        }

        /// <summary>
        /// JSON を動的オブジェクトツリーに変換 (旧 <c>JavaScriptSerializer.DeserializeObject</c> /
        /// <c>Deserialize&lt;Dictionary&lt;string,object&gt;&gt;</c> 互換)。object → <see cref="Dictionary{TKey,TValue}"/>、
        /// array → <c>object[]</c>、その他は string / bool / long / double / null。
        /// </summary>
        public static object DeserializeToObjectTree(string json)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                return ConvertElement(doc.RootElement);
            }
        }

        private static object ConvertElement(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in el.EnumerateObject())
                    {
                        dict[prop.Name] = ConvertElement(prop.Value);
                    }
                    return dict;
                case JsonValueKind.Array:
                    // 旧 JavaScriptSerializer は array を object[] で返す (consumer の `as object[]` /
                    // IEnumerable 走査が前提)。
                    var list = new List<object>();
                    foreach (var item in el.EnumerateArray())
                    {
                        list.Add(ConvertElement(item));
                    }
                    return list.ToArray();
                case JsonValueKind.String:
                    return el.GetString();
                case JsonValueKind.Number:
                    // 旧 JavaScriptSerializer は整数を Int32/Int64、小数を Decimal で返した。consumer (`AsLong`) は
                    // `is long` / `is int` 両対応 + その他 path は `.ToString()` 経由で読むため、整数=long /
                    // 非整数=double で互換が成立する。
                    long l;
                    if (el.TryGetInt64(out l)) return l;
                    return el.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    // Null / Undefined
                    return null;
            }
        }
    }
}
