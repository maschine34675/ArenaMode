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
    // GlyphScreenFraction of the screen height. The visible height at distance d is
    // 2 * d * tan(fieldOfView / 2) (the field of view is vertical), so the glyph needs
    // GlyphScreenFraction * 2 * d * tan(fieldOfView / 2) metres (at 60 degrees and 50 m: .03 * 2 * 50 *
    // .5774 = 1.73 m). A capital at label scale 1 and fontSize 2.5 is about CapitalHeight metres high,
    // so scale = that height / CapitalHeight (9.9 in the example), clamped so a letter next to the
    // camera does not fill the view (min) and a far one stays a bounded quad (max).
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
    // TMP material units (see the constructor): the face edge moves out by the dilate, the black band
    // lies half inside and half outside the moved edge. Dilate above the width, and their sum well
    // below 1, where TMP starts to scale both down (ShaderUtilities.UpdateShaderRatios).
    private const float LetterOutlineWidth = .2f;
    private const float LetterFaceDilate = .3f;

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
                // A dark edge so the near-white glyph reads on every chip colour. The width setter is what
                // clones the font material onto this label (TextMeshPro.SetOutlineThickness); the colour
                // setter returns early for black, TMP's cached default, and is kept only to state the intent.
                _label.outlineWidth = LetterOutlineWidth; _label.outlineColor = new Color(0f, 0f, 0f, 1f);
                // TMP centres the outline band on the glyph edge, so the outline alone ate this thin font's
                // face from inside: the native run of 13.11.2026 showed a black letter with a white hairline.
                // The SDF shader moves the edge outward by _FaceDilate and draws the band of _OutlineWidth
                // around the moved edge (both times _ScaleRatioA / 2), so a dilate above the outline width
                // keeps the whole band outside the glyph and leaves the white face a little heavier than the
                // regular weight. Only ever on the label's own copy: the font asset's material is shared by
                // every other label. The padding of the glyph quads was computed by the width setter before
                // the dilate existed, so it is recomputed, or the widened edge would be cut at the quad.
                var face = _label.fontSharedMaterial;
                if (face != null && !ReferenceEquals(face, HudStyle.Font?.material))
                {
                    face.SetFloat("_FaceDilate", LetterFaceDilate);
                    _label.UpdateMeshPadding();
                }
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
        float height = GlyphScreenFraction * 2f * distance * Mathf.Tan(fieldOfViewDegrees * Mathf.Deg2Rad * .5f);
        float scale = height / CapitalHeight;
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
        // Setting outlineWidth/outlineColor made TMP clone the font material onto the label's renderer
        // (TextMeshPro.SetOutlineThickness: m_renderer.material, kept in m_fontMaterial and
        // m_sharedMaterial), and TextMeshPro.OnDestroy does not destroy it. fontSharedMaterial returns
        // m_sharedMaterial without cloning again; read it before the label goes, and never destroy the
        // font asset's own shared material, which every other label uses.
        Material clone = _label != null ? _label.fontSharedMaterial : null;
        if (clone != null && !ReferenceEquals(clone, HudStyle.Font?.material)) UnityEngine.Object.Destroy(clone);
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_chipMaterial != null) UnityEngine.Object.Destroy(_chipMaterial);
    }
}
