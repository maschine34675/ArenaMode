using System;
using ArenaMode.Pickups;
using EFT;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>A single body presentation lease combines simultaneous pickup colors.</summary>
internal sealed class ArenaPowerupIndicator : IDisposable
{
    private ArenaBotEffectLeases<Player>.Lease _lease;
    internal ArenaPowerupIndicator(GameWorld world, Player body) => _lease = ArenaBotEffectRenderer.Acquire(world, body);
    internal void Update(ArenaPowerupSnapshot state)
    {
        var flags = ArenaBotEffectKind.None;
        if (state.ShieldSeconds > 0f && state.ShieldPoints > 0f) flags |= ArenaBotEffectKind.Shield;
        if (state.DamageSeconds > 0f) flags |= ArenaBotEffectKind.DoubleDamage;
        _lease?.Set(flags, Time.frameCount);
    }
    public void Dispose() { _lease?.Dispose(); _lease = null; }
}
