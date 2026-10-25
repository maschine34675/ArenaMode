param(
    [string]$SptPath=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))),
    [string]$ForeignDll=(Join-Path $PSScriptRoot '../../artifacts/compat-seven-20261025/items/UseItemsAnywhere-release/BepInEx/plugins/UseItemsAnywhere/UseItemsAnywhere.dll')
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot '../ArenaInfiniteAmmoChecks/Verify-Native.ps1') -Mode Backpack -SptPath $SptPath
if (!$?) { throw 'Native magazine contract verification failed' }
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$hash=(Get-FileHash -LiteralPath $ForeignDll -Algorithm SHA256).Hash
if ($hash -ne '51B9CB02DFDC69568299988EAC37BD6C8F5732D49FE1E04DAB8D6592F87F2346') { throw 'UIA fixture differs from the reviewed 2.1.6 binary' }
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($ForeignDll)
$script:checks=0
function Require([bool]$condition,[string]$reason) { $script:checks++; if(!$condition){throw "UIA binary contract drift: $reason"} }
try {
    Require ($assembly.Name.Name -eq 'UseItemsAnywhere') 'assembly identity'
    $plugin=$assembly.MainModule.GetType('UseItemsAnywhere.Plugin')
    $metadata=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })
    Require ($metadata.Count -eq 1 -and $metadata[0].ConstructorArguments[0].Value -ceq 'com.cj.useFromAnywhere' -and $metadata[0].ConstructorArguments[2].Value -eq '2.1.6') 'plugin GUID and version'
    $queries=$assembly.MainModule.GetType('UseItemsAnywhere.Patches.InventorySlotQueries')
    foreach($entry in @(@('CollectReloadItems','System.Void',3),@('ReloadItems','System.Collections.Generic.IEnumerable`1<TItem>',2))) {
        $method=@($queries.Methods | Where-Object Name -EQ $entry[0])
        Require ($method.Count -eq 1 -and $method[0].IsStatic -and $method[0].IsAssembly -and $method[0].GenericParameters.Count -eq 1) "$($entry[0]) method shape"
        Require ($method[0].ReturnType.FullName -eq $entry[1] -and $method[0].Parameters.Count -eq $entry[2]) "$($entry[0]) signature"
        Require ($method[0].Parameters[0].ParameterType.FullName -eq 'EFT.InventoryLogic.InventoryController') "$($entry[0]) receiver"
        Require ($method[0].GenericParameters[0].Constraints[0].FullName -eq 'EFT.InventoryLogic.Item') "$($entry[0]) item constraint"
    }
    $patches=$assembly.MainModule.GetType('UseItemsAnywhere.Patches.InventoryQueryPatches')
    $cctor=@($patches.Methods | Where-Object Name -EQ '.cctor')[0]
    $strings=@($cctor.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' } | ForEach-Object Operand)
    foreach($target in @('EFT.FirearmHandsInputTranslator','ReloadExternalMagazine','EFT.UI.ItemUiContext','FindSuitableMagazine','EFT.UI.AmmoSelector+CG_FindAmmoForWeapon','method_0')) {
        Require ($strings -contains $target) "reviewed target $target"
    }
    $transpiler=@($patches.Methods | Where-Object Name -EQ 'Transpiler')[0]
    $messages=@($transpiler.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' } | ForEach-Object Operand)
    Require (($messages -join ' ') -match 'Expected one inventory query') 'foreign exact-one requirement remains present'
    Write-Output "USE_ITEMS_ANYWHERE_NATIVE_CONTRACT_PASSED: $script:checks checks; reviewed binary SHA256=$hash"
} finally { $assembly.Dispose() }
