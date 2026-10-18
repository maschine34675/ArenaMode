using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;
using HarmonyLib;
using Microsoft.AspNetCore.Http;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Defer only Arena's exact periodic GET request line until its outcome is known.
/// The middleware itself, request execution and native 404/exception reporting
/// remain untouched. Unknown server contracts retain normal request logging.
/// </summary>
internal static class ArenaSettingsRequestLogPatch
{
    internal const string HarmonyId = "com.maschine.ArenaMode.settings-poll-log";
    internal const string MiddlewareTypeName = "SPTarkov.Server.Middleware.SptLoggerMiddleware";
    private static bool _enabled;
    private static int _reportedCompatibilityFailure;

    internal static void TryEnable()
    {
        if (_enabled) return;
        var harmony = new Harmony(HarmonyId);
        try
        {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "SPT.Server")
                .Select(a => a.GetType(MiddlewareTypeName))
                .Where(t => t != null).ToArray();
            if (types.Length != 1)
                throw new MissingMemberException("expected one loaded SPT request logger");
            var target = ResolveTarget(types[0]!);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(ArenaSettingsRequestLogPatch), nameof(Prefix)));
            _enabled = true;
            ArenaContext.Logger?.Debug("[ArenaMode] Successful settings polls use quiet request logging; failed requests remain visible.");
        }
        catch (Exception ex)
        {
            // This is optional presentation, never a reason to abort Arena startup.
            _enabled = false;
            try { harmony.UnpatchSelf(); }
            catch { /* Prefix stays fail-open if rollback itself is unavailable. */ }
            WarnCompatibilityOnce($"adapter unavailable ({ex.Message}); native request logging retained");
        }
    }

    internal static MethodInfo ResolveTarget(Type middleware)
    {
        var method = middleware.GetMethod("LogRequest", BindingFlags.Public | BindingFlags.Instance,
            binder: null, types: [typeof(HttpContext), typeof(IPAddress), typeof(bool), typeof(bool)], modifiers: null);
        if (method == null || method.DeclaringType != middleware || method.ReturnType != typeof(void) || method.ContainsGenericParameters
            || method.GetCustomAttribute<AsyncStateMachineAttribute>() != null
            || method.GetCustomAttribute<IteratorStateMachineAttribute>() != null)
            throw new MissingMethodException(middleware.FullName, "LogRequest(HttpContext, IPAddress, bool, bool)");
        return method;
    }

    private static bool Prefix(HttpContext __0, IPAddress __1, bool __3)
    {
        var logger = ArenaContext.Logger;
        if (!_enabled || logger == null) return true;
        return !TryDeferRoutineLine(__0, __1, __3,
            message => logger.Warning(message), WarnCompatibilityOnce);
    }

    // Kept separate from the detour so real ASP.NET contexts can exercise both
    // admission and completion without starting a server or mocking a response.
    internal static bool TryDeferRoutineLine(HttpContext context, IPAddress clientIp, bool isWebSocket,
        Action<string> reportFailure, Action<string> reportCompatibilityFailure)
    {
        try
        {
            if (isWebSocket || context.WebSockets.IsWebSocketRequest
                || !string.Equals(context.Request.Method, "GET", StringComparison.Ordinal)
                || !string.Equals(context.Request.Path.Value, Protocol.ClientSettingsRoute, StringComparison.Ordinal)
                || context.Request.PathBase.HasValue || context.Request.QueryString.HasValue
                || context.Response.HasStarted)
                return false;

            var completion = new PollCompletion(context, clientIp, reportFailure);
            context.Response.OnCompleted(static state => ((PollCompletion)state).Report(), completion);
            return true;
        }
        catch (Exception ex)
        {
            // In particular, failed callback registration must not hide the
            // original request line or escape into the HTTP execution pipeline.
            try { reportCompatibilityFailure($"cannot observe poll completion ({ex.Message}); native request logging retained"); }
            catch { /* Diagnostics must never break the request. */ }
            return false;
        }
    }

    private sealed class PollCompletion(HttpContext context, IPAddress clientIp, Action<string> reportFailure)
    {
        private int _reported;

        internal Task Report()
        {
            if (Interlocked.Exchange(ref _reported, 1) != 0) return Task.CompletedTask;
            try
            {
                int status = context.Response.StatusCode;
                bool aborted = context.RequestAborted.IsCancellationRequested;
                if (status is < 200 or >= 300 || aborted)
                    reportFailure($"[ArenaMode] Settings GET failed: {Protocol.ClientSettingsRoute}, "
                        + $"HTTP {status}, client {clientIp}, aborted={aborted}.");
            }
            catch { /* Do not fail HTTP completion when a diagnostic sink fails. */ }
            return Task.CompletedTask;
        }
    }

    private static void WarnCompatibilityOnce(string message)
    {
        if (Interlocked.Exchange(ref _reportedCompatibilityFailure, 1) != 0) return;
        try { ArenaContext.Logger?.Warning($"[ArenaMode] Settings request logging: {message}."); }
        catch { /* Optional logging integration cannot abort mod or server startup. */ }
    }
}
