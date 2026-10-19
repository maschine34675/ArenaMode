using ArenaMode.Config;
using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static void BotSpawnShields()
    {
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check(!new TeamSpawnProtectionLease(invalid).IsProtected(0, false), "invalid/disabled duration cannot protect");
        var bounded = new TeamSpawnProtectionLease(999);
        Check(bounded.IsProtected(50, false) && bounded.IsProtected(100, true)
            && bounded.IsProtected(129.99f, true) && !bounded.IsProtected(130, true),
            "pending activation does not consume duration, configuration capped at 30 seconds");
        Check(!bounded.IsProtected(10, true), "expired lease cannot rearm after clock rollback");
        var invalidClock = new TeamSpawnProtectionLease(4);
        Check(!invalidClock.IsProtected(float.NaN, true) && !invalidClock.IsProtected(1, true),
            "invalid clock terminates rather than granting permanent protection");

        ArenaSpawnShieldView.Active = 0;
        var raid = new Raid(size: 2, protection: 4, location: "arena_equator");
        raid.Fill();
        float live = Time.time;
        Settings.SpawnProtectionSeconds.Value = 30;
        foreach (var bot in raid.Bots)
            Check(raid.Mode.ShouldBlockMatchDamage(bot.GetPlayer), "all allied/opposing initial bots receive full duration at Live");
        Check(!raid.Mode.ShouldBlockMatchDamage(raid.Local), "bot shield never takes ownership of local respawn protection");
        raid.Step(live + .01f);
        Check(ArenaSpawnShieldView.Active == 3, "every protected bot has one visual");
        var shooter = raid.Bots[0].GetPlayer;
        raid.Mode.EndBotSpawnProtection(shooter);
        Check(!raid.Mode.ShouldBlockMatchDamage(shooter) && ArenaSpawnShieldView.Active == 2,
            "first attack ends damage shield and hides its visual synchronously");
        raid.Step(live + .1f);
        Check(!raid.Mode.ShouldBlockMatchDamage(shooter), "polling cannot rearm a shooter");
        raid.Step(live + 3.99f);
        Check(raid.Mode.ShouldBlockMatchDamage(raid.Bots[1].GetPlayer), "shield remains until original configured deadline");
        raid.Step(live + 4);
        Check(raid.Bots.All(bot => !raid.Mode.ShouldBlockMatchDamage(bot.GetPlayer)) && ArenaSpawnShieldView.Active == 0,
            "deadline expires all shields and views, later F12 edit does not extend duration");

        // A live replacement can spend seconds PreActive without losing its
        // usable protection. There is no per-frame/poll-based renewal afterward.
        BurstSpawnService.OnRequest = null;
        var killed = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
        killed.HealthController.IsAlive = false;
        raid.Step(live + 5);
        var replacement = Deliver(Request(EPlayerSide.Bear), EBotState.PreActive);
        Check(raid.Mode.ShouldBlockMatchDamage(replacement.GetPlayer), "live preactive arrival is safe before its first mode tick");
        raid.Step(live + 10);
        Check(ArenaSpawnShieldView.Active == 0,
            "preactive replacement keeps protection without creating a retireable visual lease");
        replacement.BotState = EBotState.Active;
        Check(raid.Mode.ShouldBlockMatchDamage(replacement.GetPlayer), "full spawn timer starts on physical activation");
        raid.Step(live + 10.01f);
        Check(ArenaSpawnShieldView.Active == 1,
            "activated replacement creates its first visual for the real protection window");
        raid.Step(live + 13.99f);
        Check(raid.Mode.ShouldBlockMatchDamage(replacement.GetPlayer), "replacement survives until its own deadline");
        raid.Step(live + 14);
        Check(!raid.Mode.ShouldBlockMatchDamage(replacement.GetPlayer), "replacement timer is finite");
        raid.Mode.OnMatchEnd();
        Check(ArenaSpawnShieldView.Active == 0, "normal teardown disposes owned visuals");

        foreach (int invalidation in new[] { 0, 1, 2, 3, 4, 5 })
        {
            raid = new Raid(size: 2, protection: 4, location: "arena_equator");
            raid.Fill(); raid.Step(Time.time + .01f);
            var body = raid.Bots[0].GetPlayer;
            Check(raid.Mode.ShouldBlockMatchDamage(body), "positive-control exact owned bot before invalidation");
            if (invalidation == 0) raid.World.Players.Remove(body.ProfileId);
            if (invalidation == 1) body.ProfileId = "identity-mutated";
            if (invalidation == 2) body.HealthController.IsAlive = false;
            if (invalidation == 3) Singleton<GameWorld>.Instance = new GameWorld();
            if (invalidation == 4) ArenaSession.IsActive = false;
            if (invalidation == 5) raid.Game.Status = GameStatus.Stopping;
            Check(!raid.Mode.ShouldBlockMatchDamage(body), "stale body/game/identity cannot retain immunity");
            raid.Mode.OnMatchEnd();
            Check(ArenaSpawnShieldView.Active == 0, "invalidated runtime clears every visual on end");
        }
        var ordinary = new Raid(active: false, protection: 4);
        Check(!ordinary.Mode.IsBotSpawnProtected(ordinary.Local), "unarmed raid does not grant shield");
    }
}
