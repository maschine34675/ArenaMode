using ArenaMode.Modes;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>Pure presentation: receives already selected rows, never owns ranking, totals or input.</summary>
internal sealed class ArenaScoreboardPresentation : IDisposable
{
    private readonly ArenaUiAssets _assets;
    private readonly GUIStyle _name, _body, _number, _score, _header, _title, _muted;
    private readonly Texture2D[] _headers;
    private readonly Texture2D _panel;
    internal ArenaScoreboardPresentation(GUIStyle body, GUIStyle header, ArenaUiAssets assets = null)
    {
        _assets = assets ?? new ArenaUiAssets();
        _name = Style(body, 14, TextAnchor.MiddleLeft, true);
        _name.fontStyle = FontStyle.Bold;
        _body = Style(body, 13, TextAnchor.MiddleLeft, true);
        _number = Style(body, 15, TextAnchor.MiddleCenter, false);
        _score = Style(body, 16, TextAnchor.MiddleRight, false);
        _score.fontStyle = FontStyle.Bold;
        _header = Style(header, 11, TextAnchor.MiddleLeft, false);
        _title = Style(body, 17, TextAnchor.MiddleLeft, false);
        _title.fontStyle = FontStyle.Bold;
        _muted = Style(body, 12, TextAnchor.MiddleLeft, true);
        _headers = new[] { _assets.Texture(ArenaUiIcon.Kill), _assets.Texture(ArenaUiIcon.Death),
            _assets.Texture(ArenaUiIcon.Assist), _assets.Texture(ArenaUiIcon.Headshot) };
        _panel = _assets.Texture(ArenaUiIcon.TeamPanel);
    }

    private static GUIStyle Style(GUIStyle source, int size, TextAnchor alignment, bool wrap)
        => new GUIStyle(source) { fontSize = size, alignment = alignment, wordWrap = wrap,
            richText = false, clipping = TextClipping.Clip,
            padding = new RectOffset(), margin = new RectOffset(), normal = { textColor = Color.white } };

    // Fixed semantic order; shrink the supporting columns together on small screens.
    internal static float[] Columns(float width)
    {
        width = Mathf.Max(1f, width);
        float scale = Mathf.Min(1f, width / 760f);
        float[] result = { 0f, 102f * scale, 54f * scale, 40f * scale, 40f * scale,
            40f * scale, 40f * scale, 110f * scale };
        float used = 0f;
        for (int i = 1; i < result.Length; i++) used += result[i];
        result[0] = width - used;
        return result;
    }

    internal void Draw(string heading, IReadOnlyList<ArenaScoreboardEntry> rows,
        Color sectionColor, Color friendly, Color opponent, float width)
    {
        width = Mathf.Max(180f, width);
        var banner = Reserve(width, 38f);
        if (Event.current.type == EventType.Repaint)
        {
            Fill(banner, ArenaUiTheme.WithAlpha(ArenaUiTheme.Raised, .92f));
            if (_panel != null) DrawTexture(banner, _panel, sectionColor);
            Fill(new Rect(banner.x, banner.y, 3f, banner.height), sectionColor);
            Label(new Rect(banner.x + 12f, banner.y, banner.width - 24f, banner.height), heading,
                _title, Color.Lerp(sectionColor, ArenaUiTheme.Text, .55f));
        }
        var widths = Columns(width - 20f);
        var header = Reserve(width, 32f);
        if (Event.current.type == EventType.Repaint)
        {
            Fill(header, ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .85f));
            string[] labels = { "NAME", "ROLE", "STATE", "K", "D", "A", "HS", "SCORE" };
            float x = header.x + 10f;
            for (int i = 0; i < widths.Length; i++)
            {
                var cell = new Rect(x, header.y, widths[i] - 4f, header.height);
                var icon = i >= 3 && i <= 6 ? _headers[i - 3] : null;
                if (icon != null && cell.width >= 30f)
                {
                    DrawTexture(new Rect(cell.x, cell.y + 8f, 16f, 16f), icon, ArenaUiTheme.Muted);
                    cell.x += 19f; cell.width -= 19f;
                }
                Label(cell, labels[i], _header, ArenaUiTheme.Muted);
                x += widths[i];
            }
        }
        if (rows == null || rows.Count == 0)
        {
            var empty = Reserve(width, 34f);
            if (Event.current.type == EventType.Repaint) Label(empty, "Waiting for roster...", _muted, ArenaUiTheme.Muted);
            return;
        }
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            string name = row.DisplayName + (row.IsLocal ? "  (YOU)" : "");
            var height = Mathf.Max(34f, Mathf.Max(
                _name.CalcHeight(new GUIContent(name), widths[0] - 8f),
                _body.CalcHeight(new GUIContent(row.RoleLabel), widths[1] - 8f)) + 12f);
            var rect = Reserve(width, height);
            if (Event.current.type != EventType.Repaint) continue;
            Color team = row.Side == ArenaScoreboardSide.Friendly ? friendly : opponent;
            Fill(rect, ArenaUiTheme.WithAlpha(index % 2 == 0 ? ArenaUiTheme.Raised : ArenaUiTheme.Surface, .85f));
            if (row.IsLocal)
            {
                Fill(rect, ArenaUiTheme.WithAlpha(team, .12f));
                Fill(new Rect(rect.x, rect.y, 3f, rect.height), team);
            }
            string[] values = { name, row.RoleLabel, row.IsActive ? "LIVE" : "OUT", row.Kills.ToString(),
                row.Deaths.ToString(), row.Assists.ToString(), row.Headshots.ToString(), row.Score.ToString("n0") };
            float x = rect.x + 10f;
            for (int i = 0; i < widths.Length; i++)
            {
                var cell = new Rect(x, rect.y + 6f, widths[i] - 8f, rect.height - 12f);
                var style = i == 0 ? _name : i < 3 ? _body : i == 7 ? _score : _number;
                Color color = i == 0 ? Color.Lerp(team, ArenaUiTheme.Text, .35f)
                    : i == 2 ? (row.IsActive ? ArenaUiTheme.Positive : ArenaUiTheme.Muted) : ArenaUiTheme.Text;
                // Keep even unusually large counts inside their own columns.
                if (i >= 3)
                {
                    style.fontSize = i == 7 ? 16 : 15;
                    while (style.fontSize > 7 && style.CalcSize(new GUIContent(values[i])).x > cell.width) style.fontSize--;
                }
                Label(cell, values[i], style, color);
                x += widths[i];
            }
            Fill(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), ArenaUiTheme.Edge);
        }
    }

    private static Rect Reserve(float width, float height)
        => GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
    private static void Label(Rect rect, string text, GUIStyle style, Color color)
    {
        var previous = GUI.color;
        try { GUI.color = color; GUI.Label(rect, text ?? "", style); }
        finally { GUI.color = previous; }
    }
    private static void Fill(Rect rect, Color color) => DrawTexture(rect, Texture2D.whiteTexture, color);
    private static void DrawTexture(Rect rect, Texture2D texture, Color color)
    {
        var previous = GUI.color;
        try { GUI.color = color; GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = previous; }
    }
    public void Dispose() => _assets.Dispose();
}
