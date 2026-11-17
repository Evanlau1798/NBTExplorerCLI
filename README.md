# NBTExplorerCLI vNext

Windows x64 / .NET 10 的獨立新核心，直接開發於本目錄。舊版位於 `.old/`，不參與新版 build 或 runtime。

目前公開 CLI 包含查閱、掃描、通用編輯／恢復及檔案式 session／agent 管理。help/version、get、list/ls、search/find、scan/scan-world、extract/extract-text、validate、capabilities 可查閱 NBT、原生 JSON、ZIP entry、region chunk 與世界盤點，並透過共用 verified cache 掃描／抽取文字；精確路徑與退出碼見 [查閱契約](docs/cli-query-contract.md)，完整性、快取、篩選及匯出見 [世界查詢契約](docs/cli-scan-contract.md)。尚未提供正式發行版本；剩餘整合範圍見下段及 [能力帳本](docs/capabilities.md)，重寫邊界見 [章程](docs/rewrite-charter.md)，模型規格見 [Domain 契約](docs/domain-contracts.md)。

通用 set/batch/plan、apply/status/recover 及 verified backup/restore 已接入新版服務，預設預覽與寫入條件見 [編輯命令契約](docs/cli-edit-contract.md)。`translate` 提供 [檔案式 session 管理](docs/cli-session-contract.md)及 [direct plan → apply/recover → seal](docs/cli-session-write-contract.md)；`agent setup/submit/status/help` 提供版本化問卷與可重送設定，見 [agent 契約](docs/cli-agent-contract.md)。`import-legacy` 保留舊資料、匯出報告並在完整證據下建立 session，見 [legacy CLI 契約](docs/cli-legacy-contract.md)。key plan 已接入必要 lang/pack 依賴，見 [key plan 契約](docs/cli-key-plan-contract.md)；[convert](docs/cli-convert-contract.md) 支援共用轉換、scope、keepdup 與備份政策；獨立 pack/lang 入口仍在整合。操作與交換規格見 [操作契約](docs/operation-contracts.md)，session 及封存見 [session 契約](docs/session-contract.md)，唯讀映射見 [legacy mapping](docs/legacy-mapping-contract.md)。世界寫入須明確套用並確認離線；預覽與管理命令不寫世界。

識別名稱與引用的同步需明確使用 `translate plan --direct --synchronize-references`；完整世界範圍、typed 群組、共同碰撞檢查與舊 journal 的回退方式見 [引用同步契約](docs/cli-reference-contract.md)。raw TypedEdit 與引用群組須分開規劃並重新掃描。

舊 batch JSON 可透過 `batch --legacy --world ... --migration-report ...` 保存原輸入及映射報告，再使用共用計畫與寫入流程；欄位、唯一路徑與值型別限制見 [legacy batch 契約](docs/cli-legacy-batch-contract.md)。

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
