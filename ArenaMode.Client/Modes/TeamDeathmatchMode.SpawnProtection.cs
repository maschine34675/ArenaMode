using ArenaMode.Config;
using ArenaMode.Effects;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    private sealed class SpawnShield
    {
        internal BotOwner Owner;
        internal Player Body;
        internal string ProfileId;
        internal TeamSpawnProtectionLease Lease;
        internal ArenaSpawnShieldView View;
    }

    // Bounded to the current roster, keyed by exact physical references below,
    // never by historical profile IDs (which outlive corpses/projectiles).
    private readonly List<SpawnShield> _spawnShields = new();
    private float _spawnShieldSeconds;
    private bool _shieldVisualWarning;

    private void TrackSpawnShield(BotOwner bot)
    {
        if (_spawnShieldSeconds <= 0f || bot?.GetPlayer == null) return;
        foreach (var shield in _spawnShields)
            if (ReferenceEquals(shield.Body, bot.GetPlayer)) return;
        _spawnShields.Add(new SpawnShield { Owner = bot, Body = bot.GetPlayer,
            ProfileId = bot.ProfileId,
            Lease = new TeamSpawnProtectionLease(_spawnShieldSeconds) });
    }

    private bool CurrentShield(SpawnShield shield)
        => Alive(shield.Owner) && ReferenceEquals(shield.Owner.GetPlayer, shield.Body)
           && string.Equals(shield.ProfileId, shield.Body.ProfileId, StringComparison.Ordinal)
           && ReferenceEquals(_world.GetAlivePlayerByProfileID(shield.ProfileId), shield.Body)
           && ReferenceEquals(shield.Body.AIData?.BotOwner, shield.Owner)
           && _bots.Exists(bot => ReferenceEquals(bot, shield.Owner));

    internal bool IsBotSpawnProtected(Player body)
    {
        if (body == null || !IsCurrentMatch() || _rules?.Phase != TdmMatchPhase.Live) return false;
        foreach (var shield in _spawnShields)
            if (ReferenceEquals(shield.Body, body) && CurrentShield(shield))
                return shield.Lease.IsProtected(Time.time, shield.Owner.BotState == EBotState.Active);
        return false;
    }

    internal void EndBotSpawnProtection(Player attacker)
    {
        if (attacker == null || !IsCurrentMatch() || _rules?.Phase != TdmMatchPhase.Live) return;
        foreach (var shield in _spawnShields)
        {
            if (!ReferenceEquals(shield.Body, attacker) || !CurrentShield(shield)) continue;
            shield.Lease.End();
            shield.View?.Dispose();
            shield.View = null;
            return;
        }
    }

    private void TickSpawnShields()
    {
        if (!IsCurrentMatch()) { ClearSpawnShields(); return; }
        for (int i = _spawnShields.Count - 1; i >= 0; i--)
        {
            var shield = _spawnShields[i];
            bool current = CurrentShield(shield);
            bool live = _rules?.Phase == TdmMatchPhase.Live;
            bool protectedNow = current && shield.Lease.IsProtected(Time.time,
                live && shield.Owner.BotState == EBotState.Active);
            if (!protectedNow)
            {
                shield.View?.Dispose();
                _spawnShields.RemoveAt(i);
                continue;
            }
            if (!live) continue;
            // The damage lease may protect a bounded PreActive replacement;
            // presentation must wait for its actual active, registered body.
            if (shield.Owner.BotState != EBotState.Active)
            {
                shield.View?.Dispose(); shield.View = null;
                continue;
            }
            try
            {
                shield.View ??= new ArenaSpawnShieldView(_world, shield.Body);
                shield.View.Update();
            }
            catch (Exception ex)
            {
                shield.View?.Dispose(); shield.View = null;
                if (!_shieldVisualWarning)
                    Plugin.ModLogger.LogWarning("Team DM spawn shield visual unavailable: " + ex.Message);
                _shieldVisualWarning = true;
            }
        }
    }

    private void ClearSpawnShields()
    {
        foreach (var shield in _spawnShields) shield.View?.Dispose();
        _spawnShields.Clear();
    }
}
