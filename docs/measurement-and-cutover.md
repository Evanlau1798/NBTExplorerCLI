# 量測、差異裁決與候選切換契約

政策版本 1；PlanVersion 0.3／GateRevision 2。這是 G00 的預先量測及切換政策，不是性能、Minecraft 載入或發行通過聲明。

## 固定輸入與量測

[budgets.json](../benchmarks/budgets.json) 固定初始 hard limits、各 corpus RSS 上限、樣本及回歸門檻。硬體／OS／SDK 基線在 [environment.json](baselines/environment.json)；目前使用同一台 Windows x64、同一 G: NTFS 工作卷、同一 SDK 10.0.404。Windows storage API 無法將 G: 對應至 MSFT_Partition，故不推定其實體磁碟類型；換環境須重建可比 control，不能套用另一顆磁碟的吞吐量。

S/M/L 的 recipe、來源集合、bytes、slots 與 manifest hash 記於 [corpus.json](../benchmarks/corpus.json)。使用新 benchmarks 工具的 `generate-corpus S|M|L <new-output-root>` 重建；既有根一律拒絕覆寫。manifest 不含機器路徑、時間或隨機值，固定相對路徑、profile、每檔 hash 及預期 slots。資料為專案撰寫，可重建、可追溯。

這三組是 **LangKey 容量與多包／多維度來源** fixtures：含兩個空 region，完整 slot 集合由每檔 locale、key、原文公式決定。它們不是可直接載入遊戲的完整世界，也不代替 C-G 的既有世界、其他格式 oracle、所有內容角色或 crash corpus。resource pack 格式 34 依 [Minecraft 1.21 官方技術變更](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21)。

`fixtures/cases/` 保留 F01–F20 的最小正反例；C-G 的 139 檔 raw byte baseline 與 pristine 副本已固定，DataVersion、chunk 與 slot 真值由 G02/G03 獨立裁決後才用於正確性／完整世界量測。其未建立語義真值的狀態不能當成 pass。G06 必須凍結完整必需工作流的正確串行 control；G07 再按相同語義工作量比較，不能只靠 LangKey corpus 關閉整個性能 Gate。

G06 的 [正確性 control](../benchmarks/control.json) 固定已接受的 runtime 來源、串行 scan 設定、公開工作流 assertion 與 canonical 投影。原 S/M/L recipe 沒有 world metadata，不能直接冒充成功的公開世界 scan；S control 在副本加入 [118-byte metadata](../fixtures/control/level-java121.dat)，原 corpus 與 hashes 保持。控制來源為已知 3953 profile，三個明列維度；7 sources／256 slots、完整 diagnostics 及逐 key 原文公式獨立核對。Full／warm 的原 root identity 與 portable 投影分別比較，不刪錯誤或減少工作量。完整流程使用能力帳本中的公開情境，不把 LangKey 控制當成所有格式驗收。

計時分 process cold、warm、verified incremental、少量／大量 apply、session import、pack、backup、recovery。OS file cache 未控制時明記；不宣稱清空。記錄全部 samples、wall／CPU、read／decoded／write bytes、files／chunks／slots、unsupported／error、RSS、buffer peaks、GC 與累積 allocation。語義結果／來源診斷／coverage／完整性先相同，execution telemetry 另核對。初始樣本只報 median、觀測最大值，不報可靠 p95。

256 MiB 是全域 buffer lease 預算，不等於 RSS；每輸入、metadata、session、plan 各有獨立上限。L 的 524,288 slots 不承諾能塞進單一 100,000-slot session，必須使用明示 scope／容量結果，不能靜默丟槽。過大單輸入、取消與慢消費端均須有明確結果；實作及負載驗收留給各 owning Gate。

任何調整需先版本化並在受影響比較前凍結；看過失敗再提高上限不能通過。沒有正確 control 時不報加速倍率；本機舊版速度與歷史 96/99 結果都不是新版正確性或速度分母。工期只依實際工作包、外部等待及返工更新，不預填日期。

## 差異裁決

[differences.json](baselines/differences.json) 保存 owner、fixture／oracle 與決策。分類限 CorrectParity、ExpectedFix、IntentionalBreakingChange、UnsupportedByDeclaredProfile、Regression。已確認舊缺陷使用 ExpectedFix，但仍待 owning Gate 的新版回歸與適用公開入口驗證，不能直接關閉 Fxx。

每個觀察到的新增／消失／改變結果都要裁決；尚未裁決者阻擋相關驗收。合法序列化差異以格式／型別與精確授權 projection 判斷，不以舊 reader 自證。Regression 阻擋 Gate；不得靠改 expected、刪能力、忽略失敗或稱 Unsupported 規避必需 profile。

## 候選、資料隔離與切換

候選名為 `nbtcli-next`、本機版本 `2.0.0-alpha.1`；正式版號及歷史在發行前核實，不覆蓋舊 binary 或 PATH。新狀態根 `%LOCALAPPDATA%/nbtcli-next`、temp 下的 `nbtcli-next` 與舊工具資料分離。cache 只有已識別可重建索引可重建；session／reply／譯文／notes／glossary／問卷／設定／journal／backup 不整包清除。

新舊工具與 Minecraft 不同時寫同一世界。先確認世界離線、舊作業停止、沒有未解 journal，保存世界／設定／session／備份 manifest，再於副本完成指定 profile 的完整流程與實際遊戲載入。新 lock 不代表舊 writer 或遊戲遵守；提交前仍須比對來源 preimage。正式切換依 G08 簽收及適用操作授權執行，本政策本身不切換。

切回舊 executable 不等於資料已回退。使用已驗證 backup／journal 恢復一致前像，或有該資料／profile 的明確降版相容證據，才允許舊 writer 接手。現檔既非 preimage 亦非 postimage 時回 Conflict，保留外部改動；不先刪世界、不盲覆備份、不回退呼叫舊 writer。
