using ArenaMode.Bots;
using ArenaMode.Session;
using EFT;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }
    private static void Fill(int ticks = 35) { for (int i = 0; i < ticks; i++) Harness.Tick(); }
    private static void Main()
    {
        foreach (string mode in new[] { "deathmatch", "gungame" })
        foreach (int total in new[] { 2, 4, 8, 10, 12, 16, 24 })
        {
            Harness.Reset(total, false, mode); Fill();
            Check(Harness.Alive == total - 1 && Harness.NativeCalls == total - 1, "total includes reserved human and exact PreActive bot capacity");
            Harness.World.MainPlayer.HealthController.IsAlive = false; Fill();
            Check(Harness.NativeCalls == total - 1, "dead human retains its participant slot");
            var dead = Harness.Spawner._bots.BotOwners[0]; dead.HealthController.IsAlive = false;
            Harness.Tick();
            Check(Harness.Alive == total - 1 && Harness.NativeCalls == total, "one proven bot death permits exactly one replacement");
        }
        Harness.Reset(2); Harness.DelayPreparation = true; Fill(5);
        Check(Harness.Preparations.Count == 1 && Harness.NativeCalls == 0, "unresolved preparation consumes capacity before async profile response");
        Harness.CompleteOne(); Fill(4);
        Check(Harness.Alive == 1 && Harness.NativeCalls == 1, "delayed preparation produces exactly one admitted body");

        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick();
        var oldData = Harness.Preparations.Peek().Data;
        Harness.Tick(181f);
        Check(Harness.Preparations.Count == 2, "only undispatched preparation may expire and retry");
        Harness.CompleteOne();
        Check(oldData.Stops == 1 && Harness.NativeCalls == 0, "expired profile response cannot dispatch");
        Harness.CompleteOne(); Check(Harness.Alive == 1, "replacement preparation survives stale completion");

        Harness.Reset(2); Harness.DelayArrival = Harness.DelayRegistration = true; Harness.Tick();
        Harness.Tick(10000f); Fill(5);
        Check(Harness.NativeCalls == 1, "native uncertainty never expires into overshoot");
        var arrival = Harness.Arrivals.Single();
        Harness.Spawner._bots.BotOwners.Add(arrival.Body); arrival.Callback(arrival.Body); Fill(3);
        Check(Harness.Observed == 1 && Harness.NativeCalls == 1 && arrival.Body.HealthController.IsAlive,
            "native registration before observer is counted once on acceptance");
        arrival.Callback(arrival.Body);
        Check(Harness.Observed == 1 && arrival.Body.HealthController.IsAlive, "same-body duplicate callback preserves accepted actor");
        var extra = new BotOwner(Profile.For(WildSpawnType.pmcUSEC)); Harness.Spawner._bots.BotOwners.Add(extra);
        arrival.Callback(extra);
        Check(!extra.HealthController.IsAlive && Harness.Alive == 1, "distinct duplicate body is administratively retired");

        Harness.Reset(2); Harness.DelayArrival = true; Harness.Tick(); Fill();
        Check(Harness.Alive == 1 && Harness.NativeCalls == 1 && Harness.Observed == 0,
            "registered body with missing observer cannot reopen an uncertain slot");
        Harness.Arrivals[0].Callback(Harness.Arrivals[0].Body);
        Harness.Spawner._bots.BotOwners.Clear(); Fill();
        Check(Harness.NativeCalls == 1, "accepted identity missing from native registry is not a death");

        Harness.Reset(2); Harness.ThrowNative = true; Harness.Tick(); Harness.ThrowNative = false; Fill();
        Check(Harness.NativeCalls == 1 && Harness.LastData.Stops == 0, "exception after native submission retains capacity and does not StopSpawn");
        Harness.Reset(2); Harness.RejectPlacement = true; Harness.Tick(); var deniedData = Harness.LastData;
        Harness.RejectPlacement = false; Harness.Tick();
        Check(deniedData.Stops == 1 && Harness.Alive == 1, "proven dispatch-prefix rejection safely retries");
        foreach (int count in new[] { 0, 2 })
        {
            Harness.Reset(2); Harness.ProfileCount = count; Harness.Tick();
            Check(Harness.NativeCalls == 0 && Harness.LastData.Stops == 1, "wrong-sized profile result never creates a partial or surplus native batch");
        }
        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick();
        Harness.Preparations.Peek().Data.Profiles[0].Info.Settings.Role = WildSpawnType.assault;
        Harness.CompleteOne(); Check(Harness.NativeCalls == 0, "profile role mismatch never enters native");
        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick(); Harness.PlacementAvailable = false;
        Harness.CompleteOne(); Check(Harness.NativeCalls == 0 && Harness.LastData.Stops == 1, "placement is rechecked after async preparation");

        Harness.Reset(2); Harness.DelayArrival = true; Harness.Tick();
        var wrong = Harness.Arrivals[0]; wrong.Body.Profile.Info.Side = EPlayerSide.Savage;
        wrong.Callback(wrong.Body); Harness.DelayArrival = false; Harness.Tick();
        Check(!wrong.Body.HealthController.IsAlive && Harness.Alive == 1, "wrong side arrival retires before replacement");
        Harness.Reset(2); Harness.DelayArrival = Harness.FailRetirement = true; Harness.Tick();
        var failed = Harness.Arrivals[0]; failed.Body.Profile.Info.Side = EPlayerSide.Savage;
        failed.Callback(failed.Body); Harness.Spawner._bots.BotOwners.Clear(); Fill();
        Check(Harness.NativeCalls == 1 && failed.Body.HealthController.IsAlive, "failed retirement retains reservation even if native registry is missing");
        Harness.FailRetirement = false;
        var rejectedExtra = new BotOwner(Profile.For(WildSpawnType.pmcUSEC));
        failed.Callback(rejectedExtra); Fill();
        Check(Harness.NativeCalls == 1 && !rejectedExtra.HealthController.IsAlive && failed.Body.HealthController.IsAlive,
            "retiring distinct duplicate cannot clear original failed-retirement capacity");
        failed.Body.HealthController.IsAlive = false; Fill();
        Check(Harness.NativeCalls == 2, "proven death of exact quarantined original safely releases its ticket");

        Harness.Reset(2); Harness.Tick(); var accepted = Harness.Arrivals[0]; Harness.FailRetirement = true;
        var duplicateAlive = new BotOwner(Profile.For(WildSpawnType.pmcUSEC)); accepted.Callback(duplicateAlive);
        accepted.Body.HealthController.IsAlive = false; Harness.Spawner._bots.BotOwners.Clear(); Fill();
        Check(Harness.NativeCalls == 1 && duplicateAlive.HealthController.IsAlive,
            "failed duplicate retirement is quarantined after original ticket settled");

        var rejectionPolicy = new ArenaSoloPopulationPolicy(2, false);
        rejectionPolicy.TryReserve(0, 0, false, true, 0, out var rejectionTicket);
        rejectionPolicy.TryBeginDispatch(rejectionTicket, 0, 0, 0);
        object originalIdentity = new(), duplicateIdentity = new();
        rejectionPolicy.Rejected(rejectionTicket, originalIdentity, false, true);
        rejectionPolicy.Rejected(rejectionTicket, duplicateIdentity, true, false);
        Check(rejectionPolicy.Outstanding == 1, "retired duplicate cannot settle original native ticket");
        rejectionPolicy.ObserveDeath(duplicateIdentity);
        Check(rejectionPolicy.Outstanding == 1, "duplicate death cannot settle original native ticket");
        rejectionPolicy.ObserveDeath(originalIdentity);
        Check(rejectionPolicy.Outstanding == 0, "exact original death settles its native ticket");
        Harness.Reset(2); Harness.DelayArrival = true; Harness.Tick(); Harness.Arrivals[0].Callback(null); Fill();
        Check(Harness.NativeCalls == 1, "null observer cannot prove native capacity free");

        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick(); BurstSpawnService.Reset();
        Harness.CompleteOne(); Check(Harness.Alive == 1, "ambient reset during player respawn preserves solo preparation");
        Harness.Reset(2); Harness.DelayArrival = true; Harness.Tick(); BurstSpawnService.Reset();
        Harness.Arrivals[0].Callback(Harness.Arrivals[0].Body);
        Check(Harness.Observed == 1, "ambient reset during player respawn preserves native arrival");
        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick(); var stale = Harness.Preparations.Peek();
        ArenaSoloPopulation.Reset(); stale.Task.SetResult(stale.Data);
        Check(Harness.NativeCalls == 0 && stale.Data.Stops == 1, "true raid teardown cancels prepared work");
        Harness.Reset(2); Harness.DelayArrival = true; Harness.Tick(); var late = Harness.Arrivals[0];
        ArenaSoloPopulation.Reset(); late.Callback(late.Body);
        Check(!late.Body.HealthController.IsAlive && Harness.Observed == 0, "true raid teardown retires detached late body");
        Harness.DelayArrival = false; Harness.Tick();
        Check(Harness.Alive == 1, "stale callback cannot contaminate new solo generation");
        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick();
        Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld(); Harness.CompleteOne();
        Check(Harness.NativeCalls == 0, "world identity drift cannot dispatch old prepared profiles");
        Harness.Reset(2); Harness.DelayPreparation = true; Harness.Tick();
        ArenaMode.Modes.ArenaMatchService.Current.Mode.ControlsOwnSpawns = true; Harness.CompleteOne();
        Check(Harness.NativeCalls == 0, "terminal Deathmatch result suppresses late preparation");

        foreach (int total in new[] { 2, 3, 12, 24 })
        {
            Harness.Reset(total, true); Fill();
            var boss = Harness.Spawner._bots.BotOwners.Single(b => b.Profile.Info.Settings.Role == WildSpawnType.bossTagillaAgro);
            Check(Harness.Alive == total - 1, "one dedicated Labyrinth boss slot is inside total");
            Check(boss.Boss.IamBoss && boss.Boss.Followers == 0, "dedicated boss initializes native boss logic with no extra followers");
            for (int death = 0; death < 4; death++)
            {
                boss.HealthController.IsAlive = false; Harness.Tick();
                var expected = death % 2 == 0 ? WildSpawnType.bossKillaAgro : WildSpawnType.bossTagillaAgro;
                boss = Harness.Spawner._bots.BotOwners.Single(b => b.HealthController.IsAlive
                    && (b.Profile.Info.Settings.Role == WildSpawnType.bossTagillaAgro || b.Profile.Info.Settings.Role == WildSpawnType.bossKillaAgro));
                Check(boss.Profile.Info.Settings.Role == expected && Harness.Alive == total - 1,
                    "dedicated boss alternates only after proven death without consuming extra capacity");
                Check(boss.Boss.IamBoss && boss.Boss.Followers == 0, "replacement boss initializes native boss logic with no extra followers");
            }
        }
        Harness.Reset(2, true); Harness.FailBossInitialization = true; Harness.Tick();
        Check(Harness.Alive == 0 && Harness.Observed == 0, "failed native boss initialization retires unaccepted body");
        Harness.FailBossInitialization = false; Harness.Tick();
        Check(Harness.Alive == 1 && Harness.Arrivals.Last().Body.Profile.Info.Settings.Role == WildSpawnType.bossTagillaAgro,
            "failed boss initialization retries same role without premature rotation");
        Harness.Reset(8); Fill();
        Check(Harness.Spawner._bots.BotOwners.Count(b => b.Profile.Info.Settings.Role == WildSpawnType.assault) == 3,
            "normal population mixes PMC floor with scav remainder within total");

        Harness.Reset(); var spawner = Harness.Spawner; var data = new BotCreationData(); data.Profiles.Add(Profile.For(WildSpawnType.pmcUSEC));
        Check(!ArenaSoloSpawnDispatch.CanDispatch(spawner, data), "selected solo rejects unowned spawner batch");
        Check(!Harness.Call("SoloActivationPatch", spawner._botCreator, data, null)
            && !Harness.Call("SoloProfileActivationPatch", new object[] { null })
            && !Harness.Call("SoloCreationPatch", spawner._botCreator, data.Profiles[0], null),
            "direct creator activation and profile creation cannot bypass owned dispatch");
        using (var scope = ArenaSoloSpawnDispatch.Enter(spawner, data))
        {
            Check(!ArenaSoloSpawnDispatch.CanDispatch(new BotSpawner(), data)
                && !ArenaSoloSpawnDispatch.CanDispatch(spawner, new BotCreationData()), "dispatch requires exact spawner and data identities");
            Check(ArenaSoloSpawnDispatch.CanDispatch(spawner, data) && !ArenaSoloSpawnDispatch.CanDispatch(spawner, data), "dispatch lease admits once");
            Check(ArenaSoloSpawnDispatch.CanActivate(spawner._botCreator, data) && !ArenaSoloSpawnDispatch.CanActivate(spawner._botCreator, data), "creator activation admits once");
            Check(!ArenaSoloSpawnDispatch.CanCreate(spawner._botCreator, Profile.For(WildSpawnType.pmcUSEC))
                && ArenaSoloSpawnDispatch.CanCreate(spawner._botCreator, data.Profiles[0])
                && !ArenaSoloSpawnDispatch.CanCreate(spawner._botCreator, data.Profiles[0]), "creator lease binds one exact profile before its await");
        }
        Check(!ArenaSoloSpawnDispatch.CanDispatch(spawner, data), "disposed dispatch scope never remains ambient permission");
        ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true;
        Check(!ArenaSoloSpawnDispatch.CanDispatch(spawner, data), "confirmed matching-map loading blocks unowned native batches");
        Harness.World.LocationId = "other";
        Check(ArenaSoloSpawnDispatch.CanDispatch(spawner, data), "unmatched loading world remains ordinary");
        foreach (string mode in new[] { "tactical", "teamdeathmatch" })
        {
            Harness.Reset(mode: mode); Fill(2);
            Check(Harness.NativeCalls == 0 && ArenaSoloSpawnDispatch.CanDispatch(Harness.Spawner, data), "Tactical and TDM remain outside solo ownership");
        }
        Harness.Reset(); ArenaSession.Participants = null; Fill(2);
        Check(Harness.NativeCalls == 0 && ArenaSoloSpawnDispatch.CanDispatch(Harness.Spawner, data), "legacy null participant protocol remains outside solo ownership");
        Harness.Reset(); ArenaSession.IsActive = false; Fill(2);
        Check(Harness.NativeCalls == 0 && ArenaSoloSpawnDispatch.CanDispatch(Harness.Spawner, data), "ordinary raid remains outside solo ownership");

        Console.WriteLine($"PASS: {_checks} source-linked solo runtime checks.");
        Console.WriteLine("SOLO POPULATION RUNTIME PASSED");
    }
}
