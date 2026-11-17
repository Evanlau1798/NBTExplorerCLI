# NBTExplorerCLI vNext

Windows x64 / .NET 10 的獨立新核心，直接開發於本目錄。舊版位於 `.old/`，不參與新版 build 或 runtime。

目前公開 CLI 提供 help/version、get、list/ls、search/find，以及 scan/scan-world、extract/extract-text、validate、capabilities。可查閱 NBT、原生 JSON、指定 ZIP entry、region chunk、世界盤點，並透過共用 verified cache 掃描／抽取文字；精確路徑與退出碼見 [查閱契約](docs/cli-query-contract.md)，完整性、快取、篩選及匯出見 [世界查詢契約](docs/cli-scan-contract.md)。session、plan／apply／recover API 已有核心驗收，翻譯及世界寫入的公開入口仍在整合，尚未提供正式發行版本。完整範圍見 [能力帳本](docs/capabilities.md) 及 [章程](docs/rewrite-charter.md)；模型規格見 [Domain 契約](docs/domain-contracts.md)。

操作與交換規格見 [操作契約](docs/operation-contracts.md)，session 及封存見 [session 契約](docs/session-contract.md)，舊資料唯讀映射見 [legacy mapping](docs/legacy-mapping-contract.md)。目前公開查閱命令只讀取世界。

## 建置與驗證

SDK 鎖定於 `global.json`。可使用已安裝的相同 SDK，或本機隔離工具鏈 `.tools/dotnet/dotnet.exe`；`.tools/` 不提交、不修改系統 SDK。Codec 的 SharpZipLib 1.4.2 由 packages.lock.json 固定，NuGet.Config 清除隱藏 fallback folders。

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx --no-restore -c Release
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --suite architecture
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --case help-identifies-implemented-commands
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --help
dotnet src/Next.Cli/bin/Release/net10.0/nbtcli-next.dll --version
```

建置與測試分開執行。驗證工程為無外部測試套件的 assertion harness，suite 例為 cli-read、session、session-apply、scan、discovery、storage、foundation、architecture；`--suite cli-read` 會啟動實際 CLI 做黑箱查閱，預設執行全部一般案例。使用 `--fixture-root <absolute-path>` 可覆寫預設輸出資源根，結果不依 shell cwd。每個案例在獨立 worker 執行；`--timeout-ms` 預設 30000，接受 100–120000。Stdout 為單一 JSON，stderr 為診斷；失敗、unsupported、timeout、未知／空選擇皆非零。

校準案例不包含於預設 suite：顯式選擇 `runner-calibration-failure`、`runner-calibration-unsupported` 應 exit 1；`--case runner-calibration-timeout --timeout-ms 1000` 應終止 worker 並 exit 1。校準失敗不能計為通過。

`Directory.Build.targets` 檢查 project graph、Compile containment、HintPath 與 Domain 套件邊界；architecture suite 另檢查 compiled assembly／PDB、舊來源 checksum、process／動態 assembly APIs。`--artifact-root <publish-dir>` 可檢查最小 framework-dependent 發布內容與 deps。這些是工程防線，不代表已完成世界流程或 G08 自足發行驗收。語義改寫後的舊演算法仍需獨立 review，checksum 不能判斷所有抄寫。

benchmarks 的 `generate-corpus S|M|L <new-output-root>` 可建立固定容量資料；既有輸出根拒絕覆寫。其他入口仍回 `BENCHMARK_NOT_READY`，尚未有性能 control 或速度結果。固定來源、limits、差異裁決與切換規則見 [量測與切換契約](docs/measurement-and-cutover.md)。

資料根為 `%LOCALAPPDATA%/nbtcli-next`，暫存根為系統 temp 下的 `nbtcli-next`；取得根路徑不建立任何目錄。世界、譯文及 `.old` 使用者檔案不會因 help/version 被讀寫。候選不安裝至 PATH。
