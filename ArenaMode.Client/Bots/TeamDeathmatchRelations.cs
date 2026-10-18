using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Faction relationships for the independent Arena team deathmatch. Affiliation
/// belongs to an exact physical raid body, never a globally reused profile id.
/// Weak entries retain posthumous projectile attribution without retaining corpses.
/// </summary>
internal static class TeamDeathmatchRelations
{
    private sealed class Affiliation
    {
        internal readonly EPlayerSide Side;
        internal readonly string ProfileId;
        internal Affiliation(EPlayerSide side, string profileId)
        {
            Side = side;
            ProfileId = profileId;
        }
    }

    private static ConditionalWeakTable<object, Affiliation> _affiliations = new();
    private static GameWorld _world;
    private static AbstractGame _game;
    private static bool _loggedFailure;
    private static readonly Harmony SainHarmony = new(Plugin.PluginGuid + ".teamdeathmatch-sain");
    private static bool _sainResolved;
    private static Type _sainBotType;
    private static PropertyInfo _sainOwner, _sainPlayer, _sainController, _sainListOwner;
    private static PropertyInfo _sainEnemyPlayer, _sainEnemyInfo;
    private static MethodInfo _sainAddEnemy, _sainRemoveEnemy, _sainLastSeen;

    // No armed-only mutation: this mode owns a quiet population and starts its
    // requests only after the server confirmed the exact raid token. ActiveMode
    // is necessary here because the opening hostility sweep precedes StartMatch.
    internal static bool Enabled
    {
        get
        {
            if (!ArenaSession.IsActive
                || !string.Equals(ArenaSession.ActiveMode, "teamdeathmatch", StringComparison.Ordinal)
                || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
                return false;
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance;
            if (!ArenaSession.IsExactActiveRaid(game, world)) return false;
            if (!ReferenceEquals(_world, world) || !ReferenceEquals(_game, game))
            {
                Reset();
                _world = world;
                _game = game;
            }
            return true;
        }
    }

    internal static void Enable()
    {
        new ArenaTeamDeathmatchRelationsGroupEnemyAdmissionPatch().Enable();
        new ArenaTeamDeathmatchRelationsMemoryEnemyAdmissionPatch().Enable();
        new ArenaTeamDeathmatchRelationsMemoryGoalAdmissionPatch().Enable();
    }

    internal static void Reset()
    {
        _affiliations = new ConditionalWeakTable<object, Affiliation>();
        _world = null;
        _game = null;
        _loggedFailure = false;
        TdmSearchAwareness.Reset();
    }

    /// <summary>
    /// Admits only an exact currently registered local PMC or faction-consistent
    /// PMC bot. Safe to call immediately from an owned spawn callback. Native
    /// constructors can ask earlier, so Faction also performs this validation.
    /// </summary>
    internal static bool Register(Player player)
    {
        if (!Enabled || ReferenceEquals(player, null)) return false;
        try
        {
            if (!IsCurrentBody(player) || player.HealthController?.IsAlive != true)
                return false;
            var side = player.Profile?.Info?.Side;
            if (side != EPlayerSide.Usec && side != EPlayerSide.Bear) return false;
            var owner = player.AIData?.BotOwner;
            if (player.IsAI)
            {
                var role = player.Profile?.Info?.Settings?.Role;
                if (owner == null || !ReferenceEquals(owner.GetPlayer, player)
                    || (side == EPlayerSide.Usec ? role != WildSpawnType.pmcUSEC : role != WildSpawnType.pmcBEAR))
                    return false;
            }
            else if (!ReferenceEquals(player, _world.MainPlayer)) return false;

            var affiliation = _affiliations.GetValue(player,
                _ => new Affiliation(side.Value, player.ProfileId));
            if (affiliation.Side != side.Value
                || !string.Equals(affiliation.ProfileId, player.ProfileId, StringComparison.Ordinal)) return false;
            if (owner != null)
            {
                var ownerAffiliation = _affiliations.GetValue(owner, _ => affiliation);
                if (ownerAffiliation.Side != affiliation.Side
                    || !string.Equals(ownerAffiliation.ProfileId, affiliation.ProfileId, StringComparison.Ordinal))
                    return false;
            }
            ResolveSain();
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("body registration", ex);
            return false;
        }
    }

    internal static EPlayerSide? Faction(IPlayer player)
    {
        if (!Enabled || ReferenceEquals(player, null)) return null;
        if (_affiliations.TryGetValue(player, out var known)) return known.Side;
        var body = PhysicalBody(player);
        return Register(body) && _affiliations.TryGetValue(player, out known)
            ? known.Side : null;
    }

    internal static bool AreOpponents(IPlayer a, IPlayer b)
    {
        var first = Faction(a);
        var second = Faction(b);
        return first.HasValue && second.HasValue && first != second;
    }

    internal static bool ShouldBlockFriendlyDamage(IPlayer victim, IPlayer attacker)
    {
        var victimSide = Faction(victim);
        var attackerSide = Faction(attacker);
        if (!victimSide.HasValue || victimSide != attackerSide) return false;
        if (ReferenceEquals(victim, attacker)) return false;
        // Keep self-damage even when an old body's grenade hits the replacement
        // body of the same local participant after respawn.
        return _affiliations.TryGetValue(victim, out var victimEntry)
            && _affiliations.TryGetValue(attacker, out var attackerEntry)
            && !string.Equals(victimEntry.ProfileId, attackerEntry.ProfileId, StringComparison.Ordinal);
    }

    internal static bool TryGroupRelationship(BotsGroup group, IPlayer player, out bool enemies)
    {
        enemies = false;
        var groupSide = GroupFaction(group);
        var playerSide = Faction(player);
        if (!groupSide.HasValue || !playerSide.HasValue) return false;
        enemies = groupSide != playerSide;
        return true;
    }

    private static EPlayerSide? GroupFaction(BotsGroup group)
    {
        if (!Enabled || group == null || !ReferenceEquals(group.BotGame, _game)) return null;
        var first = Faction(group._initialBot);
        if (!first.HasValue || first != group.Side) return null;
        if (first == EPlayerSide.Usec ? group.InitialBotType != WildSpawnType.pmcUSEC
            : group.InitialBotType != WildSpawnType.pmcBEAR) return null;
        for (int i = 0; i < group.MembersCount; i++)
        {
            if (Faction(group.Member(i)) != first) return null;
        }
        return first;
    }

    private static Player PhysicalBody(IPlayer player)
    {
        try { return player as Player ?? (player as BotOwner)?.GetPlayer; }
        catch { return null; }
    }

    private static bool IsCurrentBody(IPlayer player)
    {
        var body = PhysicalBody(player);
        try
        {
            return body != null && !string.IsNullOrEmpty(body.ProfileId)
                && body.HealthController?.IsAlive == true
                && ReferenceEquals(_world.GetAlivePlayerByProfileID(body.ProfileId), body);
        }
        catch { return false; }
    }

    // Passive diagnostics must not auto-register an actor or resolve optional
    // patches. Read only the already established raid/body affiliations.
    internal static bool IsRegisteredCurrentBot(BotOwner owner)
    {
        var body = owner?.GetPlayer;
        return _world != null && _game?.Status == GameStatus.Started
            && ArenaSession.IsActive && ArenaSession.ActiveMode == "teamdeathmatch"
            && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
            && ArenaSession.IsExactActiveRaid(_game, _world)
            && body?.IsAI == true && owner.BotState == EBotState.Active
            && ReferenceEquals(body.AIData?.BotOwner, owner) && IsCurrentBody(body)
            && _affiliations.TryGetValue(body, out var affiliation)
            && _affiliations.TryGetValue(owner, out var ownerAffiliation)
            && ReferenceEquals(affiliation, ownerAffiliation)
            && affiliation.ProfileId == body.ProfileId && affiliation.Side == body.Profile?.Info?.Side
            && body.Profile?.Info?.Settings?.Role == (affiliation.Side == EPlayerSide.Usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR);
    }

    /// <summary>
    /// Caller supplies adopted live bodies once per second and owns population
    /// limits. All removals precede additions because SAIN removes by profile id.
    /// Awareness continues without a living human; native/SAIN movement owns paths.
    /// </summary>
    internal static void Reconcile(IReadOnlyList<Player> liveActors)
    {
        if (!Enabled || liveActors == null) return;
        try
        {
            var actors = liveActors.Where(Register).Distinct().ToArray();
            var bots = actors.Where(player => player.IsAI)
                .Select(player => player.AIData.BotOwner)
                .Where(bot => bot?.Memory != null && bot.EnemiesController != null
                    && GroupFaction(bot.BotsGroup).HasValue).ToArray();
            foreach (var group in bots.Select(bot => bot.BotsGroup).Distinct())
            {
                try { ReconcileGroup(group, actors); }
                catch (Exception ex) { WarnOnce("group reconciliation", ex); }
            }

            foreach (var bot in bots)
            {
                try { ReconcileBot(bot, actors); }
                catch (Exception ex) { WarnOnce("bot awareness", ex); }
            }
        }
        catch (Exception ex) { WarnOnce("relationship reconciliation", ex); }
    }

    private static void ReconcileGroup(BotsGroup group, IReadOnlyList<Player> actors)
    {
        var side = GroupFaction(group);
        foreach (var key in group.Enemies.Keys.ToArray())
        {
            var faction = Faction(key);
            if (faction.HasValue && (faction == side || !IsCurrentBody(key)))
                group.RemoveEnemy(key);
        }
        // Native AddAlly has no dead-body removal counterpart for human bodies;
        // remove only our exact known entries from its public list.
        for (int i = group.Allies.Count - 1; i >= 0; i--)
        {
            var ally = group.Allies[i];
            var faction = Faction(ally);
            if (faction.HasValue && (faction != side || !IsCurrentBody(ally)))
                group.Allies.RemoveAt(i);
        }
        foreach (var actor in actors)
        {
            if (Faction(actor) != side) continue;
            group.Neutrals.Remove(actor);
            if (actor.AIData?.BotOwner is BotOwner owner) group.Neutrals.Remove(owner);
            group.AddAlly(actor);
        }
    }

    private static void ReconcileBot(BotOwner bot, IReadOnlyList<Player> actors)
    {
        var side = Faction(bot);
        foreach (var key in bot.EnemiesController.EnemyInfos.Keys.ToArray())
        {
            var faction = Faction(key);
            if (faction.HasValue && (faction == side || !IsCurrentBody(key)))
                bot.Memory.DeleteInfoAboutEnemy(key);
        }
        object sain = ExactSainController(bot);
        if (sain != null)
        {
            foreach (var ally in actors)
                if (Faction(ally) == side) _sainRemoveEnemy.Invoke(sain, new object[] { ally.ProfileId });
        }
        if (bot.BotState != EBotState.Active || bot.HealthController?.IsAlive != true) return;
        Player nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (var actor in actors)
        {
            if (!AreOpponents(bot, actor)) continue;
            float distance = (actor.Position - bot.Position).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = actor;
        }
        if (nearest == null)
        {
            TdmSearchAwareness.Observe(bot, null, null, "no-opponent");
            return;
        }
        EnemyInfo info = EnsureNativeEnemy(bot, nearest);
        if (info == null)
        {
            TdmSearchAwareness.Observe(bot, nearest, null, "native-pair-unavailable");
            return;
        }
        bot.BotsGroup.ReportAboutEnemy(nearest, EEnemyPartVisibleType.Sence, bot);
        info.GroupInfo.IsLastPositionChecked = false;
        info.IgnoreUntilAggression = false;
        bot.Memory.IsPeace = false;
        if (sain != null) RefreshSain(bot, nearest, sain, info);
        else TdmSearchAwareness.Observe(bot, nearest, null, "native-only");
    }

    private static EnemyInfo EnsureNativeEnemy(BotOwner source, Player target)
    {
        var group = source.BotsGroup;
        if (!group.Enemies.TryGetValue(target, out var groupInfo))
        {
            group.AddEnemy(target, EBotEnemyCause.bossKillArena);
            if (!group.Enemies.TryGetValue(target, out groupInfo)) return null;
        }
        if (groupInfo == null || !ReferenceEquals(groupInfo.Player, target)
            || !ReferenceEquals(groupInfo._botGroup, group)) return null;
        if (!source.EnemiesController.EnemyInfos.TryGetValue(target, out var info))
        {
            source.Memory.AddEnemy(target, groupInfo, onActivation: false);
            source.EnemiesController.EnemyInfos.TryGetValue(target, out info);
        }
        return info != null && ReferenceEquals(info.Person, target)
            && ReferenceEquals(info.GroupInfo, groupInfo) && ReferenceEquals(info.Owner, source)
            && ReferenceEquals(info.GroupOwner, group) ? info : null;
    }

    private static void ResolveSain()
    {
        if (_sainResolved) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(item => item.GetName().Name == "SAIN");
        _sainResolved = true;
        if (assembly == null) return;
        try
        {
            var bot = assembly.GetType("SAIN.Components.BotComponent");
            var list = assembly.GetType("SAIN.SAINComponent.Classes.EnemyClasses.EnemyListController");
            var admission = list?.GetMethod("tryAddEnemy", BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(IPlayer) }, null);
            var controller = bot?.GetProperty("EnemyController");
            var controllerType = controller?.PropertyType;
            var add = controllerType?.GetMethod("CheckAddEnemy", new[] { typeof(IPlayer) });
            var remove = controllerType?.GetMethod("RemoveEnemy", new[] { typeof(string) });
            var enemyType = add?.ReturnType;
            var enemyPlayer = enemyType?.GetProperty("EnemyPlayer");
            var enemyInfo = enemyType?.GetProperty("EnemyInfo");
            var seen = enemyType?.GetMethod("UpdateLastSeenPosition", new[] { typeof(Vector3), typeof(float) });
            var owner = bot?.GetProperty("BotOwner");
            var player = bot?.GetProperty("Player");
            var listOwner = list?.GetProperty("BotOwner");
            if (admission == null || admission.ReturnType != enemyType
                || controller == null || add == null || remove == null
                || enemyPlayer?.PropertyType != typeof(Player) || enemyInfo?.PropertyType != typeof(EnemyInfo)
                || seen == null || owner?.PropertyType != typeof(BotOwner)
                || player?.PropertyType != typeof(Player) || listOwner?.PropertyType != typeof(BotOwner))
                throw new InvalidOperationException("SAIN enemy relationship layout is unsupported");
            _sainBotType = bot;
            _sainOwner = owner;
            _sainPlayer = player;
            _sainController = controller;
            _sainListOwner = listOwner;
            _sainAddEnemy = add;
            _sainRemoveEnemy = remove;
            _sainEnemyPlayer = enemyPlayer;
            _sainEnemyInfo = enemyInfo;
            _sainLastSeen = seen;
            SainHarmony.Patch(admission, prefix: new HarmonyMethod(
                typeof(TeamDeathmatchRelations), nameof(SainEnemyPrefix)));
            TdmSearchAwareness.Configure(enemyType, bot);
            Plugin.ModLogger.LogDebug("Arena TDM: optional SAIN faction admission and awareness available.");
        }
        catch (Exception ex)
        {
            _sainBotType = null;
            Plugin.ModLogger.LogWarning($"Arena TDM: SAIN integration unavailable; native faction protection remains active: {ex.Message}");
        }
    }

