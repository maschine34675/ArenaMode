// External configuration/session surfaces. No actual game or config file is changed.
public enum BotDifficulty { easy, normal, hard, impossible }
namespace UnityEngine { public enum KeyCode { F6, F7, F8, F9, F10, LeftControl, Backspace } }
namespace ArenaMode.Session
{
    public static class ArenaSession { public static bool IsActive, ArmedForNextRaid; }
}
namespace ArenaMode.Config
{
    public static class SettingsSync { public static int Reports; public static void Report() => Reports++; }
}
namespace BepInEx.Configuration
{
    public sealed class KeyboardShortcut
    { public KeyboardShortcut(UnityEngine.KeyCode main, params UnityEngine.KeyCode[] modifiers) { } }
    public interface IAcceptableValue { object Clamp(object value); }
    public class AcceptableValueRange<T>(T min, T max) : IAcceptableValue where T : IComparable<T>
    {
        public T MinValue = min, MaxValue = max;
        public object Clamp(object value) => ((T)value).CompareTo(MinValue) < 0 ? MinValue : ((T)value).CompareTo(MaxValue) > 0 ? MaxValue : value;
    }
    public class AcceptableValueList<T>(params T[] values) : IAcceptableValue
    { public object Clamp(object value) => values.Contains((T)value) ? value : values[0]; }
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
