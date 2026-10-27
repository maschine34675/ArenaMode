using ArenaMode.Bots;
using ArenaMode.Modes;
using EFT;
using UnityEngine;

internal static partial class Program
{
    private static void SelectedTeamRuntime()
    {
        foreach (bool nativeUsec in new[] { true, false })
        foreach (bool domination in new[] { false, true })
        {
            var nativeSide = nativeUsec ? EPlayerSide.Usec : EPlayerSide.Bear;
            var selectedSide = nativeUsec ? EPlayerSide.Bear : EPlayerSide.Usec;
            DominationRuntime.Sample = new DominationOccupancy[3];
            var raid = new Raid(usec: nativeUsec, size: 2, selectedTeam: selectedSide, domination: domination);
            raid.Fill();
            Check(raid.Mode.InitialRosterReady && !raid.Mode.IsComplete, "opposite selected team reaches Live");
            Check(raid.Local.Profile.Info.Side == nativeSide, "team choice never rewrites native PMC faction");
            Check(TeamDeathmatchRelations.Faction(raid.Local) == selectedSide,
                "captured labels resolve selected faction for exact local body");
            Check(raid.Mode.FriendlyFaction == (nativeUsec ? "BEAR" : "USEC"), "HUD reports selected faction");
            Check(raid.Alive(selectedSide) == 1 && raid.Alive(nativeSide) == 2,
                "human reserves a slot in selected faction, not profile faction");
            var friend = raid.Bots.Single(bot => bot.Profile.Info.Side == selectedSide);
            var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == nativeSide);
            raid.Local.Position = enemy.Position;
            raid.Step(Time.time + 1);
            Check(friend.BotsGroup.Allies.Contains(raid.Local), "opposite native faction is correctly allied");
            Check(TeamDeathmatchRelations.AreOpponents(enemy, raid.Local)
                && TeamDeathmatchRelations.TryGroupRelationship(enemy.BotsGroup, raid.Local, out bool hostile) && hostile,
                "same native faction is correctly hostile");
            // Domination deliberately leaves enemy discovery to native perception
            // so objective defenders are not fed omniscient pursuit targets.
            if (!domination) Check(enemy.BotsGroup.Enemies.ContainsKey(raid.Local),
                "TDM search awareness admits the known hostile target");
            Check(TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, friend.GetPlayer), "friendly damage affiliation follows chosen side");
            Check(!TeamDeathmatchRelations.ShouldBlockFriendlyDamage(raid.Local, enemy.GetPlayer), "enemy damage remains hostile despite matching native faction");
            Check(raid.Mode.TryClassifyCurrentBody(raid.Local, out bool friendly) && friendly,
                "effects and give-up body admission accepts virtual local faction");
            var oldBody = raid.Local;
            oldBody.HealthController.IsAlive = false;
            raid.World.Players.Remove(oldBody.ProfileId);
            raid.Mode.OnLocalDeath();
            var replacement = Model.Body("local", nativeSide);
            raid.Mode.OnLocalRespawn(replacement);
            raid.Step(Time.time + 1);
            Check(!raid.Mode.IsComplete && replacement.Profile.Info.Side == nativeSide,
                "respawn preserves native profile and selected team");
            Check(TeamDeathmatchRelations.Faction(replacement) == selectedSide
                && TeamDeathmatchRelations.Faction(oldBody) == selectedSide,
                "replacement and posthumous attacker retain round affiliation");
            Check(friend.BotsGroup.Allies.Contains(replacement) && !friend.BotsGroup.Allies.Contains(oldBody),
                "replacement rejoins selected faction with exact-body identity");
            Check(raid.Mode.TryClassifyCurrentBody(replacement, out friendly) && friendly,
                "replacement effects use selected faction");
            Check(raid.Mode.MonitorClaims == 1 && raid.Mode.MonitorPushes > 0,
                "mode claims presentation once and pushes while live");
            raid.Mode.OnMatchEnd();
            Check(raid.Mode.MonitorReleases > 0, "mode end releases presentation");
            ArenaMode.Session.ArenaSession.IsActive = false;
            Check(TeamDeathmatchRelations.Faction(replacement) == null
                && TeamDeathmatchRelations.Faction(oldBody) == null,
                "ordinary mode cannot reuse prior round faction labels");
        }
    }
}
