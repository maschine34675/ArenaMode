# Assist scoring checks

`pwsh -NoProfile -File tools/AssistChecks/Verify.ps1` compiles the full production
match, scoreboard, kill/assist DTOs, scoring rules and combat awards. Native player
identity/time and unrelated mode implementations use the explicit existing
`AnnouncerCombatChecks/Stubs.cs` boundary doubles. No game or server deployment
target is referenced.

The scenarios exercise proportional PMC and other role values, downward rounding,
several helpers, unknown damage in the denominator, healing, zero-point shares,
the unchanged full killer reward, headshot exclusion, FFA participant identity,
local and authenticated PFT replacement bodies, killer exclusion across PFT
aliases, archived ordinary-bot totals, duplicates, ineligible/friendly casualties,
invalid identity/damage, round/end resets, phase rejection and both memory bounds.
Match events prove that assists refresh the local score without creating another
kill, mode callback, streak, callout or audio cue; a throwing presentation listener
does not block the real kill or its mode callback.

Three **compiled** fault controls must fail their intended behavioral assertion:

- omit unknown damage from the denominator;
- remove the final killer's assist exclusion;
- carry old damage across an award-round reset.

Each variant uses its own build directory under ignored `artifacts/assist-checks`.
The script records source hashes and installed client/server/Core hashes and
rejects concurrent changes. Its final manifest is evidence for that exact source
snapshot, not a general assertion that a later checkout is verified.

`Verify-Regressions.ps1` additionally runs the existing announcer, real mode,
scoreboard/give-up, PFT, role, Tactical and TDM checks plus the unit suite. Server
unit builds use two unique nonexistent deployment destinations, and the installed
binary hashes must remain unchanged.

These tests do not prove Harmony dispatch, native health-loss measurement,
hostility attribution or Unity HUD appearance. The independent native capture
checks cover that adapter separately; a real raid remains necessary for visible
assist presentation and gameplay acceptance.

## Independent scoring review

The independent review identified and reproduced three fail-closed gaps, fixed in production:

1. A damage record that said `AttackerIsLocal = false` could still resolve to the
   local row. Identity flags now have to agree exactly with the established row.
2. TDM/Deathmatch phase rejection returned before consuming that victim's pending
   damage. Those returns now forget the exact victim ledger, so an admitted later
   callback cannot turn a staging casualty into an assist.
3. A final grenade kill from an already compacted ordinary helper recreates its
   scoreboard row. Reference equality missed its earlier contribution and granted
   self-assist; the exclusion now compares the immutable logical key as well.

The review verified that ordinary-history compaction preserves the original
managed contributor reference. A test forces that row out of the retained top and
recent history while its victim remains alive; settlement adds its assist exactly
once to archived team totals without recreating a participant.
Contributions before and after a later row recreation are also tested together:
the immutable logical key gets one assist and one rounding of combined damage,
credited once to the current row instead of duplicated across row instances.

No remaining blocking scoring defect was found within this source-linked scope.
