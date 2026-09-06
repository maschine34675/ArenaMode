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
dotnet run --project $supplyProject -c Release -- $supplyRoot $Database
if($LASTEXITCODE -ne 0){throw 'Supplies production recheck failed.'}
dotnet test (Join-Path $supplyRoot 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --filter 'FullyQualifiedName~KitSuppliesTests'
if($LASTEXITCODE -ne 0){throw 'Supplies actual server catalog/lifecycle tests failed.'}
Write-Output 'ARENA_KIT_SUPPLIES_PASSED'
