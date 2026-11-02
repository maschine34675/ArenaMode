// The installed ArenaMapPort client, loaded read-only for the real-surface stage of these checks
// (Program.cs, last section; docs/reviews/CROWD-REACTIONS-20261102.md, "Verification"). MapPort's
// SpectatorCrowd contract and the state behind it (CrowdReactionQueue, CrowdBedState) are Unity-free
// (MapPort's own tests run them without Unity), so the stage executes them: ArenaMode's binder binds
// the real type and the service runs against the real queue. Only that state is touched, through
// the internal members MapPort's tests and its Unity side use: a fresh queue and bed state per
// scenario, SetKinds (what SpectatorAudio publishes during raid loading and its developer key
// withdraws), Reset (ArenaSpectators.OnDestroy, the crowd removed), the bed's Available flag, Drain
// and the counters. Nothing is written to disk and nothing Unity-side (SpectatorAudio, the planner,
// the bed) runs. A missing DLL, a missing contract member or a missing internal type fails loudly.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

internal sealed class RealMapPort
{
    internal const string Requirement = "an ArenaMapPort client with crowd reactions phase A "
        + "(D:\\ArenaMapPort commit d70acd2 or later on feature/iceberg, unreleased; the installed development "
        + "build 0.4.0+08964c1 carries it): the eight SpectatorCrowd members ContractVersion, IsAvailable, "
        + "Excitement, SetExcitement, ReactionKinds, ClaimReactions, React, ReleaseReactions";
    private const BindingFlags Public = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const BindingFlags Inner = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private RealMapPort() { }

    internal string Path { get; private set; }
    internal string ProductVersion { get; private set; }
    internal string Sha256 { get; private set; }
    internal Type SpectatorCrowd { get; private set; }
    private Type _queueType, _stateType, _kindType, _requestType;

