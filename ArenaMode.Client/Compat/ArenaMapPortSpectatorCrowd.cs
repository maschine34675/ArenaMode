using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace ArenaMode.Compat;

/// <summary>
/// Optional, primitive-only MapPort contract: <c>ArenaMapPort.Client.SpectatorCrowd</c>, contract
/// version 1, with its append-only reaction members (D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md,
/// 4.3; docs/reviews/CROWD-REACTIONS-20261102.md). A sibling of <see cref="ArenaMapPortScoreMonitors"/>:
/// found by reflection from the plugin <c>com.maschine.ArenaMapPort</c>, exact signatures only, no
/// BepInDependency, and nothing in a match ever depends on it.
///
/// Two independent parts, because they ship in different MapPort versions:
/// <list type="bullet">
/// <item>Excitement: <c>SetExcitement(float)</c> (and the optional <c>IsAvailable</c>). Published in
/// ArenaMapPort 0.4.0, so the murmur intensity works today.</item>
/// <item>Reactions: <c>ReactionKinds</c>, <c>ClaimReactions(string)</c>, <c>React(int, int, float)</c>,
/// <c>ReleaseReactions(int)</c>. Phase A of the plan, not shipped by any MapPort yet; against 0.4.0
/// this part is absent and stays dormant.</item>
/// </list>
/// The excitement part must never require the reaction members (plan 6, "must not"): a binder
/// that did would switch the 0.4.0 murmur off. The reaction part does not require SetExcitement
/// either. Both require ContractVersion == 1: a member is only changed in meaning together with
/// a new version, so another version means neither part can be trusted.
///
/// Absent members mean an older MapPort: that part is off without a warning (the service writes
/// one Debug line per match). A member that exists with another signature (property type,
/// parameter or return type, a field instead of a property) is an incompatibility: that part is
/// off and one Warning is written per process, like <c>ArenaMapPortSpawnGates.BindCompletion</c>,
/// which treats only true absence as an older API.
/// </summary>
internal static class ArenaMapPortSpectatorCrowd
{
    internal const string PluginGuid = "com.maschine.ArenaMapPort";
    internal const string ContractTypeName = "ArenaMapPort.Client.SpectatorCrowd";
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // MapPort documents the kind numbers instead of publishing constants (its contract pin test
    // forbids constants and fields on the contract class). Mirrored here; the plan's table 4.1 is
    // the source. Kinds 6 (Lull) and 7 (PurifiedTeam) are reserved there and never sent from here.
    internal const int Approve = 1, Disapprove = 2, TakeCover = 3, Start = 4, Finish = 5;
    internal const int LowestKind = 1, HighestKind = 31;

    // Per process, per reason: an incompatible installation does not change between matches, and
    // one line names it. The service still re-binds every match (a hot-swapped DLL is not supported).
    private static bool _warnedVersion, _warnedExcitement, _warnedReactions, _warnedBind;

    internal enum Part { Absent, Bound, Incompatible }

    /// <summary>
    /// C# masks a shift count to five bits, so <c>1 &lt;&lt; 33</c> is <c>2</c>. Both sides range-check
    /// 1..31 before shifting (plan 4.1); an out-of-range kind has no bit at all.
    /// </summary>
    internal static int Bit(int kind) => kind >= LowestKind && kind <= HighestKind ? 1 << kind : 0;

    internal static string KindName(int kind) => kind switch
    {
        Approve => "Approve",
        Disapprove => "Disapprove",
        TakeCover => "TakeCover",
        Start => "Start",
        Finish => "Finish",
        _ => "Kind" + kind,
    };

    /// <summary>The delegates of one bind. Bound once per match; members of an absent part stay null.</summary>
    internal sealed class Contract
    {
        internal Contract(Part excitement, Part reactions, string summary)
        { Excitement = excitement; Reactions = reactions; Summary = summary; }

        internal Part Excitement { get; }
        internal Part Reactions { get; }
        /// <summary>One short phrase for the service's one Debug line per match.</summary>
        internal string Summary { get; }
        internal Func<bool> IsAvailable { get; set; }
        internal Action<float> SetExcitement { get; set; }
        internal Func<int> ReactionKinds { get; set; }
        internal Func<string, int> ClaimReactions { get; set; }
        internal Func<int, int, float, bool> React { get; set; }
        internal Func<int, bool> ReleaseReactions { get; set; }
    }

