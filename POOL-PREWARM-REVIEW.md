# Arena pool-prewarm clamp (2026-09-19)

Design review. Nothing has been implemented yet — no `Performance/PoolPrewarmPolicy.cs`,
no `Performance/PoolPrewarmMonitor.cs`, no `PoolPrewarmKeepIdle` setting exist in this
worktree at the time of writing. This document is the design a later session should
implement against, in the style `GC-THRESHOLD-REVIEW.md` used for the sibling feature.
Every game-internals claim below was re-verified directly against `../_decompiled` this
session, not taken only from an earlier reader's notes.

## The problem

`ObjectsFactory.InitAndFillPools` (`../_decompiled/EFT/ObjectsFactory.cs:203-264`) keeps
one `RequestCount` per resource (`AssetPoolData.RequestCount`, `ObjectsFactory.cs:107`),
incremented by exactly one on **every** call for that resource path and never
decremented anywhere outside pool re-creation:

```csharp
int requestCount = PoolsDictionary[resourceKey.ResourceKey].RequestCount;
requestCount++;
PoolsDictionary[resourceKey.ResourceKey].RequestCount = requestCount;
int num2 = requestCount - source.Task.Result.FuturePoolSize;
if (num2 > 0)
    Task task = source.Task.Result.ExpandPool(num2, yield, cancellationToken, "LoadPlayerResources");
```

(`ObjectsFactory.cs:232-238`). Every bot that has ever spawned this raid raises the
demand counter by one, and the pool keeps expanding to match that running *total* —
never the *concurrent* demand. In a vanilla raid (~30 bots) this is invisible. In
Arena's continuous respawn it is not: measured on the GPU host, 18.09.2026, Labyrinth
deathmatch, Raid J (`RespawnLeakProbe/RESULTS-2026-09-18.md`, section "Raid J"), 267
bodies over 17 minutes —

- `item_meds_afak_container` pool: 210 idle instances, 210 created.
- `survival_first_aid_rollup_kit` pool: 207 idle instances.
- `weaponPoolCreatedTotal` grows +7.2 instances per body (R² 0.999).
- `poolsTotal` (distinct resource paths ever pooled) grows +6.3 per body.
- ~380 stale `MonoBehaviour`s per body, ~65,000 over the 17-minute raid, all sitting in
  the `poolIn` bucket of the object census (parked, `GameObject`-active, components
  disabled, position `UNSEEN_PLACE`) — nothing orphaned, nothing leaked, all of it idle
  pool surplus.
- Per-frame cost is small and separately measured: `MuzzleSystem.LateUpdate` and
  `WeaponOverHeatSystem.LateUpdate` grow ~0.005 ms/frame per raid minute. The actual
  cost is memory and the cost of every scene-wide object search, not frame time — this
  clamp is a memory/scene-search fix, not a frame-time fix.
- The kit preloader is not the cause: 19 calls total, once per kit
  (`KitBundlePreloader`, unrelated to this growth).

Corrected against the task's own framing: the class actually instantiated for every
`ResourceType` branch is `AssetPool<T>` (singular "Asset"), not `AssetsPool<T>`
(plural) — `ObjectsFactory.InitializeAssetsPool`, `ObjectsFactory.cs:287-293`, the
`switch` over `ResourceType.UsablePrefab/Ammo/Weapon/Magazine/
StationaryWeaponMagazine/WeaponModItem` plus a default branch, each calling
`AssetPool<...>.Create(...)`. Both `AssetPool<T>` and `AssetsPool<T>` extend
`AbstractAssetsPool<T>` and neither overrides `ExpandPool` — the only implementation in
the whole hierarchy lives on the abstract base, so patching goes straight there.

`AbstractAssetsPool<T>.ExpandPool` (`../_decompiled/EFT.AssetsManager/
AbstractAssetsPool.cs:209-255`):

```csharp
public async Task ExpandPool(int objectsCount, YieldDelegate yield,
    CancellationToken cancellationToken = default(CancellationToken), string reason = "")
{
    if (cancellationToken.IsCancellationRequested || IsDisposed) return;
    QueuedExpandCount += objectsCount;
    if (_isExpandPoolInProgress) return;
    _isExpandPoolInProgress = true;
    // ...loop: while CurrentPoolSize < FuturePoolSize, await yield(), CreatePoolObject()...
}
```

`CurrentPoolSize => ObjectsPool.Count` and `FuturePoolSize => ObjectsPool.Count +
QueuedExpandCount` (`AbstractAssetsPool.cs:96,98`). `QueuedExpandCount += objectsCount`
runs **before** the `_isExpandPoolInProgress` reentrancy guard — so the only point that
actually reduces what gets created, rather than just accumulating into an
already-inflated queue, is the `objectsCount` argument at the call site itself. Nothing
downstream (`CreatePoolObject`, `Push`) is a safe place to intervene: clamping there
would still let `QueuedExpandCount`/`FuturePoolSize` balloon, which would go on tricking
every *future* `InitAndFillPools` call into believing the pool is already "big enough".

