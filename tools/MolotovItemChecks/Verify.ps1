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
    @{ name='late-registration'; source=$itemRegistration; find='TypePriority = OnLoadOrder.Preload'; replace='TypePriority = OnLoadOrder.PostLoad + 97999'; failure='native item registration precedes profile snapshot' },
    @{ name='missing-private-flea-exclusion'; source=$itemRegistration; find='customFleaBlacklist.Add(new MongoId(ArenaMolotovItem.TemplateId));'; replace='// missing private flea exclusion'; failure='production private exclusion survives actual native bulk flea pass' },
    @{ name='global-handbook'; source=$itemRegistration; find='AddToHandbook = false'; replace='AddToHandbook = true'; failure='no global handbook/flea/shelf entry' },
    @{ name='unsafe-publication'; source=$itemRegistration; find='|| !IsHarmless(registered)'; replace=''; failure='failed registration does not authorize supply ignored-overrides' },
    @{ name='partial-rollback'; source=$itemRegistration; find='items.Remove(id);'; replace='// missing rollback'; failure='failed registration leaves no partial explosive clone throw-after' },
    @{ name='foreign-id'; source=$itemRegistration; find='ReferenceEquals(occupied, RegisteredTemplate) && IsHarmless(occupied)'; replace='IsHarmless(occupied)'; failure='foreign ID collision stays untouched' },
    @{ name='availability-lookalike'; source=$itemRegistration; find='ReferenceEquals(current, registered) && IsHarmless(current)'; replace='IsHarmless(current)'; failure='lookalike replacement revokes client availability' },
    @{ name='availability-unsafe'; source=$itemRegistration; find='ReferenceEquals(current, registered) && IsHarmless(current)'; replace='ReferenceEquals(current, registered)'; failure='unsafe registered template revokes client availability' },
    @{ name='second-server-molotov'; source=$itemSupplies; find='added += 2;'; replace='contents.Add(new JsonObject { ["tpl"] = ArenaMode.Shared.ArenaMolotovItem.TemplateId, ["count"] = 1, ["optionalQuickUseOnly"] = true }); added += 3;'; failure='server catalog never adds a second Molotov supply' },
    @{ name='replace-native-frag'; source=$itemSupplies; find='["tpl"] = Frag,'; replace='["tpl"] = ArenaMode.Shared.ArenaMolotovItem.TemplateId,'; failure='server catalog never adds a second Molotov supply' },
    @{ name='missing-native-frag'; source=$itemSupplies; find='for (int i = 0; i < 2; i++)'; replace='for (int i = 0; i < 1; i++)'; failure='server keeps exactly two ordinary RGD-5 supplies' },
    @{ name='optional-slot-policy'; source=$itemSupplies; find='["tpl"] = Frag, ["count"] = 1, ["optionalQuickUseOnly"] = true'; replace='["tpl"] = Frag, ["count"] = 1, ["optionalQuickUseOnly"] = false'; failure='every optional request remains single and quick-use only' }
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
$historical = & dotnet run --project (Join-Path $itemRoot 'tools/KitPolishChecks/Supplies/SuppliesChecks.csproj') -c Release -- $itemRoot $Database 2>&1 | Out-String
$historicalExit = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $itemStage 'historical-production.log'), $historical)
if ($historicalExit -ne 0 -or !$historical.Contains('KIT_SUPPLIES_ASSERTIONS_PASSED:')) { throw "Historical kit-supplies regression failed: $historical" }
Write-Output $historical.Trim()
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
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "Legacy fault must compile: $build" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet (Join-Path $dir 'out/SuppliesChecks.dll') $itemRoot $Database 2>&1 | Out-String
        $result = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $dir 'run.log'), $output)
    if ($result -eq 0 -or !$output.Contains('ASSERT: ' + $fault.failure)) { throw "Legacy fault missed intended assertion: $output" }
    Write-Output "Rejected $($fault.name) at $($fault.failure)"
}
Write-Output "ARENA_MOLOTOV_ITEM_CHECKS_PASSED; $($itemFaults.Count) item/native safety mutants and two historical packing mutants; evidence $itemStage"
