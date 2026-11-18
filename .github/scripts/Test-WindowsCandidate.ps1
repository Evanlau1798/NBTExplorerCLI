[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [string]$RunDirectory,
    [switch]$RequireSdkFree
)

$ErrorActionPreference = 'Stop'
if ($RequireSdkFree) {
    $forbidden = @(Get-Command dotnet.exe, nbtcli.exe, nbtcli-next.exe, nbtExplorerCLI.exe -ErrorAction SilentlyContinue)
    $installed = @("$env:ProgramFiles/dotnet/dotnet.exe", "$([Environment]::GetFolderPath('ProgramFilesX86'))/dotnet/dotnet.exe")
    if ($forbidden.Count -ne 0 -or @($installed | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0) {
        throw 'The smoke environment already has a .NET host or an NBT CLI.'
    }
    Write-Host "Verified no installed .NET host or NBT CLI on $([Environment]::OSVersion.Version)."
}
if (!$RunDirectory) { $RunDirectory = Join-Path ([IO.Path]::GetTempPath()) "nbt-smoke-$([guid]::NewGuid().ToString('N'))" }
if (Test-Path -LiteralPath $RunDirectory) { throw 'Smoke requires a new output directory.' }
$work = (New-Item -ItemType Directory -Path $RunDirectory).FullName
$archive = Join-Path $InputDirectory 'nbtcli-next-win-x64.zip'
$sum = (Get-Content -LiteralPath (Join-Path $InputDirectory 'SHA256SUMS.txt') -Raw).Trim()
if ($sum -cnotmatch '^([0-9a-f]{64})  nbtcli-next-win-x64\.zip$' -or (Get-FileHash -LiteralPath $archive).Hash -ne $Matches[1]) {
    throw 'Smoke input ZIP digest mismatch.'
}
$app = Join-Path $work 'app'
Expand-Archive -LiteralPath $archive -DestinationPath $app
$manifest = Get-Content -LiteralPath "$app/candidate.json" -Raw | ConvertFrom-Json
$zipFiles = @(Get-ChildItem -LiteralPath $app -File -Force -Recurse)
if ($zipFiles.Count -ne @($manifest.files.PSObject.Properties).Count + 1) { throw 'Candidate files do not match the manifest.' }
foreach ($item in $manifest.files.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $app $item.Name)).Hash -ne $item.Value) { throw "Candidate digest differs: $($item.Name)" }
}
if ($manifest.projectLicense -cne 'MIT' -or !(Test-Path -LiteralPath "$app/LICENSE" -PathType Leaf)) {
    throw 'Project license missing from the candidate.'
}

$cli = Join-Path $app 'nbtcli-next.exe'
function Invoke-VerifiedCli([string]$Label, [int]$ExpectedExit, [string[]]$Arguments) {
    $stdout = & $cli @Arguments
    $actual = $LASTEXITCODE
    $result = $stdout | ConvertFrom-Json
    if ($actual -ne $ExpectedExit -or $result.success -ne ($actual -eq 0) -or $result.schemaFamily -cne 'nbtcli.next.cli') {
        throw "$Label returned exit $actual (expected $ExpectedExit): $stdout"
    }
    Write-Host "$Label exit=$actual outcome=$($result.outcome)"
    return $result.data
}
$null = Invoke-VerifiedCli 'help' 0 @('--help')
$version = Invoke-VerifiedCli 'version' 0 @('--version')
if ($version.product -cne $manifest.product -or $version.version -cne $manifest.version) { throw 'CLI version differs from the manifest.' }
$null = Invoke-VerifiedCli 'english-help' 0 @('help','import-legacy','--ui-locale','en-US')
$world = (New-Item -ItemType Directory -Path "$work/world").FullName
$level = Join-Path $world 'level.dat'
Copy-Item -LiteralPath "$InputDirectory/level-java121.dat" -Destination $level
$before = (Get-FileHash -LiteralPath $level).Hash
$read = Invoke-VerifiedCli 'read-dataversion' 0 @('get',$world,'--path','level.dat','--pointer','/Data/DataVersion')
if ($read.value -ne 3953) { throw 'Unexpected smoke fixture DataVersion.' }
$null = Invoke-VerifiedCli 'scan' 0 @('scan',$world,'--cache-root',"$work/cache")

