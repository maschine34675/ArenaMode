using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using System.Reflection;
using System.Text.Json;

namespace ArenaMode.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ArenaContextSerialCollection
{
    public const string Name = "ArenaContext serial";
}

[Collection(ArenaContextSerialCollection.Name)]
public class PitFireTeamPersistenceGuardTests
{
    private const string Session = "0123456789abcdef01234567";
    private const string OtherSession = "89abcdef0123456701234567";

    [Fact]
    public void ExactProtectedSessionPathIsBlockedButOtherPathsFailOpen()
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        string modRoot = Full("SPT_Runtime", "user", "mods", "pitFireTeam-ServerMod");
        guard.BeginRaid(Session, TimeSpan.FromHours(1));

        AssertBlocked(guard, modRoot, Path.Combine(
            modRoot, "Resources", "teammates", Session, "1234.json"));
        AssertBlocked(guard, modRoot, Path.Combine(
            modRoot.ToUpperInvariant(), "RESOURCES", "TEAMMATES", Session.ToUpperInvariant(), "1234.JSON"));

        // Canonicalization must recognize a path that resolves into the protected
        // directory, but must not mistake traversal out to another session for it.
        AssertBlocked(guard, modRoot, Path.Combine(
            modRoot, "Resources", "teammates", OtherSession, "..", Session, "1234.json"));
        AssertAllowed(guard, modRoot, Path.Combine(
            modRoot, "Resources", "teammates", Session, "..", OtherSession, "1234.json"));

