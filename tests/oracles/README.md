# 獨立 codec fixtures

壓縮／Region 的獨立依據使用 CPython 3.13.14（zlib 版本保存在 fixture）：`python tests/oracles/compression_oracle.py` 與 `python tests/oracles/region_oracle.py` 分別輸出 `compression-oracle.json`／`region-oracle.json`；UTF-8 無 BOM、LF 保存並更新 manifest。它們只使用 Python gzip/zlib/struct 及固定 Java NBT bytes，不引用產品 assemblies。前者驗 framing／checksum／完整 EOF／多 member／非法 Huffman 與 window；後者獨立建立 index 與已知／opaque payload。`python tests/oracles/region_oracle.py <candidate-path>` 可獨立檢視合成候選的 descriptor／payload hashes，不能取代 NBT typed 驗證。一般 Verification 不需要 Python。

`python -B tests/oracles/dat_slice_oracle.py` 產生固定 `codec/dat-slice-oracle.json`：獨立 struct/gzip 建立的 DAT 原件，以及 LiteralTranslation／Int→Long TypedEdit 兩份明確 raw postimages。只含專案合成資料，MUTF-8 字串刻意限定無 NUL 的 BMP literals，其他 code units 由既有 Java oracle 負責。storage suite 比較持久化候選解壓後的完整 bytes，而不只由新 reader 自讀 writer。

`python -B tests/oracles/zip_boundary_oracle.py` 產生 `codec/zip-boundary-oracle.json`，涵蓋正常stored/Deflate、已解出完整abc但未達Deflate EOF、trailing bytes與小型entry/local/extra ZIP64。Python zipfile皆能取abc，另以raw zlib的eof/unused_data確認完整framing差異；本profile刻意不支援ZIP64，因此明確Unsupported，並非宣稱合法ZIP64損壞。

這些是 fixture 生成器，並非產品依賴。一般 `codec` suite 只讀固定 JSON；無須 Java、網路或遊戲。已驗證的產生環境為 JDK 25.0.2，runtime `25.0.2+10-LTS-69`。所有輸入是本專案合成資料；不啟動 Minecraft/server、不讀世界。生成後須核對 manifest；不能藉再生接受未調查的差異。

從 repo 根執行：

```powershell
java tests/oracles/JavaNbtOracle.java
java -cp 'tmp/codec-oracle/*' tests/oracles/MinecraftListOracle.java
```

stdout 是完整 fixture JSON；stderr 的 SLF4J 無 provider 訊息只表示沒有 logger backend。第一份產物為 `fixtures/codec/java-oracle.json`；第二份為 `minecraft-list-oracle.json`。以 UTF-8 無 BOM 保存 stdout。

Mojang oracle 使用官方 1.21.5 client 的 ListTag writer／完整 reader 與 private unwrap method，依官方 mappings 定位 obfuscated symbols（ug=ListTag、ua=CompoundTag、uf=IntTag、uy=StringTag、va=Tag、uj=NbtAccounter）。版本不符不可沿用這些名稱。未修改官方 class，也未納入產品或提交 jars。

| artifact | 官方 URL／SHA1 |
| --- | --- |
| client | https://piston-data.mojang.com/v1/objects/b88808bbb3da8d9f453694b5d8f74a3396f1a533/client.jar |
| client mappings | https://piston-data.mojang.com/v1/objects/57669731d542f98646772e91a0d68628f9827a5c/client.txt |

URL 中的 object hash 即 SHA1，下載至 ignored `tmp/codec-oracle/` 後已核對。官方版本 metadata 從 https://piston-meta.mojang.com/mc/game/version_manifest_v2.json 選 id `1.21.5` 的 URL 取得；以下 classpath 依賴也從該 metadata 的 downloads.artifact URL 下載，並核對其 SHA1：

| dependency | SHA1 |
| --- | --- |
| gson 2.11.0 | 527175ca6d81050b53bdd4c457a6d6e017626b0e |
| failureaccess 1.0.2 | c4a06a64e650562f30b7bf9aaec1bfed43aca12b |
| guava 33.3.1-jre | 852f8b363da0111e819460021ca693cacca3e8db |
| brigadier 1.3.10 | d15b53a14cf20fdcaa98f731af5dda654452c010 |
| datafixerupper 8.0.16 | 67d4de6d7f95d89bcf5862995fb854ebaec02a34 |
| fastutil 8.5.15 | 1e885b40c9563ab0d3899b871fd0b30e958705dc |
| commons-lang3 3.17.0 | b17d2136f0460dcc0d2016ceefca8723bdf4ee70 |
| slf4j-api 2.0.16 | 0172931663a09a1fa515567af5fbef00897d3c04 |
| logging 1.5.10 | 9ab1202793717af9df9c1704d0a02892067001eb |

檢視格式實作使用 `javap -c -p ug`，只讀官方 bytecode；不將反編譯內容帶入 source。fixtures 的 SHA256／byte size 由 `fixtures/manifest.json` 固定。

