using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;

public sealed class MapModeAccessTests
{
    public static IEnumerable<object[]> SupportedCases()
    {
        foreach (string map in new[] { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId, ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId,
                     ArenaMapCatalog.IcebergId, ArenaMapCatalog.AutoServiceId })
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" })
            yield return [map, mode];
    }

    // ArenaMapPort's spawn rows of 2026-11-12 removed the row argument of the three Tactical
    // refusals (installed data of 2026-11-13: Equator 9 rows beyond 25 m from the atrium and 6
    // at the worst spot, Iceberg 9 in the old centre, Chop Shop 5 in the yard centre and 3 at
    // the worst spot). Equator and Iceberg are admitted solo and with a PitFireTeam squad.
    [Theory]
    [InlineData(ArenaMapCatalog.EquatorId)]
    [InlineData(ArenaMapCatalog.IcebergId)]
    public void TacticalIsAdmittedSoloAndWithAPitFireTeamSquadOnEquatorAndIceberg(string map)
    {
        var pairs = SupportedCases().Select(c => ((string)c[0], (string)c[1])).ToArray();
        Assert.Contains((map, "tactical"), pairs);
        Assert.Contains((map, "zombie"), pairs);
        Assert.Contains((map, "teamdeathmatch"), pairs);
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", false, out _, out _));
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", true, out _, out _));
    }

    [Fact]
    public void ChopShopAdmitsSoloTacticalButRefusesTheSquadFormat()
    {
        var pairs = SupportedCases().Select(c => ((string)c[0], (string)c[1])).ToArray();
        Assert.Contains((ArenaMapCatalog.AutoServiceId, "tactical"), pairs);
        Assert.Contains((ArenaMapCatalog.AutoServiceId, "zombie"), pairs);
        Assert.Contains((ArenaMapCatalog.AutoServiceId, "teamdeathmatch"), pairs);
        Assert.True(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.AutoServiceId, "tactical", false, out _, out _));
        Assert.False(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.AutoServiceId, "tactical", true, out _, out _));
    }

    // Every formation names its two exits now (they stand in the pockets since ArenaMapPort
    // 2026-11-12), USEC's first, as in Equator's entry. The member is data: extraction is
    // isolated and nothing reads positions.
    [Theory]
    [InlineData(ArenaMapCatalog.EquatorId, "Equator North Gate", "Equator South Gate")]
    [InlineData(ArenaMapCatalog.Bay5Id, "Bay5 North Loading Bay", "Bay5 South Container Lane")]
    [InlineData(ArenaMapCatalog.AirPitId, "Air Pit North Gate", "Air Pit South Gate")]
    [InlineData(ArenaMapCatalog.BowlId, "Bowl BTR Gate", "Bowl Army Gate")]
    [InlineData(ArenaMapCatalog.IcebergId, "Iceberg West Gate", "Iceberg East Gate")]
    [InlineData(ArenaMapCatalog.AutoServiceId, "Chop Shop East Gate", "Chop Shop West Gate")]
    public void EveryFormationNamesItsTwoPocketExits(string map, string usecExit, string bearExit)
    {
        var formation = ArenaTeamBoxCatalog.Find(map)!;
        Assert.Equal(new[] { usecExit, bearExit }, formation.BoothExits);
    }

    [Theory]
    [MemberData(nameof(SupportedCases))]
    public void NewFullMapPairsRetainQuietCloneAndFrozenPopulation(string mapId, string mode)
    {
        var original = new LocationBase
        {
            Id = mapId,
            Waves = [new Wave { SpawnPoints = "original-zone", SlotsMin = 1, SlotsMax = 4 }],
            BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcBEAR", BossChance = 100 }],
            BotLocationModifier = new BotLocationModifier
                { NonWaveSpawnBotsLimitPerPlayer = 12, NonWaveSpawnBotsLimitPerPlayerPvE = 12 },
            NewSpawn = true,
            OfflineNewSpawn = true,
        };
        if (ArenaEquatorTeamSpawns.Applies(mapId, mode))
            original.SpawnPointParams = [EquatorTeamSpawnTests.Donor()];
        else if (mapId == ArenaMapCatalog.Bay5Id && mode == "teamdeathmatch")
            original.SpawnPointParams = [Bay5TeamSpawnTests.Donor()];
        else if (mapId == ArenaMapCatalog.AirPitId && mode == "teamdeathmatch")
            original.SpawnPointParams = [AirPitTeamSpawnTests.Donor()];
        else if (mapId == ArenaMapCatalog.BowlId && mode == "teamdeathmatch")
            original.SpawnPointParams = [BowlTeamSpawnTests.Donor()];
        else if (mapId == ArenaMapCatalog.IcebergId && mode == "teamdeathmatch")
            original.SpawnPointParams = [IcebergTeamSpawnTests.Donor()];
        else if (mapId == ArenaMapCatalog.AutoServiceId && mode == "teamdeathmatch")
            original.SpawnPointParams = [ChopShopTeamSpawnTests.Donor()];
        string before = JsonSerializer.Serialize(original);
        var clone = JsonSerializer.Deserialize<LocationBase>(before)!;
        var config = new ArenaModeConfig();
        int? total = mode is "deathmatch" or "gungame" or "teamdeathmatch" ? 8 : null;
        ArenaLocationMutator.Mutate(clone, config.Maps[mapId], config, mode, participants: total);
        Assert.Empty(clone.Waves!);
        Assert.Empty(clone.BossLocationSpawn!);
        Assert.False(clone.NewSpawn);
        Assert.False(clone.OfflineNewSpawn);
        Assert.Equal(0, clone.BotStop);
        Assert.Equal(0, clone.BotLocationModifier!.NonWaveSpawnBotsLimitPerPlayer);
        Assert.Equal(0, clone.BotLocationModifier.NonWaveSpawnBotsLimitPerPlayerPvE);
        Assert.Equal(before, JsonSerializer.Serialize(original));
        if (total.HasValue)
        {
            ArenaLocationMutator.ValidateParticipantPopulation(clone, mapId, mode, total.Value);
            Assert.Equal(7, clone.BotMax);
            clone.BotMax = 8;
            Assert.Throws<InvalidOperationException>(() =>
                ArenaLocationMutator.ValidateParticipantPopulation(clone, mapId, mode, total.Value));
        }
        if (mode == "zombie")
        {
            Assert.Equal(100, clone.Events!.Halloween2024!.InfectionPercentage);
            Assert.Equal(0, clone.Events.Halloween2024.MaxCrowdAttackSpawnLimit);
            Assert.True(clone.BotMax >= ZombieLocationSettings.MinimumBotCap);
        }
    }
}
