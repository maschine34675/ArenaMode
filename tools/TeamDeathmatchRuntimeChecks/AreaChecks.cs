using ArenaMode.Areas;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static void BoundedAreaModeContract()
    {
        foreach (bool usec in new[] { true, false })
        foreach (int size in new[] { 2, 3, 4, 5 })
        {
            var raid = new Raid(usec, size: 12, bounded: true, areaPending: true, areaSize: size);
            Check(raid.Mode.TeamSize == size && raid.Game.BotsController.BotSpawner.MaxBots == 2*size-1,
                "the area negotiated count, not maximum capacity or full-map config, owns every faction slot");
            ArenaAreaRuntime.FrozenTeamSize = size == 5 ? 2 : 5;
            Settings.TeamDeathmatchSize.Value = 9;
            ArenaAreaRuntime.IsPending = false;
            raid.Fill();
            Check(raid.Mode.TeamSize == size && raid.Alive(usec ? EPlayerSide.Usec : EPlayerSide.Bear) == size-1
                && raid.Alive(usec ? EPlayerSide.Bear : EPlayerSide.Usec) == size,
                "each negotiated2..5 roster remains frozen despite later configuration changes");
            Check(ArenaAreaRuntime.TeamAssemblyReleases == 1,
                "exactly one complete first roster transitions area placements to ongoing respawns");
        }
        foreach (bool usec in new[] { true, false })
        {
            var raid = new Raid(usec, size: 12, bounded: true, areaPending: true);
            Check(raid.Mode.TeamSize == 2 && raid.Game.BotsController.BotSpawner.MaxBots == 3,
                "bounded TDM freezes 2v2 and reserves only three AI slots despite larger F12 size");
            Check(raid.Mode.HoldRespawn && raid.Mode.HudStatusLine.Contains("PREPARING ARENA AREA"),
                "unready area explicitly holds respawn and displays navigation preparation");
            raid.Step(0);
            raid.Step(60);
            Check(BurstSpawnService.Requests.Count == 0 && !raid.Mode.IsComplete && raid.Match.Cues.Count == 0,
                "area preparation cannot submit bots, spend activation failures or announce ready");
            Settings.TeamDeathmatchSize.Value = 5;
            ArenaAreaRuntime.IsPending = false;
            raid.Fill();
            Check(raid.Mode.TeamSize == 2 && !raid.Mode.HoldRespawn,
                "readiness releases the same frozen 2v2 population rather than re-reading F12");
            Check(raid.Alive(usec ? EPlayerSide.Usec : EPlayerSide.Bear) == 1
                && raid.Alive(usec ? EPlayerSide.Bear : EPlayerSide.Usec) == 2
                && BurstSpawnService.Requests.Count == 3,
                "bounded mode fills exactly one teammate and two opponents");

            raid.Local.HealthController.IsAlive = false;
            raid.Match.Scoreboard.ReportLocalDeath();
            raid.Mode.OnLocalDeath();
            ArenaRespawnDriver.IsRespawnInProgress = true;
            ArenaAreaRuntime.IsPending = true;
            int pending = BurstSpawnService.Requests.Count;
            raid.Step(Time.time + 60);
            Check(raid.Mode.HoldRespawn && !raid.Mode.IsComplete && BurstSpawnService.Requests.Count == pending,
                "post-death area revalidation keeps respawn held and population refill dormant");
            ArenaAreaRuntime.IsPending = false;
            var replacement = Model.Body("local", usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            raid.Mode.OnLocalRespawn(replacement);
            ArenaRespawnDriver.IsRespawnInProgress = false;
            raid.Step(Time.time + 1);
            Check(ArenaAreaRuntime.PositionChecks == 1 && ArenaAreaRuntime.Aborts.Count == 0
                && raid.Match.Scoreboard.Local.IsActive && !raid.Mode.HoldRespawn,
                "verified replacement passes final position check before local roster reactivation");
        }

        foreach (bool pendingArea in new[] { false, true })
        {
            var raid = new Raid(bounded: true);
            raid.Local.HealthController.IsAlive = false;
            raid.Match.Scoreboard.ReportLocalDeath();
            raid.Mode.OnLocalDeath();
            int version = raid.Match.Scoreboard.Version;
            var invalid = Model.Body("local", EPlayerSide.Usec);
            ArenaAreaRuntime.IsPending = pendingArea;
            ArenaAreaRuntime.PositionAccepted = _ => false;
            raid.Mode.OnLocalRespawn(invalid);
            Check(ArenaAreaRuntime.PositionChecks == 1 && ArenaAreaRuntime.Aborts.Count == 1
                && ArenaAreaRuntime.Aborts[0].Contains("replacement body left the verified area"),
                "pending/outside replacement requests protected abort instead of continuing TDM");
            Check(raid.Match.Scoreboard.Version == version && !raid.Match.Scoreboard.Local.IsActive,
                "rejected replacement cannot reactivate the local scoreboard actor");
        }

        var fullMap = new Raid(size: 5);
        Check(fullMap.Mode.TeamSize == 5 && !fullMap.Mode.HoldRespawn,
            "later full-map fixture retains configurable size and immediate respawn semantics");
        ArenaAreaRuntime.PositionAccepted = _ => false;
        var normalReplacement = Model.Body("local", EPlayerSide.Usec);
        fullMap.Mode.OnLocalRespawn(normalReplacement);
        Check(ArenaAreaRuntime.Aborts.Count == 0 && !fullMap.Mode.IsComplete,
            "no active area verdict leaves full-map replacement behavior unchanged");
        ArenaAreaRuntime.Reset();
    }
}
