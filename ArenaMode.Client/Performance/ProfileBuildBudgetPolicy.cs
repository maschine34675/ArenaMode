namespace ArenaMode.Performance;

/// <summary>
/// Pure frame-budget decisions behind <c>ProfileBuildBudget</c>: how many bot
/// profiles of a server batch may be built in the current frame. No Unity or
/// EFT types, so <c>ArenaMode.Tests</c> links this file.
///
/// The native client builds every profile of a <c>/client/game/bot/generate</c>
/// answer in the frame the answer lands (<c>ClientBackendSession.LoadBots</c>,
/// ../_decompiled/EFT/ClientBackendSession.cs:1886-1895: one LINQ Select over
/// the descriptors). Measured on SPT-GPU-2 (2026-10-17, Zombie, Factory): the
/// batch for the next spawns arrives about 90 s into the raid and costs one
/// frame of 625-627 ms, with <c>ItemDeserializer.CreateItem</c> on top; the
/// benchmarks of 2026-10-13 showed the same 730-1708 ms hitch at 102-105 s.
/// Spreading the builds over frames under a time budget turns that one frame
/// into a few dozen frames that each stay near their normal length.
/// </summary>
internal static class ProfileBuildBudgetPolicy
{
    /// <summary>Default per-frame budget for profile construction, in milliseconds.</summary>
    internal const float DefaultBudgetMs = 3f;

    /// <summary>Weight of a new sample in the running average: 1/8.</summary>
    internal const int AverageShift = 3;

    internal struct State
    {
        /// <summary>Frame the counters belong to; another frame resets them.</summary>
        public int Frame;

        /// <summary>Ticks spent building profiles in <see cref="Frame"/>.</summary>
        public long SpentTicks;

        /// <summary>Profiles started in <see cref="Frame"/>.</summary>
        public int Started;

        /// <summary>Running average of one profile's build, in ticks (0 = no sample yet).</summary>
        public long AverageTicks;
    }

    /// <summary>
    /// Whether the budgeted path replaces the native one for this call: the
    /// switch is on with a positive budget, an Arena raid is active, the call
    /// is on the main thread (where a yield means "next frame"), and there is
    /// at least one profile to build.
    /// </summary>
    internal static bool ShouldTakeOver(bool enabled, float budgetMs, bool arenaActive, bool onMainThread, int conditionCount)
        => enabled && budgetMs > 0f && !float.IsNaN(budgetMs) && !float.IsInfinity(budgetMs)
           && arenaActive && onMainThread && conditionCount > 0;

    internal static long BudgetTicks(float budgetMs, long ticksPerSecond)
        => budgetMs <= 0f || ticksPerSecond <= 0 ? 0L : (long)(budgetMs * ticksPerSecond / 1000.0);

    /// <summary>
    /// May a batch build one more profile in <paramref name="frame"/>?
    ///
    /// Every batch that is still open gets one guaranteed build per frame:
    /// while fewer builds than <paramref name="activeBatches"/> have started
    /// in this frame and this batch has not built yet (<paramref
    /// name="batchBuiltThisFrame"/>), the answer is yes. That keeps progress
    /// independent of the budget (a frame can never be spent on nothing) and
    /// keeps a short, urgent batch from waiting behind a long one that
    /// arrived earlier: Unity resumes yielded continuations in posting order,
    /// so without the per-batch slot the older loop would win every frame.
    /// Beyond those slots a build starts only while the running average says
    /// it still fits into what is left of the budget. The effective frame cap
    /// is therefore the budget or one build per open batch, whichever is
    /// larger.
    /// </summary>
    internal static bool MayStart(ref State state, int frame, long budgetTicks, int activeBatches, bool batchBuiltThisFrame)
    {
        if (state.Frame != frame)
        {
            state.Frame = frame;
            state.SpentTicks = 0;
            state.Started = 0;
        }
        int slots = activeBatches < 1 ? 1 : activeBatches;
        if (!batchBuiltThisFrame && state.Started < slots)
            return true;
        return state.SpentTicks + state.AverageTicks <= budgetTicks;
    }

    /// <summary>Books one finished build into the frame counters and the running average.</summary>
    internal static void Record(ref State state, long tookTicks)
    {
        if (tookTicks < 0) tookTicks = 0;
        state.SpentTicks += tookTicks;
        state.Started++;
        state.AverageTicks = state.AverageTicks == 0
            ? tookTicks
            : state.AverageTicks + ((tookTicks - state.AverageTicks) >> AverageShift);
    }
}