    private static bool SainEnemyPrefix(object __instance, IPlayer enemyPlayer, ref object __result)
    {
        if (!Enabled) return true;
        try
        {
            var owner = _sainListOwner?.GetValue(__instance) as BotOwner;
            if (!TryGroupRelationship(owner?.BotsGroup, enemyPlayer, out bool enemies) || enemies)
                return true;
            __result = null;
            return false;
        }
        catch (Exception ex) { WarnOnce("SAIN enemy admission", ex); return true; }
    }

    private static object ExactSainController(BotOwner source)
    {
        if (_sainBotType == null) return null;
        var component = source.GetComponent(_sainBotType);
        return component != null && ReferenceEquals(_sainOwner.GetValue(component), source)
            && ReferenceEquals(_sainPlayer.GetValue(component), source.GetPlayer)
            ? _sainController.GetValue(component) : null;
    }

    private static void RefreshSain(BotOwner source, Player target, object controller, EnemyInfo native)
    {
        if (!IsCurrentSainOpponent(source, target, controller)) return;
        object enemy = _sainAddEnemy.Invoke(controller, new object[] { target });
        if (!IsCurrentSainOpponent(source, target, controller)) return;
        if (!IsExactEnemy(enemy, target, native))
        {
            // SAIN.RemoveEnemy also deletes native memory. Rebuild AFTER removal
            // and verify the new exact dictionary pair before feeding knowledge.
            _sainRemoveEnemy.Invoke(controller, new object[] { target.ProfileId });
            if (!IsCurrentSainOpponent(source, target, controller)) return;
            native = EnsureNativeEnemy(source, target);
            if (native == null) return;
            enemy = _sainAddEnemy.Invoke(controller, new object[] { target });
        }
        if (OwnsAwareness(source, target, controller, enemy, native))
        {
            _sainLastSeen.Invoke(enemy, new object[] { target.Position, Time.time });
            TdmSearchAwareness.Refresh(source, target, controller, enemy, native);
        }
        else TdmSearchAwareness.Observe(source, target, null, "sain-pair-unavailable");
    }

