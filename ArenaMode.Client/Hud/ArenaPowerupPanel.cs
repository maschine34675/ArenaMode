using ArenaMode.Pickups;
using Comfort.Common;
using EFT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

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
        _rect.sizeDelta = new Vector2(880f, 52f);
        var background = _root.AddComponent<Image>();
        background.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .86f); background.raycastTarget = false;
        ArenaUiTheme.Decorate(_rect);
        _text = HudStyle.CreateText(_rect, "Powerups", 19f, TextAlignmentOptions.Center);
        _text.rectTransform.anchorMin = Vector2.zero; _text.rectTransform.anchorMax = Vector2.one;
        _text.rectTransform.offsetMin = new Vector2(8f, 3f); _text.rectTransform.offsetMax = new Vector2(-8f, -3f);
        _root.SetActive(false);
    }

    internal void Tick()
    {
        if (_root == null) return;
        _rect.anchoredPosition = ArenaHudStackLayout.PowerupPosition(Modes.ArenaMatchService.Current);
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .1f;
        var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
        if (!ArenaPickupRuntime.IsLiveActor(player)) { _root.SetActive(false); return; }
        var state = ArenaPowerups.Read(player);
        var effects = new List<string>(4);
        if (state.DamageSeconds > 0) effects.Add("<color=#FF70BD>DOUBLE DAMAGE  " + Mathf.CeilToInt(state.DamageSeconds) + "s</color>");
        if (state.ShieldSeconds > 0 && state.ShieldPoints > 0)
            effects.Add("<color=#70DFFF>SHIELD  " + Mathf.CeilToInt(state.ShieldPoints) + "  |  " + Mathf.CeilToInt(state.ShieldSeconds) + "s</color>");
        if (state.JumpSeconds > 0)
            effects.Add("<color=#80FF60>JUMP BOOTS  " + Mathf.CeilToInt(state.JumpSeconds) + "s</color>");
        if (state.AmmoSeconds > 0)
            effects.Add("<color=#FF8C40>INFINITE AMMO  " + Mathf.CeilToInt(state.AmmoSeconds) + "s</color>");
        string label = string.Join("     ", effects.Take(2));
        if (effects.Count > 2) label += "\n" + string.Join("     ", effects.Skip(2));
        if (Time.unscaledTime < ArenaPickupRuntime.NoticeUntil)
            label += (label.Length > 0 ? "\n" : "") + "<size=14>" + ArenaPickupRuntime.LastNotice + "</size>";
        _text.text = label;
        _rect.sizeDelta = new Vector2(880f, effects.Count > 2 ? 78f : 52f);
        _root.SetActive(label.Length > 0);
    }
}
