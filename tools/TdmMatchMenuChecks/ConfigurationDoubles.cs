// External configuration/session surfaces. No actual game or config file is changed.
public enum BotDifficulty { easy, normal, hard, impossible }
namespace ArenaMode.Compat
{
    internal static class PitFireTeamCompat { internal static int SelectedCompanionCount = 4; }
}
namespace ArenaMode.Combat.Fire
{
    internal static class ArenaFireRuntime { internal static bool BotEscapeAvailable = true; }
}
namespace UnityEngine { public enum KeyCode { F6, F7, F8, F9, F10, LeftControl, LeftAlt, Backspace } }
namespace ArenaMode.Pickups
{
    internal enum ArenaPickupKind { DoubleDamage, Shield, Weapon, JumpBoots, InfiniteAmmo, Health }
    internal static class ArenaPickupWeapons { internal const string M60Id = "pickup-m60-retzrestdfg"; }
    internal static class ArenaHealthPickup { internal const float Amount = 150f; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool IsActive, ArmedForNextRaid; }

    // HTTP and persistence belong to the reward integration checks. This seam
    // verifies the real map view's visibility, eligibility and click ownership.
    internal static class ArenaRewardMenuState
    {
        internal static ArenaMode.Shared.RewardStatusResponse Status = new();
        internal static bool Busy;
        internal static bool CanToggle => !Busy && Status.Available && Status.CanChange
            && !ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid;
        internal static void CancelQueuedToggle() { }
        internal static int Toggles;
        internal static string Label => "PROFILE REWARDS: " + (Status.Enabled ? "ON" : "OFF");
        internal static string Detail => "Reward status fixture";
        internal static void Refresh(bool force = false) { }
        internal static void Toggle()
        {
            if (Busy || !Status.Available || !Status.CanChange || ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) return;
            Toggles++;
            Status.Enabled = !Status.Enabled;
        }
    }
}
namespace ArenaMode.Config
{
    public static class SettingsSync { public static int Reports; public static void Report() => Reports++; }
}
namespace BepInEx.Configuration
{
    public sealed class KeyboardShortcut
    {
        public static readonly KeyboardShortcut Empty = new(default);
        public KeyboardShortcut(UnityEngine.KeyCode main, params UnityEngine.KeyCode[] modifiers) { }
    }
    public interface IAcceptableValue { object Clamp(object value); bool IsValid(object value); }
    public class AcceptableValueRange<T>(T min, T max) : IAcceptableValue where T : IComparable<T>
    {
        public T MinValue = min, MaxValue = max;
        public bool IsValid(object value) => value is T typed && typed.CompareTo(MinValue) >= 0 && typed.CompareTo(MaxValue) <= 0;
        public object Clamp(object value) => ((T)value).CompareTo(MinValue) < 0 ? MinValue : ((T)value).CompareTo(MaxValue) > 0 ? MaxValue : value;
    }
    public class AcceptableValueList<T>(params T[] values) : IAcceptableValue
    {
        public bool IsValid(object value) => value is T typed && values.Contains(typed);
        public virtual object Clamp(object value) => IsValid(value) ? value : values[0];
    }
    public class ConfigDescription(string description, IAcceptableValue acceptable = null)
    { public string Description = description; public IAcceptableValue AcceptableValues = acceptable; }
    public sealed class ConfigEntry<T>(T value, ConfigDescription description, ConfigFile owner)
    {
        T _value = value;
        public ConfigDescription Description = description;
        public bool RejectWrites, ThrowWrites;
        public T Value
        {
            get => _value;
            set
            {
                if (ThrowWrites) throw new InvalidOperationException("fixture setting write failed");
                if (RejectWrites) return;
                _value = Description.AcceptableValues == null ? value : (T)Description.AcceptableValues.Clamp(value);
                if (owner.SaveOnConfigSet) owner.Save();
            }
        }
    }
    public sealed class ConfigFile
    {
        public bool SaveOnConfigSet = true, ThrowSave;
        public int Saves;
        public readonly System.Collections.Generic.Dictionary<string, object> Stored = new();
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
            => Bind(section, key, value, new ConfigDescription(description));
        public ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
            => new(Stored.TryGetValue(section + "/" + key, out var stored) ? (T)stored : value, description, this);
        public void Save() { Saves++; if (ThrowSave) throw new InvalidOperationException("fixture disk failure"); }
    }
}

namespace ArenaMode.Spectator
{
    internal static class KillcamCompat
    {
        internal static bool Available = true;
        internal static bool CanPlay(out string reason)
        { reason = Available ? null : "Requires the Killcam mod."; return Available; }
    }
}
