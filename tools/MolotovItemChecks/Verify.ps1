param([string]$Database = 'D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json')
$ErrorActionPreference = 'Stop'
$itemRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$itemProject = Join-Path $PSScriptRoot 'Supplies/MolotovItemChecks.csproj'
$itemStage = Join-Path $itemRoot ('artifacts/molotov-item-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($itemStage)
$itemRegistration = Join-Path $itemRoot 'ArenaMode.Server/Services/ArenaMolotovRegistration.cs'
$itemSupplies = Join-Path $itemRoot 'ArenaMode.Server/Services/ArenaKitSupplies.cs'

function Run-ItemCheck([string]$name, [string]$registration, [string]$supplies, [string]$expectedFailure = '') {
    $dir = Join-Path $itemStage $name
    [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build $itemProject -c Release --nologo "-p:RegistrationSource=$registration" "-p:SuppliesSource=$supplies" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "Item check $name must compile: $build" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet (Join-Path $dir 'out/MolotovItemChecks.dll') $itemRoot $Database 2>&1 | Out-String
        $result = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $dir 'run.log'), $output)
    if ($expectedFailure) {
        if ($result -eq 0 -or !$output.Contains('ASSERT: ' + $expectedFailure)) { throw "Fault $name missed intended assertion: $output" }
        Write-Output "Rejected $name at $expectedFailure"
    } else {
        if ($result -ne 0 -or !$output.Contains('KIT_SUPPLIES_ASSERTIONS_PASSED:')) { throw "Production item check failed: $output" }
        Write-Output $output.Trim()
    }
}

Run-ItemCheck 'production' $itemRegistration $itemSupplies
$itemFaults = @(
    @{ name='global-handbook'; source=$itemRegistration; find='AddToHandbook = false'; replace='AddToHandbook = true'; failure='no global handbook/flea/shelf entry' },
    @{ name='unsafe-publication'; source=$itemRegistration; find='|| !IsHarmless(registered)'; replace=''; failure='failed registration does not authorize supply ignored-overrides' },
    @{ name='partial-rollback'; source=$itemRegistration; find='items.Remove(id);'; replace='// missing rollback'; failure='failed registration leaves no partial explosive clone throw-after' },
    @{ name='foreign-id'; source=$itemRegistration; find='ReferenceEquals(occupied, RegisteredTemplate) && IsHarmless(occupied)'; replace='IsHarmless(occupied)'; failure='foreign ID collision stays untouched' },
    @{ name='unregistered-supply'; source=$itemSupplies; find='_molotovAvailable && includeMolotov && !existing.Any(tpl => _types[tpl].HasValue)'; replace='includeMolotov && !existing.Any(tpl => _types[tpl].HasValue)'; failure='missing registration retains two historical frags only' },
    @{ name='authored-throwable'; source=$itemSupplies; find='_molotovAvailable && includeMolotov && !existing.Any(tpl => _types[tpl].HasValue)'; replace='_molotovAvailable && includeMolotov'; failure='authored throwable never gains a Molotov' },
    @{ name='unsupported-mode'; source=$itemSupplies; find='_molotovAvailable && includeMolotov && !existing.Any(tpl => _types[tpl].HasValue)'; replace='_molotovAvailable && !existing.Any(tpl => _types[tpl].HasValue)'; failure='unsupported mode retains two frags without Molotov' },
    @{ name='optional-slot-policy'; source=$itemSupplies; find='["tpl"] = ArenaMolotovItem.TemplateId, ["count"] = 1, ["optionalQuickUseOnly"] = true'; replace='["tpl"] = ArenaMolotovItem.TemplateId, ["count"] = 1, ["optionalQuickUseOnly"] = false'; failure='every optional request remains single and quick-use only' }
)
foreach ($fault in $itemFaults) {
    $original = [IO.File]::ReadAllText($fault.source)
    if ($original.Split(@($fault.find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous item fault anchor $($fault.name)" }
    $mutant = Join-Path $itemStage ($fault.name + '.cs')
    [IO.File]::WriteAllText($mutant, $original.Replace($fault.find, $fault.replace))
    if ($fault.source -eq $itemRegistration) { Run-ItemCheck $fault.name $mutant $itemSupplies $fault.failure }
    else { Run-ItemCheck $fault.name $itemRegistration $mutant $fault.failure }
}
Run-ItemCheck 'production-recheck' $itemRegistration $itemSupplies

# Historical 1605-assertion production packing suite remains runnable without a
# registered Molotov. It has no deployment target or project reference to the mod.
& dotnet run --project (Join-Path $itemRoot 'tools/KitPolishChecks/Supplies/SuppliesChecks.csproj') -c Release -- $itemRoot $Database
if ($LASTEXITCODE -ne 0) { throw 'Historical kit-supplies regression failed.' }
$itemBuilder = Join-Path $itemRoot 'ArenaMode.Client/Kits/KitBuilder.cs'
$itemBuilderSource = [IO.File]::ReadAllText($itemBuilder)
foreach ($fault in @(
    @{ name='legacy-backpack'; find='entry.OptionalQuickUseOnly ? grenadeContainers : containers'; replace='containers'; failure='optional supplies never spill to backpack' },
    @{ name='legacy-patched-getter'; find='new[] { equipment.GetSlot(EquipmentSlot.TacticalVest), equipment.GetSlot(EquipmentSlot.Pockets) }'; replace='equipment.GrenadeThrowingSlots'; failure='patched quick-throw list cannot redirect grenades to backpack' }
)) {
    if ($itemBuilderSource.Split(@($fault.find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous legacy fault anchor $($fault.name)" }
    $mutant = Join-Path $itemStage ($fault.name + '.cs')
    [IO.File]::WriteAllText($mutant, $itemBuilderSource.Replace($fault.find, $fault.replace))
    $dir = Join-Path $itemStage $fault.name
    $build = & dotnet build (Join-Path $itemRoot 'tools/KitPolishChecks/Supplies/SuppliesChecks.csproj') -c Release --nologo "-p:BuilderSource=$mutant" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Legacy fault must compile: $build" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet (Join-Path $dir 'out/SuppliesChecks.dll') $itemRoot $Database 2>&1 | Out-String
        $result = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    [IO.File]::WriteAllText((Join-Path $dir 'run.log'), $output)
    if ($result -eq 0 -or !$output.Contains('ASSERT: ' + $fault.failure)) { throw "Legacy fault missed intended assertion: $output" }
    Write-Output "Rejected $($fault.name) at $($fault.failure)"
}
Write-Output 'ARENA_MOLOTOV_ITEM_CHECKS_PASSED'
