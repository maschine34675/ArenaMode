// Linked into the test project, which compiles with nullable enabled.
#nullable disable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ArenaMode.Kits;

/// <summary>
/// The id of a player's own kit (docs/reviews/CUSTOM-KITS-20261110.md): "custom-" and 16 lower-case
/// hex digits from a cryptographic source, never reused and never changed by a rename or an edit,
/// so the stored choice and the favourites keep pointing at the same kit. The prefix is a reserved
/// namespace, not the test of what is a custom kit: that is membership in the loaded custom list.
/// Every id fits <see cref="ArenaKitFavorites.IsKitId"/>, so the existing choice and favourite
/// code takes it unchanged.
/// </summary>
internal static class ArenaCustomKitId
{
    internal const string Prefix = "custom-";
    internal const int HexDigits = 16;
    private const int Attempts = 64;

    internal static bool HasPrefix(string id)
        => id != null && id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    internal static bool IsWellFormed(string id)
    {
        if (id == null || id.Length != Prefix.Length + HexDigits || !id.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        for (int index = Prefix.Length; index < id.Length; index++)
        {
            char c = id[index];
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        }

        return true;
    }

    /// <summary>A fresh id that <paramref name="taken"/> refuses for no existing kit; throws after 64 collisions.</summary>
    internal static string NewId(Func<string, bool> taken, Func<byte[]> random = null)
    {
        if (taken == null) throw new ArgumentNullException(nameof(taken));
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            byte[] bytes = random?.Invoke() ?? RandomBytes();
            if (bytes == null || bytes.Length < HexDigits / 2) continue;
            var id = new char[Prefix.Length + HexDigits];
            Prefix.CopyTo(0, id, 0, Prefix.Length);
            for (int index = 0; index < HexDigits / 2; index++)
            {
                id[Prefix.Length + 2 * index] = Hex(bytes[index] >> 4);
                id[Prefix.Length + 2 * index + 1] = Hex(bytes[index] & 15);
            }

            string candidate = new string(id);
            if (!taken(candidate)) return candidate;
        }

        throw new InvalidOperationException("No free custom kit id after 64 attempts.");
    }

    private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

    private static byte[] RandomBytes()
    {
        var bytes = new byte[HexDigits / 2];
        using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);
        return bytes;
    }
}

/// <summary>Whether a loaded custom kit may be used in this session at all.</summary>
internal enum CustomKitState
{
    /// <summary>Passed every check of this session; its price is known.</summary>
    Ready,
    /// <summary>Failed a structure or engine check here (for example an item of a mod that is not loaded); kept in the file.</summary>
    Unavailable,
    /// <summary>No price could be computed (the game's handbook is missing, or the sum is 0): never sold or issued.</summary>
    NoPrice,
}

/// <summary>
/// The owner's rule for custom kits (docs/reviews/CUSTOM-KITS-20261110.md): the price is the only
/// currency. Solo Tactical sells a ready custom kit at its price; every other mode hands one out only
/// when its price is at most the price of the dearest stock kit. An unknown price or cap refuses.
/// </summary>
internal static class ArenaCustomKitPolicy
{
    /// <summary>The dearest stock price; stock kits without a positive price do not count, none at all gives 0.</summary>
    internal static int StockCap(IEnumerable<int> stockPrices)
    {
        int cap = 0;
        if (stockPrices == null) return 0;
        foreach (int price in stockPrices)
            if (price > cap) cap = price;
        return cap;
    }

    /// <summary>Whether a ready custom kit of this price may be issued outside solo Tactical.</summary>
    internal static bool Eligible(CustomKitState state, int price, int cap)
        => state == CustomKitState.Ready && cap > 0 && price > 0 && price <= cap;

    /// <summary>Whether solo Tactical may sell it (affordability is the shop's own question).</summary>
    internal static bool Sellable(CustomKitState state, int price)
        => state == CustomKitState.Ready && price > 0;

    /// <summary>
    /// Why the picker cannot hand this kit out in a mode with the cap, or null when it can. The
    /// reason of an unavailable kit comes from its check; the rest is said here.
    /// </summary>
    internal static string PickerBlock(CustomKitState state, string reason, int price, int cap)
    {
        switch (state)
        {
            case CustomKitState.Unavailable:
                return string.IsNullOrWhiteSpace(reason) ? "not usable in this session" : reason;
            case CustomKitState.NoPrice:
                return "no price: the game's handbook is not loaded";
        }

        if (cap <= 0) return "no stock price to compare with";
        return price > cap ? "above the stock cap: solo Tactical only" : null;
    }

    /// <summary>Why the solo Tactical shop cannot sell it, or null when it can.</summary>
    internal static string ShopBlock(CustomKitState state, string reason, int price)
    {
        if (state == CustomKitState.Unavailable)
            return string.IsNullOrWhiteSpace(reason) ? "not usable in this session" : reason;
        return Sellable(state, price) ? null : "no price: the game's handbook is not loaded";
    }
}

/// <summary>
/// Holds a value for one raid, like <see cref="ArenaKitChoiceLatch"/> holds the choice: the live
/// value while no raid is armed or running; from the first question after an arm on, the value of
/// that moment until the next arm. A value that <c>holdable</c> refuses is answered but not held
/// (the custom-kit view is not held before the stock catalogue has loaded, so a server hiccup cannot
/// freeze a cap of 0 for the whole raid).
/// </summary>
internal sealed class ArenaRaidHold<T>
{
    private bool _held;
    private int _epoch;
    private T _value;

    internal T Resolve(bool armedOrActive, int armEpoch, Func<T> live, Func<T, bool> holdable = null)
    {
        if (live == null) throw new ArgumentNullException(nameof(live));
        if (!armedOrActive)
        {
            _held = false;
            _value = default;
            return live();
        }

        if (_held && _epoch == armEpoch)
            return _value;

        T value = live();
        if (holdable == null || holdable(value))
        {
            _held = true;
            _epoch = armEpoch;
            _value = value;
        }

        return value;
    }
}
