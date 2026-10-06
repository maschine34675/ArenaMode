using ArenaMode.Config;
using ArenaMode.Modes;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Top-center: kills/deaths/score plus streak, elapsed time and mode line.</summary>
internal sealed class ScorePanel
{
    private const float RefreshInterval = 0.25f;
    // Reserve the top strip for the quick-access bar (1920x1080 HUD units).
    internal const float TopOffset = 110f;
    internal const float PanelHeight = 84f;

    // Shared with the assembly notice so live F12 adjustments keep their gap.
    internal static Vector2 Position => new Vector2(
        Settings.ScorePanelOffsetX.Value, -(TopOffset + Settings.ScorePanelOffsetY.Value));

    private RectTransform _root;
    private TextMeshProUGUI _mainLine;
    private TextMeshProUGUI _subLine;
    private TextMeshProUGUI _comparisonLine;
    private TextMeshProUGUI _rivalScore;
    private float _nextRefreshAt;
    private int _assistPoints;
    private float _assistUntil;

    public void ShowAssist(int points)
    {
        if (points <= 0) return;
        _assistPoints = Time.unscaledTime < _assistUntil ? _assistPoints + points : points;
        _assistUntil = Time.unscaledTime + 2.5f;
        _nextRefreshAt = 0f;
    }

    public void Create(RectTransform hudRoot)
    {
        var go = new GameObject("ArenaScorePanel", typeof(RectTransform));
        _root = (RectTransform)go.transform;
        _root.SetParent(hudRoot, false);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f);
        _root.anchoredPosition = Position;
        _root.sizeDelta = new Vector2(760f, PanelHeight);
        ArenaUiTheme.Fill(_root, "Score surface", ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .86f));
        ArenaUiTheme.Decorate(_root);

        _mainLine = HudStyle.CreateText(_root, "Main", 24f, TextAlignmentOptions.Top);
        var mainRect = _mainLine.rectTransform;
        mainRect.anchorMin = new Vector2(0f, 1f);
        mainRect.anchorMax = new Vector2(1f, 1f);
        mainRect.pivot = new Vector2(0.5f, 1f);
        mainRect.anchoredPosition = new Vector2(0f, -9f);
        mainRect.sizeDelta = new Vector2(-32f, 30f);
        _mainLine.enableAutoSizing = true;
        _mainLine.fontSizeMin = 18f;
        _mainLine.fontSizeMax = 24f;

        _subLine = HudStyle.CreateText(_root, "Sub", 16f, TextAlignmentOptions.Top);
        _subLine.color = ArenaUiTheme.Muted;
        _subLine.enableAutoSizing = true;
        _subLine.fontSizeMin = 13f;
        _subLine.fontSizeMax = 16f;
        var subRect = _subLine.rectTransform;
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -42f);
        subRect.sizeDelta = new Vector2(-32f, 19f);
        _subLine.overflowMode = TextOverflowModes.Ellipsis;

        // Keep the comparison on its own row. The rival name may ellipsize,
        // but the independently sized score must never wrap or disappear with it.
        _comparisonLine = HudStyle.CreateText(_root, "Comparison", 16f, TextAlignmentOptions.Top);
        _comparisonLine.color = ArenaUiTheme.Muted;
        _comparisonLine.overflowMode = TextOverflowModes.Ellipsis;
        _rivalScore = HudStyle.CreateText(_root, "Rival score", 16f, TextAlignmentOptions.TopLeft);
        _rivalScore.color = new Color32(255, 107, 107, 255);
        _rivalScore.richText = false;
        foreach (var text in new[] { _comparisonLine, _rivalScore })
        {
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -62f);
            rect.sizeDelta = new Vector2(0f, 19f);
        }
    }

    public void Tick(ArenaMatch match)
    {
        if (_root == null || match == null)
            return;

        // Position responds independently of the slower score/text refresh.
        var position = Position;
        if (_root.anchoredPosition != position)
            _root.anchoredPosition = position;

        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshInterval;

        // TMP's SetText formatter does not know .NET specifiers like n0 — build
        // the string ourselves (4 Hz refresh, allocation is irrelevant).
        _mainLine.text =
            $"<b>{match.Kills}</b> <size=13>KILLS</size>   {match.Deaths} <size=13>DEATHS</size>   {match.Assists} <size=13>ASSISTS</size>   <color=#C9BF9E><b>{match.Score:n0}</b></color> <size=13>PTS</size>"
            + (Time.unscaledTime < _assistUntil ? $"   <color=#A8E6A3>ASSIST +{_assistPoints}</color>" : string.Empty);

        var elapsed = match.Mode is TeamDeathmatchMode teams ? teams.PlayingElapsed : match.Elapsed;
        var minutes = (int)(elapsed / 60f);
        var seconds = (int)(elapsed % 60f);
        var streakPart = match.Streak >= 2
            ? $"Streak {match.Streak} (Best {match.BestStreak})   "
            : string.Empty;
        var modeLine = match.Mode?.HudStatusLine;
        var modePart = string.IsNullOrEmpty(modeLine) ? string.Empty : $"   {modeLine}";
        _subLine.text = $"{streakPart}{minutes:00}:{seconds:00}{modePart}";
        _comparisonLine.text = Comparison(match, out var rivalScore) ?? string.Empty;
        _rivalScore.text = rivalScore ?? string.Empty;
        LayoutComparison();
    }

    private void LayoutComparison()
    {
        bool hasComparison = !string.IsNullOrEmpty(_comparisonLine.text);
        _subLine.enableWordWrapping = !hasComparison;
        _subLine.rectTransform.sizeDelta = new Vector2(-32f, hasComparison ? 19f : 38f);
        float width = Mathf.Max(0f, _root.rect.width - 32f);
        float scoreWidth = string.IsNullOrEmpty(_rivalScore.text) ? 0f
            : Mathf.Min(width, _rivalScore.GetPreferredValues(_rivalScore.text, Mathf.Infinity, Mathf.Infinity).x + 2f);
        float gap = scoreWidth > 0f ? 8f : 0f;
        float nameWidth = scoreWidth > 0f
            ? Mathf.Min(Mathf.Max(0f, width - scoreWidth - gap),
                _comparisonLine.GetPreferredValues(_comparisonLine.text, Mathf.Infinity, Mathf.Infinity).x + 2f)
            : width;
        float total = nameWidth + gap + scoreWidth;
        _comparisonLine.alignment = scoreWidth > 0f ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Top;
        _comparisonLine.rectTransform.sizeDelta = new Vector2(nameWidth, 19f);
        _comparisonLine.rectTransform.anchoredPosition = new Vector2((nameWidth - total) * .5f, -62f);
        _rivalScore.rectTransform.sizeDelta = new Vector2(scoreWidth, 19f);
        _rivalScore.rectTransform.anchoredPosition = new Vector2((total - scoreWidth) * .5f, -62f);
    }

    private static string Comparison(ArenaMatch match, out string rivalScore)
    {
        rivalScore = null;
        if (match?.Scoreboard == null)
            return null;

        if (match.Mode is TeamDeathmatchMode)
            return null; // faction kill totals already appear in its status line

        // Zombie's SynchronizeScoreboard early return (same reason as TDM/
        // Tactical above) leaves no opponent roster for Leader() to find — a
        // permanent "Waiting for opponents" line would be misleading. The
        // wave/remaining count already appears in its own status line.
        if (match.Mode is ZombieMode)
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
        if (leader != null) rivalScore = $"{leader.Score:n0}";
        return leader == null
            ? "Waiting for opponents"
            : $"Rival {leader.DisplayName}";
    }
}
