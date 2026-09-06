param([string]$SptPath='D:\SPT41')
$ErrorActionPreference='Stop'
$descriptionRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$descriptionDatabase=Join-Path $SptPath 'SPT_Runtime/SPT_Data/database/templates/items.json'
$descriptionProject=Join-Path $PSScriptRoot 'KitPolishChecks.csproj'
$descriptionSourcePath=Join-Path $descriptionRoot 'ArenaMode.Client/Kits/KitSummary.cs'
$descriptionSource=[IO.File]::ReadAllText($descriptionSourcePath)
$descriptionHash=(Get-FileHash -LiteralPath $descriptionSourcePath).Hash

# Pin the real boundary that the former all-ArmorTemplate stand-in missed.
# Read IL only: no Unity/game initialization and no native assets are exported.
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$descriptionAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $vest=$descriptionAssembly.MainModule.GetType('EFT.InventoryLogic.VestTemplate')
    $getter=@($vest.Methods | Where-Object Name -eq 'get_ArmorPlateColliders')
    if($getter.Count -ne 1){throw 'Native vest mask getter changed.'}
    $ops=($getter[0].Body.Instructions | ForEach-Object {$_.OpCode.Name}) -join ','
    $expected='ldc.i4.0,stloc.0,ldarg.0,ldfld,stloc.1,ldc.i4.0,stloc.2,br.s,ldloc.1,ldloc.2,ldelem.i2,stloc.3,ldloc.0,ldloc.3,or,stloc.0,ldloc.2,ldc.i4.1,add,stloc.2,ldloc.2,ldloc.1,ldlen,conv.i4,blt.s,ldloc.0,ret'
    if($ops -ne $expected){throw 'Native vest getter changed; re-review null-array behavior.'}
    $field=@($vest.Fields | Where-Object Name -eq 'armorPlateColliders')
    if($field.Count -ne 1 -or !$field[0].IsPublic -or $field[0].FieldType.FullName -ne 'EArmorPlateCollider[]') {throw 'Native optional vest backing array changed.'}
    $load=@($getter[0].Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldfld'})
    if($load.Count -ne 1 -or $load[0].Operand.FullName -ne $field[0].FullName){throw 'Native mask no longer reads its optional backing array.'}
    $armored=$descriptionAssembly.MainModule.GetType('EFT.InventoryLogic.ArmoredEquipmentTemplate')
    $safe=@($armored.Methods | Where-Object Name -eq 'get_ArmorPlateColliders')[0]
    if($safe.Body.Instructions[4].OpCode.Name -ne 'brtrue.s' -or $safe.Body.Instructions[6].OpCode.Name -ne 'ret'){throw 'Native armored-equipment null fallback changed.'}
    $types=[IO.File]::ReadAllText((Join-Path $descriptionRoot '../_decompiled/EFT.InventoryLogic/JsonTypes.cs'))
    if($types -notmatch '"5448e5284bdc2dcb718b4567",\s*typeof\(VestTemplate\)'){throw 'Native vest template discriminator changed.'}
    Write-Output 'ARENA_DESCRIPTION_NATIVE_CONTRACTS_PASSED'
} finally {$descriptionAssembly.Dispose()}

& dotnet run --project $descriptionProject -c Release -- $descriptionRoot $descriptionDatabase
if($LASTEXITCODE -ne 0){throw 'Production descriptions failed.'}
$descriptionWork=Join-Path $PSScriptRoot '.work'
[void][IO.Directory]::CreateDirectory($descriptionWork)
$mutants=@(
    @{Name='UnsafeVestGetter';From='if (!(armour is VestTemplate vest)) return armour.ArmorPlateColliders;';To='if (armour != null) return armour.ArmorPlateColliders; if (!(armour is VestTemplate vest)) return armour.ArmorPlateColliders;';Failure='ASSERT: positive legacy soft vest with null plate array remains described'},
    @{Name='LostOtherDetails';From='other details retained.");';To='other details retained."); throw;';Failure='ASSERT: broken armor preserves all unrelated description sections'}
)
foreach($mutant in $mutants){
    if([regex]::Matches($descriptionSource,[regex]::Escape($mutant.From)).Count -ne 1){throw "Description mutation anchor drift: $($mutant.Name)"}
    $path=Join-Path $descriptionWork ($mutant.Name+'.cs')
    [IO.File]::WriteAllText($path,$descriptionSource.Replace($mutant.From,$mutant.To))
    $output=& dotnet run --project $descriptionProject -c ('DescriptionMutant'+$mutant.Name) "-p:SummarySource=$path" -- $descriptionRoot $descriptionDatabase 2>&1 | Out-String
    if($LASTEXITCODE -eq 0 -or !$output.Contains($mutant.Failure)){throw "Description mutation did not fail its intended runtime assertion: $($mutant.Name)`n$output"}
    Write-Output "Rejected description mutant: $($mutant.Name)"
}
if((Get-FileHash -LiteralPath $descriptionSourcePath).Hash -ne $descriptionHash){throw 'Production description source changed during verification.'}
# Recheck the existing front/side/soft distinction and actual supply placement,
# including its old max-armor and backpack-fallback negative controls.
& (Join-Path $PSScriptRoot 'Verify.ps1') -Database $descriptionDatabase
Write-Output 'ARENA_PRESET_DESCRIPTIONS_PASSED'
