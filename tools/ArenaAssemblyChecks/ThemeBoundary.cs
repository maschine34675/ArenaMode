// Presentation dependency only: this harness proves notice ownership/lifetime,
// not native rendering. Actual theme is linked in the menu and map UI checks.
namespace ArenaMode.Hud;
internal static class ArenaUiTheme
{
    internal static readonly UnityEngine.Color Surface = default, Accent = default, Muted = default;
    internal static UnityEngine.Color WithAlpha(UnityEngine.Color value, float alpha)
    { value.a = alpha; return value; }
    internal static void Decorate(UnityEngine.RectTransform parent) { }
}
