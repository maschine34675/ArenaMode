param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx\core\Mono.Cecil.dll')
$reloadBinary = Join-Path $SptPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
$reloadAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($reloadBinary)
function Method([string]$Type, [string]$Name, [string]$Arguments, [string]$Result = 'System.Void') {
    $reloadType = $reloadAssembly.MainModule.GetType($Type)
    if (!$reloadType) { throw "Missing type $Type" }
    $reloadMatches = @($reloadType.Methods | Where-Object { $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Arguments })
    if ($reloadMatches.Count -ne 1 -or $reloadMatches[0].ReturnType.FullName -ne $Result) { throw "Contract drift: $Type.$Name($Arguments)" }
    return $reloadMatches[0]
}
function Calls($Method, [string]$Type, [string]$Name) {
    return @($Method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Name })
}
try {
    $reloadState = Method 'EFT.Player/FirearmController/BaseReloadOperation' 'set_State' 'EFT.Player/EOperationState'
    if (!$reloadState.IsVirtual -or $reloadState.Parameters[0].Name -ne 'value' -or
        (Calls $reloadState 'EFT.Player/ObjectInHandsOperation' 'set_State').Count -ne 1 -or
        (Calls $reloadState 'EFT.MovementState' 'OnReload').Count -ne 2) { throw 'Reload lifecycle setter changed.' }
    $reloadReset = Method 'EFT.Player/ObjectInHandsOperation' 'Reset' ''
    if ((Calls $reloadReset 'EFT.Player/ObjectInHandsOperation' 'set_State').Count -ne 1) { throw 'Operation reset bypasses state setter.' }
    $reloadDeath = Method 'EFT.Player/FirearmController' 'OnPlayerDead' ''
    if ((Calls $reloadDeath 'EFT.Player/FirearmController/FirearmOperation' 'FastForward').Count -ne 1) { throw 'Firearm death fast-forward contract changed.' }
    $reloadRefresh = Method 'EFT.Player/FirearmController' 'SetAnimatorAndProceduralValues' ''
    foreach ($reloadEntry in @(@('FirearmsAnimator', 3), @('EFT.PlayerAnimator', 2))) {
        if ((Calls $reloadRefresh $reloadEntry[0] 'SetSpeedParameters').Count -ne $reloadEntry[1]) { throw 'Native refresh call count changed.' }
        $reloadSetter = Method $reloadEntry[0] 'SetSpeedParameters' 'System.Single,System.Single'
        if ((Calls $reloadSetter 'AnimationControllerParametersTable' 'SetSpeedReload').Count -ne 1 -or
            (Calls $reloadSetter 'AnimationControllerParametersTable' 'SetSpeedDraw').Count -ne 1) { throw 'Independent speed channels changed.' }
        if (@($reloadSetter.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'set_speed' }).Count) { throw 'Native setter now changes global speed.' }
    }
    foreach ($reloadAccessor in @((Method 'AnimationControllerParametersTable' 'GetFloatSpeedReload' 'IAnimator' 'System.Single'),
        (Method 'AnimationControllerParametersTable' 'SetSpeedReload' 'IAnimator,System.Single'))) {
        $reloadFields = @($reloadAccessor.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] })
        if ($reloadFields.Count -ne 1 -or $reloadFields[0].Operand.Name -ne 'FLOAT_SPEEDRELOAD') { throw 'Reload accessor no longer addresses only SpeedReload.' }
    }
    $reloadArmsUpdate = Method 'EFT.Player' 'ArmsUpdate' 'System.Single'
    $reloadManual = Calls $reloadArmsUpdate 'EFT.Player/AbstractHandsController' 'ManualUpdate'
    $reloadAnimation = Calls $reloadArmsUpdate 'IAnimator' 'Update'
    if ($reloadManual.Count -ne 1 -or $reloadAnimation.Count -lt 1 -or $reloadManual[0].Offset -ge $reloadAnimation[0].Offset) { throw 'Hands update no longer precedes animator evaluation.' }
    foreach ($reloadFamily in @('ReloadExternalMagOperation', 'ReloadInternalMagOperation', 'ReloadInternalMagWithOpenBoltOperation',
        'ReloadCylinderMagOperation', 'ReloadSingleBarrelOperation', 'ReloadMultiBarrelOperation')) {
        $reloadType = $reloadAssembly.MainModule.GetType('EFT.Player/FirearmController/' + $reloadFamily)
        if (!$reloadType) { throw "Missing reload family $reloadFamily" }
        while ($reloadType.FullName -ne 'EFT.Player/FirearmController/BaseReloadOperation' -and $reloadType.BaseType) {
            if (@($reloadType.Methods | Where-Object Name -eq 'set_State').Count) { throw "Family overrides the reviewed lifecycle: $reloadFamily" }
            $reloadType = $reloadType.BaseType.Resolve()
        }
        if ($reloadType.FullName -ne 'EFT.Player/FirearmController/BaseReloadOperation') { throw "Unknown reload lifecycle $reloadFamily" }
    }
    # Native magazine swaps (also UIFixes in-place R reload) are FirearmOperation
    # siblings, not BaseReloadOperation. Pin their nontrivial completion/reset
    # endpoints rather than depending on the tiny base State setter being patched.
    foreach ($reloadBoundary in @(
        @('InsertMagOperation', 'SwitchToIdlingState', ''),
        @('InsertMagOperation', 'HideWeapon', 'System.Action,System.Boolean,EFT.InventoryLogic.Item'),
        @('InsertMagOperation', 'Reset', ''),
        @('PullOutMagOperation', 'OnMagPuttedToRig', ''),
        @('PullOutMagOperation', 'Reset', '')
    )) {
        $reloadBoundaryType = 'EFT.Player/FirearmController/' + $reloadBoundary[0]
        $reloadEndpoint = Method $reloadBoundaryType $reloadBoundary[1] $reloadBoundary[2]
        if ($reloadEndpoint.DeclaringType.BaseType.FullName -ne 'EFT.Player/FirearmController/FirearmOperation' -or
            $reloadEndpoint.Body.CodeSize -lt 20) { throw 'Native magazine boundary ownership or nontrivial body changed.' }
        if ($reloadBoundary[1] -eq 'Reset') {
            if (@($reloadEndpoint.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Reset' }).Count -ne 1) { throw 'Magazine reset no longer chains native reset.' }
        } else {
            $reloadFinished = Calls $reloadEndpoint 'EFT.Player/ObjectInHandsOperation' 'set_State'
            $reloadNext = @($reloadEndpoint.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'InitiateOperation' })
            if ($reloadFinished.Count -lt 1 -or $reloadNext.Count -ne 1 -or $reloadFinished[0].Offset -ge $reloadNext[0].Offset) { throw 'Magazine completion or next-operation boundary changed.' }
        }
    }
    foreach ($reloadMagazineType in @('InsertMagOperation', 'PullOutMagOperation')) {
        $reloadFastForward = Method ('EFT.Player/FirearmController/' + $reloadMagazineType) 'FastForward' ''
        $reloadFinishMethod = if ($reloadMagazineType -eq 'InsertMagOperation') { 'SwitchToIdlingState' } else { 'OnMagPuttedToRig' }
        $reloadFinishCalls = @($reloadFastForward.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $reloadFinishMethod -and
            $_.Operand.DeclaringType.FullName -in @('EFT.Player/FirearmController/FirearmOperation', ('EFT.Player/FirearmController/' + $reloadMagazineType))
        })
        if ($reloadFinishCalls.Count -ne 1) { throw "Magazine fast-forward bypasses reviewed completion: $reloadMagazineType" }
    }
    $reloadScope = Join-Path $PSScriptRoot '..\..\ArenaMode.Client\Movement\ArenaHandlingPatches.cs'
    $reloadIntegration = Get-Content -LiteralPath $reloadScope -Raw
    foreach ($reloadCall in @('ArenaReloadHandling.Enable();', 'ArenaReloadHandling.Shutdown();', 'ArenaReloadHandling.ResetPerRaidState();',
        'ArenaReloadHandling.OnControllerUpdate(controller);', 'ArenaReloadHandling.Release(__instance);')) {
        if (!$reloadIntegration.Contains($reloadCall)) { throw "Missing handling lifecycle integration: $reloadCall" }
    }
    Write-Output 'ARENA_RELOAD_INSTALLED_CONTRACTS_PASSED'
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $reloadBinary -Algorithm SHA256).Hash)
}
finally { $reloadAssembly.Dispose() }