`PopOrCreate` (`AbstractAssetsPool.cs:356-390`) is why a clamped pool is safe to run
dry: when the idle stack is empty (`ObjectsPool.Count - _forcedRequestsCount <= 0`) and
`poolShouldNotBeEmpty` is false, it does not fail — it increments `_forcedRequestsCount`
and creates one object immediately, then recurses once with `poolShouldNotBeEmpty:
true` to hand it back. This is the exact same branch vanilla already takes for any
pool's very first-ever request, before `InitAndFillPools` has expanded it at all. (The
one failure mode in this method, `"Fail to create object. Preventing stack overflow."`
at `AbstractAssetsPool.cs:376`, guards the recursive call itself against a broken job
system, not against a merely-small pool — the clamp does not make this path any more
reachable than it already is in vanilla.) A clamp therefore trades idle memory for a
few more on-demand creations; it never blocks a spawn.

## The design

`Config.Settings.PoolPrewarmKeepIdle` (`ArenaMode.Client/Config/Settings.cs`,
`ConfigEntry<int>`, `"Performance"` section, declared and bound immediately after
`GcHeapThresholdGB` at `Settings.cs:78,714-731`). Default `4`, range `0`–`64`,
`0 = never (pure vanilla)`:

```csharp
PoolPrewarmKeepIdle = config.Bind(
    "Performance",
    "PoolPrewarmKeepIdle",
    4,
    new ConfigDescription(
        "Cap how many idle instances EFT's own asset-prewarm keeps per resource " +
        "(weapons, AFAKs, survival kits, hands prefabs, ...) during an arena raid, " +
        "instead of letting it grow to match every bot that has ever spawned. 0 = " +
        "never (pure vanilla): ObjectsFactory.InitAndFillPools sizes every pool to " +
        "the TOTAL request count across the whole raid and that count never drops, " +
        "so Arena's continuous respawn keeps growing every pool for as long as the " +
        "raid runs (measured: 210 idle AFAKs and 207 idle survival kits after 267 " +
        "bodies in 17 minutes, ~380 stale components per body). Only acts while an " +
        "arena session is active AND the request is EFT's own periodic top-up to " +
        "total demand (reason \"LoadPlayerResources\") — pool CREATION and the " +
        "separate MaintainMinPoolSize top-up are never touched, and a pool run down " +
        "to this cap still creates on demand exactly as vanilla does for any pool's " +
        "first use, so a spawn is never short an item, only the standing idle " +
        "surplus is bounded. See POOL-PREWARM-REVIEW.md for the measured cost.",
        new AcceptableValueRange<int>(0, 64)));
```

### The pure decision function

The entire gating decision — arena-active, the exact `reason` string, `keepIdle <= 0`,
and the clamp arithmetic itself — lives in one stateless, compiled, unit-testable
function, `ArenaMode.Performance.PoolPrewarmPolicy.Decide`, so the offline checks
project can prove every gate by calling real code, not by grepping the Harmony-facing
file for substrings:

```csharp
namespace ArenaMode.Performance;

internal static class PoolPrewarmPolicy
{
    public readonly struct Decision
    {
        public readonly bool RunOriginal;   // true: let the real ExpandPool run (with ObjectsCount).
        public readonly int  ObjectsCount;  // objectsCount to hand the real ExpandPool; 0 when RunOriginal is false.
        public readonly bool Clamped;       // true only when this decision reduced the request — the ledger counts only these.

        private Decision(bool runOriginal, int objectsCount, bool clamped)
        { RunOriginal = runOriginal; ObjectsCount = objectsCount; Clamped = clamped; }

        public static Decision Unclamped(int requestedCount) => new(true, requestedCount, false);
        public static Decision ClampedTo(int allowed) => new(allowed > 0, allowed, true);
    }

    public static Decision Decide(bool arenaActive, string reason, int keepIdle, int futurePoolSize, int requestedCount)
    {
        if (!arenaActive || reason != "LoadPlayerResources" || keepIdle <= 0 || requestedCount <= 0)
            return Decision.Unclamped(requestedCount);

        var allowed = Math.Max(0, keepIdle - futurePoolSize);
        if (requestedCount <= allowed)
            return Decision.Unclamped(requestedCount);

        return Decision.ClampedTo(allowed);
    }

    // Also pure, also compiled: the one decision PoolPrewarmMonitor.Reset() needs
    // that is worth testing independently of the ledger's own bookkeeping — see
    // "Reset() is safe to call more than once" below.
    public static bool ShouldLogLedger(int poolsTouched) => poolsTouched > 0;
}
```

`Decide` clamps against `IAssetPool.FuturePoolSize` (idle **plus already-queued**), not
`CurrentPoolSize` (idle alone). This is a deliberate departure from the reader's
original `CurrentPoolSize` proposal: `QueuedExpandCount += objectsCount` happens before
either the reentrancy guard or a second concurrent caller could observe the pool having
grown, so a caller reading only `CurrentPoolSize` could think it has a full `keepIdle`
of headroom while another call (an overlapping `InitAndFillPools` batch, or a
concurrent `MaintainMinPoolSize` top-up) is already mid-flight for the same pool — and
each would independently clamp to `keepIdle`, letting the pool overshoot to nearly
`2 * keepIdle`. `FuturePoolSize` already reflects any in-flight queue, so two racing
callers converge on the same shrinking `allowed` instead of each claiming their own.

