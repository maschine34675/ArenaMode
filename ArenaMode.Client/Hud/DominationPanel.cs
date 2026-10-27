using ArenaMode.Modes;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

internal sealed class DominationPanel
{
    private GameObject _root;
    private RectTransform _rect;
    private TextMeshProUGUI _text;

    internal void Create(RectTransform parent)
    {
        _root = new GameObject("ArenaControlPointRow", typeof(RectTransform));
        _rect = (RectTransform)_root.transform;
        _rect.SetParent(parent, false);
        _rect.anchorMin = _rect.anchorMax = _rect.pivot = new Vector2(.5f, 1f);
        _rect.anchoredPosition = new Vector2(0f, -ScorePanel.PanelHeight);
        _rect.sizeDelta = new Vector2(760f, ArenaHudStackLayout.ObjectiveHeight);
        _text = HudStyle.CreateText(_rect, "ControlPoints", 20f, TextAlignmentOptions.Center);
        _text.rectTransform.anchorMin = Vector2.zero; _text.rectTransform.anchorMax = Vector2.one;
        _text.rectTransform.offsetMin = new Vector2(10, 4); _text.rectTransform.offsetMax = new Vector2(-10, -4);
        _text.enableWordWrapping = false;
        _text.enableAutoSizing = true;
        _text.fontSizeMin = 16f;
        _text.fontSizeMax = 20f;
        _root.SetActive(false);
    }

    internal void Tick(ArenaMatch match)
    {
        if (_root == null) return;
        bool show = ArenaHudStackLayout.ObjectivesVisible(match);
        _root.SetActive(show);
        if (!show) return;
        _text.text = ((TeamDeathmatchMode)match.Mode).ObjectiveStatusLine;
    }
}
