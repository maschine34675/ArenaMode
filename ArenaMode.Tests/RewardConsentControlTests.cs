using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>Only generated profiles/certificates and an in-process ordinary-server transport are used.</summary>
public sealed class RewardConsentControlTests
{
    [Fact]
    public async Task FirstPrivateOptInAndOptOutOnlyChangeOrdinaryConsentMetadata()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var p = f.Private(grant);
        string normal = f.NormalProfile, local = f.PrivateProfile;
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.True(p.GetStatus(Fixture.Id).CanChange);
        var enabled = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        Assert.True(enabled.PrivateSession);
        Assert.True(enabled.Enabled);
        Assert.False(enabled.Enrolled);
        Assert.Equal("restart-required", enabled.State);
        Assert.True(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
        Assert.False(File.Exists(f.Metadata("enrollment.json")));
        Assert.False(File.Exists(f.Metadata("journal.json")));
        Assert.False(File.Exists(f.PrivateOrigin));
        Assert.Equal(normal, f.NormalProfile);
        Assert.Equal(local, f.PrivateProfile);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.False((await p.SetConsentAsync(Fixture.Id, false, CancellationToken.None)).Enabled);
        Assert.False(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
        Assert.Equal(normal, f.NormalProfile);
        Assert.Equal(local, f.PrivateProfile);
        Assert.False(Directory.Exists(Path.Combine(f.PrivateRoot, "user", "arena-rewards")));
    }

    [Fact]
    public async Task FirstOptInStillNeedsStartupAndFreshPrivateEntry()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var p = f.Private(grant);
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        string privateBefore = f.PrivateProfile;
        var outcome = await f.Process();
        Assert.Equal("rewards-enrolled-no-pending-result", outcome.Result.Reason);
        Assert.True(File.Exists(f.Metadata("enrollment.json")));
        Assert.Equal(privateBefore, f.PrivateProfile);
        f.RestartNormal();
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        var next = f.Private(f.Prepare());
        await next.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.True(next.GetStatus(Fixture.Id).Enrolled);
        Assert.True(next.GetStatus(Fixture.Id).Enabled);
    }

