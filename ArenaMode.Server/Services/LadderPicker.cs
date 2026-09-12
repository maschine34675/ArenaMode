using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Services;

/// <summary>
/// Draws one raid's Gun Game ladder out of the weapon pool.
///
/// The pool arrives from the generator already ordered weakest to strongest and
/// holding every weapon the game can build — around a hundred and forty of them.
/// A ladder is twenty-four rungs cut from it, one drawn at random from each
/// power band, so the climb keeps its shape while the guns along the way change
/// from raid to raid. Reaching the top used to mean having seen the same
/// twenty-four weapons every time.
///
/// Drawn once per raid rather than per request: the client asks for the ladder
/// while the raid loads, and a fresh draw on every ask would hand out a
/// different gun than the one the player is standing there holding.
/// </summary>
public static class LadderPicker
{
    private static readonly Random Rng = new();
    private static readonly object Lock = new();

    private static string _current = "[]";

    /// <summary>The ladder for the raid that is running now.</summary>
    public static string Current
    {
        get
        {
            lock (Lock)
                return _current;
        }
    }

    /// <summary>
    /// Cuts a new ladder from the pool. Called when a raid starts; a pool that
    /// is missing, unreadable or already ladder-sized is passed through
    /// unchanged, which also keeps a hand-written ladder.json working.
    /// </summary>
    public static void DrawForRaid(string poolJson, int rungs, Action<string>? log = null)
        => Redraw(poolJson, rungs, log);

    /// <summary>
    /// Cuts a new ladder and returns it, for a player who has climbed the whole
    /// of the last one and would otherwise walk the same twenty-four weapons a
    /// second time in the same raid.
    ///
    /// Installs it as Current as well, so a respawn asking for the ladder after
    /// this point gets the one the player is actually climbing.
    /// </summary>
    public static string Redraw(string poolJson, int rungs, Action<string>? log = null,
        ArenaWeaponVariants? variants = null, IEnumerable<string?>? excludedTemplates = null)
    {
        var excluded = ArenaTemplateEligibility.ExclusionSet(excludedTemplates);
        var drawn = Draw(poolJson, rungs, log, excluded);
        if (variants != null)
        {
            try
            {
                // The configured ids reach the attachment draw here, at every
                // raid, so an id added on the config page mid-evening is gone
                // from the next ladder's attachments as well as its cartridges.
                variants.Exclude(excluded);
                int seed;
                lock (Rng) seed = Rng.Next();
                drawn = variants.CreateCatalog(drawn, seed, out var report);
                log?.Invoke(ArenaWeaponVariants.Summarize("Gun Game", report));
            }
            catch (Exception ex)
            { log?.Invoke($"[ArenaMode] Gun Game attachment draw failed ({ex.Message}); original drawn builds retained."); }
        }
        lock (Lock)
            _current = drawn;

        return drawn;
    }

    /// <summary>
    /// Swaps one of the entry's interchangeable cartridges into every place it
    /// holds ammunition.
    ///
    /// Without this a weapon carries the same round in every raid it ever
    /// appears in, because the pool is generated once. It shows worst on
    /// shotguns, where the choice between buckshot and a slug is most of what
    /// the gun feels like.
    ///
    /// Every ammunition site is rewritten together — the magazine's stack, the
    /// chambered round, and a revolver's individual chambers — because a
    /// cylinder loaded with one round and chambered with another is not a
    /// weapon anyone wants to be handed.
    ///
    /// Choices the configuration excludes are never drawn. The pool is
    /// generated at server start and the exclusions are meant to work without
    /// one — a round found to misbehave mid-evening goes into the list and is
    /// gone from the next raid — and an installed config/ladder.json may
    /// predate the list (the build copies the default only when the file is
    /// missing), as does any hand-written pool.
    /// </summary>
    private static void LoadWithOneOf(JsonNode entry, HashSet<string> excluded)
    {
        // Nothing to choose from means nothing to roll for: an entry without
        // choices keeps its load untouched, and the dice are not moved for it,
        // so a pool authored without choices draws the same ladder it did
        // before there was a list.
        if (entry["ammoChoices"] is not JsonArray choices || choices.Count == 0)
            return;

        var usable = UsableChoices(entry, excluded);
        if (usable.Count == 0)
            return;

        int pick;
        lock (Rng)
            pick = Rng.Next(0, usable.Count);

        var ammo = usable[pick];
        if (entry["items"] is not JsonArray nodes)
            return;

        foreach (var node in nodes)
        {
            var slot = node?["slotId"]?.GetValue<string>();
            if (slot == null)
                continue;

            if (slot == "cartridges" || slot == "patron_in_weapon" || slot.StartsWith("camora_"))
                node!["_tpl"] = ammo;
        }
    }

    /// <summary>
    /// The entry's cartridge choices minus the excluded ones, in pool order.
    /// </summary>
    private static List<string> UsableChoices(JsonNode entry, HashSet<string> excluded)
        => Choices(entry).Where(tpl => !excluded.Contains(tpl)).ToList();

