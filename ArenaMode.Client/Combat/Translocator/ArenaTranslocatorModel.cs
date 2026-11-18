#nullable disable
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Combat.Translocator;

/// <summary>Original device geometry on native instance meshes; rig, optics and shared assets remain intact.</summary>
internal static class ArenaTranslocatorModel
{
    private static bool _enabled, _patched, _warned;
    private static int _lifetime;
    private static readonly List<WeakReference<ArenaTranslocatorModelLease>> Leases = new();
    private static bool InArena => _enabled && (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid)
        && ArenaTranslocatorSupply.HasVerifiedTemplate;

    internal static void Enable()
    {
        if (_enabled) return;
        if (!_patched)
        {
            new ArenaTranslocatorModelItemPatch().Enable();
            new ArenaTranslocatorModelPoolReturnPatch().Enable();
            new ArenaTranslocatorModelPoolGetPatch().Enable();
            _patched = true;
        }
        _enabled = true;
    }

    internal static void Shutdown()
    {
        _enabled = false;
        _lifetime++;
        for (int i = Leases.Count - 1; i >= 0; i--)
        {
            if (!Leases[i].TryGetTarget(out var lease) || lease == null) { Leases.RemoveAt(i); continue; }
            try { lease.Restore(); } catch (Exception ex) { Warn(ex.GetBaseException().Message); }
        }
    }

    internal static Task<GameObject> Wrap(Task<GameObject> pending, Item item)
        => pending != null && InArena && item?.TemplateId == ArenaTranslocatorItem.TemplateId
            ? Complete(pending, ArenaSession.ArmEpoch, _lifetime) : pending;

    private static async Task<GameObject> Complete(Task<GameObject> pending, int epoch, int lifetime)
    {
        GameObject instance = await pending; // Preserve native faults, cancellation and Unity context.
        try { if (InArena && ArenaSession.ArmEpoch == epoch && lifetime == _lifetime) ApplyItem(instance); }
        catch (Exception ex) { Warn(ex.GetBaseException().Message); }
        return instance;
    }

    internal static void ApplyItem(GameObject instance)
    {
        if (instance == null) return;
        var prefab = instance.GetComponent<WeaponPrefab>();
        if (prefab == null || prefab.IsInPool || prefab.IsStub || prefab.Hierarchy == null) return;
        // Names and local mesh bounds were read from both installed rangefinder
        // bundles. Replace every body LOD; never remove optics/controller nodes.
        var bodies = prefab.Hierarchy.GetComponentsInChildren<MeshFilter>(true)
            .Where(x => x.sharedMesh != null && (x.sharedMesh.name == "item_ranger_1500_LOD0"
                || x.sharedMesh.name == "item_ranger_1500_LOD1")).ToArray();
        var lease = instance.GetComponent<ArenaTranslocatorModelLease>();
        if (lease != null && lease.Active) return;
        if (bodies.Length == 0) { Warn("native device body meshes were not found"); return; }
        var lenses = prefab.Hierarchy.GetComponentsInChildren<MeshRenderer>(true)
            .Where(x => x.name == "linza" || x.name == "back_linza").ToArray();
        try
        {
            if (lease == null)
            {
                lease = instance.AddComponent<ArenaTranslocatorModelLease>();
                Leases.Add(new WeakReference<ArenaTranslocatorModelLease>(lease));
            }
            lease.Apply(bodies, lenses);
        }
        catch (Exception ex)
        {
            try { lease?.Restore(); } catch { /* Pool get/return retries any still-bound resources. */ }
            Warn(ex.GetBaseException().Message);
        }
    }

    internal static void Restore(AssetPoolObject instance)
    {
        try { if (instance != null) instance.GetComponent<ArenaTranslocatorModelLease>()?.Restore(); }
        catch (Exception ex) { Warn(ex.GetBaseException().Message); }
    }

