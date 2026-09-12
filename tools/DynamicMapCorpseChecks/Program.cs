using System.Reflection;
using System.Runtime.CompilerServices;
using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using DynamicMaps.DynamicMarkers;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

var assertions = 0;
void Check(bool condition, string name)
{
    assertions++;
    if (!condition) throw new Exception("ASSERT: " + name + ";");
}

(GameWorld World, Corpse Corpse, Player Dead, Player Retained) Setup(bool enable = true)
{
    DynamicMapsCorpseMarkers.Shutdown();
    CorpseCleanupService.Reset();
    Chainloader.PluginInfos.Clear();
    Chainloader.PluginInfos[DynamicMapsCorpseMarkers.PluginGuid] = new PluginInfo { Instance = new DynamicMaps.Plugin() };
    Harmony.ThrowOnPatch = false;
    ArenaSession.IsActive = true;
    Settings.MaxCorpses.Value = 1;
    Settings.StripCorpseGear.Value = false;
    ArenaMode.Compat.PitFireTeamCompat.TeamProfile = null;
    ArenaMode.Loot.OpenLootTracker.Open = null;
    Plugin.ModLogger.Warnings.Clear();
    Time.time = 100;
    var world = new GameWorld();
    var corpse = new Corpse();
    corpse.Item = corpse.ItemOwner.RootItem = new InventoryEquipment();
    var dead = new Player { ProfileId = "reused-profile", gameObject = corpse.gameObject };
    dead.SetCorpse(corpse);
    corpse.gameObject.Components.Add(dead);
    corpse.gameObject.Components.Add(new Renderer());
    var retainedBody = new Corpse { PlayerProfileID = "retained" };
    var retained = new Player { ProfileId = dead.ProfileId };
    retained.SetCorpse(retainedBody);
    world.LootList.Add(corpse);
    world.LootList.Add(retainedBody);
    Singleton<GameWorld>.Instance = world;
    if (enable) DynamicMapsCorpseMarkers.Enable();
    return (world, corpse, dead, retained);
}

foreach (var fault in new[] { "absent", "version", "instance", "patch" })
{
    var (_, corpse, dead, _) = Setup(false);
    switch (fault)
    {
        case "absent": Chainloader.PluginInfos.Clear(); break;
        case "version": Chainloader.PluginInfos[DynamicMapsCorpseMarkers.PluginGuid].Metadata.Version = new Version(1, 3, 0); break;
        case "instance": Chainloader.PluginInfos[DynamicMapsCorpseMarkers.PluginGuid].Instance = new object(); break;
        case "patch": Harmony.ThrowOnPatch = true; break;
    }
    DynamicMapsCorpseMarkers.Enable();
    Check(Harmony.Hooks.Count == 0, "optional bind failure leaves no hook " + fault);
    CorpseCleanupService.Tick();
    var map = new CorpseMarkerProvider();
    map.Add(dead);
    Check(map.Contains(dead) && CorpseCleanupService.IsSuccessfullyTrimmed(corpse), "optional failure preserves cleanup and vanilla markers " + fault);
    Check(Plugin.ModLogger.Warnings.Count == (fault == "absent" ? 0 : 1), "optional bind warning is bounded " + fault);
}

{
    var (world, corpse, dead, retained) = Setup();
    DynamicMapsCorpseMarkers.Enable();
    Check(Harmony.Hooks.Count == 1, "enable registers one exact creation prefix");
    var full = new CorpseMarkerProvider();
    var mini = new CorpseMarkerProvider();
    full.Reopen(new[] { dead, retained });
    mini.Reopen(new[] { dead, retained });
    Check(!CorpseCleanupService.IsSuccessfullyTrimmed(corpse) && full.Count == 2, "untrimmed bodies retain markers");
    var oldMarker = full.Get(dead);
    var retainedMarker = full.Get(retained);
    CorpseCleanupService.Tick();
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(corpse), "successful trim is recorded");
    Check(!full.Contains(dead) && !mini.Contains(dead) && oldMarker.Destroyed, "successful trim removes existing map and minimap markers");
    Check(full.Get(retained) == retainedMarker && mini.Contains(retained), "same profile different body retains marker");
    Check(world.LootList.Contains(corpse) && corpse.gameObject.Components.Contains(dead) && !corpse.Destroyed && !dead.Destroyed,
        "native corpse loot and player owners survive marker removal");
    full.Add(dead);
    var reopened = new CorpseMarkerProvider();
    reopened.Reopen(new[] { dead, retained });
    Check(!full.Contains(dead) && !reopened.Contains(dead) && reopened.Contains(retained), "later and reopened map markers stay suppressed");
    mini.Refresh(new[] { dead, retained });
    Check(!mini.Contains(dead) && mini.Contains(retained), "refresh and map rebuild preserve trimmed suppression");
    full.Inject(dead);
    Time.time += 6;
    CorpseCleanupService.Tick();
    Check(!full.Contains(dead), "regular cleanup sweep removes a reintroduced marker");
    var alive = new Player { ProfileId = dead.ProfileId };
    full.Add(alive);
    Check(full.Contains(alive), "living reused profile retains native admission");
    Check(DynamicMapsCorpseMarkers.BeforeAddMarker(new object(), dead), "foreign provider fails open");
    Check(DynamicMapsCorpseMarkers.BeforeAddMarker(full, null), "null player fails open");
    corpse.Destroyed = true;
    full.Add(dead);
    Check(full.Contains(dead), "destroyed corpse is not retained suppression evidence");
}

