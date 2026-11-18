import type { CommandEntry } from './cli-surface';

export const COMMAND_SURFACE: CommandEntry[] = [
  {
    id: 'help',
    group: 'inspect',
    syntax: 'nbtcli help [command]',
    tier: 'safe',
    requiredArgs: {
      en: 'None (optional command or subcommand name)',
      'zh-tw': '無（可選填命令或子命令名稱）',
    },
    summary: {
      en: 'Displays available commands, subcommands, and exact accepted options in JSON.',
      'zh-tw': '以 JSON 格式列出可用的命令、子命令與接受的參數選項。',
    },
    resultAndNext: {
      en: 'Returns usage and option list. Run before constructing scripts or unfamiliar commands.',
      'zh-tw': '回傳用法與參數清單。在執行不熟悉的指令前可先查詢。',
    },
  },
  {
    id: 'version',
    group: 'inspect',
    syntax: 'nbtcli version',
    tier: 'safe',
    requiredArgs: {
      en: 'None',
      'zh-tw': '無',
    },
    summary: {
      en: 'Prints the product name (nbtcli) and release version (1.0.0).',
      'zh-tw': '輸出產品名稱（nbtcli）與目前版本號（1.0.0）。',
    },
    resultAndNext: {
      en: 'Confirms binary integrity after extracting the release ZIP.',
      'zh-tw': '用於解壓縮發行包後確認執行檔可正常啟動。',
    },
  },
  {
    id: 'get',
    group: 'inspect',
    syntax: 'nbtcli get <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder or standalone NBT/JSON/archive file',
      'zh-tw': '目標世界資料夾，或單一 NBT / JSON / 壓縮檔路徑',
    },
    summary: {
      en: 'Reads a specific NBT tag, JSON property, or chunk payload without changing any file.',
      'zh-tw': '讀取指定的 NBT 節點、JSON 屬性或區塊資料，不修改任何檔案。',
    },
    resultAndNext: {
      en: 'Pass --types to get reversible typed-nbt JSON and exact source addresses for later set/batch edits.',
      'zh-tw': '加上 --types 可取得完整型別資訊（typed-nbt）與精確來源位址，供後續 set 或 batch 編輯使用。',
    },
  },
  {
    id: 'list',
    group: 'inspect',
    syntax: 'nbtcli list <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder, NBT file, or region file',
      'zh-tw': '目標世界資料夾、NBT 檔案或區塊檔（.mca）',
    },
    summary: {
      en: 'Lists world dimensions and region counts, populated chunks in a region, or immediate child nodes.',
      'zh-tw': '列出世界的維度與區塊檔數量、區塊檔內已生成的區塊座標，或指定節點的直屬子項目。',
    },
    resultAndNext: {
      en: 'Use to explore world layout before running targeted get, search, or scan commands.',
      'zh-tw': '適合在執行 get、search 或 scan 之前，先盤點世界結構與維度清單。',
    },
  },
  {
    id: 'search',
    group: 'inspect',
    syntax: 'nbtcli search <world-or-file>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target path plus at least one filter (--query, --name, or --type)',
      'zh-tw': '目標路徑，且至少提供一個篩選條件（--query、--name 或 --type）',
    },
    summary: {
      en: 'Searches NBT or JSON subtrees by value text, key name, or tag type with pagination.',
      'zh-tw': '依據內容文字、欄位名稱或標籤型別搜尋子樹，支援分頁檢視。',
    },
    resultAndNext: {
      en: 'Returns matching node addresses and bounded previews.',
      'zh-tw': '回傳符合條件的節點位址與內容預覽。',
    },
  },
  {
    id: 'scan',
    group: 'scan',
    syntax: 'nbtcli scan <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder',
      'zh-tw': '目標世界資料夾',
    },
    summary: {
      en: 'Scans the world for translatable or inspectable text across signs, books, items, entities, command blocks, and datapacks.',
      'zh-tw': '掃描世界中的告示牌、書本、物品、實體、指令方塊與資料包文字，並建立外部快取索引。',
    },
    resultAndNext: {
      en: 'Reports per-source status and text counts. Follow with extract or translate start.',
      'zh-tw': '回報各來源檔案狀態與文字統計，後續可銜接 extract 或 translate start。',
    },
  },
  {
    id: 'extract',
    group: 'scan',
    syntax: 'nbtcli extract <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder',
      'zh-tw': '目標世界資料夾',
    },
    summary: {
      en: 'Extracts decoded text entries and their exact source locations, optionally deduplicating identical strings or filtering by regex.',
      'zh-tw': '抽取解碼後的文字與精確來源位址，支援以 --unique 合併相同文字預覽或用正規表示式篩選。',
    },
    resultAndNext: {
      en: 'Exports inspection JSON to stdout or a new file outside the world via --output.',
      'zh-tw': '將結果輸出至標準輸出，或透過 --output 另存為世界資料夾外的新檔案。',
    },
  },
  {
    id: 'validate',
    group: 'scan',
    syntax: 'nbtcli validate <world>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder',
      'zh-tw': '目標世界資料夾',
    },
    summary: {
      en: 'Verifies world catalog headers (--quick) or decodes all selected text sources to detect corrupt or unsupported data early.',
      'zh-tw': '檢查世界目錄結構（--quick）或完整解碼所選文字來源，提前發現損壞或不支援的資料格式。',
    },
    resultAndNext: {
      en: 'Distinguishes clean sources, unsupported features (exit 4), and corrupted files (exit 5).',
      'zh-tw': '明確區分正常檔案、尚未支援的格式（退出碼 4）與損壞無法讀取的檔案（退出碼 5）。',
    },
  },
  {
    id: 'capabilities',
    group: 'scan',
    syntax: 'nbtcli capabilities',
    tier: 'safe',
    requiredArgs: {
      en: 'None',
      'zh-tw': '無',
    },
    summary: {
      en: 'Lists registered CLI commands and declared support levels for each Minecraft DataVersion profile.',
      'zh-tw': '列出已註冊的命令清單，以及各 Minecraft DataVersion 支援組合的宣告支援程度。',
    },
    resultAndNext: {
      en: 'Check before working on a new Minecraft version to see read/extract/plan/write/recover declarations.',
      'zh-tw': '在處理新版本存檔前可先查詢 read、extract、plan、write 與 recover 的支援狀態。',
    },
  },
  {
    id: 'set',
    group: 'edit',
    syntax: 'nbtcli set <world> --path <rel-path> (--value <json> | --value-file <file>)',
    tier: 'preview',
    requiredArgs: {
      en: 'Target world, --path, node selector (--pointer or --segments), and --value / --value-file (typed-nbt JSON)',
      'zh-tw': '目標世界、--path、節點選擇器（--pointer 或 --segments）以及 --value 或 --value-file（typed-nbt 格式）',
    },
    summary: {
      en: 'Replaces a specific NBT value using explicit typed-nbt JSON. Runs in preview mode by default unless --apply --world-offline is given.',
      'zh-tw': '以明確型別的 typed-nbt JSON 替換指定節點的值。預設為預覽模式，不會直接修改世界，除非加上 --apply --world-offline。',
    },
    resultAndNext: {
      en: 'Preview outputs a patch-plan; save it with --output and commit via apply, or pass --apply --world-offline.',
      'zh-tw': '預覽會產生修補計畫（patch-plan），可用 --output 存成檔案再交由 apply 寫入，或直接加上 --apply --world-offline。',
    },
  },
  {
    id: 'batch',
    group: 'edit',
    syntax: 'nbtcli batch <file-or-dash> --world <world>',
    tier: 'preview',
    requiredArgs: {
      en: 'Operations JSON file (or - for stdin) and --world <world>',
      'zh-tw': '操作清單 JSON 檔案（或 - 代表標準輸入）與 --world <world>',
    },
    summary: {
      en: 'Previews or applies a batch of versioned typed operations against a world.',
      'zh-tw': '對目標世界預覽或批次套用多筆結構化編輯操作（nbtcli.operations）。',
    },
    resultAndNext: {
      en: 'Previews by default; pass --output to save the plan or --apply --world-offline to commit.',
      'zh-tw': '預設僅預覽計畫；可搭配 --output 匯出計畫檔，或搭配 --apply --world-offline 離線寫入。',
    },
  },
  {
    id: 'plan',
    group: 'edit',
    syntax: 'nbtcli plan <world> --request <file-or-dash>',
    tier: 'preview',
    requiredArgs: {
      en: 'Target world and --request <operations-file-or-dash>',
      'zh-tw': '目標世界與 --request <操作請求檔或標準輸入>',
    },
    summary: {
      en: 'Builds and verifies an immutable patch-plan from an operations request without modifying the world.',
      'zh-tw': '根據操作請求建立並驗證不可變的修補計畫（patch-plan），完全不更動世界存檔。',
    },
    resultAndNext: {
      en: 'Save the plan with --output <plan.json> and inspect before running apply.',
      'zh-tw': '使用 --output <plan.json> 儲存計畫檔，檢查無誤後再執行 apply。',
    },
  },
  {
    id: 'apply',
    group: 'edit',
    syntax: 'nbtcli apply <world> --plan <file-or-dash> --world-offline',
    tier: 'write',
    requiredArgs: {
      en: 'Target world, --plan <plan.json>, and --world-offline',
      'zh-tw': '目標世界、--plan <計畫檔> 與 --world-offline',
    },
    summary: {
      en: 'Executes a verified patch-plan with automatic preimage backup, staging, journal tracking, and readback verification.',
      'zh-tw': '執行已驗證的修補計畫，自動建立寫前備份、暫存驗證、日誌紀錄（Journal）與寫後讀回校驗。',
    },
    resultAndNext: {
      en: 'Returns a durable apply receipt. If interrupted (exit 7), check status and run recover.',
      'zh-tw': '回傳寫入回執（Receipt）。若寫入中斷（退出碼 7），請先查閱 status 再執行 recover。',
    },
  },
  {
    id: 'status',
    group: 'edit',
    syntax: 'nbtcli status <world> --plan-hash <hash>',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world and --plan-hash <sha256> (with the same --state-root)',
      'zh-tw': '目標世界與 --plan-hash <計畫雜湊值>（若原本有自訂 --state-root 需一併帶入）',
    },
    summary: {
      en: 'Inspects the journal and receipt state of a plan to determine whether a write finished, needs recovery, or rolled back.',
      'zh-tw': '查詢特定計畫的執行日誌與回執狀態，判斷寫入是否已完成、需要復原，或已回滾。',
    },
    resultAndNext: {
      en: 'Read-only check; use its output to decide whether to run recover or recover --rollback.',
      'zh-tw': '純唯讀檢查；根據回傳狀態決定要執行 recover 繼續完成，或加上 --rollback 退回原狀。',
    },
  },
  {
    id: 'recover',
    group: 'edit',
    syntax: 'nbtcli recover <world> --plan-hash <hash> --world-offline',
    tier: 'write',
    requiredArgs: {
      en: 'Target world, --plan-hash <sha256>, and --world-offline',
      'zh-tw': '目標世界、--plan-hash <計畫雜湊值> 與 --world-offline',
    },
    summary: {
      en: 'Safely completes an interrupted apply or rolls back to the verified pre-edit state (--rollback).',
      'zh-tw': '安全接續完成被中斷的寫入作業，或加上 --rollback 將世界完整還原至修改前的狀態。',
    },
    resultAndNext: {
      en: 'Restores world consistency and emits a terminal receipt (exit 0 for forward completion, exit 8 for rollback).',
      'zh-tw': '確保世界檔案恢復一致狀態並產出最終回執（繼續完成為退出碼 0，回滾至原狀為退出碼 8）。',
    },
  },
  {
    id: 'backup',
    group: 'edit',
    syntax: 'nbtcli backup <world> [--suffix <name>]',
    tier: 'safe',
    requiredArgs: {
      en: 'Target world folder',
      'zh-tw': '目標世界資料夾',
    },
    summary: {
      en: 'Captures a hash-verified, content-addressed snapshot of the world (or a single --path) inside the external state repository.',
      'zh-tw': '在外部狀態目錄中建立經雜湊驗證的世界快照（亦可透過 --path 指定單一檔案），不更動世界本身。',
    },
    resultAndNext: {
      en: 'Retain the returned manifestHash (and state root) so you can restore at any time.',
      'zh-tw': '請保存回傳的 manifestHash（以及使用的 state-root），以便隨時透過 restore 還原。',
    },
  },
  {
    id: 'restore',
    group: 'edit',
    syntax: 'nbtcli restore <world> --manifest-hash <hash>',
    tier: 'preview',
    requiredArgs: {
      en: 'Target world and --manifest-hash <sha256>',
      'zh-tw': '目標世界與 --manifest-hash <備份清單雜湊值>',
    },
    summary: {
      en: 'Previews or applies a restoration from a verified backup manifest, even if current world files are damaged or missing.',
      'zh-tw': '從已驗證的備份清單預覽或還原世界檔案，即使目前世界內的目標檔案已損壞或遺失也能修復。',
    },
    resultAndNext: {
      en: 'Previews by default; add --apply --world-offline to write the restored files.',
      'zh-tw': '預設為預覽模式；確認無誤後加上 --apply --world-offline 即可實際還原檔案。',
    },
  },
  {
    id: 'convert',
    group: 'translate',
    syntax: 'nbtcli convert <world> --lang <locale>',
    tier: 'preview',
    requiredArgs: {
      en: 'Target world and --lang <locale> (for example zh_tw or en_us)',
      'zh-tw': '目標世界與 --lang <語言代碼>（例如 zh_tw 或 en_us）',
    },
    summary: {
      en: 'Converts in-world literal text into translation keys and bundles the matching language file/resource pack.',
      'zh-tw': '將世界內的原始文字結構化轉換為翻譯鍵（Translation Keys），並同步產生對應的語言檔與資源包。',
    },
    resultAndNext: {
      en: 'Previews by default; pass --output to save the plan or --apply --world-offline to commit.',
      'zh-tw': '預設為預覽模式；可透過 --output 匯出計畫，或加上 --apply --world-offline 直接完成轉換。',
    },
  },
  {
    id: 'translate',
    group: 'translate',
    syntax: 'nbtcli translate <subcommand> <world> [options]',
    tier: 'preview',
    requiredArgs: {
      en: 'Subcommand (start, status, export, advance, submit, get, jump, note, glossary, reset, groups, memory, plan, export-lang, create-pack, seal, help)',
      'zh-tw': '子命令（start、status、export、advance、submit、get、jump、note、glossary、reset、groups、memory、plan、export-lang、create-pack、seal、help）',
    },
    summary: {
      en: 'Manages paginated translation sessions, terminology glossaries, translation plans, resource packs, and completion sealing.',
      'zh-tw': '管理分頁式地圖翻譯工作階段、術語表、翻譯計畫生成、獨立資源包匯出與完成封存（seal）。',
    },
    resultAndNext: {
      en: 'Stores session progress outside the world; world edits only happen when you apply a generated plan.',
      'zh-tw': '所有翻譯進度皆儲存在世界資料夾外；唯有將產出的計畫交由 apply 執行時才會修改世界。',
    },
  },
  {
    id: 'agent',
    group: 'agent',
    syntax: 'nbtcli agent <setup|submit|status|help> <world>',
    tier: 'preview',
    requiredArgs: {
      en: 'Subcommand (setup, submit, status, or help) and target world',
      'zh-tw': '子命令（setup、submit、status 或 help）與目標世界',
    },
    summary: {
      en: 'Configures default source/target languages and page-size preferences for external translation tools or AI assistants.',
      'zh-tw': '建立與管理外部翻譯工具或 AI 助手的預設來源／目標語言及每頁筆數偏好設定。',
    },
    resultAndNext: {
      en: 'Saves preferences in the external state root; never calls external networks or modifies the world.',
      'zh-tw': '偏好設定儲存於外部狀態目錄，不會主動連網，也不會修改任何世界檔案。',
    },
  },
];
