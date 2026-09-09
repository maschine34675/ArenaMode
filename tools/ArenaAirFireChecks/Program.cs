using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using HarmonyLib;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("ASSERT: " + message); }
    sealed class Fixture
    {
        public Player Player = new();
        public Player.FirearmController Firearm;
        public LocalGame Game;
        public GameWorld World;
        public JumpPlayerState Jump;
        public Fixture()
        {
            ArenaSession.IsActive = Settings.MovementEnabled.Value = Settings.AirHipFireEnabled.Value = true;
            ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = ArenaSpectatorService.IsActive = false;
            ArenaInputBlocker.Blocked = ArenaAirVault.Pending = false;
            Firearm = new() { _player = Player }; Player.HandsController = Firearm;
            Jump = new(Player.MovementContext) { Name = EPlayerState.Jump };
            Player.CurrentManagedState = Player.CurrentState = Jump;
            Game = new() { LocalPlayer = Player }; World = new() { MainPlayer = Player };
            ArenaSession.Game = Singleton<AbstractGame>.Instance = Game;
            ArenaSession.World = Singleton<GameWorld>.Instance = World;
        }
        public bool Allows() => ArenaAirFire.SuitableForAirFire(Firearm);
    }
    static void Scope()
    {
        foreach (var test in new (string, Action<Fixture>)[] {
            ("ordinary raid", f => ArenaSession.IsActive=false),
            ("master off", f => Settings.MovementEnabled.Value=false),
            ("AI", f => f.Player.IsAI=true), ("remote", f => f.Player.IsYourPlayer=false),
            ("dead", f => f.Player.HealthController.IsAlive=false), ("missing health", f => f.Player.HealthController=null),
            ("missing world", f => Singleton<GameWorld>.Instance=null), ("missing game", f => Singleton<AbstractGame>.Instance=null),
            ("foreign game", f => Singleton<AbstractGame>.Instance=new LocalGame()),
            ("foreign world", f => Singleton<GameWorld>.Instance=new GameWorld { MainPlayer=f.Player }),
            ("foreign current body", f => f.World.MainPlayer=new Player()),
            ("owner handover", f => f.Game.LocalPlayer=new Player()),
            ("stopping", f => f.Game.Status=GameStatus.Stopping), ("stopped", f => f.Game.Status=GameStatus.Stopped),
            ("respawning", f => ArenaRespawnDriver.IsRespawnInProgress=true), ("stand down", f => ArenaRespawnDriver.StandDown=true),
            ("spectating", f => ArenaSpectatorService.IsActive=true), ("inventory", f => f.Player.IsInventoryOpened=true),
            ("blocked UI", f => ArenaInputBlocker.Blocked=true), ("missing context", f => f.Player.MovementContext=null)
        }) {
            var f = new Fixture(); test.Item2(f);
            Check(!ArenaMovementScope.IsLocal(f.Player, true), "scope rejects " + test.Item1);
            Check(!f.Allows(), "fire rejects " + test.Item1);
            f.Firearm.NativeSuitable=true;
            Check(f.Allows(), "native true remains unchanged: " + test.Item1);
        }
        var yes = new Fixture();
        Check(ArenaMovementScope.TryGetPlayer(yes.Player.MovementContext, out var local) && ReferenceEquals(local,yes.Player), "current context resolves exact body");
        Check(!ArenaMovementScope.TryGetPlayer(new MovementContext(), out _), "foreign context rejected");
        yes.Player.IsInventoryOpened=true; ArenaInputBlocker.Blocked=true;
        Check(ArenaMovementScope.IsLocal(yes.Player) && !ArenaMovementScope.IsLocal(yes.Player,true), "input-only restrictions do not suppress passive handling cleanup");
    }
    static void AirStates()
    {
        foreach (var test in new (string,Action<Fixture>)[] {
            ("feature off", f=>Settings.AirHipFireEnabled.Value=false),
            ("aiming", f=>f.Firearm.IsAiming=true), ("not current hands", f=>f.Player.HandsController=new Player.FirearmController()),
            ("not jump", f=>f.Player.CurrentManagedState=new MovementState()),
            ("derived state", f=>f.Player.CurrentManagedState=new DerivedJump(f.Player.MovementContext)),
            ("foreign jump context", f=>f.Jump.MovementContext=new MovementContext()),
            ("falling state", f=>f.Player.CurrentState=new MovementState { Name=EPlayerState.FallDown }),
            ("animator transition", f=>f.Player.CurrentState=new MovementState { Name=EPlayerState.Transition }),
            ("grounded", f=>f.Player.MovementContext.IsGrounded=true),
            ("pushing", f=>f.Jump._jumpState=JumpPlayerState.EJumpState.PushingFromTheGround),
            ("bumped", f=>f.Jump._jumpState=JumpPlayerState.EJumpState.Bumbped),
            ("prone", f=>f.Player.MovementContext.IsInPronePose=true),
            ("mounted", f=>f.Player.MovementContext.IsInMountedState=true),
            ("firearms blocked", f=>f.Player.MovementContext.BlockFirearms=true),
            ("vault animation", f=>f.Player.MovementContext.Vaulting=true),
            ("pending mantle", f=>ArenaAirVault.Pending=true)
        }) { var f=new Fixture(); test.Item2(f); Check(!f.Allows(), "air rejects " + test.Item1); }
        var good=new Fixture(); Check(good.Allows(), "genuine local jump permits hip fire");
        Check(good.Firearm.Reads==1, "native predicate evaluated once");
        good.Jump.Name=EPlayerState.Idle; good.Firearm.NativeSuitable=true;
        Check(good.Allows(), "ordinary suitable state remains native");
        var nonfire=new Player.ItemHandsController { _player=good.Player };
        good.Player.HandsController=nonfire;
        Check(!ArenaAirFire.SuitableForAirFire(nonfire), "other hand types never gain permission");
    }
    static List<CodeInstruction> Rewrite(List<CodeInstruction> input)
        => ((IEnumerable<CodeInstruction>)typeof(ArenaAirFire).GetNestedType("TriggerStatePatch",BindingFlags.NonPublic)
            .GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input})).ToList();
    static void Transpiler()
    {
        var get=AccessTools.PropertyGetter(typeof(Player.ItemHandsController),"SuitableForHandInput");
        var label=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel();
        var source=new List<CodeInstruction>{new(OpCodes.Ldarg_0),new(OpCodes.Callvirt,get),new(OpCodes.Ret)};
        source[1].labels.Add(label);
        var output=Rewrite(source);
        Check(source[1].operand.Equals(get), "transpiler leaves incoming instructions intact");
        Check(output.Count==source.Count && output[0].opcode==OpCodes.Ldarg_0 && output[2].opcode==OpCodes.Ret, "only intended predicate changes");
        Check(output[1].opcode==OpCodes.Call && ((MethodInfo)output[1].operand).Name=="SuitableForAirFire" && output[1].labels.SequenceEqual(source[1].labels), "call replacement preserves branch target and static signature");
        var dynamic=new DynamicMethod("airPredicate",typeof(bool),new[]{typeof(Player.ItemHandsController)},typeof(Program),true);
        var il=dynamic.GetILGenerator(); il.Emit(output[0].opcode); il.Emit(output[1].opcode,(MethodInfo)output[1].operand); il.Emit(output[2].opcode);
        var actual=(Func<Player.ItemHandsController,bool>)dynamic.CreateDelegate(typeof(Func<Player.ItemHandsController,bool>));
        var f=new Fixture(); Check(actual(f.Firearm), "rewritten trigger predicate executes production gate");
        f.Player.MovementContext.IsGrounded=true; Check(!actual(f.Firearm), "rewritten predicate retains excluded context");
        foreach(int count in new[]{0,2}) {
            var drift=Enumerable.Range(0,count).Select(_=>new CodeInstruction(OpCodes.Callvirt,get)).ToList();
            Check(Rewrite(drift).All(x=>x.Calls(get)), "ambiguous or missing native call fails open " + count);
        }
        Check(Rewrite(output).All(x=>x.opcode!=OpCodes.Callvirt), "another rewrite never compounds an existing patch");
    }
    static void Main()
    {
        ArenaAirFire.Enable();
        if (SPT.Reflection.Patching.ModulePatch.Hooks.Count == 0)
            throw new Exception(string.Join("; ", ArenaMode.Plugin.ModLogger.Warnings));
        Check(SPT.Reflection.Patching.ModulePatch.Hooks.Single().DeclaringType==typeof(Player.FirearmController.Idling), "only firearm trigger operation patched");
        Scope(); AirStates(); Transpiler();
        Console.WriteLine($"ARENA_AIR_FIRE_PRODUCTION_PASSED ({checks} assertions)");
    }
}
