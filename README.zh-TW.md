# NBTExplorerCLI

[English](README.md) | [工具介紹首頁](https://nbtcli.evanlau1798.com/zh-tw/) | [線上使用文檔](https://nbtcli.evanlau1798.com/zh-tw/docs/) | [Contributing (English)](CONTRIBUTING.md)

**NBTExplorerCLI**（`nbtcli.exe`）是專為 **Minecraft Java Edition** 設計的 Windows x64 獨立封裝命令列工具。本工具採用統一的單行 JSON 輸出格式，支援查閱 NBT 結構、掃描並抽取世界文字、管理分頁式地圖翻譯工作階段、產生資源包，並在具備自動寫前備份與中斷復原日誌的保護下安全編輯世界存檔。

## 核心功能特色

- **安全唯讀查閱（`get`、`list`、`search`）**：支援以 JSON Pointer（`--pointer`）或結構化區段（`--segments`）讀取 `level.dat`、`.mca` 區塊檔、實體區塊與資料包 JSON，提供易讀顯示模式與可完整還原的型別模式（`--types`）。
- **世界文字掃描與完整性驗證（`scan`、`extract`、`validate`、`capabilities`）**：涵蓋 21 種 Minecraft 文字來源（告示牌、懸掛式告示牌、書本、物品、實體、容器、指令方塊、函數、計分板、Boss 血條、進度與語言檔），並於世界資料夾外維護可重建的掃描快取。
- **分頁式地圖翻譯工作流（`translate`、`agent`）**：將大型冒險地圖切分為易於管理的分頁與翻譯槽位（Slots），支援術語表與譯者備註，並於確認世界寫入完成後封存工作階段（`seal`）。
- **資源包生成與翻譯鍵轉換（`convert`、`translate plan --keys`、`export-lang`、`create-pack`）**：支援將世界內文字轉為翻譯鍵、匯出獨立語言檔，或建立並與既有資源包安全合併。
- **具備中斷復原能力的安全編輯（`set`、`batch`、`plan`、`apply`、`status`、`recover`、`backup`、`restore`）**：所有修改預設皆先產出不可變的預覽計畫（`nbtcli.patch-plan`）。離線寫入時自動保存寫前備份與日誌，若遇中斷可透過 `status` 與 `recover` 安全完成或完整回滾。

## 快速安裝與查閱（Windows x64）

請從 [GitHub Releases](https://github.com/Evanlau1798/NBTExplorerCLI/releases) 或通過驗證的 [GitHub Actions 候選建置](https://github.com/Evanlau1798/NBTExplorerCLI/actions) 下載 `nbtcli-win-x64.zip` 與 `SHA256SUMS.txt`。壓縮包已內建 .NET 執行環境（`10.0.15`）與所需函式庫，解壓縮後請將所有 `.dll` 與 `nbtcli.exe` 保留在同一個目錄下。

```powershell
# 1. 核對 ZIP 壓縮包的 SHA-256 雜湊值是否與 SHA256SUMS.txt 一致
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt

# 2. 完整解壓縮並確認執行檔版本與說明
Expand-Archive -Path .\nbtcli-win-x64.zip -DestinationPath .\nbtcli
.\nbtcli\nbtcli.exe --version
.\nbtcli\nbtcli.exe --help

# 3. 在世界副本上執行第一次唯讀查閱
.\nbtcli\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

執行 `.\nbtcli.exe help <命令>` 可查詢該命令支援的完整參數。可透過 `--ui-locale zh-TW` 或 `--ui-locale en-US` 切換介面訊息語言（與世界翻譯語言獨立）。請務必先在世界副本上測試流程，寫入前先預覽計畫，並確認關閉 Minecraft 與其他編輯工具後再加上 `--world-offline`。若寫入途中發生中斷，請使用相同的 `--state-root` 與 `--plan-hash` 查詢 `status`，再依結果執行 `recover`。

## 完整線上文檔

完整教學指南、操作風險說明與 CLI 命令參考皆已發布於文檔網站：

- **繁體中文文檔**：[https://nbtcli.evanlau1798.com/zh-tw/docs/](https://nbtcli.evanlau1798.com/zh-tw/docs/)
  - [安裝驗證與安全查閱](https://nbtcli.evanlau1798.com/zh-tw/docs/getting-started/)
  - [安全編輯與中斷復原](https://nbtcli.evanlau1798.com/zh-tw/docs/safe-editing/)
  - [地圖翻譯工作流](https://nbtcli.evanlau1798.com/zh-tw/docs/translation-workflow/)
  - [資源包、語言檔與引用](https://nbtcli.evanlau1798.com/zh-tw/docs/resources-and-packs/)
  - [備份與狀態檔管理](https://nbtcli.evanlau1798.com/zh-tw/docs/backups-and-state/)
  - [CLI 命令完整參考](https://nbtcli.evanlau1798.com/zh-tw/docs/command-reference/)
  - [支援範圍與疑難排解](https://nbtcli.evanlau1798.com/zh-tw/docs/support-and-troubleshooting/)
- **English Documentation**：[https://nbtcli.evanlau1798.com/docs/](https://nbtcli.evanlau1798.com/docs/)

## 建置與驗證說明

請使用 `global.json` 指定的 .NET SDK（`10.0.404`），並將套件還原、建置與驗證分開執行：

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

若需在本機建置或預覽 `docs/` 目錄下的 Astro Starlight 文檔網站：

```powershell
Set-Location .\docs
npm ci
npm run build
npm run preview
```

## 參與貢獻（Contributing）

NBTExplorerCLI 由 [@Evanlau1798](https://github.com/Evanlau1798) 建立與維護。我們歡迎聚焦且易於驗證的外部貢獻，例如獨立的錯誤修正、回歸測試、文檔勘誤與多語言文檔翻譯。

在提交 Issue 或 Pull Request 之前，請先詳閱英文版貢獻規範 [CONTRIBUTING.md](CONTRIBUTING.md)，了解專案邊界、存檔安全不變量與提交前的必要驗證步驟。

## 授權條款

本專案採用 [MIT 授權條款](LICENSE) 釋出。隨附的第三方元件依其各自之授權與聲明散布，詳見 [SharpZipLib](licenses/SharpZipLib.md) 與 [Unicode](licenses/Unicode.md)。Windows x64 發行包亦隨附 .NET Runtime 授權條款與第三方聲明。
