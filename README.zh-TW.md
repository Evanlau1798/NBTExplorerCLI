<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://nbtcli.evanlau1798.com/banner-dark.svg" />
    <source media="(prefers-color-scheme: light)" srcset="https://nbtcli.evanlau1798.com/banner-light.svg" />
    <img alt="NBTExplorerCLI — Windows 專用 Minecraft Java Edition 存檔查閱、地圖翻譯與安全離線編輯工具" src="https://nbtcli.evanlau1798.com/banner-dark.svg" width="900" />
  </picture>

  <br />
  <br />

  [![Windows Candidate CI][windows-ci-badge]][windows-ci-url]
  [![Documentation Site CI][docs-ci-badge]][docs-ci-url]
  [![Release Version][release-badge]][releases-url]
  [![Target Platform][platform-badge]][releases-url]
  [![MIT License][license-badge]][license-url]

  <p>
    <b>免費、解壓縮即用的 Windows 工具，幫你查閱 Minecraft Java Edition 存檔資料、翻譯整張冒險地圖、打包資源包，以及安全修改世界存檔——適合一般玩家、地圖作者、翻譯團隊與 AI 助手使用。</b>
  </p>

  <p>
    <a href="#這個工具可以幫你做什麼">功能介紹</a> |
    <a href="#三步驟快速開始">快速開始</a> |
    <a href="#常見任務速查">常見任務</a> |
    <a href="#給-ai-agent-與自動化工具使用">給 AI Agent</a> |
    <a href="https://nbtcli.evanlau1798.com/zh-tw/docs/">完整線上文檔</a>
  </p>

  <p>
    <a href="README.md">English</a> | <b>繁體中文</b> | <a href="https://nbtcli.evanlau1798.com/zh-tw/">工具首頁</a> | <a href="https://nbtcli.evanlau1798.com/llms.txt">AI Agent 完整說明書 (llms.txt)</a>
  </p>
</div>

---

## 這個工具可以幫你做什麼

- **零風險查閱存檔**：以唯讀方式查看 `level.dat`、地圖區塊（`.mca`）、資料包與資源包壓縮檔，完全不會動到原存檔。
- **輕鬆翻譯整張冒險地圖**：自動掃描告示牌、書本、物品、實體、計分板、Boss 血條、指令方塊與資料包文字，分成一頁一頁慢慢翻，還能搭配術語表保持譯名一致。
- **彈性輸出語言檔與資源包**：可以把翻譯直接寫回地圖、轉成多語言翻譯鍵，或單獨打包／合併成客戶端 `.zip` 資源包，不改動地圖本身。
- **內建安全防呆機制**：所有修改都會先產生「預覽計畫」再寫入，寫入前自動在世界資料夾外建立備份；萬一中途當機也能安全續寫或一鍵還原。
- **對 AI 助手與腳本超級友善**：每個指令都輸出乾淨統一的 JSON 格式，可以直接交給 Claude、ChatGPT、Codex、Cursor 或 PowerShell 腳本自動化操作。

> [!NOTE]
> **適用環境：Windows 64 位元 + Minecraft Java Edition 存檔。** 解壓縮就能執行，**不需要另外安裝 .NET**。要修改存檔前，請務必先關閉 Minecraft 並用「世界副本」測試。不支援基岩版（Bedrock Edition）存檔，也沒有 3D 畫面編輯功能。

---

## 三步驟快速開始

### 步驟 1：下載並解壓縮

1. 前往 [GitHub Releases][releases-url]，下載最新版的 **`nbtcli-win-x64.zip`**。
2. 對壓縮檔按右鍵選「**解壓縮全部...**」，解到一個固定的資料夾（例如 `C:\Tools\nbtcli`）。
3. **請讓資料夾內的所有檔案待在一起**：不要把 `nbtcli.exe` 單獨拉出來，它需要旁邊的 `.dll` 和 `.json` 檔案才能啟動。

### 步驟 2：在這個資料夾裡打開 PowerShell

打開解壓縮後的資料夾，點一下檔案總管最上方的「路徑網址列」，輸入 `powershell` 後按 **Enter**（或按住 **Shift** 鍵在空白處點右鍵選「在終端機中開啟」）。接著輸入：

```powershell
.\nbtcli.exe --version
```

只要看到 `"success": true` 和 `"version": "1.0.0"`，就代表準備就緒了。

### 步驟 3：對世界副本做第一次唯讀檢查

先關閉 Minecraft，把你的存檔資料夾從 `%APPDATA%\.minecraft\saves\` **複製一份**出來（例如複製成 `D:\WorldCopy`），再試跑這兩個完全不改檔的指令：

```powershell
# 查看這個世界的 Minecraft 資料版本編號（純讀取）
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion

