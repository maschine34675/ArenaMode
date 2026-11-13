using System.Text.Json;
using ArenaMode.Bots;
using ArenaMode.Modes;
using EFT.Game.Spawning;
using UnityEngine;

// The order of a solo Tactical round start: the squad's opening request against the body move
// that puts the player where the round begins (docs/reviews/SOLO-TACTICAL-SPAWN-REVIEW.md,
// "Dispatch order"). BurstSpawnService.SpawnPair picks the points right after
// BotCreationData.Create, at least BurstSpawnPointSelector.MinSpawnDistance from wherever the
// player stands then; with a warm profile pool that is inside the request. These checks drive
// the production gate (SoloTacticalSquadDispatch) and the production point selector on the
// installed ArenaMapPort rows, and they read from the production StartRound whether it requests
// the squad itself (the order before 2026-11-13) or hands the request to the gate. With the gate,
// a landing move is requested from the destination by construction, so the distances then only
// confirm the selector's floor; what the simulation proves is the ORDER (a request made at the
// round start fails on five of the six ports). That the release points really come after the
// production move has placed the body is held by the source contracts in Program.cs and by
// tools/TacticalPolishChecks, which runs the extracted production methods.
internal static partial class Program
{
    private const string PostMoveDistance =
        "every squad point is at least 25 m (BurstSpawnPointSelector.MinSpawnDistance) from the post-move position";

    // General/RespawnMinDistance default: ArenaSpawnSelector draws a Player row at least this far
    // from where the player stood, a nearer one only when none is that far.
    private const float RespawnMinDistance = 40f;

    // Tactical/SquadSize default.
    private const int SquadSize = 5;

    private const float Frame = 1f / 60f;

    // The six ArenaMapPort locations Arena admits; Chop Shop and Equator were named in the review
    // of 2026-11-13 and must be installed for this check to say anything.
    private static readonly string[] Ports =
        ["arena_autoservice", "arena_equator", "arena_bay5", "arena_airpit", "arena_bowl", "arena_iceberg"];

    private static readonly string[] RequiredPorts = ["arena_autoservice", "arena_equator"];

    private enum RequestModel
    {
        /// <summary>StartRound sends the request itself, before the multi-frame body move lands.</summary>
        AtRoundStart,

        /// <summary>StartRound hands the request to SoloTacticalSquadDispatch.</summary>
        AfterBodyMove,
    }

    private enum MoveKind
    {
        SurvivorSwap,
        HeldRespawn,
        Refused,
        ReportsFailure,
        NeverReports,
        StartThrows,
        LandsAfterCap,
    }

    private sealed record Move(MoveKind Kind, float After)
    {
        public bool Lands => Kind is MoveKind.SurvivorSwap or MoveKind.HeldRespawn or MoveKind.LandsAfterCap;

        public override string ToString()
            => Kind is MoveKind.SurvivorSwap or MoveKind.HeldRespawn or MoveKind.ReportsFailure or MoveKind.LandsAfterCap
                ? FormattableString.Invariant($"{Kind} after {After:0.000}s")
                : Kind.ToString();
    }

    // Measured on GPU-2 on 2026-11-13: the round-break swap landed 0.075 s after the request; 0 is
    // a swap that reports inside its own start; 2.5 s stands for cold bundles.
    private static readonly Move[] Moves =
    [
        new(MoveKind.SurvivorSwap, 0f),
        new(MoveKind.SurvivorSwap, 0.075f),
        new(MoveKind.SurvivorSwap, 2.5f),
        new(MoveKind.HeldRespawn, 0.075f),
        new(MoveKind.HeldRespawn, 2.5f),
        new(MoveKind.Refused, 0f),
        new(MoveKind.ReportsFailure, 0.5f),
        new(MoveKind.NeverReports, 0f),
        new(MoveKind.StartThrows, 0f),
        new(MoveKind.LandsAfterCap, 60f),
    ];

