export type LocaleKey = 'en' | 'zh-tw';
export type SafetyTier = 'safe' | 'preview' | 'write';

export const PRODUCT_META = {
  name: 'NBTExplorerCLI',
  binary: 'nbtcli.exe',
  command: 'nbtcli',
  version: '1.0.0',
  zipName: 'nbtcli-win-x64.zip',
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
      badge: 'Read Only',
      title: 'Read Only — Safe to Run Anytime',
      description:
        'Only reads information from your world or checks status logs. Your Minecraft save files are never modified.',
    },
    preview: {
      badge: 'Preview Only',
      title: 'Preview Mode — Saves Plans Outside Your World',
      description:
        'Prepares a reviewable plan or saves translation progress in a separate folder outside your world. Your Minecraft save stays untouched until you apply it.',
    },
    write: {
      badge: 'Modifies World',
      title: 'Modifies World Save — Close Minecraft & Use a Copy First',
      description:
        'Writes changes into the selected Minecraft world folder. Always test on a copy first, close Minecraft and other editors, and pass --world-offline.',
    },
  },
  'zh-tw': {
    safe: {
      badge: '純讀取：不改存檔',
      title: '純讀取模式 — 隨時執行都不會改到世界',
      description:
        '只讀取存檔資訊或查看狀態紀錄，完全不會修改您的 Minecraft 世界資料夾。',
    },
    preview: {
      badge: '僅預覽：先出計畫',
      title: '預覽模式 — 在世界資料夾外準備計畫，原存檔不動',
      description:
        '先在世界資料夾外產生預覽計畫或儲存翻譯進度，確認內容沒問題後才會實際寫入。',
    },
    write: {
      badge: '會修改存檔：先關遊戲',
      title: '寫入存檔模式 — 執行前請先關閉 Minecraft 並用世界副本測試',
      description:
        '這個操作會實際修改指定的世界資料夾。請務必先複製一份世界測試、關閉 Minecraft 與其他編輯器，再加上 --world-offline。',
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
      en: 'Scans the map and starts a new translation project split into easy-to-handle pages.',
      'zh-tw': '掃描地圖文字並建立新的翻譯進度，自動把文字切成方便處理的一頁一頁。',
    },
  },
  {
    subcommand: 'status',
    tier: 'safe',
    summary: {
      en: 'Shows how many text items are translated or still remaining, and checks if the world changed.',
      'zh-tw': '查看已翻譯與未翻譯的數量進度，並確認原世界檔案有沒有被改動過。',
    },
  },
  {
    subcommand: 'export',
    tier: 'safe',
    summary: {
      en: 'Exports one page of map text along with a ready-to-fill reply template.',
      'zh-tw': '匯出指定頁碼的地圖原文，並附上可直接填寫譯文的回覆範本。',
    },
  },
  {
    subcommand: 'advance',
    tier: 'preview',
    summary: {
      en: 'Submits a finished reply file (optional) and returns the next unfinished page right away.',
      'zh-tw': '可同時送出已填好的譯文檔並直接拿到下一頁還沒翻的內容（不帶譯文檔時則是查詢下一頁）。',
    },
  },
  {
    subcommand: 'submit',
    tier: 'preview',
    summary: {
      en: 'Checks and saves your completed translation reply file and updates the session progress.',
      'zh-tw': '檢查並儲存你填好的翻譯回覆檔，推進翻譯進度。',
    },
  },
  {
    subcommand: 'get',
    tier: 'safe',
    summary: {
      en: 'Looks up a single text entry by its slot ID, including original text and current translation.',
      'zh-tw': '依編號（slot ID）查看單筆文字的原文、限制規則與目前的譯文。',
    },
  },
  {
    subcommand: 'jump',
    tier: 'preview',
    summary: {
      en: 'Moves the current page pointer to a different page using a session-command JSON file.',
      'zh-tw': '透過設定檔（session-command）跳轉到指定的頁碼。',
    },
  },
  {
    subcommand: 'note',
    tier: 'preview',
    summary: {
      en: 'Reads or adds translator notes (for example, tone or character context) to the session.',
      'zh-tw': '查看或新增給譯者的備註說明（例如語氣或劇情背景）。',
    },
  },
  {
    subcommand: 'glossary',
    tier: 'preview',
    summary: {
      en: 'Reads or updates your glossary so recurring character, item, and place names stay consistent.',
      'zh-tw': '查看或更新專有名詞術語表，讓地圖裡反覆出現的人名、地名與物品譯名保持一致。',
    },
  },
  {
    subcommand: 'reset',
    tier: 'preview',
    summary: {
      en: 'Previews or resets chosen entries back to unfinished so you can re-translate them.',
      'zh-tw': '預覽或把指定幾筆譯文重置回未翻譯狀態，方便重新調整譯文。',
    },
  },
  {
    subcommand: 'groups',
    tier: 'safe',
    summary: {
      en: 'Lists groups of related text (for example, pages of the same book or lines of the same sign).',
      'zh-tw': '列出彼此相關的文字群組（例如同一本書的各頁或同一塊告示牌的多行文字）。',
    },
  },
  {
    subcommand: 'memory',
    tier: 'safe',
    summary: {
      en: 'Looks up reusable translations from older local translation sessions.',
      'zh-tw': '從本機過去的翻譯紀錄中查找可以沿用的譯文。',
    },
  },
  {
    subcommand: 'plan',
    tier: 'preview',
    summary: {
      en: 'Turns finished translations into a world update plan: --direct replaces text in-place, --keys converts text to translation keys + language file.',
      'zh-tw': '把完成的翻譯打包成世界更新計畫：--direct 直接替換地圖文字，--keys 則轉成翻譯鍵並產生語言檔。',
    },
  },
  {
    subcommand: 'export-lang',
    tier: 'preview',
    summary: {
      en: 'Previews or exports standalone original and translated .json language files without editing map NBT.',
      'zh-tw': '預覽或匯出獨立的原文／譯文 .json 語言檔，不更動地圖本身的 NBT 資料。',
    },
  },
  {
    subcommand: 'create-pack',
    tier: 'preview',
    summary: {
      en: 'Previews or builds a standalone Minecraft resource pack (.zip or folder), optionally merging into an existing pack.',
      'zh-tw': '預覽或建立獨立的 Minecraft 資源包（.zip 或資料夾），也可安全合併進既有的資源包。',
    },
  },
  {
    subcommand: 'seal',
    tier: 'preview',
    summary: {
      en: 'Marks a translation project as complete after confirming the world update succeeded.',
      'zh-tw': '在確認世界更新（apply）成功後，將這份翻譯進度正式標記為已完成並封存。',
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
    label: { en: 'Success', 'zh-tw': '執行成功' },
    meaning: {
      en: 'Everything finished normally (including a search that simply found zero matches).',
      'zh-tw': '指令正常完成（包含合法但沒有查到相符結果的搜尋）。',
    },
    action: {
      en: 'Continue to the next step.',
      'zh-tw': '可以繼續下一步。',
    },
  },
  {
    code: 1,
    label: { en: 'Unexpected Error', 'zh-tw': '非預期錯誤' },
    meaning: {
      en: 'An unexpected internal error happened.',
      'zh-tw': '程式內部發生非預期的錯誤。',
    },
    action: {
      en: 'Read error.message and error.details in the JSON output before trying again.',
      'zh-tw': '請查看輸出 JSON 中的 error.message 與 error.details 了解原因。',
    },
  },
  {
    code: 2,
    label: { en: 'Command Syntax Error', 'zh-tw': '指令或參數打錯' },
    meaning: {
      en: 'Unknown option, missing value, duplicate option, or conflicting flags.',
      'zh-tw': '輸入了不存在的參數、漏填參數值，或同時用了互斥的選項。',
    },
    action: {
      en: 'Run nbtcli help <command> (or <command> <subcommand> --help) to check spelling. No files were touched.',
      'zh-tw': '執行 nbtcli help <指令>（子命令用 <指令> <子命令> --help）核對寫法。不會改到任何檔案。',
    },
  },
  {
    code: 3,
    label: { en: 'World or Session Changed (Conflict)', 'zh-tw': '檔案已變動或衝突' },
    meaning: {
      en: 'The world files changed after you created the plan or started translating, so applying an old plan was blocked for safety.',
      'zh-tw': '建立計畫或翻譯進度後，世界檔案曾被其他程式改動過，因此工具為了保護存檔拒絕覆寫。',
    },
    action: {
      en: 'Generate a fresh plan from the updated world (or start a new translation session and reuse prior translations).',
      'zh-tw': '請針對最新的世界副本重新產生計畫（或開新翻譯進度並沿用先前譯文）。',
    },
  },
  {
    code: 4,
    label: { en: 'Unsupported Version or Feature', 'zh-tw': '尚未支援的版本或格式' },
    meaning: {
      en: 'The world uses a Minecraft version or format feature that is not supported for this operation.',
      'zh-tw': '這個存檔使用了目前操作尚未支援的 Minecraft 版本或資料結構。',
    },
    action: {
      en: 'Run nbtcli capabilities to check supported versions, or narrow your target with --path, --dimension, or --types.',
      'zh-tw': '執行 nbtcli capabilities 查看支援範圍，或加上 --path、--dimension、--types 縮小處理範圍。',
    },
  },
  {
    code: 5,
    label: { en: 'File Missing, Locked, or Damaged', 'zh-tw': '檔案找不到、被鎖定或損壞' },
    meaning: {
      en: 'A file is missing, currently locked by Minecraft or another app, or contains broken NBT/JSON data.',
      'zh-tw': '找不到指定檔案、檔案正被 Minecraft 或其他程式佔用，或檔案內容本身已損壞。',
    },
    action: {
      en: 'Close Minecraft, double-check the folder path, or run validate to find damaged files.',
      'zh-tw': '先確認已關閉 Minecraft、檢查路徑有沒有打錯，或用 validate 找出哪個檔案損壞。',
    },
  },
  {
    code: 6,
    label: { en: 'Safety Size or Time Limit Hit', 'zh-tw': '超過安全大小或時間上限' },
    meaning: {
      en: 'A file/output exceeded 64 MiB, a session exceeded 100,000 text items, depth exceeded 256 levels, or a regex pattern took over 100 ms.',
      'zh-tw': '檔案或輸出超過 64 MiB、翻譯項目超過 100,000 筆、結構深度超過 256 層，或正規表示式比對超過 100 毫秒。',
    },
    action: {
      en: 'Split the work using --dimension, --region, or --path, lower --page-size, or simplify --match-pattern.',
      'zh-tw': '用 --dimension、--region 或 --path 分批處理，調小 --page-size，或簡化搜尋條件。',
    },
  },
  {
    code: 7,
    label: { en: 'Interrupted Write — Recovery Needed', 'zh-tw': '寫入中斷，需要先執行復原' },
    meaning: {
      en: 'A world write stopped partway through (for example, the window was closed or crashed mid-write).',
      'zh-tw': '修改寫入到一半被中斷（例如視窗意外關閉），世界目前處於待復原保護狀態。',
    },
    action: {
      en: 'Do NOT open the world in Minecraft. Copy planHash and stateRoot from the output, run nbtcli status, then run nbtcli recover (or recover --rollback to undo).',
      'zh-tw': '千萬不要在 Minecraft 裡打開世界。記下輸出中的 planHash 與 stateRoot，先跑 nbtcli status，再跑 nbtcli recover 續寫或加 --rollback 還原。',
    },
  },
  {
    code: 8,
    label: { en: 'Rolled Back to Original', 'zh-tw': '已安全還原回修改前' },
    meaning: {
      en: 'nbtcli recover --rollback finished restoring your original world files from backup.',
      'zh-tw': '已透過 recover --rollback 把世界檔案完整還原成修改前的狀態。',
    },
    action: {
      en: 'Your original world is back to normal. You can safely review and create a new plan whenever ready.',
      'zh-tw': '你的原世界已恢復正常。若還要修改，重新產生新計畫即可。',
    },
  },
  {
    code: 130,
    label: { en: 'Cancelled', 'zh-tw': '操作已取消' },
    meaning: {
      en: 'The command was cancelled before completion.',
      'zh-tw': '操作在完成前被中止。',
    },
    action: {
      en: 'If this happened during apply, run status and recover before opening the world.',
      'zh-tw': '如果是在寫入途中取消的，開啟世界前請先用 status 與 recover 確認並復原。',
    },
  },
];