## SNBT grammar oracle

`MinecraftSnbtOracle.java` 各以官方 1.21.4／1.21.5 TagParser 執行相同 44 個合成字面值，輸出接受／拒絕、consumed bytes 及官方 named NBT bytes。Legacy 使用 uo.readValue 後明確檢查 EOF；modern 使用 vb 的 NBT_OPS_PARSER.parseAsArgument 後同樣檢查 EOF。無啟動遊戲／伺服器。duplicate key 的原語法順序保留是本專案額外鑑識契約，不能拿官方 Map 的最後值覆蓋來抵充。

1.21.4 client SHA1 `a7e5a6024bfd3cd614625aa05629adf760020304`，mappings SHA1 `0cf2a0b7f056da1a5a5dd99fc6dc752f33987150`，URL 同為官方 `https://piston-data.mojang.com/v1/objects/<sha1>/client.jar`／`client.txt`。存於 `tmp/codec-oracle/legacy/`；勿將兩版 client 放同一 classpath。額外 Java dependencies 也從 1.21.5 官方 metadata URL 下載且已核對 SHA1：

| dependency | SHA1 |
| --- | --- |
| authlib 6.0.58 | 9261a6d53e629469fab20136b968bf5202d9c0f7 |
| joml 1.10.8 | fc0a71dad90a2cf41d82a76156a0e700af8e4f8d |
| netty-buffer 4.1.118.Final | 7022990af1e0d449f9d5322035899745e19735c5 |
| netty-codec 4.1.118.Final | 307f665c08ce57333121de4f460479fc0c3c94d4 |
| netty-common 4.1.118.Final | 4bb0f9899146484fa89f7b9bc27389d5b8e2ecde |
| netty-handler 4.1.118.Final | 30ebb05b6b0fb071dbfcf713017c4a767a97bb9b |
| netty-resolver 4.1.118.Final | 28c378c19c1779eca1104b400452627f3ebc4aea |
| netty-transport 4.1.118.Final | 5a27232e5d08218722d94ca14f0b1b4576e7711c |

```powershell
$snbtLibraries = @(Get-ChildItem tmp/codec-oracle -Filter '*.jar' | Where-Object Name -notlike 'client*' | ForEach-Object FullName)
# 1.21.4 的 client 改用 tmp/codec-oracle/legacy/client-1.21.4.jar，最後參數改 1.21.4。
$snbtClasspath = (Join-Path $PWD 'tmp/codec-oracle/client-1.21.5.jar') + ';' + ($snbtLibraries -join ';')
java -cp $snbtClasspath tests/oracles/MinecraftSnbtOracle.java 1.21.5
```

Legacy oracle 接受 24／拒絕 20，modern 接受 29／拒絕 15。fixtures 均 UTF-8、LF 結尾；只保存 stdout，不能把 SLF4J stderr 寫進 JSON。

新增的 `MinecraftSnbtOracle.java 1.21.5 edges` 產生 63 個邊界到 `snbt-1.21.5-edges-oracle.json`，原兩份 44 例 fixture 不變。涵蓋 suffix／內部 whitespace、0b 歧義、範圍、array context、Unicode names、Java UUID 短群組等；須以新的候選明確審查差異，不能用產品 reader 改寫 expected。

`MinecraftSnbtOracle.java <version> findings` 各產生36個 bool byte narrowing／負零／Unicode UUID 回歸到 `snbt-<version>-findings-oracle.json`。兩版官方 Tag factory 將所有零 signs 映射正零；這些例子驗 reader 的正式值，不能以新 writer 自讀回宣稱負零可逆。對 raw negative-zero writer 的 UnsupportedValue 另有直接負例。既有44／63例不改寫；新資料與大小／hash 一併納入 manifest。

## 固定的 named Unicode data

```powershell
& 'C:/Program Files/Java/jdk-21/bin/java.exe' tests/oracles/JavaUnicodeNames.java
```

輸出存至 `src/Next.Formats/Unicode/java21-names.json`，UTF-8／LF。使用 Java 21.0.8 runtime `21.0.8+12-LTS-250`、Unicode15.0，共 288,767 個 getName／codePointOf 可逆的 entries。gzip 內文為 `hex-codepoint<TAB>name<LF>`；SHA256 `7820d9a7ab31fa78e4241d89d48e1a41df8fa906456062b250cef6d82466822d`。整份 JSON 2,143,040 bytes，SHA256 `660194030d42081ab3b1e22468630d8b848c59c160494f7da9f4585b6bc3f173`。不要把大型 Base64 印入診斷或 review；只核對 metadata、digest 與再生結果。

產品內嵌資料、按需載入，不呼叫 JDK。Unicode 授權完整保留在 `docs/licenses/Unicode.md`（原 JDK21 legal/java.base/unicode.md，僅統一 LF），並複製到產品輸出。JDK／Mojang 程式及 jars 均未納入 source 或產品依賴；只有本專案生成器與合成 oracle 資料。
