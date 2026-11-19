import type { LocaleKey } from './cli-surface';

export interface HeroSceneCopy {
  id: 'translate' | 'inspect' | 'pack';
  label: string;
  worldLabel: string;
  outputLabel: string;
  bridgeCmd: string;
}

export interface LandingCopy {
  langAttr: string;
  pageTitle: string;
  skipLink: string;
  navigationLabel: string;
  menuOpen: string;
  menuClose: string;
  navDocs: string;
  navAgents: string;
  navDownload: string;
  switchLangLabel: string;
  switchLangHref: string;
  docsHomeHref: string;
  gettingStartedHref: string;
  translationHref: string;
  packsHref: string;
  safetyHref: string;
  agentsHref: string;
  llmsHref: string;
  heroTitlePlain: string;
  heroTitleAccent: string;
  heroSubtitle: string;
  ctaDownload: string;
  ctaDocs: string;
  stageAriaLabel: string;
  heroScenes: HeroSceneCopy[];
  heroTranslate: {
    signHeader: string;
    itemHeader: string;
    toggleOriginal: string;
    toggleTranslated: string;
    signOriginal: string[];
    signTranslated: string[];
    itemNameOriginal: string;
    itemNameTranslated: string;
    itemLoreOriginal: string;
    itemLoreTranslated: string;
    jsonLocaleOriginal: string;
    jsonLocaleTranslated: string;
  };
  heroInspect: {
    worldName: string;
    layers: Array<{ file: string; detail: string }>;
    jsonFields: Array<{ key: string; val: string; type: 'str' | 'num' }>;
  };
  heroPack: {
    packName: string;
    entries: Array<{ path: string; tag: string }>;
    jsonFields: Array<{ key: string; val: string; type: 'str' | 'num' }>;
  };
  bentoTitle: string;
  bentoTranslate: {
    title: string;
    description: string;
    href: string;
    linkLabel: string;
    btnOriginal: string;
    btnTranslated: string;
    signMeta: string;
    signOriginal: string[];
    signTranslated: string[];
    bookTitleOriginal: string;
    bookTitleTranslated: string;
    bookLineOriginal: string;
    bookLineTranslated: string;
    progressLabel: string;
  };
  bentoInspect: {
    title: string;
    description: string;
    href: string;
    linkLabel: string;
    rootLabel: string;
    nodes: Array<{
      id: string;
      key: string;
      type: string;
      value?: string;
      children?: Array<{ key: string; type: string; value: string }>;
    }>;
  };
  bentoPack: {
    title: string;
    description: string;
    href: string;
    linkLabel: string;
    modeDirectLabel: string;
    modeZipLabel: string;
    directFiles: Array<{ path: string; note: string }>;
    zipFiles: Array<{ path: string; note: string }>;
  };
  bentoSafe: {
    title: string;
    description: string;
    href: string;
    linkLabel: string;
    checkpoints: Array<{
      id: string;
      label: string;
      detail: string;
      status: string;
    }>;
  };
  ctaTitle: string;
  ctaSubtitle: string;
  ctaPrimary: string;
  ctaQuickstart: string;
  ctaAgents: string;
  footerNote: string;
}

