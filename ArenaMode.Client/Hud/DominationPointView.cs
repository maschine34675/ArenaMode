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
    private readonly Material _chipMaterial;
    private readonly TextMeshPro _label;
    private readonly GameObject _chip;
    private readonly LineRenderer _ring;
    private readonly bool _vertexTint;
    private readonly string _id;

    // World letter, vanilla maps only. The glyph keeps a constant apparent size: it should fill
    // GlyphScreenFraction of the screen height, i.e. subtend GlyphScreenFraction * fieldOfView degrees
    // (about 1.7 degrees at a 57 degree vertical FOV, 3 % of the screen). At camera distance d that
    // needs a glyph of 2 * d * tan(angle / 2) metres; a capital at label scale 1 and fontSize 2.5 is
    // about CapitalHeight metres high, so scale = 2 * d * tan(angle / 2) / CapitalHeight, clamped so
    // a letter next to the camera does not fill the view (min) and a far one stays a bounded quad (max).
    private const float GlyphScreenFraction = .03f;
    private const float CapitalHeight = .175f;
    private const float MinLetterScale = 1.5f;
    private const float MaxLetterScale = 14f;
    // The chip is a little larger than the glyph box and sits a hair behind it on the camera axis
    // (label +Z points away from the camera) so the two never z-fight and never hide the letter.
    private const float ChipWidth = .3f;
    private const float ChipHeight = .34f;
    private const float ChipDepth = .02f;
    // Near-white: the owner is carried by the chip, and the pastel owner tint clipped to white anyway.
    private static readonly Color LetterColor = new Color(.95f, .96f, .97f);

    // showLetter is false on the ported maps: their points are marked by the ring, the HUD row and the
    // DynamicMaps diamond already, so no label and no chip object exists there.
    internal DominationPointView(string id, Vector3 floor, float radius, bool showLetter)
    {
        _id = id;
        _root = new GameObject("Arena Domination point " + id);
        _root.transform.position = floor;
        try
        {
            var shader = Shader.Find("Sprites/Default");
            _vertexTint = shader != null;
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("Domination marker shader unavailable.");
            _material = new Material(shader) { color = Color.white };
            var ring = _ring = _root.AddComponent<LineRenderer>();
            ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 64;
            ring.startWidth = ring.endWidth = .12f;
            ring.sharedMaterial = _material;
            ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .07f, Mathf.Sin(angle) * radius));
            }
            if (showLetter)
            {
                var label = new GameObject("Control point label");
                label.transform.SetParent(_root.transform, false);
                label.transform.localPosition = Vector3.up * 2.2f;
                _label = label.AddComponent<TextMeshPro>();
                _label.font = HudStyle.Font; _label.fontSize = 2.5f;
                _label.alignment = TextAlignmentOptions.Center;
                _label.richText = false; _label.enableWordWrapping = false;
                _label.rectTransform.sizeDelta = new Vector2(1.5f, .8f);
                // A dark edge so the near-white glyph reads against pale concrete.
                _label.outlineWidth = .2f; _label.outlineColor = new Color(0f, 0f, 0f, 1f);
                _label.color = LetterColor;
                _chipMaterial = new Material(shader) { color = RingColorFor(default) };
                _chip = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _chip.name = "Control point chip";
                var collider = _chip.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                _chip.transform.SetParent(label.transform, false);
                _chip.transform.localPosition = new Vector3(0f, 0f, ChipDepth);
                _chip.transform.localScale = new Vector3(ChipWidth, ChipHeight, 1f);
                var surface = _chip.GetComponent<Renderer>();
                surface.sharedMaterial = _chipMaterial;
                surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
            }
            _root.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    internal static Color ColorFor(DominationPointState state) => state.Contested ? new Color(1f, .8f, .25f)
        : state.OwnerUsec.HasValue ? (state.OwnerUsec.Value ? Effects.ArenaTeamPalette.UsecUi : Effects.ArenaTeamPalette.BearUi)
        : new Color(.85f, .88f, .9f);

    // World geometry passes through EFT's HDR exposure; the bright pastel HUD
    // palette clips toward white on Factory even with correct renderer colors.
    // Keep its low-luminance, saturated world counterpart independent of the UI.
    internal static Color RingColorFor(DominationPointState state) => state.Contested ? new Color(.25f, .2f, 0f, .9f)
        : state.OwnerUsec.HasValue ? (state.OwnerUsec.Value ? new Color(0f, .015f, .25f, .9f) : new Color(.25f, 0f, 0f, .9f))
        : new Color(.08f, .08f, .08f, .9f);

    // Label scale that keeps the glyph at GlyphScreenFraction of the screen height (see the constants).
    internal static float LetterScaleFor(float distance, float fieldOfViewDegrees)
    {
        float angle = fieldOfViewDegrees * GlyphScreenFraction * Mathf.Deg2Rad;
        float scale = 2f * distance * Mathf.Tan(angle * .5f) / CapitalHeight;
        return Mathf.Clamp(scale, MinLetterScale, MaxLetterScale);
    }

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
        var color = RingColorFor(state);
        // UnitySprites.cginc multiplies vertex tint by material/renderer tint.
        // Keep the material white for that path; Unlit/Color uses its material
        // directly. Both endpoints are explicit, not inherited white defaults.
        _material.color = _vertexTint ? Color.white : color;
        _ring.startColor = _ring.endColor = color;
        if (_label == null) return;
        // The chip carries the owner in the dark world palette; the letter stays near-white.
        _chipMaterial.color = color;
        // The HUD owns detailed progress. Keep the world marker compact so a
        // defending bot and nearby pickup labels remain readable at close range.
        _label.text = _id;
        if (camera == null) return;
        _label.transform.rotation = camera.transform.rotation;
        _label.transform.localScale = Vector3.one * LetterScaleFor(
            Vector3.Distance(camera.transform.position, _label.transform.position), camera.fieldOfView);
    }

    public void Dispose()
    {
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_chipMaterial != null) UnityEngine.Object.Destroy(_chipMaterial);
    }
}
