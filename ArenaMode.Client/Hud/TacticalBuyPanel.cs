using ArenaMode.Kits;
using ArenaMode.Modes;
using ArenaMode.Session;
using Redeploy.Core.Ui;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// The Tactical buy menu: kits with prices, what you can afford, and a way to
/// start the round early.
///
/// Same discipline as ArenaPanel, and for the same reasons. The cursor comes
/// from the game's own input tree rather than from writing Cursor.visible, and
/// input is BLOCKED while the menu is up — a cursor vote alone frees the pointer
/// but does not stop the game consuming clicks and mouse axes, so clicking a kit
/// would fire your weapon and dragging the window would turn you.
///
/// Blocking input is also why the menu closes rather than lingering: during the
/// buy phase you are alone on the map, but you may still want to walk to a
/// position, and a frozen player who cannot is worse than one who reopens the
/// menu with a keypress.
/// </summary>
internal sealed class TacticalBuyPanel : MonoBehaviour
{
    private const int WindowId = 0x4A5E02;

    private bool _open;
    private bool _autoOpenHandled;
    private float _nextCursorRetry;
    private Rect _windowRect;
    private Vector2 _scroll;

    private bool _stylesReady;
    private GUIStyle _windowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _pickedStyle;

    private void OnDestroy()
    {
        SetCursor(false);
        ArenaInputBlocker.Request(this, false);
    }

    private void SetCursor(bool show) => CursorVote.Set(this, show);

    private static TacticalMode Mode =>
        ArenaSession.IsActive ? ArenaMatchService.Current?.Mode as TacticalMode : null;

    private void Update()
    {
        var mode = Mode;
        if (mode == null)
        {
            if (_open)
                SetOpen(false);

            _autoOpenHandled = false;
            return;
        }

        // Edge-triggered on the phase, exactly as ArenaPanel is on the
        // intermission: a level-triggered check would reopen the menu in the
        // same Update that closed it, so neither Escape nor Ready would work.
        var buying = mode.InBuyPhase;
        if (buying && !_autoOpenHandled)
        {
            _autoOpenHandled = true;
            SetOpen(true);
        }
        else if (!buying)
        {
            _autoOpenHandled = false;
            if (_open)
                SetOpen(false);
        }

        // Reopen on the panel hotkey while the buy phase is still running, so
        // closing the menu to walk somewhere is not a one-way door.
        if (buying && !_open)
        {
            var shortcut = Config.Settings.PanelHotkey.Value;
            if (Input.GetKeyDown(shortcut.MainKey) && Modifiers(shortcut))
                SetOpen(true);
        }

        // The cursor vote loses to whatever the game does on a scene change, so
        // it is re-cast for as long as the menu is up.
        if (_open && Time.unscaledTime >= _nextCursorRetry)
        {
            _nextCursorRetry = Time.unscaledTime + 0.5f;
            SetCursor(true);
        }
    }

    private static bool Modifiers(BepInEx.Configuration.KeyboardShortcut shortcut)
    {
        foreach (var modifier in shortcut.Modifiers)
        {
            if (!Input.GetKey(modifier))
                return false;
        }

        return true;
    }

    private void SetOpen(bool open)
    {
        _open = open;

        // Set first and unconditionally, driven off the state rather than the
        // transition: a stranded block would leave the player unable to move or
        // shoot for the rest of the raid, and this way any desync self-heals.
        ArenaInputBlocker.Request(this, open);

        if (open)
            _nextCursorRetry = Time.unscaledTime + 0.5f;

        try
        {
            SetCursor(open);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Tactical buy menu cursor request failed: {ex.Message}");
        }
    }

    private void OnGUI()
    {
        if (!_open || Mode == null)
            return;

        EnsureStyles();

        // Lower depth draws in FRONT in IMGUI.
        GUI.depth = 90;

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            SetOpen(false);
            Event.current.Use();
            return;
        }

        var width = Mathf.Min(620f, Screen.width * 0.9f);
        var height = Mathf.Min(560f, Screen.height * 0.9f);
        if (_windowRect.width < 1f)
            _windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "Buy", _windowStyle);
    }

    private void DrawWindow(int id)
    {
        var mode = Mode;
        if (mode == null)
            return;

        GUILayout.Space(4f);
        GUILayout.Label(
            $"Round {mode.RoundNumber}   {mode.RoundsWon}-{mode.RoundsLost}", _titleStyle);
        GUILayout.Label(
            $"{mode.Money:N0} ₽    starting in {mode.PhaseRemaining:0}s", _bodyStyle);

        GUILayout.Space(8f);

        _scroll = GUILayout.BeginScrollView(_scroll);
        foreach (var kit in Ordered(KitCatalog.Kits))
        {
            var price = mode.PriceOf(kit);
            var affordable = mode.CanAfford(kit);
            var picked = ReferenceEquals(kit, mode.Bought);

            GUILayout.BeginHorizontal();

            var label = $"{kit.Name ?? kit.Id}";
            var cost = price > 0 ? $"{price:N0} ₽" : "free";

            // Unaffordable kits stay VISIBLE and disabled rather than hidden:
            // seeing what a good round would buy is most of what makes the
            // economy readable.
            GUI.enabled = affordable && !picked;
            if (GUILayout.Button($"{label}", picked ? _pickedStyle : _buttonStyle, GUILayout.MinWidth(320f)))
                mode.TryBuy(kit);

            GUI.enabled = true;
            GUILayout.Label(affordable ? cost : $"{cost}  (short)", _bodyStyle, GUILayout.Width(150f));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Ready", _buttonStyle, GUILayout.Height(30f)))
        {
            mode.ReadyUp();
            SetOpen(false);
        }

        if (GUILayout.Button("Close", _buttonStyle, GUILayout.Height(30f)))
            SetOpen(false);

        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    /// <summary>Cheapest first, so the list reads as a ladder rather than as a catalogue.</summary>
    private static IEnumerable<KitDefinition> Ordered(IReadOnlyList<KitDefinition> kits)
    {
        if (kits == null)
            yield break;

        var sorted = new List<KitDefinition>(kits);
        sorted.Sort((a, b) => (a?.Price ?? 0).CompareTo(b?.Price ?? 0));

        foreach (var kit in sorted)
        {
            if (kit != null)
                yield return kit;
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady)
            return;

        _stylesReady = true;

        _windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(12, 12, 24, 12) };
        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        _pickedStyle = new GUIStyle(_buttonStyle) { fontStyle = FontStyle.Bold };
    }
}
