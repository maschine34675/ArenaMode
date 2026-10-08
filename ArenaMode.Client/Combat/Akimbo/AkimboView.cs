using EFT;
using EFT.AssetsManager;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Two visual-only meshes. Native transforms/animator/events remain untouched.</summary>
internal sealed class AkimboView : IDisposable
{
    private sealed class Node
    {
        internal Transform Source, Parent, Right, Left;
        internal Vector3 RestPosition;
        internal Quaternion RestRotation;
    }
    private sealed class Mesh
    {
        internal MeshRenderer Source, Right, Left;
        internal Node Node;
        internal bool Hidden, WasHidden;
    }
    private readonly List<Node> _nodes = new();
    private readonly List<Mesh> _renderers = new();
    private readonly Dictionary<Transform, Node> _copies = new();
    private readonly AkimboPresentation _animation = new();
    private readonly Transform _source;
    private GameObject _root, _rightRoot, _rightFlash, _leftFlash;
    private Material _flashMaterial;
    private Vector3 _restPosition;
    private Quaternion _restRotation;
    private Transform _camera;
    private bool _hasRest;
    private int _poseFrame = -1;
    private readonly Vector3[] _gripPosition = new Vector3[2];
    private readonly Quaternion[] _gripRotation = new Quaternion[2];
    private readonly bool[] _hasGrip = new bool[2];
    internal bool Visible { get; private set; }
    internal Transform Fireport { get; private set; }
    internal Transform RightFireport { get; private set; }
    internal int RendererCount => _renderers.Count * 2;

    internal AkimboView(Player.FirearmController firearm)
    {
        _source = firearm.HandsHierarchy?.GetTransform(ECharacterWeaponBones.Weapon_root);
        if (_source == null || firearm.CurrentFireport?.Original == null)
            throw new InvalidOperationException("Weapon hierarchy missing");
        try
        {
            _root = new GameObject("Arena Akimbo left pistol");
            _rightRoot = new GameObject("Arena Akimbo right pistol");
            _root.SetActive(false); _rightRoot.SetActive(false);
            Clone(_source, _rightRoot.transform, _root.transform);
            if (_renderers.Count == 0) throw new InvalidOperationException("No supported pistol meshes");
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("Muzzle flash shader unavailable");
            _flashMaterial = new Material(shader) { color = new Color(1f, .8f, .3f, 1f) };
            _leftFlash = MakeFlash(firearm.CurrentFireport.Original.gameObject.layer);
            _rightFlash = MakeFlash(firearm.CurrentFireport.Original.gameObject.layer);
            UseFireport(firearm.CurrentFireport.Original);
        }
        catch { Dispose(); throw; }
    }

    private GameObject MakeFlash(int layer)
    {
        var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "Arena Akimbo muzzle flash";
        flash.SetActive(false);
        flash.transform.SetParent(_root.transform, false);
        var collider = flash.GetComponent<Collider>();
        collider.enabled = false; UnityEngine.Object.Destroy(collider);
        flash.transform.localScale = Vector3.one * .035f;
        flash.layer = layer;
        var renderer = flash.GetComponent<Renderer>();
        renderer.sharedMaterial = _flashMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return flash;
    }

