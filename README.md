<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://nbtcli.evanlau1798.com/banner-dark.svg" />
    <source media="(prefers-color-scheme: light)" srcset="https://nbtcli.evanlau1798.com/banner-light.svg" />
    <img alt="NBTExplorerCLI — Inspect, Translate, and Safely Edit Minecraft Java Edition Worlds on Windows" src="https://nbtcli.evanlau1798.com/banner-dark.svg" width="900" />
  </picture>

  <br />
  <br />

  [![Windows Candidate CI][windows-ci-badge]][windows-ci-url]
  [![Documentation Site CI][docs-ci-badge]][docs-ci-url]
  [![Release Version][release-badge]][releases-url]
  [![Target Platform][platform-badge]][releases-url]
  [![MIT License][license-badge]][license-url]

  <p>
    <b>A free, ready-to-run Windows tool for exploring Minecraft Java Edition saves, translating adventure maps, building resource packs, and making safe offline world edits — built for players, mapmakers, translators, and AI assistants.</b>
  </p>

  <p>
    <a href="#what-you-can-do">Features</a> |
    <a href="#quick-start">Quick Start</a> |
    <a href="#common-tasks">Common Tasks</a> |
    <a href="#using-with-ai-agents">For AI Agents</a> |
    <a href="https://nbtcli.evanlau1798.com/docs/">Full Documentation</a>
  </p>

  <p>
    <b>English</b> | <a href="README.zh-TW.md">繁體中文</a> | <a href="https://nbtcli.evanlau1798.com/">Website</a> | <a href="https://nbtcli.evanlau1798.com/llms.txt">AI Agent Manual (llms.txt)</a>
  </p>
</div>

---

## What You Can Do

- **Inspect Worlds Without Risk**: Read `level.dat`, map regions (`.mca`), datapacks, and resource pack ZIPs across Java Edition format families (`DataVersion 100` through `4774+` plus classic Anvil) in read-only mode — your save files stay untouched.
- **Translate Entire Adventure Maps Across Languages**: Automatically find text across signs, books, items, entities, scoreboards, bossbars, command blocks, and datapacks, resolve source/target languages automatically or by explicit flags, translate page by page with a glossary, and export validated `translate fallback` drafts for unresolved files.
- **Export Language Files & Resource Packs**: Replace text directly inside the map, convert text into translation keys, or build/merge a client `.zip` resource pack without touching gameplay data.
- **Edit Worlds With Built-in Safety Nets**: Every edit creates a reviewable plan first, saves an automatic backup outside your world folder before writing, and can finish or undo cleanly if interrupted.
- **Ready for Scripts & AI Assistants**: Every command prints clean, structured JSON so PowerShell scripts and AI coding tools (Claude, ChatGPT, Codex, Cursor) can inspect or translate maps reliably.

> [!NOTE]
> **Built for Minecraft Java Edition on Windows x64.** No separate .NET installation is required. CLI help, diagnostics, and translation prompt instructions are always in English, while map content can be translated between any valid language tags. Always close Minecraft before applying edits, and test on a copy of your world first. Bedrock Edition saves and 3D visual block editing are not supported.

---

## Quick Start

### 1. Download and Unzip

1. Open [GitHub Releases][releases-url] and download the latest release **`.zip`** (packaged as a `.zip` so browsers like Chrome do not block direct `.exe` downloads).
2. Right-click the ZIP file and select **Extract All...** to a folder such as `C:\Tools\nbtcli`.
3. Run `nbtcli.exe` from the extracted folder and keep its bundled files together. The ZIP includes the .NET runtime.

### 2. Open PowerShell in That Folder

Open the extracted folder in File Explorer, type `powershell` in the address bar, and press **Enter** (or hold **Shift**, right-click an empty area, and choose **Open in Terminal**). Check that it works:

```powershell
.\nbtcli.exe --version
```

You should see `"success": true` and `"version": "1.0.0"`.

### 3. Try Your First Safe Read-Only Command

