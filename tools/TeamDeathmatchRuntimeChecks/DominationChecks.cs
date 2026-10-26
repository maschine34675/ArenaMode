using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Modes;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static void DominationLifecycle()
    {
        foreach (bool usec in new[] { true, false })
        {
            DominationRuntime.Sample = new DominationOccupancy[3];
            var raid = new Raid(usec: usec, size: 2, killLimit: 1, timeLimit: 1, domination: true);
            Check(raid.Mode.Id == "domination" && raid.Mode.IsDomination && !raid.Mode.InitialRosterReady,
                "DOM exact mode identity and initial readiness");
            Check(raid.Mode.ShouldBlockMatchDamage(raid.Local), "DOM preparation blocks damage");
            raid.Fill();
            Check(raid.Mode.InitialRosterReady && raid.Mode.IsSearchActive && raid.Mode.MonitorClaims == 1,
                "DOM releases real roster/countdown and claims objective monitors");
            Check(raid.Mode.TryGetDifficultyClock(out _, out int duration) && duration == 600,
                "DOM frozen objective horizon independent of TDM F12 limits");
            float start = Time.time;
            var enemy = raid.Bots.First(b => b.Profile.Info.Side != raid.Local.Profile.Info.Side);
            var kill = Kill(raid.Local.ProfileId, enemy.ProfileId, true);
            raid.Mode.ClassifyKill(kill);
            raid.Match.Scoreboard.ReportKill(kill);
            raid.Mode.OnKill(kill);
            Check(raid.Match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills == 1
                && raid.Mode.FriendlyTeamScore == 0 && !raid.Mode.HasResult,
                "DOM kill remains personal combat stat, never objective point or kill-limit result");
            DominationRuntime.Sample = Enumerable.Repeat(usec ? new DominationOccupancy(1, 0)
                : new DominationOccupancy(0, 1), 3).ToArray();
            raid.Step(start + 5);
            Check(raid.Mode.FriendlyTeamScore == 0, "DOM capture period pays no income");
            raid.Step(start + 6);
            Check(raid.Mode.FriendlyTeamScore == 3 && raid.Mode.OpponentTeamScore == 0,
                "DOM held points map to selected local side");
            DominationRuntime.Sample = Enumerable.Repeat(new DominationOccupancy(1, 1), 3).ToArray();
            raid.Step(start + 8);
            Check(raid.Mode.FriendlyTeamScore == 3, "DOM contested points pause objective income");
            DominationRuntime.Sample = new DominationOccupancy[3];
            raid.Local.HealthController.IsAlive = false;
            raid.Mode.OnLocalDeath();
            raid.Step(start + 10);
            Check(raid.Mode.FriendlyTeamScore == 9 && !raid.Mode.HoldRespawn,
                "DOM owned points and bot match continue during local death");
            var replacement = Model.Body("local", usec ? EPlayerSide.Usec : EPlayerSide.Bear);
            raid.Mode.OnLocalRespawn(replacement);
            Check(raid.Mode.FriendlyTeamScore == 9 && raid.Mode.TryClassifyCurrentBody(replacement, out bool friendly) && friendly,
                "DOM local replacement keeps objective state and exact affiliation");
            for (int i = 0; !raid.Mode.HasResult && i < 20; i++) raid.Step(Time.time + 5);
            Check(raid.Mode.HasResult && raid.Mode.FriendlyTeamScore == 200 && raid.Mode.OpponentTeamScore == 0,
                "DOM stops exactly at held score target");
            Check(raid.Mode.HoldRespawn && !raid.Mode.IsSearchActive && raid.Mode.ResultDetail.Contains("Score limit"),
                "DOM terminal state blocks respawn/objective steering with objective wording");
            raid.Step(Time.time + 10);
            Check(raid.Mode.FriendlyTeamScore == 200, "DOM terminal score frozen");
            int stops = DominationObjectiveBridge.Stops;
            raid.Mode.OnMatchEnd();
            Check(DominationObjectiveBridge.Stops > stops && !raid.Mode.ObjectiveHudVisible,
                "DOM match end releases objective ownership and visibility");
        }
        DominationRuntime.Sample = new DominationOccupancy[3];
        var stopped = new Raid(size: 2, domination: true);
        stopped.Fill();
        int before = DominationObjectiveBridge.Stops;
        stopped.Game.Status = GameStatus.Stopping;
        stopped.Step(Time.time + 1);
        Check(DominationObjectiveBridge.Stops > before && !stopped.Mode.IsSearchActive,
            "DOM native stop revokes objectives before body disposal");
        foreach (string map in new[] { "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl" })
        foreach (bool usec in new[] { true, false })
        {
            int count = map is "arena_equator" or "arena_bay5" ? 1 : 2;
            DominationRuntime.Sample = new DominationOccupancy[count];
            var port = new Raid(usec: usec, size: 2, location: map, domination: true);
            Check(port.Mode.ObjectiveCount == count, "DOM port constructs its authored objective count: " + map);
            port.Fill();
            Check(port.Mode.InitialRosterReady && port.Mode.IsSearchActive && !port.Mode.IsComplete,
                "DOM port releases the actual independent roster: " + map);
            float liveAt = Time.time;
            DominationRuntime.Sample = Enumerable.Repeat(usec ? new DominationOccupancy(1, 0)
                : new DominationOccupancy(0, 1), count).ToArray();
            port.Step(liveAt + 5);
            Check(port.Mode.FriendlyTeamScore == 0, "DOM port cannot score during capture: " + map);
            port.Step(liveAt + 6);
            Check(port.Mode.FriendlyTeamScore == count && port.Mode.OpponentTeamScore == 0,
                "DOM port scores each authored point for selected faction: " + map);
            port.Mode.OnMatchEnd();
            Check(!port.Mode.ObjectiveHudVisible, "DOM port releases presentation: " + map);
        }
        DominationRuntime.Sample = new DominationOccupancy[3];
    }
}
