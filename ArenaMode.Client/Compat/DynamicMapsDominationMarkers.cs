using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Effects;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// One Domination round's optional map overlay. The runtime supplies its verified
/// floors; DynamicMaps owns projection, zoom, layers and removal. No map file edits.
/// </summary>
internal sealed class DynamicMapsDominationMarkers : IDisposable
{
    internal const string Category = "ArenaMode Domination";
    private static DynamicMapsDominationMarkers _current;
    private readonly Func<bool> _allowed;
    private readonly string _location;
    private readonly Dictionary<Component, List<Component>> _views = new();
    private readonly Harmony _patches = new("com.maschine.ArenaMode.DominationMaps." + Guid.NewGuid().ToString("N"));
    private Api _api;
    private DominationObjectivePoint[] _points = Array.Empty<DominationObjectivePoint>();
    private bool _disposed, _failed, _warned, _patchAttempted;

    internal DynamicMapsDominationMarkers(string location, Func<bool> allowed)
    {
        _location = location; _allowed = allowed;
        _current?.Dispose();
        _current = this;
        if (!Chainloader.PluginInfos.TryGetValue(DynamicMapsCorpseMarkers.PluginGuid, out var info)) return;
        try
        {
            var version = info.Metadata?.Version;
            if (version == null || version.Build < 0
                || new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision)) < new Version(1, 2, 1, 0))
                throw new NotSupportedException("DynamicMaps 1.2.1 or newer is required.");
            _api = new Api(info.Instance.GetType().Assembly);
            _patchAttempted = true; // also roll back a partially throwing Patch call
            _patches.Patch(_api.Load, postfix: new HarmonyMethod(typeof(DynamicMapsDominationMarkers), nameof(Loaded)));
            _patches.Patch(_api.Unload, prefix: new HarmonyMethod(typeof(DynamicMapsDominationMarkers), nameof(Unloading)));
            // A retained, closed map can reopen without LoadMap when its map is
            // unchanged. Discover inactive views once; never scan the scene per tick.
            foreach (var view in UnityEngine.Object.FindObjectsOfType(_api.ViewType, true)) Observe((Component)view);
            Plugin.ModLogger.LogDebug("Arena DynamicMaps: Domination objective overlay ready.");
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal void Update(IReadOnlyList<DominationObjectivePoint> points)
    {
        if (_disposed || _failed || _api == null) return;
        try
        {
            _points = points.ToArray();
            foreach (var view in _views.Keys.ToArray()) Refresh(view);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void Loaded(object __instance) => _current?.Observe(__instance as Component);
    private static void Unloading(object __instance) => _current?.Forget(__instance as Component);

    private void Observe(Component view)
    {
        if (_disposed || _failed || _api == null || view == null) return;
        try
        {
            if (!_views.ContainsKey(view)) _views.Add(view, new List<Component>());
            Refresh(view);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Refresh(Component view)
    {
        if (view == null) { _views.Remove(view); return; }
        var owned = _views[view];
        var definition = _api.CurrentMap.GetValue(view);
        var names = definition == null ? null : _api.MapNames.GetValue(definition) as IEnumerable<string>;
        if (!_allowed() || names == null || !names.Any(n => string.Equals(n, _location, StringComparison.OrdinalIgnoreCase)))
        { Clear(view, owned); return; }

        if (owned.Count != _points.Length || owned.Any(marker => marker == null))
        {
            Clear(view, owned);
            foreach (var point in _points)
            {
                var definitionObject = Activator.CreateInstance(_api.DefinitionType);
                _api.Text.SetValue(definitionObject, point.Id);
                _api.Category.SetValue(definitionObject, Category);
                _api.ImagePath.SetValue(definitionObject, "Markers/dot.png");
                _api.DefPosition.SetValue(definitionObject, Project(point.Position));
                var marker = (Component)_api.Add.Invoke(view, new[] { definitionObject });
                if (marker == null) throw new InvalidOperationException("DynamicMaps did not return a marker.");
                owned.Add(marker); // own it before any optional styling can fail
                _api.Size.SetValue(marker, new Vector2(28f, 28f));
                var label = (TextMeshProUGUI)_api.Label.GetValue(marker);
                // Keep native hover/layer handling, but make the compact ID always
                // visible on this floor. These dictionaries belong to this marker.
                ((IDictionary)_api.LabelAlpha.GetValue(marker))[_api.OnTop] = 1f;
                label.richText = false;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                label.rectTransform.pivot = new Vector2(.5f, .5f);
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.rectTransform.sizeDelta = new Vector2(28f, 28f);
            }
        }
        for (int i = 0; i < owned.Count; i++)
        {
            var marker = owned[i];
            _api.Color.SetValue(marker, ColorFor(_points[i]));
            var label = (TextMeshProUGUI)_api.Label.GetValue(marker);
            // Native OnEnable/hover may restyle a label; restore only our ID.
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false; label.enableWordWrapping = false;
            label.fontSize = 18f; label.fontStyle = FontStyles.Bold;
            label.color = new Color(.08f, .09f, .12f);
            // Color sets alpha to one: re-run native layer visibility afterwards,
            // including when only ownership changed on a hidden Factory floor.
            _api.Position.SetValue(marker, Project(_points[i].Position));
        }
    }

    internal static Vector3 Project(Vector3 world) => new(world.x, world.z, world.y);
    internal static Color ColorFor(DominationObjectivePoint point) => point.Contested ? new Color(1f, .8f, .25f)
        : point.OwnerUsec.HasValue ? point.OwnerUsec.Value ? ArenaTeamPalette.UsecUi : ArenaTeamPalette.BearUi
        : new Color(.85f, .88f, .9f);

    private void Forget(Component view)
    {
        if (ReferenceEquals(view, null) || !_views.TryGetValue(view, out var markers)) return;
        Clear(view, markers);
        _views.Remove(view);
    }

    private void Clear(Component view, List<Component> markers)
    {
        foreach (var marker in markers)
        {
            if (marker == null) continue;
            try
            {
                if (view != null) _api.Remove.Invoke(view, new object[] { marker });
                else Hide(marker);
            }
            catch (Exception ex)
            {
                // Never let optional UI cleanup interrupt match/profile teardown.
                // Hide only our object; native UnloadMap still owns its registry.
                Hide(marker);
                Warn(ex);
            }
        }
        markers.Clear();
    }

    private void Hide(Component marker)
    {
        // A foreign removal hook may have invalidated the object before throwing.
        // Even the fallback must not escape into native unload or profile cleanup.
        try { if (marker != null) marker.gameObject.SetActive(false); }
        catch (Exception ex) { Warn(ex); }
    }

    private void Fail(Exception ex)
    {
        _failed = true;
        Warn(ex);
        Dispose();
    }

    private void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Domination map markers unavailable: " + ex.GetBaseException().Message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(_current, this)) _current = null;
        foreach (var pair in _views) Clear(pair.Key, pair.Value);
        _views.Clear();
        _points = Array.Empty<DominationObjectivePoint>();
        if (_patchAttempted)
        {
            _patchAttempted = false;
            try { _patches.UnpatchSelf(); } catch (Exception ex) { Warn(ex); }
        }
    }

    // Bind public, exact contracts before installing either hook. No reference to
    // DynamicMaps.dll, private field reads or changes to its global categories.
    private sealed class Api
    {
        internal readonly Type ViewType, DefinitionType;
        internal readonly MethodInfo Load, Unload, Add, Remove;
        internal readonly PropertyInfo CurrentMap, MapNames, Text, Category, ImagePath, DefPosition,
            Position, Color, Size, Label, LabelAlpha;
        internal readonly object OnTop;

        internal Api(Assembly assembly)
        {
            ViewType = assembly.GetType("DynamicMaps.UI.Components.MapView", true);
            var marker = assembly.GetType("DynamicMaps.UI.Components.MapMarker", true);
            var map = assembly.GetType("DynamicMaps.Data.MapDef", true);
            DefinitionType = assembly.GetType("DynamicMaps.Data.MapMarkerDef", true);
            var layerStatus = assembly.GetType("DynamicMaps.UI.Components.LayerStatus", true);
            if (!typeof(Component).IsAssignableFrom(ViewType) || !typeof(Component).IsAssignableFrom(marker)
                || !layerStatus.IsEnum || DefinitionType.GetConstructor(Type.EmptyTypes) == null)
                throw new NotSupportedException("DynamicMaps marker types changed.");
            OnTop = Enum.Parse(layerStatus, "OnTop");
            Load = Method(ViewType, "LoadMap", typeof(void), map, typeof(RectTransform));
            Unload = Method(ViewType, "UnloadMap", typeof(void));
            Add = Method(ViewType, "AddMapMarker", marker, DefinitionType);
            Remove = Method(ViewType, "RemoveMapMarker", typeof(void), marker);
            CurrentMap = Property(ViewType, "CurrentMapDef", map);
            MapNames = Property(map, "MapInternalNames", typeof(List<string>));
            Text = Property(DefinitionType, "Text", typeof(string), true);
            Category = Property(DefinitionType, "Category", typeof(string), true);
            ImagePath = Property(DefinitionType, "ImagePath", typeof(string), true);
            DefPosition = Property(DefinitionType, "Position", typeof(Vector3), true);
            Position = Property(marker, "Position", typeof(Vector3), true);
            Color = Property(marker, "Color", typeof(Color), true);
            Size = Property(marker, "Size", typeof(Vector2), true);
            Label = Property(marker, "Label", typeof(TextMeshProUGUI));
            LabelAlpha = Property(marker, "LabelAlphaLayerStatus", typeof(Dictionary<,>).MakeGenericType(layerStatus, typeof(float)));
        }

        private static MethodInfo Method(Type type, string name, Type returns, params Type[] args)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, args, null);
            if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static PropertyInfo Property(Type type, string name, Type expected, bool writable = false)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType != expected || property.GetGetMethod() == null
                || writable && property.GetSetMethod() == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
