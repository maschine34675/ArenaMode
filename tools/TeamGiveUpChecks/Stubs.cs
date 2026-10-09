using ArenaMode.Combat;
using ArenaMode.Modes;
using EFT;

namespace UnityEngine
{
    public enum KeyCode { Backspace, LeftShift }
    public static class Input
    {
        public static bool Down = true, Held = true;
        public static bool GetKeyDown(KeyCode key) => Down;
        public static bool GetKey(KeyCode key) => Held;
    }
    public static class GUIUtility { public static int keyboardControl; }
}
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem
    {
        public static EventSystem current;
        public object currentSelectedGameObject;
    }
}
namespace ArenaMode.Config
{
    internal sealed class Shortcut
    {
        public UnityEngine.KeyCode MainKey = UnityEngine.KeyCode.Backspace;
        public UnityEngine.KeyCode[] Modifiers = [];
    }
    internal static partial class Settings
    {
        internal static readonly Entry<Shortcut> GiveUpHotkey = new(new Shortcut());
    }
}
namespace ArenaMode.Combat
{
    // HUD-only notice capture is checked independently by BarbedDeathChecks.
    internal static class ArenaUncreditedDeathPatch
    {
        internal static int Calls;
        internal static bool AllAfterDeathAndLease;
        internal static void ReportGiveUp(Player player, ArenaMatch match)
        {
            Calls++;
            AllAfterDeathAndLease &= !player.ActiveHealthController.IsAlive
                && !ArenaAdministrativeDeathGate.IsActive(player.ProfileId);
        }
    }
    internal static class RoleClassifier
    {
        internal static string DisplayName(Player player) => player?.ProfileId;
        internal static string Label(Player player) => "PMC";
    }
}
namespace ArenaMode.Modes
{
    // Full ArenaMatch now guards Clutch/LastManStanding by protected Tactical.
    // No Tactical instance is created by this TDM give-up harness.
    public sealed class TacticalMode : RoundsMode
    {
        public bool IsTeamArena => false;
    }
    public sealed class ZombieMode : RoundsMode
    { public bool IsTeamArena => false; public bool TryPublishLocalDeathKill() => true; }

    // Non-TDM mode seam: only GiveUp's existing intermission rule is modeled.
    public class RoundsMode : IArenaMode
    {
        public bool InIntermission;
        public bool HasResult, ResultOwnsCurrentRaid;
        public string ResultHeading, ResultDetail, ResultExitStatus;
        public void RequestResultExit() { }
        public void ReportResultExitProblem(string value) { }
        public bool TryBeginScoredEvent() => !InIntermission;
        public bool TryRecordLocalDeath() => !InIntermission;
        public string Id => "rounds";
        public string DisplayName => "Rounds";
        public bool IsComplete => false;
        public string HudStatusLine => "";
        public float DeathReplaySeconds => 0;
        public bool HoldRespawn => false;
        public bool ControlsOwnSpawns => true;
        public void OnMatchStart(ArenaMatch match) { }
        public void Tick(float deltaTime) { }
        public void OnKill(ArenaKill kill) { }
        public void OnLocalDeath() { }
        public void OnLocalRespawn(Player player) { }
        public void OnMatchEnd() { }
    }
}
