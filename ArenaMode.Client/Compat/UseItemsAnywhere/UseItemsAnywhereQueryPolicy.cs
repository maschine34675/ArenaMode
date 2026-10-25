using System;

namespace ArenaMode.Compat;

/// <summary>Require one understood query before changing a consumer's IL.</summary>
internal static class UseItemsAnywhereQueryPolicy
{
    internal static bool ShouldAppend(int nativeQueries, int foreignQueries)
    {
        if (nativeQueries == 1 && foreignQueries == 0) return true;
        if (nativeQueries == 0 && foreignQueries == 1) return false;
        throw new InvalidOperationException("Expected one native or reviewed Use Items Anywhere magazine query; found "
            + nativeQueries + " native and " + foreignQueries + " foreign queries");
    }
}
