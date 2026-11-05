using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// A normal start whose reward step was refused (another mod's load step before Arena's, an unreviewed server)
/// enrolls nothing, so the Arena session's lease carries no authority. Until 0.4.1 the session's reward card then
/// said "Activation pending — return to the normal session, then re-enter Arena", and following that advice looped
/// (maschine34675/ArenaMode#1). The lease now carries the reason, scoped by its origin, and the private session
/// shows the normal session's "withheld" sentence. docs/REWARDS-RUNTIME.md, "Lost reward authority".
/// </summary>
public sealed partial class RewardOfflineSessionTests
{
    private const string Refused = "earlier-startup-callback";

    [Fact]
    public void ARefusedStartIsNamedInTheLeaseAndTheSessionCardOfANeverEnrolledProfile()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);   // the player wants rewards; the refused start enrolled nothing
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, Refused));
        f.Lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused);
        Assert.Null(f.Lease.Authority);
        Assert.Equal(RewardUnavailableText.StartupScope + Refused, f.Lease.AuthorityUnavailable);
        Assert.Contains("\"authorityUnavailable\":\"startup:earlier-startup-callback\"", RewardRuntimeFiles.Json(f.Lease));
        // An identical retry (lost reply) finds the same lease, reason included, and so does a retry that no longer knows it.
        Assert.Equal(RewardRuntimeFiles.Json(f.Lease), RewardRuntimeFiles.Json(RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused)));
        Assert.Equal(RewardRuntimeFiles.Json(f.Lease), RewardRuntimeFiles.Json(RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request)));

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
    public void WithoutARefusalANeverEnrolledProfileStaysPendingAndTheLeaseHasNoReasonField()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        var lease = f.Ready();
        Assert.Null(lease.Authority);
        Assert.Null(lease.AuthorityUnavailable);
        // Absent when null: a lease without a reason is byte-identical to one written before the field existed, so
        // its hash and the signed state that carries it do not change.
        string json = File.ReadAllText(RewardOfflineSession.LeasePath(f.Root, f.Grant.SessionId));
        Assert.DoesNotContain("authorityUnavailable", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RewardRuntimeFiles.Json(lease), RewardRuntimeFiles.Json(RewardOfflineSession.ReadLease(f.Root, RewardOfflineSession.LeasePath(f.Root, f.Grant.SessionId))));
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
        Assert.Equal(RewardUnavailableText.EntryScope + why, f.Lease.AuthorityUnavailable);
        f.Start();
        var status = f.Owner!.Status();
        Assert.Equal("withheld", status.State);
        Assert.Equal(RewardUnavailableText.ForLeaseReason(RewardUnavailableText.EntryScope + why), status.Detail);
        Assert.StartsWith("Rewards are off for this profile: its reward records no longer match", status.Detail);
        Assert.EndsWith("See the normal server's log.", status.Detail);
    }

    [Fact]
    public void AHealthyAuthorityIsDelegatedDespiteTheStartsRefusal()
    {
        // The enrollment of an earlier, admitted start still delegates: the session earns, and its receipts wait
        // for the next start whose reward step runs.
        using var f = new Fixture();
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, Refused));
        var lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, Refused);
        Assert.NotNull(lease.Authority);
        Assert.Null(lease.AuthorityUnavailable);
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
    public void TheLeaseAcceptsOnlyAScopedReasonCode(string value, bool valid)
    {
        using var f = new Fixture(enroll: false);
        var lease = f.Ready() with { AuthorityUnavailable = value };
        if (valid) RewardOfflineSession.Validate(lease);
        else Assert.Equal("offline-lease-invalid", Assert.Throws<RewardDataException>(() => RewardOfflineSession.Validate(lease)).Message);
        Assert.True(RewardUnavailableText.IsLeaseReason(RewardUnavailableText.EntryScope + new string('a', 120)));
        Assert.False(RewardUnavailableText.IsLeaseReason(RewardUnavailableText.EntryScope + new string('a', 121)));
    }

    [Fact]
    public void AReasonNextToAnAuthorityIsInvalid()
    {
        using var f = new Fixture();
        var lease = f.Ready() with { AuthorityUnavailable = RewardUnavailableText.StartupScope + Refused };
        Assert.Equal("offline-lease-invalid", Assert.Throws<RewardDataException>(() => RewardOfflineSession.Validate(lease)).Message);
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

    // A refusal recorded as a wrapped RewardDataException ("cola-unpaid-plan-invalid: IOException") must reach the lease as
    // a code, not make the lease invalid: Validate runs inside the entry preflight, where that would decline the entry.
    [Fact]
    public void AReasonThatDoesNotFitTheLeaseIsCutToACodeInsteadOfDecliningTheEntry()
    {
        using var f = new Fixture(enroll: false);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        const string refused = "cola-unpaid-plan-invalid: IOException";
        Assert.Null(RewardOfflineSession.PreflightReady(f.Root, f.Grant, f.Request, refused));
        var lease = RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request, null, refused);
        Assert.Equal("startup:cola-unpaid-plan-invalid", lease.AuthorityUnavailable);
        Assert.Equal("unknown", RewardUnavailableText.LeaseCode(": nothing usable"));
        Assert.Equal("unknown", RewardUnavailableText.LeaseCode(""));
        Assert.Equal(new string('a', 120), RewardUnavailableText.LeaseCode(new string('a', 300)));
        Assert.Equal("IOException", RewardUnavailableText.LeaseCode("IOException"));
        f.Start();
        var status = f.Owner!.Status();
        Assert.Equal("withheld", status.State);
        Assert.Equal(RewardUnavailableText.Temporary, status.Detail);   // an unknown code is not claimed to recur
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
