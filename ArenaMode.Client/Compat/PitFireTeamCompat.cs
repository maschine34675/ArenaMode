using ArenaMode.Config;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Redeploy.Core.Compat;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace ArenaMode.Compat;

/// <summary>
/// Optional, version-locked adapter for PitFireTeam 0.10.0. No PitFireTeam type
/// appears in a signature or build reference: the external assembly is inspected
/// only after BepInEx reports that exact plugin as loaded.
/// </summary>
internal static class PitFireTeamCompat
{
    public const string PluginGuid = "xyz.pit.fireteam";
    public const string SupportedVersion = "0.10.0";

    private const string SelectedGroupTypeName = "pitTeam.Patches.MainMenuControllerPatch";
    private const string SelectedGroupFieldName = "GroupPlayers";
    private const string SquadFlowTypeName = "pitTeam.Modules.SquadSideSelectionFlow";
    private const string SquadModeActivePropertyName = "SquadModeActive";
    private const string SquadOpenMethodName = "Open";
    private const string BossPlayerTypeName = "pitTeam.Components.pitAIBossPlayer";
    private const string BossPlayerDeathMethodName = "OnDead";
    private const string BossPlayersTypeName = "pitTeam.Modules.BossPlayers";
    private const string FollowerTypeName = "pitTeam.Components.BotFollowerPlayer";
    private const string GetFollowersMethodName = "GetFollowers";
    private const string GetBotMethodName = "GetBot";
    private const string GetBossMethodName = "GetBoss";
    private const string BossRealPlayerFieldName = "realPlayer";
    private const string SaveFollowersProgressMethodName = "SaveFollowersProgress";
    private const string BotsControllerStopPatchTypeName = "pitTeam.Patches.BotsControllerStopPatch";
    private const string BotsControllerStopPrefixMethodName = "PatchPrefix";
    private const string TeammateProfileRoute = "/singleplayer/pitfireteam/teammate/profile";
    private const int FinalizingSeconds = 30;

    private static readonly object StateLock = new();
    private static readonly HashSet<string> FrozenAccountIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> FrozenProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BotOwner> FrozenBodiesByAccountId =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> KnownProfileIds = new(StringComparer.Ordinal);
    private static readonly Harmony Harmony = new(Plugin.PluginGuid + ".pitfireteam");
    private static readonly Harmony UiHarmony = new(Plugin.PluginGuid + ".pitfireteam-ui");

    private static bool _initialized;
    private static bool _deathSuppressionLogged;
    private static bool _progressSuppressionLogged;
    private static bool _followerDiscoveryFailureLogged;
    private static bool _hostilityFailureLogged;
    private static bool _stopCleanupFailureLogged;
    private static string _runtimeRosterProblem = string.Empty;
    private static PitFireTeamContractState _contractState = PitFireTeamContractState.Absent;
    private static string _contractProblem = string.Empty;
    private static FieldInfo _selectedGroupField;
    private static FieldInfo _selectedAccountIdField;
    private static FieldInfo _selectedRaidIdField;
    private static PropertyInfo _squadModeActiveProperty;
    private static MethodInfo _getFollowersMethod;
    private static MethodInfo _getFollowerBotMethod;
    private static MethodInfo _getFollowerBossMethod;
    private static FieldInfo _bossRealPlayerField;
    private static Player _frozenLocalPlayer;
    private static string _frozenLocalProfileId;
    private static object _frozenLeaderWrapper;
    private static DateTime _finalizingUntilUtc;
    private static int _squadOpenDepth;
    private static IReadOnlyList<StopCleanupStep> _stopCleanupSteps = Array.Empty<StopCleanupStep>();

    private sealed class StopCleanupStep
    {
        public string Name { get; set; }
        public Action Run { get; set; }
    }

    public static bool IsInstalled
        => Chainloader.PluginInfos.ContainsKey(PluginGuid);

    public static PitFireTeamContractState ContractState
    {
        get
        {
            EnsureInitialized();
            return _contractState;
        }
    }

    public static string ContractProblem
    {
        get
        {
            EnsureInitialized();
            return _contractProblem;
        }
    }

    /// <summary>
    /// PitFireTeam reuses the vanilla side-selection scene for its own "My
    /// Squad" UI. Arena must not reactivate the Random/Arena toggle that PFT
    /// deliberately hides on that screen.
    /// </summary>
    public static bool IsSquadManagementScreen
    {
        get
        {
            if (!IsInstalled)
                return false;

            EnsureInitialized();
            try
            {
                // Open() is patched independently of the 5v5 contract and stays
                // true around PFT's synchronous nested side-screen creation.
                // This remains a PFT-specific fallback if a changed layout loses
                // SquadModeActive; vanilla Random visibility is never consulted.
                if (Volatile.Read(ref _squadOpenDepth) > 0)
                    return true;

                if (_squadModeActiveProperty == null)
                    return false;

                return _squadModeActiveProperty.GetValue(null) is true;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena could not read PitFireTeam's squad-screen state; only the independent Open scope will hide it: {ex.Message}");
                return Volatile.Read(ref _squadOpenDepth) > 0;
            }
        }
    }

    /// <summary>The authenticated roster is frozen before the server is armed.</summary>
    public static bool HasFrozenTeamRoster
    {
        get
        {
            lock (StateLock)
                return HasCompleteFrozenRosterUnsafe();
        }
    }

    /// <summary>
    /// True after final raid-start authentication froze exactly four companions
    /// and until protected finalization begins. It intentionally becomes true
    /// before server arming so the server can require external-profile protection
    /// for this request. Gameplay helpers additionally require ArenaSession.IsActive.
    /// </summary>
    public static bool TeamArenaActive
    {
        get
        {
            lock (StateLock)
                return HasCompleteFrozenRosterUnsafe()
                    && _finalizingUntilUtc == DateTime.MinValue;
        }
    }

    private static bool LiveTeamArena => TeamArenaActive && ArenaSession.IsActive;

    private static bool ProtectedAbortPending
        => Modes.ArenaMatchService.TeamStartAbortPending;

    public static bool IsFinalizing
    {
        get
        {
            lock (StateLock)
                return HasCompleteFrozenRosterUnsafe()
                    && DateTime.UtcNow < _finalizingUntilUtc;
        }
    }

    public static IReadOnlyList<string> FrozenCompanionAccountIds
    {
        get
        {
            lock (StateLock)
                return FrozenAccountIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
    }

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        DiscoverContract();
    }

