using ArenaMode.Combat;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Top-right pooled killfeed: newest at the bottom, opaque hold then per-line fade.
/// Behavior adapted from DragonDen.Killfeed (MIT, (c) Drexira); rendering is
/// our own uGUI line pool, role colors instead of faction icon sprites.
/// </summary>
internal sealed class KillfeedView
{
    private const int MaxLines = 6;
    private const float LineSeconds = 6f;
    private const float FadeSeconds = 1.5f;
    private const float LineHeight = 32f;
    private const float LineGap = 4f;

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
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
        _root.anchoredPosition = new Vector2(-24f, -96f);
        _root.sizeDelta = new Vector2(460f, MaxLines * (LineHeight + LineGap));

        for (var i = 0; i < MaxLines; i++)
        {
            var lineGo = new GameObject("Line" + i, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)lineGo.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(460f, LineHeight);

            // Background and text share the pooled line's fade and lifetime.
            // Create TMP while the hierarchy is active, then hide the finished row.
            var backdrop = lineGo.AddComponent<Image>();
            backdrop.color = new Color(.03f, .04f, .05f, .88f);
            backdrop.raycastTarget = false;
            var group = lineGo.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var text = HudStyle.CreateText(rect, "Text", 18f, TextAlignmentOptions.MidlineRight);
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.overflowMode = TextOverflowModes.Ellipsis;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 3f);
            textRect.offsetMax = new Vector2(-12f, -3f);

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

        line.Text.text = Format(kill);
        line.ShownAt = Time.unscaledTime;
        line.Group.alpha = 1f;
        line.Root.gameObject.SetActive(true);
        _live.Add(line);
        Relayout();
    }

    public void Tick()
    {
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
        for (var i = 0; i < _live.Count; i++)
            _live[i].Root.anchoredPosition = new Vector2(0f, -i * (LineHeight + LineGap));
    }

    private static string Format(ArenaKill kill)
    {
        if (kill.LocalIsVictim)
            return $"<color=#FF8585>{kill.KillerName} [{kill.KillerRoleLabel}] killed YOU</color>";

        var weapon = string.IsNullOrEmpty(kill.WeaponName) ? string.Empty : $"{kill.WeaponName} · ";
        var headshot = kill.Headshot ? "<color=#FF8585>HS</color> · " : string.Empty;
        var victim = $"{kill.VictimName} <color={RoleColor(kill.VictimRole)}>[{kill.VictimRoleLabel}]</color>";

        if (kill.KilledByLocal)
            return $"{headshot}{weapon}{victim} · {Mathf.RoundToInt(kill.Distance)}m";

        return $"<color=#D5DAE0>{kill.KillerName} ➔ {victim}</color>";
    }

    private static string RoleColor(ArenaRole role)
    {
        switch (role)
        {
            case ArenaRole.Pmc: return "#7FDBFF";
            case ArenaRole.Boss: return "#FF851B";
            case ArenaRole.Raider: return "#FFDC00";
            case ArenaRole.Follower: return "#FFDC00";
            default: return "#DDDDDD";
        }
    }
}
