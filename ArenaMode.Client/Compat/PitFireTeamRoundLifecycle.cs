using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Inventory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EftInventory = EFT.InventoryLogic.Inventory;

namespace ArenaMode.Compat;

internal sealed class PitFireTeamRoundLifecycleSnapshot
{
    public PitFireTeamRoundLifecycleStage Stage { get; set; }
    public PitFireTeamRoundCapability Capability { get; set; }
    public int Epoch { get; set; }
    public string Diagnostic { get; set; }
    public int RetiringBodyCount { get; set; }
    public int CurrentFollowerCount { get; set; }
    public IReadOnlyDictionary<string, string> RuntimeProfilesByAccountId { get; set; }
}

/// <summary>
/// Exact, reflection-only PitFireTeam 0.10.0 round-body adapter. It deliberately
/// owns no Arena phase changes: the caller must keep damage and persistence
/// blocked until this adapter reports FollowersReady and the opponent/hostility
/// gates have independently passed.
/// </summary>
internal static class PitFireTeamRoundLifecycle
{
    public const string PluginGuid = "xyz.pit.fireteam";
    public const string SupportedVersion = "0.10.0";

    private const string BossTypeName = "pitTeam.Components.pitAIBossPlayer";
    private const string FollowerTypeName = "pitTeam.Components.BotFollowerPlayer";
    private const string BossPlayersTypeName = "pitTeam.Modules.BossPlayers";
    private const string BotsPatchTypeName = "pitTeam.Patches.BotsControllerPatch";

    private static readonly object StateLock = new();
    private static readonly AccessTools.FieldRef<EftInventory, InventoryEquipment> EquipmentField =
        AccessTools.FieldRefAccess<EftInventory, InventoryEquipment>(nameof(EftInventory.Equipment));

    private static Assembly _assembly;
    private static int _ownerThreadId;
    private static PitFireTeamRoundCapability _capability =
        PitFireTeamRoundCapability.Unavailable("PitFireTeam round lifecycle has not been configured.");
    private static PitFireTeamRoundLifecycleStage _stage = PitFireTeamRoundLifecycleStage.Unconfigured;
    private static string _diagnostic = "PitFireTeam round lifecycle has not been configured.";
    private static int _epoch;
    private static DateTime _deadlineUtc;

    private static Type _bossType;
    private static Type _followerType;
    private static Type _bossPlayersType;
    private static Type _botsPatchType;
    private static PropertyInfo _bossPlayersInstanceProperty;
    private static MethodInfo _getBossMethod;
    private static MethodInfo _getFollowersMethod;
    private static MethodInfo _addPlayerAsBossMethod;
    private static MethodInfo _removeFollowerMethod;
    private static MethodInfo _removeBossPlayerMethod;
    private static MethodInfo _getFollowerBotMethod;
    private static MethodInfo _getFollowerBossMethod;
    private static MethodInfo _dismissFollowerMethod;
    private static MethodInfo _unhookFollowerDeathMethod;
    private static FieldInfo _bossRealPlayerField;
    private static PropertyInfo _bossFollowersProperty;
    private static PropertyInfo _bossGroupProperty;
    private static FieldInfo _botsPatchInstanceField;
    private static FieldInfo _botsControllerField;
    private static FieldInfo _spawnedPlayersField;
    private static FieldInfo _followerCreationTasksField;
    private static MethodInfo _createFollowerProfilesMethod;
    private static MethodInfo _spawnGroupBotsMethod;

    private static Player _oldLeader;
    private static object _oldBoss;
    private static Player _currentLeader;
    private static object _currentBoss;
    private static Task _profileTask;
    private static Task _spawnTask;
    private static IDictionary _preparedProfileDictionary;
    private static List<BotOwner> _retiringBodies = new();
    private static List<BotOwner> _currentFollowers = new();
    private static List<BotOwner> _lastStableFollowers = new();
    private static int _stableFollowerPasses;
    private static Dictionary<string, string> _canonicalProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static Dictionary<string, string> _runtimeProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static Dictionary<string, Profile> _preparedProfilesByAccountId =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> HistoricalRuntimeProfileIds =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> HistoricalRuntimeItemIds =
        new(StringComparer.OrdinalIgnoreCase);

    public static PitFireTeamRoundCapability Capability
    {
        get
        {
            lock (StateLock)
                return _capability;
        }
    }

    public static PitFireTeamRoundLifecycleStage Stage
    {
        get
        {
            lock (StateLock)
                return _stage;
        }
    }

    public static string Diagnostic
    {
        get
        {
            lock (StateLock)
                return _diagnostic;
        }
    }

    public static PitFireTeamRoundLifecycleSnapshot Status
    {
        get
        {
            lock (StateLock)
            {
                return new PitFireTeamRoundLifecycleSnapshot
                {
                    Stage = _stage,
                    Capability = _capability,
                    Epoch = _epoch,
                    Diagnostic = _diagnostic,
                    RetiringBodyCount = _retiringBodies.Count,
                    CurrentFollowerCount = _currentFollowers.Count,
                    RuntimeProfilesByAccountId = new Dictionary<string, string>(
                        _runtimeProfilesByAccountId,
                        StringComparer.Ordinal)
                };
            }
        }
    }

