#Requires -Version 7.0
# Builds a LOCAL test package from committed sources. Never deploys or uploads.
[CmdletBinding()]
param(
    [string]$SptPath = (Join-Path $PSScriptRoot '..\..\..'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\test-releases'),
    [string]$ArenaRevision
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$coreRepo = (Resolve-Path (Join-Path $repo '..\Redeploy')).Path
$SptPath = (Resolve-Path $SptPath).Path
foreach ($root in @($repo, $coreRepo)) {
    if ($root -eq $repo -and $ArenaRevision) { continue }
    & git -C $root diff --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw "Commit tracked changes before packaging: $root" }
}
$arenaRef = if ($ArenaRevision) { $ArenaRevision } else { 'HEAD' }
$arenaCommit = (& git -C $repo rev-parse --verify --end-of-options "$arenaRef^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Arena source revision.' }
$coreCommit = (& git -C $coreRepo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Core source revision.' }
$run = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$payload = Join-Path $run 'package'
New-Item -ItemType Directory -Path $payload | Out-Null

function Get-InstalledSnapshot {
    $files = @(
        'BepInEx/plugins/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
        'BepInEx/plugins/maschine-Redeploy.Core.dll',
        'BepInEx/config/com.maschine.ArenaMode.cfg'
    )
    foreach ($folder in @('BepInEx/plugins/ArenaMode', 'SPT_Runtime/user/mods/ArenaMode')) {
        $path = Join-Path $SptPath $folder
        if (Test-Path -LiteralPath $path) {
            $files += Get-ChildItem -LiteralPath $path -File -Recurse | ForEach-Object {
                [IO.Path]::GetRelativePath($SptPath, $_.FullName).Replace('\', '/')
            }
        }
    }
    $state = [ordered]@{}
    foreach ($file in ($files | Sort-Object -Unique)) {
        $path = Join-Path $SptPath $file
        $state[$file] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'ABSENT' }
    }
    return ($state | ConvertTo-Json -Compress)
}

function Invoke-Logged([string]$LogName, [string[]]$Arguments) {
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $run $LogName) | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed; see $LogName" }
}

function Copy-Payload([string]$Source, [string]$Relative) {
    $destination = Join-Path $payload $Relative
    if (Test-Path -LiteralPath $destination) { throw "Duplicate package destination: $Relative" }
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination
}

# No shared obj/bin files, Git history, ignored artifacts, local configs or profiles.
foreach ($entry in @(@{Name='ArenaMode'; Root=$repo; Commit=$arenaCommit}, @{Name='Redeploy'; Root=$coreRepo; Commit=$coreCommit})) {
    $archive = Join-Path $run ($entry.Name + '-source.zip')
    & git -C $entry.Root archive --format=zip "--output=$archive" $entry.Commit
    if ($LASTEXITCODE -ne 0) { throw "Source snapshot failed: $($entry.Name)" }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $run ('source/' + $entry.Name)))
}
$source = Join-Path $run 'source/ArenaMode'
$coreSource = Join-Path $run 'source/Redeploy'
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $source 'Shared.props'))).Project.PropertyGroup.Version
$coreVersion = ([xml](Get-Content -Raw -LiteralPath (Join-Path $coreSource 'Redeploy.Core/CoreVersion.props'))).Project.PropertyGroup.RedeployCoreVersion
$noDeploy = Join-Path $run 'NoDeploy.targets'
@'
<Project>
  <PropertyGroup>
    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
    <SourceRevisionId Condition="'$(MSBuildProjectName)' == 'Redeploy.Core'">__CORE_COMMIT__</SourceRevisionId>
    <SourceRevisionId Condition="'$(MSBuildProjectName)' != 'Redeploy.Core'">__ARENA_COMMIT__</SourceRevisionId>
  </PropertyGroup>
  <Target Name="SPTClientDeploy" />
  <Target Name="DeployArenaAudioAssets" />
  <Target Name="DeployToSpt" />
