#Requires -Version 7.0
[CmdletBinding()]
param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/deploy-layout-check-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $run 'source'
New-Item -ItemType Directory -Path $source -Force | Out-Null
$revision = (& git -C $repo rev-parse HEAD).Trim()
$dll = 'maschine-ArenaMode.Client.dll'
$compat = 'maschine-ArenaMode.BigBrainCompat.dll'
foreach ($name in @($dll, $compat)) {
    $installed = @("maschine-ArenaMode/$name", "ArenaMode/$name", $name) |
        ForEach-Object { Join-Path $SptPath ('BepInEx/plugins/' + $_) } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $installed) { throw "Missing owned assembly for byte-copy fixture: $name" }
    Copy-Item -LiteralPath $installed -Destination (Join-Path $source $name)
    Set-Content -LiteralPath (Join-Path $source ([IO.Path]::ChangeExtension($name, '.pdb'))) -Value 'owned test symbols'
}
$checks = 0
function Check([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "DEPLOY_LAYOUT_FAILED: $Message" }
    $script:checks++
}
function Put([string]$Root, [string]$Relative, [string]$Value) {
    $path = Join-Path $Root $Relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, $Value)
}
function Snapshot([string]$Root) {
    $state = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName) {
        $state[[IO.Path]::GetRelativePath($Root, $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    return $state | ConvertTo-Json -Compress
}
function Deploy([string]$Root, [string]$Configuration = 'Release', [bool]$Disabled = $false, [bool]$MustFail = $false) {
    $arguments = @('msbuild', (Join-Path $repo 'ArenaMode.Client/ArenaMode.Client.csproj'), '-t:SPTClientDeploy', '-nologo',
        "-p:SPTPath=$Root", "-p:TargetPath=$(Join-Path $source $dll)", "-p:Configuration=$Configuration", "-p:SourceRevisionId=$revision")
    if ($Disabled) { $arguments += "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" }
    $log = Join-Path $run (([guid]::NewGuid().ToString('N')) + '.log')
    & dotnet @arguments *> $log
    $exit = $LASTEXITCODE
    if (($exit -ne 0) -ne $MustFail) { Get-Content -LiteralPath $log -Tail 25 | Out-Host; throw "Unexpected deploy result $exit; see $log" }
}

$fresh = Join-Path $run 'fresh'
New-Item -ItemType Directory -Path $fresh | Out-Null
Deploy $fresh
$target = Join-Path $fresh 'BepInEx/plugins/maschine-ArenaMode'
Check (Test-Path -LiteralPath (Join-Path $target $dll)) 'fresh target is canonical'
Check (Test-Path -LiteralPath (Join-Path $target 'docs/README.md')) 'normal target writes shared documents'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'Ui'))) 'fresh payload has no notice-only Ui folder'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'MapPreviews'))) 'preview provenance is grouped with licenses'
foreach ($relative in @('ui/NOTICE.md', 'audio/AUDIO-LICENSES.md', 'audio/HITMARKER-SOURCES.json', 'audio/PICKUP-SOURCES.json',
    'audio/ANNOUNCER-SOURCES.json', 'audio/ANNOUNCER-ADDITIONS-20260917.json', 'CC0-1.0.txt', 'CC-BY-3.0.txt', 'map-previews/ATTRIBUTION.md')) {
    Check (Test-Path -LiteralPath (Join-Path $target ('docs/licenses/' + $relative))) ('retained notice ' + $relative)
}
Check (@(Get-ChildItem -LiteralPath (Join-Path $target 'announcements') -File).Count -eq 41) 'fresh deployment contains 41 active announcements'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'announcements/reserve'))) 'reserve announcements remain source-only'
Check ((@(Get-ChildItem -LiteralPath (Join-Path $target 'sounds') -File).Name | Sort-Object) -join ',' -ceq 'collect.wav,headshot.wav,hit.wav,kill.wav') 'four effects share the sounds folder'
foreach ($relative in @('hitmarkers', 'pickups', 'docs/ANNOUNCER-PACK.md', 'docs/HUD-ICON-LICENSES.md', 'docs/REDEPLOY-CORE-LICENSE.txt', 'docs/licenses/ui/CC-BY-3.0.txt')) {
    Check (-not (Test-Path -LiteralPath (Join-Path $target $relative))) ('fresh payload excludes retired path ' + $relative)
}
Check (-not (Test-Path -LiteralPath (Join-Path $fresh ('BepInEx/plugins/' + $dll)))) 'fresh deploy never writes flat DLL'
# Arena session (docs/ARENA-SESSION-20261021.md section 9): the shipped default whitelist is a shared-writer asset.
$sourceWhitelist = Join-Path $repo 'assets/arena-whitelist.json'
Check ((Get-FileHash -LiteralPath (Join-Path $target 'arena-whitelist.json')).Hash -ceq (Get-FileHash -LiteralPath $sourceWhitelist).Hash) 'fresh deployment installs the shipped default whitelist unchanged'
Check (-not (Test-Path -LiteralPath (Join-Path $fresh 'BepInEx/config'))) 'client deployment never writes BepInEx/config (the user whitelist is not shipped)'
Put $fresh 'BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json' 'edited by hand'
Deploy $fresh
Check ((Get-FileHash -LiteralPath (Join-Path $target 'arena-whitelist.json')).Hash -ceq (Get-FileHash -LiteralPath $sourceWhitelist).Hash) 'the shipped default whitelist is overwritten by an update'

