using ArenaMode.Session;
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.Communications;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using Mono.Cecil;
using Mono.Cecil.Cil;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException("ASSERTION_FAILED: " + name); checks++; }
(LocalGame game, GameWorld world) Fresh()
{
    ArenaSession.IsActive = false; ArenaRaidExit.Tick();
    ArenaSession.IsActive = ArenaSession.Exact = true;
    ArenaRespawnDriver.StandDown = RespawnEngineState.IsPipelineRunning = LocalGameAccessor.Throw = NotificationManager.Throw = false;
    UnityEngine.Time.timeScale = 1;
    var game = new LocalGame(); game.Owner = new() { Player = game.LocalPlayer };
    var world = new GameWorld { MainPlayer = game.LocalPlayer };
    Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    MonoBehaviourSingleton<PreloaderUI>.Instance = new();
    return (game, world);
}
void Untouched(LocalGame game, string name)
    => Check(game.Calls == 0 && !ArenaRespawnDriver.StandDown && MonoBehaviourSingleton<PreloaderUI>.Instance.Calls == 0, name);

var (game, world) = Fresh();
Check(ArenaRaidExit.Request(game), "active request owned");
Check(game.Calls == 1 && game.Status == GameStatus.Stopping && ArenaRespawnDriver.StandDown
    && !MonoBehaviourSingleton<PreloaderUI>.Instance.Visible, "native continuation and taskbar effects once");
ArenaRaidExit.Request(game); ArenaRaidExit.Tick(); Check(game.Calls == 1, "duplicate cannot leave twice");

Action[] excluded = [() => ArenaSession.IsActive = false, () => ArenaSession.Exact = false,
    () => Singleton<AbstractGame>.Instance = new LocalGame(), () => Singleton<GameWorld>.Instance = null,
    () => game.Status = GameStatus.Starting, () => game.ProfileId = ""];
foreach (var exclusion in excluded)
{
    (game, world) = Fresh(); exclusion();
    Check(!ArenaRaidExit.Request(game), "ordinary or invalid request stays native"); Untouched(game, "excluded request untouched");
}

Action[] unsafeOwner = [() => RespawnEngineState.IsPipelineRunning = true, () => UnityEngine.Time.timeScale = 0,
    () => game.Owner = null, () => game.Owner.Player = new(), () => game.LocalPlayer = null,
    () => world.MainPlayer = new(), () => game.LocalPlayer.ProfileId = "other"];
for (int i = 0; i < unsafeOwner.Length; i++)
{
    (game, world) = Fresh(); unsafeOwner[i]();
    Check(ArenaRaidExit.Request(game), "unsafe request queued " + i);
    ArenaRaidExit.Tick(); Untouched(game, "unsafe exit untouched " + i);
    // A real rebind creates a NEW physical player and owner under this raid.
    RespawnEngineState.IsPipelineRunning = false; UnityEngine.Time.timeScale = 1;
    game.LocalPlayer = new(); game.Owner = new() { Player = game.LocalPlayer }; world.MainPlayer = game.LocalPlayer;
    ArenaRaidExit.Tick(); Check(game.Calls == 1 && game.Status == GameStatus.Stopping, "rebound owner releases exit " + i);
}

Action[] invalidate = [() => ArenaSession.IsActive = false, () => ArenaSession.Exact = false,
    () => Singleton<AbstractGame>.Instance = new LocalGame(), () => Singleton<GameWorld>.Instance = new(),
    () => game.ProfileId = "different", () => game.Status = GameStatus.Stopping];
