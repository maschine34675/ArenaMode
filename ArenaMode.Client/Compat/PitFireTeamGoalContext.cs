using ArenaMode.Shared;
using System;
using System.Reflection;

namespace ArenaMode.Compat;

/// <summary>Optional version-admitted API reference only; no PFT implementation or dependency is bundled.</summary>
internal static class PitFireTeamGoalContext
{
    internal const string Source = "ArenaMode.TeamArenaPursuit";
    internal const string Reason = "arenaPostDeathPursuit";
    private static MethodInfo _begin;
    private static MethodInfo _currentReason;

    internal static bool Configure(Assembly assembly, string pluginVersion, out string problem)
    {
        _begin = null;
        _currentReason = null;
        problem = "pft-goal-context-contract-unavailable";
        if (assembly?.GetName().Name != "pitFireTeam"
            || !PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assembly.GetName().Version)) return false;
        const string trackerName = "pitTeam.Modules.FollowerGoalEnemyTracker";
        var tracker = assembly.GetType(trackerName, false);
        var scope = assembly.GetType(trackerName + "+GoalEnemySetScope", false);
        var begin = tracker?.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string), typeof(string) }, null);
        var current = tracker?.GetProperty("CurrentReason", BindingFlags.Public | BindingFlags.Static);
        if (scope == null || !scope.IsValueType || !typeof(IDisposable).IsAssignableFrom(scope)
            || begin == null || begin.ReturnType != scope || begin.ContainsGenericParameters
            || current?.PropertyType != typeof(string) || current.GetGetMethod() == null) return false;
        _begin = begin;
        _currentReason = current.GetGetMethod();
        problem = null;
        return true;
    }

    internal static bool TryEnter(out IDisposable scope, out string problem)
    {
        scope = null;
        problem = "pft-goal-context-unavailable";
        if (_begin == null || _currentReason == null) return false;
        // Do not relabel a nested PFT decision as an Arena request. This context
        // is only for our direct unscoped call, never for a preexisting order.
        if (!string.Equals(_currentReason.Invoke(null, null) as string, "unscopedSetter", StringComparison.Ordinal))
        { problem = "pft-goal-context-already-owned"; return false; }
        scope = _begin.Invoke(null, new object[] { Source, Reason }) as IDisposable;
        if (scope == null) return false;
        problem = null;
        return true;
    }
}
