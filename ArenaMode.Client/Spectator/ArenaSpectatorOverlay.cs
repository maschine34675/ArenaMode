using UnityEngine;

namespace ArenaMode.Spectator;

internal sealed class ArenaSpectatorOverlay : MonoBehaviour
{
    private GUIStyle _style;

    private void OnDestroy()
        => ArenaSpectatorService.Stop("spectator overlay destroyed");

    private void OnGUI()
    {
        string status = ArenaSpectatorService.CurrentStatusLine;
        if (string.IsNullOrEmpty(status))
            return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                richText = false,
                normal = { textColor = Color.white }
            };
        }

        GUI.depth = 75;
        float width = Mathf.Min(560f, Screen.width * 0.8f);
        GUI.Box(
            new Rect((Screen.width - width) * 0.5f, Screen.height - 112f, width, 54f),
            status,
            _style);
    }
}
