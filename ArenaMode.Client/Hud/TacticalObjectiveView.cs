using ArenaMode.Modes;
using EFT.CameraControl;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Hud;

/// <summary>
/// The capture point of a Tactical round in the world (docs/reviews/TACTICAL-OBJECTIVE-20261108.md):
/// a ring on the floor at the capture radius, a beam above it and a label, tinted by the side
/// that holds it. No assets, no colliders (the pickup geometry queries ignore nothing here), the
/// same low-luminance world palette as the Domination ring because EFT's exposure washes the HUD
/// colours toward white. Created only for an active point and destroyed with the round.
/// </summary>
internal sealed class TacticalObjectiveView : IDisposable
{
    private readonly GameObject _root;
    private readonly Material _material;
    private readonly LineRenderer _ring, _beam;
    private readonly TextMeshPro _label;
    private readonly bool _vertexTint;

    internal TacticalObjectiveView(Vector3 floor, float radius)
    {
        _root = new GameObject("Arena Tactical capture point");
        _root.transform.position = floor;
        try
        {
            var shader = Shader.Find("Sprites/Default");
            _vertexTint = shader != null;
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("capture point shader unavailable");
            _material = new Material(shader) { color = Color.white };

            _ring = _root.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false; _ring.loop = true; _ring.positionCount = 64;
            _ring.startWidth = _ring.endWidth = .12f;
            _ring.sharedMaterial = _material;
            _ring.shadowCastingMode = ShadowCastingMode.Off; _ring.receiveShadows = false;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64f;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .07f, Mathf.Sin(angle) * radius));
            }

            var beam = new GameObject("Capture point beam");
            beam.transform.SetParent(_root.transform, false);
            _beam = beam.AddComponent<LineRenderer>();
            _beam.useWorldSpace = false; _beam.positionCount = 2;
            _beam.SetPosition(0, new Vector3(0f, .1f, 0f));
            _beam.SetPosition(1, new Vector3(0f, 24f, 0f));
            _beam.startWidth = .3f; _beam.endWidth = .04f;
            _beam.sharedMaterial = _material;
            _beam.shadowCastingMode = ShadowCastingMode.Off; _beam.receiveShadows = false;

            var label = new GameObject("Capture point label");
            label.transform.SetParent(_root.transform, false);
            label.transform.localPosition = Vector3.up * 2.2f;
            _label = label.AddComponent<TextMeshPro>();
            _label.font = HudStyle.Font; _label.fontSize = 2.5f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.richText = false; _label.enableWordWrapping = false;
            _label.rectTransform.sizeDelta = new Vector2(2.5f, .8f);
            _label.text = "POINT";
            _root.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    private static Color Dim(Color ui) => new Color(ui.r * .25f, ui.g * .25f, ui.b * .25f, .9f);

    internal static Color WorldColor(TacticalObjectiveSide holder, bool contested)
        => contested ? new Color(.25f, .2f, 0f, .9f)
        : holder == TacticalObjectiveSide.Friendly ? Dim(Effects.ArenaTeamPalette.FriendlyUi)
        : holder == TacticalObjectiveSide.Enemy ? Dim(Effects.ArenaTeamPalette.OpponentUi)
        : new Color(.08f, .08f, .08f, .9f);

    internal static Color LabelColor(TacticalObjectiveSide holder, bool contested)
        => contested ? new Color(1f, .8f, .25f)
        : holder == TacticalObjectiveSide.Friendly ? Effects.ArenaTeamPalette.FriendlyUi
        : holder == TacticalObjectiveSide.Enemy ? Effects.ArenaTeamPalette.OpponentUi
        : new Color(.85f, .88f, .9f);

    internal void Tick(TacticalObjectiveSide holder, bool contested, float progress)
    {
        if (_root == null) return;
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        _root.SetActive(camera != null);
        var color = WorldColor(holder, contested);
        // UnitySprites.cginc multiplies vertex tint by the material tint: keep the material white
        // there; Unlit/Color uses the material colour directly.
        _material.color = _vertexTint ? Color.white : color;
        _ring.startColor = _ring.endColor = color;
        _beam.startColor = color;
        _beam.endColor = new Color(color.r, color.g, color.b, 0f);
        _label.color = LabelColor(holder, contested);
        _label.text = contested ? "POINT contested"
            : holder == TacticalObjectiveSide.None || progress <= 0f ? "POINT" : "POINT " + (int)(progress * 100f) + "%";
        if (camera != null) _label.transform.rotation = camera.transform.rotation;
    }

    public void Dispose()
    {
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
    }
}
