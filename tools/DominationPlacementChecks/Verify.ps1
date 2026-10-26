param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$installedBefore = Read-ArenaInstalledSnapshot $SptPath
try {
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $runtime=Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPickupRuntime.cs'
    $spacing=Join-Path $repo 'ArenaMode.Client/Pickups/ArenaObjectivePickupSpacing.cs'
    $map=Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPickupMapLayout.cs'
    $faults=@(
        @{Name='missing-horizontal-clearance';Path=$spacing;Property='SpacingSource';Find='radius + HorizontalClearance';Replace='radius';Failure='six metre clearance boundary excluded'},
        @{Name='missing-vertical-clearance';Path=$spacing;Property='SpacingSource';Find='heightTolerance + VerticalClearance';Replace='heightTolerance';Failure='vertical clearance boundary excluded'},
        @{Name='cross-floor-block';Path=$spacing;Property='SpacingSource';Find='Math.Abs(delta.y) <= heightTolerance + VerticalClearance';Replace='true';Failure='separate floors remain independent'},
        @{Name='unready-position-permission';Path=$spacing;Property='SpacingSource';Find='centers.Count == 0';Replace='centers.Count < 0';Failure='unprepared objective positions fail closed'},
        @{Name='initial-authored-skip';Path=$runtime;Property='RuntimeSource';Find='if (!ObjectivePlacementAllows(requested, diagnostics)) continue;';Replace='';Failure='rejected original candidate is not repaired into permission'},
        @{Name='repaired-floor-skip';Path=$runtime;Property='RuntimeSource';Find='if (!ObjectivePlacementAllows(floor, diagnostics)) continue;';Replace='';Failure='final repaired floor cannot enter objective clearance'},
        @{Name='exchange-repaired-skip';Path=$runtime;Property='RuntimeSource';Find='if (!ObjectivePlacementAllows(floor, diagnostics) || !ObjectivePlacementAllows(reverseFloor, diagnostics)) continue;';Replace='';Failure='exchange final repaired floor cannot enter objective clearance'},
        @{Name='stale-factory-anchor';Path=$runtime;Property='RuntimeSource';Find='_factoryAnchorLayout = null;';Replace='';Failure='DOM reset cannot carry a Factory anchor into another map'},
        @{Name='expanded-anchor-coupling';Path=$runtime;Property='RuntimeSource';Find='ArenaPickupGeometry.TryFactoryAnchor(_factoryAnchorLayout, out anchor)';Replace='ArenaPickupGeometry.TryFactoryAnchor(_layout, out anchor)';Failure='all six types place using independent original Factory anchor: factory4_day'},
        @{Name='unfiltered-generated-pool';Path=$map;Property='MapSource';Find='.Where(p => allows(new Vector3(p.X, p.Y, p.Z))).Take(MaximumCandidates)';Replace='.Take(MaximumCandidates)';Failure='DOM frozen candidate pool excludes objectives: factory4_day'},
        @{Name='factory-trap-seeds';Path=$map;Property='MapSource';Find='|| ArenaBotSpawnPolicy.IsExcludedPoint(map, row.Id)';Replace='';Failure='DOM native expansion excludes trapped sniper and player-only seeds'},
        @{Name='player-only-seeds';Path=$map;Property='MapSource';Find='|| (row.Categories & (ESpawnCategoryMask.Bot | ESpawnCategoryMask.BotPmc)) == 0';Replace='';Failure='DOM native expansion excludes trapped sniper and player-only seeds'}
    )
    Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'DominationPlacementChecks.csproj') 'DOMINATION_PLACEMENT_BASELINE_PASSED' @() $faults
    Write-Output 'DOMINATION_PLACEMENT_CHECKS_PASSED'
} finally { Assert-ArenaInstalledSnapshotUnchanged $installedBefore $SptPath }
