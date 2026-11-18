import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: process.env.PAGES_ORIGIN?.replace(/^http:/, 'https:') || 'https://nbtcli.evanlau1798.com',
  base: process.env.PAGES_BASE_PATH || '/',
  trailingSlash: 'always',
  integrations: [
    starlight({
      title: {
        en: 'NBTExplorerCLI',
        'zh-TW': 'NBTExplorerCLI',
      },
      description: 'Windows x64 command-line tool for inspecting, translating, and safely editing Minecraft Java Edition worlds.',
      favicon: '/favicon.svg',
      logo: {
        src: './src/assets/logo.svg',
        replacesTitle: false,
      },
      defaultLocale: 'root',
      locales: {
        root: {
          label: 'English',
          lang: 'en',
        },
        'zh-tw': {
          label: '繁體中文',
          lang: 'zh-TW',
        },
      },
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/Evanlau1798/NBTExplorerCLI' }],
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        {
          label: 'Start Here',
          translations: { 'zh-TW': '入門指南' },
          items: [
            {
              label: 'Documentation Overview',
              translations: { 'zh-TW': '文檔總覽與核心概念' },
              slug: 'docs',
            },
            {
              label: 'Install and Inspect',
              translations: { 'zh-TW': '安裝驗證與安全查閱' },
              slug: 'docs/getting-started',
            },
          ],
        },
        {
          label: 'Core Workflows',
          translations: { 'zh-TW': '核心工作流程' },
          items: [
            {
              label: 'Safe Editing and Recovery',
              translations: { 'zh-TW': '安全編輯與中斷復原' },
              slug: 'docs/safe-editing',
            },
            {
              label: 'Translation Workflow',
              translations: { 'zh-TW': '地圖翻譯工作流' },
              slug: 'docs/translation-workflow',
            },
            {
              label: 'Resource Packs and References',
              translations: { 'zh-TW': '資源包、語言檔與引用' },
              slug: 'docs/resources-and-packs',
            },
            {
              label: 'Backups and State Files',
              translations: { 'zh-TW': '備份與狀態檔管理' },
              slug: 'docs/backups-and-state',
            },
          ],
        },
        {
          label: 'Reference and Support',
          translations: { 'zh-TW': '參考與疑難排解' },
          items: [
            {
              label: 'Command Reference',
              translations: { 'zh-TW': 'CLI 命令完整參考' },
              slug: 'docs/command-reference',
            },
            {
              label: 'Support Scope and Troubleshooting',
              translations: { 'zh-TW': '支援範圍與疑難排解' },
              slug: 'docs/support-and-troubleshooting',
            },
          ],
        },
      ],
    }),
  ],
});