    /// <summary>
    /// Resolve the already loaded BepInEx plugin. The exact metadata version is
    /// part of the contract; assembly layout alone never upgrades compatibility.
    /// </summary>
    public static PitFireTeamRoundCapability Initialize()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo)
            || pluginInfo?.Instance == null)
        {
            SetUnavailable("PitFireTeam is not loaded.");
            return Capability;
        }

        return Configure(
            pluginInfo.Instance.GetType().Assembly,
            pluginInfo.Metadata.Version?.ToString());
    }

    /// <summary>
    /// Explicit injection seam for the existing PitFireTeam compatibility owner.
    /// Repeating the same configuration is harmless; switching assemblies in a
    /// live process is rejected.
    /// </summary>
    public static PitFireTeamRoundCapability Configure(
        Assembly pitFireTeamAssembly,
        string pluginVersion)
    {
        lock (StateLock)
        {
            if (_assembly != null)
            {
                if (ReferenceEquals(_assembly, pitFireTeamAssembly)
                    && string.Equals(pluginVersion, SupportedVersion, StringComparison.Ordinal))
                {
                    return _capability;
                }

                FailUnsafe("PitFireTeam round lifecycle refused a different assembly after configuration.");
                return _capability;
            }

            if (pitFireTeamAssembly == null)
            {
                SetUnavailableUnsafe("PitFireTeam assembly is missing.");
                return _capability;
            }

            if (!string.Equals(pluginVersion, SupportedVersion, StringComparison.Ordinal))
            {
                SetUnavailableUnsafe(
                    $"PitFireTeam {pluginVersion ?? "unknown"} is unsupported; exact {SupportedVersion} is required.");
                return _capability;
            }

            try
            {
                DiscoverExactContract(pitFireTeamAssembly);
                _assembly = pitFireTeamAssembly;
                _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
                _capability = PitFireTeamRoundCapability.Available();
                ClearRaidStateUnsafe(clearHistory: true);
                _stage = PitFireTeamRoundLifecycleStage.ContractReady;
                _diagnostic = "PitFireTeam 0.10.0 round lifecycle contract is ready.";
            }
            catch (Exception ex)
            {
                ClearContractUnsafe();
                SetUnavailableUnsafe(
                    "PitFireTeam 0.10.0 round lifecycle contract failed: "
                    + ex.GetBaseException().Message);
            }

            return _capability;
        }
    }

    /// <summary>
    /// Detach the old PFT ownership synchronously, then request ordinary EFT
    /// deaths for any surviving old follower body. PollRetirement owns the later
    /// registry barrier. The caller must provide the four bodies frozen by the
    /// completed round; missing wrappers are not allowed to erase body identity.
    /// </summary>
    public static bool BeginTeardown(
        Player oldLeader,
        IReadOnlyCollection<BotOwner> boundFollowers,
        IReadOnlyDictionary<string, string> canonicalProfilesByAccountId,
        bool persistenceSuppressionActive,
        TimeSpan timeout,
        out string reason)
    {
        lock (StateLock)
        {
            reason = string.Empty;
            if (!CheckThreadUnsafe(out reason)
                || !RequireCapabilityUnsafe(out reason)
                || !RequirePersistenceSuppressionUnsafe(persistenceSuppressionActive, out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            if (_stage == PitFireTeamRoundLifecycleStage.AwaitingRetirement
                && ReferenceEquals(_oldLeader, oldLeader))
            {
                reason = _diagnostic;
                return true;
            }

            if (_stage != PitFireTeamRoundLifecycleStage.ContractReady
                && _stage != PitFireTeamRoundLifecycleStage.FollowersReady)
            {
                reason = $"teardown is invalid while lifecycle stage is {_stage}";
                FailUnsafe(reason);
                return false;
            }

            if (ReferenceEquals(oldLeader, null) || oldLeader == null
                || string.IsNullOrWhiteSpace(oldLeader.ProfileId))
            {
                reason = "the old local leader body or profile identity is missing";
                FailUnsafe(reason);
                return false;
            }

            if (!PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(
                    canonicalProfilesByAccountId,
                    out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            if (!TryValidateBoundFollowersUnsafe(
                    boundFollowers,
                    canonicalProfilesByAccountId,
                    out var oldBodies,
                    out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            _epoch++;
            _stage = PitFireTeamRoundLifecycleStage.TearingDown;
            _diagnostic = $"PitFireTeam round epoch {_epoch}: tearing down old ownership.";
            _deadlineUtc = Deadline(timeout);
            _canonicalProfilesByAccountId = canonicalProfilesByAccountId.ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.Ordinal);
            _oldLeader = oldLeader;
            _oldBoss = null;
            _currentLeader = null;
            _currentBoss = null;
            _profileTask = null;
            _spawnTask = null;
            _preparedProfileDictionary = null;
            _preparedProfilesByAccountId.Clear();
            _currentFollowers.Clear();
            _lastStableFollowers.Clear();
            _stableFollowerPasses = 0;

            try
            {
                object oldBoss = _getBossMethod.Invoke(null, new object[] { oldLeader.ProfileId });
                if (oldBoss == null
                    || !ReferenceEquals(_bossRealPlayerField.GetValue(oldBoss), oldLeader))
                {
                    throw new InvalidOperationException(
                        "PFT did not expose one boss wrapper for the exact old local body");
                }

                _oldBoss = oldBoss;
                if (_bossFollowersProperty.GetValue(oldBoss) is not IEnumerable bossFollowers)
                    throw new InvalidOperationException("PFT old boss follower list is unavailable");
                foreach (object follower in bossFollowers)
                {
                    if (follower is not BotOwner bossFollower
                        || !oldBodies.Any(expected => ReferenceEquals(expected, bossFollower)))
                    {
                        throw new InvalidOperationException(
                            "PFT's old boss owns a physical follower outside the frozen four-body roster");
                    }
                }

                List<object> wrappers = ReadFollowerWrappersUnsafe()
                    .Where(wrapper => ReferenceEquals(
                        _getFollowerBossMethod.Invoke(wrapper, null),
                        oldBoss))
                    .ToList();

                foreach (object wrapper in wrappers)
                {
                    BotOwner bot = _getFollowerBotMethod.Invoke(wrapper, null) as BotOwner;
                    if (ReferenceEquals(bot, null)
                        || !oldBodies.Any(expected => ReferenceEquals(expected, bot)))
                    {
                        throw new InvalidOperationException(
                            "PFT's old boss owns a wrapper outside the frozen four-body roster");
                    }
                }

                // Unhook every known body first. Dead wrappers can already be absent
                // from boss.Followers while their delegate remains in the boss map.
                foreach (BotOwner bot in oldBodies)
                    _unhookFollowerDeathMethod.Invoke(oldBoss, new object[] { bot });

                // Dismiss before list removal so PFT/SAIN lifecycle cleanup still
                // sees the wrapper's original leader and bot.
                foreach (object wrapper in wrappers)
                {
                    BotOwner bot = (BotOwner)_getFollowerBotMethod.Invoke(wrapper, null);
                    _dismissFollowerMethod.Invoke(wrapper, new object[] { false });
                    _removeFollowerMethod.Invoke(null, new object[] { bot, oldBoss });
                }

                int survivingWrappers = ReadFollowerWrappersUnsafe().Count(wrapper =>
                    ReferenceEquals(_getFollowerBossMethod.Invoke(wrapper, null), oldBoss));
                if (survivingWrappers != 0)
                {
                    throw new InvalidOperationException(
                        $"PFT retained {survivingWrappers} wrapper(s) for the old boss after explicit removal");
                }

                if (_bossFollowersProperty.GetValue(oldBoss) is not IEnumerable remainingBossFollowers
                    || remainingBossFollowers.Cast<object>().Any())
                {
                    throw new InvalidOperationException(
                        "PFT retained physical followers on the old boss after explicit removal");
                }

                object bossPlayers = _bossPlayersInstanceProperty.GetValue(null);
                if (bossPlayers == null
                    || _removeBossPlayerMethod.Invoke(
                        bossPlayers,
                        new object[] { oldLeader.ProfileId, false }) is not bool removed
                    || !removed
                    || _getBossMethod.Invoke(null, new object[] { oldLeader.ProfileId }) != null)
                {
                    throw new InvalidOperationException(
                        "PFT did not remove the old boss with died=false");
                }

                _retiringBodies = oldBodies;
                foreach (BotOwner bot in _retiringBodies)
                {
                    var health = bot?.GetPlayer?.ActiveHealthController;
                    if (health != null && health.IsAlive)
                        health.Kill(EDamageType.Undefined);
                }

                _stage = PitFireTeamRoundLifecycleStage.AwaitingRetirement;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: waiting for four old bodies to leave active registries.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "PFT old-round teardown failed: " + ex.GetBaseException().Message;
                FailUnsafe(reason);
                return false;
            }
        }
    }

    public static PitFireTeamRoundPolicyDecision PollRetirement(GameWorld world)
        => PollRetirement(world, DateTime.UtcNow);

    internal static PitFireTeamRoundPolicyDecision PollRetirement(
        GameWorld world,
        DateTime utcNow)
    {
        lock (StateLock)
        {
            if (_stage == PitFireTeamRoundLifecycleStage.ReadyForLeader)
                return PitFireTeamRoundPolicyDecision.Ready();

            if (_stage != PitFireTeamRoundLifecycleStage.AwaitingRetirement)
                return FailDecisionUnsafe($"retirement polling is invalid while lifecycle stage is {_stage}");

            if (!CheckThreadUnsafe(out var threadProblem))
                return FailDecisionUnsafe(threadProblem);

            if (world == null)
                return FailDecisionUnsafe("GameWorld is unavailable while proving old follower retirement");

            try
            {
                bool oldBossPresent = _getBossMethod.Invoke(
                    null,
                    new object[] { _oldLeader.ProfileId }) != null;
                int oldWrapperCount = ReadFollowerWrappersUnsafe().Count(wrapper =>
                    ReferenceEquals(_getFollowerBossMethod.Invoke(wrapper, null), _oldBoss));
                int active = 0;
                int registered = 0;

                foreach (BotOwner bot in _retiringBodies)
                {
                    if (ReferenceEquals(bot, null))
                        continue;

                    if (bot.BotState == EBotState.Active
                        || bot.BotState == EBotState.PreActive
                        || bot.GetPlayer?.HealthController?.IsAlive == true)
                    {
                        active++;
                    }

                    Player body = bot.GetPlayer;
                    if (world != null
                        && !ReferenceEquals(body, null)
                        && world.RegisteredPlayers.Any(player => ReferenceEquals(player, body)))
                    {
                        registered++;
                    }
                }

                PitFireTeamRoundPolicyDecision decision =
                    PitFireTeamRoundLifecyclePolicy.EvaluateRetirement(
                        new PitFireTeamRetirementObservation
                        {
                            OldBossPresent = oldBossPresent,
                            OldWrapperCount = oldWrapperCount,
                            ActiveOldBodyCount = active,
                            RegisteredOldBodyCount = registered,
                            TimedOut = utcNow >= _deadlineUtc
                        });

                _diagnostic = decision.Reason;
                if (decision.Decision == PitFireTeamRoundPollDecision.Ready)
                {
                    _retiringBodies.Clear();
                    _stage = PitFireTeamRoundLifecycleStage.ReadyForLeader;
                    _diagnostic = $"PitFireTeam round epoch {_epoch}: old bodies retired; ready for the current local leader.";
                }
                else if (decision.Decision == PitFireTeamRoundPollDecision.Failed)
                {
                    FailUnsafe(decision.Reason);
                }

                return decision;
            }
            catch (Exception ex)
            {
                return FailDecisionUnsafe(
                    "PFT retirement inspection failed: " + ex.GetBaseException().Message);
            }
        }
    }

    public static bool TryRebuildBoss(
        Player currentLeader,
        BotsController botsController,
        bool persistenceSuppressionActive,
        out string reason)
    {
        lock (StateLock)
        {
            reason = string.Empty;
            if (!CheckThreadUnsafe(out reason)
                || !RequireCapabilityUnsafe(out reason)
                || !RequirePersistenceSuppressionUnsafe(persistenceSuppressionActive, out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            if (_stage == PitFireTeamRoundLifecycleStage.LeaderReady
                && ReferenceEquals(_currentLeader, currentLeader))
            {
                return true;
            }

            if (_stage != PitFireTeamRoundLifecycleStage.ReadyForLeader)
            {
                reason = $"leader rebuild is invalid while lifecycle stage is {_stage}";
                FailUnsafe(reason);
                return false;
            }

            if (ReferenceEquals(currentLeader, null) || currentLeader == null
                || currentLeader.HealthController?.IsAlive != true
                || string.IsNullOrWhiteSpace(currentLeader.ProfileId)
                || botsController == null)
            {
                reason = "the current local leader or bots controller is unavailable";
                FailUnsafe(reason);
                return false;
            }

            try
            {
                if (_bossPlayersInstanceProperty.GetValue(null) == null)
                    throw new InvalidOperationException("PFT BossPlayers.Instance is unavailable");

                if (_botsControllerField.GetValue(null) is not BotsController pftController
                    || !ReferenceEquals(pftController, botsController))
                {
                    throw new InvalidOperationException(
                        "PFT and Arena do not reference the same BotsController");
                }

                if (_getBossMethod.Invoke(null, new object[] { currentLeader.ProfileId }) != null)
                    throw new InvalidOperationException("PFT still has a boss under the local profile ID");

                object newBoss = _addPlayerAsBossMethod.Invoke(
                    null,
                    new object[] { currentLeader, botsController });
                if (newBoss == null
                    || !ReferenceEquals(_bossRealPlayerField.GetValue(newBoss), currentLeader)
                    || !ReferenceEquals(
                        _getBossMethod.Invoke(null, new object[] { currentLeader.ProfileId }),
                        newBoss))
                {
                    throw new InvalidOperationException(
                        "PFT did not build one boss wrapper around the exact current local body");
                }

                ReplaceSpawnedBossUnsafe(newBoss, currentLeader);
                _currentLeader = currentLeader;
                _currentBoss = newBoss;
                _stage = PitFireTeamRoundLifecycleStage.LeaderReady;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: rebuilt the leader wrapper.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "PFT leader rebuild failed: " + ex.GetBaseException().Message;
                FailUnsafe(reason);
                return false;
            }
        }
    }

    /// <summary>
    /// Starts PFT's public profile prefetch but not bot activation. PFT 0.10.0
    /// caches this exact Task by leader profile, which is the supported seam for
    /// replacing its result profiles before SpawnGroupBots consumes them.
    /// </summary>
    public static bool BeginFollowerProfilePreparation(
        bool persistenceSuppressionActive,
        TimeSpan timeout,
        out string reason)
    {
        lock (StateLock)
        {
            reason = string.Empty;
            if (!CheckThreadUnsafe(out reason)
                || !RequirePersistenceSuppressionUnsafe(persistenceSuppressionActive, out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            if (_stage == PitFireTeamRoundLifecycleStage.PreparingProfiles)
                return true;

            if (_stage != PitFireTeamRoundLifecycleStage.LeaderReady)
            {
                reason = $"profile preparation is invalid while lifecycle stage is {_stage}";
                FailUnsafe(reason);
                return false;
            }

            try
            {
                object patchInstance = _botsPatchInstanceField.GetValue(null);
                if (patchInstance == null)
                    throw new InvalidOperationException("PFT BotsControllerPatch.Instance is unavailable");

                IDictionary cache = ReadProfileTaskCacheUnsafe();
                if (cache.Contains(_currentLeader.ProfileId))
                {
                    throw new InvalidOperationException(
                        "PFT retained a stale follower-profile task for the local profile");
                }

                Task task = _createFollowerProfilesMethod.Invoke(
                    patchInstance,
                    new[] { _currentBoss }) as Task;
                if (task == null)
                    throw new InvalidOperationException("PFT returned no follower-profile task");

                cache = ReadProfileTaskCacheUnsafe();
                if (!cache.Contains(_currentLeader.ProfileId)
                    || !ReferenceEquals(cache[_currentLeader.ProfileId], task))
                {
                    throw new InvalidOperationException(
                        "PFT did not cache the exact follower-profile task that SpawnGroupBots must consume");
                }

                _profileTask = task;
                _deadlineUtc = Deadline(timeout);
                _stage = PitFireTeamRoundLifecycleStage.PreparingProfiles;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: waiting for four canonical follower profiles.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "PFT follower-profile preparation failed: " + ex.GetBaseException().Message;
                FailUnsafe(reason);
                return false;
            }
        }
    }

    public static PitFireTeamRoundPolicyDecision PollFollowerProfilePreparation(
        bool persistenceSuppressionActive)
        => PollFollowerProfilePreparation(persistenceSuppressionActive, DateTime.UtcNow);

    internal static PitFireTeamRoundPolicyDecision PollFollowerProfilePreparation(
        bool persistenceSuppressionActive,
        DateTime utcNow)
    {
        lock (StateLock)
        {
            if (!CheckThreadUnsafe(out var threadProblem))
                return FailDecisionUnsafe(threadProblem);

            if (!persistenceSuppressionActive)
                return FailDecisionUnsafe("PitFireTeam persistence suppression was lost during profile preparation");

            if (_stage == PitFireTeamRoundLifecycleStage.ProfilesReady)
                return PitFireTeamRoundPolicyDecision.Ready();

            if (_stage != PitFireTeamRoundLifecycleStage.PreparingProfiles)
                return FailDecisionUnsafe($"profile polling is invalid while lifecycle stage is {_stage}");

            if (_profileTask == null)
                return FailDecisionUnsafe("the PFT follower-profile task disappeared");

            if (_profileTask.IsCanceled)
                return FailDecisionUnsafe("the PFT follower-profile task was canceled");

            if (_profileTask.IsFaulted)
            {
                return FailDecisionUnsafe(
                    "the PFT follower-profile task faulted: "
                    + (_profileTask.Exception?.GetBaseException().Message ?? "unknown error"));
            }

            if (!_profileTask.IsCompleted)
            {
                if (utcNow >= _deadlineUtc)
                    return FailDecisionUnsafe("PFT follower-profile preparation timed out");

                _diagnostic = "waiting for PFT follower-profile preparation";
                return PitFireTeamRoundPolicyDecision.Pending(_diagnostic);
            }

            try
            {
                IDictionary cache = ReadProfileTaskCacheUnsafe();
                if (!cache.Contains(_currentLeader.ProfileId)
                    || !ReferenceEquals(cache[_currentLeader.ProfileId], _profileTask))
                {
                    throw new InvalidOperationException(
                        "PFT replaced or removed the prefetched task before Arena could remap it");
                }

                PropertyInfo resultProperty = _profileTask.GetType().GetProperty(
                    "Result",
                    BindingFlags.Public | BindingFlags.Instance);
                if (resultProperty?.GetValue(_profileTask) is not IDictionary profiles)
                    throw new InvalidOperationException("PFT profile task returned no dictionary");

                if (!TryRemapProfilesUnsafe(profiles, out var remapProblem))
                    throw new InvalidOperationException(remapProblem);

                _preparedProfileDictionary = profiles;
                _stage = PitFireTeamRoundLifecycleStage.ProfilesReady;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: four fresh runtime profiles are ready before activation.";
                return PitFireTeamRoundPolicyDecision.Ready();
            }
            catch (Exception ex)
            {
                return FailDecisionUnsafe(
                    "PFT fresh runtime identity remap failed: "
                    + ex.GetBaseException().Message);
            }
        }
    }

    public static bool TryStartFollowerSpawn(
        bool persistenceSuppressionActive,
        TimeSpan timeout,
        out string reason)
    {
        lock (StateLock)
        {
            reason = string.Empty;
            if (!CheckThreadUnsafe(out reason)
                || !RequirePersistenceSuppressionUnsafe(persistenceSuppressionActive, out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            if (_stage == PitFireTeamRoundLifecycleStage.SpawnRequested)
                return true;

            if (_stage != PitFireTeamRoundLifecycleStage.ProfilesReady)
            {
                reason = $"follower spawning is invalid while lifecycle stage is {_stage}";
                FailUnsafe(reason);
                return false;
            }

            try
            {
                IDictionary cache = ReadProfileTaskCacheUnsafe();
                if (!cache.Contains(_currentLeader.ProfileId)
                    || !ReferenceEquals(cache[_currentLeader.ProfileId], _profileTask))
                {
                    throw new InvalidOperationException(
                        "PFT will not consume Arena's prefetched profile task");
                }

                PropertyInfo resultProperty = _profileTask.GetType().GetProperty(
                    "Result",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!ReferenceEquals(resultProperty?.GetValue(_profileTask), _preparedProfileDictionary))
                {
                    throw new InvalidOperationException(
                        "PFT's cached task no longer exposes Arena's remapped profile dictionary");
                }

                foreach (DictionaryEntry entry in _preparedProfileDictionary)
                {
                    string accountId = entry.Key as string;
                    if (accountId == null
                        || !_preparedProfilesByAccountId.TryGetValue(accountId, out Profile expected)
                        || !ReferenceEquals(entry.Value, expected))
                    {
                        throw new InvalidOperationException(
                            "PFT's cached profile dictionary changed after identity remap");
                    }
                }

                object patchInstance = _botsPatchInstanceField.GetValue(null);
                Task spawnTask = _spawnGroupBotsMethod.Invoke(
                    patchInstance,
                    new[] { _currentBoss }) as Task;
                if (spawnTask == null)
                    throw new InvalidOperationException("PFT returned no SpawnGroupBots task");

                _spawnTask = spawnTask;
                _deadlineUtc = Deadline(timeout);
                _lastStableFollowers.Clear();
                _stableFollowerPasses = 0;
                _stage = PitFireTeamRoundLifecycleStage.SpawnRequested;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: follower spawn requested; polling physical wrappers.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "PFT follower spawn request failed: " + ex.GetBaseException().Message;
                FailUnsafe(reason);
                return false;
            }
        }
    }

    public static PitFireTeamRoundPolicyDecision PollFollowers(
        GameWorld world,
        bool persistenceSuppressionActive)
        => PollFollowers(world, persistenceSuppressionActive, DateTime.UtcNow);

    internal static PitFireTeamRoundPolicyDecision PollFollowers(
        GameWorld world,
        bool persistenceSuppressionActive,
        DateTime utcNow)
    {
        lock (StateLock)
        {
            if (!CheckThreadUnsafe(out var threadProblem))
                return FailDecisionUnsafe(threadProblem);

            if (!persistenceSuppressionActive)
                return FailDecisionUnsafe("PitFireTeam persistence suppression was lost during follower spawning");

            if (world == null)
                return FailDecisionUnsafe("GameWorld is unavailable while binding the new followers");

            bool wasReady = _stage == PitFireTeamRoundLifecycleStage.FollowersReady;
            if (!wasReady && _stage != PitFireTeamRoundLifecycleStage.SpawnRequested)
                return FailDecisionUnsafe($"follower polling is invalid while lifecycle stage is {_stage}");

            bool spawnFaulted = _spawnTask == null || _spawnTask.IsCanceled || _spawnTask.IsFaulted;
            string spawnProblem = _spawnTask?.Exception?.GetBaseException().Message;

            try
            {
                List<BotOwner> candidates = new();
                HashSet<string> accounts = new(StringComparer.Ordinal);
                HashSet<string> profiles = new(StringComparer.Ordinal);
                bool invalid = false;
                string invalidProblem = string.Empty;

                foreach (object wrapper in ReadFollowerWrappersUnsafe())
                {
                    BotOwner bot = _getFollowerBotMethod.Invoke(wrapper, null) as BotOwner;
                    object boss = _getFollowerBossMethod.Invoke(wrapper, null);
                    string accountId = bot?.Profile?.AccountId?.Trim();
                    bool selected = accountId != null
                                    && _runtimeProfilesByAccountId.ContainsKey(accountId);

                    if (!ReferenceEquals(boss, _currentBoss))
                    {
                        if (selected && IsActiveBody(bot))
                        {
                            invalid = true;
                            invalidProblem = $"selected account {accountId} has an active wrapper under a stale boss";
                            break;
                        }

                        continue;
                    }

                    if (ReferenceEquals(bot, null) || bot == null || !selected)
                    {
                        invalid = true;
                        invalidProblem = "the new PFT boss owns a missing or unselected follower body";
                        break;
                    }

                    string profileId = bot.ProfileId?.Trim();
                    if (!_runtimeProfilesByAccountId.TryGetValue(accountId, out string expectedProfileId)
                        || !string.Equals(profileId, expectedProfileId, StringComparison.Ordinal))
                    {
                        invalid = true;
                        invalidProblem = $"selected account {accountId} spawned runtime profile {profileId ?? "?"} instead of {expectedProfileId ?? "?"}";
                        break;
                    }

                    if (!accounts.Add(accountId) || !profiles.Add(profileId))
                    {
                        invalid = true;
                        invalidProblem = "PFT produced duplicate current-round account or profile identities";
                        break;
                    }

                    candidates.Add(bot);
                }

                if (!invalid)
                {
                    if (_bossFollowersProperty.GetValue(_currentBoss) is not IEnumerable bossFollowers)
                    {
                        invalid = true;
                        invalidProblem = "the rebuilt PFT boss follower list is unavailable";
                    }
                    else
                    {
                        List<BotOwner> ownedBodies = bossFollowers.Cast<object>()
                            .Select(item => item as BotOwner)
                            .ToList();
                        if (ownedBodies.Count != PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount
                            || ownedBodies.Any(item => ReferenceEquals(item, null))
                            || ownedBodies.Any(item => !candidates.Any(candidate => ReferenceEquals(candidate, item)))
                            || candidates.Any(candidate => !ownedBodies.Any(item => ReferenceEquals(item, candidate))))
                        {
                            invalid = true;
                            invalidProblem = "the rebuilt PFT boss does not own the exact four wrapper bodies";
                        }
                    }
                }

                if (!invalid)
                {
                    BotsGroup bossGroup = _bossGroupProperty.GetValue(_currentBoss) as BotsGroup;
                    if (bossGroup == null)
                    {
                        invalid = true;
                        invalidProblem = "the rebuilt PFT boss group is unavailable";
                    }
                    else
                    {
                        foreach (BotOwner candidate in candidates)
                        {
                            if (candidate.BotFollower == null
                                || !candidate.BotFollower.HaveBoss
                                || !ReferenceEquals(candidate.BotFollower.BossToFollow, _currentBoss)
                                || !ReferenceEquals(candidate.BotsGroup, bossGroup)
                                || !bossGroup.Contains(candidate))
                            {
                                invalid = true;
                                invalidProblem = $"follower {candidate.ProfileId} did not complete its PFT boss/group binding";
                                break;
                            }
                        }
                    }
                }

                int activeCount = 0;
                int registeredCount = 0;
                foreach (BotOwner bot in candidates)
                {
                    if (IsActiveBody(bot))
                        activeCount++;

                    Player player = bot.GetPlayer;
                    bool registered = player != null
                                      && world.RegisteredPlayers.Any(item => ReferenceEquals(item, player))
                                      && ReferenceEquals(
                                          world.GetEverExistedPlayerByID(bot.ProfileId),
                                          player);
                    if (registered)
                        registeredCount++;
                    else if (IsActiveBody(bot))
                    {
                        invalid = true;
                        invalidProblem = $"active follower {bot.ProfileId} is missing from GameWorld's exact registry";
                    }
                }

                bool physicallyComplete = !invalid
                                          && candidates.Count == PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount
                                          && activeCount == PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount
                                          && registeredCount == PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount;
                if (physicallyComplete)
                {
                    List<BotOwner> ordered = candidates
                        .OrderBy(bot => bot.Profile.AccountId, StringComparer.Ordinal)
                        .ToList();
                    if (_lastStableFollowers.Count == ordered.Count
                        && ordered.Zip(
                            _lastStableFollowers,
                            (left, right) => ReferenceEquals(left, right)).All(equal => equal))
                    {
                        _stableFollowerPasses++;
                    }
                    else
                    {
                        _lastStableFollowers = ordered;
                        _stableFollowerPasses = 1;
                    }
                }
                else
                {
                    _lastStableFollowers.Clear();
                    _stableFollowerPasses = 0;
                }

                PitFireTeamRoundPolicyDecision decision =
                    PitFireTeamRoundLifecyclePolicy.EvaluateFollowers(
                        new PitFireTeamFollowerObservation
                        {
                            ExpectedCount = PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount,
                            WrapperCount = candidates.Count,
                            ActiveBodyCount = activeCount,
                            RegisteredBodyCount = registeredCount,
                            DistinctAccountCount = accounts.Count,
                            DistinctProfileCount = profiles.Count,
                            InvalidBinding = invalid,
                            InvalidBindingProblem = invalidProblem,
                            SpawnFaulted = spawnFaulted,
                            SpawnProblem = spawnProblem,
                            StablePasses = _stableFollowerPasses,
                            TimedOut = utcNow >= _deadlineUtc
                        });

                _diagnostic = decision.Reason;
                if (decision.Decision == PitFireTeamRoundPollDecision.Ready)
                {
                    _currentFollowers = _lastStableFollowers.ToList();
                    _stage = PitFireTeamRoundLifecycleStage.FollowersReady;
                    _diagnostic = $"PitFireTeam round epoch {_epoch}: four fresh followers are stable and registered.";
                }
                else if (wasReady)
                {
                    return FailDecisionUnsafe(
                        "the previously ready PFT follower roster lost its physical binding: "
                        + decision.Reason);
                }
                else if (decision.Decision == PitFireTeamRoundPollDecision.Failed)
                {
                    FailUnsafe(decision.Reason);
                }

                return decision;
            }
            catch (Exception ex)
            {
                return FailDecisionUnsafe(
                    "PFT follower binding inspection failed: "
                    + ex.GetBaseException().Message);
            }
        }
    }

    public static IReadOnlyList<BotOwner> GetCurrentFollowers()
    {
        lock (StateLock)
            return _currentFollowers.ToArray();
    }

    public static IReadOnlyList<BotOwner> GetRetiringBodies()
    {
        lock (StateLock)
            return _retiringBodies.ToArray();
    }

    /// <summary>
    /// Idempotently forget only Arena's round-local state. PFT owns its raid-stop
    /// cleanup; Reset never edits PFT collections or destroys world objects.
    /// </summary>
    public static void Reset()
    {
        lock (StateLock)
        {
            ClearRaidStateUnsafe(clearHistory: true);
            if (_capability.CanStartAdditionalRound)
            {
                _stage = PitFireTeamRoundLifecycleStage.ContractReady;
                _diagnostic = "PitFireTeam 0.10.0 round lifecycle contract is ready.";
            }
            else
            {
                _stage = _assembly == null
                    ? PitFireTeamRoundLifecycleStage.Unconfigured
                    : PitFireTeamRoundLifecycleStage.Failed;
                _diagnostic = _capability.Reason;
            }
        }
    }

    private static void DiscoverExactContract(Assembly assembly)
    {
        _bossType = RequireType(assembly, BossTypeName);
        _followerType = RequireType(assembly, FollowerTypeName);
        _bossPlayersType = RequireType(assembly, BossPlayersTypeName);
        _botsPatchType = RequireType(assembly, BotsPatchTypeName);

        _bossPlayersInstanceProperty = RequireProperty(
            _bossPlayersType,
            "Instance",
            BindingFlags.Public | BindingFlags.Static,
            _bossPlayersType);
        _getBossMethod = RequireMethod(
            _bossPlayersType,
            "GetBoss",
            BindingFlags.Public | BindingFlags.Static,
            _bossType,
            typeof(string));
        _getFollowersMethod = RequireMethod(
            _bossPlayersType,
            "GetFollowers",
            BindingFlags.Public | BindingFlags.Static,
            null);
        RequireEnumerableOf(_getFollowersMethod.ReturnType, _followerType, "BossPlayers.GetFollowers");
        _addPlayerAsBossMethod = RequireMethod(
            _bossPlayersType,
            "AddPlayerAsBoss",
            BindingFlags.Public | BindingFlags.Static,
            _bossType,
            typeof(Player),
            typeof(BotsController));
        _removeFollowerMethod = RequireMethod(
            _bossPlayersType,
            "RemoveFollower",
            BindingFlags.Public | BindingFlags.Static,
            typeof(void),
            typeof(BotOwner),
            _bossType);
        _removeBossPlayerMethod = RequireMethod(
            _bossPlayersType,
            "RemoveBossPlayer",
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(bool),
            typeof(string),
            typeof(bool));

        _getFollowerBotMethod = RequireMethod(
            _followerType,
            "GetBot",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(BotOwner));
        _getFollowerBossMethod = RequireMethod(
            _followerType,
            "GetBoss",
            BindingFlags.Public | BindingFlags.Instance,
            _bossType);
        _dismissFollowerMethod = RequireMethod(
            _followerType,
            "Dismiss",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(void),
            typeof(bool));
        _unhookFollowerDeathMethod = RequireMethod(
            _bossType,
            "UnhookFollowerDeath",
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(void),
            typeof(BotOwner));
        _bossRealPlayerField = RequireField(
            _bossType,
            "realPlayer",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(Player));
        _bossFollowersProperty = RequireProperty(
            _bossType,
            "Followers",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(List<BotOwner>));
        _bossGroupProperty = RequireProperty(
            _bossType,
            "bossGroup",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(BotsGroup));

        _botsPatchInstanceField = RequireField(
            _botsPatchType,
            "Instance",
            BindingFlags.Public | BindingFlags.Static,
            _botsPatchType);
        _botsControllerField = RequireField(
            _botsPatchType,
            "Controller",
            BindingFlags.Public | BindingFlags.Static,
            typeof(BotsController));
        _spawnedPlayersField = RequireField(
            _botsPatchType,
            "spawnedPlayers",
            BindingFlags.Public | BindingFlags.Static,
            null);
        if (!typeof(IList).IsAssignableFrom(_spawnedPlayersField.FieldType))
            throw new InvalidOperationException("BotsControllerPatch.spawnedPlayers is not an IList");

        _followerCreationTasksField = RequireField(
            _botsPatchType,
            "followerCreationTask",
            BindingFlags.Public | BindingFlags.Static,
            null);
        if (!typeof(IDictionary).IsAssignableFrom(_followerCreationTasksField.FieldType))
            throw new InvalidOperationException("BotsControllerPatch.followerCreationTask is not an IDictionary");

        _createFollowerProfilesMethod = RequireMethod(
            _botsPatchType,
            "CreateFollowerProfiles",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(Task<Dictionary<string, Profile>>),
            _bossType);
        _spawnGroupBotsMethod = RequireMethod(
            _botsPatchType,
            "SpawnGroupBots",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(Task),
            _bossType);
    }

    private static bool TryValidateBoundFollowersUnsafe(
        IReadOnlyCollection<BotOwner> boundFollowers,
        IReadOnlyDictionary<string, string> canonicalProfilesByAccountId,
        out List<BotOwner> bodies,
        out string reason)
    {
        bodies = new List<BotOwner>();
        reason = string.Empty;
        if (boundFollowers == null
            || boundFollowers.Count != PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount)
        {
            reason = "the completed round did not provide exactly four bound follower bodies";
            return false;
        }

        HashSet<BotOwner> references = new(ReferenceEqualityComparer<BotOwner>.Instance);
        HashSet<string> accounts = new(StringComparer.Ordinal);
        HashSet<string> profiles = new(StringComparer.Ordinal);
        foreach (BotOwner bot in boundFollowers)
        {
            if (ReferenceEquals(bot, null) || !references.Add(bot))
            {
                reason = "the completed round contains a missing or duplicate follower body";
                return false;
            }

            string accountId = bot.Profile?.AccountId?.Trim();
            string profileId = bot.ProfileId?.Trim();
            if (accountId == null
                || !canonicalProfilesByAccountId.TryGetValue(accountId, out string canonicalProfileId)
                || !accounts.Add(accountId)
                || string.IsNullOrWhiteSpace(profileId)
                || !profiles.Add(profileId))
            {
                reason = "the completed round contains a wrong or duplicate follower identity";
                return false;
            }

            if (_runtimeProfilesByAccountId.Count == 0)
            {
                if (!string.Equals(profileId, canonicalProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"account {accountId} does not use its authenticated canonical profile in the first bound epoch";
                    return false;
                }
            }
            else if (!_runtimeProfilesByAccountId.TryGetValue(accountId, out string expectedRuntime)
                     || !string.Equals(profileId, expectedRuntime, StringComparison.Ordinal))
            {
                reason = $"account {accountId} does not use the previous round's bound runtime profile";
                return false;
            }

            bodies.Add(bot);
        }

        if (accounts.Count != PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount)
        {
            reason = "the completed round does not cover all four authenticated accounts";
            return false;
        }

        return true;
    }

    private static bool TryRemapProfilesUnsafe(IDictionary profiles, out string reason)
    {
        reason = string.Empty;
        if (profiles == null
            || profiles.Count != PitFireTeamRoundLifecyclePolicy.RequiredCompanionCount)
        {
            reason = $"PFT prefetched {profiles?.Count ?? 0}/4 follower profiles";
            return false;
        }

        Dictionary<string, Profile> runtimeProfiles = new(StringComparer.Ordinal);
        Dictionary<string, string> runtimeProfileIds = new(StringComparer.Ordinal);
        HashSet<string> batchSourceItemIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> batchRuntimeItemIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> batchRuntimeProfileIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in profiles)
        {
            string accountId = (entry.Key as string)?.Trim();
            if (accountId == null
                || !_canonicalProfilesByAccountId.TryGetValue(accountId, out string canonicalProfileId)
                || entry.Value is not Profile source
                || source.Inventory?.Equipment == null)
            {
                reason = "PFT returned a missing or unauthenticated follower profile";
                return false;
            }

            Item[] equipmentPlayerItems = source.Inventory
                .GetPlayerItems(EPlayerItems.Equipment)
                .ToArray();
            Item[] allPlayerItems = source.Inventory
                .GetPlayerItems(EPlayerItems.All)
                .ToArray();
            if (allPlayerItems.Any(item =>
                    !equipmentPlayerItems.Any(equipment => ReferenceEquals(equipment, item))))
            {
                reason = $"PFT profile {canonicalProfileId} contains non-equipment inventory roots that Arena cannot remap safely";
                return false;
            }

            Item[] sourceItems = source.Inventory.Equipment.GetAllItems().ToArray();
            string[] sourceIds = sourceItems.Select(item => item.Id.ToString()).ToArray();
            if (sourceIds.Any(id => batchRuntimeItemIds.Contains(id)
                                    || !batchSourceItemIds.Add(id)))
            {
                reason = "PFT canonical equipment IDs overlap another source or prepared runtime profile";
                return false;
            }

            Profile runtime = source.Clone();
            if (runtime?.Inventory == null)
            {
                reason = $"PFT profile {canonicalProfileId} could not be cloned";
                return false;
            }

            InventoryEquipment clonedEquipment = source.Inventory.Equipment.CloneItem();
            if (clonedEquipment == null)
            {
                reason = $"PFT profile {canonicalProfileId} equipment could not be cloned";
                return false;
            }

            EquipmentField(runtime.Inventory) = clonedEquipment;
            runtime.Id = CreateUniqueRuntimeProfileIdUnsafe(batchRuntimeProfileIds);
            ProfileInventorySanitizer.Sanitize(runtime);

            Item[] clonedItems = runtime.Inventory.Equipment.GetAllItems().ToArray();
            string[] clonedIds = clonedItems.Select(item => item.Id.ToString()).ToArray();
            if (clonedIds.Any(id => batchSourceItemIds.Contains(id)))
            {
                reason = "fresh runtime equipment reused an item ID from another canonical follower profile";
                return false;
            }
            string[] sourceTemplates = sourceItems
                .Select(item => item.TemplateId.ToString())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            string[] runtimeTemplates = clonedItems
                .Select(item => item.TemplateId.ToString())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (!sourceTemplates.SequenceEqual(runtimeTemplates, StringComparer.Ordinal))
            {
                reason = $"PFT profile {canonicalProfileId} equipment structure changed during runtime cloning";
                return false;
            }

            if (!PitFireTeamRoundLifecyclePolicy.TryValidateRuntimeIdentityRemap(
                    accountId,
                    canonicalProfileId,
                    source.AccountId,
                    source.ProfileId,
                    runtime.AccountId,
                    runtime.ProfileId,
                    sourceIds,
                    clonedIds,
                    HistoricalRuntimeProfileIds,
                    HistoricalRuntimeItemIds,
                    out reason))
            {
                return false;
            }

            if (!batchRuntimeProfileIds.Add(runtime.ProfileId)
                || clonedIds.Any(id => !batchRuntimeItemIds.Add(id)))
            {
                reason = "fresh runtime follower profiles contain duplicate profile or item IDs";
                return false;
            }

            runtimeProfiles.Add(accountId, runtime);
            runtimeProfileIds.Add(accountId, runtime.ProfileId);
        }

        if (runtimeProfiles.Count != _canonicalProfilesByAccountId.Count
            || _canonicalProfilesByAccountId.Keys.Any(account => !runtimeProfiles.ContainsKey(account)))
        {
            reason = "PFT's prefetched profiles do not exactly cover the authenticated four accounts";
            return false;
        }

        // Commit only after every clone passed. Until this point the cached PFT
        // dictionary still contains its untouched canonical Profile objects.
        foreach (var pair in runtimeProfiles)
            profiles[pair.Key] = pair.Value;

        _preparedProfilesByAccountId = runtimeProfiles;
        _runtimeProfilesByAccountId = runtimeProfileIds;
        foreach (string profileId in batchRuntimeProfileIds)
            HistoricalRuntimeProfileIds.Add(profileId);
        foreach (string itemId in batchRuntimeItemIds)
            HistoricalRuntimeItemIds.Add(itemId);

        return true;
    }

    private static string CreateUniqueRuntimeProfileIdUnsafe(
        ISet<string> batchRuntimeProfileIds)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            string candidate = MongoID.Generate(newProcessId: false).ToString();
            if (!HistoricalRuntimeProfileIds.Contains(candidate)
                && !_canonicalProfilesByAccountId.Values.Contains(
                    candidate,
                    StringComparer.OrdinalIgnoreCase)
                && !batchRuntimeProfileIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("could not generate a unique runtime follower profile ID");
    }

    private static void ReplaceSpawnedBossUnsafe(object newBoss, Player currentLeader)
    {
        if (_spawnedPlayersField.GetValue(null) is not IList spawnedPlayers)
            throw new InvalidOperationException("PFT spawnedPlayers list is unavailable");

        bool containsNew = false;
        for (int index = spawnedPlayers.Count - 1; index >= 0; index--)
        {
            object candidate = spawnedPlayers[index];
            if (ReferenceEquals(candidate, newBoss))
            {
                if (containsNew)
                    spawnedPlayers.RemoveAt(index);
                else
                    containsNew = true;
                continue;
            }

            Player candidateLeader = candidate == null
                ? null
                : _bossRealPlayerField.GetValue(candidate) as Player;
            if (ReferenceEquals(candidate, _oldBoss)
                || string.Equals(
                    candidateLeader?.ProfileId,
                    currentLeader.ProfileId,
                    StringComparison.Ordinal))
            {
                spawnedPlayers.RemoveAt(index);
            }
        }

        if (!containsNew)
            spawnedPlayers.Add(newBoss);

        int exact = 0;
        foreach (object candidate in spawnedPlayers)
        {
            if (ReferenceEquals(candidate, newBoss))
                exact++;
            else if (candidate != null
                     && string.Equals(
                         (_bossRealPlayerField.GetValue(candidate) as Player)?.ProfileId,
                         currentLeader.ProfileId,
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PFT spawnedPlayers retained a stale boss for the local profile");
            }
        }

        if (exact != 1)
            throw new InvalidOperationException("PFT spawnedPlayers did not accept exactly one rebuilt boss");
    }

    private static List<object> ReadFollowerWrappersUnsafe()
    {
        if (_getFollowersMethod.Invoke(null, null) is not IEnumerable followers)
            throw new InvalidOperationException("PFT follower collection is unavailable");

        List<object> wrappers = new();
        foreach (object wrapper in followers)
        {
            if (wrapper != null && _followerType.IsInstanceOfType(wrapper))
                wrappers.Add(wrapper);
        }

        return wrappers;
    }

    private static IDictionary ReadProfileTaskCacheUnsafe()
    {
        if (_followerCreationTasksField.GetValue(null) is not IDictionary cache)
            throw new InvalidOperationException("PFT followerCreationTask cache is unavailable");
        return cache;
    }

    private static bool IsActiveBody(BotOwner bot)
        => !ReferenceEquals(bot, null)
           && bot != null
           && !bot.IsDead
           && bot.BotState == EBotState.Active
           && bot.GetPlayer?.HealthController?.IsAlive == true;

    private static bool RequireCapabilityUnsafe(out string reason)
    {
        reason = string.Empty;
        if (_capability.CanStartAdditionalRound)
            return true;

        reason = string.IsNullOrWhiteSpace(_capability.Reason)
            ? "PitFireTeam round lifecycle capability is unavailable"
            : _capability.Reason;
        return false;
    }

    private static bool RequirePersistenceSuppressionUnsafe(
        bool active,
        out string reason)
    {
        reason = string.Empty;
        if (active)
            return true;

        reason = "PitFireTeam persistence suppression is not proven active";
        return false;
    }

    private static bool CheckThreadUnsafe(out string reason)
    {
        reason = string.Empty;
        if (_ownerThreadId == 0
            || Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
        {
            return true;
        }

        reason = "PitFireTeam round lifecycle was called outside its configuring Unity thread";
        return false;
    }

    private static PitFireTeamRoundPolicyDecision FailDecisionUnsafe(string reason)
    {
        FailUnsafe(reason);
        return PitFireTeamRoundPolicyDecision.Failed(reason);
    }

    private static void FailUnsafe(string reason)
    {
        _stage = PitFireTeamRoundLifecycleStage.Failed;
        _diagnostic = string.IsNullOrWhiteSpace(reason)
            ? "PitFireTeam round lifecycle failed closed."
            : reason;
    }

    private static void SetUnavailable(string reason)
    {
        lock (StateLock)
            SetUnavailableUnsafe(reason);
    }

    private static void SetUnavailableUnsafe(string reason)
    {
        _capability = PitFireTeamRoundCapability.Unavailable(reason);
        _stage = PitFireTeamRoundLifecycleStage.Unconfigured;
        _diagnostic = _capability.Reason;
    }

    private static DateTime Deadline(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            timeout = TimeSpan.FromSeconds(1);
        return DateTime.UtcNow.Add(timeout);
    }

    private static void ClearRaidStateUnsafe(bool clearHistory)
    {
        _epoch = 0;
        _deadlineUtc = DateTime.MinValue;
        _oldLeader = null;
        _oldBoss = null;
        _currentLeader = null;
        _currentBoss = null;
        _profileTask = null;
        _spawnTask = null;
        _preparedProfileDictionary = null;
        _retiringBodies.Clear();
        _currentFollowers.Clear();
        _lastStableFollowers.Clear();
        _stableFollowerPasses = 0;
        _canonicalProfilesByAccountId.Clear();
        _runtimeProfilesByAccountId.Clear();
        _preparedProfilesByAccountId.Clear();
        if (clearHistory)
        {
            HistoricalRuntimeProfileIds.Clear();
            HistoricalRuntimeItemIds.Clear();
        }
    }

    private static void ClearContractUnsafe()
    {
        _assembly = null;
        _ownerThreadId = 0;
        _bossType = null;
        _followerType = null;
        _bossPlayersType = null;
        _botsPatchType = null;
        _bossPlayersInstanceProperty = null;
        _getBossMethod = null;
        _getFollowersMethod = null;
        _addPlayerAsBossMethod = null;
        _removeFollowerMethod = null;
        _removeBossPlayerMethod = null;
        _getFollowerBotMethod = null;
        _getFollowerBossMethod = null;
        _dismissFollowerMethod = null;
        _unhookFollowerDeathMethod = null;
        _bossRealPlayerField = null;
        _bossGroupProperty = null;
        _bossFollowersProperty = null;
        _botsPatchInstanceField = null;
        _botsControllerField = null;
        _spawnedPlayersField = null;
        _followerCreationTasksField = null;
        _createFollowerProfilesMethod = null;
        _spawnGroupBotsMethod = null;
    }

    private static Type RequireType(Assembly assembly, string fullName)
        => assembly.GetType(fullName, throwOnError: false)
           ?? throw new TypeLoadException(fullName);

    private static PropertyInfo RequireProperty(
        Type type,
        string name,
        BindingFlags flags,
        Type propertyType)
    {
        PropertyInfo property = type.GetProperty(name, flags)
                                ?? throw new MissingMemberException(type.FullName, name);
        if (property.PropertyType != propertyType || property.GetMethod == null)
            throw new InvalidOperationException($"{type.FullName}.{name} has an unsupported property signature");
        return property;
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        BindingFlags flags,
        Type fieldType)
    {
        FieldInfo field = type.GetField(name, flags)
                          ?? throw new MissingFieldException(type.FullName, name);
        if (fieldType != null && field.FieldType != fieldType)
            throw new InvalidOperationException($"{type.FullName}.{name} has an unsupported field type");
        return field;
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        BindingFlags flags,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo method = type.GetMethod(
            name,
            flags,
            binder: null,
            types: parameterTypes ?? Type.EmptyTypes,
            modifiers: null)
                            ?? throw new MissingMethodException(type.FullName, name);
        if (returnType != null && method.ReturnType != returnType)
            throw new InvalidOperationException($"{type.FullName}.{name} has an unsupported return type");
        return method;
    }

    private static void RequireEnumerableOf(Type sequenceType, Type elementType, string label)
    {
        Type enumerableType = sequenceType
            .GetInterfaces()
            .Concat(new[] { sequenceType })
            .FirstOrDefault(type =>
                type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableType == null || enumerableType.GetGenericArguments()[0] != elementType)
            throw new InvalidOperationException(label + " has an unsupported collection type");
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
}
