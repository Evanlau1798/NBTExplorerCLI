<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://nbtcli.evanlau1798.com/banner-dark.svg" />
    <source media="(prefers-color-scheme: light)" srcset="https://nbtcli.evanlau1798.com/banner-light.svg" />
    <img alt="NBTExplorerCLI — Minecraft Java Edition 專用 NBT 安全查閱、地圖翻譯與具備中斷復原能力之命令列工具" src="https://nbtcli.evanlau1798.com/banner-dark.svg" width="900" />
  </picture>

  <br />
  <br />

  [![Windows Candidate CI][windows-ci-badge]][windows-ci-url]
  [![Documentation Site CI][docs-ci-badge]][docs-ci-url]
  [![Release Version][release-badge]][releases-url]
  [![Target Platform][platform-badge]][releases-url]
  [![MIT License][license-badge]][license-url]

  <p>
    <b>專為 Minecraft Java Edition 世界設計的 Windows x64 獨立封裝命令列工具，提供 NBT 結構查閱、世界文字掃描、分頁式地圖翻譯、資源包生成，以及具備寫前備份與中斷復原日誌保護的安全離線編輯。</b>
  </p>

  <p>
    <a href="#核心亮點">核心亮點</a> |
    <a href="#快速開始">快速開始</a> |
    <a href="#核心工作流範例">工作流範例</a> |
    <a href="#命令速查表">命令速查表</a> |
    <a href="#二進位發行包驗證">發行包驗證</a> |
    <a href="https://nbtcli.evanlau1798.com/zh-tw/docs/">線上文檔</a>
  </p>

  <p>
    <a href="README.md">English</a> | <b>繁體中文</b> | <a href="https://nbtcli.evanlau1798.com/zh-tw/">工具介紹首頁</a> | <a href="https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md">Contributing (English)</a>
  </p>
</div>

---

## 核心亮點

- **單行可預測 JSON 輸出協定**：每次執行皆向標準輸出（`stdout`）寫入單行 UTF-8 JSON 物件（`schemaFamily: "nbtcli.cli"`、`schemaVersion: 1`），讓 PowerShell 腳本、CI 流程與 AI 輔助工具可直接解析結構化欄位，無須依賴脆弱的文字擷取。
- **安全唯讀 NBT 與區塊查閱**：支援使用標準 JSON Pointer（`--pointer`）或結構化路徑區段（`--segments`）讀取 `level.dat`、`.mca` 地形與實體區塊、資料包 JSON 與 ZIP 壓縮包，並支援易讀顯示模式（`display-json`）與完整保留 NBT 數值型別的可逆模式（`--types`）。
- **涵蓋 21 種來源的世界文字掃描**：自動索引並驗證告示牌、懸掛式告示牌、書本、物品、實體、容器、指令方塊、函數、計分板、Boss 血條、進度與語言檔中的文字，並於世界資料夾外維護可隨時重建的掃描快取。
- **分頁式冒險地圖翻譯工作流**：將大型地圖翻譯切分為可驗證的分頁與槽位（`translate`），內建術語表與譯者備註管理，並於確認世界寫入完成後封存工作階段（`seal`）。
- **資源包與翻譯鍵轉換管線**：支援將世界內的純文字轉為翻譯鍵（`convert` 或 `translate plan --keys`）、匯出獨立語言 JSON 檔（`export-lang`），或建立並與既有資源包安全合併（`create-pack`）。
- **具備中斷復原能力的安全離線編輯**：所有修改皆先產出不可變的預覽計畫（`nbtcli.patch-plan`）。離線寫入（`--world-offline`）時自動在世界目錄外保存寫前備份與交易日誌，若遇中斷可透過 `status` 與 `recover` 選擇繼續完成或完整回滾（`recover --rollback`）。

### 適用場景與設計邊界

**NBTExplorerCLI 非常適合用於：**

