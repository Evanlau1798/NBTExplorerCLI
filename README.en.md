# NBTExplorerCLI

[繁體中文](README.md)

NBTExplorerCLI is a Windows x64 command-line tool for Minecraft Java Edition worlds. Use it to inspect NBT, scan text, manage translations, generate resource packs, and preview and verify world edits. Test the workflow on a world copy. Close the game and other writers before editing a world.

## Install and inspect

Download the Windows x64 ZIP from GitHub Releases. Compare its SHA-256 with `SHA256SUMS.txt` from the same release, then extract the whole archive to a new folder. Keep the DLLs beside the executable. The package includes the .NET runtime.

```powershell
Get-FileHash .\nbtcli-next-win-x64.zip -Algorithm SHA256
.\nbtcli-next.exe --version
.\nbtcli-next.exe --help
.\nbtcli-next.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

Run `help <command>` for its options. The CLI emits one JSON response and supports `--ui-locale zh-TW` and `en-US`. Preview writes and check the plan and source before applying. Give `--world-offline` only after closing all writers. If a commit is interrupted, use the same state root and plan hash with `status`, then select the appropriate `recover` operation.

## Migrate legacy data

Keep the original world, backups, translations, glossary, questionnaire, and settings. The questionnaire, settings, and notes under `.nbtcli_cache` are user data. Select an explicit legacy profile and a world copy. Store the new state and report outside the world directory.

```powershell
.\nbtcli-next.exe import-legacy 'D:\WorldCopy' --input 'D:\Legacy\session.json' `
  --profile command-session --source-language en_us --target-language zh_tw `
  --state-root 'D:\NextState' --output 'D:\Migration\session-report.json'
```

The report contains the original bytes, their digest, and individual decisions. Protect it the same way as the source. Reconfirm questionnaires and preferences through `agent setup` and `agent submit`. After verifying the world copy and its source, use `--acknowledge-world-rebind --create-session` to project an eligible import. Review ambiguous, changed, or unresolved sources manually. Importing alone leaves world data intact.

After interruption, replay the same input and options, or use `import-legacy <world> --list` to find the report ID and `--import-id <ID> --create-session` to resume projection. Use verified backup and journal data for rollback. Switching executables leaves existing writes in place.

## Build and release

Use the SDK selected by `global.json`. Run build and verification separately.

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/Next.Verification/bin/Release/net10.0/Next.Verification.dll --suite all --timeout-ms 120000
```

GitHub Actions builds and verifies the Windows x64 candidate. The release workflow publishes verified tagged builds.

## License

The project is licensed under [MIT](LICENSE). Bundled third-party components retain their own terms and notices. See [SharpZipLib](licenses/SharpZipLib.md) and [Unicode](licenses/Unicode.md). The Windows package also includes the .NET license and third-party notices.
