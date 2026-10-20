#nullable enable
using System;

namespace ArenaMode.Shared;

/// <summary>Interim admission until ISB's separate progression stores are isolated.</summary>
public static class ArenaIsbAdmission
{
    public const string SpecialForcesGuid = "com.samc137.isbspecialforces";
    public const string AishiGuid = "com.samc137.aishi";

    // Presence is deliberate: subsystem toggles do not remove persistence routes
    // or item death hooks. Never inspect or alter a foreign configuration here.
    public static string? GetRejectionReason(Func<string, bool> isLoaded)
    {
        if (isLoaded(SpecialForcesGuid))
            return "Arena is currently unavailable with ISB Special Forces: its separate progression is not protected. Remove ISB Special Forces and restart to play Arena. Ordinary PMC/Scav raids remain available.";
        if (isLoaded(AishiGuid))
            return "Arena is currently unavailable with ISB Aishi: anomaly progression and death-prevention items are not isolated. Remove ISB Aishi and restart to play Arena. Ordinary PMC/Scav raids remain available.";
        return null;
    }
}