- **冒險地圖翻譯與本地化團隊**：將散落於數千個告示牌、書本、對話框與資料包的文字整理為結構化分頁，搭配術語表翻譯後直接寫入世界或打包為客戶端資源包。
- **技術型玩家、地圖作者與伺服器管理員**：在離線狀態下精確查閱或批次修改 `level.dat`、`.mca` 區塊、實體與資料包，並享有雜湊前置檢查與自動寫前備份保護。
- **PowerShell 自動化腳本與 AI Agent 整合**：透過嚴格驗證的 JSON Schema 與明確的結束代碼（`0` 至 `8` 及 `130`）串接自動化檢查與翻譯流程。

**NBTExplorerCLI 不適用於：**

- **遊戲執行期間的即時連線修改**：所有會修改存檔的命令皆要求先關閉 Minecraft 與其他編輯器（並加上 `--world-offline`），以防止區塊與存檔發生讀寫衝突。
- **3D 地形視覺化預覽或圖形化方塊編輯**：本工具為專注於資料精確度與自動化管線的命令列引擎，不包含 3D 視窗介面。
- **Minecraft 基岩版（Bedrock Edition）存檔**：資料格式與剖析器專為 **Minecraft Java Edition** 設計。請用 `nbtcli capabilities` 查詢各遊戲版本及操作的支援範圍。

---

## 快速開始

請從 [GitHub Releases][releases-url] 或通過驗證的 [GitHub Actions 候選建置][windows-ci-url] 下載 `nbtcli-win-x64.zip` 與 `SHA256SUMS.txt`。壓縮包已內建固定的 .NET 執行環境（`10.0.15`）與所需函式庫，解壓縮後請將所有 `.dll` 與 `nbtcli.exe` 保留在同一個資料夾內。

```powershell
# 1. 解壓縮前，先與 SHA256SUMS.txt 比對 ZIP 雜湊
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
# 確認雜湊一致後再解壓縮
Expand-Archive -Path .\nbtcli-win-x64.zip -DestinationPath .\nbtcli

# 2. 檢查執行檔版本與命令總覽
.\nbtcli\nbtcli.exe --version
.\nbtcli\nbtcli.exe --help

# 3. 在測試用世界副本上執行第一次唯讀查閱
.\nbtcli\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

```json
{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "get",
  "outcome": "Success",
  "data": {
    "representation": "display-json",
    "tagType": "Int",
    "value": 3953
  },
  "error": null
}
```

> [!IMPORTANT]
> 請務必先在測試用的世界副本（例如 `D:\WorldCopy`）上驗證操作流程；修改存檔前請先檢視預覽計畫，並確認關閉 Minecraft 與其他編輯工具後再加上 `--world-offline`。任何命令皆可加上 `--ui-locale zh-TW` 或 `--ui-locale en-US` 切換 CLI 訊息語言（不影響地圖翻譯語言設定）。

完整步驟請參閱線上文檔的 [安裝驗證與安全查閱][doc-getting-started]。

---

## 核心工作流範例

請在解壓縮後含有 `nbtcli.exe` 的資料夾內執行以下範例。

### 1. 唯讀查閱 NBT 節點、區塊與世界文字

在不改動任何存檔檔案的前提下，快速探索維度結構、區塊資料與可翻譯文字：

```powershell
# 列出世界宣告的維度清單與區塊檔數量
.\nbtcli.exe list 'D:\WorldCopy'

# 讀取節點並保留完整可逆的 NBT 型別標籤 (--types)
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName --types

# 掃描世界內全部 21 種文字來源並輸出統計摘要
.\nbtcli.exe scan 'D:\WorldCopy' --summary

