[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [string]$RunDirectory,
    [switch]$RequireSdkFree
)

$ErrorActionPreference = 'Stop'
if ($RequireSdkFree) {
    $forbidden = @(Get-Command dotnet.exe, nbtcli.exe -ErrorAction SilentlyContinue)
    $installed = @("$env:ProgramFiles/dotnet/dotnet.exe", "$([Environment]::GetFolderPath('ProgramFilesX86'))/dotnet/dotnet.exe")
    if ($forbidden.Count -ne 0 -or @($installed | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0) {
        throw 'The smoke environment already has a .NET host or an NBT CLI.'
    }
    Write-Host "Verified no installed .NET host or NBT CLI on $([Environment]::OSVersion.Version)."
}
if (!$RunDirectory) { $RunDirectory = Join-Path ([IO.Path]::GetTempPath()) "nbt-smoke-$([guid]::NewGuid().ToString('N'))" }
if (Test-Path -LiteralPath $RunDirectory) { throw 'Smoke requires a new output directory.' }
$work = (New-Item -ItemType Directory -Path $RunDirectory).FullName
$archive = Join-Path $InputDirectory 'nbtcli-win-x64.zip'
$sum = (Get-Content -LiteralPath (Join-Path $InputDirectory 'SHA256SUMS.txt') -Raw).Trim()
if ($sum -cnotmatch '^([0-9a-f]{64})  nbtcli-win-x64\.zip$' -or (Get-FileHash -LiteralPath $archive).Hash -ne $Matches[1]) {
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

$cli = Join-Path $app 'nbtcli.exe'
function Invoke-VerifiedCli([string]$Label, [int]$ExpectedExit, [string[]]$Arguments) {
    $stdout = & $cli @Arguments
    $actual = $LASTEXITCODE
    $result = $stdout | ConvertFrom-Json
    if ($actual -ne $ExpectedExit -or $result.success -ne ($actual -eq 0) -or $result.schemaFamily -cne 'nbtcli.cli') {
        throw "$Label returned exit $actual (expected $ExpectedExit): $stdout"
    }
    Write-Host "$Label exit=$actual outcome=$($result.outcome)"
    return $result.data
}
$null = Invoke-VerifiedCli 'help' 0 @('--help')
$version = Invoke-VerifiedCli 'version' 0 @('--version')
if ($version.product -cne 'nbtcli' -or $version.product -cne $manifest.product -or $version.version -cne $manifest.version) { throw 'CLI version differs from the manifest.' }
$null = Invoke-VerifiedCli 'english-help' 0 @('help','translate','--ui-locale','en-US')
$world = (New-Item -ItemType Directory -Path "$work/world").FullName
$level = Join-Path $world 'level.dat'
Copy-Item -LiteralPath "$InputDirectory/level-java121.dat" -Destination $level
$before = (Get-FileHash -LiteralPath $level).Hash
$read = Invoke-VerifiedCli 'read-dataversion' 0 @('get',$world,'--path','level.dat','--pointer','/Data/DataVersion')
if ($read.value -ne 3953) { throw 'Unexpected smoke fixture DataVersion.' }
$null = Invoke-VerifiedCli 'scan' 0 @('scan',$world,'--cache-root',"$work/cache")

$state = "$work/state"
if ((Get-FileHash -LiteralPath $level).Hash -ne $before) { throw 'Read-only commands changed the world.' }

$typed = (Invoke-VerifiedCli 'read-typed' 0 @('get',$world,'--path','level.dat','--pointer','/Data','--types')).value
$typed.data.root.value += @{name='LevelName';value=@{tag='string';value='Offline candidate smoke'}}
[IO.File]::WriteAllText("$work/typed.json", ($typed | ConvertTo-Json -Depth 30 -Compress))
$backup = Invoke-VerifiedCli 'backup' 0 @('backup',$world,'--path','level.dat','--state-root',$state)
$null = Invoke-VerifiedCli 'durable-write' 0 @('set',$world,'--path','level.dat','--pointer','/Data','--value-file',
    "$work/typed.json",'--state-root',$state,'--apply','--world-offline')
$after = Invoke-VerifiedCli 'read-postimage' 0 @('get',$world,'--path','level.dat','--pointer','/Data/LevelName')
if ($after.value -cne 'Offline candidate smoke' -or (Get-FileHash -LiteralPath $level).Hash -eq $before) { throw 'Expected verified postimage was not published.' }
$null = Invoke-VerifiedCli 'restore' 0 @('restore',$world,'--path','level.dat','--manifest-hash',$backup.manifestHash,'--state-root',$state,'--apply','--world-offline')
if ((Get-FileHash -LiteralPath $level).Hash -ne $before) { throw 'Verified restore did not reproduce the original world bytes.' }
Write-Host "Candidate $($manifest.revision), SHA256 $($sum.Substring(0,64)), passed Windows smoke."
