using System;
using System.Collections.Generic;

namespace ArenaMode.Session;

/// <summary>
/// One synchronous display pass, not an admission cache. Expensive optional-mod
/// and group inspection happens before construction; every native click, claim
/// and final roster freeze still enters ArenaMenuSelection's fresh public path.
/// </summary>
internal sealed class ArenaMapAvailabilitySnapshot
{
    private readonly ArenaMenuSelection.Eligibility _eligibility;
    private readonly Dictionary<string, ArenaAreaSelectionState> _areas = new(StringComparer.OrdinalIgnoreCase);

    internal ArenaMapAvailabilitySnapshot(ArenaMenuSelection.Eligibility eligibility)
        => _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));

    internal ArenaAreaSelectionState AreaSelection(string map)
    {
        string key = map ?? string.Empty;
        if (!_areas.TryGetValue(key, out var area))
        {
            area = ArenaMenuSelection.AreaSelection(map, _eligibility);
            _areas.Add(key, area);
        }
        return area;
    }

    internal bool IsMapDiscoverable(string map) => AreaSelection(map).Available;

    internal bool TryPrepareMapArea(EFT.RaidSettings owner, string map)
        => ArenaMenuSelection.TryPrepareMapArea(owner, map, _eligibility);
}
