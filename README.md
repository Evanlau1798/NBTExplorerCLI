<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://nbtcli.evanlau1798.com/banner-dark.svg" />
    <source media="(prefers-color-scheme: light)" srcset="https://nbtcli.evanlau1798.com/banner-light.svg" />
    <img alt="NBTExplorerCLI — Deterministic NBT Inspection, World Localization, and Crash-Safe Editing for Minecraft Java Edition" src="https://nbtcli.evanlau1798.com/banner-dark.svg" width="900" />
  </picture>

  <br />
  <br />

  [![Windows Candidate CI][windows-ci-badge]][windows-ci-url]
  [![Documentation Site CI][docs-ci-badge]][docs-ci-url]
  [![Release Version][release-badge]][releases-url]
  [![Target Platform][platform-badge]][releases-url]
  [![MIT License][license-badge]][license-url]

  <p>
    <b>A self-contained Windows x64 command-line tool for inspecting NBT structures, translating in-world text, generating resource packs, and applying crash-recoverable offline edits to Minecraft Java Edition worlds.</b>
  </p>

  <p>
    <a href="#highlights">Highlights</a> |
    <a href="#quick-start">Quick Start</a> |
    <a href="#core-workflows">Workflows</a> |
    <a href="#command-cheat-sheet">Command Cheat Sheet</a> |
    <a href="#verification-of-binaries">Binary Verification</a> |
    <a href="https://nbtcli.evanlau1798.com/docs/">Documentation</a>
  </p>

  <p>
    <b>English</b> | <a href="README.zh-TW.md">繁體中文</a> | <a href="https://nbtcli.evanlau1798.com/">Product Site</a> | <a href="https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md">Contributing</a>
  </p>
</div>

---

## Highlights

- **Predictable Single-JSON Protocol**: Every command writes exactly one newline-delimited UTF-8 JSON object (`schemaFamily: "nbtcli.cli"`, `schemaVersion: 1`) to `stdout`, making PowerShell scripts, CI pipelines, and AI agents reliable without brittle text scraping.
- **Safe Read-Only Inspection**: Query `level.dat`, `.mca` terrain and entity chunks, datapack JSON files, and ZIP archives using standard JSON Pointers (`--pointer`) or explicit segment arrays (`--segments`), in either human-readable `display-json` or reversible `nbtcli.typed-nbt` mode (`--types`).
- **21-Role World Text Scanner**: Index and validate translatable text across signs, hanging signs, books, items, entities, containers, command blocks, functions, scoreboards, bossbars, advancements, and language files with an external rebuildable scan cache.
- **Paginated Map Localization**: Split large adventure map translations into deterministic pages and slots (`translate`), track glossaries and translator notes, and seal completed sessions (`seal`) only after verified world commits.
- **Resource Pack & Translation Key Pipeline**: Convert literal world text into translation keys (`convert` or `translate plan --keys`), export standalone language JSON files (`export-lang`), or safely merge client resource packs (`create-pack`).
- **Crash-Safe Offline Editing**: Preview every modification as an immutable `nbtcli.patch-plan` before touching disk. Offline commits (`--world-offline`) automatically store content-addressed preimages and recovery journals outside the world directory, supporting deterministic forward completion or byte-for-byte rollback (`recover --rollback`).

### When to Use NBTExplorerCLI

**NBTExplorerCLI is built for:**

- **Adventure Map Localization Teams**: Scanning thousands of signs, books, dialogs, and datapack strings into structured pages, translating with glossaries, and generating either patched worlds or client resource packs.
- **Technical Minecraft Creators & Server Admins**: Inspecting or batch-patching `level.dat`, region chunks (`.mca`), entities, and datapacks offline with hash precondition checks and automatic pre-write backups.
- **Automation Scripts & AI Coding Assistants**: Driving world inspection, validation, and translation pipelines via strict JSON schemas and deterministic exit codes (`0` through `8` and `130`).

**NBTExplorerCLI is not designed for:**