    public static PitFireTeamSelectionDecision EvaluateSelection(
        MatchmakerPlayersController controller, RaidSettings raidSettings)
    {
        EnsureInitialized();

        var nativeAccountIds = ReadNativeGroupAccountIds(controller);
        var currentAccountId = controller?.CurrentPlayer?.AccountId;
        IReadOnlyList<string> selectedAccountIds = Array.Empty<string>();
        IReadOnlyList<string> selectedRaidIds = Array.Empty<string>();
        IReadOnlyList<string> selectedProfileIds = Array.Empty<string>();
        var contract = _contractState;

        if (contract == PitFireTeamContractState.Compatible
            && !TryReadSelectedMembers(
                out selectedAccountIds,
                out selectedRaidIds,
                out var readProblem))
        {
            InvalidateContract("selected-member discovery failed: " + readProblem);
            contract = PitFireTeamContractState.Incompatible;
        }

        if (contract == PitFireTeamContractState.Compatible
            && !TryResolveSelectedProfileIds(
                selectedAccountIds,
                selectedRaidIds,
                out selectedProfileIds,
                out var identityProblem))
        {
            return PitFireTeamSelectionDecision.Reject(
                "Arena could not authenticate every selected stored PitFireTeam teammate: "
                + identityProblem);
        }

        return PitFireTeamSelectionPolicy.Evaluate(new PitFireTeamSelectionInput
        {
            Contract = contract,
            IsFika = FikaCompatCheck.IsFikaLoaded,
            IsCoop = raidSettings?.RaidMode == ERaidMode.Coop,
            GameMode = Settings.GameMode?.Value,
            CurrentPlayerAccountId = currentAccountId,
            NativeGroupAccountIds = nativeAccountIds,
            SelectedPitAccountIds = selectedAccountIds,
            SelectedPitProfileIds = selectedProfileIds
        });
    }

