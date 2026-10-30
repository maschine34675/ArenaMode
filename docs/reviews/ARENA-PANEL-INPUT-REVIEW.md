# Arena panel close input review — 2026-09-12

## Evidence and diagnosis

`228d892` and `GPU-DEATHMATCH-MENU-TEST-RESULTS-r4.md` confirm R4-01:
the second deliberate F9 Close click changed MP-153 ammunition from `4/4 +1`
to `3/4 +1` and produced a new wall impact. The paired original screenshots
and recorded input actions contain no intervening deliberate fire, reload,
weapon change, death or respawn. The first trial was unchanged. The original
r4 evidence remains immutable; this review does not reinterpret its stopped
run as a completed Deathmatch acceptance.

Installed native Assembly-CSharp SHA256:
`EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
Independent source/IL review verified:

- `GUILayout.Button` reaches Unity `GUI.DoControl`. It takes mouse control on
  MouseDown and reports a successful click on MouseUp, after `Event.Use()`.
  Consuming that IMGUI event does not clear the native game command list.
- `InputManager.Update` collects input through `InputPreset.UpdateInput`, may
  dispatch a separate empty list for SuperSmooth, then delivers the real batch
  in Update or FixedUpdate according to its configuration. Installed client
  defaults to Update. A FixedUpdate delay is **not** the established r4 cause.
- A non-null `_workingKeyCombinations` array causes old commands to be cleared
  (if nonempty) and current commands rebuilt. An uninitialized preset does not
  clear the list. Normal batches do not accumulate indefinitely.
- The existing panel code removed its last blocking owner immediately in
  `SetOpen(false)`. A command delivered around that boundary could therefore
  escape filtering. Exact native sample/delivery timing of the failing click
  was not instrumented in r4; the correction protects that boundary without
  claiming a demonstrated deeper engine scheduling defect.

The observed Shoot binding was Mouse0/Continuous, Aim Mouse1/Press. There is no
evidence that a release-to-shoot binding explains this case.

## Correction and lifetime

F9 and the Tactical buy panel explicitly opt into a pending close release.
Removing a real UI owner keeps the existing command/axis filter active until
one **newly collected** batch, with all three standard mouse buttons released
and no same-frame mouse-down, has passed through the actual command delivery.
Only then can a subsequent deliberate click shoot. No arbitrary frame/time
delay, native key-state mutation, or cursor trick is used.

The filter preserves the existing End/Stop/Release/Escape name whitelist.
This heuristic is not a complete catalogue of native completion commands:
for example FinishHighThrow and RestorePose were already outside it. This
fix does not broaden those commands or claim to solve every existing panel
interaction. Escape and EndShooting remain admitted during the close drain.

Release evidence must match the current main manager, preset, actual command
list and axes. SuperSmooth's separate empty delivery cannot consume it; a
fresh empty **real** batch can. Reopening, a new owner or a second close
invalidates earlier evidence. A generation token prevents reentrant UI work
during native delivery from releasing a newer close. Other input owners such
as area preparation and spectator input retain their ordinary release behavior.

Every owner/sample/delivery is confined to the exact active Arena game/world.
Scope loss clears votes and pending input; ordinary raids and Hideout do not
receive the filter. Manager/preset/list/axes replacement discards pending old
input while preserving a still-open UI's vote. Plugin shutdown clears state.
If installation or detour removal fails, inactive callbacks cannot filter
input and cleanup still runs. An independent review found that cleanup failure
case before delivery; its correction is included here.

Runtime diagnostics are bounded to 24 state lines per game process:
`Arena panel input: close ... pending` and `... released`, including sample
number and filtered `ToggleShooting` count. Zero filtered shooting commands is
not a failed test: a correct quiet close need not produce one. Reopening can
invalidate a pending generation without a release line for that old generation.
Ready marker: `Arena input blocker ready: panel close waits for released-pointer input delivery.`

## Verification and remaining acceptance

`tools/ArenaInputChecks/Verify.ps1` compiles the production blocker with native
input I/O and Harmony recording doubles. It checks owner/scope transitions,
held/released buttons, stale and foreign samples, smooth/real deliveries,
reentrant close/open, replacement, failed installation and shutdown. Compiled
fault variants must fail their intended assertion, not compilation. This does
not run native IMGUI, the actual detour engine, or the EFT animator.
The reviewed implementation passes 60 assertions and rejects 14 compiled faults;
the existing result/preparation banner baseline also passes all 44 assertions.

`Verify-Contracts.ps1` independently reads the installed game assembly for
exact member contracts and the collection/dispatch boundaries. Independent
adversarial implementation/test review passed after the cleanup correction.
Full native client compilation and exact client-only delivery must be recorded
with the final build in HANDOFF and the r5 test job before GPU testing.

r4's manual disconnect triggered the null-only SAIN teardown guard once and
reported no new exception. Pending-pickup cancellation was unobserved. The
three-round automatic exit, final F9/banner visibility, frozen score, FFA and
Continuous/Give-up cases remain open because testing stopped at R4-01. They
must not be marked passed from the clean manual exit or these offline checks.

r4 protected PMC/Scav fields were unchanged; its sole complete-profile delta
was a Hideout heartbeat timestamp. Current settings include legitimate r4
description metadata updates and must not be restored to earlier hashes.
No server, profile, other mod, audio, corpse or gameplay rule change belongs
to this correction. The four prior refinements remain a separate GPU job.
