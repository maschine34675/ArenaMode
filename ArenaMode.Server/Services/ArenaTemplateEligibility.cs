namespace ArenaMode.Server.Services;

/// <summary>
/// The one answer to "may this template be handed out": to a Gun Game rung,
/// as its ammunition, as an attachment the variant draw bolts on.
///
/// The item database is not a list of things the game finished. BSG ships
/// unfinished duplicates alongside the real items and marks them in the one
/// place a generator walking template properties never looks — the display
/// name: <c>!!!DO_NOT_USE!!!23x75mm "Cheremukha-7M"</c>. That round carries a
/// full set of ballistic numbers (damage 115, penetration 28), sits in the
/// 23x75 calibre like any other, has no handbook entry and no price, and does
/// not fire. The pool generator ranked it by its numbers, put it in the
/// KS-23's cartridge choices, and one raid in three of those that handed out
/// the KS-23 loaded it. Eight such templates exist in the shipped database;
/// their internal names look ordinary.
///
/// Two rules, both here so that every caller refuses the same things. The
/// game's own marker, matched exactly as the game spells it (upper case, with
/// or without underscores) so that an ordinary description cannot trip it.
/// And a configured list of ids, seeded with the known placeholders, for
/// the day a content mod ships another or a round turns out to misbehave for
/// a reason no name reveals. Cartridges additionally need a handbook entry —
/// that check lives with the generator, which has the handbook.
///
/// <see cref="KnownBrokenTemplates"/> is the third case: unfinished items the
/// game does NOT mark at all. Those ids, like the marker, are refused whatever
/// the configuration says — an installed config written before the id was known
/// would otherwise keep handing the item out, since the server's start-up
/// migration only adds missing config KEYS, never missing list entries.
/// </summary>
public static class ArenaTemplateEligibility
{
    /// <summary>
    /// The placeholders in the shipped SPT 4.1 database. Seeded into a fresh
    /// config's excluded list, so the list is visible and editable rather than
    /// a constant nobody can extend. All of them also carry the marker in
    /// their display name; the ids are the belt to that brace.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownPlaceholders = new[]
    {
        "5e85aac65505fa48730d8af2", // !!!DO_NOT_USE!!!23x75mm "Cheremukha-7M" — drawn, loaded, did not fire
        "5ede47641cf3836a88318df1", // !!!DO NOT USE!!!40x46 mm M716(Smoke)
        "62811d61578c54356d6d67ea", // !!!DO_NOT_USE!!!AI AXMC 7.62x51mm bolt assembly
        "628120415631d45211793c99", // !!!DO_NOT_USE!!!AI AXMC 7.62x51mm chassis
        "628120f210e26c1f344e6558", // !!!DO_NOT_USE!!!AI AXMC 7.62x51 10-round magazine
        "6281214c1d5df4475f46a33a", // !!!DO_NOT_USE!!!AI AXMC 7.62x51 26 inch barrel
        "6281215b4fa03b6b6c35dc6c", // !!!DO_NOT_USE!!!AI AXMC 7.62x51 24 inch barrel
        "628121651d5df4475f46a33c", // !!!DO_NOT_USE!!!AI AXMC 7.62x51 20 inch barrel
    };

    /// <summary>
    /// Unfinished items with an ORDINARY display name, so neither the marker
    /// rule nor the handbook rule can see them. Established by inspecting the
    /// template against the native code that consumes it, not by a symptom.
    ///
    /// The Nightforce entry is a prefab/template mismatch that throws once per
    /// frame while the player aims. `OpticComponentUpdater.LateUpdate` decides
    /// whether to drive the zoom from the PREFAB (`ScopeSmoothCameraData`,
    /// whose `IsAdjustableOptic` is hard-coded true), while
    /// `ScopeZoomHandler.Init` only fills `_adjustableOpticData` when the ITEM
    /// template says `IsAdjustableOptic`. This scope ships the variable-zoom
    /// model and a template that says false, so `UpdateScope` reaches
    /// `method_2` with a null field. Its data is unfinished to match: the name
    /// promises 2.5-10x but `Zooms` holds a single 1x, `MinMaxFov` is all
    /// zeroes, no trader sells it, and the handbook price is 609 where real
    /// riflescopes start around 22,000.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownBrokenTemplates = new[]
    {
        "544a3d0a4bdc2d1b388b4567", // Nightforce NXS 2.5-10x24 scope — NRE per frame in ScopeZoomHandler.method_2
    };

    /// <summary>
    /// Everything this code refuses on its own authority, exactly like the
    /// marker rule: the configuration may add to it and never take from it.
    /// </summary>
    public static readonly IReadOnlyList<string> SeededExclusions =
        KnownPlaceholders.Concat(KnownBrokenTemplates).ToList();

    private static readonly HashSet<string> AlwaysExcluded =
        new(SeededExclusions, StringComparer.Ordinal);

    private static readonly string[] Markers = { "DO_NOT_USE", "DO NOT USE" };

    /// <summary>
    /// Whether a display name carries the game's placeholder marker. Case
    /// sensitive on purpose: the marker is always upper case, and a mod item
    /// whose description says "do not use in the rain" is not a placeholder.
    /// </summary>
    public static bool IsPlaceholderName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return false;

        foreach (var marker in Markers)
        {
            if (displayName!.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The configured ids as a set: trimmed, empty entries dropped, compared
    /// exactly. Always contains <see cref="SeededExclusions"/>, so a config
    /// that predates one of those ids — or drops it — still cannot hand the
    /// item out. Everything beyond that list remains the config's business.
    /// </summary>
    public static HashSet<string> ExclusionSet(IEnumerable<string?>? configured)
    {
        var set = new HashSet<string>(AlwaysExcluded, StringComparer.Ordinal);
        if (configured == null)
            return set;

        foreach (var entry in configured)
        {
            var id = entry?.Trim();
            if (!string.IsNullOrEmpty(id))
                set.Add(id!);
        }

        return set;
    }

    /// <summary>Barred by the list, by the name or by the seed; any one alone is enough.</summary>
    public static bool IsExcluded(string? tpl, string? displayName, IReadOnlyCollection<string>? excluded)
    {
        if (!string.IsNullOrEmpty(tpl)
            && (AlwaysExcluded.Contains(tpl!) || (excluded != null && excluded.Contains(tpl!))))
            return true;

        return IsPlaceholderName(displayName);
    }
}
