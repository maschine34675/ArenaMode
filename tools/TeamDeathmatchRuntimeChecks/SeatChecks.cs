using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Modes;
using EFT;
using UnityEngine;

internal static partial class Program
{
    /// <summary>
    /// Fixed scoreboard seats and named arrival rejections (2026-11-08, adopted from the fork
    /// survey). The production mode, population, relations and scoreboard are linked; the
    /// stub's display name is the profile id, so a seat's name is its first occupant's id.
    /// </summary>
    private static void SeatsAndArrivalDiagnostics()
    {
        // Every rejected arrival names the gate it failed instead of the former generic text.
        foreach (var (failure, gate) in new[]
        {
            // A BEAR side under a USEC role (and the reverse) is no faction at all to the relations.
            ("faction", "faction unknown, expected Usec"), ("role", "faction unknown, expected Usec"),
            ("foreign-group", "group side or membership inconsistent"), ("mixed-group", "group side or membership inconsistent"),
        })
        {
            var raid = new Raid();
            raid.Step(0);
            var request = Request(EPlayerSide.Usec);
            Deliver(request, change: owner =>
            {
                if (failure == "faction") owner.GetPlayer.Profile.Info.Side = EPlayerSide.Bear;
                if (failure == "role") owner.GetPlayer.Profile.Info.Settings.Role = WildSpawnType.assault;
                if (failure == "foreign-group") owner.BotsGroup.BotGame = new LocalGame();
                if (failure == "mixed-group")
                    owner.BotsGroup.Members.Add(Model.Body("mixed", EPlayerSide.Bear, true).AIData.BotOwner);
            });
            string retired = ArenaMode.Plugin.ModLogger.Lines.Last(line => line.Contains("Team Deathmatch retired"));
            Check(retired.Contains("arrival rejected: " + gate),
                "arrival retirement names the failing gate: " + failure);
        }
        {
            // A second body on an already settled request is a stale arrival.
            var raid = new Raid();
            raid.Step(0);
            var request = Request(EPlayerSide.Bear);
            Deliver(request, EBotState.Active);
            request.DispatchFinished(true);
            raid.Step(1);
            var stale = Deliver(request, EBotState.Active);
            Check(!stale.HealthController.IsAlive, "a body arriving on a settled request is retired");
            Check(ArenaMode.Plugin.ModLogger.Lines.Last(line => line.Contains("Team Deathmatch retired"))
                .Contains("arrival rejected: stale request"), "arrival retirement names a stale request");
        }

        // Fixed seats: the local is USEC with two friendly bots, three opposing BEAR bots.
        {
            var raid = new Raid(usec: true, size: 3);
            raid.Fill();
            BurstSpawnService.OnRequest = null; // deliver replacements by hand below
            var board = raid.Match.Scoreboard;
            var opponents = board.Standings(ArenaScoreboardSide.Opponent, true, 15, false);
            Check(opponents.Count == 3 && opponents.All(entry => entry.Key.StartsWith("stable:bear-", StringComparison.Ordinal)),
                "opposing bots sit in fixed seats");
            var friendlyRows = board.Standings(ArenaScoreboardSide.Friendly, true, 12, true);
            Check(friendlyRows.Count(entry => !entry.IsLocal) == 2
                && friendlyRows.Where(entry => !entry.IsLocal).All(entry => entry.Key.StartsWith("stable:usec-", StringComparison.Ordinal)),
                "friendly bots sit in fixed seats beside the local row");
            Check(opponents.Select(entry => entry.Key).Distinct().Count() == 3
                && opponents.All(entry => entry.Key == "stable:bear-0" || entry.Key == "stable:bear-1" || entry.Key == "stable:bear-2"),
                "seats are numbered from zero up to the faction's bot target");

            var enemy = raid.Bots.First(bot => bot.Profile.Info.Side == EPlayerSide.Bear);
            var seat = opponents.Single(entry => entry.ActiveProfileId == enemy.ProfileId);
            string seatName = seat.DisplayName;
            Check(seatName == enemy.ProfileId, "a seat is named by its first occupant");
            Check(TeamSeatNames.Count == 0, "first occupants keep their own names without a registry entry");

            var kill = Kill(enemy.ProfileId, "local");
            kill.LocalIsVictim = true;
            raid.Mode.ClassifyKill(kill);
            board.ReportKill(kill);
            Check(kill.ScoreEligible && seat.Kills == 1, "the seat holds its body's kill");

            enemy.HealthController.IsAlive = false;
            raid.World.Players.Remove(enemy.ProfileId);
            raid.Step(Time.time + 1);
            Check(board.Count(ArenaScoreboardSide.Opponent, true) == 3 && seat.IsCurrentRoster && !seat.IsActive,
                "a vacant seat stays on the table as OUT");
            Check(seat.Kills == 1 && seat.DisplayName == seatName, "a vacant seat keeps its name and running stats");

            var replacementRequest = Request(EPlayerSide.Bear);
            var replacement = Deliver(replacementRequest, EBotState.Active);
            replacementRequest.DispatchFinished(true); // as the native observer does after the body arrived
            raid.Step(Time.time + 1);
            Check(board.Count(ArenaScoreboardSide.Opponent, true) == 3,
                "the replacement takes the vacant seat rather than a new row");
            Check(seat.IsActive && seat.ActiveProfileId == replacement.ProfileId && seat.Kills == 1,
                "the replacement body continues the seat's row and running stats");
            Check(seat.DisplayName == seatName && seatName != replacement.ProfileId,
                "the seat keeps its first occupant's name");
            Check(TeamSeatNames.TryGet(replacement.ProfileId, out string shown) && shown == seatName,
                "the replacement body wears the seat name for killfeed and notices");
            Check(!TeamSeatNames.TryGet(enemy.ProfileId, out _), "the first occupant keeps its own name");
            var byReplacement = Kill(replacement.ProfileId, "local");
            byReplacement.LocalIsVictim = true;
            raid.Mode.ClassifyKill(byReplacement);
            board.ReportKill(byReplacement);
            Check(byReplacement.ScoreEligible && byReplacement.KillerIsFriendly == false && seat.Kills == 2,
                "the replacement's own kill is classified on its profile id and lands on the seat");

            var late = Kill(enemy.ProfileId, "local");
            late.LocalIsVictim = true;
            raid.Mode.ClassifyKill(late);
            board.ReportKill(late);
            Check(late.ScoreEligible && seat.Kills == 3, "a posthumous kill of the seat's earlier body credits the seat");

            // A second replacement while the first lives takes a different seat: nothing is reused under a living body.
            // The native owner list still holds the dead first body: pick a living one.
            var second = raid.Bots.First(bot => bot.HealthController.IsAlive
                && bot.Profile.Info.Side == EPlayerSide.Bear && bot != replacement);
            second.HealthController.IsAlive = false;
            raid.World.Players.Remove(second.ProfileId);
            raid.Step(Time.time + 1);
            var anotherRequest = Request(EPlayerSide.Bear);
            var another = Deliver(anotherRequest, EBotState.Active);
            anotherRequest.DispatchFinished(true);
            raid.Step(Time.time + 1);
            var rows = board.Standings(ArenaScoreboardSide.Opponent, true, 15, false);
            Check(rows.Count == 3 && rows.Count(entry => entry.IsActive) == 3
                && rows.Single(entry => entry.ActiveProfileId == another.ProfileId).Key != seat.Key,
                "a replacement never displaces a living body from its seat");

            raid.Mode.OnMatchEnd();
            Check(TeamSeatNames.Count == 0, "seat names do not outlive the match");
        }

        // Domination runs the same adoption and roster path, so its bots sit in seats too.
        {
            var raid = new Raid(usec: false, size: 2, domination: true);
            raid.Fill();
            BurstSpawnService.OnRequest = null;
            var board = raid.Match.Scoreboard;
            var opponents = board.Standings(ArenaScoreboardSide.Opponent, true, 15, false);
            Check(raid.Mode.IsDomination && opponents.Count == 2
                && opponents.All(entry => entry.Key.StartsWith("stable:usec-", StringComparison.Ordinal)),
                "Domination opponents sit in fixed seats");
            var victim = raid.Bots.First(bot => bot.HealthController.IsAlive && bot.Profile.Info.Side == EPlayerSide.Usec);
            var seat = opponents.Single(entry => entry.ActiveProfileId == victim.ProfileId);
            victim.HealthController.IsAlive = false;
            raid.World.Players.Remove(victim.ProfileId);
            raid.Step(Time.time + 1);
            var request = Request(EPlayerSide.Usec);
            var replacement = Deliver(request, EBotState.Active);
            request.DispatchFinished(true);
            Check(seat.IsActive && seat.ActiveProfileId == replacement.ProfileId
                && TeamSeatNames.TryGet(replacement.ProfileId, out string name) && name == victim.ProfileId,
                "the adoption sync seats a Domination replacement at once under the seat name");
            raid.Mode.OnMatchEnd();
        }

        // A body that dies right after adoption, before any tick, is a vacant seat and never a
        // second row: the adoption sync bound the seat first.
        {
            var raid = new Raid(usec: true, size: 2);
            raid.Step(0);
            var request = Request(EPlayerSide.Bear);
            var early = Deliver(request, EBotState.Active);
            request.DispatchFinished(true);
            var board = raid.Match.Scoreboard;
            var seat = Assert1(board.Standings(ArenaScoreboardSide.Opponent, true, 15, false), "one seat after the first adoption");
            Check(seat.Key == "stable:bear-0" && seat.ActiveProfileId == early.ProfileId, "the adoption sync seats the body before any tick");
            var kill = Kill("local", early.ProfileId, true);
            raid.Mode.ClassifyKill(kill);
            board.ReportKill(kill);
            early.HealthController.IsAlive = false;
            raid.World.Players.Remove(early.ProfileId);
            raid.Step(1);
            Check(!raid.Mode.IsComplete && board.Count(ArenaScoreboardSide.Opponent, true) == 1 && !seat.IsActive && seat.Deaths == 1,
                "a body dead before its first tick leaves one vacant seat, not a second row");
        }
    }

    private static T Assert1<T>(IReadOnlyList<T> rows, string description)
    {
        Check(rows.Count == 1, description);
        return rows[0];
    }
}