export interface ProfileMatrixRow {
  profile: string;
  dataVersion: number;
  packFormat: number;
  layout: string;
  itemSchema: string;
  textSchema: string;
  packLayout: string;
}

export const SUPPORTED_PROFILES: ProfileMatrixRow[] = [
  {
    profile: 'java-1.20.1',
    dataVersion: 3465,
    packFormat: 15,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-tags-1.20.4',
    textSchema: 'java-text-json (v1)',
    packLayout: 'java-pack-plural',
  },
  {
    profile: 'java-1.20.4',
    dataVersion: 3700,
    packFormat: 22,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-tags-1.20.4',
    textSchema: 'java-text-json (v1)',
    packLayout: 'java-pack-plural',
  },
  {
    profile: 'java-1.21',
    dataVersion: 3953,
    packFormat: 34,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-components-1.20.5',
    textSchema: 'java-text-json (v1)',
    packLayout: 'java-pack-singular',
  },
  {
    profile: 'java-1.21.5',
    dataVersion: 4325,
    packFormat: 55,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-components-1.21.5',
    textSchema: 'java-text-nbt (v1)',
    packLayout: 'java-pack-singular',
  },
  {
    profile: 'java-1.21.11',
    dataVersion: 4671,
    packFormat: 75,
    layout: 'java-dimensions-directory',
    itemSchema: 'java-item-components-1.21.11',
    textSchema: 'java-text-nbt (v2)',
    packLayout: 'java-pack-singular',
  },
  {
    profile: 'java-26.1-snapshot-6',
    dataVersion: 4774,
    packFormat: 80,
    layout: 'java-dimensions-namespaced',
    itemSchema: 'java-item-components-26.1-snapshot-6',
    textSchema: 'java-text-nbt (v2)',
    packLayout: 'java-pack-singular',
  },
];
