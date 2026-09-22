using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>Persistent, noninteractive wait status below the score and above the crosshair.</summary>
internal sealed class ArenaAssemblyBanner
{
    private GameObject _root;
    private RectTransform _rect;
    private TextMeshProUGUI _heading, _detail;
    private float _nextRefresh;

    public void Create(RectTransform parent)
    {
        _root = new GameObject("ArenaAssemblyBanner", typeof(RectTransform));
        var rect = _rect = (RectTransform)_root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = Position;
        rect.sizeDelta = new Vector2(760f, 90f);
        var backdrop = _root.AddComponent<Image>();
        backdrop.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .94f);
        backdrop.raycastTarget = false;
        ArenaUiTheme.Decorate(rect);

        _heading = HudStyle.CreateText(rect, "AssemblyHeading", 30f, TextAlignmentOptions.Center);
        _heading.fontStyle = FontStyles.Bold;
        _heading.color = ArenaUiTheme.Accent;
        Place(_heading.rectTransform, -23f, 38f);
        _detail = HudStyle.CreateText(rect, "AssemblyDetail", 17f, TextAlignmentOptions.Center);
        _detail.color = ArenaUiTheme.Muted;
        Place(_detail.rectTransform, -62f, 28f);
        _root.SetActive(false);
    }

    private static void Place(RectTransform rect, float y, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(728f, height);
    }

    private static Vector2 Position => ScorePanel.Position - new Vector2(0f, ScorePanel.PanelHeight + 20f);

    public void Tick(ArenaMatch match)
    {
        if (_root == null) return;
        var position = Position;
        if (_rect.anchoredPosition != position)
            _rect.anchoredPosition = position;
        string heading = null, detail = null;
        bool notice = match?.Mode is TacticalMode tactical
            ? tactical.TryGetAssemblyNotice(out heading, out detail)
            : match?.Mode is RoundsMode dm ? dm.TryGetMatchNotice(out heading, out detail)
            : match?.Mode is TeamDeathmatchMode teams && teams.TryGetMatchNotice(out heading, out detail);
        if (!ArenaSession.IsActive || !ReferenceEquals(match, ArenaMatchService.Current)
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || !ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)
            // The open scoreboard already owns heading, detail and exit timer.
            // Keep preparation notices; they are not duplicated result panels.
            || (ArenaPanel.IsVisible && ArenaMatchResult.HasResult(match?.Mode))
            || !notice)
        {
            // Never wait for the refresh timer to remove an obsolete notice.
            _root.SetActive(false);
            _nextRefresh = 0f;
            return;
        }
        if (_root.activeSelf && Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + .2f;
        _heading.text = heading;
        _detail.text = detail;
        _root.SetActive(true);
    }
}
