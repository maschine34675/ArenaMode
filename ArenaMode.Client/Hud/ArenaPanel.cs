using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Redeploy.Core.Ui;
using System;
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

    private bool _open;
    private bool _autoOpened;
    private bool _autoOpenHandled;
    private float _nextCursorRetry;
    private Rect _windowRect;
    private bool _stylesReady;
    private GUIStyle _windowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _statStyle;
    private GUIStyle _buttonStyle;

    private void OnDestroy()
    {
        SetCursor(false);
        ArenaInputBlocker.Blocked = false;
    }

    private static void SetCursor(bool show) => CursorRequestNode.Want(show);

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
        var intermission = ArenaMatchService.Current?.Mode is RoundsMode rounds
            && rounds.InIntermission
            && rounds.IntermissionProtected
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
        if (_open && !ArenaSession.IsActive)
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
        if (!ArenaSession.IsActive)
            return;

        _autoOpened = false;
        SetOpen(!_open);
    }

    private void SetOpen(bool open)
    {
        _open = open;

        // Set FIRST and unconditionally: it cannot throw, and driving it off the
        // state rather than off the transition means any desync self-heals on
        // the next call. A stranded block would leave the player unable to move
        // or shoot for the rest of the session.
        // The cursor vote frees the pointer; it does not stop the game from
        // consuming clicks and mouse axes — without this, clicking a toggle
        // fires the weapon and dragging the window turns the character.
        ArenaInputBlocker.Blocked = open;
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

        // Lower depth draws in FRONT in IMGUI — the panel has to be reachable.
        GUI.depth = 100;

        // Escape closes the panel (the blocker passes it through so the game's
        // own menu keeps working when the panel is not up).
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            _autoOpened = false;
            SetOpen(false);
            Event.current.Use();
            return;
        }

        var width = Mathf.Min(560f, Screen.width * 0.9f);
        var height = Mathf.Min(520f, Screen.height * 0.9f);
        if (_windowRect.width < 1f)
            _windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

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

        GUILayout.Space(6);
        GUILayout.Label(match.Mode?.DisplayName ?? "Arena", _titleStyle);

        if (match.Mode is RoundsMode rounds)
            DrawRoundBlock(rounds);

        GUILayout.Space(8);
        DrawStat("Kills", match.Kills.ToString());
        DrawStat("Deaths", match.Deaths.ToString());
        DrawStat("Score", match.Score.ToString("n0"));
        DrawStat("Best streak", match.BestStreak.ToString());
        DrawStat("Headshots", match.Headshots.ToString());
        DrawStat("K/D", match.Deaths > 0
            ? ((float)match.Kills / match.Deaths).ToString("0.00")
            : match.Kills.ToString("0.00"));
        DrawStat("Time", $"{Mathf.FloorToInt(match.Elapsed / 60f):00}:{Mathf.FloorToInt(match.Elapsed % 60f):00}");

        GUILayout.Space(10);
        GUILayout.Label("Live settings", _titleStyle);
        GUILayout.Space(4);

        Settings.HuntPlayer.Value = GUILayout.Toggle(
            Settings.HuntPlayer.Value, " Bots keep hunting the player", _bodyStyle);
        Settings.ShowKillfeed.Value = GUILayout.Toggle(
            Settings.ShowKillfeed.Value, " Killfeed", _bodyStyle);
        Settings.ShowHitmarker.Value = GUILayout.Toggle(
            Settings.ShowHitmarker.Value, " Hitmarker", _bodyStyle);
        Settings.ShowDamageNumbers.Value = GUILayout.Toggle(
            Settings.ShowDamageNumbers.Value, " Damage numbers", _bodyStyle);

        GUILayout.Space(6);
        DrawSlider("PMC floor", Settings.PmcFloor, 0, 20);
        DrawSlider("Visible corpses", Settings.MaxCorpses, 0, 50);

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Give up", _buttonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            _autoOpened = false;
            SetOpen(false);
            Combat.ArenaGiveUp.Request();
        }

        GUILayout.Space(12);

        if (GUILayout.Button("Close", _buttonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
        {
            _autoOpened = false;
            SetOpen(false);
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUI.DragWindow();
    }

    private void DrawRoundBlock(RoundsMode rounds)
    {
        GUILayout.Space(4);
        var round = rounds.Round;

        if (rounds.InIntermission)
        {
            GUILayout.Label(
                $"Round {round.Number} complete — next round in {Mathf.CeilToInt(rounds.IntermissionRemaining)}s",
                _bodyStyle);
        }
        else
        {
            var target = rounds.KillTarget;
            var remaining = rounds.TimeRemaining;
            var line = $"Round {round.Number}";
            if (target > 0)
                line += $" — {round.Kills}/{target} kills";
            if (remaining >= 0f)
                line += $" — {Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00} left";

            GUILayout.Label(line, _bodyStyle);
        }

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

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
        };

        _stylesReady = true;
    }
}
