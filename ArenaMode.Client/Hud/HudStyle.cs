using System;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// Shared HUD look (proven RaidInfoPanels pattern, same author): the EFT-bundled
/// TMP font with a thin dark outline over the 3D scene, no raycast targets.
/// </summary>
internal static class HudStyle
{
    private static TMP_FontAsset _font;
    private static bool _fontLoaded;

    public static TMP_FontAsset Font
    {
        get
        {
            if (_fontLoaded && _font != null)
                return _font;

            _fontLoaded = true;
            try
            {
                var fonts = Resources.LoadAll<TMP_FontAsset>("UI/Fonts");
                if (fonts != null && fonts.Length > 0)
                {
                    foreach (var candidate in fonts)
                    {
                        if (candidate != null
                            && candidate.name.IndexOf("BN SDF", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _font = candidate;
                            break;
                        }
                    }

                    if (_font == null)
                        _font = fonts[0];
                }
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena HUD font load failed: {ex.Message}");
            }

            if (_font == null)
                _font = TMP_Settings.defaultFontAsset;

            return _font;
        }
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, float fontSize,
        TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = Font;
        text.fontSize = fontSize;
        text.color = ArenaUiTheme.Text;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        ApplyOutline(text);
        return text;
    }

    internal static void ApplyOutline(TextMeshProUGUI text)
    {
        text.outlineWidth = 0.045f;
        text.outlineColor = new Color(0f, 0f, 0f, 0.7f);
    }
}
