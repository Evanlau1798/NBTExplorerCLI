# NBTExplorerCLI vNext

Windows x64 / .NET 10 的獨立新核心，直接開發於本目錄。舊版位於 `.old/`，不參與新版 build 或 runtime。

目前提供工程基礎、help/version，以及來源地址、穩定身分、保型樹與能力 profile 的領域模型。尚未提供世界讀寫、掃描或翻譯能力，不能作正式版本使用。完整交付範圍見 [能力帳本](docs/capabilities.md) 及 [章程](docs/rewrite-charter.md)；模型規格見 [Domain 契約](docs/domain-contracts.md)。

操作與交換的純模型規格見 [操作契約](docs/operation-contracts.md)，舊資料唯讀映射設計見 [legacy mapping](docs/legacy-mapping-contract.md)。這些模型不代表 CLI 已開放世界寫入。

## 建置與驗證

SDK 鎖定於 `global.json`。可使用已安裝的相同 SDK，或本機隔離工具鏈 `.tools/dotnet/dotnet.exe`；`.tools/` 不提交、不修改系統 SDK。無第三方 runtime packages，NuGet.Config 清除隱藏 fallback sources。

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx --no-restore -c Release
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --suite architecture
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --case help-identifies-implemented-commands
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --help
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --version
```

建置與測試分開執行。驗證工程為無外部測試套件的 assertion harness，suite 為 contracts、domain、foundation、architecture、corpus；預設執行全部一般案例。使用 `--fixture-root <absolute-path>` 可覆寫預設輸出資源根，結果不依 shell cwd。每個案例在獨立 worker 執行；`--timeout-ms` 預設 30000，接受 100–120000。Stdout 為單一 JSON，stderr 為診斷；失敗、unsupported、timeout、未知／空選擇皆非零。

校準案例不包含於預設 suite：顯式選擇 `runner-calibration-failure`、`runner-calibration-unsupported` 應 exit 1；`--case runner-calibration-timeout --timeout-ms 1000` 應終止 worker 並 exit 1。校準失敗不能計為通過。

`Directory.Build.targets` 檢查 project graph、Compile containment、HintPath 與 Domain 套件邊界；architecture suite 另檢查 compiled assembly／PDB、舊來源 checksum、process／動態 assembly APIs。`--artifact-root <publish-dir>` 可檢查最小 framework-dependent 發布內容與 deps。這些是工程防線，不代表已完成世界流程或 G08 自足發行驗收。語義改寫後的舊演算法仍需獨立 review，checksum 不能判斷所有抄寫。

benchmarks 的 `generate-corpus S|M|L <new-output-root>` 可建立固定容量資料；既有輸出根拒絕覆寫。其他入口仍回 `BENCHMARK_NOT_READY`，尚未有性能 control 或速度結果。固定來源、limits、差異裁決與切換規則見 [量測與切換契約](docs/measurement-and-cutover.md)。

資料根為 `%LOCALAPPDATA%/nbtcli-next`，暫存根為系統 temp 下的 `nbtcli-next`；取得根路徑不建立任何目錄。世界、譯文及 `.old` 使用者檔案不會因 help/version 被讀寫。候選不安裝至 PATH。
