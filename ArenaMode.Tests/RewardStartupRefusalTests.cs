using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// A normal start whose reward step was refused (another mod's load step before Arena's, an unreviewed server)
/// enrolls nothing, so the Arena session's lease carries no authority. Until 0.4.1 the session's reward card then
/// said "Activation pending — return to the normal session, then re-enter Arena", and following that advice looped
/// (maschine34675/ArenaMode#1). The normal server now records the reason, scoped by its origin, in a file of its own
/// next to the lease (never in the lease: the session helper parses that with a fixed member count), and the private
/// session shows the normal session's "withheld" sentence. docs/REWARDS-RUNTIME.md, "Lost reward authority".
/// </summary>
public sealed partial class RewardOfflineSessionTests
{
    private const string Refused = "earlier-startup-callback";

    private static string ReasonPath(Fixture f) => RewardOfflineSession.AuthorityUnavailablePath(f.Root, f.Grant.SessionId);

    [Fact]
    public void ARefusedStartIsRecordedNextToTheLeaseAndShownOnTheSessionCardOfANeverEnrolledProfile()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);   // the player wants rewards; the refused start enrolled nothing
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, Refused));
        Assert.False(File.Exists(ReasonPath(f)));                // the preflight writes nothing
        f.Lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused);
        Assert.Null(f.Lease.Authority);
        Assert.Equal(RewardUnavailableText.StartupScope + Refused, RewardOfflineSession.ReadAuthorityUnavailable(f.Lease));
        string recorded = File.ReadAllText(ReasonPath(f));
        Assert.Contains("\"reason\":\"startup:earlier-startup-callback\"", recorded);
        // The lease itself is what every helper and server release knows: its 17 members, nothing of the reason.
        string lease = File.ReadAllText(RewardOfflineSession.LeasePath(f.Root, f.Grant.SessionId));
        Assert.DoesNotContain("authorityUnavailable", lease, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("startup:", lease);
        // An identical retry (lost reply) finds the same lease and the same record, whether or not it still knows the reason.
        Assert.Equal(RewardRuntimeFiles.Json(f.Lease), RewardRuntimeFiles.Json(RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused)));
        Assert.Equal(RewardRuntimeFiles.Json(f.Lease), RewardRuntimeFiles.Json(RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request)));
        Assert.Equal(recorded, File.ReadAllText(ReasonPath(f)));

        f.Start();
        var status = f.Owner!.Status();
        Assert.True(status.Enabled);
        Assert.False(status.Enrolled);
        Assert.True(status.CanChange);
        Assert.Equal("withheld", status.State);
        // The normal session's sentence, pointing at the normal server's log (the private server's holds nothing about it).
        Assert.Equal("Rewards are off: another mod's startup step runs before Arena's reward step. See the normal server's log.", status.Detail);
        Assert.Equal(RewardUnavailableText.ForLeaseReason(RewardUnavailableText.StartupScope + Refused), status.Detail);
        // Switched off inside the session the card says "off"; switched on again, the reason is back.
        var off = f.Owner.SetConsent(Fixture.Id, false, status.ConsentRevision);
        Assert.Equal("disabled", off.State);
        var on = f.Owner.SetConsent(Fixture.Id, true, off.ConsentRevision);
        Assert.Equal("withheld", on.State);
        Assert.Equal(status.Detail, on.Detail);
    }

    [Fact]
    public void WithoutARefusalANeverEnrolledProfileStaysPendingAndNoReasonIsRecorded()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        var lease = f.Ready();
        Assert.Null(lease.Authority);
        Assert.False(File.Exists(ReasonPath(f)));
        Assert.Null(RewardOfflineSession.ReadAuthorityUnavailable(lease));
        f.Start();
        var status = f.Owner!.Status();
        Assert.Equal("restart-required", status.State);
        Assert.StartsWith("Rewards will activate", status.Detail);
    }

    [Theory]
    [InlineData("wiped-profile")]
    [InlineData("moved-install")]
    [InlineData("damaged-enrollment")]
    public void AProfileWhoseRecordsNoLongerMatchNamesThatBeforeAnyRefusedStart(string variant)
    {
        using var f = new Fixture();
        Break(f, variant);
        string why = RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, Refused)!;
        Assert.False(string.IsNullOrEmpty(why));
        f.Lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused);
        Assert.Null(f.Lease.Authority);
        Assert.Equal(RewardUnavailableText.EntryScope + why, RewardOfflineSession.ReadAuthorityUnavailable(f.Lease));
        f.Start();
        var status = f.Owner!.Status();
        Assert.Equal("withheld", status.State);
        Assert.Equal(RewardUnavailableText.ForLeaseReason(RewardUnavailableText.EntryScope + why), status.Detail);
        Assert.StartsWith("Rewards are off for this profile: its reward records no longer match", status.Detail);
        Assert.EndsWith("See the normal server's log.", status.Detail);
    }

    [Fact]
    public void AHealthyAuthorityIsDelegatedDespiteTheStartsRefusalAndNoReasonIsRecorded()
    {
        // The enrollment of an earlier, admitted start still delegates: the session earns, and its receipts wait
        // for the next start whose reward step runs.
        using var f = new Fixture();
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, Refused));
        var lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused);
        Assert.NotNull(lease.Authority);
        Assert.False(File.Exists(ReasonPath(f)));
        f.Start();
        Assert.Equal("ready", f.Owner!.Status().State);
    }

    // A refusal recorded as a wrapped RewardDataException ("cola-unpaid-plan-invalid: IOException") reaches the record as
    // a code: the record's shape is checked by the reader, and a reason that does not fit must not cost the session.
    [Fact]
    public void AReasonThatDoesNotFitIsCutToACode()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        const string refused = "cola-unpaid-plan-invalid: IOException";
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, refused));
        var lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, refused);
        Assert.Equal("startup:cola-unpaid-plan-invalid", RewardOfflineSession.ReadAuthorityUnavailable(lease));
        Assert.Equal("unknown", RewardUnavailableText.LeaseCode(": nothing usable"));
        Assert.Equal("unknown", RewardUnavailableText.LeaseCode(""));
        Assert.Equal(new string('a', 120), RewardUnavailableText.LeaseCode(new string('a', 300)));
        Assert.Equal("IOException", RewardUnavailableText.LeaseCode("IOException"));
        f.Start();
        var status = f.Owner!.Status();
        Assert.Equal("withheld", status.State);
        Assert.Equal(RewardUnavailableText.Temporary, status.Detail);   // an unknown code is not claimed to recur
    }

    [Theory]
    [InlineData("other-session")]
    [InlineData("other-schema")]
    [InlineData("bad-shape")]
    [InlineData("no-scope")]
    [InlineData("damaged")]
    [InlineData("oversized")]
    public void ARecordThatIsNotThisSessionsIsIgnoredAndTheCardStaysPending(string variant)
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        var lease = f.Ready();
        string text = variant switch
        {
            "other-session" => RewardRuntimeFiles.Json(new RewardOfflineAuthorityUnavailable(1, Guid.NewGuid().ToString("N"), "startup:" + Refused)),
            "other-schema" => RewardRuntimeFiles.Json(new RewardOfflineAuthorityUnavailable(2, f.Grant.SessionId, "startup:" + Refused)),
            "bad-shape" => RewardRuntimeFiles.Json(new RewardOfflineAuthorityUnavailable(1, f.Grant.SessionId, "startup:<b>bold</b>")),
            "no-scope" => RewardRuntimeFiles.Json(new RewardOfflineAuthorityUnavailable(1, f.Grant.SessionId, Refused)),
            "damaged" => "{",
            "oversized" => "{\"pad\":\"" + new string('x', 8000) + "\"}",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        File.WriteAllText(ReasonPath(f), text);
        Assert.Null(RewardOfflineSession.ReadAuthorityUnavailable(lease));
        f.Start();
        Assert.Equal("restart-required", f.Owner!.Status().State);
    }

    [Fact]
    public void ARecordNextToAnAuthorityChangesNothing()
    {
        using var f = new Fixture();
        var lease = f.Ready();
        Assert.NotNull(lease.Authority);
        File.WriteAllText(ReasonPath(f), RewardRuntimeFiles.Json(new RewardOfflineAuthorityUnavailable(1, f.Grant.SessionId, "startup:" + Refused)));
        f.Start();
        Assert.Equal("ready", f.Owner!.Status().State);
    }

    [Theory]
    [InlineData("startup:earlier-startup-callback", true)]
    [InlineData("startup:IOException", true)]
    [InlineData("entry:runtime-enrollment-origin-mismatch", true)]
    [InlineData("entry:JsonException", true)]
    [InlineData("earlier-startup-callback", false)]
    [InlineData("startup:", false)]
    [InlineData("other:earlier-startup-callback", false)]
    [InlineData("startup:has space", false)]
    [InlineData("startup:<b>bold</b>", false)]
    [InlineData("startup:ärger", false)]
    [InlineData("entry:cola-unpaid-plan-invalid: IOException", false)]
    public void OnlyAScopedReasonCodeIsARecordedReason(string value, bool valid)
    {
        Assert.Equal(valid, RewardUnavailableText.IsLeaseReason(value));
        Assert.True(RewardUnavailableText.IsLeaseReason(RewardUnavailableText.EntryScope + new string('a', 120)));
        Assert.False(RewardUnavailableText.IsLeaseReason(RewardUnavailableText.EntryScope + new string('a', 121)));
    }

    [Fact]
    public void TheReasonIsScopedByItsOriginAndTheEntrysOwnReadComesFirst()
    {
        Assert.Null(RewardOfflineSession.ScopedReason(null, null, null));
        Assert.Equal("startup:" + Refused, RewardOfflineSession.ScopedReason(null, null, Refused));
        Assert.Equal("entry:x", RewardOfflineSession.ScopedReason(null, "x", null));
        Assert.Equal("entry:x", RewardOfflineSession.ScopedReason(null, "x", Refused));
        using var f = new Fixture();
        Assert.Null(RewardOfflineSession.ScopedReason(f.Ready().Authority, "x", Refused));
    }

    // The entry's read can fail on a file a scanner holds for a moment after the stop; that is not a mismatch of the records.
    [Theory]
    [InlineData("entry:IOException", RewardUnavailableText.Temporary)]
    [InlineData("entry:UnauthorizedAccessException", RewardUnavailableText.Temporary)]
    [InlineData("entry:JsonException", "Rewards are off for this profile: its reward records no longer match it or this installation. See the normal server's log.")]
    [InlineData("entry:runtime-enrollment-origin-mismatch", "Rewards are off for this profile: its reward records no longer match it or this installation. See the normal server's log.")]
    [InlineData("startup:earlier-startup-callback", "Rewards are off: another mod's startup step runs before Arena's reward step. See the normal server's log.")]
    [InlineData("startup:reward-storage-linked", "Rewards are off: this installation's reward folder cannot be used. See the normal server's log.")]
    [InlineData("startup:another-server-or-unknown-process", RewardUnavailableText.Temporary)]
    public void TheSessionCardPointsAtTheNormalServersLogAndDoesNotCallAFileLockAMismatch(string scoped, string sentence)
        => Assert.Equal(sentence, RewardUnavailableText.ForLeaseReason(scoped));
}
