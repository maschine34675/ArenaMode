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
    Invoke-PanelFault 'magazine-exception-retains-items' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        'finally { Reset(); }' 'finally { /* deliberate scratch leak */ }' `
        'TacticalVest spares count as full and partial via the native reachable query releases scratch references'
    Invoke-PanelFault 'magazine-backpack-hook-ignored' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        'if (ArenaBackpackReload.AppendsBackpackFor(controller))' 'if (true)' `
        "Backpack magazines are not counted while Arena's backpack-reload hook is off expected"
    Invoke-PanelFault 'magazine-nested-pouch-counted' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        'controller.GetAcceptableItemsNonAlloc(BackpackSlot, _backpackScratch, magazine => magazine.Count > 0);' `
        'controller.GetAcceptableItemsNonAlloc(BackpackSlot, _backpackScratch, magazine => magazine.Count > 0); if (controller.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is CompoundItem backpackMutant && backpackMutant.Grids != null) foreach (var backpackGrid in backpackMutant.Grids) foreach (var backpackItem in backpackGrid.Items) if (backpackItem is CompoundItem backpackPouch && backpackPouch.Grids != null) foreach (var pouchGrid in backpackPouch.Grids) foreach (var pouchItem in pouchGrid.Items) if (pouchItem is Magazine pouchMag && pouchMag.Count > 0) _backpackScratch.Add(pouchMag);' `
        'Nested pouch magazines, in the rig or the backpack, are never counted expected'
    Invoke-PanelFault 'magazine-backpack-all-full' 'MagazineSource' 'MagazineInventoryCounter.cs' `
        "                for (int i = 0; i < _backpackScratch.Count; i++)`n                {`n                    Magazine candidate = _backpackScratch[i];`n                    if (!ContainsReference(_magazineScratch, candidate))`n                    {`n                        _magazineScratch.Add(candidate);`n                    }`n                }" `
        "                for (int i = 0; i < _backpackScratch.Count; i++)`n                {`n                    Magazine candidate = _backpackScratch[i];`n                    if (!ContainsReference(_magazineScratch, candidate) && IsSpareLoadedMagazine(candidate, magazineSlot))`n                    {`n                        summary.FullCount++;`n                    }`n                }" `
        'With the hook on, backpack top-level magazines count as full/partial like any other spare expected'
    Invoke-PanelFault 'grenade-exception-retains-items' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'finally { Reset(); }' 'finally { /* deliberate scratch leak */ }' `
        'Direct rig and pocket grenades are on G and usable releases scratch references'
    Invoke-PanelFault 'grenade-exception-path-leak' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'try { return CollectSummary(player, out summary); } finally { Reset(); }' `
        'bool collected = CollectSummary(player, out summary); Reset(); return collected;' `
        'Classification exception propagates to UI but finally clears grenade scratch'
    Invoke-PanelFault 'grenade-native-reach-replaced' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'controller.GetReachableItemsOfTypeNonAlloc(_onGScratch, grenade => controller.Examined(grenade));' `
        'foreach (EquipmentSlot s in new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest }) if (equipment.GetSlot(s).ContainedItem is CompoundItem c) foreach (Grid g in c.Grids) foreach (Item it in g.Items) if (it is ThrowWeap t) _onGScratch.Add(t);' `
        'An unexamined pocket grenade is neither on G nor usable expected'
    Invoke-PanelFault 'grenade-stun-not-flash' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'case ThrowWeapType.stun_grenade:' '// stun case deliberately omitted' `
        'Stun grenades count as flash by type alone expected'
    Invoke-PanelFault 'grenade-secure-counted' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch);' `
        'CollectGrenades(equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem as CompoundItem, _carriedScratch); CollectGrenades(equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem as CompoundItem, _carriedScratch);' `
        "Arena's reach widening makes an examined backpack grenade usable without changing G's own list expected"
    Invoke-PanelFault 'grenade-isatreachableplace-dropped' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        'bool usable = ContainsReference(_onGScratch, grenade) || controller.IsAtReachablePlace(grenade);' `
        'bool usable = true;' `
        "A grenade nested inside the rig is reachable without Arena's reach widening; a backpack grenade is not expected"
    Invoke-PanelFault 'grenade-ong-from-usable-set' 'GrenadeSource' 'GrenadeInventoryCounter.cs' `
        "            for (int i = 0; i < _onGScratch.Count; i++)`n            {`n                if (TryClassify(_onGScratch[i], out GrenadeKind kind))`n                {`n                    summary.AddOnG(kind);`n                }`n            }" `
        "            for (int i = 0; i < _carriedScratch.Count; i++)`n            {`n                if (TryClassify(_carriedScratch[i], out GrenadeKind kind))`n                {`n                    summary.AddOnG(kind);`n                }`n            }" `
        "A grenade nested inside the rig is reachable without Arena's reach widening; a backpack grenade is not expected"
    Invoke-PanelFault 'reserve-row-hidden-slot-reserves' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'lefts[i] = 0f;' 'lefts[i] = 0f; if (!visible[i]) { cursor += widths[i] + MagazineSpacing; }' `
        'A hidden full-magazine slot takes no room: partial starts at the left edge'
    Invoke-PanelFault 'reserve-row-grenade-spacing-omitted' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'cursor = width + GrenadeSpacing;' 'cursor = width;' `
        'Flash packs one spacing inside the frag slot'
    Invoke-PanelFault 'reserve-row-estimate-removed' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'float text = usable ? measuredText : glyphs * fontSize * 0.65f;' 'float text = measuredText;' `
        'An unmeasured count falls back to a per-glyph estimate'
    Invoke-PanelFault 'reserve-row-grenade-order-reversed' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'for (int i = 0; i < visible.Length; i++)' 'for (int i = visible.Length - 1; i >= 0; i--)' `
        'Frag stays outermost when flashes are carried too'
    Invoke-PanelFault 'reserve-row-implausible-measurement-kept' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'bool usable = !float.IsInfinity(measuredText) && measuredText >= glyphs * fontSize * MinGlyphEm;' `
        'bool usable = !float.IsInfinity(measuredText) && measuredText > 0f;' `
        'A measurement far below any real glyph width falls back to the estimate'
    Invoke-PanelFault 'reserve-row-fitscale-empty-group-ignored' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'if (magazineRight <= 0f || grenadeWidth <= 0f)' 'if (grenadeWidth <= 0f)' `
        'One empty group always returns full scale'
    Invoke-PanelFault 'reserve-row-fitscale-not-applied' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'return Math.Max(MinFitScale, ContentWidth / needed);' 'return 1f;' `
        'A crowded row (144 px of magazine slots, three grenade kinds) shrinks instead of wrapping'
    Invoke-PanelFault 'reserve-row-fitscale-no-clamp' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'return Math.Max(MinFitScale, ContentWidth / needed);' 'return ContentWidth / needed;' `
        'Extreme combined width clamps to the minimum scale instead of shrinking further'
    Invoke-PanelFault 'reserve-row-fitscale-gap-ignored' 'LayoutSource' 'UI\ReserveRowLayout.cs' `
        'float needed = magazineRight + GroupGap + grenadeWidth;' 'float needed = magazineRight + grenadeWidth;' `
        'A crowded row (144 px of magazine slots, three grenade kinds) shrinks instead of wrapping'
    Invoke-PanelFault 'armor-component-double-counted' 'ArmorSource' 'ArmorDurabilityCounter.cs' `
        'groups[(int)group].Add(armor.Repairable.Durability, armor.Repairable.MaxDurability);' `
        'groups[(int)group].Add(armor.Repairable.Durability, armor.Repairable.MaxDurability); groups[(int)group].Add(armor.Repairable.Durability, armor.Repairable.MaxDurability);' `
        'Headwear and TacticalVest each contribute their own soft armor to separate groups expected'
    Invoke-PanelFault 'armor-head-slot-facecover-missing' 'ArmorSource' 'ArmorDurabilityCounter.cs' `
        "EquipmentSlot.FaceCover,`n            EquipmentSlot.Eyewear" 'EquipmentSlot.Eyewear' `
        'FaceCover and Eyewear both land in the head group alongside Headwear expected'
    Invoke-PanelFault 'armor-armorvest-omitted-from-body' 'ArmorSource' 'ArmorDurabilityCounter.cs' `
        "EquipmentSlot.TacticalVest,`n            EquipmentSlot.ArmorVest" 'EquipmentSlot.TacticalVest' `
        'ArmorVest and an armored TacticalVest are both in the body group expected'
    Invoke-PanelFault 'armor-pockets-added-to-body' 'ArmorSource' 'ArmorDurabilityCounter.cs' `
        "EquipmentSlot.ArmorVest`n        };" "EquipmentSlot.ArmorVest,`n            EquipmentSlot.Pockets`n        };" `
        'An armor component sitting in Pockets, outside HeadSlots/BodySlots, is never scanned expected'
    Invoke-PanelFault 'armor-scratch-not-reset' 'ArmorSource' 'ArmorDurabilityCounter.cs' `
        'finally { Reset(); }' 'finally { /* deliberate scratch leak */ }' `
        'Headwear and TacticalVest each contribute their own soft armor to separate groups releases scratch references'
    # Leave the generated executable compiled from unmodified production after controls.
    Invoke-PanelCheck 'production-after' '' '' ''
    Write-Output 'ARENA_INFO_PANEL_CHECKS_PASSED (production assertions + 34 rejected source faults)'
} else {
    Write-Output 'ARENA_INFO_PANEL_PRODUCTION_ONLY_PASSED (negative controls not run)'
}
Write-Output "Evidence: $arenaPanelEvidence"
