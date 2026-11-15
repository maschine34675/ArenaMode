param([string]$SptPath='D:\SPT41')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$physics=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/UnityEngine.PhysicsModule.dll'))
function Require([bool]$Value,[string]$Reason){if(-not $Value){throw "Native teleport contract drift: $Reason"}}
function Method([string]$Type,[string]$Name,[int]$Count=-1){
    $methods=@($game.MainModule.GetType($Type).Methods|Where-Object {$_.Name -eq $Name -and ($Count -lt 0 -or $_.Parameters.Count -eq $Count)})
    Require ($methods.Count -eq 1) "$Type.$Name signature";$methods[0]
}
function Fields($method,[string]$Code){@($method.Body.Instructions|Where-Object {$_.OpCode.Name -eq $Code -and $_.Operand -is [Mono.Cecil.FieldReference]}|ForEach-Object {$_.Operand.Name})}
try {
    $teleport=Method 'EFT.Player' 'Teleport' 2
    Require ($teleport.IsPublic -and $teleport.IsVirtual -and $teleport.ReturnType.FullName -eq 'System.Void' -and $teleport.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector3' -and $teleport.Parameters[1].ParameterType.FullName -eq 'System.Boolean') 'native same-body public teleport'
    $calls=@($teleport.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    foreach($call in @('set_TransformPosition','ResetHeightInterpolation','ResetFlying','UpdateEnvironmentForPlayer')){Require ($calls -contains $call) "Teleport retains $call"}
    $writes=@(Fields $teleport 'stfld');Require ($writes.Count -eq 1 -and $writes[0] -eq '_dampVelocity') 'Teleport must not gain unreviewed motion writes'
    $reset=Method 'EFT.MovementContext' 'ResetFlying' 0
    $writes=@(Fields $reset 'stfld');Require ($writes.Count -eq 2 -and $writes -contains '_startFallingHeight' -and $writes -contains '_startFlyHeight') 'ResetFlying resets origins only, not phase or freefall'
    $jump=Method 'EFT.JumpPlayerState' 'ProcessJumpMovement' 1
    $reads=@(Fields $jump 'ldfld');Require ($reads -contains '_potentialH' -and $reads -contains '_stateTime' -and $reads -contains '_currentMagnitude') 'airborne spatial prediction and momentum remain native'
    Require ($game.MainModule.GetType('EFT.FallDown').BaseType.FullName -eq 'EFT.JumpPlayerState') 'FallDown shares the rebased jump history'
    foreach($entry in @(@('EFT.MovementContext','_lastPlayerHeight'),@('EFT.MovementContext','_hasLastHeight'),@('EFT.MovementContext','FreefallTime'),@('EFT.MovementState','Destination'),@('EFT.JumpPlayerState','_potentialH'),@('EFT.JumpPlayerState','_lastPlayerHeight'),@('EFT.JumpPlayerState','_hasLastHeight'),@('SimpleCharacterController','_mainCollider'),@('SimpleCharacterController','_colliders'),@('SimpleCharacterController','_collisionMask'),@('PlayerBones','BodyPartCollidersHashSet'))){
        $field=@($game.MainModule.GetType($entry[0]).Fields|Where-Object Name -EQ $entry[1]);Require ($field.Count -eq 1 -and $field[0].IsPublic) ($entry -join '.')
    }
    $move=Method 'SimpleCharacterController' 'Move' 2
    Require (@($move.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_position'}).Count -ge 1) 'controller starts its next native move from transform position'
    $contact=Method 'SimpleCharacterController' 'set_isGrounded' 1
    $writes=@(Fields $contact 'stfld');Require ($contact.IsPublic -and $writes.Count -eq 1 -and $writes[0] -eq '_isGrounded') 'contact invalidation changes only the stale controller witness'
    $contextContact=Method 'EFT.MovementContext' 'set_IsGrounded' 1
    $writes=@(Fields $contextContact 'stfld');Require ($contextContact.IsPublic -and $writes.Count -eq 1 -and $writes[0] -eq '_isGrounded') 'context contact invalidation changes only its stale support witness'
    $ground=Method 'EFT.MovementContext' 'UpdateGroundCollision' 1
    $calls=@($ground.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]}|ForEach-Object {$_.Operand.Name})
    foreach($call in @('GroundCollisionRaycast','get_isGrounded','set_IsGrounded','SetIsGrounded','PlayerAnimatorSetFallingDownFloat','CheckFlying')){Require ($calls -contains $call) "native ground refresh retains $call"}
    $writes=@(Fields $ground 'stfld');Require ($writes -contains '_groundHit' -and $writes -notcontains 'FreefallTime') 'ground refresh updates actual ground hit without rewriting freefall phase'
    $gravity=Method 'EFT.MovementContext' 'ApplyGravity' 3
    $reads=@(Fields $gravity 'ldfld');Require ($reads -contains 'FreefallTime') 'native idle gravity uses retained freefall time'
    foreach($entry in @(@('SyncTransforms',0),@('OverlapBoxNonAlloc',6),@('ComputePenetration',8))){
        $matches=@($physics.MainModule.GetType('UnityEngine.Physics').Methods|Where-Object {$_.Name -eq $entry[0] -and $_.Parameters.Count -eq $entry[1] -and $_.IsPublic -and $_.IsStatic})
        Require ($matches.Count -eq 1) ('Unity physical query '+$entry[0])
    }
    Write-Output "TRANSLOCATOR_NATIVE_CONTRACT_PASSED gameMvid=$($game.MainModule.Mvid); source and installed IL only, no native gameplay claim"
} finally {$game.Dispose();$physics.Dispose()}
