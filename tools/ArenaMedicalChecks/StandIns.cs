using System.Reflection;

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public ConfigEntry(T value) => Value = value;
        public T Value { get; set; }
    }
}

namespace ArenaMode.Config
{
    using BepInEx.Configuration;

    public static class Settings
    {
        public static ConfigEntry<float> MedicalUseSpeed { get; } = new(2f);
    }
}

namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive { get; set; }
    }
}

namespace ArenaMode
{
    public static class Plugin
    {
        public static TestLogger ModLogger { get; } = new();
    }

    public sealed class TestLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public void LogInfo(object value) => Infos.Add(value?.ToString() ?? string.Empty); public void LogDebug(object value) => Infos.Add(value?.ToString() ?? string.Empty);
        public void LogWarning(object value) => Warnings.Add(value?.ToString() ?? string.Empty);
        public void Clear()
        {
            Infos.Clear();
            Warnings.Clear();
        }
    }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        private static T _instance;
        public static bool Instantiated => _instance != null;
        public static T Instance
        {
            get => _instance;
            set => _instance = value;
        }
    }
}

namespace EFT.InventoryLogic
{
    public class Item
    {
        public Item(string id = "item") => Id = id;
        public string Id { get; }
    }

    public class Meds : Item
    {
        public Meds(string id = "meds") : base(id) { }
    }

    public class FoodDrink : Item
    {
        public FoodDrink(string id = "food") : base(id) { }
    }
}

namespace EFT.HealthSystem
{
    using EFT.InventoryLogic;

    public enum EEffectState
    {
        None,
        Added,
        Started,
        Residued,
        Removed,
    }

    public interface IHealthEffect { }

    public class ActiveHealthController
    {
        public IHealthEffect DoMedEffect(Item item, EFT.EBodyPart bodyPart, float? amount = null)
            => null;

        public class Effect
        {
            public EEffectState State { get; set; }
            public float DelayTime { get; set; }
            public float WorkStateTime { get; set; }
            public float _stateTime;
            public float StateCurrentTime { get; set; }
            public float WholeTime { get; set; }
            public float BuildUpTime { get; set; }
        }

        public class MedEffect : Effect, IHealthEffect
        {
            public Item MedItem { get; set; }
        }
    }
}

namespace EFT
{
    using EFT.HealthSystem;
    using EFT.InventoryLogic;

    public enum EBodyPart
    {
        Common,
        Head,
        LeftArm,
    }

    public sealed class PlayerHealthController
    {
        public bool IsAlive { get; set; } = true;
    }

    public class Player
    {
        public bool IsAI { get; set; }
        public PlayerHealthController HealthController { get; set; } = new();
        public ActiveHealthController ActiveHealthController { get; set; }
        public object HandsController { get; set; }

        public class MedsController
        {
            private readonly Item _item;
            public MedsController(Item item, FirearmsAnimator animator)
            {
                _item = item;
                _firearmsAnimator = animator;
            }

            public FirearmsAnimator _firearmsAnimator;
            public Item GetItem() => _item;
            public void Destroy() { }
        }
    }

    public sealed class GameWorld
    {
        public Player MainPlayer { get; set; }
    }
}

public sealed class FakeAnimator
{
    public float speed { get; set; } = 1f;
}

public sealed class FirearmsAnimator
{
    public FakeAnimator Animator { get; set; } = new();
    public bool ThrowOnSet { get; set; }
    public int SetCalls { get; private set; }

    public void SetAnimationSpeed(float speed)
    {
        SetCalls++;
        if (ThrowOnSet)
            throw new InvalidOperationException("controlled animator failure");
        Animator.speed = speed;
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        public static MethodInfo Method(Type type, string name, Type[] parameters)
            => type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: parameters,
                modifiers: null);
    }
}

namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPrefixAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPostfixAttribute : Attribute { }

    public abstract class ModulePatch
    {
        public static List<MethodBase> EnabledTargets { get; } = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => EnabledTargets.Add(GetTargetMethod());
    }
}

namespace Redeploy.Core.Engine
{
    using EFT;

    public static class RaidLifecycle
    {
        public static event Action<GameWorld> OnRaidStarted;
        public static event Action OnRaidEnded;
        public static int EnsureCalls { get; private set; }
        public static void EnsurePatched() => EnsureCalls++;
        public static void RaiseStarted(GameWorld world) => OnRaidStarted?.Invoke(world);
        public static void RaiseEnded() => OnRaidEnded?.Invoke();
    }
}
