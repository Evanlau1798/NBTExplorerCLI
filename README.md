# NBTExplorerCLI

[繁體中文](README.zh-TW.md) | [Product Site](https://nbtcli.evanlau1798.com/) | [Documentation](https://nbtcli.evanlau1798.com/docs/) | [Contributing](https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md)

**NBTExplorerCLI** (`nbtcli.exe`) is a self-contained Windows x64 command-line tool for **Minecraft Java Edition** worlds. It provides a deterministic, single-JSON-response interface to inspect NBT structures, scan and extract in-world text, manage paginated translation sessions, generate resource packs, and apply verified offline world edits with automatic pre-write backups and crash recovery.

## Key Capabilities

- **Safe Read-Only Inspection (`get`, `list`, `search`)**: Query `level.dat`, `.mca` region chunks, entity regions, and datapack JSON files using JSON Pointers (`--pointer`) or explicit segment arrays (`--segments`), in either human-readable display mode or reversible `typed-nbt` mode (`--types`).
- **World Text Scanning and Validation (`scan`, `extract`, `validate`, `capabilities`)**: Index text across 21 Minecraft text roles (signs, books, items, entities, containers, command blocks, functions, scoreboards, bossbars, advancements, and language files) with an external rebuildable scan cache.
- **Paginated Map Localization (`translate`, `agent`)**: Organize large adventure map translations into verifiable pages and slots, maintain terminology glossaries and translator notes, and seal completed sessions only after durable world verification.
- **Resource Packs and Key Conversion (`convert`, `translate plan --keys`, `export-lang`, `create-pack`)**: Convert literal world text into translation keys, export standalone language JSON files, or build and merge client resource packs while protecting existing assets.
- **Crash-Safe Offline Editing (`set`, `batch`, `plan`, `apply`, `status`, `recover`, `backup`, `restore`)**: Preview every modification as an immutable `nbtcli.patch-plan` before writing. Offline commits automatically stage candidates, store content-addressed preimages, record journals, and support deterministic recovery or rollback.

## Quick Start (Windows x64)

Download `nbtcli-win-x64.zip` and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/Evanlau1798/NBTExplorerCLI/releases) or a verified [GitHub Actions candidate](https://github.com/Evanlau1798/NBTExplorerCLI/actions). The archive bundles the required .NET runtime (`10.0.15`) and libraries—keep all extracted files together in the same directory.

```powershell
# 1. Verify the archive SHA-256 digest against SHA256SUMS.txt
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt

# 2. Extract the archive and check the executable
Expand-Archive -Path .\nbtcli-win-x64.zip -DestinationPath .\nbtcli
.\nbtcli\nbtcli.exe --version
.\nbtcli\nbtcli.exe --help

# 3. Inspect a disposable copy of a Minecraft world (read-only)
.\nbtcli\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

Run `.\nbtcli.exe help <command>` to inspect accepted flags. Pass `--ui-locale zh-TW` or `--ui-locale en-US` to select the CLI diagnostic language independently of world translation locales. Always test workflows on a world copy, preview plans before writing, and pass `--world-offline` only after closing Minecraft and any other editors. If a write is interrupted, run `status` with the same `--state-root` and `--plan-hash`, then run `recover`.

## Documentation

Full guides, safety explanations, and command schemas are available on the documentation site:

- **English Documentation**: [https://nbtcli.evanlau1798.com/docs/](https://nbtcli.evanlau1798.com/docs/)
  - [Install and Inspect](https://nbtcli.evanlau1798.com/docs/getting-started/)
  - [Safe Editing and Recovery](https://nbtcli.evanlau1798.com/docs/safe-editing/)
  - [Translation Workflow](https://nbtcli.evanlau1798.com/docs/translation-workflow/)
  - [Resource Packs and References](https://nbtcli.evanlau1798.com/docs/resources-and-packs/)
  - [Backups and State Files](https://nbtcli.evanlau1798.com/docs/backups-and-state/)
  - [Command Reference](https://nbtcli.evanlau1798.com/docs/command-reference/)
  - [Support Scope and Troubleshooting](https://nbtcli.evanlau1798.com/docs/support-and-troubleshooting/)
- **繁體中文文檔**: [https://nbtcli.evanlau1798.com/zh-tw/docs/](https://nbtcli.evanlau1798.com/zh-tw/docs/)

## Build and Verification

Use the .NET SDK specified in `global.json` (`10.0.404`). Run restore, build, and the verification harness as separate steps:

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

Use Node.js 22.23.5 to preview or build the Astro Starlight documentation site under `docs/`.

```powershell
Set-Location .\docs
npm ci
npm run build
npm run preview
```

## Contributing

NBTExplorerCLI is created and maintained by [@Evanlau1798](https://github.com/Evanlau1798). Focused external contributions—such as isolated bug fixes, deterministic regression tests, documentation improvements, and additional documentation translations—are welcome.

Before opening an issue or pull request, please read [CONTRIBUTING.md](https://github.com/Evanlau1798/NBTExplorerCLI/blob/main/CONTRIBUTING.md) for scope invariants, world-safety requirements, and required verification steps.

## License

This project is licensed under the [MIT License](LICENSE). Bundled third-party components retain their own terms and notices; see [SharpZipLib](licenses/SharpZipLib.md) and [Unicode](licenses/Unicode.md). The Windows x64 distribution package also includes the .NET runtime license and third-party notices.
