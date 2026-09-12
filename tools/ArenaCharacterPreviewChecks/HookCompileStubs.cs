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
        public static bool IsSelectedFor(RaidSettings _) => false;
        public static void Clear() { }
    }
    internal static class ArenaSession
    {
        public static bool IsActive => false;
    }
    internal static class ArenaSideSelectionLayout
    {
        public static ArenaCharacterPreview DetachPreview(MatchMakerSideSelectionScreen _) => null;
    }
}
