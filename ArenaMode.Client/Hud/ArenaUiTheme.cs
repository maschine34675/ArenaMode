using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Arena-owned presentation only. One quiet palette across TMP HUD, native-menu
/// additions and IMGUI windows. Team/effect colours and saved user colours keep
/// their separate semantic owners. No config, shared-material writes or input.
/// </summary>
internal static class ArenaUiTheme
{
    internal static readonly Color Surface = new Color(.055f, .071f, .09f, 1f);
    internal static readonly Color Raised = new Color(.10f, .125f, .15f, 1f);
    internal static readonly Color Hover = new Color(.17f, .20f, .23f, 1f);
    internal static readonly Color Text = new Color(.92f, .94f, .96f, 1f);
    internal static readonly Color Muted = new Color(.66f, .71f, .76f, 1f);
    internal static readonly Color Accent = new Color(.79f, .75f, .62f, 1f);
    internal static readonly Color Edge = new Color(.52f, .60f, .67f, .24f);
    internal static readonly Color Positive = new Color(.53f, .84f, .66f, 1f);
    internal static readonly Color Danger = new Color(.96f, .40f, .38f, 1f);
    internal static readonly Color Armor = new Color(.46f, .77f, .94f, 1f);
    internal static readonly Color Headshot = new Color(1f, .64f, .48f, 1f);

    internal static Color WithAlpha(Color color, float alpha)
    { color.a = alpha; return color; }

    // These primitives belong to their caller's hierarchy, with no registry or
    // retained texture. Destroying the HUD/menu destroys all owned decoration.
    internal static Image Fill(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    internal static Image Rule(RectTransform parent, string name, Color color,
        float top = 0f, float inset = 0f, float thickness = 1f)
    {
        var line = Fill(parent, name, color);
        var rect = (RectTransform)line.transform;
        rect.SetAsLastSibling(); // Above an earlier surface, still noninteractive.
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.offsetMin = new Vector2(inset, -top - thickness);
        rect.offsetMax = new Vector2(-inset, -top);
        return line;
    }

    internal static void Decorate(RectTransform parent)
    {
        Rule(parent, "Arena top edge", WithAlpha(Accent, .42f));
    }

    // Applied only to our cloned menu rows, never to the shared native prefab.
    // No outline setter here: those views are built under an inactive parent.
    internal static void StyleButton(Button button, TMP_Text label)
    {
        label.color = Text;
        label.fontStyle = FontStyles.Normal;
        var image = button.targetGraphic as Image;
        if (image == null) return;
        image.color = Color.white;
        var colors = button.colors;
        colors.normalColor = WithAlpha(Raised, .92f);
        colors.highlightedColor = Hover;
        colors.pressedColor = WithAlpha(Accent, .60f);
        colors.selectedColor = Hover;
        colors.disabledColor = WithAlpha(Raised, .30f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = .12f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
    }
}
