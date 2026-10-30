# GPU acceptance: Arena refinements and air control r4

Read AGENTS.md and the newest HANDOFF.md first. GPU installation C:\SPT41,
repo C:\SPT41\Development\ArenaMode. No builds, DLL replacement, production edits,
internal match calls or runtime instrumentation. Verify mirror and restart game;
server is unchanged. This job includes new air control and the four still-open
refinements. Preserve all previous instructions, reports and evidence.

## Build and safety prerequisite

- Test ID: `arena-refinements-r4`.
- Source/build commit: `0b7f38d594b09644803f263a510543293b11ed63` (clean); branch `feature/arena-air-control`.
- Client SHA256: `2042C558340877C85DAF0687A8AEBB4E7D54FD720AD467E770F5AEE7F0914ECD`.
- ProductVersion: `0.1.0+0b7f38d594b09644803f263a510543293b11ed63`.
- Server: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
- Redeploy: `FDA836C30247B01E1B3E2B19A9749DCA23FAF68C7B3A03CA5D5C35537BB8C7A8`.
- SAIN 4.5.1.0: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
- DynamicMaps 1.2.1.0: `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8`.
- Build: `artifacts/double-jump-build/f4f38f3505ba4d9ca1976ebccf2a4a5a/manifest.json`.
- Local receipt: `artifacts/air-control-delivery/2d2989b50ded4bcd93571978283a7754/`.

First complete safe case A of GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r6.md on this
exact client. Any subsequent whole-run stop blocks this job too. Earlier r5 A
was successful but does not release the later refinements r3 safety stop by itself.
Refinements r3 (`bd95b72`) observed neither a qualified corpse trim nor convincing
feature acceptance. Its bot NRE and ambiguous graphics-menu ammunition change
are diagnosed in BOT-WEAPON-CALLBACK-REVIEW.md and NATIVE-MENU-INPUT-REVIEW.md.
A targeted corrected-build r6 is required; no historical result is overwritten.

Hash before launch, before every raid and at the end. Record Git-HEAD, installed
versions, PID/start time, resolution and DLSS support. Preserve current settings
and assets. Restore only your own test values through normal controls, never
old config files or profile copies. New config descriptions/keys may be written
normally on first client launch. Do not treat this as unexplained gameplay drift.

At most 3 raids and 20 minutes active testing, loading/tool delays recorded
separately. Prefer one Customs TDM for movement/zone/diamonds/corpses, then a
needed ordinary PMC check; a third Arena raid only within budget, not to provoke
rare safeguards. Prioritize new air control before the remaining four features.
Stop safely at budget; unobserved cases OPEN/SKIP, never PASS by inference.

All r6 safety and private profile-comparison rules apply here. Compare protected
PMC/Scav fields and whole JSON after each Arena raid reaches the main menu.
An ordinary PMC raid legitimately changes its own profile: report that separately,
never restore it and never claim Arena protection governs ordinary gameplay.
No audio/mod changes. Stop the ENTIRE run for every new exception, unsafe profile
change, softlock, input lock, UI-fired shot or hash drift; regular safe exit and
save evidence, no more cases/raids. Cosmetic failures can be reported independently.

## New air control — first in the Arena raid

Startup must contain `Arena air control: native jump/fall steering ready (planar speed preserved).`
Missing marker or `Arena air control unavailable` blocks this feature acceptance;
record the reason without claiming it works. In F12 Movement set MovementEnabled
on; compare AirControlEnabled off/on with the SAME running jump, initial location,
view direction and equipment. Default AirControlDegreesPerSecond is 60; 0 restores
native direction, range 0–180, live changes. Use a safe clear space and original
short clips with actual key actions/times. Do not confuse camera turning with
trajectory correction. At fixed view, WASD should curve the horizontal flight
left/right/back gently; releasing WASD retains attained heading. Compare 0,60
and optionally120 from the ground between equivalent trials, then restore defaults
or the user's original value. No claim of exact world speed from a shaky clip.

Check jump and ledge fall, one double jump, direction change and release around
apex/descent, landing resets, and one natural respawn when available. Vertical arc
should remain native, no added height or strafe acceleration. When native horizontal
displacement is zero, air control must not add a sideways impulse; native forward
speed behavior remains intact. A wall/low
ceiling and visible contour must still block physically. A mantle remains native
with the existing conservative height/distance limits; do not raise those limits.
Inventory/pause deny new steering; after resume fresh input works. No movement
input should stick after release. Unusual >250 ms frame steps temporarily use
native direction; record stalls honestly rather than promise seamless steering.
Ordinary PMC must retain native air movement; no Arena feature in preview/Hideout.

## Existing refinements still awaiting acceptance

1. **Cleaned corpse map markers.** In Arena with DynamicMaps 1.2.1, exceed the
   normal visible-corpse budget only through normal play. If the minimap is
   disabled (as in the r3 baseline), temporarily enable it through its normal
   settings and restore afterward, or mark that subcase SKIP. The End hotkey
   alone is not proof of an enabled minimap. Move far enough away for the existing safe
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
   an ordinary PMC raid has no Arena air control/air jump/grid/diamonds and normal DynamicMaps
   corpse markers. Preserve the profile snapshot and restore temporary settings.

## Report

Create `GPU-ARENA-REFINEMENTS-TEST-RESULTS-r4.md` with build hashes, new air-control and existing refinement case-by-case
PASS/FAIL/OPEN/SKIP, map/mode/settings, short timestamped observations and relevant
logs/screenshots. Small clips are useful for the air-jump and DLSS movement cases.
Separate pre-existing warnings from new exceptions. Stop the ENTIRE run for
any new exception (including a single one), profile risk, input lock, hash drift,
reproduced firing from a UI click, or other unsafe state. Return safely and
preserve the evidence; do not start another feature case or raid after that stop.
Do not change gameplay code during acceptance. Commit the report and referenced
evidence only; do not include profiles, credentials, mutable logs or unrelated work.


Original media and SHA256 manifest: test-evidence/arena-refinements-r4/. Raw logs and profiles stay only under ignored artifacts/. Commit only your new report and evidence; preserve all previous reports.
