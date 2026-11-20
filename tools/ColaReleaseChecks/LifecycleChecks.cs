using System.Reflection;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using EFT;
using Redeploy.Core.Api;

namespace EFT
{
    internal sealed class GameWorld { internal Player MainPlayer = new(); }
    internal sealed class Player { internal Health HealthController = new(); }
    internal sealed class Health { internal bool IsAlive = true; }
}
namespace Redeploy.Core.Api { internal static class RespawnApi { internal static bool IsReplacing; } }
namespace ArenaMode.Compat
{
    internal sealed class ColaClosure { public object c = new(); }
    internal static class ColaForeign { internal static JObject? _pending; internal static object? _pendingFor; internal static bool Busy; }
    internal sealed class ColaContract
    {
        internal FieldInfo RerollContainer = typeof(ColaClosure).GetField("c")!;
        internal FieldInfo RerollPending = typeof(ColaForeign).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
        internal FieldInfo RerollPendingFor = typeof(ColaForeign).GetField("_pendingFor", BindingFlags.Static | BindingFlags.NonPublic)!;
    }
    // Only EFT/HTTP seams are synthetic. All request/closure/callback/application
    // ownership methods are extracted verbatim from the production adapter.
    internal static partial class ArenaTarkovaColaCompat
    {
        private static TarkovaColaClientLease? _lease;
        private static readonly ColaContract _contract = new();
        private static GameWorld? _world;
        private static int _mainThread;
        private static bool Blocks => _lease?.BlocksWrites == true;
        private static GameWorld? CurrentWorld()
        {
            if (Environment.CurrentManagedThreadId != _mainThread) throw new Exception("Unity access from callback worker");
            return _world;
        }
        internal static void CheckRerolls(Action<bool, string> check)
        {
            _mainThread = Environment.CurrentManagedThreadId;
            _lease = null;
            var ordinary = new ColaClosure();
            check(RerollRequestPrefix(out var ordinaryRequest) && ordinaryRequest == null, "ordinary requests do not acquire a scope");
            RerollRequestFinalizer(ordinaryRequest!);
            check(Task.Run(() => Reply(ordinary)).GetAwaiter().GetResult(), "ordinary worker callback passes through");
            check(Apply(ColaForeign._pending!), "ordinary response passes through");

            _world = new GameWorld(); var first = _world.MainPlayer;
            _lease = new("profile", "factory4_day", "zombie", 7);
            _lease.BindWorld(_world, first, "profile", "factory4_day", 7);
            _lease.Confirm(_world, "profile", "zombie", 7);
            var container = new object(); var initial = Request(container);
            check(_rerollRequestOwner == null && !Monitor.IsEntered(RerollSync), "request scope restored after completion");
            check(Task.Run(() => Reply(initial)).GetAwaiter().GetResult(), "Arena worker callback uses only managed state");
            var firstResponse = ColaForeign._pending!;
            check(Apply(firstResponse), "current Arena response accepted");
            check(!Apply(firstResponse, new object()), "response cannot target a different container in the same life");
            // Repeating the same container is a distinct operation, including
            // after physical replacement. There is no container-keyed lease.
            var again = Request(container);
            check(Task.Run(() => Reply(again)).GetAwaiter().GetResult() && Apply(ColaForeign._pending!), "completed container can be rerolled again");

            var failedSwap = Request(container);
            RespawnApi.IsReplacing = true;
            check(Task.Run(() => Reply(failedSwap)).GetAwaiter().GetResult() && !ColaForeign.Busy, "failed replacement callback releases native busy latch");
            var retained = ColaForeign._pending!;
            check(!Apply(retained), "application waits while replacement owns body");
            RespawnApi.IsReplacing = false;
            check(Apply(retained), "retained original body can apply after failed replacement");

            var oldPending = Request(container);
            var next = new Player();
            _lease.BeforeReplacementStatistics(_world, first, next, 0);
            _lease.Rebind(_world, first, next); _world.MainPlayer = next;
            ResetForeign();
            var current = Request(container);
            check(!Task.Run(() => Reply(oldPending)).GetAwaiter().GetResult() && ColaForeign.Busy, "old callback cannot clear a new request busy latch");
            check(!Apply(firstResponse), "old response cannot be adopted by replacement body");
            check(Task.Run(() => Reply(current)).GetAwaiter().GetResult() && Apply(ColaForeign._pending!), "same container works after physical replacement");

            // A response published during a simultaneous generation change must
            // still be tagged as the old response, never become an ordinary one.
            var crossing = Request(container);
            check(RerollCallbackPrefix(crossing, out var crossState), "crossing callback initially authorized");
            try
            {
                ColaForeign._pending = new JObject { ["owned"] = true }; ColaForeign._pendingFor = container;
                _lease.Release(); _lease = null;
                RerollCallbackPostfix(true, crossState);
            }
            finally { RerollCallbackFinalizer(crossState); }
            check(!Apply(ColaForeign._pending!), "crossing late response stays rejected after lease release");
            check(!Task.Run(() => Reply(crossing)).GetAwaiter().GetResult(), "late closure remains rejected outside Arena");
            var after = new ColaClosure { c = container };
            check(Task.Run(() => Reply(after)).GetAwaiter().GetResult() && Apply(ColaForeign._pending!), "new ordinary request on same container remains unchanged");
            check(!Monitor.IsEntered(RerollSync) && _rerollRequestOwner == null, "all ownership locks and thread state released");
            _world = null; RespawnApi.IsReplacing = false;
        }
        private static ColaClosure Request(object container)
        {
            bool allowed = RerollRequestPrefix(out var state);
            try
            {
                if (!allowed) throw new Exception("Arena request refused");
                var closure = new ColaClosure { c = container };
                RerollClosurePostfix(closure); ColaForeign.Busy = true;
                return closure;
            }
            finally { RerollRequestFinalizer(state); }
        }
        private static bool Reply(ColaClosure closure)
        {
            bool allowed = RerollCallbackPrefix(closure, out var state);
            try
            {
                if (allowed)
                {
                    try { ColaForeign._pending = new JObject { ["ok"] = true }; ColaForeign._pendingFor = closure.c; }
                    finally { ColaForeign.Busy = false; }
                }
                RerollCallbackPostfix(allowed, state);
                return allowed;
            }
            finally { RerollCallbackFinalizer(state); }
        }
        private static bool Apply(JObject result, object? container = null)
        {
            bool allowed = RerollApplyPrefix(result, container ?? ColaForeign._pendingFor!, out var state);
            try { return allowed; } finally { RerollLockFinalizer(state); }
        }
        private static void ResetForeign()
        {
            RerollResetPrefix(out bool state);
            try { ColaForeign.Busy = false; ColaForeign._pending = null; ColaForeign._pendingFor = null; }
            finally { RerollLockFinalizer(state); }
        }
    }
}