`keepIdle` is read fresh, by the caller, on every invocation — not cached — so turning
it down mid-raid through the hot-reloadable config page takes effect on the very next
`ExpandPool` call, no restart needed, the same convention `GcHeapThresholdGB` already
uses.

### The Harmony recipe

**Root cause** (confirmed twice already in this codebase — `RespawnLeakProbe.Timing.
MethodTimer.IsPatchable`, `RespawnLeakProbe/Timing/MethodTimer.cs:47-58`, doc comment):
a Harmony patch that asks for `MethodBase __originalMethod` on a method declared on a
generic type throws `"Cannot resolve method ... because the declaring type of the
method handle ... is generic"` on **every call**, before the patch body ever runs. This
was first hit patching `ComponentSystem<MuzzleManager, MuzzleSystem>.Update` — a
*closed* instantiation reached through a non-generic subclass — so "closed generic ≠
safe" is the operative lesson: the fix is simply never declaring `__originalMethod`.

Method acquisition, one closed type at a time:

```csharp
private static readonly Type[] PooledResourceTypes =
{
    typeof(UsableHandsPrefab), typeof(AmmoPoolObject), typeof(WeaponPrefab),
    typeof(MagazineInHandsVisualController), typeof(MagVisualController),
    typeof(WeaponModPoolObject), typeof(AssetPoolObject),
};

foreach (var t in PooledResourceTypes)
{
    try
    {
        var closed = typeof(AbstractAssetsPool<>).MakeGenericType(t);
        var method = AccessTools.DeclaredMethod(closed, "ExpandPool");
        if (method == null) { /* warn, skip only this T */ continue; }
        Patches.Patch(method, prefix: prefix);
    }
    catch (Exception exception) { /* warn, skip only this T */ }
}
```

`AccessTools.DeclaredMethod` on `AbstractAssetsPool<>` directly — never on the
`AssetPool<T>` subclass — is deliberate: there is exactly one `ExpandPool` in the whole
hierarchy today, so the two approaches resolve to the same `MethodInfo`, but
`DeclaredMethod` on the type that actually owns the body is independent of
inheritance-lookup semantics and cannot silently start resolving a *different*,
overriding method if a future SPT build adds one on a subclass.

Prefix signature (no `__originalMethod`, ever):

```csharp
private static bool Prefix(object __instance, ref int objectsCount, string reason, ref Task __result)
```

- `object __instance`, not `IAssetPool`: the same static method is registered against
  seven distinct closed `MethodInfo`s, so a strongly-typed parameter would only bind
  for one of them. Cast to `IAssetPool` inside the body — a fully public interface
  (`CurrentPoolSize`, `FuturePoolSize`, `TotalCreatedObjectsCount`, `PoolShortName`,
  `ResourceType`, `../_decompiled/EFT.AssetsManager/IAssetPool.cs`), no reflection
  needed at call time.
- `ref int objectsCount` **must be spelled exactly `objectsCount`**, matching the
  *implementation*'s parameter name (`AbstractAssetsPool.cs:209`). `IAssetPool`'s own
  interface declaration misspells the same parameter `objectsCoun` — no final `t`
  (`IAssetPool.cs:34`, confirmed by direct read this session). Harmony binds a
  prefix's by-name pass-through parameters against the method it actually patched,
  the concrete implementation — spelling it `objectsCoun` here would silently fail to
  bind as the real `ref` parameter and the clamp would do nothing while looking
  installed.
- `string reason`: read-only pass-through.
- `ref Task __result`: the compiler turns `async Task ExpandPool(...)` into a
  synchronous stub that builds the state machine and returns its `Task` — that stub is
  what Harmony patches, and the prefix runs before the state machine exists, so
  `__result = Task.CompletedTask; return false;` is a standard, valid async-skip.
  Verified against both call sites in `ObjectsFactory.cs` (the awaited branch and the
  `.HandleExceptions(...)` fire-and-forget branch, `ObjectsFactory.cs:238-249`).
  **Every `false`-return path sets `__result` first** — leaving it null hands the
  caller a null `Task` and crashes either call site.

Body:

```csharp
private static bool Prefix(object __instance, ref int objectsCount, string reason, ref Task __result)
{
    if (!ArenaSession.IsActive || _disabledForRaid)
        return true; // fast path: a normal raid, or a raid where this already failed once, never enters the try.

    try
    {
        var pool = (IAssetPool)__instance;
        var decision = PoolPrewarmPolicy.Decide(
            ArenaSession.IsActive, reason, Settings.PoolPrewarmKeepIdle.Value, pool.FuturePoolSize, objectsCount);

        if (!decision.Clamped)
            return true; // feature off, wrong reason, or already within budget — vanilla, unchanged.

        RecordClamp(pool.PoolShortName, requested: objectsCount, allowed: decision.ObjectsCount);

        if (!decision.RunOriginal)
        {
            __result = Task.CompletedTask;
            return false;
        }

        objectsCount = decision.ObjectsCount;
        return true;
    }
    catch (Exception exception)
    {
        DisableForRaid(exception);
        return true; // fail open: vanilla ExpandPool runs, uncapped, rather than risk a broken spawn.
    }
}
```

