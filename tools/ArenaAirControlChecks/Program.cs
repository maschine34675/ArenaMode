using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly MethodInfo MoveMethod = typeof(Player).GetMethod(nameof(Player.Move));
    private static readonly MethodInfo FlyingMethod = typeof(MovementContext).GetMethod(nameof(MovementContext.CheckFlying));

    private static void Check(bool ok, string label)
    {
        _checks++;
        if (!ok) throw new Exception("ASSERT: " + label);
    }
    private static void Near(double actual, double expected, string label, double tolerance = 0.0002)
        => Check(Math.Abs(actual - expected) <= tolerance, $"{label} (actual={actual:R}, expected={expected:R})");
    private static double Heading(Vector3 v) => Math.Atan2(v.x, v.z) * 180 / Math.PI;
    private static double Speed(Vector3 v) => Math.Sqrt((double)v.x * v.x + (double)v.z * v.z);
    private static bool Same(Vector3 a, Vector3 b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
    private static object Invoke(MethodInfo method, params object[] args)
    {
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private sealed class Fixture
    {
        internal readonly MovementContext Context = new();
        internal readonly Player Player;
        internal readonly GameWorld World;
        internal readonly LocalGame Game;
        internal MovementState State;

        internal Fixture(bool fall = false, bool enable = true)
        {
            Harmony.FailUnpatch = false;
            ArenaAirControl.Shutdown();
            Harmony.Hooks.Clear(); Harmony.FailAt = 0; Harmony.LimitCallCount = 1; Harmony.WrongLimitOpcode = false;
            Settings.MovementEnabled = new(true); Settings.AirControlEnabled = new(true);
            Settings.AirControlDegreesPerSecond = new(60);
            ArenaInputBlocker.Blocked = ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = false;
            ArenaSpectatorService.IsActive = ArenaAirVault.Pending = false;
            ArenaSession.IsActive = true; Time.frameCount = 100;
            NativeIo.Calls.Clear();
            State = fall ? new FallDown(Context) : new JumpPlayerState(Context);
            Player = new() { MovementContext = Context, CurrentManagedState = State };
            Context.CurrentState = State;
            World = new() { MainPlayer = Player };
            Game = new() { LocalPlayer = Player };
            Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = World;
            Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = Game;
            if (enable) ArenaAirControl.Enable();
        }

        internal void SetState(MovementState state)
        { State = state; Player.CurrentManagedState = state; Context.CurrentState = state; }

        internal void Input(Vector2 input, bool runOriginal = true, Action duringOriginal = null)
        {
            var hook = Harmony.Hooks[MoveMethod];
            object[] before = [Player, null];
            Invoke(hook.Prefix.Method, before);
            if (runOriginal) Player.Move(input);
            duringOriginal?.Invoke();
            Invoke(hook.Postfix.Method, Player, input, runOriginal, before[1]);
        }

        internal Vector3 Limit(Vector3 motion, float dt = 0.1f, MovementState state = null)
        {
            object[] args = [state ?? State, motion, dt];
            Invoke(typeof(ArenaAirControl).GetMethod("SteerThenLimit", PrivateStatic), args);
            return (Vector3)args[1];
        }
        internal void Flying() => Invoke(Harmony.Hooks[FlyingMethod].Postfix.Method, Context);
        internal Vector3 Turn(float dt = 0.1f, Vector3? motion = null)
        { Input(new(1, 0)); return Limit(motion ?? new Vector3(0, 2, 10), dt); }
        internal Vector3 NativeStep(Vector2 input, Vector3 motion, float dt)
        {
            Input(input);
            Context.CheckFlying(); Flying();
            ((JumpPlayerState)State).ProcessJumpMovement(dt);
            return Limit(motion, dt);
        }
    }

    private static void Main()
    {
        Functional(); Scope(); InputAdmission(); Lifetime(); Binding();
        ArenaAirControl.Shutdown();
        Console.WriteLine($"ARENA_AIR_CONTROL_CHECKS_PASSED ({_checks} assertions)");
    }

    private static void Functional()
    {
        foreach (int fps in new[] { 30, 60, 120 })
        {
            var f = new Fixture(); Vector3 output = default;
            for (int frame = 0; frame < fps; frame++)
            {
                Time.frameCount++;
                output = f.Turn(1f / fps, new(0, -3.25f, 7));
            }
            Near(Heading(output), 60, $"one second turns 60 degrees at {fps} fps", 0.002);
            Near(Speed(output), 7, $"planar speed preserved at {fps} fps");
            Check(BitConverter.SingleToInt32Bits(output.y) == BitConverter.SingleToInt32Bits(-3.25f), $"vertical motion preserved at {fps} fps");
            Check(f.State.LimitCalls == fps, $"native collision runs once per frame at {fps} fps");
        }

        var fixture = new Fixture(fall: true);
        Near(Heading(fixture.Turn()), 6, "FallDown shares native steering wrapper");
        fixture = new();
        var nativeStep = fixture.NativeStep(new(1, 0), new(0, -1, 10), 0.1f);
        Check(NativeIo.Calls.SequenceEqual(new[] { "Move", "CheckFlying", "ProcessJumpMovement", "LimitMotion" }),
            "native Move CheckFlying ProcessJumpMovement collision order represented");
        Near(Heading(nativeStep), 6, "same-frame input survives native CheckFlying before motion");
        fixture = new(); fixture.Context.TransformYawDegrees = 90;
        fixture.Input(new(0, 1)); var turned = fixture.Limit(new(0, 4, 10));
        Near(Heading(turned), 6, "input transformed by native camera-relative direction");
        Check(Same(fixture.Context.LastTransformInput, new(0, 0, 1)), "native TransformVector receives XZ input only");

        fixture = new(); fixture.Input(new(1, 0)); fixture.State.CollisionX = 0;
        fixture.State.CollisionY = 0.5f; fixture.State.CollisionZ = 0.25f;
        var clipped = fixture.Limit(new(0, -4, 10));
        Near(Heading(fixture.State.LastLimitInput), 6, "steering precedes native collision");
        Near(clipped.x, 0, "native wall clipping retained"); Near(clipped.y, -2, "native vertical clipping retained");
        Near(clipped.z, fixture.State.LastLimitInput.z * 0.25, "native planar clipping retained");
        Near(fixture.State.LastLimitDt, 0.1f, "native collision receives original dt");

        fixture = new(); fixture.Input(new(1, 0));
        var failure = new InvalidOperationException("native collision failure"); fixture.State.LimitFailure = failure;
        Exception caught = null; try { fixture.Limit(new(0, 2, 10)); } catch (Exception ex) { caught = ex; }
        Check(ReferenceEquals(caught, failure) && fixture.State.LimitCalls == 1, "native collision exception propagates unchanged without retry");

        fixture = new(); fixture.Input(new(1, 0)); fixture.Context.TransformFailure = new("transform I/O failure");
        var original = new Vector3(0, 5, 10);
        Check(Same(fixture.Limit(original), original) && fixture.State.LimitCalls == 1, "steering I/O failure falls back to original native motion");

        fixture = new(); fixture.Input(new(1, 0));
        Check(Same(fixture.Limit(new(0, 8, 0)), new(0, 8, 0)), "zero XZ never acquires an impulse");
        Near(Heading(fixture.Limit(new(10, 8, 0))), 90, "zero XZ does not invent a flight heading");
        fixture = new(); fixture.Input(new(0, 0));
        Check(Same(fixture.Limit(new(0, -2, 10)), new(0, -2, 10)), "no initial input retains native heading");
        fixture = new(); fixture.Turn(); Time.frameCount++; fixture.Input(new(0, 0));
        Near(Heading(fixture.Limit(new(0, -2, 5))), 6, "releasing input preserves steered heading");
        Near(Speed(fixture.Limit(new(0, -2, 5))), 5, "retained heading follows new native magnitude");

        fixture = new(); Settings.AirControlDegreesPerSecond.Value = 1000;
        Near(Heading(fixture.Turn()), 18, "steering rate capped at 180 degrees per second");
        fixture = new(); Near(Heading(fixture.Turn(0.2f)), 6, "large admitted dt has bounded steering step");
        fixture = new(); fixture.Context.TransformOverride = new(0.1f, 0, 10);
        fixture.Input(new(1, 0)); Near(Heading(fixture.Limit(new(0, 0, 10))), Math.Atan2(0.1, 10) * 180 / Math.PI, "steering cannot overshoot desired heading");
        fixture = new(); fixture.Context.TransformOverride = new(0, 0, 0);
        Near(Heading(fixture.Turn()), 0, "zero transformed input retains heading");
        fixture = new(); fixture.Context.TransformOverride = new(float.NaN, 0, 1);
        Check(Same(fixture.Turn(), new(0, 2, 10)), "nonfinite transformed input cannot corrupt motion");
        fixture = new(); fixture.Input(new(-1, 0));
        Near(Heading(fixture.Limit(new(0, 0, 10))), -6, "left input turns left at bounded rate");
        foreach (int sign in new[] { -1, 1 })
        {
            fixture = new();
            double initial = sign * 179 * Math.PI / 180, target = -sign * 170 * Math.PI / 180;
            fixture.Context.TransformOverride = new((float)Math.Sin(target), 0, (float)Math.Cos(target));
            fixture.Input(new(1, 0));
            var wrapped = fixture.Limit(new((float)(10 * Math.Sin(initial)), 0, (float)(10 * Math.Cos(initial))));
            Near(Heading(wrapped), -sign * 175, "shortest steering path crosses angle wrap: " + sign);
        }
    }

    private static void Scope()
    {
        var cases = new (string Label, Action<Fixture> Change)[]
        {
            ("ordinary raid", f => ArenaSession.IsActive = false),
            ("movement disabled", f => Settings.MovementEnabled.Value = false),
            ("missing movement setting", f => Settings.MovementEnabled = null),
            ("air control disabled", f => Settings.AirControlEnabled.Value = false),
            ("missing air control setting", f => Settings.AirControlEnabled = null),
            ("AI player", f => f.Player.IsAI = true),
            ("foreign player", f => f.Player.IsYourPlayer = false),
            ("destroyed player", f => f.Player.Destroyed = true),
            ("missing world", f => Singleton<GameWorld>.Instance = null),
            ("destroyed world", f => f.World.Destroyed = true),
            ("different world", f => Singleton<GameWorld>.Instance = new() { MainPlayer = f.Player }),
            ("different game", f => Singleton<AbstractGame>.Instance = new LocalGame { LocalPlayer = f.Player }),
            ("missing game", f => Singleton<AbstractGame>.Instance = null),
            ("destroyed game", f => f.Game.Destroyed = true),
            ("not local game", f => Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = new AbstractGame()),
            ("stopping game", f => f.Game.Status = GameStatus.Stopping),
            ("foreign MainPlayer", f => f.World.MainPlayer = new()),
            ("foreign LocalPlayer", f => f.Game.LocalPlayer = new()),
            ("dead player", f => f.Player.HealthController.IsAlive = false),
            ("missing health", f => f.Player.HealthController = null),
            ("respawn in progress", f => ArenaRespawnDriver.IsRespawnInProgress = true),
            ("respawn stand down", f => ArenaRespawnDriver.StandDown = true),
            ("spectator", f => ArenaSpectatorService.IsActive = true),
            ("different player context", f => f.Player.MovementContext = new()),
            ("missing player context", f => f.Player.MovementContext = null),
            ("missing character controller", f => f.Player.CharacterController = null),
            ("prone", f => f.Context.IsInPronePose = true),
            ("mounted", f => f.Context.IsInMountedState = true),
            ("stationary weapon", f => f.Context.IsStationaryWeaponInHands = true),
            ("vault animator", f => f.Context.Vaulting = true),
            ("pending vault", f => ArenaAirVault.Pending = true),
            ("overridden state", f => f.Context.OverridenState = new IdlePlayerState(f.Context)),
            ("overridden controls state", f => f.Context.OverridenControlsState = new IdlePlayerState(f.Context)),
            ("sliding", f => f.Player.Components[typeof(ArenaSlideController)] = new ArenaSlideController { IsSliding = true }),
            ("non jump state", f => f.SetState(new IdlePlayerState(f.Context))),
            ("foreign jump subclass", f => f.SetState(new ForeignJumpState(f.Context))),
            ("stale managed state", f => f.Player.CurrentManagedState = new JumpPlayerState(f.Context)),
            ("stale context state", f => f.Context.CurrentState = new JumpPlayerState(f.Context)),
            ("context ground ray", f => f.Context.IsGrounded = true),
            ("character ground contact", f => f.Player.CharacterController.isGrounded = true),
            ("push phase", f => ((JumpPlayerState)f.State)._jumpState = JumpPlayerState.EJumpState.PushingFromTheGround),
            ("bumped phase", f => ((JumpPlayerState)f.State)._jumpState = JumpPlayerState.EJumpState.Bumbped)
        };
        foreach (var item in cases)
        {
            var f = new Fixture(); f.Input(new(1, 0)); item.Change(f);
            f.Input(new(1, 0)); // Also attempt fresh input in the rejected scope.
            var original = new Vector3(0, -2, 10);
            Check(Same(f.Limit(original), original) && f.State.LimitCalls == 1, "scope passthrough: " + item.Label);
        }
        var fixture = new Fixture();
        Check(!ArenaMovementScope.IsLocal(null) && !ArenaMovementScope.TryGetPlayer(null, out _), "null bodies and contexts fail scope closed");
        Check(!ArenaMovementScope.TryGetPlayer(new MovementContext(), out _), "foreign context cannot borrow local player");
    }

    private static void InputAdmission()
    {
        var safe = new Fixture();
        var beforeMove = Harmony.Hooks[MoveMethod].Prefix.Method;
        object[] beforeArgs = [null, safe.State]; Invoke(beforeMove, beforeArgs);
        Check(beforeArgs[1] == null, "null native Move owner leaves capture empty without throwing");
        safe.Player.MovementContext = null; safe.Player.StateReadFailure = new("disposed state accessor");
        beforeArgs = [safe.Player, safe.State]; Invoke(beforeMove, beforeArgs);
        Check(beforeArgs[1] == null && safe.Player.StateReads == 0, "disposed context BeforeMove never reads managed state");
        safe = new(); safe.Player.StateReadFailure = new("state read failed");
        beforeArgs = [safe.Player, safe.State]; Invoke(Harmony.Hooks[MoveMethod].Prefix.Method, beforeArgs);
        Check(beforeArgs[1] == null, "failed native state read cannot throw from BeforeMove");
        safe = new(); safe.Player.StateReadFailure = new("foreign state must not be read"); ArenaSession.IsActive = false;
        beforeArgs = [safe.Player, safe.State]; Invoke(Harmony.Hooks[MoveMethod].Prefix.Method, beforeArgs);
        Check(beforeArgs[1] == null && safe.Player.StateReads == 0, "ordinary BeforeMove never reads managed state");

        foreach (var item in new (string Label, Action<Fixture> Change)[]
        {
            ("inventory open", f => f.Player.IsInventoryOpened = true),
            ("input blocked", f => ArenaInputBlocker.Blocked = true),
            ("axes ignored", f => f.Context.IsAxesIgnored = true),
            ("blind fire", f => f.Context.BlindFire = 1)
        })
        {
            var f = new Fixture(); item.Change(f); f.Input(new(1, 0));
            Check(Same(f.Limit(new(0, 2, 10)), new(0, 2, 10)), "input admission rejects " + item.Label);
            f = new(); f.Input(new(1, 0)); item.Change(f);
            Check(Same(f.Limit(new(0, 2, 10)), new(0, 2, 10)), "input consumption rejects newly " + item.Label);
        }
        var fixture = new Fixture(); fixture.Input(new(1, 0)); Time.frameCount++;
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "previous frame input cannot steer");
        foreach (bool menu in new[] { false, true })
        {
            fixture = new(); fixture.Turn(); Time.frameCount++;
            fixture.Player.IsInventoryOpened = !menu; ArenaInputBlocker.Blocked = menu;
            fixture.Input(new(1, 0));
            Near(Heading(fixture.Limit(new(0, -1, 10))), 6, "blocked input preserves attained heading: " + menu);
        }
        fixture = new(); fixture.Input(new(1, 0)); fixture.Input(new(1, 0), runOriginal: false);
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "skipped native Move invalidates older same-frame input");
        fixture = new(); fixture.Input(new(1, 0));
        fixture.Input(new(1, 0), duringOriginal: () => ArenaInputBlocker.Blocked = true);
        ArenaInputBlocker.Blocked = false;
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "input blocked during native Move cannot donate a later sample");
        fixture = new(); fixture.Input(new(1, 0));
        fixture.Input(new(1, 0), duringOriginal: () => fixture.Context.IsAxesIgnored = true);
        fixture.Context.IsAxesIgnored = false;
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "axes ignored during native Move invalidate earlier same-frame input");
        fixture = new(); fixture.Input(new(1, 0), duringOriginal: () => fixture.SetState(new FallDown(fixture.Context)));
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "native Move state transition cannot donate input to new state");
        fixture = new(); fixture.Player.CurrentManagedState = null;
        fixture.Input(new(1, 0), duringOriginal: () => fixture.SetState(new JumpPlayerState(fixture.Context)));
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "null pre-Move state cannot donate input");
        fixture = new(); fixture.Input(new(1, 0)); fixture.SetState(new FallDown(fixture.Context));
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "different flight state cannot consume another state's sample");
        fixture = new(); fixture.Input(new(0.05f, 0));
        Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "input below deadzone cannot turn");
        foreach (Vector2 input in new[] { new Vector2(float.NaN, 0), new Vector2(0, float.PositiveInfinity) })
        {
            fixture = new(); fixture.Input(new(1, 0)); fixture.Input(input);
            Near(Heading(fixture.Limit(new(0, 0, 10))), 0, "invalid input invalidates previous same-frame sample");
        }
        foreach (float dt in new[] { float.NaN, float.PositiveInfinity, -0.1f, 0, 0.251f })
        {
            fixture = new(); var original = new Vector3(0, -3, 10);
            Check(Same(fixture.Turn(dt, original), original) && fixture.State.LimitCalls == 1, "invalid dt preserves native call: " + dt);
        }
        foreach (Vector3 motion in new[] { new Vector3(float.NaN, 1, 10), new Vector3(0, float.PositiveInfinity, 10), new Vector3(0, 1, float.NegativeInfinity) })
        {
            fixture = new(); fixture.Input(new(1, 0));
            Check(Same(fixture.Limit(motion), motion), "nonfinite native motion passes through unchanged");
        }
    }

    private static void Lifetime()
    {
        var f = new Fixture(); f.Turn(); Time.frameCount++; f.SetState(new FallDown(f.Context));
        Near(Heading(f.Limit(new(0, -2, 10))), 6, "Jump to FallDown preserves flight heading without new input");
        f = new(); f.Turn(); Time.frameCount++; f.SetState(new JumpPlayerState(f.Context));
        Near(Heading(f.Limit(new(0, 2, 10))), 6, "new jump state in same flight preserves heading");

        foreach (string sample in new[] { "rising ray", "ray without contact", "pushing contact", "vault contact", "pending vault contact" })
        {
            f = new(); f.Turn(); Time.frameCount++;
            f.Context.IsGrounded = true;
            f.Player.CharacterController.isGrounded = sample != "ray without contact";
            var jump = (JumpPlayerState)f.State; jump._currentLiftforce.y = sample == "rising ray" ? 1 : -1;
            if (sample == "pushing contact") jump._jumpState = JumpPlayerState.EJumpState.PushingFromTheGround;
            f.Context.Vaulting = sample == "vault contact"; ArenaAirVault.Pending = sample == "pending vault contact";
            f.Flying(); f.Context.IsGrounded = f.Player.CharacterController.isGrounded = false;
            jump._jumpState = JumpPlayerState.EJumpState.Jump; f.Context.Vaulting = ArenaAirVault.Pending = false;
            Near(Heading(f.Limit(new(0, -1, 10))), 6, "nonlanding sample preserves heading: " + sample);
        }
        f = new(); f.Turn(); f.Context.IsGrounded = f.Player.CharacterController.isGrounded = true;
        ((JumpPlayerState)f.State)._currentLiftforce.y = -1; f.Flying();
        f.Context.IsGrounded = f.Player.CharacterController.isGrounded = false; Time.frameCount++;
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "actual ground contact resets flight heading");

        foreach (float rate in new[] { 0, -1, float.NaN, float.PositiveInfinity })
        {
            f = new(); f.Turn(); Time.frameCount++; Settings.AirControlDegreesPerSecond.Value = rate;
            Check(Same(f.Limit(new(0, 1, 10)), new(0, 1, 10)), "disabled rate preserves native motion: " + rate);
            Settings.AirControlDegreesPerSecond.Value = 60;
            Near(Heading(f.Limit(new(10, 1, 0))), 90, "disabled rate resets old heading: " + rate);
        }
        f = new(); f.Turn(); Settings.AirControlEnabled.Value = false; f.Flying();
        Settings.AirControlEnabled.Value = true; Time.frameCount++;
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "disabled feature cleanup discards old flight");
        f = new(); f.Turn(); ArenaRespawnDriver.IsRespawnInProgress = true; f.Flying();
        ArenaRespawnDriver.IsRespawnInProgress = false; Time.frameCount++;
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "respawn cleanup discards old flight");

        f = new(); f.Turn(); Time.frameCount++;
        var replacement = new Player { MovementContext = f.Context, CurrentManagedState = f.State };
        f.World.MainPlayer = f.Game.LocalPlayer = replacement;
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "context reused by replacement player cannot inherit heading");
        f = new(); f.Turn(); Time.frameCount++;
        Singleton<GameWorld>.Instance = ArenaSession.ActiveWorld = new GameWorld { MainPlayer = f.Player };
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "same body in replacement world cannot inherit heading");
        f = new(); f.Turn(); Time.frameCount++;
        Singleton<AbstractGame>.Instance = ArenaSession.ActiveGame = new LocalGame { LocalPlayer = f.Player };
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "same body in replacement game cannot inherit heading");
        f = new(); f.Turn(); ArenaAirControl.Shutdown(); ArenaAirControl.Enable(); Time.frameCount++;
        Near(Heading(f.Limit(new(10, 2, 0))), 90, "shutdown and enable cannot inherit old flight");
    }

    private static void Binding()
    {
        var f = new Fixture();
        Check(Harmony.Hooks.Count == 3 && Harmony.Hooks[MoveMethod].Prefix != null && Harmony.Hooks[MoveMethod].Postfix != null,
            "all native steering capture and landing hooks installed");
        var rewritten = Harmony.LastCode.Single(x => x.opcode == OpCodes.Call);
        Check(ReferenceEquals(rewritten, Harmony.OriginalLimitNode), "transpiler replaces collision instruction in place");
        Check(rewritten.labels.SequenceEqual(new[] { Harmony.CollisionLabel }) && rewritten.blocks.SequenceEqual(new[] { Harmony.CollisionBlock }),
            "transpiler preserves native labels and exception blocks");
        Check(rewritten.operand is MethodInfo method && method.Name == "SteerThenLimit" && method.IsStatic && method.ReturnType == typeof(void)
            && method.GetParameters().Select(x => x.ParameterType).SequenceEqual(new[] { typeof(MovementState), typeof(Vector3).MakeByRefType(), typeof(float) }),
            "transpiler replacement keeps instance ref-motion dt stack contract");
        Check(Harmony.LastCode.Count == 5 && Harmony.LastCode[0].opcode == OpCodes.Ldarg_0
            && Harmony.LastCode[1].opcode == OpCodes.Ldarg_1 && Harmony.LastCode[2].opcode == OpCodes.Ldarg_2
            && Harmony.LastCode[^1].opcode == OpCodes.Ret, "transpiler preserves noncollision instructions");

        foreach (int count in new[] { 0, 2 })
        {
            f = new(enable: false); Harmony.LimitCallCount = count; ArenaAirControl.Enable();
            Check(Harmony.Hooks.Count == 0, "unknown native collision count rolls back: " + count);
            Check(Harmony.LastCode.Count(x => x.opcode == OpCodes.Callvirt) == count, "ambiguous transpiler leaves native calls intact: " + count);
            Check(Same(f.Limit(new(0, 2, 10)), new(0, 2, 10)), "failed binding cannot steer: " + count);
        }
        f = new(enable: false); Harmony.WrongLimitOpcode = true; ArenaAirControl.Enable();
        Check(Harmony.Hooks.Count == 0 && Harmony.LastCode.Single(x => x.opcode == OpCodes.Call).operand is MethodInfo native && native.Name == nameof(MovementState.LimitMotion),
            "unexpected native collision opcode fails binding without rewrite");
        for (int fail = 1; fail <= 3; fail++)
        {
            f = new(enable: false); Harmony.FailAt = fail; ArenaAirControl.Enable();
            Check(Harmony.Hooks.Count == 0, "partial hook install rolls back: " + fail);
            Check(Same(f.Limit(new(0, 2, 10)), new(0, 2, 10)) && f.State.LimitCalls == 1, "partial install retains native collision: " + fail);
        }
        f = new(); f.Turn(); Harmony.FailUnpatch = true; ArenaAirControl.Shutdown();
        Check(Same(f.Limit(new(0, 2, 10)), new(0, 2, 10)), "surviving detour after failed unpatch cannot steer");
        Harmony.FailUnpatch = false; ArenaAirControl.Shutdown();
        Check(Harmony.Hooks.Count == 0, "shutdown retry removes remaining hooks");
    }
}
