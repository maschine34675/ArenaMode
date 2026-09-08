using System.Reflection;
using System.Runtime.CompilerServices;

// Narrow contract doubles only. The production patches execute unchanged; these
// are not a Unity physics engine or proof that live Harmony dispatch occurs.
namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static Vector3 operator *(Vector3 value, float multiplier)
            => new(value.x * multiplier, value.y * multiplier, value.z * multiplier);
    }
    public struct Vector2(float x, float y) { public float x = x, y = y; }
    public static class Physics { public static Vector3 gravity = new(0, -9.81f, 0); }
    public static class Mathf
    {
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger = new(); }
    public sealed class Logger
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Debug = new();
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogDebug(string message) => Debug.Add(message);
    }
}
namespace ArenaMode.Config
{
    public sealed class Setting<T>(T value)
    {
        private T _value = value;
        public event EventHandler SettingChanged;
        public int Subscribers => SettingChanged?.GetInvocationList().Length ?? 0;
        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    public static class Settings
    {
        public static readonly Setting<bool> MovementEnabled = new(true);
        public static readonly Setting<bool> DoubleJumpEnabled = new(true);
        public static readonly Setting<float> DoubleJumpHeightMult = new(0.8f);
    }
}
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle
    {
        public static event Action OnRaidEnded;
        public static int Subscribers => OnRaidEnded?.GetInvocationList().Length ?? 0;
        public static void End() => OnRaidEnded?.Invoke();
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && !ReferenceEquals(game, null) && !ReferenceEquals(world, null)
                && ReferenceEquals(game, ActiveGame) && ReferenceEquals(world, ActiveWorld);
    }
}
namespace EFT
{
    public class AbstractGame { }
    public class GameWorld { public Player MainPlayer; }
    public class GlobalConfiguration
    {
        public readonly VaultingConfiguration VaultingSettings = new();
    }
    public class VaultingConfiguration { public float VaultingInputTime = 0.2f; }
    public sealed class HealthController { public bool IsAlive = true; }
    public sealed class Physical
    {
        public bool CanJump = true;
        public int Debits;
        public bool ThrowOnJump;
        public Action OnDebit;
        public void OnJump(bool value)
        {
            if (!value) throw new InvalidOperationException("unexpected debit mode");
            Debits++;
            OnDebit?.Invoke();
            if (ThrowOnJump) throw new InvalidOperationException("physical callback failed");
        }
    }
    public class Player
    {
        public bool IsYourPlayer = true;
        public bool IsAI;
        public bool IsInventoryOpened;
        public HealthController HealthController = new();
        public Physical Physical = new();
        public MovementContext MovementContext;
        public MovementState CurrentManagedState;
        public float _vaultingTiming;
    }
    public class MovementContext
    {
        public bool IsGrounded = true;
        public bool IsInPronePose;
        public bool IsInMountedState;
        public bool CanJump = true;
        public bool Vaulting;
        public UnityEngine.Vector3 TransformPosition = new(12, 19, 24);
        public float FreefallTime = 1.2f;
        public float _startFallingHeight = 23;
        public float FallHeight = 4;
        public int Noises;
        public bool ThrowOnNoise;
        public bool PlayerAnimatorGetIsVaulting() => Vaulting;
        public void EmitJumpNoise()
        {
            Noises++;
            if (ThrowOnNoise) throw new InvalidOperationException("noise callback failed");
        }
    }
    public class MovementState(MovementContext context) { public MovementContext MovementContext = context; }
    public class JumpPlayerState(MovementContext context) : MovementState(context)
    {
        public enum EJumpState { PushingFromTheGround, Jump, Bumbped }
        public EJumpState _jumpState = EJumpState.Jump;
        public UnityEngine.Vector2 _moveDirection = new(0.5f, 0.9f);
        public UnityEngine.Vector3 _initialNormalizedDirection = new(0.6f, 0, 0.8f);
        public float _initialMagnitude = 4.5f, _currentMagnitude = 3.5f;
        public UnityEngine.Vector3 _liftForce = new(0, 8, 0);
        public float _skilledBoost = 1.25f;
        public UnityEngine.Vector3 _currentLiftforce = new(0, -2, 0);
        public float _stateTime = 1.75f, _liftDelay = 0.3f;
        public float _potentialH = 21, _timeDescending = 0.9f, _peakTime = 0.6f;
        public int _framesWithoutMotion = 2;
        public bool _hasLastHeight = true;
        public float _lastPlayerHeight = 20;
        public void Enter(bool isFromSameState) { }
        public void Exit(bool toSameState) { }
    }
    public sealed class FallDownPlayerState(MovementContext context) : JumpPlayerState(context) { }
    public class MoveInputTranslator(Player controller)
    {
        public Player _controller = controller;
        public InputSystem.InputNode.ETranslateResult TranslateCommand(InputSystem.ECommand command)
            => InputSystem.InputNode.ETranslateResult.Ignore;
    }
}
namespace EFT.InputSystem
{
    public enum ECommand { Jump, Move, ReloadWeapon }
    public abstract class InputNode
    {
        public enum ETranslateResult { Ignore, Block, BlockAll }
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            if (typeof(T) != typeof(EFT.Player) || typeof(F) != typeof(float) || name != "_vaultingTiming")
                throw new InvalidOperationException("unsupported field reflection");
            return (T instance) => ref Unsafe.As<float, F>(ref ((EFT.Player)(object)instance)._vaultingTiming);
        }
        public static MethodInfo DeclaredMethod(Type type, string name)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute : Attribute { }
    public abstract class ModulePatch
    {
        public static readonly List<(Type Patch, MethodBase Target)> Registrations = new();
        public static int FailAfter = int.MaxValue;
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            if (Registrations.Count >= FailAfter) throw new InvalidOperationException("patch installation failed");
            var target = GetTargetMethod() ?? throw new InvalidOperationException("missing patch target");
            Registrations.Add((GetType(), target));
        }
    }
}
