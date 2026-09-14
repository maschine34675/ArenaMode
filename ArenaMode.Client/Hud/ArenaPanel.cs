using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using EFT.UI;
using EFT.UI.Screens;
using Redeploy.Core.Ui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// In-raid panel: scoreboard plus the handful of knobs worth changing without
/// leaving the raid. Opens on the configured hotkey and automatically during a
/// rounds intermission. Cursor comes from the game's own input tree (a vote
/// nobody can take away) — never by writing Cursor.visible.
/// </summary>
internal sealed class ArenaPanel : MonoBehaviour
{
    private const int WindowId = 0x4A5E01;

    private static ArenaPanel _visiblePanel;
    internal static bool IsVisible => _visiblePanel != null && _visiblePanel.isActiveAndEnabled
        && _visiblePanel._open && ArenaSession.IsActive && IsBattleUiActive();

    private bool _open;
    private bool _autoOpened;
    private bool _autoOpenHandled;
    private float _nextCursorRetry;
    private Rect _windowRect;
    private Vector2 _scrollPosition;
    private bool _stylesReady;
    private int _scoreboardVersion = -1;
    private ArenaMatch _cachedMatch;
    private bool _cachedTactical;
    private List<ArenaScoreboardEntry> _friendlyRows = new();
    private List<ArenaScoreboardEntry> _opponentRows = new();
    private List<ArenaScoreboardEntry> _overallRows = new();
    private int _hiddenRows;
    private GUIStyle _windowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _statStyle;
    private GUIStyle _tableHeaderStyle;
    private GUIStyle _friendlyRowStyle;
    private GUIStyle _opponentRowStyle;
    private GUIStyle _mutedStyle;
    private GUIStyle _buttonStyle;

    private void OnDisable()
    {
        _autoOpened = false;
        _autoOpenHandled = false;
        SetOpen(false);
    }

    private void OnDestroy() => OnDisable();

    private void SetCursor(bool show) => CursorVote.Set(this, show);

    private void Update()
    {
        // BepInEx's KeyboardShortcut.IsDown() reports false while any unrelated
        // key is held — i.e. while walking — so the check is done by hand.
        var shortcut = Settings.PanelHotkey.Value;
        if (Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey))
            Toggle();

        // The scoreboard shows itself ONCE per intermission and steps aside when
        // the next round starts. Edge-triggered on purpose: a level-triggered
        // check would reopen the panel in the same Update that closed it, so
        // neither the hotkey nor the Close button would work during a pause.
        // Only auto-open when the pause actually grants invulnerability: the
        // open panel freezes input, and freezing a vulnerable player while every
        // bot hunts them is worse than showing no scoreboard.
        // Also waits out the round break respawn: opening the panel over a
        // black screen would freeze input around a body that is still being
        // built. The check is edge-triggered on the whole condition, so the
        // scoreboard still shows itself once the player is standing.
        var result = ArenaMatchResult.OwnsRaid(ArenaMatchService.Current?.Mode) && !TdmMatchExit.PipelineRunning;
        var intermission = result && IsBattleUiActive() || ArenaMatchService.Current?.Mode is RoundsMode rounds
            && rounds.InIntermission
            && rounds.IntermissionProtected
            && IsBattleUiActive()
            && !Respawn.ArenaRespawnDriver.IsRespawnInProgress;
        if (intermission)
        {
            if (!_autoOpenHandled)
            {
                _autoOpenHandled = true;
                _autoOpened = true;
                SetOpen(true);
            }
        }
        else
        {
            _autoOpenHandled = false;
            if (_autoOpened)
            {
                _autoOpened = false;
                SetOpen(false);
            }
        }

        // A raid ending with the panel open would strand the cursor.
        if (_open && (!ArenaSession.IsActive || !IsBattleUiActive()))
        {
            SetOpen(false);
            return;
        }

