using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// Reward authority that no longer matches (profile wipe, restored backup, editor, moved or copied install):
/// Arena entry must not depend on it, the return must not stop the normal server, and startup re-establishes
/// it only when nothing in the old records is unsettled. docs/REWARDS-RUNTIME.md, "Lost reward authority".
/// </summary>
public sealed partial class RewardOfflineSessionTests
{
    private static void Wipe(Fixture f)
    {
        var profile = JsonNode.Parse(f.Profile)!.AsObject();
        Assert.True(profile.Remove(RewardProfileDocument.ExtensionKey));
        File.WriteAllText(f.ProfilePath, profile.ToJsonString());
    }

    private static void MoveInstall(Fixture f)
    {
        string path = f.Metadata("enrollment.json");
        var enrollment = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string name = enrollment.Select(p => p.Key).Single(k => string.Equals(k, "runtimeRoot", StringComparison.OrdinalIgnoreCase));
        enrollment[name] = Path.Combine(Path.GetTempPath(), "ArenaMode.Moved.From", Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, enrollment.ToJsonString());
    }

    private static SortedDictionary<string, string> Tree(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            result[Path.GetRelativePath(root, file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        return result;
    }

    private static string RewardsFolder(Fixture f) => Path.Combine(f.Root, "user", "arena-rewards");
    private static string[] Archives(Fixture f) => Directory.GetDirectories(RewardsFolder(f), Fixture.Id + ".archived-*");

    private static void Break(Fixture f, string variant)
    {
        switch (variant)
        {
            case "wiped-profile": Wipe(f); break;
            case "moved-install": MoveInstall(f); break;
            case "consent-file-gone": File.Delete(f.Metadata("consent.json")); break;
            case "damaged-enrollment": File.WriteAllText(f.Metadata("enrollment.json"), "{"); break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    // ------------------------------------------------------------------ entry

    [Theory]
    [InlineData("wiped-profile")]
    [InlineData("moved-install")]
    [InlineData("consent-file-gone")]
    [InlineData("damaged-enrollment")]
    public void EntryDoesNotDependOnTheRewardAuthority(string variant)
    {
        using var f = new Fixture();
        Break(f, variant);
        string profile = f.Profile;

        // The shutdown listener asks before it pauses anything, and is told why rewards are unavailable.
        Assert.False(string.IsNullOrEmpty(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request)));
        Assert.False(Directory.Exists(RewardOfflineSession.SessionDirectory(f.Root, f.Grant.SessionId)));

        var lease = f.Ready();
        Assert.Null(lease.Authority);
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(profile, f.Profile);
        Assert.Single(Directory.GetFiles(f.Metadata("delegations"), "*.json")); // the original only, nothing delegated
        Assert.False(Directory.Exists(f.Metadata("offline-delegations")));
        // An identical retry (lost reply) finds the same lease.
        Assert.Equal(RewardRuntimeFiles.Json(lease), RewardRuntimeFiles.Json(f.Ready()));
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request));
    }

    [Fact]
    public void AHealthyAuthorityIsStillDelegatedAndPreflightWritesNothing()
    {
        using var f = new Fixture();
        var before = Tree(f.Root);
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request));
        Assert.Equal(before, Tree(f.Root));
        Assert.NotNull(f.Ready().Authority);
    }

    [Fact]
    public void PreflightRefusesWhatPublicationWouldRefuseAndWritesNothing()
    {
        using var f = new Fixture();
        f.Ready();
        var before = Tree(f.Root);
        string session = Guid.NewGuid().ToString("N");
        var secondGrant = f.Grant with { SessionId = session };
        var secondRequest = f.Request with { SessionId = session };
        Assert.Equal("offline-session-already-active",
            Assert.Throws<RewardDataException>(() => RewardOfflineSession.PreflightReady(f.Root, secondGrant, secondRequest)).Message);
        Assert.Equal("offline-entry-mismatch",
            Assert.Throws<RewardDataException>(() => RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request with { Token = "other" })).Message);
        Assert.Equal(before, Tree(f.Root));
        Assert.False(Directory.Exists(RewardOfflineSession.SessionDirectory(f.Root, session)));
    }

    [Fact]
    public void AnUnreadableConsentChoiceIsRefusedBeforeAnythingIsPaused()
    {
        // The return path rewrites this file and refuses to go on over a damaged one, so entry declines
        // up front instead of discovering it after the server cannot resume any more.
        using var f = new Fixture();
        File.WriteAllText(f.Metadata("consent.json"), "{");
        Assert.ThrowsAny<Exception>(() => RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request));
    }

    [Theory]
    [InlineData("wiped-profile")]
    [InlineData("moved-install")]
    public void ReturningFromASessionThatRanWithoutAuthorityDoesNotStopTheNormalServer(string variant)
    {
        using var f = new Fixture();
        Break(f, variant);
        f.Start();
        Assert.Null(f.Lease!.Authority);
        f.StopAndRecover(); // used to end in RewardOfflineException for a moved install: no handoff for a dead enrollment
        Assert.Equal(new RewardConsent(1, true, f.Lease.ConsentRevision), RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
        Assert.False(RewardOfflineSession.HasActive(f.Root));
    }

    // ------------------------------------------------------------------ startup recovery

    [Fact]
    public async Task AWipedProfileWithSettledHistoryIsArchivedAndEnrolledAgainWithAFreshEpoch()
    {
        using var f = new Fixture();
        var old = f.Original.Binding;
        f.Start(); f.Capture(f.Coordinator()); f.StopAndRecover();
        Assert.Equal(1000, (await f.Pay()).Roubles);
        var consent = RewardEnrollment.ReadConsent(f.Root, Fixture.Id);
        string snapshots = Path.Combine(f.Root, "user", "arena-reward-snapshots", Fixture.Id);
        Assert.True(Directory.Exists(snapshots)); // the old epoch's restoration evidence
        Wipe(f);

        var outcome = await f.Pay();

        Assert.True(outcome.Authority!.Recovered);
        Assert.Equal("profile-reward-block-missing", outcome.Authority.Trigger);
        Assert.Equal("rewards-enrolled-no-pending-result", outcome.Result.Reason);
        var fresh = RewardEnrollment.Read(f.Root, Fixture.Id).Binding;
        Assert.NotEqual(old.Epoch, fresh.Epoch);
        Assert.NotEqual(old.InstallationId, fresh.InstallationId);
        Assert.Empty(RewardProfileDocument.ReadLedger(f.Profile, fresh, new()));
        Assert.Equal(consent, RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
        // The old records are kept, not deleted: enrollment, journal with the paid receipt, all of it.
        string archive = Assert.Single(Archives(f));
        Assert.Equal(outcome.Authority.Archive, "user/arena-rewards/" + Path.GetFileName(archive));
        Assert.Equal(old, RewardStartupFiles.Read<RewardStartupEnrollment>(Path.Combine(archive, "enrollment.json")).Binding);
        var archived = RewardSettlementCore.ParseJournal(File.ReadAllText(Path.Combine(archive, "journal.json")), old, new());
        Assert.Equal(RewardJournalPhase.Committed, Assert.Single(archived.Entries).Phase);
        // Evidence tied to the old epoch's delegations moves with it; left behind it would quarantine the new one.
        Assert.False(Directory.Exists(snapshots));
        Assert.True(Directory.Exists(Path.Combine(archive, "evidence", "arena-reward-snapshots")));
        // Nothing is paid again, now or later.
        string profile = f.Profile;
        Assert.Equal("rewards-enrolled-no-pending-result", (await f.Pay()).Result.Reason);
        Assert.Equal(profile, f.Profile);
        Assert.Single(Archives(f));
    }

    [Fact]
    public async Task AnOldReceiptCannotBePaidIntoTheFreshEpoch()
    {
        using var f = new Fixture();
        f.Start(); var report = f.Capture(f.Coordinator()); f.StopAndRecover();
        string inbox = f.Metadata("inbox/" + report.RunId + ".json");
        string receipt = File.ReadAllText(inbox);
        Assert.Equal(1000, (await f.Pay()).Roubles);
        Wipe(f);
        Assert.True((await f.Pay()).Authority!.Recovered);
        string profile = f.Profile;

        // A copy of the old signed receipt (or a copy of an install that still has it) lands in the new inbox.
        Directory.CreateDirectory(f.Metadata("inbox"));
        File.WriteAllText(inbox, receipt);
        var outcome = await f.Pay();

        Assert.NotEqual(RewardSettlementStatus.Paid, outcome.Result.Status);
        Assert.Equal(profile, f.Profile);
        Assert.Empty(RewardProfileDocument.ReadLedger(f.Profile, RewardEnrollment.Read(f.Root, Fixture.Id).Binding, new()));
    }

    [Fact]
    public async Task AnOptedOutPlayerStaysOptedOutAcrossRecovery()
    {
        using var f = new Fixture();
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, false);
        var consent = RewardEnrollment.ReadConsent(f.Root, Fixture.Id);
        Assert.False(consent.Enabled);
        Wipe(f);
        string wiped = f.Profile;

        var outcome = await f.Pay();

        Assert.True(outcome.Authority!.Recovered);
        Assert.Equal("rewards-not-enabled", outcome.Result.Reason);
        Assert.Equal(consent, RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
        Assert.False(File.Exists(f.Metadata("enrollment.json")));
        Assert.Equal(wiped, f.Profile); // no enrollment, so no block and no profile write
        Assert.Equal("rewards-not-enabled", (await f.Pay()).Result.Reason);
        // Opting in again enrolls with the fresh epoch at the next start.
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        Assert.Equal("rewards-enrolled-no-pending-result", (await f.Pay()).Result.Reason);
        Assert.NotNull(RewardEnrollment.Read(f.Root, Fixture.Id));
    }

    [Fact]
    public async Task AMovedInstallKeepsItsPaidHistoryInTheArchiveAndEnrollsAgain()
    {
        using var f = new Fixture();
        var old = f.Original.Binding;
        f.Start(); f.Capture(f.Coordinator()); f.StopAndRecover();
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paidProfile = f.Profile;
        MoveInstall(f);

        var outcome = await f.Pay();

        Assert.True(outcome.Authority!.Recovered);
        Assert.Equal("install-root-changed", outcome.Authority.Trigger);
        var fresh = RewardEnrollment.Read(f.Root, Fixture.Id);
        Assert.NotEqual(old.Epoch, fresh.Binding.Epoch);
        Assert.Empty(RewardProfileDocument.ReadLedger(f.Profile, fresh.Binding, new()));
        // The ledger block that left the profile is preserved byte for byte beside the old records.
        string archive = Assert.Single(Archives(f));
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(archive, RewardAuthorityRecovery.ArchivedLedgerFile)))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(paidProfile)![RewardProfileDocument.ExtensionKey], saved));
        // Everything else in the profile, including the paid mail, is untouched.
        var before = JsonNode.Parse(paidProfile)!.AsObject(); before.Remove(RewardProfileDocument.ExtensionKey);
        var after = JsonNode.Parse(f.Profile)!.AsObject(); after.Remove(RewardProfileDocument.ExtensionKey);
        Assert.True(JsonNode.DeepEquals(before, after));
    }

    [Theory]
    [InlineData("uncommitted-payment")]
    [InlineData("unimported-receipt")]
    [InlineData("pending-run")]
    [InlineData("unrecognised-file")]
    public async Task UnsettledRecordsKeepTheQuarantineAndChangeNothing(string variant)
    {
        using var f = new Fixture();
        var binding = f.Original.Binding;
        switch (variant)
        {
            case "uncommitted-payment":
                string key = Guid.NewGuid().ToString("N") + ":1";
                var payment = new RewardPayment(key, new string('a', 64), 1000, RewardIdentity.MessageId(binding, key), 1_700_000_000);
                File.WriteAllText(f.Metadata("journal.json"), RewardRuntimeFiles.Json(new RewardJournal(1, binding,
                    [new RewardJournalEntry(payment, RewardJournalPhase.Attempting, new string('b', 64))])));
                break;
            case "unimported-receipt":
                Directory.CreateDirectory(f.Metadata("inbox"));
                File.WriteAllText(f.Metadata("inbox/" + Guid.NewGuid().ToString("N") + ".json"), "{}");
                break;
            case "pending-run":
                string runs = Path.Combine(f.Root, "user", "arena-reward-runs", Fixture.Id);
                Directory.CreateDirectory(runs);
                string run = Guid.NewGuid().ToString("N");
                File.WriteAllText(Path.Combine(runs, run + ".json"),
                    JsonSerializer.Serialize(new { schema = 1, armToken = Guid.NewGuid().ToString("N"), runId = run }));
                break;
            case "unrecognised-file":
                File.WriteAllText(f.Metadata("notes.txt"), "kept by the player");
                break;
        }
        Wipe(f);
        var tree = Tree(f.Root);

        var outcome = await f.Pay();

        Assert.False(outcome.Authority!.Recovered);
        Assert.False(string.IsNullOrEmpty(outcome.Authority.Detail));
        Assert.Equal(RewardSettlementStatus.Quarantined, outcome.Result.Status);
        Assert.Equal(tree, Tree(f.Root)); // not one byte of the old records or the profile moved
        Assert.Empty(Archives(f));
        // The menu says what is wrong instead of "temporarily unavailable".
        var coordinator = new RewardRuntimeCoordinator(f.Root);
        coordinator.ObserveStartupOutcome(outcome);
        var status = coordinator.GetStatus(Fixture.Id);
        Assert.Equal("withheld", status.State);
        Assert.Contains("no longer match", status.Detail);
        Assert.DoesNotContain("temporarily", status.Detail);
    }

    [Fact]
    public async Task ARestoredOlderLedgerBehindTheJournalIsNeverRepaidOrReplaced()
    {
        using var f = new Fixture();
        string older = f.Profile; // the block as enrolled, before any payment
        f.Start(); f.Capture(f.Coordinator()); f.StopAndRecover();
        Assert.Equal(1000, (await f.Pay()).Roubles);

        // Same install: recovery stays out of it and the existing divergence handling never pays it again
        // (it closes the payment missing from the older ledger without payment).
        File.WriteAllText(f.ProfilePath, older);
        var same = await f.Pay();
        Assert.Null(same.Authority);
        Assert.NotEqual(RewardSettlementStatus.Paid, same.Result.Status);
        Assert.Empty(RewardProfileDocument.ReadLedger(f.Profile, f.Original.Binding, new()));
        Assert.Empty(Archives(f));

        // Moved install with that older block: ledger and journal disagree, so nothing is archived or replaced.
        MoveInstall(f);
        var tree = Tree(f.Root);
        var moved = await f.Pay();
        Assert.False(moved.Authority!.Recovered);
        Assert.Contains("disagree", moved.Authority.Detail);
        Assert.Equal(tree, Tree(f.Root));
        Assert.Empty(Archives(f));
    }

    [Fact]
    public async Task ADifferentLedgerInTheProfileIsNotThisRecoverysBusiness()
    {
        using var f = new Fixture();
        // A profile carrying another epoch's block (backup from before an earlier recovery) is left to the quarantine.
        var profile = JsonNode.Parse(f.Profile)!.AsObject();
        profile[RewardProfileDocument.ExtensionKey]!["binding"]!["epoch"] = Guid.NewGuid().ToString("N");
        File.WriteAllText(f.ProfilePath, profile.ToJsonString());
        MoveInstall(f);
        var tree = Tree(f.Root);
        var outcome = await f.Pay();
        Assert.False(outcome.Authority!.Recovered);
        Assert.Equal(tree, Tree(f.Root));
    }

    [Fact]
    public async Task ACrashBetweenTheTwoRenamesCarriesTheConsentChoiceAcross()
    {
        using var f = new Fixture();
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, false);
        var consent = RewardEnrollment.ReadConsent(f.Root, Fixture.Id);
        Wipe(f);
        // The state a crash leaves after "old directory renamed to the archive": only the staged choice remains.
        string directory = Path.GetDirectoryName(f.Metadata("consent.json"))!, suffix = "20261029T000000Z-deadbeef";
        Directory.Move(directory, directory + ".archived-" + suffix);
        Directory.CreateDirectory(directory + ".reenroll-" + suffix);
        File.WriteAllText(Path.Combine(directory + ".reenroll-" + suffix, "consent.json"), RewardRuntimeFiles.Json(consent));

        var outcome = await f.Pay();

        // Without finishing it, the missing directory would read as a new profile and default to ON.
        Assert.Equal("rewards-not-enabled", outcome.Result.Reason);
        Assert.Equal(consent, RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
        Assert.False(Directory.Exists(directory + ".reenroll-" + suffix));
        Assert.True(Directory.Exists(directory + ".archived-" + suffix));
    }

    [Fact]
    public async Task AHealthyEnrollmentIsLeftExactlyAsItIs()
    {
        using var f = new Fixture();
        var before = Tree(f.Root);
        string profile = f.Profile;
        var outcome = await f.Pay();
        Assert.Null(outcome.Authority);
        Assert.Equal(before, Tree(f.Root));
        Assert.Equal(profile, f.Profile);
    }
}
