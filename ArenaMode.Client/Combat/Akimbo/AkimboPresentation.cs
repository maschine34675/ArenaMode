using System;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Independent visual impulses; never owns firing or ammunition.</summary>
internal sealed class AkimboPresentation
{
    private float _right = float.NegativeInfinity, _left = float.NegativeInfinity;
    internal bool LastLeft { get; private set; }
    internal float LastShot => Math.Max(_right, _left);
    internal void Shot(bool left, float now)
    {
        if (left) _left = now; else _right = now;
        LastLeft = left;
    }
    internal float Kick(bool left, float now)
        => Math.Max(0f, Math.Min(1f, 1f - (now - (left ? _left : _right)) / .10f));
    internal bool Flash(bool left, float now) => now - (left ? _left : _right) < .045f;
    internal bool CaptureIdle(bool firing, float now) => !firing && now - LastShot >= .20f;
    internal bool SampleNative(bool left, bool firing, float now)
        => left == LastLeft && (firing || now - LastShot < .10f);
    internal void Clear() { _left = _right = float.NegativeInfinity; LastLeft = false; }
}
