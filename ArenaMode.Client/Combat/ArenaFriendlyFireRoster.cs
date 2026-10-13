using EFT;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat;

/// <summary>
/// Match-owned weak physical affiliations, admitted only by a verified roster
/// freeze. Dead throwers keep their team; old victims cannot inherit protection.
/// No historical profile ID can admit a replacement or an unknown body.
/// </summary>
internal sealed class ArenaFriendlyFireRoster
{
    private sealed class Entry
    {
        internal int Team, Generation;
        internal string ProfileId, Participant;
    }
    private ConditionalWeakTable<Player, Entry> _bodies = new();
    private object _match;
    private GameWorld _world;
    private int _generation;
    private int _expectedCompanionCount;

    internal bool Bind(object match, int expectedCompanionCount, GameWorld world, Player leader, string leaderId,
        BotOwner[] followers, string[] followerIds, BotOwner[] enemies, string[] enemyIds)
    {
        if (!ReferenceEquals(_match, match) || !ReferenceEquals(_world, world))
        {
            _bodies = new ConditionalWeakTable<Player, Entry>();
            _match = match;
            _world = world;
            _generation = 0;
            _expectedCompanionCount = 0;
        }
        // A failed new freeze must not leave previous-round victim immunity.
        ++_generation;
        if (match == null || world == null || followers == null || followerIds == null
            || expectedCompanionCount is < 1 or > 4
            || (_expectedCompanionCount != 0 && expectedCompanionCount != _expectedCompanionCount)
            || enemies == null || enemyIds == null
            || followers.Length != expectedCompanionCount || followerIds.Length != expectedCompanionCount
            || enemies.Length != enemyIds.Length || (enemies.Length != 0 && enemies.Length != 5)) return false;
        var pending = new List<(Player body, Entry entry)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var accounts = new HashSet<string>(StringComparer.Ordinal);
        bool Add(Player body, string id, int team, string participant)
        {
            if (body == null || string.IsNullOrEmpty(id) || body.ProfileId != id
                || body.HealthController?.IsAlive != true || !ids.Add(id)
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(id), body)
                || !ReferenceEquals(world.GetEverExistedPlayerByID(id), body)) return false;
            pending.Add((body, new Entry { Team = team, ProfileId = id, Participant = participant }));
            return true;
        }
        if (!ReferenceEquals(world.MainPlayer, leader) || !Add(leader, leaderId, 1, "local:" + leaderId)) return false;
        for (int i = 0; i < followers.Length; i++)
        {
            var owner = followers[i];
            if (owner == null || owner.BotState != EBotState.Active || owner.IsDead
                || string.IsNullOrEmpty(owner.Profile?.AccountId) || !accounts.Add(owner.Profile.AccountId)
                || owner.ProfileId != followerIds[i] || !ReferenceEquals(owner.GetPlayer?.AIData?.BotOwner, owner)
                || !Add(owner.GetPlayer, followerIds[i], 1, "follower:" + owner.Profile?.AccountId)) return false;
        }
        for (int i = 0; i < enemies.Length; i++)
        {
            var owner = enemies[i];
            if (owner == null || owner.BotState != EBotState.Active || owner.IsDead
                || owner.ProfileId != enemyIds[i] || !ReferenceEquals(owner.GetPlayer?.AIData?.BotOwner, owner)
                || !Add(owner.GetPlayer, enemyIds[i], 2, "enemy:" + enemyIds[i])) return false;
        }
        foreach (var item in pending)
        {
            // A surviving leader gets the current victim generation. Historic
            // dead/retired source objects remain weak keys until match disposal.
            _bodies.Remove(item.body);
            item.entry.Generation = _generation;
            _bodies.Add(item.body, item.entry);
        }
        _expectedCompanionCount = expectedCompanionCount;
        return true;
    }

    internal bool AreTeammates(object match, GameWorld world, Player victim, IPlayer attacker)
    {
        if (!ReferenceEquals(_match, match) || !ReferenceEquals(_world, world)
            || victim == null || victim.HealthController?.IsAlive != true
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(victim.ProfileId), victim)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(victim.ProfileId), victim)
            || !_bodies.TryGetValue(victim, out var target) || target.Generation != _generation
            || target.ProfileId != victim.ProfileId) return false;
        var source = attacker as Player ?? (attacker as BotOwner)?.GetPlayer;
        if (ReferenceEquals(source, null) || ReferenceEquals(source, victim)
            || !_bodies.TryGetValue(source, out var origin) || origin.Team != target.Team) return false;
        // Destroyed Unity bodies may still be the frozen bridge on a delayed
        // projectile. The weak admission remains authoritative without getters.
        if (source != null && source.ProfileId != origin.ProfileId) return false;
        return origin.ProfileId != target.ProfileId && origin.Participant != target.Participant;
    }
}