Make a copy of a Minecraft world first (for example, copy a folder from `%APPDATA%\.minecraft\saves\` to `D:\WorldCopy`), then run:

```powershell
# Check your world's Minecraft DataVersion (read-only)
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion

# See how much translatable text exists across your map (read-only)
.\nbtcli.exe scan 'D:\WorldCopy' --summary
```

Need step-by-step screenshots-level walkthroughs, SmartScreen tips, or help finding your Minecraft saves? Read the **[Install & First Run guide][doc-getting-started]** or the **[FAQ & Glossary][doc-faq]**.

---

## Everyday Examples

Run these commands inside your extracted `nbtcli` folder:

### Explore a World and Export All Text

```powershell
# List dimensions and region files
.\nbtcli.exe list 'D:\WorldCopy'

# Export all unique in-world text to a JSON file outside the world
New-Item -ItemType Directory -Path 'D:\Exports' -Force | Out-Null
.\nbtcli.exe extract 'D:\WorldCopy' --unique --output 'D:\Exports\world-text.json'
```

### Start Translating an Adventure Map

You can omit `--source-language` and `--target-language` when starting a translation job: source language is detected from recognizable content (or marked `und` if undetermined), target language uses your Windows account display language, and both languages are shown and bound to the session. Pass explicit flags only when you want to override them:

```powershell
# Start a translation session using automatic source detection and your Windows display language (world files are NOT modified)
.\nbtcli.exe translate start 'D:\WorldCopy' --page-size 30

# Override source and target explicitly (for example, Japanese to German)
.\nbtcli.exe translate start 'D:\WorldCopy' --source-language ja-JP --target-language de-DE --page-size 30
```

Need to draft and validate translations for an unresolved NBT, JSON, SNBT, or text snippet outside a full map session? Use `translate fallback`:

```powershell
.\nbtcli.exe translate fallback 'D:\dialogue.nbt' --pointer /dialogue
```

See the **[Map Translation Guide][doc-translation]** for exporting pages, submitting translations, using `translate fallback`, and applying results to your map or resource pack.

### Back Up a World Before Making Changes

```powershell
# Create a verified backup outside your world folder
.\nbtcli.exe backup 'D:\WorldCopy' --suffix before-edit
```

See **[Backups & Where Files Live][doc-backups]** to learn how to restore an entire world or a single damaged file.

---

## Common Tasks

Every command falls into one of three clear safety levels:
- **Read Only** — Only reads data; never changes your Minecraft world.
- **Preview Only** — Creates a plan file or saves translation progress outside your world.
- **Modifies World** — Writes changes to your world (close Minecraft first and pass `--world-offline`).

| What you want to do | Command | Safety |
| :--- | :--- | :--- |
| Check version or command options | `.\nbtcli.exe --version` / `.\nbtcli.exe help <cmd>` | Read Only |
| Inspect supported Java Edition format families | `.\nbtcli.exe capabilities` | Read Only |
| List world dimensions or chunk coordinates | `.\nbtcli.exe list 'D:\WorldCopy'` | Read Only |
| Read a single value (e.g., world name or version) | `.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName` | Read Only |
| Search tags by name or text | `.\nbtcli.exe search 'D:\WorldCopy' --path level.dat --name Spawn` | Read Only |
| Count all translatable text in a map | `.\nbtcli.exe scan 'D:\WorldCopy' --summary` | Read Only |
| Check a world for broken files or unsupported formats | `.\nbtcli.exe validate 'D:\WorldCopy'` | Read Only |
| Start a map translation session (auto languages) | `.\nbtcli.exe translate start 'D:\WorldCopy' --page-size 30` | Preview Only |
| Export a validated fallback draft from a tag or line | `.\nbtcli.exe translate fallback 'D:\dialogue.nbt' --pointer /dialogue` | Read Only |
| Back up a world before editing | `.\nbtcli.exe backup 'D:\WorldCopy' --suffix before-edit` | Read Only |
| Apply a reviewed edit or translation plan | `.\nbtcli.exe apply 'D:\WorldCopy' --plan 'D:\Plans\plan.json' --world-offline` | Modifies World |

Looking for ready-to-copy recipes? See **[Common Tasks (Recipes)][doc-recipes]** or the full **[Command Reference][doc-reference]**.

---

## Using with AI Agents

`nbtcli.exe` is designed to work seamlessly with AI assistants (Claude, ChatGPT, Codex, Cursor, etc.). Instead of reading through every user guide, point your AI agent directly to our complete, high-density machine-friendly manual:

- **Online Agent Manual (`llms.txt`)**: [https://nbtcli.evanlau1798.com/llms.txt][llms-url] (structured Markdown mirror: [https://nbtcli.evanlau1798.com/llms-full.md][llms-md-url])
- **In-Repository File**: [`docs/public/llms.txt`](docs/public/llms.txt)
- **Web Guide & Copy-Paste Prompts**: **[AI Agent Quickstart][doc-ai-agents]**

`llms.txt` contains the full command surface, every flag, all 7 input JSON schemas, all 17 supported Java Edition format families, exit-code branching rules, step-by-step edit/translation/fallback/recovery SOPs, and common pitfalls in one fast-to-read document.

---

## Full Documentation

All guides are available in **English** and **Traditional Chinese (繁體中文)** on the documentation site:

| Section | English | 繁體中文 |
| :--- | :--- | :--- |
| Overview | [Documentation Overview][docs-en] | [工具總覽][docs-zh] |
| Install & First Run | [Install & First Run][doc-getting-started] | [下載安裝與初次使用][doc-zh-getting-started] |
| Common Tasks | [Common Tasks (Recipes)][doc-recipes] | [常見任務速查][doc-zh-recipes] |
| FAQ & Glossary | [FAQ & Glossary][doc-faq] | [常見問題與名詞詞典][doc-zh-faq] |
| Map Translation | [Map Translation Guide][doc-translation] | [冒險地圖翻譯教學][doc-zh-translation] |
| Resource Packs | [Resource Packs & Language Files][doc-resources] | [資源包與語言檔輸出][doc-zh-resources] |
| Editing & Recovery | [Editing Worlds & Crash Recovery][doc-safe-editing] | [安全修改存檔與中斷復原][doc-zh-safe-editing] |
| Backups | [Backups & Where Files Live][doc-backups] | [備份還原與檔案存放位置][doc-zh-backups] |
| AI Agents | [AI Agent Quickstart][doc-ai-agents] | [AI Agent 快速整合指南][doc-zh-ai-agents] |
| Reference | [Command Reference][doc-reference] | [完整命令參考][doc-zh-reference] |
| Troubleshooting | [Supported Versions & Troubleshooting][doc-support] | [支援版本與疑難排解][doc-zh-support] |

---

## Building from Source & Contributing

Want to build from source, run the verification test suite, or contribute a bug fix or translation? See **[CONTRIBUTING.md](CONTRIBUTING.md)** for the pinned .NET SDK (`10.0.404`), build commands, docs build setup (`docs/`), and contribution rules.

## License

Released under the [MIT License](LICENSE). Bundled third-party components retain their own terms and notices; see [SharpZipLib](licenses/SharpZipLib.md) and [Unicode](licenses/Unicode.md). Not an official Minecraft, Mojang, or Microsoft product.

[windows-ci-badge]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/windows.yml/badge.svg
[windows-ci-url]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/windows.yml
[docs-ci-badge]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/docs.yml/badge.svg
[docs-ci-url]: https://github.com/Evanlau1798/NBTExplorerCLI/actions/workflows/docs.yml
[release-badge]: https://img.shields.io/badge/release-v1.0.0-10b981
[releases-url]: https://github.com/Evanlau1798/NBTExplorerCLI/releases
[platform-badge]: https://img.shields.io/badge/platform-Windows%20x64-0f172a
[license-badge]: https://img.shields.io/badge/license-MIT-34d399
[license-url]: LICENSE
[docs-en]: https://nbtcli.evanlau1798.com/docs/
[docs-zh]: https://nbtcli.evanlau1798.com/zh-tw/docs/
[llms-url]: https://nbtcli.evanlau1798.com/llms.txt
[llms-md-url]: https://nbtcli.evanlau1798.com/llms-full.md
[doc-getting-started]: https://nbtcli.evanlau1798.com/docs/getting-started/
[doc-recipes]: https://nbtcli.evanlau1798.com/docs/recipes/
[doc-faq]: https://nbtcli.evanlau1798.com/docs/faq/
[doc-safe-editing]: https://nbtcli.evanlau1798.com/docs/safe-editing/
[doc-translation]: https://nbtcli.evanlau1798.com/docs/translation-workflow/
[doc-resources]: https://nbtcli.evanlau1798.com/docs/resources-and-packs/
[doc-backups]: https://nbtcli.evanlau1798.com/docs/backups-and-state/
[doc-ai-agents]: https://nbtcli.evanlau1798.com/docs/ai-agents/
[doc-reference]: https://nbtcli.evanlau1798.com/docs/command-reference/
[doc-support]: https://nbtcli.evanlau1798.com/docs/support-and-troubleshooting/
[doc-zh-getting-started]: https://nbtcli.evanlau1798.com/zh-tw/docs/getting-started/
[doc-zh-recipes]: https://nbtcli.evanlau1798.com/zh-tw/docs/recipes/
[doc-zh-faq]: https://nbtcli.evanlau1798.com/zh-tw/docs/faq/
[doc-zh-safe-editing]: https://nbtcli.evanlau1798.com/zh-tw/docs/safe-editing/
[doc-zh-translation]: https://nbtcli.evanlau1798.com/zh-tw/docs/translation-workflow/
[doc-zh-resources]: https://nbtcli.evanlau1798.com/zh-tw/docs/resources-and-packs/
[doc-zh-backups]: https://nbtcli.evanlau1798.com/zh-tw/docs/backups-and-state/
[doc-zh-ai-agents]: https://nbtcli.evanlau1798.com/zh-tw/docs/ai-agents/
[doc-zh-reference]: https://nbtcli.evanlau1798.com/zh-tw/docs/command-reference/
[doc-zh-support]: https://nbtcli.evanlau1798.com/zh-tw/docs/support-and-troubleshooting/
