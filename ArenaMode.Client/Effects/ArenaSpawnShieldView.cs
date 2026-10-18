using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Effects;

/// <summary>Private world-space shield cage. No pooled bot meshes, materials, lights or colliders are changed.</summary>
internal sealed class ArenaSpawnShieldView : IDisposable
{
    private GameObject _root;
    private Material _material;

    internal ArenaSpawnShieldView(Transform world)
        : this(world, "ArenaBotSpawnShield", new Color(.65f, 1f, 1f, .85f), .52f, .98f) { }

    // Each owner retains its own view. Concentric pickup cages must never recolor
    // or dispose an independently active spawn-protection cage.
    internal ArenaSpawnShieldView(Transform world, string name, Color color, float radius, float halfHeight)
    {
        try
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader missing");
            _material = new Material(shader) { name = name + "Material" };
            _root = new GameObject(name);
            _root.transform.SetParent(world, false);
            Ring(0, color, radius, halfHeight); Ring(1, color, radius, halfHeight); Ring(2, color, radius, halfHeight);
        }
        catch { Dispose(); throw; }
    }

    private void Ring(int plane, Color color, float radius, float halfHeight)
    {
        var ring = new GameObject("ShieldRing" + plane);
        ring.transform.SetParent(_root.transform, false);
        var line = ring.AddComponent<LineRenderer>();
        line.sharedMaterial = _material;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 48;
        line.startWidth = line.endWidth = .035f;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = 2f * Mathf.PI * i / line.positionCount;
            float x = radius * Mathf.Cos(angle), y = Mathf.Sin(angle);
            line.SetPosition(i, plane == 0 ? new Vector3(x, .08f, radius * y)
                : plane == 1 ? new Vector3(x, .04f + halfHeight + halfHeight * y, 0f)
                : new Vector3(0f, .04f + halfHeight + halfHeight * y, x));
        }
    }

    internal void Update(Vector3 position)
    {
        if (_root != null) _root.transform.position = position;
    }

    public void Dispose()
    {
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        _root = null; _material = null;
    }
}
