param([string]$Database='D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json')
$ErrorActionPreference='Stop'
$polishRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$polishProject=Join-Path $PSScriptRoot 'KitPolishChecks.csproj'
$polishNative=Join-Path $polishRoot '../_decompiled'
if ((Get-Content (Join-Path $polishNative 'EFT.InventoryLogic/ArmorComponent.cs') -Raw) -notmatch 'Template.ArmorPlateColliders & armorSlot.ArmorPlateColliderMask') { throw 'Native armor intersection contract drifted.' }
if ((Get-Content (Join-Path $polishNative 'EFT.InventoryLogic/SlotSerializer.cs') -Raw) -notmatch '64479fdf9731c8fadc0642c1') { throw 'Native ArmorSlot discriminator drifted.' }
dotnet run --project $polishProject -c Release -- $polishRoot $Database
if($LASTEXITCODE -ne 0){throw 'Kit polish production checks failed.'}
$polishSource=[IO.File]::ReadAllText((Join-Path $polishRoot 'ArenaMode.Client/Kits/KitSummary.cs'))
$polishNeedle='var armour = FrontBodyClass(kit);'
if(-not $polishSource.Contains($polishNeedle)){throw 'Armor negative-control anchor drifted.'}
$polishWork=Join-Path $PSScriptRoot '.work'
[IO.Directory]::CreateDirectory($polishWork)|Out-Null
$polishMutant=Join-Path $polishWork 'KitSummary.mutant.cs'
[IO.File]::WriteAllText($polishMutant,$polishSource.Replace($polishNeedle,'var armour = BestClass(kit, "ArmorVest", "TacticalVest");'))
$polishOutput=& dotnet run --project $polishProject -c Release "-p:SummarySource=$polishMutant" -- $polishRoot $Database 2>&1
if($LASTEXITCODE -eq 0 -or ($polishOutput -join "`n") -notmatch 'ASSERT: actual buy summary uses front class'){throw "Armor negative control failed unexpectedly: $polishOutput"}
dotnet run --project $polishProject -c Release -- $polishRoot $Database
if($LASTEXITCODE -ne 0){throw 'Production recheck failed.'}
Write-Output 'ARENA_KIT_POLISH_PASSED'
