using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
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
using System.Runtime.CompilerServices;
using System.Threading;

namespace ArenaMode.Compat;

/// <summary>
/// Optional, version-admitted adapter for the PitFireTeam policy in
/// PitFireTeamVersions. No PitFireTeam type appears in a signature or build
/// reference: the external assembly is inspected only after BepInEx reports
/// the installed plugin as loaded; reflected contracts remain mandatory.
/// </summary>
internal static partial class PitFireTeamCompat
{
    private static bool _accountWideReportsRequired;
    public const string PluginGuid = "xyz.pit.fireteam";

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
    private static readonly object ProfileCacheLock = new();
    // The menu-authenticated identities are immutable for the whole raid. A
    // round may replace PFT's runtime profile/body identities, but never this
    // account-to-stored-profile proof.
    private static readonly HashSet<string> CanonicalAccountIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CanonicalProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CurrentProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BotOwner> CurrentBodiesByAccountId =
        new(StringComparer.Ordinal);
    // Corpse cleanup must recognize bodies from every completed round. This is
    // intentionally a history, so it must not participate in the frozen companion
    // TeamArenaActive invariant.
    private static readonly HashSet<string> KnownProfileIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Harmony Harmony = new(Plugin.PluginGuid + ".pitfireteam");
    private static readonly Harmony UiHarmony = new(Plugin.PluginGuid + ".pitfireteam-ui");

    private static int _frozenCompanionCount;
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
    private static Player _currentLocalPlayer;
    private static string _currentLocalProfileId;
    private static object _currentLeaderWrapper;
    private static Player _pendingLocalPlayer;
    private static string _pendingLocalProfileId;
    private static bool _roundTransitionActive;
    private static DateTime _finalizingUntilUtc;
    private static int _squadOpenDepth;
    private static IReadOnlyList<StopCleanupStep> _stopCleanupSteps = Array.Empty<StopCleanupStep>();
    private static StoredProfileResolution _storedProfileCache;

    private sealed class StoredProfileResolution
    {
        public string[] AccountIds { get; set; }
        public string[] RaidIds { get; set; }
        public string[] ProfileIds { get; set; }
    }

    private sealed class StopCleanupStep
    {
        public string Name { get; set; }
        public Action Run { get; set; }
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }

    public static bool IsInstalled
        => Chainloader.PluginInfos.ContainsKey(PluginGuid);

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

    /// <summary>Display-only selection count. Admission still authenticates the full mapping.</summary>
    public static int SelectedCompanionCount
    {
        get
        {
            EnsureInitialized();
            return _contractState == PitFireTeamContractState.Compatible
                && TryReadSelectedMembers(out var accounts, out _, out _)
                && PitFireTeamSelectionPolicy.IsSupportedCompanionCount(accounts.Count)
                ? accounts.Count : 0;
        }
    }

    /// <summary>The immutable expected count, never inferred from arrived bodies.</summary>
    public static int FrozenCompanionCount
    {
        get { lock (StateLock) return HasCompleteCanonicalRosterUnsafe() ? _frozenCompanionCount : 0; }
    }

    /// <summary>The authenticated roster is frozen before the server is armed.</summary>
    public static bool HasFrozenTeamRoster
    {
        get
        {
            lock (StateLock)
                return HasCompleteCanonicalRosterUnsafe();
        }
    }

    /// <summary>
    /// True after final raid-start authentication froze the selected companions
    /// and until protected finalization begins. It intentionally becomes true
    /// before server arming so the server can require external-profile protection
    /// for this request. Gameplay helpers additionally require ArenaSession.IsActive.
    /// </summary>
    public static bool TeamArenaActive
    {
        get
        {
            lock (StateLock)
                return HasCompleteCanonicalRosterUnsafe()
                    && _finalizingUntilUtc == DateTime.MinValue;
        }
    }

    private static bool LiveTeamArena => TeamArenaActive && ArenaSession.IsActive;

    private static bool ProtectedAbortPending
        => Modes.ArenaMatchService.TeamStartAbortPending;

    /// <summary>
    /// Exact state used by the two Harmony persistence/death guards. The round
    /// lifecycle receives this proof instead of inferring protection merely
    /// from a configured adapter.
    /// </summary>
    public static bool PersistenceSuppressionActive
        => TeamArenaActive || IsFinalizing || ProtectedAbortPending;

    public static bool RoundTransitionActive
    {
        get
        {
            lock (StateLock)
                return _roundTransitionActive;
        }
    }

    public static bool IsFinalizing
    {
        get
        {
            lock (StateLock)
                return HasCompleteCanonicalRosterUnsafe()
                    && DateTime.UtcNow < _finalizingUntilUtc;
        }
    }

