using UnityEngine;
using ArenaMode.Hud;

namespace ArenaMode.Spectator;

internal sealed class ArenaSpectatorOverlay : MonoBehaviour
{
    private GUIStyle _style;
    private ArenaWindowSkin _skin;

    private void OnDestroy()
    {
        try { ArenaSpectatorService.Stop("spectator overlay destroyed"); }
        finally { _skin?.Dispose(); _skin = null; }
    }

    private void OnGUI()
    {
        string status = ArenaSpectatorService.CurrentStatusLine;
        if (string.IsNullOrEmpty(status))
            return;

        if (_style == null)
        {
            _skin = new ArenaWindowSkin();
            _style = _skin.Notice();
        }

        GUI.depth = 75;
        float width = Mathf.Min(560f, Screen.width * 0.8f);
        GUI.Box(
            new Rect((Screen.width - width) * 0.5f, Screen.height - 112f, width, 54f),
            status,
            _style);
    }
}
