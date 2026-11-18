# NBTExplorerCLI

[繁體中文](README.md)

[Product home](https://nbtcli.evanlau1798.com/) · [Documentation](https://nbtcli.evanlau1798.com/docs)

NBTExplorerCLI is a Windows x64 command-line tool for Minecraft Java Edition worlds. Use it to inspect NBT, scan text, manage translations, generate resource packs, and preview and verify world edits. Test the workflow on a world copy. Close the game and other writers before editing a world.

## Install and inspect

Download the Windows x64 ZIP from [GitHub Releases](https://github.com/Evanlau1798/NBTExplorerCLI/releases) or the matching [Actions candidate](https://github.com/Evanlau1798/NBTExplorerCLI/actions). Verify the ZIP against the accompanying `SHA256SUMS.txt`, then extract the whole archive to a new folder. Keep the DLLs beside the executable. The package includes the .NET runtime.

```powershell
Get-FileHash .\nbtcli-win-x64.zip -Algorithm SHA256
.\nbtcli.exe --version
.\nbtcli.exe --help
.\nbtcli.exe get 'D:\WorldCopy' --path level.dat --pointer /Data/DataVersion
```

Run `help <command>` for its options. The CLI emits one JSON response and supports `--ui-locale zh-TW` and `en-US`. Preview writes and check the plan and source before applying. Give `--world-offline` only after closing all writers. If a commit is interrupted, use the same state root and plan hash with `status`, then select the appropriate `recover` operation.

## Build and release

Use the SDK selected by `global.json`. Run build and verification separately.

```powershell
dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
dotnet build NBTExplorerCLI.slnx -c Release --no-restore
dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
```

GitHub Actions builds and verifies the Windows x64 candidate. The release workflow publishes verified tagged builds.

## License

The project is licensed under [MIT](LICENSE). Bundled third-party components retain their own terms and notices. See [SharpZipLib](licenses/SharpZipLib.md) and [Unicode](licenses/Unicode.md). The Windows package also includes the .NET license and third-party notices.
