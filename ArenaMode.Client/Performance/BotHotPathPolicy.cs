using System.Collections.Generic;

namespace ArenaMode.Performance;

/// <summary>
/// Pure decisions behind <c>ArenaBotHotPaths</c>: two pieces of per-frame
/// bot work the native game repeats without a result. No Unity or EFT types,
/// so <c>ArenaMode.Tests</c> links this file directly (see the
/// <c>ClientRules</c> Compile Include links in <c>ArenaMode.Tests.csproj</c>).
/// </summary>
internal static class BotHotPathPolicy
{
    /// <summary>
    /// Empties the AI core's removal queue after applying it.
    ///
    /// Native <c>AICoreController.Update</c> (../_decompiled/AICoreController.cs:
    /// 37-61) walks <c>_listToDel</c> every frame and removes each entry from
    /// <c>_listAgents</c>, but never clears the queue; <c>Clear()</c> (:75-79)
    /// clears only the agent and error sets. An agent is queued exactly once, by
    /// its own <c>AICoreAgent.Dispose</c> (../_decompiled/AICoreAgent.cs:135-137),
    /// after its constructor added it (:45), and no caller re-adds a disposed
    /// agent — so once the removal has been applied, every later pass over the
    /// queue is a no-op that still costs <c>queued × HashSet.Remove</c> per frame
    /// and keeps every dead brain (with its BotOwner) reachable for the whole
    /// raid. Applying the removal here and clearing the queue leaves
    /// <c>_listAgents</c> exactly as the native pass would, one statement earlier
    /// in the same frame.
    ///
    /// <paramref name="controllerEnabled"/> is the controller's own
    /// <c>_enable</c>: while it is false the native Update returns before the
    /// removal pass, so the queue must be left alone to keep the native order
    /// of events (the removal then happens on the first enabled frame, as in
    /// vanilla). Returns the number of agents applied, for the diagnostics
    /// counter.
    /// </summary>
    internal static int TrimDeletedAgents<T>(HashSet<T> live, HashSet<T> queued, bool enabled, bool controllerEnabled)
    {
        if (!enabled || !controllerEnabled || live == null || queued == null || queued.Count == 0)
            return 0;
        int count = queued.Count;
        foreach (var agent in queued)
            live.Remove(agent);
        queued.Clear();
        return count;
    }

    /// <summary>
    /// Whether a body's per-frame FOV coroutine may be replaced by its two
    /// direct calls. Only an AI body under the third-person strategy
    /// qualifies: <c>ThirdPersonStrategy.ApplyFovAdjustments</c> sets
    /// <c>RibcageScaleCurrent = 1</c> and <c>ResetFovAdjustments</c> puts a
    /// drifted ribcage scale back to one (../_decompiled/ThirdPersonStrategy.cs:
    /// 50-62); neither depends on anything that happens between the start and
    /// the end of the frame for a bot. The local player's first-person strategy
    /// keeps its coroutine untouched.
    /// </summary>
    internal static bool ShouldShortCircuitFov(bool enabled, bool arenaActive, bool isAI, bool thirdPersonStrategy)
        => enabled && arenaActive && isAI && thirdPersonStrategy;
}
