using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using ArenaMode.Server;
using ArenaMode.Server.Compat;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Models.Eft.Common;
using Path = System.IO.Path;
using Info = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;

internal static class Program
{
    private static int _checks, _configCalls, _nativeEnds, _persistCalls;
    private static object _config = null!;
    private static Type _configType = null!;
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId Other = new("1123456789abcdef01234567");
    private static ArenaSessionService _sessions = null!;
    private static ProfileSnapshotService _snapshots = null!;
    private static SaveServer _saves = null!;
    private static bool _failPersist;
    private static string _response = "null-response", _startIdentity = "ordinary.Pmc generated";
    private static Task? _nativeTask;
    private static bool _checkReentry;
    private static MatchCallbacks _callbacks = null!;
    private static TaskCompletionSource _releaseChild = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<bool>? _inheritedAuthority;
    private static MethodInfo Hook(string name) => typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Check(bool yes, string reason) { if (!yes) throw new Exception("ASSERT: " + reason); _checks++; }

    public static async Task Main(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new Exception("Supply published ServerValueModifier.dll [absent|unsupported]");
        string? fallback = args.Length == 2 ? args[1] : null;
        string dll = Path.GetFullPath(args[0]);
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) ==
            "7A142971404A4D84A20D855837EBED1E1E07921B05BA714C9F49B47BE7CFCADB", "published SVM fixture identity");
        Assembly svm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        IModMetadata metadata = (IModMetadata)Activator.CreateInstance(svm.GetType("ServerValueModifier.SVM_MD", true)!)!;
        var mod = new SptMod { Directory = Path.GetDirectoryName(dll)!, ModMetadata = metadata, Assemblies = [svm] };
        Check(metadata.Version.ToString() == "2.2.3", "published SVM semantic version");
        Type foreignType = svm.GetType("ServerValueModifier.HarmonyOverrides.LocalRaidPatch", true)!;
        MethodInfo prefix = foreignType.GetMethod("Prefix")!;
        MethodInfo callback = typeof(MatchCallbacks).GetMethod(nameof(MatchCallbacks.EndLocalRaidAsync))!;
        MethodInfo lifecycle = typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.EndLocalRaidAsync))!;
        var foreignHarmony = new Harmony(ArenaSvmCompat.ForeignHarmonyId);
        var nativeHarness = new Harmony("svm-checks-only");
        var response = new HttpResponseUtil(new JsonUtil([]), null!);
        foreignType.GetField("_httpResponseUtil", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, response);
        nativeHarness.Patch(typeof(HttpResponseUtil).GetMethod(nameof(HttpResponseUtil.NullResponse))!, prefix: new HarmonyMethod(Hook(nameof(NullResponse))));
        Type cfg = svm.GetType("ServerValueModifier.SVMConfig", true)!;
        nativeHarness.Patch(cfg.GetConstructors().Single(), prefix: new HarmonyMethod(Hook(nameof(SkipConfigConstruction))));
        nativeHarness.Patch(cfg.GetMethod("CallConfig")!, prefix: new HarmonyMethod(Hook(nameof(ConfigResult))));
        _configType = cfg.GetProperty("cf")!.PropertyType;
        Configure(saveLoot: true, ignore: false);
        if (fallback != "absent") foreignHarmony.Patch(callback, prefix: new HarmonyMethod(prefix));
        new Harmony(nameof(ArenaEndLocalRaidPatch)).Patch(callback,
            prefix: new HarmonyMethod(typeof(ArenaEndLocalRaidPatch).GetMethod(nameof(ArenaEndLocalRaidPatch.Prefix))!));
        if (fallback == null)
        {
            var resolved = ArenaSvmContract.Resolve(mod);
            Check(resolved.ForeignPrefix == prefix && resolved.Callback == callback, "published prefix/native callback contract");
            ArenaSvmCompat.TryEnable([mod]);
            Check(!ArenaSvmCompat.TryGetAdmissionFailure(out _), "published adapter ready");
        }
        else
        {
            nativeHarness.Patch(callback, prefix: new HarmonyMethod(Hook(nameof(FallbackObserver))));
            ArenaSvmCompat.TryEnable(fallback == "absent" ? [] : [mod]);
            Check(!ArenaSvmCompat.Enabled, "unavailable SVM does not enable callback bridge");
            Check(ArenaSvmCompat.TryGetAdmissionFailure(out _) == (fallback != "absent"), "only unsupported installed SVM blocks Arena");
            Check(!Harmony.GetPatchInfo(callback)!.Prefixes.Single(p => p.PatchMethod.DeclaringType == typeof(ArenaEndLocalRaidPatch)).before.Contains(ArenaSvmCompat.ForeignHarmonyId), "fallback preserves ordinary wrapper ordering");
        }
        new Harmony(nameof(ArenaPostRaidPmcSkipPatch)).Patch(lifecycle,
            prefix: new HarmonyMethod(typeof(ArenaPostRaidPmcSkipPatch).GetMethod(nameof(ArenaPostRaidPmcSkipPatch.Prefix))!));
        nativeHarness.Patch(lifecycle, prefix: new HarmonyMethod(Hook(nameof(NativeEnd))) { after = [nameof(ArenaPostRaidPmcSkipPatch)] });
        nativeHarness.Patch(typeof(ArenaProtectedRaidEndService).GetMethod(nameof(ArenaProtectedRaidEndService.GetOrStart))!,
            prefix: new HarmonyMethod(Hook(nameof(Persist))));
        var controller = (MatchController)RuntimeHelpers.GetUninitializedObject(typeof(MatchController));
        typeof(MatchController).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(LocationLifecycleService)).SetValue(controller,
                RuntimeHelpers.GetUninitializedObject(typeof(LocationLifecycleService)));
        var callbacks = new MatchCallbacks(null!, response, controller);
        _callbacks = callbacks;
        ArenaContext.ProtectedRaidEnd = (ArenaProtectedRaidEndService)RuntimeHelpers.GetUninitializedObject(typeof(ArenaProtectedRaidEndService));

        if (fallback != null)
        {
            Initialize(); Configure(false, false);
            nativeHarness.Patch(typeof(MatchController).GetMethod(nameof(MatchController.StartLocalRaidAsync))!, prefix: new HarmonyMethod(Hook(nameof(NativeStart))));
            var service = new OrdinaryRaidLifecycleService(controller, response, _sessions, _snapshots, Logger.Create<OrdinaryRaidLifecycleService>());
            ArenaContext.OrdinaryRaids = service;
            await StartOrdinary(service);
            await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session);
            Check(_nativeEnds == 1 && _fallbackObserved == 1 && !service.HasActiveTransaction(Session), "fallback uses original direct controller once");
            Check(_configCalls == (fallback == "absent" ? 0 : 1), "fallback never doubles foreign callbacks");
            Console.WriteLine($"SVM_FALLBACK_PASSED ({fallback}; {_checks})");
            return;
        }

        Initialize();
        var ordinary = Request("ordinary.Pmc test");
        int c = _configCalls, n = _nativeEnds;
        Check(await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, ordinary, Session) == "null-response", "ordinary SaveLoot response");
        Check(_configCalls == c + 1 && _nativeEnds == n, "ordinary SaveLoot retains foreign behavior");
        Configure(false, true);
        ordinary = Request("ordinary.Pmc ignore");
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, ordinary, Session);
        Check(ordinary.Results!.Result == ExitStatus.TRANSIT && _nativeEnds == n, "ordinary Ignore retains result mutation");
        Configure(false, false);
        ordinary = Request("ordinary.Pmc native");
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, ordinary, Session);
        Check(_nativeEnds == n + 1, "ordinary native end still runs");
        ordinary = Request("ordinary.Scav native", "Savage");
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, ordinary, Session);
        Check(_nativeEnds == n + 2, "ordinary Scav native end still runs");

        foreach (bool saveLoot in new[] { true, false })
        {
            Initialize(); Configure(saveLoot, !saveLoot);
            string id = Arm();
            var request = Request(id);
            c = _configCalls; n = _nativeEnds;
            await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, request, Session);
            Check(_configCalls == c, "protected Arena bypasses foreign config and mutation");
            Check(request.Results!.Result == ExitStatus.KILLED, "protected Arena result remains unchanged");
            Check(!_snapshots.HasSnapshot(Session) && _sessions.GetStatus(Session).state == ArenaSessionState.Idle,
                "protected Arena restores and completes");
            Check(_saves.GetProfile(Session).ProfileInfo!.Username == "entry", "entry profile restored before persist");
            Check(_nativeEnds == n, "protected Arena skips native post-raid effects");
            int p = _persistCalls;
            await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, request, Session);
            Check(_persistCalls == p && _configCalls == c, "completed Arena retry is idempotent");
            await Reject(callbacks, Request(id.Replace("test", "forged", StringComparison.Ordinal)), "forged server identity rejected");
            Check(_configCalls == c, "stale Arena request reaches authoritative rejection");
            await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request("next.Pmc ordinary"), Other);
            Check(_configCalls == c + 1, "other session retains SVM during completed Arena");
        }
        Initialize(); Configure(true, false);
        string retryId = Arm(); c = _configCalls;
        _failPersist = true;
        await Reject(callbacks, Request(retryId), "protected persistence failure reaches caller");
        Check(_sessions.GetStatus(Session).state == ArenaSessionState.FinalizingEnd, "failed persist remains retryable");
        _failPersist = false;
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(retryId), Session);
        Check(_configCalls == c && _sessions.GetStatus(Session).state == ArenaSessionState.Idle, "finalizing retry reaches protected persistence");

        Initialize(); var armedId = Arm(); c = _configCalls;
        await Reject(callbacks, Request("unrelated.Pmc end"), "active protected arm rejects untagged wrong end");
        Check(_configCalls == c && _snapshots.HasSnapshot(Session), "wrong request cannot be swallowed by SVM");
        await callbacks.EndLocalRaidAsync("/foreign/path", Request(armedId), Session);
        Check(_configCalls == c + 1, "non-end route does not borrow Arena scope");
        // Direct foreign invocation and a different request cannot borrow a
        // completed/native callback lease, even with the same profile/server id.
        object?[] direct = [default(ValueTask<string>), Session, Request(armedId), CancellationToken.None];
        Check(!(bool)prefix.Invoke(null, direct)! && _configCalls == c + 2, "direct foreign call remains ordinary");
        Check(!ArenaSvmCompat.OwnsExactCallback(Session, Request(armedId)), "callback finalizer releases lease");
        MethodInfo enterLease = typeof(ArenaSvmCompat).GetMethod("EnterCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo leaveLease = typeof(ArenaSvmCompat).GetMethod("LeaveCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        var exactRequest = Request(armedId);
        object?[] leaseArgs = [callbacks, ArenaSvmCompat.EndRoute, exactRequest, Session, null];
        enterLease.Invoke(null, leaseArgs);
        Check(ArenaSvmCompat.OwnsExactCallback(Session, exactRequest), "exact physical callback lease");
        Check(!ArenaSvmCompat.OwnsExactCallback(Session, Request(armedId)), "same-ID physical request cannot borrow lease");
        Check(!ArenaSvmCompat.OwnsExactCallback(Other, exactRequest), "other session cannot borrow callback lease");
        ArenaSvmCompat.MarkOrdinaryWrapperOwned(new MatchCallbacks(null!, response, controller), ArenaSvmCompat.EndRoute, Session, exactRequest);
        Check(!(bool)leaseArgs[4]!.GetType().GetField("OrdinaryWrapperOwned", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(leaseArgs[4])!, "other instance cannot mark wrapper ownership");
        object?[] nestedLease = [callbacks, "/foreign/path", exactRequest, Session, null];
        enterLease.Invoke(null, nestedLease);
        Check(!ArenaSvmCompat.OwnsExactCallback(Session, exactRequest), "nested wrong route does not inherit lease");
        leaveLease.Invoke(null, [nestedLease[4]]);
        Check(ArenaSvmCompat.OwnsExactCallback(Session, exactRequest), "nested callback restores outer lease");
        leaveLease.Invoke(null, [leaseArgs[4]]);
        Check(!ArenaSvmCompat.OwnsExactCallback(Session, exactRequest), "outer callback releases physical lease");

        metadata.GetType().GetProperty(nameof(IModMetadata.Version))!.SetValue(metadata, new SemanticVersioning.Version("2.2.2"));
        RejectContract(mod, "older SVM rejected");
        metadata.GetType().GetProperty(nameof(IModMetadata.Version))!.SetValue(metadata, new SemanticVersioning.Version("2.2.4"));
        RejectContract(mod, "metadata assembly mismatch rejected");
        metadata.GetType().GetProperty(nameof(IModMetadata.Version))!.SetValue(metadata, new SemanticVersioning.Version("2.2.3"));

        // Full ordinary wrapper chain: no hand-built state entries. Native
        // start response/persistence IO are controlled, real service identity,
        // callback detours and the published SVM prefix execute unchanged.
        nativeHarness.Patch(typeof(MatchController).GetMethod(nameof(MatchController.StartLocalRaidAsync))!, prefix: new HarmonyMethod(Hook(nameof(NativeStart))));
        foreach (bool saveLoot in new[] { true, false })
        {
            Initialize(); Configure(saveLoot, !saveLoot);
            var ordinaryService = new OrdinaryRaidLifecycleService(controller, response, _sessions, _snapshots, Logger.Create<OrdinaryRaidLifecycleService>());
            ArenaContext.OrdinaryRaids = ordinaryService;
            await StartOrdinary(ordinaryService);
            var request = Request(_startIdentity); c = _configCalls; n = _nativeEnds;
            _response = "ordinary-custom-response";
            ManualReentry(ordinaryService, callbacks, request);
            Check(await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, request, Session) == _response, "ordinary wrapper preserves actual response");
            Check(_configCalls == c + 1 && _nativeEnds == n, "ordinary wrapper runs SVM exactly once");
            Check(!ordinaryService.HasActiveTransaction(Session), "SVM ignored end retires ordinary generation");
            _response = "changed-default";
            Check(await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, request, Session) == "ordinary-custom-response", "completed ordinary retry preserves response");
            Check(_configCalls == c + 1, "completed ordinary retry does not repeat SVM");
            await StartOrdinary(ordinaryService); // same seconds-resolution server ID
            Check(ordinaryService.HasActiveTransaction(Session), "new ordinary start after SVM ignore succeeds");
            await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session);
            Check(_configCalls == c + 2 && !ordinaryService.HasActiveTransaction(Session), "new generation outranks identical end tombstone");
            _response = "null-response";
        }

        Initialize(); Configure(false, false);
        var retryOrdinary = new OrdinaryRaidLifecycleService(controller, response, _sessions, _snapshots, Logger.Create<OrdinaryRaidLifecycleService>());
        ArenaContext.OrdinaryRaids = retryOrdinary;
        await StartOrdinary(retryOrdinary);
        _nativeTask = Task.FromException(new InvalidOperationException("native callback failure"));
        await Reject(callbacks, Request(_startIdentity), "ordinary callback fault reaches caller");
        Check(retryOrdinary.HasActiveTransaction(Session), "faulted ordinary generation remains retryable");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _nativeTask = finish.Task; c = _configCalls; n = _nativeEnds;
        var cancelled = new CancellationTokenSource();
        Task<string> first = callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session, cancelled.Token).AsTask();
        for (int i = 0; i < 100 && _nativeEnds == n; i++) await Task.Delay(10);
        Check(_nativeEnds == n + 1, "retry reaches native ordinary callback");
        nativeHarness.Patch(callback, prefix: new HarmonyMethod(Hook(nameof(FallbackObserver))));
        int observedBefore = _fallbackObserved;
        Task<string> overlapping = callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session).AsTask();
        cancelled.Cancel();
        try { await first; Check(false, "caller cancellation reaches caller"); } catch (OperationCanceledException) { Check(true, "caller cancellation reaches caller"); }
        Check(retryOrdinary.HasActiveTransaction(Session) && !overlapping.IsCompleted, "caller cancellation does not cancel shared end");
        finish.SetResult();
        await overlapping;
        Check(_configCalls == c + 1 && !retryOrdinary.HasActiveTransaction(Session), "overlapping retry shares one SVM/native end");
        Check(_fallbackObserved == observedBefore + 1, "late hook runs only once for active-bridge overlap");
        _releaseChild.SetResult();
        Check(_inheritedAuthority != null && !await _inheritedAuthority, "completed ordinary scope revokes inherited async authority");
        _nativeTask = null;
        Check(ArenaSvmCompat.TryGetAdmissionFailure(out _), "late unknown hook blocks Arena admission");
        c = _configCalls;
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session);
        Check(_configCalls == c, "captured completed retry suppresses SVM after original order restore");
        await StartOrdinary(retryOrdinary);
        observedBefore = _fallbackObserved; n = _nativeEnds;
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session);
        Check(!ArenaSvmCompat.Enabled && ArenaSvmCompat.TryGetAdmissionFailure(out _), "late unknown topology revokes future bridge and Arena admission");
        Check(_fallbackObserved == observedBefore + 1 && _nativeEnds == n + 1, "late unknown callback is not replayed on future ordinary end");
        Check(!retryOrdinary.HasActiveTransaction(Session), "late fallback settles ordinary generation");
        nativeHarness.Unpatch(callback, Hook(nameof(FallbackObserver)));
        ResetAdapter(mod);

        Initialize(); Configure(false, false);
        var noAdmissionFirst = new OrdinaryRaidLifecycleService(controller, response, _sessions, _snapshots, Logger.Create<OrdinaryRaidLifecycleService>());
        ArenaContext.OrdinaryRaids = noAdmissionFirst;
        await StartOrdinary(noAdmissionFirst);
        nativeHarness.Patch(callback, prefix: new HarmonyMethod(Hook(nameof(FallbackObserver))));
        observedBefore = _fallbackObserved;
        await callbacks.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, Request(_startIdentity), Session);
        Check(!ArenaSvmCompat.Enabled && _fallbackObserved == observedBefore + 1,
            "ordinary end detects late topology without Arena admission poll");
        nativeHarness.Unpatch(callback, Hook(nameof(FallbackObserver)));
        ResetAdapter(mod);

        // Tampering with receipt graph must fail admission, not disable the
        // already-installed boundary or change ordinary saved settings.
        foreignHarmony.Unpatch(callback, prefix);
        Check(ArenaSvmCompat.TryGetAdmissionFailure(out _), "missing foreign receipt rejects admission");
        foreignHarmony.Patch(callback, prefix: new HarmonyMethod(prefix));
        ResetAdapter(mod);
        Check(!ArenaSvmCompat.TryGetAdmissionFailure(out _), "restored foreign receipt recovers admission");
        foreignHarmony.Unpatch(callback, prefix);
        foreignHarmony.Patch(callback, prefix: new HarmonyMethod(prefix) { priority = Priority.First + 100,
            before = [ArenaSvmCompat.HarmonyId, nameof(ArenaEndLocalRaidPatch)] });
        Check(ArenaSvmCompat.TryGetAdmissionFailure(out _), "contradictory foreign ordering rejects admission");
        foreignHarmony.Unpatch(callback, prefix);
        foreignHarmony.Patch(callback, prefix: new HarmonyMethod(prefix));
        ResetAdapter(mod);
        var helperAsm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SvmFutureHelper"), AssemblyBuilderAccess.Run);
        var helperType = helperAsm.DefineDynamicModule("main").DefineType("FutureEndHook", TypeAttributes.Public);
        var helperMethod = helperType.DefineMethod("Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        helperMethod.GetILGenerator().Emit(OpCodes.Ret);
        MethodInfo helper = helperType.CreateType()!.GetMethod("Prefix")!;
        typeof(ArenaSvmCompat).GetField("_mod", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null,
            new SptMod { Directory = mod.Directory, ModMetadata = metadata, Assemblies = [svm, helper.DeclaringType!.Assembly] });
        foreignHarmony.Patch(lifecycle, prefix: new HarmonyMethod(helper));
        Check(ArenaSvmCompat.TryGetAdmissionFailure(out _), "unreviewed downstream foreign hook rejects admission");
        foreignHarmony.Unpatch(lifecycle, helper);
        typeof(ArenaSvmCompat).GetField("_mod", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, mod);
        ResetAdapter(mod);
        new Harmony(ArenaSvmCompat.HarmonyId).Unpatch(callback, typeof(ArenaSvmCompat).GetMethod("EnterCallback", BindingFlags.NonPublic | BindingFlags.Static)!);
        Check(ArenaSvmCompat.TryGetAdmissionFailure(out _), "missing own receipt rejects admission");
        Console.WriteLine($"SVM_COMPAT_PASSED ({_checks})");
    }

    private static void Initialize()
    {
        _sessions = new ArenaSessionService(Logger.Create<ArenaSessionService>());
        _saves = new SaveServer(null!, [], null!, null!, null!, null!, Logger.Create<SaveServer>(), null!);
        _saves.AddProfile(new SptProfile { ProfileInfo = new() { ProfileId = Session, Username = "entry" } });
        _snapshots = new ProfileSnapshotService(new SPTarkov.Server.Core.Utils.Cloners.FastCloner(), _saves, Logger.Create<ProfileSnapshotService>());
        ArenaContext.Sessions = _sessions; ArenaContext.Snapshots = _snapshots; ArenaContext.OrdinaryRaids = null;
    }
    private static string Arm()
    {
        string token = _sessions.Arm(Session, "factory4_day", "gungame");
        Check(_snapshots.TakeSnapshot(Session), "entry snapshot captured");
        _saves.GetProfile(Session).ProfileInfo!.Username = "arena-temporary";
        string start = Protocol.BuildNativeStartIdentity(token);
        Check(_sessions.ProcessLocalRaidStart(Session, "factory4_day", start) == ArenaLocalRaidStartDisposition.ArenaStarted, "native Arena start");
        Check(_sessions.BindNativeEndIdentity(Session, start, "factory.Pmc test", out var id), "authoritative native server identity bound");
        return id!;
    }
    private static EndLocalRaidRequestData Request(string id, string side = "Usec") => new()
    {
        ServerId = id,
        Results = new() { Result = ExitStatus.KILLED, Profile = new PmcData { Info = new Info { Side = side } } },
    };
    private static async Task Reject(MatchCallbacks callback, EndLocalRaidRequestData r, string reason)
    {
        try { await callback.EndLocalRaidAsync(ArenaSvmCompat.EndRoute, r, Session); }
        catch (InvalidOperationException) { Check(true, reason); return; }
        Check(false, reason);
    }
    private static void Configure(bool saveLoot, bool ignore)
    {
        _config = Activator.CreateInstance(_configType)!;
        var raids = Get(_config, "Raids"); var startup = Get(raids, "RaidStartup");
        Set(raids, "EnableRaids", true); Set(startup, "EnableRaidStartup", true); Set(startup, "SaveLoot", saveLoot);
        Set(raids, "OnKilledState", ignore ? 5 : 1);
        Set(Get(_config, "Scav"), "EnableScav", false);
    }
    private static object Get(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o)
        ?? o.GetType().GetField(name)!.GetValue(o)!;
    private static void Set(object o, string name, object value)
    {
        var p = o.GetType().GetProperty(name);
        if (p != null) p.SetValue(o, value); else o.GetType().GetField(name)!.SetValue(o, value);
    }
    private static bool NullResponse(ref string __result) { __result = _response; return false; }
    private static int _fallbackObserved;
    private static void FallbackObserver() => _fallbackObserved++;
    private static bool SkipConfigConstruction() => false;
    private static void ResetAdapter(SptMod mod)
    {
        new Harmony(ArenaSvmCompat.HarmonyId).UnpatchSelf();
        typeof(ArenaSvmCompat).GetField("_contract", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(ArenaSvmCompat).GetField("_bridgeEnabled", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        ArenaSvmCompat.TryEnable([mod]);
        Check(!ArenaSvmCompat.TryGetAdmissionFailure(out _), "fresh fixture adapter ready");
    }
    private static void RejectContract(SptMod mod, string reason)
    {
        try { ArenaSvmContract.Resolve(mod); }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException) { Check(true, reason); return; }
        Check(false, reason);
    }
    private static bool ConfigResult(ref object __result) { _configCalls++; __result = _config; return false; }
    private static bool NativeEnd(bool __runOriginal, ref Task __result)
    {
        if (!__runOriginal) return false;
        _nativeEnds++;
        if (_nativeTask != null && !_nativeTask.IsCompleted && ArenaContext.OrdinaryRaids is { } service)
            _inheritedAuthority = Task.Run(async () => { await _releaseChild.Task; return service.IsExecutingExactEnd(Session, _startIdentity); });
        __result = _nativeTask ?? Task.CompletedTask;
        return false;
    }
    private static bool NativeStart(ref Task<StartLocalRaidResponseData> __result)
    { __result = Task.FromResult(new StartLocalRaidResponseData { ServerId = _startIdentity }); return false; }
    private static async Task StartOrdinary(OrdinaryRaidLifecycleService service)
    {
        var r = new StartLocalRaidRequestData { Location = "factory4_day", ServerId = Protocol.BuildNativeOrdinaryStartIdentity(
            "abababababababababababababababab", Guid.NewGuid().ToString("N")) };
        await service.GetOrStart(Session, r);
        Check(service.HasActiveTransaction(Session), "ordinary native generation started");
    }
    private static void CheckReentry(MatchCallbacks __instance, string url, EndLocalRaidRequestData info, MongoId sessionID)
    {
        var service = ArenaContext.OrdinaryRaids;
        if (!_checkReentry || service?.IsExecutingExactEnd(sessionID, info.ServerId) != true) return;
        _checkReentry = false;
        Check(!service.TryEnterExactEndCallback(__instance, url, sessionID, Request(info.ServerId!)), "same-ID copied request cannot reenter");
        Check(!service.TryEnterExactEndCallback((MatchCallbacks)RuntimeHelpers.GetUninitializedObject(typeof(MatchCallbacks)), url, sessionID, info), "other callback instance cannot reenter");
        Check(!service.TryEnterExactEndCallback(__instance, "/foreign/end", sessionID, info), "other callback route cannot reenter");
        Check(!service.TryEnterExactEndCallback(__instance, url, Other, info), "other callback session cannot reenter");
        string id = info.ServerId!; info.ServerId = "forged.Pmc id";
        Check(!service.TryEnterExactEndCallback(__instance, url, sessionID, info), "mutated server identity cannot reenter");
        info.ServerId = id;
    }
    private static void ManualReentry(OrdinaryRaidLifecycleService service, MatchCallbacks callback, EndLocalRaidRequestData request)
    {
        Type scopeType = typeof(OrdinaryRaidLifecycleService).GetNestedType("EndExecution", BindingFlags.NonPublic)!;
        object scope = scopeType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single()
            .Invoke([Session.ToString(), request.ServerId, callback, ArenaSvmCompat.EndRoute, request, true]);
        object current = typeof(OrdinaryRaidLifecycleService).GetField("CurrentEndExecution", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        PropertyInfo value = current.GetType().GetProperty("Value")!;
        object? old = value.GetValue(current);
        value.SetValue(current, scope);
        try
        {
            _checkReentry = true; CheckReentry(callback, ArenaSvmCompat.EndRoute, request, Session);
            Check(service.TryEnterExactEndCallback(callback, ArenaSvmCompat.EndRoute, Session, request), "exact callback consumes one-use permit");
            Check(!service.TryEnterExactEndCallback(callback, ArenaSvmCompat.EndRoute, Session, request), "exact callback cannot reuse consumed permit");
            scopeType.GetField("Active")!.SetValue(scope, 0);
            Check(!service.IsExecutingExactEnd(Session, request.ServerId), "revoked ordinary execution loses authority");
        }
        finally { value.SetValue(current, old); }
    }
    private static bool Persist(MongoId sessionId, string nativeEndIdentity, ref Task __result)
    {
        _persistCalls++;
        Check(!_snapshots.HasSnapshot(sessionId) && _saves.GetProfile(sessionId).ProfileInfo!.Username == "entry", "persist sees restored entry profile");
        if (_failPersist) __result = Task.FromException(new InvalidOperationException("fixture persistence failure"));
        else { Check(_sessions.CompleteNativeEndFinalization(sessionId, nativeEndIdentity), "exact protected finalization"); __result = Task.CompletedTask; }
        return false;
    }
    public class Logger : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType == typeof(void) ? null
            : method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
        public static ISptLogger<T> Create<T>() => Create<ISptLogger<T>, Logger>();
    }
}
