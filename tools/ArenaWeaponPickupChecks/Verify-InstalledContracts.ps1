param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx\core\Mono.Cecil.dll')
$arenaBinary = Join-Path $SptPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
$arenaAsm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($arenaBinary)
$script:arenaChecks = 0
function Require([bool]$Value, [string]$Message) {
    $script:arenaChecks++
    if (!$Value) { throw "Native weapon pickup contract changed: $Message" }
}
function Method([string]$Type, [string]$Name, [string]$Arguments) {
    $arenaType = $arenaAsm.MainModule.GetType($Type)
    Require ($null -ne $arenaType) "missing type $Type"
    $arenaMethods = @($arenaType.Methods | Where-Object { $_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Arguments })
    Require ($arenaMethods.Count -eq 1) "$Type.$Name($Arguments)"
    return $arenaMethods[0]
}
function Calls($Method, [string]$Name) {
    return @($Method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $Name })
}
function AsyncBody($Method) {
    $arenaAttribute = @($Method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.CompilerServices.AsyncStateMachineAttribute' })
    Require ($arenaAttribute.Count -eq 1) ('missing async state machine for ' + $Method.FullName)
    $arenaState = $arenaAsm.MainModule.GetType($arenaAttribute[0].ConstructorArguments[0].Value.FullName)
    return @($arenaState.Methods | Where-Object Name -eq 'MoveNext')[0]
}
try {
    $arenaLimits = Method 'EFT.InventoryLogic.ChangeDiscardLimitsResult' '.ctor' 'EFT.InventoryLogic.Item,System.Int32,EFT.InventoryLogic.IAntiRMTItemController,EFT.InventoryLogic.ItemAddress'
    $arenaCount = @($arenaLimits.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Newobj' -and $_.Operand.DeclaringType.FullName -eq 'EFT.InventoryLogic.ItemsCount' })
    Require ($arenaCount.Count -eq 1) 'discard bookkeeping constructs exactly one ItemsCount'
    $arenaNew = $arenaCount[0]
    Require ($arenaNew.Previous.OpCode.Code -eq 'Ldarg_2' -and $arenaNew.Previous.Previous.OpCode.Code -eq 'Ldc_I4_0' -and $arenaNew.Previous.Previous.Previous.OpCode.Code -eq 'Ldarg_1') 'ordinary transfer preserves count and destroys zero items'
    Require ($arenaNew.Next.OpCode.Code -eq 'Stfld' -and $arenaNew.Next.Operand.Name -eq '_itemsCount') 'zero-destroy count stored for enumeration'
    $arenaRequired = Method 'EFT.InventoryLogic.ChangeDiscardLimitsResult' 'get_ItemsDestroyRequired' ''
    Require ((@($arenaRequired.Body.Instructions | Where-Object { $_.OpCode.Code -ne 'Nop' } | ForEach-Object { $_.OpCode.Code }) -join ',') -eq 'Ldc_I4_0,Ret') 'bookkeeping requires no destruction'
    $arenaEnumerate = Method 'EFT.InventoryLogic.ChangeDiscardLimitsResult/CG_get_ItemsToDestroy' 'MoveNext' ''
    $arenaLoads = @($arenaEnumerate.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldfld' -and $_.Operand.FullName -eq $arenaNew.Next.Operand.FullName })
    Require ($arenaLoads.Count -eq 1 -and $arenaLoads[0].Next.OpCode.Code -eq 'Stfld' -and $arenaLoads[0].Next.Operand.FieldType.FullName -eq 'EFT.InventoryLogic.ItemsCount') 'nonempty enumeration exposes the zero-destroy bookkeeping entry'
    Require (@($arenaEnumerate.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldc_I4_1' -and $_.Next.OpCode.Code -eq 'Ret' }).Count -eq 1) 'iterator returns true for its bookkeeping element'
    $arenaOut = Method 'EFT.InventoryLogic.ItemController' 'OutProcess' 'EFT.InventoryLogic.ItemController,EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemAddress,EFT.InventoryLogic.ItemAddress,EFT.InventoryLogic.Operations.IInventoryOperation,Comfort.Common.Callback'
    Require ($arenaOut.IsVirtual -and !$arenaOut.IsFinal) 'Arena source owner can override the exact source process'
    $arenaDispatch = Method 'EFT.InventoryLogic.ItemController' 'OutProcess' 'EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemAddress,EFT.InventoryLogic.ItemAddress,EFT.InventoryLogic.Operations.IInventoryOperation,Comfort.Common.Callback'
    Require ((Calls $arenaDispatch 'GetOwner').Count -eq 1 -and (Calls $arenaDispatch 'OutProcess').Count -eq 2) 'source owner receives native outbound admission'
    $arenaRun = Method 'EFT.InventoryLogic.ItemController' 'RunNetworkTransaction' 'EFT.InventoryLogic.IOperationResult,Comfort.Common.Callback'
    Require ((Calls $arenaRun 'ConvertOperationResultToOperation').Count -eq 1 -and (Calls $arenaRun 'Execute').Count -eq 1) 'normal transaction conversion and execution'
    $arenaSingle = AsyncBody (Method 'EFT.Player/SinglePlayerInventoryController' 'ExecuteAsync' 'EFT.InventoryLogic.Operations.AbstractOperation,Comfort.Common.Callback')
    Require ((Calls $arenaSingle 'Yield').Count -eq 1) 'local transaction yields before execution; queued admission alone is insufficient'
    $arenaMove = AsyncBody (Method 'EFT.InventoryLogic.Operations.MoveOperation' 'ExecuteInternal' '')
    $arenaNames = @($arenaMove.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
    Require ($arenaNames.IndexOf('OutProcess') -ge 0 -and $arenaNames.IndexOf('Execute') -gt $arenaNames.IndexOf('OutProcess') -and $arenaNames.IndexOf('InProcess') -gt $arenaNames.IndexOf('Execute')) 'native source check precedes move and inbound processing'
    Require ((Calls $arenaMove 'Yield').Count -eq 0 -and (Calls $arenaMove 'Delay').Count -eq 0) 'no unconditional yield between source check and native move'
    $arenaSetHands = Method 'EFT.Player' 'TrySetInHands' 'EFT.InventoryLogic.Item,EFT.InventoryLogic.ItemAddress,EFT.InventoryLogic.Operations.IInventoryOperation,Comfort.Common.Callback'
    Require ((Calls $arenaSetHands 'get_ActiveSlot').Count -eq 1 -and (Calls $arenaSetHands 'TryProceed').Count -eq 1) 'active-slot auto-draw requires excluding even an empty active slot'
    $arenaFinalize = Method 'EFT.InventoryLogic.Operations.AbstractAsyncOperation`1' 'FinalizeOperation' ''
    Require ((Calls $arenaFinalize 'RollBack').Count -eq 1 -and (Calls $arenaFinalize 'RaiseEvents').Count -eq 2) 'final native status chooses rollback or final events'
    $arenaRemove = Method 'EFT.Interactive.LootItem' 'RemoveLootItem' 'EFT.InventoryLogic.RemoveItemEventArgs'
    Require ((Calls $arenaRemove 'DestroyLoot').Count -eq 1) 'successful source removal destroys the native loot representation'
    Require (@($arenaRemove.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.TypeReference] -and $_.Operand.FullName -eq 'EFT.InventoryLogic.OwnerItself' }).Count -eq 1) 'loot removal is scoped to source-root removal'
    $arenaKill = Method 'EFT.Interactive.LootItem' 'Kill' ''
    Require ((Calls $arenaKill 'ReturnToPool').Count -eq 1 -and (Calls $arenaKill 'remove_RemoveItemEvent').Count -eq 1) 'loot model is pooled and unregisters its removal handler'
    $arenaCreate = Method 'EFT.GameWorld' 'CreateLootWithRigidbody' 'UnityEngine.GameObject,EFT.InventoryLogic.Item,System.String,System.Boolean,EFT.MongoID[],UnityEngine.BoxCollider&,System.Boolean,System.Boolean,System.Single'
    Require ($arenaCreate.ReturnType.FullName -eq 'EFT.Interactive.LootItem') 'native loot return type'
    Write-Output "ARENA WEAPON INSTALLED CONTRACTS PASSED: $script:arenaChecks assertions"
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $arenaBinary -Algorithm SHA256).Hash)
}
finally { $arenaAsm.Dispose() }
