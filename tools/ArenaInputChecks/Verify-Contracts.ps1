param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptRoot 'BepInEx/core/Mono.Cecil.dll')
$arenaPath=Join-Path $SptRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$arenaAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($arenaPath)
try {
 $arenaManager=$arenaAssembly.MainModule.GetType('EFT.InputSystem.InputManager')
 $arenaPreset=$arenaAssembly.MainModule.GetType('EFT.InputSystem.InputPreset')
 $arenaList='System.Collections.Generic.List`1<EFT.InputSystem.ECommand>'
 function RequireField($type,$name,$expected,$static) {
  $fields=@($type.Fields | Where-Object Name -eq $name)
  if($fields.Count -ne 1 -or $fields[0].FieldType.FullName -ne $expected -or $fields[0].IsStatic -ne $static) { throw "Native field changed: $name" }
 }
 function RequireMethod($type,$name,$parameters) {
  $methods=@($type.Methods | Where-Object { $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join '|') -eq $parameters })
  if($methods.Count -ne 1 -or $methods[0].IsStatic -or $methods[0].ReturnType.FullName -ne 'System.Void') { throw "Native method changed: $name" }
  return $methods[0]
 }
 RequireField $arenaManager '_isMain' 'System.Boolean' $false
 RequireField $arenaManager '_commandsList' $arenaList $false
 RequireField $arenaManager '_emptyCommandsList' $arenaList $false
 RequireField $arenaManager '_axesList' 'System.Single[]' $false
 RequireField $arenaManager '_currentPreset' 'EFT.InputSystem.InputPreset' $true
 RequireField $arenaPreset '_workingKeyCombinations' 'EFT.InputSystem.InputCombination[]' $false
 $arenaUpdate=RequireMethod $arenaManager 'Update' ''
 $null=RequireMethod $arenaManager 'OnDestroy' ''
 $null=RequireMethod $arenaManager 'FixedUpdate' ''
 $arenaDeliver=RequireMethod $arenaManager 'DeliverInput' ''
 $arenaDispatch=RequireMethod $arenaManager 'DispatchInput' "$arenaList|System.Single[]"
 $arenaCollect=RequireMethod $arenaPreset 'UpdateInput' "$arenaList|System.Single[]|System.Single"
 $arenaCandidates=@($arenaManager.Methods | Where-Object { !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and ($_.Parameters.ParameterType.FullName -join '|') -eq "$arenaList|System.Single[]" })
 if($arenaCandidates.Count -ne 1 -or $arenaCandidates[0] -ne $arenaDispatch) { throw 'Input dispatch signature no longer unique' }
 $arenaText=($arenaUpdate.Body.Instructions | ForEach-Object ToString) -join "`n"
 if($arenaText -notmatch '(?s)::UpdateInput\(.+::_emptyCommandsList.+::DispatchInput\(.+::_haveNewInput.+::DeliverInput\(') { throw 'Native collect/smooth/real delivery order changed' }
 $arenaText=($arenaDeliver.Body.Instructions | ForEach-Object ToString) -join "`n"
 if($arenaText -notmatch '(?s)::_haveNewInput.+::Clear\(.+::_commandsList.+::DispatchInput\(') { throw 'Native real delivery changed' }
 $arenaKeyLoad=@($arenaCollect.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq '_workingKeyCombinations' })[0]
 $arenaClear=@($arenaCollect.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.Name -eq 'Clear' })
 if(!$arenaKeyLoad -or $arenaClear.Count -ne 1 -or $arenaKeyLoad.Next.OpCode.Name -notin @('brfalse','brfalse.s') -or $arenaKeyLoad.Next.Operand.Offset -le $arenaClear[0].Offset -or $arenaKeyLoad.Offset -ge $arenaClear[0].Offset) { throw 'Native non-null key collection guard changed' }
 Write-Output ('Native Assembly-CSharp SHA256: '+(Get-FileHash -LiteralPath $arenaPath -Algorithm SHA256).Hash)
 Write-Output 'ARENA_INPUT_NATIVE_CONTRACTS_PASSED'
} finally { $arenaAssembly.Dispose() }
