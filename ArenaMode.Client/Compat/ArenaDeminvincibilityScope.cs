using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Damage authority follows an exact controller/body/world, including retired bodies.</summary>
internal sealed class ArenaDeminvincibilityScope
{
    private sealed class Lease
    {
        internal readonly object Body, World;
        internal Lease(object body, object world) { Body = body; World = world; }
    }
    private ConditionalWeakTable<object, Lease> _controllers = new();

    internal static bool OwnsRaid(bool exactWorld, bool running, bool pmc, string map,
        string settingsMap, bool active, bool armed, bool pending, string armedMap, string token, string nativeId)
    {
        if (!exactWorld || !running || !pmc || string.IsNullOrWhiteSpace(map)
            || !string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)) return false;
        if (active) return true;
        return armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
            && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeId, token);
    }

    internal bool Owns(object controller, object body, object controllerOwner, object currentController,
        object bodyWorld, object currentWorld, bool nativeLocal, bool exactRaid, bool currentOrRegistering)
    {
        if (controller == null || body == null || bodyWorld == null || !nativeLocal
            || !ReferenceEquals(controllerOwner, body) || !ReferenceEquals(currentController, controller)) return false;
        if (_controllers.TryGetValue(controller, out var lease))
        {
            if (ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)) return true;
            _controllers.Remove(controller);
        }
        if (!exactRaid || !currentOrRegistering || !ReferenceEquals(bodyWorld, currentWorld)) return false;
        _controllers.Add(controller, new Lease(body, bodyWorld));
        return true;
    }

    internal void Clear() => _controllers = new ConditionalWeakTable<object, Lease>();
}