# 統計整張地圖裡有多少可翻譯的文字（純讀取）
.\nbtcli.exe scan 'D:\WorldCopy' --summary
```

如果你平常很少用 PowerShell，或想看找存檔、處理 Windows 安全提示、看懂輸出內容的完整圖文級步驟，請直接看 **[下載安裝與初次使用指南][doc-zh-getting-started]** 與 **[常見問題與名詞白話詞典][doc-zh-faq]**。

---

## 日常最常用範例

以下指令皆在解壓縮後的 `nbtcli` 資料夾中執行：

### 探索世界並匯出全部文字

```powershell
# 列出世界有哪些維度與區塊檔案
.\nbtcli.exe list 'D:\WorldCopy'

# 把地圖內所有不重複的文字匯出到外部檔案查看
New-Item -ItemType Directory -Path 'D:\Exports' -Force | Out-Null
.\nbtcli.exe extract 'D:\WorldCopy' --unique --output 'D:\Exports\world-text.json'
```

### 開始翻譯一張冒險地圖

```powershell
# 建立翻譯進度（完全不會修改世界檔案）
.\nbtcli.exe translate start 'D:\WorldCopy' --source-language en_us --target-language zh_tw --page-size 30
```

詳細的「匯出分頁 -> 填寫譯文 -> 套用到地圖或打包資源包」流程，請參閱 **[冒險地圖翻譯教學][doc-zh-translation]**。

### 修改前先幫世界做一份備份

```powershell
# 在世界資料夾外建立完整備份
.\nbtcli.exe backup 'D:\WorldCopy' --suffix before-edit
```

想了解備份存在哪裡、出事怎麼還原整張地圖或單一檔案，請看 **[備份還原與檔案存放位置][doc-zh-backups]**。

---

## 常見任務速查

每個指令都標有清楚的安全等級，執行前一眼就知道會不會改到存檔：
- **純讀取**：只看不改，世界存檔完全不會動。
- **僅預覽**：只會在世界資料夾外產生計畫檔或翻譯紀錄，原存檔保持不動。
- **會修改存檔**：實際寫入世界（請先關閉 Minecraft 並加上 `--world-offline`）。

| 你想做的事 | 執行指令 | 安全等級 |
| :--- | :--- | :--- |
| 查看版本或指令說明 | `.\nbtcli.exe --version` / `.\nbtcli.exe help <指令>` | 純讀取 |
| 切換提示訊息為中文或英文 | 在任意指令加上 `--ui-locale zh-TW` 或 `--ui-locale en-US` | 純讀取 |
| 列出世界的維度或區塊清單 | `.\nbtcli.exe list 'D:\WorldCopy'` | 純讀取 |
| 讀取單一數值（例如地圖名稱或版本） | `.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName` | 純讀取 |
| 依名稱或內容搜尋存檔欄位 | `.\nbtcli.exe search 'D:\WorldCopy' --path level.dat --name Spawn` | 純讀取 |
| 統計整張地圖有多少文字可翻譯 | `.\nbtcli.exe scan 'D:\WorldCopy' --summary` | 純讀取 |
| 檢查存檔有沒有損壞或未支援的檔案 | `.\nbtcli.exe validate 'D:\WorldCopy'` | 純讀取 |
| 開始一份地圖翻譯進度 | `.\nbtcli.exe translate start 'D:\WorldCopy' --source-language en_us --target-language zh_tw` | 僅預覽 |
| 在修改前先備份世界 | `.\nbtcli.exe backup 'D:\WorldCopy' --suffix before-edit` | 純讀取 |
| 套用已經檢查過的修改／翻譯計畫 | `.\nbtcli.exe apply 'D:\WorldCopy' --plan 'D:\Plans\plan.json' --world-offline` | 會修改存檔 |

想要直接複製貼上的完整步驟，請看 **[常見任務速查][doc-zh-recipes]** 或 **[完整命令參考][doc-zh-reference]**。

---

## 給 AI Agent 與自動化工具使用

`nbtcli.exe` 非常適合交給 AI 助手（Claude、ChatGPT、Codex、Cursor 等）幫你查存檔、修資料或翻譯整張地圖。我們專門準備了一份**高資訊密度、免讀廢話的 AI Agent 快速完整說明書**：

- **線上直讀版（推薦直接貼給 AI）**：[https://nbtcli.evanlau1798.com/llms.txt][llms-url]
- **本儲存庫檔案位置**：[`docs/public/llms.txt`](docs/public/llms.txt)
- **網頁版指南與直接可用的提示詞範本**：**[AI Agent 快速整合指南][doc-zh-ai-agents]**

這份 `llms.txt` 完整收錄了全部指令與旗標、6 種輸入 JSON Schema、退出碼判斷規則、編輯／翻譯／復原標準 SOP 與常見踩坑清單，讓 AI 讀完就能精準安全地操作。

---

## 完整線上文檔導覽

線上文檔提供完整的**繁體中文**與**英文**雙語教學：

| 主題 | 繁體中文 | English |
| :--- | :--- | :--- |
| 工具總覽 | [工具總覽][docs-zh] | [Documentation Overview][docs-en] |
| 下載與入門 | [下載安裝與初次使用][doc-zh-getting-started] | [Install & First Run][doc-getting-started] |
| 常見任務 | [常見任務速查][doc-zh-recipes] | [Common Tasks (Recipes)][doc-recipes] |
| 新手必看 | [常見問題與名詞白話詞典][doc-zh-faq] | [FAQ & Plain-Language Glossary][doc-faq] |
| 地圖翻譯 | [冒險地圖翻譯教學][doc-zh-translation] | [Map Translation Guide][doc-translation] |
| 資源包與語言檔 | [資源包與語言檔輸出][doc-zh-resources] | [Resource Packs & Language Files][doc-resources] |
| 安全編輯與復原 | [安全修改存檔與中斷復原][doc-zh-safe-editing] | [Editing Worlds & Crash Recovery][doc-safe-editing] |
| 備份與檔案位置 | [備份還原與檔案存放位置][doc-zh-backups] | [Backups & Where Files Live][doc-backups] |
| AI 整合專區 | [AI Agent 快速整合指南][doc-zh-ai-agents] | [AI Agent Quickstart][doc-ai-agents] |
| 完整參考 | [完整命令參考][doc-zh-reference] | [Command Reference][doc-reference] |
| 支援與排解 | [支援版本與疑難排解][doc-zh-support] | [Supported Versions & Troubleshooting][doc-support] |

---

## 從原始碼建置與參與貢獻

如果你想自行編譯程式、跑驗證測試套件，或是提交錯誤修正與文檔改進，請參閱 **[CONTRIBUTING.md](CONTRIBUTING.md)**（英文），裡面有完整的 .NET SDK（`10.0.404`）建置指令、文檔網站建置方式（`docs/`）與貢獻規範。

## 授權條款

本專案採用 [MIT 授權條款](LICENSE) 釋出。隨附第三方元件保留其各自授權與聲明，詳見 [SharpZipLib](licenses/SharpZipLib.md) 與 [Unicode](licenses/Unicode.md)。本工具非 Minecraft、Mojang 或 Microsoft 官方產品。

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
[doc-getting-started]: https://nbtcli.evanlau1798.com/docs/getting-started/
[doc-recipes]: https://nbtcli.evanlau1798.com/docs/recipes/
[doc-faq]: https://nbtcli.evanlau1798.com/docs/faq/
[doc-safe-editing]: https://nbtcli.evanlau1798.com/docs/safe-editing/
[doc-translation]: https://nbtcli.evanlau1798.com/docs/translation-workflow/
[doc-resources]: https://nbtcli.evanlau1798.com/docs/resources-and-packs/
[doc-backups]: https://nbtcli.evanlau1798.com/docs/backups-and-state/
[doc-ai-agents]: https://nbtcli.evanlau1798.com/docs/ai-agents/
[doc-reference]: https://nbtcli.evanlau1798.com/docs/command-reference/
[doc-support]: https://nbtcli.evanlau1798.com/docs/support-and-troubleshooting/
[doc-zh-getting-started]: https://nbtcli.evanlau1798.com/zh-tw/docs/getting-started/
[doc-zh-recipes]: https://nbtcli.evanlau1798.com/zh-tw/docs/recipes/
[doc-zh-faq]: https://nbtcli.evanlau1798.com/zh-tw/docs/faq/
[doc-zh-safe-editing]: https://nbtcli.evanlau1798.com/zh-tw/docs/safe-editing/
[doc-zh-translation]: https://nbtcli.evanlau1798.com/zh-tw/docs/translation-workflow/
[doc-zh-resources]: https://nbtcli.evanlau1798.com/zh-tw/docs/resources-and-packs/
[doc-zh-backups]: https://nbtcli.evanlau1798.com/zh-tw/docs/backups-and-state/
[doc-zh-ai-agents]: https://nbtcli.evanlau1798.com/zh-tw/docs/ai-agents/
[doc-zh-reference]: https://nbtcli.evanlau1798.com/zh-tw/docs/command-reference/
[doc-zh-support]: https://nbtcli.evanlau1798.com/zh-tw/docs/support-and-troubleshooting/
