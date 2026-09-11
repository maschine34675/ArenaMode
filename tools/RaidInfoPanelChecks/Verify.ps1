param([switch]$ProductionOnly)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$arenaPanelRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaPanelProject = Join-Path $PSScriptRoot 'RaidInfoPanelChecks.csproj'
$arenaPanelEvidence = Join-Path $PSScriptRoot ('obj\negative\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaPanelEvidence -Force | Out-Null

function Invoke-PanelCheck([string]$Name, [string]$Property, [string]$Source, [string]$ExpectedFailure) {
    $arenaPanelArgs = @('run', '--project', $arenaPanelProject, '-c', 'Release')
    if ($Property) { $arenaPanelArgs += "-p:${Property}=$Source" }
    $arenaPanelOutput = (& dotnet @arenaPanelArgs 2>&1 | Out-String)
    $arenaPanelExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $arenaPanelEvidence "$Name.log"), $arenaPanelOutput)
    if ($ExpectedFailure) {
        if ($arenaPanelExit -eq 0 -or !$arenaPanelOutput.Contains('CHECK FAILED: ' + $ExpectedFailure) -or $arenaPanelOutput.Contains('error CS')) {
            throw "Negative control '$Name' did not fail at its intended assertion: $arenaPanelOutput"
        }
        Write-Output "REJECTED_SOURCE_FAULT: $Name ($ExpectedFailure)"
    } else {
        if ($arenaPanelExit -ne 0 -or !$arenaPanelOutput.Contains('ARENA_INFO_PANEL_PRODUCTION_PASSED')) {
            throw "Production panel checks failed: $arenaPanelOutput"
        }
        Write-Output $arenaPanelOutput.Trim()
    }
}