    internal static void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Translocator presentation unavailable; device mechanics retained: " + reason);
    }

    private sealed class ArenaTranslocatorModelItemPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.GetDeclaredMethods(typeof(ObjectsFactory))
            .Single(m => m.Name == "CreateItemAsync" && m.ReturnType == typeof(Task<GameObject>)
                && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(Item));
        [PatchPostfix]
        private static void Postfix(Item item, ref Task<GameObject> __result) => __result = Wrap(__result, item);
    }
    private sealed class ArenaTranslocatorModelPoolReturnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(AssetPoolObject), nameof(AssetPoolObject.ReturnToPool), Type.EmptyTypes);
        [PatchPrefix] private static void Prefix(AssetPoolObject __instance) => Restore(__instance);
    }
    private sealed class ArenaTranslocatorModelPoolGetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(AssetPoolObject), nameof(AssetPoolObject.OnGetFromPool), Type.EmptyTypes);
        [PatchPrefix] private static void Prefix(AssetPoolObject __instance) => Restore(__instance);
    }
}

internal sealed class ArenaTranslocatorModelLease : MonoBehaviour
{
    private sealed class Part
    {
        internal MeshFilter Filter;
        internal MeshRenderer Renderer;
        internal Mesh Original, Mesh;
        internal Material[] OriginalMaterials, Materials;
        internal bool Active => Mesh != null || Materials != null;

        internal void Apply(MeshFilter filter, Shader shader)
        {
            Filter = filter;
            Renderer = filter.GetComponent<MeshRenderer>();
            if (Renderer == null) throw new InvalidOperationException("native device body renderer missing");
            Original = filter.sharedMesh;
            OriginalMaterials = Renderer.sharedMaterials;
            Mesh = DeviceMesh(Original.bounds.center);
            Materials = new Material[3];
            Materials[0] = MakeMaterial(shader, "Arena original Translocator receiver", new Color(.13f, .17f, .22f), false);
            Materials[1] = MakeMaterial(shader, "Arena original Translocator grip", new Color(.035f, .045f, .055f), false);
            Materials[2] = MakeMaterial(shader, "Arena original Translocator emitter", new Color(.1f, .85f, 1f), true);
            Filter.sharedMesh = Mesh;
            Renderer.sharedMaterials = Materials;
        }

        internal void Restore()
        {
            try
            {
                if (Filter != null && Filter.sharedMesh == Mesh) Filter.sharedMesh = Original;
                if (Renderer != null && Materials != null)
                {
                    var current = Renderer.sharedMaterials;
                    if (current.Length == Materials.Length && current.SequenceEqual(Materials)) Renderer.sharedMaterials = OriginalMaterials;
                    else
                    {
                        // Preserve foreign per-slot replacements, but release every
                        // remaining reference to an owned material before destruction.
                        for (int i = 0; i < current.Length; i++)
                            if (Materials.Contains(current[i])) current[i] = OriginalMaterials.Length == 0
                                ? null : OriginalMaterials[Math.Min(i, OriginalMaterials.Length - 1)];
                        Renderer.sharedMaterials = current;
                    }
                }
            }
            finally
            {
                if (Filter == null || Filter.sharedMesh != Mesh)
                {
                    if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
                    Mesh = null;
                }
                var attached = Renderer != null ? Renderer.sharedMaterials : Array.Empty<Material>();
                if (Materials != null)
                {
                    foreach (var material in Materials)
                        if (material != null && !attached.Contains(material)) UnityEngine.Object.Destroy(material);
                    if (!Materials.Any(x => x != null && attached.Contains(x))) Materials = null;
                }
            }
        }
    }

