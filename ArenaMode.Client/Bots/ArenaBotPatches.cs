using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Bots;

/// <summary>
/// Client-side bot behavior for arena sessions. Loading-time group hooks also
/// require a confirmed Arena arm on its matching map (see AGENTS.md).
/// </summary>
internal static class ArenaBotPatches
{
    public static void Enable()
    {
        TeamDeathmatchRelations.Enable();
        new ArenaIsPlayerEnemyPatch().Enable();
        new DeathmatchBotFriendshipPatch().Enable();
        new DeathmatchIndividualGroupPatch().Enable();
        new DeathmatchAfterCreationPatch().Enable();
        new ArenaBossAttackPatch().Enable();
        new ArenaBotCapPatch().Enable();
        new NoAccidentalBossPatch().Enable();
        ArenaBotSpawnPatches.Enable();
    }

    private sealed class IndividualGroup
    {
        internal BotSpawner Spawner;
        internal BotZone Zone;
        internal BotOwner Bot;
        internal bool CreationReturned, ObservedMembers;
        internal float NextEnemyRetry;
    }
    private static readonly Dictionary<BotsGroup, IndividualGroup> IndividualGroups = new();
    private static float _nextGroupCleanup;
    private static bool _reconciling, _admissionFailureLogged;

    public static void TickIndividualGroups()
    {
        if (!DeathmatchRulesActive() || !ArenaSession.IsActive || UnityEngine.Time.time < _nextGroupCleanup) return;
        _nextGroupCleanup = UnityEngine.Time.time + 1f;
        foreach (var pair in IndividualGroups.ToArray())
        {
            var group = pair.Key;
            var owner = pair.Value;
            if (!OwnsDeathmatchSpawner(owner.Spawner)) { IndividualGroups.Remove(group); continue; }
            // Never dispose a constructor's empty group before BotMemory.Activate
            // adds its first member (AfterCreation still runs PreActive!).
            // After observed membership or native BotDied, only native removal can
            // empty a locked one-member group. Dispose on a later frame, outside
            // death/removal subscriber stacks and only with zero native members.
            if (group.MembersCount > 0) owner.ObservedMembers = true;
            if (group.MembersCount != 0 || (!owner.ObservedMembers && owner.Bot?.IsDead != true)) continue;
            IndividualGroups.Remove(group);
            if (!owner.Spawner.Groups.TryGetValue(owner.Zone, out var registry)) continue;
            registry._bossGroup.Remove(group);
            registry._noConnectionGroups.RemoveAll(candidate => ReferenceEquals(candidate, group));
            foreach (var role in registry.data.Keys.ToArray())
                if (ReferenceEquals(registry.data[role], group)) registry.data[role] = null;
            try { group.Dispose(); }
            catch (System.Exception ex) { Plugin.ModLogger.LogWarning("Arena retired FFA group cleanup: " + ex.Message); }
        }
        ReconcileIndividualEnemies();
    }

    private static void ReconcileIndividualEnemies()
    {
        if (_reconciling) return;
        _reconciling = true;
        try
        {
            var snapshot = IndividualGroups.ToArray();
            foreach (var source in snapshot)
            {
                if (!source.Value.CreationReturned || !OwnsDeathmatchSpawner(source.Value.Spawner)
                    || source.Value.Bot?.BotState != EBotState.Active
                    || source.Value.Bot.HealthController?.IsAlive != true || source.Key.MembersCount == 0
                    || UnityEngine.Time.time < source.Value.NextEnemyRetry) continue;
                foreach (var target in snapshot)
                {
                    if (!OwnsDeathmatchSpawner(source.Value.Spawner)) return;
                    if (ReferenceEquals(source.Key, target.Key) || !target.Value.CreationReturned
                        || !ReferenceEquals(source.Value.Spawner, target.Value.Spawner)
                        || target.Value.Bot?.BotState != EBotState.Active || target.Value.Bot.HealthController?.IsAlive != true) continue;
                    var player = target.Value.Bot.GetPlayer;
                    if (player == null || source.Key.Enemies == null || source.Key.Enemies.ContainsKey(player)) continue;
                    try
                    {
                        // Construction may precede both bots entering the roster.
                        // Native AddEnemy updates every actual member's memory.
                        source.Key.AddEnemy(player, EBotEnemyCause.initial);
                    }
                    catch (System.Exception ex)
                    {
                        source.Value.NextEnemyRetry = UnityEngine.Time.time + 5f;
                        if (!_admissionFailureLogged)
                        {
                            _admissionFailureLogged = true;
                            Plugin.ModLogger.LogWarning("Arena FFA opponent admission failed; retrying with backoff: " + ex.Message);
                        }
                        break;
                    }
                }
            }
        }
        finally { _reconciling = false; }
    }