- **Live In-Game Editing**: Close Minecraft and other editors before passing `--world-offline` to a world-modifying command.
- **3D Terrain Rendering or Interactive GUI Browsing**: NBTExplorerCLI is a headless command-line engine rather than a 3D viewport editor.
- **Minecraft Bedrock Edition Saves**: The storage and NBT codecs target **Minecraft Java Edition**. Run `nbtcli capabilities` for support by game version and operation.

---

## Quick Start

Download `nbtcli-win-x64.zip` and `SHA256SUMS.txt` from [GitHub Releases][releases-url] or a verified [GitHub Actions candidate][windows-ci-url]. The archive bundles the pinned .NET runtime (`10.0.15`) and required libraries—keep all extracted files together in the same folder.

```powershell
# 1. Verify the ZIP against SHA256SUMS.txt before extracting
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
# Compare the hashes. Extract only if they match.
Expand-Archive -Path .\nbtcli-win-x64.zip -DestinationPath .\nbtcli

# 2. Check the binary version and command overview
.\nbtcli\nbtcli.exe --version
.\nbtcli\nbtcli.exe --help

# 3. Inspect a disposable copy of a Minecraft world (read-only)
.\nbtcli\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

```json
{
  "schemaFamily": "nbtcli.cli",
  "schemaVersion": 1,
  "success": true,
  "command": "get",
  "outcome": "Success",
  "data": {
    "representation": "display-json",
    "tagType": "Int",
    "value": 3953
  },
  "error": null
}
```

> [!IMPORTANT]
> Always test workflows on a disposable world copy (`D:\WorldCopy`), preview change plans before writing, and pass `--world-offline` only after closing Minecraft and any other editors. Pass `--ui-locale en-US` or `--ui-locale zh-TW` on any command to switch diagnostic message language independently of world translation locales.

See the [Install and Inspect guide][doc-getting-started] for step-by-step setup instructions.

---

## Core Workflows

Run these examples from the extracted folder that contains `nbtcli.exe`.

### 1. Inspect NBT Nodes, Chunks, and World Text

Explore world dimensions, region chunks, and translatable text without modifying any save files:

```powershell
# List declared dimensions and region file counts in a world
.\nbtcli.exe list 'D:\WorldCopy'

# Read a node with reversible NBT type tags preserved (--types)
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName --types

# Scan all 21 Minecraft text roles and output a role summary
.\nbtcli.exe scan 'D:\WorldCopy' --summary

# Extract unique decoded text occurrences to an existing external directory
New-Item -ItemType Directory -Path 'D:\Exports' -Force | Out-Null
.\nbtcli.exe extract 'D:\WorldCopy' --unique --output 'D:\Exports\world-text.json'
```

See the [Install and Inspect guide][doc-getting-started] to learn more.

### 2. Preview, Apply, and Recover Offline World Edits

Modifications never overwrite world files blindly. Preview an immutable `nbtcli.patch-plan` first, then commit offline:

```powershell
# Step 1: Preview the edit and write an immutable patch plan (world untouched)
New-Item -ItemType Directory -Path 'D:\Plans' -Force | Out-Null
$read = .\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/LevelName --types | ConvertFrom-Json
if (-not $read.success) { throw $read.error.message }
$replacement = $read.data.value
$replacement.data.root.value = 'Translated Map Name'
$replacement | ConvertTo-Json -Depth 50 | Set-Content -Encoding utf8 'D:\Plans\new-level-name.json'
.\nbtcli.exe set 'D:\WorldCopy' `
  --path level.dat `
  --pointer /Data/LevelName `
  --value-file 'D:\Plans\new-level-name.json' `
  --output 'D:\Plans\level-name-plan.json'

# Step 2: Apply the verified plan while Minecraft is closed
.\nbtcli.exe apply 'D:\WorldCopy' `
  --plan 'D:\Plans\level-name-plan.json' `
  --world-offline `
  --output 'D:\Plans\level-name-receipt.json'
```