    internal static RealMapPort Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new InvalidOperationException("FAIL: the installed ArenaMapPort client is missing (" + path
                + "). The real-surface stage needs " + Requirement + ".");
        var real = new RealMapPort
        {
            Path = path,
            ProductVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
        };
        var assembly = Assembly.LoadFrom(path);
        real.SpectatorCrowd = assembly.GetType("ArenaMapPort.Client.SpectatorCrowd");
        string missing = real.SpectatorCrowd == null ? "the type ArenaMapPort.Client.SpectatorCrowd" : MissingMembers(real.SpectatorCrowd);
        if (missing != null)
            throw new InvalidOperationException("FAIL: the installed ArenaMapPort client (" + path + ", ProductVersion "
                + real.ProductVersion + ") lacks " + missing + ". The real-surface stage needs " + Requirement + ".");
        real._queueType = Internal(assembly, "CrowdReactionQueue", real);
        real._stateType = Internal(assembly, "CrowdBedState", real);
        real._kindType = Internal(assembly, "CrowdReactionKind", real);
        real._requestType = Internal(assembly, "CrowdReactionRequest", real);
        return real;
    }

    // The contract members with their exact signatures (MapPort's SpectatorCrowd, contract version 1);
    // null when all eight are there. Program.cs also runs it on the stand-in 0.4.0 surface.
    internal static string MissingMembers(Type type)
    {
        var missing = new List<string>();
        void Property(string name, Type value)
        {
            var property = type.GetProperty(name, Public);
            if (property?.GetGetMethod() == null || property.PropertyType != value) missing.Add(value.Name + " " + name);
        }
        void Method(string name, Type returns, params Type[] args)
        {
            var method = type.GetMethod(name, Public, null, args, null);
            if (method == null || method.ReturnType != returns)
                missing.Add(returns.Name + " " + name + "(" + string.Join(", ", args.Select(arg => arg.Name)) + ")");
        }
        Property("ContractVersion", typeof(int));
        Property("IsAvailable", typeof(bool));
        Property("Excitement", typeof(float));
        Method("SetExcitement", typeof(void), typeof(float));
        Property("ReactionKinds", typeof(int));
        Method("ClaimReactions", typeof(int), typeof(string));
        Method("React", typeof(bool), typeof(int), typeof(int), typeof(float));
        Method("ReleaseReactions", typeof(bool), typeof(int));
        return missing.Count == 0 ? null : "the SpectatorCrowd members " + string.Join(", ", missing);
    }

    private static Type Internal(Assembly assembly, string name, RealMapPort real)
        => assembly.GetType("ArenaMapPort.Client." + name) ?? throw new InvalidOperationException(
            "FAIL: the installed ArenaMapPort client (" + real.Path + ", ProductVersion " + real.ProductVersion
            + ") has no internal type ArenaMapPort.Client." + name + ", which the real-surface stage drives. It needs "
            + Requirement + "; a later MapPort that renamed it needs this stage updated.");

    private static object Static(Type type, string field) => type.GetField(field, Inner).GetValue(null);
    private static object Constant(Type type, string field) => type.GetField(field, Inner).GetRawConstantValue();
    private static object Get(Type type, object instance, string property) => type.GetProperty(property, Inner).GetValue(instance);
    private object Queue => Static(SpectatorCrowd, "Reactions");
    private object State => Static(SpectatorCrowd, "State");

    /// <summary>A fresh queue and bed state, as MapPort's tests replace them, then the crowd's published kinds.</summary>
    internal void Fresh(int kinds)
    {
        SpectatorCrowd.GetField("Reactions", Inner).SetValue(null, Activator.CreateInstance(_queueType, true));
        SpectatorCrowd.GetField("State", Inner).SetValue(null, Activator.CreateInstance(_stateType, true));
        SetKinds(kinds);
    }

    /// <summary>SpectatorAudio.PublishKinds: the reaction set is ready (62), or withdrawn (0, developer key or a disabled component).</summary>
    internal void SetKinds(int mask) => _queueType.GetMethod("SetKinds", Inner).Invoke(Queue, new object[] { mask });

    /// <summary>ArenaSpectators.OnDestroy: the crowd is removed; every handle is void, Kinds is 0.</summary>
    internal void Reset() => _queueType.GetMethod("Reset", Inner).Invoke(Queue, null);

    /// <summary>The bed's flag (SpectatorCrowdBed writes it on the Unity side); IsAvailable reads it.</summary>
    internal bool Available { set => _stateType.GetProperty("Available", Inner).SetValue(State, value); }

    internal float Excitement => (float)Get(_stateType, State, "Excitement");
    internal int Kinds => (int)Get(_queueType, Queue, "Kinds");
    internal long Accepted => (long)Get(_queueType, Queue, "Accepted");
    internal long Refused => (long)Get(_queueType, Queue, "Refused");
    internal string HolderOwner => (string)Get(_queueType, Queue, "HolderOwner");
    internal int Holder => (int)_queueType.GetField("_holder", Inner).GetValue(Queue);

    internal int KindConstant(string name) => (int)Constant(_kindType, name);
    internal float MinStrength => (float)Constant(_queueType, "MinStrength");
    internal int OwnerMax => (int)Constant(_queueType, "OwnerMax");
    internal string DemoOwner => (string)Constant(_queueType, "DemoOwner");
    internal string CleanOwner(string owner) => (string)_queueType.GetMethod("CleanOwner", Inner).Invoke(null, new object[] { owner });

    /// <summary>
    /// Takes what waits in the ring, oldest first, as the Unity side's drain does. Drained at stamp 0,
    /// which counts no entry stale: the 2 s age is MapPort's playback rule, not what is checked here.
    /// </summary>
    internal List<(int kind, float strength)> Drain()
    {
        var buffer = Array.CreateInstance(_requestType, 16);
        int count = (int)_queueType.GetMethod("Drain", Inner).Invoke(Queue, new object[] { buffer, 0L });
        var kind = _requestType.GetField("Kind");
        var strength = _requestType.GetField("Strength");
        var drained = new List<(int kind, float strength)>();
        for (int index = 0; index < count; index++)
        {
            object request = buffer.GetValue(index);
            drained.Add(((int)kind.GetValue(request), (float)strength.GetValue(request)));
        }
        return drained;
    }

    // The public contract, called as another plugin or MapPort's developer demo calls it.
    internal int Claim(string owner) => (int)SpectatorCrowd.GetMethod("ClaimReactions", Public).Invoke(null, new object[] { owner });
    internal bool React(int handle, int kind, float strength)
        => (bool)SpectatorCrowd.GetMethod("React", Public).Invoke(null, new object[] { handle, kind, strength });
}