The outer `!ArenaSession.IsActive` check is a fast-path optimisation, not the source of
truth — `Decide` re-checks `arenaActive` itself, so the whole gate is provably correct
from the checks project's unit tests even if this fast path were ever removed by
accident. This mirrors `NoBotGesturesPatch`/`NoCorpseLootingPatch`'s own
`if (!ArenaSession.IsActive) return true;` and satisfies AGENTS.md's rule that "every
patch starts with an `ArenaSession.IsActive` gate and fails open to vanilla."

### Idempotence under Harmony's own generic-instantiation quirks

Whether Mono's generic-code sharing for reference-type arguments can make Harmony
invoke this prefix more than once for a single logical `ExpandPool` call could not be
confirmed or ruled out from any source in this codebase (a `MonoMod.Utils.xml` remark
about `RuntimeMethodHandle.GetFunctionPointer()` platform quirks is suggestive, not
proof). The design does not need that question answered, because of how the `ref`
parameter behaves if it does happen:

If Harmony chains two prefix invocations onto the *same* call, the second invocation
receives the `objectsCount` the first one already wrote, because `ref int objectsCount`
is threaded through the prefix chain — and `pool.FuturePoolSize` has not changed
between the two invocations, because the real `ExpandPool` body (the only thing that
mutates `QueuedExpandCount`) has not run yet at prefix time. So the second invocation
calls `Decide` with the *already-clamped* `requestedCount` against the *same*
`futurePoolSize`, and by construction that always satisfies `requestedCount <= allowed`
— an **unclamped** decision. `decision.Clamped` is `false`, so nothing is double-
clamped and nothing is double-counted in the ledger. The worst case of a repeated
invocation is a correctly-detected no-op, never a double clamp. This is the direct
target of one of the checks project's own unit tests (see Verification below): call
`Decide` once, then call it again with the first call's `ObjectsCount` as the second
call's `requestedCount` and the same `futurePoolSize` — the second call must return
`Clamped == false`.

This argument only covers "two prefixes chained onto one call". If a real GPU raid
instead shows `TotalCreatedObjectsCount` behaving as though `ExpandPool` itself ran
twice per logical bot spawn, that is a different, more serious failure than anything
this reasoning covers, and needs its own follow-up — flagged explicitly in the
measurement plan below, not silently assumed away.

### Fail-open

Two independent layers, both precedented elsewhere in this codebase:

1. **Install-time** — each of the seven `AccessTools.DeclaredMethod` +
   `Patches.Patch` calls is in its own `try`/`catch`
   (`RespawnLeakProbe.Timing.SystemTiming.PatchTick`'s own
   resolve-then-patch-then-warn-and-skip-only-that-target convention). One failing
   closed type logs once and leaves only that resource's pool unclamped; the other six
   keep working.
2. **Call-time** — the whole feature disables itself for the rest of the current raid
   (`_disabledForRaid = true`) on the first exception, from any of the seven patched
   types, and warns exactly once (`_warnedThisRaid`) rather than once per call. This is
   a deliberate, conservative choice: **the whole clamp goes vanilla, not just the pool
   that threw.** The cost is that one misbehaving pool type silences clamping for six
   pools that were working fine; the benefit is one obviously-correct fail-open branch
   instead of per-pool disable bookkeeping this feature's blast radius does not
   justify. `_disabledForRaid` clears at the next raid boundary (`Reset()`), so a bad
   raid never disables the feature permanently.

An exception inside the prefix therefore never breaks a spawn — worst case, the raid's
remaining pool-prewarm calls behave exactly like `PoolPrewarmKeepIdle = 0`.

## Nothing leaks into the next raid

Two independent reasons, the same two-part argument `GC-THRESHOLD-REVIEW.md` makes for
`GcThresholdMonitor`:

1. **The behavioural gate is the actual safety mechanism, evaluated fresh every
   call.** `ArenaSession.IsActive` (`ArenaMode.Client/Session/ArenaSession.cs:49`) is
   read at the very top of `Prefix` on every single invocation. A normal raid never
   reaches `PoolPrewarmPolicy.Decide` at all, so it cannot be affected by anything this
   feature's static state holds, including a bug in it. `Prefix` never writes to any
   of EFT's own pool state (`QueuedExpandCount`, `RequestCount`, `ObjectsPool`) — it
   only ever *reduces* a request in flight — so there is nothing of EFT's own to
   restore at a raid boundary either.
