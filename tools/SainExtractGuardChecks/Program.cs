using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.Components.BotController;

int checks = 0;
void Check(bool value, string label)
{ checks++; if (!value) throw new InvalidOperationException("FAIL: " + label); }

var target = typeof(BotExtractManager).GetMethod(nameof(BotExtractManager.IsBotAllowedToExfil));

// SAIN's method first, then the registered postfix with a by-ref result, as Harmony runs them.
bool Ask(BotComponent bot)
{
    bool result = BotExtractManager.IsBotAllowedToExfil(bot);
    if (Harmony.Hooks.TryGetValue(target, out var hook) && hook.Postfix != null)
    {
        var arguments = new object[] { result };
        hook.Postfix.method.Invoke(null, arguments);
        result = (bool)arguments[0];
    }
    return result;
}

void Raid(bool active = true, bool armed = false, string armedMap = "arena_bay5", string world = "arena_bay5")
{
    ArenaSession.IsActive = active;
    ArenaSession.ArmedForNextRaid = armed;
    ArenaSession.ArmedMap = armedMap;
    ArenaSession.Game = Singleton<AbstractGame>.Instance = new AbstractGame();
    ArenaSession.World = Singleton<GameWorld>.Instance = new GameWorld { LocationId = world };
}

// A dynamic assembly with SAIN's identity, to model contract drift without a real SAIN.
Assembly Drifted(string version, bool hasMethod, Type returnType)
{
    var builder = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("SAIN") { Version = new Version(version) }, AssemblyBuilderAccess.Run);
    var module = builder.DefineDynamicModule("SAIN");
    var component = module.DefineType("SAIN.Components.BotComponent", TypeAttributes.Public).CreateType();
    var manager = module.DefineType("SAIN.Components.BotController.BotExtractManager", TypeAttributes.Public);
    if (hasMethod)
    {
        var method = manager.DefineMethod("IsBotAllowedToExfil", MethodAttributes.Public | MethodAttributes.Static,
            returnType, new[] { component });
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
    manager.CreateType();
    return builder;
}

var bot = new BotComponent();

Raid();
Check(Ask(bot), "unbound guard keeps SAIN's answer");
ArenaSainExtractGuard.Enable();
Check(Harmony.Hooks.Count == 1 && Harmony.Hooks.TryGetValue(target, out var installed)
    && installed.Prefix == null && installed.Postfix?.priority == Priority.Last,
    "guard installs one last-priority postfix");
ArenaSainExtractGuard.Enable();
Check(Harmony.Hooks.Count == 1, "Enable is idempotent");

// Arena scope: armed for this map, or the exact active raid.
Raid(); BotExtractManager.Calls = 0;
Check(!Ask(bot), "active Arena raid refuses SAIN extraction");
Check(BotExtractManager.Calls == 1, "SAIN's own check still runs once");
foreach (string map in new[] { "factory4_day", "bigmap", "arena_equator", "arena_airpit", "arena_bowl", "arena_iceberg", "arena_autoservice" })
{
    Raid(armedMap: map, world: map);
    Check(!Ask(bot), "active Arena raid refuses SAIN extraction on " + map);
}
Raid(armedMap: "ARENA_BAY5");
Check(!Ask(bot), "map match ignores case");
Raid(active: false, armed: true);
Check(!Ask(bot), "armed Arena loading refuses SAIN extraction");
Raid(); bot.PmcOrScav = false;
Check(!Ask(bot), "SAIN's own refusal stays a refusal in Arena");
Raid(active: false, armedMap: null); bot.PmcOrScav = false;
Check(!Ask(bot), "SAIN's own refusal stays a refusal in a normal raid");
bot.PmcOrScav = true;

// Outside the exact Arena raid SAIN's answer stands.
Raid(active: false, armedMap: null);
Check(Ask(bot), "normal raid keeps SAIN's answer");
Raid(active: false);
Check(Ask(bot), "a leftover map without an Arena latch keeps SAIN's answer");
Raid(); ArenaSession.Game = new AbstractGame();
Check(Ask(bot), "stale active session keeps SAIN's answer (game)");
Raid(); ArenaSession.World = new GameWorld { LocationId = "arena_bay5" };
Check(Ask(bot), "stale active session keeps SAIN's answer (world)");
Raid(active: false, armed: true, armedMap: "arena_equator");
Check(Ask(bot), "armed for another map keeps SAIN's answer");
Raid(armedMap: "arena_equator");
Check(Ask(bot), "active session on another map keeps SAIN's answer");
Raid(armedMap: null);
Check(Ask(bot), "active session without a map keeps SAIN's answer");
Raid(); Singleton<GameWorld>.Instance = null;
Check(Ask(bot), "missing world keeps SAIN's answer");
Raid(); Singleton<AbstractGame>.Instance = null;
Check(Ask(bot), "missing game keeps SAIN's answer");

// Shutdown, rebinding and failed installation.
ArenaSainExtractGuard.Shutdown(); Raid();
Check(Harmony.Hooks.Count == 0 && Ask(bot), "shutdown removes the guard");
Check(ArenaSainExtractGuard.TryBind(typeof(BotExtractManager).Assembly) && Harmony.Hooks.Count == 1 && !Ask(bot),
    "a later bind installs the guard again");
ArenaSainExtractGuard.Shutdown(); Harmony.Fail = true;
int warnings = ArenaMode.Plugin.ModLogger.Warnings.Count;
Check(!ArenaSainExtractGuard.TryBind(typeof(BotExtractManager).Assembly) && Harmony.Hooks.Count == 0 && Ask(bot)
    && ArenaMode.Plugin.ModLogger.Warnings.Count == warnings + 1, "failed installation leaves SAIN native with one warning");
Harmony.Fail = false;
Check(!ArenaSainExtractGuard.TryBind(Drifted("4.5.0.0", true, typeof(bool))) && Harmony.Hooks.Count == 0,
    "SAIN older than 4.5.1 is refused");
Check(!ArenaSainExtractGuard.TryBind(Drifted("4.5.1.0", true, typeof(int))) && Harmony.Hooks.Count == 0,
    "changed return type is refused");
Check(!ArenaSainExtractGuard.TryBind(Drifted("4.5.1.0", false, typeof(bool))) && Harmony.Hooks.Count == 0,
    "missing admission method is refused");
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == warnings + 1, "adapter warnings are bounded");
Check(ArenaSainExtractGuard.TryBind(Drifted("4.6.0.0", true, typeof(bool))) && Harmony.Hooks.Count == 1,
    "a newer SAIN with the same contract binds");
ArenaSainExtractGuard.Shutdown();

Console.WriteLine($"ARENA_SAIN_EXTRACT_GUARD_CHECKS_PASSED ({checks} assertions)");
