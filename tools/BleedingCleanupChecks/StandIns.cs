using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace ArenaMode.Session
{
    internal static partial class ArenaSession
    {
        public static bool IsActive;
        public static AbstractGame _startedGame;
        public static GameWorld _startedWorld;
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
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static Action BeforeDestroy;
        public static bool ThrowOnDestroy;
        public static readonly List<Object> Pending = new();
        public static void Destroy(Object value)
        {
            BeforeDestroy?.Invoke();
            if (ThrowOnDestroy) throw new InvalidOperationException("destroy failed");
            Pending.Add(value);
        }
        public static void Flush()
        {
            foreach (var value in Pending) value.Destroyed = true;
            Pending.Clear();
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an && bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject : Object { }
    public struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } }
    public struct Vector3 { public float x; public Vector3(float a) { x = a; } public static Vector3 down => new(-1); }
    public struct RaycastHit { public Vector3 point, normal; }
    public static class Time { public static float time; }
    public static class Random { public static float Range(float min, float max) => max; }
    public static class Physics
    {
        public static bool Raycast(Vector3 from, Vector3 direction, out RaycastHit hit, float distance, int mask)
        { hit = new RaycastHit { point = from, normal = direction }; return true; }
    }
}
public sealed class EFTHardSettings
{
    public static readonly EFTHardSettings Instance = new();
    public float DRAW_BLEEDING_MAX_DISTANCE = 3;
    public int ENVIRONMENT_HIT_MASK = 1;
}
namespace EFT.HealthSystem
{
    public interface IHealthEffect { }
    public interface IHeavyBleeding : IHealthEffect { }
    public sealed class HeavyBleeding : IHeavyBleeding { }
    public sealed class LightBleeding : IHealthEffect { }
    public sealed class HealthController
    {
        public bool IsAlive = true;
        private Action<IHealthEffect> _started, _residual;
        private Action<EDamageType> _died;
        public string ThrowOnRemove;
        public event Action<IHealthEffect> EffectStartedEvent
        { add => _started += value; remove { if (ThrowOnRemove == "started") throw new InvalidOperationException("started detach failed"); _started -= value; } }
        public event Action<IHealthEffect> EffectResidualEvent
        { add => _residual += value; remove { if (ThrowOnRemove == "residual") throw new InvalidOperationException("residual detach failed"); _residual -= value; } }
        public event Action<EDamageType> DiedEvent
        { add => _died += value; remove { if (ThrowOnRemove == "died") throw new InvalidOperationException("died detach failed"); _died -= value; } }
        public int StartedCount => _started?.GetInvocationList().Length ?? 0;
        public int ResidualCount => _residual?.GetInvocationList().Length ?? 0;
        public int DiedCount => _died?.GetInvocationList().Length ?? 0;
        public void Start(IHealthEffect effect) => _started?.Invoke(effect);
        public void Residual(IHealthEffect effect) => _residual?.Invoke(effect);
        public void Die() { IsAlive = false; _died?.Invoke(EDamageType.Undefined); }
    }
}
namespace EFT.Interactive { public sealed class Corpse : UnityEngine.Object { } }
namespace EFT
{
    public enum EDamageType { Undefined }
    public enum GameStatus { Started, Stopping, Stopped }
    public interface IPlayer { Vector3 Position { get; } }
    public partial class Player : UnityEngine.Object, IPlayer
    {
        public string ProfileId;
        public GameObject gameObject = new();
        public Interactive.Corpse Corpse;
        public HealthSystem.HealthController HealthController = new();
        public int PositionReads;
        public Vector3 Position
        {
            get
            {
                if (gameObject.Destroyed) throw new NullReferenceException("destroyed Transform position");
                PositionReads++;
                return new Vector3(PositionReads);
            }
        }
    }
    public sealed class LocalPlayer : Player { }
    public sealed class GameWorld { public LocalPlayer MainPlayer; }
    public class AbstractGame : UnityEngine.Object { }
    public sealed class LocalGame : AbstractGame
    {
        public GameStatus Status = GameStatus.Started;
        public LocalPlayer LocalPlayer;
        public GameWorld GameWorld;
    }
}
namespace Systems.Effects
{
    public sealed class Effects
    {
        public readonly EffectsCommutator Commutator = new();
        public bool ThrowOnCommutator;
        public bool MissingCommutator;
        public EffectsCommutator EffectsCommutator
            => ThrowOnCommutator ? throw new InvalidOperationException("effects lookup failed") : MissingCommutator ? null : Commutator;
        public int Emitted;
        public void EmitBleeding(Vector3 point, Vector3 normal) => Emitted++;
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        private static ref EFT.Interactive.Corpse ReadCorpse(Player player) => ref player.Corpse;
        public static FieldRef<T, F> FieldRefAccess<T, F>(string field)
        {
            if (typeof(T) != typeof(Player) || typeof(F) != typeof(EFT.Interactive.Corpse) || field != "Corpse")
                throw new InvalidOperationException("unexpected reflection contract");
            return (FieldRef<T, F>)(object)(FieldRef<Player, EFT.Interactive.Corpse>)ReadCorpse;
        }
    }
}
