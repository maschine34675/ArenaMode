using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class RewardLifecycleTests
{
    private static readonly MongoId Id = new("abcdefabcdefabcdefabcdef");

    [Fact]
    public void SnapshotEventsObserveDetachedOriginalAndActualSuccessfulSwap()
    {
        var (saves, snapshots) = Create();
        var live = saves.GetProfile(Id);
        live.ProfileInfo!.Username = "original";
        SptProfile? observedClone = null, observedRestore = null;
        snapshots.SnapshotTaken += (_, p) => observedClone = p;
        snapshots.SnapshotRestored += (_, p) =>
        {
            Assert.Same(p, saves.GetProfile(Id));
            observedRestore = p;
        };
        Assert.True(snapshots.TakeSnapshot(Id));
        Assert.NotSame(live, observedClone);
        live.ProfileInfo.Username = "arena mutation";
        Assert.Equal("original", observedClone!.ProfileInfo!.Username);
        Assert.True(snapshots.RestoreSnapshot(Id));
        Assert.Same(observedClone, observedRestore);
        Assert.Equal("original", saves.GetProfile(Id).ProfileInfo!.Username);
    }

    [Fact]
    public void FailedOrDiscardedSnapshotNeverReportsRestoration()
    {
        var (_, snapshots) = Create();
        int restores = 0, discards = 0;
        snapshots.SnapshotRestored += (_, _) => restores++;
        snapshots.SnapshotDiscarded += _ => discards++;
        Assert.False(snapshots.RestoreSnapshot(Id));
        Assert.True(snapshots.TakeSnapshot(Id));
        snapshots.DiscardSnapshot(Id);
        snapshots.DiscardSnapshot(Id);
        Assert.False(snapshots.RestoreSnapshot(Id));
        Assert.Equal(0, restores);
        Assert.Equal(1, discards);
    }

    [Fact]
    public void UnavailableRewardStorageNeverPreventsSnapshotRestore()
    {
        var (saves, snapshots) = Create();
        saves.GetProfile(Id).ProfileInfo!.Username = "protected";
        snapshots.SnapshotTaken += (_, _) => throw new IOException("test reward store unavailable");
        snapshots.SnapshotRestored += (_, _) => throw new IOException("test reward store unavailable");
        Assert.True(snapshots.TakeSnapshot(Id));
        saves.GetProfile(Id).ProfileInfo!.Username = "must not survive";
        Assert.True(snapshots.RestoreSnapshot(Id));
        Assert.False(snapshots.HasSnapshot(Id));
        Assert.Equal("protected", saves.GetProfile(Id).ProfileInfo!.Username);
    }

    [Fact]
    public void OnlyConfirmedExactNativeSessionExposesRewardBinding()
    {
        var sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
        string old = LadderPicker.Current;
        try
        {
            Assert.False(sessions.TryGetRewardSession(Id, out _, out _, out _, out _, out _));
            string arm = sessions.Arm(Id, "factory4_day", "deathmatch", deathmatchHostility: DeathmatchSelection.FreeForAll);
            Assert.False(sessions.TryGetRewardSession(Id, out _, out _, out _, out _, out _));
            string start = Protocol.BuildNativeStartIdentity(arm);
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted, sessions.ProcessLocalRaidStart(Id, "factory4_day", start));
            Assert.True(sessions.BindNativeEndIdentity(Id, start, "factory.Pmc rewards", out string? end));
            Assert.False(sessions.TryGetRewardSession(Id, out _, out _, out _, out _, out _));
            Assert.True(sessions.ConfirmClientRaidStarted(Id, arm, end));
            Assert.True(sessions.TryGetRewardSession(Id, out var boundArm, out var boundEnd, out var mode, out bool team, out bool ffa));
            Assert.Equal(arm, boundArm);
            Assert.Equal(end, boundEnd);
            Assert.Equal("deathmatch", mode);
            Assert.False(team);
            Assert.True(ffa);
            Assert.False(sessions.TryGetRewardSession(new MongoId("111111111111111111111111"), out _, out _, out _, out _, out _));
            Assert.True(sessions.BeginNativeEndFinalization(Id, end));
            Assert.False(sessions.TryGetRewardSession(Id, out _, out _, out _, out _, out _));
            bool observed = false;
            ArenaSessionState observedState = ArenaSessionState.Idle;
            Assert.False(sessions.CompleteNativeEndFinalization(Id, "wrong end", () => observed = true));
            Assert.False(observed);
            Assert.True(sessions.CompleteNativeEndFinalization(Id, end, () =>
            {
                observed = true;
                observedState = sessions.GetStatus(Id).state;
                throw new IOException("reward file unavailable after restore");
            }));
            Assert.True(observed);
            Assert.Equal(ArenaSessionState.FinalizingEnd, observedState);
            Assert.Equal(ArenaSessionState.Idle, sessions.GetStatus(Id).state);
        }
        finally { LadderPicker.Redraw(old, 0); }
    }

    private static (SaveServer, ProfileSnapshotService) Create()
    {
        var saves = new SaveServer(null!, [], null!, null!, null!, null!,
            BotTierScalePatchTests.TestLogger.Create<SaveServer>(), null!);
        saves.AddProfile(new SptProfile { ProfileInfo = new() { ProfileId = Id } });
        return (saves, new ProfileSnapshotService(new SPTarkov.Server.Core.Utils.Cloners.FastCloner(), saves,
            BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>()));
    }
}
