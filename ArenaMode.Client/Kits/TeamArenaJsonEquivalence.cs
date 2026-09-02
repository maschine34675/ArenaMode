using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArenaMode.Kits;

/// <summary>
/// Compares the JSON value that crosses the snapshot ownership boundary. Newtonsoft
/// materializes JSON numbers as Int64/Double after parsing even when EFT originally
/// produced a Single JValue, so raw JToken.DeepEquals rejects a valid round-trip.
/// Re-parsing both compact JSON values preserves strict non-null JSON equality while
/// removing that process-local CLR number-type difference. Missing and explicit null
/// tokens follow EFT's UnparsedData.IsNull semantics.
/// </summary>
internal static class TeamArenaJsonEquivalence
{
    internal static bool AreEquivalent(JToken left, JToken right)
    {
        if (ReferenceEquals(left, right))
            return true;

        bool leftIsNull = IsJsonNull(left);
        bool rightIsNull = IsJsonNull(right);
        if (leftIsNull || rightIsNull)
            return leftIsNull && rightIsNull;

        JToken canonicalLeft = JToken.Parse(left.ToString(Formatting.None));
        JToken canonicalRight = JToken.Parse(right.ToString(Formatting.None));
        return JToken.DeepEquals(canonicalLeft, canonicalRight);
    }

    private static bool IsJsonNull(JToken token)
        => token == null || token.Type == JTokenType.Null;
}
