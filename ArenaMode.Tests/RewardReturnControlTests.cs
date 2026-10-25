using System.Security.Cryptography;
using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using ArenaMode.Shared;
using Microsoft.Extensions.Hosting;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Services.Hosted;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardReturnControlTests
{
    [Fact]
    public void ReturnCapabilityHasIndependentSecretAndExactProcessIdentity()
    {
        using var f = new Fixture();
        var grant = f.Issue();
        Assert.NotEqual(f.Consent.Token, grant.Token);
        Assert.Equal(f.Process.Pid, grant.ServerPid);
        Assert.Equal(f.Process.StartedUtc, grant.ServerStartedUtc);
        Assert.Equal(f.Process.Arguments, grant.ServerArguments);
        Assert.False(f.Control.Authorize(Fixture.Id, f.Request(grant) with { Token = f.Consent.Token }));
        Assert.True(f.Control.Authorize(Fixture.Id, f.Request(grant)));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("control")]
    [InlineData("token")]
    [InlineData("token-format")]
    [InlineData("operation")]
    [InlineData("helper-pid")]
    [InlineData("helper-start")]
    [InlineData("profile")]
    public void InvalidRequestCannotReserveOrReplaceCurrentOperation(string mutation)
    {
        using var f = new Fixture();
        var grant = f.Issue();
        var valid = f.Request(grant);
        var bad = mutation switch
        {
            "schema" => valid with { Schema = 2 },
            "control" => valid with { ControlId = Guid.NewGuid().ToString("N") },
            "token" => valid with { Token = Convert.ToBase64String(new byte[32]) },
            "token-format" => valid with { Token = "invalid" },
            "operation" => valid with { OperationId = "../operation" },
            "helper-pid" => valid with { HelperPid = 999 },
            "helper-start" => valid with { HelperStartedUtc = valid.HelperStartedUtc.AddSeconds(1) },
            _ => valid
        };
        Assert.False(f.Control.Authorize(mutation == "profile" ? new string('b', 24) : Fixture.Id, bad));
        Assert.True(f.Control.Authorize(Fixture.Id, valid));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("args")]
    [InlineData("missing-process")]
    [InlineData("private")]
    public void ChangedAuthorityRefusesShutdown(string mutation)
    {
        using var f = new Fixture();
        var grant = f.Issue();
        switch (mutation)
        {
            case "expired": f.Now = f.Now.AddHours(12); break;
            case "clock-back": f.Now = f.Now.AddMinutes(-2); break;
            case "pid": f.Process = f.Process with { Pid = f.Process.Pid + 1 }; break;
            case "start": f.Process = f.Process with { StartedUtc = f.Process.StartedUtc.AddSeconds(1) }; break;
            case "args": f.Process = f.Process with { Arguments = ["changed"] }; break;
            case "missing-process": f.ProcessAvailable = false; break;
            case "private": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}"); break;
        }
        Assert.False(f.Control.Authorize(Fixture.Id, f.Request(grant)));
    }

    [Fact]
    public void RepeatedIssueKeepsTokenButNewEntryRevokesOldReturnCapability()
    {
        using var f = new Fixture();
        var old = f.Issue();
        Assert.True(f.Control.Issue(f.Consent));
        Assert.Equal(RewardRuntimeFiles.Json(old), RewardRuntimeFiles.Json(f.ReadGrant()));
        f.Consent = f.Consent with { ControlId = Guid.NewGuid().ToString("N") };
        var current = f.Issue();
        Assert.False(f.Control.Authorize(Fixture.Id, f.Request(old)));
        Assert.True(f.Control.Authorize(Fixture.Id, f.Request(current)));
    }

    [Fact]
    public void RestartDoesNotReloadCapabilitiesFromDisk()
    {
        using var f = new Fixture();
        var grant = f.Issue();
        var restarted = f.NewControl();
        Assert.False(restarted.Authorize(Fixture.Id, f.Request(grant)));
    }

    [Fact]
    public void AcceptedOperationMayFinishAcrossExpiryButCannotChangeIdentity()
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        Assert.True(f.Control.Authorize(Fixture.Id, request));
        f.Now = f.Now.AddHours(12);
        Assert.True(f.Control.Authorize(Fixture.Id, request));
        Assert.False(f.Control.Authorize(Fixture.Id, request with { OperationId = Guid.NewGuid().ToString("N") }));
        Assert.False(f.Control.Authorize(Fixture.Id, request with { HelperPid = 1 }));
        f.Control.ReleaseBeforeQuiescence(Fixture.Id, request);
        Assert.False(f.Control.Authorize(Fixture.Id, request));
    }

    [Fact]
    public void CapturedServerArgumentsCannotBeChangedThroughTheProbeArray()
    {
        using var f = new Fixture();
        var grant = f.Issue();
        f.Process.Arguments[0] = "changed";
        Assert.False(f.Control.Authorize(Fixture.Id, f.Request(grant)));
        Assert.Equal("--test", f.ReadGrant().ServerArguments[0]);
    }

    [Fact]
    public void SameRequestCanRetryButAnotherOperationCannotTakeOver()
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        Assert.True(f.Control.Authorize(Fixture.Id, request));
        Assert.True(f.Control.Authorize(Fixture.Id, request));
        Assert.False(f.Control.Authorize(Fixture.Id, request with { OperationId = Guid.NewGuid().ToString("N") }));
        Assert.False(f.Control.Issue(f.Consent with { ControlId = Guid.NewGuid().ToString("N") }));
        f.Control.ReleaseBeforeQuiescence(Fixture.Id, request);
        Assert.True(f.Control.Authorize(Fixture.Id, request with { OperationId = Guid.NewGuid().ToString("N") }));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("192.168.1.1")]
    public void SharedOrRemoteListenerDoesNotIssueReturnAuthority(string host)
    {
        using var f = new Fixture();
        File.WriteAllText(f.Config, JsonSerializer.Serialize(new { ip = host, port = 6969 }));
        Assert.False(f.Control.Issue(f.Consent));
        Assert.False(File.Exists(f.Export));
    }

    [Fact]
    public void UnknownNativeProcessDoesNotIssueReturnAuthority()
    {
        using var f = new Fixture();
        f.ProcessAvailable = false;
        Assert.False(f.Control.Issue(f.Consent));
        Assert.False(File.Exists(f.Export));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("length")]
    [InlineData("nul")]
    [InlineData("null")]
    public void ReturnArgumentsMustFitTheHelperContract(string invalid)
    {
        using var f = new Fixture();
        f.Process = f.Process with { Arguments = invalid switch
        {
            "count" => Enumerable.Repeat("", 33).ToArray(),
            "length" => [new string('a', 2048), new string('b', 2049)],
            "nul" => ["bad\0argument"],
            _ => [null!]
        } };
        Assert.False(f.Control.Issue(f.Consent));
        Assert.False(File.Exists(f.Export));
    }

    [Fact]
    public void AutomaticReturnIndicatorRequiresCurrentNormalProcessGrant()
    {
        using var f = new Fixture();
        Assert.False(f.Control.HasGrant(Fixture.Id));
        f.Issue();
        Assert.True(f.Control.HasGrant(Fixture.Id));
        Assert.False(f.Control.HasGrant(new string('b', 24)));
        f.ProcessAvailable = false;
        Assert.False(f.Control.HasGrant(Fixture.Id));
        f.ProcessAvailable = true;
        f.Now = f.Now.AddHours(12);
        Assert.False(f.Control.HasGrant(Fixture.Id));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("expired", false)]
    [InlineData("control", false)]
    [InlineData("profile", false)]
    [InlineData("source", false)]
    [InlineData("schema", false)]
    [InlineData("server", false)]
    [InlineData("port", false)]
    public void PrivateAutomaticReturnIndicatorNeedsMatchingCopiedGrant(string mutation, bool expected)
    {
        using var f = new Fixture();
        var grant = f.Issue();
        string privateRoot = Path.Combine(f.Root, "private");
        Directory.CreateDirectory(privateRoot);
        string grantPath = Path.Combine(privateRoot, "arena-rewards-return.json");
        File.WriteAllText(grantPath, RewardRuntimeFiles.Json(mutation == "expired" ? grant with { ExpiresUtc = f.Now } : grant));
        File.WriteAllText(Path.Combine(privateRoot, "arena-session.json"), JsonSerializer.Serialize(new
        {
            schema = mutation == "schema" ? 2 : 1,
            profileId = mutation == "profile" ? new string('b', 24) : Fixture.Id,
            rewardControlId = mutation == "control" ? Guid.NewGuid().ToString("N") : grant.ControlId,
            sourceRuntime = mutation == "source" ? Path.Combine(f.Root, "other") : f.Root,
            serverExe = Path.Combine(mutation == "server" ? f.Root : privateRoot, "SPT.Server.exe"),
            port = mutation == "port" ? 6969 : 6970
        }));
        if (mutation == "missing") File.Delete(grantPath);
        Assert.Equal(expected, RewardReturnControl.PrivateGrantAvailable(privateRoot, Fixture.Id, f.Now));
    }

    [Fact]
    public async Task GateDrainsWholeAsyncLifetimeAndRejectsNewWorkWhilePaused()
    {
        var gate = new RewardReturnRequestGate();
        var lease = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());
        var native = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task tracked = RewardReturnQuiescence.Track(native.Task, lease);
        Task draining = gate.PauseAndDrainAsync(CancellationToken.None);
        Assert.False(draining.IsCompleted);
        Assert.Null(gate.TryEnter());
        native.SetResult();
        await tracked;
        await draining;
        Assert.Equal(0, gate.Active);
        lease.Dispose();
        Assert.Equal(0, gate.Active);
        gate.Resume();
        using var resumed = gate.TryEnter();
        Assert.NotNull(resumed);
    }

    [Fact]
    public async Task FaultedRequestStillReleasesItsDrainLease()
    {
        var gate = new RewardReturnRequestGate();
        var lease = gate.TryEnter()!;
        await Assert.ThrowsAsync<IOException>(() => RewardReturnQuiescence.Track(Task.FromException(new IOException()), lease));
        await gate.PauseAndDrainAsync(CancellationToken.None);
        Assert.Equal(0, gate.Active);
    }

    [Fact]
    public async Task OrderIsDrainIdlePreflightStopIdleFinalSaveAndThenReady()
    {
        var gate = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(gate);
        var events = new List<string>();
        var active = gate.TryEnter()!;
        var task = sequence.PrepareAsync(() => { events.Add("idle"); return true; },
            _ => { events.Add("save"); return Task.CompletedTask; },
            _ => { events.Add("stop"); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Empty(events);
        active.Dispose();
        var result = await task;
        Assert.True(result.Accepted);
        Assert.True(sequence.ReadyToStop);
        Assert.True(gate.Held);
        Assert.Equal(new[] { "idle", "save", "stop", "idle", "save" }, events);
        var retry = await sequence.PrepareAsync(() => throw new Exception("no repeated work"),
            _ => throw new Exception("no save"), _ => throw new Exception("no stop"), CancellationToken.None);
        Assert.True(retry.Accepted);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("preflight-save")]
    public async Task FailureBeforeStoppingUpdatesReopensNormalRequests(string failure)
    {
        var gate = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(gate);
        int stopped = 0;
        var result = await sequence.PrepareAsync(() => failure != "idle",
            _ => Task.FromException(new IOException()), _ => { stopped++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.False(result.Held);
        Assert.False(gate.Held);
        Assert.False(sequence.UpdatesStopped);
        Assert.Equal(0, stopped);
        using var resumed = gate.TryEnter();
        Assert.NotNull(resumed);
    }

    [Fact]
    public async Task TimedOutDrainNeverStopsUpdatesAndReopensRequests()
    {
        var gate = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(gate);
        using var active = gate.TryEnter();
        using var cancel = new CancellationTokenSource();
        int stopped = 0;
        var task = sequence.PrepareAsync(() => true, _ => Task.CompletedTask,
            _ => { stopped++; return Task.CompletedTask; }, cancel.Token);
        cancel.Cancel();
        var result = await task;
        Assert.False(result.Accepted);
        Assert.False(result.Held);
        Assert.False(gate.Held);
        Assert.Equal(0, stopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutCannotAbortNativeSaveOrReleaseAStillWritingReturn(bool duringFinalSave)
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, updates);
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource();
        int saves = 0, stopped = 0;
        var operation = sequence.PrepareAsync(() => true,
            ct => ArenaRewardReturnListener.SaveWithoutAborting(nativeToken =>
            {
                Assert.False(nativeToken.CanBeCanceled);
                saves++;
                return saves == (duringFinalSave ? 2 : 1) ? writing.Task : Task.CompletedTask;
            }, ct),
            _ => { stopped++; return Task.CompletedTask; }, deadline.Token);
        Assert.Equal(duringFinalSave ? 2 : 1, saves);
        deadline.Cancel();
        Assert.False(operation.IsCompleted);
        Assert.False(writing.Task.IsCompleted);
        Assert.True(requests.Held);
        Assert.True(updates.Held);

        // A helper retry can time out on the sequence mutex while the first native
        // write is pending. It must preserve the operation and never launch EFT.
        using var retryDeadline = new CancellationTokenSource();
        retryDeadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.PrepareAsync(() => true,
            _ => throw new Exception("retry must not save"),
            _ => throw new Exception("retry must not stop"), retryDeadline.Token));
        Assert.True(ArenaRewardReturnListener.ReturnHeld(sequence, requests));
        Assert.False(operation.IsCompleted);

        writing.SetResult();
        var result = await operation;
        Assert.False(result.Accepted);
        Assert.Equal(duringFinalSave, result.Held);
        Assert.Equal(duringFinalSave, requests.Held);
        Assert.Equal(duringFinalSave, updates.Held);
        Assert.Equal(duringFinalSave ? 1 : 0, stopped);
    }

    [Fact]
    public async Task ExpiredReturnDoesNotBeginAnotherNativeSave()
    {
        using var deadline = new CancellationTokenSource();
        deadline.Cancel();
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArenaRewardReturnListener.SaveWithoutAborting(
            _ => { calls++; return Task.CompletedTask; }, deadline.Token));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("final-save")]
    public async Task FailureAfterUpdateStopRetainsGateAndSupportsSafeRetry(string failure)
    {
        var gate = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(gate);
        int saved = 0, stopped = 0;
        var result = await sequence.PrepareAsync(() => true,
            _ => ++saved == 2 ? Task.FromException(new IOException()) : Task.CompletedTask,
            _ => { stopped++; return failure == "stop" ? Task.FromException(new TimeoutException()) : Task.CompletedTask; }, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.True(result.Held);
        Assert.True(gate.Held);
        Assert.True(sequence.UpdatesStopped);
        Assert.False(sequence.ReadyToStop);
        Assert.Null(gate.TryEnter());
        var retry = await sequence.PrepareAsync(() => true, _ => { saved++; return Task.CompletedTask; },
            _ => { stopped++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.True(retry.Accepted);
        Assert.Equal(2, stopped);
        Assert.Equal(failure == "stop" ? 2 : 3, saved);
    }

    [Fact]
    public async Task ActiveUpdateIsDrainedBeforeAnySaveOrTokenCancellation()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, updates);
        using var nativeStop = new CancellationTokenSource();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        long observedSeconds = -1;
        CancellationToken observedToken = default;
        var component = new UpdateCallback((seconds, token) =>
        {
            observedSeconds = seconds; observedToken = token; return completion.Task;
        });
        Task<bool> running = DispatchUpdate(updates, component, 123, nativeStop.Token);
        int saves = 0, stops = 0;
        var preparing = sequence.PrepareAsync(() => true, _ => { saves++; return Task.CompletedTask; },
            _ => { stops++; nativeStop.Cancel(); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(0, saves);
        Assert.Equal(0, stops);
        Assert.False(nativeStop.IsCancellationRequested);
        Assert.Equal(123, observedSeconds);
        Assert.Equal(nativeStop.Token, observedToken);
        Assert.False(await DispatchUpdate(updates,
            new UpdateCallback((_, _) => throw new Exception("must be skipped")), 1, CancellationToken.None));
        completion.SetResult(true);
        Assert.True(await running);
        Assert.True((await preparing).Accepted);
        Assert.Equal(2, saves);
        Assert.Equal(1, stops);
    }

    [Fact]
    public async Task FailedOrCancelledUpdateReleasesGateAndPreservesItsResultSemantics()
    {
        var gate = new RewardReturnRequestGate();
        var failed = new UpdateCallback((_, _) => Task.FromException<bool>(new IOException()));
        await Assert.ThrowsAsync<IOException>(() => DispatchUpdate(gate, failed, 1, CancellationToken.None));
        Assert.Equal(0, gate.Active);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var component = new UpdateCallback((_, ct) => Task.FromCanceled<bool>(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchUpdate(gate, component, 2, cancelled.Token));
        Assert.Equal(0, gate.Active);
        Assert.False(await DispatchUpdate(gate,
            new UpdateCallback((_, _) => Task.FromResult(false)), 3, CancellationToken.None));
        await gate.PauseAndDrainAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UpdateDrainTimeoutNeverCancelsNativeWorkAndReopensBothGates()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, updates);
        using var running = updates.TryEnter();
        using var timeout = new CancellationTokenSource();
        bool stopped = false;
        var preparing = sequence.PrepareAsync(() => true, _ => Task.CompletedTask,
            _ => { stopped = true; return Task.CompletedTask; }, timeout.Token);
        timeout.Cancel();
        var result = await preparing;
        Assert.False(result.Accepted);
        Assert.False(result.Held);
        Assert.False(stopped);
        Assert.False(requests.Held);
        Assert.False(updates.Held);
    }

    [Fact]
    public void UpdateTargetsPreserveActualTypesAndDeduplicateInheritedImplementations()
    {
        IOnUpdate[] components = [new BaseUpdate(), new DerivedUpdate(), new ExplicitUpdate(), new ExplicitUpdate()];
        var targets = RewardReturnQuiescence.ResolveUpdateTargets(components);
        Assert.Equal(2, targets.Length);
        Assert.Contains(targets, method => method.DeclaringType == typeof(BaseUpdate));
        Assert.Contains(targets, method => method.DeclaringType == typeof(ExplicitUpdate) && method.IsPrivate);
        Assert.IsType<DerivedUpdate>(components[1]);
        foreach (var method in targets)
        {
            Assert.False(method.IsStatic);
            Assert.Equal(typeof(Task<bool>), method.ReturnType);
            Assert.Equal(new[] { typeof(long), typeof(CancellationToken) }, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        }
        Assert.Throws<MissingMethodException>(() => RewardReturnQuiescence.ResolveUpdateTargets([]));
        Assert.Throws<MissingMethodException>(() => RewardReturnQuiescence.ResolveUpdateTargets([new GenericUpdate<int>()]));
    }

    [Fact]
    public void CallbackCaptureRequiresTheStableActualNativeArrayAndUniqueField()
    {
        IOnUpdate[] components = [new BaseUpdate()];
        var dispatcher = new CapturedUpdates(components);
        var field = RewardReturnQuiescence.ResolveComponentField(typeof(CapturedUpdates));
        Assert.Same(components, RewardReturnQuiescence.ReadComponents(field, dispatcher));
        Assert.Throws<NotSupportedException>(() => RewardReturnQuiescence.ReadComponents(field,
            new CapturedUpdates(components.Select(component => component))));
        Assert.Throws<NotSupportedException>(() => RewardReturnQuiescence.ReadComponents(field, new CapturedUpdates([])));
        Assert.Throws<MissingFieldException>(() => RewardReturnQuiescence.ResolveComponentField(typeof(string)));
        Assert.Throws<MissingFieldException>(() => RewardReturnQuiescence.ResolveComponentField(typeof(AmbiguousUpdates)));
    }

    [Fact]
    public void CallbackPatchFailureRollsBackTheEntirePartialSetAndPropagatesFailure()
    {
        var targets = RewardReturnQuiescence.ResolveUpdateTargets([new BaseUpdate(), new ExplicitUpdate()]);
        int attempts = 0, rollbacks = 0;
        Assert.Throws<InvalidOperationException>(() => RewardReturnQuiescence.PatchTargets(targets,
            _ => { if (++attempts == 2) throw new InvalidOperationException(); }, () => rollbacks++));
        Assert.Equal(2, attempts);
        Assert.Equal(1, rollbacks);
        attempts = 0;
        RewardReturnQuiescence.PatchTargets(targets, _ => attempts++, () => throw new Exception("must not roll back success"));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SynchronousCallbackFailureReleasesEntryLease()
    {
        var gate = new RewardReturnRequestGate();
        Assert.Throws<IOException>(() => { _ = DispatchUpdate(gate,
            new UpdateCallback((_, _) => throw new IOException()), 1, CancellationToken.None); });
        Assert.Equal(0, gate.Active);
        await gate.PauseAndDrainAsync(CancellationToken.None);
    }

    private static Task<bool> DispatchUpdate(RewardReturnRequestGate gate, IOnUpdate component, long seconds, CancellationToken token)
    {
        Task<bool> result = null!;
        if (!RewardReturnQuiescence.TryEnterUpdate(gate, ref result, out var lease)) return result;
        try { return RewardReturnQuiescence.TrackUpdate(component.OnUpdateAsync(seconds, token), lease!); }
        catch { lease!.Dispose(); throw; }
    }

    private class BaseUpdate : IOnUpdate
    {
        public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    private sealed class DerivedUpdate : BaseUpdate;
    private sealed class ExplicitUpdate : IOnUpdate
    {
        Task<bool> IOnUpdate.OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    private sealed class GenericUpdate<T> : IOnUpdate
    {
        public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    private sealed class CapturedUpdates(IEnumerable<IOnUpdate> components)
    {
        private readonly IEnumerable<IOnUpdate> _components = components;
    }
    private sealed class AmbiguousUpdates
    {
        private readonly IEnumerable<IOnUpdate> _first = [];
        private readonly IEnumerable<IOnUpdate> _second = [];
    }

    private sealed class UpdateCallback(Func<long, CancellationToken, Task<bool>> action) : IOnUpdate
    {
        public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken) => action(secondsSinceLastRun, cancellationToken);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"inraid\":{}}", true)]
    [InlineData("{\"inraid\":{\"location\":\"none\",\"character\":\"none\"}}", true)]
    [InlineData("{\"inraid\":{\"location\":\"factory4_day\"}}", false)]
    [InlineData("{\"inraid\":{\"character\":\"pmc\"}}", false)]
    [InlineData("{\"inraid\":null}", false)]
    public void NativeRaidStateMustBePositivelyIdle(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ArenaRewardReturnListener.IdleProfile(document.RootElement));
    }

    [Fact]
    public void SaveProofRejectsPoisonedNativeHashAndConcurrentRamChanges()
    {
        const string current = "{\"xp\":42,\"inventory\":[1,2]}";
        ArenaRewardReturnListener.RequireSavedProfile(current, "{ \"inventory\": [1, 2], \"xp\": 42 }", current);
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(current,
            "{\"xp\":40,\"inventory\":[1,2]}", current));
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(current,
            current, "{\"xp\":43,\"inventory\":[1,2]}"));
        Assert.Throws<RewardDataException>(() => ArenaRewardReturnListener.RequireSavedProfile(current,
            "{\"xp\":42,\"xp\":40,\"inventory\":[1,2]}", current));
    }

    [Theory]
    [InlineData(false, true, 1000, 100, false)]
    [InlineData(true, true, 0, 0, false)]
    [InlineData(true, false, 0, 0, true)]
    [InlineData(true, true, 1000, 0, true)]
    [InlineData(true, true, 0, 100, true)]
    public void OnlyPendingEnabledRewardsOrEnrollmentNeedAnAutomaticRestart(bool enabled, bool enrolled,
        long roubles, long xp, bool expected) => Assert.Equal(expected, ArenaRewardReturnListener.NeedsStartup(new RewardStatusResponse
        { Enabled = enabled, Enrolled = enrolled, PendingRoubles = roubles, PendingExperience = xp }));

    [Fact]
    public void UnknownHostedServiceRefusesAutomaticRestartBeforeAnyNativeStop()
    {
        Assert.True(ArenaRewardReturnListener.IsKnownHostedService(typeof(SPTStartupHostedService)));
        Type nativeWebHost = System.Reflection.Assembly.Load("Microsoft.AspNetCore.Hosting")
            .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService", throwOnError: true)!;
        Assert.True(ArenaRewardReturnListener.IsKnownHostedService(nativeWebHost));
        Type nativeDataProtection = System.Reflection.Assembly.Load("Microsoft.AspNetCore.DataProtection")
            .GetType("Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService", throwOnError: true)!;
        Assert.True(ArenaRewardReturnListener.IsKnownHostedService(nativeDataProtection));
        Assert.False(ArenaRewardReturnListener.IsKnownHostedService(typeof(ForeignBackgroundService)));
        Assert.False(ArenaRewardReturnListener.IsKnownHostedService(typeof(string)));
    }

    private sealed class ForeignBackgroundService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "aaaaaaaaaaaaaaaaaaaaaaaa";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.Return.Tests", Guid.NewGuid().ToString("N"));
        internal DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        internal RewardReturnProcess Process;
        internal bool ProcessAvailable = true;
        internal RewardControlGrant Consent;
        internal RewardReturnControl Control;
        internal string Config => Path.Combine(Root, "SPT_Data", "configs", "http.json");
        internal string Export => Path.Combine(Root, "user", "arena-rewards", Id, "return.json");
        private readonly string _operation = Guid.NewGuid().ToString("N");
        private readonly DateTimeOffset _helperStart = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            File.WriteAllText(Path.Combine(Root, ".owner"), "synthetic-return");
            File.WriteAllText(Config, "{\"ip\":\"127.0.0.1\",\"port\":6969}");
            Process = new(42, Now.AddMinutes(-10), ["--test", "two words"]);
            Consent = new(1, Guid.NewGuid().ToString("N"), Id, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                Root, "https://127.0.0.1:6969", new string('a', 64), Now, Now.AddHours(12));
            Control = NewControl();
        }
        internal RewardReturnControl NewControl() => new(Root, () => Now, () => ProcessAvailable ? Process : null,
            request => request.HelperPid == 99 && request.HelperStartedUtc == _helperStart);
        internal RewardReturnGrant Issue() { Assert.True(Control.Issue(Consent)); return ReadGrant(); }
        internal RewardReturnGrant ReadGrant() => RewardStartupFiles.Read<RewardReturnGrant>(Export, 16384);
        internal RewardReturnRequest Request(RewardReturnGrant grant) => new(1, grant.ControlId, grant.Token, _operation, 99, _helperStart);
        public void Dispose()
        {
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.Return.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".owner")) != "synthetic-return") throw new InvalidOperationException("Not an owned fixture.");
            Directory.Delete(Root, true);
        }
    }
}
