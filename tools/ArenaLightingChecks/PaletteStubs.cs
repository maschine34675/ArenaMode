namespace ArenaMode.Session { internal static class ArenaTeamSideSelection { internal static bool? SelectedUsec { get; set; } } }
namespace ArenaMode.Modes { internal static class ArenaModeCatalog { internal const string TeamDeathmatchId = "teamdeathmatch"; } }
namespace EFT { public sealed class AbstractGame { } }
namespace UnityEngine
{
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color c)
            => $"{(int)MathF.Round(c.r * 255):X2}{(int)MathF.Round(c.g * 255):X2}{(int)MathF.Round(c.b * 255):X2}";
    }
}
