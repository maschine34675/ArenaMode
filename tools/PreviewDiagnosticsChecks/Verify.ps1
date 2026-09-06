param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
# Inspect the installed assembly without loading Unity or running game code.
# This checks the asynchronous completion boundary, not merely a method name.
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$previewAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
    (Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $preview = $previewAssembly.MainModule.GetType('EFT.UI.WeaponModding.WeaponPreview')
    if ($null -eq $preview) { throw 'Native preview type missing' }
    foreach ($field in @('_originalObject', '_currentItem')) {
        if (@($preview.Fields | Where-Object Name -eq $field).Count -ne 1) { throw "Missing preview field $field" }
    }
    $callback = @($preview.NestedTypes | Where-Object Name -eq 'CG_SetupItemPreview')
    if ($callback.Count -ne 1 -or !$callback[0].IsNestedPublic) { throw 'Preview callback contract changed' }
    $owner = @($callback[0].Fields | Where-Object Name -eq 'weaponPreview_0')
    $item = @($callback[0].Fields | Where-Object Name -eq 'item')
    if ($owner.Count -ne 1 -or !$owner[0].IsPublic -or $owner[0].FieldType.FullName -ne $preview.FullName -or
        $item.Count -ne 1 -or !$item[0].IsPublic -or $item[0].FieldType.FullName -ne 'EFT.InventoryLogic.Item') {
        throw 'Preview completion owner/item contract changed'
    }
    $ready = @($callback[0].Methods | Where-Object Name -eq 'method_1')
    if ($ready.Count -ne 1 -or !$ready[0].HasBody -or $ready[0].ReturnType.FullName -ne 'System.Void') {
        throw 'Preview completion method changed'
    }
    $calls = @($ready[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]})
    $create = @($calls | Where-Object {$_.Operand.Name -eq 'CreateCleanLootPrefab'})
    $position = @($calls | Where-Object {$_.Operand.Name -eq 'PositionGameObject'})
    $layer = @($calls | Where-Object {$_.Operand.Name -eq 'SetLayersRecursively'})
    if ($create.Count -ne 1 -or $position.Count -ne 1 -or $layer.Count -ne 1 -or
        $create[0].Offset -ge $position[0].Offset -or $position[0].Offset -ge $layer[0].Offset) {
        throw 'Callback no longer finishes creation, parenting and preview layer assignment in order'
    }
    if (@($calls | Where-Object {$_.Operand.Name -match 'Await|WaitForAllBundles'}).Count -gt 0) {
        throw 'Completion now contains deferred work; re-review snapshot timing'
    }
    Write-Output 'ARENA_PREVIEW_DIAGNOSTICS_VERIFIED'
} finally { $previewAssembly.Dispose() }