    private void Clone(Transform source, Transform right, Transform left)
    {
        if (_nodes.Count >= 512) throw new InvalidOperationException("Pistol hierarchy too large");
        var node = new Node { Source = source, Parent = source.parent, Right = right, Left = left,
            RestPosition = source.localPosition, RestRotation = source.localRotation };
        _nodes.Add(node); _copies.Add(source, node);
        right.gameObject.layer = left.gameObject.layer = source.gameObject.layer;
        if (source.GetComponent<SkinnedMeshRenderer>() != null)
            throw new InvalidOperationException("Unsupported skinned pistol geometry");
        var original = source.GetComponent<MeshRenderer>();
        var filter = source.GetComponent<MeshFilter>();
        if (original != null && filter?.sharedMesh != null)
        {
            MeshRenderer Copy(Transform target)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = target.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = original.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = original.receiveShadows;
                copy.lightProbeUsage = original.lightProbeUsage;
                copy.reflectionProbeUsage = original.reflectionProbeUsage;
                return copy;
            }
            _renderers.Add(new Mesh { Source = original, Node = node, Right = Copy(right), Left = Copy(left) });
        }
        for (int i = 0; i < source.childCount; i++)
        {
            var child = source.GetChild(i);
            // Live cartridges/shells are reparented and returned to native pools
            // while firing. Never copy or take renderer ownership of them.
            if (child.GetComponent<AmmoPoolObject>() != null) continue;
            var r = new GameObject(child.name); r.transform.SetParent(right, false);
            var l = new GameObject(child.name); l.transform.SetParent(left, false);
            Clone(child, r.transform, l.transform);
        }
    }

    internal bool Matches(Player.FirearmController firearm)
        => _root != null && _rightRoot != null && _source != null && Fireport != null
            && ReferenceEquals(_source, firearm.HandsHierarchy?.GetTransform(ECharacterWeaponBones.Weapon_root));

    internal void UseFireport(Transform source)
    {
        if (source == null || !_copies.TryGetValue(source, out var node) || node.Left == null || node.Right == null)
            throw new InvalidOperationException("Pistol muzzle is outside the owned model");
        Fireport = node.Left; RightFireport = node.Right;
        _leftFlash.transform.SetParent(Fireport, false); _leftFlash.transform.localPosition = Vector3.zero;
        _rightFlash.transform.SetParent(RightFireport, false); _rightFlash.transform.localPosition = Vector3.zero;
    }

    // Camera-relative idle pose excludes the shared native firing recoil. The
    // camera still receives native recoil; only the two visual guns are separate.
    internal void Pose(Transform camera, Vector3 offset, bool visible, bool firing)
    {
        if (_root == null || _rightRoot == null) return;
        if (!visible || camera == null) { Hide(); return; }
        if (_poseFrame == Time.frameCount && Visible && ReferenceEquals(camera, _camera)) return;
        bool idle = _animation.CaptureIdle(firing, Time.time);
        if (!_hasRest || !ReferenceEquals(camera, _camera))
        {
            if (!idle) { Hide(); return; }
            _camera = camera; _hasRest = true;
            _root.transform.SetParent(camera, true); _rightRoot.transform.SetParent(camera, true);
            Array.Clear(_hasGrip, 0, _hasGrip.Length);
        }
        if (idle)
        {
            _restPosition = camera.InverseTransformPoint(_source.position);
            _restRotation = Quaternion.Inverse(camera.rotation) * _source.rotation;
        }
        Visible = true; _root.SetActive(true); _rightRoot.SetActive(true);
        Vector3 position = camera.TransformPoint(_restPosition);
        Quaternion rotation = camera.rotation * _restRotation;
        Place(_rightRoot.transform, position, rotation, false);
        Place(_root.transform, position + offset, rotation, true);
        float returning = 1f - Mathf.Exp(-35f * Time.deltaTime);
        for (int i = 1; i < _nodes.Count; i++)
        {
            var node = _nodes[i];
            if (!Owned(node)) throw new InvalidOperationException("Pistol hierarchy changed");
            if (idle) { node.RestPosition = node.Source.localPosition; node.RestRotation = node.Source.localRotation; }
            CopyPose(node, node.Right, false, idle, firing, returning);
            CopyPose(node, node.Left, true, idle, firing, returning);
        }
        foreach (var mesh in _renderers)
        {
            if (mesh.Source == null || !Owned(mesh.Node)) throw new InvalidOperationException("Pistol renderer changed");
            if (!mesh.Hidden) { mesh.WasHidden = mesh.Source.forceRenderingOff; mesh.Hidden = true; }
            mesh.Source.forceRenderingOff = true;
            // isVisible becomes false when we hide the original. Never feed that
            // back into our copies; native enabled/active hierarchy still applies.
            mesh.Right.enabled = mesh.Left.enabled = mesh.Source.enabled && !mesh.WasHidden;
        }
        _leftFlash.SetActive(_animation.Flash(true, Time.time));
        _rightFlash.SetActive(_animation.Flash(false, Time.time));
        _poseFrame = Time.frameCount;
    }

    private void Place(Transform target, Vector3 position, Quaternion rotation, bool left)
    {
        float kick = _animation.Kick(left, Time.time);
        target.SetPositionAndRotation(position - rotation * Vector3.forward * (.035f * kick),
            rotation * Quaternion.Euler(-7f * kick, 0, 0));
        target.localScale = _source.lossyScale;
    }
    private void CopyPose(Node node, Transform copy, bool left, bool idle, bool firing, float returning)
    {
        bool native = idle || _animation.SampleNative(left, firing, Time.time);
        copy.localPosition = native ? node.Source.localPosition : Vector3.Lerp(copy.localPosition, node.RestPosition, returning);
        copy.localRotation = native ? node.Source.localRotation : Quaternion.Slerp(copy.localRotation, node.RestRotation, returning);
        copy.localScale = node.Source.localScale;
        copy.gameObject.SetActive(node.Source.gameObject.activeSelf);
    }

    internal void Grip(int index, ref Vector3 position, ref Quaternion rotation, bool firing)
    {
        if (!Visible) return;
        if (_animation.CaptureIdle(firing, Time.time))
        {
            _gripPosition[index] = _source.InverseTransformPoint(position);
            _gripRotation[index] = Quaternion.Inverse(_source.rotation) * rotation;
            _hasGrip[index] = true;
        }
        if (!_hasGrip[index]) return;
        var target = index == 0 ? _root.transform : _rightRoot.transform;
        position = target.TransformPoint(_gripPosition[index]);
        rotation = target.rotation * _gripRotation[index];
    }
    internal void Shot(bool left) { _animation.Shot(left, Time.time); _poseFrame = -1; }
    internal void Hide()
    {
        Visible = false;
        if (_root != null) _root.SetActive(false);
        if (_rightRoot != null) _rightRoot.SetActive(false);
        RestoreNative();
        _animation.Clear(); _hasRest = false; _poseFrame = -1;
    }
    private void RestoreNative()
    {
        foreach (var mesh in _renderers)
        {
            if (!mesh.Hidden) continue;
            // Detached/pooled objects belong to their next native owner.
            if (mesh.Source != null && Owned(mesh.Node) && mesh.Source.forceRenderingOff)
                mesh.Source.forceRenderingOff = mesh.WasHidden;
            mesh.Hidden = false;
        }
    }
    private bool Owned(Node node)
    {
        while (node != null)
        {
            if (node.Source == null || _source == null) return false;
            if (!ReferenceEquals(node.Source.parent, node.Parent)) return false;
            if (ReferenceEquals(node.Source, _source)) return true;
            if (node.Parent == null) return false;
            if (!_copies.TryGetValue(node.Parent, out node)) return false;
        }
        return false;
    }
    public void Dispose()
    {
        Hide();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_rightRoot != null) UnityEngine.Object.Destroy(_rightRoot);
        if (_flashMaterial != null) UnityEngine.Object.Destroy(_flashMaterial);
        _root = _rightRoot = _leftFlash = _rightFlash = null; _flashMaterial = null;
        Fireport = RightFireport = null;
        _nodes.Clear(); _renderers.Clear(); _copies.Clear();
    }
}