export const LANDING_COPY: Record<LocaleKey, LandingCopy> = {
  en: {
    langAttr: 'en',
    pageTitle: 'Inspect, translate, and safely edit Minecraft Java worlds',
    skipLink: 'Skip to content',
    navigationLabel: 'Main navigation',
    menuOpen: 'Open menu',
    menuClose: 'Close menu',
    navDocs: 'Docs',
    navAgents: 'AI Agents',
    navDownload: 'Download',
    switchLangLabel: '繁體中文',
    switchLangHref: '/zh-tw/',
    docsHomeHref: '/docs/',
    gettingStartedHref: '/docs/getting-started/',
    translationHref: '/docs/translation-workflow/',
    packsHref: '/docs/resources-and-packs/',
    safetyHref: '/docs/safe-editing/',
    agentsHref: '/docs/ai-agents/',
    llmsHref: '/llms.txt',
    heroTitlePlain: 'Minecraft worlds,',
    heroTitleAccent: 'clearly decoded.',
    heroSubtitle:
      'Inspect saves, translate entire adventure maps, and export resource packs — safely offline.',
    ctaDownload: 'Download',
    ctaDocs: 'Explore Docs',
    stageAriaLabel: 'Interactive world transformation preview',
    heroScenes: [
      {
        id: 'translate',
        label: 'Translate Map',
        worldLabel: 'Map Text Slots',
        outputLabel: 'Translated Output',
        bridgeCmd: 'translate',
      },
      {
        id: 'inspect',
        label: 'Inspect Save',
        worldLabel: 'World Save',
        outputLabel: 'NBT JSON',
        bridgeCmd: 'get',
      },
      {
        id: 'pack',
        label: 'Export Pack',
        worldLabel: 'Resource Pack',
        outputLabel: 'Pack Manifest',
        bridgeCmd: 'create-pack',
      },
    ],
    heroTranslate: {
      signHeader: 'sign · 128, 64, -42',
      itemHeader: 'item.lore · slot #14',
      toggleOriginal: 'Original',
      toggleTranslated: 'Translated',
      signOriginal: ['SUNKEN CITADEL', 'Enter the vault', 'to claim the key'],
      signTranslated: ['沉沒的要塞', '進入地下寶庫', '尋找遺落之鑰'],
      itemNameOriginal: 'Blade of Dawn',
      itemNameTranslated: '曙光之刃',
      itemLoreOriginal: 'Forged beneath the ancient spire',
      itemLoreTranslated: '鍛造於遠古尖塔之下',
      jsonLocaleOriginal: 'en_us',
      jsonLocaleTranslated: 'zh_tw',
    },
    heroInspect: {
      worldName: 'Sunken Citadel',
      layers: [
        { file: 'level.dat', detail: 'DataVersion 3953 · Java 1.21' },
        { file: 'region/r.0.0.mca', detail: '32 x 32 chunks indexed' },
        { file: 'datapacks/quest/', detail: '48 functions · read-only' },
      ],
      jsonFields: [
        { key: 'LevelName', val: '"Sunken Citadel"', type: 'str' },
        { key: 'DataVersion', val: '3953', type: 'num' },
        { key: 'GameType', val: '2', type: 'num' },
        { key: 'Difficulty', val: '2', type: 'num' },
      ],
    },
    heroPack: {
      packName: 'citadel-zh_tw.zip',
      entries: [
        { path: 'pack.mcmeta', tag: 'format 34' },
        { path: 'assets/minecraft/lang/en_us.json', tag: '142 keys' },
        { path: 'assets/minecraft/lang/zh_tw.json', tag: '142 keys' },
      ],
      jsonFields: [
        { key: 'archive', val: '"citadel-zh_tw.zip"', type: 'str' },
        { key: 'packFormat', val: '34', type: 'num' },
        { key: 'entries', val: '142', type: 'num' },
        { key: 'worldModified', val: 'false', type: 'num' },
      ],
    },
    bentoTitle: 'See every workflow at a glance.',
    bentoTranslate: {
      title: 'Translate whole adventure maps',
      description: 'Signs, books, items, NPCs, and command blocks — paged cleanly while preserving formatting.',
      href: '/docs/translation-workflow/',
      linkLabel: 'Map translation guide',
      btnOriginal: 'EN',
      btnTranslated: '繁中',
      signMeta: 'sign · block_entity',
      signOriginal: ['ANCIENT GATE', 'Place the emerald', 'to awaken the bridge'],
      signTranslated: ['古老石門', '放上綠寶石', '喚醒沉睡之橋'],
      bookTitleOriginal: 'book · p.1 · Keeper Journal',
      bookTitleTranslated: 'book · p.1 · 守門人手記',
      bookLineOriginal: 'The lighthouse dims when night falls across the harbor.',
      bookLineTranslated: '當夜幕降臨海港，燈塔的光芒便會逐漸黯淡。',
      progressLabel: '142 / 142 slots ready',
    },
    bentoInspect: {
      title: 'Explore saves read-only',
      description: 'Inspect level.dat, region chunks, and datapacks without modifying a single byte.',
      href: '/docs/getting-started/',
      linkLabel: 'Start inspecting',
      rootLabel: 'level.dat',
      nodes: [
        {
          id: 'data',
          key: 'Data',
          type: 'Compound',
          children: [
            { key: 'LevelName', type: 'String', value: '"Sunken Citadel"' },
            { key: 'DataVersion', type: 'Int', value: '3953' },
            { key: 'GameType', type: 'Int', value: '2' },
          ],
        },
        {
          id: 'player',
          key: 'Player',
          type: 'Compound',
          children: [
            { key: 'Dimension', type: 'String', value: '"minecraft:overworld"' },
            { key: 'XpLevel', type: 'Int', value: '18' },
          ],
        },
      ],
    },
    bentoPack: {
      title: 'Ship a map or a resource pack',
      description: 'Write translations directly into your world or bundle a standalone .zip client pack.',
      href: '/docs/resources-and-packs/',
      linkLabel: 'Resource pack options',
      modeDirectLabel: 'Direct Map',
      modeZipLabel: 'Pack .zip',
      directFiles: [
        { path: 'region/r.0.0.mca', note: 'Signs & books updated' },
        { path: 'datapacks/story/', note: 'Dialogue localized' },
      ],
      zipFiles: [
        { path: 'pack.mcmeta', note: 'pack_format: 34' },
        { path: 'assets/.../zh_tw.json', note: '142 translation keys' },
      ],
    },
    bentoSafe: {
      title: 'Preview first. Roll back anytime.',
      description: 'Every write creates a reviewable plan and an external backup before touching your save.',
      href: '/docs/safe-editing/',
      linkLabel: 'Safe editing & recovery',
      checkpoints: [
        {
          id: 'preview',
          label: 'Preview Plan',
          detail: 'Saved outside your world folder — original files stay untouched.',
          status: 'World untouched',
        },
        {
          id: 'backup',
          label: 'Auto Backup',
          detail: 'Snapshots every target file before writing a single byte offline.',
          status: 'Snapshot sealed',
        },
        {
          id: 'recover',
          label: 'Apply or Undo',
          detail: 'Interrupted mid-write? Resume cleanly or roll back to the original state.',
          status: 'Instant rollback ready',
        },
      ],
    },
    ctaTitle: 'Ready for your next world.',
    ctaSubtitle: 'Extract the full ZIP from GitHub Releases. The .NET runtime is included.',
    ctaPrimary: 'Go to Download Page',
    ctaQuickstart: 'Quickstart Guide',
    ctaAgents: 'AI Agent Manual (llms.txt)',
    footerNote:
      'MIT License. Bundled third-party notices included in release. Not an official Mojang or Microsoft product.',
  },
  'zh-tw': {
    langAttr: 'zh-TW',
    pageTitle: 'Minecraft 存檔查閱、冒險地圖翻譯與安全編輯工具',
    skipLink: '跳至主要內容',
    navigationLabel: '主要導覽',
    menuOpen: '開啟選單',
    menuClose: '關閉選單',
    navDocs: '線上文件',
    navAgents: 'AI 整合',
    navDownload: '下載',
    switchLangLabel: 'English',
    switchLangHref: '/',
    docsHomeHref: '/zh-tw/docs/',
    gettingStartedHref: '/zh-tw/docs/getting-started/',
    translationHref: '/zh-tw/docs/translation-workflow/',
    packsHref: '/zh-tw/docs/resources-and-packs/',
    safetyHref: '/zh-tw/docs/safe-editing/',
    agentsHref: '/zh-tw/docs/ai-agents/',
    llmsHref: '/llms.txt',
    heroTitlePlain: '解碼方塊世界，',
    heroTitleAccent: '安全翻譯與編輯。',
    heroSubtitle:
      '解壓縮即用的本地命令列工具，幫你唯讀查閱存檔、翻譯整張冒險地圖並打包資源包。',
    ctaDownload: '前往下載頁面',
    ctaDocs: '瀏覽教學文件',
    stageAriaLabel: '互動式世界轉換預覽',
    heroScenes: [
      {
        id: 'translate',
        label: '翻譯地圖',
        worldLabel: '地圖文字槽位',
        outputLabel: '轉換結果',
        bridgeCmd: 'translate',
      },
      {
        id: 'inspect',
        label: '查閱存檔',
        worldLabel: '世界結構',
        outputLabel: 'NBT JSON',
        bridgeCmd: 'get',
      },
      {
        id: 'pack',
        label: '匯出資源包',
        worldLabel: '資源包封裝',
        outputLabel: '輸出資訊',
        bridgeCmd: 'create-pack',
      },
    ],
    heroTranslate: {
      signHeader: 'sign · 128, 64, -42',
      itemHeader: 'item.lore · slot #14',
      toggleOriginal: '原文',
      toggleTranslated: '譯文',
      signOriginal: ['SUNKEN CITADEL', 'Enter the vault', 'to claim the key'],
      signTranslated: ['沉沒的要塞', '進入地下寶庫', '尋找遺落之鑰'],
      itemNameOriginal: 'Blade of Dawn',
      itemNameTranslated: '曙光之刃',
      itemLoreOriginal: 'Forged beneath the ancient spire',
      itemLoreTranslated: '鍛造於遠古尖塔之下',
      jsonLocaleOriginal: 'en_us',
      jsonLocaleTranslated: 'zh_tw',
    },
    heroInspect: {
      worldName: '沉沒的要塞',
      layers: [
        { file: 'level.dat', detail: 'DataVersion 3953 · Java 1.21' },
        { file: 'region/r.0.0.mca', detail: '已索引 32 x 32 區塊' },
        { file: 'datapacks/quest/', detail: '48 個函式檔 · 純讀取' },
      ],
      jsonFields: [
        { key: 'LevelName', val: '"沉沒的要塞"', type: 'str' },
        { key: 'DataVersion', val: '3953', type: 'num' },
        { key: 'GameType', val: '2', type: 'num' },
        { key: 'Difficulty', val: '2', type: 'num' },
      ],
    },
    heroPack: {
      packName: 'citadel-zh_tw.zip',
      entries: [
        { path: 'pack.mcmeta', tag: 'format 34' },
        { path: 'assets/minecraft/lang/en_us.json', tag: '142 筆鍵值' },
        { path: 'assets/minecraft/lang/zh_tw.json', tag: '142 筆鍵值' },
      ],
      jsonFields: [
        { key: 'archive', val: '"citadel-zh_tw.zip"', type: 'str' },
        { key: 'packFormat', val: '34', type: 'num' },
        { key: 'entries', val: '142', type: 'num' },
        { key: 'worldModified', val: 'false', type: 'num' },
      ],
    },
    bentoTitle: '用視覺一眼看懂核心功能。',
    bentoTranslate: {
      title: '翻譯整張冒險地圖',
      description: '告示牌、書本、道具、NPC 與指令方塊，自動分頁並完整保留顏色與格式。',
      href: '/zh-tw/docs/translation-workflow/',
      linkLabel: '看地圖翻譯教學',
      btnOriginal: '原文',
      btnTranslated: '繁中',
      signMeta: 'sign · 告示牌',
      signOriginal: ['ANCIENT GATE', 'Place the emerald', 'to awaken the bridge'],
      signTranslated: ['古老石門', '放上綠寶石', '喚醒沉睡之橋'],
      bookTitleOriginal: 'book · p.1 · Keeper Journal',
      bookTitleTranslated: 'book · 第 1 頁 · 守門人手記',
      bookLineOriginal: 'The lighthouse dims when night falls across the harbor.',
      bookLineTranslated: '當夜幕降臨海港，燈塔的光芒便會逐漸黯淡。',
      progressLabel: '142 / 142 筆譯文就緒',
    },
    bentoInspect: {
      title: '零風險探索存檔',
      description: '唯讀檢視 level.dat、區塊與資料包內容，絕不意外改動任何位元組。',
      href: '/zh-tw/docs/getting-started/',
      linkLabel: '開始探索存檔',
      rootLabel: 'level.dat',
      nodes: [
        {
          id: 'data',
          key: 'Data',
          type: 'Compound',
          children: [
            { key: 'LevelName', type: 'String', value: '"沉沒的要塞"' },
            { key: 'DataVersion', type: 'Int', value: '3953' },
            { key: 'GameType', type: 'Int', value: '2' },
          ],
        },
        {
          id: 'player',
          key: 'Player',
          type: 'Compound',
          children: [
            { key: 'Dimension', type: 'String', value: '"minecraft:overworld"' },
            { key: 'XpLevel', type: 'Int', value: '18' },
          ],
        },
      ],
    },
    bentoPack: {
      title: '直接改地圖，或打包資源包',
      description: '自由選擇把譯文寫回地圖，或一鍵封裝與合併成獨立 .zip 資源包。',
      href: '/zh-tw/docs/resources-and-packs/',
      linkLabel: '了解資源包方案',
      modeDirectLabel: '直接寫回地圖',
      modeZipLabel: '獨立 .zip',
      directFiles: [
        { path: 'region/r.0.0.mca', note: '告示牌與書本已更新' },
        { path: 'datapacks/story/', note: '劇情對話已完成中文化' },
      ],
      zipFiles: [
        { path: 'pack.mcmeta', note: 'pack_format: 34' },
        { path: 'assets/.../zh_tw.json', note: '142 筆多語翻譯鍵' },
      ],
    },
    bentoSafe: {
      title: '先預覽，隨時可還原',
      description: '所有修改都先在世界外產出計畫與自動備份，離線寫入更安心。',
      href: '/zh-tw/docs/safe-editing/',
      linkLabel: '看安全編輯與復原',
      checkpoints: [
        {
          id: 'preview',
          label: '預覽計畫',
          detail: '計畫存在世界資料夾外，確認前原存檔完全不動。',
          status: '原存檔零變動',
        },
        {
          id: 'backup',
          label: '自動備份',
          detail: '離線寫入前自動封存原始檔案快照。',
          status: '快照已鎖定',
        },
        {
          id: 'recover',
          label: '套用或還原',
          detail: '萬一寫到一半視窗關閉，可續寫或一鍵回復修改前。',
          status: '支援即時回溯',
        },
      ],
    },
    ctaTitle: '準備好整理你的世界了嗎？',
    ctaSubtitle: '前往 GitHub Releases 下載並解壓完整 ZIP，內含 .NET 執行環境。',
    ctaPrimary: '前往下載頁面',
    ctaQuickstart: '快速入門指南',
    ctaAgents: 'AI Agent 專區 (llms.txt)',
    footerNote: 'MIT 授權條款釋出。隨附第三方授權聲明。非 Minecraft、Mojang 或 Microsoft 官方產品。',
  },
};
