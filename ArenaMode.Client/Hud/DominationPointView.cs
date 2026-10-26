using ArenaMode.Modes;
using EFT.CameraControl;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaMode.Hud;

internal sealed class DominationPointView : IDisposable
{
    private readonly GameObject _root;
    private readonly Material _material;
    private readonly TextMeshPro _label;
    private readonly string _id;

    internal DominationPointView(string id, Vector3 floor, float radius)
    {
        _id = id;
        _root = new GameObject("Arena Domination point " + id);
        _root.transform.position = floor;
        try
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Domination marker shader unavailable.");
            _material = new Material(shader) { color = Color.white };
            var ring = _root.AddComponent<LineRenderer>();
            ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 64;
            ring.startWidth = ring.endWidth = .07f;
            ring.sharedMaterial = _material;
            ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .07f, Mathf.Sin(angle) * radius));
            }
            var label = new GameObject("Control point label");
            label.transform.SetParent(_root.transform, false);
            label.transform.localPosition = Vector3.up * 2.2f;
            _label = label.AddComponent<TextMeshPro>();
            _label.font = HudStyle.Font; _label.fontSize = 2.5f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.richText = false; _label.enableWordWrapping = false;
            _label.rectTransform.sizeDelta = new Vector2(1.5f, .8f);
            _root.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    internal static Color ColorFor(DominationPointState state) => state.Contested ? new Color(1f, .8f, .25f)
        : state.OwnerUsec.HasValue ? (state.OwnerUsec.Value ? Effects.ArenaTeamPalette.UsecUi : Effects.ArenaTeamPalette.BearUi)
        : new Color(.85f, .88f, .9f);

    internal static string Describe(string id, DominationPointState state, bool rich)
    {
        string text = id + "  " + (state.Contested ? "CONTESTED" : state.OwnerUsec.HasValue
            ? (state.OwnerUsec.Value ? "USEC" : "BEAR") : "NEUTRAL");
        if (!state.Contested && Math.Abs(state.CaptureProgress) > .001f && Math.Abs(state.CaptureProgress) < .999f)
            text += " " + (int)(Math.Abs(state.CaptureProgress) * 100f) + "%";
        else if (state.UnderAttack) text += " !";
        return rich ? "<color=#" + ColorUtility.ToHtmlStringRGB(ColorFor(state)) + ">" + text + "</color>" : text;
    }

    internal void Tick(DominationPointState state)
    {
        var camera = CameraManager.Exist ? CameraManager.Instance.Camera : null;
        if (_root == null) return;
        _root.SetActive(camera != null);
        _material.color = ColorFor(state);
        _label.color = ColorFor(state);
        // The HUD owns detailed progress. Keep the world marker compact so a
        // defending bot and nearby pickup labels remain readable at close range.
        _label.text = _id;
        if (camera != null) _label.transform.rotation = camera.transform.rotation;
    }

    public void Dispose()
    {
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
    }
}
