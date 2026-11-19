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
        Assert.ThrowsAny<HelperException>(() => HelperOfflineSession.CompleteStop(() => Reply(false, false),
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
        Assert.Contains("no longer exists", refused.Message);
        Assert.Contains("move it back", refused.Message);
        Assert.Equal("foreign-session-unreadable", refused.Code);
        Assert.Contains(area.Tree, refused.Message);
        Assert.True(Directory.Exists(area.Tree)); // nothing was deleted on a guess
        // stop-server (no caller) reads the owner's records too; with no server running it has nothing to stop and must not throw.
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(null!));
        Assert.True(Directory.Exists(area.Tree));
    }

    // ---- the whole SPT folder was moved: the kept tree names a folder that no longer exists ----

    // A move renames the installation folder, so its records (offline-sessions, offline-active.json) go along and the
    // tree lease still names the old path.
    private static string MoveInstallation(Area area)
    {
        string moved = Path.Combine(area.Root, "moved");
        Directory.Move(area.Source, moved);
        return moved;
    }

    private static string MovedSessionDirectory(string moved) => Path.Combine(moved, "user", "arena-rewards", "offline-sessions", Session);

    [Fact]
    public void AMovedInstallationWithAFinishedSessionIsNotBlockedByTheFolderItLeft()
    {
        using var area = new Area();
        area.Close();
        string moved = MoveInstallation(area);
        HelperRunner runner = RunnerFor(area);
        Assert.Equal(StopResult.NothingToStop, runner.StopArenaServer(moved));
        runner.RequireForeignSessionClosed(moved);
        Assert.False(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, moved));
        Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree));
    }

    [Fact]
    public void AMovedInstallationWithADiscardedSessionIsNotBlockedEither()
    {
        using var area = new Area();
        area.Discard();
        string moved = MoveInstallation(area);
        Assert.True(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, moved));
    }

    [Fact]
    public void AMovedInstallationWhoseSessionIsNotFinishedInItsOwnRecordsIsRefusedWithTheMovedExplanation()
    {
        foreach (string state in new[] { "nothing", "closed-without-completion", "still-active" })
        {
            using var area = new Area();
            if (state == "closed-without-completion") area.Close(completed: false);
            if (state == "still-active") { area.Close(); area.Active(true); }
            string moved = MoveInstallation(area);
            var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, moved));
            Assert.Equal("moved-session-unfinished", refused.Code);
            Assert.Contains("was moved", refused.Message);
            Assert.Contains("\"" + area.Source + "\"", refused.Message); // the folder it was started from
            Assert.Contains(moved, refused.Message);
            Assert.Contains("Move the folder back", refused.Message);
            Assert.Contains("Nothing was changed.", refused.Message);
            Assert.Throws<HelperException>(() => RunnerFor(area).RequireForeignSessionClosed(moved));
            Assert.True(Directory.Exists(area.Tree)); // nothing was deleted on a guess
        }
    }

    [Fact]
    public void ACopyOfTheLeaseThatIsNotByteIdenticalIsNotTakenForTheSession()
    {
        using var area = new Area();
        area.Close();
        string moved = MoveInstallation(area);
        // Same session id, same meaning, other bytes: the copy proves nothing about the session the tree was built for.
        File.WriteAllBytes(Path.Combine(MovedSessionDirectory(moved), "lease.json"), Bytes(area.Lease with { Port = 6971 }));
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, moved));
        Assert.Equal("moved-session-unfinished", refused.Code);
        File.WriteAllBytes(Path.Combine(MovedSessionDirectory(moved), "lease.json"), [.. Bytes(area.Lease), (byte)' ']);
        Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, moved));
    }

    [Fact]
    public void AReadableRecordingInstallationAlwaysKeepsTheDecisionEvenIfACopyOfItsRecordsSaysClosed()
    {
        using var area = new Area();
        area.Close();
        string copy = Path.Combine(area.Root, "copy");
        CopyDirectory(area.Source, copy); // a copied (not moved) installation: the original still exists
        File.Delete(Path.Combine(area.SessionDirectory, "closed.json"));
        File.Delete(Path.Combine(area.SessionDirectory, "completion.json"));
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, copy));
        Assert.Contains("has not been closed", refused.Message); // the original's records win: that session is still open there
        Assert.Equal("foreign-session-open", refused.Code);
        Assert.Contains(area.Source, refused.Message);
        // and once the original has closed it, the copy may go on
        area.Close();
        Assert.False(HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, copy));
    }

    [Fact]
    public void AVanishedInstallationWithoutACopyOfTheSessionStaysRefusedAndSaysWhatToDo()
    {
        using var area = new Area();
        area.Close();
        string other = OtherInstallation(area);
        Directory.Delete(area.Source, true);
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, other));
        Assert.Equal("foreign-session-unreadable", refused.Code);
        Assert.Contains("no longer exists", refused.Message);
        Assert.Contains("move it back", refused.Message);
        Assert.Contains(area.Tree, refused.Message);
    }

    [Fact]
    public void AnInstallationWhoseRecordsFolderIsGoneIsNotCalledGone()
    {
        using var area = new Area();
        area.Close();
        string other = OtherInstallation(area);
        // The folder is still there (records wiped, or a fresh SPT at the old path): only its records are missing.
        Directory.Delete(Path.Combine(area.Source, "user", "arena-rewards", "offline-sessions"), true);
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireClosedBeforeRebuild(area.Runtime, other));
        Assert.Equal("foreign-session-unreadable", refused.Code);
        Assert.Contains("records are missing", refused.Message);
        Assert.DoesNotContain("no longer exists", refused.Message);
        Assert.True(Directory.Exists(area.Tree)); // nothing was deleted on a guess
    }

    // A real rebuild over the kept tree of a moved installation: the junctions of the old tree point into the folder
    // that left (dangling) or at a folder that is still there. The first must go, the second must stay untouched.
    [Fact]
    public void BuildOverTheKeptTreeOfAMovedInstallationSucceedsAndRemovesLinksWithoutTouchingTheirTargets()
    {
        using var area = new Area();
        area.Close();
        string keep = Path.Combine(area.Root, "keep-target");
        Directory.CreateDirectory(keep);
        File.WriteAllText(Path.Combine(keep, "important.txt"), "do not delete");
        string vanishing = Path.Combine(area.Source, "SPT_Data", "old-database");
        Directory.CreateDirectory(vanishing);
        File.WriteAllText(Path.Combine(vanishing, "items.json"), "old");
        string danglingLink = Path.Combine(area.Tree, "SPT_Data", "database");
        string keptLink = Path.Combine(area.Tree, "SPT_Data", "images");
        Directory.CreateDirectory(Path.Combine(area.Tree, "SPT_Data"));
        var box = new HelperSandbox(area.Runtime, _ => { });
        box.CreateJunction(danglingLink, vanishing);
        box.CreateJunction(keptLink, keep);
        string moved = MoveInstallation(area);
        Assert.False(Directory.Exists(vanishing));
        WriteRuntimeFiles(moved);

        try
        {
            var result = HelperServerTree.Build(LegacyRequest(moved), 6970, area.Runtime, "test", DateTime.UtcNow, _ => { });

            Assert.Equal(area.Tree, result.TreePath);
            Assert.Equal("do not delete", File.ReadAllText(Path.Combine(keep, "important.txt"))); // the link went, never its target
            Assert.True(File.Exists(Path.Combine(area.Tree, ArenaSessionContract.MarkerFileName)));
            Assert.False(File.Exists(Path.Combine(area.Tree, HelperOfflineSession.LeaseFile))); // the old session's private copy is gone
        }
        finally { box.DeleteTree(area.Tree); } // the new junctions point into the moved folder, which Dispose removes first
    }

    [Fact]
    public void BuildOverTheKeptTreeOfAMovedInstallationWithAnUnfinishedSessionIsRefusedWithTheSameExplanationAsTheEarlyGuard()
    {
        using var area = new Area();
        string moved = MoveInstallation(area);
        WriteRuntimeFiles(moved);
        var refused = Assert.Throws<HelperException>(() => HelperServerTree.Build(LegacyRequest(moved), 6970, area.Runtime, "test", DateTime.UtcNow, _ => { }));
        Assert.Contains("was moved", refused.Message);
        Assert.True(File.Exists(Path.Combine(area.Tree, HelperOfflineSession.LeaseFile)));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void WriteRuntimeFiles(string runtime)
    {
        foreach (string name in new[] { "SPT.Server.exe", "SPT.Server.dll", "SPT.Server.deps.json", "SPT.Server.runtimeconfig.json" })
            File.WriteAllText(Path.Combine(runtime, name), name == "SPT.Server.exe" ? "MZ" : "x");
        File.WriteAllText(Path.Combine(runtime, "sptLogger.json"),
            "{\"loggers\":[{\"type\":\"File\",\"filePath\":\"./user/logs/spt/\",\"filePattern\":\"spt-%DATE%.log\"}]}");
        string data = Path.Combine(runtime, "SPT_Data");
        Directory.CreateDirectory(Path.Combine(data, "configs"));
        File.WriteAllText(Path.Combine(data, "configs", "http.json"), "{\"ip\":\"127.0.0.1\",\"port\":6969,\"backendIp\":\"127.0.0.1\",\"backendPort\":6969}");
        Directory.CreateDirectory(Path.Combine(data, "database")); File.WriteAllText(Path.Combine(data, "database", "items.json"), "items");
        Directory.CreateDirectory(Path.Combine(data, "images")); File.WriteAllText(Path.Combine(data, "images", "a.png"), "png");
        Directory.CreateDirectory(Path.Combine(runtime, "user", "profiles"));
        File.WriteAllText(Path.Combine(runtime, "user", "profiles", Profile + ".json"), "{\"profile\":1}");
        Directory.CreateDirectory(Path.Combine(runtime, "user", "mods", "ArenaMode"));
        File.WriteAllText(Path.Combine(runtime, "user", "mods", "ArenaMode", "maschine-ArenaMode.Server.dll"), "arena");
    }

    private static HelperRequest LegacyRequest(string runtime)
    {
        var fields = new Dictionary<string, object?>
        {
            ["schema"] = 1, ["direction"] = "enter", ["gamePid"] = 1234, ["gameExe"] = Path.Combine(runtime, "EscapeFromTarkov.exe"), ["gameDir"] = runtime,
            ["gameArgs"] = new List<object?> { "-token=" + Profile, "-config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}", "-force-gfx-jobs" },
            ["profileId"] = Profile, ["sptRuntimeDir"] = runtime, ["policyHash"] = "3755e0db97983440",
            ["serverMods"] = new List<object?> { new Dictionary<string, object?>
                { ["guid"] = "com.maschine.ArenaMode", ["folder"] = Path.Combine(runtime, "user", "mods", "ArenaMode") } },
        };
        Assert.True(HelperRequest.TryParse(ArenaMiniJson.Write(fields), "enter", out var request, out var error), error);
        return request;
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

    // ownRecordsWiped: the installation that enters is the one that owns the kept tree, but its session records are gone, so
    // the refusal is a file error and not a HelperException; the player still has to be told why the game restarted.
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnterRefusedBeforeTheNormalServerIsTouchedReopensTheNormalGameAndSaysWhy(bool ownRecordsWiped)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real server stand-in");
        using var area = new Area();
        if (!ownRecordsWiped) area.Active(true); // installation A left a session that is not closed
        else Directory.Delete(area.SessionDirectory, true);
        string installB = ownRecordsWiped ? area.Source : OtherInstallation(area);
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
            Assert.DoesNotContain("Keep the SPT server open", notice);
            Assert.DoesNotContain("running", notice);
            Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree)); // A's session is exactly as it was
            if (ownRecordsWiped) Assert.Contains("helper.log", notice);
            else
            {
                Assert.Contains(area.Source, notice);
                Assert.Contains("has not been closed", notice);
                Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source)));
                Assert.False(File.Exists(HelperOfflineSession.ActivePath(installB)));
            }
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
    // ---- a normal server that started with Arena recovery paused ----

    // The note exactly as the server writes it (RewardPausedNote), so the helper's reader is checked against the real writer.
    private static void PausedNote(Area area, int pid, DateTime started, string reason = "offline-consent-conflict")
        => RewardPausedNote.Write(area.Source, reason, true, pid, new DateTimeOffset(started, TimeSpan.Zero));

    private static (Process Process, DateTime Started) Self()
    {
        var self = Process.GetCurrentProcess();
        Assert.True(HelperProcesses.TryGetStartTimeUtc(self, out DateTime started));
        return (self, started);
    }

    private static HelperHttpEndpoint FreeEndpoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        return new HelperHttpEndpoint { ServerHost = "127.0.0.1", ServerPort = port, BackendPort = port };
    }

    [Fact]
    public void ANormalServerThatPausedItsRecoveryIsKeptAndNamesItsReasonInsteadOfFailingTheReturn()
    {
        using var area = new Area();
        area.Active(true); // the Arena session is not settled: no closure, the pointer is still there
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        var (self, started) = Self();
        int starts = 0, waits = 0;
        string? reason = HelperOfflineSession.StartNormal(lease, FreeEndpoint(), (exe, args, working) =>
        {
            starts++;
            PausedNote(area, self.Id, started); // what the paused server writes before it listens
            return Process.GetCurrentProcess();
        }, (host, port, process, what, free) => waits++, _ => { });
        Assert.Equal("offline-consent-conflict", reason);
        Assert.Equal(1, starts); Assert.Equal(1, waits); // started once, never restarted
        Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source))); // the session is left for the next start
        Assert.True(HelperOfflineSession.HasOfflineTree(area.Tree)); // and its private tree is kept
        // The note proves nothing about a closure: with the pointer present a new entry is still refused.
        Assert.Throws<HelperException>(() => HelperOfflineSession.RequireNoUnsettledSession(area.Source));
    }

    public static IEnumerable<object[]> NotesThatDoNotNameThisServer() => new[]
    {
        new object[] { "none" }, new object[] { "other-pid" }, new object[] { "other-start" }, new object[] { "written-before-start" },
        new object[] { "bad-reason" }, new object[] { "garbage" },
    };

    [Theory]
    [MemberData(nameof(NotesThatDoNotNameThisServer))]
    public void ANoteThatDoesNotNameTheStartedServerNeverExcusesAnUnsettledSession(string kind)
    {
        using var area = new Area();
        area.Active(true);
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        var (self, started) = Self();
        int starts = 0;
        Assert.ThrowsAny<Exception>(() => HelperOfflineSession.StartNormal(lease, FreeEndpoint(), (exe, args, working) =>
        {
            starts++;
            switch (kind)
            {
                case "other-pid": PausedNote(area, self.Id + 1, started); break;
                case "other-start": PausedNote(area, self.Id, started.AddHours(-3)); break;
                case "written-before-start":
                    RewardStartupFiles.WriteAtomic(area.Source, RewardPausedNote.PathFor(area.Source), JsonSerializer.Serialize(new
                    {
                        schema = 1, reason = "offline-consent-conflict", recoveryPending = true, serverPid = self.Id,
                        serverStartedUtc = started.ToString("O"), writtenUtc = started.AddHours(-1).ToString("O")
                    }));
                    break;
                case "bad-reason":
                    RewardStartupFiles.WriteAtomic(area.Source, RewardPausedNote.PathFor(area.Source), JsonSerializer.Serialize(new
                    {
                        schema = 1, reason = "bad reason with spaces", recoveryPending = true, serverPid = self.Id,
                        serverStartedUtc = started.ToString("O"), writtenUtc = started.AddSeconds(1).ToString("O")
                    }));
                    break;
                case "garbage":
                    Directory.CreateDirectory(Path.GetDirectoryName(RewardPausedNote.PathFor(area.Source))!);
                    File.WriteAllText(RewardPausedNote.PathFor(area.Source), "{ not json");
                    break;
            }
            return Process.GetCurrentProcess();
        }, (host, port, process, what, free) => { }, _ => { }));
        Assert.Equal(1, starts);
    }

    [Fact]
    public void ASettledSessionIsNeverReportedAsPausedEvenWithAMatchingNote()
    {
        using var area = new Area();
        area.Active(true);
        var lease = HelperOfflineSession.ReadPrivate(area.Tree);
        var (self, started) = Self();
        PausedNote(area, self.Id, started);
        Assert.Null(HelperOfflineSession.StartNormal(lease, FreeEndpoint(), (exe, args, working) => Process.GetCurrentProcess(), (host, port, process, what, free) =>
        {
            area.Close();
            File.Delete(HelperOfflineSession.ActivePath(area.Source));
        }, _ => { }));
    }

    [Fact]
    public void AnEntryIsRefusedLocallyOnlyWhileAnEarlierSessionIsUnsettled()
    {
        using var area = new Area();
        HelperOfflineSession.RequireNoUnsettledSession(area.Source); // no pointer: nothing blocks
        area.Active(true);
        var refused = Assert.Throws<HelperException>(() => HelperOfflineSession.RequireNoUnsettledSession(area.Source));
        Assert.Equal("session-unsettled", refused.Code);
        Assert.True(refused.PointsToServerLog);
        string text = HelperRunner.EnterFailureNotice(refused, area.Source, true);
        Assert.Contains("earlier Arena session is not settled yet", text);
        Assert.Contains("Your profile was not changed.", text);
        Assert.Equal("session-unsettled", HelperRunner.EnterFailureCode(refused));
        Assert.DoesNotContain("Arena startup could not finish", text);
    }

    // A real runner leave against a stand-in normal server that listens but did not settle the session: the server is neither
    // stopped nor started a second time, the game opens against it, and the one dialog names what happened and the reason code.
    [SkippableFact]
    public void ALeaveWhoseNormalServerPausedItsRecoveryOpensTheGameOnceAndShowsOneDialog()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real server stand-in");
        using var area = new Area();
        area.Active(true);
        HelperHttpEndpoint endpoint = FreeEndpoint();
        string configs = Path.Combine(area.Source, "SPT_Data", "configs");
        Directory.CreateDirectory(configs);
        File.WriteAllText(Path.Combine(configs, HelperLayout.HttpJsonFileName),
            "{ \"ip\": \"127.0.0.1\", \"port\": " + endpoint.ServerPort + ", \"backendIp\": \"127.0.0.1\", \"backendPort\": " + endpoint.ServerPort + " }");
        string gameExe = Path.Combine(area.Root, "ArenaC4Game.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), gameExe);
        var request = new HelperRequest
        {
            Schema = 3, Direction = "leave", GamePid = FinishedProcessId(), GameDir = area.Root, GameExe = gameExe,
            GameArgs = ["-token=" + Profile, "-config={'BackendUrl':'https://127.0.0.1:6970','Version':'live','MatchingVersion':'live'}"],
            ProfileId = Profile, OfflineSessionId = Session, SptRuntimeDir = area.Source,
        };
        var serverIds = new List<int>(); var games = new List<int>(); var starts = new List<string>();
        string? notice = null;
        HelperRunner runner = RunnerFor(area);
        runner.FailureNotice = message => notice = message;
        runner.ProcessStarter = (exe, arguments, working) =>
        {
            starts.Add(Path.GetFileName(exe));
            if (exe == gameExe)
            {
                var game = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
                games.Add(game.Id); // the runner disposes the Process it is handed, so remember the id
                return game;
            }
            var server = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                "-NoProfile -NonInteractive -Command \"$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback," + endpoint.ServerPort + ");$l.Start();Start-Sleep 120\"")
                { UseShellExecute = false, CreateNoWindow = true })!;
            serverIds.Add(server.Id);
            Assert.True(HelperProcesses.TryGetStartTimeUtc(server, out DateTime serverStarted));
            PausedNote(area, server.Id, serverStarted, "offline-activation-proof-missing");
            return server;
        };
        try
        {
            Task<bool> leaving = Task.Run(() => runner.Leave(request));
            Assert.True(leaving.Wait(TimeSpan.FromSeconds(60)), "the leave did not finish");
            Assert.True(leaving.Result);
            Assert.Equal(2, starts.Count); // the server once, the game once: no restart loop
            Assert.Equal("ArenaC4Game.exe", starts[1]);
            using (Process kept = Process.GetProcessById(serverIds[0])) Assert.False(kept.HasExited); // the paused server was kept
            Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source))); // and the session left for its next start
            Assert.NotNull(notice);
            Assert.Contains("Arena ended and SPT is running again", notice);
            Assert.Contains("could not settle the Arena session", notice);
            Assert.Contains("retried the next time SPT starts", notice);
            Assert.Contains("report the code", notice); // a pause that recurs must not leave the player without a next step
            Assert.Contains("offline-activation-proof-missing", notice);
            Assert.Contains(@"user\arena-rewards", notice);
            Assert.Contains("helper.log", notice);
            Assert.EndsWith("Code: return-rewards-paused", notice);
            Assert.DoesNotContain("could not be reopened", notice);
            Assert.DoesNotContain("not changed", notice); // the helper cannot prove the profile state, so it does not claim it
        }
        finally
        {
            foreach (int id in games) { try { using Process game = Process.GetProcessById(id); game.Kill(); game.WaitForExit(5000); } catch (Exception) { } }
            foreach (int id in serverIds) { try { using Process p = Process.GetProcessById(id); p.Kill(); p.WaitForExit(5000); } catch (Exception) { } }
        }
    }

    // The player entered while an earlier session is unsettled: the helper refuses before it stops anything, reopens the game
    // against the unchanged normal server (whose pointer belongs to the OLD session, not to this entry) and says why.
    [SkippableFact]
    public void AnEnterWhileAnEarlierSessionIsUnsettledReopensTheNormalGameWithItsOwnTextAndCode()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real server stand-in");
        using var area = new Area();
        area.Active(true); // the earlier session: pointer present, never closed
        HelperHttpEndpoint endpoint = FreeEndpoint();
        int port = endpoint.ServerPort;
        string newSession = Guid.NewGuid().ToString("N");
        string configs = Path.Combine(area.Source, "SPT_Data", "configs");
        Directory.CreateDirectory(configs);
        File.WriteAllText(Path.Combine(configs, HelperLayout.HttpJsonFileName),
            "{ \"ip\": \"127.0.0.1\", \"port\": " + port + ", \"backendIp\": \"127.0.0.1\", \"backendPort\": " + port + " }");
        string serverExe = Path.Combine(area.Source, HelperLayout.ServerExeName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), serverExe);
        string gameExe = Path.Combine(area.Source, "ArenaC4Game.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), gameExe);
        using Process normal = Process.Start(new ProcessStartInfo(serverExe,
            "-NoProfile -NonInteractive -Command \"$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback," + port + ");$l.Start();Start-Sleep 120\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var games = new List<int>();
        try
        {
            var clock = Stopwatch.StartNew();
            while (!HelperProcesses.CanConnect("127.0.0.1", port, 500))
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the stand-in server did not listen");
            Assert.True(HelperProcesses.TryGetStartTimeUtc(normal, out DateTime started));
            var grant = new RewardEntryGrant(1, "arena-entry", newSession, Profile, Convert.ToBase64String(new byte[32]), area.Source,
                "https://127.0.0.1:" + port, new string('a', 64), new DateTimeOffset(DateTime.UtcNow), new DateTimeOffset(DateTime.UtcNow.AddMinutes(5)),
                normal.Id, new DateTimeOffset(started, TimeSpan.Zero), ["--normal-arg"]);
            string grantDirectory = Path.Combine(area.Source, "user", "arena-rewards", Profile);
            Directory.CreateDirectory(grantDirectory);
            File.WriteAllBytes(Path.Combine(grantDirectory, "entry.json"), Bytes(grant));
            var request = new HelperRequest
            {
                Schema = 3, Direction = "enter", GamePid = FinishedProcessId(), GameDir = area.Source, GameExe = gameExe,
                GameArgs = ["-token=" + Profile, "-config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}", "-force-gfx-jobs"],
                ProfileId = Profile, OfflineSessionId = newSession, SptRuntimeDir = area.Source,
                ServerMods = [new HelperServerMod { Guid = "com.maschine.ArenaMode", Folder = Path.Combine(area.Source, "user", "mods", "ArenaMode") }],
            };
            var starts = new List<string>();
            string? notice = null;
            HelperRunner runner = RunnerFor(area);
            runner.FailureNotice = message => notice = message;
            runner.ProcessStarter = (exe, arguments, working) =>
            {
                starts.Add(exe + " " + arguments);
                Process game = Process.Start(new ProcessStartInfo(exe, "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
                games.Add(game.Id);
                return game;
            };
            // Without the refusal the helper would ask the stand-in to stop and wait on a desktop prompt; the bound turns that into a failure.
            Task<bool> entering = Task.Run(() => runner.Enter(request));
            Assert.True(entering.Wait(TimeSpan.FromSeconds(45)), "the enter went on to stop the normal server instead of refusing first");
            Assert.False(entering.Result);
            Assert.True(starts.Count == 1, "notice: " + notice + " / log: " + File.ReadAllText(Path.Combine(area.Runtime, "helper.log")));
            Assert.DoesNotContain("-arena-session", starts[0]);
            Assert.False(normal.HasExited); // the normal server was never stopped
            Assert.True(File.Exists(HelperOfflineSession.ActivePath(area.Source))); // the old session is untouched
            Assert.NotNull(notice);
            Assert.Contains("SPT was reopened unchanged", notice);
            Assert.Contains("earlier Arena session is not settled yet", notice);
            Assert.EndsWith("Code: session-unsettled", notice);
            Assert.DoesNotContain("Arena startup could not finish", notice);
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

    private static HelperRequest Request(Area area, string direction) => new()
    {
        Schema = 3, Direction = direction, GamePid = 123, GameDir = area.Root, GameExe = Path.Combine(area.Root, "EscapeFromTarkov.exe"),
        ProfileId = Profile, OfflineSessionId = Session, SptRuntimeDir = area.Source,
        ServerMods = direction == "enter" ? [new HelperServerMod { Guid = "com.maschine.ArenaMode", Folder = Path.Combine(area.Source, "user", "mods", "maschine-ArenaMode") }] : []
    };
}