$old = Join-Path $run 'legacy'
Put $old 'BepInEx/plugins/ArenaMode/AreaDrafts/custom.json' 'area-user-data'
Put $old 'BepInEx/plugins/ArenaMode/PickupDrafts/custom.json' 'pickup-user-data'
Put $old 'BepInEx/plugins/ArenaMode/Pickups.json' 'custom-layout'
Put $old 'BepInEx/plugins/ArenaMode/sounds/custom.ogg' 'custom-audio'
Put $old 'BepInEx/plugins/ArenaMode/sounds/first_blood.ogg' 'old owned announcement'
Put $old 'BepInEx/plugins/ArenaMode/hitmarkers/hit.wav' 'old owned hitmarker'
Put $old 'BepInEx/plugins/ArenaMode/pickups/collect.wav' 'old owned pickup'
Put $old 'BepInEx/plugins/ArenaMode/AUDIO-LICENSES.md' 'old notice'
Put $old 'BepInEx/plugins/ArenaMode/docs/ANNOUNCER-SOURCES.json' 'old documented provenance'
Put $old 'BepInEx/plugins/ArenaMode/docs/ANNOUNCER-PACK.md' 'merged pack notes'
Put $old 'BepInEx/plugins/ArenaMode/docs/HUD-ICON-LICENSES.md' 'merged equipment credits'
Put $old 'BepInEx/plugins/ArenaMode/docs/REDEPLOY-CORE-LICENSE.txt' 'duplicate original license'
Put $old 'BepInEx/plugins/ArenaMode/docs/licenses/pickups/README.md' 'merged pickup notes'
Put $old 'BepInEx/plugins/ArenaMode/docs/licenses/pickups/CC0-1.0.txt' 'old duplicate CC0'
Put $old 'BepInEx/plugins/ArenaMode/Ui/NOTICE.md' 'old UI notice'
Put $old 'BepInEx/plugins/ArenaMode/MapPreviews/Source/Customs-Ground_Level.svg' 'old map provenance'
Put $old 'BepInEx/plugins/ArenaMode/hitmarkers/KENNEY-IMPACT-LICENSE.txt' 'obsolete notice'
Put $old 'BepInEx/plugins/ArenaMode/pickups/custom.txt' 'user pickup notes'
Put $old 'BepInEx/config/com.maschine.ArenaMode.cfg' 'user-settings'
Put $old 'BepInEx/config/com.maschine.ArenaMode.whitelist.json' 'user-whitelist'
Put $old 'BepInEx/plugins/maschine-Redeploy.Core.dll' 'untouched-shared-core'
Put $old 'BepInEx/plugins/Foreign.dll' 'untouched-foreign-mod'
foreach ($name in @($dll, $compat)) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $old ('BepInEx/plugins/' + $name))
    Copy-Item -LiteralPath (Join-Path $source ([IO.Path]::ChangeExtension($name, '.pdb'))) -Destination (Join-Path $old ('BepInEx/plugins/' + [IO.Path]::ChangeExtension($name, '.pdb')))
}
Deploy $old 'Debug'
$target = Join-Path $old 'BepInEx/plugins/maschine-ArenaMode'
foreach ($entry in @(@('AreaDrafts/custom.json','area-user-data'), @('PickupDrafts/custom.json','pickup-user-data'), @('Pickups.json','custom-layout'), @('sounds/custom.ogg','custom-audio'))) {
    Check ([IO.File]::ReadAllText((Join-Path $target $entry[0])) -ceq $entry[1]) ('preserved ' + $entry[0])
}
Check (-not (Test-Path -LiteralPath (Join-Path $old 'BepInEx/plugins/ArenaMode'))) 'legacy asset folder migrated'
foreach ($name in @($dll, $compat)) {
    Check (Test-Path -LiteralPath (Join-Path $target $name)) ('grouped ' + $name)
    Check (-not (Test-Path -LiteralPath (Join-Path $old ('BepInEx/plugins/' + $name)))) ('no flat duplicate ' + $name)
    Check (-not (Test-Path -LiteralPath (Join-Path $old ('BepInEx/plugins/' + [IO.Path]::ChangeExtension($name, '.pdb'))))) ('no flat symbols ' + $name)
}
Check (Test-Path -LiteralPath (Join-Path $target ([IO.Path]::ChangeExtension($dll, '.pdb')))) 'debug symbols grouped'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'AUDIO-LICENSES.md'))) 'old notice leaves asset root'
foreach ($relative in @('sounds/first_blood.ogg', 'hitmarkers/hit.wav', 'pickups/collect.wav',
    'docs/ANNOUNCER-SOURCES.json', 'docs/ANNOUNCER-PACK.md', 'docs/HUD-ICON-LICENSES.md', 'docs/REDEPLOY-CORE-LICENSE.txt',
    'docs/licenses/pickups/README.md', 'docs/licenses/pickups/CC0-1.0.txt')) {
    Check (-not (Test-Path -LiteralPath (Join-Path $target $relative))) ('known old asset path retired ' + $relative)
}
foreach ($relative in @('announcements/first_blood.ogg', 'sounds/collect.wav', 'sounds/hit.wav')) {
    Check ((Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash -ceq (Get-FileHash -LiteralPath (Join-Path $repo ('ArenaMode.Client/Assets/ArenaMode/' + $relative))).Hash) ('migrated runtime audio refreshed at ' + $relative)
}
Check (-not (Test-Path -LiteralPath (Join-Path $target 'Ui'))) 'empty retired UI folder removed'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'MapPreviews'))) 'empty retired preview tree removed'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'hitmarkers/KENNEY-IMPACT-LICENSE.txt'))) 'unused owned license archived'
Check ([IO.File]::ReadAllText((Join-Path $target 'pickups/custom.txt')) -ceq 'user pickup notes') 'unknown pickup files preserved'
Check ([IO.File]::ReadAllText((Join-Path $old 'BepInEx/config/com.maschine.ArenaMode.cfg')) -ceq 'user-settings') 'user config unchanged'
Check ([IO.File]::ReadAllText((Join-Path $old 'BepInEx/config/com.maschine.ArenaMode.whitelist.json')) -ceq 'user-whitelist') 'user whitelist unchanged'
Check ((Get-FileHash -LiteralPath (Join-Path $target 'arena-whitelist.json')).Hash -ceq (Get-FileHash -LiteralPath $sourceWhitelist).Hash) 'migrated installation receives the shipped default whitelist'
Check ([IO.File]::ReadAllText((Join-Path $old 'BepInEx/plugins/maschine-Redeploy.Core.dll')) -ceq 'untouched-shared-core') 'shared Core unchanged by client deploy'
Check ([IO.File]::ReadAllText((Join-Path $old 'BepInEx/plugins/Foreign.dll')) -ceq 'untouched-foreign-mod') 'other mod unchanged'
Check (@(Get-ChildItem -LiteralPath (Join-Path $old 'BepInEx/backups/maschine-ArenaMode-layout') -File -Recurse).Count -ge 4) 'old flat files remain recoverable outside plugin scan'
$manifest = Get-Content -LiteralPath (Join-Path $target 'docs/licenses/audio/ANNOUNCER-SOURCES.json') -Raw | ConvertFrom-Json
Check ($manifest.files[0].file.StartsWith('../../../announcements/')) 'deployed provenance uses same relative paths as ZIP'
Deploy $old
Check (-not (Test-Path -LiteralPath (Join-Path $target ([IO.Path]::ChangeExtension($dll, '.pdb'))))) 'Release retires stale client debug symbols'
$before = Snapshot $old
Deploy $old
Check ((Snapshot $old) -ceq $before) 'repeated Release deploy does not create redundant backups or change files'
Deploy $old 'Release' $true
Check ((Snapshot $old) -ceq $before) 'NoDeploy override suppresses DLL, migration and assets'

