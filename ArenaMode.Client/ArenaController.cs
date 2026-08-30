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

        BurstSpawnService.Tick();
        ArenaHostility.Tick();
        Movement.MovementRuntime.Tick();
        CorpseLightService.Tick();
        CorpseCleanupService.Tick();
        Combat.ArenaSkills.Tick();
        Combat.ArenaGiveUp.Tick();
        Modes.ArenaMatchService.Tick();
        Hud.ArenaHud.Tick();
    }

    /// <summary>
    /// Warms the other kits shortly after the raid settles, so a random draw
    /// never stalls a respawn waiting for bundles. Deliberately late: the
    /// raid-start kit swap runs its own respawn first.
    /// </summary>
    private void TickKitWarmup()
    {
        if (!Session.ArenaSession.IsActive)
        {
            _kitWarmupStarted = false;
            _kitWarmupAt = 0f;
            return;
        }

        if (_kitWarmupStarted)
            return;

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

        _kitWarmupStarted = true;
        StartCoroutine(Kits.KitBundlePreloader.PreloadAll());
    }
}
