using System;
using EFT;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>Presentation lease only; the team's exact-body lease owns immunity.</summary>
internal sealed class ArenaSpawnShieldView : IDisposable
{
    private ArenaBotEffectLeases<Player>.Lease _lease;
    internal ArenaSpawnShieldView(GameWorld world, Player body) => _lease = ArenaBotEffectRenderer.Acquire(world, body);
    internal void Update() => _lease?.Set(ArenaBotEffectKind.Spawn, Time.frameCount);
    public void Dispose() { _lease?.Dispose(); _lease = null; }
}
