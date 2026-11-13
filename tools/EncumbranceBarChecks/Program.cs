using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using PlayerEncumbranceBar;
using PlayerEncumbranceBar.Utils;
using ArenaMode.Compat;

internal static class Program
{
    private static int _checks;
    private static void Require(bool ok, string message)
    { if (!ok) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    private static void Main()
    {
        Require(ArenaEncumbranceBarCompat.GetRejectionReason() == null, "absent plugin is inert");
        var plugin = new PlayerEncumbranceBar.Plugin();
        Chainloader.PluginInfos.Add("com.mpstark.PlayerEncumbranceBar", new PluginInfo { Instance = plugin, Metadata = typeof(PlayerEncumbranceBar.Plugin).GetCustomAttribute<BepInPlugin>() });
        Require(ArenaEncumbranceBarCompat.GetRejectionReason() == null, "released weight-layout contract accepted");
        var world = Singleton<GameWorld>.Instance = new();
        var game = new LocalGame { GameWorld = world, Profile = GameUtils.Backend };
        Singleton<AbstractGame>.Instance = game;
        var body = new LocalPlayer { GameWorld = world, Profile = game.Profile, IsYourPlayer = true };
        world.MainPlayer = game.LocalPlayer = body; game.PlayerOwner.Player = body;
        ArenaMode.Session.ArenaSession.Game = game; ArenaMode.Session.ArenaSession.World = world;
        body.Physical.BaseOverweightLimits = new(40, 80); body.Physical.WalkOverweightLimits = new(60, 100);
        var bar = plugin.PlayerEncumbranceBar;
        bar.Show(body.HealthController);
        Require(bar.DisplayedBase == 20 && bar.DisplayedWalk == 30, "normal raid preserves native reconstructed limits");
        ArenaMode.Session.ArenaSession.IsActive = true;
        ArenaMode.Session.ArenaSession.Game = game; ArenaMode.Session.ArenaSession.World = world;
        bar.Refresh();
        Require(bar.DisplayedBase == 40 && bar.DisplayedWalk == 60, "Arena uses current physical limits before drawing ticks");
        body.Physical.BaseOverweightLimits = new(50, 100); body.Physical.WalkOverweightLimits = new(75, 125);
        bar.Refresh();
        Require(bar.DisplayedBase == 50 && bar.DisplayedWalk == 75, "subsequent UI refresh reads updated settings without compounding");
        world.MainPlayer = new LocalPlayer { GameWorld = world, Profile = game.Profile, IsYourPlayer = true };
        bar.Refresh(); Require(bar.DisplayedBase == 20, "same profile without matching physical local player stays native");
        world.MainPlayer = body;
        bar.Show(new EFT.HealthSystem.Health()); Require(bar.DisplayedBase == 20, "unrelated panel health is untouched");
        bar.Show(body.HealthController); ArenaMode.Session.ArenaSession.World = new();
        bar.Refresh(); Require(bar.DisplayedBase == 20, "stale Arena world cannot alter display");
        ArenaEncumbranceBarCompat.Shutdown(); ArenaEncumbranceBarCompat.Shutdown();
        bar.Refresh(); Require(bar.DisplayedBase == 20, "shutdown restores original method idempotently");
        Console.WriteLine("ARENA_ENCUMBRANCE_BAR_CHECKS_PASSED " + _checks);
    }
}
namespace ArenaMode { internal static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; } }
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive; internal static LocalGame Game; internal static GameWorld World;
        internal static bool IsExactActiveRaid(LocalGame game, GameWorld world) => ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
