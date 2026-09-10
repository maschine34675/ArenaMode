using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Bots;
using NativeCloner = SPTarkov.Server.Core.Utils.Cloners.FastCloner;

namespace ArenaMode.Tests;

public sealed partial class BotTierScalePatchTests
{
    private static BotGenerationDetails OwnedWave()
    {
        var token = StartTimedArena();
        var wave = Details("factory4_day", 47, 33, 61);
        wave.IsPmc = true; wave.Role = "pmcUSEC"; wave.Side = "Usec"; wave.BotCountToGenerate = 15;
        BotTierScalePatch.Prefix(new MongoId(ArenaSession), wave, Condition(token, 300, 240));
        return wave;
    }

    [Fact]
    public void ApbsPermitSurvivesOnlyTheOwnedNativeCloneScope()
    {
        var wave = OwnedWave();
        var clone = new NativeCloner().Clone(wave)!;
        Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        var scope = ArenaBotTierOwnership.Begin(new(ArenaSession), wave);
        try
        {
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.True(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out var range));
            Assert.Equal(60, range!.Min); Assert.Equal(70, range.Max);
            Assert.False(ArenaBotTierOwnership.TryGetRange(wave, new(1, 100), 79, out _));
            Assert.False(ArenaBotTierOwnership.TryGetRange(new NativeCloner().Clone(clone)!, new(1, 100), 79, out _));
            ArenaBotTierOwnership.ReleaseClone(clone);
            Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        }
        finally { ArenaBotTierOwnership.ReleaseClone(clone); ArenaBotTierOwnership.End(scope); }
    }

    [Theory]
    [InlineData("sameTokenRearm")] [InlineData("stale")] [InlineData("serviceReplacement")]
    [InlineData("optOut")] [InlineData("ordinaryWaveReuse")] [InlineData("cloneLevel")]
    [InlineData("cloneRange")] [InlineData("cloneMap")] [InlineData("cloneRole")]
    [InlineData("playerScav")] [InlineData("foreignPrepare")] [InlineData("foreignDispatch")]
    [InlineData("finalizing")]
    public void ApbsRejectsStaleForeignAndChangedOwnership(string cause)
    {
        var wave = OwnedWave();
        var sessions = ArenaContext.Sessions!;
        var clone = new NativeCloner().Clone(wave)!;
        if (cause == "ordinaryWaveReuse") BotTierScalePatch.Prefix(new(OrdinarySession), wave);
        var scope = ArenaBotTierOwnership.Begin(new(cause == "foreignDispatch" ? OrdinarySession : ArenaSession), wave);
        try
        {
            ArenaBotTierOwnership.BindClone(new(cause == "foreignPrepare" ? OrdinarySession : ArenaSession), clone);
            switch (cause)
            {
                case "sameTokenRearm":
                    var token = sessions.GetArmToken(new(ArenaSession))!;
                    sessions.Arm(new(ArenaSession), "factory4_day", "teamdeathmatch", armToken: token);
                    sessions.ProcessLocalRaidStart(new(ArenaSession), "factory4_day", Protocol.BuildNativeStartIdentity(token));
                    break;
                case "stale": MakeRaidStale(sessions, new(ArenaSession)); break;
                case "serviceReplacement": StartTimedArena(); break;
                case "optOut": ArenaContext.Config.Maps["factory4_day"].ScaleBotTier = false; break;
                case "cloneLevel": clone.PlayerLevel++; break;
                case "cloneRange": clone.LocationSpecificPmcLevelOverride!.Max++; break;
                case "cloneMap": clone.Location = "laboratory"; break;
                case "cloneRole": clone.Role = "assault"; break;
                case "playerScav": clone.IsPlayerScav = true; break;
                case "finalizing":
                    Assert.True(sessions.BindNativeEndIdentity(new(ArenaSession),
                        Protocol.BuildNativeStartIdentity(sessions.GetArmToken(new(ArenaSession))!), "test-server", out var end));
                    Assert.True(sessions.BeginNativeEndFinalization(new(ArenaSession), end));
                    break;
            }
            Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        }
        finally { ArenaBotTierOwnership.ReleaseClone(clone); ArenaBotTierOwnership.End(scope); }
    }

    [Fact]
    public void ApbsNestedOrdinaryGenerationShadowsAndRestoresPendingArenaScope()
    {
        var wave = OwnedWave(); var clone = new NativeCloner().Clone(wave)!;
        var outer = ArenaBotTierOwnership.Begin(new(ArenaSession), wave);
        try
        {
            var nested = ArenaBotTierOwnership.Begin(new(OrdinarySession), wave);
            try
            {
                ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
                Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
            }
            finally { ArenaBotTierOwnership.End(nested); }
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.True(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        }
        finally { ArenaBotTierOwnership.ReleaseClone(clone); ArenaBotTierOwnership.End(outer); }
    }

    [Theory]
    [InlineData(1, 79, 60, 70)] [InlineData(65, 79, 65, 70)]
    [InlineData(1, 62, 60, 62)] [InlineData(100, 79, 79, 79)]
    public void ApbsRangeRespectsTemplateMinimumAndExperienceCeiling(int templateMin, int ceiling, int min, int max)
    {
        var wave = OwnedWave(); var clone = new NativeCloner().Clone(wave)!;
        var scope = ArenaBotTierOwnership.Begin(new(ArenaSession), wave);
        try
        {
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.True(ArenaBotTierOwnership.TryGetRange(clone, new(templateMin, 100), ceiling, out var range));
            Assert.Equal(min, range!.Min); Assert.Equal(max, range.Max);
        }
        finally { ArenaBotTierOwnership.ReleaseClone(clone); ArenaBotTierOwnership.End(scope); }
    }

    [Fact]
    public void ApbsFailedNestedGuardReturnsEmptyScopeAndRestoresOuterOnExit()
    {
        var wave = OwnedWave(); var clone = new NativeCloner().Clone(wave)!;
        var outer = ArenaBotTierOwnership.Begin(new(ArenaSession), wave);
        var config = ArenaContext.Config;
        ArenaBotTierOwnership.Scope? nested = null;
        try
        {
            ArenaContext.Config = null!; // Force IsLive's config guard to throw.
            nested = ArenaBotTierOwnership.Begin(new(ArenaSession), wave);
            ArenaContext.Config = config;
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.False(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
            ArenaBotTierOwnership.End(nested); nested = null;
            ArenaBotTierOwnership.BindClone(new(ArenaSession), clone);
            Assert.True(ArenaBotTierOwnership.TryGetRange(clone, new(1, 100), 79, out _));
        }
        finally
        {
            ArenaContext.Config = config;
            ArenaBotTierOwnership.End(nested);
            ArenaBotTierOwnership.ReleaseClone(clone);
            ArenaBotTierOwnership.End(outer);
        }
    }
}
