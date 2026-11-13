param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$installedBefore = Read-ArenaInstalledSnapshot $SptPath
try {
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$view=Join-Path $repo 'ArenaMode.Client/Pickups/ArenaPickupView.cs'
$domination=Join-Path $repo 'ArenaMode.Client/Hud/DominationPointView.cs'
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
    @{Name='global-beacon-suppression';Path=$view;Property='ViewSource';Find='_beam.SetActive(available)';Replace='_beam.SetActive(false)';Failure='ordinary pickups keep their beacon'},
    @{Name='objective-beacon-suppression';Path=$view;Property='ViewSource';Find='_beam.SetActive(available)';Replace='_beam.SetActive(available && !(ArenaMode.Modes.ArenaMatchService.Current?.Mode is ArenaMode.Modes.TeamDeathmatchMode))';Failure='team mode without a nearby objective keeps the beacon'},
    @{Name='ring-white-vertices';Path=$domination;Property='DominationViewSource';Find='_ring.startColor = _ring.endColor = color;';Replace='_ring.startColor = _ring.endColor = Color.white;';Failure='objective ring vertex tint follows neutral'},
    @{Name='ring-fallback-white';Path=$domination;Property='DominationViewSource';Find='_material.color = _vertexTint ? Color.white : color;';Replace='_material.color = Color.white;';Failure='objective material tint follows the shader contract neutral'},
    @{Name='ring-double-tint';Path=$domination;Property='DominationViewSource';Find='_material.color = _vertexTint ? Color.white : color;';Replace='_material.color = color;';Failure='objective material tint follows the shader contract neutral'},
    @{Name='ring-uses-overexposed-ui-tint';Path=$domination;Property='DominationViewSource';Find='var color = RingColorFor(state);';Replace='var color = ColorFor(state);';Failure='objective ring vertex tint follows neutral'},
    @{Name='ported-map-letter';Path=$domination;Property='DominationViewSource';Find='if (showLetter)';Replace='if (true)';Failure='objective ported view creates no label and no chip'},
    @{Name='chip-ignores-owner';Path=$domination;Property='DominationViewSource';Find='_chipMaterial.color = color;';Replace='_chipMaterial.color = RingColorFor(default);';Failure='objective chip colour follows the owner USEC'},
    @{Name='letter-owner-tint';Path=$domination;Property='DominationViewSource';Find='_label.color = LetterColor;';Replace='_label.color = ColorFor(default);';Failure='objective letter is near-white neutral'},
    @{Name='letter-without-outline';Path=$domination;Property='DominationViewSource';Find='_label.outlineWidth = .2f;';Replace='_label.outlineWidth = 0f;';Failure='objective letter has a dark outline'},
    @{Name='chip-in-front-of-letter';Path=$domination;Property='DominationViewSource';Find='new Vector3(0f, 0f, ChipDepth)';Replace='new Vector3(0f, 0f, -ChipDepth)';Failure='objective chip is a child of the letter, larger than the glyph and behind it'},
    @{Name='letter-scale-unclamped';Path=$domination;Property='DominationViewSource';Find='return Mathf.Clamp(scale, MinLetterScale, MaxLetterScale);';Replace='return scale;';Failure='objective letter scale respects the minimum clamp'},
    @{Name='letter-scale-no-maximum';Path=$domination;Property='DominationViewSource';Find='return Mathf.Clamp(scale, MinLetterScale, MaxLetterScale);';Replace='return Mathf.Clamp(scale, MinLetterScale, 1000f);';Failure='objective letter scale respects the maximum clamp'},
    @{Name='letter-scale-angle-proportional-to-fov';Path=$domination;Property='DominationViewSource';Find='float height = GlyphScreenFraction * 2f * distance * Mathf.Tan(fieldOfViewDegrees * Mathf.Deg2Rad * .5f);';Replace='float height = 2f * distance * Mathf.Tan(fieldOfViewDegrees * GlyphScreenFraction * Mathf.Deg2Rad * .5f);';Failure='objective letter scale follows the distance law at 20 m'},
    @{Name='letter-scale-fixed-distance';Path=$domination;Property='DominationViewSource';Find='2f * distance * Mathf.Tan';Replace='2f * 30f * Mathf.Tan';Failure='objective letter scale follows the distance law at 20 m'},
    @{Name='letter-scale-fixed-fov';Path=$domination;Property='DominationViewSource';Find='fieldOfViewDegrees * Mathf.Deg2Rad';Replace='60f * Mathf.Deg2Rad';Failure='objective letter scale follows the camera field of view'},
    @{Name='letter-font-clone-leak';Path=$domination;Property='DominationViewSource';Find='UnityEngine.Object.Destroy(clone);';Replace='';Failure='objective letter''s cloned font material released on dispose'},
    @{Name='font-own-material-destroyed';Path=$domination;Property='DominationViewSource';Find='if (clone != null && !ReferenceEquals(clone, HudStyle.Font?.material)) UnityEngine.Object.Destroy(clone);';Replace='if (clone != null) UnityEngine.Object.Destroy(clone); if (_label != null) UnityEngine.Object.Destroy(HudStyle.Font.material);';Failure='objective dispose leaves the font asset''s own material alone'},
    @{Name='chip-material-leak';Path=$domination;Property='DominationViewSource';Find='if (_chipMaterial != null) UnityEngine.Object.Destroy(_chipMaterial);';Replace='';Failure='objective letter, chip and chip material released on dispose'}
)
foreach($fault in $faults) {
    $count=([IO.File]::ReadAllText($fault.Path).Split(@($fault.Find),[StringSplitOptions]::None).Length-1)
    if($count -ne 1) { throw ('Fault anchor must match exactly once ('+$count+'): '+$fault.Name) }
}
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'ArenaPickupViewChecks.csproj') 'ARENA_PICKUP_VIEW_BASELINE_PASSED' @() $faults
Write-Output 'ARENA_PICKUP_VIEW_CHECKS_PASSED'
} finally { Assert-ArenaInstalledSnapshotUnchanged $installedBefore $SptPath }