$collision = Join-Path $run 'collision'
Put $collision 'BepInEx/plugins/ArenaMode/Pickups.json' 'old custom'
Put $collision 'BepInEx/plugins/maschine-ArenaMode/Pickups.json' 'new custom'
$before = Snapshot $collision
Deploy $collision 'Release' $false $true
Check ((Snapshot $collision) -ceq $before) 'conflicting user data aborts before any mutation'

$locked = Join-Path $run 'locked'
Put $locked 'BepInEx/plugins/ArenaMode/AreaDrafts/keep.json' 'keep'
$lockedDll = Join-Path $locked ('BepInEx/plugins/' + $dll)
Copy-Item -LiteralPath (Join-Path $source $dll) -Destination $lockedDll
$before = Snapshot $locked
$hold = [IO.File]::Open($lockedDll, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Deploy $locked 'Release' $false $true } finally { $hold.Dispose() }
Check ((Snapshot $locked) -ceq $before) 'locked old DLL blocks before folder migration'

$merge = Join-Path $run 'merge'
Put $merge 'BepInEx/plugins/ArenaMode/AreaDrafts/old.json' 'old draft'
Put $merge 'BepInEx/plugins/maschine-ArenaMode/PickupDrafts/new.json' 'new draft'
Put $merge 'BepInEx/plugins/maschine-ArenaMode/Ui/custom.txt' 'custom UI note'
Put $merge 'BepInEx/plugins/maschine-ArenaMode/MapPreviews/custom.png' 'custom map'
Deploy $merge
Check ([IO.File]::ReadAllText((Join-Path $merge 'BepInEx/plugins/maschine-ArenaMode/AreaDrafts/old.json')) -ceq 'old draft') 'coexisting layout retains legacy-only draft'
Check ([IO.File]::ReadAllText((Join-Path $merge 'BepInEx/plugins/maschine-ArenaMode/PickupDrafts/new.json')) -ceq 'new draft') 'coexisting layout retains canonical-only draft'
Check (-not (Test-Path -LiteralPath (Join-Path $merge 'BepInEx/plugins/ArenaMode'))) 'merged old folder is archived outside plugins'
Check ([IO.File]::ReadAllText((Join-Path $merge 'BepInEx/plugins/maschine-ArenaMode/Ui/custom.txt')) -ceq 'custom UI note') 'custom UI data retained'
Check ([IO.File]::ReadAllText((Join-Path $merge 'BepInEx/plugins/maschine-ArenaMode/MapPreviews/custom.png')) -ceq 'custom map') 'custom map data retained'