foreach (var invalidation in invalidate)
{
    (game, world) = Fresh(); RespawnEngineState.IsPipelineRunning = true; ArenaRaidExit.Request(game);
    invalidation(); RespawnEngineState.IsPipelineRunning = false; ArenaRaidExit.Tick(); Untouched(game, "stale queued exit discarded");
    ArenaSession.IsActive = ArenaSession.Exact = true; Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
    game.ProfileId = "local"; game.Status = GameStatus.Started; ArenaRaidExit.Tick(); Untouched(game, "discarded exit cannot revive");
}
foreach (bool previousHold in new[] { false, true })
{
    (game, world) = Fresh(); ArenaRespawnDriver.StandDown = previousHold;
    game.Before = () => throw new Exception("precommit"); ArenaRaidExit.Request(game);
    Check(game.Status == GameStatus.Started && ArenaRespawnDriver.StandDown == previousHold, "precommit restores prior hold");
    ArenaRaidExit.Tick(); Check(game.Calls == 1, "precommit does not retry automatically");
    game.Before = null; ArenaRaidExit.Request(game); Check(game.Status == GameStatus.Stopping, "explicit retry permitted");
}
(game, world) = Fresh(); game.Ignore = true; ArenaRaidExit.Request(game);
Check(!ArenaRespawnDriver.StandDown, "noncommitting callback restores hold"); ArenaRaidExit.Tick(); Check(game.Calls == 1, "ignored callback not retried");
(game, world) = Fresh(); game.After = () => throw new Exception("postcommit"); ArenaRaidExit.Request(game); ArenaRaidExit.Tick();
Check(game.Calls == 1 && ArenaRespawnDriver.StandDown, "postcommit never retries or rearms death hold");
(game, world) = Fresh(); NotificationManager.Throw = true; RespawnEngineState.IsPipelineRunning = true;
bool notificationEscaped = false;
try { ArenaRaidExit.Request(game); } catch { notificationEscaped = true; }
RespawnEngineState.IsPipelineRunning = false; ArenaRaidExit.Tick();
Check(!notificationEscaped && game.Calls == 1, "notification failure cannot discard pending exit");
(game, world) = Fresh(); LocalGameAccessor.Throw = NotificationManager.Throw = true;
ArenaRaidExit.Request(game); Untouched(game, "owner access fault contained");

var repo = Path.GetFullPath(args[0]);
// Verify the installed native seam without loading Unity or executing EFT.
using var assembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(Path.Combine(repo, "../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")));
var nativeBase = assembly.MainModule.Types.Single(t => t.Name == "BaseLocalGame`1");
var nativeLocal = assembly.MainModule.Types.Single(t => t.FullName == "EFT.LocalGame");
Check(nativeLocal.BaseType.FullName == "EFT.BaseLocalGame`1<EFT.EftGamePlayerOwner>"
    && !nativeLocal.Methods.Any(m => m.Name == "vmethod_4"), "installed local leave dispatch remains inherited");
var leave = nativeBase.Methods.Single(m => m.Name == "vmethod_4");
var callback = nativeBase.Methods.Single(m => m.Name == "CG_method_18");
Check(leave.Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "CG_method_18") == 1
    && leave.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "add_OnLeave"), "installed confirmation owns this exact callback");
Check(callback.IsPublic && callback.Parameters.Count == 0 && callback.ReturnType.FullName == "System.Void", "callback callable signature");
var call = callback.Body.Instructions.Single(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference m && m.Name == "Stop");
Check(call.Previous.OpCode == OpCodes.Ldc_R4 && (float)call.Previous.Operand == 0f
    && call.Previous.Previous.OpCode == OpCodes.Ldnull && call.Previous.Previous.Previous.OpCode == OpCodes.Ldc_I4_2,
    "installed native callback Stop Left null zero");
var exitEnum = assembly.MainModule.Types.Single(t => t.FullName == "EFT.ExitStatus");
Check((int)exitEnum.Fields.Single(f => f.Name == "Left").Constant == 2, "native Left value");
var reconnect = assembly.MainModule.Types.Single(t => t.FullName == "EFT.UI.ReconnectionScreen");
var controller = reconnect.NestedTypes.Single(t => t.Name == "ReconnectionScreenController");
Check(controller.Methods.Single(m => m.Name == "Leave").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "SetMenuTaskBarVisibility"), "native taskbar effect retained");
Console.WriteLine($"ARENA_EXIT_CHECKS_PASSED: {checks} assertions");
