#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$RuntimePackage,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Revision,
    [Parameter(Mandatory)][string]$OutputZip
)

$ErrorActionPreference = 'Stop'
$runtimeVersion = '10.0.15'
$runtimeId = 'Microsoft.NETCore.App.Runtime.win-x64'
$runtimeDigest = '7423f58917342b9e4bf276fb164d5726d2c90fe2fe989c552b9e39056256a035'
$sharpDigest = '32ea2d0ce3512e74f1c7ad82591fe67e6b8939d76a8a4ff9c93ead030131e71c'
$publish = (Get-Item -LiteralPath $PublishDirectory).FullName
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
if (Test-Path -LiteralPath $target) { throw 'The candidate ZIP already exists.' }
if ([IO.Path]::GetExtension($target) -ine '.zip') { throw 'The output must be a ZIP path.' }
$version = ([xml][IO.File]::ReadAllText("$PSScriptRoot/Directory.Build.props")).Project.PropertyGroup.Version
$product = @('nbtcli', 'NBTExplorerCLI.Application', 'NBTExplorerCLI.Domain', 'NBTExplorerCLI.Formats', 'NBTExplorerCLI.Infrastructure', 'NBTExplorerCLI.Minecraft')
$paths = [Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
$hashes = [ordered]@{}

function Add-CandidateFile([string]$Name, [string]$Path, [string]$Expected = '') {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Expected a regular file: $Name" }
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Expected -and $hash -cne $Expected) { throw "Digest differs: $Name" }
    $paths.Add($Name, $file.FullName)
    $hashes.Add($Name, $hash)
}

foreach ($name in $product) {
    $file = "$publish/$name.dll"
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductVersion -cne "$version+$Revision") {
        throw "Source or product version differs: $name.dll"
    }
    Add-CandidateFile "$name.dll" $file
}
foreach ($name in 'nbtcli.exe', 'nbtcli.deps.json', 'nbtcli.runtimeconfig.json') {
    Add-CandidateFile $name "$publish/$name"
}
Add-CandidateFile 'ICSharpCode.SharpZipLib.dll' "$publish/ICSharpCode.SharpZipLib.dll" $sharpDigest

$config = (Get-Content -LiteralPath "$publish/nbtcli.runtimeconfig.json" -Raw | ConvertFrom-Json).runtimeOptions
if ($config.framework -or $config.frameworks -or @($config.includedFrameworks).Count -ne 1 -or
    $config.includedFrameworks[0].name -cne 'Microsoft.NETCore.App' -or $config.includedFrameworks[0].version -cne $runtimeVersion) {
    throw 'The app does not include the pinned .NET runtime.'
}
$deps = Get-Content -LiteralPath "$publish/nbtcli.deps.json" -Raw | ConvertFrom-Json -AsHashtable
$expectedLibraries = @($product | ForEach-Object { "$_/$version" }) + @("runtimepack.$runtimeId/$runtimeVersion", 'SharpZipLib/1.4.2')
if ($deps.runtimeTarget.name -cne '.NETCoreApp,Version=v10.0/win-x64' -or
    (Compare-Object $expectedLibraries @($deps.libraries.Keys) -CaseSensitive)) {
    throw 'The Windows x64 dependency graph differs.'
}

