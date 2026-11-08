using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using EFT.Settings;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// Live Arena's inventory key in an Arena raid (2026-11-08): a tap opens the inventory as
/// always, a hold shows the team table (<see cref="ArenaScoreboardPeek"/>) for as long as the
/// key stays down, while moving and shooting stay possible. The node joins EFT's input tree
/// with <c>InputTree.Add</c>, which appends it: <c>InputNodeAbstract.TranslateInput</c> walks
/// its children last to first, so this node sees <c>ToggleInventory</c> before every UI screen
/// and before the player owner (added with <c>AddWithLowPriority</c>, index 0). The command
/// arrives on key DOWN or key UP depending on the binding's press type (a user setting, not
/// in the code), so both are handled: on a down-dispatch the command is blocked and a short
/// release forwards it to the game's own started player owner; on an up-dispatch the command
/// is blocked only when the press was a hold, a tap passes untouched. The node is eligible
/// only in the battle UI of an active Arena session, outside the respawn pipeline, while no
/// Arena panel blocks input and while the peek overlay is healthy; otherwise it ignores every
/// command, so the inventory screen keeps receiving its own close key and the stock path
/// handles the rest. Lives on the per-match HUD object, so it exists only while an Arena
/// match has a HUD and leaves the tree with it.
/// </summary>
internal sealed class ArenaInventoryKeyNode : InputNode
{
    private static ArenaInventoryKeyNode _current;

    private readonly InventoryKeyHold _hold = new();
    private readonly List<KeyCode> _keys = new();
    private InputTree _joined;
    private bool _forwarding, _blocked, _wasDown, _wasEligible, _releaseWasHold, _reportedBinding;
    private int _releaseFrame = -1000;

    /// <summary>True while the inventory key is held past the threshold in an eligible battle UI.</summary>
    internal static bool Held => _current != null && _current.Eligible && _current._hold.Held;

    internal static ArenaInventoryKeyNode Attach(GameObject host)
    {
        if (host == null) return null;
        InputTree tree = null;
        try
        {
            var input = GameObject.Find("___Input");
            tree = input == null ? null : input.GetComponent<InputTree>();
        }
        catch (Exception ex) { Plugin.ModLogger.LogDebug("Arena inventory key: input tree lookup failed: " + ex.Message); }
        if (tree == null)
        {
            Plugin.ModLogger.LogDebug("Arena inventory key: EFT input tree not found; the key keeps its stock behaviour.");
            return null;
        }
        var node = host.AddComponent<ArenaInventoryKeyNode>();
        node._joined = tree;
        node.ResolveKeys();
        tree.Add(node);
        _current = node;
        return node;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        _hold.Cancel();
        try { if (_joined != null) _joined.Remove(this); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug("Arena inventory key: leaving the input tree failed: " + ex.Message); }
        _joined = null;
    }

    /// <summary>
    /// The only states in which the node acts. The respawn gates keep a tap out of the body swap
    /// (the stock key reaches no owner there either); the input blocker and the F9 panel keep it
    /// out of every Arena panel; a broken peek hands the key back to the game.
    /// </summary>
    private bool Eligible
        => ArenaSession.IsActive && ArenaHudVisibility.IsBattleUiActive && !ArenaPanel.IsVisible
           && !ArenaInputBlocker.Blocked && !Respawn.ArenaRespawnDriver.IsRespawnInProgress
           && !RespawnEngineState.IsPipelineRunning && !ArenaScoreboardPeek.Disabled;

    public override ETranslateResult TranslateCommand(ECommand command)
    {
        // Explicit Block: GetDefaultBlockResult deliberately ignores ToggleInventory.
        if (_forwarding || !command.IsCommand(ECommand.ToggleInventory) || !Eligible) return ETranslateResult.Ignore;
        if (_hold.Pressed || (KeysDown() && !_wasDown))
        {
            // Dispatched on key down: hold the command back until the release decides.
            if (!_hold.Pressed) _hold.Press(Time.unscaledTime);
            _blocked = true;
            return ETranslateResult.Block;
        }
        if (_releaseFrame >= Time.frameCount - 1)
            // Dispatched on key up: the table was shown for a hold, a tap stays the game's.
            return _releaseWasHold ? ETranslateResult.Block : ETranslateResult.Ignore;
        // Another binding variant, a mouse button or an unknown key: stock behaviour.
        return ETranslateResult.Ignore;
    }

