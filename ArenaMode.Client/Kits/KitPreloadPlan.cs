#nullable enable

using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// How much of the kit catalogue the loading screen warms before the raid.
/// </summary>
public enum KitPreloadScope
{
    /// <summary>Nothing: every kit loads cold the first time it is handed out.</summary>
    Off,

    /// <summary>The kit the raid-start swap will hand out, and only that one.</summary>
    InitialKit,

    /// <summary>
    /// The initial kit first, then every other kit in the catalogue (and every
    /// rung of the weapon ladder in Gun Game), one batch each, for as long as
    /// the loading screen lasts. The batch in flight when the raid begins is
    /// finished; the ones not yet started are left to the in-raid warm-up.
    /// </summary>
    WholeCatalogue,
}

/// <summary>
/// Decides WHAT the loading-screen kit preload loads, without the game.
///
/// Why the loading screen: the first thirty seconds of an arena raid on a
/// machine the scheduler sees as over budget are spent waiting. Measured on
/// Customs Team Deathmatch, the player's start kit (32 cold templates) took
/// 22 s to load on the slow machine and 10 s on the fast one, sharing the
/// loader with the first bot pair (23 s each on the slow machine). Inside the
/// loading screen the game's own loading force mode multiplies the in-raid
/// step budget (by three unless the app config says otherwise) and the bundle
/// lock is unlimited, so the same load costs a fraction of that — and nothing
/// is fighting for the loader yet.
///
/// The decisions here are pure so they can be tested: which kit the raid-start
/// swap will actually hand out for a mode, which templates a kit conjures, and
/// how the catalogue is split into the opening batch the raid start waits for
/// and the per-kit batches behind it that it does not.
/// </summary>
public static class KitPreloadPlan
{
    /// <summary>
    /// Declared here, where the tests can see it, and referenced by the mode
    /// catalogue: a Gun Game raid opens on the first rung of its ladder rather
    /// than on the base kit, and a mode id that drifted apart from this one
    /// would warm the wrong loadout without any error.
    /// </summary>
    public const string GunGameModeId = "gungame";

    /// <summary>One load request: a label for the log and the templates it warms.</summary>
    public sealed class Batch
    {
        public Batch(string label, IReadOnlyList<string> templates)
        {
            Label = label;
            Templates = templates;
        }

        public string Label { get; }
        public IReadOnlyList<string> Templates { get; }
    }

    /// <summary>
    /// The templates a kit conjures, distinct and in kit order: every node under
    /// the root (the root maps onto the live equipment and has nothing to load)
    /// plus the fill's rig contents. Mirrors what KitBundlePreloader warms.
    /// </summary>
    public static List<string> TemplatesOf(KitDefinition? kit)
    {
        var templates = new List<string>();
        if (kit == null)
            return templates;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? tpl)
        {
            if (!string.IsNullOrEmpty(tpl) && seen.Add(tpl!))
                templates.Add(tpl!);
        }

        foreach (var node in kit.Items ?? new List<KitItemNode>())
        {
            if (node != null && !string.IsNullOrEmpty(node.ParentId))
                Consider(node.Tpl);
        }

        foreach (var entry in kit.Fill?.RigContents ?? new List<KitStackedItem>())
            Consider(entry?.Tpl);

        return templates;
    }

    /// <summary>
    /// The kit the raid-start swap will hand out, asked the way the swap asks:
    /// Gun Game the first rung of its ladder merged onto the base kit, every
    /// other mode the base kit (the configured one or the random draw).
    /// Tactical included, and not by accident: it pre-selects the dearest
    /// affordable kit at match start, but its opening buy phase clears that
    /// selection again before the swap asks, so the provider falls through to
    /// the base kit for the first life — the review that found this had the
    /// plan warming a kit the raid never handed out. Null means the raid does
    /// not swap at all — a protected PitFireTeam raid keeps the PMC's entry
    /// loadout — and there is nothing to warm.
    ///
    /// The suppliers are delegates so that only the one that applies runs: the
    /// base-kit supplier is expected to DRAW the raid's kit and reserve it, and
    /// drawing it for a raid that never asks for it would burn a random pick.
    /// </summary>
    public static KitDefinition? OpeningKit(
        string? mode,
        bool protectedTeamRaid,
        Func<KitDefinition?> baseKit,
        Func<KitDefinition?> gunGameOpening)
    {
        if (protectedTeamRaid)
            return null;

        if (string.Equals(mode, GunGameModeId, StringComparison.OrdinalIgnoreCase))
            return gunGameOpening();

        return baseKit();
    }

    /// <summary>
    /// The batches to load, in order. The first is the opening kit, which the
    /// raid start waits for. With the whole catalogue in scope every other kit
    /// (and every ladder rung) follows as a batch of its own, minus whatever an
    /// earlier batch covers — one request per kit rather than one for the lot,
    /// so a raid that begins while a batch is in flight waits for one kit at
    /// most, and the batches not yet started can be left to the in-raid
    /// warm-up. A batch with nothing to load is left out rather than started
    /// empty.
    /// </summary>
    public static List<Batch> Batches(
        KitPreloadScope scope,
        KitDefinition? opening,
        IEnumerable<KitDefinition>? catalogue,
        IEnumerable<KitDefinition>? ladder)
    {
        var batches = new List<Batch>();
        if (scope == KitPreloadScope.Off)
            return batches;

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openingTemplates = TemplatesOf(opening);
        if (openingTemplates.Count > 0)
        {
            covered.UnionWith(openingTemplates);
            batches.Add(new Batch($"kit '{opening!.Id}'", openingTemplates));
        }

        if (scope != KitPreloadScope.WholeCatalogue)
            return batches;

        void Add(IEnumerable<KitDefinition>? source, string kind)
        {
            if (source == null)
                return;

            foreach (var kit in source)
            {
                if (kit == null)
                    continue;

                var fresh = new List<string>();
                foreach (var tpl in TemplatesOf(kit))
                {
                    if (covered.Add(tpl))
                        fresh.Add(tpl);
                }

                if (fresh.Count > 0)
                    batches.Add(new Batch($"{kind} '{kit.Id}'", fresh));
            }
        }

        Add(catalogue, "kit");
        Add(ladder, "rung");
        return batches;
    }
}
