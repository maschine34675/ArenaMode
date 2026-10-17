#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $repo 'tools/AnnouncerDelivery/Inventory.ps1')
$stage = Join-Path $repo ('artifacts/deployment-helper-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
$script:checks = 0
function Require([bool]$Value, [string]$Message) {
    if (!$Value) { throw "ASSERT: $Message" }
    $script:checks++
}
function Reject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Require $rejected $Message
}
$empty = Read-ArenaInstalledSnapshot $stage
Require ($empty.Count -eq 12) 'two assemblies, DLL/PDB, three layouts are observed'
Require (@($empty.Values | Where-Object { $_ -ne '<absent>' }).Count -eq 0) 'a fresh installation permits all Arena copies to be absent'
Assert-ArenaInstalledSnapshotUnchanged $empty $stage
Require $true 'unchanged absent installation passes'
$index = 0
foreach ($relative in @('BepInEx/plugins', 'BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode')) {
    foreach ($name in @('maschine-ArenaMode.Client.dll', 'maschine-ArenaMode.Client.pdb', 'maschine-ArenaMode.BigBrainCompat.dll', 'maschine-ArenaMode.BigBrainCompat.pdb')) {
        $root = Join-Path $stage ('case-' + $index++)
        $before = Read-ArenaInstalledSnapshot $root
        $file = Join-Path $root ($relative + '/' + $name)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
        [IO.File]::WriteAllText($file, 'initial')
        Reject { Assert-ArenaInstalledSnapshotUnchanged $before $root } "new file rejected: $relative/$name"
        $before = Read-ArenaInstalledSnapshot $root
        Assert-ArenaInstalledSnapshotUnchanged $before $root
        Require $true "existing unmodified file passes: $relative/$name"
        [IO.File]::WriteAllText($file, 'changed')
        Reject { Assert-ArenaInstalledSnapshotUnchanged $before $root } "changed file rejected: $relative/$name"
    }
}
foreach ($relative in @('BepInEx/plugins/maschine-Redeploy.Core.dll', 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll', 'source.cs')) {
    Reject { Get-ArenaObservedFileHash -LiteralPath (Join-Path $stage $relative) } "missing required non-Arena file still rejected: $relative"
}
$legacy = Join-Path $stage 'BepInEx/plugins/ArenaMode'
$canonical = Join-Path $stage 'BepInEx/plugins/maschine-ArenaMode'
$unrelated = Join-Path $stage 'BepInEx/plugins/foreign.dll'
$flatClient = Join-Path $stage 'BepInEx/plugins/maschine-ArenaMode.Client.dll'
$inventory = @{
    $flatClient = 'client';
    (Join-Path $legacy 'sounds/first_blood.ogg') = 'sound';
    (Join-Path $legacy 'AreaDrafts/user.json') = 'survey';
    (Join-Path $legacy 'PickupDrafts/user.json') = 'pickup-draft';
    (Join-Path $legacy 'Pickups.json') = 'custom-layout';
    (Join-Path $legacy 'ANNOUNCER-SOURCES.json') = 'provenance';
    $unrelated = 'foreign'
}
$expected = ConvertTo-AnnouncerCanonicalInventory $inventory $stage
Require ($expected[(Join-Path $canonical 'maschine-ArenaMode.Client.dll')] -eq 'client') 'flat DLL normalizes to canonical folder'
Require ($expected[(Join-Path $canonical 'docs/ANNOUNCER-SOURCES.json')] -eq 'provenance') 'legacy root provenance normalizes to docs'
foreach ($relative in @('sounds/first_blood.ogg', 'AreaDrafts/user.json', 'PickupDrafts/user.json', 'Pickups.json')) {
    Require ($expected[(Join-Path $canonical $relative)] -eq $inventory[(Join-Path $legacy $relative)]) "migration preserves $relative"
}
Require ($expected[$unrelated] -eq 'foreign') 'foreign plugin location and bytes preserved'
Assert-AnnouncerInventory $expected $expected.Clone()
Require $true 'matching migrated inventory passes'
$changed = $expected.Clone(); $changed[$unrelated] = 'tampered'
Reject { Assert-AnnouncerInventory $expected $changed } 'foreign plugin mutation rejected'
$changed = $expected.Clone(); $changed.Remove((Join-Path $canonical 'AreaDrafts/user.json'))
Reject { Assert-AnnouncerInventory $expected $changed } 'lost user survey rejected'
$changed = $expected.Clone(); $changed[$flatClient] = 'stale'
Reject { Assert-AnnouncerInventory $expected $changed } 'duplicate flat client rejected'
$duplicates = @{
    $flatClient = 'flat-old';
    (Join-Path $legacy 'maschine-ArenaMode.Client.dll') = 'grouped-old';
    (Join-Path $canonical 'maschine-ArenaMode.Client.dll') = 'canonical';
    (Join-Path $legacy 'ANNOUNCER-SOURCES.json') = 'legacy-notice';
    (Join-Path $canonical 'ANNOUNCER-SOURCES.json') = 'canonical-root-notice';
    (Join-Path $canonical 'docs/ANNOUNCER-SOURCES.json') = 'canonical-docs-notice'
}
$normalized = ConvertTo-AnnouncerCanonicalInventory $duplicates $stage
Require ($normalized[(Join-Path $canonical 'maschine-ArenaMode.Client.dll')] -eq 'canonical') 'canonical DLL wins over retired duplicates regardless of enumeration order'
Require ($normalized[(Join-Path $canonical 'docs/ANNOUNCER-SOURCES.json')] -eq 'canonical-docs-notice') 'canonical docs win over retired root notices'
$duplicates.Remove((Join-Path $canonical 'maschine-ArenaMode.Client.dll'))
$normalized = ConvertTo-AnnouncerCanonicalInventory $duplicates $stage
Require ($normalized[(Join-Path $canonical 'maschine-ArenaMode.Client.dll')] -eq 'grouped-old') 'migrated grouped DLL wins over retired flat DLL'
Write-Output "DEPLOYMENT_HELPER_CHECKS_PASSED checks=$script:checks evidence=$stage; no build or deployment"