    public override void TranslateAxes(ref float[] axes) { }

    public override ECursorResult ShouldLockCursor() => ECursorResult.Ignore;

    private void Update()
    {
        bool eligible = Eligible;
        if (!eligible)
        {
            // A screen change under a pending press forwards nothing, and a key that is still
            // down when eligibility returns is not a new press.
            _hold.Cancel();
            _blocked = false;
            _wasDown = KeysDown();
            _wasEligible = false;
            return;
        }
        if (!_wasEligible)
        {
            // Back in the battle UI: the player may have rebound the key in the settings.
            ResolveKeys();
            _wasEligible = true;
        }
        bool down = KeysDown();
        if (!_hold.Pressed)
        {
            if (down && !_wasDown) _hold.Press(Time.unscaledTime);
        }
        else
        {
            bool tap = _hold.Update(Time.unscaledTime, down);
            if (!down)
            {
                _releaseFrame = Time.frameCount;
                _releaseWasHold = !tap;
                if (tap && _blocked) ForwardTap();
                _blocked = false;
            }
        }
        _wasDown = down;
    }

    private bool KeysDown()
    {
        if (_keys.Count == 0) return false;
        for (int i = 0; i < _keys.Count; i++)
            if (!Input.GetKey(_keys[i])) return false;
        return true;
    }

    /// <summary>
    /// The blocked down-dispatch, delivered to the game's own started owner of the current body
    /// (the same gates the stock tree applies: an owner is in the tree only between its start and
    /// its stop). Nothing else is forwarded, and a tap that finds no such owner is dropped.
    /// </summary>
    private void ForwardTap()
    {
        EftGamePlayerOwner owner = null;
        try
        {
            var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var candidate = game == null ? null : LocalGameAccessor.GetPlayerOwner(game);
            if (candidate != null && candidate.State == PlayerOwner.EState.Started
                && world?.MainPlayer != null && ReferenceEquals(candidate.Player, world.MainPlayer)
                && world.MainPlayer.HealthController?.IsAlive == true)
                owner = candidate;
        }
        catch (Exception ex) { Plugin.ModLogger.LogDebug("Arena inventory key: player owner lookup failed: " + ex.Message); }
        if (owner == null || !Eligible) return;
        _forwarding = true;
        try { owner.TranslateCommand(ECommand.ToggleInventory); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug("Arena inventory key: forwarding the tap failed: " + ex.Message); }
        finally { _forwarding = false; }
    }

    /// <summary>
    /// The first keyboard variant of the Inventory binding (a chord, all keys held). A binding on
    /// mouse or joystick buttons, or none at all, leaves the key list empty and the key to the
    /// game; only an unreadable settings object falls back to Tab.
    /// </summary>
    private void ResolveKeys()
    {
        _keys.Clear();
        try
        {
            var groups = Singleton<SettingsManager>.Instantiated
                ? Singleton<SettingsManager>.Instance?.Control?.Settings?.GetAllKeyBindings() : null;
            if (groups == null) throw new InvalidOperationException("control settings unavailable");
            var group = groups.FirstOrDefault(candidate => candidate != null && candidate.keyName == EGameKey.Inventory);
            var variant = group?.variants?.FirstOrDefault(candidate =>
                candidate != null && !candidate.isAxis && candidate.keyCode != null
                && candidate.keyCode.Count > 0 && candidate.keyCode.All(IsKeyboardKey));
            if (variant != null) _keys.AddRange(variant.keyCode);
        }
        catch (Exception ex)
        {
            if (!_reportedBinding) Plugin.ModLogger.LogDebug("Arena inventory key: binding lookup failed, assuming Tab: " + ex.Message);
            _reportedBinding = true;
            _keys.Clear();
            _keys.Add(KeyCode.Tab);
        }
    }

    private static bool IsKeyboardKey(KeyCode code)
        => code != KeyCode.None && !(code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6)
           && code < KeyCode.JoystickButton0;
}
