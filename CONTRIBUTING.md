# Contributing

NBTExplorerCLI was created and is primarily developed and maintained by
[@Evanlau1798](https://github.com/Evanlau1798). Product direction, core architecture, and release
decisions remain with the creator.

External contributions are welcome, but this is an intentionally maintainer-led project. Pull
requests are expected to be small, focused, and easy to review and verify. Good contributions
include isolated bug fixes, deterministic regression tests, documentation corrections, and new
language adaptations for the documentation site.

Before opening a bug report, work through the
[Support Scope and Troubleshooting](https://nbtcli.evanlau1798.com/docs/support-and-troubleshooting/)
guide and reproduce the issue on a disposable world copy using the latest release. Attach the exact
command invocation, CLI exit code, and redacted JSON output; never upload personal Minecraft saves,
player credentials, or private local file system paths.

Large feature branches, broad refactors, architectural rewrites, and unsolicited changes to the core
planner/journal engine or CLI wire schemas are generally not accepted. If you want to propose a
broader change, open a focused issue for discussion before writing code. Prior discussion does not
guarantee acceptance, and large unsolicited pull requests may be closed.

## Scope and invariants

- Keep the project focused on safe, deterministic Windows x64 CLI workflows for Minecraft Java
  Edition worlds.
- **Fail-closed world safety**: Reads and previews must never modify world files. Every world write
  must go through the verified planner, staging, preimage backup, journal, and readback engine, and
  must require explicit apply authorization (`--apply` / `apply`) plus `--world-offline`.
- **No silent data loss or coercion**: Never guess NBT numeric widths, silently drop unrecognized
  tags, or truncate operation IDs, translated strings, or receipts. Resource and regex bounds must
  fail explicitly with their documented exit codes.
- **Deterministic CLI contract**: Every invocation emits a single newline-delimited UTF-8 JSON
  envelope on `stdout`. `--ui-locale` affects human-readable messages only, never machine keys,
  schema families, or exit codes.
- **No emojis**: Do not use emojis anywhere in source code, CLI output, documentation pages, or
  commit messages.
- **Repository hygiene and privacy**: Never commit personal Minecraft worlds, `.test_worlds/`,
  unredacted state/journal directories, build outputs (`bin/`, `obj/`, `docs/dist/`), or absolute
  user paths.

## Before opening a pull request

1. Verify that your environment uses the exact .NET SDK pinned in `global.json` (`10.0.404`).
2. Restore and build in locked Release mode:

   ```powershell
   dotnet restore NBTExplorerCLI.slnx --configfile NuGet.Config --locked-mode
   dotnet build NBTExplorerCLI.slnx -c Release --no-restore
   ```

3. Run the verification suite and confirm all cases pass:

   ```powershell
   dotnet tests/NBTExplorerCLI.Verification/bin/Release/net10.0/NBTExplorerCLI.Verification.dll --suite all --timeout-ms 120000
   ```

4. For behavior changes or bug fixes, add a focused regression test in the verification harness
   using synthetic fixtures rather than real user worlds.
5. If your change alters CLI options or commands, ensure `fixtures/verification/cli-command-surface.json`,
   actual `help` output, and the documentation data in `docs/src/data/cli-surface.ts` remain aligned.
6. If you modified files under `docs/`, verify that the documentation site builds cleanly:

   ```powershell
   Set-Location .\docs
   npm ci
   npm run build
   ```

## Verification gates by change type

Choose the verification scope that matches the boundaries touched by your change:

| Change Area | Required Verification Evidence |
| --- | --- |
| Documentation or translations (`docs/`, `README*.md`) | `npm run build` in `docs/` completes with zero errors, broken links, or emojis. |
| Focused codec, query, scan, or session fix | The smallest regression test for the changed behavior plus the affected verification suite (`--suite <name>`). |
| Planner, journal, recovery, or CLI surface change | Full verification suite (`--suite all`) and command-surface parity check. |
| Packaging or release scripts (`New-WindowsCandidate.ps1`, `.github/workflows/windows.yml`) | Full locked restore, Release build, `--suite all`, and Windows Server Core SDK-free container smoke verification in CI. |
