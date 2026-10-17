using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Server.Services;

/// <summary>Optional imported maps are discovered from live server data, never a hard mod dependency.</summary>
internal static class ArenaOptionalMapPolicy
{
    // Bound by PatchManager; resolved on each request after all database loaders
    // have run. A missing/unready provider fails closed only for optional maps.
    internal static Func<string, LocationBase?>? FindLocation { get; set; }

    internal static bool IsAvailable(string? map)
    {
        if (!ArenaMapCatalog.IsOptionalMap(map!)) return true;
        try
        {
            string canonicalId = ArenaMapCatalog.CanonicalOptionalMapId(map!);
            var location = FindLocation?.Invoke(canonicalId);
            return location?.Enabled == true && string.Equals(location.Id, canonicalId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static string ClientConfigJson(ArenaModeConfig config)
    {
        // Filter a detached transport view; never persist availability or change
        // the config editor, user settings, or the shared database location.
        var view = JsonSerializer.SerializeToNode(config)!;
        if (!IsAvailable(ArenaMapCatalog.EquatorId))
            view["maps"]?.AsObject().Remove(ArenaMapCatalog.EquatorId);
        if (!IsAvailable(ArenaMapCatalog.Bay5Id))
            view["maps"]?.AsObject().Remove(ArenaMapCatalog.Bay5Id);
        if (!IsAvailable(ArenaMapCatalog.AirPitId))
            view["maps"]?.AsObject().Remove(ArenaMapCatalog.AirPitId);
        return view.ToJsonString();
    }
}
