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
    private void Update()
    {
        BurstSpawnService.Tick();
        ArenaHostility.Tick();
        Movement.MovementRuntime.Tick();
        CorpseLightService.Tick();
        CorpseCleanupService.Tick();
        Combat.ArenaSkills.Tick();
        Modes.ArenaMatchService.Tick();
        Hud.ArenaHud.Tick();
    }
}
