# GPU acceptance: Arena refinements r2

Read `AGENTS.md` and the newest `HANDOFF.md` entry first. This job is separate
from the completed `deathmatch-menu-preview-r3` report and its focused r4 retest.
Preserve both reports; neither is acceptance of these four features.

The GPU installation is `C:\SPT41`; development is `D:\SPT41`.
This revision shares the locally delivered r4 client including the four
refinements and the r3 exit/result safeguards. Verify the GPU mirror and restart
the game first. No server/audio/config replacement and no solution build.
The old r1 instruction is historical and binds a different, superseded DLL.

## Build identity

- Test ID: `arena-refinements-r2`.
- Source/build commit: `39bd7882713da14afcc2432cad092a6c9e1919f6` (clean at build).
- Client SHA256: `9D27F9A0EA9F76805E33F3F9FF5CBA2AF8821A4C210CFB6822EDA8EDD6090BD9`.
- ProductVersion: `0.1.0+39bd7882713da14afcc2432cad092a6c9e1919f6`.
- Build/manifest: `artifacts/double-jump-build/f51871e16b864fdeb72337aab1d0c367/`.
- Delivery receipt: `artifacts/deathmatch-r4-delivery/529c55a9d656423692b4cdf1e1131d74/`.
- Unchanged server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
- Unchanged Redeploy SHA256: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`.
- Full client build: 0 errors, 4 existing warnings. No GPU proof here.

The r3 report `ee0a421` was triaged in `DEATHMATCH-R3-REVIEW.md`.
First pass the focused A exit regression in
`GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r4.md` without a new exception or unsafe
profile change. If it fails, stop: this separate feature checklist does not
bypass that stop. Do not count the old r3 raids as acceptance of these features.

Compare SHA256 on the GPU before launch; filename/version alone is insufficient.
Record installed DynamicMaps and DLSS availability. Never mark a skipped case PASS.

## Bounded checks

1. **Cleaned corpse map markers.** In Arena with DynamicMaps 1.2.1, exceed the
   normal visible-corpse budget and move far enough away for the existing safe
   cleanup rules. Match `Arena corpse cleanup: trimmed ...` with disappearing
   skulls of those bodies on full map and minimap. Reopen the map and toggle the
   corpse category: hidden bodies must not return as skulls; recent/untrimmed
   bodies must retain normal marker behavior. Do not expect the latest own body
   or protected PFT bodies to be trimmed. No manual native corpse destruction.
   Startup should log `Arena DynamicMaps: trimmed-corpse marker cleanup ready (1.2.1).`
   Missing/other DynamicMaps versions are an optional skipped compatibility case,
   not proof of this feature. Check normal maps again in the ordinary-raid case.

2. **Air jump after falling.** With movement and double jump enabled, walk off
   a tiny edge and a clear ledge without first jumping; press the normal jump
   binding while airborne. Repeat standing/walking/sprinting where practical.
   Check one upward impulse, no repeated impulses from repeated input, then
   renewed availability after landing. Also retain ground jump -> extra jump
   during ascent, near the apex and during descent; check a low ceiling/wall
   still stops movement rather than allowing a collision bypass.
   Toggle the setting while on the ground (next flight works), and off/on while
   airborne (must not refill a spent jump). Check one respawn and input while
   inventory/menu is open. Existing mantle behavior and conservative height/
   distance limits are unchanged; do not raise them to make this test pass.
   After a tiny fall, opening/closing inventory and jumping on the ground must
   not leave a stale air-jump request that cancels the normal ground jump.
   A failed animator transition or unsupported movement state should refuse
   safely, without a stream of stamina debits or a later surprise impulse.
   For an observable debit check, temporarily turn InfiniteStamina off and
   compare one accepted extra jump with repeated denied presses. Restore it
   afterward; its default hides stamina changes, so an unchanged HUD with that
   setting on is not evidence of correct debit behavior. If unchanged fall
   consequences are checked, temporarily turn NoFallDamage off for a safely
   bounded fall and restore it afterward. Otherwise mark these cost subcases SKIP.

3. **Contour grid approach.** Use the existing Customs contour in independent
   Team Deathmatch. From its interior, far sides should be invisible. Approach
   a long side near its middle, a diagonal side and a corner: fade begins below
   20 m and reaches the previous opacity at 6 m. Retreat and repeat. Only nearby
   sides appear; distance is measured to the finite wall segment, not its center.
   Player collision, bots staying inside, team spawns and normal round flow must
   remain intact while the mesh is transparent. Moving upstairs/downstairs must
   not add a floor/ceiling or change side visibility at the same horizontal spot.
   The local body controls this effect; a detached camera is not the observer.

4. **Friendly diamond alignment.** In TDM compare the same visible teammate
   with DLSS off, Quality and another supported quality setting. Use center and
   screen-edge positions, movement, crouch/prone and an optional resolution
   change. Diamonds must stay above the actual current head with the existing
   offset, not shift toward the lower-left reduced-render corner. Repeat after
   local respawn/camera replacement and, if available, in Tactical 5v5 spectator
   view. Occluded/enemy/dead bodies must not gain markers. If the GPU cannot run
   DLSS, explicitly SKIP its alignment test rather than using a math check as proof.

5. **Exit and normal raid.** Exit through the normal protected Arena path,
   inspect logs for new exceptions and unchanged profile behavior, then confirm
   an ordinary PMC raid has no Arena air jump/grid/diamonds and normal DynamicMaps
   corpse markers. Preserve the profile snapshot and restore temporary settings.

## Report

Create `GPU-ARENA-REFINEMENTS-TEST-RESULTS-r2.md` with build hashes, case-by-case
PASS/FAIL/SKIP, map/mode/settings, short timestamped observations and relevant
logs/screenshots. Small clips are useful for the air-jump and DLSS movement cases.
Separate pre-existing warnings from new exceptions. Stop the ENTIRE run for
any new exception (including a single one), profile risk, input lock, hash drift,
reproduced firing from a UI click, or other unsafe state. Return safely and
preserve the evidence; do not start another feature case or raid after that stop.
Do not change gameplay code during acceptance. Commit the report and referenced
evidence only; do not include profiles, credentials, mutable logs or unrelated work.

