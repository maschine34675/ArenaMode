#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ArenaMode.Combat.Fire;

/// <summary>
/// Original bottle art on exact item instances. The installed grenade prefab supplies only
/// its native rig/physics; neither that prefab nor any of its shared meshes/materials is edited.
/// </summary>
internal static class ArenaMolotovModel
{
    private static bool _warned;
    private static bool InArena => (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid)
        && ArenaFireSupply.HasVerifiedTemplate;

    internal static void Enable()
    {
        new ItemModelPatch().Enable();
        new ThrownModelPatch().Enable();
        new PoolReturnPatch().Enable();
        new PoolGetPatch().Enable();
    }

    // The armed exception is presentation only: kit models/icons are also built while loading.
    // The server never supplies this private template to the permanent profile or ordinary bots.
    internal static Task<GameObject> Wrap(Task<GameObject> pending, Item item)
        => pending != null && InArena && item?.TemplateId == ArenaMolotovItem.TemplateId
            ? Complete(pending) : pending;

    private static async Task<GameObject> Complete(Task<GameObject> pending)
    {
        // Preserve Unity's synchronization context and the original fault/cancellation behavior.
        GameObject instance = await pending;
        try { if (InArena) ApplyItem(instance); }
        catch (Exception ex) { Warn(ex.Message); }
        return instance;
    }

    internal static void ApplyItem(GameObject instance)
    {
        if (instance == null) return;
        var prefab = instance.GetComponent<GrenadePrefab>();
        // Role models and queued pool objects have IsInPool=true; never decorate them.
        if (prefab == null || prefab.IsInPool || prefab.IsStub || prefab.Hierarchy == null) return;
        MeshFilter body = prefab.Hierarchy.GetComponentsInChildren<MeshFilter>(true)
            .FirstOrDefault(x => x.name == "weapon_grenade_rgd5_LOD0");
        if (body == null) { Warn("native hand mesh was not found"); return; }
        var siblings = body.transform.parent.GetComponentsInChildren<MeshRenderer>(true);
        Apply(instance, body, siblings.Where(x => x.gameObject != body.gameObject).ToArray());
    }

    internal static void ApplyThrown(Grenade grenade)
    {
        if (!InArena || grenade == null || grenade.WeaponSource?.TemplateId != ArenaMolotovItem.TemplateId) return;
        try
        {
            MeshFilter body = grenade.GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(x => x.name == "weapon_rgd5_world(Clone)");
            if (body == null) { Warn("native projectile mesh was not found"); return; }
            Apply(grenade.gameObject, body, Array.Empty<MeshRenderer>());
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private static void Apply(GameObject instance, MeshFilter body, MeshRenderer[] hidden)
    {
        var lease = instance.GetComponent<ArenaMolotovModelLease>();
        if (lease != null && lease.Active) return;
        try
        {
            if (lease == null) lease = instance.AddComponent<ArenaMolotovModelLease>();
            lease.Apply(body, hidden);
        }
        catch (Exception ex)
        {
            lease?.Restore();
            Warn(ex.Message);
        }
    }

    internal static void Restore(AssetPoolObject instance)
    {
        // Presentation must not prevent native pool retirement or acquisition.
        try { if (instance != null) instance.GetComponent<ArenaMolotovModelLease>()?.Restore(); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    internal static void Warn(string reason)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena Molotov bottle presentation unavailable; native item mechanics retained: " + reason);
    }

    private sealed class ItemModelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.GetDeclaredMethods(typeof(ObjectsFactory))
            .Single(m => m.Name == "CreateItemAsync" && m.ReturnType == typeof(Task<GameObject>)
                && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(Item));
        [PatchPostfix]
        private static void Postfix(Item item, ref Task<GameObject> __result) => __result = Wrap(__result, item);
    }

    private sealed class ThrownModelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.BaseGrenadeHandsController),
            nameof(Player.BaseGrenadeHandsController.CreateAndRegisterGrenade),
            new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
        [PatchPostfix]
        private static void Postfix(Grenade __result) => ApplyThrown(__result);
    }

    private sealed class PoolReturnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(AssetPoolObject),
            nameof(AssetPoolObject.ReturnToPool), Type.EmptyTypes);
        [PatchPrefix]
        private static void Prefix(AssetPoolObject __instance) => Restore(__instance);
    }

    private sealed class PoolGetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(AssetPoolObject),
            nameof(AssetPoolObject.OnGetFromPool), Type.EmptyTypes);
        // Additional protection if a native/custom pool path skipped its normal return callback.
        [PatchPrefix]
        private static void Prefix(AssetPoolObject __instance) => Restore(__instance);
    }
}

/// <summary>Lives on a single generated item/projectile, never on a prefab or role model.</summary>
internal sealed class ArenaMolotovModelLease : MonoBehaviour
{
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private Mesh _originalMesh, _mesh;
    private Material[] _originalMaterials, _materials;
    private readonly List<MeshRenderer> _hidden = new();
    internal bool Active => _mesh != null || _materials != null || _hidden.Count != 0;

