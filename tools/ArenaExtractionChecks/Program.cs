using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.Interactive.SecretExfiltrations;
using HarmonyLib;
using System.Reflection;

int checks = 0;
void Check(bool value, string label)
{ checks++; if (!value) throw new InvalidOperationException("FAIL: " + label); }

(LocalGame game, GameWorld world, Player local, ExfiltrationPoint regular, ScavExfiltrationPoint scav, SecretExfiltrationPoint secret) Setup(
    bool active = true, bool armed = false, string map = "arena_equator")
{
    ArenaExtractionIsolation.Shutdown(); Harmony.FailAt = -1;
    Redeploy.Core.GameAccess.LocalGameAccessor.Throw = false;
    var local = new Player();
    var world = new GameWorld { LocationId = map, MainPlayer = local };
    world.Players.Add(local.ProfileId, local);
    var game = new LocalGame { GameWorld = world, Status = active ? GameStatus.Started : GameStatus.Running };
    string token = Guid.NewGuid().ToString("N");
    game.Settings = new LocalRaidSettings { location = map, serverId = Protocol.BuildNativeStartIdentity(token) };
    Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    ArenaSession.IsActive = active; ArenaSession.ArmedForNextRaid = armed;
    ArenaSession.ExactGame = game; ArenaSession.ExactWorld = world;
    ArenaSession.ArmedMap = map; ArenaSession.ArmedMode = "teamdeathmatch";
    ArenaServerClient.PendingArmToken = token;
    var regular = new ExfiltrationPoint { Settings = new() { Name = "regular" } };
    var scav = new ScavExfiltrationPoint { Settings = new() { Name = "scav" } };
    var secret = new SecretExfiltrationPoint { Settings = new() { Name = "secret" } };
    world.ExfiltrationController.ExfiltrationPoints = new[] { regular };
    world.ExfiltrationController.ScavExfiltrationPoints = new[] { scav };
    world.ExfiltrationController.SecretExfiltrationPoints = new[] { secret };
    ArenaExtractionIsolation.Enable();
    return (game, world, local, regular, scav, secret);
}
void NativeProceed(ExfiltrationPoint point, Player player)
{
    var target = point.GetType().GetMethod(nameof(ExfiltrationPoint.Proceed), new[] { typeof(Player), typeof(bool) });
    var patch = Harmony.Registered.SingleOrDefault(p => p.target.Module == target.Module && p.target.MetadataToken == target.MetadataToken);
    if (patch.prefix == null || (bool)patch.prefix.Method.Invoke(null, new object[] { point, player }))
        point.Proceed(player);
}
void NativeExtraction(LocalGame game, string profile, ExitStatus status, string exit)
{
    var map = typeof(BaseLocalGame<EftGamePlayerOwner>).GetInterfaceMap(typeof(EndByExitTrigerScenario.IGame));
    var target = map.TargetMethods.Single();
    var patch = Harmony.Registered.SingleOrDefault(p => p.target.Module == target.Module && p.target.MetadataToken == target.MetadataToken);
    if (patch.prefix == null || (bool)patch.prefix.Method.Invoke(null, new object[] { game, profile, status, exit }))
        ((EndByExitTrigerScenario.IGame)game).StopSession(profile, status, exit);
}

var f = Setup();
Check(Harmony.Registered.Count == 3, "all extraction-specific hooks installed");
Check(Harmony.Registered.Any(p => p.target.DeclaringType == typeof(SecretExfiltrationPoint)), "secret Proceed override hooked independently");
Check(Harmony.Registered.All(p => p.target.Name != "Stop"), "general native Stop is never patched");
ArenaExtractionIsolation.Enable(); Check(Harmony.Registered.Count == 3, "Enable is idempotent");

