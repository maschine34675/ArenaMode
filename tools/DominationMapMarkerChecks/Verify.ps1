#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = Join-Path $PSScriptRoot '../../ArenaMode.Client/Compat/DynamicMapsDominationMarkers.cs'
$faults = @(
    @{ Name='world-axes'; Path=$source; Property='MarkerSource'; Find='new(world.x, world.z, world.y)'; Replace='new(world.x, world.y, world.z)'; Failure='Unity coordinates project x z y exactly once' },
    @{ Name='inactive-view-omitted'; Path=$source; Property='MarkerSource'; Find='FindObjectsOfType(_api.ViewType, true)'; Replace='FindObjectsOfType(_api.ViewType, false)'; Failure='inactive retained map gets objectives and preserves native markers' },
    @{ Name='round-gate-omitted'; Path=$source; Property='MarkerSource'; Find='if (!_allowed() || names == null'; Replace='if (false || names == null'; Failure='countdown has no objectives' },
    @{ Name='map-gate-omitted'; Path=$source; Property='MarkerSource'; Find='!names.Any(n => string.Equals(n, _location, StringComparison.OrdinalIgnoreCase))'; Replace='false'; Failure='unload and wrong-location selection remove only owned objectives' },
    @{ Name='owner-colour-frozen'; Path=$source; Property='MarkerSource'; Find='ColorFor(_points[i])'; Replace='new Color(.85f, .88f, .9f)'; Failure='USEC ownership changes the existing marker to blue' },
    @{ Name='owned-removal-omitted'; Path=$source; Property='MarkerSource'; Find='_api.Remove.Invoke(view, new object[] { marker })'; Replace='Hide(marker)'; Failure='unload and wrong-location selection remove only owned objectives' },
    @{ Name='hide-can-escape'; Path=$source; Property='MarkerSource'; Find='Hide(marker);'; Replace='marker.gameObject.SetActive(false);'; Failure='failing fallback hide cannot interrupt native unload or final unpatch' }
)
# The last fault must fail the named boundary assertion rather than just crash.
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationMapMarkerChecks.csproj') -Token 'DOMINATION_MAP_MARKER_CHECKS_PASSED' -Properties @() -Faults $faults
& (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Installed contract check failed.' }
Write-Output 'DOMINATION_MAP_MARKERS_VERIFIED'