        AssertAllowed(guard, modRoot, Path.Combine(
            modRoot, "Resources", "teammates", OtherSession, "1234.json"));
        AssertAllowed(guard, modRoot, Path.Combine(
            modRoot, "Resources", "teammates", Session + "0", "1234.json"));
        AssertAllowed(guard, modRoot, Path.Combine(
            modRoot, "Resources", "recruit-requests.json"));
        AssertAllowed(guard, modRoot, Path.Combine(
            Path.GetDirectoryName(modRoot)!, "someOtherMod", "Resources", "teammates", Session, "1234.json"));
    }

    [Fact]
    public void InactiveDifferentAndMalformedSessionsNeverBroadenThePathGuard()
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        string modRoot = Full("SPT_Runtime", "user", "mods", "pitFireTeam-ServerMod");
        string protectedPath = Path.Combine(
            modRoot, "Resources", "teammates", Session, "1234.json");

        AssertAllowed(guard, modRoot, protectedPath);

        guard.BeginRaid("..", TimeSpan.FromHours(1));
        Assert.False(guard.AnyProtected());
        AssertAllowed(guard, modRoot, protectedPath);

        guard.BeginRaid(OtherSession, TimeSpan.FromHours(1));
        AssertAllowed(guard, modRoot, protectedPath);
    }

    [Fact]
    public void FinalizationBlocksDelayedWritesThenExpiresAndClearEndsItImmediately()
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        string modRoot = Full("SPT_Runtime", "user", "mods", "pitFireTeam-ServerMod");
        string profile = Path.Combine(
            modRoot, "Resources", "teammates", Session, "1234.json");

        guard.BeginPostRaidFinalization(Session);
        AssertBlocked(guard, modRoot, profile);

        now += PitFireTeamPersistenceGuard.PostRaidFinalizationWindow - TimeSpan.FromMilliseconds(1);
        AssertBlocked(guard, modRoot, profile);

        now += TimeSpan.FromMilliseconds(1);
        AssertAllowed(guard, modRoot, profile);
        Assert.False(guard.AnyProtected());

        guard.BeginPostRaidFinalization(Session);
        guard.Clear(Session); // cancel, failure, or the next ordinary raid
        AssertAllowed(guard, modRoot, profile);
    }

    [Fact]
    public void ArmedAndRaidFailureWindowsAlsoExpireInsteadOfLeakingForever()
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        string modRoot = Full("SPT_Runtime", "user", "mods", "pitFireTeam-ServerMod");
        string profile = Path.Combine(
            modRoot, "Resources", "teammates", Session, "1234.json");

        guard.BeginArmed(Session, TimeSpan.FromSeconds(5));
        AssertBlocked(guard, modRoot, profile);
        now += TimeSpan.FromSeconds(5);
        AssertAllowed(guard, modRoot, profile);

        guard.BeginRaid(Session, TimeSpan.FromMinutes(2));
        AssertBlocked(guard, modRoot, profile);
        now += TimeSpan.FromMinutes(2);
        AssertAllowed(guard, modRoot, profile);
    }

    [Fact]
    public void EveryFileUtilMutationOverloadHasAnExactArenaPrefix()
    {
        const string harmonyId = "com.maschine.ArenaMode.tests.fileutil";
        var expected = new HashSet<string>
        {
            Signature(nameof(FileUtil.WriteFile), typeof(string), typeof(string)),
            Signature(nameof(FileUtil.WriteFile), typeof(string), typeof(byte[])),
            Signature(nameof(FileUtil.WriteFileAsync), typeof(string), typeof(string), typeof(CancellationToken)),
            Signature(nameof(FileUtil.WriteFileAsync), typeof(string), typeof(byte[]), typeof(CancellationToken)),
            Signature(nameof(FileUtil.DeleteFile), typeof(string)),
        };

        Type[] patchTypes =
        [
            typeof(ArenaPitFireTeamTextWritePatch),
            typeof(ArenaPitFireTeamByteWritePatch),
            typeof(ArenaPitFireTeamAsyncTextWritePatch),
            typeof(ArenaPitFireTeamAsyncByteWritePatch),
            typeof(ArenaPitFireTeamDeleteFilePatch),
        ];

        var targets = patchTypes.Select(type =>
        {
            object patch = Activator.CreateInstance(type)!;
            MethodInfo getter = type.GetMethod(
                "GetTargetMethod",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var target = (MethodBase)getter.Invoke(patch, null)!;
            return (type, target);
        }).ToArray();
        var actual = targets.Select(pair => Signature(
            pair.target.Name,
            pair.target.GetParameters().Select(parameter => parameter.ParameterType).ToArray())).ToHashSet();

        Assert.True(expected.SetEquals(actual));

        // Patch the installed SPT methods offline as well as comparing their
        // signatures. This catches an invalid Harmony argument/result binding,
        // especially on the two Task-returning overloads.
        var harmony = new Harmony(harmonyId);
        try
        {
            foreach ((Type type, MethodBase target) in targets)
            {
                MethodInfo prefix = type.GetMethod(
                    "Prefix",
                    BindingFlags.Static | BindingFlags.Public)!;
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Assert.Contains(
                    Harmony.GetPatchInfo(target)!.Prefixes,
                    patch => patch.owner == harmonyId);
            }
        }
        finally
        {
            harmony.UnpatchSelf();
        }
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("zombie")]
    public void SoloArmDoesNotProtectButTeamArmBlocksAllFiveFileMutations(string mode)
    {
        ArenaSessionService? previous = GetSessions();
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        SetSessions(sessions);

        try
        {
            var sessionId = new MongoId(Session);
            var fileUtil = new FileUtil();
            string profile = Path.Combine(
                fileUtil.GetModPath("pitFireTeam-ServerMod"),
                "Resources",
                "teammates",
                Session,
                "1234.json");

            // No active Arena: every prefix exits before optional-mod path work.
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));

            // Solo Arena deliberately leaves PitFireTeam persistence alone.
            sessions.Arm(sessionId, "factory4_day", mode);
            Assert.False(sessions.IsExternalProfileWriteProtected(sessionId));
            Assert.False(sessions.AnyExternalProfileWriteProtection());
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
            Assert.False(sessions.BeginPostRaidFinalization(sessionId));
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));

            // Only an explicitly requested PFT team raid owns this boundary.
            sessions.Arm(
                sessionId,
                "factory4_day",
                mode,
                protectPitFireTeam: true);
            Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
            Assert.True(sessions.AnyExternalProfileWriteProtection());
            Assert.False(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
            Assert.False(ArenaPitFireTeamByteWritePatch.Prefix(fileUtil, profile));

            Task asyncTextResult = Task.Delay(1);
            Assert.False(ArenaPitFireTeamAsyncTextWritePatch.Prefix(
                fileUtil,
                profile,
                ref asyncTextResult));
            Assert.True(asyncTextResult.IsCompletedSuccessfully);

            Task asyncByteResult = Task.Delay(1);
            Assert.False(ArenaPitFireTeamAsyncByteWritePatch.Prefix(
                fileUtil,
                profile,
                ref asyncByteResult));
            Assert.True(asyncByteResult.IsCompletedSuccessfully);

            bool deleteResult = true;
            Assert.False(ArenaPitFireTeamDeleteFilePatch.Prefix(
                fileUtil,
                profile,
                ref deleteResult));
            Assert.False(deleteResult);

            string otherProfile = Path.Combine(
                fileUtil.GetModPath("pitFireTeam-ServerMod"),
                "Resources",
                "teammates",
                OtherSession,
                "1234.json");
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, otherProfile));

            sessions.MarkIdle(sessionId);
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
        }
        finally
        {
            SetSessions(previous);
        }
    }

    [Fact]
    public void CommittedRaidStartAnnouncementReplaysOnlyItsExactOriginalArm()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string soloToken = "10101010101010101010101010101010";

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: false,
            armToken: soloToken);

        Assert.True(sessions.TryReplayArmedRequest(
            sessionId,
            "FACTORY4_DAY",
            "TACTICAL",
            protectPitFireTeam: false,
            soloToken,
            out string? replayedSolo));
        Assert.Equal(soloToken, replayedSolo);
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "laboratory",
            "tactical",
            protectPitFireTeam: false,
            soloToken,
            out _));
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "rounds",
            protectPitFireTeam: false,
            soloToken,
            out _));
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            soloToken,
            out _));
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: false,
            "99999999999999999999999999999999",
            out _));

        Assert.True(ConsumeArm(sessions, sessionId, "factory4_day"));
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: false,
            soloToken,
            out _));
        sessions.MarkIdle(sessionId);

        const string teamToken = "20202020202020202020202020202020";
        Assert.True(sessions.BeginTeamArmPreparation(
            sessionId,
            "laboratory",
            "tactical",
            teamToken));
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "laboratory",
            "tactical",
            protectPitFireTeam: true,
            teamToken,
            out _));
        Assert.True(sessions.CompleteTeamArmPreparation(sessionId));
        Assert.True(sessions.TryReplayArmedRequest(
            sessionId,
            "laboratory",
            "tactical",
            protectPitFireTeam: true,
            teamToken,
            out string? replayedTeam));
        Assert.Equal(teamToken, replayedTeam);
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public async Task SoloSnapshotPreparationSerializesStatusAndExactCancellation()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string requestId = "30303030303030303030303030303030";
        var preparationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSnapshot = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task preparation = Task.Run(async () =>
        {
            using (await ArenaRaidStartGate.EnterAsync(
                       sessionId,
                       CancellationToken.None))
            {
                Assert.True(sessions.BeginArmPreparation(
                    sessionId,
                    "factory4_day",
                    "tactical",
                    protectPitFireTeam: false,
                    requestId));
                preparationEntered.TrySetResult();
                await releaseSnapshot.Task;
                Assert.True(sessions.CompleteArmPreparation(sessionId));
            }
        });

        await preparationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ArenaSessionState.Preparing, sessions.GetStatus(sessionId).state);
        Assert.Equal(requestId, sessions.GetArmToken(sessionId));

        var statusAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(ArenaSessionState state, string? token)> gatedStatus = Task.Run(async () =>
        {
            statusAttempted.TrySetResult();
            using (await ArenaRaidStartGate.EnterAsync(
                       sessionId,
                       CancellationToken.None))
            {
                return (sessions.GetStatus(sessionId).state, sessions.GetArmToken(sessionId));
            }
        });

        await statusAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        Assert.False(gatedStatus.IsCompleted);

        releaseSnapshot.TrySetResult();
        await preparation.WaitAsync(TimeSpan.FromSeconds(2));
        var observed = await gatedStatus.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ArenaSessionState.Armed, observed.state);
        Assert.Equal(requestId, observed.token);

        Assert.True(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: false,
            requestId,
            out string? replayed));
        Assert.Equal(requestId, replayed);
        Assert.False(sessions.TryReplayArmedRequest(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: false,
            "40404040404040404040404040404040",
            out _));
        Assert.False(sessions.TryBeginArmedCancellation(
            sessionId,
            "40404040404040404040404040404040"));
        Assert.Equal(ArenaSessionState.Armed, sessions.GetStatus(sessionId).state);
        Assert.True(sessions.TryBeginArmedCancellation(sessionId, requestId));
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public void OrdinaryStartMarkerRequiresExactClientGeneration()
    {
        const string requestId = "50505050505050505050505050505050";
        const string processId = "60606060606060606060606060606060";
        string exact = Protocol.BuildNativeOrdinaryStartIdentity(
            processId,
            requestId);

        Assert.True(Protocol.IsNativeOrdinaryStartIdentity(exact));
        Assert.True(Protocol.HasNativeOrdinaryStartMarker(exact));
        Assert.False(Protocol.IsNativeOrdinaryStartIdentity(
            Protocol.NativeOrdinaryStartIdentityPrefix + processId + ":not-a-guid"));
        Assert.True(Protocol.HasNativeOrdinaryStartMarker(
            "  ARENAMODE-ORDINARY-START:malformed"));
        Assert.False(Protocol.IsNativeOrdinaryStartIdentity(
            "  ARENAMODE-ORDINARY-START:malformed"));
    }

    [Fact]
    public void ConfirmedOrdinaryRaidCanOnlyBeRecoveredByANewClientProcess()
    {
        const string owner = "70707070707070707070707070707070";
        const string replacement = "80808080808080808080808080808080";

        Assert.True(OrdinaryRaidRecoveryPolicy.AllowsAbandon(
            clientRaidStarted: false,
            owner,
            owner));
        Assert.False(OrdinaryRaidRecoveryPolicy.AllowsAbandon(
            clientRaidStarted: true,
            owner,
            owner));
        Assert.True(OrdinaryRaidRecoveryPolicy.AllowsAbandon(
            clientRaidStarted: true,
            owner,
            replacement));
    }

    [Fact]
    public void ProtectedTeamRaidRequiresSuccessfulLocationMutationSignal()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true);
        Assert.True(ConsumeArm(sessions, sessionId, "factory4_day"));

        var pending = sessions.GetProtectedTeamRaidValidation(sessionId);
        Assert.True(pending.protectedTeam);
        Assert.False(pending.locationReady);
        Assert.Contains("not completed", pending.failure, StringComparison.OrdinalIgnoreCase);

        sessions.MarkLocationMutationFailed(sessionId, "mutator exploded");
        var failed = sessions.GetProtectedTeamRaidValidation(sessionId);
        Assert.True(failed.protectedTeam);
        Assert.False(failed.locationReady);
        Assert.Equal("mutator exploded", failed.failure);

        sessions.MarkLocationMutationReady(sessionId);
        var ready = sessions.GetProtectedTeamRaidValidation(sessionId);
        Assert.True(ready.protectedTeam);
        Assert.True(ready.locationReady);
        Assert.Null(ready.failure);

        sessions.MarkIdle(sessionId);
        var idle = sessions.GetProtectedTeamRaidValidation(sessionId);
        Assert.False(idle.protectedTeam);
        Assert.False(idle.locationReady);
    }

    [Fact]
    public async Task TeamArmPreparationDrainsOlderOperationsBeforePublishingSnapshotBoundary()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        var operationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseOperation = new ManualResetEventSlim();

        Task olderOperation = Task.Run(() =>
        {
            using (PitFireTeamOperationGate.EnterOperation())
            {
                operationEntered.TrySetResult();
                releaseOperation.Wait();
            }
        });
        await operationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<bool> preparation = Task.Run(() =>
            sessions.BeginTeamArmPreparation(
                sessionId,
                "factory4_day",
                "tactical",
                "0123456789abcdef0123456789abcdef"));
        await Task.Delay(100);
        Assert.False(preparation.IsCompleted);

        releaseOperation.Set();
        await olderOperation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await preparation.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ArenaSessionState.Preparing, sessions.GetStatus(sessionId).state);
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
        Assert.False(ConsumeArm(sessions, sessionId, "factory4_day"));

        Assert.True(sessions.CompleteTeamArmPreparation(sessionId));
        Assert.Equal(ArenaSessionState.Armed, sessions.GetStatus(sessionId).state);
        Assert.True(ConsumeArm(sessions, sessionId, "factory4_day"));
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public async Task OrdinaryRaidCutoverWaitsForOlderOperationBeforeClearingFinalizationGuard()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        var readerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cutoverScheduled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseReader = new ManualResetEventSlim();

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true);
        Assert.True(sessions.BeginPostRaidFinalization(sessionId));
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));

        Task olderOperation = Task.Run(() =>
        {
            using (PitFireTeamOperationGate.EnterOperation())
            {
                readerEntered.TrySetResult();
                releaseReader.Wait();
            }
        });

        try
        {
            await readerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task cutover = Task.Run(() =>
            {
                cutoverScheduled.TrySetResult();
                sessions.CutOverToOrdinaryRaid(sessionId, () => false);
            });
            await cutoverScheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(100);

            Assert.False(cutover.IsCompleted);
            Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));

            releaseReader.Set();
            await olderOperation.WaitAsync(TimeSpan.FromSeconds(2));
            await cutover.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(sessions.IsExternalProfileWriteProtected(sessionId));
        }
        finally
        {
            releaseReader.Set();
            await olderOperation.WaitAsync(TimeSpan.FromSeconds(2));
            sessions.MarkIdle(sessionId);
        }
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("zombie")]
    public void PreparingAndArmedStateKeepSnapshotBoundaryClosedPastLowLevelTtl(string mode)
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        var sessions = new ArenaSessionService(
            NoOpLogger.Create<ArenaSessionService>(),
            guard);
        var sessionId = new MongoId(Session);
        int previousTtl = ArenaContext.Config.ArmedFlagTtlSeconds;
        ArenaContext.Config.ArmedFlagTtlSeconds = 2;

        try
        {
            Assert.True(sessions.BeginTeamArmPreparation(
                sessionId,
                "factory4_day",
                mode,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

            now += TimeSpan.FromMinutes(14);
            Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));

            Assert.True(sessions.CompleteTeamArmPreparation(sessionId));
            now += TimeSpan.FromSeconds(1);
            Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
            now += TimeSpan.FromHours(1);
            Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
            sessions.MarkIdle(sessionId);
            Assert.False(sessions.IsExternalProfileWriteProtected(sessionId));
        }
        finally
        {
            ArenaContext.Config.ArmedFlagTtlSeconds = previousTtl;
            sessions.MarkIdle(sessionId);
        }
    }

    [Fact]
    public void StaleProtectedInRaidStateStillBlocksCrossStoreWritesAfterGuardTtl()
    {
        DateTime now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        var sessions = new ArenaSessionService(
            NoOpLogger.Create<ArenaSessionService>(),
            guard);
        var sessionId = new MongoId(Session);
        var fileUtil = new FileUtil();
        string modRoot = fileUtil.GetModPath("pitFireTeam-ServerMod");
        string profile = Path.Combine(
            modRoot,
            "Resources",
            "teammates",
            Session,
            "1234.json");

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: "abababababababababababababababab");
        Assert.True(ConsumeArm(sessions, sessionId, "factory4_day"));

        now += TimeSpan.FromDays(7);
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
        Assert.True(sessions.AnyExternalProfileWriteProtection());
        Assert.True(sessions.ShouldBlockPitFireTeamFileMutation(
            profile,
            modRoot,
            out string? protectedSession));
        Assert.Equal(Session, protectedSession);

        sessions.MarkIdle(sessionId);
        Assert.False(sessions.IsExternalProfileWriteProtected(sessionId));
    }

    [Fact]
    public void OrdinaryStartCannotClearAConcurrentlyPublishedPreparingGuard()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);

        Assert.True(sessions.BeginTeamArmPreparation(
            sessionId,
            "factory4_day",
            "tactical",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress,
            ProcessStart(sessions, sessionId, "factory4_day"));
        Assert.Equal(ArenaSessionState.Preparing, sessions.GetStatus(sessionId).state);
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));

        Assert.True(sessions.CompleteTeamArmPreparation(sessionId));
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public void FailedStartTokenCannotCancelAConfirmedOrDifferentArenaRaid()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string token = "cccccccccccccccccccccccccccccccc";

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: token);
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ArenaStarted,
            ProcessStart(sessions, sessionId, "factory4_day"));
        Assert.False(sessions.TryBeginFailedStartRecovery(
            sessionId,
            "dddddddddddddddddddddddddddddddd"));
        string nativeEnd = Protocol.BuildNativeEndIdentity(
            "factory4_day.Pmc unit-test",
            token);
        Assert.False(sessions.ConfirmClientRaidStarted(
            sessionId,
            token,
            Protocol.BuildNativeEndIdentity(
                "factory4_day.Pmc forged",
                token)));
        Assert.True(sessions.ConfirmClientRaidStarted(
            sessionId,
            token,
            nativeEnd));
        Assert.False(sessions.TryBeginFailedStartRecovery(sessionId, token));
        sessions.MarkIdle(sessionId);

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: token);
        Assert.True(ConsumeArm(sessions, sessionId, "factory4_day"));
        Assert.True(sessions.TryBeginFailedStartRecovery(sessionId, token));
        Assert.Equal(
            ArenaSessionState.RecoveringStartFailure,
            sessions.GetStatus(sessionId).state);
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
        sessions.MarkIdle(sessionId);
        Assert.False(sessions.IsExternalProfileWriteProtected(sessionId));
    }

    [Fact]
    public void ExactNativeStartRetryCannotRestoreItsOwnLiveArenaSnapshot()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string token = "12121212121212121212121212121212";
        string nativeStart = Protocol.BuildNativeStartIdentity(token);

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: token);

        Assert.Equal(
            ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", nativeStart));
        Assert.Equal(
            ArenaLocalRaidStartDisposition.DuplicateArenaStart,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", nativeStart));
        Assert.Equal(ArenaSessionState.InRaid, sessions.GetStatus(sessionId).state);
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));

        // A later ordinary request cannot recover a snapshot implicitly, and a
        // foreign Arena marker is rejected rather than falling through to SPT.
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", null));
        Assert.Equal(
            ArenaLocalRaidStartDisposition.RejectedArenaStart,
            sessions.ProcessLocalRaidStart(
                sessionId,
                "factory4_day",
                Protocol.BuildNativeStartIdentity(
                    "99999999999999999999999999999999")));
        Assert.Equal(ArenaSessionState.InRaid, sessions.GetStatus(sessionId).state);
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public void TaggedNativeStartWithoutExactLiveArmAlwaysFailsClosed()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string token = "90909090909090909090909090909090";
        string tagged = Protocol.BuildNativeStartIdentity(token);

        Assert.Equal(
            ArenaLocalRaidStartDisposition.RejectedArenaStart,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", tagged));

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: token);
        Assert.Equal(
            ArenaLocalRaidStartDisposition.RejectedArenaStart,
            sessions.ProcessLocalRaidStart(sessionId, "laboratory", tagged));
        Assert.Equal(ArenaSessionState.Armed, sessions.GetStatus(sessionId).state);
        sessions.MarkIdle(sessionId);

        Assert.Equal(
            ArenaLocalRaidStartDisposition.RejectedArenaStart,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", tagged));
        Assert.True(Protocol.HasNativeStartMarker(
            "  ARENAMODE-START:malformed"));
        Assert.True(Protocol.HasNativeEndMarker(
            "factory4_day.Pmc test|ArenaMode:malformed"));
        Assert.Equal(
            ArenaLocalRaidStartDisposition.RejectedArenaStart,
            sessions.ProcessLocalRaidStart(
                sessionId,
                "factory4_day",
                "  ARENAMODE-START:malformed"));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(
                sessionId,
                "factory4_day.Pmc test|ArenaMode:malformed"));
    }

    [Fact]
    public void ExactNativeEndIdentitySerializesOverlapAndLeavesCompletedTombstone()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        const string token = "34343434343434343434343434343434";
        string nativeStart = Protocol.BuildNativeStartIdentity(token);
        string nativeEnd = Protocol.BuildNativeEndIdentity(
            "factory4_day.Pmc 01_09_2026_12_00_00",
            token);
        string foreignEnd = Protocol.BuildNativeEndIdentity(
            "factory4_day.Pmc 01_09_2026_11_00_00",
            "56565656565656565656565656565656");
        string forgedSameTokenEnd = Protocol.BuildNativeEndIdentity(
            "factory4_day.Pmc 01_09_2026_13_00_00",
            token);

        sessions.Arm(
            sessionId,
            "factory4_day",
            "tactical",
            protectPitFireTeam: true,
            armToken: token);
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", nativeStart));
        Assert.False(sessions.TryBeginFailedStartRecovery(sessionId, token));
        Assert.True(sessions.BindNativeEndIdentity(
            sessionId,
            nativeStart,
            "factory4_day.Pmc 01_09_2026_12_00_00",
            out string? boundNativeEnd));
        Assert.Equal(nativeEnd, boundNativeEnd);
        Assert.True(sessions.CanReplayNativeStartResponse(sessionId, nativeStart));
        Assert.True(sessions.OwnsNativeStartTransaction(sessionId, nativeStart));
        Assert.False(sessions.OwnsNativeStartTransaction(
            sessionId,
            Protocol.BuildNativeStartIdentity(
                "abababababababababababababababab")));

        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, foreignEnd));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, forgedSameTokenEnd));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.FirstArenaEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, nativeEnd));
        Assert.True(sessions.BeginNativeEndFinalization(sessionId, nativeEnd));
        Assert.Equal(ArenaSessionState.FinalizingEnd, sessions.GetStatus(sessionId).state);
        Assert.True(sessions.TryGetFinalizingNativeEnd(
            sessionId,
            token,
            out string? finalizingNativeEnd));
        Assert.Equal(nativeEnd, finalizingNativeEnd);
        Assert.False(sessions.TryGetFinalizingNativeEnd(
            sessionId,
            "00000000000000000000000000000000",
            out _));
        Assert.True(sessions.IsExternalProfileWriteProtected(sessionId));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.FinalizingArenaEndRetry,
            sessions.ClassifyLocalRaidEnd(sessionId, nativeEnd));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, foreignEnd));
        Assert.Equal(
            ArenaLocalRaidStartDisposition.ProtectedTransitionInProgress,
            sessions.ProcessLocalRaidStart(sessionId, "factory4_day", null));

        Assert.True(sessions.CompleteNativeEndFinalization(sessionId, nativeEnd));
        Assert.Equal(ArenaSessionState.Idle, sessions.GetStatus(sessionId).state);
        Assert.Equal(
            ArenaLocalRaidEndDisposition.CompletedArenaEndRetry,
            sessions.ClassifyLocalRaidEnd(sessionId, nativeEnd));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, foreignEnd));
        Assert.True(sessions.IsCompletedNativeEndArm(sessionId, token));
        Assert.False(sessions.CanReplayNativeStartResponse(sessionId, nativeStart));
        Assert.False(sessions.OwnsNativeStartTransaction(sessionId, nativeStart));
        Assert.False(sessions.IsCompletedNativeEndArm(
            sessionId,
            "00000000000000000000000000000000"));

        // A new Arena arm replaces SessionEntry, but a bounded independent
        // tombstone still suppresses a very late retry from the completed raid.
        sessions.Arm(
            sessionId,
            "laboratory",
            "tactical",
            protectPitFireTeam: true,
            armToken: "78787878787878787878787878787878");
        Assert.Equal(
            ArenaLocalRaidEndDisposition.CompletedArenaEndRetry,
            sessions.ClassifyLocalRaidEnd(sessionId, nativeEnd));
        Assert.Equal(
            ArenaLocalRaidEndDisposition.RejectedProtectedEnd,
            sessions.ClassifyLocalRaidEnd(sessionId, foreignEnd));
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public async Task InviteOriginFlowsIntoTaskRunAfterParentScopeExits()
    {
        var sessionId = new MongoId(Session);
        object scope = PitFireTeamInviteOriginScope.Enter(sessionId);
        var releaseChild = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> child = Task.Run(async () =>
        {
            await releaseChild.Task;
            return PitFireTeamInviteOriginScope.TryGetMatchingSession(
                [sessionId],
                out MongoId captured)
                && captured == sessionId;
        });

        PitFireTeamInviteOriginScope.Exit(scope);
        Assert.False(PitFireTeamInviteOriginScope.TryGetMatchingSession(
            [sessionId],
            out _));
        releaseChild.TrySetResult();
        Assert.True(await child.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task InviteTaskRunIsRegisteredBeforeSchedulingAndDrainsAfterCompleteChild()
    {
        var sessionId = new MongoId(Session);
        object scope = PitFireTeamInviteOriginScope.Enter(sessionId);
        var childEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> childBody = async () =>
        {
            Task notificationResult = Task.CompletedTask;
            Assert.True(PitFireTeamDelayedInviteSideEffectPatch.NotificationPrefix(
                [sessionId],
                ref notificationResult,
                out PitFireTeamAsyncNotificationState? nestedState));
            Assert.Null(nestedState);
            childEntered.TrySetResult();
            await releaseChild.Task;
        };

        Assert.True(PitFireTeamInviteOriginScope.TryWrapCurrentTaskRun(ref childBody));
        Task child = Task.Run(childBody);
        PitFireTeamInviteOriginScope.Exit(scope);
        await childEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromMilliseconds(50)));
        releaseChild.TrySetResult();
        await child.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task TeamPreparationWaitsForLogicalAsyncInviteSideEffectAfterSyncLeaseEnds()
    {
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        var sessionId = new MongoId(Session);
        using var releaseSynchronous = new ManualResetEventSlim();
        using var synchronousReleased = new ManualResetEventSlim();
        using var releaseLogical = new ManualResetEventSlim();
        var sideEffectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task sideEffect = Task.Run(() =>
        {
            IDisposable synchronousLease = PitFireTeamOperationGate.EnterOperation();
            IDisposable logicalLease = PitFireTeamAsyncSideEffectGate.Enter();
            sideEffectEntered.TrySetResult();
            releaseSynchronous.Wait();
            synchronousLease.Dispose();
            synchronousReleased.Set();
            releaseLogical.Wait();
            logicalLease.Dispose();
        });
        await sideEffectEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<bool> preparation = Task.Run(() => sessions.BeginTeamArmPreparation(
            sessionId,
            "factory4_day",
            "tactical",
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"));
        await Task.Delay(100);
        Assert.False(preparation.IsCompleted);

        releaseSynchronous.Set();
        Assert.True(synchronousReleased.Wait(TimeSpan.FromSeconds(2)));
        await Task.Delay(100);
        Assert.False(preparation.IsCompleted);

        releaseLogical.Set();
        await sideEffect.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await preparation.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(sessions.CompleteTeamArmPreparation(sessionId));
        sessions.MarkIdle(sessionId);
    }

    [Fact]
    public void ManagementFinalizationScopeIsCompleteExactSessionAndClosedByFinalizer()
    {
        ArenaSessionService? previous = GetSessions();
        var sessions = new ArenaSessionService(NoOpLogger.Create<ArenaSessionService>());
        SetSessions(sessions);

        object? managementState = null;
        try
        {
            var sessionId = new MongoId(Session);
            var otherSessionId = new MongoId(OtherSession);
            var fileUtil = new FileUtil();
            string modRoot = fileUtil.GetModPath("pitFireTeam-ServerMod");
            string profile = Path.Combine(
                modRoot,
                "Resources",
                "teammates",
                Session,
                "1234.json");
            string otherProfile = Path.Combine(
                modRoot,
                "Resources",
                "teammates",
                OtherSession,
                "5678.json");
            MethodInfo managementMethod = typeof(PitFireTeamPersistenceGuardTests)
                .GetMethod(
                    nameof(ManagementBoundary),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo recoveryMethod = typeof(PitFireTeamPersistenceGuardTests)
                .GetMethod(
                    nameof(PrivateRecoveryBoundary),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo globalSettingsMethod = typeof(PitFireTeamPersistenceGuardTests)
                .GetMethod(
                    nameof(GlobalSettingsBoundary),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo globalSettingsTransaction = typeof(PitFireTeamPersistenceGuardTests)
                .GetMethod(
                    nameof(SetServerSettings),
                    BindingFlags.Static | BindingFlags.NonPublic)!;

            sessions.Arm(
                sessionId,
                "factory4_day",
                "tactical",
                protectPitFireTeam: true);
            sessions.Arm(
                otherSessionId,
                "factory4_day",
                "tactical",
                protectPitFireTeam: true);

            // Armed/InRaid work is suppressed at the complete management entry
            // point; no scope is created that could authorize inner writes.
            Assert.False(PitFireTeamDynamicPersistencePatches.ManagementServicePrefix(
                [sessionId],
                managementMethod,
                out managementState));
            Assert.Null(managementState);
            Assert.False(PitFireTeamManagementScope.HasActiveScope());
            Assert.False(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
            Assert.False(PitFireTeamDynamicPersistencePatches.ShouldRunRecovery(
                [sessionId],
                recoveryMethod));
            Assert.False(PitFireTeamDynamicPersistencePatches.GlobalSettingsPrefix(
                globalSettingsMethod));

            // Put both protected sessions in the finalization state so the
            // following assertions prove session binding, not a state mismatch.
            Assert.True(sessions.BeginPostRaidFinalization(sessionId));
            Assert.True(sessions.BeginPostRaidFinalization(otherSessionId));
            Assert.True(PitFireTeamDynamicPersistencePatches.ManagementServicePrefix(
                [sessionId],
                managementMethod,
                out managementState));
            Assert.NotNull(managementState);

            Assert.True(PitFireTeamManagementScope.Authorizes(Session));
            Assert.False(PitFireTeamManagementScope.Authorizes(OtherSession));
            Assert.True(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
            Assert.False(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, otherProfile));
            Assert.True(PitFireTeamDynamicPersistencePatches.ShouldRunRecovery(
                [sessionId],
                recoveryMethod));
            Assert.False(PitFireTeamDynamicPersistencePatches.ShouldRunRecovery(
                [otherSessionId],
                recoveryMethod));
            Assert.False(PitFireTeamDynamicPersistencePatches.GlobalSettingsPrefix(
                globalSettingsMethod));

            object? otherSettingsState = null;
            Assert.False(PitFireTeamDynamicPersistencePatches.ManagementServicePrefix(
                [otherSessionId],
                globalSettingsTransaction,
                out otherSettingsState));
            Assert.Null(otherSettingsState);

            var originalException = new InvalidOperationException("management failed");
            Exception? returnedException =
                PitFireTeamDynamicPersistencePatches.ManagementServiceFinalizer(
                    originalException,
                    managementState);
            managementState = null;
            Assert.Same(originalException, returnedException);

            Assert.False(PitFireTeamManagementScope.HasActiveScope());
            Assert.False(PitFireTeamManagementScope.Authorizes(Session));
            Assert.False(ArenaPitFireTeamTextWritePatch.Prefix(fileUtil, profile));
            Assert.False(PitFireTeamDynamicPersistencePatches.ShouldRunRecovery(
                [sessionId],
                recoveryMethod));
            Assert.False(PitFireTeamDynamicPersistencePatches.GlobalSettingsPrefix(
                globalSettingsMethod));

            sessions.MarkIdle(otherSessionId);
            Assert.True(PitFireTeamDynamicPersistencePatches.ManagementServicePrefix(
                [sessionId],
                globalSettingsTransaction,
                out managementState));
            Assert.NotNull(managementState);
            Assert.True(PitFireTeamDynamicPersistencePatches.GlobalSettingsPrefix(
                globalSettingsMethod));
            PitFireTeamDynamicPersistencePatches.ManagementServiceFinalizer(null, managementState);
            managementState = null;
        }
        finally
        {
            // Keep the AsyncLocal clean even when an assertion inside the scope
            // fails before the explicit finalizer checks can run.
            PitFireTeamDynamicPersistencePatches.ManagementServiceFinalizer(
                null,
                managementState);
            SetSessions(previous);
        }
    }

    [SkippableFact]
    public void InstalledPitFireTeamContractAcceptsEveryTransactionTargetOfItsBuild()
    {
        const string harmonyId = "com.maschine.ArenaMode.server.pitfireteam-persistence";
        string runtimeRoot = Environment.GetEnvironmentVariable("SPT_RUNTIME")
            ?? Path.GetFullPath(Path.Combine(RepoFiles.Root, "..", "..", "SPT_Runtime"));
        string assemblyPath = Path.Combine(
            runtimeRoot,
            "user",
            "mods",
            "pitFireTeam-ServerMod",
            "pitFireTeam.Server.dll");
        Skip.IfNot(File.Exists(assemblyPath), "PitFireTeam is optional and is not installed on this test host.");

        Assembly pitFireTeam = Assembly.LoadFrom(assemblyPath);
        Assert.Contains(pitFireTeam.GetName().Version, ArenaMode.Shared.PitFireTeamVersions.SupportedAssemblyVersions);

        InstalledMethodContract[] raidContracts =
        [
            new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleReturnItems", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleTeamEscaped", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "RecordKillMessage", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "RegisterProtectedRaidItems", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "RegisterProtectedRaidItemIds", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "RemoveProtectedTeammateItemsFromExtractedProfile", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleEndLocalRaidKillMessages", "System.Void"),
            new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleDeathEscapeSummary", "System.Void"),
            new("pitTeam.Server.Services.FriendlyRecruitService", "QueueRecruitPickups", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "PersistFollowerProgress", "System.Void"),
            new(
                "pitTeam.Server.Services.FriendlyTeammateService",
                "PersistDeathEscapeOutcomes",
                "pitTeam.Server.Models.FriendlyTeammateDeathEscapeSummary"),
        ];
        InstalledMethodContract[] managementContracts =
        [
            new("pitTeam.Server.Services.FriendlyTeammateService", "CreateTeammate", "SPTarkov.Server.Core.Models.Eft.Profile.SearchFriendResponse"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "CreateTeammateFromRecruitCandidate", "SPTarkov.Server.Core.Models.Eft.Profile.SearchFriendResponse"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SelectDefaultLoadoutForAllTeammates", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateSuit", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "RenameTeammate", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateLoadout", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SaveTeammateDefaultEquipment", "pitTeam.Server.Models.FriendlyTeammateDefaultEquipmentResponse"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "BuyTeammateKit", "pitTeam.Server.Models.FriendlyTeammateBuyKitResponse"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "RepairTeammateDefaultEquipment", "pitTeam.Server.Models.FriendlyTeammateRepairEquipmentResponse"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateAggression", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateTactic", "System.Void"),
            // Mirrors production's version-narrowed contract, so this test
            // resolves against whichever accepted build is actually installed.
            .. pitFireTeam.GetName().Version == new Version(0, 10, 3, 0)
                ? new InstalledMethodContract[]
                {
                    new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateProficiency", "System.Void"),
                }
                : [],
            new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateAutoJoin", "System.Void"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "DeleteTeammate", "System.Boolean"),
            new("pitTeam.Server.Services.FriendlyTeammateService", "DeleteTeammateByProfileId", "System.Boolean"),
            new("pitTeam.Server.Services.FriendlyRecruitService", "AcceptRecruitRequest", "System.Boolean"),
            new("pitTeam.Server.Services.FriendlyRecruitService", "AcceptAllRecruitRequests", "System.Boolean"),
            new("pitTeam.Server.Services.FriendlyRecruitService", "DeclineRecruitRequest", "System.Boolean"),
            new(
                "pitTeam.Server.Callbacks.FriendlyTeammateCallbacks",
                "SetServerSettings",
                "System.Threading.Tasks.ValueTask<System.String>",
                SessionParameterIndex: 2),
            new(
                "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks",
                "SendGroupInvite",
                "System.Threading.Tasks.ValueTask<System.String>",
                SessionParameterIndex: 2),
        ];
        InstalledMethodContract[] recoveryContracts =
        [
            new(
                "pitTeam.Server.Services.FriendlyTeammateService",
                "RecoverTeammateProfileIfNeeded",
                "System.Void",
                IsPublic: false),
            new(
                "pitTeam.Server.Services.FriendlyTeammateService",
                "RecoverDuplicateTeammateItemsForSession",
                "System.Void",
                IsPublic: false),
            new(
                "pitTeam.Server.Services.FriendlyRecruitService",
                "EnsureRecruitRequestAccountIds",
                "System.Boolean",
                IsPublic: false),
        ];
        InstalledMethodContract[] operationBoundaryContracts =
        [
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "ReturnItems", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "TeamEscaped", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RecruitPickup", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RecordKillMessage", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RegisterProtectedItems", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "EndLocalRaid", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RaidOutcomes", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "DeathEscape", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Create", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetServerSettings", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "AcknowledgeStartupRecoveryNotice", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetSuit", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Rename", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetLoadout", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SaveDefaultEquipment", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "BuyKit", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "RepairDefaultEquipment", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetAggression", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetTactic", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetAutoJoin", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Delete", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "DeleteFriend", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "AcceptFriendRequest", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "AcceptAllFriendRequests", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "DeclineFriendRequest", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "MergeFriendRequestInbox", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "MergeProfileView", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "SendGroupInvite", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "GenerateFollowerProfile", "System.Threading.Tasks.ValueTask<System.String>", 2),
            new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "PersistFollowerProgress", "System.Threading.Tasks.ValueTask<System.String>", 2),
        ];

        MethodInfo[] raidTargets = raidContracts
            .Select(contract => ResolveInstalledContract(pitFireTeam, contract))
            .ToArray();
        MethodInfo[] managementTargets = managementContracts
            .Select(contract => ResolveInstalledContract(pitFireTeam, contract))
            .ToArray();
        MethodInfo[] recoveryTargets = recoveryContracts
            .Select(contract => ResolveInstalledContract(pitFireTeam, contract))
            .ToArray();
        MethodInfo[] operationBoundaryTargets = operationBoundaryContracts
            .Select(contract => ResolveInstalledContract(pitFireTeam, contract))
            .ToArray();

        MethodInfo recoveryBackup = pitFireTeam
            .GetType("pitTeam.Server.Services.FriendlyTeammateService", throwOnError: true)!
            .GetMethod(
                "BackupFileBeforeRecovery",
                BindingFlags.Instance | BindingFlags.NonPublic,
                Type.DefaultBinder,
                [typeof(string)],
                null)!;
        Assert.NotNull(recoveryBackup);
        Assert.Equal(typeof(void), recoveryBackup.ReturnType);
        Type settingsRequestType = pitFireTeam.GetType(
            "pitTeam.Server.Models.FriendlyServerSettingsRequest",
            throwOnError: true)!;
        MethodInfo saveAndApply = pitFireTeam
            .GetType(
                "pitTeam.Server.Services.FriendlyServerSettingsService",
                throwOnError: true)!
            .GetMethod(
                "SaveAndApply",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                Type.DefaultBinder,
                [settingsRequestType],
                null)!;
        Assert.NotNull(saveAndApply);
        Assert.Equal(typeof(void), saveAndApply.ReturnType);

        MethodInfo[] allTargets =
        [
            .. raidTargets,
            .. managementTargets,
            .. recoveryTargets,
            .. operationBoundaryTargets,
            recoveryBackup,
            saveAndApply,
        ];
        allTargets = allTargets.Distinct().ToArray();
        // 0.10.3 adds exactly one management transaction (teammate proficiency).
        int expectedManagement = pitFireTeam.GetName().Version == new Version(0, 10, 3, 0) ? 20 : 19;
        Assert.Equal(11, raidTargets.Length);
        Assert.Equal(expectedManagement, managementTargets.Length);
        Assert.Equal(3, recoveryTargets.Length);
        Assert.Equal(30, operationBoundaryTargets.Length);
        // Two targets are shared between groups, so the distinct total is the
        // other four groups plus the management count for the installed build.
        Assert.Equal(44 + expectedManagement, allTargets.Length);

        PitFireTeamDynamicPersistencePatches.TryEnable();

        Assert.True(PitFireTeamDynamicPersistencePatches.IsInstalled);
        Assert.True(PitFireTeamDynamicPersistencePatches.IsCompatible);
        Assert.True(PitFireTeamDynamicPersistencePatches.FileMutationPatchesReady);
        Assert.True(PitFireTeamDynamicPersistencePatches.ProtectionReady);
        Assert.Equal(string.Empty, PitFireTeamDynamicPersistencePatches.IncompatibilityReason);

        foreach (MethodInfo target in allTargets)
        {
            Patches patchInfo = AssertHarmonyPrefix(target, harmonyId);
            Assert.Contains(
                patchInfo.Prefixes,
                patch => patch.owner == harmonyId
                    && patch.PatchMethod.Name == nameof(PitFireTeamOperationBoundaryPatch.Prefix));
            Assert.Contains(
                patchInfo.Finalizers,
                patch => patch.owner == harmonyId
                    && patch.PatchMethod.Name == nameof(PitFireTeamOperationBoundaryPatch.Finalizer));
        }

        foreach (MethodInfo target in raidTargets)
        {
            AssertHarmonyPrefix(target, harmonyId);
        }

        foreach (MethodInfo target in managementTargets)
        {
            Patches patchInfo = AssertHarmonyPrefix(target, harmonyId);
            Assert.Contains(
                patchInfo.Finalizers,
                patch => patch.owner == harmonyId
                    && patch.PatchMethod.Name == nameof(
                        PitFireTeamDynamicPersistencePatches.ManagementServiceFinalizer));
        }

        foreach (MethodInfo target in recoveryTargets)
        {
            AssertHarmonyPrefix(target, harmonyId);
        }

        AssertHarmonyPrefix(recoveryBackup, harmonyId);
        Patches settingsPatchInfo = AssertHarmonyPrefix(saveAndApply, harmonyId);
        Assert.Contains(
            settingsPatchInfo.Prefixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(
                    PitFireTeamDynamicPersistencePatches.GlobalSettingsPrefix));

        MethodInfo sendGroupInvite = operationBoundaryTargets.Single(target =>
            target.DeclaringType?.FullName ==
                "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks"
            && target.Name == "SendGroupInvite");
        Patches invitePatches = Harmony.GetPatchInfo(sendGroupInvite)!;
        Assert.Contains(
            invitePatches.Prefixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamInviteOriginPatch.Prefix));
        Assert.Contains(
            invitePatches.Finalizers,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamInviteOriginPatch.Finalizer));

        MethodInfo inviteTaskRun = typeof(Task)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Single(method =>
                method.Name == nameof(Task.Run)
                && method.ReturnType == typeof(Task)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(Func<Task>));
        Patches inviteTaskRunPatches = Harmony.GetPatchInfo(inviteTaskRun)!;
        Assert.Contains(
            inviteTaskRunPatches.Prefixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamInviteTaskRunPatch.Prefix));
        Assert.Contains(
            inviteTaskRunPatches.Finalizers,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamInviteTaskRunPatch.Finalizer));

        MethodInfo notificationSend = typeof(NotificationSendHelper).GetMethod(
            "SendMessageAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        Patches notificationPatches = Harmony.GetPatchInfo(notificationSend)!;
        Assert.Contains(
            notificationPatches.Prefixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationPrefix));
        Assert.Contains(
            notificationPatches.Postfixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationPostfix));
        Assert.Contains(
            notificationPatches.Finalizers,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationFinalizer));

        MethodInfo mailSend = typeof(MailSendService).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == "SendSystemMessageToPlayer");
        Patches mailPatches = Harmony.GetPatchInfo(mailSend)!;
        Assert.Contains(
            mailPatches.Prefixes,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamDelayedInviteSideEffectPatch.MailPrefix));
        Assert.Contains(
            mailPatches.Finalizers,
            patch => patch.owner == harmonyId
                && patch.PatchMethod.Name == nameof(PitFireTeamDelayedInviteSideEffectPatch.MailFinalizer));
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("zombie")]
    public void RaidStartTeamFlagIsOptionalAndFailClosedWithoutChangingSolo(string mode)
    {
        ArenaRaidStartRequest omitted = JsonSerializer.Deserialize<ArenaRaidStartRequest>(
            $$"""{"map":"factory4_day","mode":"{{mode}}"}""")!;
        Assert.False(omitted.PitFireTeamTeam);
        Assert.Null(PitFireTeamRaidStartPolicy.GetRejectionReason(
            omitted.PitFireTeamTeam,
            omitted.Mode,
            persistenceProtectionReady: false,
            incompatibilityReason: "not ready"));

        ArenaRaidStartRequest team = JsonSerializer.Deserialize<ArenaRaidStartRequest>(
            $$"""{"map":"factory4_day","mode":"{{mode}}","pitFireTeamTeam":true}""")!;
        Assert.True(team.PitFireTeamTeam);
        Assert.Equal(
            "contract mismatch",
            PitFireTeamRaidStartPolicy.GetRejectionReason(
                team.PitFireTeamTeam,
                team.Mode,
                persistenceProtectionReady: false,
                incompatibilityReason: "contract mismatch"));
        Assert.Null(PitFireTeamRaidStartPolicy.GetRejectionReason(
            team.PitFireTeamTeam,
            team.Mode,
            persistenceProtectionReady: true,
            incompatibilityReason: "ignored"));
        Assert.Contains(
            "Tactical or Zombies",
            PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: "rounds",
                persistenceProtectionReady: true,
                incompatibilityReason: null));
    }

    [Theory]
    [InlineData("deathmatch")]
    [InlineData("teamdeathmatch")]
    [InlineData("gungame")]
    [InlineData("endless")]
    [InlineData("rounds")]
    [InlineData("zombies")]
    [InlineData("")]
    [InlineData(null)]
    public void ReadyPersistenceCannotEnableUnsupportedSquadModes(string? mode)
    {
        Assert.NotNull(PitFireTeamRaidStartPolicy.GetRejectionReason(
            true, mode, persistenceProtectionReady: true, incompatibilityReason: null));
        Assert.Null(PitFireTeamRaidStartPolicy.GetRejectionReason(
            false, mode, persistenceProtectionReady: false, incompatibilityReason: "not installed"));
    }

    private sealed record InstalledMethodContract(
        string TypeName,
        string MethodName,
        string ReturnTypeName,
        int SessionParameterIndex = 0,
        bool IsPublic = true);

    private static MethodInfo ResolveInstalledContract(
        Assembly assembly,
        InstalledMethodContract contract)
    {
        Type type = assembly.GetType(
            contract.TypeName,
            throwOnError: true,
            ignoreCase: false)!;
        BindingFlags visibility = contract.IsPublic
            ? BindingFlags.Public
            : BindingFlags.NonPublic;
        MethodInfo[] candidates = type
            .GetMethods(BindingFlags.Instance | visibility | BindingFlags.DeclaredOnly)
            .Where(method =>
                string.Equals(method.Name, contract.MethodName, StringComparison.Ordinal)
                && InstalledReturnTypeMatches(method.ReturnType, contract.ReturnTypeName)
                && method.GetParameters().Length > contract.SessionParameterIndex
                && method.GetParameters()[contract.SessionParameterIndex].ParameterType
                    == typeof(MongoId))
            .ToArray();

        Assert.True(
            candidates.Length == 1,
            $"Expected one installed target for {contract.TypeName}.{contract.MethodName}, " +
            $"found {candidates.Length}.");
        return candidates[0];
    }

    private static bool InstalledReturnTypeMatches(Type actual, string expected)
        => expected == "System.Threading.Tasks.ValueTask<System.String>"
            ? actual == typeof(ValueTask<string>)
            : string.Equals(actual.FullName, expected, StringComparison.Ordinal);

    private static Patches AssertHarmonyPrefix(MethodBase target, string harmonyId)
    {
        Patches? patchInfo = Harmony.GetPatchInfo(target);
        Assert.NotNull(patchInfo);
        Assert.Contains(
            patchInfo!.Prefixes,
            patch => patch.owner == harmonyId);
        return patchInfo;
    }

    private static void ManagementBoundary(MongoId sessionId)
        => _ = sessionId;

    private static void PrivateRecoveryBoundary(MongoId sessionId)
        => _ = sessionId;

    private static void GlobalSettingsBoundary()
    {
    }

    private static void SetServerSettings(MongoId sessionId)
        => _ = sessionId;

    private static void AssertBlocked(
        PitFireTeamPersistenceGuard guard,
        string modRoot,
        string path)
    {
        Assert.True(guard.ShouldBlockFileMutation(path, modRoot, out string? session));
        Assert.Equal(Session, session, ignoreCase: true);
    }

    private static void AssertAllowed(
        PitFireTeamPersistenceGuard guard,
        string modRoot,
        string path)
        => Assert.False(guard.ShouldBlockFileMutation(path, modRoot, out _));

    private static string Full(params string[] parts)
        => Path.GetFullPath(Path.Combine([Path.GetTempPath(), .. parts]));

    private static string Signature(string name, params Type[] parameters)
        => $"{name}({string.Join(",", parameters.Select(type => type.FullName))})";

    private static bool ConsumeArm(
        ArenaSessionService sessions,
        MongoId sessionId,
        string location)
        => ProcessStart(sessions, sessionId, location)
            == ArenaLocalRaidStartDisposition.ArenaStarted;

    private static ArenaLocalRaidStartDisposition ProcessStart(
        ArenaSessionService sessions,
        MongoId sessionId,
        string location)
    {
        string token = sessions.GetArmToken(sessionId)!;
        string nativeStart = Protocol.BuildNativeStartIdentity(token);
        ArenaLocalRaidStartDisposition disposition = sessions.ProcessLocalRaidStart(
            sessionId,
            location,
            nativeStart);
        if (disposition == ArenaLocalRaidStartDisposition.ArenaStarted)
        {
            if (!sessions.BindNativeEndIdentity(
                    sessionId,
                    nativeStart,
                    $"{location}.Pmc unit-test",
                    out _))
            {
                throw new InvalidOperationException(
                    "Unit test could not complete the simulated native start");
            }
        }

        return disposition;
    }

    private static ArenaSessionService? GetSessions()
        => ArenaContext.Sessions;

    private static void SetSessions(ArenaSessionService? sessions)
        => ArenaContext.Sessions = sessions;

    public class NoOpLogger : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;

        public static ISptLogger<T> Create<T>()
            => DispatchProxy.Create<ISptLogger<T>, NoOpLogger>();
    }
}
