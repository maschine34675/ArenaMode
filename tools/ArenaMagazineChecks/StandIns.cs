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
        public static ConfigEntry<float> MagazineHandlingSpeed { get; } = new(2f);
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
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static TestLogger ModLogger { get; } = new();
    }

    public sealed class TestLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public void LogInfo(object value) => Infos.Add(value?.ToString() ?? string.Empty);
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
    public class InventoryController { }
    public class Magazine { }
    public class Ammo { }
}

namespace EFT
{
    using EFT.InventoryLogic;

    public sealed class PlayerHealthController
    {
        public bool IsAlive { get; set; } = true;
    }

    public class Player
    {
        public bool IsAI { get; set; }
        public PlayerHealthController HealthController { get; set; } = new();
        public InventoryController InventoryController { get; set; }

        public class PlayerInventoryController : InventoryController
        {
            public Player Player;

            public sealed class LoadMagazineProcess
            {
                public LoadMagazineProcess(
                    InventoryController inventoryController,
                    Magazine magazine,
                    Ammo sourceAmmo,
                    int count,
                    bool elite,
                    float loadOneAmmoSpeed)
                {
                    _loadOneAmmoSpeed = loadOneAmmoSpeed;
                    _cooldown = loadOneAmmoSpeed;
                }

                public float _loadOneAmmoSpeed;
                public float _cooldown;
            }

            public sealed class UnloadMagazineProcess
            {
                public UnloadMagazineProcess(
                    InventoryController inventoryController,
                    Magazine magazine,
                    float loadOneAmmoSpeed,
                    bool elite)
                {
                    _loadOneAmmoSpeed = loadOneAmmoSpeed;
                    _cooldown = loadOneAmmoSpeed;
                }

                public float _loadOneAmmoSpeed;
                public float _cooldown;
            }
        }
    }

    public sealed class GameWorld
    {
        public Player MainPlayer { get; set; }
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static ConstructorInfo Constructor(Type type, Type[] parameters)
            => type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: parameters,
                modifiers: null);
    }
}

namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PatchPrefixAttribute : Attribute { }

    public abstract class ModulePatch
    {
        public static List<MethodBase> EnabledTargets { get; } = new();
        public static List<MethodBase> DisabledTargets { get; } = new();
        public static List<string> EnabledHarmonyIds { get; } = new();
        public static List<string> DisabledHarmonyIds { get; } = new();
        public static string FailNextEnableAfterPatchForHarmonyId { get; set; }
        public static string FailNextDisableBeforeUnpatchForHarmonyId { get; set; }
        public static string FailNextDisableAfterUnpatchForHarmonyId { get; set; }
        public bool IsActive { get; private set; }
        public MethodBase TargetMethod { get; private set; }
        public string HarmonyId { get; }

        protected ModulePatch(string name = null)
            => HarmonyId = name ?? GetType().Name;

        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            bool hasPrefix = GetType()
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic
                            | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Any(method => method.GetCustomAttribute<PatchPrefixAttribute>() != null);
            if (!hasPrefix)
                throw new InvalidOperationException("ModulePatch requires a patch method");

            TargetMethod = GetTargetMethod();
            EnabledTargets.Add(TargetMethod);
            EnabledHarmonyIds.Add(HarmonyId);
            if (FailNextEnableAfterPatchForHarmonyId == HarmonyId)
            {
                FailNextEnableAfterPatchForHarmonyId = null;
                throw new InvalidOperationException(
                    "Injected failure after patch installation and before IsActive");
            }
            IsActive = true;
        }

        public void Disable()
        {
            TargetMethod ??= GetTargetMethod();
            if (FailNextDisableBeforeUnpatchForHarmonyId == HarmonyId)
            {
                FailNextDisableBeforeUnpatchForHarmonyId = null;
                throw new InvalidOperationException(
                    "Injected failure before owner unpatch");
            }
            EnabledTargets.Remove(TargetMethod);
            EnabledHarmonyIds.Remove(HarmonyId);
            if (FailNextDisableAfterUnpatchForHarmonyId == HarmonyId)
            {
                FailNextDisableAfterUnpatchForHarmonyId = null;
                throw new InvalidOperationException(
                    "Injected failure after owner unpatch and before IsActive reset");
            }
            DisabledTargets.Add(TargetMethod);
            DisabledHarmonyIds.Add(HarmonyId);
            IsActive = false;
        }
    }
}
