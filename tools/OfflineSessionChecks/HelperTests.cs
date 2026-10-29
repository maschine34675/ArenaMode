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
