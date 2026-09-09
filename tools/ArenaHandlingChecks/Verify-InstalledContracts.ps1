param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx\core\Mono.Cecil.dll')
$arenaBinary = Join-Path $SptPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
$arenaAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($arenaBinary)
function Find-Method([string]$Type, [string]$Name, [string]$Arguments, [string]$Result = 'System.Void') {
    $arenaType = $arenaAsm.MainModule.GetType($Type)
    if ($null -eq $arenaType) { throw "Missing installed type: $Type" }
    $arenaMatches = @($arenaType.Methods | Where-Object {
        $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Arguments
    })
    if ($arenaMatches.Count -ne 1 -or $arenaMatches[0].ReturnType.FullName -ne $Result) { throw "Installed method drift: $Type.$Name($Arguments)" }
    return $arenaMatches[0]
}
function Calls($Method, [string]$Type, [string]$Name) {
    return @($Method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Name
    })
}
try {
    $arenaLanding = Find-Method 'EFT.Player' 'LandingAdjustments' 'System.Single'
    $arenaLimit = Find-Method 'EFT.MovementContext' 'ChangeSpeedLimit' 'System.Single,EFT.Player/ESpeedLimit,System.Single'
    $arenaJumpEnd = Find-Method 'EFT.MovementContext' 'OnJumpEnd' ''
    if ((Calls $arenaLanding 'EFT.MovementContext' 'ChangeSpeedLimit').Count -ne 1 -or
        (Calls $arenaJumpEnd 'EFT.MovementContext' 'ChangeSpeedLimit').Count -ne 1) {
        throw 'Landing and ordinary jump no longer have the reviewed distinct limit call sites.'
    }
    if ((Calls $arenaLimit 'UnityEngine.Time' 'get_realtimeSinceStartup').Count -lt 1) { throw 'Native limit duration clock changed.' }
    $arenaStart = Find-Method 'EFT.Player/ObjectInHandsOperation' 'Start' ''
    if ($arenaStart.IsVirtual -or (Calls $arenaStart 'EFT.Player/ObjectInHandsOperation' 'set_State').Count -ne 1) {
        throw 'The common nonvirtual operation-start contract changed.'
    }
    $arenaSpawn = Find-Method 'EFT.Player/FirearmController/SpawnOperation' 'Start' 'System.Action'
    $arenaRemove = Find-Method 'EFT.Player/FirearmController/Remove' 'Start' 'System.Action,System.Boolean,EFT.InventoryLogic.Item'
    # Remove has a separate simplified-skeleton fast-forward branch plus the
    # ordinary animated branch; each invokes the same common Start once.
    if ((Calls $arenaSpawn 'EFT.Player/ObjectInHandsOperation' 'Start').Count -ne 1 -or
        (Calls $arenaRemove 'EFT.Player/ObjectInHandsOperation' 'Start').Count -ne 2) {
        throw 'Reviewed draw/holster no longer passes through the exact common Start.'
    }
    $arenaSpeed = Find-Method 'FirearmsAnimator' 'SetSpeedParameters' 'System.Single,System.Single'
    if (($arenaSpeed.Parameters.Name -join ',') -ne 'reload,draw') { throw 'Harmony draw argument name changed.' }
    $arenaSpeedCalls = @($arenaSpeed.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
    if ((Calls $arenaSpeed 'AnimationControllerParametersTable' 'SetSpeedReload').Count -ne 1 -or
        (Calls $arenaSpeed 'AnimationControllerParametersTable' 'SetSpeedDraw').Count -ne 1 -or
        ($arenaSpeedCalls | Where-Object { $_.Operand.Name -eq 'set_speed' })) { throw 'Native independent reload/draw setter contract changed.' }
    $arenaDrawSetter = Find-Method 'AnimationControllerParametersTable' 'SetSpeedDraw' 'IAnimator,System.Single'
    $arenaDrawGetter = Find-Method 'AnimationControllerParametersTable' 'GetFloatSpeedDraw' 'IAnimator' 'System.Single'
    foreach ($arenaAccessor in @($arenaDrawSetter, $arenaDrawGetter)) {
        if (@($arenaAccessor.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'FLOAT_SPEEDDRAW'
        }).Count -ne 1) { throw 'Draw accessor no longer targets only SpeedDraw.' }
    }
    [void](Find-Method 'ObjectInHandsAnimator' 'get_Animator' '' 'IAnimator')
    [void](Find-Method 'ObjectInHandsAnimator' 'HasParameter' 'System.Int32' 'System.Boolean')
    [void](Find-Method 'EFT.Player/FirearmController' 'ManualUpdate' 'System.Single')
    [void](Find-Method 'EFT.Player/FirearmController' 'Destroy' '')
    foreach ($arenaField in @('Controller', 'Player', 'FirearmsAnimator', 'Weapon')) {
        $arenaMatches = @($arenaAsm.MainModule.GetType('EFT.Player/FirearmController/FirearmOperation').Fields | Where-Object Name -eq $arenaField)
        if ($arenaMatches.Count -ne 1 -or !$arenaMatches[0].IsPublic) { throw "Native operation field is unavailable: $arenaField" }
    }
    $arenaIdle = Find-Method 'EFT.Player/FirearmController/Idling' 'Start' 'System.Action'
    if ((Calls $arenaIdle 'EFT.Player/FirearmController' 'SetAnimatorAndProceduralValues').Count -ne 1) { throw 'Idle no longer recalculates native skill/injury speeds.' }
    $arenaAppeared = Find-Method 'EFT.Player/FirearmController/SpawnOperation' 'WeaponAppeared' ''
    if ((Calls $arenaAppeared 'EFT.Player/FirearmController/Idling' 'Start').Count -ne 1) { throw 'Draw completion no longer transitions through native idle.' }
    Write-Output 'ARENA_HANDLING_INSTALLED_CONTRACTS_PASSED'
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $arenaBinary -Algorithm SHA256).Hash)
}
finally { $arenaAsm.Dispose() }
