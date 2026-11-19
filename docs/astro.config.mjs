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
      description: 'Windows x64 tool for inspecting, translating, and safely editing Minecraft Java Edition worlds.',
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
          translations: { 'zh-TW': '快速入門' },
          items: [
            {
              label: 'Overview',
              translations: { 'zh-TW': '工具總覽' },
              slug: 'docs',
            },
            {
              label: 'Install & First Run',
              translations: { 'zh-TW': '下載安裝與初次使用' },
              slug: 'docs/getting-started',
            },
            {
              label: 'Common Tasks (Recipes)',
              translations: { 'zh-TW': '常見任務速查' },
              slug: 'docs/recipes',
            },
            {
              label: 'FAQ & Glossary',
              translations: { 'zh-TW': '常見問題與名詞白話詞典' },
              slug: 'docs/faq',
            },
          ],
        },
        {
          label: 'Core Guides',
          translations: { 'zh-TW': '核心功能指南' },
          items: [
            {
              label: 'Map Translation Guide',
              translations: { 'zh-TW': '冒險地圖翻譯教學' },
              slug: 'docs/translation-workflow',
            },
            {
              label: 'Resource Packs & Language Files',
              translations: { 'zh-TW': '資源包與語言檔輸出' },
              slug: 'docs/resources-and-packs',
            },
            {
              label: 'Editing Worlds & Crash Recovery',
              translations: { 'zh-TW': '安全修改存檔與中斷復原' },
              slug: 'docs/safe-editing',
            },
            {
              label: 'Backups & Where Files Live',
              translations: { 'zh-TW': '備份還原與檔案存放位置' },
              slug: 'docs/backups-and-state',
            },
          ],
        },
        {
          label: 'Reference & AI Agents',
          translations: { 'zh-TW': '參考手冊與 AI 整合' },
          items: [
            {
              label: 'AI Agent Quickstart',
              translations: { 'zh-TW': 'AI Agent 快速整合指南' },
              slug: 'docs/ai-agents',
            },
            {
              label: 'Command Reference',
              translations: { 'zh-TW': '完整命令參考' },
              slug: 'docs/command-reference',
            },
            {
              label: 'Supported Versions & Troubleshooting',
              translations: { 'zh-TW': '支援版本與疑難排解' },
              slug: 'docs/support-and-troubleshooting',
            },
          ],
        },
      ],
    }),
  ],
});
