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
        => TryResolveCore(
            groupEntries,
            memoryEntries,
            playerKey,
            botOwnerKey,
            preferredEnemyInfo,
            groupIsValid: (_, _) => true,
            pairIsValid: (_, _, _) => true,
            out resolvedKey,
            out groupInfo,
            out enemyInfo,
            out hasEnemyInfo);

    /// <summary>
    /// Resolves only a semantically current dictionary pair. Two exact wrapper
    /// keys can coexist after a round/body transition, so a stale complete pair
    /// must not hide the other wrapper's valid current relation.
    /// </summary>
    internal static bool TryResolveValid<TKey, TGroupInfo, TEnemyInfo>(
        IReadOnlyDictionary<TKey, TGroupInfo> groupEntries,
        IReadOnlyDictionary<TKey, TEnemyInfo> memoryEntries,
        TKey playerKey,
        TKey botOwnerKey,
        TEnemyInfo preferredEnemyInfo,
        Func<TKey, TGroupInfo, bool> groupIsValid,
        Func<TKey, TGroupInfo, TEnemyInfo, bool> pairIsValid,
        out TKey resolvedKey,
        out TGroupInfo groupInfo,
        out TEnemyInfo enemyInfo,
        out bool hasEnemyInfo)
        where TKey : class
        where TGroupInfo : class
        where TEnemyInfo : class
        => TryResolveCore(
            groupEntries,
            memoryEntries,
            playerKey,
            botOwnerKey,
            preferredEnemyInfo,
            groupIsValid,
            pairIsValid,
            out resolvedKey,
            out groupInfo,
            out enemyInfo,
            out hasEnemyInfo);

    private static bool TryResolveCore<TKey, TGroupInfo, TEnemyInfo>(
        IReadOnlyDictionary<TKey, TGroupInfo> groupEntries,
        IReadOnlyDictionary<TKey, TEnemyInfo> memoryEntries,
        TKey playerKey,
        TKey botOwnerKey,
        TEnemyInfo preferredEnemyInfo,
        Func<TKey, TGroupInfo, bool> groupIsValid,
        Func<TKey, TGroupInfo, TEnemyInfo, bool> pairIsValid,
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
            || botOwnerKey == null
            || groupIsValid == null
            || pairIsValid == null)
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

        bool validPlayerGroup = hasPlayerGroup
            && playerGroup != null
            && groupIsValid(playerKey, playerGroup);
        bool validOwnerGroup = hasOwnerGroup
            && ownerGroup != null
            && groupIsValid(botOwnerKey, ownerGroup);
        bool hasPlayerPair = validPlayerGroup
            && hasPlayerMemory
            && playerEnemy != null
            && pairIsValid(playerKey, playerGroup, playerEnemy);
        bool hasOwnerPair = validOwnerGroup
            && hasOwnerMemory
            && ownerEnemy != null
            && pairIsValid(botOwnerKey, ownerGroup, ownerEnemy);
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

            // Both objects describe the same current AI body. If another
            // subsystem changed GoalEnemy to a different opponent between
            // sweeps, prefer EFT's canonical Player entry deterministically.
            // This matches the BigBrain consumer and prevents a foreign goal
            // decision from stranding an otherwise complete target relation.
            resolvedKey = playerKey;
            groupInfo = playerGroup;
            enemyInfo = playerEnemy;
            hasEnemyInfo = true;
            return true;
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

        if (validPlayerGroup)
        {
            resolvedKey = playerKey;
            groupInfo = playerGroup;
            return true;
        }

        if (validOwnerGroup)
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
