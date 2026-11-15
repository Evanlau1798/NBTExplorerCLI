# NBTExplorerCLI 新版章程

PlanVersion: 0.2；GateRevision: 2；範圍凍結日期：2026-11-15。

依使用者 2026-11-15 的明確指示，新版直接建立於 `G:\NBTExplorerCLI`，不另建 sibling 根目錄。`plan/` 是本機企劃及驗收清單，`.old/` 是既有專案的唯讀參考；兩者不進新版 Git 提交或產品依賴。不得修改、清理或提交舊工作樹。

## 版本與交付範圍

舊 project 宣告 1.0.0；本機舊 Git 沒有 tag 或 remote。新版以 `2.0.0-alpha.1` 作本機開發候選版本，不宣稱已正式發行。正式發行前仍須核對實際發布歷史；取得相衝證據時調整候選版本及遷移文件。

目標為 Windows x64、離線 Minecraft Java 世界、檔案式人類／外部 agent 翻譯。必需能力為 [能力帳本](capabilities.md) 的 CAP01–CAP28；保留通用 typed NBT 編輯、batch、全部 translate 工作子命令、agent 問卷、設定及人工知識。未實作的能力維持 PLANNED，不以空結果冒充完成。

非本期項目：GUI/TUI、Bedrock、運行中世界即時修改、雲端服務、模型 API／付款整合、plugin host、自訂網路封包。

## 新核心責任

| 專案 | 責任 | 允許的內部依賴 |
| --- | --- | --- |
| Next.Domain | typed tree、來源、slots、operations、receipts | 無 |
| Next.Formats | bounded NBT/SNBT、壓縮、region | Domain |
| Next.Minecraft | profiles、logical values、文字及命令規則 | Domain、Formats |
| Next.Application | use cases、I/O ports、plan/apply/recovery | Domain、Formats、Minecraft |
| Next.Infrastructure | 檔案、cache、session、journal、備份 ports | Application 及其依賴 |
| Next.Cli | composition root、參數、UTF-8 JSON、help | 上述新版專案 |
| Verification / Benchmarks | 契約、oracle、integration、資源量測 | 新版專案；不進產品發布包 |

production 不引用舊 project、DLL、source、scanner/writer，不啟動或 fallback 至舊 CLI。新 codec、流程與測試重新實作；只沿用經核對的需求、格式知識與獨立 fixture。Domain 不讀檔、不讀 Console、不依賴序列化套件。

## 需求與既有 finding 歸屬

單一整合 owner 為本專案維護者（目前由 Codex 實作）；各 gate 保留獨立驗證及 fresh reviewer。owner 是責任而非預先假定多位代理並行。

| IDs | 負責 Gate |
| --- | --- |
| R01 | G02 |
| R02 | G01 |
| R03、R04 | G03 |
| R05 | G04 |
| R06、R12 | G05 |
| R07 | G00、G06 |
| R08 | G07 |
| R09 | G01、G02、G03、G08 |
| R10 | G00、G08 |
| R11 | G06 |
| F01、F02、F03 | G02 |
| F04 | G01、G05 |
| F05 | G01、G03 |
| F06 | G03、G05 |
| F07、F08 | G03 |
| F09 | G04 |
| F10 | G05 |
| F11 | G04、G05 |
| F12、F13 | G05、G06 |
| F14 | G00、G07 |
| F15 | G03、G08 |
| F16 | G00、G06 |
| F17 | G01、G03 |
| F18 | G01、G02 |
| F19 | G02、G05 |
| F20 | G05、G08 |

finding 關閉須有新版測例及適用公開入口證據；本章程沒有關閉任何 finding。

## 首版必需 profiles

必需 corpus 涵蓋既有 1.21 與 26.1 世界副本；世界名稱僅為樣本標籤，實際 DataVersion 必須於 G02/G03 讀取並登錄。另含 1.20.5 item components、1.21.5 NBT text／logical heterogeneous wrappers 的獨立規格 fixtures。

布局、text、item、command/SNBT grammar、pack metadata、compression 與操作能力分開驗收。DAT、region/entities、世界 data、datapack ZIP／目錄、mcfunction、resource pack ZIP／目錄都在必需範圍。Raw/gzip/zlib 必需；LZ4、external chunks、自訂 codec 在格式 fixture 通過前明示 Unsupported，不能當成空來源，也不能寫未知 payload。

## 相容與切換

保留已盤點的公開命令與 aliases，轉成新版 request；開發用 core-test/test/debug-scan 移至 Verification。新 executable 為 `nbtcli-next`，不替換現有 PATH。UI 使用 `--ui-locale`；舊全域 `--lang` 明確改為 UI 參數，convert 的翻譯語言不得被全域消耗。新 schema family 首版為 1，舊 JSON 經顯式 importer，不猜格式。

世界／pack／lang／ArtifactRestore 共用唯一 PatchPlan／ApplyEngine。工具狀態使用自己的 atomic/CAS repository。掃描不改世界；提交只在離線、來源 preimage 符合時執行。新鎖不保證舊 writer 或遊戲遵守。

新舊 writer 不同時寫同一世界。候選版只對可丟棄副本驗證；正式切換及 PATH 修改留到 G08。回退須驗證 journal／backup 的資料前像，不能只換回舊 executable。所有原始世界、session、譯文、notes、glossary、問卷、設定及備份保持可追溯，不整包刪除 `.nbtcli_cache`。
