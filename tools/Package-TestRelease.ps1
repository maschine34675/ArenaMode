#Requires -Version 7.0
# Builds an installable package from committed sources. Never deploys or uploads.
[CmdletBinding()]
param(
    [string]$SptPath = (Join-Path $PSScriptRoot '..\..\..'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\test-releases'),
    [string]$ArenaRevision,
    [switch]$Release,
    [string]$CoreAssemblyPath,
    [string]$CoreAssemblySha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$coreRepo = (Resolve-Path (Join-Path $repo '..\Redeploy')).Path
$SptPath = (Resolve-Path $SptPath).Path
if ([bool]$CoreAssemblyPath -ne [bool]$CoreAssemblySha256) {
    throw 'CoreAssemblyPath and its independently verified SHA256 must be supplied together.'
}
if ($CoreAssemblyPath) {
    $CoreAssemblyPath = (Resolve-Path -LiteralPath $CoreAssemblyPath).Path
    if ($CoreAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        (Get-FileHash -LiteralPath $CoreAssemblyPath -Algorithm SHA256).Hash -ne $CoreAssemblySha256) {
        throw 'Supplied Core assembly does not match its verified release hash.'
    }
}
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
$assetRoot = 'BepInEx/plugins/maschine-ArenaMode'
$docsRoot = "$assetRoot/docs"


function Get-InstalledSnapshot {
    $files = @(
        'BepInEx/plugins/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
        'BepInEx/plugins/ArenaMode/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/ArenaMode/maschine-ArenaMode.BigBrainCompat.dll',
        'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll',
        'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll',
        'BepInEx/plugins/maschine-Redeploy.Core.dll',
        'BepInEx/config/com.maschine.ArenaMode.cfg'
    )
    # The Arena session gate lives in its own preloader patcher folder; the helper exe and the shipped default
    # whitelist are inside the plugin folder that is already enumerated.
    foreach ($folder in @('BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode', 'BepInEx/patchers/maschine-ArenaMode', 'SPT_Runtime/user/mods/ArenaMode')) {
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
        'ArenaMode.Client/ArenaAssetPaths.cs' = @(
            'static string FromPluginDirectory\(', 'const string DirectoryName = "maschine-ArenaMode"'
        )
        'tools/Write-ArenaAssets.ps1' = @('function Convert-AssetLinks', 'Core license differs from the shared project notice')
        'ArenaMode.Client/Audio/AnnouncerService.cs' = @('ArenaAssetPaths\.FromPluginDirectory\(')
        'ArenaMode.Client/Audio/HitmarkerAudioService.cs' = @('ArenaAssetPaths\.FromPluginDirectory\(')
        'ArenaMode.Client/Pickups/ArenaPickupAudio.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Pickups/ArenaPickupRuntime.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Pickups/ArenaPickupCapture.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Areas/ArenaAreaCapture.cs' = @('ArenaAssetPaths\.FromPluginFile\(')
        'ArenaMode.Client/Plugin.cs' = @(
            'BepInDependency\(ArenaBigBrainLibrary.BigBrainGuid,\s*BepInDependency\.DependencyFlags\.SoftDependency\)',
            'CoreVersionGate.Check\(', 'if \(_runtimeStarted\) StartRuntime\(\);',
            '\[MethodImpl\(MethodImplOptions.NoInlining\)\]\s*private void StartRuntime\(\)', 'ArenaBigBrainLibrary.Shutdown\('
        )
        'ArenaMode.Client/Compat/ArenaBigBrainLibrary.cs' = @('Assembly.LoadFrom\(path\)', 'entry.GetMethod\("Initialize"')
        'ArenaMode.BigBrainCompat/Library.cs' = @(
            'public static class Library', 'public static bool Initialize\(ManualLogSource logger\)',
            'PluginInfos\.TryGetValue\(BigBrainPluginGuid,', 'BigBrainRuntime\.Register\('
        )
        'ArenaMode.BigBrainCompat/BigBrainRuntime.cs' = @(
            '\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*internal static void Register\('
        )
    }
    foreach ($sessionFile in @('ArenaMode.Gate/ArenaMode.Gate.csproj', 'ArenaMode.Session/ArenaMode.Session.csproj', 'assets/arena-whitelist.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $sessionFile) -PathType Leaf)) {
            throw "Selected Arena revision predates package layout v6 (Arena session): missing $sessionFile. Select the whitelist-session revision or later."
        }
    }
    foreach ($entry in $seams.GetEnumerator()) {
        $path = Join-Path $SourceRoot $entry.Key
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Selected Arena revision predates package layout v3: missing $($entry.Key). Select the canonical-folder/shared-payload revision or later."
        }
        $body = Get-Content -LiteralPath $path -Raw
        foreach ($pattern in $entry.Value) {
            if ($body -notmatch $pattern) {
                throw "Selected Arena revision lacks a required package layout v3 seam in $($entry.Key). Select the canonical-folder/shared-payload revision or later; review this guard when refactoring."
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
$packageKind = if ($Release) { 'release' } else { 'local-test' }
$packageLabel = if ($Release) { 'RELEASE PACKAGE' } else { 'LOCAL TEST PACKAGE' }
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
  <Target Name="DeployArenaGate" />
  <Target Name="DeployArenaSessionHelper" />
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
    Invoke-Logged 'build-gate.log' (@('build', (Join-Path $source 'ArenaMode.Gate/ArenaMode.Gate.csproj'), '-c', 'Release', '--nologo') + $props)
    Invoke-Logged 'build-session-helper.log' (@('build', (Join-Path $source 'ArenaMode.Session/ArenaMode.Session.csproj'), '-c', 'Release', '--nologo') + $props)
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
    @{Source=(Join-Path $source 'ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll'); Dest="$assetRoot/maschine-ArenaMode.BigBrainCompat.dll"; Version=$version},
    # Arena session (docs/ARENA-SESSION-20261021.md section 9). Keep these last: the entries above are used by index.
    @{Source=(Join-Path $source 'ArenaMode.Gate/bin/Release/maschine-ArenaMode.Gate.dll'); Dest='BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll'; Version=$version},
    @{Source=(Join-Path $source 'ArenaMode.Session/bin/Release/maschine-ArenaMode.Session.exe'); Dest="$assetRoot/maschine-ArenaMode.Session.exe"; Version=$version}
)
# Keep the shared library byte-identical across independently built mod packages.
# A supplied published binary must match the frozen Core source revision/version,
# not just a caller-selected filename. The regular build still checks its API.
if ($CoreAssemblyPath) { $dlls[2].Source = $CoreAssemblyPath }
. (Join-Path $source 'tools/Assert-BigBrainLibrary.ps1')
Assert-BigBrainLibrary $dlls[0].Source $dlls[3].Source (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
foreach ($dll in $dlls) {
    $actual = [Reflection.AssemblyName]::GetAssemblyName($dll.Source).Version
    if ($actual -ne [version]($dll.Version + '.0')) { throw "Unexpected assembly version: $($dll.Dest): $actual" }
    if ($dll.Dest.EndsWith('maschine-Redeploy.Core.dll') -and
        [Reflection.AssemblyName]::GetAssemblyName($dll.Source).Name -ne 'maschine-Redeploy.Core') {
        throw 'The supplied shared library is not Redeploy.Core.'
    }
    $revision = if ($dll.Dest.EndsWith('maschine-Redeploy.Core.dll')) { $coreCommit } else { $arenaCommit }
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll.Source).ProductVersion
    if ($productVersion -ne ($dll.Version + '+' + $revision)) { throw "Unexpected embedded source revision: $($dll.Dest): $productVersion" }
    if ($CoreAssemblyPath -and $dll.Dest.EndsWith('maschine-Redeploy.Core.dll') -and
        (Get-FileHash -LiteralPath $dll.Source -Algorithm SHA256).Hash -ne $CoreAssemblySha256) {
        throw 'Supplied Core assembly changed during the build.'
    }
    Copy-Payload $dll.Source $dll.Dest
}
foreach ($name in @('kits.json', 'ladder.json')) {
    Copy-Payload (Join-Path $source "ArenaMode.Server/defaults/$name") "SPT_Runtime/user/mods/ArenaMode/config/$name"
}
# The same payload writer serves normal deployment and release snapshots.
& (Join-Path $source 'tools/Write-ArenaAssets.ps1') -SourceRoot $source `
    -Destination (Join-Path $payload $assetRoot) -SourceRevision $arenaCommit -CoreSourceRoot $coreSource
@"
ArenaMode $version - $packageLabel
Arena source: $arenaCommit
Redeploy.Core $coreVersion source: $coreCommit

Requires SPT 4.1.x (native tests have used 4.1.6).
1. Back up the profile and customized Arena config; close the game and server.
2. When updating an older installation, remove the old flat copies
   BepInEx/plugins/maschine-ArenaMode.Client.dll and
   BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll, if present.
   Keep exactly one copy of each Arena DLL anywhere under BepInEx/plugins.
   Migrate the old BepInEx/plugins/ArenaMode folder into
   BepInEx/plugins/maschine-ArenaMode before copying this ZIP. Preserve custom
   Pickups.json, AreaDrafts, PickupDrafts and other personal files; if both folders
   exist, compare conflicts rather than discarding either copy. Then remove only
   duplicate Arena DLLs from the old folder; do not delete unrelated files.
   This ZIP places both DLLs in BepInEx/plugins/maschine-ArenaMode.
   Then copy BepInEx and SPT_Runtime into the SPT installation root.
   Merge the folders; do not replace or delete the whole existing directories.
   The ZIP contains default kits.json and ladder.json. Preserve any intentional
   customizations before overwriting them. No user config.json, client settings,
   BepInEx settings, profiles or other mods are included.
3. Keep exactly one maschine-Redeploy.Core.dll in BepInEx/plugins (including
   its subfolders). Use the supplied version; never downgrade a newer Core.
4. The Arena BigBrain library is always included and has no separate plugin GUID.
   Arena starts it only when BigBrain is loaded; you do not need to remove its DLL.
   Update both Arena client DLLs together and remove old duplicate copies.
   BigBrain 1.5.0 is the tested
   baseline. Newer versions are allowed with a warning, not certified compatible;
   older versions are not enabled. Bot pickup pursuit, post-death pursuit and
   Molotov and Domination require its usable integration. BigBrain and its own
   prerequisites are NOT included.
5. Start server and game, select ARENA. Optional maps/PFT/SAIN/APBS/replays
   must be installed separately. Fika is unsupported.
6. Arena session (whitelist): the ZIP also carries the preloader gate
   BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll, the helper
   BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe and the shipped
   default list BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json (overwritten by
   updates). Arena runs only with whitelisted mods; with other mods installed the
   ARENA screen offers START ARENA SESSION, which restarts the game with only the
   whitelisted plugins and a private copy of the server. The default list allows the
   mods that were installed when it was written and denies ORBIT, Questing Bots,
   Adaptive Bot Culling, ISB Special Forces and Aishi. Your own additions and
   exclusions belong in BepInEx/config/com.maschine.ArenaMode.whitelist.json, which is
   never shipped or overwritten (an allow entry there lifts a default deny). The session
   needs the SPT_Runtime folder next to the game. All three files are built from the
   same source revision as the Arena DLLs.
7. To start without opening normal EFT, run the installed Session.exe and choose
   a profile. Its picker can create a desktop shortcut. A brief normal-server
   bootstrap before the private server is expected for profile preparation.
   Direct start manages a local SPT installation, not a remote server host.
8. Recognized versions differing from the reviewed whitelist are attempted in
   direct play and new private sessions. Version advisories do not exclude them;
   identity, deny/dependency and mandatory protection/API checks still apply.

Documentation, licenses and PACKAGE-MANIFEST.json are in
BepInEx/plugins/maschine-ArenaMode/docs, with asset notices under docs/licenses.
Retain this documentation with the package. Audio/artwork have separate terms; the entire package
is not MIT. Relative paths in the audio prose such as announcements/ and sounds/
refer to the parent maschine-ArenaMode folder; packaged announcer JSON paths are relative
to their JSON file and their recorded audio hashes are unchanged.
This package has build/offline checks only; packaging is not a new in-game test.
Nothing is installed or uploaded by the packaging script.
SHA256 and exact source revisions are recorded in PACKAGE-MANIFEST.json.
Full source and documentation: https://github.com/maschine34675/ArenaMode
"@ | Set-Content -LiteralPath (Join-Path $payload "$docsRoot/INSTALL.txt") -Encoding utf8

$manifest = [ordered]@{
    schemaVersion=2; layout='spt-root-folders-v6'; kind=$packageKind; arenaVersion=$version; arenaCommit=$arenaCommit
    redeployCoreVersion=$coreVersion; redeployCoreCommit=$coreCommit
    redeployCoreBinary= $(if ($CoreAssemblyPath) { 'verified-input' } else { 'source-build' })
    builtAtUtc=[DateTime]::UtcNow.ToString('o'); deployed=$false; uploaded=$false
    files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload "$docsRoot/PACKAGE-MANIFEST.json") -Encoding utf8
$zipName = if ($Release) { "ArenaMode-$version.zip" } else { "ArenaMode-$version-test-$($arenaCommit.Substring(0,7)).zip" }
$zip = Join-Path $run $zipName
# AGENTS.md rule 3 at packaging time: no PitFireTeam binary and no byte-identical reference payload in the package.
& pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Verify-PitFireTeamDeployment.ps1') -PayloadRoot $payload
if ($LASTEXITCODE -ne 0) { throw 'The package payload violates the PitFireTeam rule; see the output above.' }
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
& python (Join-Path $PSScriptRoot 'Verify-TestRelease.py') $zip
if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed.' }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output "PACKAGE_READY $zip"
Write-Output "SHA256 $hash"