    private static bool IsCurrentSainOpponent(BotOwner source, Player target, object controller)
        => Enabled && _game.Status == GameStatus.Started
            && source != null && source.BotState == EBotState.Active
            && Register(source.GetPlayer) && Register(target)
            && GroupFaction(source.BotsGroup).HasValue && AreOpponents(source, target)
            && controller != null && ReferenceEquals(ExactSainController(source), controller);

    // Recheck after optional mod callbacks: a profile id alone must never transfer
    // a search cue to a replacement body, another raid, or a foreign enemy wrapper.
    internal static bool OwnsAwareness(BotOwner source, Player target, object controller,
        object enemy, EnemyInfo native)
    {
        if (!IsCurrentSainOpponent(source, target, controller) || native == null) return false;
        return source.EnemiesController?.EnemyInfos.TryGetValue(target, out var current) == true
            && ReferenceEquals(current, native) && ReferenceEquals(native.Person, target)
            && ReferenceEquals(native.Owner, source) && ReferenceEquals(native.GroupOwner, source.BotsGroup)
            && source.BotsGroup.Enemies.TryGetValue(target, out var group)
            && ReferenceEquals(native.GroupInfo, group) && ReferenceEquals(group?.Player, target)
            && ReferenceEquals(group._botGroup, source.BotsGroup) && IsExactEnemy(enemy, target, native);
    }

