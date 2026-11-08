using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Modes;

/// <summary>
/// Shuts the doors a round left open (docs/reviews/TACTICAL-OBJECTIVE-20261108.md). Only
/// <see cref="Door"/> objects that are open now but were not open when the scene placed them
/// (<c>InitialDoorState</c>), unlocked, not mid-interaction, not breaching, not broken, operable,
/// allowed to be interacted with and without a map-script trigger on Shut are touched; every
/// other door is skipped (a Close on a locked door would silently unlock it, Door.Interact only
/// guards Open; a Shut trigger would fire with whoever last touched the door, or with nobody).
/// The state is set with <c>force: true</c>: immediate, no shut sound through
/// <c>GamePlayerOwner.MyPlayer</c> (unavailable while Tactical holds the player dead) and no
/// flood of coroutines; the state is read back, because SetDoorState is a silent no-op while an
/// interaction toward Shut is still settling. MapPort spawn gates are not doors and are never
/// touched. Verified against the decompiled WorldInteractiveObject.SetDoorState/PushTriggers,
/// Door.Interact and EDoorState.
/// </summary>
internal static class ArenaRoundDoorReset
{
    private const EDoorState Untouchable = EDoorState.Locked | EDoorState.Interacting | EDoorState.Breaching;

    /// <summary>
    /// Returns how many doors were shut; <paramref name="skipped"/> counts the open doors left alone and
    /// <paramref name="seen"/> every door the scenes hold (0 means the enumeration itself found nothing).
    /// </summary>
    internal static int CloseOpenDoors(out int skipped, out int seen)
    {
        skipped = 0; seen = 0;
        if (!Singleton<GameWorld>.Instantiated) return 0;
        // GameWorld.WorldInteractiveObjects() reads the World component, which the local game world does
        // not carry (GPU-2, 2026-11-08: NullReferenceException at every round start), and the scenes
        // register their doors under WorldInteractiveObject, so GetAllObjects<Door> finds none (the
        // same run: open doors stayed open without a line). Enumerate the registered base type, as the
        // game does for its own door bookkeeping, and keep the Door objects.
        List<Door> doors;
        try { doors = LocationScene.GetAllObjects<WorldInteractiveObject>().OfType<Door>().ToList(); }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogDebug("Arena round door reset: the scene's doors are unavailable: " + ex.Message);
            return 0;
        }
        int closed = 0;
        foreach (var door in doors)
        {
            if (door == null) continue;
            seen++;
            try
            {
                var state = door.DoorState;
                if ((state & EDoorState.Open) == 0) continue;
                if ((door.InitialDoorState & EDoorState.Open) != 0 || (state & Untouchable) != 0 || door.IsBroken
                    || !door.Operatable || door.NoInteractionsAllowed || HasShutTrigger(door)) { skipped++; continue; }
                door.SetDoorState(EDoorState.Shut, true);
                if (IsShut(door)) closed++; else skipped++;
            }
            catch (Exception ex)
            {
                // The state is set before the native hooks run; a hook that throws has not reopened the door.
                if (IsShut(door)) { closed++; Plugin.ModLogger.LogDebug($"Arena round door reset: door {door.Id} shut, its state hook threw: {ex.Message}"); }
                else { skipped++; Plugin.ModLogger.LogDebug($"Arena round door reset: door {door.Id} would not shut: {ex.Message}"); }
            }
        }
        return closed;
    }

    private static bool HasShutTrigger(Door door)
    {
        var triggers = door.TriggersMap;
        if (triggers == null) return false;
        foreach (var pair in triggers)
            if (pair.State == EDoorState.Shut) return true;
        return false;
    }

    private static bool IsShut(Door door)
    {
        try { return door != null && (door.DoorState & EDoorState.Open) == 0; }
        catch { return false; }
    }
}
