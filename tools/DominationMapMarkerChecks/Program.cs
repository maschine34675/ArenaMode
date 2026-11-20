using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using BepInEx.Bootstrap;
using DynamicMaps.Data;
using DynamicMaps.UI.Components;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception("FAIL: " + reason); }
MapDef Map(string name) => new() { MapInternalNames = new() { name } };
void Load(MapView view, MapDef map)
{
#if DYNAMICMAPS_130
    view.LoadMap(map);
#else
    view.LoadMap(map, null);
#endif
}
DominationObjectivePoint[] Points(bool? owner = null, bool contested = false) => new[]
{
    new DominationObjectivePoint("A", new(16.77f, 4.5f, -91.39f), owner, contested),
    new DominationObjectivePoint("B", new(10.614f, 3.13f, -77.727f), null, false),
    new DominationObjectivePoint("C", new(21.91f, 3.15f, -47.3f), false, false)
};
void Reset()
{ Object.Scene.Clear(); Object.Scans = 0; Harmony.Hooks.Clear(); Harmony.Count = 0; Harmony.FailAt = -1;
  Chainloader.PluginInfos.Clear(); Plugin.ModLogger.Warnings = 0; }
#if DYNAMICMAPS_130
var fixtureVersion = new Version(1, 3, 0);
#else
var fixtureVersion = new Version(1, 2, 1);
#endif
void Install(Version version = null) => Chainloader.PluginInfos[DynamicMapsCorpseMarkers.PluginGuid] = new() { Metadata = new() { Version = version ?? fixtureVersion } };

#if DYNAMICMAPS_BAD_SHAPE
Reset(); Install(new Version(1, 4, 0));
using (var unknown = new DynamicMapsDominationMarkers("arena_iceberg", () => true)) unknown.Update(Points());
Need(Object.Scans == 0 && Harmony.Hooks.Count == 0 && Plugin.ModLogger.Warnings == 1,
    "unknown mixed API is refused before hooks and UI discovery");
Console.WriteLine($"DOMINATION_MAP_MARKER_CHECKS_PASSED {checks} assertions, unsupported API refused");
#else
Reset();
using (var absent = new DynamicMapsDominationMarkers("arena_iceberg", () => true)) absent.Update(Points());
Need(Object.Scans == 0 && Harmony.Hooks.Count == 0 && Plugin.ModLogger.Warnings == 0, "optional absence leaves game untouched and silent");
foreach (var version in new[] { new Version(1,2,0), new Version(1,1) })
{
    Reset(); Install(version);
    using var refused = new DynamicMapsDominationMarkers("arena_iceberg", () => true);
    refused.Update(Points());
    Need(Object.Scans == 0 && Harmony.Hooks.Count == 0 && Plugin.ModLogger.Warnings == 1, "old versions fail closed before UI discovery");
}
foreach (int failAt in new[] { 0, 1 })
{
    Reset(); Install(); Harmony.FailAt = failAt;
    using var failed = new DynamicMapsDominationMarkers("arena_iceberg", () => true);
    failed.Update(Points());
    Need(Harmony.Hooks.Count == 0 && Plugin.ModLogger.Warnings == 1, "partial hook failure rolls back without escaping to match");
}

Reset(); Install();
bool allowed = false;
var view = new MapView(); Load(view, Map("arena_iceberg")); view.gameObject.SetActive(false);
var foreign = new MapMarker { Id = "native-player" }; view.Markers.Add(foreign);
using var overlay = new DynamicMapsDominationMarkers("arena_iceberg", () => allowed);
overlay.Update(Points());
Need(view.Markers.Count == 1, "countdown has no objectives");
allowed = true; overlay.Update(Points());
Need(view.Markers.Count == 4 && view.Markers[0] == foreign, "inactive retained map gets objectives and preserves native markers");
var a = view.Markers.Single(m => m.Id == "A");
Need(a.Position.x == 16.77f && a.Position.y == -91.39f && a.Position.z == 4.5f, "Unity coordinates project x z y exactly once");
Need(a.Category == DynamicMapsDominationMarkers.Category && a.Icon == "Markers/dot.png", "markers use an owned category and existing icon");
Need(a.TestImage.sprite == null && a.TestImage.rectTransform.sizeDelta.x == 30 && a.TestImage.rectTransform.localRotation.z == 45,
    "possession backdrop fills a readable diamond instead of the padded native dot");
Need(a.TestAlpha[LayerStatus.OnTop] == 1 && a.TestLabel.alignment == TMPro.TextAlignmentOptions.Center && a.TestLabel.fontSize == 27,
    "IDs remain visible and centred without hover");