</Project>
'@.Replace('__CORE_COMMIT__', $coreCommit).Replace('__ARENA_COMMIT__', $arenaCommit) | Set-Content -LiteralPath $noDeploy -Encoding utf8
$neverDeploy = Join-Path $run 'never-deploy'
if (Test-Path -LiteralPath $neverDeploy) { throw 'Deployment sentinel must not exist.' }
$props = @(
    "-p:SPTPath=$SptPath", "-p:SPTUserPath=$(Join-Path $SptPath 'SPT_Runtime')",
    "-p:SPTUserMods=$neverDeploy", '-p:EnableSourceControlManagerQueries=false',
    "-p:CustomAfterMicrosoftCommonTargets=$noDeploy"
)
$before = Get-InstalledSnapshot
$oldDatabase = $env:SPT_DATABASE
try {
    $env:SPT_DATABASE = Join-Path $SptPath 'SPT_Runtime/SPT_Data/database'
    # Build project graphs directly: the solution omits Core and can otherwise
    # assign that external reference Debug even when the solution is Release.
    Invoke-Logged 'build-client.log' (@('build', (Join-Path $source 'ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj'), '-c', 'Release', '--nologo') + $props)
    Invoke-Logged 'build-server-tests.log' (@('build', (Join-Path $source 'ArenaMode.Tests/ArenaMode.Tests.csproj'), '-c', 'Release', '--nologo') + $props)
    Invoke-Logged 'tests.log' (@('test', (Join-Path $source 'ArenaMode.Tests/ArenaMode.Tests.csproj'), '-c', 'Release', '--no-build', '--no-restore', '--nologo', '--logger', 'trx', '--results-directory', (Join-Path $run 'test-results')) + $props)
} finally {
    $env:SPT_DATABASE = $oldDatabase
    if ((Get-InstalledSnapshot) -cne $before) { throw 'Installed Arena/Core files changed unexpectedly; package creation stopped.' }
    if (Test-Path -LiteralPath $neverDeploy) { throw 'Server deployment sentinel was created unexpectedly.' }
}

