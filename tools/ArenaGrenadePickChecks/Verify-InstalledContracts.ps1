#requires -Version 7.0
# Pins the native members, signatures and calls the grenade-pick model relies
# on against the INSTALLED Assembly-CSharp (read with Mono.Cecil, nothing runs).
param([string] $SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$gamePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$mod = $null

function Require-Type($assembly, [string] $name) {
    $type = $assembly.MainModule.GetType($name)
    if ($null -eq $type) { throw "Missing native type $name" }
    return $type
}
function Require-Method($assembly, [string] $typeName, [string] $name, [string] $arguments, [string] $returns) {
    $type = Require-Type $assembly $typeName
    $methods = @($type.Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $arguments
    })
    if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne $returns) {
        throw "Native method contract drift: $typeName.$name($arguments)"
    }
    return $methods[0]
}
function Get-Calls($method) {
    return @($method.Body.Instructions | Where-Object { $_.OpCode.Code.ToString() -in @('Call', 'Callvirt', 'Newobj') } |
        ForEach-Object { $_.Operand.DeclaringType.FullName + '::' + $_.Operand.Name })
}
function Require-Call($method, [string] $callee) {
    if ((Get-Calls $method) -notcontains $callee) { throw "Native flow drift: $($method.FullName) no longer calls $callee" }
}
function Require-FieldOp($method, [string] $opcode, [string] $field) {
    $hits = @($method.Body.Instructions | Where-Object {
        $_.OpCode.Code.ToString() -eq $opcode -and $_.Operand.FullName -eq $field })
    if ($hits.Count -eq 0) { throw "Native flow drift: $($method.FullName) no longer uses $opcode $field" }
}
function Require-Field($type, [string] $name, [string] $fieldType, [bool] $public) {
    $fields = @($type.Fields | Where-Object { $_.Name -eq $name -and -not $_.IsStatic })
    if ($fields.Count -ne 1 -or $fields[0].FieldType.FullName -ne $fieldType -or $fields[0].IsPublic -ne $public) {
        throw "Native field drift: $($type.FullName).$name"
    }
}
function Require-SingleName($type, [string] $name) {
    if (@($type.Methods | Where-Object { $_.Name -eq $name }).Count -ne 1) {
        throw "$($type.FullName).$name is no longer a single method; AccessTools.Method by name would be ambiguous"
    }
}
function Get-Int($instruction) {
    switch ($instruction.OpCode.Code.ToString()) {
        'Ldc_I4_M1' { return -1 }
        'Ldc_I4_S' { return [int][sbyte]$instruction.Operand }
        'Ldc_I4' { return [int]$instruction.Operand }
        default {
            if ($_ -match '^Ldc_I4_(\d)$') { return [int]$Matches[1] }
            return $null
        }
    }
}
function Get-SlotNames {
    $names = @{}
    foreach ($field in (Require-Type $game 'EFT.InventoryLogic.EquipmentSlot').Fields) {
        if ($field.HasConstant) { $names[[int]$field.Constant] = $field.Name }
    }
    return $names
}
# The EquipmentSlot constants a static array field is initialised with, from its .cctor.
function Get-StaticSlotArray($type, [string] $fieldName, $slotNames) {
    $cctor = @($type.Methods | Where-Object { $_.Name -eq '.cctor' })[0]
    $instructions = @($cctor.Body.Instructions)
    $store = [array]::FindIndex($instructions, [Predicate[object]] {
        param($i) $i.OpCode.Code.ToString() -eq 'Stsfld' -and $i.Operand.Name -eq $fieldName })
    if ($store -lt 0) { throw "No initialiser for $($type.FullName).$fieldName" }
    $start = $store
    while ($start -ge 0 -and $instructions[$start].OpCode.Code.ToString() -ne 'Newarr') { $start-- }
    $values = @()
    $token = @($instructions[$start..$store] | Where-Object { $_.OpCode.Code.ToString() -eq 'Ldtoken' })
    if ($token.Count -eq 1) {
        $bytes = $token[0].Operand.Resolve().InitialValue
        for ($o = 0; $o -lt $bytes.Length; $o += 4) { $values += [BitConverter]::ToInt32($bytes, $o) }
    }
    else {
        for ($k = $start; $k -lt $store; $k++) {
            if ($instructions[$k].OpCode.Code.ToString() -eq 'Stelem_I4') { $values += (Get-Int $instructions[$k - 1]) }
        }
    }
    return @($values | ForEach-Object { $slotNames[$_] })
}
# The EquipmentSlot constants passed to GetSlot, in call order.
function Get-GetSlotArgs($method, $slotNames) {
    $instructions = @($method.Body.Instructions)
    $result = @()
    for ($k = 1; $k -lt $instructions.Count; $k++) {
        $i = $instructions[$k]
        if ($i.OpCode.Code.ToString() -in @('Call', 'Callvirt') -and $i.Operand.Name -eq 'GetSlot' -and
            $i.Operand.DeclaringType.FullName -eq 'EFT.InventoryLogic.InventoryEquipment') {
            $result += $slotNames[(Get-Int $instructions[$k - 1])]
        }
    }
    return $result
}
function Require-List($actual, [string[]] $expected, [string] $what) {
    if (($actual -join ',') -ne ($expected -join ',')) { throw "$what drift: $($actual -join ',')" }
}