2. **Our own bookkeeping is cleared at every raid boundary regardless.**
   `PoolPrewarmMonitor.Reset()` is called from `ArenaRespawnDriver.ResetSession()`
   (`ArenaMode.Client/Respawn/ArenaRespawnDriver.cs:464-495`), the same static method
   every other per-raid Arena service resets from
   (`CorpseCleanupService.Reset()` at line 469, `Performance.GcThresholdMonitor.
   Reset()` at line 478, and eleven neighbours). `ResetSession()` runs on *every* raid
   transition, arena or not — from `ArenaRespawnDriver.OnRaidEnded()` (line 460) and
   from `ArenaSession`'s own raid-start latch, immediately after `IsActive = true`
   (`ArenaSession.cs:255,258`) — so a raid's ledger and `_disabledForRaid` flag cannot
   survive into the next one even before reason (1) above is considered.

### Reset() is safe to call more than once per boundary

`ResetSession()` itself fires more than once around a single raid boundary in some
paths (the exit-path calls at `ArenaSession.cs:157,184,381` plus the start-of-raid call
at `ArenaSession.cs:258`), so `PoolPrewarmMonitor.Reset()` must not double-log. The
log-worthiness decision is itself pulled into the pure, compiled policy —
`PoolPrewarmPolicy.ShouldLogLedger(poolsTouched) => poolsTouched > 0` — checked
*before* the ledger is cleared, on every call:

```csharp
public static void Reset()
{
    if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))
    {
        /* build and log the one summary line from _poolsTouched/_expansionsClamped/_instancesAvoided, see Logging below */
    }
    _poolsTouched.Clear();
    _expansionsClamped = 0;
    _instancesAvoided = 0;
    _disabledForRaid = false;
    _warnedThisRaid = false;
}
```

The first call in a boundary sees a non-empty ledger, logs once, then clears
everything; every subsequent call in the same boundary sees an empty ledger,
`ShouldLogLedger(0)` is `false` (a compiled, unit-tested fact — see Verification), and
clearing an already-empty set/counters and already-false flags is a no-op. The
ordering invariant this depends on — the log decision must be evaluated strictly
before `_poolsTouched.Clear()` runs — is checked at the source level by the offline
checks project (see below), since `PoolPrewarmMonitor` itself cannot be compiled
outside the game to unit-test `Reset()` directly.

(The ledger is three fields, not one dictionary: `_poolsTouched` is a
`HashSet&lt;string&gt;` of `IAssetPool.PoolShortName` values, `_expansionsClamped` an
`int` count of clamp decisions, `_instancesAvoided` a `long` running total. All three
are cleared together; `ShouldLogLedger` is asked about `_poolsTouched.Count` because
that is the one field whose emptiness exactly tracks "did anything happen this raid".)

## Logging

Four log lines, no more:

- **Install-time, once**: `Arena pool prewarm clamp ready: N/7 pool types patched.`
  (Info; a lower `N` means a game-version drift the install-time try/catch already
  isolated to specific pool types, one warning per failed type.)
- **Per-raid ledger, at most once per raid, only when non-trivial** (from
  `Reset()`, gated by `ShouldLogLedger`):
  `Arena pool prewarm: {pools} pool(s) capped at {keepIdle} idle this raid,
  {expansions} expansion(s) clamped, ~{avoided} instance(s) not created.`
  A vanilla raid, an arena raid with the feature off, or an arena raid whose demand
  never exceeded `keepIdle` all produce zero extra lines.
- **Fail-open, at most once per raid**: `Arena pool prewarm clamp disabled for the
  rest of this raid: {exception.Message}` (Warning, from `DisableForRaid`, guarded by
  `_warnedThisRaid`).
- **Fail-open follow-up, only when the ledger line above also fires**: an additional
  one-line note that the ledger stops at the point the feature disabled itself, so a
  partial ledger from a bad raid is never mistaken for a complete one.

No per-call logging anywhere — `ExpandPool` fires thousands of times over a long
Arena raid, and a feature built to bound that exact cost must not itself add a
line-per-call cost. The `"Arena <Feature>: ..."` prefix matches every other Info line
in this codebase (`"Arena GC threshold ..."`, `"Arena match ended: ..."`).

The ledger is logged from inside `PoolPrewarmMonitor` itself, at its own decision
point — the same structural choice `GcThresholdMonitor` already makes for its own
follow-up line, rather than reaching into `ArenaMatchService.OnRaidEnded()`'s
`"Arena match ended: ..."` line. The two are separate concerns with separate data: the
match-ended line reads fields off a `Modes.*Match` object; this reads static
`Performance`-service counters that need no match object and must not depend on
`ArenaMatchService`'s subscription order relative to `ArenaRespawnDriver`'s (both are
merely subscribers/callers of `ResetSession()`, with no ordering guarantee between
them documented anywhere in this codebase).

## Verification

### Offline: `tools/PoolPrewarmChecks`

Mirrors `tools/GcThresholdChecks` file-for-file: same `.csproj` property-redirection
trick (`PolicySource`/`MonitorSource` MSBuild properties plus `AssemblyMetadata` so
`Program.cs` can read back exactly which file it ran against), same `Program.cs`
shape, same `Verify.ps1` shape dot-sourcing the shared, unmodified
`tools/Invoke-ArenaSourceChecks.ps1` runner.

