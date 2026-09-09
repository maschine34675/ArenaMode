param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$arenaGamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$arenaAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($arenaGamePath)
try {
    $arenaIdling = $arenaAssembly.MainModule.GetType('EFT.Player/FirearmController/Idling')
    $arenaTrigger = @($arenaIdling.Methods | Where-Object { $_.Name -eq 'SetTriggerPressed' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Boolean' })
    if ($arenaTrigger.Count -ne 1 -or !$arenaTrigger[0].HasBody) { throw 'Native trigger operation differs from reviewed source' }
    $arenaCalls = @($arenaTrigger[0].Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
    $arenaPredicates = @($arenaCalls | Where-Object { $_.Operand.Name -eq 'get_SuitableForHandInput' -and $_.Operand.DeclaringType.FullName -eq 'EFT.Player/ItemHandsController' })
    if ($arenaPredicates.Count -ne 1) { throw 'Native trigger must contain exactly one intended state predicate' }
    foreach ($arenaGuard in @('CanPressTrigger', 'get_ChamberAmmoCount', 'get_MalfState', 'DryShot')) {
        if (@($arenaCalls | Where-Object { $_.Operand.Name -eq $arenaGuard }).Count -lt 1) { throw "Native trigger guard drifted: $arenaGuard" }
    }
    $arenaHands = $arenaAssembly.MainModule.GetType('EFT.Player/ItemHandsController')
    $arenaOwner = @($arenaHands.Fields | Where-Object { $_.Name -eq '_player' -and $_.FieldType.FullName -eq 'EFT.Player' })
    if ($arenaOwner.Count -ne 1) { throw 'Native hands owner contract differs' }
    Write-Output "Installed Assembly-CSharp SHA256: $((Get-FileHash -LiteralPath $arenaGamePath).Hash)"
    Write-Output 'ARENA_AIR_FIRE_INSTALLED_CONTRACT_PASSED'
}
finally { $arenaAssembly.Dispose() }
