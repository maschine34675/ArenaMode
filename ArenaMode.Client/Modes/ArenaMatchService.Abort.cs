using System;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ArenaMode.Modes;

internal static partial class ArenaMatchService
{
    public static bool TeamStartAbortPending
        => _teamStartAbortPending || _teamStartAbortCommitted;

    private static bool _teamStartAbortPending, _teamStartAbortCommitted;
    private static LocalGame _teamStartAbortGame;
    private static GameWorld _teamStartAbortWorld;
    private static string _teamStartAbortProfileId, _teamStartAbortReason;
    private static int _teamStartAbortFrame;
    private static float _nextTeamStartAbortAttemptAt, _nextTeamStartAbortLogAt;

    internal static void ScheduleFailedStartAbort(string reason)
    {
        if (!Singleton<AbstractGame>.Instantiated
            || Singleton<AbstractGame>.Instance is not LocalGame localGame
            || !Singleton<GameWorld>.Instantiated) return;
        var world = Singleton<GameWorld>.Instance;
        // Also admit the exact server-rejected start, before IsActive was set.
        // LastRaidWasArena alone cannot identify the game after a missed end.
        if ((!ArenaSession.IsActive && !ArenaSession.LastRaidWasArena)
            || !ArenaSession.IsExactStartedRaid(localGame, world)
            || !ReferenceEquals(localGame.GameWorld, world)) return;
        if (TeamStartAbortPending) return; // First reason/epoch/frame owns this abort.

        _teamStartAbortGame = localGame;
        _teamStartAbortWorld = world;
        _teamStartAbortProfileId = localGame.ProfileId;
        _teamStartAbortReason = string.IsNullOrWhiteSpace(reason)
            ? "Arena match initialization failed" : reason;
        _teamStartAbortFrame = Time.frameCount;
        _nextTeamStartAbortAttemptAt = Time.unscaledTime;
        _nextTeamStartAbortLogAt = 0f;
        _teamStartAbortPending = true;
        Respawn.ArenaRespawnDriver.StandDown = true;
        Compat.PitFireTeamCompat.BeginFinalizing();
        Bots.BurstSpawnService.Reset(); // Invalidate already detached profile requests too.
        Plugin.ModLogger.LogError(
            $"Arena start is invalid; protected exit queued for the next frame: {_teamStartAbortReason}");
        // Never call Stop inside OnGameStarted/area Begin. Native startup still
        // owns UI/black-screen transitions on that stack; stopping there left
        // the observed game in Stopping with no usable results screen.
    }

    internal static void TickFailedStartAbort()
    {
        if (!_teamStartAbortPending) return;
        var game = _teamStartAbortGame;
        var world = _teamStartAbortWorld;
        if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !ReferenceEquals(game?.GameWorld, world)
            || !ArenaSession.IsExactStartedRaid(game, world))
        {
            // Never transfer a stale abort to another raid or refresh its PFT
            // finalization. New-raid recovery owns the other service resets.
            ResetFailedStartAbort();
            return;
        }

        Respawn.ArenaRespawnDriver.StandDown = true;
        Compat.PitFireTeamCompat.BeginFinalizing();
        if (IsNativeTeardown(game))
        {
            CommitFailedStartAbort();
            return;
        }
        if (Time.frameCount <= _teamStartAbortFrame || game.Status != GameStatus.Started
            || Time.unscaledTime < _nextTeamStartAbortAttemptAt) return;

        // The immutable LocalGame profile is authoritative for BaseLocalGame.Stop.
        // MainPlayer can still be unpublished, or already be a replacement body.
        if (string.IsNullOrWhiteSpace(_teamStartAbortProfileId))
            _teamStartAbortProfileId = game.ProfileId;
        if (string.IsNullOrWhiteSpace(_teamStartAbortProfileId)
            || !string.Equals(game.ProfileId, _teamStartAbortProfileId, StringComparison.Ordinal))
        {
            NoteFailedStartAbort("native profile identity is unavailable or changed; exit remains protected");
            return;
        }

        _nextTeamStartAbortAttemptAt = Time.unscaledTime + 1f;
        var body = world.MainPlayer ?? game.LocalPlayer;
        var exitStatus = body?.HealthController?.IsAlive == true
            ? ExitStatus.MissingInAction : ExitStatus.Killed;
        try
        {
            game.Stop(_teamStartAbortProfileId, exitStatus, "Arena", 0f);
            // Stop may return without changing status, or synchronously publish
            // GameEnd and clear this latch. Neither means we may invent success.
            if (!ReferenceEquals(_teamStartAbortGame, game) || !_teamStartAbortPending) return;
            if (IsNativeTeardown(game)) CommitFailedStartAbort();
            else NoteFailedStartAbort($"native Stop returned in {game.Status}; waiting to retry while Started");
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_teamStartAbortGame, game) || !_teamStartAbortPending) return;
            if (IsNativeTeardown(game))
            {
                CommitFailedStartAbort();
                NoteFailedStartAbort("native Stop threw after teardown committed: " + ex.GetBaseException().Message);
            }
            else NoteFailedStartAbort("native Stop threw before teardown committed; retry only while Started: "
                + ex.GetBaseException().Message);
        }
    }

    private static bool IsNativeTeardown(LocalGame game)
        => game.Status == GameStatus.Stopping || game.Status == GameStatus.Stopped;

    private static void CommitFailedStartAbort()
    {
        _teamStartAbortPending = false;
        _teamStartAbortCommitted = true;
        Plugin.ModLogger.LogInfo(
            $"Arena invalid-start native teardown accepted; guards remain until raid end: {_teamStartAbortReason}");
    }

    private static void NoteFailedStartAbort(string problem)
    {
        if (Time.unscaledTime < _nextTeamStartAbortLogAt) return;
        _nextTeamStartAbortLogAt = Time.unscaledTime + 5f;
        Plugin.ModLogger.LogWarning("Arena invalid-start exit: " + problem);
    }

    internal static void ResetFailedStartAbort()
    {
        _teamStartAbortPending = _teamStartAbortCommitted = false;
        _teamStartAbortGame = null;
        _teamStartAbortWorld = null;
        _teamStartAbortProfileId = _teamStartAbortReason = null;
        _teamStartAbortFrame = 0;
        _nextTeamStartAbortAttemptAt = _nextTeamStartAbortLogAt = 0f;
    }
}
