# GPU acceptance: Arena refinements r1

Read `AGENTS.md` and the newest `HANDOFF.md` entry first. This job is separate
from `deathmatch-menu-preview-r3`: finish and preserve that report before changing
its client DLL. Do not reinterpret r3 evidence as acceptance of these features.

The GPU installation is `C:\SPT41`; the development installation is `D:\SPT41`.
The client below is staged only, not installed by the development task. Begin
native testing only after this exact DLL has been delivered and the game restarted.
No server change, audio replacement, dependency bundle or default change is needed.
Do not run the solution build just to deploy this client: it also replaces the server.

## Build identity

- Test ID: `arena-refinements-r1`.
- Production source: `c151aa683a7bcb83eec8d0183f36fc19ab8ce18a` (clean tree at build).
- Staged client: `artifacts/double-jump-build/146ebfa928204a548a5c314c528e431c/out/maschine-ArenaMode.Client.dll`.
- Client SHA256: `3FFF60D7EA67B7CF40046421362F93BED3A87C984449F0C62B518026344F3FF6`.
- ProductVersion: `0.1.0+c151aa683a7bcb83eec8d0183f36fc19ab8ce18a`.
- Manifest and live-integrity receipt: same stage directory, `manifest.json` and `live-integrity.json`.
- Existing unchanged server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
- Existing Redeploy.Core SHA256: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`.
- Old still-installed r3 client SHA256: `4F33B337108987C5471E42FDD706F57D61421CB05D9AAD25186A592366130473`.
- Full client build: 0 errors, 4 existing nullable warnings. No native GPU acceptance yet.

The concurrent r3 report `ee0a421` is preserved. Its recorded C2 exit failures
were not triaged by this feature task; assess that separate report before any
new acceptance raid. This checklist does not release the r3 stop or authorize
new acceptance raids before that separate C2 triage. Do not label those old-client failures as a result
of the new staged client. The report and movement feature histories are both
retained by merge `3f9052b`.

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

Create `GPU-ARENA-REFINEMENTS-TEST-RESULTS-r1.md` with build hashes, case-by-case
PASS/FAIL/SKIP, map/mode/settings, short timestamped observations and relevant
logs/screenshots. Small clips are useful for the air-jump and DLSS movement cases.
Separate new reproducible failures from pre-existing warnings; stop a case for
profile risk, input lock or persistent new exceptions and preserve its evidence.
Do not change gameplay code during acceptance. Commit the report and referenced
evidence only; do not include profiles, credentials, mutable logs or unrelated work.
