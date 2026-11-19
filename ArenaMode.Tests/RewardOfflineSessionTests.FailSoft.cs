using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// Fail-soft startup: reward problems never stop SPT, except when another process may be writing (a live or unverifiable
/// private process, a changed active pointer, a private server appearing, a held lock, a private tree). Everything else
/// pauses rewards for the whole installation for this start: nothing is paid, enrolled or changed, one Warning names the
/// reason, and the next start retries from the untouched records. docs/REWARDS-RUNTIME.md, "Rewards paused at start".
/// </summary>
[Collection(RewardNativeLoadCollection.Name)]
public sealed partial class RewardOfflineSessionTests
{
    private static Task<ArenaRewardStartup.RecoveryPause?> PauseOf(Fixture f, Action<string>? step = null, Func<bool>? unloaded = null)
        => ArenaRewardStartup.RecoverOrPauseAsync(f.Root, unloaded ?? (() => true), () => { }, CancellationToken.None, TimeSpan.Zero, step);

    // Every reward record and profile file, by content; the lock files and the pause note are not records.
    private static SortedDictionary<string, string> RecordTree(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string folder in new[] { "arena-rewards", "arena-reward-runs", "arena-reward-snapshots", "profiles" })
        {
            string directory = Path.Combine(root, "user", folder);
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) is ".startup.lock" or ".server.lock" or RewardPausedNote.FileName) continue;
                result[Path.GetRelativePath(root, path)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }
        }
        return result;
    }

    private static async Task<(RewardRuntimeCoordinator Coordinator, ArenaServerInstanceTests.RecordingLogger Log)> StartSpt(Fixture f, Func<int> loaded)
    {
        var logger = DispatchProxy.Create<ISptLogger<ArenaRewardStartup>, ArenaServerInstanceTests.RecordingLogger>();
        var log = (ArenaServerInstanceTests.RecordingLogger)(object)logger;
        var coordinator = new RewardRuntimeCoordinator(f.Root);
        using var startup = new ArenaRewardStartup(null!, [], logger)
        { LoadedProfiles = loaded, Coordinator = coordinator, RecoveryRetryDelay = TimeSpan.Zero };
        string previous = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(f.Root);
            await startup.OnLoadAsync(CancellationToken.None);
        }
        finally { Directory.SetCurrentDirectory(previous); }
        return (coordinator, log);
    }

    private static void StopPrivate(Fixture f) { f.Owner!.Dispose(); f.Owner = null; }

    // Soft recovery failures that happen before recovery wrote anything: the whole reward tree must stay byte for byte.
    private static string Setup(Fixture f, string shape)
    {
        switch (shape)
        {
            case "activation-proof-missing":
                f.Start(); StopPrivate(f);
                File.Delete(Path.Combine(SessionFolder(f, f.Lease!.SessionId), "activation.json"));
                return "offline-activation-proof-missing";
            case "consent-conflict":
                f.Start(); StopPrivate(f);
                RewardEnrollment.SetConsent(f.Root, Fixture.Id, false);
                return "offline-consent-conflict";
            case "foreign-unfinished":
                f.Ready(); f.MoveRoot();
                return "offline-history-binding-mismatch";
            case "foreign-active-pointer":
                f.Start(); StopPrivate(f);
                byte[] pointer = File.ReadAllBytes(RewardOfflineSession.ActivePath(f.Root));
                f.Recover();
                File.WriteAllBytes(RewardOfflineSession.ActivePath(f.Root), pointer);
                f.MoveRoot();
                return "offline-recovery-lease-mismatch";
            case "multiple-unclosed":
                f.Ready(); File.Delete(RewardOfflineSession.ActivePath(f.Root));
                string next = Guid.NewGuid().ToString("N");
                f.Grant = f.Grant with { SessionId = next };
                f.Request = f.Request with { SessionId = next, OperationId = Guid.NewGuid().ToString("N") };
                f.Ready(); File.Delete(RewardOfflineSession.ActivePath(f.Root));
                return "offline-multiple-unclosed-sessions";
            case "profiles-loaded":
                f.Start(); StopPrivate(f);
                return "native-startup-boundary-unavailable";
            default: throw new ArgumentException(shape);
        }
    }

    [Theory]
    [InlineData("activation-proof-missing")]
    [InlineData("foreign-unfinished")]
    [InlineData("foreign-active-pointer")]
    [InlineData("multiple-unclosed")]
    [InlineData("profiles-loaded")]
    public async Task ASoftRecoveryFailurePausesRewardsAndLeavesEveryRecordAsItWas(string shape)
    {
        using var f = new Fixture();
        string code = Setup(f, shape);
        bool active = RewardOfflineSession.HasActive(f.Root);
        var before = RecordTree(f.Root);

        var (coordinator, log) = await StartSpt(f, () => shape == "profiles-loaded" ? 1 : 0);

        // SPT started (StartSpt returned). Nothing was paid, enrolled or changed.
        Assert.Equal(before, RecordTree(f.Root));
        Assert.Equal(active, RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(code, coordinator.StartupUnavailable);
        Assert.Equal(new RewardPauseState(code, true), coordinator.Pause);
        var status = coordinator.GetStatus(Fixture.Id);
        Assert.Equal("withheld", status.State);
        Assert.False(status.CanChange);
        Assert.False(status.Enabled);
        Assert.False(ArenaRewardReturnListener.NeedsStartup(status));
        Assert.Equal(shape == "profiles-loaded" ? RewardUnavailableText.ProfilesLoadedEarly : RewardUnavailableText.SessionUnsettled, status.Detail);
        // Exactly one Warning, it names the code, and nothing about the pause is logged at a lower level.
        var warning = Assert.Single(log.Lines, l => l.Level == "Warning");
        Assert.Contains(code, warning.Text);
        Assert.Contains("paused", warning.Text);
        Assert.DoesNotContain(log.Lines, l => l.Level != "Warning" && l.Text.Contains("paused"));
        // The helper's note, written before OnLoad returned.
        var note = JsonDocument.Parse(File.ReadAllText(RewardPausedNote.PathFor(f.Root))).RootElement;
        Assert.Equal(code, note.GetProperty("reason").GetString());
        Assert.Equal(Environment.ProcessId, note.GetProperty("serverPid").GetInt32());
        Assert.True(note.GetProperty("recoveryPending").GetBoolean());
    }

    [Fact]
    public async Task TheForeignSessionWarningCarriesTheFullRemedyAndSaysThatSptStarted()
    {
        using var f = new Fixture();
        f.Ready(); string oldRoot = f.MoveRoot();
        var (_, log) = await StartSpt(f, () => 0);
        var warning = Assert.Single(log.Lines, l => l.Level == "Warning").Text;
        Assert.Contains("\"" + oldRoot + "\"", warning);
        Assert.Contains("Start SPT from", warning);
        Assert.Contains("SPT starts normally", warning);
        Assert.DoesNotContain("startup stopped", warning);
    }

    [Fact]
    public async Task ARemovedCauseIsRetriedAtTheNextStartAndPaysExactlyOnce()
    {
        using var f = new Fixture();
        f.Start(); f.Capture(f.Coordinator());
        StopPrivate(f);
        string consent = File.ReadAllText(f.Metadata("consent.json"));

        var first = await StartSpt(f, () => 1); // another mod loaded the profiles first: recovery cannot run
        Assert.NotNull(first.Coordinator.Pause);
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(consent, File.ReadAllText(f.Metadata("consent.json")));
        Assert.True(File.Exists(RewardPausedNote.PathFor(f.Root)));
        string profile = f.Profile;
        // While paused, the reward choice is frozen: a change would make the retry fail with a consent conflict.
        first.Coordinator.SetConsent(Fixture.Id, false);
        Assert.Equal(consent, File.ReadAllText(f.Metadata("consent.json")));

        var second = await StartSpt(f, () => 0); // the cause is gone
        Assert.Null(second.Coordinator.Pause);
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.False(File.Exists(RewardPausedNote.PathFor(f.Root))); // a clean start removes the stale note
        Assert.Equal(profile, f.Profile);
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paid = f.Profile;
        await f.Pay();
        Assert.Equal(paid, f.Profile);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("receipt")]
    [InlineData("imported")]
    public async Task AFileErrorThatPersistsPausesAndTheNextStartPaysExactlyOnce(string stage)
    {
        using var f = new Fixture();
        f.Start(); f.Capture(f.Coordinator());
        StopPrivate(f);
        string profile = f.Profile;

        int tries = 0;
        var paused = await PauseOf(f, s => { if (s == stage) { if (++tries > 3) throw new InvalidOperationException("retried"); throw new IOException("synthetic"); } });

        Assert.Equal("offline-recovery-IOException", paused!.Code);
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(profile, f.Profile);
        Assert.False(File.Exists(f.Metadata("acknowledged.json")));
        Assert.Null(await PauseOf(f)); // the next start
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paid = f.Profile;
        await f.Pay();
        Assert.Equal(paid, f.Profile);
    }

    [Fact]
    public async Task AConsentConflictPausesWithoutOverwritingTheChoiceOrPaying()
    {
        using var f = new Fixture();
        string code = Setup(f, "consent-conflict");
        byte[] pointer = File.ReadAllBytes(RewardOfflineSession.ActivePath(f.Root));
        string consent = File.ReadAllText(f.Metadata("consent.json")), profile = f.Profile;

        var (coordinator, log) = await StartSpt(f, () => 0);

        Assert.Equal(code, coordinator.Pause!.Reason);
        Assert.Equal(consent, File.ReadAllText(f.Metadata("consent.json")));
        Assert.Equal(pointer, File.ReadAllBytes(RewardOfflineSession.ActivePath(f.Root)));
        Assert.Equal(profile, f.Profile);
        Assert.False(File.Exists(f.Metadata("acknowledged.json")));
        Assert.Single(log.Lines, l => l.Level == "Warning");
    }

    [Fact]
    public async Task APersistentFileErrorIsTriedExactlyThreeTimes()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        int attempts = 0;
        // A fourth attempt ends the loop with another error, so a retry that never stops fails here instead of hanging.
        var paused = await PauseOf(f, s => { if (s == "consent") { if (++attempts > 3) throw new InvalidOperationException("retried"); throw new IOException("synthetic"); } });
        Assert.Equal(3, attempts);
        Assert.Equal("offline-recovery-IOException", paused!.Code);
    }

    [Fact]
    public async Task AFileErrorThatClearsOnTheSecondAttemptSettlesTheSession()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        int attempts = 0;
        var paused = await PauseOf(f, s => { if (s == "consent" && ++attempts == 1) throw new IOException("scanner"); });
        Assert.Null(paused);
        Assert.False(RewardOfflineSession.HasActive(f.Root));
    }

    [Fact]
    public async Task ADataErrorIsNeverRetried()
    {
        using var f = new Fixture();
        Setup(f, "activation-proof-missing");
        int attempts = 0;
        var paused = await ArenaRewardStartup.RecoverOrPauseAsync(f.Root, () => true, () => { }, CancellationToken.None, TimeSpan.Zero,
            _ => attempts++);
        Assert.Equal("offline-activation-proof-missing", paused!.Code);
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task ACorruptInboxReceiptPausesAndTheNextStartPaysExactlyOnce()
    {
        using var f = new Fixture();
        f.Start(); var report = f.Capture(f.Coordinator());
        StopPrivate(f);
        string inbox = Path.Combine(f.Metadata(""), "inbox");
        Directory.CreateDirectory(inbox);
        string conflict = Path.Combine(inbox, report.RunId + ".json");
        File.WriteAllText(conflict, "{ not a receipt");
        string profile = f.Profile;

        var paused = await PauseOf(f);

        Assert.StartsWith("offline-", paused!.Code);
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(profile, f.Profile);
        Assert.False(File.Exists(f.Metadata("acknowledged.json")));
        File.Delete(conflict);
        Assert.Null(await PauseOf(f));
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paid = f.Profile;
        await f.Pay();
        Assert.Equal(paid, f.Profile);
    }

    // ---- what still stops SPT -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALivePrivateProcessStillStopsStartup(bool realProbe)
    {
        using var f = new Fixture();
        if (realProbe)
        {
            // The real liveness check: this very test process, with its real start time.
            using var current = Process.GetCurrentProcess();
            f.Process = new(current.Id, current.StartTime.ToUniversalTime(), []);
        }
        f.Start(); StopPrivate(f);
        var before = RecordTree(f.Root);
        RewardOfflineSession.LivenessProbe = realProbe ? null : _ => true;
        try
        {
            var e = await Assert.ThrowsAsync<RewardOfflineException>(() => PauseOf(f));
            Assert.True(e.LiveWriter);
            Assert.Contains("offline-private-process-still-running", e.Message);
        }
        finally { RewardOfflineSession.LivenessProbe = null; }
        Assert.Equal(before, RecordTree(f.Root));
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Fact]
    public async Task APrivateProcessWhoseLivenessCannotBeReadStillStopsStartup()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        var before = RecordTree(f.Root);
        RewardOfflineSession.LivenessProbe = _ => throw new Win32Exception(5, "access denied");
        try
        {
            var e = await Assert.ThrowsAsync<RewardOfflineException>(() => PauseOf(f));
            Assert.True(e.LiveWriter);
            Assert.Contains("offline-private-process-unverifiable", e.Message);
        }
        finally { RewardOfflineSession.LivenessProbe = null; }
        Assert.Equal(before, RecordTree(f.Root));
    }

    [Fact]
    public async Task ALiveProcessIsNeverRetried()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        int probes = 0;
        RewardOfflineSession.LivenessProbe = _ => { probes++; return true; };
        try { await Assert.ThrowsAsync<RewardOfflineException>(() => PauseOf(f)); }
        finally { RewardOfflineSession.LivenessProbe = null; }
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task ALiveProcessStopsOnTheDiscardPathToo()
    {
        using var f = new Fixture();
        f.Start(); f.Capture(f.Coordinator());
        StopPrivate(f);
        Directory.Move(f.Private, f.Private + "-lost"); // private state lost: the discard path decides
        // The first check (before the discard decision) passes, the one inside the discard path finds the process.
        int calls = 0;
        RewardOfflineSession.LivenessProbe = _ => ++calls > 1;
        try
        {
            var e = await Assert.ThrowsAsync<RewardOfflineException>(() => PauseOf(f));
            Assert.True(e.LiveWriter);
            Assert.Equal(2, calls);
        }
        finally { RewardOfflineSession.LivenessProbe = null; }
    }

    [Fact]
    public async Task AnActivePointerThatChangesUnderBothLocksStillStopsStartup()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        var reference = RewardOfflineSession.ReadReference(f.Root, RewardOfflineSession.ActivePath(f.Root));
        var e = await Assert.ThrowsAsync<RewardOfflineException>(() => PauseOf(f, s =>
        {
            if (s == "imported") RewardRuntimeFiles.Write(f.Root, RewardOfflineSession.ActivePath(f.Root), reference with { Activated = !reference.Activated });
        }));
        Assert.True(e.LiveWriter);
        Assert.Contains("offline-owner-changed-during-recovery", e.Message);
    }

    [Fact]
    public async Task APrivateServerMarkerAppearingDuringRecoveryStillStopsStartup()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        // Without the marker the same boundary failure is a pause (profiles-loaded above); with it, another server starts here.
        await Assert.ThrowsAnyAsync<Exception>(() => PauseOf(f, s =>
        {
            if (s == "closure") File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        }));
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Fact]
    public async Task APrivateTreeThatStartsAtTheBeginningStillStopsStartup()
    {
        using var f = new Fixture();
        f.Start(); StopPrivate(f);
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        await Assert.ThrowsAnyAsync<Exception>(() => PauseOf(f, unloaded: () => false));
    }

    // Every offline-* reason the recovery or discard code can raise is classified here on purpose. A new code fails this
    // test until someone decides whether it can mean "another process is writing" (hard) or not (soft, the default).
    [Fact]
    public void EveryOfflineReasonCodeOfTheRecoveryIsClassified()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "ArenaMode.Server", "Rewards", "RewardOfflineRecovery.cs")))
            folder = Path.GetDirectoryName(folder);
        if (folder is null) return; // run from a packaged copy without the sources
        var hard = new HashSet<string> { "offline-private-process-still-running", "offline-private-process-unverifiable", "offline-owner-changed-during-recovery" };
        var soft = new HashSet<string>
        {
            "offline-recovery-lease-mismatch", "offline-activation-proof-missing", "offline-private-lease-changed", "offline-result-changed-after-closure",
            "offline-inbox-conflict", "offline-result-missing", "offline-import-readback-mismatch", "offline-initialization-proof-incomplete",
            "offline-predecessor-not-complete", "offline-history-capacity", "offline-unexpected-history-entry", "offline-history-lease-missing",
            "offline-history-binding-mismatch", "offline-completion-invalid", "offline-completion-without-closure", "offline-multiple-unclosed-sessions",
            "offline-active-history-mismatch", "offline-receipt-capacity", "offline-unexpected-receipt", "offline-unenrolled-receipt",
            "offline-receipt-name-mismatch", "offline-closure-invalid", "offline-closure-receipt-invalid", "offline-unstarted-closure-invalid",
            "offline-closure-too-large", "offline-discard-missing", "offline-activation-invalid",
            "offline-delegation-binding-missing", "offline-discard-authority-mismatch", "offline-discard-completion-invalid", "offline-discard-invalid",
            "offline-receipt-authority-mismatch", "offline-receipt-not-closed"
        };
        var found = new HashSet<string>();
        foreach (string file in new[] { "RewardOfflineRecovery.cs", "RewardOfflineDiscard.cs" })
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(Path.Combine(folder, "ArenaMode.Server", "Rewards", file)), "new Reward(?:Data|LiveOwner)Exception\\(\"(offline-[a-z-]+)\"\\)"))
                found.Add(m.Groups[1].Value);
        Assert.NotEmpty(found);
        Assert.Empty(found.Where(code => !hard.Contains(code) && !soft.Contains(code)));
    }

    // ---- entry while paused ---------------------------------------------------------------------------------------

    [Fact]
    public void APausedStartMintsNoDelegationAndTheLeaseCarriesTheReason()
    {
        using var f = new Fixture();
        var pause = new RewardPauseState("reward-admission-ArgumentException", false);
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, pause.Reason, pause));
        var lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, startupUnavailable: pause.Reason, paused: pause);
        Assert.Null(lease.Authority);
        Assert.Single(Directory.GetFiles(f.Metadata("delegations"), "*.json")); // the original only
        Assert.False(Directory.Exists(f.Metadata("offline-delegations")));
        Assert.Equal("startup:reward-admission-ArgumentException", RewardOfflineSession.ReadAuthorityUnavailable(lease));
    }

    [Fact]
    public void APauseThatLeftTheLastSessionUnsettledDeclinesTheEntryBeforeAnythingIsPaused()
    {
        using var f = new Fixture();
        var pause = new RewardPauseState("offline-consent-conflict", true);
        var e = Assert.Throws<RewardDataException>(() => RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, pause.Reason, pause));
        Assert.Equal("rewards-paused-recovery-pending", e.Message);
        Assert.Throws<RewardDataException>(() => RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, startupUnavailable: pause.Reason, paused: pause));
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.False(File.Exists(RewardOfflineSession.LeasePath(f.Root, f.Grant.SessionId)));
    }
}