$dlls = @(
    @{Source=(Join-Path $source 'ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll'); Dest='BepInEx/plugins/maschine-ArenaMode.Client.dll'; Version=$version},
    @{Source=(Join-Path $source 'build/server/Release/maschine-ArenaMode.Server/maschine-ArenaMode.Server.dll'); Dest='SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'; Version=$version},
    @{Source=(Join-Path $coreSource 'Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll'); Dest='BepInEx/plugins/maschine-Redeploy.Core.dll'; Version=$coreVersion},
    @{Source=(Join-Path $source 'ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll'); Dest='Optional/BigBrain-1.5.0/BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll'; Version=$version}
)
foreach ($dll in $dlls) {
    $actual = [Reflection.AssemblyName]::GetAssemblyName($dll.Source).Version
    if ($actual -ne [version]($dll.Version + '.0')) { throw "Unexpected assembly version: $($dll.Dest): $actual" }
    $revision = if ($dll.Dest.EndsWith('maschine-Redeploy.Core.dll')) { $coreCommit } else { $arenaCommit }
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll.Source).ProductVersion
    if ($productVersion -ne ($dll.Version + '+' + $revision)) { throw "Unexpected embedded source revision: $($dll.Dest): $productVersion" }
    Copy-Payload $dll.Source $dll.Dest
}
foreach ($name in @('kits.json', 'ladder.json')) {
    Copy-Payload (Join-Path $source "ArenaMode.Server/defaults/$name") "SPT_Runtime/user/mods/ArenaMode/config/$name"
}
# Only tracked files exist in these source snapshots; do not copy a build folder.
foreach ($mapping in @(
    @{Source='ArenaMode.Client/Assets/ArenaMode'; Dest='BepInEx/plugins/ArenaMode'},
    @{Source='ArenaMode.Client/Assets/MapPreviews'; Dest='BepInEx/plugins/ArenaMode/MapPreviews'}
)) {
    $base = Join-Path $source $mapping.Source
    foreach ($file in (Get-ChildItem -LiteralPath $base -File -Recurse)) {
        if ($file.Name -eq '.gitattributes') { continue }
        Copy-Payload $file.FullName (Join-Path $mapping.Dest ([IO.Path]::GetRelativePath($base, $file.FullName)))
    }
}
foreach ($name in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt', 'CC-BY-3.0.txt')) {
    Copy-Payload (Join-Path $source "ArenaMode.Client/Assets/Ui/$name") "BepInEx/plugins/ArenaMode/Ui/$name"
}
Copy-Payload (Join-Path $source 'LICENSE') 'LICENSE'
Copy-Payload (Join-Path $coreSource 'LICENSE') 'REDEPLOY-CORE-LICENSE.txt'
foreach ($name in @('README.md', 'THIRD-PARTY-NOTICES.md')) {
    # Source-relative citations must still work in a binary-only distribution.
    $body = Get-Content -LiteralPath (Join-Path $source $name) -Raw
    $body = [regex]::Replace($body, '\]\(([^)]+)\)', {
        param($match)
        $target = $match.Groups[1].Value.Trim('<', '>')
        if ($target -match '^[a-z]+:|^#' -or $target -in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md')) { return $match.Value }
        $parts = $target.Split('#', 2)
        $encoded = ($parts[0].Split('/') | ForEach-Object { [Uri]::EscapeDataString([Uri]::UnescapeDataString($_)) }) -join '/'
        if ($parts.Length -eq 2) { $encoded += '#' + $parts[1] }
        return "](https://github.com/maschine34675/ArenaMode/blob/$arenaCommit/$encoded)"
    })
    $body | Set-Content -LiteralPath (Join-Path $payload $name) -Encoding utf8
}
@"
ArenaMode $version - LOCAL TEST PACKAGE
Arena source: $arenaCommit
Redeploy.Core $coreVersion source: $coreCommit

Requires SPT 4.1.x (native tests have used 4.1.6).
1. Back up the profile and customized Arena config; close the game and server.
2. Copy BepInEx and SPT_Runtime from this ZIP into the SPT installation root.
   Merge the folders; do not replace or delete the whole existing directories.
   The ZIP contains default kits.json and ladder.json. Preserve any intentional
   customizations before overwriting them. No user config.json, client settings,
   BepInEx settings, profiles or other mods are included.
3. Keep exactly one maschine-Redeploy.Core.dll in BepInEx/plugins (including
   its subfolders). Use the supplied version; never downgrade a newer Core.
4. OPTIONAL: only with BigBrain EXACTLY 1.5.0 installed, copy the DLL from
   Optional/BigBrain-1.5.0/BepInEx/plugins into your real BepInEx/plugins.
   This enables Arena's bot pickup pursuit, post-death pursuit and Molotov
   support. BigBrain and its own prerequisites are NOT included.
5. Start server and game, select ARENA. Optional maps/PFT/SAIN/APBS/replays
   must be installed separately. Fika is unsupported.

Retain LICENSE, REDEPLOY-CORE-LICENSE.txt and THIRD-PARTY-NOTICES.md with this
package. Audio/artwork have separate terms; the entire package is not MIT.
This package has build/offline checks only; packaging is not a new in-game test.
Nothing is installed or uploaded by the packaging script.
SHA256 and exact source revisions are recorded in PACKAGE-MANIFEST.json.
Full source and documentation: https://github.com/maschine34675/ArenaMode
"@ | Set-Content -LiteralPath (Join-Path $payload 'INSTALL.txt') -Encoding utf8

$manifest = [ordered]@{
    schemaVersion=1; kind='local-test'; arenaVersion=$version; arenaCommit=$arenaCommit
    redeployCoreVersion=$coreVersion; redeployCoreCommit=$coreCommit
    builtAtUtc=[DateTime]::UtcNow.ToString('o'); deployed=$false; uploaded=$false
    files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'PACKAGE-MANIFEST.json') -Encoding utf8
$zip = Join-Path $run "ArenaMode-$version-test-$($arenaCommit.Substring(0,7)).zip"
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
& python (Join-Path $PSScriptRoot 'Verify-TestRelease.py') $zip
if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed.' }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output "PACKAGE_READY $zip"
Write-Output "SHA256 $hash"
