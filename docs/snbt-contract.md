# SNBT syntax 與精確修改

`Next.Formats.Snbt` 使用 G01 的 immutable typed／logical value 模型，提供 `ParseDocument`、`ParseFragment`、`Write` 與 `Replace`。輸入／輸出是嚴格 UTF-8 bytes；unknown enum profile 回 `UnsupportedGrammar`，不猜測新版 grammar。這是語法核心，不是 Minecraft command discovery 或世界寫入服務。

## 解析與位置

`ParseDocument` 接受單一任意 root value 與前後 whitespace，必須消耗整份輸入，否則 TrailingInput。文件開頭可有 UTF-8 BOM。`ParseFragment` 接受完整外層 bytes 與明確 `startByte`；不要求後面 EOF，回傳實際 `ConsumedSpan`。start 必須在 UTF-8 邊界，負值／越界為 argument error。兩者共用同一 reader。

AST node 有 decoded logical value、原始 value byte span、children 的 typed key／index 與 key span。所有 span／diagnostic offset 為原 source 的 UTF-8 byte 座標，不是 UTF-16 char index。Root span 不包括外層 whitespace；ConsumedSpan 包括從入口到解析結束的實際消耗。Source、value 與 children 都是不可變 snapshot；SourceHash 綁定整份外層 bytes，可供後續 SyntaxLocator 的 whole-artifact preimage 使用。

Compound 保留原 key 順序與重複 entry，不沿用官方 Map 的最後值覆蓋。重複 key 的 Resolve 回 Ambiguous，不能安全修改該歧義路徑；能鑑識顯示不代表可寫世界。一般 type/value/keep 沒有特殊語義。Namespace keys 須加引號。合法 binary 空 key 不等於合法 Java SNBT key，兩個支援 profile 均拒絕空 key。

## 明示 grammar

| profile | 語義 |
| --- | --- |
| JavaLegacy | 以官方 1.21.4 TagParser 為固定依據：同型 list、精確 B/I/L array 元素型別、b/s/l/f/d suffix、布林、quoted escape 只接受當前 quote 與 backslash。01、01b、1e3、整數 overflow 等依原 grammar 為字串；1.e3 為 double。 |
| Java1215 | 1.21.5 數字 grammar，含 hex/binary、underscores、signed/unsigned suffix、range checks、部分 atom 間 whitespace；異型 lists、typed array 的 contextual inference／整數 widening、擴充 escapes、bool／uuid。非法 numeric start 不 fallback 成字串。 |

新版規則依 [Mojang 1.21.5](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-5)，細部接受集合另以官方 artifact oracle 固定。0b 可代表 Byte(0)，0b1 則是 binary Int(1)；不能把所有 0b 前綴都當成 binary 數字。Array child value 是 promoted 有效型別，例如 `[I;1b]` 的 index 0 為 Int(1)，仍保存原 `1b` span。新版 typed arrays 僅接受數字字面值，不因 bool 的結果是 Byte 就接受 `[B;true]`。

`bool(value)` 依官方 NBT Ops 先呼叫 Number.byteValue，再以窄化後的 byte 是否非零判定；因此 bool(0.5) 與 bool(256) 均為 false。浮點先向零截斷／飽和至 Java int 範圍再窄化為 byte。`uuid(string)` 保留 Java UUID.fromString 的短群組／mask 與 BMP Character.digit 十六進位規則（含阿拉伯、全形數字及全形 A–F），拒絕無效群組及額外空白。這些 conversion 的參數不是輸出 value 的可直接定位子節點；例如 uuid(...) 是 atomic syntax value，替換產生的 array 需選整個 node，不捏造內部 index 的原文 span。

Named Unicode escapes 固定為 Java 21 的 Unicode 15.0 名稱集合；不依 OS／locale 或機器是否安裝 Java。資料由 JDK 21.0.8 `Character.getName/codePointOf` 生成並逐項 roundtrip 驗證，288,767 項，以 compressed resource 延遲載入。這也保留 Java 對未明列名稱所用的 block-plus-hex 名稱，而非自行推導 Hangul 別名。未知名稱回 InvalidEscape；後續 JDK 增加的名稱不自動納入此 profile。[Java Character](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Character.html#codePointOf(java.lang.String))；資料授權附於 [Unicode notice](licenses/Unicode.md)，並隨 output 複製至 licenses/。

## 序列化與 patch

Writer 使用 invariant culture，保留可表示的 scalar widths、array kinds 及 finite float/double 值；字串／key 一律加引號。兩版官方 Tag factory 都將解析的正負零映射至正零，reader 亦採該正式 typed value，原字面值仍由 span 保存。Raw 負零、NaN payload、非 End 空 list header 沒有官方可逆 SNBT 表示，writer 回 UnsupportedValue；binary／typed interchange 仍保留這些 bits／型別。新版使用 Unicode escapes 保留孤立 surrogate；legacy 因其 grammar 無此表示而回 UnsupportedValue。Legacy Infinity 使用可解析的 overflow-exponent 文字；新版拒絕非有限數字。不以改成其他型別來掩蓋無法表示的值。

`Replace(document, path, expected, replacement)` 必須找到唯一現有 node 且 expected 完全符合，才序列化 replacement。它只 splice 該 byte span，保留 prefix／suffix（含命令其他 components、quote 樣式、空白與 newline）原 bytes；不做全文 Replace。然後重解析候選，確認整棵 logical tree 等於精確替換的預期結果，並確認 root 消耗邊界符合長度 delta。混型 legacy list、array narrowing、詞彙黏合或越界語義變化一律拒絕，不回傳半成品。此純函式不寫來源檔；持久化仍須後續 candidate／commit ports。

## 資源與依賴

沿用只能收緊的 DecodeLimits：source/output bytes、depth、nodes、compound entries、collection elements、decoded MUTF-8 string bytes／單字串 bytes。SNBT node count 包含 array literals 的元素與 conversion arguments；數字 token 不消耗 decoded-string quota，但有 65,535 char 的固定 lexer ceiling。長度／count 在增長前驗證，checked 算術保護候選長度。CompressedBytes 不是此 API 的輸入契約；內嵌 Unicode resource 是可信資料，不代表已實作外部 GZip 容器驗收。

每個 case 由現有 worker deadline 保護。Malformed-input tests 含完整 container 的所有 proper prefixes、512 個固定 seed 輸入與 128 個 typed tree roundtrips；獨立 oracle 包含兩版各44例、新版63個邊界與兩版各36個審查反例，共223例，涵蓋 accept/reject、typed binary value、消耗範圍及 writer roundtrip。產生方式見 [oracles](../tests/oracles/README.md)。一般測試與產品均不需要 Java／網路。

沿用 binary ADR 的 fNbt 固定 revision `8715304372831b19786616384aaa1ea0ec136d65`：其 [SnbtParser](https://github.com/mstefarov/fNbt/blob/8715304372831b19786616384aaa1ea0ec136d65/fNbt/Internal/SnbtParser.cs) 明示放寬現代 grammar、接受舊版 fallback、Infinity／NaN suffix 與空 key，無法直接提供本契約的兩個精確接受集合；其 compound model／非累積 allocation policy 也不符合 entry／budget 需求。因此保留自有單一 reader／literal rules，搭配 BCL 的 UTF-8、數值及 resource decompression，不新增 NuGet，也未複製其程式碼。Unicode names 是資料相依，來源、SHA256 與完整 notices 均保留。