$blocked = Join-Path $run 'blocked-notice'
Put $blocked 'BepInEx/plugins/maschine-ArenaMode/docs/licenses/ui/NOTICE.md/keep.txt' 'do not touch'
$before = Snapshot $blocked
Deploy $blocked 'Release' $false $true
Check ((Snapshot $blocked) -ceq $before) 'notice destination collision aborts before DLL or asset writes'
foreach ($case in @(@('legacy-parent', 'docs/licenses'), @('legacy-notice-directory', 'Ui/NOTICE.md/keep.txt'),
    @('legacy-retired-directory', 'hitmarkers/KENNEY-IMPACT-LICENSE.txt/keep.txt'),
    @('legacy-announcement-target', 'announcements/first_blood.ogg/keep.txt'),
    @('legacy-effect-target', 'sounds/collect.wav/keep.txt'),
    @('legacy-effect-source', 'hitmarkers/hit.wav/keep.txt'),
    @('legacy-audio-notice-target', 'docs/licenses/audio/ANNOUNCER-SOURCES.json/keep.txt'),
    @('legacy-shared-license-target', 'docs/licenses/CC0-1.0.txt/keep.txt'),
    @('legacy-merged-notice-directory', 'docs/ANNOUNCER-PACK.md/keep.txt'))) {
    $root = Join-Path $run $case[0]
    Put $root ('BepInEx/plugins/ArenaMode/' + $case[1]) 'user file must survive'
    $before = Snapshot $root
    Deploy $root 'Release' $false $true
    Check ((Snapshot $root) -ceq $before) ('legacy collision fails before any mutation: ' + $case[0])
}

