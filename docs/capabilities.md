# 首版能力帳本

GateRevision: 2；owner：專案維護者。本表定義完整能力與相容要求；實際可用入口見 README 及各 CLI 契約，未接線能力仍屬必需。

舊來源為 `.old/Program.cs`、各 Commands dispatcher 與參數處理。完整實際參數及來源見 [CLI 盤點](legacy-cli-inventory.json)。以下包含已接線與後續必需入口，一律經新版服務；新版參數與差異見 [查閱](cli-query-contract.md)、[掃描](cli-scan-contract.md)、[編輯](cli-edit-contract.md)及 [session 管理](cli-session-contract.md)。每個 fixture 在負責 gate 實作，必須保存 hash 與正反例結果才能 VERIFIED。

| CAP | 新版入口／能力 | 決策與相容影響 | Gate | 驗收 fixture |
| --- | --- | --- | --- | --- |
| CAP01 | scan-world / scan：完整世界掃描 | 保留；逐來源狀態與 coverage，錯誤不當零項 | G03/G06 | SCAN-COMPLETE-01 |
| CAP02 | scan 篩選／分頁／cache | 保留 dimension/region/types/limit/offset/summary/show-items/refresh/no-cache | G03/G06 | SCAN-FILTER-01、CACHE-01 |
| CAP03 | extract-text / extract | 保留 output/all/unique/match-pattern；去重不丟來源，regex 有 timeout | G03/G06 | EXTRACT-01、SCAN-LANG-01 |
| CAP04 | get、list / ls | 保留 NBT path/types、datapack JSON 值查閱與世界名稱／維度／region 盤點；顯示與 typed interchange 分離；歧義地址拒絕 | G01/G02/G06 | ADDRESS-KEY-01、NBT-TYPES-01、GET-JSON-01、LIST-WORLD-01 |
| CAP05 | search / find | 保留 query/name/type 與分頁；失敗與無結果分開 | G02/G06 | SEARCH-01 |
| CAP06 | set | 重新設計為顯式 TypedEdit；保留非文字型別編輯；舊含糊 path 需轉 typed address | G01/G05/G06 | TYPED-EDIT-01 |
| CAP07 | batch <file> / stdin | 保留一般操作用途；新版 schema 與逐項九態回執；逐欄相容決策見下表 | G05/G06 | BATCH-01、BATCH-LEGACY-FIELDS-01、APPLY-COUNTS-01 |
| CAP08 | agent setup/submit/status/help | 保留檔案問卷、語言與能力偏好；版本化設定，不寫舊資料位置 | G04/G06 | AGENT-01、LEGACY-USER-DATA-01 |
| CAP09 | translate start | 保留 page-size/types/match-pattern/standalone/no-group | G04/G06 | SESSION-START-01、GROUP-WORLD-01 |
| CAP10 | translate next | 重新設計 decoded slots/revision/source hash；舊 marker reply 不直接可信 | G04/G06 | SLOT-SET-01、SESSION-CRASH-01 |
| CAP11 | translate status/jump/get | 保留 page/key 查閱；legacy key 僅明確 mapping | G04/G06 | SESSION-NAV-01 |
| CAP12 | translate note、notes/glossary | 保留並支援來源可追溯的人工知識匯入／匯出 | G04/G08 | SESSION-KNOWLEDGE-01 |
| CAP13 | translate reset | 重新設計 archive/reset；先顯示範圍，保留成果與歷史 | G04/G06 | SESSION-RESET-01 |
| CAP14 | translate apply --direct | LiteralTranslation；不授權更改 event/style/UUID | G05/G06 | SLOT-01、AST-PRESERVE-01 |
| CAP15 | translate apply / apply-keys | TranslationKeyConversion；lang/pack 是必要提交依賴 | G05/G06 | APPLY-PACK-01、KEY-CONVERSION-01 |
| CAP16 | translate export-lang | 保留 original-lang/target-lang/output；既有檔及重複 key 檢查 | G05/G06 | LANG-EXPORT-01 |
| CAP17 | translate create-pack | 保留 name/output/merge/pack-format；不覆蓋未選 entry | G05/G06 | PACK-MERGE-01 |
| CAP18 | convert | 保留 prefix/lang/keepdup/no-backup；改走同一 planner/apply | G05/G06 | CONVERT-01、APPLY-TEXT-ONLY-01 |
| CAP19 | backup、寫前備份 | 新 manifest 完整性；suffix/路徑保留，不得關閉必要恢復材料 | G05/G06 | BACKUP-01 |
| CAP20 | validate | 保留 quick/verbose；保證差異、未知與損壞分開 | G02/G05/G06 | VALIDATE-01、NBT-LIMIT-01 |
| CAP21 | help/version、JSON／退出碼 | 保留機器輸出；新增 schema/outcome，部分失敗非成功 | G06 | CLI-01 |
| CAP22 | 繁中／英文、UTF-8 | UI 改 --ui-locale；convert --lang 是翻譯語言；machine code 不翻譯 | G06 | I18N-01 |
| CAP23 | Verification 開發 harness | 舊 core-test/test/debug-scan 改用新測試入口；Release 不帶測試 | G00/G06 | RUNNER-01、REWRITE-INDEPENDENCE-01 |
| CAP24 | plan 預覽／衝突 | 新增；不可變 plan，預覽不寫世界 | G01/G05/G06 | PLAN-01、APPLY-CONFLICT-01 |
| CAP25 | apply/recover/status | 新增；prepared/journal/readback、重送冪等 | G05/G06/G08 | APPLY-CRASH-01 |
| CAP26 | restore | 新增；verified manifest，允許損壞／缺失目的檔，先 stage 再 replace | G05/G06/G08 | RESTORE-01 |
| CAP27 | import-legacy | 新增唯讀匯入與衝突報告；缺維度／包位置不猜測 | G04/G06/G08 | LEGACY-IMPORT-01、MIGRATION-01 |
| CAP28 | capabilities | 新增機器 profile／操作支援表；可讀不推定可寫 | G01/G03/G06/G08 | PROFILE-01 |