public sealed class RewardPausedTextAndNoteTests
{
    [Theory]
    [InlineData("offline-activation-proof-missing", RewardUnavailableText.SessionUnsettled)]
    [InlineData("offline-recovery-IOException", RewardUnavailableText.SessionUnsettled)]
    [InlineData("native-startup-boundary-unavailable", RewardUnavailableText.ProfilesLoadedEarly)]
    [InlineData("reward-admission-ArgumentException", RewardUnavailableText.PausedAtStart)]
    public void ThePauseCodesHaveTheirOwnCardSentenceInBothSessions(string code, string sentence)
    {
        Assert.Equal(sentence, RewardUnavailableText.ForStartupReason(code));
        Assert.Equal(sentence.Replace("the server log", "the normal server's log"), RewardUnavailableText.ForLeaseReason("startup:" + code));
        Assert.True(RewardUnavailableText.IsLeaseReason("startup:" + RewardUnavailableText.LeaseCode(code)));
        Assert.EndsWith("See the server log.", sentence);
    }

    [Fact]
    public void TheNoteNamesTheServerProcessAndNeverThrows()
    {
        string root = Path.Combine(Path.GetTempPath(), "ArenaMode.PausedNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "user", "arena-rewards"));
        try
        {
            var started = DateTimeOffset.UtcNow.AddMinutes(-3);
            RewardPausedNote.Write(root, "offline-x", true, 4242, started);
            var note = JsonDocument.Parse(File.ReadAllText(RewardPausedNote.PathFor(root))).RootElement;
            Assert.Equal(4242, note.GetProperty("serverPid").GetInt32());
            Assert.Equal(started.UtcDateTime, note.GetProperty("serverStartedUtc").GetDateTime().ToUniversalTime());
            RewardPausedNote.Remove(root);
            Assert.False(File.Exists(RewardPausedNote.PathFor(root)));
            RewardPausedNote.Remove(root); // nothing to remove
            RewardPausedNote.Write(Path.Combine(root, "missing"), "offline-x", false); // the folder cannot be written: ignored
        }
        finally { Directory.Delete(root, true); }
    }
}
