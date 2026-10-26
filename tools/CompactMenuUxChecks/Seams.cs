namespace UnityEngine
{
    public class Transform { }
    public struct Rect { public float width, height; public Rect(float w, float h) { width = w; height = h; } }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public class RectTransform : Transform
    {
        public Rect rect;
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
        public Vector3 localScale;
    }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value { get; set; } public ConfigEntry(T value) { Value = value; } }
    public class ConfigFile { public bool SaveOnConfigSet; public int Saves; public void Save() { Saves++; } }
}
namespace ArenaMode
{
    internal static class Plugin { public static Logger ModLogger = new(); }
    internal sealed class Logger { public void LogWarning(string message) { } }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession { public static bool IsActive, ArmedForNextRaid; }
}
namespace ArenaMode.Kits
{
    internal static class KitPreloadPlan { internal const string GunGameModeId = "gungame"; }
}
namespace ArenaMode.Combat.Fire
{
    internal static class ArenaFireRuntime { internal static bool BotEscapeAvailable = true; }
    internal static class ArenaFireSettings { internal static BepInEx.Configuration.ConfigEntry<bool> Enabled = new(true); }
}
namespace ArenaMode.Pickups
{
    internal static class ArenaPickupSettings { internal static BepInEx.Configuration.ConfigEntry<bool> Enabled = new(true); }
}
namespace ArenaMode.Spectator
{
    internal static class KillcamCompat
    {
        internal static bool Available = true;
        internal static bool CanPlay(out string reason) { reason = Available ? null : "Requires Killcam."; return Available; }
    }
}
namespace ArenaMode.Config
{
    using BepInEx.Configuration;
    using ArenaMode.Modes;
    internal static class Settings
    {
        internal static ConfigFile File = new();
        internal static ConfigEntry<bool> MovementEnabled = new(true), DoubleJumpEnabled = new(true),
            BunnyhopEnabled = new(true), AirControlEnabled = new(true), AirHipFireEnabled = new(true),
            AirVaultEnabled = new(true), SlideEnabled = new(true), InfiniteStamina = new(true),
            NoFallDamage = new(true), UseItemsAnywhere = new(true), TeamLasers = new(true),
            AkimboEnabled = new(false), PmcVoiceEnabled = new(true), AutoQuickSlots = new(true),
            InstantSearch = new(true), CombatHudEnabled = new(true), BotMovementEnabled = new(true),
            ZombieCrowdEnabled = new(false), HudEnabled = new(true), Tracers = new(true),
            Announcer = new(true), TeamFriendlyFire = new(true), ZombieFriendlyFire = new(false);
        internal static float ReplaySeconds;
        internal static float GetReplaySeconds(string mode) => ReplaySeconds;
        internal static bool TrySelectKillcam(string mode, bool enabled) { ReplaySeconds = enabled ? 3 : 0; return true; }
        internal static ConfigEntry<bool> AnnouncerEntry(string mode) => Announcer;
        internal static ConfigEntry<bool> TracerEntry(string mode, bool team)
            => mode is ArenaModeCatalog.TeamDeathmatchId or ArenaModeCatalog.DominationId || team ? Tracers : null;
        internal static ConfigEntry<bool> FriendlyFireEntry(string mode, bool team)
            => mode is ArenaModeCatalog.TeamDeathmatchId or ArenaModeCatalog.DominationId ? TeamFriendlyFire
                : team ? mode == ArenaModeCatalog.ZombieId ? ZombieFriendlyFire : TeamFriendlyFire : null;
    }
}
