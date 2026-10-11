# Zombie PFT prefix fast path — 2026-10-09

Source baseline: `2316e65b44467ba2077ab513c25acde879c43fea`.
Scope: only PitFireTeam's `HostileNonCombatActiveLayerFilterPatch.PatchPrefix`
for an exact current, living generic infected in the exact active Zombie raid.
The measured pre-change cost was approximately 0.22–0.27 ms/frame with 56 bots;
that is a baseline measurement, not a promised saving.

## Why this seam

The installed PFT 0.10.3 prefix reads the active list by reflection, resolves a
bot owner through a layer, determines hostility toward the leader/followers,
wakes standby, and then searches the layers for a usable combat layer. It
returns without changing the list when no layer has a matching combat name.
The six native `InfectedLayersStrategy` layers and optional native debug layer
do not match PFT's `Combat` / SAIN `Squad` name/type/namespace predicate. Native
`ZombieFast`, `ZombieSlow` and `ZombieShooting` are names returned by
`InfectedAttackLayer`, not evidence of a custom combat layer.

Arena adds its own Harmony prefix to PFT's nongeneric static prefix method.
The patch uses `object __0` to receive that method's first argument, the actual
strategy. `__instance` would be null because the patched PFT method is static.
This does not detour a constructed generic method, unregister/reorder a
foreign patch, skip the native strategy update, or affect PFT's finalizer.
PFT's unused filter state stays at the caller's original null value on this
early-return path. No PFT source was copied; installed IL is the contract and
the local source was used only to understand behavior.

## Eligibility and fallbacks

`ZombiePftFastPathEnabled` is an independently switchable default-on comparison
setting, wired by the parent implementation. Outside an active exact Zombie
raid, with the setting off, or before installation succeeds, the original PFT
prefix runs. Runtime installation accepts only matching plugin/assembly
versions 0.10.0 or 0.10.3, exact reflected method signatures, and PFT's prefix
actually registered on the expected native update. Unknown builds, missing
members or failure to compile/verify the list-version reader retain PFT.

Every use verifies the exact `InfectedLayersStrategy` type and its current
`BotOwner.Brain.BaseBrain` identity; a living active AI body; owner/body binding;
the current world's alive-profile lookup; and one of the four generic infected
roles. `infectedTagilla`, ordinary bots, custom/derived brains, destroyed and
replaced bodies all retain PFT. Follower exclusion checks both native
`BotFollower.HaveBoss` and a bound delegate to PFT's `BossPlayers.IsFollower`,
including PFT's pending-follower registry semantics.

If standby is anything other than `none` or `active`, the entire original PFT
prefix runs. PFT therefore retains its exact hostility decision and wake
behavior; Arena does not reproduce or weaken either. A null standby is already
a no-op in PFT. For awake infected, PFT's remaining layer filtering cannot
change anything when all active layer types are exactly native.

An eligibility proof is cached per strategy in a `ConditionalWeakTable` and
bound to the active list identity plus its mutation version. A startup-compiled
reader avoids reflection per call; startup probes verify add and same-count
replacement both change the installed CLR's version. Any ordinary list
insertion/removal/replacement triggers another complete scan of the active
list. The cost is linear in the list's current length per mutation, not per
frame; there is no artificial total-entry cap. Unknown layers and subclasses
retain PFT immediately. Before replacing
an old proof, its accepted flag is cleared, so a failed revalidation cannot
leave an accepted stale result. The list version is checked again after the
scan. The first layer's owner is checked each call, because that is where PFT
would obtain its owner even if that field changed without a list mutation.

The cache's weak key creates no strong root to a dead bot; the only world
reference is weak. World changes, inactive diagnostics and explicit `Reset()`
discard cached entries and counters. No corpse/profile registry is altered.
Counters are cumulative for the current world and logged at most once every
30 seconds: `calls`, `skipped`, `standbyFallbacks`, `layerChecks`,
`shapeFallbacks`, and `errors`. No per-bot/frame logging or sampling allocation
is added. Native counter hits establish actual detour execution.

### Native duplicate wait layers

The first GPU run proved the nested Harmony interception (`calls=98797`,
`skipped=95210` at 30 seconds), but exposed the initial seven-entry guard as
unnecessarily restrictive. After convergence, skipped calls fell even though
all 56 bots remained alive and active. Read-only UiBridge inspection of GPU2
instance `901f5d68` found exact `InfectedLayersStrategy` on four sampled bots
(Bot38, Bot1, Bot24, Bot14), with 15, 15, 21 and 22 active-list entries. Two
lists were expanded: each contained only repeated `InfectedWaitLayer` entries
followed by the five ordinary native infected layers.