$linked = Join-Path $run 'linked'
$external = Join-Path $run 'link-destination'
Put $external 'keep.txt' 'untouched link destination'
Put $linked 'BepInEx/plugins/ArenaMode/keep.txt' 'legacy data'
$junction = Join-Path $linked 'BepInEx/plugins/maschine-ArenaMode'
New-Item -ItemType Junction -Path $junction -Target $external | Out-Null
Deploy $linked 'Release' $false $true
Check ([IO.File]::ReadAllText((Join-Path $external 'keep.txt')) -ceq 'untouched link destination') 'junction destination untouched'
Check (@(Get-ChildItem -LiteralPath $external -File).Count -eq 1) 'junction never receives DLL or payload'
Check (Test-Path -LiteralPath (Join-Path $linked 'BepInEx/plugins/ArenaMode/keep.txt')) 'junction rejection leaves legacy folder unmoved'
# ---- Arena session files: gate patcher and helper exe (tools/Deploy-ArenaSession.ps1) -------------------
# Built here without deployment (the no-deploy override and an absent server mods folder, like every offline
# build of this repository), then installed into scratch roots by the real MSBuild targets.
$noDeployTargets = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
$sessionDeployScript = Join-Path $repo 'tools/Deploy-ArenaSession.ps1'
function Build-Session([string]$Project, [string]$Configuration) {
    $log = Join-Path $run (([guid]::NewGuid().ToString('N')) + '.log')
    & dotnet build (Join-Path $repo $Project) -c $Configuration -nologo "-p:CustomAfterMicrosoftCommonTargets=$noDeployTargets" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 25 | Out-Host; throw "Session project build failed: $Project; see $log" }
}
$builds = [ordered]@{}
foreach ($configuration in @('Release', 'Debug')) {
    Build-Session 'ArenaMode.Gate/ArenaMode.Gate.csproj' $configuration
    Build-Session 'ArenaMode.Session/ArenaMode.Session.csproj' $configuration
    $builds[$configuration] = [ordered]@{
        Gate = Join-Path $repo "ArenaMode.Gate/bin/$configuration/maschine-ArenaMode.Gate.dll"
        Helper = Join-Path $repo "ArenaMode.Session/bin/$configuration/maschine-ArenaMode.Session.exe"
    }
    foreach ($built in $builds[$configuration].Values) { Check (Test-Path -LiteralPath $built) "built $built" }
}
$sessionParts = @{
    Gate = @{ Project = 'ArenaMode.Gate/ArenaMode.Gate.csproj'; Target = 'DeployArenaGate'; Folder = 'BepInEx/patchers/maschine-ArenaMode'; Name = 'maschine-ArenaMode.Gate.dll' }
    Helper = @{ Project = 'ArenaMode.Session/ArenaMode.Session.csproj'; Target = 'DeployArenaSessionHelper'; Folder = 'BepInEx/plugins/maschine-ArenaMode'; Name = 'maschine-ArenaMode.Session.exe' }
}
function Deploy-Session([string]$Part, [string]$Root, [string]$Configuration = 'Release', [string[]]$Extra = @(), [bool]$MustFail = $false) {
    $spec = $sessionParts[$Part]
    $arguments = @('msbuild', (Join-Path $repo $spec.Project), "-t:$($spec.Target)", '-nologo',
        "-p:SPTPath=$Root", "-p:TargetPath=$($builds[$Configuration][$Part])", "-p:Configuration=$Configuration") + $Extra
    $log = Join-Path $run (([guid]::NewGuid().ToString('N')) + '.log')
    & dotnet @arguments *> $log
    $exit = $LASTEXITCODE
    if (($exit -ne 0) -ne $MustFail) { Get-Content -LiteralPath $log -Tail 25 | Out-Host; throw "Unexpected session deploy result $exit ($Part); see $log" }
}
function Installed([string]$Root, [string]$Part) { Join-Path $Root ($sessionParts[$Part].Folder + '/' + $sessionParts[$Part].Name) }
function Same([string]$Left, [string]$Right) { (Test-Path -LiteralPath $Left) -and ((Get-FileHash -LiteralPath $Left).Hash -ceq (Get-FileHash -LiteralPath $Right).Hash) }

