using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Akimbo;

/// <summary>
/// Disposable Arena geometry, never an inventory attachment or a native LaserBeam.
/// The latter needs prefab materials, a tactical-device owner and pooled resources.
/// Each beam follows the actual corrected shot ray and ends at the first obstacle.
/// </summary>
internal sealed class AkimboAimLaser : IDisposable
{
    private const float Distance = 100f;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly GameObject[] _roots = new GameObject[2];
    private readonly GameObject[] _points = new GameObject[2];
    private readonly LineRenderer[] _beams = new LineRenderer[2];
    private Material _material;

    internal AkimboAimLaser()
    {
        try
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("Akimbo laser shader unavailable");
            _material = new Material(shader);
            for (int i = 0; i < 2; i++)
            {
                var root = new GameObject(i == 0 ? "Arena Akimbo right aim laser" : "Arena Akimbo left aim laser");
                _roots[i] = root; root.SetActive(false);
                // World layer + ordinary depth testing: no overlay, wall visibility,
                // collider, light, item, native component or shared material write.
                root.layer = LayerMask.NameToLayer("Default");
                var beam = root.AddComponent<LineRenderer>();
                _beams[i] = beam;
                beam.useWorldSpace = true; beam.positionCount = 2;
                beam.startWidth = beam.endWidth = .0015f;
                beam.sharedMaterial = _material;
                beam.shadowCastingMode = ShadowCastingMode.Off; beam.receiveShadows = false;
                var point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _points[i] = point; point.name = "Arena Akimbo aim point";
                point.SetActive(false); point.layer = root.layer;
                point.transform.SetParent(root.transform, false);
                point.transform.localScale = Vector3.one * .018f;
                var collider = point.GetComponent<Collider>();
                collider.enabled = false; UnityEngine.Object.Destroy(collider);
                var renderer = point.GetComponent<Renderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
        }
        catch { Dispose(); throw; }
    }

    internal void Draw(int side, Vector3 camera, Vector3 origin, Vector3 direction,
        Vector3 visibleMuzzle, Transform self, Color color)
    {
        Hide(side);
        if (_roots[side] == null || self == null || !AkimboShotGeometry.Finite(camera)
            || !AkimboShotGeometry.Finite(origin) || !AkimboShotGeometry.Finite(direction)
            || !AkimboShotGeometry.Finite(visibleMuzzle) || direction.sqrMagnitude < .9f) return;
        int environment = (int)LayersMaskController.HighPolyWithTerrainMask;
        if (Physics.CheckSphere(origin, .025f, environment, QueryTriggerInteraction.Ignore)
            || Physics.CheckSphere(visibleMuzzle, .025f, environment, QueryTriggerInteraction.Ignore)
            || Physics.Linecast(camera, origin, environment, QueryTriggerInteraction.Ignore)
            || Physics.Linecast(camera, visibleMuzzle, environment, QueryTriggerInteraction.Ignore)) return;

        if (!Cast(origin, direction.normalized, Distance, self, out bool struck, out var hit)) return;
        Vector3 target = struck ? hit.point : origin + direction.normalized * Distance;
        Vector3 delta = target - visibleMuzzle;
        float length = delta.magnitude;
        if (!AkimboShotGeometry.Finite(target) || length < .005f) return;
        // Native FOV/back-offset correction and solved hands can put the visible
        // muzzle off the shot ray. A second bounded cast stops the drawn segment
        // at intervening geometry; the aiming aid never changes ballistics.
        if (!Cast(visibleMuzzle, delta / length, length, self, out bool clipped, out var obstruction)) return;
        if (clipped) { hit = obstruction; target = hit.point; struck = true; }
        _material.color = color;
        _beams[side].SetPosition(0, visibleMuzzle);
        _beams[side].SetPosition(1, target);
        if (struck)
        {
            _points[side].transform.position = target + hit.normal * .004f;
            _points[side].SetActive(true);
        }
        _roots[side].SetActive(true);
    }

    private bool Cast(Vector3 origin, Vector3 direction, float distance, Transform self,
        out bool struck, out RaycastHit nearest)
    {
        struck = false; nearest = default;
        int mask = (int)LayersMaskController.HighPolyWithTerrainMask | (int)LayersMaskController.HitColliderMask;
        int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance, mask, QueryTriggerInteraction.Collide);
        // An overflowing unsorted result may have omitted the nearer wall. Hide
        // this frame instead of drawing an unproven line through it.
        if (count >= _hits.Length) { Array.Clear(_hits, 0, _hits.Length); return false; }
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var candidate = _hits[i];
            _hits[i] = default; // Do not retain bodies/colliders past this query.
            if (candidate.collider == null || candidate.collider.transform.IsChildOf(self)
                || candidate.collider.isTrigger && ((1 << candidate.collider.gameObject.layer) & (int)LayersMaskController.HitColliderMask) == 0
                || !AkimboShotGeometry.Finite(candidate.point) || !AkimboShotGeometry.Finite(candidate.normal)
                || float.IsNaN(candidate.distance) || float.IsInfinity(candidate.distance)
                || candidate.distance < 0f || candidate.distance > distance || candidate.distance >= closest) continue;
            closest = candidate.distance; nearest = candidate; struck = true;
        }
        return true;
    }

    internal void Hide(int side)
    {
        if (_roots[side] != null) _roots[side].SetActive(false);
        if (_points[side] != null) _points[side].SetActive(false);
    }
    internal void Hide() { Hide(0); Hide(1); Array.Clear(_hits, 0, _hits.Length); }

    public void Dispose()
    {
        Hide();
        for (int i = 0; i < _roots.Length; i++)
        {
            if (_roots[i] != null) UnityEngine.Object.Destroy(_roots[i]);
            _roots[i] = _points[i] = null; _beams[i] = null;
        }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        _material = null;
    }
}
