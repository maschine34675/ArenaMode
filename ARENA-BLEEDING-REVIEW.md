# Arena bleeding balance — 2026-09-09

Light and heavy bleeding now default to **3 times native HP loss** for the
current living local player and active bots in an Arena match. F12 → Character
→ `BleedingDamageMultiplier` accepts 1–10; 1 preserves native damage. This is an
initial balancing choice, not an assertion that every mod combination needs 3.

The existing `TeamStagingDamagePatch` adjusts only the `ref float damage`
argument of `ActiveHealthController.ApplyDamage` after the protected-team and
friendly-fire vetoes. Native protection, multipliers, body-part distribution,
destruction rules, networking and attribution continue through that method.
Treatment, effect lifetime, shared `DamageInfo`, bleed chance, other damage and
normal raids are unchanged. No profile, global health config or effect timer
is rewritten.

Admission requires the exact current Arena game/world, started unfinished
match, current health owner, living registered body and active AI owner or exact
current local body. A local respawn excludes only that local body; bots still
bleed during spectator play. Invalid input/factor and overflow fail open.

Native source anchors under `../_decompiled/EFT.HealthSystem/ActiveHealthController.cs`:
`Bleeding.BleedHealth` around 707–711 passes the base amount afresh for each limb;
`ApplyDamage` around 3347 applies the native transaction. Bleeding is self-inflicted
and skips overdamage. Neither later ticks nor native destruction/kill callbacks
recursively multiply this argument. The native special head/thorax bleeding
death semantics are deliberately retained.

Verification: `tools/ArenaBleedingChecks/Verify.ps1` links the actual helper and
extracts the actual production health prefix verbatim. Host checks cover both
bleed types, sequential limbs/ticks, overflow, unrelated damage, exact body
ownership, local-respawn/AI separation and veto-before-adjustment ordering.
Six deliberately broken source copies must fail their intended assertions;
production sources are not edited by verification. These are API doubles,
not Unity execution. Independent source review found no unresolved P0–P2.

User GPU acceptance remains: compare light/heavy HP loss, treatment and respawn
protection in Arena, then verify unchanged ordinary PMC bleeding. Restart the
game for the new client; no real raid was run on the GPU-less development host.
