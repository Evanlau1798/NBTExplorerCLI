# Corpus v1

本目錄只放專案撰寫的最小資料，沒有舊 production 程式碼或私人世界。`cases/` 的 JSON 保存具體輸入、profile、獨立預期與不變條件，覆蓋 F01–F20；目前是後續 Gate 的測例輸入，**不是測試已通過的證據**。

格式 bytes 與 SNBT 依規格判定，舊 probes 只提供觸發條件；G02 還需執行 Java MUTF-8、獨立 zlib／NBT oracle。未知 compression、macro、junction、混型 logical list 及損壞現檔還原不因本資料存在而宣稱支援。

`legacy-user-data/` 是刻意包含衝突／未知內容的合成資料，不是完整有效的真實世界或備份。session 的重複 ID 與缺 dimension 應成為衝突；pending/reply 的未知格式也必須原件保留。`scan-index.bin` 明確是文字 sentinel，不是可讀的 MemoryPack 檔。backup 缺件、journal 未識別，均不可當成可信恢復依據。

資料分類及逐檔 hash 見 `manifest.json`、`user-data-classification.json`；新 importer 必須保留原始 bytes／origin hash，不可整包刪除 `.nbtcli_cache`。

舊 GoldenTestWorld 的 139 份原件僅保存 hash manifest 於 `docs/baselines/golden-worlds.json`，本機 pristine 副本位於忽略的 `tmp/corpus/C-G`。授權尚未建立，不重新散佈世界；其 DataVersion、chunk 與完整 slot 集合待 G02/G03 獨立核對。它是保留的歷史資料，不是已獨立裁決的 expected output。所有寫入測試必須先建立另外的可丟棄副本。

S/M/L 的具體 workload 與資源預算由 G00-C06 凍結；測例 runner 及 fixture-root 解析由 G00-C05 提供。後續更動 fixture 必須更新 manifest hash，不能沿用舊驗收。
