# Corpus v1

本目錄提供專案合成資料與格式 oracle。`cases/` 的 JSON 保存具體輸入、profile、獨立預期與不變條件，覆蓋 F01–F20；目前供驗證程式核對預期行為。

二進位格式與 SNBT 測例包含 Java MUTF-8、獨立 zlib／NBT oracle 及 Minecraft 語法範例。所有寫入測試均使用可丟棄的副本。

S/M/L 的 workload 與資源預算記錄在 benchmarks/。變更 fixture 時同步更新 manifest.json 內的檔案大小與 SHA-256。
