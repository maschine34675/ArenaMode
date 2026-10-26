$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$view=Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPickupView.cs'
$faults=@(
    # The label is created under an inactive parent: native TMP defers Awake and the font setter fails.
    @{Name='inactive-tmp-awake';Path=$view;Property='ViewSource';Find='_root = new GameObject("Arena pickup marker " + point.Id);';Replace='_root = new GameObject("Arena pickup marker " + point.Id); _root.SetActive(false);';Failure='native TMP awakens before font configuration'},
    # The final hide is dropped: the marker is live before any camera has been checked.
    @{Name='early-active-marker';Path=$view;Property='ViewSource';Find='            _root.SetActive(false);';Replace='            // marker left active';Failure='constructed marker stays hidden until Tick has checked a camera'},
    # The constructor no longer cleans up after a late failure.
    @{Name='constructor-failure-leak';Path=$view;Property='ViewSource';Find='            Dispose();';Replace='            // no cleanup';Failure='failed constructor hides and destroys its complete private tree'},
    @{Name='material-leak';Path=$view;Property='ViewSource';Find='if (_material != null) UnityEngine.Object.Destroy(_material);';Replace='';Failure='failed constructor releases private materials'},
    # Tick shows the marker without a camera.
    @{Name='tick-without-camera';Path=$view;Property='ViewSource';Find='bool visible = state.Phase != ArenaPickupPhase.Retired && camera != null';Replace='bool visible = state.Phase != ArenaPickupPhase.Retired';Failure='unknown camera keeps the marker hidden'},
    @{Name='objective-beacon-overlap';Path=$view;Property='ViewSource';Find='_beam.SetActive(available && !nearObjective)';Replace='_beam.SetActive(available)';Failure='nearby objective suppresses the tall beacon'},
    @{Name='global-beacon-suppression';Path=$view;Property='ViewSource';Find='_beam.SetActive(available && !nearObjective)';Replace='_beam.SetActive(false)';Failure='ordinary pickups keep their beacon'}
)
foreach($fault in $faults) {
    $count=([IO.File]::ReadAllText($fault.Path).Split(@($fault.Find),[StringSplitOptions]::None).Length-1)
    if($count -ne 1) { throw ('Fault anchor must match exactly once ('+$count+'): '+$fault.Name) }
}
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'ArenaPickupViewChecks.csproj') 'ARENA_PICKUP_VIEW_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_PICKUP_VIEW_CHECKS_PASSED'
