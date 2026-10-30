# Individual Deathmatch FFA groups

2026-09-12. Companion review and offline verification for the native group
integration in `ArenaMode.Client/Bots/ArenaBotPatches.cs`. The parent owns the
production change; this leaf owns only the verification harness and this review.

## Native boundaries checked

- `EFT/BotSpawner.GetGroupAndSetEnemies` can reuse a same-role group. The ordinary
  registry's `data` has one slot per role, so merely locking a previous group
  would make the next insertion lose its reference. `_bossGroup` is the native
  collection that retains multiple distinct groups and participates in
  `GetGroups`, `AddPlayer`, dead-player removal and disposal. Storing an existing
  group there does not call a boss constructor or promote its bot to a boss.
- `BotZoneGroupsDictionary.TryGetValue` rejects a locked candidate. Native PMC
  groups already use the multi-group registry; adding their existing reference
  to the HashSet again is idempotent. The ordinary per-role registration and any
  unkeyed aliases are removed by exact reference when adopting an individual.
- Native `_freeForAll` also makes `BotZoneGroups.AddBot` append unkeyed group
  references during each zone pass. The two synchronous patched operations
  temporarily turn that native flag off, then restore its exact previous value
  in their finalizers, including failure. The mod's frozen FFA relationship
  policy remains separate from that native registration switch.
- `BotCreatorClient.method_3` calls `PreActivate`, then the creation callback.
  This is not proof of actual activation or group membership. Native
  `EFT/BotMemory.Activate` adds the owner with `AddMember(..., true)` later.
  Therefore an empty PreActive group must survive even when `AfterCreation`
  has returned and a long loading delay has passed.
- Two bots can finish creation before either enters the active roster. They
  need reciprocal admissions after both actually activate. The one-second
  sweep only admits missing enemy references, never self, never unready/dead
  actors. A throwing source is isolated with a five-second retry delay and a
  bounded warning. Reentrant native callbacks cannot restart the sweep, and a
  creation during the sweep is handled from the next snapshot.
- Native death removes the sole member but does not dispose its group. Cleanup
  waits for zero native members and either previously observed membership or
  native `BotOwner.IsDead`. It runs outside death/removal callback stacks,
  removes exact references from every registry, and disposes once. It never
  commandeers a stale spawner's native teardown. Pending, never-activated groups
  are intentionally not guessed dead from elapsed time.

Sources inspected in the installed `../_decompiled`: `EFT/BotSpawner.cs`,
`BotCreatorClient.cs`, `EFT/BotMemory.cs`, `EFT/BotOwner.cs`, `BotsGroup.cs`,
`BotZoneGroups.cs`, `BotZoneGroupsDictionary.cs`. These source paths are external
to the mod and are not redistributed.

The preactivation disposal and missing reciprocal cohort defects were identified
by the independent runtime reviewer before this harness was written. Additional
review findings about foreign-group ownership and failure isolation were fixed
by the parent and are covered below.

## What the harness executes

Run `pwsh -NoProfile -File tools/DeathmatchGroupChecks/Verify.ps1`.
`-NativeRoot` can point to another installation's decompiled source directory.

The extractor takes the actual production group records, cleanup/reconciliation
methods, ownership guards, reset method and Harmony patch classes each run. It
does not rewrite their algorithms into a second implementation. It also compiles
the actual installed `BotZoneGroups` and `BotZoneGroupsDictionary` classes, so
lookup, registry insertion, enumeration and disposal are native source rather
than a hand-written approximation. One unused compiler-generated cache field is
renamed because the decompiler gave it its enclosing type's name, which is not
legal C# source. Registry method bodies remain unchanged.

Minimal spies provide Unity time, exact game/world/spawner references, actor
readiness, member counts, native enemy admissions and group disposal counters.
Tests explicitly arrange native-created return values and invoke the extracted
prefix/postfix/finalizer methods. They do not simulate a full BotOwner, native
activation, physics or brain. No script builds or deploys the production client,
server or another mod.

The passing baseline covers **83 assertions**: multiple same-role scav and PMC
groups, exact references/no duplicate disposal, native locked lookup, delayed
PreActive creation, one-member activation, reciprocal cohort repair, missing-only
admission and pacing, native failure/backoff, reentrant creation, raid-ending
callbacks, dead-but-still-member deferral, death between sweeps, throwing
disposal, stale spawner, armed-to-active preservation, ordinary/Tactical/Team DM/
all-vs-player isolation and exact-game relationship admission. It also covers a
confirmed new same-map arm while an old `IsActive` latch survives a missed
GameEnd: the new arm is accepted only when the old exact game/world pair no
longer matches, while omission, another map and an exact non-Deathmatch raid
still reject the override.

Two compiled negative controls are required to fail their intended assertions:

1. Removing the observed-member/native-death requirement causes premature
   disposal of the zero-member PreActive cohort.
2. Disabling reconciliation leaves the activated cohort without reciprocal
   enemy relationships.

Compile failures do not count as successful fault controls. The script requires
nonzero test execution and the particular failed assertion. Production and
native input hashes are recorded and rechecked after execution. All extracted
production/native code, fault variants, logs and receipts stay under ignored
`tools/DeathmatchGroupChecks/evidence/`; no decompiled game source is tracked.

## Acceptance limits

These checks prove the executed rule/registry boundaries with supplied native
I/O states. They do not prove Harmony callback ordering at runtime, compatibility
with another mod that bypasses native spawning, native brain/animator behavior,
actual combat movement or long-match performance. Final production compilation,
parent independent review and GPU FFA/all-vs-player tests remain separate gates.
