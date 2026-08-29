using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Session;
using EFT;
using Redeploy.Core.Engine;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Match lifecycle glue: creates exactly one ArenaMatch when an arena session
/// begins, ticks it from ArenaController, and tears it down at raid end.
/// The local death/respawn signals come from ArenaEvents (raised by the
/// respawn driver — they also cover deaths without an aggressor).
/// </summary>
internal static class ArenaMatchService
{
    public static ArenaMatch Current { get; private set; }

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        RaidLifecycle.OnRaidStarted += world => StartMatch(world);
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
        ArenaEvents.OnPlayerDied += OnLocalDied;
        ArenaEvents.OnPlayerRespawned += OnLocalRespawned;
    }

    public static void Tick()
    {
        var match = Current;
        if (match == null)
            return;

        match.Tick(Time.deltaTime);
    }

    /// <summary>
    /// Called explicitly by ArenaSession before it swaps in the raid-start kit,
    /// and again by the lifecycle event as a backstop. Starting twice would tear
    /// the first match's HUD off its own match object, so the second call is a
    /// no-op.
    /// </summary>
    public static void StartMatch(GameWorld world)
    {
        if (Current != null)
            return;

        // ArenaSession's own OnRaidStarted handler decides IsActive; subscription
        // order between the two handlers is not guaranteed, so defer one frame
        // via the controller: cheapest is to check lazily here — IsActive is set
        // synchronously inside the same RaidLifecycle event dispatch, and
        // ArenaSession.Initialize() runs before ArenaMatchService.Initialize()
        // in Plugin.Awake, so its handler already ran when this one fires.
        if (!ArenaSession.IsActive)
            return;

        try
        {
            Combat.CombatPatches.ResetRaidState();
            Audio.AnnouncerService.EnsureLoaded();
            var mode = ModeRegistry.Create(Settings.GameMode.Value);
            var match = new ArenaMatch(mode);
            Current = match;
            mode.OnMatchStart(match);
            Hud.ArenaHud.Attach(match);
            Audio.AnnouncerService.Attach(match);
            Plugin.ModLogger.LogInfo($"Arena match started ({mode.DisplayName}).");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena match start failed: {ex}");
            Current = null;
            // A partially attached HUD would tick into the menu forever.
            Try(Hud.ArenaHud.Detach, "hud detach");
            Try(Audio.AnnouncerService.Detach, "announcer detach");
        }
    }

    private static void OnRaidEnded()
    {
        var match = Current;
        if (match == null)
            return;

        Current = null;

        // Independent teardown steps — one failing must not skip the rest.
        Try(Hud.ArenaHud.Detach, "hud detach");
        Try(Audio.AnnouncerService.Detach, "announcer detach");
        Try(match.End, "mode end");
        Plugin.ModLogger.LogInfo(
            $"Arena match ended: {match.Kills} kills / {match.Deaths} deaths, "
            + $"score {match.Score}, best streak {match.BestStreak}, "
            + $"{match.Elapsed / 60f:0.0} min.");
    }

    private static void Try(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena match {name} failed: {ex}");
        }
    }

    private static void OnLocalDied(Player player)
        => Current?.ReportLocalDeath();

    private static void OnLocalRespawned(Player player)
        => Current?.ReportLocalRespawn();
}
