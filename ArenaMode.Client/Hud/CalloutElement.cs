using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>UT-style center callout ("DOUBLE KILL") — brief, big, fading.</summary>
internal sealed class CalloutElement
{
    private const float HoldSeconds = 1.6f;

    private TextMeshProUGUI _text;
    private float _shownAt = -10f;

    public void Create(RectTransform hudRoot)
    {
        _text = HudStyle.CreateText(hudRoot, "ArenaCallout", 44f, TextAlignmentOptions.Center);
        _text.color = new Color(1f, 0.85f, 0.25f, 0f);
        _text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        var rect = _text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 180f);
        rect.sizeDelta = new Vector2(800f, 60f);
    }

    public void Show(string label)
    {
        if (_text == null || string.IsNullOrEmpty(label))
            return;

        _text.text = label;
        _shownAt = Time.unscaledTime;
    }

    public void Tick()
    {
        if (_text == null)
            return;

        var age = Time.unscaledTime - _shownAt;
        var alpha = age >= HoldSeconds
            ? 0f
            : age < 0.15f
                ? age / 0.15f
                : 1f - Mathf.Clamp01((age - 0.9f) / (HoldSeconds - 0.9f));

        var color = _text.color;
        color.a = alpha;
        _text.color = color;
    }
}