If a write is interrupted or the command returns `RECOVERY_REQUIRED` (exit code `7`), keep Minecraft closed. Use `status` with the same world, plan hash, and state root. Confirm the journal and review the repair guidance before running `recover`.

See the [Safe Editing and Recovery guide][doc-safe-editing] to learn more.

### 3. Paginated Adventure Map Localization

Manage large map translation projects across verifiable pages, glossaries, and delivery modes:

```powershell
$session = .\nbtcli.exe translate start 'D:\WorldCopy' `
  --source-language en_us `
  --target-language zh_tw `
  --page-size 30 | ConvertFrom-Json
if (-not $session.success) { throw $session.error.message }

New-Item -ItemType Directory -Path 'D:\Translate' -Force | Out-Null
$page = .\nbtcli.exe translate export 'D:\WorldCopy' `
  --session-id $session.data.status.sessionId --page 0 `
  --output 'D:\Translate\page-0.txt' | ConvertFrom-Json
if (-not $page.success) { throw $page.error.message }
$page.data.replyTemplate | ConvertTo-Json -Depth 50 |
  Set-Content -Encoding utf8 'D:\Translate\reply-0.json'
```

This starts a session and saves its actual reply template. Use the guide to fill in and submit the reply.

See the [Translation Workflow guide][doc-translation] and [Resource Packs and References guide][doc-resources] to learn more.

---

## Command Cheat Sheet

The table describes what each command reads or writes.

- **Safe to Inspect**: Reads world data or status. Some commands also save a cache or export outside the world.
- **Preview Only**: Generates a patch plan or updates external session/agent state outside your world folder.
- **Writes External Backup**: Saves world data in the external backup store.
- **Modifies World Save**: Writes changes to the world folder. Requires a closed game and `--world-offline`.

| Goal / Task | Command | Safety Level |
| :--- | :--- | :--- |
| Check CLI version or command help | `nbtcli --version` / `nbtcli help <cmd>` | **Safe to Inspect** |
| Read an NBT tag, chunk, or JSON node | `nbtcli get <target> --path <rel> --pointer <ptr>` | **Safe to Inspect** |
| List world dimensions or chunk coordinates | `nbtcli list <target> [--path <rel>]` | **Safe to Inspect** |
| Search NBT nodes by value, name, or tag type | `nbtcli search <target> [--query <text>] [--name <key>]` | **Safe to Inspect** |
| Scan 21 world text roles with external cache | `nbtcli scan <world> [--summary] [--show-items]` | **Safe to Inspect** |
| Export decoded world text entries | `nbtcli extract <world> [--unique] --output <file>` | **Safe to Inspect** |
| Validate world text and container structures | `nbtcli validate <world>` | **Safe to Inspect** |
| Check supported Minecraft `DataVersion` profiles | `nbtcli capabilities` | **Safe to Inspect** |
| Preview a single typed NBT modification | `nbtcli set <target> --pointer <ptr> --value-file <file> --output <plan>` | **Preview Only** |
| Preview a multi-operation batch plan | `nbtcli plan <world> --request <ops.json> --output <plan>` | **Preview Only** |
| Preview one-shot translation key conversion | `nbtcli convert <world> --lang <code_id> --output <plan>` | **Preview Only** |
| Manage paginated map translation sessions | `nbtcli translate <start\|status\|export\|submit\|advance\|plan\|...>` | **Preview Only** |
| Export language files or build a resource pack | `nbtcli translate <export-lang\|create-pack> <world> --output <path>` | **Preview Only** |
| Manage external AI assistant preferences | `nbtcli agent <setup\|submit\|status\|help> <world>` | **Preview Only** |
| Apply a verified patch plan offline | `nbtcli apply <world> --plan <plan.json> --world-offline` | **Modifies World Save** |
| Check an interrupted write | `nbtcli status <world> --plan-hash <hash>` | **Safe to Inspect** |
| Recover or roll back an interrupted write | `nbtcli recover <world> --plan-hash <hash> --world-offline [--rollback]` | **Modifies World Save** |
| Create a content-addressed backup | `nbtcli backup <world> [--suffix <name>]` | **Writes External Backup** |
| Preview a restore | `nbtcli restore <world> --manifest-hash <hash> --output <plan>` | **Preview Only** |

See the complete [Command Reference][doc-reference] for all accepted flags and JSON input schemas.

---

## Verification of Binaries

Every Windows x64 release archive (`nbtcli-win-x64.zip`) is accompanied by `SHA256SUMS.txt` and an embedded `candidate.json` provenance manifest inside the archive.

1. **Verify the ZIP SHA-256 checksum before extraction:**

   ```powershell
   $zipHash = (Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
   $expected = (Get-Content .\SHA256SUMS.txt | Select-Object -First 1).Split(' ')[0].Trim().ToLowerInvariant()
   if ($zipHash -ne $expected) { throw "SHA-256 mismatch!" } else { "Verified OK: $zipHash" }
   ```

2. **Inspect the embedded `candidate.json` provenance manifest:**

   ```powershell
   Get-Content .\nbtcli\candidate.json | ConvertFrom-Json | Select-Object product, version, commit, sdkVersion, runtimeVersion
   ```

   `candidate.json` records the exact Git commit, pinned .NET SDK (`10.0.404`), bundled .NET runtime (`10.0.15`), dependency versions, and individual SHA-256 digests for every file in the distribution.

---

## Documentation

Full guides, safety explanations, copy-paste JSON schema templates, and troubleshooting checklists are available on the documentation site:

- **English Documentation**: [https://nbtcli.evanlau1798.com/docs/][docs-en]
  - [Install and Inspect][doc-getting-started]
  - [Safe Editing and Recovery][doc-safe-editing]
  - [Translation Workflow][doc-translation]
  - [Resource Packs and References][doc-resources]
  - [Backups and State Files][doc-backups]
  - [Command Reference][doc-reference]
  - [Support Scope and Troubleshooting][doc-support]
- **繁體中文文檔**: [https://nbtcli.evanlau1798.com/zh-tw/docs/][docs-zh]
- **LLM & Automation Context Index**: [https://nbtcli.evanlau1798.com/llms.txt][llms-url] (`docs/public/llms.txt`)

---

## Build and Verification

Use the .NET SDK specified in `global.json` (`10.0.404`). Run package restore, Release compilation, and the verification harness as separate steps:

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

Use Node.js `22.23.5` to install dependencies, build, or preview the Astro Starlight documentation site under `docs/`:

```powershell
Set-Location .\docs
npm ci
npm run build
npm run preview
```

---

## Contributing

NBTExplorerCLI is created and maintained by [@Evanlau1798](https://github.com/Evanlau1798). Focused external contributions—such as isolated bug fixes, deterministic regression tests, documentation improvements, and additional language localizations—are welcome.

Before opening an issue or pull request, please read [CONTRIBUTING.md](https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md) for scope invariants, world-safety requirements, and required verification steps.

---

## License

This project is licensed under the [MIT License](LICENSE). Bundled third-party components retain their own terms and notices; see [SharpZipLib](licenses/SharpZipLib.md) and [Unicode](licenses/Unicode.md). The Windows x64 distribution package also includes the .NET runtime license and third-party notices.

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
[doc-getting-started]: https://nbtcli.evanlau1798.com/docs/getting-started/
[doc-safe-editing]: https://nbtcli.evanlau1798.com/docs/safe-editing/
[doc-translation]: https://nbtcli.evanlau1798.com/docs/translation-workflow/
[doc-resources]: https://nbtcli.evanlau1798.com/docs/resources-and-packs/
[doc-backups]: https://nbtcli.evanlau1798.com/docs/backups-and-state/
[doc-reference]: https://nbtcli.evanlau1798.com/docs/command-reference/
[doc-support]: https://nbtcli.evanlau1798.com/docs/support-and-troubleshooting/
