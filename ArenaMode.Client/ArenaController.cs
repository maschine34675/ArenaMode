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

        if (_kitWarmupStarted || !Config.Settings.RandomKit.Value)
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
