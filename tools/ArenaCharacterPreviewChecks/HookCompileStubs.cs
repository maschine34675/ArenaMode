// Only the mod integration boundary is stubbed here. Native EFT/Unity/Harmony
// signatures compile against this install's real managed assemblies.
using EFT;
using EFT.UI.Matchmaker;

namespace ArenaMode
{
    internal static class Plugin
    {
        public static readonly PreviewCompileLogger ModLogger = new();
    }
    internal sealed class PreviewCompileLogger
    {
        public void LogInfo(object _) { }
        public void LogWarning(object _) { }
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaMenuSelection
    {
        public static Eligibility CurrentEligibility => new();
        public sealed class Eligibility { public bool Allowed => true; public bool IsTeam => false; }
        public static bool IsSelectedFor(RaidSettings _) => false;
        public static void Clear() { }
    }
    internal static class ArenaSession
    {
        public static bool IsActive => false;
        public static bool ArmedForNextRaid => false;
        public static int ArmEpoch => 0;
        public static string ArmedMode => null;
        public static bool ZombieOwnGearArmed => false;
    }
    internal static class ArenaSideSelectionLayout
    {
        public static ArenaCharacterPreview DetachPreview(MatchMakerSideSelectionScreen _) => null;
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaModeCatalog { public const string TacticalId = "tactical"; }
}
namespace ArenaMode.Shared
{
    internal static class ArenaPopulationPolicy { public const string Zombie = "zombie"; }
}
namespace ArenaMode.Config
{
    internal static class Settings
    {
        public static string SelectedGameModeId => "deathmatch";
        public static bool GetEquipmentChoiceOwnGear(string mode) => false;
    }
}
namespace ArenaMode.Compat
{
    internal static class PitFireTeamCompat { public static bool HasFrozenTeamRoster => false; }
}
