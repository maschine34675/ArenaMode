// Rendering boundary: readiness, kit purchase and Escape remain production code.
// Native client build/GPU review verify the skin; this fixture does not simulate it.
namespace ArenaMode.Hud;
internal static class ArenaUiTheme
{
    internal static readonly UnityEngine.Color Accent = default, Muted = default;
}
internal sealed class ArenaWindowSkin : System.IDisposable
{
    internal UnityEngine.GUIStyle Window() => new();
    internal UnityEngine.GUIStyle Label(int size = 14, UnityEngine.Color? color = null,
        bool bold = false) => new() { fontSize = size };
    internal UnityEngine.GUIStyle Button(bool selected = false) => new();
    public void Dispose() { }
}