## 內容最低覆蓋

Sign、HangingSign、Book、LecternBook、Title、Author、ItemName、ItemLore、ArmorStand、EntityName、ContainerName、CommandBlock、McFunction、TextDisplay、BossBar、ScoreboardTeam、ScoreboardObjective、Advancement、CommandStorage、LangKey、TranslationKey 共 21 舊類型，均需對照新版 role，驗證抽取、精確定位、允許操作、提交及 scope 外保留。

必需失敗例包含跨維度同名 region、多 ZIP 同 entry、特殊／空 key、同文不同語境、with/hover/execute、多 component、typed arrays、literal markers、來源變動、壞 cache、壞檔、pack stage 失敗、crash recovery 與設定保留。

## 舊 batch 欄位映射（CAP07）

來源：`Commands/BatchModels.cs` 與 `BatchCommand.cs`。舊反序列化大小寫不敏感；新版 importer 只在顯式 legacy 模式接受此格式。fixture `BATCH-LEGACY-FIELDS-01` 逐欄驗證成功映射、歧義拒絕及原輸入保留。

| 舊欄位／型別 | 新版決策 |
| --- | --- |
| WorldPath / string? | 映射顯式 world root binding；缺值拒絕，不作永久 world identity |
| Operations / List? | 逐筆建立 typed intent；缺值／空集合拒絕，保留逐筆結果 |
| Options / object? | 缺值採明列安全預設；只接受已識別設定，未知欄位列遷移報告 |
| Id / string? | 保存 legacy provenance；不作權威 occurrenceId 或唯一索引 |
| Path / string? | 唯一回解至完整 SourceAddress；缺維度／ZIP 或歧義時拒絕，要求重掃 |
| Value / string? | 保存原文字；依目標型別及明列 importer 規則轉 typed replacement，不能猜任意 JSON wrapper |
| LineIndex / int? | 舊公開 batch 流程未使用；保留 provenance 並報 LegacyFieldUnused，不讓它悄悄改變新 locator |
| CreateBackup / bool，預設 true | 映射便利備份政策；false 不能關閉必要 preimage/journal／恢復材料 |
| ValidateBeforeWrite / bool，預設 true | 舊公開 batch 流程未使用；保留並報 LegacyFieldUnused；新版驗證固定必要，false 不可停用 |

## 查閱模式映射（CAP04）

`get <world> --path <datapack.json#property>` 的 JSON 物件／array／scalar 查閱能力保留，由新版 JSON locator 取代舊 lossy NBT 轉型；可唯一解讀的舊 dot/index path 由 argument adapter 映射，含分隔符或歧義時改用 typed segments／JSON Pointer。`GET-JSON-01` 覆蓋 nested array、特殊 key、錯地址與讀取錯誤。

`list <world>` 保留 worldName、dimensions、各維度 region 路徑／數量，使用新版 profile-aware discovery；`list <dat>` 保留 typed 子節點、preview 與分頁。`LIST-WORLD-01` 覆蓋兩種 layout、自訂維度、region/entity 區分及來源失敗；世界資料不得因盤點被改動。

## 明確破壞性變更與遷移

- 輸入／輸出加 schemaFamily/schemaVersion；Display JSON 不再默認可逆。使用 typed-NBT interchange 或顯式 legacy importer。
- 地址保存 exact artifact/entry、dimension、chunk、typed key/index；舊顯示 path 無法唯一還原時回 Conflict，先重掃再配對。
- translate reply 改為 decoded slot 集合；重新 export page 後按 slotId 回覆，不提交 AST 或 marker 模板。
- --lang 的舊全域 UI 用法改為 --ui-locale；原 convert 翻譯語言保留命令內語義。無明確短 alias 的旗標不可再由首字母猜配。
- no-backup 只關閉便利備份，不能關閉 journal/preimage；force 不能繞過來源、slot、依賴或受保護結構驗證。
- 無旗標值的 bare words、未知／重複互斥選項不再默默忽略；回非零及可操作錯誤。

其餘已盤點參數採語法映射保留；debug/verbose 只控制 stderr 診斷，不改成功、完整性或寫入範圍。舊未公開 restore helper 不算既有公開能力。G06 提供每項新用法、help 與黑箱驗收，之前不將本表標為 VERIFIED。
