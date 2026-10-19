# Remove The Dead: scoped Arena cleanup pause

Reviewed 2026-10-19 against Arena baseline `ab5243c`. Remove The Dead stays optional.
Its corpse/nearby-weapon destruction is skipped in an exact protected Arena world;
ordinary PMC/Scav raids and hideout behavior are unchanged. Arena continues its own
corpse cleanup, including its existing retained-body safety checks.

## Reviewed release and concrete conflict

- [Forge listing](https://sp-mod.com/mod/1551/remove-the-dead): SPT 4.1.3 / EFT 40743.
- [Official 4.1.3 release](https://github.com/jbs4bmx/RemoveTheDead/releases/tag/4.1.3),
  source commit `4dc3aad5c6856e213795ff7cbdef48338ef363a0`.
- Actual published assembly `RemoveTheDead`, version `4.1.3.0`, type
  `RemoveTheDead.RTDPlugin`, **case-sensitive runtime GUID
  `com.jbs4bmx.RemoveTheDead`**. The Forge display uses lower case; discovery uses
  the real BepInPlugin identity. The released plugin requires `com.SPT.core` 4.1.3.
- ZIP SHA256: `08c7218f55d94f39441c310076538d112ab1bdfe2486bc2af5204cd40bc961ed`.
  DLL SHA256: `39ae97546f4359dbdfd1f369de3e070f6f17e2db5d21d4ab25d023e20123ba66`.

The [published configuration](https://github.com/jbs4bmx/RemoveTheDead/blob/4dc3aad5c6856e213795ff7cbdef48338ef363a0/project/RemoveTheDead/RTDPlugin.cs#L44-L64)
defaults to automatic cleanup enabled, 15 minutes and a 30 m distance threshold,
with an F5 shortcut and an F12 action. Its timer belongs to the plugin rather than
one raid. Both [automatic and manual routes](https://github.com/jbs4bmx/RemoveTheDead/blob/4dc3aad5c6856e213795ff7cbdef48338ef363a0/project/RemoveTheDead/RTDPlugin.cs#L79-L157)
select dead BotOwners and call the private common method
`RemoveBotAndWeapons(BotOwner)`.

That [method and its weapon helper](https://github.com/jbs4bmx/RemoveTheDead/blob/4dc3aad5c6856e213795ff7cbdef48338ef363a0/project/RemoveTheDead/RTDPlugin.cs#L160-L186)
destroy `bot.gameObject` and every weapon LootItem GameObject within 3 m. They do
not consult Arena's release policy or distinguish an Arena weapon pickup from
ordinary dropped weapons.

This overlaps two concrete protections:

- Tactical/PFT retains exact Player/BotOwner references until the round or match
  releases them. Destroying a frozen opponent prematurely can produce the
  identity failure already reproduced in [the Tactical corpse review](TACTICAL-CORPSE-REVIEW.md).
  `CorpseCleanupService.MayRelease` also protects pending damage sources, retained
  local bodies, interactions and ragdolls. A foreign cleanup bypasses those guards.
- `ArenaWeaponPickup.TryCreate` creates a native weapon LootItem using
  `CreateLootWithRigidbody`. A cleanup candidate within 3 m can therefore cause RTD
  to destroy that pickup's native object. Its item owner may survive, so this audit
  does not claim that every such pickup necessarily becomes unrecoverable.

Native `Player.OnDestroy` does call `Dispose`; this is not a claim that destruction
does nothing. However, it bypasses Arena's release decision and the deliberate
`DestroyInfo` / player-dispose / corpse-record / `GameWorld.DestroyLoot` sequence.
Destroying a loot GameObject directly also differs from `GameWorld.DestroyLoot`
and `Corpse.Kill`, which perform registry/body/pool cleanup. A destroyed dead killer
or PFT body can affect observations used around replay/respawn; no new native
Killcam failure was reproduced for this audit. In particular, Killcam's standalone
respawn hold is disabled while Arena owns that lifecycle.

## Adapter and admission behavior

`ArenaRemoveTheDeadContract` checks plugin identity, metadata/assembly agreement,
version, inheritance, exact public/private method signatures and managed bodies.
It follows the iterator actually constructed by `QueueRemoval`, checks the
Update/F5, F12, automatic and manual call chains, and proves that both destruction
sites sit below the guarded common method. A traversal through reachable local
helpers rejects known Unity/loot/corpse destruction outside that seam. No foreign
method is executed during discovery.

`ArenaRemoveTheDeadCompat` installs one owned, first-priority Harmony prefix on
`RemoveBotAndWeapons`. It verifies the actual prefix receipt and rechecks it on
Arena admission. A missing mod requires no action. A loaded but unbindable mod,
removed hook, unsupported older version or latched runtime failure refuses only
Arena admission with a useful warning. Normal raids remain available. Parent
integration includes the final native-start admission check, not just menu display.

Versions newer than 4.1.3 are accepted only after these contracts pass, with a
once-per-process warning. This is a checked compatibility boundary, not a proof of
all possible future behavior: a future external, reflection-based or newly
delegate/event-driven cleanup mechanism
would need another source review.

The prefix requires the current native LocalGame, GameWorld and IBotGame singletons
to be the same concrete game/world pair. It never authorizes a pause merely from a
map name or an old raid lease:

1. An exact active Arena game/world pair remains protected through native
   Stopping/SoftStopping/Stopped while Arena still owns it. Losing location/settings
   data does not revoke that already established body-retention protection.
2. Before Arena's active claim, protection requires native Started status, PMC
   settings matching the current world and armed map, a pending arm and valid
   request token, and the matching **server-confirmed end identity** written into
   those native settings. The request/start marker alone is insufficient.
3. A later ordinary raid, even on the same map and with stale old Arena references,
   passes through to RTD. A scope-read exception never authorizes an unknown world;
   after exact active ownership was proved, it keeps the current Arena protected
   and latches a refusal for the next admission.

No RTD config, timer, shortcut, queue or saved value is rewritten. Its own loop and
timer continue normally; only the destructive common call is skipped. Consequently
its own attempted-cleanup counters can still count candidates without destruction.
There is no need to restore a foreign setting on exit. Shutdown revokes the
adapter's authority before unpatching, so even a failed unpatch leaves an inert
prefix. No foreign DLL is bundled or installed.

Logging follows [LOGGING.md](LOGGING.md): successful hook setup is Debug; one
player-relevant pause notice per protected world is Info; contract/runtime
failures and the newer-version advisory are bounded Warnings.

## Verification and limitations

[RemoveTheDeadCompatChecks](../tools/RemoveTheDeadCompatChecks/README.md) compiles
the actual production adapter, contract and scope against authored boundary
doubles, using real Reflection.Emit plugin call graphs and a real iterator. The
published DLL is separately checked with Cecil for its pinned hash, identity,
version, EFT gate, signatures and both destructive entry chains. It is never
executed by the harness.

The baseline covers 209 assertions; 13 compiled faults exercise ordinary-raid
overreach, active-body release, early unconfirmed claims, wrong world/bot-game,
Scav inheritance, missing prefix/receipt, unreadable settings and contract bypass.
`Verify.ps1` only emits `REMOVE_THE_DEAD_COMPAT_VERIFIED` after both unchanged
baselines and all negative controls pass. The verifier also confirms that the
three production sources did not change during the run.

Local review passes cover (1) upstream/native destruction evidence, (2) contract
and entry graph, (3) scope/teardown/error boundaries, and (4) compiled positive and
negative controls. Parent adversarial review is a separate acceptance gate.

These are source, metadata and offline behavior checks. They do not establish
native Mono/Harmony acceptance or reproduce Unity's physical destruction. No RTD
installation or GPU raid test was performed by this implementation leaf. A native
follow-up should attempt F5/F12 and automatic cleanup around a retained Tactical/PFT
corpse and an Arena weapon pickup, then verify ordinary cleanup in a subsequent
PMC raid in the same process.
