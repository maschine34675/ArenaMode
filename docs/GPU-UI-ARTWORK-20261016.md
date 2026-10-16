# Categorical UI artwork — native verification

2026-10-16, SPT-GPU-2, Factory daytime, independent Team Deathmatch. Production
source is committed in `4ab38f9`; the earlier staged build contains those same
production files. Client SHA256:
`428C390759C2A7E5F7A683506BB2DDC726188985B3D5BBB2FCB04075F2DD7F9C`.
Only the client DLL and three readable `ArenaMode/Ui` notices were deployed.
Server, Redeploy.Core, BigBrain, other plugins and audio were not deployed.

## Loaded build and test conditions

The host was free, outside a raid. gpu-run confirmed two matching sync polls
for all four files, restarted EFT, and UiBridge confirmed all nineteen maschine
assemblies matched VM, host disk and loaded process. UiBridge version was
`0.9.4+42bbfb38`; EFT `0.16.9.5-40743`, Unity `2022.3.43f1`, 1920 x 1080.
Only the GPU test profile was used. No profile JSON was overwritten, no items
were injected, and no infinite-ammunition hook was enabled.

The effective roster was **5v5**, despite an attempted F12 fixture of two per
team: the existing map participant preference remained authoritative. This
run therefore does not certify the participant-selector precedence. Godmode
was enabled after the real opening kit swap, then explicitly disabled before
leaving. Real navigation and safety-checked teleports approached existing enemy
bots; aim tracking and native trigger input fired the issued VSS. One navigation
attempt stopped as stuck and was not counted as arrival. No fake kill or HUD
event was injected.

## Visual results

- The native killfeed displays the new assault-rifle, marksman, shotgun and SMG
  silhouettes. Own and other kills retain complete names, team colors, distances
  and weapon labels. Symbols stay inside the existing icon rail and dark row.
- A real VSS kill shows the orange headshot badge and the new white skull above
  the crosshair. Subsequent frames and a later still show normal expiry. The
  match finished with two local kills, zero deaths and 500 points.
- F9 shows the original blue and red team banners, with the K/D/A/HS symbols
  visible and aligned beside their labels. The existing score columns and row
  layout remain readable against the real Factory background.
- No Arena asset-load, PNG-decode, sprite/texture or HUD exception appears in
  the captured client/server logs.

The successful capture has 98 encoded frames and a complete index; selected
actual frames were copied locally. Two earlier shot captures did not establish
a kill and hit their frame limit; they are not successful skull evidence.
Screenshots are real game backbuffer frames, not the offline mockup.

Private evidence is ignored beneath `artifacts/ui-art-replacement-20261016/native/`:
`scoreboard.png`, `attempt-frame.png`, `after-kills.png`, and
`confirmed-kills/frame-0057-f42237.png` with its complete `index.json`.
The last file shows the local headshot, category icon and confirmation skull.

## Cleanup and profile boundary

Protected disconnect completed and returned to MainMenu. The server recorded
snapshot restoration and protected end at **12:31:27 local time**, explicitly
skipping ordinary post-raid processing. No healing purchase was performed.
All Arena config key/value pairs match the pre-test copy; the restore recipe
initially encountered this session's own short-lived control lease, then passed
on retry after expiry. Final status has no active raid, no godmode/tracking and
free control; loaded assemblies still match disk/VM.

The before/after inventory contains the same **401 items**. Two pre-existing
missing `StackObjectsCount` fields were set to 1 by SPT's ProfileFixer at
12:22:37, before the 12:25:50 Arena snapshot. After accounting for exactly those
two logged startup repairs, the complete inventory objects are equal.
Other differences are energy/hydration 97 to 100, Health.UpdateTime and the
Hideout run timestamp; no other profile values differ. This is not a claim that
the raw full-profile files are byte-identical or that every menu-time health
change was caused by Arena. No test restoration wrote to the profile directly.

## Warnings and limits

The captured merged window has 85 warning/error entries: 31 BepInEx warnings,
nine server warnings and 45 EFT trace errors. BepInEx has no Error/Fatal entries
in that window. The known intentionally throwing RespawnApiProbe subscriber and
stale PerfectCulling camera warning are present, not filtered out. Other entries
include absent optional Fika references, third-party Harmony target warnings,
missing Factory scene scripts/negative collider scale, server profile-display
fallbacks, two startup item repairs and Fence plate warnings.

EFT traces include `JetMaterial == null`, map-marker repairs, missing foldable
or utility-animation events, handler-hash messages, a grenade FastForward
message, GetClosePoints and one unknown-custom-item-type message. This bounded
artwork run did not establish their causes or certify the whole mod set as
error-free; none reports a replaced UI resource or the changed loader.

The gpu-run Team DM recipe still waits for the old text `TEAM DEATHMATCH` while
the current menu says `TEAM DM`. It timed out before loading; the test continued
through observed FACTORY/NEXT/READY controls. No UiBridge source was changed.

Native coverage is one Factory TDM run, not every weapon category, rare damage
cause, screen resolution, map or mode. All eighteen assets additionally passed
the independent pixel/provenance and actual-size checks in the
[implementation record](UI-ARTWORK-REPLACEMENT-20261016.md).
