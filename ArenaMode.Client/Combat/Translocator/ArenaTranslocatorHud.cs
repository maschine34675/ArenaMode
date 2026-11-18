using ArenaMode.Config;
using UnityEngine;

namespace ArenaMode.Combat.Translocator;

/// <summary>One short state line while the device is held; no panel or input ownership.</summary>
internal sealed class ArenaTranslocatorHud : MonoBehaviour
{
    private GUIStyle _style;
    private void OnGUI()
    {
        if (Settings.HudEnabled?.Value != true || Settings.CombatHudEnabled?.Value != true) return;
        var status = ArenaTranslocatorRuntime.HeldStatus();
        if (status == null) return;
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        float scale = Mathf.Clamp(Screen.height / 1080f, .75f, 2f);
        _style.fontSize = Mathf.RoundToInt(13f * scale);
        _style.normal.textColor = new Color(.35f, .9f, 1f, .9f);
        GUI.Label(new Rect(Screen.width * .5f - 130f * scale, Screen.height * .61f, 260f * scale, 24f * scale), status, _style);
        var before = GUI.color;
        try
        {
            GUI.color = _style.normal.textColor;
            GUI.DrawTexture(new Rect(Screen.width * .5f - 1.5f * scale, Screen.height * .5f - 1.5f * scale,
                3f * scale, 3f * scale), Texture2D.whiteTexture);
        }
        finally { GUI.color = before; }
    }
}
