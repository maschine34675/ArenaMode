using System.Reflection;
using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string name)
    {
        _assertions++;
        if (!value) throw new Exception("ASSERT: " + name);
    }
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.0001f;
    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static object Invoke(string patch, string method, params object[] arguments)
    {
        var type = typeof(ArenaDoubleJump).GetNestedType(patch, BindingFlags.NonPublic);
        return type.GetMethod(method, PrivateStatic).Invoke(null, arguments);
    }
    private sealed class Fixture
    {
        internal readonly MovementContext Context = new();
        internal readonly Player Player;
        internal JumpPlayerState Jump;
        internal readonly MoveInputTranslator Translator;
        internal readonly GameWorld World;
        internal Fixture(bool enter = true, bool falling = false)
        {
            ArenaDoubleJump.Shutdown();
            ModulePatch.Registrations.Clear();
            ModulePatch.FailAfter = int.MaxValue;
            ArenaMode.Plugin.ModLogger.Debug.Clear();
            Settings.MovementEnabled.Value = true;
            Settings.DoubleJumpEnabled.Value = true;
            Settings.DoubleJumpHeightMult.Value = 0.8f;
            ArenaAirVault.Pending = false;
            ArenaInputBlocker.Blocked = ArenaRespawnDriver.IsRespawnInProgress = ArenaSpectatorService.IsActive = false;
            ArenaSession.IsActive = true;
            Jump = falling ? new FallDownPlayerState(Context) : new JumpPlayerState(Context);
            Player = new Player { MovementContext = Context, CurrentManagedState = Jump };
            Translator = new MoveInputTranslator(Player);
            World = new GameWorld { MainPlayer = Player };
            Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = World;
            Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = new AbstractGame();
            Singleton<GlobalConfiguration>.Instance = new GlobalConfiguration();
            Physics.gravity = new Vector3(0, -9.81f, 0);
            ArenaDoubleJump.Enable();
            if (enter) Enter();
        }
        internal void Enter(bool same = false) => Invoke("JumpEnterPatch", "Postfix", Jump, same);
        internal void Exit() => Invoke("JumpExitPatch", "Prefix", Jump);
        internal void Airborne() => Context.IsGrounded = false;
        internal void Press(Action native = null, ECommand command = ECommand.Jump)
        {
            object[] args = { Translator, command, null };
            Invoke("JumpCommandPatch", "Prefix", args);
            native?.Invoke();
            Invoke("JumpCommandPatch", "Postfix", Translator, args[2]);
        }
    }
    private static void Denied(Fixture f, string name)
    {
        float clock = f.Jump._stateTime;
        Vector3 velocity = f.Jump._currentLiftforce;
        Vector3 lift = f.Jump._liftForce;
        int debits = f.Player.Physical?.Debits ?? 0, noises = f.Context.Noises;
        f.Press();
        Check((f.Player.Physical?.Debits ?? 0) == debits && f.Context.Noises == noises
            && f.Jump._stateTime.Equals(clock) && Same(f.Jump._currentLiftforce, velocity)
            && f.Jump._liftForce.x.Equals(lift.x) && f.Jump._liftForce.y.Equals(lift.y) && f.Jump._liftForce.z.Equals(lift.z), name);
    }
    private static void SuccessfulImpulse()
    {
        foreach (float beforeY in new[] { 4f, -5f })
        {
            var f = new Fixture(); f.Airborne();
            f.Jump._currentLiftforce = new Vector3(0, beforeY, 0);
            var horizontal = f.Jump._initialNormalizedDirection;
            var gravity = Physics.gravity;
            f.Press();
            Check(f.Player.Physical.Debits == 1 && f.Context.Noises == 1, "ascending and descending jumps debit once and emit one noise");
            Check(Near(f.Jump._currentLiftforce.y, 8 * 1.25f * MathF.Sqrt(0.8f)), "height ratio uses square root once after existing lift and skill");
            Check(Near(f.Jump._liftForce.y, 8 * MathF.Sqrt(0.8f)), "native lift retained at configured second-jump strength");
            Check(f.Jump._stateTime == f.Jump._liftDelay, "extra impulse restarts native vertical clock");
            Check(f.Jump._potentialH == f.Context.TransformPosition.y, "extra impulse resets potential height to actual body height");
            Check(f.Jump._timeDescending == 0 && f.Jump._framesWithoutMotion == 0 && !f.Jump._hasLastHeight,
                "extra impulse clears descending and stale height history");
            Check(Same(horizontal, f.Jump._initialNormalizedDirection) && f.Jump._initialMagnitude == 4.5f
                && f.Jump._currentMagnitude == 3.5f && f.Jump._moveDirection.x == 0.5f && f.Jump._moveDirection.y == 0.9f,
                "extra impulse preserves horizontal motion");
            Check(Same(gravity, Physics.gravity) && f.Context.FreefallTime == 1.2f && f.Context._startFallingHeight == 23
                && f.Context.FallHeight == 4 && f.Jump._lastPlayerHeight == 20 && f.Jump._peakTime == 0.6f,
                "extra impulse preserves gravity and native fall damage fields");
            Check(f.Jump._skilledBoost == 1.25f && f.Jump._jumpState == JumpPlayerState.EJumpState.Jump,
                "extra impulse preserves skilled boost and native managed state");
            for (int i = 0; i < 4; i++) Denied(f, "spent credit denies repeated input");
        }
    }
    private static void IdentityAndFeatureGuards()
    {
        var cases = new (string Name, Action<Fixture> Change)[]
        {
            ("ordinary raid never receives extra impulse", f => ArenaSession.IsActive = false),
            ("master movement disabled denies", f => Settings.MovementEnabled.Value = false),
            ("double jump disabled denies", f => Settings.DoubleJumpEnabled.Value = false),
            ("respawn denies", f => ArenaRespawnDriver.IsRespawnInProgress = true),
            ("spectator denies", f => ArenaSpectatorService.IsActive = true),
            ("blocked UI denies", f => ArenaInputBlocker.Blocked = true),
            ("opened inventory denies", f => f.Player.IsInventoryOpened = true),
            ("AI body denies", f => f.Player.IsAI = true),
            ("foreign local flag denies", f => f.Player.IsYourPlayer = false),
            ("dead body denies", f => f.Player.HealthController.IsAlive = false),
            ("missing health denies", f => f.Player.HealthController = null),
            ("missing physical denies", f => f.Player.Physical = null),
            ("foreign movement context denies", f => f.Player.MovementContext = new MovementContext()),
            ("foreign translator body denies", f => f.Translator._controller = new Player { MovementContext = f.Context, CurrentManagedState = f.Jump }),
            ("missing translator body denies", f => f.Translator._controller = null),
            ("foreign managed state denies", f => f.Player.CurrentManagedState = new JumpPlayerState(f.Context)),
            ("missing game world denies", f => Singleton<GameWorld>.Instance = null),
            ("missing active game denies", f => Singleton<AbstractGame>.Instance = null),
            ("stale active game denies", f => Singleton<AbstractGame>.Instance = new AbstractGame()),
            ("stale active world denies", f => Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = f.Player }),
            ("missing main player denies", f => f.World.MainPlayer = null),
            ("missing vault configuration denies", f => Singleton<GlobalConfiguration>.Instance = null),
            ("grounded body denies", f => f.Context.IsGrounded = true),
            ("ground push phase denies", f => f.Jump._jumpState = JumpPlayerState.EJumpState.PushingFromTheGround),
            ("head bump phase denies", f => f.Jump._jumpState = JumpPlayerState.EJumpState.Bumbped),
            ("prone pose denies", f => f.Context.IsInPronePose = true),
            ("mounted pose denies", f => f.Context.IsInMountedState = true),
            ("native physical restriction or meds denies", f => f.Context.CanJump = false),
            ("native stamina restriction denies", f => f.Player.Physical.CanJump = false),
            ("vault input guard denies", f => f.Player._vaultingTiming = 0.3f),
            ("vault animation denies", f => f.Context.Vaulting = true),
            ("pending native mantle takes precedence", f => ArenaAirVault.Pending = true),
        };
        foreach (var test in cases)
        {
            var f = new Fixture(); f.Airborne(); test.Change(f); Denied(f, test.Name);
        }
        var changedBody = new Fixture(); changedBody.Airborne();
        var replacement = new Player { MovementContext = changedBody.Context, CurrentManagedState = changedBody.Jump };
        changedBody.World.MainPlayer = replacement;
        changedBody.Translator._controller = replacement;
        changedBody.Press();
        Check(replacement.Physical.Debits == 0 && changedBody.Context.Noises == 0, "credit cannot transfer to replacement body sharing state");
        var changedWorld = new Fixture(); changedWorld.Airborne();
        Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = new GameWorld { MainPlayer = changedWorld.Player };
        Denied(changedWorld, "credit cannot transfer into newly active world");
        var fall = new Fixture(falling: true); fall.Airborne(); Denied(fall, "FallDown subclass never grants rescue credit");
    }
    private static void NativeCommandAndCreditLifecycle()
    {
        var f = new Fixture(enter: false);
        f.Player.CurrentManagedState = new MovementState(f.Context);
        f.Press(() => { f.Player.CurrentManagedState = f.Jump; f.Enter(); f.Airborne(); });
        Check(f.Player.Physical.Debits == 0, "native ground press cannot become second press after state transition");
        f.Press(); Check(f.Player.Physical.Debits == 1, "following airborne press consumes fresh ground jump credit");
        f.Exit(); Denied(f, "exit removes credit");
        f.Enter(); Denied(f, "airborne fresh entry cannot create credit");
        f.Context.IsGrounded = true; f.Enter(same: true); f.Airborne();
        Denied(f, "same-state grounded reentry cannot create credit");
        f.Exit(); f.Context.IsGrounded = true; f.Enter(); f.Airborne();
        f.Press(); Check(f.Player.Physical.Debits == 2, "exit and fresh landed entry restore one credit");
        f.Enter(); Denied(f, "airborne reentry cannot refill spent credit");
        var command = new Fixture(); command.Airborne();
        command.Press(command: ECommand.ReloadWeapon);
        Check(command.Player.Physical.Debits == 0, "non-jump command leaves credit untouched");
        command.Press(); Check(command.Player.Physical.Debits == 1, "jump after other command still works");
        var grounded = new Fixture();
        grounded.Press(() => grounded.Airborne());
        Check(grounded.Player.Physical.Debits == 0, "grounded jump press is never captured even when original clears grounded");
        var changed = new Fixture(); changed.Airborne();
        changed.Press(() => changed.Player.CurrentManagedState = new MovementState(changed.Context));
        Check(changed.Player.Physical.Debits == 0, "original translator state change invalidates captured jump");
        var changedController = new Fixture(); changedController.Airborne();
        changedController.Press(() => changedController.Translator._controller = new Player());
        Check(changedController.Player.Physical.Debits == 0, "original translator controller change invalidates captured jump");
        var landed = new Fixture(); landed.Airborne(); landed.Press(() => landed.Context.IsGrounded = true);
        Check(landed.Player.Physical.Debits == 0, "landing between prefix and postfix denies impulse");
        var blocked = new Fixture(); blocked.Airborne(); blocked.Press(() => ArenaInputBlocker.Blocked = true);
        Check(blocked.Player.Physical.Debits == 0, "UI activation between prefix and postfix denies impulse");
        var reused = new Fixture(); reused.Airborne();
        reused.Press(() => { reused.Exit(); reused.Context.IsGrounded = true; reused.Enter(); reused.Airborne(); });
        Check(reused.Player.Physical.Debits == 0, "same-object exit and entry cannot replace captured credit");
        reused.Press(); Check(reused.Player.Physical.Debits == 1, "new input may consume legitimate replacement credit");
        foreach (var feature in new[] { Settings.DoubleJumpEnabled, Settings.MovementEnabled })
        {
            var toggled = new Fixture(); toggled.Airborne(); feature.Value = false; feature.Value = true;
            Denied(toggled, "toggle cycle cannot resurrect airborne credit");
        }
        var ended = new Fixture(); ended.Airborne(); RaidLifecycle.End();
        Denied(ended, "raid end clears credit even before singleton release");
    }
    private static void StrengthAndFiniteInputs()
    {
        foreach (float ratio in new[] { -3f, 0.25f, 1f, 1.5f, 9f })
        {
            var f = new Fixture(); f.Airborne(); Settings.DoubleJumpHeightMult.Value = ratio;
            f.Press();
            Check(Near(f.Jump._currentLiftforce.y, 10f * MathF.Sqrt(Math.Clamp(ratio, 0.25f, 1.5f))),
                "finite height ratio clamps to supported range");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var f = new Fixture(); f.Airborne(); Settings.DoubleJumpHeightMult.Value = invalid;
            Denied(f, "nonfinite height ratio denies");
            Settings.DoubleJumpHeightMult.Value = 1f; f.Press();
            Check(f.Player.Physical.Debits == 1, "invalid setting does not consume unspent valid credit");
            f = new Fixture(); f.Airborne(); f.Context.TransformPosition.y = invalid; Denied(f, "nonfinite body height denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftDelay = invalid; Denied(f, "nonfinite lift delay denies");
            f = new Fixture(); f.Airborne(); f.Jump._skilledBoost = invalid; Denied(f, "nonfinite skill velocity denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftForce.y = invalid; Denied(f, "nonfinite native lift denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftForce.x = invalid; Denied(f, "nonfinite native horizontal lift denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftForce.z = invalid; Denied(f, "nonfinite native depth lift denies");
        }
        foreach (float invalid in new[] { 0f, -1f })
        {
            var f = new Fixture(); f.Airborne(); f.Jump._liftForce.y = invalid; Denied(f, "nonpositive native lift denies");
            f = new Fixture(); f.Airborne(); f.Jump._skilledBoost = invalid; Denied(f, "nonpositive skill velocity denies");
        }
        var overflow = new Fixture(); overflow.Airborne(); overflow.Jump._liftForce.y = float.MaxValue;
        overflow.Jump._skilledBoost = float.MaxValue; Denied(overflow, "overflowing impulse denies");
    }
    private static void CallbackFailuresAndShutdown()
    {
        var f = new Fixture(); f.Airborne(); f.Player.Physical.ThrowOnJump = true;
        float clock = f.Jump._stateTime; f.Press();
        Check(f.Player.Physical.Debits == 1 && f.Context.Noises == 0 && f.Jump._stateTime == clock,
            "physical callback failure does not apply partial extra impulse");
        f.Player.Physical.ThrowOnJump = false; Denied(f, "physical callback exception denies retry debit");
        var noise = new Fixture(); noise.Airborne(); noise.Context.ThrowOnNoise = true; noise.Press();
        Check(noise.Player.Physical.Debits == 1 && noise.Context.Noises == 1, "noise failure follows one successful debit");
        noise.Context.ThrowOnNoise = false; Denied(noise, "noise callback exception denies retry debit");
        var reentrant = new Fixture(); reentrant.Airborne(); reentrant.Player.Physical.OnDebit = () => reentrant.Press();
        reentrant.Press(); Check(reentrant.Player.Physical.Debits == 1 && reentrant.Context.Noises == 1,
            "reentrant native callback cannot spend a second credit");
        var exhausted = new Fixture(); exhausted.Airborne();
        exhausted.Player.Physical.OnDebit = () => exhausted.Player.Physical.CanJump = false;
        exhausted.Press(); Check(exhausted.Player.Physical.Debits == 1 && exhausted.Context.Noises == 1
            && exhausted.Jump._stateTime == exhausted.Jump._liftDelay,
            "paying last available stamina still applies purchased impulse");
        var callbacks = new (string Name, Action<Fixture> Change)[]
        {
            ("body swap during debit rejects impulse", x => x.World.MainPlayer = new Player { MovementContext = x.Context, CurrentManagedState = x.Jump }),
            ("same-object reentry during debit rejects impulse", x => { x.Exit(); x.Context.IsGrounded = true; x.Enter(); x.Airborne(); }),
            ("feature change during debit rejects impulse", x => Settings.DoubleJumpEnabled.Value = false),
            ("raid end during debit rejects impulse", x => RaidLifecycle.End()),
            ("landing during debit rejects impulse", x => x.Context.IsGrounded = true),
            ("inventory during debit rejects impulse", x => x.Player.IsInventoryOpened = true),
            ("shutdown during debit rejects impulse", x => ArenaDoubleJump.Shutdown()),
        };
        foreach (var callback in callbacks)
        {
            var interrupted = new Fixture(); interrupted.Airborne();
            float before = interrupted.Jump._stateTime;
            interrupted.Player.Physical.OnDebit = () => callback.Change(interrupted);
            interrupted.Press();
            Check(interrupted.Player.Physical.Debits == 1 && interrupted.Context.Noises == 0
                && interrupted.Jump._stateTime == before, callback.Name);
        }
        var shutdown = new Fixture(); shutdown.Airborne(); ArenaDoubleJump.Shutdown(); Denied(shutdown, "shutdown immediately denies existing credit");
        Check(Settings.DoubleJumpEnabled.Subscribers == 0 && Settings.MovementEnabled.Subscribers == 0 && RaidLifecycle.Subscribers == 0,
            "shutdown unsubscribes setting and raid callbacks");
        ArenaDoubleJump.Enable(); Denied(shutdown, "re-enable does not restore stale credit");
        shutdown.Context.IsGrounded = true; shutdown.Enter(); shutdown.Airborne(); shutdown.Press();
        Check(shutdown.Player.Physical.Debits == 1, "new grounded entry after re-enable works");
        var failure = new Fixture(enter: false); ArenaDoubleJump.Shutdown();
        ModulePatch.Registrations.Clear(); ModulePatch.FailAfter = 1; ArenaDoubleJump.Enable();
        failure.Enter(); failure.Airborne(); Denied(failure, "partial patch installation remains disabled");
    }
    private static void Metadata()
    {
        _ = new Fixture();
        Check(ModulePatch.Registrations.Count == 3, "installs exactly three movement patches");
        ArenaDoubleJump.Enable();
        Check(ModulePatch.Registrations.Count == 3 && Settings.DoubleJumpEnabled.Subscribers == 1
            && Settings.MovementEnabled.Subscribers == 1 && RaidLifecycle.Subscribers == 1,
            "repeated enable does not duplicate patch or event registration");
        Check(ModulePatch.Registrations.Any(p => p.Target.DeclaringType == typeof(JumpPlayerState) && p.Target.Name == "Enter"), "hooks actual Jump Enter target");
        Check(ModulePatch.Registrations.Any(p => p.Target.DeclaringType == typeof(JumpPlayerState) && p.Target.Name == "Exit"), "hooks actual Jump Exit target");
        Check(ModulePatch.Registrations.Any(p => p.Target.DeclaringType == typeof(MoveInputTranslator) && p.Target.Name == "TranslateCommand"), "hooks native command translator target");
        var translate = (MethodInfo)ModulePatch.Registrations.Single(p => p.Target.Name == "TranslateCommand").Target;
        Check(translate.DeclaringType.FullName == "EFT.MoveInputTranslator"
            && translate.ReturnType == typeof(InputNode.ETranslateResult)
            && translate.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ECommand) }),
            "translator target matches native namespace return and parameter shape");
        var exit = (MethodInfo)ModulePatch.Registrations.Single(p => p.Target.Name == "Exit").Target;
        Check(exit.ReturnType == typeof(void) && exit.GetParameters().Length == 1
            && exit.GetParameters()[0].ParameterType == typeof(bool) && exit.GetParameters()[0].Name == "toSameState",
            "exit target matches native bool transition parameter");
        var enter = (MethodInfo)ModulePatch.Registrations.Single(p => p.Target.Name == "Enter").Target;
        Check(enter.ReturnType == typeof(void) && enter.GetParameters().Length == 1
            && enter.GetParameters()[0].ParameterType == typeof(bool) && enter.GetParameters()[0].Name == "isFromSameState",
            "enter target matches native bool transition parameter");
        var command = typeof(ArenaDoubleJump).GetNestedType("JumpCommandPatch", BindingFlags.NonPublic);
        Check(command.GetMethod("Prefix", PrivateStatic).ReturnType == typeof(void), "prefix cannot suppress original native command");
        Check(command.GetMethod("Prefix", PrivateStatic).GetCustomAttribute<PatchPrefixAttribute>() != null
            && command.GetMethod("Postfix", PrivateStatic).GetCustomAttribute<PatchPostfixAttribute>() != null,
            "command callbacks retain Harmony prefix and postfix metadata");
        ArenaSession.IsActive = false;
        Check(!ArenaSession.IsExactActiveRaid(ArenaSession.ActiveGame, ArenaSession.ActiveWorld),
            "session contract double includes actual redundant active gate");
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        Check(!ArenaSession.IsExactActiveRaid(null, null), "session contract double rejects null identity pair");
    }
    public static int Main()
    {
        try
        {
            Metadata(); SuccessfulImpulse(); IdentityAndFeatureGuards(); NativeCommandAndCreditLifecycle();
            StrengthAndFiniteInputs(); CallbackFailuresAndShutdown();
            Console.WriteLine($"ARENA_DOUBLE_JUMP_CONTRACTS_PASSED ({_assertions} assertions; production source with contract doubles, no live Unity/Harmony claim)");
            return 0;
        }
        catch (Exception exception) { Console.WriteLine(exception); return 1; }
    }
}
