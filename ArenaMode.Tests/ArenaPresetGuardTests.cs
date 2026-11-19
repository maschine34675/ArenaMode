using ArenaMode.Server.Services;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class ArenaPresetGuardTests
{
    /// <summary>
    /// Evidence helper, not an assertion about the data: when ARENA_POOL_HASH_OUT names a file, the SHA-256 of
    /// the normalised vanilla pool and of the two database files it was built from are written there. It was
    /// run once on the generator before the preset guard existed and once after, to show the guard changed
    /// nothing on vanilla data. The hash is not pinned: it depends on the SPT data version.
    /// </summary>
    [SkippableFact]
    public async Task RecordTheNormalisedVanillaPoolHash()
    {
        Skip.If(RepoFiles.ItemDatabase == null, "real item database required");

        var json = await WeaponPoolFixture.GenerateAsync(WeaponPoolFixture.LoadTemplates(), WeaponPoolFixture.LoadPresets());
        var pool = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();

        // The shipped fallback is a one-entry array with no items: a pool that really was generated is large.
        Assert.True(pool.Count >= 100, $"the generator did not produce a pool ({pool.Count} entries)");

        var normalised = WeaponPoolFixture.Normalise(json);
        var report = $"pool entries: {pool.Count}{Environment.NewLine}"
                     + $"normalised pool sha256: {WeaponPoolFixture.Sha256(normalised)}{Environment.NewLine}"
                     + $"items.json sha256: {WeaponPoolFixture.Sha256File(WeaponPoolFixture.ItemsPath)}{Environment.NewLine}"
                     + $"globals.json sha256: {WeaponPoolFixture.Sha256File(WeaponPoolFixture.GlobalsPath)}{Environment.NewLine}";

        if (Environment.GetEnvironmentVariable("ARENA_POOL_HASH_OUT") is { Length: > 0 } outPath)
            File.WriteAllText(outPath, report);
    }
}
