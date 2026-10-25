using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace ArenaMode.Compat;

/// <summary>
/// Optional, primitive-only MapPort contract. A failed display never changes
/// match state; each session gives back only its exact, non-reusable handle.
/// </summary>
internal static class ArenaMapPortScoreMonitors
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    internal readonly struct Frame
    {
        internal Frame(int colorA, int colorB, int teamSize, int scoreA, int scoreB,
            int aliveA, int aliveB, int clockMode, float clockSeconds)
        {
            ColorA = colorA; ColorB = colorB; TeamSize = teamSize;
            ScoreA = scoreA; ScoreB = scoreB; AliveA = aliveA; AliveB = aliveB;
            ClockMode = clockMode; ClockSeconds = clockSeconds;
        }
        internal int ColorA { get; }
        internal int ColorB { get; }
        internal int TeamSize { get; }
        internal int ScoreA { get; }
        internal int ScoreB { get; }
        internal int AliveA { get; }
        internal int AliveB { get; }
        internal int ClockMode { get; }
        internal float ClockSeconds { get; }
    }

    internal static Session TryClaim(string owner)
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.maschine.ArenaMapPort", out var plugin)
                || plugin.Instance == null) return null;
            return TryClaimContract(plugin.Instance.GetType().Assembly.GetType("ArenaMapPort.Client.ScoreMonitors"), owner);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("ArenaMapPort score monitors unavailable: " + ex.GetBaseException().Message);
            return null;
        }
    }

    // Kept separate for production-linked optional-contract checks.
    internal static Session TryClaimContract(Type type, string owner)
    {
        if (type == null || string.IsNullOrWhiteSpace(owner)) return null;
        try
        {
            var version = Getter<int>(type, "ContractVersion");
            var available = Getter<bool>(type, "IsAvailable");
            var claim = Bind<Func<string, int>>(type, "Claim", typeof(int), typeof(string));
            var release = Bind<Func<int, bool>>(type, "Release", typeof(bool), typeof(int));
            var sides = Bind<Func<int, int, int, int, bool>>(type, "SetSides", typeof(bool),
                typeof(int), typeof(int), typeof(int), typeof(int));
            var score = Bind<Func<int, int, int, int, int, bool>>(type, "SetScore", typeof(bool),
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(int));
            var clock = Bind<Func<int, int, float, bool>>(type, "SetClock", typeof(bool),
                typeof(int), typeof(int), typeof(float));
            if (version == null || available == null || claim == null || release == null
                || sides == null || score == null || clock == null)
            {
                Plugin.ModLogger.LogWarning("ArenaMapPort score monitor contract is incompatible; monitors remain off.");
                return null;
            }
            if (version() != 1 || !available()) return null;
            int handle = claim(owner);
            return handle > 0 ? new Session(handle, release, sides, score, clock) : null;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning("ArenaMapPort score monitors unavailable: " + ex.GetBaseException().Message);
            return null;
        }
    }

    private static Func<T> Getter<T>(Type type, string name)
    {
        var getter = type.GetProperty(name, Declared)?.GetGetMethod();
        if (getter == null || getter.ReturnType != typeof(T) || getter.ContainsGenericParameters
            || getter.GetParameters().Length != 0) return null;
        return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), getter);
    }

    private static T Bind<T>(Type type, string name, Type returns, params Type[] args) where T : Delegate
    {
        MethodInfo method;
        try { method = type.GetMethod(name, Declared, null, args, null); }
        catch (AmbiguousMatchException) { return null; }
        if (method == null || method.ReturnType != returns || method.ContainsGenericParameters) return null;
        var parameters = method.GetParameters();
        if (parameters.Length != args.Length) return null;
        for (int index = 0; index < args.Length; index++)
            if (parameters[index].ParameterType != args[index]) return null;
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    internal sealed class Session
    {
        private readonly int _handle;
        private readonly Func<int, bool> _release;
        private readonly Func<int, int, int, int, bool> _sides;
        private readonly Func<int, int, int, int, int, bool> _score;
        private readonly Func<int, int, float, bool> _clock;
        private Frame? _sent;
        private bool _releaseAttempted;

        internal Session(int handle, Func<int, bool> release, Func<int, int, int, int, bool> sides,
            Func<int, int, int, int, int, bool> score, Func<int, int, float, bool> clock)
        { _handle = handle; _release = release; _sides = sides; _score = score; _clock = clock; }

        internal bool Alive => !_releaseAttempted;

        internal bool Push(in Frame frame)
        {
            if (_releaseAttempted) return false;
            try
            {
                // The cheap idempotent clock setter also validates the handle
                // when the entire frame is unchanged (e.g. a frozen result).
                bool accepted = _clock(_handle, frame.ClockMode, frame.ClockSeconds);
                var last = _sent;
                if (accepted && (last == null || last.Value.ColorA != frame.ColorA
                    || last.Value.ColorB != frame.ColorB || last.Value.TeamSize != frame.TeamSize))
                    accepted = _sides(_handle, frame.ColorA, frame.ColorB, frame.TeamSize);
                if (accepted && (last == null || last.Value.ScoreA != frame.ScoreA
                    || last.Value.ScoreB != frame.ScoreB || last.Value.AliveA != frame.AliveA
                    || last.Value.AliveB != frame.AliveB))
                    accepted = _score(_handle, frame.ScoreA, frame.ScoreB, frame.AliveA, frame.AliveB);
                if (!accepted) { Release(); return false; }
                _sent = frame;
                return true;
            }
            catch (Exception ex)
            {
                Release();
                Plugin.ModLogger.LogWarning("ArenaMapPort score monitors stopped: " + ex.GetBaseException().Message);
                return false;
            }
        }

        internal void Release()
        {
            if (_releaseAttempted) return;
            _releaseAttempted = true;
            // A stale handle changes nothing. Still attempt this after a
            // setter failure: that failure need not mean the claim was lost.
            try { _release(_handle); }
            catch (Exception ex)
            { Plugin.ModLogger.LogWarning("ArenaMapPort score monitors release failed: " + ex.GetBaseException().Message); }
        }
    }
}
