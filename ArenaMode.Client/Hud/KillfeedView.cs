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
    private const float FeedWidth = 460f;
    private const float HorizontalPadding = 12f;
    private const float VerticalPadding = 6f;
    private const float MinLineHeight = 32f;
    private const float LineGap = 4f;

    private static Vector2 Position => new Vector2(
        24f + Settings.KillfeedOffsetX.Value, -(96f + Settings.KillfeedOffsetY.Value));

    private sealed class Line
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Text;
        public float ShownAt;
    }

    private readonly Line[] _pool = new Line[MaxLines];
    private readonly List<Line> _live = new List<Line>(MaxLines);
    private RectTransform _root;

    public void Create(RectTransform hudRoot)
    {
        var go = new GameObject("ArenaKillfeed", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(hudRoot, false);
        // Leave the top-right corner free for DynamicMaps' default minimap.
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = Position;
        _root.sizeDelta = new Vector2(FeedWidth, 0f);

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
            backdrop.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .90f);
            backdrop.raycastTarget = false;
            ArenaUiTheme.Rule(rect, "Feed edge", ArenaUiTheme.Edge);
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
            textRect.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
            textRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);

            lineGo.SetActive(false);
            _pool[i] = new Line
            {
                Root = rect,
                Group = group,
                Text = text,
            };
        }
    }

    public void Push(ArenaKill kill)
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

        line.Text.text = KillfeedText.Format(kill,
            TeamHtmlColor(Settings.FriendlyTracerColor.Value),
            TeamHtmlColor(Settings.OpponentTracerColor.Value));
        // Measure actual font metrics and wrapped lines, not a fixed character
        // count or row height. The explicit width also works for an inactive
        // pooled row; its TMP component was initialized while active in Create.
        var preferred = line.Text.GetPreferredValues(FeedWidth - 2f * HorizontalPadding, Mathf.Infinity);
        line.Root.sizeDelta = new Vector2(FeedWidth,
            Mathf.Max(MinLineHeight, Mathf.Ceil(preferred.y) + 2f * VerticalPadding));
        line.ShownAt = Time.unscaledTime;
        line.Group.alpha = 1f;
        line.Root.gameObject.SetActive(true);
        _live.Add(line);
        Relayout();
    }

    // Tracers use HDR RGB values; normalize their hue like the friendly marker
    // before writing opaque UI text. Read settings for each new entry only.
    private static string TeamHtmlColor(ArenaTracerColor color)
        => "#" + ColorUtility.ToHtmlStringRGB(
            ArenaFriendlyMarkerPolicy.Color(ArenaTracerPalette.Resolve(color)));

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
