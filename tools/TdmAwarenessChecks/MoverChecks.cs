using ArenaMode;
using ArenaMode.Bots;
using SAIN.Components;
using UnityEngine;
internal static partial class Program
{
    private static string[] MoverDetails() => Plugin.ModLogger.Lines.Where(l => l.StartsWith("Arena TDM mover detail:")).ToArray();
    private static void MoverDiagnostics()
    {
        var raid = Fresh(); raid.Enemy();
        var path = new ActivePath { PathCorners = new() { new Corner() }, CurrentCornerMoveData = new Progress { Magnitude = 9, PercentageComplete = .25f } };
        raid.Sain.MoverState.State = path;
        int reads = 0; raid.Sain.OnGetMover = () => reads++;
        raid.Tick(0); raid.Tick(30);
        Check(MoverDetails().Length == 0 && reads == 0, "mover detail waits for two measured low-motion samples");
        raid.Tick(60);
        string line = MoverDetails().Single();
        Check(line.Contains("path=present, pathId=17, pathAge=59.0s") && line.Contains("paused=True, cancel=False, recalc=False"), "mover identity age and explicit pause preserved");
        Check(line.Contains("index=0, corners=1, corner=(3.00;0.00;0.00), cornerStatus=Moving, cornerDistance=9.0, progress=0.3")
            && line.Contains("cachedPathStatus=PathInvalid, nativePathStatus=PathComplete"), "actual mover corners and distinct native versus cached statuses");
        int firstReads = reads;
        raid.Tick(90); raid.Tick(120);
        Check(MoverDetails().Length == 1 && reads == firstReads, "mover detail reads bounded per body raid");
        Check(path.PausedRequested && path.CurrentIndex == 0 && path.PathCorners.Count == 1 && !line.Contains("private-"), "mover details neither modify path nor reveal identities");
        foreach (var change in new[] { "path", "reuse", "index", "corners", "body" })
        {
            raid = Fresh(); raid.Enemy(); path = new ActivePath { PathCorners = new() { new Corner() } };
            raid.Sain.MoverState.State = path;
            raid.Tick(0); raid.Tick(30);
            int pathReads = 0;
            raid.Sain.MoverState.OnGetPath = () =>
            {
                if (++pathReads != 2) return;
                if (change == "path") raid.Sain.MoverState.State = new();
                if (change == "reuse") path.TimeStarted = 59;
                if (change == "index") path.CurrentIndex = 1;
                if (change == "corners") path.PathCorners = new();
            };
            if (change == "body") raid.Sain.OnGetMover = () => raid.World.Players[raid.Source.ProfileId] = new();
            raid.Tick(60);
            Check(MoverDetails().Length == 0, "mover rejects changed " + change);
            raid.Sain.MoverState.OnGetPath = null; raid.Sain.OnGetMover = null;
            raid.World.Players[raid.Source.ProfileId] = raid.Source.GetPlayer;
            raid.Tick(90);
            Check(MoverDetails().Length == 1, "mixed mover snapshot does not consume allowance");
        }
        foreach (int index in new[] { -1, 1, int.MaxValue })
        {
            raid = Fresh(); raid.Enemy();
            raid.Sain.MoverState.State = new ActivePath { CurrentIndex = index, PathCorners = new() { new Corner() } };
            raid.Tick(0); raid.Tick(30); raid.Tick(60);
            Check(MoverDetails().Single().Contains("corner=n/a"), "invalid corner index stays unavailable");
        }
        raid = Fresh(); raid.Enemy();
        raid.Tick(0); raid.Tick(30); raid.Tick(60);
        Check(MoverDetails().Single().Contains("path=none"), "absent active path is explicit");
        raid = Fresh(); raid.Enemy();
        raid.Sain.MoverState.OnGetPath = () => throw new Exception("modeled read failure");
        raid.Tick(0); raid.Tick(30); raid.Tick(60);
        Check(MoverDetails().Single().Contains("path=n/a"), "unreadable path is not an absent path");
        raid = Fresh(); raid.Enemy(); raid.Tick(0); raid.Tick(30);
        raid.Source.GetPlayer.Position = new Vector3(2); raid.Tick(60); raid.Tick(90);
        Check(MoverDetails().Length == 0, "larger endpoint displacement resets low-motion trigger");
        raid.Tick(120);
        Check(MoverDetails().Length == 1, "new low-motion window can trigger later");
        TeamDeathmatchRelations.Reset(); raid.Tick(150); raid.Tick(180); raid.Tick(210);
        Check(MoverDetails().Length == 2, "mover detail allowance resets with raid");

        // Bypass reconciliation deliberately: a diagnostic call alone must not
        // create relations for a body missing from the affiliation cache.
        raid = Fresh(); var enemy = raid.Enemy();
        var cache = typeof(TeamDeathmatchRelations).GetField("_affiliations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        cache.GetType().GetMethod("Remove", new[] { typeof(object) }).Invoke(cache, new object[] { raid.Source.GetPlayer });
        for (int time = 0; time <= 60; time += 30)
        { Time.time = time; TdmSearchAwareness.Observe(raid.Source, raid.Target, enemy, "test-passive"); }
        Check(MoverDetails().Length == 0 && !TeamDeathmatchRelations.IsRegisteredCurrentBot(raid.Source), "mover diagnosis cannot register a missing affiliation");
    }
}
