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