try {
    $inv = 'EFT.InventoryLogic'
    $view = Require-Type $game 'EFT.UI.DragAndDrop.FastAccessGrenadeItemView'
    if ($view.BaseType.FullName -ne 'EFT.UI.DragAndDrop.QuickSlotView') { throw 'The quick-throw view no longer derives from QuickSlotView' }

    # The postfix target, bound by Harmony through the parameter NAME obj.
    $removed = Require-Method $game $view.FullName 'OnItemRemoved' "$inv.RemoveItemEventArgs" 'System.Void'
    if (-not $removed.IsPublic -or $removed.Parameters[0].Name -ne 'obj') { throw 'OnItemRemoved signature/parameter name drift' }
    Require-SingleName $view 'OnItemRemoved'
    $show = Require-Method $game $view.FullName 'Show' "$inv.InventoryController,EFT.UI.ItemUiContext" 'System.Void'
    $subscribes = @($show.Body.Instructions | Where-Object { $_.OpCode.Code.ToString() -eq 'Ldftn' -and $_.Operand.Name -eq 'OnItemRemoved' })
    $adds = @($show.Body.Instructions | Where-Object { $_.OpCode.Code.ToString() -in @('Call', 'Callvirt') -and $_.Operand.Name -eq 'add_RemoveItemEvent' })
    if ($subscribes.Count -ne 1 -or $adds.Count -ne 1) { throw 'Show no longer subscribes OnItemRemoved to RemoveItemEvent' }
    Require-Call $removed "$inv.InventoryEquipment::get_GrenadeThrowingSlots"
    Require-Call $removed "$view::SetNewTopPriorityGrenade"
    Require-Call $removed "$view::set_TopPriorityGrenade"
    $setNew = Require-Method $game $view.FullName 'SetNewTopPriorityGrenade' '' 'System.Void'
    if (-not $setNew.IsPublic) { throw 'SetNewTopPriorityGrenade is no longer public' }
    Require-Call $setNew "$view::set_TopPriorityGrenade"
    $inHands = @($view.Methods | Where-Object { $_.Name -like '*OnSetInHands' })
    if ($inHands.Count -ne 1) { throw 'The view no longer has exactly one OnSetInHands' }
    Require-Call $inHands[0] "$view::set_TopPriorityGrenade"
    $getter = Require-Method $game $view.FullName 'get_TopPriorityGrenade' '' "$inv.ThrowWeap"
    Require-FieldOp $getter 'Ldfld' "$inv.ThrowWeap $inv.InventoryEquipment::TopPriorityGrenade"

    # ___InventoryController: Harmony binds the nearest field of that name up the hierarchy.
    if (@($view.Fields | Where-Object { $_.Name -eq 'InventoryController' }).Count -ne 0) {
        throw 'The view now declares its own InventoryController field; the injected one would change'
    }
    $quick = Require-Type $game 'EFT.UI.DragAndDrop.QuickSlotView'
    Require-Field $quick 'InventoryController' "$inv.InventoryController" $false

    # The pick itself, and what a grenade is.
    Require-Field (Require-Type $game "$inv.InventoryEquipment") 'TopPriorityGrenade' "$inv.ThrowWeap" $true
    if ((Require-Type $game "$inv.ThrowWeap").BaseType.FullName -ne "$inv.Item") { throw 'ThrowWeap is no longer a plain Item' }

    # The event: owner and status as the view reads them; one try around the multicast.
    $eventArgs = Require-Type $game "$inv.ItemEventArgs"
    Require-Field $eventArgs 'OwnerId' 'System.String' $true
    Require-Field $eventArgs 'Status' "$inv.CommandStatus" $true
    Require-Field $eventArgs 'Item' "$inv.Item" $true
    if ((Require-Type $game "$inv.RemoveItemEventArgs").BaseType.FullName -ne "$inv.ItemEventArgs") { throw 'RemoveItemEventArgs base drift' }
    $safe = Require-Method $game "$inv.ItemController" 'SafeRemoveItemEvent' "$inv.RemoveItemEventArgs" 'System.Void'
    if ($safe.Body.ExceptionHandlers.Count -ne 1) { throw 'SafeRemoveItemEvent no longer wraps the multicast in one handler' }
    Require-Call $safe "System.Action``1<$inv.RemoveItemEventArgs>::Invoke"
    $raise = Require-Method $game "$inv.ItemController" 'RaiseRemoveEvent' "$inv.RemoveItemEventArgs" 'System.Void'
    Require-Call $raise "$inv.ItemController::SafeRemoveItemEvent"
    $controller = Require-Type $game "$inv.ItemController"
    $id = @($controller.Properties | Where-Object { $_.Name -eq 'ID' })
    if ($id.Count -ne 1 -or $id[0].PropertyType.FullName -ne 'System.String') { throw 'ItemController.ID drift' }

    # The throw raises the removal after the discard (the address is gone by Succeed).
    $throw = Require-Method $game 'EFT.Player/BaseGrenadeHandsController' 'ThrowGrenade' 'System.Single,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Vector3,System.Boolean' 'System.Void'
    Require-Call $throw 'EFT.Player::ThrowGrenade'
    Require-Call $throw 'EFT.Player/GrenadeThrowResult::RaiseEvents'
    $discard = Require-Method $game 'EFT.Player' 'ThrowGrenade' "$inv.ThrowWeap,System.Boolean,System.Boolean" 'Diz.LanguageExtensions.OperationResult`1<EFT.Player/GrenadeThrowResult>'
    Require-Call $discard "$inv.ItemManipulator::Discard"

    # The key: pick first whenever G has a candidate, through SetItemInHands.
    $translator = Require-Type $game 'EFT.PlayerInputTranslator'
    Require-Field $translator '_player' 'EFT.Player' $true
    $tap = Require-Method $game 'EFT.PlayerInputTranslator' 'vmethod_1' '' 'System.Void'
    if (-not ($tap.IsPublic -and $tap.IsVirtual)) { throw 'vmethod_1 is no longer public virtual' }
    Require-SingleName $translator 'vmethod_1'
    Require-FieldOp $tap 'Ldfld' "$inv.ThrowWeap $inv.InventoryEquipment::TopPriorityGrenade"
    Require-Call $tap 'EFT.Player::SetItemInHands'
    Require-Call $tap "$inv.InventoryController::GetReachableItemsOfTypeNonAlloc"
    $translate = Require-Method $game 'EFT.PlayerInputTranslator' 'TranslateCommand' 'EFT.InputSystem.ECommand' 'EFT.InputSystem.InputNode/ETranslateResult'
    Require-Call $translate 'EFT.PlayerInputTranslator::vmethod_1'
    $overrides = @($game.MainModule.GetTypes() | Where-Object {
        $t = $_.BaseType; $derived = $false
        while ($t -ne $null) { if ($t.FullName -eq 'EFT.PlayerInputTranslator') { $derived = $true; break }; try { $t = $t.Resolve().BaseType } catch { $t = $null } }
        $derived -and @($_.Methods | Where-Object { $_.Name -eq 'vmethod_1' }).Count -gt 0
    } | ForEach-Object { $_.FullName })
    if (($overrides -join ',') -ne 'EFT.HideoutPlayerInputTranslator') { throw "Unexpected vmethod_1 overrides: $($overrides -join ',')" }

    # The refusal and fallback the stale pick runs into.
    $reach = Require-Method $game "$inv.InventoryController" 'IsAtReachablePlace' "$inv.Item" 'System.Boolean'
    if ($reach.Parameters[0].Name -ne 'item') { throw 'IsAtReachablePlace parameter name drift' }
    # An addressless (thrown) item is refused before anything else is asked.
    $head = @($reach.Body.Instructions | Select-Object -First 5 | ForEach-Object { $_.OpCode.Code.ToString() })
    if (($head[0] -ne 'Ldarg_1') -or ($reach.Body.Instructions[1].Operand.Name -ne 'get_CurrentAddress') -or
        ($head[2] -notin @('Brtrue', 'Brtrue_S')) -or ($head[3] -ne 'Ldc_I4_0') -or ($head[4] -ne 'Ret')) {
        throw "IsAtReachablePlace no longer opens with the null-address refusal: $($head -join ',')"
    }
    $bind = Require-Method $game "$inv.InventoryController" 'IsAtBindablePlace' "$inv.Item" 'System.Boolean'
    if ($bind.Parameters[0].Name -ne 'item') { throw 'IsAtBindablePlace parameter name drift' }

    # The slot lists the model uses, as shipped (UseItemsAnywhere's own are checked below).
    $slotNames = Get-SlotNames
    $inventoryType = Require-Type $game "$inv.Inventory"
    Require-List (Get-StaticSlotArray $inventoryType 'FastAccessSlots' $slotNames) @('Pockets', 'TacticalVest') 'Inventory.FastAccessSlots'
    Require-List (Get-StaticSlotArray $inventoryType 'BindAvailableSlotsExtended' $slotNames) `
        @('FirstPrimaryWeapon', 'SecondPrimaryWeapon', 'Holster', 'Scabbard', 'Pockets', 'TacticalVest') 'Inventory.BindAvailableSlotsExtended'
    $throwing = @((Require-Type $game "$inv.InventoryEquipment").Methods | Where-Object { $_.Name -eq 'get_GrenadeThrowingSlots' })
    if ($throwing.Count -ne 1) { throw 'GrenadeThrowingSlots getter drift' }
    Require-List (Get-GetSlotArgs $throwing[0] $slotNames) @('TacticalVest', 'Pockets') 'InventoryEquipment.GrenadeThrowingSlots'
    $try = @((Require-Type $game 'EFT.Player').Methods | Where-Object {
        $_.Name -eq 'TryProceed' -and $_.Parameters.Count -eq 3 -and $_.Parameters[0].ParameterType.FullName -eq "$inv.Item" })
    if ($try.Count -ne 1) { throw 'Player.TryProceed(Item, callback, bool) drift' }
    Require-Call $try[0] "$inv.InventoryController::IsAtReachablePlace"
    Require-Call $try[0] 'EFT.Player::SetFirstAvailableItem'
    $check = Require-Method $game "$inv.Item" 'CheckAction' "$inv.ItemAddress" 'Diz.LanguageExtensions.Option'
    Require-Call $check "$inv.Item::get_CurrentAddress"

    # The user's third-party install, for the documented slot lists.
    $modPath = Join-Path $SptPath 'BepInEx/plugins/UseItemsFromAnywhere.dll'
    if (Test-Path -LiteralPath $modPath) {
        $mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($modPath)
        if ($mod.Name.Version.ToString() -ne '1.4.0.0') { throw "UseItemsAnywhere is $($mod.Name.Version), the model is 1.4.0" }
        foreach ($patch in @('UseItemsAnywhere.Patches.GrenadeThrowingSlotsPatch', 'UseItemsAnywhere.Patches.IsAtReachablePlace',
                             'UseItemsAnywhere.Patches.IsAtBindablePlace', 'UseItemsAnywhere.Patches.GetThrowablePriorityGrenadesListPatch')) {
            if ($null -eq $mod.MainModule.GetType($patch)) { throw "Installed UseItemsAnywhere lacks $patch" }
        }
        $plugin = Require-Type $mod 'UseItemsAnywhere.Plugin'
        Require-List (Get-StaticSlotArray $plugin 'ExtendedFastAccessSlots' $slotNames) `
            @('Pockets', 'TacticalVest', 'Backpack', 'SecuredContainer', 'ArmBand') 'UseItemsAnywhere FastAccessSlots'
        $awake = @($plugin.Methods | Where-Object { $_.Name -eq 'Awake' })[0]
        if (@($awake.Body.Instructions | Where-Object { $_.OpCode.Code.ToString() -eq 'Ldstr' -and $_.Operand -eq 'FastAccessSlots' }).Count -ne 1) {
            throw 'UseItemsAnywhere no longer rewrites Inventory.FastAccessSlots'
        }
        Require-Call $awake 'System.Reflection.FieldInfo::SetValue'
        $slotsPrefix = @((Require-Type $mod 'UseItemsAnywhere.Patches.GrenadeThrowingSlotsPatch').Methods | Where-Object { $_.Name -eq 'PatchPrefix' })[0]
        Require-List (Get-GetSlotArgs $slotsPrefix $slotNames) @('Backpack', 'TacticalVest', 'Pockets', 'SecuredContainer', 'ArmBand') `
            'UseItemsAnywhere GrenadeThrowingSlots'
        Write-Output ('UseItemsAnywhere ' + $mod.Name.Version + ' SHA256 ' + (Get-FileHash -LiteralPath $modPath -Algorithm SHA256).Hash)
    }
    else {
        Write-Output 'UseItemsAnywhere not installed (vanilla slot lists apply)'
    }
    Write-Output ('Native game SHA256 ' + (Get-FileHash -LiteralPath $gamePath -Algorithm SHA256).Hash)
    Write-Output 'ARENA_GRENADE_PICK_INSTALLED_CONTRACTS_PASSED'
}
finally {
    $game.Dispose()
    if ($mod) { $mod.Dispose() }
}