# 抽取去重後的世界文字與來源座標至既有的外部目錄
New-Item -ItemType Directory -Path 'D:\Exports' -Force | Out-Null
.\nbtcli.exe extract 'D:\WorldCopy' --unique --output 'D:\Exports\world-text.json'
```

詳見 [安裝驗證與安全查閱指南][doc-getting-started]。

### 2. 預覽編輯計畫、離線套用與中斷復原

所有修改皆遵循「先預覽計畫、後離線寫入」的安全保護機制：

```powershell
# 步驟 1：產生不可變的修改預覽計畫（完全不改動世界存檔）
New-Item -ItemType Directory -Path 'D:\Plans' -Force | Out-Null
$read = .\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName --types | ConvertFrom-Json
if (-not $read.success) { throw $read.error.message }
$replacement = $read.data.value
$replacement.data.root.value = 'Translated Map Name'
$replacement | ConvertTo-Json -Depth 50 | Set-Content -Encoding utf8 'D:\Plans\new-level-name.json'
.\nbtcli.exe set 'D:\WorldCopy' `
  --path level.dat `
  --pointer /Data/LevelName `
  --value-file 'D:\Plans\new-level-name.json' `
  --output 'D:\Plans\level-name-plan.json'

# 步驟 2：確認已關閉 Minecraft 後，離線套用計畫並驗證讀回雜湊
.\nbtcli.exe apply 'D:\WorldCopy' `
  --plan 'D:\Plans\level-name-plan.json' `
  --world-offline `
  --output 'D:\Plans\level-name-receipt.json'
```

寫入中斷或收到 `RECOVERY_REQUIRED`（退出碼 `7`）時，請保持 Minecraft 關閉，用相同的世界路徑、計畫雜湊及 state root 執行 `status`。確認日誌並核對復原指引後，再執行 `recover`。

詳見 [安全編輯與中斷復原指南][doc-safe-editing]。

### 3. 分頁式冒險地圖翻譯工作流

將大型地圖文字切分為可追蹤進度的分頁，完成翻譯後寫回世界或匯出資源包：

```powershell
$session = .\nbtcli.exe translate start 'D:\WorldCopy' `
  --source-language en_us `
  --target-language zh_tw `
  --page-size 30 | ConvertFrom-Json
if (-not $session.success) { throw $session.error.message }

New-Item -ItemType Directory -Path 'D:\Translate' -Force | Out-Null
$page = .\nbtcli.exe translate export 'D:\WorldCopy' `
  --session-id $session.data.status.sessionId --page 0 `
  --output 'D:\Translate\page-0.txt' | ConvertFrom-Json
if (-not $page.success) { throw $page.error.message }
$page.data.replyTemplate | ConvertTo-Json -Depth 50 |
  Set-Content -Encoding utf8 'D:\Translate\reply-0.json'
