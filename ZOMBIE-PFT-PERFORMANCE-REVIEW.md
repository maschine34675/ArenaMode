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
insertion/removal/replacement triggers another bounded scan (at most seven
layers). Unknown layers and subclasses retain PFT immediately. Before replacing
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
is added. Native counter hits must establish actual detour execution.

## Verification and limits

The standalone source-linked harness exercises the actual production guard,
installation signatures and hook argument name. It runs 183 assertions,
including wake/no-wake states, follower roles/registries, changed current body,
changed brain, an unknown subclass, same-count replacement, a changed list,
changed first-layer owner, toggles, failures, world reset and bounded logging.
The fixture models foreign/native effects without copying vendor code. The
native strategy and finalizer remain callable while only the foreign prefix
is bypassed. This is a controlled fixture, not a Unity/Mono detour proof.

`tools/ZombiePftFastPathChecks/Verify.ps1` reruns the production-linked harness,
then compiles and rejects 18 behavioral mutants: no-op, static argument
misbinding, ordinary-raid leakage, ignored toggle, wrong world, boss leakage,
stale brain/body, missed native/PFT follower, lost wake, accepted custom layer,
missed list mutation/replacement, wrong first-layer owner, cross-raid counters,
metadata mismatch and unknown PFT version. Compilation failures do not count
as successful negative controls. It runs the installed contract check and
refuses a verification manifest if production source changed during the run.

`Verify-NativeContract.ps1` reads the installed DLLs with Cecil. It verifies the
static private prefix signature, hostility-before-wake-before-combat sequence,
combat classification strings, follower method signature, wake call, exact
native infected constructor layer set, native names/request enum, public owner
and active-list fields, and dictionary-based live-body lookup. Verified inputs:

- PFT assembly `0.10.3.0`, SHA256
  `0F875F824215F68A47FFD9183FB0BDD92CBA72762E1702F458A0EE614CC6DBB9`.
- `Assembly-CSharp.dll`, SHA256
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Production `ZombiePftFastPath.cs`, SHA256
  `AA9E459A22788B6892E9E5CFB9B3BF89FE407A76205BE35FB8AA84779B6D2402`.

Four implementation passes: (1) complete adapter, cache and guards; (2)
domain review against installed PFT/native call order and tightening cache
revalidation failure; (3) adverse fixtures and compiled mutations, fixing the
test harness's source exclusion and Cecil opcode selection; (4) full source,
reset/retention, documentation and evidence review, with no remaining local
implementation finding. The parent owns independent review and reruns.

These checks do not establish FPS improvement, the cost on Mono, actual
Harmony interception of a prefix already called by another wrapper, or native
gameplay. Required parent evidence is an isolated client build, a restarted
source-bound GPU run with nonzero `calls`/`skipped`, a comparable toggle-off/on
measurement, and behavior checks covering infected approach/attacks and PFT
followers. No main-project build, deployment, game action or commit was made
by this implementation leaf. Server/Core and foreign binaries remain outside
its ownership.

Final leaf verification passed both `leaf-1.1:G1` and `leaf-1.1:G2` with
machine-bound ledger evidence (2 met, 0 unmet, 0 abandoned). The final mutation
manifest is in
`tools/ZombiePftFastPathChecks/evidence/2a70cd09a4274607aeea65ef189cb1bf/manifest.json`:
183 assertions, 18 compiled faults rejected, installed contract passed, and
the production hash above unchanged throughout verification. Earlier exploratory
evidence is not the final source binding. The independent review reported no
blocking PFT implementation finding; native wrapper execution remains a parent
acceptance gate.

Integration API: `EnablePatch()` after plugin dependencies/config initialization,
`TickDiagnostics()` during controller updates, and `Reset()` at raid cleanup.
