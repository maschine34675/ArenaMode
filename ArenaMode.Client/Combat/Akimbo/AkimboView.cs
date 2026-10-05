using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Owns only fresh transforms/mesh renderers. Never clones or destroys a pooled native component.</summary>
internal sealed class AkimboView : IDisposable
{
    private readonly List<(Transform Source, Transform Copy)> _nodes = new();
    private readonly List<(MeshRenderer Source, MeshRenderer Copy)> _renderers = new();
    private readonly Transform _source;
    private GameObject _root, _flash;
    private Material _flashMaterial;
    private float _flashUntil;
    internal bool Visible { get; private set; }
    internal Transform Fireport { get; private set; }
    internal int RendererCount => _renderers.Count;

    internal AkimboView(Player.FirearmController firearm)
    {
        _source = firearm.HandsHierarchy?.GetTransform(ECharacterWeaponBones.Weapon_root);
        if (_source == null || firearm.CurrentFireport?.Original == null) throw new InvalidOperationException("Weapon hierarchy missing");
        try
        {
            _root = new GameObject("Arena Akimbo PM replica");
            _root.SetActive(false);
            Clone(_source, _root.transform, firearm.CurrentFireport.Original);
            if (_renderers.Count == 0 || Fireport == null) throw new InvalidOperationException("No supported pistol meshes/fireport");
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("Muzzle flash shader unavailable");
            _flashMaterial = new Material(shader) { color = new Color(1f, .8f, .3f, 1f) };
            _flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _flash.name = "Arena Akimbo muzzle flash";
            var collider = _flash.GetComponent<Collider>();
            collider.enabled = false; UnityEngine.Object.Destroy(collider);
            _flash.transform.SetParent(Fireport, false);
            _flash.transform.localScale = Vector3.one * .035f;
            _flash.layer = firearm.CurrentFireport.Original.gameObject.layer;
            var renderer = _flash.GetComponent<Renderer>();
            renderer.sharedMaterial = _flashMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            _flash.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    private void Clone(Transform source, Transform target, Transform fireport)
    {
        if (_nodes.Count >= 512) throw new InvalidOperationException("Pistol hierarchy too large");
        _nodes.Add((source, target));
        target.gameObject.layer = source.gameObject.layer;
        if (ReferenceEquals(source, fireport)) Fireport = target;
        if (source.GetComponent<SkinnedMeshRenderer>() != null)
            throw new InvalidOperationException("Skinned weapon geometry is outside the initial PM prototype");
        var original = source.GetComponent<MeshRenderer>();
        var filter = source.GetComponent<MeshFilter>();
        if (original != null && filter?.sharedMesh != null)
        {
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = target.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = original.sharedMaterials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = original.receiveShadows;
            copy.lightProbeUsage = original.lightProbeUsage;
            copy.reflectionProbeUsage = original.reflectionProbeUsage;
            _renderers.Add((original, copy));
        }
        for (int i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            var go = new GameObject(child.name);
            go.transform.SetParent(target, false);
            Clone(child, go.transform, fireport);
        }
    }

    internal bool Matches(Player.FirearmController firearm)
        => _root != null && _source != null && Fireport != null
            && ReferenceEquals(_source, firearm.HandsHierarchy?.GetTransform(ECharacterWeaponBones.Weapon_root));

    internal void Pose(Vector3 offset, bool visible)
    {
        if (_root == null) return;
        Visible = visible;
        _root.SetActive(visible);
        if (!visible) return;
        _root.transform.SetPositionAndRotation(_source.position + offset, _source.rotation);
        _root.transform.localScale = _source.lossyScale;
        for (int i = 1; i < _nodes.Count; i++)
        {
            var pair = _nodes[i];
            if (pair.Source == null) throw new InvalidOperationException("Pistol hierarchy changed");
            pair.Copy.localPosition = pair.Source.localPosition;
            pair.Copy.localRotation = pair.Source.localRotation;
            pair.Copy.localScale = pair.Source.localScale;
            pair.Copy.gameObject.SetActive(pair.Source.gameObject.activeSelf);
        }
        foreach (var pair in _renderers)
            pair.Copy.enabled = pair.Source != null && pair.Source.enabled && pair.Source.isVisible;
        _flash.SetActive(Time.time < _flashUntil);
    }

    internal void Flash() => _flashUntil = Time.time + .055f;
    public void Dispose()
    {
        Visible = false;
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_flashMaterial != null) UnityEngine.Object.Destroy(_flashMaterial);
        _root = _flash = null; _flashMaterial = null; Fireport = null;
        _nodes.Clear(); _renderers.Clear();
    }
}
