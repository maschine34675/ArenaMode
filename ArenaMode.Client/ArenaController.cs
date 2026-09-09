using ArenaMode.Bots;
using UnityEngine;

namespace ArenaMode;

/// <summary>
/// Per-frame driver on the plugin GameObject (survives raid transitions; the
/// services it ticks gate themselves on the arena session). The mode framework
/// (M5) will hook in here as well.
/// </summary>
internal sealed class ArenaController : MonoBehaviour
{
    private bool _kitWarmupStarted;
    private float _kitWarmupAt;

    private void Update()
    {
        TickKitWarmup();

        // Outside a raid only; the service enforces that itself.
        Config.SettingsSync.Tick();

        Compat.PitFireTeamCompat.Tick();
        Areas.ArenaAreaRuntime.Tick();
        BurstSpawnService.Tick();
        BotLoadBoost.Tick();
        ArenaHostility.Tick();
        Movement.MovementRuntime.Tick();
        CorpseLightService.Tick();
        CorpseCleanupService.Tick();
        Combat.ArenaSkills.Tick();
        Combat.ArenaGiveUp.Tick();
        Kits.AutoQuickSlots.Tick();
        Kits.ArenaSecureContainer.Tick();
        Modes.ArenaMatchService.Tick();
        Effects.ArenaTeamLaserColors.Tick();
        Effects.ArenaLightingDirector.Tick();
        Audio.AnnouncerService.Tick();
        Audio.LabyrinthAlarmPatches.Tick();
        Session.LabyrinthSpawnTraps.Tick();
        Spectator.ArenaSpectatorService.Tick();
        Hud.ArenaHud.Tick();
    }

    /// <summary>
    /// Ask for the warm-up to run again, for when the set of kits that can come
    /// up has changed mid-raid — a fresh Gun Game ladder is the case that needs
    /// it. Cheap to repeat: the preloader tracks warmed TEMPLATES, so a second
    /// pass only builds what is genuinely new.
    /// </summary>
    public static void RequestKitWarmup() => _rewarmRequested = true;

    private static bool _rewarmRequested;

    /// <summary>
    /// Warms the other kits shortly after the raid settles, so a random draw
    /// never stalls a respawn waiting for bundles. Deliberately late: the
    /// raid-start kit swap runs its own respawn first.
    /// </summary>
    private void TickKitWarmup()
    {
        if (_rewarmRequested)
        {
            _rewarmRequested = false;
            _kitWarmupStarted = false;

            // Short, not the eight seconds a raid start waits: nothing is
            // loading, the player is simply standing in a fight.
            _kitWarmupAt = Time.time + 1f;
        }

        if (!Session.ArenaSession.IsActive)
        {
            _kitWarmupStarted = false;
            _kitWarmupAt = 0f;
            return;
        }

        if (_kitWarmupStarted)
            return;

        // Team Tactical preserves the PMC's entry loadout itself and never
        // consumes Arena kits. Starting a persistent-controller preload here
        // would only leave unrelated raid work alive during round teardown.
        if (Compat.PitFireTeamCompat.TeamArenaActive
            || Compat.PitFireTeamCompat.IsFinalizing)
        {
            return;
        }

        // Random kits need every bundle warm because any of them can come up on
        // the next life. So does a mode that hands out kits itself (Gun Game
        // walks the whole ladder), even with the random draw switched off.
        if (!Config.Settings.RandomKit.Value && Kits.KitCatalog.KitProvider == null)
            return;

        if (_kitWarmupAt <= 0f)
        {
            _kitWarmupAt = Time.time + 8f;
            return;
        }

        if (Time.time < _kitWarmupAt)
            return;

        // TDM profile preparation also builds native item pools. Loading the
        // whole optional catalog alongside the initial teams competes with
        // those jobs and can even duplicate the opening player's cold kit.
        // Required per-life Preload calls remain untouched. Read the latched
        // mode, including its gap before Current exists, not the mutable menu.
        if (string.Equals(Session.ArenaSession.ActiveMode, "teamdeathmatch",
                System.StringComparison.Ordinal)
            && (!(Modes.ArenaMatchService.Current?.Mode is Modes.TeamDeathmatchMode team)
                || !team.InitialRosterReady
                || Kits.KitBundlePreloader.IsPreloading
                || Respawn.ArenaRespawnDriver.IsRespawnInProgress))
        {
            return;
        }

        _kitWarmupStarted = true;
        StartCoroutine(Kits.KitBundlePreloader.PreloadAll());
    }
}
