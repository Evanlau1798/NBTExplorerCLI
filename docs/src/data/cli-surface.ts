export type LocaleKey = 'en' | 'zh-tw';
export type SafetyTier = 'safe' | 'preview' | 'write';

export const PRODUCT_META = {
  name: 'NBTExplorerCLI',
  binary: 'nbtcli.exe',
  command: 'nbtcli',
  version: '1.0.0',
  zipName: 'nbtcli-win-x64.zip',
  checksumFile: 'SHA256SUMS.txt',
  manifestFile: 'candidate.json',
  repoUrl: 'https://github.com/Evanlau1798/NBTExplorerCLI',
  releasesUrl: 'https://github.com/Evanlau1798/NBTExplorerCLI/releases',
  actionsUrl: 'https://github.com/Evanlau1798/NBTExplorerCLI/actions',
  sdkVersion: '10.0.404',
  runtimeVersion: '10.0.15',
} as const;

export const SAFETY_LABELS: Record<
  LocaleKey,
  Record<SafetyTier, { badge: string; title: string; description: string }>
> = {
  en: {
    safe: {
      badge: 'Safe to Inspect',
      title: 'Safe to Inspect — Does Not Modify Your World',
      description:
        'Commands in this mode only read files or display information. Your Minecraft world save remains completely untouched.',
    },
    preview: {
      badge: 'Preview Only',
      title: 'Preview Mode — Generates a Plan File Without Touching the World',
      description:
        'Creates a reviewable change plan or session file outside your world folder so you can check every change before writing.',
    },
    write: {
      badge: 'Modifies World Save',
      title: 'Modifies World Save — Close Minecraft and Back Up First',
      description:
        'Writes changes to the selected world folder. Always test on a world copy, close Minecraft and other tools first, and pass --world-offline.',
    },
  },
  'zh-tw': {
    safe: {
      badge: '安全查閱：不改存檔',
      title: '安全查閱模式 — 完全不會修改您的世界存檔',
      description:
        '此模式下的指令僅讀取資料並輸出結果，不會對 Minecraft 世界資料夾進行任何寫入或更動。',
    },
    preview: {
      badge: '預覽模式：僅產出計畫',
      title: '預覽與計畫模式 — 在世界資料夾外產生變更計畫，不直接改動存檔',
      description:
        '先將預計修改的內容產生為獨立的計畫檔（Plan）或工作階段紀錄，讓您在實際寫入前能完整檢查每一項變更。',
    },
    write: {
      badge: '修改存檔：請先關遊戲並備份',
      title: '寫入存檔模式 — 執行前請務必關閉 Minecraft 並使用世界副本',
      description:
        '此操作會實際修改指定的世界資料夾。請先建立備份或在世界副本上操作，確認遊戲與其他編輯器皆已關閉後，再加上 --world-offline 執行。',
    },
  },
};

export interface CommandEntry {
  id: string;
  group: 'inspect' | 'scan' | 'edit' | 'translate' | 'agent';
  syntax: string;
  tier: SafetyTier;
  requiredArgs: Record<LocaleKey, string>;
  commonOptions: string[];
  summary: Record<LocaleKey, string>;
  resultAndNext: Record<LocaleKey, string>;
}