    public static void ResetRaidState()
    {
        _nextGroupCleanup = 0f;
        _admissionFailureLogged = false;
        if (!DeathmatchRulesActive()) IndividualGroups.Clear();
        else foreach (var pair in IndividualGroups.ToArray())
            if (!OwnsDeathmatchSpawner(pair.Value.Spawner)) IndividualGroups.Remove(pair.Key);
        NoAccidentalBossPatch.ResetRaidState();
        ArenaBotSpawnPatches.ResetRaidState();
    }

    /// <summary>
    /// Keeps ordinary bots from being promoted to squad bosses.
    ///
    /// Every mid-raid injection goes through ActivateBotsByWave, which is a BOSS
    /// spawn path: it sets ShallBeGroup(bossGroup: true), so the spawn callback
    /// calls SetBoss on a plain scav. That flips the bot to IamBoss, replaces its
    /// roaming patrol with a zone-guard loop (and on a "reserved" patrol way the
    /// guard move returns immediately — literally zero movement), and lets it
    /// recruit every other scav in the zone as a follower. The result was the
    /// playtest's cluster of a dozen scavs standing around doing nothing.
    ///
    /// Skipping the promotion leaves the patrol mode the bot already got from
    /// PatrollingData.Activate: plain roaming. Real bosses are untouched.
    /// </summary>
    private sealed class NoAccidentalBossPatch : ModulePatch
    {
        private static int _deniedThisRaid;

