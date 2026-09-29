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
    private const float KillSeconds = 0.8f;
    private const float KillHoldSeconds = 0.15f;

    private static readonly Color HitColor = ArenaUiTheme.Positive;
    private static readonly Color KillColor = ArenaUiTheme.Danger;
    private static readonly Color HeadshotColor = ArenaUiTheme.Headshot;
    private static readonly Color ArmorColor = ArenaUiTheme.Armor;

    private RectTransform _root;
    private CanvasGroup _group;
    private readonly Image[] _arms = new Image[4];
    private float _shownAt = -10f;
    private bool _pulse;
    private CanvasGroup _killGroup;
    private Image _skull;
    private float _killShownAt = -10f;

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
        _group.interactable = false;

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

        // A sibling, never a child of the fading/pulsing crosshair group. Later
        // nonlethal hits cannot hide, extend or tint the confirmed-kill cue.
        var skullGo = new GameObject("ArenaKillSkull", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        var skullRect = (RectTransform)skullGo.transform;
        skullRect.SetParent(hudRoot, false);
        skullRect.anchorMin = skullRect.anchorMax = new Vector2(0.5f, 0.5f);
        skullRect.anchoredPosition = new Vector2(0f, -42f);
        skullRect.sizeDelta = new Vector2(32f, 32f);
        _killGroup = skullGo.GetComponent<CanvasGroup>();
        _killGroup.alpha = 0f;
        _killGroup.interactable = _killGroup.blocksRaycasts = false;
        _skull = skullGo.GetComponent<Image>();
        _skull.sprite = skullGo.AddComponent<ArenaUiAssetOwner>().Assets.Sprite(ArenaUiIcon.Kill);
        _skull.enabled = _skull.sprite != null;
        _skull.preserveAspect = true;
        _skull.raycastTarget = false;
        _skull.color = Color.white;
        var outline = skullGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .9f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    // ArenaMatch emits a deduplicated, already classified kill. Do not turn
    // damage.Kill into another confirmation: post-mortem pellets can repeat it.
    public void ShowKill(ArenaKill kill)
    {
        if (_killGroup == null || _skull == null || !_skull.enabled || kill == null
            || !kill.KilledByLocal || kill.LocalIsVictim || !kill.ScoreEligible || !kill.AwardEligible)
            return;
        _killShownAt = Time.unscaledTime;
        _killGroup.alpha = 1f;
    }

    public void Show(ArenaDamage damage)
    {
        if (_root == null)
            return;

        _shownAt = Time.unscaledTime;
        _pulse = damage.Kill || damage.Headshot;
        if (!_pulse) _root.localScale = Vector3.one;
        var color = damage.Kill ? KillColor
            : damage.Headshot ? HeadshotColor
            : damage.ArmorOnly ? ArmorColor
            : HitColor;
        for (var i = 0; i < 4; i++)
            _arms[i].color = color;
    }

    public void Tick()
    {
        if (_killGroup != null)
            _killGroup.alpha = Mathf.Clamp01(1f - (Time.unscaledTime - _killShownAt - KillHoldSeconds)
                / (KillSeconds - KillHoldSeconds));
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
