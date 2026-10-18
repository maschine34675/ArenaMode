using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Shared;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mono.Cecil;

internal static class Program
{
    private static int _checks;

    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static async Task Main(string[] args)
    {
        var runtime = args.Length == 1 ? Path.GetFullPath(args[0]) : FindRuntime();
        int warningsBefore = ArenaContext.Logger!.Warnings.Count;
        ArenaSettingsRequestLogPatch.TryEnable(); // No host assembly loaded yet.
        Check(ArenaContext.Logger.Warnings.Count == warningsBefore + 1, "missing host seam warns without aborting startup");
        ArenaSettingsRequestLogPatch.TryEnable();
        Check(ArenaContext.Logger.Warnings.Count == warningsBefore + 1, "compatibility failure warning is bounded");
        VerifyInstalledSeam(runtime);
        await VerifyOutcomes();
        await VerifyScopeAndFallback();
        await VerifyActualDetour();
        Console.WriteLine($"SETTINGS_POLL_LOG_CHECKS_PASSED assertions={_checks}");
    }

    private static async Task VerifyOutcomes()
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("192.168.1.7"), IPAddress.Parse("203.0.113.7") })
        foreach (int status in new[] { 100, 199, 200, 201, 204, 206, 299, 300, 304, 400, 401, 403, 404, 429, 500, 503 })
        {
            var (context, response) = Context();
            var warnings = new List<string>();
            var compatibility = new List<string>();
            var originalBody = context.Response.Body;
            context.Request.Headers["Cookie"] = "test-only-do-not-log";
            bool deferred = ArenaSettingsRequestLogPatch.TryDeferRoutineLine(context, address, false, warnings.Add, compatibility.Add);
            Check(deferred, $"exact poll admitted for {address}/{status}");
            Check(warnings.Count == 0 && compatibility.Count == 0, "no speculative success/failure output before request");
            int executions = 0;
            RequestDelegate downstream = ctx => { executions++; ctx.Response.StatusCode = status; return Task.CompletedTask; };
            await downstream(context);
            await response.Complete();
            await response.Complete(); // A second callback invocation must not duplicate a failure.
            Check(executions == 1 && context.Response.StatusCode == status && ReferenceEquals(originalBody, context.Response.Body), "response/execution unchanged");
            Check(context.Request.Headers.Cookie == "test-only-do-not-log" && context.Request.Path == Protocol.ClientSettingsRoute, "authentication input/path unchanged");
            bool failed = status < 200 || status >= 300;
            Check(warnings.Count == (failed ? 1 : 0), $"final HTTP {status} observability");
            if (failed)
                Check(warnings[0].Contains($"HTTP {status}") && warnings[0].Contains(address.ToString())
                    && !warnings[0].Contains("test-only-do-not-log"), "failure identifies status/client without cookie");
        }

        var (aborted, abortedResponse) = Context();
        using var cancellation = new CancellationTokenSource();
        aborted.RequestAborted = cancellation.Token;
        var abortedWarnings = new List<string>();
        Check(ArenaSettingsRequestLogPatch.TryDeferRoutineLine(aborted, IPAddress.Loopback, false, abortedWarnings.Add, _ => { }), "abort case registered");
        cancellation.Cancel();
        await abortedResponse.Complete();
        Check(abortedWarnings.Count == 1 && abortedWarnings[0].Contains("aborted=True"), "aborted 200 response remains observable");

        var (throwing, throwingResponse) = Context();
        Check(ArenaSettingsRequestLogPatch.TryDeferRoutineLine(throwing, IPAddress.Loopback, false, _ => throw new Exception("sink failure"), _ => { }), "throwing sink registered");
        throwing.Response.StatusCode = 503;
        await throwingResponse.Complete();
        Check(throwing.Response.StatusCode == 503, "diagnostic sink cannot break or alter completion");

        var (faulted, faultedResponse) = Context();
        Check(ArenaSettingsRequestLogPatch.TryDeferRoutineLine(faulted, IPAddress.Loopback, false, _ => { }, _ => { }), "native exception case registered");
        var original = new InvalidOperationException("native handler failure");
        RequestDelegate fail = _ => throw original;
        Exception? caught = null;
        try { await fail(faulted); } catch (Exception ex) { caught = ex; }
        Check(ReferenceEquals(caught, original), "downstream exception is still available to native middleware");
        await faultedResponse.Complete();
    }

    private static async Task VerifyScopeAndFallback()
    {
        var rejected = new (string Method, string Path, string Query, string Base, bool WsArgument, bool WsFeature)[]
        {
            ("POST", Protocol.ClientSettingsRoute, "", "", false, false),
            ("HEAD", Protocol.ClientSettingsRoute, "", "", false, false),
            ("DELETE", Protocol.ClientSettingsRoute, "", "", false, false),
            ("get", Protocol.ClientSettingsRoute, "", "", false, false),
            ("GET", Protocol.ClientSettingsPushRoute, "", "", false, false),
            ("GET", Protocol.ClientSettingsRoute + "/", "", "", false, false),
            ("GET", Protocol.ClientSettingsRoute + "-other", "", "", false, false),
            ("GET", "/ArenaMode/client-settings", "", "", false, false),
            ("GET", "/arenamode%2fclient-settings", "", "", false, false),
            ("GET", "/client/game/start", "?path=" + Protocol.ClientSettingsRoute, "", false, false),
            ("GET", Protocol.ClientSettingsRoute, "?x=1", "", false, false),
            ("GET", Protocol.ClientSettingsRoute, "", "/proxy", false, false),
            ("GET", Protocol.ClientSettingsRoute, "", "", true, false),
            ("GET", Protocol.ClientSettingsRoute, "", "", false, true),
        };
        foreach (var test in rejected)
        {
            var (context, response) = Context();
            context.Request.Method = test.Method;
            context.Request.Path = test.Path;
            context.Request.QueryString = new QueryString(test.Query);
            context.Request.PathBase = test.Base;
            context.Features.Set<IHttpWebSocketFeature>(new SocketFeature(test.WsFeature));
            int reports = 0;
            Check(!ArenaSettingsRequestLogPatch.TryDeferRoutineLine(context, IPAddress.Loopback, test.WsArgument, _ => reports++, _ => reports++), "non-poll retains native request line: " + test);
            Check(response.Callbacks.Count == 0 && reports == 0, "non-poll has no response observer or diagnostics");
        }

        var (started, startedResponse) = Context();
        startedResponse.Started = true;
        Check(!ArenaSettingsRequestLogPatch.TryDeferRoutineLine(started, IPAddress.Loopback, false, _ => { }, _ => { }), "already-started response keeps original logging");

        var (unobservable, unobservableResponse) = Context();
        unobservableResponse.RejectRegistration = true;
        var compatibility = new List<string>();
        Check(!ArenaSettingsRequestLogPatch.TryDeferRoutineLine(unobservable, IPAddress.Loopback, false, _ => throw new Exception(), compatibility.Add), "registration failure keeps original logging");
        Check(compatibility.Count == 1 && compatibility[0].Contains("native request logging retained"), "registration incompatibility visible");
        Check(unobservableResponse.Callbacks.Count == 0, "registration failure did not attach partial observer");
        Check(!ArenaSettingsRequestLogPatch.TryDeferRoutineLine(unobservable, IPAddress.Loopback, false, _ => { }, _ => throw new Exception()), "diagnostic failure cannot affect native fallback");
        await unobservableResponse.Complete();

        Check(ArenaSettingsRequestLogPatch.ResolveTarget(typeof(SignatureFixture)).ReturnType == typeof(void), "exact signature accepted");
        try { ArenaSettingsRequestLogPatch.ResolveTarget(typeof(WrongSignatureFixture)); throw new Exception("wrong return accepted"); }
        catch (MissingMethodException) { _checks++; }
    }

    private static void VerifyInstalledSeam(string runtime)
    {
        var assemblyPath = Path.Combine(runtime, "SPT.Server.dll");
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Installed server is required; no silent skip", assemblyPath);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(runtime, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var liveAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var liveType = liveAssembly.GetType(ArenaSettingsRequestLogPatch.MiddlewareTypeName, true)!;
        var seam = ArenaSettingsRequestLogPatch.ResolveTarget(liveType);
        Check(seam.DeclaringType == liveType && !seam.IsStatic, "production resolver accepts actual installed middleware signature");

        using var cecil = AssemblyDefinition.ReadAssembly(assemblyPath);
        var type = cecil.MainModule.GetType(ArenaSettingsRequestLogPatch.MiddlewareTypeName);
        var request = type.Methods.Single(m => m.Name == "LogRequest");
        var move = type.NestedTypes.Single(t => t.Name.StartsWith("<InvokeAsync>"))
            .Methods.Single(m => m.Name == "MoveNext");
        var calls = move.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        int logOffset = calls.Single(i => ((MethodReference)i.Operand).FullName == request.FullName).Offset;
        var nextOffsets = calls.Where(i => ((MethodReference)i.Operand).DeclaringType.FullName == "Microsoft.AspNetCore.Http.RequestDelegate"
            && ((MethodReference)i.Operand).Name == "Invoke").Select(i => i.Offset).ToArray();
        Check(nextOffsets.Length == 2 && nextOffsets[0] < logOffset && nextOffsets[1] > logOffset,
            "actual middleware has its untouched disabled/health bypass and normal request-before-next path");
        var literals = move.Body.Instructions.Where(i => i.OpCode.Name == "ldstr").Select(i => i.Operand.ToString()).ToArray();
        Check(literals.Contains("unhandled_response") && literals.Contains("Error handling request: "), "actual native 404 and exception diagnostics present");
        Check(calls.Any(i => ((MethodReference)i.Operand).Name == "Error" && i.Offset > nextOffsets[1])
            && calls.Count(i => ((MethodReference)i.Operand).Name == "Critical" && i.Offset > nextOffsets[1]) == 3,
            "actual middleware retains post-next error and complete Critical exception reporting");
        Check(request.Body.Instructions.All(i => i.Operand is not MethodReference method
            || method.DeclaringType.FullName != "Microsoft.AspNetCore.Http.RequestDelegate"), "patched helper never executes a request");
        Console.WriteLine($"INSTALLED_SPT_REQUEST_LOG_SEAM_VERIFIED sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assemblyPath)))}");
    }

    private static async Task VerifyActualDetour()
    {
        var liveType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "SPT.Server")
            .GetType(ArenaSettingsRequestLogPatch.MiddlewareTypeName, true)!;
        var target = ArenaSettingsRequestLogPatch.ResolveTarget(liveType);
        var harmony = new Harmony(ArenaSettingsRequestLogPatch.HarmonyId);
        try
        {
            ArenaSettingsRequestLogPatch.TryEnable();
            Check(Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == ArenaSettingsRequestLogPatch.HarmonyId) == 1,
                "actual installed LogRequest accepts the optional prefix");
            // This host object intentionally has no services. If the exact quiet
            // request reaches the original logger, its missing dependencies throw.
            // It is never a running server or a substitute for native GPU proof.
            var middleware = RuntimeHelpers.GetUninitializedObject(liveType);
            var (context, response) = Context();
            target.Invoke(middleware, [context, IPAddress.Loopback, true, false]);
            Check(response.Callbacks.Count == 1, "actual detour registers exact poll completion and bypasses only original log helper");
            context.Response.StatusCode = 503;
            int before = ArenaContext.Logger!.Warnings.Count;
            await response.Complete();
            Check(ArenaContext.Logger.Warnings.Count == before + 1, "actual prefix routes failed response to warning");

            var (ordinary, _) = Context();
            ordinary.Request.Path = "/client/game/start";
            bool reachedOriginal = false;
            try { target.Invoke(middleware, [ordinary, IPAddress.Loopback, true, false]); }
            catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException) { reachedOriginal = true; }
            Check(reachedOriginal, "negative control reaches real original logger with intentionally absent native services");
        }
        finally { harmony.UnpatchSelf(); }
        Check(Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == ArenaSettingsRequestLogPatch.HarmonyId) != true,
            "offline detour removed after test");
    }

    private static (DefaultHttpContext, CompletionFeature) Context()
    {
        var context = new DefaultHttpContext();
        var response = new CompletionFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Method = "GET";
        context.Request.Path = Protocol.ClientSettingsRoute;
        return (context, response);
    }

    private static string FindRuntime()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("Arena repo root not found");
        return Path.GetFullPath(Path.Combine(dir.FullName, "..", "..", "SPT_Runtime"));
    }

    private sealed class CompletionFeature : HttpResponseFeature
    {
        internal readonly List<(Func<object, Task> Callback, object State)> Callbacks = [];
        internal bool RejectRegistration;
        internal bool Started;
        public override bool HasStarted => Started;
        public override void OnCompleted(Func<object, Task> callback, object state)
        {
            if (RejectRegistration) throw new InvalidOperationException("response completion unavailable");
            Callbacks.Add((callback, state));
        }
        internal async Task Complete()
        {
            foreach (var (callback, state) in Callbacks.AsEnumerable().Reverse()) await callback(state);
        }
    }

    private sealed class SocketFeature(bool isWebSocket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => isWebSocket;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }

    private sealed class SignatureFixture
    {
        public void LogRequest(HttpContext context, IPAddress ip, bool local, bool ws) { }
    }
    private sealed class WrongSignatureFixture
    {
        public Task LogRequest(HttpContext context, IPAddress ip, bool local, bool ws) => Task.CompletedTask;
    }
}