    public static IReadOnlyList<string> FrozenCompanionAccountIds
    {
        get
        {
            lock (StateLock)
                return CanonicalAccountIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>
    /// Defensive copy of the authenticated account-to-stored-profile mapping.
    /// No runtime round profile is ever written to this map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CanonicalCompanionProfilesByAccountId
    {
        get
        {
            lock (StateLock)
                return new Dictionary<string, string>(
                    CanonicalProfilesByAccountId,
                    StringComparer.Ordinal);
        }
    }

    public static IReadOnlyDictionary<string, string> CurrentCompanionProfilesByAccountId
    {
        get
        {
            lock (StateLock)
                return new Dictionary<string, string>(
                    CurrentProfilesByAccountId,
                    StringComparer.Ordinal);
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
        MatchmakerPlayersController controller, RaidSettings raidSettings,
        bool allowPendingModeSelection = false)
    {
        EnsureInitialized();

        string persistenceProblem = PitFireTeamReportGuard.AdmissionProblem(_accountWideReportsRequired, _contractProblem);
        if (!string.IsNullOrEmpty(persistenceProblem))
            return PitFireTeamSelectionDecision.Reject(persistenceProblem);

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
        }, allowPendingModeSelection);
    }

    /// <summary>
    /// Freeze only the already live-validated decision. Display ticks may reuse
    /// a stored-profile cache; raid start always re-authenticates the live
    /// roster against that decision before arming.
    /// </summary>
    public static bool TryFreezeTeamRoster(PitFireTeamSelectionDecision decision, out string reason)
    {
        reason = string.Empty;
        string persistenceProblem = PitFireTeamReportGuard.AdmissionProblem(_accountWideReportsRequired, _contractProblem);
        if (!string.IsNullOrEmpty(persistenceProblem))
        {
            reason = persistenceProblem;
            return false;
        }
        if (decision == null || !decision.Allowed)
        {
            reason = decision?.Reason ?? "Arena could not validate the current PitFireTeam roster.";
            return false;
        }

        InvalidateStoredProfileCache();
        if (decision.IsTeam)
        {
            if (!TryReadSelectedMembers(
                    out IReadOnlyList<string> liveAccountIds,
                    out IReadOnlyList<string> liveRaidIds,
                    out var readProblem))
            {
                reason = "could not re-authenticate the live PitFireTeam roster: " + readProblem;
                return false;
            }

            if (!TryResolveSelectedProfileIds(
                    liveAccountIds,
                    liveRaidIds,
                    out IReadOnlyList<string> liveProfileIds,
                    out var identityProblem))
            {
                reason = "could not re-authenticate every selected stored PitFireTeam teammate: "
                         + identityProblem;
                return false;
            }

            if (!TryMatchLiveRosterToDecision(
                    decision,
                    liveAccountIds,
                    liveProfileIds,
                    out reason))
            {
                return false;
            }
        }

        // Protected raid end deliberately keeps the canonical roster while
        // persistence finalizes. Retire the previous raid's pending placements
        // before the next authenticated selection replaces that state; creator
        // tombstones still veto delayed raw creation into the ended game.
        PitFireTeamAreaSpawns.Reset();
        PitFireTeamRoundLifecycle.Reset();

        lock (StateLock)
        {
            _frozenCompanionCount = 0;
            CanonicalAccountIds.Clear();
            CanonicalProfilesByAccountId.Clear();
            CurrentProfilesByAccountId.Clear();
            CurrentBodiesByAccountId.Clear();
            KnownProfileIds.Clear();
            _currentLocalPlayer = null;
            _currentLocalProfileId = null;
            _currentLeaderWrapper = null;
            _pendingLocalPlayer = null;
            _pendingLocalProfileId = null;
            _roundTransitionActive = false;
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
                || !PitFireTeamSelectionPolicy.IsSupportedCompanionCount(decision.CompanionAccountIds.Count)
                || decision.CompanionProfilesByAccountId.Count != decision.CompanionAccountIds.Count)
            {
                reason = "The PitFireTeam runtime or companion roster changed before Arena could arm.";
                return false;
            }

            _frozenCompanionCount = decision.CompanionAccountIds.Count;
            foreach (var accountId in decision.CompanionAccountIds)
            {
                var normalizedAccount = accountId?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedAccount)
                    || !decision.CompanionProfilesByAccountId.TryGetValue(normalizedAccount, out var profileId)
                    || string.IsNullOrWhiteSpace(profileId)
                    || !CanonicalAccountIds.Add(normalizedAccount)
                    || !CanonicalProfilesByAccountId.TryAdd(normalizedAccount, profileId.Trim())
                    || !CurrentProfilesByAccountId.TryAdd(normalizedAccount, profileId.Trim())
                    || !KnownProfileIds.Add(profileId.Trim()))
                {
                    _frozenCompanionCount = 0;
                    CanonicalAccountIds.Clear();
                    CanonicalProfilesByAccountId.Clear();
                    CurrentProfilesByAccountId.Clear();
                    KnownProfileIds.Clear();
                    reason = "Arena refused an invalid, duplicate or mismatched PitFireTeam companion identity.";
                    return false;
                }
            }
        }

        Plugin.ModLogger.LogDebug(
            $"Arena authenticated PitFireTeam roster: {string.Join(", ", FrozenCompanionAccountIds)}.");
        return true;
    }

    /// <summary>
    /// Bind the exact local PMC body for the current round. A replacement is
    /// accepted only as a pending leader during an explicit round transition;
    /// TryAdoptRoundRoster promotes it only after the lifecycle proves the new
    /// PFT boss and all selected followers.
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
            if (!HasCompleteCanonicalRosterUnsafe())
            {
                reason = "Arena lost the authenticated PitFireTeam roster before binding its local leader.";
                return false;
            }

            if (!ReferenceEquals(_currentLocalPlayer, null))
            {
                if (ReferenceEquals(_currentLocalPlayer, player)
                    && string.Equals(_currentLocalProfileId, profileId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (_roundTransitionActive)
                {
                    if (!ReferenceEquals(_pendingLocalPlayer, null)
                        && (!ReferenceEquals(_pendingLocalPlayer, player)
                            || !string.Equals(
                                _pendingLocalProfileId,
                                profileId,
                                StringComparison.Ordinal)))
                    {
                        reason = "Arena observed more than one pending PitFireTeam leader body during the round transition.";
                        return false;
                    }

                    _pendingLocalPlayer = player;
                    _pendingLocalProfileId = profileId;
                    return true;
                }

                if (string.IsNullOrEmpty(_runtimeRosterProblem))
                    _runtimeRosterProblem = "Arena attempted to replace the local PitFireTeam leader body outside a round transition";
                reason = _runtimeRosterProblem;
                return false;
            }

            _currentLocalPlayer = player;
            _currentLocalProfileId = profileId;
        }

        return true;
    }

    /// <summary>
    /// Opens the only window in which the current PFT leader/follower bodies may
    /// be replaced. The returned frozen-body snapshot includes corpses and is the
    /// exact roster the lifecycle adapter must retire. Canonical identities and
    /// persistence guards remain armed throughout the transition.
    /// </summary>
    public static bool BeginRoundTransition(
        out Player currentLeader,
        out IReadOnlyList<BotOwner> currentFollowers,
        out string reason)
    {
        currentLeader = null;
        currentFollowers = Array.Empty<BotOwner>();
        reason = string.Empty;

        PitFireTeamRoundLifecycleStage lifecycleStage = PitFireTeamRoundLifecycle.Stage;
        bool lifecycleCapable = PitFireTeamRoundLifecycle.Capability.CanStartAdditionalRound;
        bool persistenceSuppressionActive = PersistenceSuppressionActive;
        lock (StateLock)
        {
            if (_roundTransitionActive)
            {
                currentLeader = _currentLocalPlayer;
                currentFollowers = CurrentBodiesByAccountId
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value)
                    .ToArray();
                return true;
            }

            if (!ArenaSession.IsActive
                || _contractState != PitFireTeamContractState.Compatible
                || !HasCompleteCanonicalRosterUnsafe()
                || _finalizingUntilUtc != DateTime.MinValue)
            {
                reason = "Arena cannot begin a PitFireTeam round transition outside the active protected raid.";
                return false;
            }

            if (!persistenceSuppressionActive)
            {
                reason = "Arena cannot begin a PitFireTeam round transition without proven persistence suppression.";
                return false;
            }

            if (!lifecycleCapable
                || (lifecycleStage != PitFireTeamRoundLifecycleStage.ContractReady
                    && lifecycleStage != PitFireTeamRoundLifecycleStage.FollowersReady))
            {
                reason = $"PitFireTeam round lifecycle is not ready to replace the current roster (stage {lifecycleStage}).";
                return false;
            }

            if (!string.IsNullOrEmpty(_runtimeRosterProblem))
            {
                reason = _runtimeRosterProblem;
                return false;
            }

            if (ReferenceEquals(_currentLocalPlayer, null)
                || _currentLocalPlayer == null
                || string.IsNullOrWhiteSpace(_currentLocalProfileId)
                || _currentLeaderWrapper == null
                || !string.Equals(
                    _currentLocalPlayer.ProfileId?.Trim(),
                    _currentLocalProfileId,
                    StringComparison.Ordinal)
                || CurrentProfilesByAccountId.Count != _frozenCompanionCount
                || CurrentBodiesByAccountId.Count != _frozenCompanionCount
                || !CanonicalAccountIds.SetEquals(CurrentProfilesByAccountId.Keys)
                || !CanonicalAccountIds.SetEquals(CurrentBodiesByAccountId.Keys))
            {
                reason = "Arena cannot prove the complete current PitFireTeam leader/follower roster before teardown.";
                return false;
            }

            HashSet<BotOwner> distinctBodies = new(ReferenceEqualityComparer<BotOwner>.Instance);
            foreach (var pair in CurrentBodiesByAccountId)
            {
                BotOwner body = pair.Value;
                if (ReferenceEquals(body, null)
                    || body == null
                    || !CurrentProfilesByAccountId.TryGetValue(pair.Key, out string expectedProfileId)
                    || !string.Equals(body.Profile?.AccountId?.Trim(), pair.Key, StringComparison.Ordinal)
                    || !string.Equals(body.ProfileId?.Trim(), expectedProfileId, StringComparison.Ordinal)
                    || !distinctBodies.Add(body))
                {
                    reason = "Arena cannot prove the complete set of distinct current PitFireTeam bodies before teardown.";
                    return false;
                }
            }

            _roundTransitionActive = true;
            _pendingLocalPlayer = null;
            _pendingLocalProfileId = null;
            currentLeader = _currentLocalPlayer;
            currentFollowers = CurrentBodiesByAccountId
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToArray();
            return true;
        }
    }

    /// <summary>
    /// Atomically adopts a new current round only after the reflection lifecycle
    /// reached FollowersReady. It independently rechecks the frozen account,
    /// runtime-profile, body, world-registry and exact leader-wrapper bindings;
    /// any mismatch leaves the transition closed to combat.
    /// </summary>
    public static bool TryAdoptRoundRoster(
        Player currentLeader,
        out IReadOnlyList<BotOwner> followers,
        out string reason)
    {
        followers = Array.Empty<BotOwner>();
        reason = string.Empty;

        try
        {
            PitFireTeamRoundLifecycleSnapshot lifecycle = PitFireTeamRoundLifecycle.Status;
            IReadOnlyList<BotOwner> lifecycleFollowers = PitFireTeamRoundLifecycle.GetCurrentFollowers();
            bool persistenceSuppressionActive = PersistenceSuppressionActive;

            lock (StateLock)
            {
                if (!_roundTransitionActive)
                {
                    reason = "Arena refused to adopt a PitFireTeam roster outside an explicit round transition.";
                    return false;
                }

                if (!persistenceSuppressionActive)
                    return LatchRoundTransitionProblemUnsafe(
                        "PitFireTeam persistence suppression was lost before roster adoption",
                        out followers,
                        out reason);

                if (lifecycle.Stage != PitFireTeamRoundLifecycleStage.FollowersReady)
                {
                    reason = $"PitFireTeam followers are not ready for adoption (stage {lifecycle.Stage}).";
                    return false;
                }

                if (lifecycle.Epoch <= 0
                    || lifecycle.Capability?.CanStartAdditionalRound != true)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the ready PitFireTeam lifecycle has no valid round epoch or capability",
                        out followers,
                        out reason);
                }

                if (ReferenceEquals(currentLeader, null)
                    || currentLeader == null
                    || currentLeader.HealthController?.IsAlive != true
                    || string.IsNullOrWhiteSpace(currentLeader.ProfileId))
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the new PitFireTeam leader body is unavailable or dead",
                        out followers,
                        out reason);
                }

                string currentLeaderProfileId = currentLeader.ProfileId.Trim();
                bool reusesCurrentLeader = ReferenceEquals(_pendingLocalPlayer, null)
                    && string.IsNullOrEmpty(_pendingLocalProfileId)
                    && ReferenceEquals(_currentLocalPlayer, currentLeader)
                    && string.Equals(
                        _currentLocalProfileId,
                        currentLeaderProfileId,
                        StringComparison.Ordinal);
                bool adoptsPendingLeader = !ReferenceEquals(_pendingLocalPlayer, null)
                    && ReferenceEquals(_pendingLocalPlayer, currentLeader)
                    && string.Equals(
                        _pendingLocalProfileId,
                        currentLeaderProfileId,
                        StringComparison.Ordinal);
                if (!reusesCurrentLeader && !adoptsPendingLeader)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the lifecycle leader is neither Arena's exact surviving leader nor its explicitly bound replacement body",
                        out followers,
                        out reason);
                }

                IReadOnlyDictionary<string, string> runtimeProfiles =
                    lifecycle.RuntimeProfilesByAccountId;
                if (runtimeProfiles == null
                    || runtimeProfiles.Count != _frozenCompanionCount
                    || !CanonicalAccountIds.SetEquals(runtimeProfiles.Keys)
                    || lifecycleFollowers == null
                    || lifecycleFollowers.Count != _frozenCompanionCount)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the lifecycle did not prove the exact frozen runtime identities and follower bodies",
                        out followers,
                        out reason);
                }

                HashSet<string> runtimeProfileIds = new(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in runtimeProfiles)
                {
                    string accountId = pair.Key?.Trim();
                    string profileId = pair.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(accountId)
                        || string.IsNullOrWhiteSpace(profileId)
                        || !CanonicalAccountIds.Contains(accountId)
                        || !runtimeProfileIds.Add(profileId)
                        || KnownProfileIds.Contains(profileId))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            "the lifecycle returned a missing, duplicate or previously used runtime profile identity",
                            out followers,
                            out reason);
                    }
                }

                var world = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance
                    : null;
                if (ReferenceEquals(world, null) || world == null)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "GameWorld is unavailable while adopting the new PitFireTeam roster",
                        out followers,
                        out reason);
                }

                if (!ReferenceEquals(world.MainPlayer, currentLeader))
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the lifecycle leader is not GameWorld's exact current MainPlayer body",
                        out followers,
                        out reason);
                }

                if (!world.RegisteredPlayers.Any(player => ReferenceEquals(player, currentLeader))
                    || !ReferenceEquals(
                        world.GetEverExistedPlayerByID(currentLeaderProfileId),
                        currentLeader))
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the lifecycle leader is not GameWorld's exact registered and persistent player body",
                        out followers,
                        out reason);
                }

                Dictionary<string, BotOwner> bodiesByAccount = new(StringComparer.Ordinal);
                HashSet<BotOwner> distinctBodies = new(ReferenceEqualityComparer<BotOwner>.Instance);
                Dictionary<BotOwner, object> wrappersByBody =
                    new(ReferenceEqualityComparer<BotOwner>.Instance);
                if (_getFollowersMethod.Invoke(null, null) is not IEnumerable reflectedFollowers)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "PitFireTeam's follower wrapper collection is unavailable during roster adoption",
                        out followers,
                        out reason);
                }

                foreach (object wrapper in reflectedFollowers)
                {
                    if (wrapper != null
                        && _getFollowerBotMethod.Invoke(wrapper, null) is BotOwner wrapperBody
                        && lifecycleFollowers.Any(candidate => ReferenceEquals(candidate, wrapperBody))
                        && !wrappersByBody.TryAdd(wrapperBody, wrapper))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            "PitFireTeam returned duplicate wrappers for a new follower body",
                            out followers,
                            out reason);
                    }
                }

                if (wrappersByBody.Count != _frozenCompanionCount)
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "PitFireTeam did not retain exactly one wrapper for every ready follower body",
                        out followers,
                        out reason);
                }

                object commonLeaderWrapper = null;
                foreach (BotOwner bot in lifecycleFollowers)
                {
                    string accountId = bot?.Profile?.AccountId?.Trim();
                    string profileId = bot?.ProfileId?.Trim();
                    if (ReferenceEquals(bot, null)
                        || bot == null
                        || bot.BotState != EBotState.Active
                        || bot.HealthController?.IsAlive != true
                        || bot.IsDead
                        || string.IsNullOrWhiteSpace(accountId)
                        || !runtimeProfiles.TryGetValue(accountId, out string expectedProfileId)
                        || !string.Equals(profileId, expectedProfileId, StringComparison.Ordinal)
                        || !distinctBodies.Add(bot)
                        || !bodiesByAccount.TryAdd(accountId, bot))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            "the lifecycle returned an invalid or duplicate current-round follower body",
                            out followers,
                            out reason);
                    }

                    Player body = bot.GetPlayer;
                    if (ReferenceEquals(body, null)
                        || body == null
                        || !world.RegisteredPlayers.Any(player => ReferenceEquals(player, body))
                        || !ReferenceEquals(world.GetEverExistedPlayerByID(profileId), body))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            $"runtime follower {profileId} is not the exact registered GameWorld body",
                            out followers,
                            out reason);
                    }

                    if (!wrappersByBody.TryGetValue(bot, out object followerWrapper))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            $"runtime follower {profileId} has no exact PFT wrapper",
                            out followers,
                            out reason);
                    }

                    object leaderWrapper = _getFollowerBossMethod.Invoke(followerWrapper, null);
                    Player leaderPlayer = leaderWrapper == null
                        ? null
                        : _bossRealPlayerField.GetValue(leaderWrapper) as Player;
                    if (leaderWrapper == null
                        || ReferenceEquals(leaderPlayer, null)
                        || !ReferenceEquals(leaderPlayer, currentLeader)
                        || (commonLeaderWrapper != null
                            && !ReferenceEquals(commonLeaderWrapper, leaderWrapper)))
                    {
                        return LatchRoundTransitionProblemUnsafe(
                            "the new followers do not share the exact current local PFT leader wrapper",
                            out followers,
                            out reason);
                    }

                    commonLeaderWrapper ??= leaderWrapper;
                }

                if (bodiesByAccount.Count != _frozenCompanionCount
                    || !CanonicalAccountIds.SetEquals(bodiesByAccount.Keys))
                {
                    return LatchRoundTransitionProblemUnsafe(
                        "the new follower bodies do not cover the authenticated frozen-account roster",
                        out followers,
                        out reason);
                }

                CurrentProfilesByAccountId.Clear();
                CurrentBodiesByAccountId.Clear();
                foreach (var pair in runtimeProfiles)
                {
                    CurrentProfilesByAccountId.Add(pair.Key, pair.Value);
                    CurrentBodiesByAccountId.Add(pair.Key, bodiesByAccount[pair.Key]);
                    KnownProfileIds.Add(pair.Value);
                }

                _currentLocalPlayer = currentLeader;
                _currentLocalProfileId = currentLeader.ProfileId.Trim();
                _currentLeaderWrapper = commonLeaderWrapper;
                _pendingLocalPlayer = null;
                _pendingLocalProfileId = null;
                _roundTransitionActive = false;
                _runtimeRosterProblem = string.Empty;
                followers = CurrentBodiesByAccountId
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value)
                    .ToArray();
                Plugin.ModLogger.LogDebug(
                    $"Arena adopted PitFireTeam round epoch {lifecycle.Epoch}: the fresh runtime followers are active.");
                return true;
            }
        }
        catch (Exception ex)
        {
            lock (StateLock)
            {
                return LatchRoundTransitionProblemUnsafe(
                    "PitFireTeam round-roster adoption failed: "
                    + ex.GetBaseException().Message,
                    out followers,
                    out reason);
            }
        }
    }

    private static bool LatchRoundTransitionProblemUnsafe(
        string value,
        out IReadOnlyList<BotOwner> followers,
        out string reason)
    {
        if (string.IsNullOrEmpty(_runtimeRosterProblem))
        {
            _runtimeRosterProblem = value;
            Plugin.ModLogger.LogError(
                $"Arena rejected the replacement PitFireTeam round roster: {_runtimeRosterProblem}.");
        }

        followers = Array.Empty<BotOwner>();
        reason = _runtimeRosterProblem;
        return false;
    }

    /// <summary>
    /// Discover and cache follower bodies while proving that PFT has exactly one
    /// active body for each current-round account/profile pair and no active
    /// unselected follower. A replacement remains invalid inside a round; only
    /// BeginRoundTransition plus TryAdoptRoundRoster can replace the whole set.
    /// The first live-round physical-roster violation is latched for the raid.
    /// </summary>
    public static bool TryGetLiveFollowers(
        out IReadOnlyList<BotOwner> followers,
        out string problem)
    {
        followers = Array.Empty<BotOwner>();
        bool transitioning;
        lock (StateLock)
        {
            problem = _runtimeRosterProblem;
            transitioning = _roundTransitionActive;
        }

        if (!string.IsNullOrEmpty(problem))
            return false;

        // HUD/status callers also reach this method outside Tick. Once the
        // exact raid has committed native Stop, missing wrappers are disposal,
        // not a live-roster violation. Keep any earlier latched fault above;
        // do not clear identities, persistence guards or pending creations.
        if (ArenaRaidExit.NativeStopOwnsCurrentRaid)
            return true;

        if (!LiveTeamArena || _contractState != PitFireTeamContractState.Compatible)
            return true;

        if (ShouldDeferInitialRosterDiscovery())
            return true;

        // The lifecycle owns the old/new wrapper collections while rebinding.
        // Normal within-round discovery must not interpret intentional retirement
        // or new runtime IDs as a replacement-body violation.
        if (transitioning)
            return true;

        try
        {
            bool rosterFullyBound;
            Dictionary<string, BotOwner> boundBodies;
            Player currentLocalPlayer;
            string currentLocalProfileId;
            lock (StateLock)
            {
                rosterFullyBound = CurrentBodiesByAccountId.Count
                    == _frozenCompanionCount;
                boundBodies = new Dictionary<string, BotOwner>(
                    CurrentBodiesByAccountId,
                    StringComparer.Ordinal);
                currentLocalPlayer = _currentLocalPlayer;
                currentLocalProfileId = _currentLocalProfileId;
            }

            if (ReferenceEquals(currentLocalPlayer, null)
                || currentLocalPlayer == null
                || string.IsNullOrWhiteSpace(currentLocalProfileId)
                || !string.Equals(
                    currentLocalPlayer.ProfileId?.Trim(),
                    currentLocalProfileId,
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
            List<BotOwner> live = new(_frozenCompanionCount);
            foreach (var follower in reflectedFollowers)
            {
                if (follower == null || _getFollowerBotMethod.Invoke(follower, null) is not BotOwner bot)
                    continue;

                var accountId = bot.Profile?.AccountId?.Trim();
                bool selectedCurrentIdentity =
                    TryGetCurrentProfileId(accountId, out var expectedProfileId);
                bool liveBody = bot.BotState == EBotState.Active
                    && bot.HealthController?.IsAlive == true
                    && !bot.IsDead;

                if (!selectedCurrentIdentity)
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
                    replacementBody = CurrentBodiesByAccountId.TryGetValue(accountId, out var originalBody)
                        && !ReferenceEquals(originalBody, bot);
                    alreadyBoundToThisBody = !replacementBody
                        && originalBody != null
                        && ReferenceEquals(originalBody, bot);
                }

                if (replacementBody)
                {
                    return LatchRuntimeRosterProblem(
                        $"PitFireTeam replaced the current-round body for account {accountId}",
                        out followers,
                        out problem);
                }

                // PFT's global follower list can contain wrappers owned by a
                // stale or different player-boss. Every active selected wrapper,
                // and every wrapper whose body was already frozen, must resolve
                // to one common PFT leader wrapper whose readonly realPlayer is
                // the exact local PMC body bound for this round.
                if (liveBody || alreadyBoundToThisBody)
                {
                    object leaderWrapper = _getFollowerBossMethod.Invoke(follower, null);
                    Player leaderPlayer = leaderWrapper == null
                        ? null
                        : _bossRealPlayerField.GetValue(leaderWrapper) as Player;
                    if (leaderWrapper == null
                        || ReferenceEquals(leaderPlayer, null)
                        || leaderPlayer == null
                        || !ReferenceEquals(leaderPlayer, currentLocalPlayer))
                    {
                        return LatchRuntimeRosterProblem(
                            $"PitFireTeam follower {profileId} was bound to a stale or wrong local leader body",
                            out followers,
                            out problem);
                    }

                    bool splitLeader;
                    lock (StateLock)
                    {
                        splitLeader = _currentLeaderWrapper != null
                            && !ReferenceEquals(_currentLeaderWrapper, leaderWrapper);
                        if (!splitLeader && _currentLeaderWrapper == null)
                            _currentLeaderWrapper = leaderWrapper;
                    }

                    if (splitLeader)
                    {
                        return LatchRuntimeRosterProblem(
                            "PitFireTeam split the Arena followers across different leader wrappers",
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
                        CurrentBodiesByAccountId[accountId] = bot;
                }

                // Before physical binding, dead/inactive selected wrappers are
                // just incomplete assembly and must not become the canonical
                // body. Once bound, the same corpse remains valid evidence.
                bool boundToThisBody;
                lock (StateLock)
                {
                    boundToThisBody = CurrentBodiesByAccountId.TryGetValue(accountId, out var originalBody)
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
                    if (!TryGetCurrentProfileId(pair.Key, out string expectedProfileId))
                    {
                        return LatchRuntimeRosterProblem(
                            $"the current PitFireTeam profile for account {pair.Key} disappeared",
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
        if (CurrentIdentityMatches(accountId, bot.ProfileId))
            return true;

        return IsTeamProfileId(bot.ProfileId);
    }

    public static bool IsTeamPlayer(Player player)
    {
        if (!LiveTeamArena || player == null)
            return false;

        if (CurrentIdentityMatches(player.AccountId?.Trim(), player.ProfileId))
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
            || RoundTransitionActive
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
            var team = new List<IPlayer>(_frozenCompanionCount + 1);
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
        PitFireTeamRoundLifecycle.TickPendingRawPlayerCompletions();

        // During an exact Zombie team's terminal result, its last followers may
        // already be disposing. Continue draining creation completions above;
        // only the obsolete live-roster poll stops, never persistence protection.
        // Manual native Stop also disposes followers before Arena's later End.
        bool zombieResult = Modes.ArenaMatchService.Current?.Mode is Modes.ZombieMode zombie
            && zombie.TeamResultOwnsCurrentRaid;
        if (LiveTeamArena && !RoundTransitionActive && !zombieResult
            && !ArenaRaidExit.NativeStopOwnsCurrentRaid)
            _ = GetLiveFollowers();
    }

    public static void BeginFinalizing()
    {
        lock (StateLock)
        {
            if (HasCompleteCanonicalRosterUnsafe())
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
            if (HasCompleteCanonicalRosterUnsafe())
                _finalizingUntilUtc = DateTime.MinValue;
        }
    }

    public static void Clear(string reason)
    {
        bool hadState;
        lock (StateLock)
        {
            hadState = CanonicalAccountIds.Count > 0 || CanonicalProfilesByAccountId.Count > 0
                || CurrentProfilesByAccountId.Count > 0 || CurrentBodiesByAccountId.Count > 0
                || KnownProfileIds.Count > 0 || _roundTransitionActive
                || _finalizingUntilUtc != DateTime.MinValue;
            _frozenCompanionCount = 0;
            CanonicalAccountIds.Clear();
            CanonicalProfilesByAccountId.Clear();
            CurrentProfilesByAccountId.Clear();
            CurrentBodiesByAccountId.Clear();
            KnownProfileIds.Clear();
            _currentLocalPlayer = null;
            _currentLocalProfileId = null;
            _currentLeaderWrapper = null;
            _pendingLocalPlayer = null;
            _pendingLocalProfileId = null;
            _roundTransitionActive = false;
            _finalizingUntilUtc = DateTime.MinValue;
            _deathSuppressionLogged = false;
            _progressSuppressionLogged = false;
            _followerDiscoveryFailureLogged = false;
            _hostilityFailureLogged = false;
            _stopCleanupFailureLogged = false;
            _runtimeRosterProblem = string.Empty;
        }

        InvalidateStoredProfileCache();
        PitFireTeamAreaSpawns.Reset();
        PitFireTeamRoundLifecycle.Reset();

        if (hadState)
            Plugin.ModLogger.LogDebug($"Arena cleared PitFireTeam compatibility state ({reason}).");
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
            _accountWideReportsRequired = pluginInfo?.Metadata?.Version >= new System.Version(1, 0, 0);
            if (pluginInfo?.Instance == null)
                throw new InvalidOperationException("the BepInEx plugin instance is unavailable");

            var assembly = pluginInfo.Instance.GetType().Assembly;
            _accountWideReportsRequired |= assembly.GetName().Version >= new System.Version(1, 0, 0, 0);
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

            string pluginVersion = pluginInfo.Metadata.Version?.ToString();
            if (!PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion))
                throw new InvalidOperationException(
                    $"version {pluginInfo.Metadata.Version ?? new System.Version(0, 0)} is unsupported (expected {PitFireTeamVersions.Describe})");

            if (!PitFireTeamVersions.IsSupportedAssemblyVersion(assembly.GetName().Version)
                || !PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assembly.GetName().Version))
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
            PitFireTeamReportGuard.Configure(assembly, Harmony);

            var stopFinalizer = typeof(PitFireTeamCompat).GetMethod(nameof(PitStopCleanupFinalizer),
                BindingFlags.NonPublic | BindingFlags.Static);
            Harmony.Patch(stopGettingInfoPrefixMethod, finalizer: new HarmonyMethod(stopFinalizer));

            // The ACTUAL version, not the accepted list: the lifecycle repeats both
            // gates for the build in front of it, which is only a real second
            // opinion if it is told what that build claims to be.
            PitFireTeamRoundCapability roundCapability =
                PitFireTeamRoundLifecycle.Configure(assembly, pluginVersion);
            if (!roundCapability.CanStartAdditionalRound)
            {
                throw new InvalidOperationException(
                    "the protected multi-round lifecycle contract is unavailable: "
                    + roundCapability.Reason);
            }

            if (!PitFireTeamGoalContext.Configure(assembly, pluginVersion, out string goalContextProblem))
                Plugin.ModLogger.LogWarning("Arena PFT pursuit goal handoff unavailable: " + goalContextProblem);
            PitFireTeamPortedMapSpawns.Configure(assembly, pluginVersion, Harmony);
            PitFireTeamPortedMapCombat.Configure(Harmony);
            PitFireTeamAreaSpawns.Configure(assembly, Harmony);
            _contractState = PitFireTeamContractState.Compatible;
            _contractProblem = string.Empty;
            if (PitFireTeamVersions.IsNewerThanVerified(assembly.GetName().Version))
                Plugin.ModLogger.LogWarning($"Arena is attempting unreviewed PitFireTeam {pluginVersion}; reflected contracts passed, but this version has not been tested. Reviewed versions: {string.Join(", ", PitFireTeamVersions.VerifiedPluginVersions)}.");
            Plugin.ModLogger.LogInfo(
                $"Arena found compatible PitFireTeam {pluginVersion}; optional Tactical/Zombie squad integration is available.");
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
            PitFireTeamRoundLifecycle.RollbackConfiguration(ex.Message);
            PitFireTeamReportGuard.RollbackConfiguration();
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
            Plugin.ModLogger.LogDebug(
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
        if (!PersistenceSuppressionActive && !PitFireTeamReportGuard.SuppressAutomaticReportCapture)
            return true;

        if (!_progressSuppressionLogged)
        {
            _progressSuppressionLogged = true;
            Plugin.ModLogger.LogDebug(
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
            || !PersistenceSuppressionActive)
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
        Plugin.ModLogger.LogWarning(_accountWideReportsRequired && !PitFireTeamReportGuard.IsReady
            ? $"Arena is unavailable because PitFireTeam report protection failed: {_contractProblem}."
            : $"Arena will not start a PitFireTeam team raid: {_contractProblem}. Solo Arena remains available.");
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
    ///
    /// Map-screen eligibility re-runs this on every 0.2s updater tick. Display
    /// ticks reuse one successful resolution for the same selected AID/RaidPlayer
    /// set; freeze invalidates that cache and re-authenticates immediately
    /// before arming.
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

        if (TryGetCachedSelectedProfileIds(accountIds, raidIds, out profileIds))
            return true;

        List<string> resolved = new(accountIds.Count);
        var trimmedAccounts = new string[accountIds.Count];
        var trimmedRaidIds = new string[raidIds.Count];
        for (int index = 0; index < accountIds.Count; index++)
        {
            string accountId = accountIds[index]?.Trim();
            string raidId = raidIds[index]?.Trim();
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(raidId))
            {
                problem = "a selected member has no account or RaidPlayer identity";
                return false;
            }

            trimmedAccounts[index] = accountId;
            trimmedRaidIds[index] = raidId;

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

        StoreSelectedProfileCache(trimmedAccounts, trimmedRaidIds, resolved.ToArray());
        profileIds = resolved;
        return true;
    }

    private static bool TryGetCachedSelectedProfileIds(
        IReadOnlyList<string> accountIds,
        IReadOnlyList<string> raidIds,
        out IReadOnlyList<string> profileIds)
    {
        profileIds = Array.Empty<string>();
        lock (ProfileCacheLock)
        {
            if (_storedProfileCache == null
                || _storedProfileCache.AccountIds == null
                || _storedProfileCache.RaidIds == null
                || _storedProfileCache.ProfileIds == null
                || _storedProfileCache.AccountIds.Length != accountIds.Count
                || _storedProfileCache.RaidIds.Length != raidIds.Count
                || _storedProfileCache.ProfileIds.Length != accountIds.Count)
            {
                return false;
            }

            for (int index = 0; index < accountIds.Count; index++)
            {
                if (!string.Equals(
                        _storedProfileCache.AccountIds[index],
                        accountIds[index]?.Trim(),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _storedProfileCache.RaidIds[index],
                        raidIds[index]?.Trim(),
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            profileIds = (string[])_storedProfileCache.ProfileIds.Clone();
            return true;
        }
    }

    private static void StoreSelectedProfileCache(
        string[] accountIds,
        string[] raidIds,
        string[] profileIds)
    {
        lock (ProfileCacheLock)
        {
            _storedProfileCache = new StoredProfileResolution
            {
                AccountIds = accountIds,
                RaidIds = raidIds,
                ProfileIds = profileIds
            };
        }
    }

    private static void InvalidateStoredProfileCache()
    {
        lock (ProfileCacheLock)
            _storedProfileCache = null;
    }

    private static bool TryMatchLiveRosterToDecision(
        PitFireTeamSelectionDecision decision,
        IReadOnlyList<string> liveAccountIds,
        IReadOnlyList<string> liveProfileIds,
        out string reason)
    {
        reason = string.Empty;
        if (!PitFireTeamSelectionPolicy.IsSupportedCompanionCount(decision.CompanionAccountIds.Count)
            || decision.CompanionProfilesByAccountId.Count != decision.CompanionAccountIds.Count
            || liveAccountIds == null
            || liveProfileIds == null
            || liveAccountIds.Count != liveProfileIds.Count
            || liveAccountIds.Count != decision.CompanionAccountIds.Count)
        {
            reason = "the PitFireTeam roster changed between map display and raid start.";
            return false;
        }

        var live = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < liveAccountIds.Count; index++)
        {
            string accountId = liveAccountIds[index]?.Trim();
            string profileId = liveProfileIds[index]?.Trim();
            if (string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrWhiteSpace(profileId)
                || !live.TryAdd(accountId, profileId))
            {
                reason = "Arena refused an invalid, duplicate or mismatched live PitFireTeam companion identity.";
                return false;
            }
        }

        foreach (string accountId in decision.CompanionAccountIds)
        {
            string normalized = accountId?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !live.ContainsKey(normalized))
            {
                reason = "the PitFireTeam roster changed between map display and raid start.";
                return false;
            }
        }

        foreach (var pair in decision.CompanionProfilesByAccountId)
        {
            if (!live.TryGetValue(pair.Key, out string liveProfile)
                || !string.Equals(liveProfile, pair.Value?.Trim(), StringComparison.Ordinal))
            {
                reason = "a PitFireTeam companion stored profile changed between map display and raid start.";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetCurrentProfileId(string accountId, out string profileId)
    {
        profileId = null;
        if (string.IsNullOrWhiteSpace(accountId))
            return false;

        lock (StateLock)
            return CurrentProfilesByAccountId.TryGetValue(accountId.Trim(), out profileId);
    }

    private static bool CurrentIdentityMatches(string accountId, string profileId)
        => TryGetCurrentProfileId(accountId, out var expectedProfileId)
           && !string.IsNullOrWhiteSpace(profileId)
           && string.Equals(expectedProfileId, profileId.Trim(), StringComparison.Ordinal);

    private static bool HasCompleteCanonicalRosterUnsafe()
        => PitFireTeamSelectionPolicy.IsSupportedCompanionCount(_frozenCompanionCount)
           && CanonicalAccountIds.Count == _frozenCompanionCount
           && CanonicalProfilesByAccountId.Count == _frozenCompanionCount
           && CanonicalAccountIds.SetEquals(CanonicalProfilesByAccountId.Keys);
}
