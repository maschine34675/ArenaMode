using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using Logger = ArenaMode.Tests.PitFireTeamPersistenceGuardTests.NoOpLogger;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public class PitFireTeamSoloProtectionTests
{
    private static readonly MongoId Session = new("0123456789abcdef01234567");
    private static readonly MongoId Other = new("89abcdef0123456701234567");
    private const string Token = "abababababababababababababababab";

    [Theory]
    [InlineData("tactical")]
    [InlineData("zombie")]
    [InlineData("deathmatch")]
    [InlineData("gungame")]
    [InlineData("teamdeathmatch")]
    public void InstalledPftProtectsEveryArenaModeWithoutEnablingCompanionRules(string mode)
    {
        DateTime now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var guard = new PitFireTeamPersistenceGuard(() => now);
        var sessions = new ArenaSessionService(Logger.Create<ArenaSessionService>(), guard, () => true);
        string? hostility = mode == "deathmatch" ? DeathmatchSelection.FreeForAll : null;
        Assert.True(sessions.BeginArmPreparation(Session, "factory4_day", mode, false, Token, deathmatchHostility: hostility));
        Assert.True(sessions.IsExternalProfileWriteProtected(Session));
        Assert.False(sessions.IsExternalProfileWriteProtected(Other));
        Assert.True(sessions.CompleteArmPreparation(Session));
        Assert.True(sessions.TryReplayArmedRequest(Session, "factory4_day", mode, false, Token, out _, deathmatchHostility: hostility));

        string start = Protocol.BuildNativeStartIdentity(Token);
        Assert.Equal(ArenaLocalRaidStartDisposition.ArenaStarted,
            sessions.ProcessLocalRaidStart(Session, "factory4_day", start));
        Assert.False(sessions.IsProtectedTeamRaid(Session));
        Assert.True(sessions.BindNativeEndIdentity(Session, start, "factory4_day.Pmc unit-test", out var end));
        now += TimeSpan.FromDays(1);
        Assert.True(sessions.IsExternalProfileWriteProtected(Session));
        Assert.True(sessions.AnyExternalProfileWriteProtection());
        Assert.True(sessions.BeginNativeEndFinalization(Session, end));
        Assert.True(sessions.CompleteNativeEndFinalization(Session, end));
        Assert.True(sessions.IsExternalProfileWriteProtected(Session));
        now += PitFireTeamPersistenceGuard.PostRaidFinalizationWindow + TimeSpan.FromSeconds(1);
        Assert.False(sessions.IsExternalProfileWriteProtected(Session));
    }

    [Fact]
    public void AbsentPftAndOrdinaryRaidsDoNotAcquireExternalProtection()
    {
        var absent = new ArenaSessionService(Logger.Create<ArenaSessionService>(), new(), () => false);
        absent.Arm(Session, "factory4_day", "deathmatch", armToken: Token, deathmatchHostility: DeathmatchSelection.FreeForAll);
        Assert.False(absent.IsExternalProfileWriteProtected(Session));
        Assert.False(absent.BeginPostRaidFinalization(Session));
        var installed = new ArenaSessionService(Logger.Create<ArenaSessionService>(), new(), () => true);
        Assert.Equal(ArenaLocalRaidStartDisposition.Ordinary,
            installed.ProcessLocalRaidStart(Session, "factory4_day", null));
        Assert.False(installed.AnyExternalProfileWriteProtection());
    }

    [Fact]
    public void CancelledPreparationReleasesSoloExternalOwnership()
    {
        var sessions = new ArenaSessionService(Logger.Create<ArenaSessionService>(), new(), () => true);
        Assert.True(sessions.BeginArmPreparation(Session, "factory4_day", "deathmatch", false, Token, deathmatchHostility: DeathmatchSelection.FreeForAll));
        Assert.True(sessions.IsExternalProfileWriteProtected(Session));
        sessions.CancelArmPreparation(Session);
        Assert.False(sessions.AnyExternalProfileWriteProtection());
        Assert.True(sessions.BeginArmPreparation(Session, "factory4_day", "deathmatch", false, Token, deathmatchHostility: DeathmatchSelection.FreeForAll));
        sessions.MarkIdle(Session);
        Assert.False(sessions.AnyExternalProfileWriteProtection());
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "changed contract")]
    [InlineData(true, true, null)]
    public void SoloAdmissionRequiresProtectionOnlyWhenPftIsInstalled(bool installed, bool ready, string? expected)
        => Assert.Equal(expected, PitFireTeamRaidStartPolicy.GetRejectionReason(
            false, "deathmatch", ready, "changed contract", installed));
}
