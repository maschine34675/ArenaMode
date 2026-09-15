using ArenaMode.Pickups;
using Comfort.Common;
using EFT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

internal sealed class ArenaPowerupPanel
{
    private GameObject _root;
    private RectTransform _rect;
    private TextMeshProUGUI _text;
    private float _next;

    internal void Create(RectTransform parent)
    {
        _root = new GameObject("ArenaPowerupPanel", typeof(RectTransform));
        _rect = (RectTransform)_root.transform;
        _rect.SetParent(parent, false);
        _rect.anchorMin = _rect.anchorMax = _rect.pivot = new Vector2(.5f, 1f);
        _rect.sizeDelta = new Vector2(660f, 52f);
        var background = _root.AddComponent<Image>();
        background.color = new Color(.03f, .04f, .06f, .85f); background.raycastTarget = false;
        _text = HudStyle.CreateText(_rect, "Powerups", 19f, TextAlignmentOptions.Center);
        _text.rectTransform.anchorMin = Vector2.zero; _text.rectTransform.anchorMax = Vector2.one;
        _text.rectTransform.offsetMin = new Vector2(8f, 3f); _text.rectTransform.offsetMax = new Vector2(-8f, -3f);
        _root.SetActive(false);
    }

    internal void Tick()
    {
        if (_root == null) return;
        _rect.anchoredPosition = ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + 16f);
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .1f;
        var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        if (!ArenaPickupRuntime.IsLiveActor(player)) { _root.SetActive(false); return; }
        var state = ArenaPowerups.Read(player);
        string label = "";
        if (state.DamageSeconds > 0) label = "<color=#FF70BD>DOUBLE DAMAGE  " + Mathf.CeilToInt(state.DamageSeconds) + "s</color>";
        if (state.ShieldSeconds > 0 && state.ShieldPoints > 0)
            label += (label.Length > 0 ? "     " : "") + "<color=#70DFFF>SHIELD  "
                + Mathf.CeilToInt(state.ShieldPoints) + "  |  " + Mathf.CeilToInt(state.ShieldSeconds) + "s</color>";
        if (Time.unscaledTime < ArenaPickupRuntime.NoticeUntil)
            label += (label.Length > 0 ? "\n" : "") + "<size=14>" + ArenaPickupRuntime.LastNotice + "</size>";
        _text.text = label;
        _root.SetActive(label.Length > 0);
    }
}
