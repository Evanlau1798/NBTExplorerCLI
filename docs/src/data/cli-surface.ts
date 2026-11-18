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
  summary: Record<LocaleKey, string>;
  resultAndNext: Record<LocaleKey, string>;
}

export { COMMAND_SURFACE } from './cli-commands';

export interface TranslateSubcommandEntry {
  subcommand: string;
  tier: SafetyTier;
  summary: Record<LocaleKey, string>;
}

export const TRANSLATE_SUBCOMMANDS: TranslateSubcommandEntry[] = [
  {
    subcommand: 'start',
    tier: 'preview',
    summary: {
      en: 'Scans the selected scope and opens a new translation session with paginated text slots.',
      'zh-tw': '掃描指定範圍並建立新的翻譯工作階段，將世界文字切分為可分頁處理的翻譯槽位（Slots）。',
    },
  },
  {
    subcommand: 'status',
    tier: 'safe',
    summary: {
      en: 'Checks session translation progress and verifies whether the world files have changed since the session started.',
      'zh-tw': '查看目前的翻譯進度，並檢查世界檔案自工作階段建立以來是否曾被外部修改。',
    },
  },
  {
    subcommand: 'export',
    tier: 'safe',
    summary: {
      en: 'Exports a specific page of text slots along with a structured reply template.',
      'zh-tw': '匯出指定頁碼的待譯文字與結構化回覆範本（Reply Template）。',
    },
  },
  {
    subcommand: 'advance',
    tier: 'preview',
    summary: {
      en: 'Optionally validates and saves a completed reply file, then returns the next pending page.',
      'zh-tw': '可選擇同時提交並驗證已完成的回覆檔，接著自動回傳下一個待處理的分頁。',
    },
  },
  {
    subcommand: 'submit',
    tier: 'preview',
    summary: {
      en: 'Validates and saves a translated reply JSON file (or stdin) for the current revision and returns the next pending page.',
      'zh-tw': '驗證並儲存翻譯回覆 JSON（支援檔案或標準輸入），更新工作階段版本號並回傳下一頁。',
    },
  },
  {
    subcommand: 'get',
    tier: 'safe',
    summary: {
      en: 'Inspects a single translation slot, its source text, format rules, and current translated value.',
      'zh-tw': '查詢單一翻譯槽位（Slot）的原文、格式限制與目前儲存的譯文。',
    },
  },
  {
    subcommand: 'jump',
    tier: 'preview',
    summary: {
      en: 'Updates the manual page pointer using a versioned session-command request.',
      'zh-tw': '透過版本化的 session-command 請求切換目前手動檢視的目標頁碼。',
    },
  },
  {
    subcommand: 'note',
    tier: 'preview',
    summary: {
      en: 'Reads or updates translator context notes attached to the session.',
      'zh-tw': '查閱或新增工作階段內的譯者備註與背景說明。',
    },
  },
  {
    subcommand: 'glossary',
    tier: 'preview',
    summary: {
      en: 'Reads or updates the session terminology glossary to keep recurring names consistent.',
      'zh-tw': '查閱或更新專有名詞術語表，確保地圖內重複出現的人名、地名與物品譯名一致。',
    },
  },
  {
    subcommand: 'reset',
    tier: 'preview',
    summary: {
      en: 'Previews or confirms resetting selected slots back to pending while preserving historical records.',
      'zh-tw': '預覽或確認將指定的槽位重置回待翻譯狀態（保留歷史紀錄，不會刪除已寫入世界的檔案）。',
    },
  },
  {
    subcommand: 'groups',
    tier: 'safe',
    summary: {
      en: 'Lists context groups of related text occurrences across the world.',
      'zh-tw': '分頁列出世界內具相同語境關聯的文字群組。',
    },
  },
  {
    subcommand: 'memory',
    tier: 'safe',
    summary: {
      en: 'Queries compatible translation candidates from other local sessions.',
      'zh-tw': '從本機其他翻譯工作階段中查詢相容的歷史翻譯記憶候選詞。',
    },
  },
  {
    subcommand: 'plan',
    tier: 'preview',
    summary: {
      en: 'Generates a verified world PatchPlan in either --direct (replace text in-place) or --keys (convert to translation keys + language file) mode.',
      'zh-tw': '根據已完成的翻譯產生修補計畫：可選擇 --direct（直接替換世界文字）或 --keys（轉為翻譯鍵並附加語言檔）。',
    },
  },
  {
    subcommand: 'export-lang',
    tier: 'preview',
    summary: {
      en: 'Previews or writes standalone original/target language JSON files from frozen session values without modifying gameplay NBT.',
      'zh-tw': '從工作階段匯出獨立的原文／譯文語言檔（.json），不修改世界內的遊戲 NBT 資料。',
    },
  },
  {
    subcommand: 'create-pack',
    tier: 'preview',
    summary: {
      en: 'Previews or builds a standalone Minecraft resource pack (ZIP or directory), optionally merging with an existing pack.',
      'zh-tw': '預覽或建立獨立的 Minecraft 資源包（ZIP 或資料夾格式），支援與既有資源包安全合併（--merge）。',
    },
  },
  {
    subcommand: 'seal',
    tier: 'preview',
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
