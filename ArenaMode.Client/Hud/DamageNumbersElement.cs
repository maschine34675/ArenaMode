using ArenaMode.Combat;
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

    private static readonly Color NumberColor = ArenaUiTheme.Text;
    private static readonly Color KillColor = ArenaUiTheme.Danger;

    private sealed class Entry
    {
        public RectTransform Rect;
        public TextMeshProUGUI Text;
        public Vector3 WorldPos;
        public float BornAt;
        public bool Active;
        public float PlacedYOffset;
        public bool Placed;
        public Vector2 Size;
        public Vector2 PendingLocal;
        public Rect PendingRect;
    }

    private readonly Entry[] _pool = new Entry[PoolSize];
    private readonly List<Rect> _occupied = new List<Rect>(PoolSize);
    private Canvas _canvas;
    private int _next;

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
    }

    public void Spawn(ArenaDamage damage)
    {
        var entry = _pool[_next];
        _next = (_next + 1) % PoolSize;

        entry.WorldPos = damage.WorldPos;
        entry.BornAt = damage.Time;
        entry.Active = true;
        entry.Placed = false;
        entry.PlacedYOffset = 0f;

        var flesh = Mathf.RoundToInt(Mathf.Max(0f, damage.BodyDamage));
        var armor = Mathf.RoundToInt(Mathf.Max(0f, damage.ArmorDamage));
        if (armor > 0)
            entry.Text.SetText("{0} <color=#00BFFF>({1})</color>", flesh, armor);
        else
            entry.Text.SetText("{0}", flesh);

        entry.Text.color = damage.Kill || damage.Headshot ? KillColor : NumberColor;
        entry.Rect.gameObject.SetActive(true);
        entry.Size = entry.Text.GetPreferredValues();
    }

    public void Tick()
    {
        var camera = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
        var now = Time.unscaledTime;
        var scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
        if (scaleFactor <= 0f)
            scaleFactor = 1f;

        _occupied.Clear();

        // Pass 1: expire, project, and register every ALREADY-PLACED entry so a
        // new number sees the complete occupied set regardless of pool order.
        for (var i = 0; i < PoolSize; i++)
        {
            var entry = _pool[i];
            if (!entry.Active)
                continue;

            var age = now - entry.BornAt;
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
            var rise = Mathf.Lerp(0f, RisePixels, t);
            entry.PendingLocal = new Vector2(
                viewport.x * Screen.width,
                viewport.y * Screen.height + rise) / scaleFactor;

            entry.PendingRect = new Rect(
                entry.PendingLocal.x - entry.Size.x * 0.5f,
                entry.PendingLocal.y - entry.Size.y * 0.5f + entry.PlacedYOffset,
                entry.Size.x,
                entry.Size.y);

            if (entry.Placed)
            {
                _occupied.Add(entry.PendingRect);
                Apply(entry, entry.PendingRect, t);
            }
        }

        // Pass 2: place the fresh entries against the full set.
        for (var i = 0; i < PoolSize; i++)
        {
            var entry = _pool[i];
            if (!entry.Active || entry.Placed)
                continue;

            var placed = PlaceWithoutOverlap(entry.PendingRect, entry.Size.y + 4f, 10);
            entry.PlacedYOffset = placed.y - entry.PendingRect.y;
            entry.Placed = true;
            Apply(entry, placed, Mathf.Clamp01((now - entry.BornAt) / LifetimeSeconds));
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
