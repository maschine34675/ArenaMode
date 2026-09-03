using System;
using System.Collections.Generic;

namespace ArenaMode.Bots;

/// <summary>
/// Resolves the two concrete IPlayer objects EFT exposes for one live AI body.
/// BotsGroup and BotMemory must agree on the exact dictionary key because
/// EnemyInfo later indexes the group dictionary through EnemyInfo.Person.
/// </summary>
internal static class TeamArenaEnemyIdentityPolicy
{
    internal static bool TryResolve<TKey, TGroupInfo, TEnemyInfo>(
        IReadOnlyDictionary<TKey, TGroupInfo> groupEntries,
        IReadOnlyDictionary<TKey, TEnemyInfo> memoryEntries,
        TKey playerKey,
        TKey botOwnerKey,
        out TKey resolvedKey,
        out TGroupInfo groupInfo,
        out TEnemyInfo enemyInfo,
        out bool hasEnemyInfo)
        where TKey : class
        where TGroupInfo : class
        where TEnemyInfo : class
        => TryResolve(
            groupEntries,
            memoryEntries,
            playerKey,
            botOwnerKey,
            preferredEnemyInfo: null,
            out resolvedKey,
            out groupInfo,
            out enemyInfo,
            out hasEnemyInfo);

    internal static bool TryResolve<TKey, TGroupInfo, TEnemyInfo>(
        IReadOnlyDictionary<TKey, TGroupInfo> groupEntries,
        IReadOnlyDictionary<TKey, TEnemyInfo> memoryEntries,
        TKey playerKey,
        TKey botOwnerKey,
        TEnemyInfo preferredEnemyInfo,
        out TKey resolvedKey,
        out TGroupInfo groupInfo,
        out TEnemyInfo enemyInfo,
        out bool hasEnemyInfo)
        where TKey : class
        where TGroupInfo : class
        where TEnemyInfo : class
    {
        resolvedKey = null;
        groupInfo = null;
        enemyInfo = null;
        hasEnemyInfo = false;
        if (groupEntries == null
            || memoryEntries == null
            || playerKey == null
            || botOwnerKey == null)
        {
            return false;
        }

        bool sameKey = ReferenceEquals(playerKey, botOwnerKey);
        bool hasPlayerGroup = TryGetReferenceValue(groupEntries, playerKey, out TGroupInfo playerGroup);
        TGroupInfo ownerGroup = null;
        bool hasOwnerGroup = !sameKey
            && TryGetReferenceValue(groupEntries, botOwnerKey, out ownerGroup);
        bool hasPlayerMemory = TryGetReferenceValue(memoryEntries, playerKey, out TEnemyInfo playerEnemy);
        TEnemyInfo ownerEnemy = null;
        bool hasOwnerMemory = !sameKey
            && TryGetReferenceValue(memoryEntries, botOwnerKey, out ownerEnemy);

        if ((hasPlayerGroup && playerGroup == null)
            || (hasOwnerGroup && ownerGroup == null)
            || (hasPlayerMemory && playerEnemy == null)
            || (hasOwnerMemory && ownerEnemy == null))
        {
            return false;
        }

        bool hasPlayerPair = hasPlayerGroup && hasPlayerMemory;
        bool hasOwnerPair = hasOwnerGroup && hasOwnerMemory;
        if (hasPlayerPair && hasOwnerPair)
        {
            if (preferredEnemyInfo != null && ReferenceEquals(preferredEnemyInfo, playerEnemy))
            {
                resolvedKey = playerKey;
                groupInfo = playerGroup;
                enemyInfo = playerEnemy;
                hasEnemyInfo = true;
                return true;
            }

            if (preferredEnemyInfo != null && ReferenceEquals(preferredEnemyInfo, ownerEnemy))
            {
                resolvedKey = botOwnerKey;
                groupInfo = ownerGroup;
                enemyInfo = ownerEnemy;
                hasEnemyInfo = true;
                return true;
            }

            // Both EFT wrappers have complete entries. Only the exact current
            // GoalEnemy can identify which object graph remains authoritative.
            return false;
        }

        if (hasPlayerPair)
        {
            resolvedKey = playerKey;
            groupInfo = playerGroup;
            enemyInfo = playerEnemy;
            hasEnemyInfo = true;
            return true;
        }

        if (hasOwnerPair)
        {
            resolvedKey = botOwnerKey;
            groupInfo = ownerGroup;
            enemyInfo = ownerEnemy;
            hasEnemyInfo = true;
            return true;
        }

        // A memory entry under the other wrapper is structural drift. Do not
        // combine it with a group entry whose key EnemyInfo.Person cannot use.
        if (hasPlayerMemory || hasOwnerMemory)
            return false;

        if (hasPlayerGroup)
        {
            resolvedKey = playerKey;
            groupInfo = playerGroup;
            return true;
        }

        if (hasOwnerGroup)
        {
            resolvedKey = botOwnerKey;
            groupInfo = ownerGroup;
            return true;
        }

        return false;
    }

    internal static bool ContainsReferenceKey<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> entries,
        TKey requested)
        where TKey : class
        where TValue : class
        => entries != null
           && requested != null
           && TryGetReferenceValue(entries, requested, out _);

    private static bool TryGetReferenceValue<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> entries,
        TKey requested,
        out TValue value)
        where TKey : class
        where TValue : class
    {
        foreach (KeyValuePair<TKey, TValue> pair in entries)
        {
            if (ReferenceEquals(pair.Key, requested))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