`PoolPrewarmPolicy.cs` has no `UnityEngine`/`EFT`/`HarmonyLib` usings (only `System`),
so it compiles directly into the checks project, exactly like `GcThresholdPolicy.cs`.
`PoolPrewarmMonitor.cs` references Unity/EFT/Harmony types that only exist inside the
game, so its half of the source guard is a text scan, exactly like `GcThresholdMonitor.
cs`'s own guard against `GC.Collect(`/`GetTotalMemory(true`.

**`Program.cs`**, in order:

1. `SourceGuard()` — text-scans `PoolPrewarmMonitor.cs` (via `MonitorSourcePath`
   metadata) and asserts: does **not** contain the literal `"__originalMethod)"` — the
   parameter name immediately followed by a closing paren, i.e. appearing AS a
   parameter, deliberately **narrower** than a bare `"__originalMethod"` substring
   check, because this file's own doc comments explain the `__originalMethod` trap by
   name as prior art and a bare-substring check would permanently fail against that
   legitimate prose (the generic-declaring-type crash guard); contains the literal
   `"ref int objectsCount,"` (the correct spelling, never the interface's
   `objectsCoun` typo); contains `"__result = Task.CompletedTask;"` (the null-`Task`
   guard on the skip path); contains `"PoolPrewarmPolicy.ShouldLogLedger("`, and that
   substring's index precedes the index of `"_poolsTouched.Clear();"` (the `Reset()`
   log-before-clear ordering); does **not** contain `"GC.Collect("` or
   `"GetTotalMemory(true"` (inherited habit-guard from the GC feature; this feature
   never has a reason to touch the heap, so the guard costs nothing and catches an
   accidental copy-paste).
2. `ClampDecisions()` — unit assertions against the compiled `PoolPrewarmPolicy.
   Decide`:
   - `keepIdle = 0` never clamps, regardless of `arenaActive`/`reason`/demand (the
     `0 = never` contract).
   - `arenaActive = false` never clamps even with `reason = "LoadPlayerResources"`
     and demand far exceeding `keepIdle` (the Arena gate).
   - `reason` of `"initial"`, `"MaintainMinPoolSize"`, or `""` never clamps inside an
     active arena with demand exceeding `keepIdle` (pool creation and EFT's own
     top-up must be untouched).
   - Demand at or under `allowed = keepIdle - futurePoolSize` returns `Unclamped`
     with `ObjectsCount` equal to the request, unchanged.
   - Demand over `allowed`, with `allowed > 0`, returns `Clamped` with
     `ObjectsCount == allowed` and `RunOriginal == true`.
   - `allowed == 0` (pool already at or above `keepIdle`, counting the queue) returns
     `RunOriginal == false`, `ObjectsCount == 0`.
   - `futurePoolSize` already greater than `keepIdle` never produces a negative
     `allowed` (the `Math.Max(0, ...)` guard) and never a negative `ObjectsCount`.
   - `requestedCount <= 0` is a defensive no-op (`Unclamped`), never a negative
     `ObjectsCount`.
   - **Idempotence**: call `Decide` once, then call it again with `requestedCount`
     set to the first call's `ObjectsCount` and the same `futurePoolSize` — the
     second call must return `Clamped == false`. This is the direct unit-test
     encoding of the ref-parameter-chain argument above.
3. `LedgerLogging()` — `ShouldLogLedger(0) == false`, `ShouldLogLedger(1) == true`,
   and (restating the point explicitly, not just implicitly) calling it twice with `0`
   after a hypothetical clear still returns `false` both times — trivial for a pure
   function, but stated as its own named assertion so a future refactor that adds
   hidden state to this method trips a labelled failure, not silence.
4. `Console.WriteLine($"ARENA_POOL_PREWARM_BASELINE_PASSED checks={checks}")`.

**`Verify.ps1`** fault list, one entry per invariant, run through the shared
`Test-ArenaSourceFaults` (each traced by hand against the exact code above before being
listed here, so every entry is expected to trip the FAIL label it names, not merely
plausible):

Targeting `PoolPrewarmPolicy.cs` (behavioural mutants, caught by re-running the
compiled unit assertions):

| Name | Find | Replace | Expected failure |
|---|---|---|---|
| `arena-gate-removed` | `!arenaActive \|\|` | `` | clamp fires outside an active arena session |
| `reason-gate-removed` | `reason != "LoadPlayerResources" \|\|` | `` | clamp fires for a reason other than LoadPlayerResources |
| `keepidle-zero-ignored` | `keepIdle <= 0 \|\|` | `` | a zero keepIdle still clamps |
| `negative-allowed-possible` | `Math.Max(0, keepIdle - futurePoolSize)` | `(keepIdle - futurePoolSize)` | allowed goes negative once futurePoolSize exceeds keepIdle |
| `already-in-budget-still-clamped` | `requestedCount <= allowed` | `false` | a request already within budget gets clamped anyway |

Targeting `PoolPrewarmMonitor.cs` (structural mutants, caught by `SourceGuard`'s text
scan — each confirmed against the exact prefix/`Reset()` text in this design):

