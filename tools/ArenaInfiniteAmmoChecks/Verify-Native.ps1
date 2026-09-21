param([ValidateSet('Ammo','Backpack')][string]$Mode='Ammo',
    [string]$SptPath=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$binary=Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($binary)
$script:count=0
function Require([bool]$ok,[string]$why){$script:count++;if(!$ok){throw "Native $Mode contract drift: $why"}}
function Method([string]$type,[string]$name){$t=$assembly.MainModule.GetType($type);Require ($null -ne $t) $type;$m=@($t.Methods | Where-Object Name -EQ $name);Require ($m.Count -eq 1) "$type.$name";return $m[0]}
function Calls($method,[string]$name){return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name })}
try {
    if($Mode -eq 'Ammo') {
        $type='EFT.Player/FirearmController/FireOperationBase'
        $hook=Method $type 'ProcessInventoryOperationsForSingleShot'
        Require ($hook.ReturnType.FullName -eq "$type/SingleShotData" -and $hook.Parameters.Count -eq 1 -and $hook.Parameters[0].Name -eq 'malfState') 'shot hook signature'
        foreach($name in @('RemoveItem','PopTo','PopToNowhere','get_ChamberAmmoCount','GetCurrentMagazineCount')) { Require ((Calls $hook $name).Count -gt 0) "native consumption/counter $name" }
        $prepare=Method $type 'ProcessInventoryOperationsForSingleshotWithMalf'
        Require ((Calls $prepare 'ProcessInventoryOperationsForSingleShot').Count -eq 1) 'malfunction wrapper reaches hook'
        foreach($entry in @(@('FireOperation','PrepareShot'),@('AutomaticFireOperation','InternalOnFireEvent'))) {
            $m=Method ('EFT.Player/FirearmController/'+$entry[0]) $entry[1]
            Require ((Calls $m 'ProcessInventoryOperationsForSingleshotWithMalf').Count -eq 1) 'native semi/automatic shot route'
        }
        $clone=Method 'EFT.InventoryLogic.Ammo' 'Clone'
        Require ($clone.ReturnType.FullName -eq 'EFT.InventoryLogic.Ammo' -and ($clone.Parameters.ParameterType.FullName -join ',') -eq 'System.String') 'detached clone signature'
        $constructors=@($clone.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Newobj' -and $_.Operand.DeclaringType.FullName -eq 'EFT.InventoryLogic.Ammo' })
        Require ($constructors.Count -eq 1 -and (Calls $clone 'AddItem').Count -eq 0) 'clone creates ammo without inventory insertion'
        $nextId=Method 'EFT.ItemFactory' 'get_NextId'
        Require ($nextId.ReturnType.FullName -eq 'EFT.MongoID' -and (Calls $nextId 'Next').Count -eq 1) 'factory-local id source'
        $fields=$assembly.MainModule.GetType("$type/SingleShotData").Fields.Name
        foreach($name in @('AmmoToFire','AmmoWillBeLoadedToChamber','FedAmmo','AmmoCountInChamberBeforeShot','AmmoCountInChamberAfterShot','AmmoCountInMagBeforeShot','AmmoCountInMagAfterShot','IsAmmoCompatible')) { Require ($name -in $fields) "shot result $name" }
    } else {
        $type='EFT.InventoryLogic.InventoryController'
        $query=Method $type 'GetReachableItemsOfTypeNonAlloc'
        Require ($query.GenericParameters.Count -eq 1 -and $query.Parameters.Count -eq 2 -and $query.Parameters[0].Name -eq 'preAllocatedList' -and $query.Parameters[1].Name -eq 'predicate') 'generic magazine query signature'
        Require ((Calls $query 'GetAcceptableItemsNonAlloc').Count -eq 1) 'reachable uses native scanner'
        Require ((Method $type 'GetAcceptableItemsNonAlloc').IsVirtual) 'preserve player search/examination overrides'
        Require ((Calls (Method $type 'GetReachableItemsOfType') 'GetReachableItemsOfTypeNonAlloc').Count -eq 1) 'legacy menu wrapper reaches same query'
        Require ((Calls (Method 'EFT.FirearmHandsInputTranslator' 'ReloadExternalMagazine') 'GetReachableItemsOfTypeNonAlloc').Count -eq 1) 'R reload candidate seam'
        Require ((Calls (Method 'EFT.UI.AmmoSelector/CG_FindAmmoForWeapon' 'method_0') 'GetReachableItemsOfType').Count -eq 1) 'reload selection menu seam'
        Require ((Calls (Method 'EFT.UI.ItemUiContext' 'FindSuitableMagazine') 'GetReachableItemsOfType').Count -eq 1) 'inventory reload action seam'
        foreach($entry in @(@('EFT.FirearmHandsInputTranslator','ReloadExternalMagazine','GetReachableItemsOfTypeNonAlloc'),@('EFT.UI.AmmoSelector/CG_FindAmmoForWeapon','method_0','GetReachableItemsOfType'),@('EFT.UI.ItemUiContext','FindSuitableMagazine','GetReachableItemsOfType'))) {
            $consumer=Method $entry[0] $entry[1]
            Require ($consumer.GenericParameters.Count -eq 0 -and $consumer.DeclaringType.GenericParameters.Count -eq 0) 'non-generic consumer target'
            $call=@(Calls $consumer $entry[2])
            Require ($call.Count -eq 1 -and $call[0].Operand -is [Mono.Cecil.GenericInstanceMethod] -and $call[0].Operand.DeclaringType.FullName -eq $type -and $call[0].Operand.GenericArguments.Count -eq 1 -and $call[0].Operand.GenericArguments[0].FullName -eq 'EFT.InventoryLogic.Magazine') 'exact typed magazine callsite'
        }
        $grenades=@(Calls (Method 'EFT.PlayerInputTranslator' 'vmethod_1') 'GetReachableItemsOfTypeNonAlloc')
        Require ($grenades.Count -eq 1 -and $grenades[0].Operand.GenericArguments[0].FullName -eq 'EFT.InventoryLogic.ThrowWeap') 'G remains separate native grenade query'
    }
    Write-Output "ARENA $Mode NATIVE CONTRACTS PASSED: $script:count assertions; no Unity execution"
    Write-Output "Assembly-CSharp SHA256=$((Get-FileHash -LiteralPath $binary).Hash)"
} finally { $assembly.Dispose() }
