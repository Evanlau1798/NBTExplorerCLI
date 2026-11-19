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
  extract: 'Expand-Archive .\\' + PRODUCT_META.zipName + ' -DestinationPath .\\nbtcli\ncd .\\nbtcli',
  check: executable + ' --version',
  inspect: inspectCommand,
};

export const LANDING_COPY: Record<LocaleKey, LandingCopy> = {
  en: {
    langAttr: 'en',
    pageTitle: 'Explore, translate, and safely edit Minecraft worlds',
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
    commandsHref: '/docs/recipes/',
    safetyHref: '/docs/safe-editing/',
    badgeText: 'WINDOWS x64 · JAVA EDITION',
    heroLines: ['Explore. Translate.', 'Edit your world.'],
    heroSubtitle: 'A ready-to-run Windows tool for inspecting Minecraft saves, translating adventure maps, building resource packs, and making safe offline edits — for players, translators, and AI assistants.',
    ctaDocs: 'Read the guide',
    ctaDownload: 'Download release',
    runtimeIncluded: 'No .NET install needed',
    terminalTitle: 'See how it works',
    responseLabel: 'EXAMPLE OUTPUT · JSON',
    exampleNote: 'Formatted for easy reading. The tool outputs one clean JSON line per command.',
    terminalTabs: [
      { ...examples.inspect, label: 'Inspect Save', status: 'Read only · world files stay untouched', note: 'Check any value in level.dat without opening the game.' },
      { ...examples.translate, label: 'Translate Map', status: 'Preview only · world files stay untouched', note: 'Split map text into manageable pages for you or your AI assistant to translate.' },
      { ...examples.apply, label: 'Apply Changes', status: 'Modifies world · close Minecraft first', note: 'Apply a reviewed plan offline with automatic backup and crash recovery.' },
    ],
    proofItems: ['READ-ONLY INSPECTION', 'ADVENTURE MAP TRANSLATION', 'AI AGENT READY'],
    featuresEyebrow: 'WHAT YOU CAN DO',
    featuresTitle: 'One toolkit.\nEvery part of your world.',
    featuresSubtitle: 'From checking a single save setting to translating an entire adventure map.',
    features: [
      {
        kicker: '01 / EXPLORE',
        title: 'Inspect saves without risk',
        description: 'Look inside level.dat, region chunks, and datapacks in read-only mode so your world is never altered by accident.',
        commands: 'get · list · scan',
        linkLabel: 'Start with safe inspection',
        href: '/docs/getting-started/',
      },
      {
        kicker: '02 / TRANSLATE',
        title: 'Translate whole adventure maps',
        description: 'Collect signs, books, items, entities, and commands into clean pages, keep terms consistent, and translate by hand or with AI.',
        commands: 'translate start · export · submit',
        linkLabel: 'Read the translation guide',
        href: '/docs/translation-workflow/',
      },
      {
        kicker: '03 / RESOURCE PACKS',
        title: 'Export language files & packs',
        description: 'Write translations directly into the map, switch to translation keys, or build and merge a ready-to-use .zip resource pack.',
        commands: 'export-lang · create-pack',
        linkLabel: 'See resource pack options',
        href: '/docs/resources-and-packs/',
      },
      {
        kicker: '04 / SAFE EDITS',
        title: 'Preview first, edit safely',
        description: 'Every edit makes a plan before touching disk, saves an automatic backup outside your world, and lets you roll back if interrupted.',
        commands: 'set · apply · recover',
        linkLabel: 'Learn safe editing & recovery',
        href: '/docs/safe-editing/',
      },
    ],
    workflowEyebrow: 'SIMPLE BY DESIGN',
    workflowTitle: 'Three clear steps,\nzero guesswork.',
    workflowSubtitle: 'Explore what is inside your map, prepare your translations or edits outside the world folder, and apply only when you are ready.',
    workflowLink: 'Browse common task recipes',
    workflowSteps: [
      { title: 'Inspect your world copy', body: 'Read world info and scan map text in read-only mode so you know exactly what is inside.' },
      { title: 'Prepare edits or translations', body: 'Work page by page or build a preview plan outside your save folder — nothing changes yet.' },
      { title: 'Apply safely or export a pack', body: 'Export a standalone resource pack, or close Minecraft and apply your plan with automatic backups.' },
    ],
    writeNote: 'Always test on a copy of your world and close Minecraft before applying changes.',
    safetyLink: 'Safe editing guide',
    installEyebrow: 'GET STARTED IN 1 MINUTE',
    installTitle: 'Download, unzip,\nand run.',
    installSubtitle: 'Grab ' + PRODUCT_META.zipName + ' from GitHub Releases and extract all files into one folder. Everything you need is already bundled inside.',
    installLink: 'Read the step-by-step setup guide',
    installSteps: [
      { id: 'extract', title: '01 / Unzip the download', command: installCommands.extract, hint: 'Extract all files together — keep the .dll and .json files next to ' + PRODUCT_META.binary + '.' },
      { id: 'check', title: '02 / Check that it runs', command: installCommands.check, hint: 'You should see "success": true and the version number.' },
      { id: 'inspect', title: '03 / Inspect a world copy', command: installCommands.inspect, hint: 'Replace D:\\WorldCopy with your copied save folder. This command is 100% read-only.' },
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
    pageTitle: 'Minecraft 存檔查閱、地圖翻譯與安全編輯工具',
    skipLink: '跳至主要內容',
    navigationLabel: '主要導覽',
    menuOpen: '開啟導覽選單',
    menuClose: '關閉導覽選單',
    navFeatures: '功能特色',
    navWorkflow: '使用流程',
    navDocs: '線上教學',
    switchLangLabel: 'English',
    switchLangHref: '/',
    docsHomeHref: '/zh-tw/docs/',
    gettingStartedHref: '/zh-tw/docs/getting-started/',
    commandsHref: '/zh-tw/docs/recipes/',
    safetyHref: '/zh-tw/docs/safe-editing/',
    badgeText: 'WINDOWS x64 · JAVA 版存檔',
    heroLines: ['查存檔、翻地圖、', '安全改世界。'],
    heroSubtitle: '解壓縮就能用的 Windows 工具，幫你查閱 Minecraft Java 版存檔、翻譯整張冒險地圖、打包資源包，以及安全進行離線編輯。\n適合一般玩家、地圖作者、翻譯團隊與 AI 助手。',
    ctaDocs: '看新手入門教學',
    ctaDownload: '前往 GitHub 下載',
    runtimeIncluded: '免另外安裝 .NET',
    terminalTitle: '看看怎麼使用',
    responseLabel: '輸出範例 · JSON',
    exampleNote: '為了方便閱讀已排版；實際執行時每個指令會輸出一行乾淨的 JSON。',
    terminalTabs: [
      { ...examples.inspect, label: '查閱存檔', status: '純讀取 · 完全不改世界檔案', note: '不用開遊戲，就能查看 level.dat 裡的任何資訊。' },
      { ...examples.translate, label: '翻譯地圖', status: '僅預覽 · 完全不改世界檔案', note: '自動把整張地圖的文字整理成分頁，自己翻或交給 AI 翻都方便。' },
      { ...examples.apply, label: '套用變更', status: '會修改存檔 · 請先關閉 Minecraft', note: '離線套用檢查過的計畫，寫入前自動備份、中斷也能還原。' },
    ],
    proofItems: ['零風險唯讀查閱', '整張冒險地圖翻譯', '專為 AI 助手優化'],
    featuresEyebrow: '這個工具可以幫你做什麼',
    featuresTitle: '一套工具，\n搞定地圖大小事。',
    featuresSubtitle: '從查一個存檔設定，到翻譯整張冒險地圖與打包資源包。',
    features: [
      {
        kicker: '01 / 探索查閱',
        title: '零風險查看存檔內容',
        description: '以純讀取方式查看 level.dat、區塊檔與資料包內容，不怕手滑把心愛的存檔改壞。',
        commands: 'get · list · scan',
        linkLabel: '從安全查閱開始',
        href: '/zh-tw/docs/getting-started/',
      },
      {
        kicker: '02 / 地圖翻譯',
        title: '翻譯整張冒險地圖',
        description: '自動整理告示牌、書本、物品、實體與指令文字，一頁一頁翻譯並管理術語表，支援搭配 AI 協助。',
        commands: 'translate start · export · submit',
        linkLabel: '看冒險地圖翻譯教學',
        href: '/zh-tw/docs/translation-workflow/',
      },
      {
        kicker: '03 / 資源包',
        title: '匯出語言檔與資源包',
        description: '可把譯文直接寫回地圖、轉成翻譯鍵，或直接打包並合併成即用的 .zip 客戶端資源包。',
        commands: 'export-lang · create-pack',
        linkLabel: '了解資源包輸出選項',
        href: '/zh-tw/docs/resources-and-packs/',
      },
      {
        kicker: '04 / 安全編輯',
        title: '先預覽計畫，再安全寫入',
        description: '改檔前先產出預覽計畫並自動備份在世界外；萬一寫到一半中斷，也能安心續寫或一鍵還原。',
        commands: 'set · apply · recover',
        linkLabel: '看安全編輯與復原教學',
        href: '/zh-tw/docs/safe-editing/',
      },
    ],
    workflowEyebrow: '簡單好懂的操作邏輯',
    workflowTitle: '三個步驟，\n安心完成修改。',
    workflowSubtitle: '先看清楚存檔裡有什麼、在世界資料夾外準備好翻譯或修改計畫，確認沒問題再一次套用。',
    workflowLink: '瀏覽常見任務速查',
    workflowSteps: [
      { title: '先對世界副本做檢查', body: '用純讀取指令查看存檔結構、掃描所有文字，完全不會改動檔案。' },
      { title: '在世界外準備翻譯或計畫', body: '分頁翻譯文字或產出修改預覽計畫，在正式套用前原存檔完全不動。' },
      { title: '匯出資源包或離線套用', body: '選擇匯出獨立資源包，或關閉 Minecraft 後安全寫入世界並保留自動備份。' },
    ],
    writeNote: '修改存檔前，請務必複製一份世界測試並先關閉 Minecraft。',
    safetyLink: '看安全編輯教學',
    installEyebrow: '一分鐘立即上手',
    installTitle: '下載、解壓縮，\n馬上開始。',
    installSubtitle: '從 GitHub Releases 下載 ' + PRODUCT_META.zipName + '，把全部檔案解壓縮到同一個資料夾就能執行，不需另外安裝環境。',
    installLink: '看完整新手安裝步驟',
    installSteps: [
      { id: 'extract', title: '01 / 解壓縮整個檔案', command: installCommands.extract, hint: '請把壓縮檔內全部的 .dll 與 .json 檔跟 ' + PRODUCT_META.binary + ' 放在同一個資料夾。' },
      { id: 'check', title: '02 / 測試啟動是否正常', command: installCommands.check, hint: '看到 "success": true 與版本號，就代表可以使用了。' },
      { id: 'inspect', title: '03 / 對世界副本試跑唯讀查詢', command: installCommands.inspect, hint: '把 D:\\WorldCopy 換成你複製出來的測試世界路徑，這個指令完全不會改檔。' },
    ],
    copyLabel: '複製',
    copyCommandLabel: '複製指令',
    copiedLabel: '已複製',
    copySuccess: '已複製指令到剪貼簿。',
    copyFailure: '無法自動複製，請手動選取指令複製。',
    footerNote: '本專案採用 MIT 授權條款釋出。隨附第三方元件保留各自授權與聲明。非 Minecraft、Mojang 或 Microsoft 官方產品。',
  },
};
