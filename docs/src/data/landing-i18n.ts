import { PRODUCT_META, type LocaleKey } from './cli-surface';

type TerminalExample = {
  id: string;
  label: string;
  tier: 'safe' | 'preview' | 'write';
  status: string;
  command: string;
  output: string;
  note: string;
};

export interface LandingCopy {
  langAttr: string;
  pageTitle: string;
  skipLink: string;
  navigationLabel: string;
  menuOpen: string;
  menuClose: string;
  navFeatures: string;
  navWorkflow: string;
  navDocs: string;
  switchLangLabel: string;
  switchLangHref: string;
  docsHomeHref: string;
  gettingStartedHref: string;
  commandsHref: string;
  safetyHref: string;
  badgeText: string;
  heroLines: string[];
  heroSubtitle: string;
  ctaDocs: string;
  ctaDownload: string;
  runtimeIncluded: string;
  terminalTitle: string;
  responseLabel: string;
  exampleNote: string;
  terminalTabs: TerminalExample[];
  proofItems: string[];
  featuresEyebrow: string;
  featuresTitle: string;
  featuresSubtitle: string;
  features: Array<{
    kicker: string;
    title: string;
    description: string;
    commands: string;
    linkLabel: string;
    href: string;
  }>;
  workflowEyebrow: string;
  workflowTitle: string;
  workflowSubtitle: string;
  workflowLink: string;
  workflowSteps: Array<{ title: string; body: string }>;
  writeNote: string;
  safetyLink: string;
  installEyebrow: string;
  installTitle: string;
  installSubtitle: string;
  installLink: string;
  installSteps: Array<{ id: string; title: string; command: string; hint: string }>;
  copyLabel: string;
  copyCommandLabel: string;
  copiedLabel: string;
  copySuccess: string;
  copyFailure: string;
  footerNote: string;
}

const executable = '.\\' + PRODUCT_META.binary;
const inspectCommand = executable + ' get "D:\\WorldCopy" --path level.dat --pointer /Data/DataVersion';

const examples = {
  inspect: {
    id: 'inspect',
    tier: 'safe' as const,
    command: inspectCommand,
    output: JSON.stringify({
      success: true,
      command: 'get',
      data: { representation: 'display-json', tagType: 'Int', value: 3953 },
    }, null, 2),
  },
  translate: {
    id: 'translate',
    tier: 'preview' as const,
    command: executable + ' translate start "D:\\WorldCopy" --source-language en_us --target-language zh_tw --page-size 30',
    output: JSON.stringify({
      success: true,
      command: 'translate',
      data: { status: { revision: 0, total: 142 } },
    }, null, 2),
  },
  apply: {
    id: 'apply',
    tier: 'write' as const,
    command: executable + ' apply "D:\\WorldCopy" --plan "D:\\Plans\\edit-plan.json" --world-offline',
    output: JSON.stringify({
      success: true,
      command: 'apply',
      data: {
        receiptIsTerminal: true,
        receipt: { data: {
          results: [{ operationId: 'edit-0', outcome: 'applied' }],
          counts: [{ outcome: 'applied', count: 1 }],
        } },
      },
    }, null, 2),
  },
};

const installCommands = {
  verify: 'Get-FileHash .\\' + PRODUCT_META.zipName + ' -Algorithm SHA256\nGet-Content .\\' + PRODUCT_META.checksumFile,
  extract: 'Expand-Archive .\\' + PRODUCT_META.zipName + ' -DestinationPath .\\nbtcli\ncd .\\nbtcli',
  inspect: inspectCommand,
};