$legacy = "$InputDirectory/legacy"
$legacyBefore = @(Get-ChildItem -LiteralPath $legacy -Recurse -File -Force | Get-FileHash)
$state = "$work/state"
function Import-Arguments([string]$Profile,[string]$File,[string]$Root) {
    return @('import-legacy',$world,'--input',$File,'--profile',$Profile,'--source-language','en_us',
        '--target-language','zh_tw','--state-root',$Root,'--acknowledge-world-rebind')
}
$cases = @(
    @('duplicate-session','command-session','.nbtcli_translate/session.json',3,'conflict',2),
    @('unknown-reply','reply-array','.nbtcli_translate/translated.json',4,'unsupported',1),
    @('glossary','glossary','glossary.json',0,'retained',1),
    @('questionnaire','questionnaire','.nbtcli_cache/agent_questionnaire.json',0,'retained',1),
    @('settings','agent-config','.nbtcli_cache/agent_config.json',0,'retained',1),
    @('cache','scan-cache','.nbtcli_cache/scan-index.bin',4,'rescanRequired',1),
    @('unknown-journal','unknown','journal.json',4,'unsupported',1)
)
foreach ($case in $cases) {
    $label,$profile,$relative,$exit,$decision,$count = $case
    $file = Join-Path $legacy $relative
    $result = Invoke-VerifiedCli $label $exit (Import-Arguments $profile $file $state)
    $report = Get-Content -LiteralPath "$state/imports/import-$($result.report.importId).json" -Raw | ConvertFrom-Json
    if ($report.schemaFamily -cne 'nbtcli.next.legacy-import' -or $report.schemaVersion -ne 1 -or
        $report.data.inputHash -ne (Get-FileHash -LiteralPath $file).Hash -or
        $report.data.rawInputBase64 -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -or
        @($report.data.items).Count -ne $count -or @($report.data.items | Where-Object decision -cne $decision).Count -ne 0) {
        throw "Legacy report differs: $label"
    }
}

$bulk = "$work/bulk.json"
$terms = [ordered]@{}
for ($index = 0; $index -lt 4096; ++$index) { $terms.Add("term-$index", ('x' * 256)) }
[IO.File]::WriteAllText($bulk, ($terms | ConvertTo-Json -Compress))
$interrupted = "$work/interrupted"
$replayArgs = @(Import-Arguments 'glossary' $bulk $interrupted)
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $cli
$start.Arguments = (($replayArgs + @('--limit','4096')) | ForEach-Object { '"' + $_ + '"' }) -join ' '
$start.WorkingDirectory = $work
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$child = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        if ($child.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Import did not reach the interruption boundary.' }
        Start-Sleep -Milliseconds 50
        $stored = @(Get-ChildItem -LiteralPath "$interrupted/imports" -Filter 'import-*.json' -File -ErrorAction SilentlyContinue)
    } until ($stored.Count -eq 1)
    $storedHash = (Get-FileHash -LiteralPath $stored[0].FullName).Hash
    # Hold stdout unread so a full receipt cannot be delivered before termination.
    if ($child.HasExited) { throw 'Import finished before termination.' }
    $child.Kill()
    if (!$child.WaitForExit(10000) -or $child.ExitCode -eq 0) { throw 'Import was not interrupted.' }
    Write-Host "interrupted-import exit=$($child.ExitCode) reportSha256=$storedHash"
} finally {
    if (!$child.HasExited) { $child.Kill(); [void]$child.WaitForExit(10000) }
    $child.Dispose()
}
$list = Invoke-VerifiedCli 'list-interrupted' 0 @('import-legacy',$world,'--list','--state-root',$interrupted)
$retry = Invoke-VerifiedCli 'retry-interrupted' 0 ($replayArgs + @('--limit','1'))
if (@($list.reports).Count -ne 1 -or $list.reports[0].importId -cne $retry.report.importId -or
    (Get-FileHash -LiteralPath "$interrupted/imports/import-$($retry.report.importId).json").Hash -ne $storedHash) {
    throw 'Import retry did not reuse the original report.'
}
foreach ($item in $legacyBefore) {
    if ((Get-FileHash -LiteralPath $item.Path).Hash -ne $item.Hash) { throw 'A legacy original changed.' }
}
if ((Get-FileHash -LiteralPath $level).Hash -ne $before) { throw 'Import changed the world.' }

$typed = (Invoke-VerifiedCli 'read-typed' 0 @('get',$world,'--path','level.dat','--pointer','/Data','--types')).value
$typed.data.root.value += @{name='LevelName';value=@{tag='string';value='Offline candidate smoke'}}
[IO.File]::WriteAllText("$work/typed.json", ($typed | ConvertTo-Json -Depth 30 -Compress))
$null = Invoke-VerifiedCli 'durable-write' 0 @('set',$world,'--path','level.dat','--pointer','/Data','--value-file',
    "$work/typed.json",'--state-root',$state,'--apply','--world-offline')
$after = Invoke-VerifiedCli 'read-postimage' 0 @('get',$world,'--path','level.dat','--pointer','/Data/LevelName')
if ($after.value -cne 'Offline candidate smoke' -or (Get-FileHash -LiteralPath $level).Hash -eq $before) { throw 'Expected verified postimage was not published.' }
Write-Host "Candidate $($manifest.revision), SHA256 $($sum.Substring(0,64)), passed Windows smoke."
