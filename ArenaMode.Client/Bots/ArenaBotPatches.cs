using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Client-side bot behavior for arena sessions. All patches lead with the
/// IsActive gate — normal raids are bit-identical.
/// </summary>
internal static class ArenaBotPatches
{
    public static void Enable()
    {
        new ArenaIsPlayerEnemyPatch().Enable();
        new ArenaBossAttackPatch().Enable();
        new ArenaBotCapPatch().Enable();
        new NoAccidentalBossPatch().Enable();
    }

    public static void ResetRaidState() => NoAccidentalBossPatch.ResetRaidState();

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
        private static void Postfix(IPlayer player, ref bool __result)
        {
            if (!ArenaSession.IsActive || player == null || player.IsAI)
                return;

            __result = true;
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

            var cap = ArenaClientConfig.GetBotCap(ArenaSession.ArmedMap ?? "factory4_day");
            __instance._maxCount = cap;
            __instance.BotSpawner?.SetMaxBots(cap);
            __instance.ZonesLeaveController?.SetMaxBots(cap);
            Plugin.ModLogger.LogInfo($"Arena bot cap set to {cap}.");
        }
    }
}
