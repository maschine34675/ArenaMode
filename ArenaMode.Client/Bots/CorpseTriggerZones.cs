using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Stops the one native coroutine that cannot survive a body leaving the scene.
///
/// A trigger zone in MoveInside mode starts an ENDLESS coroutine per player who
/// walks into it — `while (true) { yield return null; ... player.Transform.position }`
/// — and hosts it on ITSELF, not on the player, keyed by the player's id
/// (`EFT.GameTriggers/TriggerZone.cs:172,198-211`). The only thing that ever
/// stops it is that player's collider count in the zone falling back to zero,
/// and a dead body never walks out. So the coroutine keeps reading a dead
/// player's transform for the rest of the raid.
///
/// While the body is merely hidden that is harmless, which is why nothing
/// noticed before. Destroying the body makes the next read throw, and the 24-min
/// measuring raid of 2026-09-17 logged 21 of them — one per released body that
/// had been standing in such a zone, the coroutine ending itself on the
/// exception. Labyrinth has these zones around its boss alarm (see
/// LABYRINTH-ALARM-REVIEW.md, which reads the same registry).
///
/// So Arena stops that body's coroutine itself, just before the body goes. It is
/// the same thing the zone's own exit path does — remove the entry, stop the
/// coroutine — for exactly the one player id that is about to disappear. The
/// zone's enter/exit bookkeeping (`_playersInside`, `_playerInCollidersCount`)
/// is NOT touched: those hold ints, not bodies, and a dead player never leaving
/// is a native condition that predates the release and is not Arena's to change.
///
/// No scene search: the zones come from LocationScene's own per-scene registry
/// of ITriggerEntity, read once per raid, the same source the alarm patch uses.
/// </summary>
internal static class CorpseTriggerZones
{
    private static readonly List<TriggerZone> Zones = new List<TriggerZone>();
    private static FieldInfo _coroutines;
    private static GameWorld _boundWorld;
    private static bool _reportedFailure;

    internal static void Reset()
    {
        Zones.Clear();
        _coroutines = null;
        _boundWorld = null;
        _reportedFailure = false;
    }

    /// <summary>
    /// Fails OPEN, deliberately and unlike Arena's other native integrations.
    /// The whole consequence of not managing this is one logged exception per
    /// affected body and a coroutine that ends itself — worth a warning, not
    /// worth giving up the release that this exists to support.
    /// </summary>
    internal static void Release(Player player)
    {
        if (player == null)
            return;

        try
        {
            if (!Bind())
                return;

            var id = player.Id;
            for (var i = 0; i < Zones.Count; i++)
            {
                var zone = Zones[i];
                if (zone == null)
                    continue;

                if (!(_coroutines.GetValue(zone) is IDictionary running) || !running.Contains(id))
                    continue;

                var walking = running[id] as Coroutine;
                running.Remove(id);
                if (walking != null)
                    zone.StopCoroutine(walking);
            }
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
        }
    }

    /// <summary>
    /// Once per raid. Rebound on a new GameWorld, because the zone list belongs
    /// to the loaded scene and nothing else identifies a raid.
    /// </summary>
    private static bool Bind()
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (world == null)
            return false;

        if (ReferenceEquals(_boundWorld, world))
            return _coroutines != null;

        Zones.Clear();
        _coroutines = null;
        _boundWorld = world;

        var field = typeof(TriggerZone).GetField(
            "_playerMoveInsideCoroutines", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Dictionary<int, Coroutine>))
        {
            WarnOnce(new MissingMemberException("TriggerZone's per-player coroutine table changed"));
            return false;
        }

        _coroutines = field;
        Zones.AddRange(LocationScene.GetAll<ITriggerEntity>().OfType<TriggerZone>());
        return true;
    }

    private static void WarnOnce(Exception ex)
    {
        if (_reportedFailure)
            return;

        _reportedFailure = true;
        Plugin.ModLogger.LogWarning(
            "Arena corpse release cannot retire trigger-zone coroutines ("
            + ex.GetBaseException().Message
            + "); a released body that stood in a MoveInside zone will log one native exception.");
    }
}
