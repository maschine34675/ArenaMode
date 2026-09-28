using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using K = ArenaMode.Movement.ArenaBotAgility.ValueKind;

internal static class Program
{
    private static int _assertions;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static object Call(string name, params object[] args) => typeof(ArenaBotAgility).GetMethod(name, Private).Invoke(null,args);
    private static void Check(bool yes, string name) { _assertions++; if (!yes) throw new Exception("ASSERT: " + name); }
    private static bool Near(float a, float b) => System.Math.Abs(a-b) < .00001f;
    private static float Tune(float input, object receiver, K kind) => (float)Call("Tune", input, receiver, kind);
    private static object Enter(object receiver, float dt = .02f)
    { object[] args = [receiver, dt, null]; Call("Prefix", args); return args[2]; }
    private static void Exit(object scope) => Call("Finalizer", null, scope);
    private sealed class Rig
    {
        public GameWorld World = new(); public LocalGame Game = new(); public Player Player = new();
        public BotOwner Owner = new(); public MovementContext Context = new(); public MovePlayerState Move;
        public Rig()
        {
            Owner.GetPlayer = Player; Player.AIData.BotOwner = Owner; Context._player = Player;
            Player.MovementContext = Context; Context.CharacterController = Player.CharacterController;
            Move = new(Context); Context.CurrentState = Move;
            World.Ever[Player.ProfileId] = World.Alive[Player.ProfileId] = Player;
            World.MainPlayer = new() { IsAI = false, IsYourPlayer = true };
            Singleton<GameWorld>.Instance = ArenaSession.World = World; Singleton<AbstractGame>.Instance = ArenaSession.Game = Game;
            Singleton<GlobalConfiguration>.Instance = new();
            ArenaMatchService.Current = new(); ArenaMatchService.TeamStartAbortPending = false;
            ArenaMode.Areas.ArenaAreaRuntime.IsPending = false; ArenaSession.IsActive = true;
            ArenaRespawnDriver.StandDown = false; Settings.BotMovementEnabled = new(true);
            ArenaBotMovementSain.Allowed = true; ArenaBotMovementSain.Throw = false; Time.timeScale = 1;
        }
        public float Apply(K kind, float value, bool context = false)
        {
            object receiver = context ? Context : Move; var previous = Enter(receiver);
            try { return Tune(value, receiver, kind); } finally { Exit(previous); }
        }
    }
    private static void Reject(string name, Action<Rig> action)
    {
        var r = new Rig(); action(r);
        foreach (K kind in Enum.GetValues<K>()) Check(r.Apply(kind,.4f, kind is K.Sprint or K.PreSprint) == .4f, name);
    }
    private static void Main()
    {
        try
        {
            ArenaBotMovement.Enable(); ArenaBotAgility.Enable();
            Check(Harmony.Hooks.Count == 4, "four native consumers patched");
            Math(); Scope(); Lifetime(); Il(); Diagnostics(); Failures();
            Console.WriteLine($"BOT_AGILITY_BEHAVIOR_PASSED ({_assertions} assertions)");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    private static void Math()
    {
        var r = new Rig(); var p = r.Player.Physical;
        Check(Near(r.Apply(K.Walk, .203f), .0624f), "walk weight and blend benefits compose");
        Check(Near(r.Apply(K.Inertia, .6f), .3f), "remaining inertia halved");
        Check(Near(r.Apply(K.PreSprint, 2.22f, true), 3.915f), "pre-sprint weight and acceleration compose");
        Check(Near(r.Apply(K.Sprint, .4f, true), 1.05f), "sprint uses inverse-lerp zero-weight baseline");
        Check(Near(r.Apply(K.Acceleration, 1), 1.5f), "direction and second ramp accelerate");
        Check(r.Apply(K.Side, 1.4f) == 1.4f && r.Apply(K.Diagonal,1.16f) == 1.16f, "beneficial native side curves preserved");
        Check(Near(r.Apply(K.Side, 3), 2.5f), "adverse custom side penalty reduced");
        Check(Near(r.Apply(K.Walk,.001f),.0006f) && Near(r.Apply(K.Sprint,2,true),3), "better native values retain separate agility bonus");
        foreach (K kind in Enum.GetValues<K>())
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check(r.Apply(kind,invalid,kind is K.Sprint or K.PreSprint).Equals(invalid), "native zero invalid and negative preserved");
        p.Inertia = 1; p.SprintAcceleration = 0;
        Check(r.Apply(K.Sprint,p.SprintAcceleration,true) == 0, "extreme weight zero acceleration retained conservatively");
        Check(r.Context.WalkInertia == .203f && p.PreSprintAcceleration == 2.22f && p.Side == 1.4f
            && r.Move._directionSpeedAcceleration == 1 && Singleton<GlobalConfiguration>.Instance.Inertia.SuddenChangesSmoothness == 5,
            "no stored physics or global mutation");
        Check(r.Apply(K.Acceleration,float.MaxValue) == float.MaxValue, "overflow retains input");
    }
    private static void Scope()
    {
        Reject("ordinary raid unchanged", _ => ArenaSession.IsActive = false);
        Reject("switch off", _ => Settings.BotMovementEnabled.Value = false);
        Reject("nonlive mode", _ => ((RoundsMode)ArenaMatchService.Current.Mode).AssistScoringOpen = false);
        Reject("complete mode", _ => ((RoundsMode)ArenaMatchService.Current.Mode).Complete = true);
        Reject("exit pending", _ => ArenaRespawnDriver.StandDown = true);
        Reject("wrong world", _ => ArenaSession.World = new());
        Reject("wrong game", _ => ArenaSession.Game = new LocalGame());
        Reject("not started", r => r.Game.Status = GameStatus.Starting);
        Reject("area pending", _ => ArenaMode.Areas.ArenaAreaRuntime.IsPending = true);
        Reject("abort pending", _ => ArenaMatchService.TeamStartAbortPending = true);
        Reject("human excluded", r => r.Player.IsYourPlayer = true);
        Reject("nonAI excluded", r => r.Player.IsAI = false);
        Reject("main body excluded", r => r.World.MainPlayer = r.Player);
        Reject("dead body", r => r.Player.HealthController.IsAlive = false);
        Reject("retired owner", r => r.Owner.BotState = EBotState.NonActive);
        Reject("replaced body", r => r.World.Alive[r.Player.ProfileId] = new());
        Reject("replaced controller", r => r.Player.CharacterController = new Controller());
        Reject("missing physical", r => r.Player.Physical = null);
        Reject("airborne", r => r.Context.IsGrounded = false);
        Reject("prone", r => r.Context.IsInPronePose = true);
        Reject("mounted", r => r.Context.IsInMountedState = true);
        Reject("platform", r => r.Context.PlatformMotion = new(0,0,1));
        Reject("stationary", r => r.Context.StationaryWeapon = new());
        Reject("vaulting", r => r.Move.Name = EPlayerState.Vaulting);
        Reject("near door", r => r.Owner.DoorOpener.NearDoor = true);
        Reject("door interaction", r => r.Owner.DoorOpener.Interacting = true);
        Reject("mover door", r => r.Owner.Mover.CurrentState = EBotMoverState.NearDoor);
        Reject("SAIN door", _ => ArenaBotMovementSain.Allowed = false);
        Reject("SAIN read failure", _ => ArenaBotMovementSain.Throw = true);
        Reject("time paused", _ => Time.timeScale = 0);
        foreach (var mode in new Mode[] { new RoundsMode(),new TacticalMode(),new TeamDeathmatchMode(),new GunGameMode() })
        { var r = new Rig(); ArenaMatchService.Current.Mode = mode; Check(r.Apply(K.Acceleration,1) == 1.5f,"live mode " + mode.GetType().Name); }
        var rig = new Rig(); rig.Context.CurrentState = new();
        Check(rig.Apply(K.Walk,.4f) == .4f,"obsolete movement state rejected");
    }
    private static void Lifetime()
    {
        var r = new Rig(); var outer = Enter(r.Move);
        Check(Tune(1,r.Move,K.Acceleration) == 1.5f,"admitted receiver active");
        Call("Finalizer",null,default(ArenaBotAgility.RestoreScope));
        Check(Tune(1,r.Move,K.Acceleration) == 1.5f,"skipped prefix does not clear outer scope");
        var inner = Enter(r.Move,0);
        Check(Tune(1,r.Move,K.Acceleration) == 1,"ineligible nested call is barrier");
        Exit(inner); Check(Tune(1,r.Move,K.Acceleration) == 1.5f,"outer scope restored");
        Check(Tune(1,r.Context,K.Acceleration) == 1,"foreign receiver cannot borrow scope");
        inner = Enter(r.Context); Check(Tune(1,r.Context,K.Acceleration) == 1.5f,"eligible nested consumer");
        var error = new Exception("native movement failed");
        Check(ReferenceEquals(Call("Finalizer",error,inner),error),"native exception retained");
        Check(Tune(1,r.Move,K.Acceleration) == 1.5f,"exception restores outer scope");
        Exit(outer); Check(Tune(1,r.Move,K.Acceleration) == 1,"no scope after completion");
        var changes = new Action<Rig>[] {
            x => x.Player.Physical = new(), x => x.Context.CurrentState = new(), x => x.Player.MovementContext = new(),
            x => x.Owner.GetPlayer = new(), _ => Settings.BotMovementEnabled.Value = false, _ => ArenaSession.IsActive = false,
            _ => ((RoundsMode)ArenaMatchService.Current.Mode).AssistScoringOpen = false, _ => ArenaRespawnDriver.StandDown = true };
        foreach (var change in changes) { r = new Rig(); outer = Enter(r.Move); change(r);
            Check(Tune(1,r.Move,K.Acceleration) == 1,"mid-call scope change rejected"); Exit(outer); }
        foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
        { r = new Rig(); outer = Enter(r.Move,invalid); Check(Tune(1,r.Move,K.Acceleration) == 1,"invalid clock not accelerated"); Exit(outer); }
    }
    private static void Il()
    {
        var expectedKinds = new Dictionary<string,K> {
            ["get_WalkInertia"] = K.Walk, ["get_MoveSideInertia"] = K.Side, ["get_MoveDiagonalInertia"] = K.Diagonal,
            ["get_Inertia"] = K.Inertia, ["Inertia"] = K.Inertia, ["SuddenChangesSmoothness"] = K.Acceleration,
            ["_directionSpeedAcceleration"] = K.Acceleration, ["PreSprintAcceleration"] = K.PreSprint,
            ["SprintAcceleration"] = K.Sprint };
        foreach (var pair in Harmony.Hooks)
        {
            var original = Harmony.Il(pair.Key); var code = pair.Value.Code;
            int expected = original.Count - 1;
            Check(code.Count == original.Count + expected*3,"all and only inventoried operands transformed");
            Check(code[0].opcode == OpCodes.Ldc_R4 && (float)code[0].operand == 42,"unrelated constant preserved");
            for (int i=1;i<code.Count;i+=4)
            {
                Check(code[i+1].opcode == OpCodes.Ldarg_0 && code[i+2].opcode == OpCodes.Ldc_I4
                    && code[i+3].opcode == OpCodes.Call && ((MethodInfo)code[i+3].operand).Name == "Tune", "scalar receiver kind stack contract");
                Check((K)(int)code[i+2].operand == expectedKinds[((MemberInfo)code[i].operand).Name], "each native read dispatches correct policy");
            }
        }
    }
    private static void Diagnostics()
    {
        var r = new Rig(); Plugin.ModLogger.Lines.Clear(); r.Apply(K.Walk,.2f); r.Apply(K.Acceleration,1);
        Check(Plugin.ModLogger.Lines.Count == 1 && Plugin.ModLogger.Lines[0].Contains("agility active"),"one activation log per world");
    }
    private static void Failures()
    {
        ArenaBotAgility.Shutdown(); Harmony.Calls = 0; Harmony.FailAt = 3; ArenaBotAgility.Enable();
        Check(Harmony.Hooks.Count == 0 && new Rig().Apply(K.Acceleration,1) == 1,"partial install rolled back");
        Harmony.FailAt = 0; Harmony.BadIl = true; ArenaBotAgility.Enable();
        Check(Harmony.Hooks.Count == 0,"changed native IL rejected");
        Harmony.BadIl = false; ArenaBotAgility.Enable(); Check(Harmony.Hooks.Count == 4,"rebind succeeds");
        ArenaBotAgility.Shutdown(); Check(new Rig().Apply(K.Acceleration,1) == 1,"shutdown restores native");
    }
}