$session = Join-Path $run 'session'
Put $session 'BepInEx/plugins/Foreign.dll' 'untouched-foreign-mod'
Put $session 'BepInEx/patchers/FixPluginTypesSerialization.dll' 'untouched-foreign-patcher'
Put $session 'BepInEx/config/com.maschine.ArenaMode.whitelist.json' 'user-whitelist'
$untouched = Snapshot $session | ConvertFrom-Json -AsHashtable
Deploy-Session 'Gate' $session
Deploy-Session 'Helper' $session
Check (Same (Installed $session 'Gate') $builds['Release'].Gate) 'gate lands in BepInEx/patchers/maschine-ArenaMode'
Check (Same (Installed $session 'Helper') $builds['Release'].Helper) 'helper lands beside the client DLL in BepInEx/plugins/maschine-ArenaMode'
$state = Snapshot $session
$installedSet = $state | ConvertFrom-Json -AsHashtable
Check ($installedSet.Count -eq $untouched.Count + 2) 'session deployment writes exactly two new files (no pdb, no pending file, nothing else)'
foreach ($key in $untouched.Keys) { Check ($installedSet[$key] -ceq $untouched[$key]) "session deploy leaves $key alone" }
Check ($installedSet['BepInEx\patchers\maschine-ArenaMode\maschine-ArenaMode.Gate.dll'] -ceq (Get-FileHash -LiteralPath $builds['Release'].Gate).Hash) 'gate snapshot entry matches the build'
Check ($installedSet['BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Session.exe'] -ceq (Get-FileHash -LiteralPath $builds['Release'].Helper).Hash) 'helper snapshot entry matches the build'
# Copy-Item keeps the source's write time, so a rewrite shows in the creation time of the new file.
$stamp = (Get-Item -LiteralPath (Installed $session 'Gate')).CreationTimeUtc
Start-Sleep -Milliseconds 1200
Deploy-Session 'Gate' $session
Deploy-Session 'Helper' $session
Check ((Snapshot $session) -ceq $state) 'repeated session deployment changes nothing'
Check ((Get-Item -LiteralPath (Installed $session 'Gate')).CreationTimeUtc -eq $stamp) 'identical gate is not rewritten (no file churn in synchronised installations)'

foreach ($part in @('Gate', 'Helper')) {
    [IO.File]::WriteAllText((Installed $session $part), 'stale build')
    $stale = Snapshot $session
    Deploy-Session $part $session 'Release' @("-p:CustomAfterMicrosoftCommonTargets=$noDeployTargets")
    Check ((Snapshot $session) -ceq $stale) "NoDeploy override suppresses the $part deployment"
    Deploy-Session $part $session 'Release' @('-p:SPTModType=NoDeploy')
    Check ((Snapshot $session) -ceq $stale) "-p:SPTModType=NoDeploy suppresses the $part deployment"
    Deploy-Session $part $session
    Check (Same (Installed $session $part) $builds['Release'][$part]) "$part replaces a different installed file"
}
Check ((Snapshot $session) -ceq $state) 'replacement restores the exact installed set'

