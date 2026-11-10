using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// The player's kit choice for the base draw (docs/reviews/KIT-PICKER-20261110.md): a random
/// kit for every life, or one catalogue kit. It is the pair the two long-standing settings
/// Kits/RandomKit and Kits/SelectedKit already express, carried as one value so it can be
/// held for a raid. An empty id with the random draw off keeps its old meaning, the first
/// buildable kit of the catalogue.
/// </summary>
internal readonly struct ArenaKitChoice : IEquatable<ArenaKitChoice>
{
    internal ArenaKitChoice(bool random, string kitId)
    {
        Random = random;
        KitId = (kitId ?? string.Empty).Trim();
    }

    internal bool Random { get; }
    internal string KitId { get; }

    /// <summary>True when this choice names <paramref name="kitId"/> and the random draw is off.</summary>
    internal bool Selects(string kitId)
        => !Random && KitId.Length > 0 && string.Equals(KitId, (kitId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    public bool Equals(ArenaKitChoice other)
        => Random == other.Random && string.Equals(KitId, other.KitId, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object obj) => obj is ArenaKitChoice other && Equals(other);

    public override int GetHashCode()
        => (Random ? 1 : 0) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(KitId ?? string.Empty);
}

/// <summary>
/// Writes a kit choice into the two settings it is stored in (Settings.TrySelectKit holds the
/// entries): refused while locked or for an id that is no plain kit id, the id before the
/// switch so a failed second write cannot leave a fixed choice on the previous kit, both
/// restored when a write throws, and success only when both read back. The random draw
/// leaves the stored id alone, so switching back to a fixed kit returns to the last one.
/// </summary>
internal static class ArenaKitChoiceWrite
{
    internal static bool Apply(bool locked, bool random, string kitId,
        Func<bool> readRandom, Action<bool> writeRandom, Func<string> readId, Action<string> writeId,
        Action<Exception> failed)
    {
        if (locked) return false;
        string id = (kitId ?? string.Empty).Trim();
        if (!random && !ArenaKitFavorites.IsKitId(id)) return false;
        bool oldRandom = readRandom();
        string oldId = readId();
        try
        {
            if (!random) writeId(id);
            writeRandom(random);
        }
        catch (Exception ex)
        {
            failed?.Invoke(ex);
            try { writeRandom(oldRandom); } catch { }
            try { writeId(oldId); } catch { }
            return false;
        }

        return readRandom() == random
               && (random || string.Equals(readId(), id, StringComparison.Ordinal));
    }
}

/// <summary>
/// Holds the kit choice for one raid. While no raid is armed or running the live settings
/// answer, so the menu shows what the player just picked. From the first question after an
/// arm on (the loading screen's kit reservation asks first) the answer of that moment is kept
/// until the next arm: every life of the raid, the preload and the round breaks then agree,
/// and an F12 edit made in the raid takes effect in the next one.
/// </summary>
internal sealed class ArenaKitChoiceLatch
{
    private bool _held;
    private int _epoch;
    private ArenaKitChoice _choice;

    internal ArenaKitChoice Resolve(bool armedOrActive, int armEpoch, ArenaKitChoice live)
    {
        if (!armedOrActive)
        {
            _held = false;
            return live;
        }

        if (!_held || _epoch != armEpoch)
        {
            _held = true;
            _epoch = armEpoch;
            _choice = live;
        }

        return _choice;
    }
}

/// <summary>
/// The favourite kits of the picker, stored as one comma-separated setting (Kits/FavoriteKits).
/// Ids compare without case; anything that is not a plain kit id is dropped on reading, so a
/// hand-edited value cannot grow without bound or smuggle separators into the list.
/// </summary>
internal static class ArenaKitFavorites
{
    internal const int MaxEntries = 64;
    internal const int MaxIdLength = 64;

    internal static List<string> Parse(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (string part in value.Split(','))
        {
            string id = part.Trim();
            if (!IsKitId(id) || Contains(result, id)) continue;
            result.Add(id);
            if (result.Count >= MaxEntries) break;
        }

        return result;
    }

    internal static bool Contains(IEnumerable<string> favorites, string kitId)
    {
        if (favorites == null || string.IsNullOrWhiteSpace(kitId)) return false;
        string wanted = kitId.Trim();
        foreach (string id in favorites)
            if (string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// The stored value after one click on a kit's favourite mark: removed when it was there,
    /// appended otherwise. A full list or a name that is no kit id leaves the value as it was read.
    /// </summary>
    internal static string Toggle(string value, string kitId)
    {
        var list = Parse(value);
        string id = (kitId ?? string.Empty).Trim();
        if (!IsKitId(id)) return Format(list);
        int at = list.FindIndex(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) list.RemoveAt(at);
        else if (list.Count < MaxEntries) list.Add(id);
        return Format(list);
    }

    internal static string Format(IEnumerable<string> favorites)
        => favorites == null ? string.Empty : string.Join(",", favorites);

    internal static bool IsKitId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
        foreach (char c in id)
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                && c != '-' && c != '_' && c != '.') return false;
        return true;
    }
}
