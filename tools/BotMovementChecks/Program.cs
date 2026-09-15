using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Movement;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private sealed class Rig
    {
        internal GameWorld World = new();
        internal LocalGame Game = new();
        internal Player Player = new();
        internal BotOwner Bot = new();
        internal MovementContext Context = new();
        internal Controller Controller => (Controller)Player.CharacterController;
        internal Rig()
        {
            Bot.GetPlayer = Player; Player.AIData.BotOwner = Bot;
            Player.MovementContext = Context; Context._player = Player; Context.CharacterController = Player.CharacterController;
            World.Ever[Player.ProfileId] = World.Alive[Player.ProfileId] = Player;
            World.MainPlayer = new Player { IsAI = false, IsYourPlayer = true, ProfileId = "local" };
            Singleton<GameWorld>.Instance = ArenaSession.World = World;
            Singleton<AbstractGame>.Instance = ArenaSession.Game = Game;
            ArenaSession.IsActive = true; ArenaMatchService.Current = new();
            ArenaMatchService.TeamStartAbortPending = false; ArenaMode.Areas.ArenaAreaRuntime.IsPending = false;
            Settings.BotMovementEnabled = new(true); Settings.BotWalkSpeedMult = new(1.25f); Settings.BotSprintSpeedMult = new(1.3f);
            ArenaBotMovementSain.Allowed = true; ArenaBotMovementSain.Throw = false; Time.timeScale = 1;
        }
    }
    private static void Assert(bool condition, string name)
    { _assertions++; if (!condition) throw new InvalidOperationException("ASSERT: " + name); }
    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.000002f;
    private static bool Equal(float a, float b) => a.Equals(b);
    private static bool Equal(Vector3 a, Vector3 b) => Equal(a.x, b.x) && Equal(a.y, b.y) && Equal(a.z, b.z);
    private static void NoBoost(string name, Action<Rig> change, Vector3? input = null, float dt = .02f)
    {
        var rig = new Rig(); var controller = rig.Controller; change(rig);
        var original = input ?? new Vector3(.1f, -.01f, -.04f); var motion = original;
        float limit = controller.Value; int writes = controller.Writes;
        var lease = ArenaBotMovement.Begin(rig.Context, ref motion, dt);
        Assert(lease == null && Equal(motion, original) && Equal(controller.Value, limit) && controller.Writes == writes, name);
        ArenaBotMovement.End(lease);
    }
    private static void Main()
    {
        try
        {
            ArenaBotMovement.Enable();
            Assert(ModulePatch.Target?.Name == nameof(MovementContext.DirectApplyMotion), "exact native motion hook registered");
            Assert(ModulePatch.PrefixHook != null && ModulePatch.FinalizerHook != null, "native hook has prefix and exception finalizer");
            Scope(); Motion(); Lifetime(); NativeDispatch();
            Console.WriteLine($"BOT_MOVEMENT_BEHAVIOR_PASSED ({_assertions} assertions)");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    private static void Scope()
    {
        NoBoost("normal raid unchanged", _ => ArenaSession.IsActive = false);
        NoBoost("master disabled", _ => Settings.BotMovementEnabled.Value = false);
        NoBoost("missing setting rejected", _ => Settings.BotMovementEnabled = null);
        NoBoost("missing world rejected", _ => Singleton<GameWorld>.Instance = null);
        NoBoost("foreign world rejected", _ => Singleton<GameWorld>.Instance = new());
        NoBoost("missing game rejected", _ => Singleton<AbstractGame>.Instance = null);
        NoBoost("foreign game rejected", _ => Singleton<AbstractGame>.Instance = new LocalGame());
        NoBoost("nonlocal game rejected", _ => Singleton<AbstractGame>.Instance = new AbstractGame());
        NoBoost("loading game unchanged", x => x.Game.Status = GameStatus.Starting);
        NoBoost("stopping game unchanged", x => x.Game.Status = GameStatus.Stopping);
        NoBoost("missing match rejected", _ => ArenaMatchService.Current = null);
        NoBoost("missing mode rejected", _ => ArenaMatchService.Current.Mode = null);
        NoBoost("unknown mode rejected", _ => ArenaMatchService.Current.Mode = new Mode());
        NoBoost("abort pending rejected", _ => ArenaMatchService.TeamStartAbortPending = true);
        NoBoost("pending area rejected", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
        foreach (Mode mode in new Mode[] { new RoundsMode(), new TacticalMode(), new TeamDeathmatchMode(), new GunGameMode() })
        {
            NoBoost("closed phase " + mode.GetType().Name, _ => { mode.Complete = false; mode.AssistScoringOpen = false; ArenaMatchService.Current.Mode = mode; });
            NoBoost("completed " + mode.GetType().Name, _ => { mode.Complete = true; mode.AssistScoringOpen = true; ArenaMatchService.Current.Mode = mode; });
            var rig = new Rig(); mode.Complete = false; mode.AssistScoringOpen = true; ArenaMatchService.Current.Mode = mode;
            var motion = new Vector3(.1f, 0, 0); var lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
            Assert(lease != null && Near(motion.x, .125f), "all live modes support boost " + mode.GetType().Name); ArenaBotMovement.End(lease);
        }
        NoBoost("non AI player unchanged", x => x.Player.IsAI = false);
        NoBoost("local player excluded", x => x.Player.IsYourPlayer = true);
        NoBoost("main body excluded", x => x.World.MainPlayer = x.Player);
        NoBoost("dead body excluded", x => x.Player.HealthController.IsAlive = false);
        NoBoost("missing health excluded", x => x.Player.HealthController = null);
        NoBoost("foreign movement context excluded", x => x.Player.MovementContext = new());
        NoBoost("missing context owner excluded", x => x.Context._player = null);
        NoBoost("missing profile excluded", x => x.Player.ProfileId = null);
        NoBoost("replaced historical body excluded", x => x.World.Ever[x.Player.ProfileId] = new());
        NoBoost("unregistered alive body excluded", x => x.World.Alive.Clear());
        NoBoost("replaced alive body excluded", x => x.World.Alive[x.Player.ProfileId] = new());
        NoBoost("missing AIData excluded", x => x.Player.AIData = null);
        NoBoost("missing bot owner excluded", x => x.Player.AIData.BotOwner = null);
        NoBoost("foreign bot owner excluded", x => x.Bot.GetPlayer = new());
        NoBoost("preactive bot excluded", x => x.Bot.BotState = EBotState.PreActive);
        NoBoost("nonactive bot excluded", x => x.Bot.BotState = EBotState.NonActive);
        NoBoost("foreign controller excluded", x => x.Context.CharacterController = new Controller());
        NoBoost("missing controller excluded", x => x.Context.CharacterController = x.Player.CharacterController = null);
        NoBoost("airborne excluded", x => x.Context.IsGrounded = false);
        NoBoost("prone excluded", x => x.Context.IsInPronePose = true);
        NoBoost("mounted excluded", x => x.Context.IsInMountedState = true);
        NoBoost("stationary weapon excluded", x => x.Context.StationaryWeapon = new());
        NoBoost("moving platform excluded", x => x.Context.PlatformMotion = new(0, .001f, 0));
        NoBoost("door interaction excluded", x => x.Bot.DoorOpener.Interacting = true);
        NoBoost("near door excluded", x => x.Bot.DoorOpener.NearDoor = true);
        NoBoost("mover near door excluded", x => x.Bot.Mover.CurrentState = EBotMoverState.NearDoor);
        NoBoost("SAIN boundary rejection respected", _ => ArenaBotMovementSain.Allowed = false);
        NoBoost("SAIN boundary failure leaves native motion", _ => ArenaBotMovementSain.Throw = true);
        NoBoost("pause excluded", _ => Time.timeScale = 0);
        foreach (EPlayerState state in Enum.GetValues<EPlayerState>().Except(new[] { EPlayerState.Run, EPlayerState.Sprint }))
            NoBoost("state excluded " + state, x => x.Context.CurrentState.Name = state);
        NoBoost("missing state excluded", x => x.Context.CurrentState = null);
    }
    private static void Motion()
    {
        var rig = new Rig(); var motion = new Vector3(.12f, -.02f, -.08f);
        var lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(lease != null && Near(motion.x, .15f) && Near(motion.z, -.1f) && motion.y == -.02f, "walk scales horizontal only");
        Assert(Near(rig.Controller.Value, 5), "positive native cap scales with displacement");
        ArenaBotMovement.End(lease); Assert(rig.Controller.Value == 4, "normal completion restores native cap");
        rig.Context.CurrentState.Name = EPlayerState.Sprint; motion = new(.1f, 0, 0);
        lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .13f), "sprint uses separate factor"); ArenaBotMovement.End(lease);
        Settings.BotSprintSpeedMult.Value = 99; motion = new(.1f, 0, 0);
        lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .15f), "user multiplier bounded at one point five"); ArenaBotMovement.End(lease);
        rig.Context.CurrentState.Name = EPlayerState.Run; Settings.BotWalkSpeedMult.Value = 1.5f;
        foreach (Vector3 source in new[] { new Vector3(.25f, 0, 0), new Vector3(.15f, .2f, .15f), new Vector3(0, -.2f, -.22f) })
        {
            motion = source; lease = ArenaBotMovement.Begin(rig.Context, ref motion, .2f);
            double length = Math.Sqrt((double)motion.x * motion.x + (double)motion.y * motion.y + (double)motion.z * motion.z);
            Assert(length <= .30000003 && motion.y == source.y, "bonus respects native per-step ceiling");
            Assert(Math.Abs(motion.x) >= Math.Abs(source.x) && Math.Abs(motion.z) >= Math.Abs(source.z), "ceiling never slows native input");
            ArenaBotMovement.End(lease);
        }
        NoBoost("large native step untouched", _ => { }, new(.31f, 0, 0));
        NoBoost("large vertical native step untouched", _ => { }, new(.02f, .3f, 0));
        NoBoost("vertical-only movement untouched", _ => { }, new(0, -.02f, 0));
        NoBoost("stationary bot receives no motion", _ => { }, Vector3.zero);
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            NoBoost("invalid delta rejected " + invalid, _ => { }, dt: invalid);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            NoBoost("invalid X rejected " + invalid, _ => { }, new(invalid, 0, .1f));
            NoBoost("invalid Y rejected " + invalid, _ => { }, new(.1f, invalid, 0));
            NoBoost("invalid Z rejected " + invalid, _ => { }, new(.1f, 0, invalid));
            NoBoost("invalid setting rejected " + invalid, _ => Settings.BotWalkSpeedMult.Value = invalid);
            NoBoost("invalid cap rejected " + invalid, x => x.Controller.Value = invalid);
        }
        NoBoost("native hard-stop zero cap respected", x => x.Controller.Value = 0);
        NoBoost("factor one restores vanilla", _ => Settings.BotWalkSpeedMult.Value = 1);
        NoBoost("sub-native factor rejected", _ => Settings.BotWalkSpeedMult.Value = .5f);
        NoBoost("overflow cap rejected", x => x.Controller.Value = float.MaxValue);
        rig = new Rig(); rig.Controller.Value = -1; motion = new(.1f, .02f, 0);
        lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(lease != null && Near(motion.x, .125f) && rig.Controller.Writes == 0 && rig.Controller.Value == -1, "native negative cap never rewritten"); ArenaBotMovement.End(lease);
        // A native path is an optional source of corner data. SAIN can drive a
        // paused/pathless native mover; this must not silently disable its boost.
        rig = new Rig(); rig.Bot.Mover.Pause = true; rig.Bot.Mover.HasPathAndNoComplete = true; rig.Bot.Mover.CurrentCornerPoint = Vector3.zero;
        motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .125f), "SAIN paused native mover remains eligible"); ArenaBotMovement.End(lease);
        rig.Bot.Mover.Pause = false; rig.Bot.Mover.HasPathAndNoComplete = false;
        motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .125f), "SAIN pathless native mover remains eligible"); ArenaBotMovement.End(lease);
        NoBoost("native near corner not accelerated", x => { x.Bot.Mover.HasPathAndNoComplete = true; x.Bot.Mover.CurrentCornerPoint = new(.5f, 7, 0); });
        NoBoost("invalid native corner rejected", x => { x.Bot.Mover.HasPathAndNoComplete = true; x.Bot.Mover.CurrentCornerPoint = new(float.NaN, 0, 0); });
        rig = new Rig(); rig.Bot.Mover.HasPathAndNoComplete = true; rig.Bot.Mover.CurrentCornerPoint = new(1.375f, 9, 0);
        motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .1125f), "corner approach eases bonus horizontally"); ArenaBotMovement.End(lease);
        rig.Bot.Mover.CurrentCornerPoint = new(2, 0, 0); motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(Near(motion.x, .125f), "full bonus beyond corner threshold"); ArenaBotMovement.End(lease);
    }
    private static void Lifetime()
    {
        var rig = new Rig(); var motion = new Vector3(.1f, 0, 0);
        var lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f); var scaled = motion;
        var nested = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(nested == null && Equal(motion, scaled) && rig.Controller.Value == 5, "reentrant call cannot compound boost");
        ArenaBotMovement.End(nested); Assert(rig.Controller.Value == 5, "inner cleanup does not release outer lease");
        ArenaBotMovement.End(lease); motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        Assert(lease != null && Near(motion.x, .125f), "later frame can acquire fresh lease"); ArenaBotMovement.End(lease);
        motion = new(.1f, 0, 0); lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f); rig.Controller.Value = 2;
        ArenaBotMovement.End(lease); Assert(rig.Controller.Value == 2, "foreign cap write preserved");
        rig = new Rig(); var captured = rig.Controller; motion = new(.1f, 0, 0);
        lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
        rig.Context.CharacterController = new Controller { Value = 7 }; ArenaSession.IsActive = false; ArenaMatchService.Current = null;
        ArenaBotMovement.End(lease);
        Assert(captured.Value == 4 && rig.Context.CharacterController.SpeedLimit == 7, "captured controller restored after raid and body swap");
        foreach (bool afterWrite in new[] { false, true })
        {
            rig = new Rig(); if (afterWrite) rig.Controller.ThrowAfterWrites = 1; else rig.Controller.ThrowBeforeWrites = 1;
            var original = new Vector3(.1f, .02f, -.04f); motion = original;
            lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
            Assert(lease == null && Equal(motion, original) && rig.Controller.Value == 4, "throwing cap setter restores input and cap " + afterWrite);
            lease = ArenaBotMovement.Begin(rig.Context, ref motion, .02f);
            Assert(lease != null, "throwing setter releases reentrant guard " + afterWrite); ArenaBotMovement.End(lease);
        }
        ArenaBotMovement.End(null);
    }
    private static void NativeDispatch()
    {
        var rig = new Rig(); var motion = new Vector3(.1f, -.01f, 0);
        object[] args = { rig.Context, motion, .02f, null };
        ModulePatch.PrefixHook.Invoke(null, args); var lease = (ArenaBotMovement.MotionLease)args[3];
        rig.Context.DirectApplyMotion((Vector3)args[1], .02f);
        var normal = ModulePatch.FinalizerHook.Invoke(null, new object[] { null, lease });
        Assert(normal == null && rig.Context.NativeCalls == 1 && Near(rig.Context.NativeMotion.x, .125f), "native collision operation remains in dispatch");
        Assert(rig.Controller.Value == 4, "native success finalizer restores cap");
        rig.Context.FailNative = true; args = new object[] { rig.Context, motion, .02f, null }; ModulePatch.PrefixHook.Invoke(null, args);
        Exception original = null;
        try { rig.Context.DirectApplyMotion((Vector3)args[1], .02f); } catch (Exception ex) { original = ex; }
        var returned = ModulePatch.FinalizerHook.Invoke(null, new object[] { original, args[3] });
        Assert(ReferenceEquals(returned, original) && original != null && rig.Controller.Value == 4, "native exception retained and cap restored");
        rig.Context.FailNative = false; args = new object[] { rig.Context, motion, .02f, null }; ModulePatch.PrefixHook.Invoke(null, args);
        Assert(args[3] != null, "native exception releases movement lease"); ModulePatch.FinalizerHook.Invoke(null, new object[] { null, args[3] });
    }
}
