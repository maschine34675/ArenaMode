using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Effects;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Top-left pooled killfeed: newest at the bottom, opaque hold then per-line fade.
/// Behavior adapted from DragonDen.Killfeed (MIT, (c) Drexira); rendering is
/// our own uGUI line pool, player-relative team colors instead of faction icons.
/// </summary>
internal sealed class KillfeedView
{
    private const int MaxLines = 6;
    private const float LineSeconds = 6f;
    private const float FadeSeconds = 1.5f;
    // Rendered weapons need a little more width; keep the text column unchanged.
    private const float FeedWidth = 540f;
    private const float IconRail = 80f;
    private const float HorizontalPadding = 12f;
    private const float VerticalPadding = 6f;
    private const float MinLineHeight = 56f;
    private const float LineGap = 4f;

    private static Vector2 Position => new Vector2(
        24f + Settings.KillfeedOffsetX.Value, -(96f + Settings.KillfeedOffsetY.Value));

    private sealed class Line
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Text;
        public Image Method;
        public Image Headshot;
        public float ShownAt;
    }

    private readonly Line[] _pool = new Line[MaxLines];
    private readonly List<Line> _live = new List<Line>(MaxLines);
    private RectTransform _root;
    private ArenaUiAssets _assets;

    public void Create(RectTransform hudRoot)
    {
        var go = new GameObject("ArenaKillfeed", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(hudRoot, false);
        // Leave the top-right corner free for DynamicMaps' default minimap.
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = Position;
        _root.sizeDelta = new Vector2(FeedWidth, 0f);
        _assets = go.AddComponent<ArenaUiAssetOwner>().Assets;
        // Bounded one-time loads, never create textures at combat-event rate.
        foreach (ArenaUiIcon icon in System.Enum.GetValues(typeof(ArenaUiIcon)))
            if (icon != ArenaUiIcon.TeamPanel) _assets.Sprite(icon);

        for (var i = 0; i < MaxLines; i++)
        {
            var lineGo = new GameObject("Line" + i, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)lineGo.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(FeedWidth, MinLineHeight);

            // Background and text share the pooled line's fade and lifetime.
            // Create TMP while the hierarchy is active, then hide the finished row.
            var backdrop = lineGo.AddComponent<Image>();
            backdrop.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .94f);
            backdrop.raycastTarget = false;
            ArenaUiTheme.Rule(rect, "Feed edge", ArenaUiTheme.Edge);
            var rail = ArenaUiTheme.Fill(rect, "Icon rail", ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .9f));
            rail.rectTransform.SetAsLastSibling();
            rail.rectTransform.anchorMax = new Vector2(0f, 1f);
            rail.rectTransform.offsetMin = Vector2.zero;
            rail.rectTransform.offsetMax = new Vector2(IconRail - 4f, 0f);
            var method = CreateIcon(rect, "Kill method", new Vector2(38f, -20f), new Vector2(44f, 22f));
            var headshot = CreateIcon(rect, "Headshot", new Vector2(38f, -46f), new Vector2(18f, 18f));
            headshot.color = ArenaUiTheme.Headshot;
            var group = lineGo.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var text = HudStyle.CreateText(rect, "Text", 18f, TextAlignmentOptions.TopLeft);
            text.color = ArenaUiTheme.Text;
            text.fontStyle = FontStyles.Normal;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(IconRail + HorizontalPadding, VerticalPadding);
            textRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);

            lineGo.SetActive(false);
            _pool[i] = new Line
            {
                Root = rect,
                Group = group,
                Text = text,
                Method = method,
                Headshot = headshot,
            };
        }
    }

    public void Push(ArenaKill kill)
        => Push(KillfeedText.Format(kill,
            ArenaTeamPalette.FriendlyHex,
            ArenaTeamPalette.OpponentHex), KillfeedIcon.Select(kill), kill.Headshot);

    internal void PushUncreditedDeath(ArenaUncreditedDeath death)
        => Push(KillfeedText.FormatUncreditedDeath(death,
            ArenaTeamPalette.FriendlyHex,
            ArenaTeamPalette.OpponentHex), KillfeedIcon.SelectDeath(death), false);

    private void Push(string text, ArenaUiIcon method, bool headshot)
    {
        Line line;
        if (_live.Count < MaxLines)
        {
            line = _pool[IndexOfFreeLine()];
        }
        else
        {
            line = _live[0];
            _live.RemoveAt(0);
        }

        line.Text.text = text;
        var hazard = method == ArenaUiIcon.Bleeding || method == ArenaUiIcon.BarbedWire;
        line.Method.sprite = _assets.Sprite(method);
        line.Method.enabled = line.Method.sprite != null;
        line.Method.color = hazard ? ArenaUiTheme.Danger : Color.white;
        // Reset dimensions on every pooled reuse, including weapon -> hazard.
        line.Method.rectTransform.sizeDelta = method switch
        {
            ArenaUiIcon.AssaultRifle or ArenaUiIcon.Carbine or ArenaUiIcon.Sniper or
            ArenaUiIcon.Marksman or ArenaUiIcon.Machinegun or ArenaUiIcon.Shotgun or
            ArenaUiIcon.Pistol or ArenaUiIcon.Smg => new Vector2(64f, 32f),
            _ => new Vector2(44f, 22f)
        };
        // A delayed bleed keeps native LastBodyPart from the preceding hit.
        // Keep scoring metadata intact, but never depict that as a lethal headshot.
        line.Headshot.sprite = headshot && !hazard ? _assets.Sprite(ArenaUiIcon.Headshot) : null;
        line.Headshot.enabled = line.Headshot.sprite != null;
        // Measure actual font metrics and wrapped lines, not a fixed character
        // count or row height. The explicit width also works for an inactive
        // pooled row; its TMP component was initialized while active in Create.
        var preferred = line.Text.GetPreferredValues(FeedWidth - IconRail - 2f * HorizontalPadding, Mathf.Infinity);
        line.Root.sizeDelta = new Vector2(FeedWidth,
            Mathf.Max(MinLineHeight, Mathf.Ceil(preferred.y) + 2f * VerticalPadding));
        line.ShownAt = Time.unscaledTime;
        line.Group.alpha = 1f;
        line.Root.gameObject.SetActive(true);
        _live.Add(line);
        Relayout();
    }

    private static Image CreateIcon(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var icon = go.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;
        return icon;
    }

    public void Tick()
    {
        if (_root == null)
            return;

        // Settings move the complete feed, including existing rows, without
        // resetting their fade, lifetime or individual wrapped-line layout.
        var position = Position;
        if (_root.anchoredPosition != position)
            _root.anchoredPosition = position;

        var now = Time.unscaledTime;
        var changed = false;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var age = now - _live[i].ShownAt;
            if (age > LineSeconds)
            {
                _live[i].Root.gameObject.SetActive(false);
                _live.RemoveAt(i);
                changed = true;
                continue;
            }

            // Keep each entry readable for 4.5 seconds; fade only at the end.
            _live[i].Group.alpha = 1f - Mathf.Clamp01((age - (LineSeconds - FadeSeconds)) / FadeSeconds);
        }

        if (changed)
            Relayout();
    }

    private int IndexOfFreeLine()
    {
        for (var i = 0; i < MaxLines; i++)
        {
            if (!_live.Contains(_pool[i]))
                return i;
        }

        return 0;
    }

    private void Relayout()
    {
        var top = 0f;
        for (var i = 0; i < _live.Count; i++)
        {
            _live[i].Root.anchoredPosition = new Vector2(0f, -top);
            top += _live[i].Root.sizeDelta.y + LineGap;
        }
        _root.sizeDelta = new Vector2(FeedWidth, Mathf.Max(0f, top - LineGap));
    }
}
