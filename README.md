# NBTExplorerCLI

[English](README.en.md)

NBTExplorerCLI 是 Windows x64 的 Minecraft Java 版世界資料 CLI。它能查閱 NBT、掃描文字、管理翻譯、輸出資源包，以及預覽和驗證世界編輯。請在世界副本上確認工作流程，並在遊戲及其他寫入工具關閉時修改世界。

## 安裝與查閱

從 GitHub Releases 取得 Windows x64 ZIP，依同一版本的 `SHA256SUMS.txt` 驗證下載，解壓到新目錄，保留隨附的 DLL。發布包內含 .NET runtime。

```powershell
Get-FileHash .\nbtcli-next-win-x64.zip -Algorithm SHA256
.\nbtcli-next.exe --version
.\nbtcli-next.exe --help
.\nbtcli-next.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

`help <命令>` 顯示實際選項。CLI 輸出單一 JSON，使用 `--ui-locale zh-TW` 或 `en-US` 選擇介面語言。寫入前請先預覽，核對計畫及來源，並只在世界離線時提供 `--world-offline`。若提交中斷，使用同一個 state root 和計畫 hash 查詢 `status`，再依結果決定如何 `recover`。

## 遷移舊資料

保留原世界、備份、譯文、術語、問卷及設定。`.nbtcli_cache` 內的問卷、設定與筆記也是使用者資料。指定舊格式和世界副本，並將新版 state 和報告放在世界之外。

```powershell
.\nbtcli-next.exe import-legacy 'D:\WorldCopy' --input 'D:\Legacy\session.json' `
  --profile command-session --source-language en_us --target-language zh_tw `
  --state-root 'D:\NextState' --output 'D:\Migration\session-report.json'
```

匯入報告保存原始 bytes、雜湊及逐筆決策，請像原資料一樣保護。設定與問卷需透過 `agent setup` 和 `agent submit` 重新確認。完全核對世界副本與來源後，可用 `--acknowledge-world-rebind --create-session` 建立 session。歧義、來源變更與沒有可靠位置的譯文須人工處理。匯入本身不套用世界變更。

遷移中斷時，重送相同輸入與選項，或用 `import-legacy <世界> --list` 找回 ID，再用 `--import-id <ID> --create-session` 接續。同一版回退請使用已驗證備份與 journal。切換 executable 不會復原已寫入的資料。

舊 batch 使用 `batch <file> --legacy --world <副本> --migration-report <報告> --output <計畫>` 預覽。可重送相同的預覽來補齊未輸出的計畫，前提是原始 batch、世界來源和報告仍能驗證出同一份計畫。已有 apply journal 時，用 `status` 與 `recover` 接續。

## 開發與發布

使用 `global.json` 指定的 SDK。建置與驗證分開執行。

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --suite all --timeout-ms 120000
```

GitHub Actions 建置並驗證 Windows x64 候選，發布則由通過驗證的 tag 工作流程執行。

## 授權

本專案採用 [MIT](LICENSE)。隨附的第三方元件依各自的授權與聲明散布。見 [SharpZipLib](licenses/SharpZipLib.md) 與 [Unicode](licenses/Unicode.md)。Windows 發行包也附有 .NET 授權及第三方聲明。
