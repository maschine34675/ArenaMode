#requires -Version 7.0
param([string]$SptRoot = 'D:/SPT41', [string]$AdditionalDynamicMapsPaths = $env:ARENA_DYNAMICMAPS_CONTRACT_DLLS)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = Join-Path $PSScriptRoot '../../ArenaMode.Client/Compat/DynamicMapsDominationMarkers.cs'
$faults = @(
    @{ Name='world-axes'; Path=$source; Property='MarkerSource'; Find='new(world.x, world.z, world.y)'; Replace='new(world.x, world.y, world.z)'; Failure='Unity coordinates project x z y exactly once' },
    @{ Name='inactive-view-omitted'; Path=$source; Property='MarkerSource'; Find='FindObjectsOfType(_api.ViewType, true)'; Replace='FindObjectsOfType(_api.ViewType, false)'; Failure='inactive retained map gets objectives and preserves native markers' },
    @{ Name='round-gate-omitted'; Path=$source; Property='MarkerSource'; Find='if (!_allowed() || names == null'; Replace='if (false || names == null'; Failure='countdown has no objectives' },
    @{ Name='map-gate-omitted'; Path=$source; Property='MarkerSource'; Find='!names.Any(n => string.Equals(n, _location, StringComparison.OrdinalIgnoreCase))'; Replace='false'; Failure='unload and wrong-location selection remove only owned objectives' },
    @{ Name='owner-colour-frozen'; Path=$source; Property='MarkerSource'; Find='ColorFor(_points[i])'; Replace='new Color(.85f, .88f, .9f)'; Failure='USEC ownership changes the existing marker to blue' },
    @{ Name='pack-refresh-not-restored'; Path=$source; Property='MarkerSource'; Find=@('var image = (Image)_api.Image.GetValue(marker);', '((Image)_api.Image.GetValue(marker)).sprite = null;'); Replace=@('var image = (Image)_api.Image.GetValue(marker); image.sprite = null;', ''); Failure='marker pack refresh preserves owned diamonds and leaves native icons untouched' },
    @{ Name='owned-removal-omitted'; Path=$source; Property='MarkerSource'; Find='_api.Remove.Invoke(view, new object[] { marker })'; Replace='Hide(marker)'; Failure='unload and wrong-location selection remove only owned objectives' },
    @{ Name='hide-can-escape'; Path=$source; Property='MarkerSource'; Find='Hide(marker);'; Replace='marker.gameObject.SetActive(false);'; Failure='failing fallback hide cannot interrupt native unload or final unpatch' }
)
# The entire lifecycle runs against two distinct compiled API fixtures. The
# private modern members must really be private; public test observation aliases
# never participate in the production reflection binding.
$project = Join-Path $PSScriptRoot 'DominationMapMarkerChecks.csproj'
foreach ($fixture in @('Legacy121', 'Modern130')) {
    $shapeFaults = @(
        @{ Name='load-shape-inverted'; Path=$source; Property='MarkerSource'; Find='null, new[] { map }, null) != null'; Replace='null, new[] { map }, null) == null'; Failure='inactive retained map gets objectives and preserves native markers' },
        @{ Name='visual-access-inverted'; Path=$source; Property='MarkerSource'; Find='modern ? MethodAttributes.Private : MethodAttributes.Public'; Replace='modern ? MethodAttributes.Public : MethodAttributes.Private'; Failure='inactive retained map gets objectives and preserves native markers' },
        @{ Name='alpha-access-inverted'; Path=$source; Property='MarkerSource'; Find='modern ? MethodAttributes.Family : MethodAttributes.Public'; Replace='modern ? MethodAttributes.Public : MethodAttributes.Family'; Failure='inactive retained map gets objectives and preserves native markers' }
    )
    Test-ArenaSourceFaults -Project $project -Token 'DOMINATION_MAP_MARKER_CHECKS_PASSED' -Properties @("-p:ApiFixture=$fixture") -Faults ($faults + $shapeFaults)
}
$guardFault = @(
    @{ Name='unknown-shape-accepted'; Path=$source; Property='MarkerSource'; Find='(getter.Attributes & MethodAttributes.MemberAccessMask) != getterAccess'; Replace='false'; Failure='unknown mixed API is refused before hooks and UI discovery' }
)
Test-ArenaSourceFaults -Project $project -Token 'DOMINATION_MAP_MARKER_CHECKS_PASSED' -Properties @('-p:ApiFixture=UnknownShape') -Faults $guardFault
& (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1') -SptPath $SptRoot
if ($LASTEXITCODE -ne 0) { throw 'Installed contract check failed.' }
foreach ($path in @($AdditionalDynamicMapsPaths -split ';' | Where-Object { $_ })) {
    & (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1') -SptPath $SptRoot -DynamicMapsPath $path
    if ($LASTEXITCODE -ne 0) { throw "Additional release contract check failed: $path" }
}
Write-Output 'DOMINATION_MAP_MARKERS_VERIFIED'
