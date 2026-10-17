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
    {
        try
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader missing");
            _material = new Material(shader) { name = "ArenaBotSpawnShieldMaterial" };
            _root = new GameObject("ArenaBotSpawnShield");
            _root.transform.SetParent(world, false);
            Ring(0); Ring(1); Ring(2);
        }
        catch { Dispose(); throw; }
    }

    private void Ring(int plane)
    {
        var ring = new GameObject("ShieldRing" + plane);
        ring.transform.SetParent(_root.transform, false);
        var line = ring.AddComponent<LineRenderer>();
        line.sharedMaterial = _material;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 48;
        line.startWidth = line.endWidth = .035f;
        // Pale cyan/white is deliberately distinct from red/blue team identity.
        line.startColor = line.endColor = new Color(.65f, 1f, 1f, .85f);
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = 2f * Mathf.PI * i / line.positionCount;
            float x = .52f * Mathf.Cos(angle), y = Mathf.Sin(angle);
            line.SetPosition(i, plane == 0 ? new Vector3(x, .08f, .52f * y)
                : plane == 1 ? new Vector3(x, 1.02f + .98f * y, 0f)
                : new Vector3(0f, 1.02f + .98f * y, x));
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