$packPath = (Get-Item -LiteralPath $RuntimePackage).FullName
$packFile = [IO.File]::OpenRead($packPath)
try {
    if ((Get-FileHash -InputStream $packFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $runtimeDigest) {
        throw 'The .NET runtime package digest differs.'
    }
    $packFile.Position = 0
    $pack = [IO.Compression.ZipArchive]::new($packFile, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        $reader = [IO.StreamReader]::new($pack.GetEntry('data/RuntimeList.xml').Open())
        try { [xml]$runtimeFiles = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($asset in $runtimeFiles.FileList.File) {
            $name = [IO.Path]::GetFileName($asset.Path)
            $stream = $pack.GetEntry($asset.Path).Open()
            try { $digest = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant() }
            finally { $stream.Dispose() }
            Add-CandidateFile $name "$publish/$name" $digest
        }

        $allowed = @($paths.Keys) + @($product | ForEach-Object { "$_.pdb" }) + @('licenses')
        foreach ($entry in Get-ChildItem -LiteralPath $publish -Force) {
            if ($entry.Name -cnotin $allowed -or ($entry.PSIsContainer -ne ($entry.Name -ceq 'licenses')) -or
                ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unexpected publish artifact: $($entry.Name)" }
        }
        Add-CandidateFile 'README.md' "$PSScriptRoot/README.md"
        Add-CandidateFile 'README.zh-TW.md' "$PSScriptRoot/README.zh-TW.md"
        foreach ($name in 'SharpZipLib.md', 'Unicode.md') {
            Add-CandidateFile "licenses/$name" "$PSScriptRoot/licenses/$name"
        }
        Add-CandidateFile 'LICENSE' "$PSScriptRoot/LICENSE"

        $runtimeNotices = [ordered]@{'licenses/dotnet-LICENSE.txt' = 'LICENSE.TXT'; 'licenses/dotnet-THIRD-PARTY-NOTICES.txt' = 'THIRD-PARTY-NOTICES.TXT'}
        foreach ($name in $runtimeNotices.Keys) {
            $stream = $pack.GetEntry($runtimeNotices[$name]).Open()
            try { $hashes.Add($name, (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()) }
            finally { $stream.Dispose() }
        }
        $manifest = [ordered]@{
            product = 'nbtcli'; version = $version; revision = $Revision; rid = 'win-x64'; configuration = 'Release'; sdk = '10.0.404'
            dependencies = @(
                @{ name = $runtimeId; version = $runtimeVersion; license = 'MIT and bundled third-party notices'; packageSha256 = $runtimeDigest },
                @{ name = 'SharpZipLib'; version = '1.4.2'; license = 'MIT' },
                @{ name = 'Unicode Character Database'; version = '15.0.0'; license = 'Bundled Unicode data license' }
            )
            projectLicense = 'MIT'
            files = $hashes
        }

        $staged = "$target.$([guid]::NewGuid().ToString('N')).tmp"
        $created = $false
        try {
            $output = [IO.File]::Open($staged, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $created = $true
            try {
                $zip = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $true)
                try {
                    foreach ($entry in $paths.GetEnumerator()) {
                        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key)
                    }
                    foreach ($name in $runtimeNotices.Keys) {
                        $source = $pack.GetEntry($runtimeNotices[$name]).Open()
                        try {
                            $destination = $zip.CreateEntry($name).Open()
                            try { $source.CopyTo($destination) } finally { $destination.Dispose() }
                        } finally { $source.Dispose() }
                    }
                    $writer = [IO.StreamWriter]::new($zip.CreateEntry('candidate.json').Open())
                    try { $writer.WriteLine(($manifest | ConvertTo-Json -Depth 6)) } finally { $writer.Dispose() }
                } finally { $zip.Dispose() }
            } finally { $output.Dispose() }
            $check = [IO.Compression.ZipFile]::OpenRead($staged)
            try {
                if ($check.Entries.Count -ne $hashes.Count + 1 -or $null -eq $check.GetEntry('candidate.json')) { throw 'Candidate inventory differs.' }
                foreach ($name in $hashes.Keys) {
                    $stream = $check.GetEntry($name).Open()
                    try { $digest = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant() }
                    finally { $stream.Dispose() }
                    if ($digest -cne $hashes[$name]) { throw "Candidate entry differs: $name" }
                }
            } finally { $check.Dispose() }
            $archiveHash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::Move($staged, $target)
            $created = $false
            [ordered]@{ file = [IO.Path]::GetFileName($target); sha256 = $archiveHash; version = $version; revision = $Revision; entries = $hashes.Count + 1 } | ConvertTo-Json
        } finally {
            if ($created) { [IO.File]::Delete($staged) }
        }
    } finally { $pack.Dispose() }
} finally { $packFile.Dispose() }
