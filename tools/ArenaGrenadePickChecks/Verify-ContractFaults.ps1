#requires -Version 7.0
# Proves Verify-InstalledContracts.ps1 is not vacuous: each deliberately wrong
# expectation, applied to a temporary copy, must make it fail; the unchanged
# script must then pass. No repository file is replaced.
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1'
$source = [IO.File]::ReadAllText($script)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaGrenadePickContractFaults-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

$faults = [ordered]@{
    'wrong-callee'          = @('Require-Call $removed "$view::SetNewTopPriorityGrenade"', 'Require-Call $removed "$view::NoSuchMethod"')
    'wrong-parameter-name'  = @("-ne 'obj') { throw 'OnItemRemoved", "-ne 'args') { throw 'OnItemRemoved")
    'wrong-field-access'    = @('Require-Field $quick ''InventoryController'' "$inv.InventoryController" $false',
                                'Require-Field $quick ''InventoryController'' "$inv.InventoryController" $true')
    'wrong-override-set'    = @("-ne 'EFT.HideoutPlayerInputTranslator')", "-ne 'EFT.Nothing')")
    'wrong-throwing-slots'  = @("@('TacticalVest', 'Pockets') 'InventoryEquipment.GrenadeThrowingSlots'",
                                "@('Pockets', 'TacticalVest') 'InventoryEquipment.GrenadeThrowingSlots'")
    'wrong-fast-access'     = @("@('Pockets', 'TacticalVest') 'Inventory.FastAccessSlots'",
                                "@('Pockets', 'TacticalVest', 'Backpack') 'Inventory.FastAccessSlots'")
    'wrong-null-refusal'    = @("(`$head[3] -ne 'Ldc_I4_0')", "(`$head[3] -ne 'Ldc_I4_1')")
    'wrong-mod-slots'       = @("@('Backpack', 'TacticalVest', 'Pockets', 'SecuredContainer', 'ArmBand')", "@('TacticalVest', 'Pockets')")
}

foreach ($name in $faults.Keys) {
    $from, $to = $faults[$name]
    $count = ([regex]::Matches($source, [regex]::Escape($from))).Count
    if ($count -ne 1) { throw "Fault '$name' matches $count times; refusing an ambiguous fault." }
    $copy = Join-Path $scratch ($name + '.ps1')
    [IO.File]::WriteAllText($copy, $source.Replace($from, $to))
    $output = & pwsh -NoProfile -File $copy -SptPath $SptPath 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or $output -match 'ARENA_GRENADE_PICK_INSTALLED_CONTRACTS_PASSED') {
        throw "Contract fault '$name' was not rejected. Evidence: $copy"
    }
    Write-Output "Contract fault rejected: $name"
}

& pwsh -NoProfile -File $script -SptPath $SptPath
if ($LASTEXITCODE -ne 0) { throw 'The unchanged installed-contract check failed after the fault run.' }
Write-Output "ARENA_GRENADE_PICK_CONTRACT_FAULTS_VERIFIED ($($faults.Count) faults rejected; evidence: $scratch)"
