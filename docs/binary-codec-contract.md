# Binary NBT 與 logical interchange

`Next.Formats.BinaryNbt` 目前提供 Java named-root、big-endian、未壓縮 NBT 的完整讀寫與量測。root 可為任意非 End tag；整份文件必須消耗至 EOF，尾隨資料拒絕。buffer 與 Stream 共用一個 decoder，支援短讀且不關閉 caller stream。傳輸 I/O 例外原樣回報；格式錯誤使用 `CodecException.Code/Offset`，offset 自本次讀取起算。對 seekable stream，呼叫端須讓 Position 至 Length 為完整文件。

Writer 先以同一 traversal 量測／驗證，再寫 caller stream；型別或容量錯誤不寫 prefix。I/O 失敗可能留下部分輸出，因此儲存層仍必須使用獨立 candidate，不能直接覆寫來源。`MeasureDocument` 不產生 bytes；`Validate` 重用完整 reader，沒有另一份 skip grammar。

## 型別與限制

三種 arrays、整數寬度、浮點 raw bits（含 NaN payload／負零）、空 list 的 element type、compound entry 順序及重複／空 key 均保留。MUTF-8 writer 使用 UTF-16 code units，不合併 surrogate 或做 normalization。reader 依 Java `readUTF` 演算法接受 raw NUL 與該演算法可解讀的非最短編碼；4-byte UTF-8、錯誤 continuation 與缺尾拒絕。因此非 canonical MUTF-8 的重寫會正規化編碼 bytes，但不改字串 code units。[Java DataInput](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/io/DataInput.html)

一次來源共用累積預算，不在子樹重設。`DecodeLimits` 只能收緊下列 hard caps；超限拒絕，不當作空資料。長度／最小 payload bytes／node 數先檢查才配置；深度以 root=0 計。Metrics 的 nodes 包含 root 及每個 tag payload；entries 是 compound members；elements 是所有 list／array elements；string bytes 是含 root／key 的 encoded MUTF-8 payload，不含兩 byte 長度。

| 預算 | 上限 |
| --- | ---: |
| decoded bytes | 134,217,728 |
| depth | 256 |
| nodes | 2,000,000 |
| compound entries | 1,000,000 |
| collection elements | 1,000,000 |
| cumulative encoded string bytes | 8,388,608 |
| single encoded MUTF-8 string bytes | 65,535 |
| compressed bytes | 67,108,864；由後續壓縮層執行，raw API 不收 compressed input |

`TypedNbtJson` 是 `nbtcli.next.typed-nbt` version 1，具有明示 `name/root`，重用既有 typed value converter；Display JSON 不可匯入。Long 用十進位字串，Float/Double 用固定長度 hex bits，孤立 surrogate 用 lossless UTF-16 表示。讀寫都驗 binary limits；JSON 另限 64 MiB、1,000,000 tokens、JSON depth 1024、單 typed payload 100,000 nodes／depth 256。因此 interchange 支援範圍是兩組預算的交集，較大的 binary 文件仍可由 binary API 處理。無效 JSON 使用 JsonException，未知 family/version 使用 NotSupportedException。

## Minecraft logical adapter

`MinecraftNbtAdapter` 位於 Next.Minecraft，以獨立的遞迴 `LogicalMinecraftDocument/Value` 轉換；Formats 不猜測遊戲語義。CapabilityProfile 的 Lists 必須明示 Homogeneous 或空 key CompoundWrapper；Unknown／其他 wrapper mapping 拒絕。此 adapter 只實作 list encoding，不替 layout、text、item、command、write-world 等其他 facet 宣告支援。

1. Homogeneous mapping 原樣保留 list elements；異型 logical list 回 UnsupportedProfile。
2. CompoundWrapper mapping：同型 scalar list 保留原 header；異型 list 使用 Compound header。只有 list element 是「恰一個空 key」的 compound 才解包一次。
3. 寫 Compound list 時，scalar 和合法的單空 key compound 都包成單空 key compound；後者必須多一層，才不會在讀取時誤失原 compound。含多個 entries（即使有空 key／重複空 key）的普通 compound 不解包。
4. 對巢狀 list 遞迴套用相同規則；compound 的普通欄位不當作 wrapper。空 list 記住原 element type。

這與 [Mojang 1.21.5 NBT changes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-5) 的格式不變及異型 list 例子一致；單空 key escape 規則另由官方 client 的 ListTag methods 實際驗證。logical encode 是該 profile 的 canonical representation：任意非 canonical physical wrappers 可能改 header／包裝層。保留原 physical tree 才是磁碟鑑識與未授權資料保真的來源；不得用 logical canonicalization 取代精確 patch 的範圍外保真檢查。重複 keys 只保留，不代表可安全定位修改。

## 依賴決策與獨立依據

本階段使用 BCL BinaryPrimitives、Stream、System.Text.Json；不新增 NuGet 依賴。比較候選 fNbt 固定於 [8715304](https://github.com/mstefarov/fNbt/tree/8715304372831b19786616384aaa1ea0ec136d65)：

| 評估面 | 查證結果與決策 |
| --- | --- |
| 授權 | [BSD-3-Clause](https://github.com/mstefarov/fNbt/blob/8715304372831b19786616384aaa1ea0ec136d65/docs/LICENSE.txt) 可使用；若採用須保留 notices，本次未納入其程式碼。 |
| 資源界線 | [NbtOptions](https://github.com/mstefarov/fNbt/blob/8715304372831b19786616384aaa1ea0ec136d65/fNbt/NbtOptions.cs) 提供 MaxAllocation，但明定單次 allocation、不是文件累積額度；仍需額外累積 ledger。 |
| 保型與 Java 相容 | [NbtStringCodec](https://github.com/mstefarov/fNbt/blob/8715304372831b19786616384aaa1ea0ec136d65/fNbt/Internal/NbtStringCodec.cs) 接受 standard UTF-8 與 MUTF-8 聯集；與本契約的 Java readUTF accept/reject 集合不同。[NbtCompound](https://github.com/mstefarov/fNbt/blob/8715304372831b19786616384aaa1ea0ec136d65/fNbt/Tags/NbtCompound.cs) 的讀取政策對 duplicate names 採拒絕或取最後值，無法原樣保存全部 entry 序列。 |
| 選擇 | 使用小型自有單一 grammar 與上述 BCL primitives；第三方無法直接符合三項必要契約，包裝後仍需補 parser／保型語義。沒有聲稱其他 library 全部不合格，也未做效能比較。SNBT／壓縮／LZ4 決策留各 owning 工作包。 |

獨立 fixtures／再生方式見 [oracles](../tests/oracles/README.md)。Java oracle 產生全部 tags 的 golden、MUTF-8 正反例及 65,535-byte 邊界；Mojang oracle 產生五種 list encoding bytes 並由官方 reader readback。一般測試只讀 frozen fixtures，不下載或依賴 Java／Minecraft。fixtures 僅含本專案選定的合成資料，不散佈官方 jars／mappings。
