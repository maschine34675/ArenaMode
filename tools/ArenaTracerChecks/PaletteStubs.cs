namespace ArenaMode.Session { internal static class ArenaTeamSideSelection { internal static bool? SelectedUsec { get; set; } } }
namespace ArenaMode.Modes { internal static class ArenaModeCatalog { internal const string TeamDeathmatchId = "teamdeathmatch"; } }
namespace EFT { public sealed class AbstractGame { } public sealed class GameWorld { } }
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace UnityEngine
{
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color c)
            => $"{(int)MathF.Round(c.r * 255):X2}{(int)MathF.Round(c.g * 255):X2}{(int)MathF.Round(c.b * 255):X2}";
    }
}