    /// <summary>
    /// Every cartridge the entry could be loaded with, excluded or not. An
    /// entry without choices — a hand-written POOL, one larger than the rung
    /// count; a ladder-sized file is served verbatim and never reaches the
    /// draw — is judged by what it is loaded with instead, for the band draw
    /// only: its load itself is never rewritten.
    /// </summary>
    private static List<string> Choices(JsonNode entry)
    {
        IEnumerable<JsonNode?> source = entry["ammoChoices"] is JsonArray choices && choices.Count > 0
            ? choices
            : (entry["items"] as JsonArray ?? [])
                .Where(node => IsAmmunitionSite(node?["slotId"]?.GetValue<string>()))
                .Select(node => node?["_tpl"]);

        return source
            .Select(node => node?.GetValue<string>())
            .Where(tpl => !string.IsNullOrEmpty(tpl))
            .Select(tpl => tpl!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Had cartridges, and the configuration excludes every one of them. A
    /// weapon that never had any is not barred: it stays in the draw exactly
    /// as it did before there was a list.
    /// </summary>
    private static bool IsBarred(JsonNode entry, HashSet<string> excluded)
    {
        var all = Choices(entry);
        return all.Count > 0 && all.All(excluded.Contains);
    }

    /// <summary>
    /// The pool index nearest to the band that is not barred and not already
    /// on the ladder, searching outward on both sides; -1 when there is none.
    /// The pool is sorted, so the nearest neighbour bends the climb by a rung
    /// rather than breaking it. Asked a second time without the taken set when
    /// nothing untaken is left: a gun that repeats beats a gun that cannot fire.
    /// </summary>
    private static int NearestUnbarred(JsonArray pool, int low, int high, HashSet<string> excluded, HashSet<int>? taken)
    {
        for (var distance = 1; ; distance++)
        {
            var below = low - distance;
            var above = high - 1 + distance;
            if (below < 0 && above >= pool.Count)
                return -1;

            if (above < pool.Count && !IsBarred(pool[above]!, excluded) && taken?.Contains(above) != true)
                return above;

            if (below >= 0 && !IsBarred(pool[below]!, excluded) && taken?.Contains(below) != true)
                return below;
        }
    }

    private static string NameOf(JsonNode? entry)
        => entry?["name"]?.GetValue<string>() ?? entry?["id"]?.GetValue<string>() ?? "?";

    private static bool IsAmmunitionSite(string? slot)
        => slot == "cartridges" || slot == "patron_in_weapon" || slot?.StartsWith("camora_", StringComparison.Ordinal) == true;

    private static string Draw(string poolJson, int rungs, Action<string>? log, HashSet<string> excluded)
    {
        try
        {
            if (JsonNode.Parse(poolJson) is not JsonArray pool || pool.Count == 0)
                return poolJson;

            if (rungs < 1 || pool.Count <= rungs)
                return poolJson;

            var ladder = new JsonArray();
            var passedOver = 0;
            var taken = new HashSet<int>();
            for (var i = 0; i < rungs; i++)
            {
                // Equal slices of the sorted pool, so rung one is always drawn
                // from the weakest guns and the last from the strongest.
                var low = i * pool.Count / rungs;
                var high = Math.Max(low + 1, (i + 1) * pool.Count / rungs);

                int index;
                if (excluded.Count == 0)
                {
                    // Nothing excluded: the draw exactly as it always was.
                    lock (Rng)
                        index = Rng.Next(low, high);
                }
                else
                {
                    // Within the band, only weapons the configuration has not
                    // barred and no earlier rung has taken. A band with nothing
                    // left borrows the nearest such weapon from the pool around
                    // it; with none left anywhere it repeats one that is
                    // already on the ladder — a gun twice beats a gun that
                    // cannot fire — and only when the whole pool is barred does
                    // a rung go out with the pool's own load, said out loud,
                    // because that load is the excluded round itself.
                    var unbarred = Enumerable.Range(low, high - low)
                        .Where(j => !IsBarred(pool[j]!, excluded))
                        .ToList();
                    passedOver += high - low - unbarred.Count;
                    var open = unbarred.Where(j => !taken.Contains(j)).ToList();

                    if (open.Count > 0)
                    {
                        lock (Rng)
                            index = open[Rng.Next(0, open.Count)];
                    }
                    else
                    {
                        index = NearestUnbarred(pool, low, high, excluded, taken);
                        if (index >= 0)
                        {
                            log?.Invoke($"[ArenaMode] Gun Game rung {i + 1}: every weapon of its band is excluded or "
                                        + $"already on the ladder; borrowed '{NameOf(pool[index])}' from the pool next to it.");
                        }
                        else
                        {
                            // Nothing untaken anywhere. The band's own unbarred
                            // weapons first, then the nearest outside it.
                            if (unbarred.Count > 0)
                            {
                                lock (Rng)
                                    index = unbarred[Rng.Next(0, unbarred.Count)];
                            }
                            else
                            {
                                index = NearestUnbarred(pool, low, high, excluded, null);
                            }

                            if (index >= 0)
                            {
                                log?.Invoke($"[ArenaMode] Gun Game rung {i + 1}: no unbarred weapon is left that the ladder "
                                            + $"does not already carry; '{NameOf(pool[index])}' is repeated rather than a "
                                            + "round that does not fire.");
                            }
                            else
                            {
                                lock (Rng)
                                    index = Rng.Next(low, high);
                                passedOver--;
                                log?.Invoke($"[ArenaMode] Gun Game rung {i + 1}: nothing in the pool can be loaded with an "
                                            + $"allowed cartridge; '{NameOf(pool[index])}' goes out with the pool's own load, "
                                            + "which the configuration excludes.");
                            }
                        }
                    }
                }

                taken.Add(index);

                // Detached before re-parenting: a node still owned by the pool
                // array cannot be added to another one.
                var picked = pool[index]!.DeepClone();
                picked["order"] = i;
                picked["id"] = $"rung{i + 1:00}";
                LoadWithOneOf(picked, excluded);
                ladder.Add(picked);
            }

            if (passedOver > 0)
                log?.Invoke($"[ArenaMode] Gun Game ladder: {passedOver} pool weapon(s) passed over, every cartridge of theirs is excluded.");

            log?.Invoke($"[ArenaMode] Gun Game ladder drawn: {rungs} rungs from {pool.Count} weapons.");
            return ladder.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ArenaMode] Ladder draw failed ({ex.Message}) — serving the pool as-is.");
            return poolJson;
        }
    }
}
