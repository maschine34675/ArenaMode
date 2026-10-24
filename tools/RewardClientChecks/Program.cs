using ArenaMode.Session;
using System;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    private static int _assertions;
    private const string Local = "aaaaaaaaaaaaaaaaaaaaaaaa", Enemy = "bbbbbbbbbbbbbbbbbbbbbbbb";
    private static void Check(bool value, string reason)
    { _assertions++; if (!value) throw new InvalidOperationException(reason); }
    private static RewardCaptureBuffer Fresh()
    {
        var b = new RewardCaptureBuffer("11111111111111111111111111111111", "22222222222222222222222222222222", "native|arenamode:22222222222222222222222222222222");
        b.BeginRound(1); b.Advance(1000);
        b.Register(1, Local, "Friendly", 0); b.Register(2, Enemy, "Opponent", 0);
        return b;
    }
    public static void Main()
    {
        var normal = Fresh();
        normal.Death(2, 1, "Combat", 500);
        normal.Death(2, 1, "Combat", 500);
        Check(normal.Current.Deaths.Count == 1, "Repeated native death inflated the reward journal");
        Check(normal.Current.Deaths[0].Sequence == 1, "Duplicate native death consumed sequence");
        var packet = normal.Finish();
        var before = JsonSerializer.Serialize(packet);
        normal.Advance(2000); normal.Register(3, Enemy, "Opponent", 1500); normal.Death(1, 2, "Combat", 1500);
        normal.BeginRound(2); normal.Invalidate(); normal.CloseRound();
        Check(ReferenceEquals(packet, normal.Finish()), "Final retry changed object");
        Check(before == JsonSerializer.Serialize(normal.Finish()), "Final retry changed payload");
        Check(packet.Final && packet.Revision == 1, "Final upload was not a fixed revision");
        Check(!packet.Rounds[0].Closed, "Normal mid-round exit asserted a completed scoring round");

        var finished = Fresh(); finished.CloseRound();
        Check(finished.Finish().Rounds[0].Closed, "Natural completed-round evidence was lost");
        var forfeit = Fresh(); forfeit.Death(1, null, "Environment", 500); forfeit.Death(1, null, "LocalForfeit", 500);
        Check(forfeit.Current.Deaths.Count == 1 && forfeit.Current.Deaths[0].Kind == "LocalForfeit", "Give-up was double counted or not upgraded");
        Check(forfeit.Current.Deaths[0].KillerBody == null, "Give-up invented a credited opponent");

        var reusedBody = Fresh(); reusedBody.Register(1, Enemy, "Friendly", 0);
        Check(reusedBody.Packet.State == "NoContest", "A physical body changed native participant");
        var side = Fresh(); side.Register(2, Enemy, "Friendly", 0);
        Check(side.Packet.State == "NoContest", "A physical body changed affiliation");
        var unknown = Fresh(); unknown.Death(2, 77, "Combat", 500);
        Check(unknown.Packet.State == "NoContest" && unknown.Current.Deaths.Count == 0, "Unknown attacker accepted");
        var unknownVictim = Fresh(); unknownVictim.Death(77, 1, "Combat", 500);
        Check(unknownVictim.Packet.State == "NoContest", "Unknown victim accepted");
        var changedDeath = Fresh(); changedDeath.Death(2, 1, "Combat", 500); changedDeath.Death(2, null, "Environment", 500);
        Check(changedDeath.Packet.State == "NoContest", "Changed duplicate death was accepted");
        var backward = Fresh(); backward.Advance(999);
        Check(backward.Packet.State == "NoContest", "Round time went backwards");
        var outside = Fresh(); outside.Death(2, 1, "Combat", 1001);
        Check(outside.Packet.State == "NoContest", "Future fatal event accepted");
        var outOfOrder = Fresh(); outOfOrder.Death(2, 1, "Combat", 700); outOfOrder.Death(1, null, "Environment", 600);
        Check(outOfOrder.Packet.State == "NoContest", "Fatal event ordering changed");
        var spawn = Fresh(); spawn.Register(3, Enemy, "Opponent", 1001);
        Check(spawn.Packet.State == "NoContest", "Future activation accepted");

        var rounds = Fresh(); rounds.Death(2, 1, "Combat", 500); rounds.CloseRound(); rounds.BeginRound(2); rounds.Advance(100);
        // A surviving local Player wrapper can participate in a new round. The
        // server issues a distinct life identity for (run, round, physical body).
        rounds.Register(1, Local, "Friendly", 0); rounds.Register(3, Enemy, "Opponent", 0);
        rounds.Death(3, 1, "Combat", 99);
        Check(rounds.Packet.Rounds.Count == 2 && rounds.Packet.Rounds[0].Closed, "Round transition lost closure");
        Check(rounds.Current.Deaths.Single().Sequence == 2, "Death sequence reset across rounds");
        Check(rounds.Packet.State == "Completed", "Surviving physical wrapper invalidated next round");
        var skipped = Fresh(); skipped.BeginRound(3);
        Check(skipped.Packet.State == "NoContest", "Skipped scoring round accepted");
        var cap = Fresh(); for (int n = 2; n <= RewardCaptureBuffer.MaxRounds + 1; n++) cap.BeginRound(n);
        Check(cap.Packet.State == "NoContest" && cap.Packet.Rounds.Count == RewardCaptureBuffer.MaxRounds, "Round capacity was unbounded");
        var lives = Fresh(); for (int i = 3; i <= RewardCaptureBuffer.MaxLives + 1; i++) lives.Register(i, Enemy, "Opponent", 0);
        Check(lives.Packet.State == "NoContest" && lives.Current.Actors.Count == RewardCaptureBuffer.MaxLives, "Physical life capacity was unbounded");
        var wallClock = Fresh(); wallClock.Advance(86400001);
        Check(wallClock.Packet.State == "NoContest", "Maximum observed duration was unbounded");
        var noContest = Fresh(); noContest.Invalidate(); noContest.Death(2, 1, "Combat", 500);
        Check(noContest.Finish().State == "NoContest", "A later valid kill erased the no-contest outcome");

        Console.WriteLine($"REWARD_CLIENT_CHECKS passed={_assertions} failed=0");
    }
}
