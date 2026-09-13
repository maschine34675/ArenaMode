# GPU menu test: Customs area map preview r1

Prepared 2026-09-13. **Not released for execution yet. Do not deploy while the
user's current tests are running.** This is a later menu-only acceptance task,
not authorization to replace their bound build or start autonomous raid tests.

## Bind the eventual candidate

After the user authorizes installation, use the clean-source delivery manifest
from `tools/AreaMapDelivery/Build-Staged.ps1`. It records the exact SourceCommit,
client/server SHA256 and map content path. Both DLLs must be installed for the
new shared `customs-factory-east-polygon-v1` ID, with the map's attribution,
license and sources preserved. Restart the client and server after installation.

Before accepting evidence, compare those recorded hashes with the actual client
and server under `C:\SPT41`, and record the source commit, manifest, hashes,
game resolution and UI scaling in the report. Do not rebuild mid-test or infer
the running build from the checkout. An old currently installed candidate is
not valid for this task. If hashes differ, stop and report the mismatch.

Read `AGENTS.md`, `HANDOFF.md` and `AREA-MAP-PREVIEW-REVIEW.md` first. Follow the
local computer-use workflow and preserve other sessions' files and reports.
Do not change or remove another mod to make this test pass.

## Menu checks

1. Open ARENA, select Team Deathmatch and Customs. The left area panel offers
   four entries, including **Factory East (contour)**. Record one full-screen
   image with the new area selected. All area rows, native map controls,
   time selection and Back/Next/Ready controls must remain visible and usable.
2. Check AREA for both measured contours. The selected polygon is blue, its
   interior map is brighter than its surroundings, and the full contour fits
   the preview. Buildings, roads and boundaries remain aligned when switching
   to OVERVIEW. Use visible map landmarks; do not treat an attractive image as
   proof of geometric alignment. Record both views of Factory East and the
   center contour.
3. OVERVIEW shows the whole Customs ground floor. Other available geometries
   are grey. The center contour/team-start variants share the same footprint
   and must not create two offset or thick duplicate boundaries. The legacy
   rectangle remains a distinct available outline. Return to AREA: neighboring
   grey contours disappear and the selected contour is framed again.
4. Toggle AREA/OVERVIEW repeatedly, then wait at least ten seconds. The camera
   choice must persist during periodic menu refresh. Changing the selected
   area starts in AREA view. Camera buttons must not change area/team size/mode.
   Factory East stays fixed **2v2**; the center team-start variant retains its
   existing 2v2–5v5 selection. Do not launch a raid to check this menu behavior.
5. Move forward to the next screen and return without starting the raid. Repeat
   ten open/back cycles, including switching between the two contours. The
   preview must appear only once, with no flicker or growing duplicated text,
   controls or artwork. The selected settings must stay consistent with the
   existing menu behavior. Record any restoration/change instead of assuming
   it is intended.
6. Switch to Factory, Labs and Labyrinth: the Customs artwork is absent and
   native map information is restored. Return to Customs; the preview works
   again. Return to character selection and inspect ordinary PMC and Scav:
   neither must display the Arena preview or leave native information invisible.
7. Inspect at the actual test resolution and, if supported by the UI without
   restarting, at 1920x1080 and a smaller supported resolution. Record each
   exact resolution/scaling. Look for cut-off attribution/captions, distorted
   image aspect, unclipped map content, overlapping buttons and unreadable fonts.
   Restore the user's original display setting afterward. Mark unavailable
   resolutions as untested, not passed.

## Evidence and failure handling

Save full-screen screenshots and a short clip of AREA/OVERVIEW plus a back/return
cycle. Review the clip for flicker and alignment; screenshots alone cannot prove
temporal stability. If recording is unavailable, document that limitation.
Read only the log interval belonging to this build/test and report counts for:

- `Arena area map preview unavailable`;
- duplicate CanvasGroup or other component errors;
- missing texture, shader/material, MissingReference or NullReference failures;
- lost native controls, invisible map information or menu exceptions.

Preserve unrelated known log errors separately with timestamps and stacks. Stop
on a reproducible new menu failure and capture the screen, exact steps and log.
Do not patch, rebuild or deploy to continue the same acceptance run. Write a
dated report with a pass/fail/untested row for each check, linked evidence, exact
build binding and limitations. Commit the report/evidence according to the repo
workflow; do not include unrelated modifications. No raid/profile claim follows
from this menu test.

## Separate user-owned raid acceptance

The user later tests Customs / Factory East / independent TDM 2v2: initial and
respawn positions inside the eleven-point contour, both teams' ability to reach
combat, no stuck spawns or boundary escape, unlimited height as intended, match
end/leave and unchanged protected profile. Capture candidate/path failures from
the log rather than weakening readiness checks. The menu's polygon image and
the database candidate counts cannot certify any of these runtime outcomes.
