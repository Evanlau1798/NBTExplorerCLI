# NBTExplorerCLI vNext

Windows x64 / .NET 10 的獨立新核心，直接開發於本目錄。舊版位於 `.old/`，不參與新版 build 或 runtime。

目前僅有工程基礎、help/version 與最小驗證。尚未提供世界讀寫、掃描或翻譯能力，不能作正式版本使用。完整交付範圍見 [能力帳本](docs/capabilities.md) 及 [章程](docs/rewrite-charter.md)。

## 建置與驗證

SDK 鎖定於 `global.json`。可使用已安裝的相同 SDK，或本機隔離工具鏈 `.tools/dotnet/dotnet.exe`；`.tools/` 不提交、不修改系統 SDK。無第三方 runtime packages，NuGet.Config 清除隱藏 fallback sources。

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx --no-restore -c Release
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --help
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --version
```

建置與測試分開執行。Stdout 為 JSON，失敗回非零。驗證工程為無外部測試套件的 assertion harness；case/suite 選擇及 timeout 由 G00-C05 完成。benchmarks 目前回 `BENCHMARK_NOT_READY`，不能作性能證據。

資料根為 `%LOCALAPPDATA%/nbtcli-next`，暫存根為系統 temp 下的 `nbtcli-next`；取得根路徑不建立任何目錄。世界、譯文及 `.old` 使用者檔案不會因 help/version 被讀寫。候選不安裝至 PATH。