    private readonly List<Part> _parts = new();
    private readonly List<MeshRenderer> _hidden = new();
    internal bool Active => _parts.Count > 0 || _hidden.Count > 0;
    internal void Apply(MeshFilter[] bodies, MeshRenderer[] lenses)
    {
        if (Active) return;
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) throw new InvalidOperationException("device shader unavailable");
        foreach (var body in bodies)
        {
            var part = new Part();
            _parts.Add(part); // Retain ownership before any allocation or native setter can throw.
            part.Apply(body, shader);
        }
        foreach (var lens in lenses)
            if (lens != null && lens.enabled) { _hidden.Add(lens); lens.enabled = false; }
    }

    internal void Restore()
    {
        Exception failure = null;
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            try { part.Restore(); } catch (Exception ex) { failure = ex; }
            if (!part.Active) _parts.RemoveAt(i);
        }
        foreach (var lens in _hidden) if (lens != null) lens.enabled = true;
        _hidden.Clear();
        if (failure != null) throw failure;
    }
    private void OnDestroy()
    {
        try { Restore(); } catch (Exception ex) { ArenaTranslocatorModel.Warn(ex.GetBaseException().Message); }
    }

    private static Material MakeMaterial(Shader shader, string name, Color color, bool emissive)
    {
        var material = new Material(shader);
        try
        {
            material.name = name; material.color = color;
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .32f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", emissive ? .1f : .5f);
            if (emissive && material.HasProperty("_EmissionColor"))
            { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color); }
            return material;
        }
        catch { UnityEngine.Object.Destroy(material); throw; }
    }

    /// <summary>Original compact receiver/grip, circular emitter and two luminous side strips, in metres.</summary>
    internal static Mesh DeviceMesh(Vector3 center)
    {
        var vertices = new List<Vector3>();
        var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
        void Face(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = vertices.Count;
            vertices.AddRange(new[] { center + a, center + b, center + c, center + d });
            triangles[material].AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        void Box(int material, Vector3 offset, Vector3 half)
        {
            Vector3 V(float x, float y, float z) => offset + new Vector3(x * half.x, y * half.y, z * half.z);
            Face(material, V(-1,-1,1), V(1,-1,1), V(1,1,1), V(-1,1,1));
            Face(material, V(1,-1,-1), V(-1,-1,-1), V(-1,1,-1), V(1,1,-1));
            Face(material, V(1,-1,1), V(1,-1,-1), V(1,1,-1), V(1,1,1));
            Face(material, V(-1,-1,-1), V(-1,-1,1), V(-1,1,1), V(-1,1,-1));
            Face(material, V(-1,1,1), V(1,1,1), V(1,1,-1), V(-1,1,-1));
            Face(material, V(-1,-1,-1), V(1,-1,-1), V(1,-1,1), V(-1,-1,1));
        }
        Box(0, new Vector3(0, .008f, 0), new Vector3(.023f, .032f, .036f));
        Box(1, new Vector3(0, -.035f, -.012f), new Vector3(.017f, .026f, .019f));
        Box(2, new Vector3(-.0235f, .015f, 0), new Vector3(.001f, .004f, .025f));
        Box(2, new Vector3(.0235f, .015f, 0), new Vector3(.001f, .004f, .025f));
        const int segments = 24;
        for (int i = 0; i < segments; i++)
        {
            float a = 2 * Mathf.PI * i / segments, b = 2 * Mathf.PI * (i + 1) / segments;
            Vector3 V(float angle, float radius, float z) => new Vector3(Mathf.Cos(angle) * radius, .01f + Mathf.Sin(angle) * radius, z);
            Face(2, V(a,.025f,.042f), V(a,.031f,.042f), V(b,.031f,.042f), V(b,.025f,.042f));
            Face(0, V(a,.031f,.034f), V(b,.031f,.034f), V(b,.031f,.042f), V(a,.031f,.042f));
            Face(1, V(b,.025f,.034f), V(a,.025f,.034f), V(a,.025f,.042f), V(b,.025f,.042f));
        }
        var mesh = new Mesh { name = "Arena original Translocator device", subMeshCount = 3 };
        try
        {
            mesh.SetVertices(vertices);
            for (int i = 0; i < 3; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        catch { UnityEngine.Object.Destroy(mesh); throw; }
    }
}
