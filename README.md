# NBTExplorerCLI

[English](README.en.md)

[產品首頁](https://nbtcli.evanlau1798.com/) · [使用文件](https://nbtcli.evanlau1798.com/docs)

NBTExplorerCLI 是 Windows x64 的 Minecraft Java 版世界資料 CLI。它能查閱 NBT、掃描文字、管理翻譯、輸出資源包，以及預覽和驗證世界編輯。請在世界副本上確認工作流程，並在遊戲及其他寫入工具關閉時修改世界。

## 安裝與查閱

Windows x64 ZIP 可從 [GitHub Releases](https://github.com/Evanlau1798/NBTExplorerCLI/releases) 或對應的 [Actions 候選](https://github.com/Evanlau1798/NBTExplorerCLI/actions) 取得。用隨附的 `SHA256SUMS.txt` 核對 ZIP，完整解壓到新目錄並保留隨附的 DLL。封裝內含 .NET runtime。

```powershell
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
.\nbtcli.exe --version
.\nbtcli.exe --help
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

`help <命令>` 顯示實際選項。CLI 輸出單一 JSON，使用 `--ui-locale zh-TW` 或 `en-US` 選擇介面語言。寫入前請先預覽，核對計畫及來源，並只在世界離線時提供 `--world-offline`。若提交中斷，使用同一個 state root 和計畫 hash 查詢 `status`，再依結果決定如何 `recover`。

## 開發與發布

使用 `global.json` 指定的 SDK。建置與驗證分開執行。

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

GitHub Actions 建置並驗證 Windows x64 候選，發布則由通過驗證的 tag 工作流程執行。

## 授權

本專案採用 [MIT](LICENSE)。隨附的第三方元件依各自的授權與聲明散布。見 [SharpZipLib](licenses/SharpZipLib.md) 與 [Unicode](licenses/Unicode.md)。Windows 發行包也附有 .NET 授權及第三方聲明。
