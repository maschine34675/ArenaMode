# Bot spawn protection is restricted to Team DM boxes

The finite **bot** spawn-damage shield now applies only to independent Team DM on
Equator, Bay5 and AirPit. These are the maps with catalog-backed, mandatory team
boxes for both first spawns and replacements. Factory day/night, Labs, Labyrinth,
the Customs areas and every other non-box map receive no bot spawn shield.

`TeamDeathmatchMode.OnMatchStart` first resets its frozen bot-shield duration to
zero. After the existing exact active game/world, local identity and independent
roster checks succeed, it queries `ArenaTeamBoxCatalog.Applies` with the **captured
world's LocationId** and active mode. This is the same catalog used by
`ArenaTeamBoxRuntime.IsRequired` and native burst placement. Box readiness is not
an admission signal: it can still be assembling at this point, and the runtime
never falls back to map-wide starts after box negotiation.

No second map-name list, menu preference or armed-next-raid value is authoritative.
The scope and duration are frozen once, so editing the protection setting during
a match neither grants Factory replacement bots a shield nor extends later
ported-map replacements. Existing exact match/body registration, attack release,
expiry and teardown checks remain unchanged. Zero/invalid duration still grants
no protection; the existing lease caps excessive durations at 30 seconds.

This change does **not** alter the player's protection, Team DM staging/results
damage suppression, pickup Shield buffs or other modes, including PFT Tactical
and PFT Zombies. It does not modify native health coefficients.

## Verification

`tools/SpawnShieldScopeChecks/Verify.ps1` passes 225 production-linked behavior
checks and rejects five compiled faults. Both factions on all three ports are
positive controls; negative controls include Factory day/night, all other listed
maps, other modes, PFT/unarmed worlds and stale active-match ownership. Settings
changes, replacement bodies and invalid/excessive durations are covered. The
test compiles real mode/catalog/lease sources against explicit engine doubles.

This is scope and lifetime evidence, not native visual/damage acceptance. Parent
integration must separately build the client and test a ported Team DM plus a
Factory Team DM in EFT, preserving the local-player protection behavior. Shared
indicator rendering is reviewed in `BOT-EFFECT-OUTLINES-20261019.md`.

## Note 2026-11-13: Domination

Domination has had the same scope by rule since 2026-10-26 (`ArenaTeamBoxCatalog.Applies` accepts
`domination` on the six box maps, Factory and the other maps get none). The shield never took effect
there, though: the duration was frozen and bots were tracked, but the three phase reads in
`TeamDeathmatchMode.SpawnProtection.cs` used the Team DM kill policy (`_rules?.Phase`), which is null in
Domination. Damage was never refused, the lease clock never started and no visual was created. The reads
now use the mode-neutral `MatchPhase`. Scope, duration, first-attack release and visual are unchanged.

Evidence: `tools/TeamDeathmatchRuntimeChecks` replays every bot-shield case in a live Domination match
on Equator (three mutants revert one read each and die at a `DOM ...` assertion);
`tools/SpawnShieldScopeChecks` pins Domination on all six box maps (shield, lease ends on time) and on
Factory day/night (no shield). Native, the same day on GPU-2
([report](../reports/2026-11/GPU-PRE-RELEASE-POLISH-20261113.md)): on Chop Shop the team bots carry the shield
outline from the first second of the live match and not while the teams assemble; on Equator a replacement bot
took fifteen tracked rounds to the head 2.8 to 3.8 s after its arrival and lived, while the same burst killed a
team bot that had been alive for a minute and an opposing bot.
