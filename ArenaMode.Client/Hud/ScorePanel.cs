using ArenaMode.Modes;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Top-center: kills/deaths/score plus streak, elapsed time and mode line.</summary>
internal sealed class ScorePanel
{
    private const float RefreshInterval = 0.25f;

    private RectTransform _root;
    private TextMeshProUGUI _mainLine;
    private TextMeshProUGUI _subLine;
    private float _nextRefreshAt;

    public void Create(RectTransform hudRoot)
    {
        var go = new GameObject("ArenaScorePanel", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(hudRoot, false);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f);
        _root.anchoredPosition = new Vector2(0f, -18f);
        _root.sizeDelta = new Vector2(900f, 60f);

        _mainLine = HudStyle.CreateText(_root, "Main", 24f, TextAlignmentOptions.Top);
        var mainRect = _mainLine.rectTransform;
        mainRect.anchorMin = new Vector2(0f, 1f);
        mainRect.anchorMax = new Vector2(1f, 1f);
        mainRect.pivot = new Vector2(0.5f, 1f);
        mainRect.anchoredPosition = Vector2.zero;
        mainRect.sizeDelta = new Vector2(0f, 30f);

        _subLine = HudStyle.CreateText(_root, "Sub", 16f, TextAlignmentOptions.Top);
        _subLine.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        var subRect = _subLine.rectTransform;
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -30f);
        subRect.sizeDelta = new Vector2(0f, 22f);
    }

    public void Tick(ArenaMatch match)
    {
        if (_root == null || match == null || Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;

        // TMP's SetText formatter does not know .NET specifiers like n0 — build
        // the string ourselves (4 Hz refresh, allocation is irrelevant).
        _mainLine.text =
            $"<b>{match.Kills}</b> Kills   {match.Deaths} Deaths   <color=#7FDBFF>{match.Score:n0}</color>";

        var elapsed = match.Elapsed;
        var minutes = (int)(elapsed / 60f);
        var seconds = (int)(elapsed % 60f);
        var streakPart = match.Streak >= 2
            ? $"Streak {match.Streak} (Best {match.BestStreak})   "
            : string.Empty;
        var modeLine = match.Mode?.HudStatusLine;
        var modePart = string.IsNullOrEmpty(modeLine) ? string.Empty : $"   {modeLine}";
        var comparison = Comparison(match);
        var comparisonPart = string.IsNullOrEmpty(comparison) ? string.Empty : $"   {comparison}";
        _subLine.text = $"{streakPart}{minutes:00}:{seconds:00}{modePart}{comparisonPart}";
    }

    private static string Comparison(ArenaMatch match)
    {
        if (match?.Scoreboard == null)
            return null;

        if (match.Mode is TacticalMode tactical)
        {
            var friendly = match.Scoreboard.Totals(ArenaScoreboardSide.Friendly);
            var opponent = match.Scoreboard.Totals(ArenaScoreboardSide.Opponent);
            return tactical.IsTeamArena
                ? $"Team pts <color=#7FDBFF>{friendly.Score:n0}</color>:<color=#FF6B6B>{opponent.Score:n0}</color>"
                : $"Opposition <color=#FF6B6B>{opponent.Score:n0}</color> pts";
        }

        ArenaScoreboardEntry leader = match.Scoreboard.Leader(ArenaScoreboardSide.Opponent);
        return leader == null
            ? "Waiting for opponents"
            : $"Rival {leader.DisplayName} <color=#FF6B6B>{leader.Score:n0}</color>";
    }
}
