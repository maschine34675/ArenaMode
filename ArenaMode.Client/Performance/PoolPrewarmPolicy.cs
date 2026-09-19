using System;

namespace ArenaMode.Performance;

/// <summary>
/// Decides whether to clamp one EFT asset-pool top-up request during an
/// Arena raid. Pure so the offline checks project can prove every gate with
/// real code, not a source-text grep; the Harmony/EFT-facing half lives in
/// PoolPrewarmMonitor.
///
/// ObjectsFactory.InitAndFillPools (../_decompiled/EFT/ObjectsFactory.cs:
/// 203-264) keeps one RequestCount per resource path, incremented by exactly
/// one on EVERY call for that resource — never decremented — and sizes the
/// pool's FuturePoolSize to that running TOTAL, not the concurrent demand.
/// Vanilla's ~30 bots never notice; Arena's continuous respawn does: measured
/// 267 bodies over 17 minutes left the AFAK pool with 210 idle instances and
/// the survival-kit pool with 207 (RespawnLeakProbe/RESULTS-2026-09-18.md,
/// "Raid J"). See POOL-PREWARM-REVIEW.md for the full measurement.
///
/// This class answers one question per ExpandPool call: is this the specific
/// request this feature is allowed to shrink, and if so, by how much.
/// </summary>
internal static class PoolPrewarmPolicy
{
    /// <summary>
    /// The result of one clamp decision. ObjectsCount is always what the
    /// caller should pass to the real ExpandPool (0 only when RunOriginal is
    /// false, in which case the caller must skip the call entirely).
    /// </summary>
    public readonly struct Decision
    {
        /// <summary>True: let the real ExpandPool run, with ObjectsCount. False: skip it — the caller must complete the Task itself.</summary>
        public readonly bool RunOriginal;

        /// <summary>The objectsCount to hand the real ExpandPool; 0 when RunOriginal is false.</summary>
        public readonly int ObjectsCount;

        /// <summary>True only when this decision reduced the request — the ledger counts only these.</summary>
        public readonly bool Clamped;

        private Decision(bool runOriginal, int objectsCount, bool clamped)
        {
            RunOriginal = runOriginal;
            ObjectsCount = objectsCount;
            Clamped = clamped;
        }

        /// <summary>The request passes through untouched — feature off, wrong reason, outside Arena, or already within budget.</summary>
        public static Decision Unclamped(int requestedCount) => new(true, requestedCount, false);

        /// <summary>The request is reduced to `allowed`. `allowed == 0` means skip ExpandPool entirely (RunOriginal is false).</summary>
        public static Decision ClampedTo(int allowed) => new(allowed > 0, allowed, true);
    }

    /// <summary>
    /// Decide what to do with one ExpandPool(objectsCount, ..., reason) call.
    ///
    /// Clamps against `futurePoolSize` (idle instances PLUS whatever is
    /// already queued), never `CurrentPoolSize` (idle alone) — deliberately.
    /// AbstractAssetsPool&lt;T&gt;.ExpandPool folds `objectsCount` into
    /// `QueuedExpandCount` before its own reentrancy guard, so a second,
    /// overlapping caller (another InitAndFillPools batch, or a concurrent
    /// MaintainMinPoolSize top-up) can already be mid-flight for the same
    /// pool. Reading only the idle count would let each caller believe it has
    /// a full `keepIdle` of headroom and independently clamp to `keepIdle`,
    /// letting the pool overshoot toward nearly `2 * keepIdle`.
    /// `futurePoolSize` already reflects any in-flight queue, so two racing
    /// callers converge on the same shrinking `allowed` instead of each
    /// claiming their own.
    ///
    /// This is also what makes the decision idempotent if Harmony ever
    /// chains this prefix twice onto one logical call (see
    /// POOL-PREWARM-REVIEW.md, "Idempotence under Harmony's own
    /// generic-instantiation quirks"): a second invocation sees the SAME
    /// `futurePoolSize` (the real ExpandPool has not run yet, so
    /// QueuedExpandCount has not moved) and the FIRST invocation's already-
    /// clamped `requestedCount` — which by construction always satisfies
    /// `requestedCount &lt;= allowed`, so the second call returns Unclamped
    /// and nothing is double-clamped or double-counted.
    /// </summary>
    public static Decision Decide(bool arenaActive, string reason, int keepIdle, int futurePoolSize, int requestedCount)
    {
        if (!arenaActive || reason != "LoadPlayerResources" || keepIdle <= 0 || requestedCount <= 0)
            return Decision.Unclamped(requestedCount);

        var allowed = Math.Max(0, keepIdle - futurePoolSize);
        if (requestedCount <= allowed)
            return Decision.Unclamped(requestedCount);

        return Decision.ClampedTo(allowed);
    }

    /// <summary>
    /// Whether Reset() should emit the per-raid ledger line. Pulled out as
    /// its own pure function — not just an inline `poolsTouched > 0` in
    /// PoolPrewarmMonitor — because Reset() fires more than once around a
    /// single raid boundary (see ArenaSession.cs's several ResetSession call
    /// sites) and the log-before-clear ordering this depends on cannot be
    /// unit-tested on the Unity-facing class itself; this compiled half can
    /// at least prove the predicate itself never drifts.
    /// </summary>
    public static bool ShouldLogLedger(int poolsTouched) => poolsTouched > 0;
}