foreach (string mapName in new[] { "factory4_day", "factory4_night", "labyrinth", "bigmap", "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl" })
{
    f = Setup(map: mapName);
    foreach (var point in new ExfiltrationPoint[] { f.regular, f.scav, f.secret })
    {
        NativeProceed(point, f.local);
        Check(point.NativeProceeds == 0, "active local Proceed blocked on " + mapName + " " + point.Settings.Name);
        NativeExtraction(f.game, f.local.ProfileId, ExitStatus.Survived, point.Settings.Name);
        NativeExtraction(f.game, f.local.ProfileId, ExitStatus.Runner, point.Settings.Name);
        Check(f.game.Stops == 0, "shared/individual extraction-only completion blocked on " + mapName + " " + point.Settings.Name);
    }
    f.game.Stop(f.local.ProfileId, ExitStatus.Left, null);
    Check(f.game.Stops == 1 && f.game.LastExit == ExitStatus.Left, "manual leave remains native on " + mapName);
    f.game.Stop(f.local.ProfileId, ExitStatus.Survived, "Arena match result");
    Check(f.game.Stops == 2, "match-owned result Stop remains native on " + mapName);
}

foreach (bool armed in new[] { false, true })
{
    f = Setup(active: false, armed: armed);
    if (armed) f.game.Settings.serverId = "ordinary-id";
    NativeProceed(f.regular, f.local); NativeExtraction(f.game, "local", ExitStatus.Runner, "regular");
    Check(f.regular.NativeProceeds == 1 && f.game.Stops == 1, "ordinary or unconfirmed arming passes through " + armed);
}
f = Setup(); f.game.Status = GameStatus.SoftStopping;
NativeProceed(f.regular, f.local); NativeExtraction(f.game, "local", ExitStatus.Runner, "regular");
Check(f.regular.NativeProceeds == 0 && f.game.Stops == 0, "timer grace period cannot reenable Arena extractions");
f.game.Stop("local", ExitStatus.Left, null);
Check(f.game.Stops == 1, "manual leave still works during timer grace period");
foreach (string fault in new[] { "foreign-body", "ai-body", "dead", "profile", "registry", "world", "game-world", "exact-game", "exact-world", "missing-world", "missing-game", "stopping", "stopped" })
{
    f = Setup(); Player caller = f.local;
    switch (fault)
    {
        case "foreign-body": caller = new Player { ProfileId = "local" }; break;
        case "ai-body": f.local.IsAI = true; break;
        case "dead": f.local.HealthController.IsAlive = false; break;
        case "profile": f.game.Profile.Id = "other"; break;
        case "registry": f.world.Players["local"] = new Player(); break;
        case "world": Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = f.local }; break;
        case "game-world": f.game.GameWorld = new GameWorld(); break;
        case "exact-game": ArenaSession.ExactGame = new LocalGame(); break;
        case "exact-world": ArenaSession.ExactWorld = new GameWorld(); break;
        case "missing-world": Singleton<GameWorld>.Instance = null; break;
        case "missing-game": Singleton<AbstractGame>.Instance = null; break;
        case "stopping": f.game.Status = GameStatus.Stopping; break;
        case "stopped": f.game.Status = GameStatus.Stopped; break;
    }
    NativeProceed(f.regular, caller);
    Check(f.regular.NativeProceeds == 1, "scope excludes " + fault);
    if (fault != "foreign-body")
    {
        NativeExtraction(f.game, "local", ExitStatus.Runner, "regular");
        if (fault is "ai-body" or "dead" or "registry")
            Check(f.game.Stops == 0, "completion does not require a living published body: " + fault);
        else
            Check(f.game.Stops == 1, "completion scope excludes " + fault);
    }
}
f = Setup(); f.world.MainPlayer = null; f.world.Players.Clear();
NativeExtraction(f.game, "local", ExitStatus.Runner, "regular");
Check(f.game.Stops == 0, "queued extraction blocked while respawn publishes no body");
f.game.Stop("local", ExitStatus.Killed, null);
Check(f.game.Stops == 1 && f.game.LastExit == ExitStatus.Killed, "native death remains available during replacement");
f = Setup(); var unknown = new ExfiltrationPoint { Settings = new() { Name = "regular" } };
NativeProceed(unknown, f.local); Check(unknown.NativeProceeds == 1, "same-name stale trigger remains native");
NativeExtraction(f.game, "someone-else", ExitStatus.Runner, "regular");
Check(f.game.Stops == 1, "foreign profile completion remains native");
NativeExtraction(f.game, "local", ExitStatus.Runner, "unknown");
Check(f.game.Stops == 2, "unregistered exit name remains native");
var oldGame = new LocalGame { GameWorld = f.world }; NativeExtraction(oldGame, "local", ExitStatus.Runner, "regular");
Check(oldGame.Stops == 1 && f.game.Stops == 2, "old game completion cannot claim current raid");
foreach (ExitStatus status in new[] { ExitStatus.Left, ExitStatus.Killed, ExitStatus.MissingInAction })
{
    f = Setup(); NativeExtraction(f.game, "local", status, "regular");
    Check(f.game.Stops == 1, "non-extraction exit status preserved " + status);
}

