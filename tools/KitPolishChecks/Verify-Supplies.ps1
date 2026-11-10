param([string]$Database='D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json')
$ErrorActionPreference='Stop'
$supplyRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$supplyProject=Join-Path $PSScriptRoot 'Supplies/SuppliesChecks.csproj'
$supplyNative=Join-Path $supplyRoot '../_decompiled'
$supplyEquipment=[IO.File]::ReadAllText((Join-Path $supplyNative 'EFT.InventoryLogic/InventoryEquipment.cs'))
$supplyQuick=$supplyEquipment.Substring($supplyEquipment.IndexOf('public IReadOnlyList<Slot> GrenadeThrowingSlots'))
$supplyQuick=$supplyQuick.Substring(0,$supplyQuick.IndexOf('public Slot GetSlot'))
if($supplyQuick -notmatch 'EquipmentSlot.TacticalVest' -or $supplyQuick -notmatch 'EquipmentSlot.Pockets' -or $supplyQuick -match 'EquipmentSlot.Backpack'){throw 'Native quick-throw container contract drifted.'}
$supplyGrid=[IO.File]::ReadAllText((Join-Path $supplyNative 'EFT.InventoryLogic/Grid.cs'))
if(-not $supplyGrid.Contains('AddInternal(item, FindFreeSpace(item), simulate: false, ignoreRestrictions: false)') -or -not $supplyGrid.Contains('Filters.CheckItemFilter(item)')){throw 'Native AddAnywhere/filter contract drifted.'}
dotnet run --project $supplyProject -c Release -- $supplyRoot $Database
if($LASTEXITCODE -ne 0){throw 'Supplies production-linked checks failed.'}
$supplySource=[IO.File]::ReadAllText((Join-Path $supplyRoot 'ArenaMode.Client/Kits/KitBuilder.cs'))
$supplyWork=Join-Path $PSScriptRoot 'Supplies/.work'
[IO.Directory]::CreateDirectory($supplyWork)|Out-Null
$supplyNeedle='entry.OptionalQuickUseOnly ? grenadeContainers : containers'
if(-not $supplySource.Contains($supplyNeedle)){throw 'Supplies negative-control anchor drifted.'}
$supplyMutant=Join-Path $supplyWork 'KitBuilder.mutant.cs'
[IO.File]::WriteAllText($supplyMutant,$supplySource.Replace($supplyNeedle,'containers'))
$supplyOutput=& dotnet run --project $supplyProject -c Release "-p:BuilderSource=$supplyMutant" -- $supplyRoot $Database 2>&1
if($LASTEXITCODE -eq 0 -or ($supplyOutput -join "`n") -notmatch 'ASSERT: optional supplies never spill to backpack'){throw "Supplies negative control failed unexpectedly: $supplyOutput"}

$supplyGetterNeedle='new[] { equipment.GetSlot(EquipmentSlot.TacticalVest), equipment.GetSlot(EquipmentSlot.Pockets) }'
if(-not $supplySource.Contains($supplyGetterNeedle)){throw 'Supplies patched-getter negative-control anchor drifted.'}
[IO.File]::WriteAllText($supplyMutant,$supplySource.Replace($supplyGetterNeedle,'equipment.GrenadeThrowingSlots'))
$supplyOutput=& dotnet run --project $supplyProject -c Release "-p:BuilderSource=$supplyMutant" -- $supplyRoot $Database 2>&1
if($LASTEXITCODE -eq 0 -or ($supplyOutput -join "`n") -notmatch 'ASSERT: patched quick-throw list cannot redirect grenades to backpack'){throw "Supplies patched-getter negative control failed unexpectedly: $supplyOutput"}

# Reserve stacks (2026-11-10): one stack per spare magazine, the fill before this date, left every box
# that holds more than one stack of its round short (an RPD kit's spare read 60/100 natively); an uncut
# loose share put three stack-1 40 mm grenades into one item. Each anchor must exist exactly once.
foreach($supplyFault in @(
    @{Name='single-spare-stack';Find='room -= ammo.StackObjectsCount;';Replace='break;';Failure='spare magazines are loaded to capacity'},
    @{Name='uncut-loose-share';Find='ammo.StackObjectsCount = Math.Min(rounds, perStack);';Replace='ammo.StackObjectsCount = rounds;';Failure='loose reserve stacks never exceed the round''s stack size'}
)){
    if($supplySource.Split(@($supplyFault.Find),[StringSplitOptions]::None).Length -ne 2){throw "Supplies $($supplyFault.Name) anchor drifted or is ambiguous."}
    [IO.File]::WriteAllText($supplyMutant,$supplySource.Replace($supplyFault.Find,$supplyFault.Replace))
    $supplyOutput=& dotnet run --project $supplyProject -c Release "-p:BuilderSource=$supplyMutant" -- $supplyRoot $Database 2>&1
    if($LASTEXITCODE -eq 0 -or ($supplyOutput -join "`n") -notmatch [regex]::Escape('ASSERT: '+$supplyFault.Failure)){throw "Supplies $($supplyFault.Name) negative control failed unexpectedly: $supplyOutput"}
    Write-Output "Rejected $($supplyFault.Name) at $($supplyFault.Failure)"
}
dotnet run --project $supplyProject -c Release -- $supplyRoot $Database
if($LASTEXITCODE -ne 0){throw 'Supplies production recheck failed.'}
# The test project builds the server, whose deploy target writes the live user/mods unless the no-deploy
# targets and a sentinel that must not exist are passed (review 2026-10-23: this leaf deployed on every run).
dotnet test (Join-Path $supplyRoot 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --filter 'FullyQualifiedName~KitSuppliesTests' `
    "-p:CustomAfterMicrosoftCommonTargets=$supplyRoot/tools/AirPitChecks/NoDeploy.targets" "-p:SPTUserMods=$supplyRoot/artifacts/kit-supplies-must-not-deploy"
if($LASTEXITCODE -ne 0){throw 'Supplies actual server catalog/lifecycle tests failed.'}
Write-Output 'ARENA_KIT_SUPPLIES_PASSED'
