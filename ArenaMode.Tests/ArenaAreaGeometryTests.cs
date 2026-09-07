using ArenaMode.Shared;
using System.Text.Json;
using Xunit;

namespace ArenaMode.Tests;

public sealed class ArenaAreaGeometryTests
{
    [Theory]
    [InlineData(float.NaN, 0, 0)]
    [InlineData(0, float.PositiveInfinity, 0)]
    [InlineData(0, 0, float.NegativeInfinity)]
    [InlineData(-25.01f, 0, 0)]
    [InlineData(40.01f, 0, 0)]
    [InlineData(0, -1.01f, 0)]
    [InlineData(0, 10.01f, 0)]
    [InlineData(0, 0, -46.01f)]
    [InlineData(0, 0, 26.01f)]
    public void InvalidOrOutsidePositionsAreRejected(float x, float y, float z)
        => Assert.False(ArenaAreaCatalog.FactorySouth.Bounds.Contains(x, y, z));

    [Fact]
    public void SpawnFloorDoesNotBecomeAJumpCeiling()
    {
        var area = ArenaAreaCatalog.FactorySouth;
        Assert.True(area.Bounds.Contains(5, 4, 5));
        Assert.False(area.AllowsSpawn(5, 4, 5));
        Assert.False(area.AllowsSpawn(5, -3, 5));
        Assert.True(area.AllowsSpawn(5, 0.1f, 5));
        Assert.False(area.AllowsSpawn(-24.9f, 0.1f, 5));
    }

    [Fact]
    public void BadAndDefaultBoundsCannotAdmitActors()
    {
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(0, 0, 0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(0, 0, 0, float.NaN, 1, 1));
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(1, 0, 0, -1, 1, 1));
        Assert.False(default(ArenaAreaBounds).Contains(0, 0, 0));
        Assert.Throws<ArgumentException>(() => new ArenaAreaDefinition("x", "x", "x", default, 0, 0, 2));
    }

    [Fact]
    public void FullPathIncludesOriginEveryCornerAndHeight()
    {
        var box = new ArenaAreaBounds(-10, -1, -10, 10, 8, 10);
        var start = new ArenaAreaPoint(0, 0, 0);
        var valid = new[] { start, new ArenaAreaPoint(5, 1, 5), new ArenaAreaPoint(8, 2, 8) };
        Assert.True(box.ContainsPath(start, valid, true, 0.4f));
        Assert.False(box.ContainsPath(new ArenaAreaPoint(-11, 0, 0), valid, true));
        Assert.False(box.ContainsPath(start, new[] { start, new ArenaAreaPoint(12, 0, 0), valid[2] }, true));
        Assert.False(box.ContainsPath(start, new[] { start, new ArenaAreaPoint(0, -2, 0), valid[2] }, true));
        Assert.False(box.ContainsPath(start, valid, false));
        Assert.False(box.ContainsPath(start, Array.Empty<ArenaAreaPoint>(), true));
        Assert.False(box.ContainsPath(start, valid, true, float.NaN));
        Assert.False(box.ContainsPath(start, valid, true, -1));
        Assert.False(box.ContainsPath(start, valid, true, 20));
        Assert.True(box.ContainsPath(start, new[] { (0f, 0f, 0f), (3f, 0f, 2f) }, true,
            p => new ArenaAreaPoint(p.Item1, p.Item2, p.Item3)));
    }

    [SkippableFact]
    public void InstalledFactoryCandidateHasSeparatedGroundSpawns()
    {
        string? database = Environment.GetEnvironmentVariable("SPT_DATABASE");
        string file = Path.Combine(database ?? "", "locations", "factory4_day", "base.json");
        Skip.If(!File.Exists(file), "Set SPT_DATABASE to the installed database directory.");
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var area = ArenaAreaCatalog.FactorySouth;
        var candidates = json.RootElement.GetProperty("SpawnPointParams").EnumerateArray()
            .Select(p => new
            {
                Id = p.GetProperty("Id").GetString(),
                Pos = ReadPosition(p.GetProperty("Position")),
                Categories = p.GetProperty("Categories").EnumerateArray().Select(c => c.GetString()).ToArray()
            }).Where(p => area.AllowsSpawn(p.Pos.X, p.Pos.Y, p.Pos.Z)).ToArray();
        var players = candidates.Where(p => p.Categories.Contains("Player")).ToArray();
        var bots = candidates.Where(p => p.Categories.Contains("Bot")).Select(p => p.Pos).Distinct().ToArray();
        Assert.True(players.Length >= 2);
        Assert.True(bots.Length >= 3);
        Assert.DoesNotContain(candidates, p => p.Id == "c671337f-e58f-4de6-a387-307d877954a3");
        foreach (var player in players)
        {
            Assert.Contains(players, p => Distance(p.Pos, player.Pos) >= 40);
            Assert.True(bots.Count(p => Distance(p, player.Pos) >= 25) >= 3);
        }
    }

    private static ArenaAreaPoint ReadPosition(JsonElement p)
        => new(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle(), p.GetProperty("z").GetSingle());
    private static double Distance(ArenaAreaPoint a, ArenaAreaPoint b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
}
