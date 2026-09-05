using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArenaMode.Combat;

/// <summary>
/// Ends the current life on demand.
///
/// Bleeding out with a blacked leg and no way back into the fight is dead time
/// in an arena — this hands the player the respawn they are waiting for. It
/// kills through the health controller's own death path, so everything
/// downstream is the ordinary one: the death interception, the kit, the spawn
/// protection, and the death counter. A given-up life costs a death, exactly
/// like being shot.
/// </summary>
internal static class ArenaGiveUp
{
    /// <summary>Ignores repeats for a moment so one press cannot double-fire.</summary>
    private const float CooldownSeconds = 1f;

    private static float _nextAllowedAt;
    private static bool _requestInProgress;

    public static void Reset() => _nextAllowedAt = 0f;

    public static void Tick()
    {
        if (!ArenaSession.IsActive)
            return;

        // Raw key reads do not care that the player is typing, and the default
        // binding is Backspace — the key you press to fix a typo. There are text
        // fields reachable in a raid (the map's marker note, the item tag
        // window) and IMGUI ones on top (the F12 config manager), and correcting
        // a letter in one of them must not cost a life.
        if (EventSystem.current?.currentSelectedGameObject != null || GUIUtility.keyboardControl != 0)
            return;

        // BepInEx's KeyboardShortcut.IsDown() reports false while any unrelated
        // key is held — i.e. while walking — so the check is done by hand.
        var shortcut = Settings.GiveUpHotkey.Value;
        if (!Input.GetKeyDown(shortcut.MainKey) || !shortcut.Modifiers.All(Input.GetKey))
            return;

        Request();
    }

    /// <summary>Also reachable from the panel button.</summary>
    public static void Request()
    {
        if (!ArenaSession.IsActive || _requestInProgress || Time.time < _nextAllowedAt)
            return;

        // Nothing to give up on while the round is already over and the player
        // is standing behind intermission protection.
        if (ArenaMatchService.Current?.Mode is RoundsMode rounds && rounds.InIntermission)
            return;

        if (ArenaRespawnDriver.IsRespawnInProgress)
            return;

        try
        {
            var player = Singleton<GameWorld>.Instantiated
                ? Singleton<GameWorld>.Instance.MainPlayer
                : null;

            var health = player?.ActiveHealthController;
            if (health == null || !health.IsAlive)
                return;

            var teams = ArenaMatchService.Current?.Mode as TeamDeathmatchMode;
            bool scoreReserved = teams?.TryBeginLocalGiveUp(player) == true;

            // Raises the death event the player subscribes to, so the normal
            // death pipeline runs and the arena driver picks it up. No aggressor
            // means no individual kill, killfeed line or combat award. In Team
            // DM the opponent team receives the forfeited life separately. An
            // ended/failed team match must still permit this existing escape
            // action even when its scoring reservation is no longer eligible.
            _requestInProgress = true;
            try
            {
                _nextAllowedAt = Time.time + CooldownSeconds;
                Plugin.ModLogger.LogInfo("Arena: player gave up — respawning.");
                using (ArenaAdministrativeDeathGate.Enter(player.ProfileId))
                    health.Kill(EDamageType.Undefined);
            }
            finally
            {
                // Native Kill flips IsAlive before invoking death subscribers.
                // A subscriber may throw after death succeeded; conversely a
                // suppressed Kill must not create a point. Always consume the
                // synchronous request even if the raid/body changed meanwhile.
                try
                {
                    if (scoreReserved)
                        teams.CompleteLocalGiveUp(player, ReferenceEquals(player.ActiveHealthController, health)
                            && !health.IsAlive);
                }
                finally { _requestInProgress = false; }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena give-up failed: {ex.Message}");
        }
    }
}
