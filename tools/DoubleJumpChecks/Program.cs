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
using HarmonyLib;
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
        => typeof(ArenaDoubleJump).GetNestedType(patch, BindingFlags.NonPublic)
            .GetMethod(method, PrivateStatic).Invoke(null, arguments);

    private sealed class Fixture
    {
        internal readonly MovementContext Context = new();
        internal readonly Player Player;
        internal JumpPlayerState Jump;
        internal readonly MoveInputTranslator Translator;
        internal readonly GameWorld World;
        internal Fixture(bool falling = false, bool observe = true)
        {
            ArenaDoubleJump.Shutdown();
            ModulePatch.Registrations.Clear(); ModulePatch.FailAfter = int.MaxValue;
            Settings.MovementEnabled.Value = Settings.DoubleJumpEnabled.Value = true;
            Settings.DoubleJumpHeightMult.Value = 0.8f; Settings.JumpHeightMult.Value = 1;
            ArenaAirVault.Pending = false;
            ArenaInputBlocker.Blocked = ArenaRespawnDriver.IsRespawnInProgress = ArenaSpectatorService.IsActive = false;
            ArenaSession.IsActive = true;
            Jump = falling ? new FallDown(Context) { _liftForce = Vector3.zero } : new JumpPlayerState(Context);
            Player = new Player { MovementContext = Context, CurrentManagedState = Jump };
            Translator = new MoveInputTranslator(Player);
            World = new GameWorld { MainPlayer = Player };
            Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = World;
            Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = new LocalGame { LocalPlayer = Player };
            ArenaMode.Pickups.ArenaPowerups.State = new(); ArenaMode.Pickups.ArenaPowerups.Owner = Player;
            Time.time = 10; ArenaRespawnDriver.StandDown = false;
            Singleton<GlobalConfiguration>.Instance = new GlobalConfiguration();
            EFTHardSettings.Instance = new(); Time.realtimeSinceStartup = 10;
            Physics.gravity = new Vector3(0, -9.81f, 0);
            ArenaDoubleJump.Enable();
            if (observe) Tick();
        }
        internal void Tick() => Invoke("LandingPatch", "Postfix", Context);
        internal void Airborne() { Context.IsGrounded = false; Player.CharacterController.isGrounded = false; }
        internal void Land()
        {
            Context.IsGrounded = Player.CharacterController.isGrounded = true;
            Jump._currentLiftforce.y = -1; Jump._jumpState = JumpPlayerState.EJumpState.Jump;
            Tick();
        }
        internal void Entry(JumpPlayerState state, bool nativeDebit = false, Action duringNative = null)
        {
            Jump = state; Player.CurrentManagedState = state;
            string patch = state is FallDown ? "FallEnterPatch" : "JumpEnterPatch";
            object[] args = { state, null };
            Invoke(patch, "Prefix", args);
            if (nativeDebit) Player.Physical.OnJump(true);
            duringNative?.Invoke();
            ArenaJumpBoots.ApplyEntry(state);
            Invoke(patch, "Postfix", state, args[1]);
        }
        internal void EarlyEdge(int kind = 0)
        {
            Player.CurrentManagedState = kind switch { 1 => new MovePlayerState(Context), 2 => new SprintPlayerState(Context), _ => new IdlePlayerState(Context) };
            Airborne(); Context.TransformPosition.y -= 0.01f;
        }
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
        Vector3 velocity = f.Jump._currentLiftforce, lift = f.Jump._liftForce;
        int debits = f.Player.Physical?.Debits ?? 0, noises = f.Context.Noises, requests = f.Context.NativeRequests;
        f.Press();
        Check((f.Player.Physical?.Debits ?? 0) == debits && f.Context.Noises == noises && f.Context.NativeRequests == requests
            && f.Jump._stateTime.Equals(clock) && Same(f.Jump._currentLiftforce, velocity)
            && f.Jump._liftForce.x.Equals(lift.x) && f.Jump._liftForce.y.Equals(lift.y) && f.Jump._liftForce.z.Equals(lift.z), name);
    }
    private static void SuccessfulImpulse()
    {
        foreach (bool falling in new[] { false, true })
        foreach (float beforeY in new[] { 4f, -5f })
        {
            var f = new Fixture(falling); f.Airborne();
            f.Jump._currentLiftforce.y = beforeY;
            var horizontal = f.Jump._initialNormalizedDirection; var gravity = Physics.gravity;
            f.Press();
            Check(f.Player.Physical.Debits == 1 && f.Context.Noises == 1, falling
                ? "walking off edge receives one deliberate impulse and debit" : "ascending and descending jumps debit once and emit one noise");
            Check(Near(f.Jump._currentLiftforce.y, 10 * MathF.Sqrt(0.8f)), "height ratio applies once to native lift and skill");
            Check(f.Jump._stateTime == f.Jump._liftDelay, "extra impulse restarts native vertical clock");
            Check(f.Jump._potentialH == f.Context.TransformPosition.y && f.Jump._timeDescending == 0
                && f.Jump._framesWithoutMotion == 0 && !f.Jump._hasLastHeight, "impulse clears stale native height and descending history");
            Check(Same(horizontal, f.Jump._initialNormalizedDirection) && f.Jump._initialMagnitude == 4.5f
                && f.Jump._currentMagnitude == 3.5f && f.Jump._moveDirection.x == 0.5f && f.Jump._moveDirection.y == 0.9f,
                "extra impulse preserves horizontal motion");
            Check(Same(gravity, Physics.gravity) && f.Context.FreefallTime == 1.2f && f.Context._startFallingHeight == 23
                && f.Context.FallHeight == 4 && f.Jump._lastPlayerHeight == 20 && f.Jump._peakTime == 0.6f,
                "extra impulse preserves gravity and native fall damage fields");
            for (int i = 0; i < 4; i++) Denied(f, "spent credit denies repeated input");
        }
        var fresh = new Fixture(falling: true, observe: false); fresh.Airborne(); fresh.Press();
        Check(fresh.Player.Physical.Debits == 1 && fresh.Jump._currentLiftforce.y > 0,
            "fresh falling body needs no preceding observed ground jump");
        foreach (float multiplier in new[] { 0.8f, 1.5f })
        {
            var retained = new Fixture(); retained.Airborne(); Settings.DoubleJumpHeightMult.Value = multiplier;
            retained.Press(); float first = retained.Jump._currentLiftforce.y;
            for (int episode = 0; episode < 3; episode++)
            {
                retained.Land(); retained.Airborne(); retained.Press();
                Check(retained.Player.Physical.Debits == episode + 2, "genuine landing restores the reused jump credit");
                Check(Near(retained.Jump._currentLiftforce.y, first), "reused native jump state cannot compound height across landings");
            }
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
        foreach (bool falling in new[] { false, true })
        foreach (var test in cases)
        {
            var f = new Fixture(falling); f.Airborne(); test.Change(f); Denied(f, test.Name);
        }
        var derived = new Fixture(); derived.Jump = new OtherJumpState(derived.Context);
        derived.Player.CurrentManagedState = derived.Jump; derived.Airborne(); Denied(derived, "unknown jump subclasses remain excluded");
        var grounded = new Fixture(); grounded.Press(() => grounded.Airborne());
        Check(grounded.Player.Physical.Debits == 0, "native grounded press cannot become an extra press");
        var other = new Fixture(); other.Airborne(); other.Press(command: ECommand.ReloadWeapon);
        Check(other.Player.Physical.Debits == 0, "unrelated command leaves credit untouched");
        other.Press(); Check(other.Player.Physical.Debits == 1, "configured jump after unrelated input still works");
    }
    private static void EpisodeLifecycle()
    {
        var f = new Fixture(); f.Airborne(); f.Press();
        for (int i = 0; i < 8; i++)
        {
            f.Entry(i % 2 == 0 ? new FallDown(f.Context) { _liftForce = Vector3.zero } : new JumpPlayerState(f.Context));
            Denied(f, "Jump FallDown transitions cannot replenish airborne credit");
            f.Entry(f.Jump); Denied(f, "same state reentry cannot replenish airborne credit");
        }
        f.Context.IsGrounded = true; f.Jump._currentLiftforce.y = -1; f.Tick(); f.Airborne();
        Denied(f, "ground proximity ray alone cannot replenish credit");
        f.Context.IsGrounded = f.Player.CharacterController.isGrounded = true; f.Jump._currentLiftforce.y = 3; f.Tick(); f.Airborne();
        Denied(f, "upward native controller contact cannot replenish credit");
        f.Context.IsGrounded = f.Player.CharacterController.isGrounded = true;
        f.Jump._currentLiftforce.y = 0; f.Jump._jumpState = JumpPlayerState.EJumpState.PushingFromTheGround;
        f.Tick(); f.Airborne(); f.Jump._jumpState = JumpPlayerState.EJumpState.Jump;
        Denied(f, "native push phase contact cannot replenish credit");
        f.Land(); f.Airborne(); f.Press();
        Check(f.Player.Physical.Debits == 2, "genuine landing restores exactly one airborne credit");
        Denied(f, "next episode also remains one use");
        foreach (var setting in new[] { Settings.DoubleJumpEnabled, Settings.MovementEnabled })
        {
            var toggled = new Fixture(falling: true); toggled.Airborne(); toggled.Press();
            ArenaInputBlocker.Blocked = true; setting.Value = false; setting.Value = true; ArenaInputBlocker.Blocked = false;
            Denied(toggled, "settings toggle cannot restore spent credit while UI blocked");
            toggled.Land(); toggled.Airborne(); toggled.Press();
            Check(toggled.Player.Physical.Debits == 2, "landing restores feature after airborne toggle");
            var unspent = new Fixture(falling: true); unspent.Airborne(); setting.Value = false; setting.Value = true;
            Denied(unspent, "settings toggle invalidates outstanding unspent credit");
            var grounded = new Fixture(); grounded.Player.CharacterController.isGrounded = true;
            setting.Value = false; setting.Value = true; grounded.Airborne(); grounded.Press();
            Check(grounded.Player.Physical.Debits == 1, "grounded setting toggle preserves next airborne credit");
        }
        var body = new Fixture(); body.Airborne(); body.Press();
        var replacement = new Player { MovementContext = body.Context, CurrentManagedState = new FallDown(body.Context) { _liftForce = Vector3.zero } };
        body.World.MainPlayer = body.Translator._controller = replacement; body.Press();
        Check(replacement.Physical.Debits == 1, "fresh replacement falling body receives independent credit");
        var world = new Fixture(falling: true); world.Airborne(); world.Press();
        Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = new GameWorld { MainPlayer = world.Player };
        world.Press(); Check(world.Player.Physical.Debits == 2, "new exact session world receives independent credit");
        RaidLifecycle.End(); Denied(world, "raid end denies even before singleton teardown");
    }
    private static void EarlyLedgeNativeTransition()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool falling in new[] { false, true })
        {
            var f = new Fixture(); f.EarlyEdge(kind); f.Press();
            Check(f.Context.NativeRequests == 1 && f.Player.Physical.Debits == 0 && f.Context.Noises == 1,
                "centimetre edge press requests native transition without premature debit");
            Denied(f, "pending native request cannot be repeated");
            var entered = falling ? new FallDown(f.Context) { _liftForce = Vector3.zero }
                : new JumpPlayerState(f.Context) { _jumpState = JumpPlayerState.EJumpState.PushingFromTheGround };
            entered._currentLiftforce = Vector3.zero;
            f.Entry(entered, nativeDebit: true);
            Check(f.Player.Physical.Debits == 1 && f.Context.Noises == 1 && f.Jump._currentLiftforce.y > 0,
                "successful native ledge entry owns one debit and one noise");
            Check(f.Context.PlayerAnimator.Jump == !falling,
                falling ? "FallDown completion releases only the owned native jump signal" : "native Jump completion retains its native jump signal");
            Check(Near(f.Jump._currentLiftforce.y, 10 * MathF.Sqrt(0.8f)), "native ledge entry applies configured impulse once");
            Denied(f, "native ledge completion cannot grant another extra jump");
            f.Entry(new FallDown(f.Context) { _liftForce = Vector3.zero });
            Denied(f, "native ledge credit survives later FallDown entry");
        }
        var failed = new Fixture(); failed.EarlyEdge(); failed.Press();
        Time.realtimeSinceStartup += 1; failed.Tick();
        Denied(failed, "failed native transition cannot retry indefinitely");
        Check(!failed.Context.PlayerAnimator.Jump, "failed native transition expiry releases owned jump signal");
        Check(failed.Player.Physical.Debits == 0, "failed native transition incurs no mod debit");
        var expiredFall = new FallDown(failed.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        failed.Entry(expiredFall, nativeDebit: true);
        Check(expiredFall._liftForce.y == 0 && failed.Player.Physical.Debits == 1, "expired request cannot lift later native fall");
        var expired = new Fixture(); expired.EarlyEdge(); expired.Press(); Time.realtimeSinceStartup += 1;
        var lateFall = new FallDown(expired.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        expired.Entry(lateFall, nativeDebit: true);
        Check(lateFall._liftForce.y == 0, "expired request denied even before landing observer ticks");
        var blocked = new Fixture(); blocked.EarlyEdge(); ArenaAirVault.Pending = true; Denied(blocked, "early edge preserves mantle precedence");
        ArenaAirVault.Pending = false; blocked.Context.PoseLevel = 0.6f; Denied(blocked, "early edge preserves native low pose restriction");
        var ordinary = new Fixture(); ordinary.EarlyEdge(); ArenaSession.IsActive = false; Denied(ordinary, "ordinary early edge never requests animator jump");
        var cancelled = new Fixture(); cancelled.EarlyEdge(); cancelled.Press(); Settings.DoubleJumpEnabled.Value = false; Settings.DoubleJumpEnabled.Value = true;
        var cancelledFall = new FallDown(cancelled.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        cancelled.Entry(cancelledFall, nativeDebit: true);
        Check(cancelledFall._liftForce.y == 0, "settings cancellation cannot revive pending native lift");
        Check(!cancelled.Context.PlayerAnimator.Jump, "settings cancellation releases owned native jump signal");
        Denied(cancelled, "cancelled request retains spent episode credit");
        var landed = new Fixture(); landed.EarlyEdge(); landed.Press(); landed.Land(); landed.Airborne();
        var laterFall = new FallDown(landed.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        landed.Entry(laterFall, nativeDebit: true);
        Check(laterFall._liftForce.y == 0, "landing cancels pending native lift");
        landed.Press(); Check(landed.Player.Physical.Debits == 2, "fresh input works after pending request landing");
        var preexisting = new Fixture(); preexisting.EarlyEdge(); preexisting.Context.PlayerAnimator.Jump = true;
        Denied(preexisting, "preexisting native jump signal is never borrowed");
        preexisting.Tick(); Settings.DoubleJumpEnabled.Value = false;
        Check(preexisting.Context.PlayerAnimator.Jump, "unowned native jump signal survives cancellation");
        var noiseCancel = new Fixture(); noiseCancel.EarlyEdge();
        noiseCancel.Context.OnNoise = () => Settings.DoubleJumpEnabled.Value = false; noiseCancel.Press();
        Check(!noiseCancel.Context.PlayerAnimator.Jump && noiseCancel.Player.Physical.Debits == 0,
            "noise callback cancellation releases signal raised later by TryJump");
        var synchronous = new Fixture(); synchronous.EarlyEdge();
        synchronous.Context.OnNoise = () => synchronous.Entry(new FallDown(synchronous.Context)
            { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero }, nativeDebit: true);
        synchronous.Press();
        Check(!synchronous.Context.PlayerAnimator.Jump && synchronous.Player.Physical.Debits == 1 && synchronous.Jump._liftForce.y > 0,
            "synchronous FallDown completion cannot leave later native jump signal");
        var foreign = new Fixture(); foreign.EarlyEdge(); foreign.Press();
        foreign.World.MainPlayer = new Player(); Settings.DoubleJumpEnabled.Value = false;
        Check(foreign.Context.PlayerAnimator.Jump, "owned signal cleanup never writes a replaced body");
        foreach (int kind in new[] { 1, 2 })
        {
            var moving = new Fixture(); moving.EarlyEdge(); moving.Press(); moving.EarlyEdge(kind);
            Time.realtimeSinceStartup += 1; moving.Tick();
            Check(!moving.Context.PlayerAnimator.Jump, "owned pending signal expires after Idle to Move Sprint transition");
        }
        var handoff = new Fixture(); handoff.EarlyEdge(); handoff.Press();
        ArenaInputBlocker.Blocked = true;
        handoff.Context.IsGrounded = handoff.Player.CharacterController.isGrounded = true; handoff.Tick();
        ArenaInputBlocker.Blocked = false; handoff.Press(() => handoff.Context.TryJump()); handoff.Tick();
        Check(handoff.Context.PlayerAnimator.Jump, "grounded native command takes signal ownership before delayed landing cleanup");
    }
    private static void CaptureAndDebitOwnership()
    {
        var callbacks = new (string Name, Action<Fixture> Change)[]
        {
            ("body swap during debit rejects impulse", f => f.World.MainPlayer = new Player { MovementContext = f.Context, CurrentManagedState = f.Jump }),
            ("session replacement during debit rejects impulse", f => Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = new GameWorld { MainPlayer = f.Player }),
            ("state change during debit rejects impulse", f => f.Player.CurrentManagedState = new MovementState(f.Context)),
            ("same object reentry during debit rejects impulse", f => f.Entry(f.Jump)),
            ("feature change during debit rejects impulse", f => Settings.DoubleJumpEnabled.Value = false),
            ("raid end during debit rejects impulse", f => RaidLifecycle.End()),
            ("landing during debit rejects impulse", f => f.Context.IsGrounded = true),
            ("inventory during debit rejects impulse", f => f.Player.IsInventoryOpened = true),
            ("shutdown during debit rejects impulse", f => ArenaDoubleJump.Shutdown()),
        };
        foreach (var callback in callbacks)
        {
            var f = new Fixture(); f.Airborne(); float before = f.Jump._stateTime;
            f.Player.Physical.OnDebit = () => callback.Change(f); f.Press();
            Check(f.Player.Physical.Debits == 1 && f.Context.Noises == 0 && f.Jump._stateTime == before, callback.Name);
        }
        var exhaust = new Fixture(); exhaust.Airborne(); exhaust.Player.Physical.OnDebit = () => exhaust.Player.Physical.CanJump = false;
        exhaust.Press(); Check(exhaust.Player.Physical.Debits == 1 && exhaust.Context.Noises == 1,
            "paying final stamina still completes accepted jump");
        var failure = new Fixture(); failure.Airborne(); failure.Player.Physical.ThrowOnJump = true; failure.Press();
        Check(failure.Player.Physical.Debits == 1 && failure.Context.Noises == 0, "failed debit grants no impulse");
        failure.Player.Physical.ThrowOnJump = false; Denied(failure, "failed debit cannot repeat consumption");
        var captured = new Fixture(); captured.Airborne();
        captured.Press(() => { captured.Land(); captured.Airborne(); });
        Check(captured.Player.Physical.Debits == 0, "landing replacement invalidates captured credit");
        captured.Press(); Check(captured.Player.Physical.Debits == 1, "fresh command may use legitimate landed replacement");
        var reentered = new Fixture(); reentered.Airborne(); reentered.Press(() => reentered.Entry(reentered.Jump));
        Check(reentered.Player.Physical.Debits == 0, "same object reentry invalidates captured command");
        var pendingSwap = new Fixture(); pendingSwap.EarlyEdge(); pendingSwap.Press();
        pendingSwap.Player.Physical.OnDebit = () => pendingSwap.World.MainPlayer = new Player { MovementContext = pendingSwap.Context, CurrentManagedState = pendingSwap.Jump };
        var pendingFall = new FallDown(pendingSwap.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        pendingSwap.Entry(pendingFall, nativeDebit: true);
        Check(pendingFall._liftForce.y == 0 && pendingSwap.Player.Physical.Debits == 1,
            "native entry debit body swap rejects pending lift");
        var pendingReentry = new Fixture(); pendingReentry.EarlyEdge(); pendingReentry.Press();
        var reenteredFall = new FallDown(pendingReentry.Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero };
        pendingReentry.Entry(reenteredFall, nativeDebit: true, duringNative: () =>
        {
            pendingReentry.Entry(reenteredFall);
            // Model the outer FallDown.Enter's final write after the nested entry.
            reenteredFall._liftForce = Vector3.zero; reenteredFall._currentLiftforce = Vector3.zero;
        });
        Check(reenteredFall._liftForce.y == 0, "outer native entry cannot reuse nested completion capture");
    }
    private static void StrengthAndFiniteInputs()
    {
        foreach (float ratio in new[] { -3f, 0.25f, 1f, 1.5f, 9f })
        {
            var f = new Fixture(); f.Airborne(); Settings.DoubleJumpHeightMult.Value = ratio; f.Press();
            Check(Near(f.Jump._currentLiftforce.y, 10f * MathF.Sqrt(Math.Clamp(ratio, 0.25f, 1.5f))), "height ratio clamps to supported range");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var f = new Fixture(); f.Airborne(); Settings.DoubleJumpHeightMult.Value = invalid; Denied(f, "nonfinite height ratio denies");
            Settings.DoubleJumpHeightMult.Value = 1; f.Press(); Check(f.Player.Physical.Debits == 1, "invalid setting does not spend credit");
            f = new Fixture(); f.Airborne(); f.Context.TransformPosition.y = invalid; Denied(f, "nonfinite body height denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftDelay = invalid; Denied(f, "nonfinite lift delay denies");
            f = new Fixture(); f.Airborne(); f.Jump._skilledBoost = invalid; Denied(f, "nonfinite skill velocity denies");
            f = new Fixture(); f.Airborne(); f.Jump._liftForce.y = invalid; Denied(f, "nonfinite native lift denies");
            f = new Fixture(falling: true); f.Airborne(); f.Context.Overweight = invalid; Denied(f, "nonfinite falling weight denies");
            f = new Fixture(falling: true); f.Airborne(); Settings.JumpHeightMult.Value = invalid; Denied(f, "nonfinite first height denies fall rescue");
        }
        foreach (bool painkillers in new[] { false, true })
        {
            var f = new Fixture(falling: true); f.Airborne(); f.Context.Overweight = 0.5f;
            f.Context.Conditions = EPhysicalCondition.LeftLegDamaged | (painkillers ? EPhysicalCondition.OnPainkillers : 0);
            Settings.JumpHeightMult.Value = 2.25f; f.Press();
            float weight = painkillers ? 0.83f : 0.66f;
            Check(Near(f.Jump._currentLiftforce.y, 10 * weight * 1.5f * MathF.Sqrt(0.8f)), "falling lift retains native injured weight skill and Arena height factors");
            Check(EFTHardSettings.Instance.LIFT_VELOCITY_BY_SPEED.LastSpeed == f.Jump._initialMagnitude,
                "falling lift evaluates native speed curve using actual entry speed");
        }
    }
    private static void JumpBoots()
    {
        void Grant() => ArenaMode.Pickups.ArenaPowerups.State.GrantJump(Time.time,30);
        float boosted=10*MathF.Sqrt(.8f*2f), ordinary=10*MathF.Sqrt(.8f);
        var f=new Fixture();Grant();f.Entry(f.Jump);
        Check(Near(f.Jump._liftForce.y,8*MathF.Sqrt(2f)),"boots boost native entry even at base height one");
        Check(Near(ArenaJumpBoots.WithoutEntryBonus(f.Jump).y,8),"entry normalization strips only the applied boots factor");
        f.Airborne();f.Press();Check(Near(f.Jump._currentLiftforce.y,boosted),"boots affect double jump exactly once");
        for(int i=0;i<3;i++)
        {
            f.Land();f.Jump._liftForce=new(0,8,0);Grant();f.Entry(f.Jump);f.Airborne();f.Press();
            Check(Near(f.Jump._currentLiftforce.y,boosted),"reused entry and refreshed boots never compound");
        }
        f=new Fixture();Grant();f.Entry(f.Jump);Time.time+=30;f.Airborne();f.Press();
        Check(Near(f.Jump._currentLiftforce.y,ordinary),"expired boots do not survive in cached takeoff lift");
        f=new Fixture();f.Entry(f.Jump);f.Airborne();Grant();f.Press();
        Check(Near(f.Jump._currentLiftforce.y,boosted),"collecting boots midair boosts the next deliberate impulse");
        f=new Fixture();Grant();f.Entry(f.Jump);f.Airborne();
        f.Player.Physical.OnDebit=()=>ArenaMode.Pickups.ArenaPowerups.State=new();f.Press();
        Check(Near(f.Jump._currentLiftforce.y,ordinary)&&f.Player.Physical.Debits==1,"debit callback clears boots before impulse without double cost");
        f=new Fixture(falling:true);Grant();f.Entry(f.Jump);
        Check(f.Jump._liftForce.y==0,"boots do not turn passive falling into an automatic jump");
        f.Airborne();f.Press();Check(Near(f.Jump._currentLiftforce.y,boosted),"falling boots impulse applies height once");
        f=new Fixture();Grant();ArenaSession.IsActive=false;ArenaJumpBoots.ApplyEntry(f.Jump);
        Check(f.Jump._liftForce.y==8,"ordinary jump receives no boots force");
        f=new Fixture();Grant();Settings.MovementEnabled.Value=false;ArenaJumpBoots.ApplyEntry(f.Jump);
        Check(f.Jump._liftForce.y==8,"movement master disables boots force");
        f=new Fixture();Grant();var foreign=new JumpPlayerState(new());ArenaJumpBoots.ApplyEntry(foreign);
        Check(foreign._liftForce.y==8,"foreign movement context receives no boots force");
        f.Entry(f.Jump);ArenaMode.Pickups.ArenaPowerups.State=new();f.Jump._liftForce=new(0,8,0);f.Entry(f.Jump);
        Check(ArenaJumpBoots.WithoutEntryBonus(f.Jump).y==8,"unbuffed native reentry clears the old normalization factor");
    }
    private static void MetadataAndShutdown()
    {
        var f = new Fixture();
        Check(ModulePatch.Registrations.Count == 4, "installs four native movement patches"); ArenaDoubleJump.Enable();
        Check(ModulePatch.Registrations.Count == 4 && Settings.DoubleJumpEnabled.Subscribers == 1
            && Settings.MovementEnabled.Subscribers == 1 && RaidLifecycle.Subscribers == 1,
            "repeated enable does not duplicate registration");
        foreach (var target in new[] { (typeof(JumpPlayerState), "Enter"), (typeof(FallDown), "Enter"),
            (typeof(MovementContext), "CheckFlying"), (typeof(MoveInputTranslator), "TranslateCommand") })
            Check(ModulePatch.Registrations.Any(p => p.Target.DeclaringType == target.Item1 && p.Target.Name == target.Item2), "hooks actual declared native method");
        var translate = (MethodInfo)ModulePatch.Registrations.Single(p => p.Target.Name == "TranslateCommand").Target;
        Check(translate.ReturnType == typeof(InputNode.ETranslateResult) && translate.GetParameters().Single().ParameterType == typeof(ECommand),
            "translator target retains native method signature");
        Check(typeof(ArenaDoubleJump).GetNestedType("JumpCommandPatch", BindingFlags.NonPublic).GetMethod("Prefix", PrivateStatic).ReturnType == typeof(void),
            "command prefix never suppresses native input");
        Check(typeof(ArenaDoubleJump).GetNestedType("JumpEnterPatch", BindingFlags.NonPublic).GetMethod("Postfix", PrivateStatic)
            .GetCustomAttribute<HarmonyPriority>().Priority == Priority.Last, "native first height patch precedes reserved air height scaling");
        f.Airborne(); ArenaDoubleJump.Shutdown(); Denied(f, "shutdown denies action");
        Check(Settings.DoubleJumpEnabled.Subscribers == 0 && Settings.MovementEnabled.Subscribers == 0 && RaidLifecycle.Subscribers == 0,
            "shutdown unsubscribes setting and raid events");
        ArenaDoubleJump.Enable(); Denied(f, "shutdown and reenable cannot restore in flight credit");
        f.Land(); f.Airborne(); f.Press(); Check(f.Player.Physical.Debits == 1, "landing restores credit after reenable");
        ArenaDoubleJump.Shutdown(); ModulePatch.Registrations.Clear(); ModulePatch.FailAfter = 1; ArenaDoubleJump.Enable();
        Denied(f, "partial patch installation remains disabled");
    }
    public static int Main()
    {
        try
        {
            SuccessfulImpulse(); IdentityAndFeatureGuards(); EpisodeLifecycle();
            EarlyLedgeNativeTransition(); CaptureAndDebitOwnership(); StrengthAndFiniteInputs(); JumpBoots(); MetadataAndShutdown();
            Console.WriteLine($"ARENA_DOUBLE_JUMP_CONTRACTS_PASSED ({_assertions} assertions; production source with contract doubles, no live Unity/Harmony claim)");
            return 0;
        }
        catch (Exception exception) { Console.WriteLine(exception); return 1; }
    }
}