export const COMMAND_SURFACE: CommandEntry[] = [
  {
    id: 'help',
    group: 'inspect',
    syntax: 'nbtcli help [command] [subcommand]',
    tier: 'safe',
    requiredArgs: {
      en: 'None (optional command or subcommand name)',
      'zh-tw': '無（可選填命令或子命令名稱）',
    },
    commonOptions: ['--help', '--ui-locale'],
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
    commonOptions: ['--help', '--ui-locale'],
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
    commonOptions: [
      '--path',
      '--pointer',
      '--segments',
      '--chunk',
      '--dimension',
      '--entry',
      '--compression',
      '--types',
      '--ui-locale',
    ],
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
    commonOptions: [
      '--path',
      '--pointer',
      '--segments',
      '--chunk',
      '--dimension',
      '--entry',
      '--compression',
      '--types',
      '--limit',
      '--offset',
      '--ui-locale',
    ],
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
    commonOptions: [
      '--query',
      '--name',
      '--type',
      '--path',
      '--pointer',
      '--segments',
      '--chunk',
      '--dimension',
      '--entry',
      '--types',
      '--limit',
      '--offset',
      '--ui-locale',
    ],
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
    commonOptions: [
      '--dimension',
      '--region',
      '--types',
      '--path',
      '--limit',
      '--offset',
      '--summary',
      '--show-items',
      '--refresh',
      '--no-cache',
      '--cache-root',
      '--workers',
      '--verbose',
      '--debug',
      '--ui-locale',
    ],
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
    commonOptions: [
      '--dimension',
      '--region',
      '--types',
      '--path',
      '--all',
      '--unique',
      '--match-pattern',
      '--output',
      '--limit',
      '--offset',
      '--refresh',
      '--no-cache',
      '--cache-root',
      '--workers',
      '--ui-locale',
    ],
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
    commonOptions: [
      '--quick',
      '--dimension',
      '--region',
      '--types',
      '--path',
      '--limit',
      '--offset',
      '--refresh',
      '--no-cache',
      '--cache-root',
      '--workers',
      '--verbose',
      '--ui-locale',
    ],
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
    commonOptions: ['--help', '--ui-locale'],
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
    commonOptions: [
      '--path',
      '--pointer',
      '--segments',
      '--chunk',
      '--dimension',
      '--compression',
      '--value',
      '--value-file',
      '--operation-id',
      '--apply',
      '--world-offline',
      '--state-root',
      '--output',
      '--ui-locale',
    ],
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
    commonOptions: ['--world', '--apply', '--world-offline', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--request', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--plan', '--world-offline', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--plan-hash', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--plan-hash', '--world-offline', '--rollback', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--path', '--suffix', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: ['--manifest-hash', '--path', '--apply', '--world-offline', '--state-root', '--output', '--ui-locale'],
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
    commonOptions: [
      '--lang',
      '--prefix',
      '--keepdup',
      '--no-backup',
      '--language-output',
      '--path',
      '--dimension',
      '--region',
      '--types',
      '--match-pattern',
      '--workers',
      '--apply',
      '--world-offline',
      '--state-root',
      '--output',
      '--verbose',
      '--ui-locale',
    ],
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
    commonOptions: ['--session-id', '--state-root', '--output', '--ui-locale', '--help'],
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
    commonOptions: ['--source-language', '--target-language', '--request', '--operation-id', '--state-root', '--output', '--ui-locale'],
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

export interface TranslateSubcommandEntry {
  subcommand: string;
  tier: SafetyTier;
  options: string[];
  summary: Record<LocaleKey, string>;
}

export const TRANSLATE_SUBCOMMANDS: TranslateSubcommandEntry[] = [
  {
    subcommand: 'start',
    tier: 'preview',
    options: [
      '--source-language',
      '--target-language',
      '--session-id',
      '--operation-id',
      '--page-size',
      '--page-bytes',
      '--types',
      '--path',
      '--dimension',
      '--region',
      '--match-pattern',
      '--standalone',
      '--no-group',
      '--state-root',
      '--output',
    ],
    summary: {
      en: 'Scans the selected scope and opens a new translation session with paginated text slots.',
      'zh-tw': '掃描指定範圍並建立新的翻譯工作階段，將世界文字切分為可分頁處理的翻譯槽位（Slots）。',
    },
  },
  {
    subcommand: 'status',
    tier: 'safe',
    options: ['--session-id', '--state-root', '--output'],
    summary: {
      en: 'Checks session translation progress and verifies whether the world files have changed since the session started.',
      'zh-tw': '查看目前的翻譯進度，並檢查世界檔案自工作階段建立以來是否曾被外部修改。',
    },
  },
  {
    subcommand: 'export',
    tier: 'safe',
    options: ['--session-id', '--page', '--operation-id', '--no-group', '--state-root', '--output'],
    summary: {
      en: 'Exports a specific page of text slots along with a structured reply template.',
      'zh-tw': '匯出指定頁碼的待譯文字與結構化回覆範本（Reply Template）。',
    },
  },
  {
    subcommand: 'advance',
    tier: 'preview',
    options: ['--session-id', '--reply', '--no-group', '--state-root', '--output'],
    summary: {
      en: 'Optionally validates and saves a completed reply file, then returns the next pending page.',
      'zh-tw': '可選擇同時提交並驗證已完成的回覆檔，接著自動回傳下一個待處理的分頁。',
    },
  },
  {
    subcommand: 'submit',
    tier: 'preview',
    options: ['--session-id', '--reply', '--no-group', '--state-root', '--output'],
    summary: {
      en: 'Validates and saves a translated reply JSON file (or stdin) for the current revision and returns the next pending page.',
      'zh-tw': '驗證並儲存翻譯回覆 JSON（支援檔案或標準輸入），更新工作階段版本號並回傳下一頁。',
    },
  },
  {
    subcommand: 'get',
    tier: 'safe',
    options: ['--session-id', '--slot-id', '--state-root', '--output'],
    summary: {
      en: 'Inspects a single translation slot, its source text, format rules, and current translated value.',
      'zh-tw': '查詢單一翻譯槽位（Slot）的原文、格式限制與目前儲存的譯文。',
    },
  },
  {
    subcommand: 'jump',
    tier: 'preview',
    options: ['--session-id', '--request', '--state-root', '--output'],
    summary: {
      en: 'Updates the manual page pointer using a versioned session-command request.',
      'zh-tw': '透過版本化的 session-command 請求切換目前手動檢視的目標頁碼。',
    },
  },
  {
    subcommand: 'note',
    tier: 'preview',
    options: ['--session-id', '--request', '--limit', '--offset', '--state-root', '--output'],
    summary: {
      en: 'Reads or updates translator context notes attached to the session.',
      'zh-tw': '查閱或新增工作階段內的譯者備註與背景說明。',
    },
  },
  {
    subcommand: 'glossary',
    tier: 'preview',
    options: ['--session-id', '--request', '--limit', '--offset', '--state-root', '--output'],
    summary: {
      en: 'Reads or updates the session terminology glossary to keep recurring names consistent.',
      'zh-tw': '查閱或更新專有名詞術語表，確保地圖內重複出現的人名、地名與物品譯名一致。',
    },
  },
  {
    subcommand: 'reset',
    tier: 'preview',
    options: ['--session-id', '--request', '--confirm', '--state-root', '--output'],
    summary: {
      en: 'Previews or confirms resetting selected slots back to pending while preserving historical records.',
      'zh-tw': '預覽或確認將指定的槽位重置回待翻譯狀態（保留歷史紀錄，不會刪除已寫入世界的檔案）。',
    },
  },
  {
    subcommand: 'groups',
    tier: 'safe',
    options: ['--session-id', '--limit', '--offset', '--state-root', '--output'],
    summary: {
      en: 'Lists context groups of related text occurrences across the world.',
      'zh-tw': '分頁列出世界內具相同語境關聯的文字群組。',
    },
  },
  {
    subcommand: 'memory',
    tier: 'safe',
    options: ['--session-id', '--slot-id', '--source-sessions', '--glossary-version', '--limit', '--offset', '--state-root', '--output'],
    summary: {
      en: 'Queries compatible translation candidates from other local sessions.',
      'zh-tw': '從本機其他翻譯工作階段中查詢相容的歷史翻譯記憶候選詞。',
    },
  },
  {
    subcommand: 'plan',
    tier: 'preview',
    options: [
      '--session-id',
      '--revision',
      '--direct',
      '--keys',
      '--prefix',
      '--language-output',
      '--operation-id',
      '--synchronize-references',
      '--state-root',
      '--output',
    ],
    summary: {
      en: 'Generates a verified world PatchPlan in either --direct (replace text in-place) or --keys (convert to translation keys + language file) mode.',
      'zh-tw': '根據已完成的翻譯產生修補計畫：可選擇 --direct（直接替換世界文字）或 --keys（轉為翻譯鍵並附加語言檔）。',
    },
  },
  {
    subcommand: 'export-lang',
    tier: 'preview',
    options: [
      '--session-id',
      '--revision',
      '--original-lang',
      '--target-lang',
      '--prefix',
      '--output',
      '--plan-output',
      '--apply',
      '--world-offline',
      '--state-root',
    ],
    summary: {
      en: 'Previews or writes standalone original/target language JSON files from frozen session values without modifying gameplay NBT.',
      'zh-tw': '從工作階段匯出獨立的原文／譯文語言檔（.json），不修改世界內的遊戲 NBT 資料。',
    },
  },
  {
    subcommand: 'create-pack',
    tier: 'preview',
    options: [
      '--session-id',
      '--revision',
      '--original-lang',
      '--target-lang',
      '--prefix',
      '--name',
      '--format',
      '--merge',
      '--pack-format',
      '--output',
      '--plan-output',
      '--apply',
      '--world-offline',
      '--state-root',
    ],
    summary: {
      en: 'Previews or builds a standalone Minecraft resource pack (ZIP or directory), optionally merging with an existing pack.',
      'zh-tw': '預覽或建立獨立的 Minecraft 資源包（ZIP 或資料夾格式），支援與既有資源包安全合併（--merge）。',
    },
  },
  {
    subcommand: 'seal',
    tier: 'preview',
    options: ['--session-id', '--revision', '--operation-id', '--plan-hash', '--state-root', '--output'],
    summary: {
      en: 'Marks the translation session as completed after verifying the durable world apply receipt and plan hash.',
      'zh-tw': '在確認世界已完成 apply 寫入並核對 plan-hash 後，將翻譯工作階段正式封存為已完成狀態。',
    },
  },
];

export interface ExitCodeEntry {
  code: number;
  label: Record<LocaleKey, string>;
  meaning: Record<LocaleKey, string>;
  action: Record<LocaleKey, string>;
}

export const EXIT_CODES: ExitCodeEntry[] = [
  {
    code: 0,
    label: { en: 'Success', 'zh-tw': '成功完成' },
    meaning: {
      en: 'The command completed normally (including a valid search with zero matches or a verified no-op).',
      'zh-tw': '指令正常完成（包含合法但無符合項目的搜尋，或確認無需變更的操作）。',
    },
    action: {
      en: 'Proceed to the next step in your workflow.',
      'zh-tw': '可繼續進行下一步驟。',
    },
  },
  {
    code: 1,
    label: { en: 'Internal Failure', 'zh-tw': '非預期內部錯誤' },
    meaning: {
      en: 'An unexpected internal error occurred.',
      'zh-tw': '發生非預期的內部錯誤。',
    },
    action: {
      en: 'Inspect error.code and error.details; verify the world status before retrying.',
      'zh-tw': '請檢查 JSON 中的 error.code 與 error.details，並在重試前先確認世界狀態。',
    },
  },
  {
    code: 2,
    label: { en: 'Invalid Arguments', 'zh-tw': '參數或語法錯誤' },
    meaning: {
      en: 'Unknown command or flag, missing required value, repeated option, or incompatible selector.',
      'zh-tw': '未知的命令或旗標、缺少必要參數值、重複指定選項，或選擇器彼此衝突。',
    },
    action: {
      en: 'Run nbtcli help <command> to check exact flag names. No files were changed.',
      'zh-tw': '請執行 nbtcli help <command> 核對正確參數名稱。此時不會有任何檔案被修改。',
    },
  },
  {
    code: 3,
    label: { en: 'Conflict or Stale State', 'zh-tw': '狀態衝突或來源已變更' },
    meaning: {
      en: 'The world or session changed since the plan/page was created, the node address is ambiguous, or an operation ID was reused with different input.',
      'zh-tw': '世界存檔或工作階段在產生計畫後曾被修改、節點位址具歧義，或重複使用了相同的 operation-id 卻帶入不同內容。',
    },
    action: {
      en: 'Re-scan or export the latest session revision and generate a fresh plan.',
      'zh-tw': '請重新掃描世界或匯出最新工作階段分頁，並重新產生計畫。',
    },
  },
  {
    code: 4,
    label: { en: 'Unsupported Format or Incomplete Scope', 'zh-tw': '格式不支援或範圍不完整' },
    meaning: {
      en: 'The target world uses an unsupported DataVersion/profile feature, or selected sources could not be completely discovered.',
      'zh-tw': '目標世界使用了未支援的 DataVersion 或結構特性，或選定的範圍無法完整解析。',
    },
    action: {
      en: 'Check nbtcli capabilities and narrow the scan/edit scope with --path, --dimension, or --types.',
      'zh-tw': '請執行 nbtcli capabilities 查詢支援程度，並使用 --path、--dimension 或 --types 縮小操作範圍。',
    },
  },
  {
    code: 5,
    label: { en: 'File, Access, or Validation Error', 'zh-tw': '檔案讀寫、權限或資料損壞錯誤' },
    meaning: {
      en: 'Missing file, locked world file, malformed NBT/JSON input, or pre-commit preparation failure.',
      'zh-tw': '找不到檔案、檔案被遊戲或其他程式鎖定、NBT/JSON 格式損壞，或寫前準備失敗。',
    },
    action: {
      en: 'Ensure Minecraft is closed, verify the file path, or restore damaged files from a backup.',
      'zh-tw': '請確認 Minecraft 已關閉、檢查檔案路徑是否正確，或使用 backup/restore 修復損壞檔案。',
    },
  },
  {
    code: 6,
    label: { en: 'Resource or Timeout Limit', 'zh-tw': '超過資源上限或正規表示式逾時' },
    meaning: {
      en: 'Input size, slot count, node depth, output size (64 MiB max), or regex match time (100 ms) exceeded safety limits.',
      'zh-tw': '輸入大小、翻譯槽位數量、節點深度、輸出大小（上限 64 MiB）或正規表示式配對（100 毫秒）超過安全上限。',
    },
    action: {
      en: 'Filter by --dimension, --region, or --path, reduce --page-size, or simplify --match-pattern.',
      'zh-tw': '請透過 --dimension、--region 或 --path 縮小範圍，調降 --page-size，或簡化 --match-pattern。',
    },
  },
  {
    code: 7,
    label: { en: 'Recovery Required', 'zh-tw': '需要執行復原（Recovery Required）' },
    meaning: {
      en: 'A write operation was interrupted or encountered an I/O error after committing began.',
      'zh-tw': '寫入作業在提交過程中斷或發生 I/O 錯誤，世界處於待復原狀態。',
    },
    action: {
      en: 'Do NOT open the world in Minecraft. Keep the returned planHash and stateRoot, run nbtcli status, then run nbtcli recover.',
      'zh-tw': '請勿在遊戲中開啟該世界。保留回傳的 planHash 與 stateRoot，先執行 nbtcli status 檢查，再執行 nbtcli recover。',
    },
  },
  {
    code: 8,
    label: { en: 'Rolled Back', 'zh-tw': '已完成回滾（Rolled Back）' },
    meaning: {
      en: 'Recovery rolled the world back to its verified pre-edit state rather than applying the changes forward.',
      'zh-tw': '復原程序已將世界安全退回修改前的驗證狀態，而非完成寫入變更。',
    },
    action: {
      en: 'Your original world bytes are restored. Create a new plan if you wish to apply changes again.',
      'zh-tw': '世界已安全恢復原狀。若仍需套用修改，請重新產生新的計畫檔。',
    },
  },
  {
    code: 130,
    label: { en: 'Cancelled', 'zh-tw': '操作已取消' },
    meaning: {
      en: 'The operation recorded a cancelled receipt.',
      'zh-tw': '操作記錄為已取消狀態。',
    },
    action: {
      en: 'If cancelled during a write, run status and recover with the plan hash before using the world.',
      'zh-tw': '若在寫入期間中断，請在使用該世界前透過計畫雜湊值執行 status 與 recover。',
    },
  },
];

export interface ProfileMatrixRow {
  profile: string;
  dataVersion: number;
  layout: string;
  itemSchema: string;
  textSchema: string;
  packLayout: string;
}

export const SUPPORTED_PROFILES: ProfileMatrixRow[] = [
  {
    profile: 'java-1.20.4',
    dataVersion: 3700,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-tags-1.20.4',
    textSchema: 'java-text-json (v1)',
    packLayout: 'java-pack-plural',
  },
  {
    profile: 'java-1.21',
    dataVersion: 3953,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-components-1.20.5',
    textSchema: 'java-text-json (v1)',
    packLayout: 'java-pack-singular',
  },
  {
    profile: 'java-1.21.5',
    dataVersion: 4325,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-components-1.21.5',
    textSchema: 'java-text-nbt (v1)',
    packLayout: 'java-pack-singular',
  },
  {
    profile: 'java-26.1-snapshot-6',
    dataVersion: 4774,
    layout: 'java-dimensions-namespaced',
    itemSchema: 'java-item-components-26.1-snapshot-6',
    textSchema: 'java-text-nbt (v2)',
    packLayout: 'java-pack-singular',
  },
];