```

此範例會建立工作階段並保存實際的回覆範本。請依指南填寫和提交回覆。

詳見 [地圖翻譯工作流指南][doc-translation] 與 [資源包、語言檔與引用指南][doc-resources]。

---

## 命令速查表

下表說明各命令會讀取或寫入哪些檔案。

- **安全查閱：不改存檔**：讀取世界資料或狀態。部分命令也會在世界資料夾外保存快取或匯出檔案。
- **預覽模式：僅產出計畫**：僅在世界資料夾外產出變更計畫或更新工作階段，世界存檔保持原狀。
- **寫入外部備份**：將世界資料保存至外部備份儲存庫。
- **修改存檔：請先關遊戲並備份**：實際寫入世界資料夾。執行前請先關閉 Minecraft 並加上 `--world-offline`。

| 任務目標 | 執行命令 | 安全等級 |
| :--- | :--- | :--- |
| 查詢版本或特定命令的參數說明 | `nbtcli --version` / `nbtcli help <命令>` | **安全查閱：不改存檔** |
| 讀取單一 NBT 標籤、區塊或 JSON 節點 | `nbtcli get <目標> --path <相對路徑> --pointer <指標>` | **安全查閱：不改存檔** |
| 列出世界維度或 `.mca` 區塊座標清單 | `nbtcli list <目標> [--path <相對路徑>]` | **安全查閱：不改存檔** |
| 依文字內容、標籤名稱或型別搜尋節點 | `nbtcli search <目標> [--query <文字>] [--name <名稱>]` | **安全查閱：不改存檔** |
| 掃描世界內 21 種文字來源並建立外部快取 | `nbtcli scan <世界目錄> [--summary] [--show-items]` | **安全查閱：不改存檔** |
| 抽取世界文字與來源位置至外部檔案 | `nbtcli extract <世界目錄> [--unique] --output <檔案>` | **安全查閱：不改存檔** |
| 驗證世界文字與容器結構完整性 | `nbtcli validate <世界目錄>` | **安全查閱：不改存檔** |
| 查詢支援的 Minecraft `DataVersion` 範圍 | `nbtcli capabilities` | **安全查閱：不改存檔** |
| 預覽單一節點的型別安全修改計畫 | `nbtcli set <目標> --pointer <指標> --value-file <檔案> --output <計畫>` | **預覽模式：僅產出計畫** |
| 預覽多筆操作的批次修改計畫 | `nbtcli plan <世界目錄> --request <ops.json> --output <計畫>` | **預覽模式：僅產出計畫** |
| 預覽將世界文字轉為翻譯鍵的計畫 | `nbtcli convert <世界目錄> --lang <語言代碼> --output <計畫>` | **預覽模式：僅產出計畫** |
| 管理分頁式地圖翻譯工作階段 | `nbtcli translate <start\|status\|export\|submit\|advance\|plan\|...>` | **預覽模式：僅產出計畫** |
| 匯出語言 JSON 檔或建立客戶端資源包 | `nbtcli translate <export-lang\|create-pack> <世界目錄> --output <路徑>` | **預覽模式：僅產出計畫** |
| 設定外部 AI 翻譯輔助偏好問卷 | `nbtcli agent <setup\|submit\|status\|help> <世界目錄>` | **預覽模式：僅產出計畫** |
| 離線套用已驗證的修改計畫 | `nbtcli apply <世界目錄> --plan <plan.json> --world-offline` | **修改存檔：請先關遊戲並備份** |
| 檢查中斷的寫入 | `nbtcli status <世界目錄> --plan-hash <雜湊>` | **安全查閱：不改存檔** |
| 繼續或回退中斷的寫入 | `nbtcli recover <世界目錄> --plan-hash <雜湊> --world-offline [--rollback]` | **修改存檔：請先關遊戲並備份** |
| 建立內容定址備份 | `nbtcli backup <世界目錄> [--suffix <名稱>]` | **寫入外部備份** |
| 預覽還原計畫 | `nbtcli restore <世界目錄> --manifest-hash <雜湊> --output <計畫>` | **預覽模式：僅產出計畫** |

完整旗標與輸入 JSON Schema 範本請參閱 [CLI 命令完整參考][doc-reference]。

---

## 二進位發行包驗證

每個 Windows x64 發行壓縮包（`nbtcli-win-x64.zip`）皆附帶外部 `SHA256SUMS.txt` 以及壓縮包內的 `candidate.json` 建置溯源清單。

1. **解壓縮前核對 ZIP 的 SHA-256 雜湊值：**

   ```powershell
   $zipHash = (Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
   $expected = (Get-Content .\SHA256SUMS.txt | Select-Object -First 1).Split(' ')[0].Trim().ToLowerInvariant()
   if ($zipHash -ne $expected) { throw "SHA-256 雜湊不符！" } else { "驗證成功: $zipHash" }
   ```

2. **檢視壓縮包內的 `candidate.json` 建置溯源清單：**

   ```powershell
   Get-Content .\nbtcli\candidate.json | ConvertFrom-Json | Select-Object product, version, commit, sdkVersion, runtimeVersion
   ```

   `candidate.json` 記錄了確切的 Git Commit 雜湊、固定的 .NET SDK（`10.0.404`）、內建的 .NET Runtime（`10.0.15`）、第三方相依套件版本，以及發行包內每一個檔案各自的 SHA-256 雜湊值。

---

## 完整線上文檔

完整教學指南、操作安全等級說明、可複製的 JSON Schema 範本與疑難排解清單皆已發布於文檔網站：

- **繁體中文文檔**：[https://nbtcli.evanlau1798.com/zh-tw/docs/][docs-zh]
  - [安裝驗證與安全查閱][doc-getting-started]
  - [安全編輯與中斷復原][doc-safe-editing]
  - [地圖翻譯工作流][doc-translation]
  - [資源包、語言檔與引用][doc-resources]
  - [備份與狀態檔管理][doc-backups]
  - [CLI 命令完整參考][doc-reference]
  - [支援範圍與疑難排解][doc-support]
- **English Documentation**：[https://nbtcli.evanlau1798.com/docs/][docs-en]
- **LLM 與自動化工具結構索引**：[https://nbtcli.evanlau1798.com/llms.txt][llms-url]（`docs/public/llms.txt`）

---

## 建置與驗證說明

請使用 `global.json` 指定的 .NET SDK（`10.0.404`），並將套件還原、Release 建置與驗證套件分開執行：

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

使用 Node.js `22.23.5` 可在本機安裝相依套件、建置或預覽 `docs/` 目錄下的 Astro Starlight 文檔網站：

```powershell
Set-Location .\docs
npm ci
npm run build
npm run preview
```

---

## 參與貢獻（Contributing）

NBTExplorerCLI 由 [@Evanlau1798](https://github.com/Evanlau1798) 建立與維護。我們歡迎聚焦且易於驗證的外部貢獻，例如獨立的錯誤修正、確定性回歸測試、文檔強化與多語言文檔翻譯。

在提交 Issue 或 Pull Request 之前，請先詳閱英文版貢獻規範 [CONTRIBUTING.md](https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md)，了解專案邊界、存檔安全不變量與提交前的必要驗證步驟。

---

## 授權條款

本專案採用 [MIT 授權條款](LICENSE) 釋出。隨附的第三方元件依其各自之授權與聲明散布，詳見 [SharpZipLib](licenses/SharpZipLib.md) 與 [Unicode](licenses/Unicode.md)。Windows x64 發行包亦隨附 .NET Runtime 授權條款與第三方聲明。

[windows-ci-badge]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/windows.yml/badge.svg
[windows-ci-url]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/windows.yml
[docs-ci-badge]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/docs.yml/badge.svg
[docs-ci-url]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/docs.yml
[release-badge]: https://img.shields.io/badge/release-v1.0.0-10b981
[releases-url]: https://github.com/Evanlau1798/NBTExplorerCLI/releases
[platform-badge]: https://img.shields.io/badge/platform-Windows%20x64-0f172a
[license-badge]: https://img.shields.io/badge/license-MIT-34d399
[license-url]: LICENSE
[docs-en]: https://nbtcli.evanlau1798.com/docs/
[docs-zh]: https://nbtcli.evanlau1798.com/zh-tw/docs/
[llms-url]: https://nbtcli.evanlau1798.com/llms.txt
[doc-getting-started]: https://nbtcli.evanlau1798.com/zh-tw/docs/getting-started/
[doc-safe-editing]: https://nbtcli.evanlau1798.com/zh-tw/docs/safe-editing/
[doc-translation]: https://nbtcli.evanlau1798.com/zh-tw/docs/translation-workflow/
[doc-resources]: https://nbtcli.evanlau1798.com/zh-tw/docs/resources-and-packs/
[doc-backups]: https://nbtcli.evanlau1798.com/zh-tw/docs/backups-and-state/
[doc-reference]: https://nbtcli.evanlau1798.com/zh-tw/docs/command-reference/
[doc-support]: https://nbtcli.evanlau1798.com/zh-tw/docs/support-and-troubleshooting/
