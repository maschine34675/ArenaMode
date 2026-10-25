namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public sealed class TeamProfile { public TeamInfo Info = new(); }
    public sealed class TeamInfo { public EPlayerSide Side; }
    public sealed class TeamSession { public TeamProfile Profile = new(); }
}
namespace EFT.UI.Matchmaker
{
    public sealed class MatchMakerSelectionLocationScreen : UnityEngine.Component
    {
        public sealed class SelectionLocationScreenController { public EFT.TeamSession Session = new(); }
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        private static class Slot<T, F> { internal static F Value = System.Activator.CreateInstance<F>(); }
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
            => (T instance) => ref Slot<T, F>.Value;
    }
}
namespace ArenaMode.Effects
{
    // Color mapping itself is exercised with production code by TdmPaletteChecks.
    internal static class ArenaTeamPalette
    {
        internal static UnityEngine.Color UsecUi => new(.5f, .65f, 1f, .9f);
        internal static UnityEngine.Color BearUi => new(1f, .42f, .42f, .9f);
    }
}
namespace UnityEngine.UI
{
    internal static class TeamSideComponentQueries
    {
        internal static T[] GetComponentsInChildren<T>(this Button button, bool includeInactive) where T : UnityEngine.Component
            => button.gameObject.GetComponentsInChildren<T>(includeInactive);
    }
}
