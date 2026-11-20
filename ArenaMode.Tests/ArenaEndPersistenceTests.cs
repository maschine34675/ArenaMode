using ArenaMode.Server.Rewards;
using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaEndPersistenceTests
{
    private static readonly MongoId Id = new("abcdefabcdefabcdefabcdef");
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task OldRestoreThenUpdateOrderingReproducesTheReportedNativeFailure()
    {
        var fixture = new ProfileFixture();
        Assert.True(fixture.Snapshots.RestoreSnapshot(Id));
        fixture.Tick(resources: false);
        var failure = await Assert.ThrowsAsync<RewardDataException>(() => fixture.Save());
        Assert.Equal("return-profile-save-not-confirmed", failure.Message);
        Assert.True(fixture.Snapshots.HasPendingPersistence(Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningUpdateDrainsBeforeRestoreAndNewUpdatesCannotMutateTheSavedProfile(bool rewardObserverFails)
    {
        var fixture = new ProfileFixture();
        if (rewardObserverFails) fixture.Snapshots.SnapshotRestored += (_, _) => throw new IOException("optional reward");
        var updates = new RewardReturnRequestGate();
        var window = new ArenaProfilePersistenceWindow(updates);
        var oldUpdate = updates.TryEnter()!;
        var saving = Signal();
        var finishSave = Signal();
        Task end = window.RunAsync(Id.ToString(), () => fixture.Snapshots.HasPendingPersistence(Id),
            () => fixture.Snapshots.RestoreSnapshot(Id), async () =>
            {
                Assert.Equal(fixture.Expected, fixture.Serialize());
                saving.SetResult();
                await finishSave.Task;
                await fixture.Save();
                fixture.Snapshots.CompleteRestoredPersistence(Id);
            }, CancellationToken.None);
        Assert.False(end.IsCompleted);
        Assert.True(fixture.Snapshots.HasSnapshot(Id));
        fixture.Tick(); // A writer that already entered may finish, before the swap.
        oldUpdate.Dispose();
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<bool> attempted = Task.FromResult(true);
        Assert.False(RewardReturnQuiescence.TryEnterUpdate(updates, ref attempted, out var denied));
        Assert.Null(denied);
        Assert.False(await attempted);
        finishSave.SetResult();
        await end.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(RewardRuntimeFiles.SameProfile(fixture.Expected, fixture.Disk));
        Assert.False(updates.Held);
        using var resumed = updates.TryEnter();
        Assert.NotNull(resumed);
        fixture.Tick();
        Assert.NotEqual(fixture.Expected, fixture.Serialize());
    }

    [Fact]
    public async Task SaveFailureAndCachedNoOpKeepTheSameHoldAndWitnessUntilSuccessfulRetry()
    {
        var fixture = new ProfileFixture();
        var updates = new RewardReturnRequestGate();
        var window = new ArenaProfilePersistenceWindow(updates);
        int restores = 0;
        Task Run(Func<Task> save) => window.RunAsync(Id.ToString(), () => fixture.Snapshots.HasPendingPersistence(Id),
            () => { restores++; return fixture.Snapshots.RestoreSnapshot(Id); }, save, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => Run(() => throw new IOException("write failed")));
        Assert.Null(updates.TryEnter());
        await Assert.ThrowsAsync<RewardDataException>(() => Run(() => fixture.Save(write: false)));
        Assert.Null(updates.TryEnter());
        Assert.Equal(fixture.Expected, fixture.Serialize());
        await Run(async () => { await fixture.Save(); fixture.Snapshots.CompleteRestoredPersistence(Id); });
        Assert.Equal(1, restores);
        Assert.False(updates.Held);
        Assert.Equal(fixture.Expected, fixture.Disk);
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelCommittedSaveOrReleaseItsHold()
    {
        var fixture = new ProfileFixture();
        var updates = new RewardReturnRequestGate();
        var window = new ArenaProfilePersistenceWindow(updates);
        var writing = Signal();
        var started = Signal();
        Task committed = window.RunAsync(Id.ToString(), () => fixture.Snapshots.HasPendingPersistence(Id),
            () => fixture.Snapshots.RestoreSnapshot(Id), async () =>
            {
                started.SetResult(); await writing.Task;
                await fixture.Save(); fixture.Snapshots.CompleteRestoredPersistence(Id);
            }, CancellationToken.None);
        await started.Task;
        using var caller = new CancellationTokenSource();
        Task waiting = committed.WaitAsync(caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(committed.IsCompleted);
        Assert.Null(updates.TryEnter());
        writing.SetResult();
        await committed;
        Assert.False(updates.Held);
        Assert.Equal(fixture.Expected, fixture.Disk);
    }

    [Fact]
    public async Task CancelledDrainAndReturnResumeCannotReleaseAnotherOwnersHold()
    {
        var updates = new RewardReturnRequestGate();
        using var active = updates.TryEnter()!;
        using var firstCancellation = new CancellationTokenSource();
        var first = updates.HoldAndDrainAsync(firstCancellation.Token);
        var second = updates.HoldAndDrainAsync(CancellationToken.None);
        var returning = updates.PauseAndDrainAsync(CancellationToken.None);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        updates.Resume();
        Assert.Null(updates.TryEnter());
        active.Dispose();
        using var secondHold = await second;
        await returning;
        Assert.Null(updates.TryEnter());
        secondHold.Dispose(); secondHold.Dispose();
        Assert.False(updates.Held);
        using var resumed = updates.TryEnter();
        Assert.NotNull(resumed);
    }

    [Fact]
    public async Task RaidCompletionCannotReleaseThePermanentReturnHold()
    {
        var updates = new RewardReturnRequestGate();
        using var raid = await updates.HoldAndDrainAsync(CancellationToken.None);
        await updates.PauseAndDrainAsync(CancellationToken.None);
        raid.Dispose();
        Assert.True(updates.Held);
        Assert.Null(updates.TryEnter());
        updates.Resume();
        Assert.False(updates.Held);
    }

    [Fact]
    public async Task CancellationBeforeRestoreKeepsSnapshotAndReopensOnlyItsOwnGate()
    {
        var fixture = new ProfileFixture();
        var updates = new RewardReturnRequestGate();
        var window = new ArenaProfilePersistenceWindow(updates);
        using var active = updates.TryEnter()!;
        using var deadline = new CancellationTokenSource();
        Task end = window.RunAsync(Id.ToString(), () => fixture.Snapshots.HasPendingPersistence(Id),
            () => fixture.Snapshots.RestoreSnapshot(Id), () => throw new Exception("must not save"), deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => end);
        Assert.True(fixture.Snapshots.HasSnapshot(Id));
        Assert.False(fixture.Snapshots.HasPendingPersistence(Id));
        Assert.False(updates.Held);
    }

    [Fact]
    public async Task UncooperativeMutationStillFailsTheCompleteProfileComparison()
    {
        var fixture = new ProfileFixture();
        var updates = new RewardReturnRequestGate();
        var window = new ArenaProfilePersistenceWindow(updates);
        await Assert.ThrowsAsync<RewardDataException>(() => window.RunAsync(Id.ToString(),
            () => fixture.Snapshots.HasPendingPersistence(Id), () => fixture.Snapshots.RestoreSnapshot(Id), async () =>
            {
                fixture.Tick(); // Bypasses the cooperative gate deliberately.
                await fixture.Save();
            }, CancellationToken.None));
        Assert.True(updates.Held);
        Assert.True(fixture.Snapshots.HasPendingPersistence(Id));
    }

    // A profile-write mod (Pack A Punch) strips data on its way to disk. The raid-end readback accepts that only
    // through the captured pipeline: RAM equals the write's input and the write's output equals the disk text.
    [Fact]
    public async Task NativeEndCompletesUnderTransformingPipeline()
    {
        using var scope = new PipelineScope();
        var fixture = new ProfileFixture();
        Assert.True(fixture.Snapshots.RestoreSnapshot(Id));
        var warnings = new List<string>();
        await ArenaProtectedRaidEndService.SaveAndConfirmRestoredAsync(fixture.Snapshots, Id,
            _ => { fixture.Disk = scope.TransformingWrite(Id, fixture.Serialize()); return Task.CompletedTask; },
            p => fixture.SerializeProfile(p), () => fixture.Disk, CancellationToken.None, warnings.Add, "raid-end");
        Assert.NotEqual(fixture.Expected, fixture.Disk);
        Assert.Empty(warnings);
        // The same restored profile without a captured pipeline is the strict failure it always was.
        ProfileWriteProof.ResetForTests();
        await Assert.ThrowsAsync<RewardDataException>(() => ArenaProtectedRaidEndService.SaveAndConfirmRestoredAsync(
            fixture.Snapshots, Id, _ => Task.CompletedTask, p => fixture.SerializeProfile(p), () => fixture.Disk,
            CancellationToken.None, warnings.Add, "raid-end"));
        Assert.Single(warnings);
        Assert.Contains("operation=raid-end;", warnings[0]);
        Assert.Contains("pipeline=unavailable;", warnings[0]);
    }

    [Fact]
    public async Task TransformedOutputThatIsNotOnDiskStaysRefusedAndIsDiagnosed()
    {
        using var scope = new PipelineScope();
        var fixture = new ProfileFixture();
        Assert.True(fixture.Snapshots.RestoreSnapshot(Id));
        var warnings = new List<string>();
        var failure = await Assert.ThrowsAsync<RewardDataException>(() => ArenaProtectedRaidEndService.SaveAndConfirmRestoredAsync(
            fixture.Snapshots, Id,
            _ =>
            {
                scope.TransformingWrite(Id, fixture.Serialize());
                fixture.Disk = fixture.Serialize().Replace("12345", "99999"); // someone else wrote the file
                return Task.CompletedTask;
            }, p => fixture.SerializeProfile(p), () => fixture.Disk, CancellationToken.None, warnings.Add, "failed-start"));
        Assert.Equal("return-profile-save-not-confirmed", failure.Message);
        Assert.True(fixture.Snapshots.HasPendingPersistence(Id));
        string warning = Assert.Single(warnings);
        Assert.Contains("operation=failed-start;", warning);
        Assert.Contains("pipeline=transformed;", warning);
        Assert.Contains("outputDiskMatch=false;", warning);
    }

    // The ProfileWriteProof state for one test: a temp profiles directory and the hook marked ready, with the
    // same Begin/Seal/End sequence the capture patches run around SPT's profile write.
    private sealed class PipelineScope : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ArenaMode.EndPersistence.Tests", Guid.NewGuid().ToString("N"));
        internal PipelineScope()
        {
            Directory.CreateDirectory(_dir);
            ProfileWriteProof.ResetForTests();
            ProfileWriteProof.ProfilesDirectory = () => _dir;
            ProfileWriteProof.MarkReady(true);
        }
        // Removes one inventory item (a subtractive edit away from every protected subtree) and returns the output.
        internal string TransformingWrite(MongoId id, string input)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(input)!;
            ((System.Text.Json.Nodes.JsonArray)node["characters"]!["pmc"]!["Inventory"]!["items"]!).RemoveAt(0);
            string output = node.ToJsonString();
            string path = Path.Combine(_dir, id + ".json");
            ProfileWriteProof.Begin(path, input);
            ProfileWriteProof.Seal(path, System.Text.Encoding.UTF8.GetBytes(output));
            ProfileWriteProof.End();
            return output;
        }
        public void Dispose()
        {
            ProfileWriteProof.ResetForTests();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }

    private sealed class ProfileFixture
    {
        private readonly JsonUtil _json = new([new SptJsonConverterRegistrator()]);
        private readonly SaveServer _saves = new(null!, [], null!, null!, null!, null!,
            BotTierScalePatchTests.TestLogger.Create<SaveServer>(), null!);
        internal readonly ProfileSnapshotService Snapshots;
        internal readonly string Expected;
        internal string Disk = "{}";

        internal ProfileFixture()
        {
            // Native converter + real PMC/scav nested model: unlike the old
            // id-only fixture this covers the actual hideout timestamp/resources.
            _saves.AddProfile(_json.Deserialize<SptProfile>("""
                {"info":{"id":"abcdefabcdefabcdefabcdef"},"characters":{
                 "pmc":{"_id":"abcdefabcdefabcdefabcdef","Info":{"Experience":12345},
                 "Inventory":{"items":[{"_id":"111111111111111111111111","_tpl":"222222222222222222222222","upd":{"StackObjectsCount":3}}]},
                 "Hideout":{"sptUpdateLastRunTimestamp":1793103836,"Production":{},"Areas":[],
                   "HideoutCounters":{"fuelCounter":17.25,"craftingTimeCounter":22.5}},"Quests":[],"Skills":{"Common":[],"Mastering":[]}},
                 "scav":{"_id":"333333333333333333333333","Inventory":{"items":[]},"Quests":[]}},
                 "inraid":{"location":"none","character":"none"},"insurance":[],"userbuilds":{"weaponBuilds":[]}}
                """)!);
            Snapshots = new(new SPTarkov.Server.Core.Utils.Cloners.FastCloner(), _saves,
                BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
            Expected = Serialize();
            Assert.True(Snapshots.TakeSnapshot(Id));
        }
        internal string Serialize() => _json.Serialize(_saves.GetProfile(Id))!;
        internal string SerializeProfile(SptProfile profile) => _json.Serialize(profile)!;
        internal void Tick(bool resources = true)
        {
            var hideout = _saves.GetProfile(Id).CharacterData!.PmcData!.Hideout!;
            hideout.SptUpdateLastRunTimestamp += 10;
            if (resources) hideout.HideoutCounters!.FuelCounter += 1;
        }
        internal Task Save(bool write = true) => ArenaProtectedRaidEndService.SaveAndConfirmRestoredAsync(Snapshots, Id,
            _ => { if (write) Disk = Serialize(); return Task.CompletedTask; }, p => _json.Serialize(p)!, () => Disk, CancellationToken.None);
    }
}