export const LANDING_COPY: Record<LocaleKey, LandingCopy> = {
  en: {
    langAttr: 'en',
    pageTitle: 'Explore, translate, and edit Minecraft worlds',
    skipLink: 'Skip to content',
    navigationLabel: 'Main navigation',
    menuOpen: 'Open navigation',
    menuClose: 'Close navigation',
    navFeatures: 'Features',
    navWorkflow: 'Workflow',
    navDocs: 'Docs',
    switchLangLabel: '繁體中文',
    switchLangHref: '/zh-tw/',
    docsHomeHref: '/docs/',
    gettingStartedHref: '/docs/getting-started/',
    commandsHref: '/docs/command-reference/',
    safetyHref: '/docs/safe-editing/',
    badgeText: 'WINDOWS x64 · JAVA EDITION',
    heroLines: ['Explore. Translate.', 'Edit your world.'],
    heroSubtitle: 'Bring NBT exploration, map localization, resource packs, and world editing into one command-line workflow. Built for mapmakers, translators, and tool builders.',
    ctaDocs: 'Read the guide',
    ctaDownload: 'View releases',
    runtimeIncluded: 'Runtime included',
    terminalTitle: 'Try the workflows',
    responseLabel: 'RESPONSE EXCERPT · JSON',
    exampleNote: 'Illustrative output, shortened for readability. The CLI emits one JSON line.',
    terminalTabs: [
      { ...examples.inspect, label: 'Inspect NBT', status: 'Read only · world files unchanged', note: 'Find a precise value in level.dat with a JSON Pointer.' },
      { ...examples.translate, label: 'Translate', status: 'Creates an external session · world files unchanged', note: 'Organize map text into pages for translation by you or your preferred external tools.' },
      { ...examples.apply, label: 'Apply a plan', status: 'Writes to the world · close Minecraft and other editors first', note: 'Apply a reviewed plan offline, with pre-edit backups and recovery journals.' },
    ],
    proofItems: ['NBT + JSON', 'MAP LOCALIZATION', 'WORLD EDITING'],
    featuresEyebrow: 'BUILT FOR YOUR WORLD',
    featuresTitle: 'One toolkit.\nMore possibilities for your world.',
    featuresSubtitle: 'From a single NBT tag to the text and resources of an entire adventure map.',
    features: [
      {
        kicker: '01 / EXPLORE',
        title: 'Explore NBT and world data',
        description: 'Browse level.dat, region chunks, and datapack JSON. Pinpoint the values you need with JSON Pointers.',
        commands: 'get · scan',
        linkLabel: 'Explore world data',
        href: '/docs/getting-started/',
      },
      {
        kicker: '02 / LOCALIZE',
        title: 'Localize entire adventure maps',
        description: 'Work through text in pages, keep terminology consistent, and hand translations to people or external tools.',
        commands: 'translate start · advance',
        linkLabel: 'See the translation workflow',
        href: '/docs/translation-workflow/',
      },
      {
        kicker: '03 / RESOURCES',
        title: 'Build language files and packs',
        description: 'Replace text, convert it to translation keys, and export language files or merged resource packs.',
        commands: 'translate export-lang · create-pack',
        linkLabel: 'Explore resources and packs',
        href: '/docs/resources-and-packs/',
      },
      {
        kicker: '04 / EDIT',
        title: 'Edit with a reviewable plan',
        description: 'Prepare precise changes, apply them offline, and inspect recovery journals when you need to roll back.',
        commands: 'apply · recover',
        linkLabel: 'Explore editing and recovery',
        href: '/docs/safe-editing/',
      },
    ],
    workflowEyebrow: 'A WORKFLOW THAT CONNECTS',
    workflowTitle: 'From world data\nto your next creation.',
    workflowSubtitle: 'Explore content, organize translations, and prepare changes. Structured JSON output connects individual work, team workflows, and your own scripts.',
    workflowLink: 'Explore the command reference',
    workflowSteps: [
      { title: 'Discover the content', body: 'Inspect NBT paths and scan map text to understand the material you are working with.' },
      { title: 'Organize the work', body: 'Use paginated translation sessions and glossaries to manage progress and terminology.' },
      { title: 'Create the result', body: 'Export resources or prepare an edit plan, then apply world changes offline.' },
    ],
    writeNote: 'Before writing, keep a world copy and close Minecraft and other editors.',
    safetyLink: 'Editing guide',
    installEyebrow: 'READY WHEN YOU ARE',
    installTitle: 'Your next world project\nstarts here.',
    installSubtitle: 'Download the archive and ' + PRODUCT_META.checksumFile + ', verify the checksum, then extract the entire package. The runtime is included.',
    installLink: 'Read the getting-started guide',
    installSteps: [
      { id: 'verify', title: '01 / Compare SHA-256', command: installCommands.verify, hint: 'Compare the hash with the matching filename in ' + PRODUCT_META.checksumFile + ' before extracting.' },
      { id: 'extract', title: '02 / Extract the full archive', command: installCommands.extract, hint: 'Keep the DLL and JSON files alongside ' + PRODUCT_META.binary + '.' },
      { id: 'inspect', title: '03 / Inspect a world copy', command: installCommands.inspect, hint: 'Replace D:\\WorldCopy with your world copy. This first command does not modify it.' },
    ],
    copyLabel: 'Copy',
    copyCommandLabel: 'Copy command',
    copiedLabel: 'Copied',
    copySuccess: 'Command copied to clipboard.',
    copyFailure: 'Clipboard unavailable. Select the command and copy it manually.',
    footerNote: 'Released under the MIT License. Bundled third-party components retain their respective licenses. Not an official Mojang or Microsoft product.',
  },
  'zh-tw': {
    langAttr: 'zh-TW',
    pageTitle: 'Minecraft 世界資料、翻譯與編輯工具',
    skipLink: '跳至主要內容',
    navigationLabel: '主要導覽',
    menuOpen: '開啟導覽選單',
    menuClose: '關閉導覽選單',
    navFeatures: '功能',
    navWorkflow: '工作流程',
    navDocs: '文件',
    switchLangLabel: 'English',
    switchLangHref: '/',
    docsHomeHref: '/zh-tw/docs/',
    gettingStartedHref: '/zh-tw/docs/getting-started/',
    commandsHref: '/zh-tw/docs/command-reference/',
    safetyHref: '/zh-tw/docs/safe-editing/',
    badgeText: 'WINDOWS x64 · JAVA EDITION',
    heroLines: ['探索、翻譯、編輯', 'Minecraft 世界。'],
    heroSubtitle: '把 NBT 探索、地圖在地化、資源包與世界編輯，串成流暢的命令列工作流程。\n為地圖作者、翻譯團隊與工具開發者打造。',
    ctaDocs: '閱讀入門指南',
    ctaDownload: '前往版本下載',
    runtimeIncluded: '免另裝 .NET',
    terminalTitle: '探索指令工作流程',
    responseLabel: '回應節錄 · JSON',
    exampleNote: '此為便於閱讀的示例節錄；CLI 實際輸出為單行 JSON。',
    terminalTabs: [
      { ...examples.inspect, label: '查閱 NBT', status: '唯讀查閱 · 世界檔案不變更', note: '透過 JSON Pointer，精準查閱 level.dat 中的資料。' },
      { ...examples.translate, label: '翻譯工作階段', status: '建立外部工作階段 · 世界檔案不變更', note: '將地圖文字整理成分頁，搭配人工或偏好的外部工具完成翻譯。' },
      { ...examples.apply, label: '套用計畫', status: '修改世界 · 請先關閉 Minecraft 與其他編輯器', note: '離線套用已確認的計畫，並保存寫前備份與復原日誌。' },
    ],
    proofItems: ['NBT + JSON', '地圖在地化', '世界編輯'],
    featuresEyebrow: 'BUILT FOR YOUR WORLD',
    featuresTitle: '一套工具，\n深入你的世界。',
    featuresSubtitle: '從一個 NBT 標籤，到整張冒險地圖的文字與資源。',
    features: [
      {
        kicker: '01 / EXPLORE',
        title: '探索 NBT 與世界資料',
        description: '深入 level.dat、區域資料與資料包 JSON，用 JSON Pointer 精準定位需要的內容。',
        commands: 'get · scan',
        linkLabel: '探索世界資料',
        href: '/zh-tw/docs/getting-started/',
      },
      {
        kicker: '02 / LOCALIZE',
        title: '完成整張地圖的在地化',
        description: '分頁處理冒險地圖文字、維護術語一致性，搭配人工或外部工具完成翻譯。',
        commands: 'translate start · advance',
        linkLabel: '查看翻譯工作流程',
        href: '/zh-tw/docs/translation-workflow/',
      },
      {
        kicker: '03 / RESOURCES',
        title: '產出語言檔與資源包',
        description: '替換文字、轉為翻譯鍵，或匯出獨立語言檔與合併資源包，配合你的發佈方式。',
        commands: 'translate export-lang · create-pack',
        linkLabel: '探索資源與資料包',
        href: '/zh-tw/docs/resources-and-packs/',
      },
      {
        kicker: '04 / EDIT',
        title: '用計畫完成精準編輯',
        description: '整理與確認變更後離線套用；寫前備份與復原日誌，讓後續維護有跡可循。',
        commands: 'apply · recover',
        linkLabel: '查看編輯與復原方式',
        href: '/zh-tw/docs/safe-editing/',
      },
    ],
    workflowEyebrow: 'A WORKFLOW THAT CONNECTS',
    workflowTitle: '從世界資料，\n到你的下一個作品。',
    workflowSubtitle: '探索內容、整理翻譯、產出變更。結構化 JSON 輸出，讓個人作業、團隊協作與自訂腳本接得起來。',
    workflowLink: '查看完整命令參考',
    workflowSteps: [
      { title: '探索地圖內容', body: '查閱 NBT 路徑、掃描地圖文字，掌握要處理的內容。' },
      { title: '整理翻譯與變更', body: '透過分頁翻譯工作階段與詞彙表，管理進度與術語。' },
      { title: '產出你的成果', body: '匯出資源，或產生編輯計畫並離線套用世界變更。' },
    ],
    writeNote: '寫入前請保留世界副本，並關閉 Minecraft 與其他編輯器。',
    safetyLink: '編輯指南',
    installEyebrow: 'READY WHEN YOU ARE',
    installTitle: '下一個世界專案，\n從這裡開始。',
    installSubtitle: '下載壓縮檔與 ' + PRODUCT_META.checksumFile + '，核對後完整解壓縮。執行階段已包含在內。',
    installLink: '查看完整入門指南',
    installSteps: [
      { id: 'verify', title: '01 / 比對 SHA-256', command: installCommands.verify, hint: '確認雜湊值與 ' + PRODUCT_META.checksumFile + ' 中的對應檔名一致後，再進行解壓縮。' },
      { id: 'extract', title: '02 / 完整解壓縮', command: installCommands.extract, hint: '保留 DLL 與 JSON 檔案，不要只移動 ' + PRODUCT_META.binary + '。' },
      { id: 'inspect', title: '03 / 查閱世界副本', command: installCommands.inspect, hint: '將 D:\\WorldCopy 換成你的世界副本；第一個指令不會修改世界。' },
    ],
    copyLabel: '複製',
    copyCommandLabel: '複製指令',
    copiedLabel: '已複製',
    copySuccess: '指令已複製到剪貼簿。',
    copyFailure: '無法存取剪貼簿，請選取指令並手動複製。',
    footerNote: '本專案採用 MIT 授權條款釋出。隨附第三方元件保留各自授權與聲明。非 Mojang 或 Microsoft 官方產品。',
  },
};
