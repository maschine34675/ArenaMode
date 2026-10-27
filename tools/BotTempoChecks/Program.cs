using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Classes.Bot.Search;
using SAIN.Components;
using SAIN.Layers.Combat.Solo;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Mover;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.AI;

internal static class Program
{
    private static int _assertions;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static object Call(string name, params object[] args) => typeof(ArenaBotTempo).GetMethod(name, Private).Invoke(null, args);
    private static void Check(bool yes, string name) { _assertions++; if (!yes) throw new Exception("ASSERT: " + name); }
    private sealed class Rig
    {
        public GameWorld World = new(); public LocalGame Game = new();
        public Player Player = new(); public Player Target = new() { ProfileId = "enemy" };
        public BotOwner Owner = new(); public BotComponent Bot = new();
        public Enemy Enemy; public SearchClass Search => Bot.Search;
        public BotPathDataManual Path => Bot.Mover.ActivePath;
        public Rig()
        {
            Owner.GetPlayer = Player; Player.AIData.BotOwner = Owner; Owner.Component = Bot;
            Player.MovementContext = new() { _player = Player, CharacterController = Player.CharacterController };
            Bot.Player = Player; Bot.BotOwner = Owner;
            Bot.CurrentAction = new SearchAction(Bot); Enemy = new() { Bot = Bot, EnemyPlayer = Target };
            Bot.Search = new() { Bot = Bot, SearchTarget = Enemy }; Bot.Mover.DogFight = new() { Bot = Bot };
            Bot.Mover.ActivePath = new(Bot);
            World.Ever[Player.ProfileId] = World.Alive[Player.ProfileId] = Player;
            World.Ever[Target.ProfileId] = World.Alive[Target.ProfileId] = Target;
            World.MainPlayer = new() { IsYourPlayer = true, IsAI = false };
            Singleton<GameWorld>.Instance = ArenaSession.World = World; Singleton<AbstractGame>.Instance = ArenaSession.Game = Game;
            ArenaSession.IsActive = true; ArenaRespawnDriver.StandDown = false;
            ArenaMatchService.Current = new(); ArenaMatchService.TeamStartAbortPending = false;
            ArenaMode.Areas.ArenaAreaRuntime.IsPending = false;
            Settings.BotMovementEnabled = new(true); ArenaBotMovementSain.Allowed = true; ArenaBotMovementSain.Throw = false;
            Time.time = 100; Time.timeScale = 1;
        }
        public bool Sprint(bool input = false)
        { object[] args = [Search, input, Enemy]; Call("SearchPrefix", args); return (bool)args[1]; }
        public float Strafe(float input = .4f) => (float)Call("StrafeSpeed", input, Bot.Mover.DogFight, Enemy);
        public void WaitStart() { Search.CurrentState = State.Wait; Search.Deadline = Path.Deadline = 110; Path.PausedRequested = true; }
        public object WaitPrefix()
        { object[] args = [Search, null]; Call("WaitPrefix", args); return args[1]; }
        public void WaitTick()
        {
            var lease = WaitPrefix();
            typeof(SearchClass).GetMethod("WaitAtPoint", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Search, []);
            Call("WaitFinalizer", null, lease);
        }
    }
    private static void RejectAll(string name, Action<Rig> change)
    {
        var r = new Rig(); r.WaitStart(); change(r); r.WaitPrefix(); Time.time += .25f; r.WaitPrefix(); Time.time += .3f;
        Check(r.WaitPrefix() == null && r.Search.Deadline == 110, name + " wait");
        r.Search.CurrentState = State.DirectMove;
        Check(!r.Sprint() && r.Strafe() == .4f, name);
    }
    private static void Main()
    {
        try
        {
            ArenaBotMovement.Enable(); ArenaBotTempo.Enable();
            Check(Harmony.Hooks.Count == 3, "three exact native hooks bound");
            Scope(); Search(); Wait(); Strafe(); Diagnostics(); BindingFailure();
            Console.WriteLine($"BOT_TEMPO_BEHAVIOR_PASSED ({_assertions} assertions)");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    private static void Scope()
    {
        RejectAll("normal raid unchanged", _ => ArenaSession.IsActive = false);
        RejectAll("setting off", _ => Settings.BotMovementEnabled.Value = false);
        RejectAll("pre-live unchanged", _ => ((RoundsMode)ArenaMatchService.Current.Mode).AssistScoringOpen = false);
        RejectAll("complete unchanged", _ => ((RoundsMode)ArenaMatchService.Current.Mode).Complete = true);
        RejectAll("exiting unchanged", _ => ArenaRespawnDriver.StandDown = true);
        RejectAll("wrong world", _ => ArenaSession.World = new());
        RejectAll("wrong game", _ => ArenaSession.Game = new LocalGame());
        RejectAll("pre-start", r => r.Game.Status = GameStatus.Starting);
        RejectAll("area pending", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
        RejectAll("team abort", _ => ArenaMatchService.TeamStartAbortPending = true);
        RejectAll("player excluded", r => r.Player.IsYourPlayer = true);
        RejectAll("nonAI excluded", r => r.Player.IsAI = false);
        RejectAll("dead excluded", r => r.Player.HealthController.IsAlive = false);
        RejectAll("replaced body", r => r.World.Alive[r.Player.ProfileId] = new());
        RejectAll("retired owner", r => r.Owner.BotState = EBotState.NonActive);
        RejectAll("replaced controller", r => r.Player.CharacterController = new Controller());
        RejectAll("foreign component", r => r.Owner.Component = new BotComponent());
        RejectAll("foreign action owner", r => r.Bot.CurrentAction = new SearchAction(new()));
        RejectAll("custom action owner", r => r.Bot.CurrentAction = new ExternalAction());
        RejectAll("no SAIN layer", r => r.Bot.SAINLayersActive = false);
        RejectAll("self action", r => r.Bot.Decision.CurrentSelfDecision = SelfAction.Heal);
        RejectAll("native door", r => r.Owner.DoorOpener.Interacting = true);
        RejectAll("SAIN door", _ => ArenaBotMovementSain.Allowed = false);
        RejectAll("guard throws", _ => ArenaBotMovementSain.Throw = true);
        RejectAll("reload active", r => r.Owner.WeaponManager.Reload.Reloading = true);
        RejectAll("med active", r => r.Owner.Medecine.SurgicalKit.Using = true);
        RejectAll("grenade action", r => r.Owner.WeaponManager.Grenades.ThrowindNow = true);
        RejectAll("grenade danger", r => r.Bot.Grenade.GrenadeReactionClass.DangerGrenade = new());
        RejectAll("native grenade danger", r => r.Owner.BewareGrenade.GrenadeDangerPoint = new());
        RejectAll("artillery danger", r => r.Owner.ArtilleryDangerPlace.IsActive = true);
        RejectAll("BTR danger", r => r.Owner.BewareBTR.Danger = true);
        RejectAll("flashed", r => r.Owner.FlashGrenade._isFlashed = true);
        RejectAll("airborne", r => r.Player.MovementContext.IsGrounded = false);
        RejectAll("vaulting", r => r.Player.MovementContext.CurrentState.Name = EPlayerState.Vaulting);
        RejectAll("prone", r => r.Player.MovementContext.IsInPronePose = true);
        RejectAll("paused game", _ => Time.timeScale = 0);
        RejectAll("foreign enemy", r => r.Enemy.Bot = new());
        RejectAll("dead enemy", r => r.Target.HealthController.IsAlive = false);
        RejectAll("replaced enemy", r => r.World.Alive[r.Target.ProfileId] = new());
        foreach (var mode in new Mode[] { new RoundsMode(), new TacticalMode(), new TeamDeathmatchMode(), new GunGameMode() })
        { var r = new Rig(); ArenaMatchService.Current.Mode = mode; Check(r.Sprint() && r.Strafe() == .85f, "supported live mode " + mode.GetType().Name); }
    }
    private sealed class ExternalAction : SAIN.Layers.IBotAction { }
    private static void Search()
    {
        var r = new Rig(); Check(r.Sprint(), "long clear search sprints");
        r.Player.Position = new(5,0,0); Check(r.Sprint(), "advanced path origin does not flutter");
        r.Player.Position = new(5,0,4); Check(!r.Sprint(), "off route unchanged");
        r = new Rig(); r.Enemy.Path.PathToEnemy.corners = [new(0,0,0), new(3,0,0), new(3,0,20)];
        Check(!r.Sprint(), "near corner unchanged");
        r.Enemy.Path.PathToEnemy.corners = [new(0,0,0), new(7,0,0)]; Check(!r.Sprint(), "short path unchanged");
        r.Enemy.Path.PathToEnemy.corners = [new(float.NaN,0,0), new(20,0,0)]; Check(!r.Sprint(), "nonfinite path unchanged");
        foreach (var status in new[] { NavMeshPathStatus.PathPartial, NavMeshPathStatus.PathInvalid })
        { r = new Rig(); r.Enemy.Path.PathToEnemy.status = status; Check(!r.Sprint(), "incomplete path unchanged"); }
        var exclusions = new Action<Rig>[] {
            x => x.Enemy.IsVisible = true, x => x.Enemy.InLineOfSight = true,
            x => x.Bot.EnemyController.VisibleEnemies.Add(x.Enemy), x => x.Bot.EnemyController.EnemiesInLineOfSight.Add(x.Enemy),
            x => x.Enemy.Hearing.EnemyHeardFromPeace = true, x => x.Owner.Memory.IsUnderFire = true,
            x => x.Path.PausedRequested = true, x => x.Path.PathRecalcRequested = true, x => x.Path.CancelRequested = true,
            x => x.Search.CurrentState = State.Wait, x => x.Search.SearchActive = false,
            x => x.Search.SearchTarget = new(), x => x.Bot.CurrentAction = new FightAction(x.Bot) };
        foreach (var change in exclusions) { r = new Rig(); change(r); Check(!r.Sprint(), "search safety retained"); Check(r.Sprint(true), "native sprint retained"); }
    }
    private static void Wait()
    {
        var r = new Rig(); r.WaitStart(); r.Owner.WeaponManager.Reload.Reloading = true;
        r.WaitTick(); Time.time += 1; r.WaitTick(); Check(r.Path.UnpauseCalls == 0 && r.Search.Deadline == 110, "reload remains paused");
        r.Owner.WeaponManager.Reload.Reloading = false;
        r.WaitTick(); Time.time += .25f; r.WaitTick(); Check(r.Path.UnpauseCalls == 0, "safe tail not released early");
        Time.time += .25f; r.WaitTick(); Check(r.Path.UnpauseCalls == 1 && r.Search.Deadline == -1, "completed reload native release");
        Action<Rig>[] invalid = [x => x.Path.Deadline += 1, x => x.Path.PausedRequested = false,
            x => x.Path.CancelRequested = true, x => x.Path.PathRecalcRequested = true,
            x => x.Path.PathFinder = new(), x => x.Bot.Mover.ActivePath = new(new()) { Deadline = 110 }];
        foreach (var change in invalid)
        {
            r = new Rig(); r.WaitStart(); r.WaitPrefix(); Time.time += .25f; r.WaitPrefix(); change(r); Time.time += .25f;
            Check(r.WaitPrefix() == null && r.Search.Deadline == 110, "foreign pause preserved");
        }
        r = new Rig(); r.WaitStart(); r.WaitPrefix(); Time.time += .25f; r.WaitPrefix(); r.Path.TimeStarted = 100.3f; Time.time += .25f;
        Check(r.WaitPrefix() == null, "pooled path epoch resets observation");
        Time.time += .25f; Check(r.WaitPrefix() == null, "pooled path gets full safe tail");
        r = new Rig(); r.WaitStart(); r.WaitPrefix(); Time.time += 2;
        Check(r.WaitPrefix() == null, "observation gap resets tail");
        r = new Rig(); r.WaitStart(); r.Search.Deadline = r.Path.Deadline = 100.2f;
        r.WaitTick(); Time.time += .25f; r.WaitTick(); Check(r.Path.UnpauseCalls == 1, "native shorter wait wins");
        r = new Rig(); r.WaitStart(); r.WaitPrefix(); Time.time += .25f; r.WaitPrefix(); Time.time += .25f;
        var lease = r.WaitPrefix(); Check(lease != null && r.Path.UnpauseCalls == 0, "prefix never unpauses path");
        var error = new Exception("native failed"); Check(ReferenceEquals(Call("WaitFinalizer", error, lease), error) && r.Search.Deadline == 110, "exception restores owned wait");
        r.Search.Deadline = 120; Call("WaitFinalizer", error, lease); Check(r.Search.Deadline == 120, "foreign deadline retained");
    }
    private static void Strafe()
    {
        var r = new Rig(); Check(r.Strafe() == .85f && r.Bot.Info.FileSettings.Move.STRAFE_SPEED == .4f, "strafe floor without preset mutation");
        r.Owner.Memory.IsUnderFire = true; Check(r.Strafe() == .85f, "combat under fire still strafes");
        foreach (float x in new[] { float.Epsilon, .69f, .7f, .8f, float.BitDecrement(.85f) })
            Check(r.Strafe(x) == .85f, "subfloor strafe raised across old boundary");
        foreach (float x in new[] { 0, -1, .85f, float.BitIncrement(.85f), .9f, 1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(r.Strafe(x).Equals(x), "native exceptional or higher strafe preserved");
        Check(r.Bot.Info.FileSettings.Move.STRAFE_SPEED == .4f, "all strafe inputs leave preset unchanged");
        var original = Harmony.Il();
        var il = ((IEnumerable<CodeInstruction>)Call("StrafeTranspiler", original)).ToList();
        Check(il.Count == original.Count + 3 && ReferenceEquals(il[0], original[0]) && (float)il[0].operand == .9f
            && il[2].opcode == OpCodes.Ldarg_0 && il[3].opcode == OpCodes.Ldarg_2 && il[4].opcode == OpCodes.Call
            && ((MethodInfo)il[4].operand).Name == "StrafeSpeed" && ReferenceEquals(il[5], original[2]), "only strafe load gains helper");
    }
    private static void Diagnostics()
    {
        var r = new Rig(); Plugin.ModLogger.Lines.Clear(); r.Sprint(); r.Strafe();
        Check(Plugin.ModLogger.Lines.Count == 0, "no per-call tempo logs");
        Time.time += 60; r.Sprint(); Check(Plugin.ModLogger.Lines.Count == 1 && Plugin.ModLogger.Lines[0].Contains("searchSprintInputs=2"), "minute aggregate diagnostics");
    }
    private static void BindingFailure()
    {
        ArenaBotTempo.Shutdown(); Harmony.Calls = 0; Harmony.FailAt = 2; ArenaBotTempo.Enable();
        Check(Harmony.Hooks.Count == 0 && !new Rig().Sprint(), "partial patch rolled back");
        Harmony.FailAt = 0; Harmony.BadIl = true; ArenaBotTempo.Enable();
        Check(Harmony.Hooks.Count == 0 && new Rig().Strafe() == .4f, "IL drift rolls back adapter");
        Harmony.BadIl = false; ArenaBotTempo.Enable(); Check(Harmony.Hooks.Count == 3 && new Rig().Sprint(), "supported rebind succeeds");
        ArenaBotTempo.Shutdown(); Check(Harmony.Hooks.Count == 0 && !new Rig().Sprint(), "shutdown restores native");
    }
}