# Debug symbols travel with a Debug build only; a Release build archives a stale pair outside plugin discovery.
Deploy-Session 'Gate' $session 'Debug'
Deploy-Session 'Helper' $session 'Debug'
foreach ($part in @('Gate', 'Helper')) {
    Check (Same (Installed $session $part) $builds['Debug'][$part]) "Debug $part deployed"
    Check (Test-Path -LiteralPath ([IO.Path]::ChangeExtension((Installed $session $part), '.pdb'))) "Debug $part has its symbols"
}
Deploy-Session 'Gate' $session
Deploy-Session 'Helper' $session
foreach ($part in @('Gate', 'Helper')) {
    Check (Same (Installed $session $part) $builds['Release'][$part]) "Release $part replaces the Debug build"
    Check (-not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension((Installed $session $part), '.pdb')))) "Release retires stale $part symbols"
}
Check (@(Get-ChildItem -LiteralPath (Join-Path $session 'BepInEx/backups/maschine-ArenaMode-layout') -File -Recurse -Filter '*.pdb').Count -eq 2) 'retired symbols remain recoverable outside plugin and patcher discovery'
Check (@(Get-ChildItem -LiteralPath (Join-Path $session 'BepInEx') -File -Recurse -Filter '*.pending').Count -eq 0) 'no staged copy is left behind'

# Safety rules shared with the client deployer.
$renamedGate = Join-Path $run 'renamed.dll'
Copy-Item -LiteralPath $builds['Release'].Gate -Destination $renamedGate
foreach ($case in @(@('a client assembly', (Join-Path $source $dll)), @('a renamed gate', $renamedGate))) {
    $guardRoot = Join-Path $run ('session-guard-' + [guid]::NewGuid().ToString('N'))
    Put $guardRoot 'BepInEx/keep.txt' 'keep'
    $before = Snapshot $guardRoot
    & pwsh -NoLogo -NoProfile -File $sessionDeployScript -AssemblyPath $case[1] -SptPath $guardRoot *> $null
    Check ($LASTEXITCODE -ne 0) "session deployer refuses $($case[0])"
    Check ((Snapshot $guardRoot) -ceq $before) "refusing $($case[0]) writes nothing"
}
$lockedSession = Join-Path $run 'session-locked'
Put $lockedSession 'BepInEx/keep.txt' 'keep'
Deploy-Session 'Helper' $lockedSession
$before = Snapshot $lockedSession
$hold = [IO.File]::Open((Installed $lockedSession 'Helper'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Deploy-Session 'Helper' $lockedSession 'Release' @() $true } finally { $hold.Dispose() }
Check ((Snapshot $lockedSession) -ceq $before) 'a running (locked) helper blocks the deployment before any write'
$before = Snapshot $session
$holdGate = [IO.File]::Open((Installed $session 'Gate'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Deploy-Session 'Gate' $session 'Release' @() $true } finally { $holdGate.Dispose() }
Check ((Snapshot $session) -ceq $before) 'a loaded (locked) gate blocks the deployment before any write'

# A link at the target folder itself (the write would land in the destination) and a link further up.
$linkIndex = 0
foreach ($case in @(@('Gate', 'BepInEx/patchers'), @('Gate', 'BepInEx/patchers/maschine-ArenaMode'), @('Helper', 'BepInEx/plugins/maschine-ArenaMode'))) {
    $linkIndex++
    $linkedRoot = Join-Path $run ("session-linked-$linkIndex")
    $linkTarget = Join-Path $run ("session-link-destination-$linkIndex")
    Put $linkTarget 'keep.txt' 'untouched link destination'
    Put $linkedRoot 'BepInEx/plugins/keep.txt' 'keep'
    New-Item -ItemType Directory -Path (Split-Path (Join-Path $linkedRoot $case[1])) -Force | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $linkedRoot $case[1]) -Target $linkTarget | Out-Null
    Deploy-Session $case[0] $linkedRoot 'Release' @() $true
    Check (@(Get-ChildItem -LiteralPath $linkTarget -Recurse -File).Count -eq 1) "junction at $($case[1]) never receives the $($case[0])"
}
$blockedSession = Join-Path $run 'session-blocked'
Put $blockedSession 'BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll/keep.txt' 'do not touch'
$before = Snapshot $blockedSession
Deploy-Session 'Gate' $blockedSession 'Release' @() $true
Check ((Snapshot $blockedSession) -ceq $before) 'a directory in place of the gate aborts before any write'

Write-Output "DEPLOY_LAYOUT_PASSED ($checks checks; scratch $run; no real installation writes)"