    /// <summary>
    /// Freeze only the already live-validated decision. The caller evaluates it
    /// once alongside the final map/request checks, avoiding display-time state.
    /// </summary>
    public static bool TryFreezeTeamRoster(PitFireTeamSelectionDecision decision, out string reason)
    {
        reason = string.Empty;
        if (decision == null || !decision.Allowed)
        {
            reason = decision?.Reason ?? "Arena could not validate the current PitFireTeam roster.";
            return false;
        }

        lock (StateLock)
        {
            FrozenAccountIds.Clear();
            FrozenProfilesByAccountId.Clear();
            FrozenBodiesByAccountId.Clear();
            KnownProfileIds.Clear();
            _frozenLocalPlayer = null;
            _frozenLocalProfileId = null;
            _frozenLeaderWrapper = null;
            _finalizingUntilUtc = DateTime.MinValue;
            _deathSuppressionLogged = false;
            _progressSuppressionLogged = false;
            _followerDiscoveryFailureLogged = false;
            _hostilityFailureLogged = false;
            _stopCleanupFailureLogged = false;
            _runtimeRosterProblem = string.Empty;

            if (!decision.IsTeam)
                return true;

            if (_contractState != PitFireTeamContractState.Compatible
                || decision.CompanionAccountIds.Count != PitFireTeamSelectionPolicy.RequiredCompanionCount
                || decision.CompanionProfilesByAccountId.Count !=
                    PitFireTeamSelectionPolicy.RequiredCompanionCount)
            {
                reason = "The PitFireTeam runtime or companion roster changed before Arena could arm.";
                return false;
            }

            foreach (var accountId in decision.CompanionAccountIds)
            {
                var normalizedAccount = accountId?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedAccount)
                    || !decision.CompanionProfilesByAccountId.TryGetValue(normalizedAccount, out var profileId)
                    || string.IsNullOrWhiteSpace(profileId)
                    || !FrozenAccountIds.Add(normalizedAccount)
                    || !FrozenProfilesByAccountId.TryAdd(normalizedAccount, profileId.Trim())
                    || !KnownProfileIds.Add(profileId.Trim()))
                {
                    FrozenAccountIds.Clear();
                    FrozenProfilesByAccountId.Clear();
                    KnownProfileIds.Clear();
                    reason = "Arena refused an invalid, duplicate or mismatched PitFireTeam companion identity.";
                    return false;
                }
            }
        }

        Plugin.ModLogger.LogInfo(
            $"Arena authenticated PitFireTeam roster: {string.Join(", ", FrozenCompanionAccountIds)}.");
        return true;
    }

    /// <summary>
    /// Freeze the exact local PMC body which PFT's leader wrapper must own for
    /// the whole one-round raid. Tactical calls this before follower discovery;
    /// changing the body later is an unrecoverable physical-roster violation.
    /// </summary>
    public static bool TryBindLocalPlayerBody(Player player, out string reason)
    {
        reason = string.Empty;
        if (ReferenceEquals(player, null) || player == null)
        {
            reason = "Arena could not bind the exact local PMC body for PitFireTeam.";
            return false;
        }

        string profileId = player.ProfileId?.Trim();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            reason = "Arena could not bind the local PMC profile identity for PitFireTeam.";
            return false;
        }

        lock (StateLock)
        {
            if (!HasCompleteFrozenRosterUnsafe())
            {
                reason = "Arena lost the authenticated PitFireTeam roster before binding its local leader.";
                return false;
            }

            if (!ReferenceEquals(_frozenLocalPlayer, null))
            {
                if (ReferenceEquals(_frozenLocalPlayer, player)
                    && string.Equals(_frozenLocalProfileId, profileId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(_runtimeRosterProblem))
                    _runtimeRosterProblem = "Arena attempted to replace the exact local PitFireTeam leader body";
                reason = _runtimeRosterProblem;
                return false;
            }

            _frozenLocalPlayer = player;
            _frozenLocalProfileId = profileId;
        }

        return true;
    }

    /// <summary>
    /// Discover and cache follower bodies while proving that PFT has exactly one
    /// active body for each frozen account/profile pair and no active unselected
    /// follower. Once an identity becomes active, that exact BotOwner stays bound
    /// for the one-round raid; a replacement would amount to an illicit respawn.
    /// The first physical-roster violation is latched for the raid; silently
    /// deduplicating it would let a sixth friendly actor enter a claimed 5v5.
    /// </summary>
    public static bool TryGetLiveFollowers(
        out IReadOnlyList<BotOwner> followers,
        out string problem)
    {
        followers = Array.Empty<BotOwner>();
        lock (StateLock)
            problem = _runtimeRosterProblem;

        if (!string.IsNullOrEmpty(problem))
            return false;

        if (!LiveTeamArena || _contractState != PitFireTeamContractState.Compatible)
            return true;

        try
        {
            bool rosterFullyBound;
            Dictionary<string, BotOwner> boundBodies;
            Player frozenLocalPlayer;
            string frozenLocalProfileId;
            lock (StateLock)
            {
                rosterFullyBound = FrozenBodiesByAccountId.Count
                    == PitFireTeamSelectionPolicy.RequiredCompanionCount;
                boundBodies = new Dictionary<string, BotOwner>(
                    FrozenBodiesByAccountId,
                    StringComparer.Ordinal);
                frozenLocalPlayer = _frozenLocalPlayer;
                frozenLocalProfileId = _frozenLocalProfileId;
            }

            if (ReferenceEquals(frozenLocalPlayer, null)
                || frozenLocalPlayer == null
                || string.IsNullOrWhiteSpace(frozenLocalProfileId)
                || !string.Equals(
                    frozenLocalPlayer.ProfileId?.Trim(),
                    frozenLocalProfileId,
                    StringComparison.Ordinal))
            {
                return LatchRuntimeRosterProblem(
                    "the exact local PMC body was not available for PitFireTeam leader verification",
                    out followers,
                    out problem);
            }

            if (_getFollowersMethod.Invoke(null, null) is not IEnumerable reflectedFollowers)
            {
                if (rosterFullyBound)
                {
                    return LatchRuntimeRosterProblem(
                        "PitFireTeam lost its live follower collection after the 5v5 roster was bound",
                        out followers,
                        out problem);
                }

                return true;
            }

            HashSet<string> seenAccounts = new(StringComparer.Ordinal);
            HashSet<string> seenProfiles = new(StringComparer.Ordinal);
            List<BotOwner> seenBodies = new();
            HashSet<string> seenBoundBodies = new(StringComparer.Ordinal);
            List<BotOwner> live = new(PitFireTeamSelectionPolicy.RequiredCompanionCount);
            foreach (var follower in reflectedFollowers)
            {
                if (follower == null || _getFollowerBotMethod.Invoke(follower, null) is not BotOwner bot)
                    continue;

                var accountId = bot.Profile?.AccountId?.Trim();
                bool frozen = TryGetFrozenProfileId(accountId, out var expectedProfileId);
                bool liveBody = bot.BotState == EBotState.Active
                    && bot.HealthController?.IsAlive == true
                    && !bot.IsDead;

                if (!frozen)
                {
                    // Dead/inactive unselected wrappers can survive PFT teardown
                    // and cannot affect the fixed 5v5. A living extra still
                    // invalidates the roster immediately.
                    if (!liveBody)
                    {
                        continue;
                    }

                    return LatchRuntimeRosterProblem(
                        $"an active unselected PitFireTeam follower appeared (account {accountId ?? "?"})",
                        out followers,
                        out problem);
                }

                var profileId = bot.ProfileId?.Trim();
                if (string.IsNullOrEmpty(profileId)
                    || !string.Equals(profileId, expectedProfileId, StringComparison.Ordinal))
                {
                    return LatchRuntimeRosterProblem(
                        $"PitFireTeam account {accountId} spawned unexpected profile {profileId ?? "?"} " +
                        $"instead of {expectedProfileId}",
                        out followers,
                        out problem);
                }

                bool replacementBody;
                bool alreadyBoundToThisBody;
                lock (StateLock)
                {
                    replacementBody = FrozenBodiesByAccountId.TryGetValue(accountId, out var originalBody)
                        && !ReferenceEquals(originalBody, bot);
                    alreadyBoundToThisBody = !replacementBody
                        && originalBody != null
                        && ReferenceEquals(originalBody, bot);
                }

                if (replacementBody)
                {
                    return LatchRuntimeRosterProblem(
                        $"PitFireTeam replaced the one-round body for account {accountId}",
                        out followers,
                        out problem);
                }

                // PFT's global follower list can contain wrappers owned by a
                // stale or different player-boss. Every active selected wrapper,
                // and every wrapper whose body was already frozen, must resolve
                // to one common PFT leader wrapper whose readonly realPlayer is
                // the exact local PMC body frozen above.
                if (liveBody || alreadyBoundToThisBody)
                {
                    object leaderWrapper = _getFollowerBossMethod.Invoke(follower, null);
                    Player leaderPlayer = leaderWrapper == null
                        ? null
                        : _bossRealPlayerField.GetValue(leaderWrapper) as Player;
                    if (leaderWrapper == null
                        || ReferenceEquals(leaderPlayer, null)
                        || leaderPlayer == null
                        || !ReferenceEquals(leaderPlayer, frozenLocalPlayer))
                    {
                        return LatchRuntimeRosterProblem(
                            $"PitFireTeam follower {profileId} was bound to a stale or wrong local leader body",
                            out followers,
                            out problem);
                    }

                    bool splitLeader;
                    lock (StateLock)
                    {
                        splitLeader = _frozenLeaderWrapper != null
                            && !ReferenceEquals(_frozenLeaderWrapper, leaderWrapper);
                        if (!splitLeader && _frozenLeaderWrapper == null)
                            _frozenLeaderWrapper = leaderWrapper;
                    }

                    if (splitLeader)
                    {
                        return LatchRuntimeRosterProblem(
                            "PitFireTeam split the four Arena followers across different leader wrappers",
                            out followers,
                            out problem);
                    }
                }

                if (seenBodies.Any(existing => ReferenceEquals(existing, bot)))
                    continue;
                seenBodies.Add(bot);

                if (!alreadyBoundToThisBody && liveBody)
                {
                    lock (StateLock)
                        FrozenBodiesByAccountId[accountId] = bot;
                }

                // Before physical binding, dead/inactive selected wrappers are
                // just incomplete assembly and must not become the canonical
                // body. Once bound, the same corpse remains valid evidence.
                bool boundToThisBody;
                lock (StateLock)
                {
                    boundToThisBody = FrozenBodiesByAccountId.TryGetValue(accountId, out var originalBody)
                        && ReferenceEquals(originalBody, bot);
                }
                if (!boundToThisBody)
                    continue;

                seenBoundBodies.Add(accountId);
                if (bot.HealthController?.IsAlive != true || bot.IsDead)
                    continue;

                if (bot.BotState != EBotState.Active)
                {
                    return LatchRuntimeRosterProblem(
                        $"the bound PitFireTeam body for account {accountId} became inactive while still alive",
                        out followers,
                        out problem);
                }

                if (!seenAccounts.Add(accountId))
                {
                    return LatchRuntimeRosterProblem(
                        $"multiple active PitFireTeam bodies used account {accountId}",
                        out followers,
                        out problem);
                }

                if (!seenProfiles.Add(profileId))
                {
                    return LatchRuntimeRosterProblem(
                        "PitFireTeam returned a missing or duplicate active follower profile identity",
                        out followers,
                        out problem);
                }

                live.Add(bot);
            }

            if (rosterFullyBound)
            {
                var world = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance
                    : null;
                if (ReferenceEquals(world, null) || world == null)
                {
                    return LatchRuntimeRosterProblem(
                        "GameWorld became unavailable while verifying the bound PitFireTeam bodies",
                        out followers,
                        out problem);
                }

                foreach (var pair in boundBodies)
                {
                    BotOwner original = pair.Value;
                    if (!TryGetFrozenProfileId(pair.Key, out string expectedProfileId))
                    {
                        return LatchRuntimeRosterProblem(
                            $"the frozen PitFireTeam profile for account {pair.Key} disappeared",
                            out followers,
                            out problem);
                    }

                    // GameWorld never removes this entry; it overwrites it when
                    // another body registers with the same profile. This catches
                    // a replacement which appeared and died between two Arena
                    // ticks and is already absent from PFT's live wrappers.
                    Player everExisted = world.GetEverExistedPlayerByID(expectedProfileId);
                    Player originalPlayer = original?.GetPlayer;
                    if (ReferenceEquals(everExisted, null)
                        || everExisted == null
                        || ReferenceEquals(originalPlayer, null)
                        || originalPlayer == null
                        || !ReferenceEquals(everExisted, originalPlayer))
                    {
                        return LatchRuntimeRosterProblem(
                            $"PitFireTeam registered a replacement body for account {pair.Key}",
                            out followers,
                            out problem);
                    }

                    bool demonstrablyDead = original != null
                        && (original.IsDead || original.HealthController?.IsAlive == false);
                    if (!demonstrablyDead && !seenBoundBodies.Contains(pair.Key))
                    {
                        return LatchRuntimeRosterProblem(
                            $"the living bound PitFireTeam body for account {pair.Key} disappeared",
                            out followers,
                            out problem);
                    }
                }
            }

            followers = live;
            return true;
        }
        catch (Exception ex)
        {
            if (!_followerDiscoveryFailureLogged)
            {
                _followerDiscoveryFailureLogged = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena could not inspect live PitFireTeam followers (silenced for this raid): {ex}");
            }

            return LatchRuntimeRosterProblem(
                "PitFireTeam follower inspection failed: " + ex.GetBaseException().Message,
                out followers,
                out problem);
        }
    }

    public static IReadOnlyList<BotOwner> GetLiveFollowers()
        => TryGetLiveFollowers(out var followers, out _) ? followers : Array.Empty<BotOwner>();

    private static bool LatchRuntimeRosterProblem(
        string value,
        out IReadOnlyList<BotOwner> followers,
        out string problem)
    {
        lock (StateLock)
        {
            if (string.IsNullOrEmpty(_runtimeRosterProblem))
            {
                _runtimeRosterProblem = value;
                Plugin.ModLogger.LogError(
                    $"Arena rejected the physical PitFireTeam roster: {_runtimeRosterProblem}.");
            }

            problem = _runtimeRosterProblem;
        }

        followers = Array.Empty<BotOwner>();
        return false;
    }

    public static bool IsTeamMember(BotOwner bot)
    {
        if (!LiveTeamArena || bot == null)
            return false;

        var accountId = bot.Profile?.AccountId?.Trim();
        if (FrozenIdentityMatches(accountId, bot.ProfileId))
            return true;

        return IsTeamProfileId(bot.ProfileId);
    }

    public static bool IsTeamPlayer(Player player)
    {
        if (!LiveTeamArena || player == null)
            return false;

        if (FrozenIdentityMatches(player.AccountId?.Trim(), player.ProfileId))
            return true;

        return IsTeamProfileId(player.ProfileId);
    }

    public static bool IsTeamProfileId(string profileId)
    {
        // Corpse cleanup can tick after Tactical has started finalization but
        // before GameEnd clears ArenaSession.IsActive. Keep known teammate bodies
        // protected for that bounded end sequence too.
        if ((!LiveTeamArena && !IsFinalizing && !ProtectedAbortPending)
            || string.IsNullOrWhiteSpace(profileId))
            return false;

        lock (StateLock)
            return KnownProfileIds.Contains(profileId.Trim());
    }

    /// <summary>
    /// Make every selected follower and the local player enemies of every Arena
    /// opponent in both directions. Returns true only after the full matrix is
    /// observable through EFT's own group state.
    /// </summary>
    public static bool EnsureHostility(IReadOnlyList<BotOwner> enemies, Player boundLocalPlayer)
    {
        if (!LiveTeamArena
            || enemies == null
            || enemies.Count == 0
            || ReferenceEquals(boundLocalPlayer, null)
            || boundLocalPlayer == null)
            return false;

        try
        {
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var followers = GetLiveFollowers();
            var currentPlayer = world?.MainPlayer;
            bool boundPlayerDead = boundLocalPlayer.HealthController?.IsAlive == false;
            if (!ReferenceEquals(currentPlayer, boundLocalPlayer)
                && (currentPlayer != null || !boundPlayerDead))
                return false;

            // Readiness callers separately require 5/5. Once combat is live,
            // keep reasserting hostility for whoever survives: losing the local
            // player or one follower must not prevent the remaining followers
            // from retaining every Arena opponent as an enemy.
            var team = new List<IPlayer>(PitFireTeamSelectionPolicy.RequiredCompanionCount + 1);
            if (boundLocalPlayer.HealthController?.IsAlive == true)
                team.Add(boundLocalPlayer);
            team.AddRange(followers.Cast<IPlayer>());
            if (team.Count == 0)
                return false;

            HashSet<string> enemyProfiles = new(StringComparer.Ordinal);
            List<BotOwner> liveEnemies = new(enemies.Count);
            foreach (var enemy in enemies)
            {
                if (enemy == null)
                    continue;

                // A dead opponent remains in Tactical's fixed five-member
                // roster so elimination can be counted. It no longer belongs
                // in a live hostility matrix; rejecting the whole list here
                // would stop reassertion for every survivor after the first
                // kill. A health-alive but not-yet-active actor still fails
                // closed, which is what staging readiness needs.
                if (enemy.HealthController?.IsAlive != true || enemy.IsDead)
                    continue;

                if (enemy.BotState != EBotState.Active
                    || enemy.BotsGroup == null || IsTeamMember(enemy)
                    || string.IsNullOrWhiteSpace(enemy.ProfileId)
                    || !enemyProfiles.Add(enemy.ProfileId))
                    return false;

                liveEnemies.Add(enemy);
            }

            if (liveEnemies.Count == 0)
                return false;

            foreach (var enemy in liveEnemies)
            {
                foreach (var teamMember in team)
                {
                    if (!enemy.BotsGroup.IsEnemy(teamMember))
                        enemy.BotsGroup.AddEnemy(teamMember, EBotEnemyCause.bossKillArena);
                }

                foreach (var follower in followers)
                {
                    if (follower.BotsGroup == null)
                        return false;

                    if (!follower.BotsGroup.IsEnemy(enemy))
                        follower.BotsGroup.AddEnemy(enemy, EBotEnemyCause.bossKillArena);
                }
            }

            foreach (var enemy in liveEnemies)
            {
                foreach (var teamMember in team)
                {
                    if (!enemy.BotsGroup.IsEnemy(teamMember))
                        return false;
                }

                foreach (var follower in followers)
                {
                    if (!follower.BotsGroup.IsEnemy(enemy))
                        return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!_hostilityFailureLogged)
            {
                _hostilityFailureLogged = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena PitFireTeam hostility matrix failed (silenced for this raid): {ex}");
            }
            return false;
        }
    }

    public static void Tick()
    {
        if (LiveTeamArena)
            _ = GetLiveFollowers();
    }

    public static void BeginFinalizing()
    {
        lock (StateLock)
        {
            if (HasCompleteFrozenRosterUnsafe())
                _finalizingUntilUtc = DateTime.UtcNow.AddSeconds(FinalizingSeconds);
        }
    }

    /// <summary>
    /// LocalGame.Stop can fail synchronously before EFT starts its end sequence.
    /// Restore the live team latch so Tactical can retry without dropping the
    /// follower/death/hostility guards in the still-running raid.
    /// </summary>
    public static void CancelFinalizingAfterFailedStop()
    {
        lock (StateLock)
        {
            if (HasCompleteFrozenRosterUnsafe())
                _finalizingUntilUtc = DateTime.MinValue;
        }
    }

    public static void Clear(string reason)
    {
        bool hadState;
        lock (StateLock)
        {
            hadState = FrozenAccountIds.Count > 0 || FrozenProfilesByAccountId.Count > 0
                || FrozenBodiesByAccountId.Count > 0 || KnownProfileIds.Count > 0
                || _finalizingUntilUtc != DateTime.MinValue;
            FrozenAccountIds.Clear();
            FrozenProfilesByAccountId.Clear();
            FrozenBodiesByAccountId.Clear();
            KnownProfileIds.Clear();
            _frozenLocalPlayer = null;
            _frozenLocalProfileId = null;
            _frozenLeaderWrapper = null;
            _finalizingUntilUtc = DateTime.MinValue;
            _deathSuppressionLogged = false;
            _progressSuppressionLogged = false;
            _followerDiscoveryFailureLogged = false;
            _hostilityFailureLogged = false;
            _stopCleanupFailureLogged = false;
            _runtimeRosterProblem = string.Empty;
        }

        if (hadState)
            Plugin.ModLogger.LogInfo($"Arena cleared PitFireTeam compatibility state ({reason}).");
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
    }

    private static void DiscoverContract()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo pluginInfo))
        {
            _contractState = PitFireTeamContractState.Absent;
            _contractProblem = string.Empty;
            return;
        }

        MethodInfo deathMethod = null;
        MethodInfo saveFollowersProgressMethod = null;
        MethodInfo stopGettingInfoPrefixMethod = null;
        try
        {
            if (pluginInfo?.Instance == null)
                throw new InvalidOperationException("the BepInEx plugin instance is unavailable");

            var assembly = pluginInfo.Instance.GetType().Assembly;
            if (!string.Equals(assembly.GetName().Name, "pitFireTeam", StringComparison.Ordinal))
                throw new InvalidOperationException($"assembly identity '{assembly.FullName}' is unsupported");

            // This UI marker is useful even when the 5v5 contract/version is
            // rejected: an unsupported PFT must still keep its My Squad screen
            // isolated from Arena's native third toggle.
            var squadFlowType = RequireType(assembly, SquadFlowTypeName);
            var squadOpenMethod = squadFlowType.GetMethod(SquadOpenMethodName,
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(SquadFlowTypeName, SquadOpenMethodName);
            if (squadOpenMethod.ReturnType != typeof(void))
                throw new InvalidOperationException("PitFireTeam squad-screen Open has an unsupported signature");

            var squadOpenPrefix = typeof(PitFireTeamCompat).GetMethod(nameof(PitSquadOpenPrefix),
                BindingFlags.NonPublic | BindingFlags.Static);
            var squadOpenFinalizer = typeof(PitFireTeamCompat).GetMethod(nameof(PitSquadOpenFinalizer),
                BindingFlags.NonPublic | BindingFlags.Static);
            UiHarmony.Patch(
                squadOpenMethod,
                prefix: new HarmonyMethod(squadOpenPrefix),
                finalizer: new HarmonyMethod(squadOpenFinalizer));

            _squadModeActiveProperty = squadFlowType.GetProperty(SquadModeActivePropertyName,
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMemberException(SquadFlowTypeName, SquadModeActivePropertyName);
            if (_squadModeActiveProperty.PropertyType != typeof(bool)
                || _squadModeActiveProperty.GetMethod == null)
                throw new InvalidOperationException("PitFireTeam squad-screen state has an unsupported signature");

            if (!string.Equals(pluginInfo.Metadata.Version?.ToString(), SupportedVersion,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"version {pluginInfo.Metadata.Version ?? new System.Version(0, 0)} is unsupported (expected {SupportedVersion})");

            if (assembly.GetName().Version != new System.Version(0, 10, 0, 0))
                throw new InvalidOperationException($"assembly identity '{assembly.FullName}' is unsupported");

            var selectedGroupType = RequireType(assembly, SelectedGroupTypeName);
            _selectedGroupField = selectedGroupType.GetField(SelectedGroupFieldName,
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingFieldException(SelectedGroupTypeName, SelectedGroupFieldName);

            var selectedElementType = RequireListElementType(_selectedGroupField.FieldType,
                "selected-member group");
            if (selectedElementType != typeof(RaidPlayer))
                throw new InvalidOperationException(
                    $"selected-member element type '{selectedElementType.FullName}' is unsupported");

            // RaidPlayer inherits these as public GroupPlayer FIELDS in EFT
            // 4.1. They are not CLR properties; GetProperty would reject the
            // real PitFireTeam 0.10.0 runtime on every launch.
            _selectedAccountIdField = selectedElementType.GetField(nameof(RaidPlayer.AccountId),
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(selectedElementType.FullName, nameof(RaidPlayer.AccountId));
            if (_selectedAccountIdField.FieldType != typeof(string))
                throw new InvalidOperationException("PitFireTeam RaidPlayer.AccountId has an unsupported type");

            _selectedRaidIdField = selectedElementType.GetField(nameof(RaidPlayer.Id),
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(selectedElementType.FullName, nameof(RaidPlayer.Id));
            if (_selectedRaidIdField.FieldType != typeof(string))
                throw new InvalidOperationException("PitFireTeam RaidPlayer.Id has an unsupported type");

            var followerType = RequireType(assembly, FollowerTypeName);
            var bossPlayerType = RequireType(assembly, BossPlayerTypeName);
            _getFollowerBotMethod = followerType.GetMethod(GetBotMethodName,
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(FollowerTypeName, GetBotMethodName);
            if (_getFollowerBotMethod.ReturnType != typeof(BotOwner))
                throw new InvalidOperationException("PitFireTeam GetBot has an unsupported return type");

            _getFollowerBossMethod = followerType.GetMethod(GetBossMethodName,
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(FollowerTypeName, GetBossMethodName);
            if (_getFollowerBossMethod.ReturnType != bossPlayerType)
                throw new InvalidOperationException("PitFireTeam GetBoss has an unsupported return type");

            _bossRealPlayerField = bossPlayerType.GetField(BossRealPlayerFieldName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingFieldException(BossPlayerTypeName, BossRealPlayerFieldName);
            if (_bossRealPlayerField.FieldType != typeof(Player) || !_bossRealPlayerField.IsInitOnly)
                throw new InvalidOperationException("PitFireTeam leader realPlayer has an unsupported layout");

            var bossPlayersType = RequireType(assembly, BossPlayersTypeName);
            _getFollowersMethod = bossPlayersType.GetMethod(GetFollowersMethodName,
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(BossPlayersTypeName, GetFollowersMethodName);
            if (RequireListElementType(_getFollowersMethod.ReturnType, "live-follower collection") != followerType)
                throw new InvalidOperationException("PitFireTeam GetFollowers has an unsupported return type");

            var followerListType = typeof(List<>).MakeGenericType(followerType);
            saveFollowersProgressMethod = bossPlayersType.GetMethod(SaveFollowersProgressMethodName,
                BindingFlags.Public | BindingFlags.Static, null, new[] { followerListType }, null)
                ?? throw new MissingMethodException(BossPlayersTypeName, SaveFollowersProgressMethodName);
            var saveParameters = saveFollowersProgressMethod.GetParameters();
            if (saveFollowersProgressMethod.ReturnType != typeof(void)
                || saveParameters.Length != 1 || !saveParameters[0].IsOptional
                || saveParameters[0].DefaultValue != null)
                throw new InvalidOperationException("PitFireTeam SaveFollowersProgress has an unsupported signature");

            // If PFT's own stop prefix throws halfway through, our finalizer must
            // finish every critical reset independently before it can safely
            // swallow the exception. Validate that exact 0.10.0 cleanup surface
            // up front; otherwise team Arena is not offered.
            _stopCleanupSteps = BuildStopCleanupContract(assembly);

            deathMethod = bossPlayerType.GetMethod(BossPlayerDeathMethodName,
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(EDamageType) }, null)
                ?? throw new MissingMethodException(BossPlayerTypeName, BossPlayerDeathMethodName);
            if (deathMethod.ReturnType != typeof(void))
                throw new InvalidOperationException("PitFireTeam leader death callback has an unsupported signature");

            var stopPatchType = RequireType(assembly, BotsControllerStopPatchTypeName);
            stopGettingInfoPrefixMethod = stopPatchType.GetMethod(
                BotsControllerStopPrefixMethodName,
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(BotsController) },
                null)
                ?? throw new MissingMethodException(
                    BotsControllerStopPatchTypeName,
                    BotsControllerStopPrefixMethodName);
            if (stopGettingInfoPrefixMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("PitFireTeam raid-stop cleanup has an unsupported signature");

            var prefix = typeof(PitFireTeamCompat).GetMethod(nameof(PitBossOnDeadPrefix),
                BindingFlags.NonPublic | BindingFlags.Static);
            Harmony.Patch(deathMethod, prefix: new HarmonyMethod(prefix));

            var progressPrefix = typeof(PitFireTeamCompat).GetMethod(nameof(SaveFollowersProgressPrefix),
                BindingFlags.NonPublic | BindingFlags.Static);
            Harmony.Patch(saveFollowersProgressMethod, prefix: new HarmonyMethod(progressPrefix));

            var stopFinalizer = typeof(PitFireTeamCompat).GetMethod(nameof(PitStopCleanupFinalizer),
                BindingFlags.NonPublic | BindingFlags.Static);
            Harmony.Patch(stopGettingInfoPrefixMethod, finalizer: new HarmonyMethod(stopFinalizer));
            _contractState = PitFireTeamContractState.Compatible;
            _contractProblem = string.Empty;
            Plugin.ModLogger.LogInfo(
                $"Arena found compatible PitFireTeam {SupportedVersion}; optional 5v5 Tactical integration is available.");
        }
        catch (Exception ex)
        {
            // Team mode needs both protections. A half-installed contract could
            // keep the squad alive while still persisting it (or vice versa), so
            // remove every patch from this adapter before rejecting the layout.
            try
            {
                Harmony.UnpatchSelf();
            }
            catch (Exception rollbackException)
            {
                Plugin.ModLogger.LogError(
                    $"Arena could not roll back partial PitFireTeam patches: {rollbackException}");
            }
            InvalidateContract(ex.Message);
        }
    }

    private static bool PitBossOnDeadPrefix()
    {
        if (!LiveTeamArena && !IsFinalizing && !ProtectedAbortPending)
            return true;

        if (!_deathSuppressionLogged)
        {
            _deathSuppressionLogged = true;
            Plugin.ModLogger.LogInfo(
                "Arena kept the PitFireTeam squad active after the local player was eliminated.");
        }

        return false;
    }

    private static void PitSquadOpenPrefix()
        => Interlocked.Increment(ref _squadOpenDepth);

    private static Exception PitSquadOpenFinalizer(Exception __exception)
    {
        if (Interlocked.Decrement(ref _squadOpenDepth) < 0)
            Interlocked.Exchange(ref _squadOpenDepth, 0);
        return __exception;
    }

    private static bool SaveFollowersProgressPrefix()
    {
        if (!TeamArenaActive && !IsFinalizing && !ProtectedAbortPending)
            return true;

        if (!_progressSuppressionLogged)
        {
            _progressSuppressionLogged = true;
            Plugin.ModLogger.LogInfo(
                "Arena suppressed PitFireTeam follower-progress submission for the protected team raid.");
        }

        return false;
    }

    /// <summary>
    /// PFT performs several unguarded Dispose calls inside its Harmony prefix on
    /// BotsController.StopGettingInfo. If one throws after EFT has already set
    /// GameStatus.Stopping, vanilla never schedules GameEnd and cannot retry.
    /// Keep ordinary PFT behavior unchanged; for the protected team raid only,
    /// contain that optional cleanup failure and let EFT's original stop path run.
    /// </summary>
    private static Exception PitStopCleanupFinalizer(Exception __exception, ref bool __result)
    {
        if (__exception == null
            || (!TeamArenaActive && !IsFinalizing && !ProtectedAbortPending))
            return __exception;

        __result = true;
        foreach (var step in _stopCleanupSteps)
        {
            try
            {
                step.Run();
            }
            catch (Exception cleanupException)
            {
                Plugin.ModLogger.LogError(
                    $"Arena best-effort PitFireTeam stop cleanup '{step.Name}' failed: " +
                    cleanupException.GetBaseException());
            }
        }

        if (!_stopCleanupFailureLogged)
        {
            _stopCleanupFailureLogged = true;
            Plugin.ModLogger.LogError(
                $"Arena contained a PitFireTeam raid-stop cleanup failure so EFT can finish stopping: {__exception}");
        }

        return null;
    }

    private static IReadOnlyList<StopCleanupStep> BuildStopCleanupContract(Assembly assembly)
    {
        List<StopCleanupStep> steps = new();

        AddStaticCall(steps, assembly, "pitTeam.Modules.InteractableObjects", "Dispose");
        AddSetStaticField(steps, assembly, "pitTeam.Modules.InteractableObjects", "Instance", null);
        AddStaticCall(steps, assembly, "pitTeam.Modules.NpcMessage", "Dispose");
        AddSetStaticField(steps, assembly, "pitTeam.Modules.NpcMessage", "Instance", null);
        AddStaticCall(steps, assembly, BossPlayersTypeName, "Dispose");
        AddSetStaticProperty(steps, assembly, BossPlayersTypeName, "Instance", null);

        foreach (var field in new[]
                 {
                     "spawnedPlayers",
                     "followerCreationTask",
                     "alliesCreationTask",
                     "bossCreationTask",
                 })
        {
            AddClearStaticField(steps, assembly, "pitTeam.Patches.BotsControllerPatch", field);
        }

        AddSetStaticField(steps, assembly, "pitTeam.Patches.BotsControllerPatch", "Controller", null);
        AddClearStaticField(
            steps,
            assembly,
            "pitTeam.Patches.BotOwnerManualUpdatePatch",
            "BotOwnerUpdate");
        AddStaticCall(steps, assembly, "pitTeam.Utils.PingTeamates", "Disable");
        AddSetStaticField(steps, assembly, "pitTeam.Utils.PingTeamates", "Instance", null);
        AddStaticCall(steps, assembly, "pitTeam.Utils.Enemy", "ClearEnemiesLocations");
        AddStaticCall(steps, assembly, "pitTeam.Utils.Utils", "FlagsClear");
        AddStaticCall(steps, assembly, "pitTeam.Utils.Utils", "ValuesClear");
        AddStaticCall(
            steps,
            assembly,
            "pitTeam.Modules.TeammateAutoJoinRuntime",
            "ClearAllSuppression");
        AddClearStaticField(steps, assembly, SelectedGroupTypeName, "GroupPlayers");
        AddClearStaticField(steps, assembly, SelectedGroupTypeName, "TransitPlayers");
        AddClearStaticField(steps, assembly, "pitTeam.Utils.SpawnHelper", "spawnMemberIds");
        AddClearStaticField(steps, assembly, "pitTeam.Utils.SpawnHelper", "spawnMemberIdsScav");
        AddClearStaticField(steps, assembly, "pitTeam.Utils.SpawnHelper", "spawnMemberIdsBoss");
        AddStaticCall(steps, assembly, "pitTeam.Modules.FollowerTransitStateCache", "Clear");
        AddSetStaticField(
            steps,
            assembly,
            "pitTeam.Patches.BotsEventsControllerSpawnPatch",
            "squadSpawned",
            false);
        AddSetStaticField(
            steps,
            assembly,
            "pitTeam.Patches.LocalGameCtorPatch",
            "Instance",
            null);
        AddStaticCall(
            steps,
            assembly,
            "pitTeam.Modules.BattleRecorder",
            "EndRaid",
            new[] { typeof(string) },
            new object[] { "arenaProtectedStopRecovery" });

        return steps;
    }

    private static void AddStaticCall(
        ICollection<StopCleanupStep> steps,
        Assembly assembly,
        string typeName,
        string methodName,
        Type[] parameterTypes = null,
        object[] arguments = null)
    {
        parameterTypes ??= Type.EmptyTypes;
        arguments ??= Array.Empty<object>();
        var type = RequireType(assembly, typeName);
        var method = type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null,
            parameterTypes,
            null)
            ?? throw new MissingMethodException(typeName, methodName);
        if (method.ReturnType != typeof(void))
            throw new InvalidOperationException($"PitFireTeam cleanup {typeName}.{methodName} must return void");

        steps.Add(new StopCleanupStep
        {
            Name = typeName + "." + methodName,
            Run = () => method.Invoke(null, arguments),
        });
    }

    private static void AddClearStaticField(
        ICollection<StopCleanupStep> steps,
        Assembly assembly,
        string typeName,
        string fieldName)
    {
        var field = RequireStaticField(assembly, typeName, fieldName);
        var clear = field.FieldType.GetMethod(
            "Clear",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null)
            ?? throw new MissingMethodException(field.FieldType.FullName, "Clear");
        if (clear.ReturnType != typeof(void))
            throw new InvalidOperationException($"PitFireTeam cleanup {typeName}.{fieldName}.Clear must return void");

        steps.Add(new StopCleanupStep
        {
            Name = typeName + "." + fieldName + ".Clear",
            Run = () =>
            {
                var value = field.GetValue(null);
                if (value != null)
                    clear.Invoke(value, null);
            },
        });
    }

    private static void AddSetStaticField(
        ICollection<StopCleanupStep> steps,
        Assembly assembly,
        string typeName,
        string fieldName,
        object value)
    {
        var field = RequireStaticField(assembly, typeName, fieldName);
        if (field.IsInitOnly || (value == null && field.FieldType.IsValueType))
            throw new InvalidOperationException($"PitFireTeam cleanup field {typeName}.{fieldName} is not writable");
        if (value != null && !field.FieldType.IsInstanceOfType(value))
            throw new InvalidOperationException($"PitFireTeam cleanup field {typeName}.{fieldName} has an unsupported type");

        steps.Add(new StopCleanupStep
        {
            Name = typeName + "." + fieldName + " reset",
            Run = () => field.SetValue(null, value),
        });
    }

    private static void AddSetStaticProperty(
        ICollection<StopCleanupStep> steps,
        Assembly assembly,
        string typeName,
        string propertyName,
        object value)
    {
        var type = RequireType(assembly, typeName);
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMemberException(typeName, propertyName);
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new MissingMethodException(typeName, "set_" + propertyName);
        if (value == null && property.PropertyType.IsValueType)
            throw new InvalidOperationException($"PitFireTeam cleanup property {typeName}.{propertyName} is not nullable");

        steps.Add(new StopCleanupStep
        {
            Name = typeName + "." + propertyName + " reset",
            Run = () => setter.Invoke(null, new[] { value }),
        });
    }

    private static FieldInfo RequireStaticField(Assembly assembly, string typeName, string fieldName)
    {
        var field = RequireType(assembly, typeName).GetField(
            fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeName, fieldName);
        if (!field.IsStatic)
            throw new InvalidOperationException($"PitFireTeam cleanup field {typeName}.{fieldName} is not static");
        return field;
    }

    private static void InvalidateContract(string problem)
    {
        _contractState = PitFireTeamContractState.Incompatible;
        _contractProblem = problem ?? "unknown compatibility failure";
        Plugin.ModLogger.LogWarning(
            $"Arena will not start a PitFireTeam team raid: {_contractProblem}. Solo Arena remains available.");
    }

    private static Type RequireType(Assembly assembly, string typeName)
        => assembly.GetType(typeName, throwOnError: false, ignoreCase: false)
           ?? throw new TypeLoadException($"required type '{typeName}' is missing");

    private static Type RequireListElementType(Type collectionType, string label)
    {
        if (!collectionType.IsGenericType || collectionType.GetGenericTypeDefinition() != typeof(List<>))
            throw new InvalidOperationException($"PitFireTeam {label} is not the expected List<T>");
        return collectionType.GetGenericArguments()[0];
    }

    private static IReadOnlyList<string> ReadNativeGroupAccountIds(MatchmakerPlayersController controller)
    {
        if (controller?.GroupPlayers == null)
            return null;

        List<string> result = new(controller.GroupPlayers.Count);
        foreach (var player in controller.GroupPlayers)
            result.Add(player?.AccountId);
        return result;
    }

    private static bool TryReadSelectedMembers(
        out IReadOnlyList<string> accountIds,
        out IReadOnlyList<string> profileIds,
        out string problem)
    {
        accountIds = Array.Empty<string>();
        profileIds = Array.Empty<string>();
        problem = string.Empty;
        try
        {
            if (_selectedGroupField?.GetValue(null) is not IEnumerable selected)
            {
                problem = "GroupPlayers is null or not enumerable";
                return false;
            }

            List<string> accounts = new();
            List<string> profiles = new();
            foreach (var player in selected)
            {
                accounts.Add(player == null ? null : _selectedAccountIdField.GetValue(player) as string);
                // PFT 0.10.0 stores AID here for ordinary synthetic teammates;
                // only its fixed special companions carry a true profile marker.
                // The stored teammate profile id is resolved below via PFT's
                // authenticated server route and never trusted from this field.
                profiles.Add(player == null ? null : _selectedRaidIdField.GetValue(player) as string);
            }
            accountIds = accounts;
            profileIds = profiles;
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.GetBaseException().Message;
            return false;
        }
    }

    /// <summary>
    /// PFT's synthetic matchmaker RaidPlayer intentionally sets Id=AID, while
    /// the spawned BotBase retains its separate Mongo profile id. Resolve every
    /// ordinary selection through PFT's own authenticated teammate profile route
    /// before Arena can arm. A foreign native group member therefore fails here,
    /// and the live body can later be matched against the real stored profile.
    /// Fixed Goon ids stay as markers so the policy can reject them explicitly.
    /// </summary>
    private static bool TryResolveSelectedProfileIds(
        IReadOnlyList<string> accountIds,
        IReadOnlyList<string> raidIds,
        out IReadOnlyList<string> profileIds,
        out string problem)
    {
        profileIds = Array.Empty<string>();
        problem = string.Empty;
        if (accountIds == null || raidIds == null || accountIds.Count != raidIds.Count)
        {
            problem = "the selected account and RaidPlayer identity lists differ";
            return false;
        }

        List<string> resolved = new(accountIds.Count);
        for (int index = 0; index < accountIds.Count; index++)
        {
            string accountId = accountIds[index]?.Trim();
            string raidId = raidIds[index]?.Trim();
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(raidId))
            {
                problem = "a selected member has no account or RaidPlayer identity";
                return false;
            }

            if (PitFireTeamSelectionPolicy.IsUnsupportedSpecialProfileId(raidId))
            {
                resolved.Add(raidId);
                continue;
            }

            try
            {
                string response = RequestHandler.PostJson(
                    TeammateProfileRoute,
                    JsonConvert.SerializeObject(new { accountId }));
                JToken root = JToken.Parse(response);
                int error = root["err"]?.Value<int>() ?? -1;
                JToken data = root["data"];
                string returnedAid = data?["aid"]?.Value<string>()?.Trim()
                    ?? data?["aid"]?.ToString()?.Trim();
                string profileId = data?["id"]?.Value<string>()?.Trim();
                if (error != 0
                    || !PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                        accountId,
                        returnedAid,
                        profileId))
                {
                    problem = $"account {accountId} was not returned as one stored teammate profile";
                    return false;
                }

                resolved.Add(profileId);
            }
            catch (Exception ex)
            {
                problem = $"profile lookup for account {accountId} failed: {ex.GetBaseException().Message}";
                return false;
            }
        }

        profileIds = resolved;
        return true;
    }

    private static bool TryGetFrozenProfileId(string accountId, out string profileId)
    {
        profileId = null;
        if (string.IsNullOrWhiteSpace(accountId))
            return false;

        lock (StateLock)
            return FrozenProfilesByAccountId.TryGetValue(accountId.Trim(), out profileId);
    }

    private static bool FrozenIdentityMatches(string accountId, string profileId)
        => TryGetFrozenProfileId(accountId, out var expectedProfileId)
           && !string.IsNullOrWhiteSpace(profileId)
           && string.Equals(expectedProfileId, profileId.Trim(), StringComparison.Ordinal);

    private static bool HasCompleteFrozenRosterUnsafe()
        => FrozenAccountIds.Count == PitFireTeamSelectionPolicy.RequiredCompanionCount
           && FrozenProfilesByAccountId.Count == PitFireTeamSelectionPolicy.RequiredCompanionCount
           && KnownProfileIds.Count == PitFireTeamSelectionPolicy.RequiredCompanionCount;
}
