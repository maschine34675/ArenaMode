using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardEntryControlTests
{
    [Fact]
    public void EntryDoesNotRequireRewardsOrEnrollmentAndUsesItsOwnSecret()
    {
        using var f = new Fixture();
        var first = f.Issue();
        Assert.Equal("arena-entry", first.Purpose);
        Assert.Equal(f.Process.Pid, first.ServerPid);
        Assert.Equal(f.Process.StartedUtc, first.ServerStartedUtc);
        Assert.Equal(f.Process.Arguments, first.ServerArguments);
        Assert.Equal("https://127.0.0.1:6969", first.OriginUrl);
        Assert.Equal(32, Convert.FromBase64String(first.Token).Length);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(f.Export)!, "consent.json")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(f.Export)!, "enrollment.json")));
        Assert.True(f.Control.Issue(Fixture.Id, first.SessionId));
        Assert.Equal(first.Token, f.Read().Token);
        var next = f.Issue();
        Assert.NotEqual(first.Token, next.Token);
        Assert.False(f.Accept(f.Request(first)));
        Assert.True(f.Accept(f.Request(next)));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("session")]
    [InlineData("operation")]
    [InlineData("token")]
    [InlineData("token-format")]
    [InlineData("helper-pid")]
    [InlineData("helper-start")]
    [InlineData("private-root")]
    [InlineData("relative-root")]
    [InlineData("port-low")]
    [InlineData("port-high")]
    [InlineData("marker-format")]
    [InlineData("marker-stale")]
    [InlineData("marker-future")]
    [InlineData("profile")]
    public void InvalidEntryCannotReserveTheShutdown(string mutation)
    {
        using var f = new Fixture();
        var good = f.Request(f.Issue());
        var bad = mutation switch
        {
            "schema" => good with { Schema = 2 },
            "session" => good with { SessionId = Guid.NewGuid().ToString("N") },
            "operation" => good with { OperationId = "../invalid" },
            "token" => good with { Token = Convert.ToBase64String(new byte[32]) },
            "token-format" => good with { Token = "invalid" },
            "helper-pid" => good with { HelperPid = 123 },
            "helper-start" => good with { HelperStartedUtc = good.HelperStartedUtc.AddSeconds(1) },
            "private-root" => good with { PrivateRoot = Path.Combine(f.Root, "elsewhere") },
            "relative-root" => good with { PrivateRoot = "private" },
            "port-low" => good with { Port = 6969 },
            "port-high" => good with { Port = 6980 },
            "marker-format" => good with { MarkerCreatedUtc = "yesterday" },
            "marker-stale" => good with { MarkerCreatedUtc = f.Now.AddMinutes(-2).ToString("O") },
            "marker-future" => good with { MarkerCreatedUtc = f.Now.AddMinutes(2).ToString("O") },
            _ => good
        };
        Assert.False(f.Control.Authorize(mutation == "profile" ? new string('b', 24) : Fixture.Id, bad, out _));
        Assert.True(f.Accept(good));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("arguments")]
    [InlineData("absent")]
    [InlineData("private")]
    public void ChangedProcessOrAuthorityCannotStopTheServer(string mutation)
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        switch (mutation)
        {
            case "expiry": f.Now = f.Now.AddMinutes(6); break;
            case "clock": f.Now = f.Now.AddSeconds(-1); break;
            case "pid": f.Process = f.Process with { Pid = 43 }; break;
            case "start": f.Process = f.Process with { StartedUtc = f.Process.StartedUtc.AddSeconds(1) }; break;
            case "arguments": f.Process.Arguments[0] = "changed"; break;
            case "absent": f.ProcessAvailable = false; break;
            case "private": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}"); break;
        }
        Assert.False(f.Accept(request));
        Assert.Equal("--test", f.Read().ServerArguments[0]);
    }

    [Fact]
    public void AcceptedOperationCanRetryAcrossExpiryButCannotBeReplaced()
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        Assert.True(f.Accept(request));
        f.Now = f.Now.AddHours(1);
        Assert.True(f.Accept(request with { }));
        var competing = request with { OperationId = Guid.NewGuid().ToString("N") };
        Assert.False(f.Accept(competing));
        f.Control.ReleaseBeforeQuiescence(Fixture.Id, competing);
        Assert.True(f.Accept(request));
        Assert.False(f.Control.Issue(Fixture.Id, Guid.NewGuid().ToString("N")));
        f.Control.ReleaseBeforeQuiescence(Fixture.Id, request with { });
        Assert.False(f.Accept(request));
    }

    [Fact]
    public void RestartNeverRehydratesAnOldEntryCapability()
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        Assert.False(f.NewControl().Authorize(Fixture.Id, request, out _));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.2")]
    [InlineData("localhost")]
    public void NonLoopbackOrUnverifiableServerCannotIssueEntry(string ip)
    {
        using var f = new Fixture();
        File.WriteAllText(f.Config, "{\"ip\":\"" + ip + "\",\"port\":6969}");
        Assert.False(f.Control.Issue(Fixture.Id, Guid.NewGuid().ToString("N")));
        Assert.False(File.Exists(f.Export));
    }

    [Fact]
    public void FailedExportDoesNotLeaveAnAcceptedCapability()
    {
        using var f = new Fixture();
        string session = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(f.Export);
        Assert.ThrowsAny<Exception>(() => f.Control.Issue(Fixture.Id, session));
        Directory.Delete(f.Export);
        Assert.True(f.Control.Issue(Fixture.Id, session));
        Assert.True(f.Accept(f.Request(f.Read())));
    }

    [Fact]
    public void EntryAndReturnMustNotShareOrTakeOverOneShutdownSequence()
    {
        using var f = new Fixture();
        var request = f.Request(f.Issue());
        var reserve = new RewardShutdownReservation();
        Assert.True(reserve.Reserve(true, Fixture.Id, request));
        Assert.True(reserve.Reserve(true, Fixture.Id, request with { }));
        Assert.False(reserve.Reserve(false, Fixture.Id, request));
        Assert.False(reserve.Reserve(true, new string('b', 24), request));
        reserve.Release(false, Fixture.Id, request);
        Assert.False(reserve.Reserve(false, Fixture.Id, request));
        reserve.Release(true, Fixture.Id, request with { });
        Assert.True(reserve.Reserve(false, Fixture.Id, request));
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "aaaaaaaaaaaaaaaaaaaaaaaa";
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "ArenaMode.Entry.Tests");
        internal string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        internal DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        internal RewardReturnProcess Process;
        internal bool ProcessAvailable = true;
        internal RewardEntryControl Control;
        internal string Config => Path.Combine(Root, "SPT_Data", "configs", "http.json");
        internal string Export => Path.Combine(Root, "user", "arena-rewards", Id, "entry.json");
        private string PrivateRoot => Path.Combine(Root, "private");
        private readonly DateTimeOffset _helperStart = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
            File.WriteAllText(Path.Combine(Root, ".owner"), "synthetic-entry");
            File.WriteAllText(Config, "{\"ip\":\"127.0.0.1\",\"port\":6969}");
            var certs = Directory.CreateDirectory(Path.Combine(Root, "user", "certs"));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=synthetic-entry", key, HashAlgorithmName.SHA256);
            using var cert = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
            File.WriteAllText(Path.Combine(certs.FullName, "server.crt"), cert.ExportCertificatePem());
            Process = new(42, Now.AddMinutes(-10), ["--test", "two words"]);
            Control = NewControl();
        }
        internal RewardEntryControl NewControl() => new(Root, () => Now, () => ProcessAvailable ? Process : null,
            request => request.HelperPid == 99 && request.HelperStartedUtc == _helperStart, () => PrivateRoot);
        internal RewardEntryGrant Issue() { Assert.True(Control.Issue(Id, Guid.NewGuid().ToString("N"))); return Read(); }
        internal RewardEntryGrant Read() => RewardStartupFiles.Read<RewardEntryGrant>(Export, 16384);
        internal RewardEntryRequest Request(RewardEntryGrant grant) => new(1, grant.SessionId, grant.Token,
            Guid.NewGuid().ToString("N"), 99, _helperStart, PrivateRoot, Now.UtcDateTime.ToString("O"), 6970);
        internal bool Accept(RewardEntryRequest request) => Control.Authorize(Id, request, out _);
        public void Dispose()
        {
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != Path.GetFullPath(Parent)
                || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".owner")) != "synthetic-entry") throw new InvalidOperationException("Not an owned fixture.");
            Directory.Delete(Root, true);
        }
    }
}