        // The cursor vote silently no-ops while the game's input tree is not up
        // yet, and it is otherwise only cast on a state change — so a single
        // early miss would leave the panel open behind a locked cursor for the
        // rest of the raid. Re-voting is free once the node is cached; the
        // throttle is for the uncached path, which searches the scene.
        if (_open && Time.unscaledTime >= _nextCursorRetry)
        {
            _nextCursorRetry = Time.unscaledTime + 0.5f;
            SetCursor(true);
        }
    }

    private void Toggle()
    {
        // Closing must always work. Opening over Inventory/RaidMenu would leave
        // two cursor owners and freeze clicks behind this window.
        if (!_open && (!ArenaSession.IsActive || !IsBattleUiActive()))
            return;

        _autoOpened = false;
        SetOpen(!_open);
    }

    private void SetOpen(bool open)
    {
        _open = open;
        if (open) _visiblePanel = this;
        else if (ReferenceEquals(_visiblePanel, this)) _visiblePanel = null;
        if (!open)
            ClearScoreboardCache();

        // Set FIRST and unconditionally: it cannot throw, and driving it off the
        // state rather than off the transition means any desync self-heals on
        // the next call. A stranded block would leave the player unable to move
        // or shoot for the rest of the session.
        // The cursor vote frees the pointer; it does not stop the game from
        // consuming clicks and mouse axes — without this, clicking a toggle
        // fires the weapon and dragging the window turns the character.
        ArenaInputBlocker.Request(this, open, drainOnRelease: true);
        // IMGUI and the HUD canvas have independent draw orders. Remove the
        // duplicate result now, even if the HUD already ticked this frame.
        // This optional refresh must never prevent input/cursor cleanup.
        try { ArenaHud.RefreshMatchNotice(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena result notice refresh failed: " + ex.Message); }
        if (open)
            _nextCursorRetry = Time.unscaledTime + 0.5f;

        // Dragging a slider assigns a ConfigEntry every frame, and BepInEx
        // rewrites the whole config file on each assignment by default.
        try
        {
            var file = Settings.File;
            if (file != null)
            {
                file.SaveOnConfigSet = !open;
                if (!open)
                    file.Save();
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena panel config save toggle failed: {ex.Message}");
        }

        try
        {
            SetCursor(open);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena panel cursor request failed: {ex.Message}");
        }
    }

    private void OnGUI()
    {
        if (!_open)
            return;

        EnsureStyles();

        // Lower depth draws in FRONT in IMGUI. Tactical's buy panel uses 90;
        // F9 must put this deliberately requested scoreboard above it.
        GUI.depth = 80;

        // Escape closes the panel (the blocker passes it through so the game's
        // own menu keeps working when the panel is not up).
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            _autoOpened = false;
            SetOpen(false);
            Event.current.Use();
            return;
        }

        var width = Mathf.Min(880f, Screen.width * 0.94f);
        var height = Mathf.Min(780f, Screen.height * 0.92f);
        if (_windowRect.width < 1f)
            _windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        else
        {
            // Resolution/fullscreen changes must not strand the footer outside
            // the new screen. Keep the user's dragged position where possible.
            _windowRect.width = width;
            _windowRect.height = height;
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - height));
        }

        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "Arena", _windowStyle);
    }

    private void DrawWindow(int id)
    {
        var match = ArenaMatchService.Current;
        if (match == null)
        {
            GUILayout.Label("No arena match running.", _bodyStyle);
            GUI.DragWindow();
            return;
        }

        // ArenaPanel lives on the persistent plugin object. A match can be
        // replaced while the panel is open, so do not carry scroll position or
        // cached entry objects from the previous raid into the next one.
        if (_cachedMatch != null && !ReferenceEquals(_cachedMatch, match))
            ClearScoreboardCache();

        GUILayout.Space(6);
        var resultTeams = match.Mode as TeamDeathmatchMode;
        bool terminalResult = ArenaMatchResult.HasResult(match.Mode);
        GUILayout.Label(terminalResult ? ArenaMatchResult.Heading(match.Mode) : match.Mode?.DisplayName ?? "Arena", _titleStyle);
        if (terminalResult)
            GUILayout.Label(ArenaMatchResult.Detail(match.Mode) + "\n" + ArenaMatchResult.ExitStatus(match.Mode), _statStyle);

        _scrollPosition = GUILayout.BeginScrollView(
            _scrollPosition,
            false,
            true,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        if (match.Mode is RoundsMode rounds)
            DrawRoundBlock(rounds);

        GUILayout.Space(8);
        DrawScoreboard(match);

        GUILayout.Space(10);
        GUILayout.Label("Your details", _titleStyle);
        DrawStat("Best streak", match.BestStreak.ToString());
        DrawStat("Headshots", match.Headshots.ToString());
        DrawStat("K/D", match.Deaths > 0
            ? ((float)match.Kills / match.Deaths).ToString("0.00")
            : match.Kills.ToString("0.00"));
        var played = resultTeams != null ? resultTeams.PlayingElapsed : match.Elapsed;
        DrawStat("Time", $"{Mathf.FloorToInt(played / 60f):00}:{Mathf.FloorToInt(played % 60f):00}");
        if (!terminalResult)
        {
            DrawStat("Pressure", Bots.ArenaPressure.Describe());

            GUILayout.Space(10);
            GUILayout.Label("Live settings", _titleStyle);
            GUILayout.Space(4);

            if (match.Mode is TeamDeathmatchMode)
                GUILayout.Label("Both factions track opposing combatants. Team size is fixed for this match.", _mutedStyle);
            else
                Settings.HuntPlayer.Value = GUILayout.Toggle(
                    Settings.HuntPlayer.Value, " Bots keep hunting the player", _bodyStyle);
            Settings.ScaleDifficulty.Value = GUILayout.Toggle(
                Settings.ScaleDifficulty.Value, " Arena pushes back harder over time", _bodyStyle);
            Settings.ShowKillfeed.Value = GUILayout.Toggle(
                Settings.ShowKillfeed.Value, " Killfeed", _bodyStyle);
            Settings.ShowHitmarker.Value = GUILayout.Toggle(
                Settings.ShowHitmarker.Value, " Hitmarker", _bodyStyle);
            Settings.ShowDamageNumbers.Value = GUILayout.Toggle(
                Settings.ShowDamageNumbers.Value, " Damage numbers", _bodyStyle);

            GUILayout.Space(6);
            if (!(match.Mode is TeamDeathmatchMode))
                DrawSlider("PMC floor", Settings.PmcFloor, 0, 20);
            DrawSlider("Visible corpses", Settings.MaxCorpses, 0, 50);
        }

        GUILayout.EndScrollView();

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(terminalResult ? "Return to menu" : "Give up", _buttonStyle, GUILayout.Width(150f), GUILayout.Height(30f)))
        {
            _autoOpened = false;
            SetOpen(false);
            if (terminalResult) ArenaMatchResult.RequestExit(match.Mode);
            else Combat.ArenaGiveUp.Request();
        }

        GUILayout.Space(12);

        if (GUILayout.Button("Close", _buttonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            _autoOpened = false;
            SetOpen(false);
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 28f));
    }

    private void DrawScoreboard(ArenaMatch match)
    {
        RefreshScoreboardCache(match);
        GUILayout.Label("Scoreboard", _titleStyle);
        GUILayout.Space(4);

        if (match.Mode is TeamDeathmatchMode teams)
        {
            ArenaScoreboardTotals friendly = match.Scoreboard.Totals(ArenaScoreboardSide.Friendly);
            ArenaScoreboardTotals opponent = match.Scoreboard.Totals(ArenaScoreboardSide.Opponent);
            GUILayout.Label($"Kills  {friendly.Kills} - {opponent.Kills}     "
                + $"Match points  {friendly.Score:n0} - {opponent.Score:n0}", _statStyle);
            DrawStandingsTable(teams.FriendlyFaction + " — YOUR TEAM", _friendlyRows, _friendlyRowStyle);
            GUILayout.Space(6);
            DrawStandingsTable(teams.OpponentFaction, _opponentRows, _opponentRowStyle);
            GUILayout.Label("Team totals include earlier bot lives. Replacement bots start a new personal run.", _mutedStyle);
            return;
        }

        if (_cachedTactical && match.Mode is TacticalMode tactical)
        {
            ArenaScoreboardTotals friendly = match.Scoreboard.Totals(ArenaScoreboardSide.Friendly);
            ArenaScoreboardTotals opponent = match.Scoreboard.Totals(ArenaScoreboardSide.Opponent);
            string rounds = $"Rounds  {tactical.RoundsWon} - {tactical.RoundsLost}";
            GUILayout.Label(
                $"{rounds}     Match points  {friendly.Score:n0} - {opponent.Score:n0}     "
                + $"Kills  {friendly.Kills} - {opponent.Kills}",
                _statStyle);
            GUILayout.Space(5);
            DrawStandingsTable(tactical.IsTeamArena ? "YOUR TEAM" : "YOU", _friendlyRows, _friendlyRowStyle);
            GUILayout.Space(6);
            DrawStandingsTable("OPPOSITION", _opponentRows, _opponentRowStyle);
            int earlierOpponentPoints = opponent.Score - _opponentRows.Sum(entry => entry.Score);
            int earlierOpponentKills = opponent.Kills - _opponentRows.Sum(entry => entry.Kills);
            if (earlierOpponentPoints > 0 || earlierOpponentKills > 0)
            {
                GUILayout.Label(
                    $"Earlier opponent bodies: {earlierOpponentPoints:n0} points, "
                    + $"{earlierOpponentKills} kills (included in match total)",
                    _mutedStyle);
            }
            if (_hiddenRows > 0)
                GUILayout.Label($"{_hiddenRows} additional current participant(s) hidden", _mutedStyle);
            return;
        }

        DrawStandingsTable("MATCH STANDINGS", _overallRows, null);
        if (_hiddenRows > 0)
            GUILayout.Label($"{_hiddenRows} lower or completed bot run(s) not shown", _mutedStyle);
    }

    private void RefreshScoreboardCache(ArenaMatch match)
    {
        bool tactical = match.Mode is TacticalMode || match.Mode is TeamDeathmatchMode;
        if (ReferenceEquals(_cachedMatch, match)
            && _scoreboardVersion == match.Scoreboard.Version
            && _cachedTactical == tactical)
            return;

        _cachedMatch = match;
        _scoreboardVersion = match.Scoreboard.Version;
        _cachedTactical = tactical;
        if (tactical)
        {
            _friendlyRows = match.Scoreboard.Standings(
                    ArenaScoreboardSide.Friendly,
                    currentRosterOnly: true,
                    maxRows: match.Mode is TeamDeathmatchMode ? 12 : 5,
                    pinLocal: true)
                .ToList();
            _opponentRows = match.Scoreboard.Standings(
                    ArenaScoreboardSide.Opponent,
                    currentRosterOnly: true,
                    maxRows: 15,
                    pinLocal: false)
                .ToList();
            _overallRows.Clear();
            _hiddenRows = Math.Max(
                0,
                match.Scoreboard.Count(ArenaScoreboardSide.Friendly, true) - _friendlyRows.Count
                + match.Scoreboard.Count(ArenaScoreboardSide.Opponent, true) - _opponentRows.Count);
            return;
        }

        _overallRows = match.Scoreboard.Standings(
                side: null,
                currentRosterOnly: false,
                maxRows: 12,
                pinLocal: true)
            .ToList();
        _friendlyRows.Clear();
        _opponentRows.Clear();
        _hiddenRows = Math.Max(
            0,
            match.Scoreboard.HistoricalParticipantCount - _overallRows.Count);
    }

    private void ClearScoreboardCache()
    {
        _cachedMatch = null;
        _scoreboardVersion = -1;
        _friendlyRows.Clear();
        _opponentRows.Clear();
        _overallRows.Clear();
        _hiddenRows = 0;
        _scrollPosition = Vector2.zero;
    }

    private void DrawStandingsTable(
        string heading,
        IReadOnlyList<ArenaScoreboardEntry> rows,
        GUIStyle sideStyle)
    {
        GUILayout.Label(heading, sideStyle ?? _tableHeaderStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("NAME", _tableHeaderStyle, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
        GUILayout.Label("ROLE", _tableHeaderStyle, GUILayout.Width(105f));
        GUILayout.Label("STATE", _tableHeaderStyle, GUILayout.Width(55f));
        GUILayout.Label("K", _tableHeaderStyle, GUILayout.Width(35f));
        GUILayout.Label("D", _tableHeaderStyle, GUILayout.Width(35f));
        GUILayout.Label("A", _tableHeaderStyle, GUILayout.Width(35f));
        GUILayout.Label("HS", _tableHeaderStyle, GUILayout.Width(35f));
        GUILayout.Label("SCORE", _tableHeaderStyle, GUILayout.Width(95f));
        GUILayout.EndHorizontal();

        if (rows == null || rows.Count == 0)
        {
            GUILayout.Label("Waiting for roster...", _mutedStyle);
            return;
        }

        foreach (ArenaScoreboardEntry row in rows)
        {
            GUIStyle style = row.IsLocal
                ? _friendlyRowStyle
                : sideStyle ?? (row.Side == ArenaScoreboardSide.Friendly
                    ? _friendlyRowStyle
                    : _opponentRowStyle);
            string name = row.IsLocal ? row.DisplayName + "  (YOU)" : row.DisplayName;
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, style, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
            GUILayout.Label(row.RoleLabel, style, GUILayout.Width(105f));
            GUILayout.Label(row.IsActive ? "LIVE" : "OUT", row.IsActive ? style : _mutedStyle, GUILayout.Width(55f));
            GUILayout.Label(row.Kills.ToString(), style, GUILayout.Width(35f));
            GUILayout.Label(row.Deaths.ToString(), style, GUILayout.Width(35f));
            GUILayout.Label(row.Assists.ToString(), style, GUILayout.Width(35f));
            GUILayout.Label(row.Headshots.ToString(), style, GUILayout.Width(35f));
            GUILayout.Label(row.Score.ToString("n0"), style, GUILayout.Width(95f));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawRoundBlock(RoundsMode rounds)
    {
        GUILayout.Space(4);
        var round = rounds.Round;

        GUILayout.Label(rounds.HudStatusLine, _bodyStyle);
        if (!rounds.Continuous)
            DrawStat("Rounds won", rounds.RoundsWon.ToString());

        GUILayout.Space(4);
        DrawStat("This round", $"{round.Kills} kills / {round.Deaths} deaths / {round.Score:n0} pts");
    }

    private void DrawStat(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _bodyStyle, GUILayout.Width(150f));
        GUILayout.Label(value, _statStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawSlider(string label, BepInEx.Configuration.ConfigEntry<int> entry, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {entry.Value}", _bodyStyle, GUILayout.Width(150f));
        var value = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max));
        if (value != entry.Value)
            entry.Value = value;

        GUILayout.EndHorizontal();
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
            return;

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _titleStyle.normal.textColor = new Color(0.55f, 0.85f, 1f);

        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
        };
        _bodyStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        _statStyle = new GUIStyle(_bodyStyle)
        {
            fontStyle = FontStyle.Bold,
        };
        _statStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);

        _tableHeaderStyle = new GUIStyle(_bodyStyle)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
        };
        _tableHeaderStyle.normal.textColor = new Color(0.72f, 0.76f, 0.8f);

        _friendlyRowStyle = new GUIStyle(_bodyStyle)
        {
            fontStyle = FontStyle.Bold,
        };
        _friendlyRowStyle.normal.textColor = new Color(0.49f, 0.85f, 1f);

        _opponentRowStyle = new GUIStyle(_bodyStyle);
        _opponentRowStyle.normal.textColor = new Color(1f, 0.48f, 0.43f);

        _mutedStyle = new GUIStyle(_bodyStyle);
        _mutedStyle.normal.textColor = new Color(0.58f, 0.61f, 0.64f);

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
        };

        _stylesReady = true;
    }

    private static bool IsBattleUiActive()
    {
        try
        {
            return EftScreenManager.Instance != null
                   && EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
