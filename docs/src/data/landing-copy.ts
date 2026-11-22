export interface HeroSceneCopy {
  id: 'translate' | 'inspect' | 'pack';
  label: string;
  worldLabel: string;
  outputLabel: string;
  bridgeCmd: string;
  cliCommand: string;
}

export interface SpecStripItem {
  value: string;
  label: string;
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
  themeToLight: string;
  themeToDark: string;
  switchLangLabel: string;
  switchLangHref: string;
  docsHomeHref: string;
  gettingStartedHref: string;
  translationHref: string;
  packsHref: string;
  safetyHref: string;
  agentsHref: string;
  llmsHref: string;
  heroEyebrow: string;
  heroTitlePlain: string;
  heroTitleAccent: string;
  heroSubtitle: string;
  ctaDownload: string;
  ctaDocs: string;
  copyCmdLabel: string;
  copiedCmdLabel: string;
  copyFailedCmdLabel: string;
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
  specStripAriaLabel: string;
  specStrip: SpecStripItem[];
  bentoEyebrow: string;
  bentoTitle: string;
  bentoSubtitle: string;
  bentoTranslate: {
    indexLabel: string;
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
    indexLabel: string;
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
    indexLabel: string;
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
    indexLabel: string;
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
  ctaEyebrow: string;
  ctaTitle: string;
  ctaSubtitle: string;
  ctaPrimary: string;
  ctaQuickstart: string;
  ctaAgents: string;
  footerNote: string;
}
