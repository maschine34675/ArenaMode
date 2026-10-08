param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
$pickupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pickupSource = Join-Path $pickupRepo 'ArenaMode.Client/Pickups'
$pickupEvidence = Join-Path $pickupRepo ('artifacts/pickup-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($pickupEvidence)
$pickupInputs = @{}
$pickupBound = @('ArenaMode.Client/Plugin.cs','ArenaMode.Client/ArenaController.cs','ArenaMode.Client/Hud/ArenaHud.cs',
    'ArenaMode.Client/Hud/ArenaPowerupPanel.cs','ArenaMode.Client/Kits/KitModels.cs')
$pickupBound += @(Get-ChildItem -LiteralPath $pickupSource -File | ForEach-Object { 'ArenaMode.Client/Pickups/' + $_.Name })
$pickupBound += @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/ArenaPickupChecks/' + $_.Name })
foreach ($path in $pickupBound) { $pickupInputs[$path] = (Get-FileHash -LiteralPath (Join-Path $pickupRepo $path)).Hash }
$pickupInstalled = @{}
foreach ($path in @('BepInEx/plugins/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll','EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll',
    'SPT_Runtime/SPT_Data/database/locations/factory4_day/base.json','SPT_Runtime/SPT_Data/database/locations/factory4_night/base.json')) {
    $pickupInstalled[$path] = (Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash
}
function Run-Pickup([string]$name, [string]$source, [string]$failure = '') {
    $directory = Join-Path $pickupEvidence $name; [void][IO.Directory]::CreateDirectory($directory)
    $build = & dotnet build (Join-Path $PSScriptRoot 'ArenaPickupChecks.csproj') -c Release --nologo `
        "-p:PickupSourceRoot=$source" "-p:BaseIntermediateOutputPath=$directory/obj/" "-p:OutputPath=$directory/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $directory 'build.log'), $build)
    if ($code -ne 0) { throw "$name must compile before behavioral verification: $build" }
    $run = & dotnet (Join-Path $directory 'out/ArenaPickupChecks.dll') $SptPath 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $directory 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains('ASSERT: ' + $failure)) { throw "$name did not reject intended fault: $run" }
        Write-Output "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('ARENA_PICKUP_BEHAVIOR_PASSED:')) { throw "$name failed: $run" }
    else { Write-Output $run.Trim() }
}
try {
    # These are wiring/ownership source contracts only, not simulated visual acceptance.
    $contracts = @(
        @('ArenaMode.Client/Plugin.cs','Pickups.ArenaPickupSettings.Init(Config);'),
        @('ArenaMode.Client/Plugin.cs','Pickups.ArenaPowerups.Enable();'),
        @('ArenaMode.Client/Plugin.cs','Pickups.ArenaInfiniteAmmo.Enable();'),
        @('ArenaMode.Client/Plugin.cs','AddComponent<Pickups.ArenaPickupCapture>()'),
        @('ArenaMode.Client/Plugin.cs','Pickups.ArenaPickupRuntime.Reset();'),
        @('ArenaMode.Client/ArenaController.cs','Pickups.ArenaPickupRuntime.Tick();'),
        @('ArenaMode.Client/Hud/ArenaHud.cs','_powerups.Tick();'),
        @('ArenaMode.Client/Hud/ArenaHud.cs','new ArenaPowerupPanel()'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','ArenaPickupRuntime.IsLiveActor(player)'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','background.raycastTarget = false'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','state.ShieldPoints'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','state.DamageSeconds'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','state.JumpSeconds'),
        @('ArenaMode.Client/Hud/ArenaPowerupPanel.cs','state.AmmoSeconds'),
        @('ArenaMode.Client/Pickups/ArenaPickupSettings.cs','ArenaPickupWeapons.M60Id'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','"JUMP BOOTS"'),
        @('ArenaMode.Client/Pickups/ArenaPickupCapture.cs','Settings.AreaCaptureAllowInNormalPmc?.Value != true'),
        @('ArenaMode.Client/Pickups/ArenaPickupCapture.cs','ArenaSession.IsExactActiveRaid(game, world)'),
        @('ArenaMode.Client/Pickups/ArenaPickupCapture.cs','settings.playerSide == ESideType.Pmc'),
        @('ArenaMode.Client/Pickups/ArenaPickupCapture.cs','!ArenaSession.ArmedForNextRaid'),
        @('ArenaMode.Client/Pickups/ArenaPickupCapture.cs','ArenaPickupLayouts.Export(directory, file)'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','collider.enabled = false; UnityEngine.Object.Destroy(collider);'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','UnityEngine.Object.Destroy(_material);'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','UnityEngine.Object.Destroy(_beamMaterial);'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','if (_beam != null) _beam.SetActive(available);'),
        @('ArenaMode.Client/Pickups/ArenaPickupView.cs','float beamLength = ArenaPickupGeometry.BeamLength(floor);'),
        @('ArenaMode.Client/Pickups/ArenaPickupSettings.cs','"Development - Pickups", "Enabled", false'),
        @('ArenaMode.Client/Pickups/ArenaPickupLayout.cs','FileMode.CreateNew')
    )
    foreach ($contract in $contracts) {
        $text = [IO.File]::ReadAllText((Join-Path $pickupRepo $contract[0]))
        # Capture explicitly excludes armed ordinary raids using an OR early return.
        $needle = if ($contract[1] -eq '!ArenaSession.ArmedForNextRaid') { '|| ArenaSession.ArmedForNextRaid) return false;' } else { $contract[1] }
        if (!$text.Contains($needle)) { throw "Missing source contract: $($contract[0]) :: $needle" }
    }
    $capture = [IO.File]::ReadAllText((Join-Path $pickupSource 'ArenaPickupCapture.cs'))
    if ($capture -match 'ArenaPowerups\.(Grant|Enable)|ArenaWeaponPickup\.Prepare|ArenaPickupRuntime\.Tick') { throw 'Capture must not activate gameplay.' }
    $view = [IO.File]::ReadAllText((Join-Path $pickupSource 'ArenaPickupView.cs'))
    if ($view -match 'Destroy\(weapon\)|weapon\.transform\.SetParent') { throw 'View must not destroy/reparent native pooled weapon.' }
    $contractCount = $contracts.Count + 2
    Write-Output "ARENA_PICKUP_SOURCE_CONTRACTS_PASSED: $contractCount assertions (not Unity visual proof)"
    Run-Pickup 'production' $pickupSource
    $faults = @(
        @{Name='no-live-rotation';File='ArenaPickupRuntime.cs';Find='private const float AvailableRotationSeconds = 60f;';Replace='private const float AvailableRotationSeconds = 600f;';Failure='unclaimed pickup rotates after sixty gameplay seconds'},
        @{Name='same-cooldown-position';File='ArenaPickupRuntime.cs';Find='                    TryRotate(entry, rearming: true);';Replace='';Failure='collected pickup chooses different safe spot after cooldown'},
        @{Name='rotation-token-alias';File='ArenaPickupState.cs';Find='        ++_reservation;';Replace='';Failure='relocation invalidates the previous appearance'},
        @{Name='rotation-overlaps';File='ArenaPickupRuntime.cs';Find='|| Entries.Any(other => !ReferenceEquals(other, entry) && (other.Floor - floor).sqrMagnitude < 9f)';Replace='';Failure='rotation retains position when only alternative overlaps a fixed pickup'},
        @{Name='rotation-unknown-source';File='ArenaPickupRuntime.cs';Find='&& (entry.Definition.Kind != ArenaPickupKind.Weapon || rearming || entry.Weapon?.IsAvailable == true)';Replace='';Failure='unsafe exchange retains both original appearances: unknownWeapon'},
        @{Name='rotation-unsafe-fallback';File='ArenaPickupRuntime.cs';Find='new Vector3(point.X, point.Y, point.Z), anchor, _builtInLayout,';Replace='new Vector3(point.X, point.Y, point.Z), anchor, true,';Failure='unsafe or unavailable rotation retains old available position: clearance'},
        @{Name='rotation-no-exchange';File='ArenaPickupRuntime.cs';Find='if (TryExchange(entry, candidates, anchor, diagnostics, rearming)) return true;';Replace='';Failure='two occupied available slots exchange without a spare position'},
        @{Name='rotation-native-pending';File='ArenaPickupRuntime.cs';Find='        && entry.Weapon?.IsPending != true';Replace='';Failure='unsafe exchange retains both original appearances: nativePending'},
        @{Name='rotation-exchange-preparing';File='ArenaPickupRuntime.cs';Find='        && entry.State.Phase == ArenaPickupPhase.Available && !entry.Preparing';Replace='        && entry.State.Phase == ArenaPickupPhase.Available';Failure='unsafe exchange retains both original appearances: preparing'},
        @{Name='rotation-exchange-authored';File='ArenaPickupRuntime.cs';Find='if (point == null || reverse == null) continue;';Replace='if (point == null) continue; reverse ??= new ArenaPickupPoint { Id = partner.Definition.Id, Kind = partner.Definition.Kind, X = entry.Definition.X, Y = entry.Definition.Y, Z = entry.Definition.Z };';Failure='unsafe exchange retains both original appearances: asymmetric'},
        @{Name='rotation-exchange-double-move';File='ArenaPickupRuntime.cs';Find='|| Time.time < partner.AvailableSince + AvailableRotationSeconds';Replace='';Failure='cooldown rearm does not displace a newly available partner'},
        @{Name='rotation-float-boundary';File='ArenaPickupRuntime.cs';Find='|| Time.time < partner.AvailableSince + AvailableRotationSeconds';Replace='|| Time.time - partner.AvailableSince < AvailableRotationSeconds';Failure='partner availability accepts the exact float deadline without another minute delay'},
        @{Name='rotation-camping';File='ArenaPickupRuntime.cs';Find='entry.MustLeave.Clear(); entry.NeedsEntry = true;';Replace='entry.MustLeave.Clear();';Failure='actor standing at new spot must leave before collection'},
        @{Name='rotation-retroactive-contact';File='ArenaPickupRuntime.cs';Find='!entry.NeedsEntry && !sample.Fresh';Replace='!sample.Fresh';Failure='relocation cannot collect a preappearance movement segment'},
        @{Name='rotation-stale-disposal';File='ArenaPickupRuntime.cs';Find='if (tickGeneration != Generation) return;';Replace='';Failure='native disposal callback preserves replacement round and creates no orphan weapon'},
        @{Name='overlapping-pickups';File='ArenaPickupRuntime.cs';Find='Entries.Any(other => (rotating || other.Rotating) && (other.Floor - floor).sqrMagnitude < 9f)';Replace='false';Failure='final pickup floors cannot overlap within three metres'},
        @{Name='single-placement-pass';File='ArenaPickupRuntime.cs';Find='if (Entries.Count == _layout.Points.Count) FinishPlacement("complete");';Replace='FinishPlacement("complete");';Failure='failed placement recovers using fresh moved anchor'},
        @{Name='replace-successful-entry';File='ArenaPickupRuntime.cs';Find='if (Entries.Any(placedEntry => placedEntry.Definition.Id == definition.Id)) continue;';Replace='';Failure='partial retries preserve successful entry state and weapon load'},
        @{Name='retry-flood';File='ArenaPickupRuntime.cs';Find='_nextPlacement = Time.time + 1f;';Replace='_nextPlacement = Time.time;';Failure='full placement retries are throttled to one second'},
        @{Name='late-fixed-overlap';File='ArenaPickupRuntime.cs';Find='(rotating || other.Rotating)';Replace='rotating';Failure='late fixed point cannot overlap an already placed rotating point'},
        @{Name='custom-relocates';File='ArenaPickupGeometry.cs';Find='if (!allowNearby) return false;';Replace='';Failure='custom positions never try nearby alternatives'},
        @{Name='unbounded-final-snap';File='ArenaPickupGeometry.cs';Find='delta.x * delta.x + delta.z * delta.z > RelocationRadius * RelocationRadius + .0001f';Replace='false';Failure='final navigation snap outside two metre radius rejected'},
        @{Name='token-alias';File='ArenaPickupState.cs';Find='reservation != _reservation';Replace='false';Failure='foreign token cannot settle'},
        @{Name='teleport-sweep';File='ArenaPickupState.cs';Find='planar + dy * dy <= 25';Replace='true';Failure='teleport does not sweep pickup'},
        @{Name='timeout-rearms';File='ArenaPickupState.cs';Find='if (Phase == ArenaPickupPhase.Pending && now >= _pendingUntil) Retire();';Replace='if (Phase == ArenaPickupPhase.Pending && now >= _pendingUntil) Phase = ArenaPickupPhase.Available;';Failure='timeout retires instead of duplicating'},
        @{Name='area-fallback';File='ArenaPickupLayout.cs';Find='&& string.Equals(layout.AreaId, area, StringComparison.Ordinal)';Replace='&& (layout.AreaId == null || string.Equals(layout.AreaId, area, StringComparison.Ordinal))';Failure='area never falls back to full map'},
        @{Name='body-alias';File='ArenaPickupRuntime.cs';Find='|| !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)';Replace='';Failure='stale or dead local identity rejected before tick'},
        @{Name='fresh-spawn-collects';File='ArenaPickupRuntime.cs';Find='if ((sample.Fresh || entry.NeedsEntry) && inside)';Replace='if (false && inside)';Failure='fresh spawn inside point never collects'},
        @{Name='outward-retry';File='ArenaPickupRuntime.cs';Find='if (!inside && (current - entry.Floor).sqrMagnitude > 2.25f && entry.MustLeave.Remove(player)) continue;';Replace='if (!inside && (current - entry.Floor).sqrMagnitude > 2.25f) entry.MustLeave.Remove(player);';Failure='leaving after failed take must not retry outward sweep'},
        @{Name='projected-wall-bypass';File='ArenaPickupRuntime.cs';Find='if (!ArenaPickupGeometry.LineClear(current + Vector3.up * .6f, entry.Floor + Vector3.up * .6f)) continue;';Replace='';Failure='projected zero length ray cannot bypass current actor wall'},
        @{Name='duplicate-failure-retires';File='ArenaPickupRuntime.cs';Find='if (!entry.State.Complete(reservation, success, Time.time, entry.Definition.RespawnSeconds)) return;';Replace='entry.State.Complete(reservation, success, Time.time, entry.Definition.RespawnSeconds);';Failure='duplicate false callback cannot retire confirmed success'},
        @{Name='anchor-unbounded';File='ArenaPickupRuntime.cs';Find='_placementDeadline = Time.time + 15f;';Replace='_placementDeadline = Time.time + 300f;';Failure='anchor timeout skips round without repeated warning'},
        @{Name='disable-recreates';File='ArenaPickupRuntime.cs';Find='if (ReferenceEquals(_disabledMatch, match)) return;';Replace='';Failure='disable latch prevents same match recreation'}
    )
    foreach ($fault in $faults) {
        $faultRoot = Join-Path $pickupEvidence ($fault.Name + '/source'); [void][IO.Directory]::CreateDirectory($faultRoot)
        foreach ($name in @('ArenaPickupRuntime.cs','ArenaPickupState.cs','ArenaPickupLayout.cs','ArenaPickupGeometry.cs','ArenaPickupWeapons.cs','ArenaHealthPickup.cs','ArenaPowerupState.cs')) {
            $text = [IO.File]::ReadAllText((Join-Path $pickupSource $name))
            if ($name -eq $fault.File) {
                if ($text.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.Name)" }
                $text = $text.Replace($fault.Find, $fault.Replace)
            }
            [IO.File]::WriteAllText((Join-Path $faultRoot $name), $text)
        }
        Run-Pickup $fault.Name $faultRoot $fault.Failure
    }
    Run-Pickup 'production-final' $pickupSource
    foreach ($path in $pickupInputs.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $pickupRepo $path)).Hash -ne $pickupInputs[$path]) { throw "Bound source changed; rerun required: $path" }
    }
} finally {
    foreach ($path in $pickupInstalled.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $pickupInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes=$pickupInputs; installedHashes=$pickupInstalled; sourcesUnchanged=$true; installedUnchanged=$true;
    compiledFaultCount=$faults.Count; sourceContractCount=$contractCount; completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$pickupEvidence;
    limitation='Actual production runtime/state/layout/geometry use controlled world, physics/navigation, UI, powerup and native inventory result boundaries. Source contracts inspect wiring only. Native game behavior and visuals require GPU acceptance; no client build or deployment.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $pickupEvidence 'manifest.json')
Write-Output "ARENA PICKUP CHECKS PASSED; $($faults.Count) compiled faults rejected; evidence: $pickupEvidence"