| Name | Find | Replace | Expected failure |
|---|---|---|---|
| `originalmethod-reintroduced` | `ref Task __result)` | `ref Task __result, System.Reflection.MethodBase __originalMethod)` | the prefix requests `__originalMethod` on a generic declaring type again |
| `objectscount-misspelled` | `ref int objectsCount,` | `ref int objectsCoun,` | the ref parameter no longer matches the implementation's spelling |
| `missing-completed-task-on-skip` | `__result = Task.CompletedTask;` | `` | the skip path leaves `__result` null |
| `ledger-cleared-before-log-decision` | `if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))` | `_poolsTouched.Clear(); if (PoolPrewarmPolicy.ShouldLogLedger(_poolsTouched.Count))` | `Reset()` clears the ledger before deciding whether to log it |

`Write-Output 'ARENA_POOL_PREWARM_CHECKS_PASSED'` at the end, same
token-and-hash-verification discipline `Invoke-ArenaSourceChecks.ps1` already runs for
the GC feature (baseline pass, per-fault mutated-copy run with SHA256 verification that
production source was never touched, final baseline rerun).

Run with `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/PoolPrewarmChecks/
Verify.ps1` — never Windows PowerShell 5.1's `powershell.exe`, for the same reason
`GC-THRESHOLD-REVIEW.md` documents: 5.1's `$ErrorActionPreference = 'Stop'` turns a
mutant's intentional `FAIL: ...` into a terminating error before the runner's own
exit-code check ever sees it, aborting on the first fault instead of evaluating it.

Compile-check for the client half (never `dotnet build ArenaMode.sln`, which deploys):

```
dotnet build D:\SPT41\Development\ArenaMode-PoolPrewarm\ArenaMode.Client\ArenaMode.Client.csproj -c Release -v:q -p:SPTModType=Library
```

Record the exact warning count here once run, the same way `GC-THRESHOLD-REVIEW.md`
records its own baseline (`0 errors, the same 4 pre-existing CS8632 warnings in
ArenaMode.Shared/ClientSettingsModels.cs`) — so a new warning this feature introduces
is caught by diff, not by feel.

### In-game measurement plan (orchestrator, not this session)

Same scenario as the Raid J baseline: Labyrinth deathmatch, long enough to pass
~200+ bodies, `RespawnLeakProbe` attached.

What must improve, compared against the Raid J baseline:

- Top pools' `CurrentPoolSize`/`FuturePoolSize` (idle, plus whatever is legitimately
  in use) at or near `PoolPrewarmKeepIdle`'s configured value at any snapshot taken
  between spawns — never anywhere near Raid J's 210/207.
- `TotalCreatedObjectsCount` should still climb (every clamp-triggered `PopOrCreate`
  forced-create still increments it, and genuinely *new* resource paths still get
  their `"initial"` creation, untouched by this clamp) but at a materially reduced
  *rate* once `keepIdle` worth of idle instances exist for a given pool — the
  measurement plan should read this as the expected, correct signature of the clamp
  working, not mistake continued growth in `TotalCreatedObjectsCount` for the clamp
  failing.
- The active-`MonoBehaviour` census growth per body should drop sharply from Raid
  J's ~380/body, since far fewer pooled `WeaponPrefab`/`UsableHandsPrefab` instances
  sit `GameObject`-active in the now much smaller pools.

What must **not** appear:

- No spawn errors in `BepInEx/LogOutput.log` around kit application or weapon
  assembly.
- Bots visibly armed (the probe's own armed-bot check, or a manual look at a few
  bots).
- No `Arena pool prewarm clamp disabled for the rest of this raid` warning — if it
  does appear, the raid should still show bots armed and no spawn errors (proving
  fail-open itself worked), but the rest of that raid's pool numbers should not be
  trusted as representative of a clean run.

What the new ledger line must show, cross-checked against the probe's own pool
numbers, not just trusted at face value:

- `pool(s) capped` in the low tens (matches expected kit/weapon variety, not the
  fixed count of 7 closed generic types).
- `expansion(s) clamped` growing roughly with body count.
- `instance(s) not created` in the same order of magnitude as Raid J's *excess* per
  pool (roughly: Raid J's final idle count for a pool minus `PoolPrewarmKeepIdle`,
  summed across pools) — a sanity cross-check that the ledger's arithmetic matches
  the externally observable pool sizes, not just an internally consistent number.

A second, short **non-arena** raid should also run once, to confirm zero
`Arena pool prewarm:` lines and zero behavioural difference from before this feature
existed — the one thing that must stay true no matter what.

Finally, because the Harmony-chained-double-invoke question is explicitly unresolved
(see "Idempotence" above): if `TotalCreatedObjectsCount` for any clamped pool ever
exceeds `keepIdle` plus a plausible in-use count by more than a handful, or the
ledger's `instancesAvoided` looks implausibly doubled against the probe's own creation
counts, that is the specific signal that `Prefix` is being invoked as two
*independent* calls rather than a chained pair on one call — a materially different
problem from what this design assumes, worth a dedicated follow-up rather than a
silent shrug.

### Measured