foreach (var exclusion in new[] { "normal", "off", "ragdoll", "team", "own", "interacting", "open", "near", "failed", "no-world", "no-player" })
{
    var (world, corpse, dead, _) = Setup();
    var map = new CorpseMarkerProvider();
    map.Add(dead);
    switch (exclusion)
    {
        case "normal": ArenaSession.IsActive = false; break;
        case "off": Settings.MaxCorpses.Value = 0; break;
        case "ragdoll": corpse.Ragdoll._isPhysicsDone = false; break;
        case "team": PitFireTeamCompat.TeamProfile = corpse.PlayerProfileID; break;
        case "own": corpse.PlayerProfileID = world.MainPlayer.ProfileId; break;
        case "interacting": world.MainPlayer.InteractableObject = corpse; break;
        case "open": ArenaMode.Loot.OpenLootTracker.Open = corpse.Item; break;
        case "near": corpse.transform.position = world.MainPlayer.Position; break;
        case "failed": corpse.gameObject.FailComponentRead = true; break;
        case "no-world": Singleton<GameWorld>.Instance = null; break;
        case "no-player": world.MainPlayer = null; break;
    }
    CorpseCleanupService.Tick();
    Check(!CorpseCleanupService.IsSuccessfullyTrimmed(corpse) && map.Contains(dead), "excluded or failed trim keeps existing marker " + exclusion);
    var later = new CorpseMarkerProvider();
    later.Add(dead);
    Check(later.Contains(dead), "excluded or failed trim permits later marker " + exclusion);
}

{
    var (_, corpse, dead, retained) = Setup();
    var broken = new CorpseMarkerProvider { ThrowOnRemove = true };
    var healthy = new CorpseMarkerProvider();
    broken.Add(dead);
    healthy.Add(dead);
    CorpseCleanupService.Tick();
    Check(CorpseCleanupService.IsSuccessfullyTrimmed(corpse) && !healthy.Contains(dead), "map removal failure cannot fail a completed trim or another provider");
    DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();
    Check(Plugin.ModLogger.Warnings.Count == 1, "runtime failure logs once per reset");
    broken.ThrowOnRemove = false;
    Time.time += 6;
    CorpseCleanupService.Tick();
    Check(!broken.Contains(dead), "next sweep retries a transient map removal failure");
    DynamicMapsCorpseMarkers.Shutdown();
    Check(Harmony.Hooks.Count == 0, "shutdown removes owned hook");
    healthy.Add(dead);
    Check(healthy.Contains(dead), "shutdown restores original admission");
}

foreach (var boundary in new[] { "ordinary", "reset", "world" })
{
    var (_, corpse, dead, _) = Setup();
    var map = new CorpseMarkerProvider();
    map.Add(dead);
    CorpseCleanupService.Tick();
    switch (boundary)
    {
        case "ordinary": ArenaSession.IsActive = false; break;
        case "reset": CorpseCleanupService.Reset(); break;
        case "world": Singleton<GameWorld>.Instance = new GameWorld(); break;
    }
    map.Add(dead);
    DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();
    Check(map.Contains(dead) && !CorpseCleanupService.IsSuccessfullyTrimmed(corpse), "lifetime boundary stops both suppression paths " + boundary);
}

{
    Setup();
    var weak = MakeTransientProvider();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(!weak.IsAlive, "adapter does not retain map provider lifetime");
    DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();
    Check(Plugin.ModLogger.Warnings.Count == 0, "collected provider is safely pruned");
}
DynamicMapsCorpseMarkers.Shutdown();
Console.WriteLine($"ARENA_DYNAMICMAP_CORPSE_BEHAVIOR_PASSED ({assertions} assertions)");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference MakeTransientProvider()
{
    var provider = new CorpseMarkerProvider();
    provider.Add(new Player());
    return new WeakReference(provider);
}