foreach (var status in new[] { GameStatus.Running, GameStatus.Starting, GameStatus.Started })
foreach (bool endIdentity in new[] { false, true })
{
    f = Setup(active: false, armed: true);
    f.game.Status = status;
    if (endIdentity) f.game.Settings.serverId = Protocol.BuildNativeEndIdentity("server", ArenaServerClient.PendingArmToken);
    NativeProceed(f.regular, f.local); NativeExtraction(f.game, "local", ExitStatus.Runner, "regular");
    Check(f.regular.NativeProceeds == 0 && f.game.Stops == 0, "authenticated loading blocks extraction " + status + " end=" + endIdentity);
}
foreach (string fault in new[] { "unarmed", "empty-mode", "map", "settings-map", "token", "stale-token", "settings-null", "identity", "stopping", "stopped" })
{
    f = Setup(active: false, armed: true);
    switch (fault)
    {
        case "unarmed": ArenaSession.ArmedForNextRaid = false; break;
        case "empty-mode": ArenaSession.ArmedMode = null; break;
        case "map": ArenaSession.ArmedMap = "another-map"; break;
        case "settings-map": f.game.Settings.location = "another-map"; break;
        case "token": ArenaServerClient.PendingArmToken = "not-a-token"; break;
        case "stale-token": ArenaServerClient.PendingArmToken = Guid.NewGuid().ToString("N"); break;
        case "settings-null": f.game.Settings = null; break;
        case "identity": f.game.Settings.serverId = "ordinary"; break;
        case "stopping": f.game.Status = GameStatus.Stopping; break;
        case "stopped": f.game.Status = GameStatus.Stopped; break;
    }
    NativeProceed(f.regular, f.local);
    Check(f.regular.NativeProceeds == 1, "loading scope excludes " + fault);
}
f = Setup(active: false, armed: true); Redeploy.Core.GameAccess.LocalGameAccessor.Throw = true;
NativeProceed(f.regular, f.local); NativeProceed(f.regular, f.local);
Check(f.regular.NativeProceeds == 2, "unreadable ownership fails open");
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "ownership warning is bounded");
f = Setup(); ArenaExtractionIsolation.Shutdown(); NativeProceed(f.regular, f.local);
Check(Harmony.Registered.Count == 0 && f.regular.NativeProceeds == 1, "shutdown removes hooks without touching exit state");
f = Setup(); ArenaExtractionIsolation.Shutdown(); Harmony.FailAt = 1; ArenaExtractionIsolation.Enable();
Check(Harmony.Registered.Count == 0, "partial installation rolls back all own hooks");
NativeProceed(f.regular, f.local); Check(f.regular.NativeProceeds == 1, "failed enable leaves native behavior available");

Console.WriteLine($"ARENAEXTRACTIONCHECKS_PASSED ({checks} assertions)");
