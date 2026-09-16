using System.Reflection;
using ArenaMode.Server.Services;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class IsolationProtectionTests
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");

    [Fact]
    public void OrdinaryStartIdentityIsNotANativeEndMarker()
    {
        string ordinary = Protocol.BuildNativeOrdinaryStartIdentity(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"));
        Assert.True(Protocol.HasNativeOrdinaryStartMarker(ordinary));
        Assert.True(Protocol.HasNativeStartMarker(ordinary));
        Assert.False(Protocol.HasNativeEndMarker(ordinary));
    }

    [Fact]
    public void SubstringArenamodeWithoutSeparatorIsNotAnEndMarker()
    {
        Assert.False(Protocol.HasNativeEndMarker("factory4_day.Pmc arenamode leftover"));
        Assert.False(Protocol.HasNativeEndMarker(Protocol.BuildNativeStartIdentity(
            Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ExactEndIdentityKeepsItsMarkerIncludingCaseFoldedSeparator()
    {
        const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string exact = Protocol.BuildNativeEndIdentity("factory4_day.Pmc 01_09_2026_12_00_00", token);
        Assert.True(Protocol.HasNativeEndMarker(exact));
        Assert.True(Protocol.HasNativeEndIdentity(exact, token));
        Assert.True(Protocol.HasNativeEndMarker("factory4_day.Pmc test|ArenaMode:malformed"));
    }

    [Fact]
    public void FailedRestoreCommitKeepsRetryState()
    {
        Assert.False(ArenaRestoreCommit.ShouldDropRetryState(false));
        Assert.True(ArenaRestoreCommit.ShouldDropRetryState(true));
    }

    [Fact]
    public void SkillAndVaultingRestoreDropRetryStateOnlyAfterSuccessfulWriteBack()
    {
        string skillsRestore = MethodBody(
            File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Client", "Combat", "ArenaSkills.cs")),
            "public static void Restore()");
        string vaultRestore = MethodBody(
            File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Client", "Movement", "VaultingTuning.cs")),
            "public static void Restore()");

        Assert.Contains("ArenaRestoreCommit.ShouldDropRetryState(succeeded)", skillsRestore, StringComparison.Ordinal);
        Assert.Contains("ArenaRestoreCommit.ShouldDropRetryState(succeeded)", vaultRestore, StringComparison.Ordinal);
        Assert.DoesNotContain("finally", skillsRestore, StringComparison.Ordinal);
        Assert.DoesNotContain("finally", vaultRestore, StringComparison.Ordinal);
        Assert.DoesNotContain("BuffSnapshot.Clear();", MethodCatch(skillsRestore), StringComparison.Ordinal);
        Assert.DoesNotContain("_applied = false;", MethodCatch(vaultRestore), StringComparison.Ordinal);
    }

    [Fact]
    public void PluginUnloadRestoresClientMutations()
    {
        string unload = MethodBody(
            File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Client", "Plugin.cs")),
            "private void OnDestroy()");
        Assert.Contains("Combat.ArenaSkills.Restore();", unload, StringComparison.Ordinal);
        Assert.Contains("Movement.VaultingTuning.Restore();", unload, StringComparison.Ordinal);
        Assert.Contains("Movement.FastDeployPatches.Restore();", unload, StringComparison.Ordinal);
        Assert.True(unload.IndexOf("Combat.ArenaSkills.Restore();", StringComparison.Ordinal)
                    < unload.IndexOf("Pickups.ArenaPickupRuntime.Reset();", StringComparison.Ordinal));
    }

    [Fact]
    public void UnclaimedNativeEndDoesNotConsumeTheSnapshot()
    {
        InArenaRaid(liveSaveServer: true, (sessions, snapshots, nativeEnd) =>
        {
            Assert.False(ArenaNativeEndAdmission.TryClaimAndRestore(
                sessions, snapshots, Session, "factory.Pmc isolation|arenamode:deadbeefdeadbeefdeadbeefdeadbeef"));
            Assert.True(snapshots.HasSnapshot(Session));
            Assert.Equal(ArenaSessionState.InRaid, sessions.GetStatus(Session).state);
            Assert.Equal(ArenaLocalRaidEndDisposition.FirstArenaEnd,
                sessions.ClassifyLocalRaidEnd(Session, nativeEnd));
        });
    }

    [Fact]
    public void RestoreFailureAfterClaimAbortsFinalizationAndKeepsTheSnapshot()
    {
        InArenaRaid(liveSaveServer: false, (sessions, snapshots, nativeEnd) =>
        {
            Assert.False(ArenaNativeEndAdmission.TryClaimAndRestore(sessions, snapshots, Session, nativeEnd));
            Assert.True(snapshots.HasSnapshot(Session));
            Assert.Equal(ArenaSessionState.InRaid, sessions.GetStatus(Session).state);
            Assert.Equal(ArenaLocalRaidEndDisposition.FirstArenaEnd,
                sessions.ClassifyLocalRaidEnd(Session, nativeEnd));
        });
    }

    [Fact]
    public void SuccessfulClaimRestoresThenLetsOrdinaryStartFollow()
    {
        InArenaRaid(liveSaveServer: true, (sessions, snapshots, nativeEnd) =>
        {
            Assert.True(ArenaNativeEndAdmission.TryClaimAndRestore(sessions, snapshots, Session, nativeEnd));
            Assert.False(snapshots.HasSnapshot(Session));
            Assert.Equal(ArenaSessionState.FinalizingEnd, sessions.GetStatus(Session).state);
            Assert.True(sessions.CompleteNativeEndFinalization(Session, nativeEnd));
            sessions.CutOverToOrdinaryRaid(Session, () => snapshots.HasSnapshot(Session));
        });
    }

    [Fact]
    public void NativeEndAdmissionClaimsBeforeRestore()
    {
        string admission = File.ReadAllText(Path.Combine(
            RepoFiles.Root, "ArenaMode.Server", "Services", "ArenaNativeEndAdmission.cs"));
        Assert.True(admission.IndexOf("BeginNativeEndFinalization", StringComparison.Ordinal)
                    < admission.IndexOf("RestoreSnapshot", StringComparison.Ordinal));
        Assert.Contains("AbortNativeEndFinalization", admission, StringComparison.Ordinal);
        Assert.True(admission.IndexOf("RestoreSnapshot", StringComparison.Ordinal)
                    < admission.IndexOf("AbortNativeEndFinalization", StringComparison.Ordinal));
    }

    private static void InArenaRaid(
        bool liveSaveServer,
        Action<ArenaSessionService, ProfileSnapshotService, string> body)
    {
        string oldDraw = LadderPicker.Current;
        try
        {
            ArenaSessionService sessions;
            ProfileSnapshotService snapshots;
            if (liveSaveServer)
            {
                (sessions, snapshots) = LiveSnapshotSession();
            }
            else
            {
                sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
                snapshots = new ProfileSnapshotService(
                    null!,
                    null!,
                    BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
                var store = (Dictionary<string, SptProfile>)typeof(ProfileSnapshotService)
                    .GetField("_snapshots", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(snapshots)!;
                store[Session.ToString()] = new SptProfile();
            }

            string token = sessions.Arm(Session, "factory4_day", "gungame");
            string start = Protocol.BuildNativeStartIdentity(token);
            if (liveSaveServer)
                Assert.True(snapshots.TakeSnapshot(Session));
            Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
                sessions.ProcessLocalRaidStart(Session, "factory4_day", start));
            Assert.True(sessions.BindNativeEndIdentity(Session, start, "factory.Pmc isolation", out var nativeEnd));
            body(sessions, snapshots, nativeEnd!);
        }
        finally
        {
            LadderPicker.Redraw(oldDraw, 0);
        }
    }

    private static (ArenaSessionService sessions, ProfileSnapshotService snapshots) LiveSnapshotSession()
    {
        var saves = new SaveServer(
            null!,
            [],
            null!,
            null!,
            null!,
            null!,
            BotTierScalePatchTests.TestLogger.Create<SaveServer>(),
            null!);
        saves.AddProfile(new SptProfile { ProfileInfo = new() { ProfileId = Session } });
        var snapshots = new ProfileSnapshotService(
            new SnapshotCloner(),
            saves,
            BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
        var sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
        return (sessions, snapshots);
    }

    private sealed class SnapshotCloner : ICloner
    {
        public T? Clone<T>(T? value)
            => new SPTarkov.Server.Core.Utils.Cloners.FastCloner().Clone(value);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        int brace = source.IndexOf('{', start);
        Assert.True(brace > start, signature);
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(brace, i - brace + 1);
            }
        }

        throw new InvalidOperationException(signature);
    }

    private static string MethodCatch(string body)
    {
        int start = body.IndexOf("catch", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int brace = body.IndexOf('{', start);
        Assert.True(brace > start);
        int depth = 0;
        for (int i = brace; i < body.Length; i++)
        {
            if (body[i] == '{')
                depth++;
            else if (body[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return body.Substring(brace, i - brace + 1);
            }
        }

        throw new InvalidOperationException("catch");
    }
}