    private static bool IsExactEnemy(object enemy, Player player, EnemyInfo native)
        => enemy != null && ReferenceEquals(_sainEnemyPlayer.GetValue(enemy), player)
            && ReferenceEquals(_sainEnemyInfo.GetValue(enemy), native);

    private static void WarnOnce(string operation, Exception ex)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        Plugin.ModLogger.LogWarning($"Arena TDM {operation} failed (further relationship warnings silenced this raid): {ex.Message}");
    }

    private sealed class ArenaTeamDeathmatchRelationsGroupEnemyAdmissionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.AddEnemy),
                new[] { typeof(IPlayer), typeof(EBotEnemyCause) });
        [PatchPrefix]
        private static bool Prefix(BotsGroup __instance, IPlayer person, ref bool __result)
        {
            if (!TryGroupRelationship(__instance, person, out bool enemies) || enemies) return true;
            __result = false;
            return false;
        }
    }

    private sealed class ArenaTeamDeathmatchRelationsMemoryEnemyAdmissionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotMemory), nameof(BotMemory.AddEnemy),
                new[] { typeof(IPlayer), typeof(BotGroupEnemyInfo), typeof(bool) });
        [PatchPrefix]
        private static bool Prefix(BotMemory __instance, IPlayer enemy)
            => !TryGroupRelationship(__instance?._owner?.BotsGroup, enemy, out bool enemies) || enemies;
    }

    private sealed class ArenaTeamDeathmatchRelationsMemoryGoalAdmissionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertySetter(typeof(BotMemory), nameof(BotMemory.GoalEnemy));
        [PatchPrefix]
        private static bool Prefix(BotMemory __instance, EnemyInfo value)
            => value == null || !TryGroupRelationship(__instance?._owner?.BotsGroup,
                value.Person, out bool enemies) || enemies;
    }
}