Need(a.Color.r == .85f && a.Color.b == .9f, "neutral uses light grey");
overlay.Update(Points(true));
Need(a.Color.b == 1 && a.Color.r < .6f, "USEC ownership changes the existing marker to blue");
overlay.Update(Points(false));
Need(a.Color.r == 1 && a.Color.b < .5f, "BEAR ownership changes the existing marker to red");
overlay.Update(Points(false, true));
Need(a.Color.g == .8f && a.Color.b == .25f, "contested overrides ownership with yellow");
// DynamicMaps 1.3's marker-pack callback reloads each marker sprite. Our existing
// periodic refresh must recover only owned backdrops, without recreating markers.
a.TestImage.sprite = new object();
var foreignSprite = foreign.TestImage.sprite;
overlay.Update(Points());
Need(a.TestImage.sprite == null && ReferenceEquals(foreign.TestImage.sprite, foreignSprite),
    "marker pack refresh preserves owned diamonds and leaves native icons untouched");
a.Hidden = true; overlay.Update(Points(true));
Need(a.TestImage.color.a == 0 && a.TestLabel.color.a == 0 && a.Color.a == 1, "ownership repaint preserves native floor hiding");
Need(view.Adds == 3 && Object.Scans == 1, "steady updates neither recreate markers nor scan the scene");

// A separately created view (e.g. another screen) receives the same current round.
var second = new MapView(); Load(second, Map("ARENA_ICEBERG"));
Need(second.Markers.Count == 3, "late map load uses the live snapshot");
Load(second, Map("arena_equator"));
Need(second.Markers.Count == 0 && second.Removes == 3, "unload and wrong-location selection remove only owned objectives");
Load(second, Map("arena_iceberg"));
Need(second.Markers.Count == 3, "returning to the current map recreates one set");
second.Markers[0].Destroyed = true; overlay.Update(Points());
Need(second.Markers.Count(m => !m.Destroyed) == 3, "destroyed native markers are not reused");
second.Destroyed = true; overlay.Update(Points());
Need(Plugin.ModLogger.Warnings == 0, "destroyed views are pruned without errors");
allowed = false; overlay.Update(Points(true));
Need(view.Markers.Count == 1 && view.Markers[0] == foreign, "terminal sample immediately removes objectives but preserves native markers");
allowed = true; overlay.Update(Points());
var next = new DynamicMapsDominationMarkers("arena_iceberg", () => true); next.Update(Points());
overlay.Dispose(); overlay.Dispose();
Need(Harmony.Hooks.Count == 1 && view.Markers.Count(m => m.Category == DynamicMapsDominationMarkers.Category) == 3,
    "late disposal of old round cannot unpatch or remove new round markers");
next.Dispose();
Need(Harmony.Hooks.Count == 0 && view.Markers.Count == 1, "disposal releases every owned marker and hook");

Reset(); Install();
var throwing = new MapView(); Load(throwing, Map("arena_iceberg"));
using var failing = new DynamicMapsDominationMarkers("arena_iceberg", () => true);
throwing.ThrowAdd = true; failing.Update(Points()); failing.Update(Points());
Need(Plugin.ModLogger.Warnings == 1 && Harmony.Hooks.Count == 0, "runtime failure disables optional overlay once without failing match");
Reset(); Install();
throwing = new MapView(); Load(throwing, Map("arena_iceberg"));
using var removalFailure = new DynamicMapsDominationMarkers("arena_iceberg", () => true);
removalFailure.Update(Points()); throwing.ThrowRemove = true; removalFailure.Dispose();
Need(throwing.Markers.All(m => !m.gameObject.activeSelf) && Plugin.ModLogger.Warnings == 1 && Harmony.Hooks.Count == 0,
    "failed native removal hides only owned UI and cannot interrupt teardown");
Reset(); Install();
throwing = new MapView(); Load(throwing, Map("arena_iceberg"));
var brokenHide = new DynamicMapsDominationMarkers("arena_iceberg", () => true);
brokenHide.Update(Points()); throwing.ThrowRemove = true;
throwing.Markers[0].gameObject.ThrowSetActive = true;
bool escaped = false;
try { throwing.UnloadMap(); brokenHide.Dispose(); } catch { escaped = true; }
Need(!escaped && throwing.Markers.Count == 0 && Harmony.Hooks.Count == 0 && Plugin.ModLogger.Warnings == 1,
    "failing fallback hide cannot interrupt native unload or final unpatch");

Console.WriteLine($"DOMINATION_MAP_MARKER_CHECKS_PASSED {checks} assertions, API {fixtureVersion}");
#endif
