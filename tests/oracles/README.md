# 獨立 codec fixtures

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