The native source explains this: `BotHalloweenDistributions.UpdateCanAttack`
runs every 10 seconds and calls `SetCanAttack(false)` for non-shooters outside
the nearest eight infected assigned to a target. That calls `ActivateLayer(5)`;
`AICoreStrategy.method_4` inserts without a membership check. Conversely,
`DeactivateLayer` removes only one occurrence. The number of native entries can
therefore grow during a long round. The corrected fast path validates every
entry on mutation, regardless of duplicate count. It never removes, deduplicates
or changes a layer and never changes the native distribution/attack policy.
This also explains why a larger arbitrary cap would only postpone the loss of
coverage. Final-build counters and timings remain the parent's acceptance work.

## Verification and limits

The standalone source-linked harness exercises the actual production guard,
installation signatures and hook argument name. It runs 190 assertions,
including wake/no-wake states, follower roles/registries, changed current body,
changed brain, an unknown subclass, same-count replacement, a changed list,
changed first-layer owner, toggles, failures, world reset and bounded logging.
The duplicate regression uses 256 references to the same native wait layer
(261 total entries), verifies the list remains untouched and stable proof is
cached, and detects an unknown far-end layer and same-count far-end custom
replacement before accepting their removal/restoration.
The fixture models foreign/native effects without copying vendor code. The
native strategy and finalizer remain callable while only the foreign prefix
is bypassed. This is a controlled fixture, not a Unity/Mono detour proof.

`tools/ZombiePftFastPathChecks/Verify.ps1` reruns the production-linked harness,
then compiles and rejects 19 behavioral mutants: no-op, static argument
misbinding, ordinary-raid leakage, ignored toggle, wrong world, boss leakage,
stale brain/body, missed native/PFT follower, lost wake, accepted custom layer,
missed list mutation/replacement, wrong first-layer owner, cross-raid counters,
metadata mismatch, unknown PFT version and the obsolete seven-entry limit.
Compilation failures do not count
as successful negative controls. It runs the installed contract check and
refuses a verification manifest if production source changed during the run.

`Verify-NativeContract.ps1` reads the installed DLLs with Cecil. It verifies the
static private prefix signature, hostility-before-wake-before-combat sequence,
combat classification strings, follower method signature, wake call, exact
native infected constructor layer set, native names/request enum, public owner
and active-list fields, duplicate-prone native wait-layer insertion, and
dictionary-based live-body lookup. Verified inputs:

- PFT assembly `0.10.3.0`, SHA256
  `0F875F824215F68A47FFD9183FB0BDD92CBA72762E1702F458A0EE614CC6DBB9`.
- `Assembly-CSharp.dll`, SHA256
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Production `ZombiePftFastPath.cs`, SHA256
  `729829569422C0A59724D7DEFDEF65D8E752765620033C8132F422BCDE9E0F36`.

Four implementation passes: (1) complete adapter, cache and guards; (2)
domain review against installed PFT/native call order and tightening cache
revalidation failure; (3) adverse fixtures and compiled mutations, fixing the
test harness's source exclusion and Cecil opcode selection; (4) full source,
reset/retention, documentation and evidence review, with no remaining local
implementation finding. The parent owns independent review and reruns.

These offline checks do not establish FPS improvement, the cost on Mono or
native gameplay. The first GPU run has now established actual Harmony
interception; the corrected long-list behavior still needs final-build native
acceptance. Required parent evidence is an isolated client build, a restarted
source-bound GPU run with nonzero `calls`/`skipped`, a comparable toggle-off/on
measurement, and behavior checks covering infected approach/attacks and PFT
followers. No main-project build, deployment, game action or commit was made
by this implementation leaf. Server/Core and foreign binaries remain outside
its ownership.

Initial leaf verification passed both gates before the parent's native test.
That initial 183-assertion/18-fault evidence and its source hash predate the
duplicate-list correction; they are not the final source binding. The final
190-assertion/19-fault rerun is recorded separately below. The independent
review reported no blocking initial implementation finding, and the parent
requested a second review of the native-evidence-driven correction.

Corrected final verification: both leaf gates passed again (2 met, 0 unmet,
0 abandoned), 190 assertions and 19 compiled faults rejected. The manifest is
`tools/ZombiePftFastPathChecks/evidence/1ca85efb5d6c41dabcdc89702349bb2c/manifest.json`;
production SHA256 is the `729829...` value above and remained unchanged during
verification. The second independent review also reported no blocking finding.
The correction's four passes covered native duplicate evidence, exact-type and
owner/wake preservation, long-list and compiled regression controls, and final
diff/documentation/hash review. No other production behavior was changed.

Integration API: `EnablePatch()` after plugin dependencies/config initialization,
`TickDiagnostics()` during controller updates, and `Reset()` at raid cleanup.
