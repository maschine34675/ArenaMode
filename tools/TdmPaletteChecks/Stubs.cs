namespace UnityEngine
{
    public readonly record struct Color(float r, float g, float b, float a = 1f);
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color)
            => Byte(color.r).ToString("X2") + Byte(color.g).ToString("X2") + Byte(color.b).ToString("X2");
        private static int Byte(float value) => Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }
}
namespace ArenaMode.Config
{
    public sealed class Entry<T>(T value) { public T Value = value; }
    internal static class Settings
    {
        internal static Entry<Effects.ArenaTracerColor> FriendlyTracerColor = new(Effects.ArenaTracerColor.Green);
        internal static Entry<Effects.ArenaTracerColor> OpponentTracerColor = new(Effects.ArenaTracerColor.Orange);
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaModeCatalog { internal const string TeamDeathmatchId = "teamdeathmatch"; }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static string ActiveMode;
        internal static object ExactGame, ExactWorld;
        internal static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
    internal static class ArenaTeamSideSelection { internal static bool? SelectedUsec; }
}
namespace EFT
{
    public sealed class AbstractGame { }
    public sealed class GameWorld { }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