    /// <summary>Finds the contract of the loaded ArenaMapPort. Never throws.</summary>
    internal static Contract Bind()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin) || plugin?.Instance == null)
                return new Contract(Part.Absent, Part.Absent, "ArenaMapPort not loaded");
            return BindContract(plugin.Instance.GetType().Assembly.GetType(ContractTypeName));
        }
        catch (Exception ex)
        {
            Warn(ref _warnedBind, "ArenaMapPort crowd contract unavailable: " + ex.GetBaseException().Message);
            return new Contract(Part.Incompatible, Part.Incompatible, "bind failed");
        }
    }

    // Kept separate for production-linked optional-contract checks (tools/CrowdReactionChecks),
    // as ArenaMapPortScoreMonitors.TryClaimContract is for the score monitors.
    internal static Contract BindContract(Type type)
    {
        if (type == null)
            return new Contract(Part.Absent, Part.Absent, "ArenaMapPort without SpectatorCrowd (older than 0.4.0)");
        try
        {
            var version = Getter<int>(type, "ContractVersion", out _);
            if (version == null)
            {
                Warn(ref _warnedVersion, "ArenaMapPort SpectatorCrowd has no int ContractVersion property; "
                    + "crowd reactions and murmur intensity stay off.");
                return new Contract(Part.Incompatible, Part.Incompatible, "contract without ContractVersion");
            }
            int contractVersion = version();
            if (contractVersion != 1)
            {
                Warn(ref _warnedVersion, $"ArenaMapPort SpectatorCrowd contract version {contractVersion} is not "
                    + "supported (expected 1); crowd reactions and murmur intensity stay off.");
                return new Contract(Part.Incompatible, Part.Incompatible, "contract version " + contractVersion);
            }

            // Excitement: SetExcitement alone decides the part. IsAvailable is optional (the
            // service only skips writes while it reads false); a changed IsAvailable still means
            // the published 0.4.0 surface changed without a version bump.
            var available = Getter<bool>(type, "IsAvailable", out bool availablePresent);
            var excite = Method<Action<float>>(type, "SetExcitement", typeof(void), out bool excitePresent, typeof(float));
            bool excitementChanged = (excitePresent && excite == null) || (availablePresent && available == null);
            var excitement = excitementChanged ? Part.Incompatible : excite != null ? Part.Bound : Part.Absent;
            if (excitement == Part.Incompatible)
                Warn(ref _warnedExcitement, "ArenaMapPort SpectatorCrowd SetExcitement/IsAvailable have another "
                    + "signature; murmur intensity stays off.");

            // Reactions: all four or nothing. A member with another signature is an
            // incompatibility; a missing one (with no wrong one) is an older MapPort.
            var kinds = Getter<int>(type, "ReactionKinds", out bool kindsPresent);
            var claim = Method<Func<string, int>>(type, "ClaimReactions", typeof(int), out bool claimPresent,
                typeof(string));
            var react = Method<Func<int, int, float, bool>>(type, "React", typeof(bool), out bool reactPresent,
                typeof(int), typeof(int), typeof(float));
            var release = Method<Func<int, bool>>(type, "ReleaseReactions", typeof(bool), out bool releasePresent,
                typeof(int));
            bool reactionsChanged = (kindsPresent && kinds == null) || (claimPresent && claim == null)
                || (reactPresent && react == null) || (releasePresent && release == null);
            var reactions = reactionsChanged ? Part.Incompatible
                : kinds != null && claim != null && react != null && release != null ? Part.Bound : Part.Absent;
            if (reactions == Part.Incompatible)
                Warn(ref _warnedReactions, "ArenaMapPort SpectatorCrowd reaction members have another signature; "
                    + "crowd reactions stay off.");

            return new Contract(excitement, reactions,
                "excitement=" + Describe(excitement) + ", reactions=" + Describe(reactions))
            {
                IsAvailable = excitement == Part.Bound ? available : null,
                SetExcitement = excitement == Part.Bound ? excite : null,
                ReactionKinds = reactions == Part.Bound ? kinds : null,
                ClaimReactions = reactions == Part.Bound ? claim : null,
                React = reactions == Part.Bound ? react : null,
                ReleaseReactions = reactions == Part.Bound ? release : null,
            };
        }
        catch (Exception ex)
        {
            Warn(ref _warnedBind, "ArenaMapPort crowd contract unavailable: " + ex.GetBaseException().Message);
            return new Contract(Part.Incompatible, Part.Incompatible, "bind failed");
        }
    }

    private static string Describe(Part part) => part switch
    {
        Part.Bound => "bound",
        Part.Incompatible => "incompatible",
        _ => "absent (older ArenaMapPort)",
    };

    private static void Warn(ref bool warned, string message)
    {
        if (warned) return;
        warned = true;
        Plugin.ModLogger.LogWarning(message);
    }

    // "Present" is any public static member of that name: a field, a property of another type and a
    // method overload all count, so a changed member never reads as an older MapPort.
    private static bool Present(Type type, string name) => type.GetMember(name, Declared).Length > 0;

    // Semantics of ArenaMapPortScoreMonitors.Getter, plus presence for the absent/changed split.
    private static Func<T> Getter<T>(Type type, string name, out bool present)
    {
        present = Present(type, name);
        if (!present) return null;
        PropertyInfo property;
        try { property = type.GetProperty(name, Declared); }
        catch (AmbiguousMatchException) { return null; }
        var getter = property?.GetGetMethod();
        if (getter == null || getter.ReturnType != typeof(T) || getter.ContainsGenericParameters
            || getter.GetParameters().Length != 0) return null;
        return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), getter);
    }

    // Semantics of ArenaMapPortScoreMonitors.Bind (exact parameter types: a contravariant object
    // parameter is not the string contract), plus presence for the absent/changed split.
    private static T Method<T>(Type type, string name, Type returns, out bool present, params Type[] args)
        where T : Delegate
    {
        present = Present(type, name);
        if (!present) return null;
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

    /// <summary>
    /// One match's reaction claim: the exact, non-reusable handle MapPort returned. A lost claim
    /// shows up as <c>false</c> from React (stale generation, foreign owner, refused) and ends the
    /// claim for the match; it is never re-claimed, so a crowd rebuilt after a failure stays silent
    /// instead of being fought over.
    /// </summary>
    internal sealed class ReactionClaim
    {
        private readonly Func<int, int, float, bool> _react;
        private readonly Func<int, bool> _release;
        private bool _released;

        private ReactionClaim(int handle, int kinds, Func<int, int, float, bool> react, Func<int, bool> release)
        { Handle = handle; Kinds = kinds; _react = react; _release = release; }

        internal int Handle { get; }
        internal int Kinds { get; }
        internal bool Alive => !_released;
        internal bool Has(int kind) => (Kinds & Bit(kind)) != 0;

        /// <summary>
        /// One attempt: read ReactionKinds, claim only when it holds a kind we send. <paramref name="kinds"/>
        /// returns the mask read (0 = no crowd: Iceberg, a vanilla map, no reaction set, switched off).
        /// <paramref name="failed"/> means a member threw, which the contract says never happens:
        /// the caller stops trying for this match.
        /// </summary>
        internal static ReactionClaim TryClaim(Contract contract, string owner, int wanted, out int kinds, out bool failed)
        {
            kinds = 0;
            failed = false;
            if (contract?.Reactions != Part.Bound || string.IsNullOrWhiteSpace(owner)) return null;
            try
            {
                kinds = contract.ReactionKinds();
                if ((kinds & wanted) == 0) return null;
                int handle = contract.ClaimReactions(owner);
                return handle > 0 ? new ReactionClaim(handle, kinds, contract.React, contract.ReleaseReactions) : null;
            }
            catch (Exception ex)
            {
                failed = true;
                Plugin.ModLogger.LogWarning("ArenaMapPort crowd reactions unavailable: " + ex.GetBaseException().Message);
                return null;
            }
        }

        /// <summary>
        /// Queues one reaction. A kind outside the claimed mask is skipped. Returns false once the
        /// claim is lost, after releasing it.
        /// </summary>
        internal bool Push(int kind, float strength)
        {
            if (_released) return false;
            if (!Has(kind) || float.IsNaN(strength)) return true;
            try
            {
                if (_react(Handle, kind, strength)) return true;
                Release();
                return false;
            }
            catch (Exception ex)
            {
                Release();
                Plugin.ModLogger.LogWarning("ArenaMapPort crowd reactions stopped: " + ex.GetBaseException().Message);
                return false;
            }
        }

        /// <summary>
        /// Gives the handle back exactly once. Attempted after a refused React too: a refusal need not
        /// mean the claim was lost, and a stale handle changes nothing on MapPort's side.
        /// </summary>
        internal bool Release()
        {
            if (_released) return false;
            _released = true;
            try { _release(Handle); }
            catch (Exception ex)
            { Plugin.ModLogger.LogWarning("ArenaMapPort crowd reactions release failed: " + ex.GetBaseException().Message); }
            return true;
        }
    }
}
