using ArenaMode.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Hud;

/// <summary>
/// Procedural crosshair hitmarker (four rotated arms). Geometry, timing and
/// color scheme adapted from DragonDen.Hitmarker (MIT, (c) 2025 Drexira);
/// rendering is uGUI on the arena canvas instead of IMGUI.
/// </summary>
internal sealed class HitmarkerElement
{
    private const float Size = 13f;
    private const float FadeSeconds = 0.5f;

    private static readonly Color HitColor = new Color(0f, 0.75f, 0f, 1f);
    private static readonly Color KillColor = new Color(1f, 0f, 0f, 1f);
    private static readonly Color HeadshotColor = new Color(1f, 0.25f, 0.25f, 1f);
    private static readonly Color ArmorColor = new Color(0f, 0.75f, 1f, 1f);

    private RectTransform _root;
    private CanvasGroup _group;
    private readonly Image[] _arms = new Image[4];
    private float _shownAt = -10f;
    private bool _pulse;

    public void Create(RectTransform hudRoot)
    {
        var go = new GameObject("ArenaHitmarker", typeof(RectTransform), typeof(CanvasGroup));
        _root = (RectTransform)go.transform;
        _root.SetParent(hudRoot, false);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = Vector2.zero;
        _group = go.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        var length = Size * 0.6f;
        const float thickness = 2f;
        var offset = Size * 0.7f;
        for (var i = 0; i < 4; i++)
        {
            var arm = new GameObject("Arm" + i, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)arm.transform;
            rect.SetParent(_root, false);
            rect.sizeDelta = new Vector2(length, thickness);
            var angle = 45f + 90f * i;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            rect.anchoredPosition = Quaternion.Euler(0f, 0f, angle) * new Vector2(offset, 0f);
            _arms[i] = arm.GetComponent<Image>();
            _arms[i].raycastTarget = false;
        }
    }

    public void Show(ArenaDamage damage)
    {
        if (_root == null)
            return;

        _shownAt = Time.unscaledTime;
        _pulse = damage.Kill || damage.Headshot;
        var color = damage.Kill ? KillColor
            : damage.Headshot ? HeadshotColor
            : damage.ArmorOnly ? ArmorColor
            : HitColor;
        for (var i = 0; i < 4; i++)
            _arms[i].color = color;
    }

    public void Tick()
    {
        if (_group == null)
            return;

        var age = Time.unscaledTime - _shownAt;
        if (age >= FadeSeconds)
        {
            if (_group.alpha != 0f)
            {
                _group.alpha = 0f;
                _root.localScale = Vector3.one;
            }

            return;
        }

        _group.alpha = 1f - age / FadeSeconds;
        if (_pulse)
        {
            var pulse = 1f + 0.2f * Mathf.Abs(Mathf.Sin(age * 25f)) * (1f - age / FadeSeconds);
            _root.localScale = new Vector3(pulse, pulse, 1f);
        }
    }
}
