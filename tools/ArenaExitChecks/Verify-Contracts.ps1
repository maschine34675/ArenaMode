param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$arenaGame=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$arenaSain=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
$arenaComfort=$null

function Require-ArenaMethod($Type, [string]$Name, [string]$ReturnType, [string[]]$Parameters=@()) {
 $arenaMatches=@($Type.Methods | Where-Object {
  $_.Name -eq $Name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($Parameters -join '|')
 })
 if($arenaMatches.Count -ne 1 -or !$arenaMatches[0].IsPublic -or $arenaMatches[0].IsStatic -or !$arenaMatches[0].HasBody -or $arenaMatches[0].ReturnType.FullName -ne $ReturnType) {
  throw ('Native method contract changed: '+$Type.FullName+'::'+$Name)
 }
 return $arenaMatches[0]
}
function Require-ArenaField($Type, [string]$Name, [string]$FieldType) {
 $arenaMatches=@($Type.Fields | Where-Object Name -eq $Name)
 if($arenaMatches.Count -ne 1 -or !$arenaMatches[0].IsPublic -or $arenaMatches[0].IsStatic -or $arenaMatches[0].FieldType.FullName -ne $FieldType) {
  throw ('Native field contract changed: '+$Type.FullName+'::'+$Name)
 }
}
function Get-ArenaMemberIndex($Method, [string]$Member, [int]$ExpectedCount=1) {
 $arenaIndices=@(for($arenaIndex=0; $arenaIndex -lt $Method.Body.Instructions.Count; $arenaIndex++) {
  if($Method.Body.Instructions[$arenaIndex].Operand -is [Mono.Cecil.MemberReference] -and $Method.Body.Instructions[$arenaIndex].Operand.FullName -eq $Member) { $arenaIndex }
 })
 if($arenaIndices.Count -ne $ExpectedCount) { throw ('Native member use changed in '+$Method.FullName+': '+$Member) }
 return $arenaIndices[0]
}
try {
 $arenaOwner=$arenaGame.MainModule.GetType('EFT.BotOwner')
 $arenaDispose=@($arenaOwner.Methods | Where-Object { $_.Name -eq 'Dispose' -and $_.Parameters.Count -eq 0 })
 if($arenaDispose.Count -ne 1 -or $arenaDispose[0].ReturnType.FullName -ne 'System.Void') { throw 'Bot disposal changed' }
 $arenaDisposeText=($arenaDispose[0].Body.Instructions | ForEach-Object ToString) -join "`n"
 if($arenaDisposeText -notmatch 'ldnull\s*\n[^\n]+call[^\n]+::set_Medecine\(') { throw 'Native retired Medecine boundary changed' }
 $arenaClosure=$arenaGame.MainModule.GetType('BotItemTaker').NestedTypes | Where-Object Name -eq 'CG_PickupAction'
 foreach($arenaPair in @(@('owner','EFT.Player'),@('botItemTaker_0','BotItemTaker'))) {
  $arenaField=@($arenaClosure.Fields | Where-Object Name -eq $arenaPair[0])
  if($arenaField.Count -ne 1 -or !$arenaField[0].IsPublic -or $arenaField[0].FieldType.FullName -ne $arenaPair[1]) { throw ('Pickup capture changed: '+$arenaPair[0]) }
 }
 $arenaAction=@($arenaClosure.Methods | Where-Object Name -eq 'method_0')
 $arenaText=($arenaAction[0].Body.Instructions | ForEach-Object ToString) -join "`n"
 if($arenaAction.Count -ne 1 -or !$arenaAction[0].IsPublic -or $arenaAction[0].Parameters.Count -ne 0 -or $arenaText -notmatch 'ldftn[^\n]+BotMedecine::RefreshCurMeds') { throw 'Pending pickup callback changed' }
 if($arenaSain.Name.Version.ToString() -ne '4.5.1.0') { throw 'Unsupported SAIN version' }
 $arenaType=$arenaSain.MainModule.GetType('SAIN.Components.BotComponent')
 $arenaMethod=$arenaType.Methods | Where-Object Name -eq 'OnDisable'
 $arenaOpcodes=($arenaMethod.Body.Instructions | ForEach-Object { $_.OpCode.Name }) -join ','
 if($arenaOpcodes -ne 'ldarg.0,call,ldc.i4.0,callvirt,ldarg.0,call,ret' -or $arenaMethod.Body.CodeSize -ne 19 -or $arenaMethod.Body.Variables.Count -ne 0 -or $arenaMethod.Body.ExceptionHandlers.Count -ne 0) { throw 'SAIN cleanup body changed' }
 $arenaCalls=@($arenaMethod.Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') } | ForEach-Object { $_.Operand.FullName })
 $arenaExpected=@('SAIN.SAINComponent.Classes.SAINActivationClass SAIN.Components.BotComponent::get_BotActivation()', 'System.Void SAIN.SAINComponent.Classes.SAINActivationClass::SetActive(System.Boolean)', 'System.Void UnityEngine.MonoBehaviour::StopAllCoroutines()')
 if(($arenaCalls -join '|') -ne ($arenaExpected -join '|')) { throw 'SAIN cleanup call targets changed' }

 # The following reads installed metadata/IL only. It does not execute game
 # methods, dispatch Harmony, or prove the historical F3 null receiver.
 $arenaComfort=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Comfort.dll'))
 $arenaSelector=$arenaGame.MainModule.GetType('BotWeaponSelector')
 $arenaManager=$arenaGame.MainModule.GetType('BotWeaponManager')
 $arenaData=$arenaGame.MainModule.GetType('BotData')
 if($arenaSelector.BaseType.FullName -ne 'BotData' -or $arenaManager.BaseType.FullName -ne 'BotData') { throw 'Weapon owner inheritance changed' }
 Require-ArenaField $arenaData '_owner' 'EFT.BotOwner'
 Require-ArenaField $arenaManager '_disposed' 'System.Boolean'
 [void](Require-ArenaMethod $arenaOwner 'get_WeaponManager' 'BotWeaponManager')
 [void](Require-ArenaMethod $arenaOwner 'get_GetPlayer' 'EFT.Player')
 [void](Require-ArenaMethod $arenaOwner 'get_IsDead' 'System.Boolean')
 [void](Require-ArenaMethod $arenaOwner 'get_BotState' 'EFT.EBotState')
 [void](Require-ArenaMethod $arenaSelector 'set_IsChanging' 'System.Void' @('System.Boolean'))
 [void](Require-ArenaMethod $arenaSelector 'get_IsWeaponReady' 'System.Boolean')
 [void](Require-ArenaMethod $arenaManager 'get_Selector' 'BotWeaponSelector')
 $arenaResult=$arenaComfort.MainModule.GetType('Comfort.Common.Result`1')
 if(!$arenaResult.IsValueType -or $arenaResult.GenericParameters.Count -ne 1) { throw 'Hands Result is no longer the expected value type' }
 [void](Require-ArenaMethod $arenaResult 'get_Succeed' 'System.Boolean')
 [void](Require-ArenaMethod $arenaResult 'get_Value' 'T')
 $arenaTaken=Require-ArenaMethod $arenaSelector 'OnWeaponTaken' 'System.Void' @('Comfort.Common.Result`1<EFT.IHandsController>')
 if(!$arenaTaken.IsVirtual -or $arenaTaken.Body.ExceptionHandlers.Count -ne 0) { throw 'Native weapon callback dispatch changed' }
 $arenaChanging=Get-ArenaMemberIndex $arenaTaken 'System.Void BotWeaponSelector::set_IsChanging(System.Boolean)'
 if($arenaChanging -ne 2 -or $arenaTaken.Body.Instructions[0].OpCode.Name -ne 'ldarg.0' -or $arenaTaken.Body.Instructions[1].OpCode.Name -ne 'ldc.i4.0') { throw 'Native callback completion flag changed' }
 $arenaHandsIndex=Get-ArenaMemberIndex $arenaTaken 'System.Void BotWeaponManager::UpdateHandsController(EFT.IHandsController,System.Boolean&)'
 $arenaStateIndex=Get-ArenaMemberIndex $arenaTaken 'EFT.EBotState EFT.BotOwner::get_BotState()' 2
 [void](Get-ArenaMemberIndex $arenaTaken 'System.Int32 AITaskManager::RegisterDelayedTask(EFT.BotOwner,System.Single,System.Action)' 2)
 [void](Get-ArenaMemberIndex $arenaTaken 'System.Void BotWeaponSelector::TakeMainWeapon()' 2)
 if($arenaHandsIndex -ge $arenaStateIndex -or (($arenaTaken.Body.Instructions | ForEach-Object ToString) -join "`n") -match '::get_IsDead\(|::_disposed') { throw 'Native callback lifetime assumptions changed; review new guards' }
 $arenaSelectorDispose=Require-ArenaMethod $arenaSelector 'Dispose' 'System.Void'
 if((($arenaSelectorDispose.Body.Instructions | ForEach-Object { $_.OpCode.Name }) -join ',') -ne 'ret') { throw 'Selector disposal is no longer empty; review callback ownership' }

 # Preserve the outer callback: its slot/event work precedes OnWeaponTaken;
 # the optional caller continuation follows it. No runtime continuation count
 # is inferred from this IL-order check.
 $arenaWeaponClosure=$arenaSelector.NestedTypes | Where-Object Name -eq 'CG_TryChangeToSlot'
 Require-ArenaField $arenaWeaponClosure 'botWeaponSelector_0' 'BotWeaponSelector'
 Require-ArenaField $arenaWeaponClosure 'slot' 'EFT.InventoryLogic.EquipmentSlot'
 Require-ArenaField $arenaWeaponClosure 'callback' 'System.Action`1<Comfort.Common.Result`1<EFT.IHandsController>>'
 $arenaCompletion=Require-ArenaMethod $arenaWeaponClosure 'method_0' 'System.Void' @('Comfort.Common.Result`1<EFT.IHandsController>')
 $arenaStartIndex=Get-ArenaMemberIndex $arenaCompletion 'System.Single BotWeaponSelector::_startChangeTime'
 $arenaSlotIndex=Get-ArenaMemberIndex $arenaCompletion 'EFT.InventoryLogic.EquipmentSlot BotWeaponSelector::_lastEquipmentSlot'
 $arenaEventIndex=Get-ArenaMemberIndex $arenaCompletion 'System.Void System.Action`1<EFT.InventoryLogic.EquipmentSlot>::Invoke(!0)'
 $arenaTakenIndex=Get-ArenaMemberIndex $arenaCompletion $arenaTaken.FullName
 $arenaContinuationIndex=Get-ArenaMemberIndex $arenaCompletion 'System.Void System.Action`1<Comfort.Common.Result`1<EFT.IHandsController>>::Invoke(!0)'
 if(!($arenaStartIndex -lt $arenaSlotIndex -and $arenaSlotIndex -lt $arenaEventIndex -and $arenaEventIndex -lt $arenaTakenIndex -and $arenaTakenIndex -lt $arenaContinuationIndex)) { throw 'Outer weapon callback order changed' }

 $arenaManagerDispose=Require-ArenaMethod $arenaManager 'Dispose' 'System.Void'
 $arenaSelectorClear=Get-ArenaMemberIndex $arenaManagerDispose 'System.Void BotWeaponManager::set_Selector(BotWeaponSelector)'
 $arenaMeleeClear=Get-ArenaMemberIndex $arenaManagerDispose 'System.Void BotWeaponManager::set_Melee(BotMeleeWeaponData)'
 $arenaInfoClear=Get-ArenaMemberIndex $arenaManagerDispose 'System.Collections.Generic.Dictionary`2<EFT.InventoryLogic.EquipmentSlot,BotWeaponInfo> BotWeaponManager::info' 2
 # info is read for Clear() before the final null store; check that store itself.
 $arenaInfoStore=@($arenaManagerDispose.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'info' })
 if($arenaInfoStore.Count -ne 1 -or $arenaInfoStore[0].Previous.OpCode.Name -ne 'ldnull') { throw 'Weapon info retirement changed' }
 $arenaDisposedStore=Get-ArenaMemberIndex $arenaManagerDispose 'System.Boolean BotWeaponManager::_disposed'
 if($arenaManagerDispose.Body.Instructions[$arenaSelectorClear-1].OpCode.Name -ne 'ldnull' -or $arenaManagerDispose.Body.Instructions[$arenaMeleeClear-1].OpCode.Name -ne 'ldnull' -or $arenaManagerDispose.Body.Instructions[$arenaDisposedStore-1].OpCode.Name -ne 'ldc.i4.1' -or $arenaManagerDispose.Body.Instructions[$arenaDisposedStore].OpCode.Name -ne 'stfld' -or !($arenaSelectorClear -lt $arenaMeleeClear -and $arenaMeleeClear -lt $arenaInfoClear -and $arenaInfoStore[0].Offset -lt $arenaManagerDispose.Body.Instructions[$arenaDisposedStore].Offset)) { throw 'Weapon manager retirement order changed' }
 $arenaHands=Require-ArenaMethod $arenaManager 'UpdateHandsController' 'System.Void' @('EFT.IHandsController','System.Boolean&')
 $arenaFirearms=Require-ArenaMethod $arenaManager 'UpdateFirearmsController' 'System.Void' @('EFT.IFirearmHandsController','EFT.Interactive.StationaryWeapon')
 $arenaFirearmsIndex=Get-ArenaMemberIndex $arenaHands $arenaFirearms.FullName
 $arenaMeleeIndex=Get-ArenaMemberIndex $arenaHands 'BotMeleeWeaponData BotWeaponManager::get_Melee()' 2
 $arenaKnifeIndex=Get-ArenaMemberIndex $arenaHands 'System.Void BotMeleeWeaponData::UpdateKnifeController(EFT.IKnifeController)' 2
 if(!($arenaFirearmsIndex -lt $arenaMeleeIndex -and $arenaMeleeIndex -lt $arenaKnifeIndex) -or (($arenaHands.Body.Instructions | ForEach-Object ToString) -join "`n") -match '::_disposed') { throw 'Native hands retirement gap changed; re-review necessity' }
 if((($arenaFirearms.Body.Instructions | Select-Object -First 4 | ForEach-Object { $_.OpCode.Name }) -join ',') -ne 'ldarg.0,ldfld,brfalse.s,ret' -or $arenaFirearms.Body.Instructions[1].Operand.FullName -ne 'System.Boolean BotWeaponManager::_disposed') { throw 'Native inner firearm retirement guard changed' }
 # NonActive is also used by temporary disable/deactivate, without disposal.
 foreach($arenaName in @('Disable','Deactivate')) {
  $arenaTemporary=Require-ArenaMethod $arenaOwner $arenaName 'System.Void'
  if((($arenaTemporary.Body.Instructions | ForEach-Object { $_.OpCode.Name }) -join ',') -ne 'ldarg.0,ldc.i4.0,call,ret' -or $arenaTemporary.Body.Instructions[2].Operand.FullName -ne 'System.Void EFT.BotOwner::set_BotState(EFT.EBotState)') { throw ('Temporary bot state transition changed: '+$arenaName) }
 }
 $arenaStates=$arenaGame.MainModule.GetType('EFT.EBotState')
 foreach($arenaPair in @(@('NonActive',0),@('PreActive',1),@('Active',2),@('Disposed',4))) {
  $arenaState=@($arenaStates.Fields | Where-Object Name -eq $arenaPair[0])
  if($arenaState.Count -ne 1 -or $arenaState[0].Constant -ne $arenaPair[1]) { throw 'Native retirement enum changed' }
 }
 Write-Output 'ARENA_WEAPON_CALLBACK_INSTALLED_CONTRACTS_PASSED'
 Write-Output 'ARENA_EXIT_INSTALLED_CONTRACTS_PASSED'
} finally { $arenaGame.Dispose(); $arenaSain.Dispose(); if($arenaComfort) { $arenaComfort.Dispose() } }
