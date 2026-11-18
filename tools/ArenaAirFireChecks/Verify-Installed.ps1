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
    $arenaPlayer = $arenaAssembly.MainModule.GetType('EFT.Player')
    $arenaVisual = @($arenaPlayer.Methods | Where-Object Name -eq 'VisualPass')
    if ($arenaVisual.Count -ne 1 -or !$arenaVisual[0].HasBody) { throw 'Native VisualPass contract differs' }
    $arenaPoseReads = @($arenaVisual[0].Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetCurveValue' `
            -and $_.Previous.OpCode.Code -eq 'Ldsfld' -and $_.Previous.Operand.Name -eq 'WEAPON_ROOT_3RD'
    })
    if ($arenaPoseReads.Count -ne 1) { throw 'Native VisualPass must read body weapon authority exactly once' }
    $arenaShift = @($arenaVisual[0].Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ShiftWeaponRoot'
    })
    if ($arenaShift.Count -ne 1 -or $arenaShift[0].Operand.Parameters[2].Name -ne 'thirdPersonAuthority') { throw 'Native first-person weapon authority consumer drifted' }
    $arenaFirearm = $arenaAssembly.MainModule.GetType('EFT.Player/FirearmController')
    $arenaSetter = @($arenaFirearm.Methods | Where-Object Name -eq 'set_IsAiming')
    if ($arenaSetter.Count -ne 1 -or $arenaSetter[0].Parameters[0].Name -ne 'value' -or !$arenaSetter[0].HasBody) { throw 'Native aiming setter contract drifted' }
    $arenaSetterCalls = @($arenaSetter[0].Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
    foreach ($arenaEffect in @('HoldBreath','SetAimingSlowdown','Aim','SetAimingInAnimator','UpdateSensitivity','AimingChanged')) {
        if ($arenaEffect -notin $arenaSetterCalls) { throw "Native aiming effect drifted: $arenaEffect" }
    }
    $arenaChanged = @($arenaFirearm.Methods | Where-Object Name -eq 'StateChangedHandler')
    if ($arenaChanged.Count -ne 1 -or $arenaChanged[0].Parameters.Count -ne 2 -or $arenaChanged[0].Parameters[1].Name -ne 'nextstate') { throw 'Native state-change callback contract drifted' }
    Write-Output "Installed Assembly-CSharp SHA256: $((Get-FileHash -LiteralPath $arenaGamePath).Hash)"
    Write-Output 'ARENA_AIR_FIRE_INSTALLED_CONTRACT_PASSED'
}
finally { $arenaAssembly.Dispose() }
