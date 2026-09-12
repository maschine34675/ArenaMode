param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$arenaPath = Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$arenaAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($arenaPath)
try {
 $arenaModule = $arenaAssembly.MainModule
 function Get-ArenaMethod([string]$type, [string]$name, [string]$parameters) {
  $matches = @($arenaModule.GetType($type).Methods | Where-Object {
   $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $parameters
  })
  if ($matches.Count -ne 1 -or !$matches[0].IsPublic -or $matches[0].IsStatic -or $matches[0].ReturnType.FullName -ne 'System.Void') { throw "Changed method: $type.$name" }
  return $matches[0]
 }
 $arenaJump = Get-ArenaMethod 'EFT.JumpPlayerState' 'ProcessJumpMovement' 'System.Single'
 $arenaLimit = Get-ArenaMethod 'EFT.MovementState' 'LimitMotion' 'UnityEngine.Vector3&,System.Single'
 $arenaMove = Get-ArenaMethod 'EFT.Player' 'Move' 'UnityEngine.Vector2'
 $null = Get-ArenaMethod 'EFT.MovementContext' 'CheckFlying' 'System.Single'
 $arenaCalls = @($arenaJump.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.FullName -eq $arenaLimit.FullName })
 if ($arenaCalls.Count -ne 1) { throw 'Jump collision call is not unique' }
 $arenaCall = $arenaCalls[0]
 if ($arenaCall.Previous.OpCode.Name -ne 'ldarg.1' -or $arenaCall.Previous.Previous.OpCode.Name -notin @('ldloca','ldloca.s') -or $arenaCall.Previous.Previous.Previous.OpCode.Name -ne 'ldarg.0') { throw 'Jump collision stack changed' }
 $arenaApply = @($arenaJump.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.FullName -eq 'System.Void EFT.MovementContext::ApplyMotion(UnityEngine.Vector3,System.Single)' })
 if ($arenaApply.Count -ne 1 -or $arenaApply[0].Offset -le $arenaCall.Offset) { throw 'Native motion scheduling no longer follows collision' }
 $arenaLimitCalls = @($arenaLimit.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' })
 if ($arenaLimitCalls.Count -ne 1 -or $arenaLimitCalls[0].Operand.FullName -ne 'System.Void EFT.MovementContext::LimitMotionXZ(UnityEngine.Vector3&,System.Single,System.Single)') { throw 'Native limiter delegation changed' }
 if (!$arenaMove.IsVirtual -or !($arenaMove.Body.Instructions | Where-Object { $_.Operand.FullName -eq 'System.Void EFT.MovementState::Move(UnityEngine.Vector2)' })) { throw 'Player input seam changed' }
 foreach ($arenaType in @('EFT.LocalPlayer','EFT.ClientPlayer')) {
  if ($arenaModule.GetType($arenaType).Methods | Where-Object { $_.Name -eq 'Move' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Vector2' }) { throw "Player subclass now overrides Move: $arenaType" }
 }
 $arenaFall = $arenaModule.GetType('EFT.FallDown')
 if ($arenaFall.BaseType.FullName -ne 'EFT.JumpPlayerState' -or ($arenaFall.Methods | Where-Object Name -in @('ProcessJumpMovement','LimitMotion'))) { throw 'Fall no longer inherits the jump integration seam' }
 foreach ($arenaField in @(@('EFT.JumpPlayerState','_jumpState','EFT.JumpPlayerState/EJumpState'), @('EFT.JumpPlayerState','_currentLiftforce','UnityEngine.Vector3'), @('EFT.MovementState','MovementContext','EFT.MovementContext'))) {
  $arenaActual = @($arenaModule.GetType($arenaField[0]).Fields | Where-Object Name -eq $arenaField[1])
  if ($arenaActual.Count -ne 1 -or !$arenaActual[0].IsPublic -or $arenaActual[0].FieldType.FullName -ne $arenaField[2]) { throw ('Native field changed: '+$arenaField[1]) }
 }
 Write-Output ('Assembly SHA256: '+(Get-FileHash -LiteralPath $arenaPath -Algorithm SHA256).Hash)
 Write-Output 'ARENA_AIR_CONTROL_INSTALLED_CONTRACTS_PASSED'
} finally { $arenaAssembly.Dispose() }
