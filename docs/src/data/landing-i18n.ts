import { PRODUCT_META, type LocaleKey } from './cli-surface';

export interface LandingCopy {
  langAttr: string;
  navDocs: string;
  navCommands: string;
  navReleases: string;
  navGithub: string;
  switchLangLabel: string;
  switchLangHref: string;
  docsHomeHref: string;
  commandsHref: string;
  badgeText: string;
  heroTitleLine1: string;
  heroTitleHighlight: string;
  heroSubtitle: string;
  ctaDocs: string;
  ctaDownload: string;
  safetyTitle: string;
  safetySubtitle: string;
  safetyCards: Array<{
    tier: 'safe' | 'preview' | 'write';
    tag: string;
    title: string;
    body: string;
  }>;
  terminalTitle: string;
  terminalTabs: Array<{
    id: string;
    label: string;
    tierLabel: string;
    tier: 'safe' | 'preview' | 'write';
    command: string;
    output: string;
  }>;
  bentoTitle: string;
  bentoSubtitle: string;
  bentoItems: Array<{
    kicker: string;
    title: string;
    description: string;
    linkText: string;
    linkHref: string;
  }>;
  quickInstallTitle: string;
  quickInstallSubtitle: string;
  footerNote: string;
}

export const LANDING_COPY: Record<LocaleKey, LandingCopy> = {
  en: {
    langAttr: 'en',
    navDocs: 'Documentation',
    navCommands: 'Command Reference',
    navReleases: 'Releases',
    navGithub: 'GitHub',
    switchLangLabel: '繁體中文',
    switchLangHref: '/zh-tw/',
    docsHomeHref: '/docs/',
    commandsHref: '/docs/command-reference/',
    badgeText: `Windows x64 Self-Contained CLI · v${PRODUCT_META.version}`,
    heroTitleLine1: 'Precision Engineering for',
    heroTitleHighlight: 'Minecraft Java Worlds',
    heroSubtitle:
      'Inspect NBT structures, scan and translate adventure maps, build resource packs, and apply verified offline world edits — with built-in backups and crash recovery.',
    ctaDocs: 'Read the Documentation',
    ctaDownload: 'Download Windows x64 ZIP',
    safetyTitle: 'Clear Risk Levels Before You Run a Command',
    safetySubtitle:
      'Every command and guide in NBTExplorerCLI clearly states whether it only reads data, creates a preview plan outside your world, or modifies your save folder.',
    safetyCards: [
      {
        tier: 'safe',
        tag: 'SAFE TO INSPECT',
        title: 'Does Not Modify Your World',
        body: 'Commands like get, list, search, scan, extract, validate, and status only read files or external logs. Your world folder stays untouched.',
      },
      {
        tier: 'preview',
        tag: 'PREVIEW ONLY',
        title: 'Generates a Plan Outside the World',
        body: 'Edit and translation commands generate a reviewable plan file by default so you can inspect every change before touching any world file.',
      },
      {
        tier: 'write',
        tag: 'MODIFIES WORLD SAVE',
        title: 'Close Minecraft and Back Up First',
        body: 'Actual world writes require explicit --apply and --world-offline flags, automatically storing pre-edit backups and recovery journals.',
      },
    ],
    terminalTitle: 'Interactive Workflow Preview',
    terminalTabs: [
      {
        id: 'inspect',
        label: '1. Inspect World NBT',
        tierLabel: 'Safe to Inspect',
        tier: 'safe',
        command: `.\\nbtcli.exe get "D:\\WorldCopy" --path level.dat --pointer /Data/DataVersion`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "get",
  "outcome": "Success",
  "data": {
    "format": "display-json",
    "pointer": "/Data/DataVersion",
    "value": 3953
  },
  "error": null
}`,
      },
      {
        id: 'translate',
        label: '2. Translation Session',
        tierLabel: 'Preview Only',
        tier: 'preview',
        command: `.\\nbtcli.exe translate start "D:\\WorldCopy" --source-language en_us --target-language zh_tw --page-size 30`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "translate",
  "outcome": "Success",
  "data": {
    "status": {
      "sessionId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff",
      "revision": 0,
      "sourceLanguage": "en_us",
      "targetLanguage": "zh_tw",
      "totalSlots": 142
    }
  },
  "error": null
}`,
      },
      {
        id: 'apply',
        label: '3. Offline Apply & Seal',
        tierLabel: 'Modifies World Save',
        tier: 'write',
        command: `.\\nbtcli.exe apply "D:\\WorldCopy" --plan "D:\\Plans\\edit-plan.json" --world-offline`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "apply",
  "outcome": "Success",
  "data": {
    "receiptIsTerminal": true,
    "receipt": {
      "schemaFamily": "nbtcli.apply-receipt",
      "schemaVersion": 1,
      "outcome": "Applied"
    }
  },
  "error": null
}`,
      },
    ],
    bentoTitle: 'Built for Mapmakers, Translators, and Tool Builders',
    bentoSubtitle:
      'One deterministic JSON protocol across world inspection, localization workflows, resource pack generation, and crash-safe editing.',
    bentoItems: [
      {
        kicker: 'INSPECTION & SCANNING',
        title: 'Deep NBT, Region, and Datapack Discovery',
        description:
          'Query level.dat, region chunks (.mca), entity regions, and datapack JSON files with exact JSON Pointers or reversible typed-nbt output.',
        linkText: 'Install & Inspect Guide',
        linkHref: '/docs/getting-started/',
      },
      {
        kicker: 'MAP LOCALIZATION',
        title: 'Paginated Translation Sessions & Glossaries',
        description:
          'Break large adventure maps into manageable pages, maintain consistent terminology glossaries, and verify every slot before generating a plan.',
        linkText: 'Translation Workflow',
        linkHref: '/docs/translation-workflow/',
      },
      {
        kicker: 'RESOURCE PACKS & KEYS',
        title: 'In-World Text or Standalone Resource Packs',
        description:
          'Replace literal text directly, convert text to translation keys, or export standalone language files and merged resource packs.',
        linkText: 'Resource Packs Guide',
        linkHref: '/docs/resources-and-packs/',
      },
      {
        kicker: 'CRASH-SAFE EDITING',
        title: 'Pre-Write Backups, Journals & Recovery',
        description:
          'Every write stages candidates and records preimage hashes in an external state folder. Interrupted writes can be inspected with status and safely recovered or rolled back.',
        linkText: 'Safe Editing & Recovery',
        linkHref: '/docs/safe-editing/',
      },
    ],
    quickInstallTitle: 'Verify and Run in 60 Seconds',
    quickInstallSubtitle:
      'No separate .NET SDK installation required. Verify the SHA-256 checksum, extract all files together, and inspect a world copy.',
    footerNote:
      'Released under the MIT License. Bundled third-party components retain their respective licenses.',
  },
  'zh-tw': {
    langAttr: 'zh-TW',
    navDocs: '使用文檔',
    navCommands: '命令參考',
    navReleases: '版本下載',
    navGithub: 'GitHub',
    switchLangLabel: 'English',
    switchLangHref: '/',
    docsHomeHref: '/zh-tw/docs/',
    commandsHref: '/zh-tw/docs/command-reference/',
    badgeText: `Windows x64 獨立封裝 CLI · v${PRODUCT_META.version}`,
    heroTitleLine1: '專為 Minecraft Java 版打造的',
    heroTitleHighlight: '世界資料查閱與安全翻譯工程工具',
    heroSubtitle:
      '查閱 NBT 結構、掃描並翻譯大型冒險地圖、打包資源包，並在完整備份與中斷復原保護下安全編輯世界存檔。',
    ctaDocs: '閱讀完整文檔',
    ctaDownload: '下載 Windows x64 發行包',
    safetyTitle: '直觀明確的操作風險分級',
    safetySubtitle:
      '無需具備深厚的程式背景，文檔中的每個步驟與指令都會清楚標示是否會動到您的 Minecraft 世界存檔。',
    safetyCards: [
      {
        tier: 'safe',
        tag: '安全查閱：不改存檔',
        title: '完全不會修改您的世界存檔',
        body: '包含 get、list、search、scan、extract、validate 與 status 等指令僅讀取資料，您的地圖資料夾不會有任何變動。',
      },
      {
        tier: 'preview',
        tag: '預覽模式：僅產出計畫',
        title: '在世界資料夾外產生變更計畫',
        body: '編輯與翻譯指令預設只會在外部目錄建立計畫檔（Plan）或翻譯進度，讓您在實際寫入前能放心檢查所有細節。',
      },
      {
        tier: 'write',
        tag: '修改存檔：請先關遊戲並備份',
        title: '執行前請關閉遊戲並使用世界副本',
        body: '實際修改世界的指令必須明確加上 --apply 與 --world-offline，並會自動建立寫前備份與復原日誌。',
      },
    ],
    terminalTitle: '核心工作流互動預覽',
    terminalTabs: [
      {
        id: 'inspect',
        label: '1. 安全查閱 NBT',
        tierLabel: '安全查閱：不改存檔',
        tier: 'safe',
        command: `.\\nbtcli.exe get "D:\\WorldCopy" --path level.dat --pointer /Data/DataVersion`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "get",
  "outcome": "Success",
  "data": {
    "format": "display-json",
    "pointer": "/Data/DataVersion",
    "value": 3953
  },
  "error": null
}`,
      },
      {
        id: 'translate',
        label: '2. 建立翻譯工作階段',
        tierLabel: '預覽模式：僅產出計畫',
        tier: 'preview',
        command: `.\\nbtcli.exe translate start "D:\\WorldCopy" --source-language en_us --target-language zh_tw --page-size 30`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "translate",
  "outcome": "Success",
  "data": {
    "status": {
      "sessionId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff",
      "revision": 0,
      "sourceLanguage": "en_us",
      "targetLanguage": "zh_tw",
      "totalSlots": 142
    }
  },
  "error": null
}`,
      },
      {
        id: 'apply',
        label: '3. 離線套用計畫',
        tierLabel: '修改存檔：請先關遊戲並備份',
        tier: 'write',
        command: `.\\nbtcli.exe apply "D:\\WorldCopy" --plan "D:\\Plans\\edit-plan.json" --world-offline`,
        output: `{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "apply",
  "outcome": "Success",
  "data": {
    "receiptIsTerminal": true,
    "receipt": {
      "schemaFamily": "nbtcli.apply-receipt",
      "schemaVersion": 1,
      "outcome": "Applied"
    }
  },
  "error": null
}`,
      },
    ],
    bentoTitle: '為地圖創作者、翻譯團隊與工具開發者設計',
    bentoSubtitle:
      '統一的單行 JSON 輸出協定，涵蓋世界查閱、分頁翻譯、資源包生成與具備中斷復原能力的安全寫入。',
    bentoItems: [
      {
        kicker: 'INSPECTION & SCANNING',
        title: '深入查閱 NBT、區塊與資料包',
        description:
          '支援讀取 level.dat、區塊檔（.mca）、實體區塊與資料包 JSON，提供直觀的顯示格式與可完整還原的 typed-nbt 格式。',
        linkText: '查看安裝與查閱指南',
        linkHref: '/zh-tw/docs/getting-started/',
      },
      {
        kicker: 'MAP LOCALIZATION',
        title: '分頁翻譯工作階段與術語表管理',
        description:
          '將大型地圖中的文字切分為易於處理的分頁，透過術語表統一專有名詞，並在寫入前逐一驗證所有槽位。',
        linkText: '查看地圖翻譯工作流',
        linkHref: '/zh-tw/docs/translation-workflow/',
      },
      {
        kicker: 'RESOURCE PACKS & KEYS',
        title: '世界內文字替換與獨立資源包輸出',
        description:
          '可選擇直接替換世界文字、轉為翻譯鍵（Translation Keys），或匯出獨立語言檔並與既有資源包安全合併。',
        linkText: '查看資源包與語言檔指南',
        linkHref: '/zh-tw/docs/resources-and-packs/',
      },
      {
        kicker: 'CRASH-SAFE EDITING',
        title: '寫前備份、日誌追蹤與中斷復原',
        description:
          '每次寫入皆會在外部狀態目錄保存原始檔案雜湊與執行日誌。若寫入途中斷電或中斷，可透過 status 與 recover 安全完成或退回原狀。',
        linkText: '查看安全編輯與復原指南',
        linkHref: '/zh-tw/docs/safe-editing/',
      },
    ],
    quickInstallTitle: '一分鐘完成雜湊驗證與首次查閱',
    quickInstallSubtitle:
      '壓縮包已內建 .NET 執行環境，無需另外安裝 SDK。請先核對 SHA-256 雜湊、完整解壓縮，並在世界副本上執行查閱。',
    footerNote: '本專案採用 MIT 授權條款釋出。隨附之第三方元件保留其各自之授權與聲明。',
  },
};
