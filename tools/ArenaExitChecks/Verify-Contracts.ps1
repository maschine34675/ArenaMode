param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$arenaGame=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
$arenaSain=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
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
 Write-Output 'ARENA_EXIT_INSTALLED_CONTRACTS_PASSED'
} finally { $arenaGame.Dispose(); $arenaSain.Dispose() }