        public static void ResetRaidState() => _deniedThisRaid = 0;

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotBoss), nameof(BotBoss.SetBoss), new[] { typeof(int) });

        [PatchPrefix]
        private static bool Prefix(BotBoss __instance)
        {
            // Armed too: bots spawn during the deploy countdown, before IsActive
            // latches, and a promotion granted there sticks for the whole raid
            // (the later activation only sets a patrol mode if none was set).
            if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
                return true;

            var role = __instance?.Owner?.Profile?.Info?.Settings?.Role;
            if (role == null)
                return true;

            // CAREFUL: BSG's IsBoss flag is set for pmcUSEC/pmcBEAR, pmcBot,
            // exUsec, arenaFighter, assaultGroup and shooterBTR as well — gating
            // on it would have promoted exactly the roles this patch exists to
            // protect. IsPureBoss additionally demands "not a follower", which
            // sorts all of those out; the two Goons are genuine bosses that BSG
            // files under followers, so they get named explicitly.
            var keepsBossBehavior = role.Value.IsPureBoss()
                || role.Value.IsSectant()
                || role.Value == WildSpawnType.followerBigPipe
                || role.Value == WildSpawnType.followerBirdEye;

            if (!keepsBossBehavior)
            {
                _deniedThisRaid++;

                // Counted, not one-shot: the count is the only way to tell from a
                // log whether idle bot clusters are still boss-promoted.
                if (_deniedThisRaid <= 3 || _deniedThisRaid % 10 == 0)
                {
                    Plugin.ModLogger.LogInfo(
                        $"Arena: denied boss promotion #{_deniedThisRaid} (role {role.Value}).");
                }
            }

            return keepsBossBehavior;
        }
    }

    /// <summary>
    /// Every bot group treats the human player as an enemy, regardless of what
    /// generation-time hostility configs (SVM, faction mods) decided.
    /// </summary>
    private sealed class ArenaIsPlayerEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.IsPlayerEnemy));

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BotsGroup __instance, IPlayer player, ref bool __result)
        {
            if (TeamDeathmatchRelations.Enabled)
            {
                if (TeamDeathmatchRelations.TryGroupRelationship(__instance, player, out bool enemies))
                    __result = enemies;
                return;
            }
            if (player?.IsAI == true && OwnsDeathmatchGroup(__instance))
            {
                // Native IsSuitable short-circuits same-role enemies before
                // reading the server hostility lists. Freeze the selected rule
                // here as well; never change the human/PFT/TDM admission paths.
                __result = ArenaSession.DeathmatchSelection.FreeForAll
                    && player.AIData?.BotOwner?.BotsGroup != __instance;
                return;
            }
            if (!ArenaSession.IsActive || player == null || player.IsAI)
                return;

            // A group that counts the player among its allies is a TEAM. Without
            // this, a teammate mod's followers would be told every frame that the
            // human they are escorting is an enemy.
            if (ArenaHostility.CountsAsAlly(__instance, player))
                return;

            __result = true;
        }
    }

    internal static bool DeathmatchRulesActive()
    {
        if (ArenaSession.DeathmatchSelection == null || !Comfort.Common.Singleton<GameWorld>.Instantiated) return false;
        var world = Comfort.Common.Singleton<GameWorld>.Instance;
        if (ArenaSession.IsActive && Comfort.Common.Singleton<AbstractGame>.Instantiated
            && ArenaSession.IsExactActiveRaid(Comfort.Common.Singleton<AbstractGame>.Instance, world))
            return ArenaSession.ActiveMode == Modes.ArenaModeCatalog.DeathmatchId;
        // Groups are built during loading. A confirmed Arena arm owns only its
        // matching map; otherwise initial members could remain allied in FFA.
        return ArenaSession.ArmedForNextRaid && ArenaSession.ArmedMode == Modes.ArenaModeCatalog.DeathmatchId
            && string.Equals(world?.LocationId, ArenaSession.ArmedMap, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool OwnsDeathmatchGroup(BotsGroup group)
        => DeathmatchRulesActive() && group != null && Comfort.Common.Singleton<IBotGame>.Instantiated
            && ReferenceEquals(group.BotGame, Comfort.Common.Singleton<IBotGame>.Instance);

    private static bool OwnsDeathmatchSpawner(BotSpawner spawner)
        => DeathmatchRulesActive() && ArenaSession.DeathmatchSelection.FreeForAll
            && Comfort.Common.Singleton<IBotGame>.Instantiated
            && ReferenceEquals(Comfort.Common.Singleton<IBotGame>.Instance.BotsController?.BotSpawner, spawner);

    private static void EnterIndividualGroups(BotSpawner spawner, out bool? previous)
    {
        previous = null;
        if (!OwnsDeathmatchSpawner(spawner)) return;
        previous = spawner._freeForAll;
        // Native AddBot otherwise appends the same group again for every zone
        // and bypasses GetGroups/AddPlayer enumeration for unkeyed groups.
        spawner._freeForAll = false;
    }

    private sealed class DeathmatchIndividualGroupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotSpawner), nameof(BotSpawner.GetGroupAndSetEnemies));
        [PatchPrefix]
        private static void Prefix(BotSpawner __instance, out bool? __state)
            => EnterIndividualGroups(__instance, out __state);
        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BotSpawner __instance, BotZone zone, BotOwner bot, BotsGroup __result, bool? __state)
        {
            if (!__state.HasValue || __result == null || !__instance.Groups.TryGetValue(zone, out var groups)) return;
            // The native role dictionary has one slot per role. The native
            // multi-group registry retains every individual for enemy updates,
            // GetGroups and Dispose. This does NOT give a bot boss behavior.
            groups._bossGroup.Add(__result);
            if (groups.data.TryGetValue(__result.InitialBotType, out var keyed) && ReferenceEquals(keyed, __result))
                groups.data[__result.InitialBotType] = null;
            groups._noConnectionGroups.RemoveAll(group => ReferenceEquals(group, __result));
            __result.Lock();
            IndividualGroups[__result] = new IndividualGroup { Spawner = __instance, Zone = zone, Bot = bot };
        }
        [PatchFinalizer]
        private static void Finalizer(BotSpawner __instance, bool? __state)
        { if (__state.HasValue) __instance._freeForAll = __state.Value; }
    }

    private sealed class DeathmatchAfterCreationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotSpawner), nameof(BotSpawner.AfterCreation));
        [PatchPrefix]
        private static void Prefix(BotSpawner __instance, out bool? __state)
            => EnterIndividualGroups(__instance, out __state);
        [PatchPostfix]
        private static void Postfix(BotOwner bot)
        {
            if (bot?.BotsGroup != null && IndividualGroups.TryGetValue(bot.BotsGroup, out var owner))
                owner.CreationReturned = true;
            if (DeathmatchRulesActive()) ReconcileIndividualEnemies();
        }
        [PatchFinalizer]
        private static void Finalizer(BotSpawner __instance, bool? __state)
        { if (__state.HasValue) __instance._freeForAll = __state.Value; }
    }

    private sealed class DeathmatchBotFriendshipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.AddEnemy), new[] { typeof(IPlayer), typeof(EBotEnemyCause) });
        [PatchPrefix]
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(BotsGroup __instance, IPlayer person, ref bool __result)
        {
            if (person?.IsAI != true || !OwnsDeathmatchGroup(__instance) || ArenaSession.DeathmatchSelection.FreeForAll) return true;
            // A revenge/forced-aggression call may bypass IsPlayerEnemy.
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// ShallBossAttack==true means "loyal player, boss must NOT attack" — the
    /// warn-and-escalate pipeline (CollectWarnTargets → AddPlayersToEnemies) only
    /// processes a player when it returns FALSE. Forcing false for the human makes
    /// bosses treat them as an intruder and escalate to hostile — the fallback for
    /// any boss group whose creation predates the arena hostility patches.
    /// </summary>
    private sealed class ArenaBossAttackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotGroupWarnData), nameof(BotGroupWarnData.ShallBossAttack));

        [PatchPostfix]
        private static void Postfix(Player enemyInfo, ref bool __result)
        {
            // TDM uses faction admission instead of overriding boss loyalty for
            // every human. PMC bots are kept out of boss behavior separately.
            if (TeamDeathmatchRelations.Enabled)
                return;
            if (!ArenaSession.IsActive || enemyInfo == null || !enemyInfo.IsYourPlayer)
                return;

            __result = false;
        }
    }

    /// <summary>
    /// The alive-bot ceiling. HarmonyPriority.Low so this runs AFTER other mods'
    /// SetSettings postfixes (BotPlacementSystem overwrites _maxCount with its own
    /// per-map F12 value) — last writer wins, and in arena that must be us.
    /// </summary>
    private sealed class ArenaBotCapPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsController), nameof(BotsController.SetSettings));

        [PatchPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(BotsController __instance)
        {
            // SetSettings fires during raid INIT, before OnGameStarted latches
            // IsActive — the armed-for-next-raid flag is the correct gate here.
            if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
                return;

            if (ArenaSession.Participants.HasValue)
            {
                var world = Comfort.Common.Singleton<GameWorld>.Instance;
                var game = Comfort.Common.Singleton<AbstractGame>.Instance;
                if (world == null || !ReferenceEquals(
                        Comfort.Common.Singleton<IBotGame>.Instance?.BotsController, __instance)
                    || (ArenaSession.IsActive ? !ArenaSession.IsExactActiveRaid(game, world)
                        : !string.Equals(world.LocationId, ArenaSession.ArmedMap, System.StringComparison.OrdinalIgnoreCase)))
                    return;
            }

            var cap = ArenaClientConfig.GetBotCap(ArenaSession.ArmedMap ?? "factory4_day");
            var mode = ArenaSession.IsActive ? ArenaSession.ActiveMode : ArenaSession.ArmedMode;
            if (ArenaSession.Participants.HasValue)
                cap = ArenaSession.Participants.Value - 1;
            else if (string.Equals(mode, "teamdeathmatch", System.StringComparison.Ordinal))
            {
                int teamSize = System.Math.Max(2, System.Math.Min(12, Config.Settings.TeamDeathmatchSize.Value));
                cap = System.Math.Max(cap, 2 * teamSize - 1);
            }
            else if (string.Equals(mode, Modes.ArenaModeCatalog.ZombieId, System.StringComparison.Ordinal))
            {
                // +2 over the configured alive target: ZombieMaxAlive bounds the
                // wave policy's own spawn pacing, but a body lingers briefly
                // between death and corpse cleanup/slot release, and the native
                // cap must never be the thing that silently drops a requested
                // spawn below what the wave policy asked for.
                cap = System.Math.Max(cap, Config.Settings.ZombieMaxAlive.Value + 2);
            }
            __instance._maxCount = cap;
            __instance.BotSpawner?.SetMaxBots(cap);
            __instance.ZonesLeaveController?.SetMaxBots(cap);
            Plugin.ModLogger.LogInfo($"Arena bot cap set to {cap}.");
        }
    }
}