    [Fact]
    public async Task EnrolledPrivateEntryCanEnableFromOffThenDisableWithoutReplacingProfilesOrReceiptKey()
    {
        using var f = new Fixture(enroll: true);
        f.Normal.SetConsent(Fixture.Id, false);
        var p = f.Private(f.Prepare());
        Assert.False(File.Exists(f.PrivateOrigin));
        string normal = f.NormalProfile, local = f.PrivateProfile;
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Enabled);
        var status = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        Assert.True(status.Enabled);
        Assert.True(status.Enrolled);
        string origin = File.ReadAllText(f.PrivateOrigin);
        Assert.Equal(RewardEnrollment.Read(f.NormalRoot, Fixture.Id).Binding,
            RewardRuntimeFiles.ReadHandoff(f.PrivateRoot, f.PrivateOrigin).Binding);
        var report = f.Capture(p);
        f.Restore(p, report);
        string receiptPath = Path.Combine(f.PrivateRoot, "user", "arena-rewards-outbox", report.RunId + ".json");
        string receipt = File.ReadAllText(receiptPath);
        await p.SetConsentAsync(Fixture.Id, false, CancellationToken.None);
        Assert.False(p.GetStatus(Fixture.Id).Enabled);
        Assert.Equal(origin, File.ReadAllText(f.PrivateOrigin));
        Assert.Equal(receipt, File.ReadAllText(receiptPath));
        p.ObserveSnapshot(Fixture.Id, local);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.Equal(normal, f.NormalProfile);
        Assert.Equal(local, f.PrivateProfile);
        // The already earned receipt is still valid after switching off and on.
        await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        Directory.CreateDirectory(f.Metadata("inbox"));
        File.Copy(receiptPath, f.Metadata("inbox/" + report.RunId + ".json"));
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(1000, f.Normal.GetStatus(Fixture.Id).LastPaidRoubles);
        Assert.Equal(local, f.PrivateProfile);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("malformed-token")]
    [InlineData("short-token")]
    [InlineData("profile")]
    [InlineData("control-id")]
    [InlineData("schema")]
    [InlineData("missing-revision")]
    [InlineData("negative-revision")]
    public void UnauthenticatedOrStaleChangesDoNotWriteConsent(string problem)
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var request = f.Request(grant, true);
        string id = Fixture.Id;
        request = problem switch
        {
            "token" => request with { Token = Convert.ToBase64String(new byte[32]) },
            "malformed-token" => request with { Token = "not base64" },
            "short-token" => request with { Token = "AA==" },
            "control-id" => request with { ControlId = Guid.NewGuid().ToString("N") },
            "schema" => request with { Schema = 2 },
            "missing-revision" => request with { ExpectedRevision = null },
            "negative-revision" => request with { ExpectedRevision = -1 },
            _ => request
        };
        if (problem == "profile") id = "ffffffffffffffffffffffff";
        var before = f.Files(f.NormalRoot);
        var response = f.Normal.ExchangeControl(id, request, canChange: true);
        Assert.False(response.Accepted);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public void NormalConsentChangesInvalidateAQueuedPrivateWriteEvenAfterAnAbaToggle()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var oldEnable = f.Request(grant, true);
        f.Normal.SetConsent(Fixture.Id, true);
        f.Normal.SetConsent(Fixture.Id, false);
        long revision = RewardEnrollment.ReadConsent(f.NormalRoot, Fixture.Id).Revision;
        Assert.True(revision > oldEnable.ExpectedRevision);
        var before = f.Files(f.NormalRoot);
        var rejected = f.Normal.ExchangeControl(Fixture.Id, oldEnable, true);
        Assert.False(rejected.Accepted);
        Assert.Equal("stale-consent", rejected.Reason);
        Assert.False(rejected.Status!.Enabled);
        Assert.Equal(revision, rejected.Revision);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public void DuplicateWriteCannotAdvanceConsentTwice()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var request = f.Request(grant, true);
        var first = f.Normal.ExchangeControl(Fixture.Id, request, true);
        Assert.True(first.Accepted);
        var before = f.Files(f.NormalRoot);
        var repeated = f.Normal.ExchangeControl(Fixture.Id, request, true);
        Assert.False(repeated.Accepted);
        Assert.Equal("stale-consent", repeated.Reason);
        Assert.True(repeated.Status!.Enabled);
        Assert.Equal(first.Revision, repeated.Revision);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("rotated")]
    [InlineData("restarted")]
    public void OldControlCannotBeRenewedByReadingItsExport(string reason)
    {
        using var f = new Fixture();
        var old = f.Prepare();
        if (reason == "expired") f.Now = f.Now.AddHours(12);
        if (reason == "rotated") f.Prepare();
        if (reason == "restarted") f.RestartNormal();
        var before = f.Files(f.NormalRoot);
        var response = f.Normal.ExchangeControl(Fixture.Id, f.Request(old, true), true);
        Assert.False(response.Accepted);
        Assert.Null(response.Status);
        Assert.Null(response.Handoff);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public void RetriedPrepareIsIdempotentButAReplacedControlCannotBePreparedAgain()
    {
        using var f = new Fixture();
        var first = f.Prepare();
        string export = File.ReadAllText(f.Metadata("control.json"));
        Assert.True(f.Normal.PrepareControl(Fixture.Id, first.ControlId).Accepted);
        Assert.Equal(export, File.ReadAllText(f.Metadata("control.json")));
        var second = f.Prepare();
        var before = f.Files(f.NormalRoot);
        Assert.Throws<RewardDataException>(() => f.Normal.PrepareControl(Fixture.Id, first.ControlId));
        Assert.False(f.Normal.ExchangeControl(Fixture.Id, f.Request(first, null), true).Accepted);
        Assert.True(f.Normal.ExchangeControl(Fixture.Id, f.Request(second, null), true).Accepted);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public void IssuedGrantUsesOneClockSampleForItsBoundedLifetime()
    {
        using var f = new Fixture();
        int samples = 0;
        var movingClock = new RewardRuntimeCoordinator(f.NormalRoot, () => f.Now.AddMilliseconds(++samples));
        Assert.True(movingClock.PrepareControl(Fixture.Id, Guid.NewGuid().ToString("N")).Accepted);
        var grant = RewardStartupFiles.Read<RewardControlGrant>(f.Metadata("control.json"));
        Assert.Equal(TimeSpan.FromHours(12), grant.ExpiresUtc - grant.IssuedUtc);
        RewardConsentControl.ValidateGrant(grant, grant.IssuedUtc);
    }

    [Fact]
    public void BusyOrdinaryRaidRejectsWriteAndReadCannotAdvertiseAnEnabledControl()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        var before = f.Files(f.NormalRoot);
        var write = f.Normal.ExchangeControl(Fixture.Id, f.Request(grant, true), false);
        Assert.False(write.Accepted);
        var read = f.Normal.ExchangeControl(Fixture.Id, f.Request(grant, null), false);
        Assert.True(read.Accepted);
        Assert.False(read.Status!.CanChange);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public void ActiveNormalSnapshotRejectsControlChangeEvenIfCallerForgotBusyFlag()
    {
        using var f = new Fixture(enroll: true);
        var grant = f.Prepare();
        f.Normal.ObserveSnapshot(Fixture.Id, f.NormalProfile);
        var before = f.Files(f.NormalRoot);
        Assert.False(f.Normal.ExchangeControl(Fixture.Id, f.Request(grant, false), true).Accepted);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Theory]
    [InlineData("marker-profile")]
    [InlineData("marker-root")]
    [InlineData("marker-time")]
    [InlineData("marker-port")]
    [InlineData("marker-schema")]
    [InlineData("marker-control")]
    [InlineData("lease-root")]
    [InlineData("lease-schema")]
    [InlineData("self-endpoint")]
    [InlineData("broken-json")]
    [InlineData("duplicate-property")]
    [InlineData("oversize")]
    public async Task MalformedPrivateControlNeverFallsBackToCopiedEnabledOrigin(string problem)
    {
        using var f = new Fixture(enroll: true);
        var grant = f.Prepare();
        f.WritePrivate(grant);
        string markerPath = Path.Combine(f.PrivateRoot, "arena-session.json");
        var marker = JsonNode.Parse(File.ReadAllText(markerPath))!;
        var lease = f.Lease(grant);
        switch (problem)
        {
            case "marker-profile": marker["profileId"] = "ffffffffffffffffffffffff"; break;
            case "marker-root": marker["sourceRuntime"] = f.PrivateRoot; break;
            case "marker-time": marker["createdUtc"] = "1999-01-01T00:00:00Z"; break;
            case "marker-port": marker["port"] = 7072; break;
            case "marker-schema": marker["schema"] = 2; break;
            case "marker-control": marker["rewardControlId"] = Guid.NewGuid().ToString("N"); break;
            case "lease-root": lease = lease with { PrivateRoot = f.NormalRoot }; break;
            case "lease-schema": lease = lease with { Schema = 2 }; break;
            case "self-endpoint": lease = lease with { Grant = grant with { OriginUrl = "https://127.0.0.1:7071" } }; break;
        }
        File.WriteAllText(markerPath, marker.ToJsonString());
        File.WriteAllText(f.ControlPath, RewardRuntimeFiles.Json(lease));
        if (problem == "broken-json") File.WriteAllText(f.ControlPath, "{");
        if (problem == "duplicate-property") File.WriteAllText(f.ControlPath, "{\"schema\":1,\"schema\":1}");
        if (problem == "oversize") File.WriteAllText(f.ControlPath, new string(' ', 16385));
        int calls = 0;
        var p = f.NewPrivate((g, r, ct) => { calls++; return f.Relay(g, r, ct); });
        var before = f.Files(f.NormalRoot);
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        Assert.False(p.GetStatus(Fixture.Id).CanChange);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.Equal(0, calls);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
        Assert.True(File.Exists(f.PrivateOrigin));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("external")]
    [InlineData("dns")]
    [InlineData("credentials")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("fragment")]
    [InlineData("pin")]
    [InlineData("token")]
    [InlineData("ttl")]
    [InlineData("future")]
    [InlineData("relative-root")]
    public void GrantsRejectUntrustedTransportDestinationsAndMalformedAuthority(string problem)
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        grant = problem switch
        {
            "http" => grant with { OriginUrl = "http://127.0.0.1:6969" },
            "external" => grant with { OriginUrl = "https://192.0.2.1:6969" },
            "dns" => grant with { OriginUrl = "https://localhost:6969" },
            "credentials" => grant with { OriginUrl = "https://user:pass@127.0.0.1:6969" },
            "path" => grant with { OriginUrl = "https://127.0.0.1:6969/other" },
            "query" => grant with { OriginUrl = "https://127.0.0.1:6969/?redirect=somewhere" },
            "fragment" => grant with { OriginUrl = "https://127.0.0.1:6969/#other" },
            "pin" => grant with { CertificateSha256 = "bad" },
            "token" => grant with { Token = "AA==" },
            "ttl" => grant with { ExpiresUtc = f.Now.AddDays(1) },
            "future" => grant with { IssuedUtc = f.Now.AddHours(1), ExpiresUtc = f.Now.AddHours(2) },
            "relative-root" => grant with { RuntimeRoot = "relative" },
            _ => throw new InvalidOperationException()
        };
        Assert.ThrowsAny<Exception>(() => RewardConsentControl.ValidateGrant(grant, f.Now));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("delegation")]
    [InlineData("epoch")]
    [InlineData("origin")]
    public async Task AReplyCannotReplaceTheOriginRequiredToReturnEarlierReceipts(string problem)
    {
        using var f = new Fixture(enroll: true);
        var grant = f.Prepare();
        f.WritePrivate(grant);
        string original = File.ReadAllText(f.PrivateOrigin);
        var p = f.NewPrivate(async (g, r, ct) =>
        {
            var reply = await f.Relay(g, r, ct);
            var authority = reply.Handoff!;
            authority = problem switch
            {
                "key" => authority with { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
                "delegation" => authority with { DelegationId = Guid.NewGuid().ToString("N") },
                "epoch" => authority with { Binding = authority.Binding with { Epoch = Guid.NewGuid().ToString("N") } },
                "origin" => authority with { RuntimeRoot = f.PrivateRoot },
                _ => throw new InvalidOperationException()
            };
            return reply with { Handoff = authority };
        });
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        Assert.Equal(original, File.ReadAllText(f.PrivateOrigin));
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
    }

    [Fact]
    public async Task LostWriteReplyIsNotRetriedAndNextReadReconcilesActualChoice()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        int writes = 0;
        var p = f.Private(grant, async (g, r, ct) =>
        {
            var reply = await f.Relay(g, r, ct);
            if (r.Enabled.HasValue) { writes++; throw new IOException("synthetic lost response"); }
            return reply;
        });
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        var result = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        Assert.False(result.Available);
        Assert.False(result.CanChange);
        Assert.Equal(1, writes);
        Assert.True(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
        long revision = RewardEnrollment.ReadConsent(f.NormalRoot, Fixture.Id).Revision;
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.True(p.GetStatus(Fixture.Id).Enabled);
        Assert.Equal(revision, RewardEnrollment.ReadConsent(f.NormalRoot, Fixture.Id).Revision);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task OfflineRefreshClearsCachedAuthorityBeforeAnyNewCapture()
    {
        using var f = new Fixture(enroll: true);
        bool offline = false;
        var p = f.Private(f.Prepare(), (g, r, ct) => offline
            ? Task.FromException<RewardControlReply>(new IOException("synthetic offline")) : f.Relay(g, r, ct));
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.True(p.GetStatus(Fixture.Id).Enabled);
        offline = true;
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetriedPublicConsentKeepsOriginalRevisionAcrossLaterNormalChanges(bool privateSession)
    {
        using var f = new Fixture();
        var target = privateSession ? f.Private(f.Prepare()) : f.Normal;
        await target.RefreshPrivateConsentAsync(Fixture.Id);
        long submittedRevision = target.GetStatus(Fixture.Id).ConsentRevision;
        // Server applies the first request, but the public client loses its reply.
        Assert.True((await target.SetConsentAsync(Fixture.Id, true, CancellationToken.None, submittedRevision)).Enabled);
        // A subsequent explicit ordinary choice must survive the client's automatic retry.
        f.Normal.SetConsent(Fixture.Id, false);
        f.Normal.SetConsent(Fixture.Id, true);
        f.Normal.SetConsent(Fixture.Id, false);
        if (privateSession) await target.RefreshPrivateConsentAsync(Fixture.Id);
        var before = f.Files(f.NormalRoot);
        var repeated = await target.SetConsentAsync(Fixture.Id, true, CancellationToken.None, submittedRevision);
        Assert.False(repeated.Enabled);
        Assert.Contains("changed elsewhere", repeated.Detail);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyStartedRunRemainsIdempotentAndFinalizesAfterAuthorityBecomesUnavailable(bool switchedOff)
    {
        using var f = new Fixture(enroll: true);
        bool offline = false;
        var p = f.Private(f.Prepare(), (g, r, ct) => offline
            ? Task.FromException<RewardControlReply>(new IOException("synthetic offline")) : f.Relay(g, r, ct));
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        var report = f.Capture(p);
        if (switchedOff) f.Normal.SetConsent(Fixture.Id, false);
        else offline = true;
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        var begin = f.BeginAgain(p, report);
        Assert.True(begin.Accepted, begin.Reason);
        Assert.Equal(report.RunId, begin.RunId);
        f.Restore(p, report);
        Assert.True(File.Exists(Path.Combine(f.PrivateRoot, "user", "arena-rewards-outbox", report.RunId + ".json")));
    }

    [Fact]
    public async Task ActivePrivateCaptureCannotToggleOrPrepareAnotherGrant()
    {
        using var f = new Fixture(enroll: true);
        var p = f.Private(f.Prepare());
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        var before = f.Files(f.NormalRoot);
        await Assert.ThrowsAsync<RewardDataException>(() => p.SetConsentAsync(Fixture.Id, false, CancellationToken.None));
        Assert.False(p.GetStatus(Fixture.Id).CanChange);
        Assert.False(p.PrepareControl(Fixture.Id, Guid.NewGuid().ToString("N")).Accepted);
        AssertFilesEqual(before, f.Files(f.NormalRoot));
    }

    [Fact]
    public async Task CapturedLeaseDoesNotSilentlyAdoptAReplacementSessionGrant()
    {
        using var f = new Fixture(enroll: true);
        var p = f.Private(f.Prepare());
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        var next = f.Prepare();
        File.WriteAllText(f.ControlPath, RewardRuntimeFiles.Json(f.Lease(next)));
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
    }

    [Fact]
    public async Task NewSessionMarkerWithoutItsControlSidecarCannotUseCopiedLegacyAuthority()
    {
        using var f = new Fixture(enroll: true);
        f.WritePrivate(f.Prepare());
        File.Delete(f.ControlPath);
        var p = f.NewPrivate();
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.False(p.GetStatus(Fixture.Id).Available);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.True(File.Exists(f.PrivateOrigin));
    }

    [Fact]
    public async Task PrivateRewardFailureRemainsVisibleAfterAnotherSuccessfulPreferenceRead()
    {
        using var f = new Fixture(enroll: true);
        var p = f.Private(f.Prepare());
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        var report = f.Capture(p);
        Assert.Throws<RewardDataException>(() => p.FinalizePersisted(Fixture.Id, report.NativeEndIdentity, f.PrivateProfile));
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.Equal("withheld", p.GetStatus(Fixture.Id).State);
        Assert.Equal("No reward for the last match.", p.GetStatus(Fixture.Id).Detail);
    }

    [Fact]
    public async Task AuthorityCacheCannotBeUsedIndefinitelyWithoutAnotherRefresh()
    {
        using var f = new Fixture(enroll: true);
        var p = f.Private(f.Prepare());
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        f.Now = f.Now.AddMinutes(3);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.True(p.HasCapture(Fixture.Id));
    }

    [Fact]
    public async Task PublicStatusDoesNotExposeControlOrSigningCredentials()
    {
        using var f = new Fixture(enroll: true);
        var grant = f.Prepare();
        var p = f.Private(grant);
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        string status = RewardRuntimeFiles.Json(p.GetStatus(Fixture.Id));
        var authority = RewardRuntimeFiles.ReadHandoff(f.PrivateRoot, f.PrivateOrigin);
        Assert.DoesNotContain(grant.Token, status);
        Assert.DoesNotContain(authority.Key, status);
        Assert.DoesNotContain(grant.RuntimeRoot, status);
        Assert.DoesNotContain(grant.ControlId, status);
    }

    [Fact]
    public async Task PrivateRequestsAreSerializedAndEachExplicitChangeUsesLatestConfirmedRevision()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int inFlight = 0, maximum = 0;
        var p = f.Private(f.Prepare(), async (g, r, ct) =>
        {
            int active = Interlocked.Increment(ref inFlight);
            maximum = Math.Max(maximum, active);
            try
            {
                if (r.Enabled == true) { entered.SetResult(); await release.Task; }
                return await f.Relay(g, r, ct);
            }
            finally { Interlocked.Decrement(ref inFlight); }
        });
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Task<RewardStatusResponse> enable = p.SetConsentAsync(Fixture.Id, true, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<RewardStatusResponse> disable = p.SetConsentAsync(Fixture.Id, false, CancellationToken.None);
        Assert.False(disable.IsCompleted);
        release.SetResult();
        await Task.WhenAll(enable, disable);
        Assert.Equal(1, maximum);
        Assert.False(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
        Assert.Equal(2, RewardEnrollment.ReadConsent(f.NormalRoot, Fixture.Id).Revision);
    }

    [Theory]
    [InlineData(16384, false)]
    [InlineData(16385, true)]
    public async Task ControlTransportHasAByteBoundEvenWithoutContentLength(int length, bool reject)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', length)));
        if (reject) await Assert.ThrowsAsync<RewardDataException>(() => RewardConsentTransport.ReadBoundedAsync(stream, CancellationToken.None));
        else Assert.Equal(length, (await RewardConsentTransport.ReadBoundedAsync(stream, CancellationToken.None)).Length);
    }

    [Fact]
    public async Task RealPinnedHttpsCarriesExactlyOneProfileBoundExplicitConsentRequest()
    {
        using var f = new Fixture();
        var grant = f.Prepare();
        RewardControlRequest? observed = null;
        await using var server = new TlsServer((request, _) =>
        {
            Assert.Equal("POST " + RewardConsentControl.Route + " HTTP/1.1", request.StartLine);
            Assert.Equal("PHPSESSID=" + Fixture.Id, request.Headers["Cookie"]);
            observed = JsonSerializer.Deserialize<RewardControlRequest>(request.Body, RewardSettlementCore.JournalJson);
            Assert.NotNull(observed);
            return Task.FromResult<string?>(TlsServer.Ok(RewardRuntimeFiles.Json(f.Normal.ExchangeControl(Fixture.Id, observed!, true))));
        });
        var sent = f.Request(grant, true);
        string before = f.NormalProfile;
        var reply = await RewardConsentTransport.SendAsync(server.Bind(grant), sent, CancellationToken.None);
        Assert.True(reply.Accepted);
        Assert.True(reply.Status!.Enabled);
        Assert.Equal(sent, observed);
        Assert.Equal(1, server.Requests);
        Assert.Equal(before, f.NormalProfile);
    }

    [Fact]
    public async Task RealHttpsRejectsADifferentCertificateBeforeSendingCredentials()
    {
        using var f = new Fixture();
        await using var server = new TlsServer((_, _) => Task.FromResult<string?>(TlsServer.Ok("{}")));
        var grant = server.Bind(f.Prepare()) with { CertificateSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<HttpRequestException>(() => RewardConsentTransport.SendAsync(grant, f.Request(grant, true), CancellationToken.None));
        Assert.Equal(0, server.Requests);
        Assert.False(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
    }

    [Fact]
    public async Task RealHttpsDoesNotFollowRedirectsToAnotherEndpoint()
    {
        using var f = new Fixture();
        await using var destination = new TlsServer((_, _) => Task.FromResult<string?>(TlsServer.Ok("{}")));
        await using var server = new TlsServer((_, _) => Task.FromResult<string?>(
            "HTTP/1.1 307 Temporary Redirect\r\nLocation: " + destination.Url + "/stolen\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        var grant = server.Bind(f.Prepare());
        await Assert.ThrowsAsync<HttpRequestException>(() => RewardConsentTransport.SendAsync(grant, f.Request(grant, true), CancellationToken.None));
        Assert.Equal(1, server.Requests);
        Assert.Equal(0, destination.Requests);
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("duplicates")]
    [InlineData("null")]
    public async Task RealHttpsRefusesUnboundedOrAmbiguousReplies(string problem)
    {
        using var f = new Fixture();
        string body = problem switch
        {
            "oversize" => new string(' ', RewardConsentTransport.MaxBytes + 1),
            "duplicates" => "{\"accepted\":true,\"accepted\":false}",
            _ => "null"
        };
        await using var server = new TlsServer((_, _) => Task.FromResult<string?>(TlsServer.Ok(body)));
        var grant = server.Bind(f.Prepare());
        await Assert.ThrowsAsync<RewardDataException>(() => RewardConsentTransport.SendAsync(grant, f.Request(grant, null), CancellationToken.None));
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task RealHttpsLostWriteReplyIsNotRetried()
    {
        using var f = new Fixture();
        await using var server = new TlsServer((request, _) =>
        {
            var parsed = JsonSerializer.Deserialize<RewardControlRequest>(request.Body, RewardSettlementCore.JournalJson)!;
            Assert.True(f.Normal.ExchangeControl(Fixture.Id, parsed, true).Accepted);
            // A successful commit followed by connection loss is deliberately ambiguous.
            return Task.FromResult<string?>(null);
        });
        var grant = server.Bind(f.Prepare());
        await Assert.ThrowsAsync<HttpRequestException>(() => RewardConsentTransport.SendAsync(grant, f.Request(grant, true), CancellationToken.None));
        Assert.True(RewardEnrollment.WantsRewards(f.NormalRoot, Fixture.Id));
        Assert.Equal(1, RewardEnrollment.ReadConsent(f.NormalRoot, Fixture.Id).Revision);
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task RealHttpsStalledResponseHasABoundedTimeoutAndNoRetry()
    {
        using var f = new Fixture();
        await using var server = new TlsServer(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            return null;
        });
        var grant = server.Bind(f.Prepare());
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RewardConsentTransport.SendAsync(grant, f.Request(grant, null), CancellationToken.None));
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        Assert.Equal(1, server.Requests);
    }

    private static void AssertFilesEqual(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
    }

    private sealed record HttpRequest(string StartLine, Dictionary<string, string> Headers, string Body);

    /// <summary>Owned loopback TLS listener; no installation server, credentials or certificate is used.</summary>
    private sealed class TlsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly X509Certificate2 _certificate;
        private readonly Task _loop;
        private readonly Func<HttpRequest, CancellationToken, Task<string?>> _respond;
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);
        internal string Url => "https://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

        internal TlsServer(Func<HttpRequest, CancellationToken, Task<string?>> respond)
        {
            _respond = respond;
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=owned-loopback-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            // Schannel needs a Windows-backed private key; the import's transient key is
            // released with the owned certificate rather than retained in a machine store.
            _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
            _listener.Start();
            _loop = Serve();
        }

        internal RewardControlGrant Bind(RewardControlGrant grant) => grant with
        {
            OriginUrl = Url, CertificateSha256 = Convert.ToHexStringLower(SHA256.HashData(_certificate.RawData)),
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        internal static string Ok(string body) => "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
            + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body;

        private async Task Serve()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var ssl = new SslStream(connection.GetStream(), leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token);
                    var headerBytes = new List<byte>();
                    byte[] one = new byte[1];
                    while (headerBytes.Count < 8192)
                    {
                        if (await ssl.ReadAsync(one, _stop.Token) == 0) throw new IOException("closed before request");
                        headerBytes.Add(one[0]);
                        int n = headerBytes.Count;
                        if (n >= 4 && headerBytes[n - 4] == 13 && headerBytes[n - 3] == 10 && headerBytes[n - 2] == 13 && headerBytes[n - 1] == 10) break;
                    }
                    string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines.Skip(1).Where(x => x.Contains(':')))
                    {
                        int colon = line.IndexOf(':');
                        headers.Add(line[..colon], line[(colon + 1)..].Trim());
                    }
                    int length = int.Parse(headers["Content-Length"]);
                    if (length > 16384) throw new InvalidOperationException("Unexpected synthetic request size.");
                    byte[] body = new byte[length];
                    await ssl.ReadExactlyAsync(body, _stop.Token);
                    Interlocked.Increment(ref _requests);
                    string? response = await _respond(new(lines[0], headers, Encoding.UTF8.GetString(body)), _stop.Token);
                    if (response is not null)
                    {
                        await ssl.WriteAsync(Encoding.UTF8.GetBytes(response), _stop.Token);
                        await ssl.FlushAsync(_stop.Token);
                    }
                }
                catch (Exception error) when (error is IOException or System.Security.Authentication.AuthenticationException
                    || _stop.IsCancellationRequested && error is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // A rejected pin or deliberately lost response is expected to end the connection.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { _certificate.Dispose(); _stop.Dispose(); }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "0123456789abcdef01234567";
        private const string LocalId = "aaaaaaaaaaaaaaaaaaaaaaaa";
        private const string MarkerTime = "2026-10-25T12:00:00Z";
        private static readonly Lazy<byte[]> Certificate = new(() =>
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Arena synthetic consent tests", key, HashAlgorithmName.SHA256);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return cert.Export(X509ContentType.Cert);
        });
        private readonly string _base = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardConsent.Tests", Guid.NewGuid().ToString("N"));
        private readonly RewardStartupWindow _window;
        internal DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        internal string NormalRoot => Path.Combine(_base, "normal");
        internal string PrivateRoot => Path.Combine(_base, "private");
        internal string PrivateOrigin => Path.Combine(PrivateRoot, "arena-rewards-origin.json");
        internal string ControlPath => Path.Combine(PrivateRoot, "arena-rewards-control.json");
        internal string NormalProfile => File.ReadAllText(ProfilePath(NormalRoot));
        internal string PrivateProfile => File.ReadAllText(ProfilePath(PrivateRoot));
        internal RewardRuntimeCoordinator Normal { get; private set; }
        internal string Metadata(string name) => Path.Combine(NormalRoot, "user", "arena-rewards", Id, name);
        private static string ProfilePath(string root) => Path.Combine(root, "user", "profiles", Id + ".json");

        internal Fixture(bool enroll = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath(NormalRoot))!);
            File.WriteAllText(Path.Combine(_base, ".owner"), "synthetic-reward-consent");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 160_000 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            File.WriteAllText(ProfilePath(NormalRoot), profile.ToJsonString());
            string configs = Path.Combine(NormalRoot, "SPT_Data", "configs");
            Directory.CreateDirectory(configs);
            File.WriteAllText(Path.Combine(configs, "http.json"), "{\"ip\":\"127.0.0.1\",\"port\":6969}");
            string certs = Path.Combine(NormalRoot, "user", "certs");
            Directory.CreateDirectory(certs);
            File.WriteAllBytes(Path.Combine(certs, "server.crt"), Certificate.Value);
            _window = new(NormalRoot, () => true);
            Normal = new(NormalRoot, () => Now);
            if (enroll)
            {
                Normal.SetConsent(Id, true);
                RewardEnrollment.EnsureAtStartup(_window, Id);
            }
        }

        internal void RestartNormal() => Normal = new(NormalRoot, () => Now);
        internal Task<RewardStartupOutcome> Process() => RewardStartupProcessor.ProcessProfileAsync(_window, Id, CancellationToken.None);
        internal RewardControlGrant Prepare()
        {
            var response = Normal.PrepareControl(Id, Guid.NewGuid().ToString("N"));
            Assert.True(response.Accepted, response.Reason);
            return RewardStartupFiles.Read<RewardControlGrant>(Metadata("control.json"), 16384);
        }

        internal RewardControlRequest Request(RewardControlGrant grant, bool? enabled)
            => new(1, grant.ControlId, grant.Token, enabled.HasValue ? RewardEnrollment.ReadConsent(NormalRoot, Id).Revision : null, enabled);
        internal RewardControlLease Lease(RewardControlGrant grant) => new(1, grant, PrivateRoot, MarkerTime, 7071);
        internal void WritePrivate(RewardControlGrant grant)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath(PrivateRoot))!);
            File.Copy(ProfilePath(NormalRoot), ProfilePath(PrivateRoot), overwrite: true);
            File.WriteAllText(Path.Combine(PrivateRoot, "arena-session.json"), JsonSerializer.Serialize(new
            {
                schema = 1, profileId = Id, sourceRuntime = NormalRoot, createdUtc = MarkerTime, port = 7071,
                rewardControlId = grant.ControlId
            }));
            File.WriteAllText(ControlPath, RewardRuntimeFiles.Json(Lease(grant)));
            if (RewardEnrollment.WantsRewards(NormalRoot, Id) && File.Exists(Metadata("handoff.json")))
                File.Copy(Metadata("handoff.json"), PrivateOrigin, overwrite: true);
        }

        internal RewardRuntimeCoordinator Private(RewardControlGrant grant,
            Func<RewardControlGrant, RewardControlRequest, CancellationToken, Task<RewardControlReply>>? send = null)
        {
            WritePrivate(grant);
            return NewPrivate(send);
        }

        internal RewardRuntimeCoordinator NewPrivate(
            Func<RewardControlGrant, RewardControlRequest, CancellationToken, Task<RewardControlReply>>? send = null)
            => new(PrivateRoot, () => Now, send ?? Relay);
        internal Task<RewardControlReply> Relay(RewardControlGrant grant, RewardControlRequest request, CancellationToken ct)
            => Task.FromResult(Normal.ExchangeControl(grant.ProfileId, request, canChange: true));

        internal RewardCaptureRequest Capture(RewardRuntimeCoordinator coordinator)
        {
            coordinator.ObserveSnapshot(Id, PrivateProfile);
            string arm = Guid.NewGuid().ToString("N");
            string native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc " + Now.ToUnixTimeSeconds(), arm);
            var begin = coordinator.Begin(Id, arm, native, LocalId, "deathmatch", false, false,
                new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" });
            Assert.True(begin.Accepted, begin.Reason);
            Now = Now.AddSeconds(10);
            var report = new RewardCaptureRequest
            {
                RunId = begin.RunId, ArmToken = arm, NativeEndIdentity = native, Revision = 1, Final = true,
                Rounds = [new() { Number = 1, Closed = true, ObservedDurationMilliseconds = 2000,
                    Actors = [new() { Body = 1, ParticipantId = LocalId, Side = "Friendly" },
                        new() { Body = 2, ParticipantId = "bbbbbbbbbbbbbbbbbbbbbbbb", Side = "Opponent" }],
                    Deaths = [new() { Sequence = 1, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }] }]
            };
            var captured = coordinator.Capture(Id, report);
            Assert.True(captured.Accepted, captured.Reason);
            return report;
        }

        internal RewardBeginResponse BeginAgain(RewardRuntimeCoordinator coordinator, RewardCaptureRequest report)
            => coordinator.Begin(Id, report.ArmToken, report.NativeEndIdentity, LocalId, "deathmatch", false, false,
                new() { ArmToken = report.ArmToken, NativeEndIdentity = report.NativeEndIdentity, Mode = "deathmatch", Rule = "KillsOnly" });
        internal void Restore(RewardRuntimeCoordinator coordinator, RewardCaptureRequest report)
        {
            coordinator.ObserveRestored(Id, PrivateProfile);
            coordinator.FinalizePersisted(Id, report.NativeEndIdentity, PrivateProfile);
        }

        internal Dictionary<string, string> Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        public void Dispose()
        {
            _window.Dispose();
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RewardConsent.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(_base)) != parent || !RewardIdentity.IsGuid(Path.GetFileName(_base))
                || File.ReadAllText(Path.Combine(_base, ".owner")) != "synthetic-reward-consent")
                throw new InvalidOperationException("Not an owned synthetic reward fixture.");
            Directory.Delete(_base, recursive: true);
        }
    }
}
