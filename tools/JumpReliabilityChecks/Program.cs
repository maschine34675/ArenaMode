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

internal static class JumpReliabilityProgram
{
    private static int _checks;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new Exception("ASSERT: " + name); }
    private static object Patch(Type type, string patch, string method, params object[] args)
        => type.GetNestedType(patch, BindingFlags.NonPublic).GetMethod(method, PrivateStatic).Invoke(null, args);
    private sealed class Fixture
    {
        internal readonly MovementContext Context = new();
        internal readonly Player Player;
        internal readonly LocalGame Game;
        internal readonly GameWorld World;
        internal Fixture()
        {
            ArenaJumpReliability.Shutdown(); ArenaDoubleJump.Shutdown();
            ModulePatch.Registrations.Clear(); ModulePatch.FailAfter = int.MaxValue;
            Settings.MovementEnabled.Value = Settings.DoubleJumpEnabled.Value = true;
            Settings.SlideEnabled.Value = true;
            ArenaSlideTransitions.Active = false; ArenaSlideTransitions.OwnerEpoch++; ArenaSlideTransitions.OnPrepare = null;
            ArenaSlideTransitions.BoundSprintExit = null; ArenaSlideTransitions.WindowOverride = null;
            Settings.DoubleJumpHeightMult.Value = .8f; Settings.JumpHeightMult.Value = 1;
            ArenaRespawnDriver.IsRespawnInProgress = ArenaRespawnDriver.StandDown = ArenaSpectatorService.IsActive = ArenaInputBlocker.Blocked = false;
            ArenaAirVault.Pending = ArenaBunnyhop.Buffered = false; ArenaSession.IsActive = true;
            Time.time = Time.realtimeSinceStartup = 10;
            Player = new Player { MovementContext = Context };
            State(new MovePlayerState(Context));
            Game = new LocalGame { LocalPlayer = Player }; World = new GameWorld { MainPlayer = Player };
            Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = Game;
            Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = World;
            Singleton<GlobalConfiguration>.Instance = new(); EFTHardSettings.Instance = new();
            ArenaMode.Pickups.ArenaPowerups.State = new(); ArenaMode.Pickups.ArenaPowerups.Owner = Player;
            ArenaDoubleJump.Enable(); ArenaJumpReliability.Enable();
        }
        internal void State(MovementState state) => Context.CurrentState = Player.CurrentManagedState = state;
        internal void Tick() => Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpLandingPatch", "Postfix", Context);
        internal void Ground() { Context.IsGrounded = Player.CharacterController.isGrounded = true; Tick(); }
        internal void Leave(float after = .05f, bool falling = true)
        {
            Time.time += after; Time.realtimeSinceStartup += after;
            Context.IsGrounded = Player.CharacterController.isGrounded = false;
            if (falling) State(new FallDown(Context) { _liftForce = Vector3.zero });
        }
        internal void Press(Action native = null, Action afterPostfix = null)
        {
            var translator = new MoveInputTranslator(Player);
            object[] args = { translator, ECommand.Jump, null };
            Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpJumpCommandPatch", "Prefix", args);
            Exception failure = null;
            try
            {
                native?.Invoke();
                Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpJumpCommandPatch", "Postfix", translator, args[2]);
                afterPostfix?.Invoke();
            }
            catch (Exception ex) { failure = ex; throw; }
            finally
            {
                if (!ReferenceEquals(failure, Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpJumpCommandPatch", "Finalizer", failure, args[2])))
                    throw new Exception("ASSERT: command finalizer preserves original exception");
            }
        }
        internal void Enter(bool falling, Action callback = null)
        {
            var jump = falling ? new FallDown(Context) { _liftForce = Vector3.zero, _currentLiftforce = Vector3.zero }
                : new JumpPlayerState(Context) { _jumpState = JumpPlayerState.EJumpState.PushingFromTheGround, _currentLiftforce = Vector3.zero };
            State(jump);
            string name = falling ? "ArenaDoubleJumpFallEnterPatch" : "ArenaDoubleJumpJumpEnterPatch";
            object[] args = { jump, null };
            Patch(typeof(ArenaDoubleJump), name, "Prefix", args);
            Player.Physical.OnJump(true); callback?.Invoke(); ArenaJumpBoots.ApplyEntry(jump);
            Patch(typeof(ArenaDoubleJump), name, "Postfix", jump, args[1]);
        }
        internal object BeginMotion(float dt = .016f)
        {
            object[] args = { Context, dt, null };
            Patch(typeof(ArenaJumpReliability), "ArenaJumpReliabilityMotionPatch", "Prefix", args);
            return args[2];
        }
        internal void Substep(bool foreign = false)
        {
            var controller = foreign ? new SimpleCharacterController() : (SimpleCharacterController)Player.CharacterController;
            controller.isGrounded = true;
            Patch(typeof(ArenaJumpReliability), "ArenaJumpReliabilityContactPatch", "Postfix", controller);
            controller.isGrounded = false;
        }
        internal void EndMotion(object sample, bool ran = true, Exception exception = null)
        {
            if (exception == null) Patch(typeof(ArenaJumpReliability), "ArenaJumpReliabilityMotionPatch", "Postfix", ran, sample);
            object result = Patch(typeof(ArenaJumpReliability), "ArenaJumpReliabilityMotionPatch", "Finalizer", exception, sample);
            Check(ReferenceEquals(result, exception), "motion finalizer preserves original exception");
        }
    }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
    private static void FreshInput()
    {
        var f = new Fixture(); f.Ground(); f.Player._vaultingTiming = 1;
        f.Press(() => { if (!(f.Player._vaultingTiming > .2f)) f.Context.TryJump(); });
        Check(f.Context.NativeRequests == 1 && f.Player._vaultingTiming == 0, "fresh tap repairs stale unheld vault timing");
        foreach (var deny in new Action<Fixture>[] {
            x => x.Player._isVaultingPressed = true, x => x.Context.Vaulting = true,
            x => ArenaAirVault.Pending = true, x => Settings.MovementEnabled.Value = false,
            x => ArenaSession.IsActive = false, x => x.Player.IsAI = true,
            x => x.Player.HealthController.IsAlive = false, x => x.World.MainPlayer = new(),
            x => x.Game.LocalPlayer = new(), x => x.Game.Status = GameStatus.Stopped,
            x => x.Player.IsInventoryOpened = true, x => ArenaInputBlocker.Blocked = true,
            x => ArenaRespawnDriver.StandDown = true, x => ArenaRespawnDriver.IsRespawnInProgress = true,
            x => ArenaSpectatorService.IsActive = true })
        {
            f = new Fixture(); f.Player._vaultingTiming = 1; deny(f); f.Press();
            Check(f.Player._vaultingTiming == 1, "held vault and excluded owners retain native timing");
        }
    }
    private static void CoyoteCredit()
    {
        foreach (bool doubleEnabled in new[] { false, true })
        foreach (float delay in new[] { 0f, .05f, .149f })
        {
            var f = new Fixture(); Settings.DoubleJumpEnabled.Value = doubleEnabled; f.Ground(); f.Leave(delay); f.Press();
            Check(f.Player.Physical.Debits == 1 && Near(((FallDown)f.Player.CurrentManagedState)._currentLiftforce.y, 10),
                "coyote uses first jump strength independent of double toggle");
            f.Press();
            Check(f.Player.Physical.Debits == (doubleEnabled ? 2 : 1), "coyote preserves exactly one separate double jump credit");
            f.Press(); Check(f.Player.Physical.Debits == (doubleEnabled ? 2 : 1), "repeated coyote presses cannot mint credit");
        }
        foreach (bool nativeFall in new[] { false, true })
        {
            var f = new Fixture(); f.Ground(); f.Leave(falling: false); f.Press();
            Check(f.Context.NativeRequests == 1 && f.Player.Physical.Debits == 0, "early coyote waits for native entry debit");
            f.Press(); Check(f.Context.NativeRequests == 1, "pending coyote has sole command ownership");
            f.Enter(nativeFall);
            var jump = (JumpPlayerState)f.Player.CurrentManagedState;
            Check(f.Player.Physical.Debits == 1 && (nativeFall ? Near(jump._currentLiftforce.y, 10) : jump._jumpState == JumpPlayerState.EJumpState.PushingFromTheGround),
                "coyote native entry retains first jump timing and one debit");
            jump._jumpState = JumpPlayerState.EJumpState.Jump; f.Press();
            Check(f.Player.Physical.Debits == 2, "native coyote completion preserves double jump credit");
        }
        var boots = new Fixture(); boots.Ground(); ArenaMode.Pickups.ArenaPowerups.State.GrantJump(Time.time, 30); boots.Leave(); boots.Press();
        Check(Near(((FallDown)boots.Player.CurrentManagedState)._currentLiftforce.y, 10 * MathF.Sqrt(2)), "coyote applies boots exactly once");
        boots.Press(); Check(Near(((FallDown)boots.Player.CurrentManagedState)._currentLiftforce.y, 10 * MathF.Sqrt(1.6f)), "coyote then double jump boots never compound");
        var bunny = new Fixture(); bunny.Ground(); bunny.Leave(); ArenaBunnyhop.Buffered = true; bunny.Press();
        Check(bunny.Player.Physical.Debits == 0 && ArenaJumpReliability.TryConsumeCoyote(bunny.Player), "bunnyhop reservation keeps command precedence");
    }
    private static void ContactAndBoundaries()
    {
        foreach (float delay in new[] { .151f, 1f, float.NaN, float.PositiveInfinity, -.01f })
        {
            var f = new Fixture(); Settings.DoubleJumpEnabled.Value = false; f.Ground(); f.Leave(delay); f.Press();
            Check(f.Player.Physical.Debits == 0, "expired nonfinite or backwards coyote time is denied");
        }
        var ray = new Fixture(); Settings.DoubleJumpEnabled.Value = false; ray.Tick(); ray.Leave(); ray.Press();
        Check(ray.Player.Physical.Debits == 0, "ray proximity alone never creates coyote grace");
        var fresh = new Fixture(); Settings.DoubleJumpEnabled.Value = false; fresh.Leave(); fresh.Press();
        Check(fresh.Player.Physical.Debits == 0, "fresh airborne body has no unobserved coyote credit");
        foreach (int kind in new[] { 0, 1, 2, 3, 4 })
        {
            var f = new Fixture(); Settings.DoubleJumpEnabled.Value = false;
            var sample = f.BeginMotion(); f.Substep(foreign: kind == 1);
            if (kind == 4) { f.Tick(); f.Leave(falling: false); f.Press(); Check(f.Context.NativeRequests == 0, "unfinished motion cannot publish contact"); f.Context.IsGrounded = true; }
            f.EndMotion(sample, ran: kind != 2, exception: kind == 3 ? new Exception("native motion") : null);
            f.Tick(); f.Leave(); f.Press();
            Check(f.Player.Physical.Debits == (kind is 0 or 4 ? 1 : 0), "only successful exact controller contact creates coyote grace");
        }
        var delayed = new Fixture(); Settings.DoubleJumpEnabled.Value = false;
        var capture = delayed.BeginMotion(); delayed.Substep(); delayed.EndMotion(capture);
        Time.time += .09f; delayed.Tick(); delayed.Leave(.07f); delayed.Press();
        Check(delayed.Player.Physical.Debits == 0, "delayed contact observer cannot extend physical window");
        var native = new Fixture(); Settings.DoubleJumpEnabled.Value = false; native.Ground(); native.Leave(falling: false); native.Enter(false);
        native.State(new FallDown(native.Context) { _liftForce = Vector3.zero }); native.Press();
        Check(native.Player.Physical.Debits == 1, "ordinary ground jump entry disarms coyote grace");
    }
    private static void CleanupAndCallbacks()
    {
        foreach (var deny in new Action<Fixture>[] {
            x => { Settings.MovementEnabled.Value = false; Settings.MovementEnabled.Value = true; },
            x => { x.Player.IsInventoryOpened = true; ArenaJumpReliability.TickOwner(); x.Player.IsInventoryOpened = false; },
            x => { ArenaJumpReliability.Shutdown(); ArenaJumpReliability.Enable(); },
            x => RaidLifecycle.End(), x => x.World.MainPlayer = new(), x => x.Game.LocalPlayer = new(),
            x => ArenaSession.ActiveGame = new LocalGame(), x => x.Player.HealthController.IsAlive = false })
        {
            var f = new Fixture(); Settings.DoubleJumpEnabled.Value = false; f.Ground(); f.Leave(); deny(f); f.Press();
            Check(f.Player.Physical.Debits == 0, "scope shutdown and setting changes invalidate coyote grace");
        }
        var nested = new Fixture(); nested.Ground(); nested.Leave(); bool inDebit = false;
        nested.Player.Physical.OnDebit = () => { if (!inDebit) { inDebit = true; nested.Press(); } }; nested.Press();
        Check(nested.Player.Physical.Debits == 1, "reentrant debit cannot steal double jump command");
        nested.Player.Physical.OnDebit = null; nested.Press(); Check(nested.Player.Physical.Debits == 2, "double credit remains after completed coyote callback");
        var entered = new Fixture(); entered.Ground(); entered.Leave(falling: false); entered.Press();
        entered.Enter(true, () => entered.Press());
        Check(entered.Player.Physical.Debits == 1, "native coyote entry callback cannot steal credit");
        var cancelled = new Fixture(); cancelled.Ground(); cancelled.Leave();
        cancelled.Player.Physical.OnDebit = () => { Settings.MovementEnabled.Value = false; Settings.MovementEnabled.Value = true; };
        cancelled.Press(); Check(((FallDown)cancelled.Player.CurrentManagedState)._currentLiftforce.y == -2, "synchronous movement toggle rejects captured coyote impulse");
        var pending = new Fixture(); Settings.DoubleJumpEnabled.Value = false; pending.Ground(); pending.Leave(falling: false); pending.Press();
        Time.realtimeSinceStartup += .6f; pending.Tick();
        Check(!pending.Context.PlayerAnimator.Jump && pending.Player.Physical.Debits == 0, "failed coyote transition releases owned native signal");
        pending.Enter(true); Check(((FallDown)pending.Player.CurrentManagedState)._currentLiftforce.y == 0, "expired coyote cannot lift later fall entry");
    }
    private static void PendingCleanupAndCommandLifetime()
    {
        foreach (var cancel in new Action<Fixture>[] {
            f => f.Player.IsInventoryOpened = true, f => ArenaInputBlocker.Blocked = true,
            f => ArenaRespawnDriver.IsRespawnInProgress = true, f => ArenaRespawnDriver.StandDown = true,
            f => f.Player.HealthController.IsAlive = false, f => f.Game.Status = GameStatus.Stopped,
            f => { Settings.MovementEnabled.Value = false; Settings.MovementEnabled.Value = true; },
            f => { Settings.DoubleJumpEnabled.Value = false; Settings.DoubleJumpEnabled.Value = true; },
            f => f.Context.CanJump = false,
            f => Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpInputOwnerPatch", "Prefix", new PlayerOwner { Player = f.Player }) })
        {
            var f = new Fixture(); f.Ground(); f.Leave(falling: false); f.Press();
            Check(f.Context.PlayerAnimator.Jump, "pending coyote positive control owns raised native signal");
            cancel(f); ArenaJumpReliability.TickOwner(); ArenaDoubleJump.TickOwner();
            Check(!f.Context.PlayerAnimator.Jump && f.Player.Physical.Debits == 0,
                "ineligible pending coyote releases signal without movement observer");
        }
        var noise = new Fixture(); noise.Ground(); noise.Leave(falling: false);
        noise.Context.OnNoise = () => noise.Player.IsInventoryOpened = true; noise.Press();
        Check(!noise.Context.PlayerAnimator.Jump, "noise callback inventory cancellation clears later native signal");
        var foreign = new Fixture(); foreign.Ground(); foreign.Leave(falling: false); foreign.Press();
        var replacementContext = new MovementContext();
        foreign.World.MainPlayer = new Player { MovementContext = replacementContext };
        ArenaDoubleJump.TickOwner();
        var credit = typeof(ArenaDoubleJump).GetField("_credit", PrivateStatic).GetValue(null);
        var nativeRequest = credit.GetType().GetField("NativeRequest", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(credit);
        Check(nativeRequest == null && !replacementContext.PlayerAnimator.Jump, "owner replacement drops reservation without writing replacement body");
        var nested = new Fixture(); nested.Ground(); nested.Leave(); nested.Press(() => nested.Press());
        Check(nested.Player.Physical.Debits == 1, "translator lifetime permits only one captured command");
        nested.Press(); Check(nested.Player.Physical.Debits == 2, "prefix command lock releases after successful original");
        var latePostfix = new Fixture(); latePostfix.Ground(); latePostfix.Leave(); latePostfix.Press(afterPostfix: () => latePostfix.Press());
        Check(latePostfix.Player.Physical.Debits == 1, "later Harmony postfix cannot claim a second jump command");
        var thrown = new Fixture(); thrown.Ground(); thrown.Leave(); var failure = new Exception("native translator failure");
        try { thrown.Press(() => throw failure); } catch (Exception ex) { Check(ReferenceEquals(ex, failure), "original translator exception preserved"); }
        thrown.Press(); Check(thrown.Player.Physical.Debits == 1, "command finalizer releases lock after native exception");
    }
    private static Fixture LandingSlide()
    {
        var f = new Fixture(); f.Ground(); f.State(new JumpLandingState(f.Context));
        f.Context.PoseLevel = 0; ArenaSlideTransitions.Active = true;
        ArenaSlideTransitions.OnPrepare = p => { if (p.CurrentManagedState is MovePlayerState || p.CurrentManagedState is IdlePlayerState) p.MovementContext.PoseLevel = 1; };
        return f;
    }
    private static void LandingSlideQueue()
    {
        var f = LandingSlide(); f.Press();
        ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0 && !f.Context.PlayerAnimator.Jump, "landing slide jump waits for native locomotion");
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 1 && f.Context.PlayerAnimator.Jump && f.Player.Physical.Debits == 0,
            "landing slide queue dispatches one native ground jump");
        ArenaDoubleJump.TickOwner(); Check(f.Player.JumpCalls == 1, "landing slide queue cannot dispatch twice");
        f.Enter(false); var jump = (JumpPlayerState)f.Player.CurrentManagedState;
        Check(jump._jumpState == JumpPlayerState.EJumpState.PushingFromTheGround && f.Player.Physical.Debits == 1,
            "buffered slide native entry owns unchanged lift timing and debit");
        f.Leave(falling: false); jump._jumpState = JumpPlayerState.EJumpState.Jump; f.Press();
        Check(f.Player.Physical.Debits == 2, "buffered slide jump preserves separate extra jump credit");
        f = LandingSlide(); f.Press(); Time.realtimeSinceStartup += .3f; f.Press(); Time.realtimeSinceStartup += .21f;
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "repeated landing input cannot extend queue timeout");
        foreach (var cancel in new Action<Fixture>[] {
            x => x.Player.IsInventoryOpened = true, x => ArenaRespawnDriver.IsRespawnInProgress = true,
            x => ArenaSlideTransitions.Active = false, x => ArenaSlideTransitions.OwnerEpoch++,
            x => Settings.SlideEnabled.Value = false, x => Settings.MovementEnabled.Value = false,
            x => x.Player.HealthController.IsAlive = false, x => x.World.MainPlayer = new(),
            x => x.Game.LocalPlayer = new(), x => x.Context.IsGrounded = false,
            x => Patch(typeof(ArenaDoubleJump), "ArenaDoubleJumpInputOwnerPatch", "Prefix", new PlayerOwner { Player = x.Player }) })
        {
            f = LandingSlide(); f.Press(); cancel(f); f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
            Check(f.Player.JumpCalls == 0, "landing slide queue cancels on lost owner intent or contact");
        }
        f = LandingSlide(); f.Press(); ArenaSlideTransitions.OnPrepare = _ => { }; f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "blocked native standing pose cannot dispatch queued slide jump");
        f = LandingSlide(); f.Player._vaultingTiming = 1f; f.Press();
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 1 && f.Player._vaultingTiming == 0f,
            "queued jump repairs unheld stale vault timing after native standing");
        f = LandingSlide(); f.Player._vaultingTiming = 1f; f.Player._isVaultingPressed = true; f.Press();
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0 && f.Player._vaultingTiming == 1f,
            "queued jump preserves held vault intent after standing");
        f = LandingSlide(); f.Press(); f.State(new MovePlayerState(f.Context)); f.Context.OnNoise = () => f.Player.IsInventoryOpened = true;
        ArenaDoubleJump.TickOwner(); Check(!f.Context.PlayerAnimator.Jump, "queued slide callback cancellation clears owned signal");
        f = LandingSlide(); f.Press(); f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner(); f.Enter(true);
        Check(((FallDown)f.Player.CurrentManagedState)._currentLiftforce.y == 0 && !f.Context.PlayerAnimator.Jump,
            "passive fall never completes queued slide as artificial jump");
    }
    private static Fixture SprintExitSlide(bool bound = true)
    {
        var f = LandingSlide();
        var transition = new SprintToIdleTransitionState(f.Context);
        f.State(transition);
        if (bound) ArenaSlideTransitions.BoundSprintExit = transition;
        return f;
    }
    private static void SprintExitQueue()
    {
        var f = SprintExitSlide(); f.Press();
        Time.realtimeSinceStartup += .1f; ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0 && f.Player.CurrentManagedState is SprintToIdleTransitionState,
            "sprint exit queue never forces native transition");
        Time.realtimeSinceStartup += .65f; ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "sprint exit queue waits through native seven hundred millisecond phase");
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 1 && f.Context.PlayerAnimator.Jump && f.Player.Physical.Debits == 0,
            "bound sprint exit queue survives beyond half second and dispatches once");
        f.Enter(false); Check(f.Player.Physical.Debits == 1, "sprint exit queued jump retains native stamina debit");
        f = SprintExitSlide(); f.Press(); Time.realtimeSinceStartup += .6f; f.Press();
        Time.realtimeSinceStartup += .401f; f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "sprint exit queue expires at one second without repeat extension");
        f = SprintExitSlide(); f.Press(); Time.realtimeSinceStartup -= .01f;
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "queue rejects clock rollback against actual creation timestamp");
        f = SprintExitSlide(bound: false); f.Press(); f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "unbound native transition receives no buffered jump");
        f = SprintExitSlide(); f.Press(); ArenaSlideTransitions.BoundSprintExit = null; ArenaDoubleJump.TickOwner();
        f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
        Check(f.Player.JumpCalls == 0, "losing exact sprint exit binding cancels queue before later ground state");
        foreach (float invalid in new[] { 0f, -1f, 1.001f, float.NaN, float.PositiveInfinity })
        {
            f = SprintExitSlide(); ArenaSlideTransitions.WindowOverride = invalid; f.Press();
            f.State(new MovePlayerState(f.Context)); ArenaDoubleJump.TickOwner();
            Check(f.Player.JumpCalls == 0, "buffer window must be finite positive and at most one second");
        }
    }
    public static int Main()
    {
        try
        {
            if (Program.Main() != 0) return 1;
            FreshInput(); CoyoteCredit(); ContactAndBoundaries(); CleanupAndCallbacks();
            PendingCleanupAndCommandLifetime(); LandingSlideQueue();
            SprintExitQueue();
            ArenaJumpReliability.Shutdown(); ArenaDoubleJump.Shutdown();
            Console.WriteLine($"JUMP_RELIABILITY_CONTRACTS_PASSED ({_checks} assertions; production sources and native contract doubles, no Unity claim)");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}
