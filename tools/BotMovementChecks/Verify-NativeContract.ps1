param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$botBinary = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$botAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($botBinary)
function Method([string]$Type, [string]$Name, [string]$Arguments, [string]$Result) {
    $botType = $botAssembly.MainModule.GetType($Type)
    if ($null -eq $botType) { throw "Missing installed type $Type" }
    $botMethods = @($botType.Methods | Where-Object {
        $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Arguments
    })
    if ($botMethods.Count -ne 1 -or $botMethods[0].ReturnType.FullName -ne $Result) { throw "Installed contract drift $Type.$Name" }
    return $botMethods[0]
}
function Member([string]$Type, [string]$Name, [string]$Expected) {
    $botType = $botAssembly.MainModule.GetType($Type)
    if ($null -eq $botType) { throw "Missing installed type $Type" }
    $botFields = @($botType.Fields | Where-Object { $_.Name -eq $Name -and $_.FieldType.FullName -eq $Expected })
    $botProperties = @($botType.Properties | Where-Object { $_.Name -eq $Name -and $_.PropertyType.FullName -eq $Expected -and $_.GetMethod })
    if (($botFields.Count + $botProperties.Count) -ne 1) { throw "Installed member drift $Type.$Name : $Expected" }
}
function Calls($Method, [string]$Type, [string]$Name) {
    return @($Method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Name
    })
}
try {
    $botMotion = Method 'EFT.MovementContext' 'DirectApplyMotion' 'UnityEngine.Vector3,System.Single' 'System.Void'
    if (!$botMotion.IsPublic -or !$botMotion.IsVirtual -or ($botMotion.Parameters.Name -join ',') -ne 'motion,deltaTime') {
        throw 'DirectApplyMotion signature or Harmony argument names changed'
    }
    if ((Calls $botMotion 'ICharacterController' 'Move').Count -ne 1 -or
        (Calls $botMotion 'ICharacterController' 'get_SpeedLimit').Count -ne 1 -or
        (Calls $botMotion 'ICharacterController' 'set_SpeedLimit').Count -ne 2) {
        throw 'Reviewed native motion no longer calls collision controller and brackets only platform cap'
    }
    [void](Method 'ICharacterController' 'Move' 'UnityEngine.Vector3,System.Single' 'UnityEngine.CollisionFlags')
    [void](Method 'ICharacterController' 'get_SpeedLimit' '' 'System.Single')
    [void](Method 'ICharacterController' 'set_SpeedLimit' 'System.Single' 'System.Void')
    $botCheckLimit = Method 'SimpleCharacterController' 'CheckSpeedLimit' 'UnityEngine.Vector3,System.Single' 'System.Void'
    if (@($botCheckLimit.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_speedLimit'
    }).Count -lt 2) { throw 'Simple controller no longer reads its native speed cap' }
    $botAccounting = Method 'BotMoverImpostor' 'OnMotionApplied' 'UnityEngine.CollisionFlags,UnityEngine.Vector3' 'System.Void'
    $botAccountingBody = $botAccounting.Body.Instructions
    $botMagnitudeCalls = Calls $botAccounting 'UnityEngine.Vector3' 'get_magnitude'
    $botClamps = @($botAccountingBody | Where-Object {
        $_.OpCode.Code.ToString() -eq 'Ldc_R4' -and [Math]::Abs([double]$_.Operand - 0.3) -lt 0.000001
    })
    if ($botMagnitudeCalls.Count -lt 1 -or $botClamps.Count -lt 2) {
        throw 'Reviewed impostor path-accounting maximum no longer uses 0.3m; re-review per-step safety bound'
    }
    Member 'EFT.MovementContext' '_player' 'EFT.Player'
    Member 'EFT.MovementContext' 'CharacterController' 'ICharacterController'
    Member 'EFT.MovementContext' 'IsGrounded' 'System.Boolean'
    Member 'EFT.MovementContext' 'IsInPronePose' 'System.Boolean'
    Member 'EFT.MovementContext' 'IsInMountedState' 'System.Boolean'
    Member 'EFT.MovementContext' 'PlatformMotion' 'UnityEngine.Vector3'
    Member 'EFT.Player' 'MovementContext' 'EFT.MovementContext'
    Member 'EFT.Player' 'CharacterController' 'ICharacterController'
    Member 'EFT.Player' 'AIData' 'IAIData'
    Member 'EFT.Player' 'IsAI' 'System.Boolean'
    Member 'EFT.Player' 'IsYourPlayer' 'System.Boolean'
    Member 'IAIData' 'BotOwner' 'EFT.BotOwner'
    Member 'EFT.BotOwner' 'GetPlayer' 'EFT.Player'
    Member 'EFT.BotOwner' 'BotState' 'EFT.EBotState'
    Member 'EFT.BotOwner' 'Mover' 'BotMover'
    Member 'EFT.BotOwner' 'DoorOpener' 'BotDoorOpener'
    Member 'BotDoorOpener' 'NearDoor' 'System.Boolean'
    Member 'BotDoorOpener' 'Interacting' 'System.Boolean'
    Member 'BotMover' 'Pause' 'System.Boolean'
    Member 'BotMover' 'HasPathAndNoComplete' 'System.Boolean'
    Member 'BotMover' 'CurrentCornerPoint' 'UnityEngine.Vector3'
    Member 'BotMover' 'CurrentState' 'EBotMoverState'
    [void](Method 'EFT.GameWorld' 'GetEverExistedPlayerByID' 'System.String' 'EFT.Player')
    [void](Method 'EFT.GameWorld' 'GetAlivePlayerByProfileID' 'System.String' 'EFT.Player')
    Write-Output 'BOT_MOVEMENT_NATIVE_CONTRACT_PASSED'
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $botBinary -Algorithm SHA256).Hash)
} finally { $botAssembly.Dispose() }
