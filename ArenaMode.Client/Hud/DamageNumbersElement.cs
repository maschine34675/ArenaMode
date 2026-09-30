using ArenaMode.Combat;
using ArenaMode.Config;
using EFT.CameraControl;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// Pooled floating damage numbers at the hit position. Projection, lifetimes and
/// the PlaceWithoutOverlap layout adapted from DragonDen.Hitmarker (MIT,
/// (c) 2025 Drexira) — the overlap offset is resolved once at spawn, so lines
/// never jump when a neighbor expires.
/// </summary>
internal sealed class DamageNumbersElement
{
    private const int PoolSize = 12;
    private const float LifetimeSeconds = 1.25f;
    private const float RisePixels = 30f;
    private const float EdgePadding = 0.04f;
    private const float BurstGapSeconds = 0.25f;

    private static readonly Color NumberColor = ArenaUiTheme.Text;
    private static readonly Color KillColor = ArenaUiTheme.Danger;

    private sealed class Entry
    {
        public RectTransform Rect;
        public TextMeshProUGUI Text;
        public Vector3 WorldPos;
        public float BornAt;
        public float LastHitAt;
        public bool Active;
        public bool BurstLayout;
        public int VictimBodyId;
        public bool MergeOpen;
        public bool Emphasized;
        public float Flesh;
        public float Armor;
        public float PlacedYOffset;
        public bool Placed;
        public Vector2 Size;
        public Vector2 PendingLocal;
        public Rect PendingRect;
        public Rect PendingFootprint;
    }

    private readonly Entry[] _pool = new Entry[PoolSize];
    private readonly List<Rect> _occupied = new List<Rect>(PoolSize);
    private Canvas _canvas;
    private int _next;
    private bool _combining;
    private Vector2 _combinedSize;

    public void Create(RectTransform hudRoot, Canvas canvas)
    {
        _canvas = canvas;
        for (var i = 0; i < PoolSize; i++)
        {
            var text = HudStyle.CreateText(hudRoot, "DmgNum" + i, 18f, TextAlignmentOptions.Center);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.gameObject.SetActive(false);
            _pool[i] = new Entry { Rect = rect, Text = text };
        }

        // Reserve the maximum integer presentation width before placing a burst.
        // A growing total must not shove an older neighbor out of its lane. TMP's
        // measured digits account for proportional fonts; both channels fit even
        // when a later hit starts showing armor that the first hit did not have.
        for (var digit = '0'; digit <= '9'; digit++)
        {
            var widest = "-" + new string(digit, 10);
            var size = _pool[0].Text.GetPreferredValues(widest + " <color=#00BFFF>(" + widest + ")</color>");
            _combinedSize.x = Mathf.Max(_combinedSize.x, size.x);
            _combinedSize.y = Mathf.Max(_combinedSize.y, size.y);
        }
        _combining = Settings.CombineDamageNumbers.Value;
    }

    public void Spawn(ArenaDamage damage)
    {
        ObserveOption();
        if (_combining && damage.VictimBodyId != 0)
        {
            for (var i = 0; i < PoolSize; i++)
            {
                var candidate = _pool[i];
                if (!candidate.Active || !candidate.MergeOpen) continue;
                var gap = damage.Time - candidate.LastHitAt;
                if (gap < 0f || gap > BurstGapSeconds
                    || Time.unscaledTime - candidate.LastHitAt > LifetimeSeconds)
                {
                    candidate.MergeOpen = false;
                    continue;
                }
                if (candidate.VictimBodyId == damage.VictimBodyId)
                {
                    AddHit(candidate, damage);
                    return;
                }
            }
        }

        var entry = _pool[_next];
        _next = (_next + 1) % PoolSize;

        entry.WorldPos = damage.WorldPos;
        entry.BornAt = damage.Time;
        entry.LastHitAt = damage.Time;
        entry.Active = true;
        entry.BurstLayout = _combining;
        entry.VictimBodyId = damage.VictimBodyId;
        entry.MergeOpen = _combining && damage.VictimBodyId != 0;
        entry.Emphasized = false;
        entry.Flesh = 0f;
        entry.Armor = 0f;
        entry.Placed = false;
        entry.PlacedYOffset = 0f;

        AddHit(entry, damage);
        entry.Rect.gameObject.SetActive(true);
        entry.Size = _combining ? _combinedSize : entry.Text.GetPreferredValues();
    }

    private static void AddHit(Entry entry, ArenaDamage damage)
    {
        entry.Flesh += Mathf.Max(0f, damage.BodyDamage);
        entry.Armor += Mathf.Max(0f, damage.ArmorDamage);
        entry.LastHitAt = damage.Time;
        entry.MergeOpen &= !damage.Kill;
        entry.Emphasized |= damage.Kill || damage.Headshot;

        // Keep fractional damage in each independent channel. Rounding each hit
        // first would make a burst's displayed sum depend on pellet count.
        var flesh = Mathf.RoundToInt(entry.Flesh);
        var armor = Mathf.RoundToInt(entry.Armor);
        if (armor > 0)
            entry.Text.SetText("{0} <color=#00BFFF>({1})</color>", flesh, armor);
        else
            entry.Text.SetText("{0}", flesh);

        entry.Text.color = entry.Emphasized ? KillColor : NumberColor;
    }

