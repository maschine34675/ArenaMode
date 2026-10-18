using System;
using ArenaMode.Pickups;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>Presentation only: two independently owned cages, never an immunity authority.</summary>
internal sealed class ArenaPowerupIndicator : IDisposable
{
    private readonly Transform _world;
    private ArenaSpawnShieldView _shield, _damage;
    private bool _shieldAttempted, _damageAttempted;
    private static bool _warned;

    internal ArenaPowerupIndicator(Transform world) => _world = world;

    internal void Update(Vector3 position, ArenaPowerupSnapshot state)
    {
        // Blue inside pink; both remain readable without arbitration or flicker.
        // The ordinary spawn cage is smaller again and has a separate owner.
        UpdateOne(ref _shield, ref _shieldAttempted, state.ShieldSeconds > 0f && state.ShieldPoints > 0f,
            position, "ArenaBotPickupShield", ArenaPickupColors.Shield, .60f, 1.08f);
        UpdateOne(ref _damage, ref _damageAttempted, state.DamageSeconds > 0f,
            position, "ArenaBotDoubleDamage", ArenaPickupColors.DoubleDamage, .68f, 1.18f);
    }

    private void UpdateOne(ref ArenaSpawnShieldView view, ref bool attempted, bool active,
        Vector3 position, string name, Color color, float radius, float halfHeight)
    {
        if (!active)
        {
            view?.Dispose(); view = null; attempted = false;
            return;
        }
        try
        {
            if (!attempted)
            {
                attempted = true; color.a = .85f;
                view = new ArenaSpawnShieldView(_world, name, color, radius, halfHeight);
            }
            view?.Update(position);
        }
        catch (Exception ex)
        {
            view?.Dispose(); view = null;
            // Rendering failure cannot refuse a collected buff or retry every frame.
            if (!_warned)
            {
                _warned = true;
                Plugin.ModLogger.LogWarning("Arena bot powerup indicator unavailable: " + ex.GetBaseException().Message);
            }
        }
    }

    public void Dispose()
    {
        _shield?.Dispose(); _shield = null;
        _damage?.Dispose(); _damage = null;
    }
}