    private static void CheckDispatchGate()
    {
        Assert(SoloTacticalSquadDispatch.BodyMoveCapSeconds > 0f
               && SoloTacticalSquadDispatch.BodyMoveCapSeconds <= SoloTacticalSpawnPolicy.AssemblySeconds,
            "a move that never reports holds the request no longer than the squad's own 45-second assembly window");

        float now = 0f;
        var sent = new List<(int Round, SoloTacticalSquadRelease Why, float Held)>();
        var gate = new SoloTacticalSquadDispatch(() => now, (round, why, held) => sent.Add((round, why, held)));

        Action<bool> report = null;
        gate.Open(1, landed => { report = landed; return true; });
        Assert(sent.Count == 0 && gate.IsHeldFor(1),
            "a round's request is held while its body move is in flight");
        now = 0.05f;
        Assert(gate.Waiting(1) && sent.Count == 0, "a held round keeps waiting before its cap");
        now = 0.075f;
        report(true);
        Assert(sent.Count == 1 && sent[0] == (1, SoloTacticalSquadRelease.BodyLanded, 0.075f),
            "the landing releases the held request once, with the round and the time it was held");
        report(true);
        report(false);
        Assert(sent.Count == 1 && !gate.Waiting(1) && !gate.IsHeldFor(1),
            "a second report releases nothing and a released round no longer waits");

        sent.Clear();
        gate.Open(2, landed => { landed(true); return true; });
        Assert(sent.Count == 1 && sent[0].Why == SoloTacticalSquadRelease.BodyLanded,
            "a move that lands inside its own start releases the request exactly once");

        sent.Clear();
        gate.Open(3, landed => { landed(false); return false; });
        Assert(sent.Count == 1 && sent[0].Why == SoloTacticalSquadRelease.BodyMoveFailed,
            "a move that reports failure inside its start and then returns false releases the request exactly once");

        sent.Clear();
        gate.Open(4, _ => false);
        gate.Open(5, null);
        Assert(sent.Count == 2 && sent.All(entry => entry.Why == SoloTacticalSquadRelease.NoBodyMove && entry.Held == 0f),
            "a move that is refused, or none at all, releases the request at once");

        sent.Clear();
        bool threw = false;
        try
        {
            gate.Open(6, _ => throw new InvalidOperationException("start"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert(threw && sent.Count == 1 && sent[0].Why == SoloTacticalSquadRelease.NoBodyMove,
            "a start that throws still releases the request, and the caller still sees the exception");

        sent.Clear();
        now = 100f;
        gate.Open(7, _ => true);
        now = 100f + SoloTacticalSquadDispatch.BodyMoveCapSeconds - 0.01f;
        Assert(gate.Waiting(7) && sent.Count == 0, "a move that never reports is waited for up to the cap");
        now = 100f + SoloTacticalSquadDispatch.BodyMoveCapSeconds;
        Assert(!gate.Waiting(7) && sent.Count == 1 && sent[0].Why == SoloTacticalSquadRelease.CapExpired,
            "the cap releases a request whose move never reported, once");

        sent.Clear();
        Action<bool> roundEight = null;
        gate.Open(8, landed => { roundEight = landed; return true; });
        gate.Cancel();
        roundEight(true);
        Assert(sent.Count == 0 && !gate.Waiting(8), "a round that ended before its move landed releases nothing");

        Action<bool> roundNine = null;
        gate.Open(9, landed => { roundNine = landed; return true; });
        Action<bool> roundTen = null;
        gate.Open(10, landed => { roundTen = landed; return true; });
        roundNine(true);
        Assert(sent.Count == 0 && gate.IsHeldFor(10), "a late report from an earlier round's move releases nothing");
        roundTen(true);
        Assert(sent.Count == 1 && sent[0].Round == 10, "the current round's own report releases its request");

        sent.Clear();
        gate.Open(11, _ => true);
        Assert(!gate.Waiting(12) && !gate.IsHeldFor(11) && sent.Count == 0,
            "a hold left over from another round is dropped, so it can never stall the current round");

        int reentrant = 0;
        SoloTacticalSquadDispatch self = null;
        self = new SoloTacticalSquadDispatch(() => now, (round, _, _) =>
        {
            reentrant++;
            self.Report(round, true);
        });
        self.Open(13, landed => { landed(true); return true; });
        Assert(reentrant == 1, "a report from inside the request itself releases nothing more");
    }

    private static void CheckDispatchOrderOnInstalledRows(string tacticalSource, string sptRoot)
    {
        RequestModel model = ReadRequestModel(tacticalSource);
        string database = Path.Combine(sptRoot, "SPT_Runtime", "user", "mods", "ArenaMapPort", "db");
        foreach (string required in RequiredPorts)
            Assert(File.Exists(Path.Combine(database, required, "base.json")),
                $"the installed ArenaMapPort rows of {required} are present under {database}");

        var failures = new List<string>();
        int maps = 0, roundStarts = 0;
        float closest = float.MaxValue;
        foreach (string map in Ports)
        {
            string file = Path.Combine(database, map, "base.json");
            if (!File.Exists(file))
                continue;

            maps++;
            var (players, bots) = ReadRows(file, map);
            var zones = new[] { new BotZone { NameZone = "BotZone_Main", SpawnPoints = bots } };
            ArenaBotSpawnFilter.CurrentMap = map;

            // Where a round can find the player: on any row the map has (a survivor stands where
            // the last fight left them, a dead player respawns from where they fell).
            var origins = players.Concat(bots).Select(point => point.Position).Distinct().ToArray();
            int landingStarts = 0, failingStarts = 0;
            string example = null;
            foreach (Vector3 origin in origins)
            {
                var far = players.Where(row => Vector3.Distance(row.Position, origin) >= RespawnMinDistance).ToArray();
                foreach (ISpawnPoint destinationRow in far.Length > 0 ? far : players)
                foreach (Move move in Moves)
                {
                    Vector3 destination = destinationRow.Position;
                    var start = SimulateRoundStart(model, origin, destination, move);
                    roundStarts++;
                    Assert(start.Requests == 1, $"the round's squad is requested exactly once ({map}, {move})");
                    Assert(start.RequestedAt <= SoloTacticalSpawnPolicy.AssemblySeconds + Frame,
                        $"a move that never reports holds the request no longer than the squad's own 45-second assembly window ({map}, {move})");

                    // The documented limit: a move that lands only after the cap was not waited
                    // for, and the request went out from where the player stood at the cap.
                    if (move.Kind == MoveKind.LandsAfterCap)
                    {
                        Assert(model == RequestModel.AtRoundStart
                               || Math.Abs(start.RequestedAt - SoloTacticalSquadDispatch.BodyMoveCapSeconds) <= Frame,
                            $"a move that outlives the cap gets the request at the cap ({map})");
                        continue;
                    }

                    Vector3 postMove = move.Lands ? destination : origin;
                    bool tooClose = false;
                    for (int first = 0; first < bots.Length; first++)
                    {
                        int cursor = first;
                        BotZone zone = BurstSpawnPointSelector.PickZone(zones, start.RequestedFrom, SquadSize, ref cursor);
                        var points = BurstSpawnPointSelector.PickPoints(zone, start.RequestedFrom, SquadSize,
                            ref cursor, out _, out _, grouped: false);
                        Assert(points != null && points.Count > 0,
                            $"the squad has points at all from {start.RequestedFrom} on {map}");
                        foreach (ISpawnPoint point in points)
                        {
                            float distance = Vector3.Distance(point.Position, postMove);
                            closest = Math.Min(closest, distance);
                            if (distance >= BurstSpawnPointSelector.MinSpawnDistance || tooClose)
                                continue;

                            tooClose = true;
                            example ??= FormattableString.Invariant(
                                $"a player at {origin} moved to the Player row at {destination} ({move}) got squad point {point.Id} at {distance:0.0} m");
                        }
                    }

                    if (move.Lands)
                    {
                        landingStarts++;
                        if (tooClose)
                            failingStarts++;
                    }
                    else if (tooClose)
                    {
                        failingStarts++;
                        example ??= $"{move} at {origin} on {map}";
                    }
                }
            }

            if (failingStarts > 0)
                failures.Add(FormattableString.Invariant(
                    $"{map}: {failingStarts} of {landingStarts} landing round starts ({100.0 * failingStarts / Math.Max(1, landingStarts):0} %) pick a point closer, e.g. {example}"));
        }

        Assert(failures.Count == 0,
            $"{PostMoveDistance} (StartRound order: {model}): " + string.Join("; ", failures));
        Assert(maps >= RequiredPorts.Length, "the dispatch order was simulated on the installed ports");
        Console.WriteLine(FormattableString.Invariant(
            $"Dispatch order ({model}): {roundStarts} simulated round starts on {maps} installed ports; closest squad point {closest:0.0} m from the post-move position."));
    }

    /// <summary>
    /// Reads from the production StartRound when the round's squad request goes out: a request
    /// StartRound sends itself precedes the multi-frame move it starts (the order before
    /// 2026-11-13); otherwise StartRound must hand it to the dispatch gate.
    /// </summary>
    private static RequestModel ReadRequestModel(string tacticalSource)
    {
        string start = MethodBody(tacticalSource, "private void StartRound()");
        if (start.Contains("RequestSoloSquad(", StringComparison.Ordinal))
            return RequestModel.AtRoundStart;

        Assert(start.Contains("_squadDispatch.Open(", StringComparison.Ordinal),
            "StartRound either requests the squad itself or hands the request to the dispatch gate");
        return RequestModel.AfterBodyMove;
    }

    private sealed class RoundStart
    {
        public float Now;
        public Vector3 Position;
        public Vector3 RequestedFrom;
        public float RequestedAt = -1f;
        public int Requests;
    }

    /// <summary>
    /// One round start on a fake frame clock. A frame runs the mode's Update first (TickLive asks
    /// the gate whether the round still waits) and the move's coroutine second, as Unity does. The
    /// profile pool is warm: the points are chosen at the request, from where the player stands.
    /// </summary>
    private static RoundStart SimulateRoundStart(RequestModel model, Vector3 origin, Vector3 destination, Move move)
    {
        const int round = 3;
        var start = new RoundStart { Position = origin };
        void Request()
        {
            start.Requests++;
            start.RequestedFrom = start.Position;
            start.RequestedAt = start.Now;
        }

        var gate = new SoloTacticalSquadDispatch(() => start.Now, (_, _, _) => Request());
        Action<bool> swapReport = null;
        bool Begin(Action<bool> landed)
        {
            switch (move.Kind)
            {
                case MoveKind.SurvivorSwap when move.After <= 0f:
                    start.Position = destination;
                    landed(true);
                    return true;
                case MoveKind.SurvivorSwap:
                case MoveKind.ReportsFailure:
                case MoveKind.LandsAfterCap:
                    swapReport = landed;
                    return true;
                case MoveKind.HeldRespawn:
                case MoveKind.NeverReports:
                    return true;
                case MoveKind.StartThrows:
                    throw new InvalidOperationException("the body move threw while starting");
                default:
                    return false;
            }
        }

        if (model == RequestModel.AtRoundStart)
        {
            Request();
            try { Begin(_ => { }); } catch (InvalidOperationException) { }
        }
        else
        {
            try { gate.Open(round, Begin); } catch (InvalidOperationException) { }
        }

        bool reported = move.After <= 0f;
        for (int frame = 1; frame <= (int)(70f / Frame); frame++)
        {
            start.Now = frame * Frame;
            gate.Waiting(round);

            // One more simulated second after the request and the move's report, so a second
            // request would still be seen.
            if (reported && start.Requests > 0 && start.Now >= move.After + 1f)
                break;

            if (reported || start.Now < move.After)
                continue;

            reported = true;
            switch (move.Kind)
            {
                case MoveKind.SurvivorSwap:
                case MoveKind.LandsAfterCap:
                    start.Position = destination;
                    swapReport?.Invoke(true);
                    break;
                case MoveKind.HeldRespawn:
                    // OnSoloLocalRespawn: the held respawn has published its body.
                    start.Position = destination;
                    gate.Report(round, true);
                    break;
                case MoveKind.ReportsFailure:
                    swapReport?.Invoke(false);
                    break;
            }
        }

        return start;
    }

    private static (ISpawnPoint[] Players, ISpawnPoint[] Bots) ReadRows(string file, string map)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        Assert(document.RootElement.GetProperty("Id").GetString() == map, $"the installed file is {map}");
        var rows = document.RootElement.GetProperty("SpawnPointParams").EnumerateArray().ToArray();
        bool Category(JsonElement row, string value)
            => row.GetProperty("Categories").EnumerateArray().Any(category => category.GetString() == value);
        ISpawnPoint ToPoint(JsonElement row)
        {
            var position = row.GetProperty("Position");
            return new Point(row.GetProperty("Id").GetString(), new Vector3(
                position.GetProperty("x").GetSingle(), position.GetProperty("y").GetSingle(), position.GetProperty("z").GetSingle()));
        }

        var players = rows.Where(row => Category(row, "Player")
                && row.GetProperty("Sides").EnumerateArray().Any(side => side.GetString() is "All" or "Pmc" or "Usec" or "Bear"))
            .Select(ToPoint).ToArray();
        var bots = rows.Where(row => (Category(row, "Bot") || Category(row, "BotPmc"))
                && row.TryGetProperty("BotZoneName", out var zone) && zone.GetString() == "BotZone_Main")
            .Select(ToPoint).ToArray();
        Assert(players.Length > 0 && bots.Length >= SquadSize, $"{map} has Player rows and at least five bot rows");
        return (players, bots);
    }

    /// <summary>One method of a production source, from its signature to its closing brace (LF).</summary>
    private static string MethodBody(string source, string signature)
    {
        string text = source.Replace("\r\n", "\n");
        int first = text.IndexOf(signature, StringComparison.Ordinal);
        int last = first < 0 ? -1 : text.IndexOf("\n    }\n", first, StringComparison.Ordinal);
        if (first < 0 || last < 0)
            throw new InvalidOperationException($"source method not found: {signature}");
        return text.Substring(first, last - first + "\n    }".Length);
    }
}
