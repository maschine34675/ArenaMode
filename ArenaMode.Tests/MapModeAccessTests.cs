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
        foreach (string map in new[] { ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId })
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" })
            if (!(map == ArenaMapCatalog.EquatorId && mode == "tactical"))
                yield return [map, mode];
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
