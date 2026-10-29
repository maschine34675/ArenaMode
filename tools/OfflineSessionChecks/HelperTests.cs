using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ArenaMode.Server.Rewards;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

public sealed class OfflineSessionHelperTests
{
    private const string Profile = "aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Session = "11111111111111111111111111111111";
    private const string Operation = "22222222222222222222222222222222";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static byte[] Bytes<T>(T value) => Encoding.UTF8.GetBytes(RewardRuntimeFiles.Json(value));

    private sealed class Area : IDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "arena-offline-helper-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Root, "normal");
        internal string Runtime => Path.Combine(Root, "helper");
        internal string Tree => HelperServerTree.GetTreePath(Runtime);
        internal RewardOfflineLease Lease;
        internal Area(bool enabled = false, bool marker = true)
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Tree);
            Lease = new(1, Session, Profile, Source, Tree, HelperOfflineSession.MarkerTime(Now.UtcDateTime), 6970, 3, enabled, null,
                Convert.ToBase64String(new byte[32]), int.MaxValue - 1, Now.AddMinutes(-30),
                ["--flag", "two words", "quotes\"and\\slashes"], Operation, 123, Now.AddMinutes(-1));
            WriteOriginal();
            File.WriteAllBytes(Path.Combine(Tree, HelperOfflineSession.LeaseFile), Bytes(Lease));
            if (marker) WriteMarker();
        }
        internal string SessionDirectory => Path.Combine(Source, "user", "arena-rewards", "offline-sessions", Session);
        internal void WriteOriginal()
        {
            Directory.CreateDirectory(SessionDirectory);
            File.WriteAllBytes(Path.Combine(SessionDirectory, "lease.json"), Bytes(Lease));
        }
        internal void WriteMarker()
        {
            var marker = HelperMarker.Create(Now.UtcDateTime, Profile, 6970, Source, "test", "test", Path.Combine(Tree, "SPT.Server.exe"));
            marker.OfflineSessionId = Session;
            marker.CreatedUtc = Lease.MarkerCreatedUtc;
            File.WriteAllText(Path.Combine(Tree, ArenaSessionContract.MarkerFileName), marker.ToJson());
        }
        internal void Active(bool activated = false) => File.WriteAllBytes(HelperOfflineSession.ActivePath(Source),
            Bytes(new RewardOfflineReference(1, Session, RewardOfflineSession.Hash(Lease), activated)));
        internal void Close(bool activated = true, bool completed = true)
        {
            var state = new RewardOfflineState(1, Session, RewardOfflineSession.Hash(Lease), 3, Lease.Enabled);
            byte[] closure = Bytes(new RewardOfflineClosure(1, Session, RewardOfflineSession.Hash(Lease), state, activated, []));
            File.WriteAllBytes(Path.Combine(SessionDirectory, "closed.json"), closure);
            if (completed) File.WriteAllBytes(Path.Combine(SessionDirectory, "completion.json"), Bytes(new
                { schema = 1, sessionId = Session, closureHash = HelperOfflineSession.Hash(closure) }));
        }
        internal void Discard(bool complete = true)
        {
            var activation = new RewardOfflineActivation(1, Session, RewardOfflineSession.Hash(Lease), int.MaxValue - 2, Now);
            File.WriteAllBytes(Path.Combine(SessionDirectory, "activation.json"), Bytes(activation));
            var discard = new RewardOfflineDiscard(1, Session, RewardOfflineSession.Hash(Lease), HelperOfflineSession.Hash(Bytes(activation)),
                new(1, Lease.Enabled, 3), new(1, false, 4));
            File.WriteAllBytes(Path.Combine(SessionDirectory, "discarded.json"), Bytes(discard));
            if (complete) File.WriteAllBytes(Path.Combine(SessionDirectory, "discard-completion.json"),
                Bytes(new RewardOfflineDiscardCompletion(1, Session, HelperOfflineSession.Hash(Bytes(discard)))));
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualServerWireAndRawLeaseHashAgreeWithoutEnrollment(bool enabled)
    {
        using var area = new Area(enabled);
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        Assert.Equal(RewardOfflineSession.Hash(area.Lease), lease.Hash);
        Assert.Equal(area.Lease.NormalServerArguments, lease.Arguments);
        Assert.Equal(area.Lease.MarkerCreatedUtc, lease.MarkerCreated);
        area.Active();
        HelperOfflineSession.RequireActive(lease);
        area.Active(true);
        HelperOfflineSession.RequireActive(lease);
    }

    [Fact]
    public void RealEntryGrantParsesAndRetainsLaunchArguments()
    {
        using var area = new Area();
        var grant = new RewardEntryGrant(1, "arena-entry", Session, Profile, Convert.ToBase64String(new byte[32]),
            area.Source, "https://127.0.0.1:6969", new string('a', 64), Now, Now.AddMinutes(5),
            area.Lease.NormalServerPid, area.Lease.NormalServerStartedUtc, area.Lease.NormalServerArguments);
        var parsed = HelperOfflineSession.ParseGrant(Bytes(grant), area.Source, Profile, Session, Now.UtcDateTime);
        Assert.Equal(grant.ServerArguments, parsed.Arguments);
        Assert.Equal(grant.ServerStartedUtc.UtcDateTime, parsed.ServerStarted);
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.ParseGrant(Bytes(grant with { Purpose = "reward-return" }), area.Source, Profile, Session, Now.UtcDateTime));
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.ParseGrant(Bytes(grant), area.Source, Profile, Session, Now.AddMinutes(6).UtcDateTime));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("helper")]
    [InlineData("helper-start")]
    [InlineData("normal")]
    [InlineData("normal-start")]
    [InlineData("args")]
    [InlineData("port")]
    [InlineData("marker")]
    [InlineData("profile")]
    [InlineData("root")]
    public void ReadyLeaseMustMatchTheExactShutdownOperation(string changed)
    {
        using var area = new Area();
        var grant = new HelperOfflineSession.Grant { SessionId = Session, ProfileId = Profile, Source = area.Source,
            ServerPid = area.Lease.NormalServerPid, ServerStarted = area.Lease.NormalServerStartedUtc.UtcDateTime,
            Arguments = area.Lease.NormalServerArguments.ToList() };
        var lease = HelperOfflineSession.ParseLease(Bytes(area.Lease));
        void Check() => HelperOfflineSession.RequireEntryBinding(lease, grant, area.Tree, area.Lease.MarkerCreatedUtc, 6970,
            Operation, area.Lease.EntryHelperPid, area.Lease.EntryHelperStartedUtc.UtcDateTime);
        Check();
        switch (changed)
        {
            case "operation": lease.Operation = new string('9', 32); break;
            case "helper": lease.HelperPid++; break;
            case "helper-start": lease.HelperStarted = lease.HelperStarted.AddTicks(1); break;
            case "normal": lease.NormalPid--; break;
            case "normal-start": lease.NormalStarted = lease.NormalStarted.AddTicks(1); break;
            case "args": lease.Arguments.Add("extra"); break;
            case "port": lease.Port++; break;
            case "marker": lease.MarkerCreated = Now.AddSeconds(1).ToString("o"); break;
            case "profile": lease.ProfileId = new string('b', 24); break;
            case "root": lease.Source = area.Tree; break;
        }
        Assert.Throws<HelperException>(Check);
    }

    [Fact]
    public void AcceptedShutdownRequiresReadyBeforeExitBeforeCopy()
    {
        var events = new List<string>();
        HelperOfflineSession.CompleteStop(() => { events.Add("send"); return Reply(true, true); },
            () => events.Add("ready"), _ => { events.Add("exit"); return true; }, () => throw new Exception("unexpected retry"), _ => { });
        events.Add("copy");
        Assert.Equal(["send", "ready", "exit", "copy"], events);
    }

    [Fact]
    public void LostReplyCanCompleteOnlyWithReadyAndActualExit()
    {
        int ready = 0, exited = 0;
        HelperOfflineSession.CompleteStop(() => throw new IOException(), () => ready++, _ => { exited++; return true; },
            () => throw new Exception("unexpected retry"), _ => { });
        Assert.Equal(1, ready); Assert.Equal(1, exited);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void MissingReadbackProofOrMissingExitNeverAuthorizesCopy(bool accepted, bool proof)
    {
        int exitCalls = 0;
        Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(() => accepted ? Reply(true, true) : throw new IOException(),
            () => { if (!proof) throw new InvalidDataException(); }, _ => { exitCalls++; return !proof; }, () => false, _ => { }));
        Assert.Equal(proof ? 1 : 0, exitCalls);
    }

    [Fact]
    public void UnheldRefusalDoesNotReadProofOrWaitForExit()
    {
        Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(() => Reply(false, false),
            () => throw new Exception("must not use old proof"), _ => throw new Exception("must not await old exit"),
            () => throw new Exception("must not retry refusal"), _ => { }));
    }

    [Fact]
    public void HeldRefusalDoesNotReturnWithoutProof()
    {
        int retry = 0;
        Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(() => Reply(false, true),
            () => throw new InvalidDataException(), _ => throw new Exception("must not infer exit"), () => { retry++; return false; }, _ => { }));
        Assert.Equal(1, retry);
    }

    [Fact]
    public void ActivationBindsExactChildAndLease()
    {
        using var area = new Area();
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        var proof = new RewardOfflineActivation(1, Session, lease.Hash, 333, Now);
        HelperOfflineSession.ValidateActivation(Bytes(proof), lease, 333, Now.UtcDateTime);
        Assert.Throws<HelperException>(() => HelperOfflineSession.ValidateActivation(Bytes(proof), lease, 334, Now.UtcDateTime));
        Assert.Throws<HelperException>(() => HelperOfflineSession.ValidateActivation(Bytes(proof), lease, 333, Now.AddTicks(1).UtcDateTime));
        Assert.Throws<HelperException>(() => HelperOfflineSession.ValidateActivation(Bytes(proof with { LeaseHash = new string('f', 64) }), lease, 333, Now.UtcDateTime));
    }

    [Fact]
    public void ChangedPrivateLeaseOrMarkerCannotBeReturned()
    {
        using var area = new Area();
        File.WriteAllBytes(Path.Combine(area.Tree, HelperOfflineSession.LeaseFile), Bytes(area.Lease with { Port = 6971 }));
        Assert.Throws<HelperException>(() => HelperOfflineSession.ReadPrivate(area.Tree));
        File.WriteAllBytes(Path.Combine(area.Tree, HelperOfflineSession.LeaseFile), Bytes(area.Lease));
        var markerPath = Path.Combine(area.Tree, ArenaSessionContract.MarkerFileName);
        File.WriteAllText(markerPath, File.ReadAllText(markerPath).Replace(Session, new string('a', 32)));
        Assert.Throws<HelperException>(() => HelperOfflineSession.ReadPrivate(area.Tree));
    }

    [Fact]
    public void LeaseCopyIsByteExactAndRejectsChangedDestinationMarker()
    {
        using var area = new Area();
        area.Active();
        string destination = Path.Combine(area.Tree, HelperOfflineSession.LeaseFile);
        File.Delete(destination);
        using var entry = EntryFor(area);
        Assert.True(HelperMarker.TryParse(File.ReadAllText(Path.Combine(area.Tree, ArenaSessionContract.MarkerFileName)), out var marker, out _));
        marker.Port++;
        Assert.Throws<HelperException>(() => entry.CopyLease(marker));
        Assert.False(File.Exists(destination));
        marker.Port--;
        entry.CopyLease(marker);
        Assert.Equal(File.ReadAllBytes(Path.Combine(area.SessionDirectory, "lease.json")), File.ReadAllBytes(destination));
    }

    [Fact]
    public void ReadyProofCannotCopyWhileTheExactOriginalProcessStillRuns()
    {
        using var area = new Area();
        area.Active();
        using (var positive = EntryFor(area))
        {
            positive.Startup = HelperOfflineSession.AcquireStartup(area.Source);
            positive.ReadyStopped = true;
            positive.RequireBuild(Request(area, "enter"), area.Tree, 6970, Now.UtcDateTime);
        }
        using var current = Process.GetCurrentProcess();
        Assert.True(HelperProcesses.TryGetStartTimeUtc(current, out var started));
        area.Lease = area.Lease with { NormalServerPid = current.Id, NormalServerStartedUtc = started };
        area.WriteOriginal(); area.Active();
        using var entry = EntryFor(area);
        entry.Startup = HelperOfflineSession.AcquireStartup(area.Source);
        entry.ReadyStopped = true;
        Assert.Throws<HelperException>(() => entry.RequireBuild(Request(area, "enter"), area.Tree, 6970, Now.UtcDateTime));
    }

    [Fact]
    public void EmptyOutboxStillNeedsCommittedClosureBeforeRebuild()
    {
        using var area = new Area();
        area.Active(true);
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        area.Close();
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        File.Delete(HelperOfflineSession.ActivePath(area.Source));
        HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardedTreeCanBeRebuiltWithoutItsMarkerOrOldOutboxTransfer(bool missingMarker)
    {
        using var area = new Area(marker: !missingMarker);
        string outbox = Path.Combine(area.Tree, "user", HelperRewardTransfer.OutboxFolderName);
        Directory.CreateDirectory(outbox);
        File.WriteAllText(Path.Combine(outbox, new string('a', 32) + ".json"), "discarded-result");
        area.Discard();
        var lease = HelperOfflineSession.ReadBoundPrivate(area.Tree);
        Assert.True(HelperOfflineSession.IsDiscarded(lease));
        Assert.False(HelperOfflineSession.RequireClosed(lease));
        Assert.True(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        Assert.True(File.Exists(Path.Combine(outbox, new string('a', 32) + ".json")));
        area.Active(true);
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireActive(lease));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("wrong-hash")]
    [InlineData("foreign-lease")]
    [InlineData("closure")]
    public void DiscardedTreeRequiresExactDurableAbandonment(string broken)
    {
        using var area = new Area();
        area.Discard(complete: broken != "incomplete");
        if (broken == "wrong-hash") File.WriteAllBytes(Path.Combine(area.SessionDirectory, "discard-completion.json"),
            Bytes(new RewardOfflineDiscardCompletion(1, Session, new string('f', 64))));
        if (broken == "foreign-lease") File.WriteAllBytes(Path.Combine(area.Tree, HelperOfflineSession.LeaseFile), Bytes(area.Lease with { Port = 6971 }));
        if (broken == "closure") area.Close();
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("json")]
    [InlineData("base64")]
    [InlineData("utf8")]
    public void DiscardedTreeWithLostLeaseUsesTheExactIntactMarker(string damage)
    {
        using var area = new Area();
        area.Discard();
        string path = Path.Combine(area.Tree, HelperOfflineSession.LeaseFile);
        if (damage == "json") File.WriteAllText(path, "{");
        else if (damage == "base64") File.WriteAllBytes(path, Bytes(area.Lease with { ControlKey = "not-base64" }));
        else if (damage == "utf8") File.WriteAllBytes(path, [0xff, 0xff]);
        else File.Delete(path);
        Assert.True(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        File.Delete(Path.Combine(area.Tree, ArenaSessionContract.MarkerFileName));
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.ReadBoundPrivate(area.Tree));
    }

    [Fact]
    public void StoppedTreeWithLostMarkerCanReachNormalStartupThroughDirectStartCleanup()
    {
        using var area = new Area(marker: false);
        area.Active(true);
        var log = new HelperLog(Path.Combine(area.Runtime, "helper.log"));
        var runner = new HelperRunner(area.Runtime, log, "test");
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(area.Source));
        Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source)));
        Assert.False(File.Exists(Path.Combine(area.SessionDirectory, "discarded.json")));
    }

    [Fact]
    public void ClosurePlanAndLostActivePointerAreNotCompletedImport()
    {
        using var area = new Area();
        area.Close(completed: false);
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
        area.Close();
        HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime);
        string completion = Path.Combine(area.SessionDirectory, "completion.json");
        File.WriteAllText(completion, File.ReadAllText(completion).Replace(HelperOfflineSession.Hash(File.ReadAllBytes(Path.Combine(area.SessionDirectory, "closed.json"))), new string('f', 64)));
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
    }

    [Fact]
    public void NeverActivatedClosedBuildCanBeRecoveredWithoutMarker()
    {
        using var area = new Area(marker: false);
        area.Close(false);
        HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime);
        area.Close(true);
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
    }

    [Fact]
    public void OfflineResultsNeverTakeLegacyTransferPath()
    {
        using var area = new Area();
        string outbox = Path.Combine(area.Tree, "user", HelperRewardTransfer.OutboxFolderName);
        Directory.CreateDirectory(outbox);
        string receipt = Path.Combine(outbox, new string('a', 32) + ".json");
        File.WriteAllText(receipt, "synthetic-opaque-result");
        Assert.Equal(0, HelperRewardTransfer.ReturnPending(area.Runtime, area.Source));
        Assert.Equal("synthetic-opaque-result", File.ReadAllText(receipt));
        area.Close();
        Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime));
    }

    [Fact]
    public void StartupHandoffActuallyExcludesAnotherLocalOwner()
    {
        using var area = new Area();
        string path = Path.Combine(area.Source, "user", "arena-rewards", ".startup.lock");
        using (var held = HelperOfflineSession.AcquireStartup(area.Source))
            Assert.Throws<IOException>(() => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        using var replacement = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.True(replacement.CanWrite);
    }

    [SkippableFact]
    public void OfflineDetectionDoesNotTurnLinkedAuthorityIntoPermission()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows junction boundary");
        using var area = new Area();
        string holder = Path.Combine(area.Root, "holder");
        Directory.CreateDirectory(holder);
        string link = Path.Combine(holder, "linked");
        var box = new HelperSandbox(holder, _ => { });
        box.CreateJunction(link, area.Runtime);
        try
        {
            string linkedTree = HelperServerTree.GetTreePath(link);
            Assert.True(HelperOfflineSession.HasOfflineTree(linkedTree));
            Assert.ThrowsAny<Exception>(() => HelperOfflineSession.ReadPrivate(linkedTree));
        }
        finally { box.DeleteTree(link); }
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("leave")]
    public void SchemaThreeRoundtripsBoundIdentity(string direction)
    {
        using var area = new Area();
        var request = Request(area, direction);
        Assert.True(HelperRequest.TryParse(request.ToJson(), direction, out var parsed, out var error), error);
        Assert.Equal(Session, parsed.OfflineSessionId);
        request.Schema = 1;
        Assert.False(HelperRequest.TryParse(request.ToJson(), direction, out _, out _));
        request.Schema = 3; request.OfflineSessionId = null;
        Assert.False(HelperRequest.TryParse(request.ToJson(), direction, out _, out _));
    }

    [Fact]
    public void SchemaThreePublicBuildCannotBypassShutdownContext()
    {
        using var area = new Area();
        string sentinel = Path.Combine(area.Tree, "keep.txt");
        File.WriteAllText(sentinel, "retained");
        Assert.Throws<HelperException>(() => HelperServerTree.Build(Request(area, "enter"), 6970, area.Runtime, "test", Now.UtcDateTime, _ => { }));
        Assert.Equal("retained", File.ReadAllText(sentinel));
    }

    [Fact]
    public void NormalReturnUsesOriginalArgumentsAndRequiresCommittedImportAfterListening()
    {
        using var area = new Area();
        area.Active(true);
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var endpoint = new HelperHttpEndpoint { ServerHost = "127.0.0.1", ServerPort = port, BackendPort = port };
        int starts = 0, waits = 0;
        Process Start(string exe, string args, string working)
        {
            starts++;
            Assert.Equal(Path.Combine(area.Source, HelperLayout.ServerExeName), exe);
            Assert.Equal(HelperCommandLine.BuildCommandLine(area.Lease.NormalServerArguments), args);
            Assert.Equal(area.Source, working);
            return Process.GetCurrentProcess(); // no process or live server is started by this fixture
        }
        void Wait(string host, int actualPort, Process process, string what, bool formerlyFree)
        {
            waits++;
            Assert.False(formerlyFree); // actual listener ownership is mandatory on the production path
            Assert.Equal(port, actualPort);
        }
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.StartNormal(lease, endpoint, Start, Wait, _ => { }));
        Assert.Equal(1, starts); Assert.Equal(1, waits); // listening alone did not complete return
        HelperOfflineSession.StartNormal(lease, endpoint, Start, (host, actualPort, process, what, free) =>
        {
            Wait(host, actualPort, process, what, free);
            area.Close();
            File.Delete(HelperOfflineSession.ActivePath(area.Source));
        }, _ => { });
        Assert.Equal(2, starts); Assert.Equal(2, waits);
    }

    // ---- several installations of one Windows user share the kept tree (C4) ----

    private static HelperRunner RunnerFor(Area area) => new(area.Runtime, new HelperLog(Path.Combine(area.Runtime, "helper.log")), "test")
    {
        GameSettleMilliseconds = 10, GameStartCheckMilliseconds = 100, ServerSettleMilliseconds = 10, StartWatchdogAfterEnter = false,
    };
    private static string OtherInstallation(Area area)
    {
        string other = Path.Combine(area.Root, "installB");
        Directory.CreateDirectory(other);
        return other;
    }

    [Fact]
    public void ClosedSessionOfAnotherInstallationDoesNotBlock()
    {
        using var area = new Area();
        area.Close(); // closed and completed, no active pointer: the original installation is done with it
        string other = OtherInstallation(area);
        HelperRunner runner = RunnerFor(area);
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(other));
        Assert.Null(runner.ForeignRuntime);
        runner.RequireForeignSessionClosed(other);
        Assert.False(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, other));
        Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree)); // the stop step decides nothing about the tree
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(null!)); // stop-server
    }

    [Fact]
    public void UnclosedSessionOfAnotherInstallationStillRefusesTheRebuildWithTheAccurateMessage()
    {
        using var area = new Area();
        area.Active(true); // the session of installation A was never closed: its results are not returned yet
        string other = OtherInstallation(area);
        HelperRunner runner = RunnerFor(area);
        // No server runs, so the stop step proves nothing against the caller; the closure guard speaks.
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(other));
        foreach (Action refuse in new Action[]
        {
            () => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, other),
            () => runner.RequireForeignSessionClosed(other),
        })
        {
            var refused = Assert.Throws<HelperException>(refuse);
            Assert.Contains(area.Source, refused.Message);
            Assert.Contains("has not been closed", refused.Message);
            Assert.Contains("return to the normal session there", refused.Message);
            Assert.DoesNotContain("running", refused.Message);
            Assert.DoesNotContain("cannot be read", refused.Message);
        }
        Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source)));
        Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree));
        // The owner is not asked to prove anything it did not need to: its own rebuild keeps the plain refusal.
        runner.RequireForeignSessionClosed(area.Source);
        Assert.DoesNotContain("belongs to the SPT installation", Assert.ThrowsAny<Exception>(
            () => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, area.Source)).Message);
    }

    [Fact]
    public void ResultsOfAClosedForeignSessionThatWereNeverReturnedStillRefuseTheRebuild()
    {
        using var area = new Area();
        area.Close();
        string outbox = Path.Combine(area.Tree, "user", HelperRewardTransfer.OutboxFolderName);
        Directory.CreateDirectory(outbox);
        File.WriteAllText(Path.Combine(outbox, new string('a', 32) + ".json"), "unreturned-result");
        string other = OtherInstallation(area);
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, other));
        Assert.Contains("results have not been returned", refused.Message);
        Assert.Contains(area.Source, refused.Message);
        Assert.True(File.Exists(Path.Combine(outbox, new string('a', 32) + ".json")));
    }

    [Fact]
    public void ForeignSessionOfAVanishedInstallationIsRefusedWithAnExplanationNotAFileError()
    {
        using var area = new Area();
        area.Close();
        string other = OtherInstallation(area);
        Directory.Delete(area.Source, true); // installation A was deleted or moved after its session closed
        HelperRunner runner = RunnerFor(area);
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(other));
        var refused = Assert.Throws<HelperException>(() => runner.RequireForeignSessionClosed(other));
        Assert.Contains(area.Source, refused.Message);
        Assert.Contains("cannot be read", refused.Message);
        Assert.Contains(area.Tree, refused.Message);
        Assert.True(Directory.Exists(area.Tree)); // nothing was deleted on a guess
    }

    [SkippableFact]
    public void LiveServerOfAnotherInstallationStillYieldsOtherInstallation()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real process image");
        using var area = new Area();
        area.Active(true);
        string exe = Path.Combine(area.Tree, HelperLayout.ServerExeName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
        string other = OtherInstallation(area);
        HelperRunner runner = RunnerFor(area);
        using Process server = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Assert.Equal(StopResult.OtherInstallation, runner.StopArenaServer(other));
            Assert.Equal(area.Source, runner.ForeignRuntime);
            Assert.False(server.HasExited);
            string message = runner.DescribeStopRefusal(StopResult.OtherInstallation);
            Assert.Contains(area.Source, message);
            Assert.Contains("is running", message);
            Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree));
        }
        finally { try { server.Kill(); server.WaitForExit(5000); } catch (Exception) { } }
        // Without the process the same call proves nothing any more.
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(other));
        Assert.Null(runner.ForeignRuntime);
    }

    [Fact]
    public void OnlyAProvenLiveServerIsDescribedAsRunning()
    {
        using var area = new Area();
        HelperRunner runner = RunnerFor(area);
        foreach (StopResult result in new[] { StopResult.Unverified, StopResult.StillRunning })
            Assert.DoesNotContain("another SPT installation", runner.DescribeStopRefusal(result));
        Assert.DoesNotContain("is running", runner.DescribeStopRefusal(StopResult.Unverified));
    }

    [SkippableFact]
    public void AnEnterRefusedBeforeTheNormalServerIsTouchedReopensTheNormalGameAndSaysWhy()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real server stand-in");
        using var area = new Area();
        area.Active(true); // installation A left a session that is not closed
        string installB = OtherInstallation(area);
        string sessionB = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        string configs = Path.Combine(installB, "SPT_Data", "configs");
        Directory.CreateDirectory(configs);
        File.WriteAllText(Path.Combine(configs, HelperLayout.HttpJsonFileName),
            "{ \"ip\": \"127.0.0.1\", \"port\": " + port + ", \"backendIp\": \"127.0.0.1\", \"backendPort\": " + port + " }");
        // The normal server of B: a process whose image is B\SPT.Server.exe and which is the sole listener on its port.
        string serverExe = Path.Combine(installB, HelperLayout.ServerExeName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), serverExe);
        // Not named EscapeFromTarkov.exe: the process tests of this project keep stand-ins of that name alive, and the helper
        // refuses to start a game while a process of the game's name runs.
        string gameExe = Path.Combine(installB, "ArenaC4Game.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), gameExe);
        using Process normal = Process.Start(new ProcessStartInfo(serverExe,
            "-NoProfile -NonInteractive -Command \"$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback," + port + ");$l.Start();Start-Sleep 120\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var games = new List<int>(); // the runner disposes the Process objects it is handed, so remember the ids
        try
        {
            var clock = Stopwatch.StartNew();
            while (!HelperProcesses.CanConnect("127.0.0.1", port, 500))
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the stand-in server did not listen");
            Assert.True(HelperProcesses.TryGetStartTimeUtc(normal, out DateTime started));
            var grant = new RewardEntryGrant(1, "arena-entry", sessionB, Profile, Convert.ToBase64String(new byte[32]), installB,
                "https://127.0.0.1:" + port, new string('a', 64), new DateTimeOffset(DateTime.UtcNow), new DateTimeOffset(DateTime.UtcNow.AddMinutes(5)),
                normal.Id, new DateTimeOffset(started, TimeSpan.Zero), ["--normal-arg"]);
            string grantDirectory = Path.Combine(installB, "user", "arena-rewards", Profile);
            Directory.CreateDirectory(grantDirectory);
            File.WriteAllBytes(Path.Combine(grantDirectory, "entry.json"), Bytes(grant));
            var request = new HelperRequest
            {
                Schema = 3, Direction = "enter", GamePid = FinishedProcessId(), GameDir = installB, GameExe = gameExe,
                GameArgs = ["-token=" + Profile, "-config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}", "-force-gfx-jobs"],
                ProfileId = Profile, OfflineSessionId = sessionB, SptRuntimeDir = installB,
                ServerMods = [new HelperServerMod { Guid = "com.maschine.ArenaMode", Folder = Path.Combine(installB, "user", "mods", "ArenaMode") }],
            };
            var starts = new List<string>();
            string notice = null;
            HelperRunner runner = RunnerFor(area);
            runner.FailureNotice = message => notice = message;
            runner.ProcessStarter = (exe, arguments, working) =>
            {
                starts.Add(exe + " " + arguments);
                Process game = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
                games.Add(game.Id);
                return game;
            };

            // Without the early refusal the helper would go on to ask this stand-in to stop and then wait on a desktop
            // prompt; the bound turns that into a failure instead of a hung run.
            Task<bool> entering = Task.Run(() => runner.Enter(request));
            Assert.True(entering.Wait(TimeSpan.FromSeconds(45)), "the enter went on to stop the normal server instead of refusing first");
            Assert.False(entering.Result); // the Arena entry itself is refused

            Assert.Single(starts); // the normal game came back, once, against the untouched normal server
            Assert.StartsWith(gameExe, starts[0]);
            Assert.Contains(":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture), starts[0]); // BackendUrl routed back to B's server
            Assert.DoesNotContain("-arena-session", starts[0]);
            Assert.False(normal.HasExited); // the normal server was never stopped
            Assert.NotNull(notice);
            Assert.Contains("SPT was reopened unchanged", notice);
            Assert.Contains(area.Source, notice);
            Assert.Contains("has not been closed", notice);
            Assert.DoesNotContain("Keep the SPT server open", notice);
            Assert.DoesNotContain("running", notice);
            Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree)); // A's unclosed session is exactly as it was
            Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source)));
            Assert.False(File.Exists(HelperOfflineSession.ActivePath(installB)));
        }
        finally
        {
            foreach (int id in games)
            {
                try { using Process game = Process.GetProcessById(id); game.Kill(); game.WaitForExit(5000); } catch (Exception) { }
            }
            try { normal.Kill(); normal.WaitForExit(5000); } catch (Exception) { }
        }
    }

    private static int FinishedProcessId()
    {
        using Process finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })!;
        Assert.True(finished.WaitForExit(10000));
        return finished.Id;
    }

    private static Dictionary<string, object> Reply(bool accepted, bool held) => new() { ["accepted"] = accepted, ["held"] = held, ["reason"] = "test" };
    private static HelperOfflineSession.Entry EntryFor(Area area) => new()
    {
        Grant = new HelperOfflineSession.Grant { SessionId = Session, ProfileId = Profile, Source = area.Source,
            ServerPid = area.Lease.NormalServerPid, ServerStarted = area.Lease.NormalServerStartedUtc.UtcDateTime,
            Arguments = area.Lease.NormalServerArguments.ToList() },
        Private = area.Tree, MarkerCreated = area.Lease.MarkerCreatedUtc, Port = 6970, Operation = Operation,
        HelperPid = area.Lease.EntryHelperPid, HelperStarted = area.Lease.EntryHelperStartedUtc.UtcDateTime
    };
    private static HelperRequest Request(Area area, string direction) => new()
    {
        Schema = 3, Direction = direction, GamePid = 123, GameDir = area.Root, GameExe = Path.Combine(area.Root, "EscapeFromTarkov.exe"),
        ProfileId = Profile, OfflineSessionId = Session, SptRuntimeDir = area.Source,
        ServerMods = direction == "enter" ? [new HelperServerMod { Guid = "com.maschine.ArenaMode", Folder = Path.Combine(area.Source, "user", "mods", "maschine-ArenaMode") }] : []
    };
}
