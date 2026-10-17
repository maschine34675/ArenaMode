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
$assetRoot = 'BepInEx/plugins/ArenaMode'
$docsRoot = "$assetRoot/docs"
$relocatedNotices = @(
    'ANNOUNCER-ADDITIONS-20260917.json', 'ANNOUNCER-PACK.md', 'ANNOUNCER-SOURCES.json',
    'AUDIO-LICENSES.md', 'CC-BY-3.0.txt', 'HUD-ICON-LICENSES.md'
)

function Get-InstalledSnapshot {
    $files = @(
        'BepInEx/plugins/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
        'BepInEx/plugins/ArenaMode/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/ArenaMode/maschine-ArenaMode.BigBrainCompat.dll',
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

function Assert-SourceLayoutCompatibility([string]$SourceRoot) {
    # Reject old -ArenaRevision values before any build: the grouped archive
    # needs both relocated asset lookup and an optional, isolated BigBrain entry.
    # These are baseline/source checks, not a substitute for the runtime harness.
    $seams = [ordered]@{
        'ArenaMode.Client/ArenaAssetPaths.cs' = @('static string FromPluginDirectory\(')
        'ArenaMode.Client/Audio/AnnouncerService.cs' = @('ArenaAssetPaths\.FromPluginDirectory\(')
        'ArenaMode.Client/Audio/HitmarkerAudioService.cs' = @('ArenaAssetPaths\.FromPluginDirectory\(')
        'ArenaMode.Client/Pickups/ArenaPickupAudio.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Pickups/ArenaPickupRuntime.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Pickups/ArenaPickupCapture.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Areas/ArenaAreaCapture.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.BigBrainCompat/Plugin.cs' = @(
            'BepInDependency\(BigBrainPluginGuid,\s*BepInDependency\.DependencyFlags\.SoftDependency\)',
            'PluginInfos\.TryGetValue\(BigBrainPluginGuid,', 'BigBrainRuntime\.Register\('
        )
        'ArenaMode.BigBrainCompat/BigBrainRuntime.cs' = @(
            '\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*internal static void Register\('
        )
    }
    foreach ($entry in $seams.GetEnumerator()) {
        $path = Join-Path $SourceRoot $entry.Key
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Selected Arena revision predates package layout v2: missing $($entry.Key). Select the grouped-layout/optional-loader revision or later."
        }
        $body = Get-Content -LiteralPath $path -Raw
        foreach ($pattern in $entry.Value) {
            if ($body -notmatch $pattern) {
                throw "Selected Arena revision lacks a required package layout v2 seam in $($entry.Key). Select the grouped-layout/optional-loader revision or later; review this guard when refactoring."
            }
        }
    }
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
Assert-SourceLayoutCompatibility $source
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
    @{Source=(Join-Path $source 'ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll'); Dest="$assetRoot/maschine-ArenaMode.Client.dll"; Version=$version},
    @{Source=(Join-Path $source 'build/server/Release/maschine-ArenaMode.Server/maschine-ArenaMode.Server.dll'); Dest='SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'; Version=$version},
    @{Source=(Join-Path $coreSource 'Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll'); Dest='BepInEx/plugins/maschine-Redeploy.Core.dll'; Version=$coreVersion},
    @{Source=(Join-Path $source 'ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll'); Dest="$assetRoot/maschine-ArenaMode.BigBrainCompat.dll"; Version=$version}
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
# Record the complete source-to-package mapping before rewriting document links.
# Only tracked files exist in these source snapshots; do not copy a build folder.
$documents = [ordered]@{
    'LICENSE' = "$docsRoot/LICENSE"
    'README.md' = "$docsRoot/README.md"
    'THIRD-PARTY-NOTICES.md' = "$docsRoot/THIRD-PARTY-NOTICES.md"
}
foreach ($mapping in @(
    @{Source='ArenaMode.Client/Assets/ArenaMode'; Dest=$assetRoot},
    @{Source='ArenaMode.Client/Assets/MapPreviews'; Dest="$assetRoot/MapPreviews"}
)) {
    $base = Join-Path $source $mapping.Source
    foreach ($file in (Get-ChildItem -LiteralPath $base -File -Recurse)) {
        if ($file.Name -eq '.gitattributes') { continue }
        $relative = [IO.Path]::GetRelativePath($base, $file.FullName).Replace('\', '/')
        $destination = "$($mapping.Dest)/$relative"
        if ($mapping.Dest -eq $assetRoot -and -not $relative.Contains('/')) {
            if ($relative -notin $relocatedNotices) { throw "Unclassified root asset: $relative" }
            $destination = "$docsRoot/$relative"
        }
        $documents["$($mapping.Source)/$relative"] = $destination
    }
}
foreach ($name in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt', 'CC-BY-3.0.txt')) {
    $documents["ArenaMode.Client/Assets/Ui/$name"] = "$assetRoot/Ui/$name"
}

function Convert-PackageLinks([string]$Body, [string]$SourceRelative, [string]$DestinationRelative) {
    # Keep shipped references local; pin source-only references to the exact build.
    $sourceFolder = Split-Path (Join-Path $source $SourceRelative)
    $destinationFolder = Split-Path (Join-Path $payload $DestinationRelative)
    return [regex]::Replace($Body, '\]\(([^)]+)\)', {
        param($match)
        $target = $match.Groups[1].Value.Trim('<', '>')
        if ($target -match '^[a-z]+:|^#') { return $match.Value }
        $parts = $target.Split('#', 2)
        $resolved = [IO.Path]::GetFullPath((Join-Path $sourceFolder ([Uri]::UnescapeDataString($parts[0]))))
        $sourceTarget = [IO.Path]::GetRelativePath($source, $resolved).Replace('\', '/')
        if ($sourceTarget.StartsWith('../') -or -not (Test-Path -LiteralPath $resolved)) {
            throw "Broken source reference in ${SourceRelative}: $target"
        }
        if ($documents.Contains($sourceTarget)) {
            $relativeTarget = [IO.Path]::GetRelativePath($destinationFolder, (Join-Path $payload $documents[$sourceTarget])).Replace('\', '/')
            $encoded = ($relativeTarget.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        } else {
            $encoded = 'https://github.com/maschine34675/ArenaMode/blob/' + $arenaCommit + '/' +
                (($sourceTarget.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
        }
        if ($parts.Length -eq 2) { $encoded += '#' + $parts[1] }
        return "]($encoded)"
    })
}

foreach ($entry in $documents.GetEnumerator()) {
    $inputFile = Join-Path $source $entry.Key
    if ([IO.Path]::GetExtension($entry.Key) -eq '.md') {
        $originalBody = Get-Content -LiteralPath $inputFile -Raw
        $body = Convert-PackageLinks $originalBody $entry.Key $entry.Value
        if ($entry.Key -eq 'ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-PACK.md') {
            $body = $body.Replace('File (relative to this folder)', 'File (relative to the parent ArenaMode folder)')
        }
        if ($body -ceq $originalBody) {
            # Preserve unchanged legal texts and their recorded source hashes.
            Copy-Payload $inputFile $entry.Value
        } else {
            New-Item -ItemType Directory -Path (Split-Path (Join-Path $payload $entry.Value)) -Force | Out-Null
            $body | Set-Content -LiteralPath (Join-Path $payload $entry.Value) -Encoding utf8 -NoNewline
        }
    } elseif ($entry.Key -match '/ANNOUNCER-(SOURCES|ADDITIONS-20260917)\.json$') {
        # The audio stays byte-identical. Only the packaged provenance record moves.
        $record = Get-Content -LiteralPath $inputFile -Raw | ConvertFrom-Json
        foreach ($clip in $record.files) { $clip.file = '../' + $clip.file }
        $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $payload $entry.Value) -Encoding utf8
    } else {
        Copy-Payload $inputFile $entry.Value
    }
}
Copy-Payload (Join-Path $coreSource 'LICENSE') "$docsRoot/REDEPLOY-CORE-LICENSE.txt"
@"
ArenaMode $version - LOCAL TEST PACKAGE
Arena source: $arenaCommit
Redeploy.Core $coreVersion source: $coreCommit

Requires SPT 4.1.x (native tests have used 4.1.6).
1. Back up the profile and customized Arena config; close the game and server.
2. When updating an older installation, remove the old flat copies
   BepInEx/plugins/maschine-ArenaMode.Client.dll and
   BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll, if present.
   Keep exactly one copy of each Arena DLL anywhere under BepInEx/plugins.
   This ZIP now places both DLLs in BepInEx/plugins/ArenaMode.
   Then copy BepInEx and SPT_Runtime into the SPT installation root.
   Merge the folders; do not replace or delete the whole existing directories.
   The ZIP contains default kits.json and ladder.json. Preserve any intentional
   customizations before overwriting them. No user config.json, client settings,
   BepInEx settings, profiles or other mods are included.
3. Keep exactly one maschine-Redeploy.Core.dll in BepInEx/plugins (including
   its subfolders). Use the supplied version; never downgrade a newer Core.
4. The Arena BigBrain companion is always included. Without BigBrain it stays
   inactive; you do not need to remove its DLL. BigBrain 1.5.0 is the tested
   baseline. Newer versions are allowed with a warning, not certified compatible;
   older versions are not enabled. Bot pickup pursuit, post-death pursuit and
   Molotov support require its usable integration. BigBrain and its own
   prerequisites are NOT included.
5. Start server and game, select ARENA. Optional maps/PFT/SAIN/APBS/replays
   must be installed separately. Fika is unsupported.

Documentation, licenses and PACKAGE-MANIFEST.json are in
BepInEx/plugins/ArenaMode/docs. Retain this folder and the adjacent asset
notices with the package. Audio/artwork have separate terms; the entire package
is not MIT. Relative paths in the audio prose such as sounds/ and pickups/
refer to the parent ArenaMode folder; packaged announcer JSON paths are relative
to their JSON file and their recorded audio hashes are unchanged.
This package has build/offline checks only; packaging is not a new in-game test.
Nothing is installed or uploaded by the packaging script.
SHA256 and exact source revisions are recorded in PACKAGE-MANIFEST.json.
Full source and documentation: https://github.com/maschine34675/ArenaMode
"@ | Set-Content -LiteralPath (Join-Path $payload "$docsRoot/INSTALL.txt") -Encoding utf8

$manifest = [ordered]@{
    schemaVersion=2; layout='spt-root-folders-v2'; kind='local-test'; arenaVersion=$version; arenaCommit=$arenaCommit
    redeployCoreVersion=$coreVersion; redeployCoreCommit=$coreCommit
    builtAtUtc=[DateTime]::UtcNow.ToString('o'); deployed=$false; uploaded=$false
    files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload "$docsRoot/PACKAGE-MANIFEST.json") -Encoding utf8
$zip = Join-Path $run "ArenaMode-$version-test-$($arenaCommit.Substring(0,7)).zip"
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
& python (Join-Path $PSScriptRoot 'Verify-TestRelease.py') $zip
if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed.' }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output "PACKAGE_READY $zip"
Write-Output "SHA256 $hash"