_Placeholder — to be filled in by the orchestrator after the probe raid described
above. Record here: the `TotalCreatedObjectsCount` curve before/after warm-up per top
pool, the top pools' `CurrentPoolSize`/`FuturePoolSize` readings against
`PoolPrewarmKeepIdle`, the exact `Arena pool prewarm: ...` ledger line at match end,
spawn-error count (expect zero), whether the fail-open warning ever fired, the
active-`MonoBehaviour`-per-body growth rate compared to Raid J's ~380/body, and the
result of the required non-arena control raid (expect zero `Arena pool prewarm:`
lines)._

## Implementation checklist

Ordered, one commit per step, matching this project's convention of a small
self-contained commit per completed change with a message that explains the
reasoning, ending with this session's own attribution trailer.

1. **Add the pure policy.** File: `ArenaMode.Client/Performance/PoolPrewarmPolicy.cs`
   (`Decide`, `Decision`, `ShouldLogLedger`, no Unity/EFT/HarmonyLib usings). Commit:
   *"Add the Arena pool-prewarm clamp's pure decision function"* — explain the
   `FuturePoolSize`-vs-`CurrentPoolSize` choice and the idempotence argument in the
   body, since both are load-bearing and not obvious from the diff alone.
2. **Add the setting.** File: `ArenaMode.Client/Config/Settings.cs` — declare and bind
   `PoolPrewarmKeepIdle` next to `GcHeapThresholdGB`. Commit: *"Add PoolPrewarmKeepIdle
   (default 4, 0 = off)"* — cite the measured 267-body numbers in the body, same as
   the config description does.
3. **Add the offline checks project.** Files: `tools/PoolPrewarmChecks/
   PoolPrewarmChecks.csproj`, `Program.cs`, `Verify.ps1` (dot-sourcing the existing,
   unmodified `tools/Invoke-ArenaSourceChecks.ps1`). Run `pwsh -NoProfile
   -ExecutionPolicy Bypass -File tools/PoolPrewarmChecks/Verify.ps1` and confirm
   `ARENA_POOL_PREWARM_CHECKS_PASSED` before committing. Commit: *"Add offline checks
   for the pool-prewarm clamp's decision logic"* — this step deliberately precedes the
   Harmony half, so the decision logic is proven correct in isolation before anything
   depends on it at runtime.
4. **Add the Harmony installer and prefix.** File: `ArenaMode.Client/Performance/
   PoolPrewarmMonitor.cs` (the seven-type patch loop, `Prefix`, the per-pool ledger,
   `Initialize()`/`Reset()`). Compile-check only —
   `dotnet build D:\SPT41\Development\ArenaMode-PoolPrewarm\ArenaMode.Client\
   ArenaMode.Client.csproj -c Release -v:q -p:SPTModType=Library` — never a full
   solution or client build. Re-run `tools/PoolPrewarmChecks/Verify.ps1`'s
   `SourceGuard` step manually against the new file before committing, since the
   checks project's own `MonitorSource` text scan is what this step must satisfy.
   Commit: *"Patch AbstractAssetsPool<T>.ExpandPool to bound Arena's idle prewarm"* —
   explain the `__originalMethod` trap and point at
   `RespawnLeakProbe/Timing/MethodTimer.cs` as prior art, since a future reader must
   not "simplify" the prefix signature back toward including it.
5. **Wire it into `Plugin.Awake` and `ArenaRespawnDriver.ResetSession`.** Files:
   `ArenaMode.Client/Plugin.cs` (one `Performance.PoolPrewarmMonitor.Initialize();`
   line, grouped with the other Harmony `.Enable()`/`Initialize()` calls — near
   `Bots.ArenaSainPersonalities.Enable()`, since like it this feature owns a `Harmony`
   instance and installs several patches from a loop, unlike
   `GcThresholdMonitor.Initialize()`'s zero-Harmony event subscription), and
   `ArenaMode.Client/Respawn/ArenaRespawnDriver.cs` (one
   `Performance.PoolPrewarmMonitor.Reset();` line next to
   `Performance.GcThresholdMonitor.Reset();` at line 478). Commit: *"Wire the
   pool-prewarm clamp into Plugin.Awake and the raid-boundary reset"*.
6. **Update `AGENTS.md`.** One Traps paragraph (the `__originalMethod`-on-a-generic-
   type lesson, generalised beyond this one feature, placed near the existing GC
   paragraph) and one State-section paragraph (naming `PoolPrewarmKeepIdle`, its
   default, and this document). Commit: *"Document the pool-prewarm clamp in
   AGENTS.md"*.
7. **GPU probe raid** (orchestrator). Fill in the "Measured" section above from the
   actual raid, update the State-section AGENTS.md paragraph if the measured numbers
   materially change the story (the way the GC feature's own two follow-up commits
   did after its own GPU raids), and commit that update on its own: *"Record the
   pool-prewarm clamp's GPU raid"*.

Every commit ends with a blank line then this session's own
`Co-Authored-By:` trailer — do not copy an older commit's trailer name verbatim; use
whichever one the implementing session's own instructions specify at the time.
