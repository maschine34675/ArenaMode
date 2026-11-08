using ArenaMode.Modes;
using ArenaMode.Session;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// The team table shown while the inventory key is held (<see cref="ArenaInventoryKeyNode"/>,
/// 2026-11-08). Presentation only: the same rows and the same table renderer as the F9 panel,
/// but no window, no cursor, no input blocking and no controls, so the player keeps moving and
/// shooting under it. It steps aside for the F9 panel (which has the controls and blocks input
/// deliberately) and for every non-battle screen. Lives on the per-match HUD object. A drawing
/// fault disables it for the match and hands the key back to the game through
/// <see cref="Disabled"/>, so a hold never swallows the key for nothing.
/// </summary>
internal sealed class ArenaScoreboardPeek : MonoBehaviour
{
    // In front of the Tactical buy panel (90), behind the deliberately requested F9 panel (80).
    private const int Depth = 85;
    private const float Padding = 12f;
    private const float TableGap = 6f;

    private readonly ArenaScoreboardRows _rows = new();
    private ArenaWindowSkin _skin;
    private ArenaScoreboardPresentation _table;
    private GUIStyle _title, _muted;
    private float _measuredHeight;
    private bool _wasVisible;

    /// <summary>True after a drawing fault until the next match builds a fresh peek.</summary>
    internal static bool Disabled { get; private set; }

    internal static bool Visible
        => ArenaInventoryKeyNode.Held && ArenaSession.IsActive && ArenaHudVisibility.IsBattleUiActive
           && !ArenaPanel.IsVisible && ArenaMatchService.Current?.Scoreboard != null;

    private void Awake() => Disabled = false;

    private void OnDestroy()
    {
        _table?.Dispose();
        _table = null;
        _skin?.Dispose();
        _skin = null;
    }

    private void OnGUI()
    {
        bool visible = Visible;
        if (visible != _wasVisible)
        {
            // One Debug line per transition: the native check reads it, the player never does.
            Plugin.ModLogger.LogDebug(visible ? "Arena scoreboard peek shown (inventory key held)." : "Arena scoreboard peek hidden.");
            _wasVisible = visible;
        }
        if (!visible) { _rows.Clear(); return; }
        var match = ArenaMatchService.Current;
        if (match == null) return;
        bool areaOpen = false;
        try
        {
            EnsureStyles();
            _rows.Refresh(match);
            GUI.depth = Depth;
            float width = Mathf.Min(760f, Screen.width * .9f);
            float x = (Screen.width - width) * .5f;
            float y = Mathf.Max(ScorePanel.TopOffset + ScorePanel.PanelHeight + 40f, Screen.height * .14f);
            float height = _measuredHeight > 0f ? _measuredHeight : Estimate();
            if (Event.current.type == EventType.Repaint)
                Fill(new Rect(x - Padding, y - Padding, width + 2f * Padding, height + 2f * Padding),
                    ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .9f));
            GUILayout.BeginArea(new Rect(x, y, width, Screen.height - y));
            areaOpen = true;
            GUILayout.Label(match.Mode?.DisplayName ?? "Arena", _title, GUILayout.Width(width), GUILayout.Height(26f));
            GUILayout.Space(4f);
            DrawTables(match, width);
            if (_rows.Hidden > 0)
                GUILayout.Label(_rows.Teams ? $"{_rows.Hidden} additional current participant(s) hidden"
                    : $"{_rows.Hidden} lower or completed bot run(s) not shown", _muted, GUILayout.Width(width));
            if (Event.current.type == EventType.Repaint)
                _measuredHeight = GUILayoutUtility.GetLastRect().yMax;
        }
        catch (Exception ex)
        {
            // A drawing fault must never reach the input tree or the match: hide for this match
            // and let the key fall back to its stock behaviour (ArenaInventoryKeyNode reads Disabled).
            Plugin.ModLogger.LogWarning("Arena scoreboard peek failed and is disabled for this match: " + ex.Message);
            Disabled = true;
            enabled = false;
        }
        finally
        {
            if (areaOpen)
            {
                try { GUILayout.EndArea(); }
                catch (Exception ex) { Plugin.ModLogger.LogDebug("Arena scoreboard peek: EndArea failed: " + ex.Message); }
            }
        }
    }

    private void DrawTables(ArenaMatch match, float width)
    {
        var friendly = Effects.ArenaTeamPalette.FriendlyUi;
        var opponent = Effects.ArenaTeamPalette.OpponentUi;
        if (_rows.Teams && match.Mode is TeamDeathmatchMode teams)
        {
            _table.Draw(teams.FriendlyFaction + " — YOUR TEAM", _rows.Friendly, friendly, friendly, opponent, width);
            GUILayout.Space(TableGap);
            _table.Draw(teams.OpponentFaction, _rows.Opponent, opponent, friendly, opponent, width);
            return;
        }
        if (_rows.Teams && match.Mode is TacticalMode tactical)
        {
            _table.Draw(tactical.IsTeamArena ? "YOUR TEAM" : "YOU", _rows.Friendly, friendly, friendly, opponent, width);
            GUILayout.Space(TableGap);
            _table.Draw("OPPOSITION", _rows.Opponent, opponent, friendly, opponent, width);
            return;
        }
        _table.Draw("MATCH STANDINGS", _rows.Overall, ArenaUiTheme.Accent, friendly, opponent, width);
    }

    private float Estimate()
    {
        // Banner 38 + header 32 + 34 per row per table; refined by the first Repaint.
        int tables = _rows.Teams ? 2 : 1;
        int rows = _rows.Teams ? Math.Max(1, _rows.Friendly.Count) + Math.Max(1, _rows.Opponent.Count) : Math.Max(1, _rows.Overall.Count);
        return 30f + tables * 70f + rows * 34f + (tables - 1) * TableGap + (_rows.Hidden > 0 ? 20f : 0f);
    }

    private void EnsureStyles()
    {
        if (_table != null) return;
        _skin = new ArenaWindowSkin();
        _title = _skin.Label(18, ArenaUiTheme.Accent, true, TextAnchor.MiddleCenter);
        _muted = _skin.Label(12, ArenaUiTheme.Muted);
        _table = new ArenaScoreboardPresentation(_skin.Label(14), _skin.Label(12, ArenaUiTheme.Muted, true));
    }

    private static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        try { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = previous; }
    }
}