    internal void Apply(MeshFilter filter, MeshRenderer[] hidden)
    {
        if (Active || filter.sharedMesh == null) return;
        _filter = filter;
        _renderer = filter.GetComponent<MeshRenderer>();
        if (_renderer == null) throw new InvalidOperationException("body renderer unavailable");
        _originalMesh = filter.sharedMesh;
        _originalMaterials = _renderer.sharedMaterials;
        // The projectile mesh has a baked -39cm Y offset which its Transform cancels.
        // Retain the existing local mesh center instead of assuming hand/world pivots match.
        _mesh = BottleMesh(_originalMesh.bounds.center - new Vector3(0, 0, .04753137f));
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) throw new InvalidOperationException("bottle material shader unavailable");
        _materials = new Material[3];
        _materials[0] = MakeMaterial(shader, "Arena original amber bottle", new Color(.25f, .105f, .025f), .65f);
        _materials[1] = MakeMaterial(shader, "Arena original cloth wick", new Color(.67f, .58f, .39f), .1f);
        _materials[2] = MakeMaterial(shader, "Arena original hazard label", new Color(.92f, .39f, .04f), .15f);
        _filter.sharedMesh = _mesh;
        _renderer.sharedMaterials = _materials;
        foreach (var r in hidden)
            if (r != null && r.enabled) { _hidden.Add(r); r.enabled = false; }
    }

    private static Material MakeMaterial(Shader shader, string name, Color color, float gloss)
    {
        var material = new Material(shader) { name = name, color = color };
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", gloss);
        return material;
    }

    internal void Restore()
    {
        try
        {
            // Don't overwrite a later third-party replacement. Shared source assets were never edited.
            if (_filter != null && _filter.sharedMesh == _mesh) _filter.sharedMesh = _originalMesh;
            if (_renderer != null && _materials != null)
            {
                var current = _renderer.sharedMaterials;
                if (current.Length == _materials.Length && current.SequenceEqual(_materials))
                    _renderer.sharedMaterials = _originalMaterials;
                else
                {
                    // Other mods can replace just one material. Remove every remaining owned
                    // reference before disposal while keeping their individual foreign slots.
                    for (int i = 0; i < current.Length; i++)
                        if (_materials.Contains(current[i])) current[i] = _originalMaterials.Length == 0
                            ? null : _originalMaterials[Math.Min(i, _originalMaterials.Length - 1)];
                    _renderer.sharedMaterials = current;
                }
            }
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
        }
        finally
        {
            // If an external/native setter threw, retain only resources still bound to a live
            // renderer for the pool-get/OnDestroy retry; never destroy an asset still in use.
            if (_filter == null || _filter.sharedMesh != _mesh)
            {
                if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
                _mesh = null;
            }
            var attached = _renderer != null ? _renderer.sharedMaterials : Array.Empty<Material>();
            if (_materials != null)
            {
                foreach (var material in _materials)
                    if (material != null && !attached.Contains(material)) UnityEngine.Object.Destroy(material);
                if (!_materials.Any(x => x != null && attached.Contains(x))) _materials = null;
            }
            if (!Active)
            {
                _filter = null; _renderer = null; _originalMesh = null; _originalMaterials = null;
            }
        }
    }

    private void OnDestroy()
    {
        try { Restore(); }
        catch (Exception ex) { ArenaMolotovModel.Warn(ex.Message); }
    }

    /// <summary>Original lathed bottle, bent cloth wick and a raised orange label. Dimensions in metres.</summary>
    internal static Mesh BottleMesh(Vector3 offset)
    {
        const int segments = 28;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var glass = new List<int>();
        var cloth = new List<int>();
        var label = new List<int>();
        float[] heights = { -.045f, -.042f, -.034f, .055f, .078f, .09f, .127f, .132f, .138f, .141f };
        float[] radii = { .027f, .032f, .033f, .033f, .022f, .012f, .011f, .014f, .014f, .010f };
        for (int ring = 0; ring < heights.Length; ring++)
            for (int s = 0; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2 / segments;
                vertices.Add(offset + new Vector3(Mathf.Cos(angle) * radii[ring], Mathf.Sin(angle) * radii[ring], heights[ring]));
                uv.Add(new Vector2((float)s / segments, (heights[ring] + .045f) / .186f));
                if (ring > 0 && s < segments)
                {
                    int a = (ring - 1) * (segments + 1) + s, b = ring * (segments + 1) + s;
                    glass.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            }
        // Solid base and dark glass top under the cloth, avoiding open geometry in inspection.
        for (int s = 1; s < segments - 1; s++)
        {
            glass.AddRange(new[] { 0, s + 1, s });
            int top = (heights.Length - 1) * (segments + 1);
            glass.AddRange(new[] { top, top + s, top + s + 1 });
        }
        Vector3[] wick = { new(0, 0, .132f), new(.004f, -.002f, .150f), new(.016f, -.006f, .164f), new(.026f, -.009f, .175f) };
        for (int ring = 0; ring < wick.Length; ring++)
            for (int side = 0; side <= 8; side++)
            {
                float angle = side * Mathf.PI / 4;
                int start = vertices.Count;
                vertices.Add(offset + wick[ring] + new Vector3(Mathf.Cos(angle) * .005f, Mathf.Sin(angle) * .005f, 0));
                uv.Add(new Vector2(side / 8f, ring / 3f));
                if (ring > 0 && side < 8) cloth.AddRange(new[] { start - 9, start - 8, start, start - 8, start + 1, start });
            }
        // Label wraps around the straight bottle body and leaves two glass gaps at its edges.
        const int labelSegments = 16;
        int labelStart = vertices.Count;
        for (int side = 0; side <= labelSegments; side++)
        {
            float angle = -.85f + side * 1.7f / labelSegments;
            for (int end = 0; end < 2; end++)
            {
                vertices.Add(offset + new Vector3(Mathf.Cos(angle) * .03325f, Mathf.Sin(angle) * .03325f, -.011f + end * .047f));
                uv.Add(new Vector2(side / (float)labelSegments, end));
            }
            if (side < labelSegments)
            {
                int a = labelStart + side * 2;
                label.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });
            }
        }
        var mesh = new Mesh { name = "Arena original Molotov bottle", subMeshCount = 3 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
        mesh.SetTriangles(glass, 0); mesh.SetTriangles(cloth, 1); mesh.SetTriangles(label, 2);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