function Invoke-PanelFault([string]$Name, [string]$Property, [string]$Filename, [string]$Before, [string]$After, [string]$ExpectedFailure) {
    $arenaPanelOriginal = [IO.File]::ReadAllText((Join-Path $arenaPanelRepo "ArenaMode.Client\Hud\InfoPanels\$Filename"))
    if (($arenaPanelOriginal.Split(@($Before), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw "Fault '$Name' production anchor must match exactly once."
    }
    $arenaPanelMutant = Join-Path $arenaPanelEvidence "$Name.cs"
    [IO.File]::WriteAllText($arenaPanelMutant, $arenaPanelOriginal.Replace($Before, $After))
    Invoke-PanelCheck $Name $Property $arenaPanelMutant $ExpectedFailure
}

Invoke-PanelCheck 'production-before' '' '' ''
if (!$ProductionOnly) {
    Invoke-PanelFault 'inventory-flag-omitted' 'ContextSource' 'RaidPlayerContext.cs' `
        '|| player.IsInventoryOpened' '' 'Inventory opening before screen switch clears out player'
    Invoke-PanelFault 'native-screen-omitted' 'ContextSource' 'RaidPlayerContext.cs' `
        '|| !ArenaHudVisibility.IsBattleUiActive' '' 'Inventory screen after flag reset clears out player'
    Invoke-PanelFault 'native-canvas-kept-visible' 'VisibilitySource' '..\ArenaHudVisibility.cs' `
        'canvas.enabled = IsBattleUiActive;' 'canvas.enabled = true;' `
        'Native screens stop canvas drawing without disabling its object'
    Invoke-PanelFault 'inactive-parent-before-tmp-awake' 'PanelSource' 'ArenaInfoPanels.cs' `
        '_weapon = new WeaponInfoPanel(rect);' '_root.SetActive(false); _weapon = new WeaponInfoPanel(rect);' `
        'Active-hierarchy panel construction completes before the root is hidden'
    Invoke-PanelFault 'inactive-session-guard-omitted' 'ContextSource' 'RaidPlayerContext.cs' `
        '!Session.ArenaSession.IsActive || ' '' 'Inactive Arena guard avoids accessing exact-raid state'
    Invoke-PanelFault 'foreign-world-leak' 'ContextSource' 'RaidPlayerContext.cs' `
        '|| !Session.ArenaSession.IsExactActiveRaid(game, gameWorld)' '' 'Foreign started game clears out player'
    Invoke-PanelFault 'stale-weapon-resurrection' 'PanelSource' 'ArenaInfoPanels.cs' `
        '_weaponValid = false;' '_weaponValid = true;' 'Throttled frame must not resurrect stale weapon data'
    Invoke-PanelFault 'theme-subscription-leak' 'PanelSource' 'ArenaInfoPanels.cs' `
        'if (_subscribed) InfoPanelSettings.PanelOpacity.SettingChanged -= OnThemeChanged;' `
        '// deliberately omitted theme unsubscribe' 'Dispose unsubscribes theme handler'
    Invoke-PanelFault 'legacy-config-write' 'SettingsSource' 'InfoPanelSettings.cs' `
        'SaveOnConfigSet = false' 'SaveOnConfigSet = true' 'Legacy bind cannot write config'
    Invoke-PanelFault 'tactical-vest-omitted' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        'Item tacticalVest = equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;' `
        'Item tacticalVest = null;' 'Armored CompoundItem in TacticalVest counts full and partial ready mags expected'
    Invoke-PanelFault 'magazine-exception-retains-items' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        'finally { Reset(); }' 'finally { /* deliberate scratch leak */ }' `
        'Armored CompoundItem in TacticalVest counts full and partial ready mags releases scratch references'
    Invoke-PanelFault 'grenade-exception-retains-items' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'finally { Reset(); }' 'finally { /* deliberate scratch leak */ }' `
        'Direct rig and pocket grenades are ready for the native quick throw releases scratch references'
    Invoke-PanelFault 'grenade-exception-path-leak' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'try { return CollectSummary(player, out summary); } finally { Reset(); }' `
        'bool collected = CollectSummary(player, out summary); Reset(); return collected;' `
        'Classification exception propagates to UI but finally clears grenade scratch'
    Invoke-PanelFault 'grenade-nested-counted-ready' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'summary.Add(kind, ready: false);' 'summary.Add(kind, ready: true);' `
        'Backpack and nested-pouch grenades are carried but not quick-throwable expected'
    Invoke-PanelFault 'grenade-ready-from-carried-only' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'if (TryClassify(_readyScratch[i], out GrenadeKind kind))' `
        'if (ContainsReference(_carriedScratch, _readyScratch[i]) && TryClassify(_readyScratch[i], out GrenadeKind kind))' `
        "With UseItemsAnywhere's slots, loose backpack and secure-container grenades are ready expected"
    Invoke-PanelFault 'grenade-native-reach-replaced' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'controller.GetReachableItemsOfTypeNonAlloc(_readyScratch, grenade => controller.Examined(grenade));' `
        'foreach (EquipmentSlot s in new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest }) if (equipment.GetSlot(s).ContainedItem is CompoundItem c) foreach (Grid g in c.Grids) foreach (Item it in g.Items) if (it is ThrowWeap t) _readyScratch.Add(t);' `
        'An unexamined grenade is not a native quick-throw candidate expected'
    Invoke-PanelFault 'grenade-stun-not-flash' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'case ThrowWeapType.stun_grenade:' '// stun case deliberately omitted' `
        'Stun grenades count as flash by type alone expected'
    Invoke-PanelFault 'grenade-secure-counted' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch);' `
        'CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch); CollectGrenades(equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem as CompoundItem, _carriedScratch);' `
        'ArmorVest and secure container grenades are excluded expected'
    # Leave the generated executable compiled from unmodified production after controls.
    Invoke-PanelCheck 'production-after' '' '' ''
    Write-Output 'ARENA_INFO_PANEL_CHECKS_PASSED (production assertions + 18 rejected source faults)'
} else {
    Write-Output 'ARENA_INFO_PANEL_PRODUCTION_ONLY_PASSED (negative controls not run)'
}
Write-Output "Evidence: $arenaPanelEvidence"
