using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// Optional DynamicMaps 1.2.1 UI integration. Arena keeps native corpses alive;
/// the map otherwise keeps their skulls alive too. Remove only its own marker,
/// through its own removal method, after the exact corpse completed trimming.
/// See DYNAMICMAPS-CORPSE-REVIEW.md for the installed contract and native owners.
/// </summary>
internal static class DynamicMapsCorpseMarkers
{
    internal const string PluginGuid = "com.mpstark.dynamicmaps";
    private static readonly Harmony Patches = new Harmony(Plugin.PluginGuid + ".DynamicMapsCorpseMarkers");
    private static readonly List<WeakReference<object>> Providers = new List<WeakReference<object>>();
    private static FieldInfo _playerCorpse, _markers;
    private static MethodInfo _remove;
    private static bool _enabled, _reportedFailure;

    internal static void Enable()
    {
        if (_enabled) return;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) return;
        try
        {
            if (info.Metadata.Version != new System.Version(1, 2, 1))
                throw new NotSupportedException("reviewed DynamicMaps version is 1.2.1");
            var assembly = info.Instance.GetType().Assembly;
            var provider = assembly.GetType("DynamicMaps.DynamicMarkers.CorpseMarkerProvider", true);
            var marker = assembly.GetType("DynamicMaps.UI.Components.MapMarker", true);
            if (!typeof(Component).IsAssignableFrom(marker))
                throw new MissingMemberException("DynamicMaps MapMarker component changed");

            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var playerCorpse = typeof(Player).GetField("Corpse", fields);
            var markers = provider.GetField("_corpseMarkers", fields);
            if (playerCorpse == null || playerCorpse.FieldType != typeof(Corpse)
                || markers == null || markers.FieldType != typeof(Dictionary<,>).MakeGenericType(typeof(Player), marker))
                throw new MissingMemberException("DynamicMaps corpse identity/marker table changed");
            var add = ExactMethod(provider, "TryAddMarker");
            var remove = ExactMethod(provider, "TryRemoveMarker");
            _playerCorpse = playerCorpse;
            _markers = markers;
            _remove = remove;
            Patches.Patch(add, prefix: new HarmonyMethod(typeof(DynamicMapsCorpseMarkers), nameof(BeforeAddMarker))
                { priority = Priority.Last });
            _enabled = true;
            Plugin.ModLogger.LogDebug("Arena DynamicMaps: trimmed-corpse marker cleanup ready (1.2.1).");
        }
        catch (Exception ex)
        {
            // No partial compatibility remains active after a bind/patch failure.
            Shutdown();
            WarnOnce(ex);
        }
    }

    private static MethodInfo ExactMethod(Type type, string name)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, new[] { typeof(Player) }, null);
        if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name + "(Player): void");
        return method;
    }

    internal static void Reset()
    {
        Providers.Clear();
        _reportedFailure = false;
    }

    internal static void Shutdown()
    {
        Patches.UnpatchSelf();
        _enabled = false;
        _playerCorpse = _markers = null;
        _remove = null;
        Reset();
    }

    private static bool IsTrimmed(Player player)
        => player != null && _playerCorpse != null
            && CorpseCleanupService.IsSuccessfullyTrimmed(_playerCorpse.GetValue(player) as Corpse);

    // Every 1.2.1 creation path reaches this seam: initial/reopened full map,
    // minimap, later unregister/death, category enable, refresh and map change.
    // Weak providers do not extend a map's lifetime. Profile ids are deliberately
    // irrelevant: the same Arena participant can have several different bodies.
    internal static bool BeforeAddMarker(object __instance, Player __0)
    {
        if (!ArenaSession.IsActive || !_enabled) return true;
        try
        {
            if (__instance == null || !_markers.DeclaringType.IsInstanceOfType(__instance)) return true;
            Track(__instance);

            // A body Arena has RELEASED is destroyed, so it compares equal to
            // null while still being a live reference. Suppress that one rather
            // than fall through: 1.2.1's own creation path reads the body's
            // nickname and world position, which a destroyed Player cannot
            // answer. Defence in depth, not the primary guard — that is
            // HoldsMarker below, because every 1.2.1 caller except OnMapChanged
            // gates on HasCorpse() and a released body has none.
            //
            // A genuine null is NOT ours and keeps failing open, as before.
            if (!ReferenceEquals(__0, null) && __0 == null) return false;

            return !IsTrimmed(__0);
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            return true;
        }
    }

    private static void Track(object provider)
    {
        for (var i = Providers.Count - 1; i >= 0; i--)
        {
            if (!Providers[i].TryGetTarget(out var existing)) Providers.RemoveAt(i);
            else if (ReferenceEquals(existing, provider)) return;
        }
        Providers.Add(new WeakReference<object>(provider));
    }

    // Runs immediately after a successful trim, then on the existing 5s sweep.
    // Snapshot keys: DynamicMaps removes from its own dictionary. We never write
    // that table ourselves, or unregister/destroy a corpse, Player or loot item.
    internal static void RemoveTrimmedMarkers()
    {
        if (!ArenaSession.IsActive || !_enabled) return;
        for (var i = Providers.Count - 1; i >= 0; i--)
        {
            if (!Providers[i].TryGetTarget(out var provider))
            {
                Providers.RemoveAt(i);
                continue;
            }
            try
            {
                if (!(_markers.GetValue(provider) is IDictionary markers)) continue;
                var players = new List<Player>();
                foreach (var key in markers.Keys)
                    if (key is Player player && IsTrimmed(player)) players.Add(player);
                foreach (var player in players)
                {
                    try { _remove.Invoke(provider, new object[] { player }); }
                    catch (Exception ex) { WarnOnce(ex); }
                }
            }
            catch (Exception ex) { WarnOnce(ex); }
        }
    }

    /// <summary>
    /// True while a tracked provider still holds a marker for this exact body,
    /// or while that cannot be established.
    ///
    /// Arena asks before it destroys a trimmed body. 1.2.1's OnMapChanged walks
    /// the keys it already has and RE-ADDS each one, reading the body's nickname
    /// and world position on the way — a destroyed Player answers neither, and
    /// the creation prefix above cannot help because removal never happened.
    /// Fails closed: an unreadable table counts as holding, so the body stays
    /// hidden instead of being destroyed behind a marker that outlives it.
    /// </summary>
    internal static bool HoldsMarker(Player player)
    {
        if (!_enabled || player == null) return false;
        for (var i = Providers.Count - 1; i >= 0; i--)
        {
            if (!Providers[i].TryGetTarget(out var provider))
            {
                Providers.RemoveAt(i);
                continue;
            }
            try
            {
                if (_markers.GetValue(provider) is IDictionary markers && markers.Contains(player))
                    return true;
            }
            catch (Exception ex)
            {
                WarnOnce(ex);
                return true;
            }
        }
        return false;
    }

    private static void WarnOnce(Exception ex)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        Plugin.ModLogger.LogWarning("Arena optional DynamicMaps corpse markers unavailable: " + ex.GetBaseException().Message);
    }
}