    private void ObserveOption()
    {
        var combining = Settings.CombineDamageNumbers.Value;
        if (_combining == combining) return;
        _combining = combining;
        // Already-visible numbers stay where they are, but toggling must never
        // revive an older burst (including a row created while grouping was off).
        for (var i = 0; i < PoolSize; i++)
            _pool[i].MergeOpen = false;
    }

    public void Tick()
    {
        ObserveOption();
        var camera = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
        var now = Time.unscaledTime;
        var scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
        if (scaleFactor <= 0f)
            scaleFactor = 1f;

        // A refreshed burst can outlive its own rise and remain above a later
        // label. Reserve every label's full rise while any burst is visible,
        // including after grouping is switched off. Otherwise a later label
        // can start clear below that burst, then rise into it.
        var reserveRise = _combining;
        for (var i = 0; i < PoolSize && !reserveRise; i++)
            reserveRise = _pool[i].Active && _pool[i].BurstLayout
                && now - _pool[i].LastHitAt <= LifetimeSeconds;
        var reservedRise = reserveRise ? RisePixels / scaleFactor : 0f;

        _occupied.Clear();

        // Pass 1: expire, project, and register every ALREADY-PLACED entry so a
        // new number sees the complete occupied set regardless of pool order.
        for (var i = 0; i < PoolSize; i++)
        {
            var entry = _pool[i];
            if (!entry.Active)
                continue;

            var age = now - entry.LastHitAt;
            if (age > LifetimeSeconds || camera == null)
            {
                entry.Active = false;
                entry.Rect.gameObject.SetActive(false);
                continue;
            }

            var viewport = camera.WorldToViewportPoint(entry.WorldPos);
            if (viewport.z < 0f)
                viewport.x = viewport.x < 0.5f ? EdgePadding : 1f - EdgePadding;
            viewport.x = Mathf.Clamp(viewport.x, EdgePadding, 1f - EdgePadding);
            viewport.y = Mathf.Clamp(viewport.y, EdgePadding, 1f - EdgePadding);

            var t = Mathf.Clamp01(age / LifetimeSeconds);
            // Refresh fade/lifetime on a combined hit, but keep the first impact
            // anchor and continuous rise: automatic fire does not jerk the label
            // between hit body parts or reset its vertical motion on every shot.
            var rise = Mathf.Lerp(0f, RisePixels, Mathf.Clamp01((now - entry.BornAt) / LifetimeSeconds));
            entry.PendingLocal = new Vector2(
                viewport.x * Screen.width,
                viewport.y * Screen.height + rise) / scaleFactor;

            entry.PendingRect = new Rect(
                entry.PendingLocal.x - entry.Size.x * 0.5f,
                entry.PendingLocal.y - entry.Size.y * 0.5f + entry.PlacedYOffset,
                entry.Size.x,
                entry.Size.y);
            // Keep collision space separate from the visible rectangle. Its
            // envelope starts at the original projected height, so extending
            // the fade cannot shift either the label or its assigned lane.
            entry.PendingFootprint = entry.PendingRect;
            if (reserveRise)
            {
                entry.PendingFootprint.y -= rise / scaleFactor;
                entry.PendingFootprint.height += reservedRise;
            }

            if (entry.Placed)
            {
                _occupied.Add(entry.PendingFootprint);
                Apply(entry, entry.PendingRect, t);
            }
        }

        // Pass 2: place the fresh entries against the full set.
        for (var i = 0; i < PoolSize; i++)
        {
            var entry = _pool[i];
            if (!entry.Active || entry.Placed)
                continue;

            var placed = PlaceWithoutOverlap(entry.PendingFootprint,
                entry.PendingFootprint.height + 4f, reserveRise ? PoolSize * 2 : 10);
            entry.PlacedYOffset = placed.y - entry.PendingFootprint.y;
            entry.Placed = true;
            var visible = entry.PendingRect;
            visible.y += entry.PlacedYOffset;
            Apply(entry, visible, Mathf.Clamp01((now - entry.LastHitAt) / LifetimeSeconds));
        }
    }

    private static void Apply(Entry entry, Rect rect, float t)
    {
        entry.Rect.anchoredPosition = new Vector2(entry.PendingLocal.x, rect.center.y);
        var color = entry.Text.color;
        color.a = 1f - t;
        entry.Text.color = color;
    }

    private Rect PlaceWithoutOverlap(Rect preferred, float stepY, int maxSteps)
    {
        if (!IntersectsAny(preferred))
        {
            _occupied.Add(preferred);
            return preferred;
        }

        for (var i = 1; i <= maxSteps; i++)
        {
            var dy = (i % 2 == 1 ? 1f : -1f) * Mathf.Ceil(i * 0.5f) * stepY;
            var candidate = preferred;
            candidate.y += dy;
            if (!IntersectsAny(candidate))
            {
                _occupied.Add(candidate);
                return candidate;
            }
        }

        _occupied.Add(preferred);
        return preferred;
    }

    private bool IntersectsAny(Rect rect)
    {
        for (var i = 0; i < _occupied.Count; i++)
        {
            if (rect.Overlaps(_occupied[i]))
                return true;
        }

        return false;
    }
}
