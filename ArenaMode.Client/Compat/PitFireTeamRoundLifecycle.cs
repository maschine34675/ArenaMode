using BepInEx.Bootstrap;
using ArenaMode.Combat;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Shared;
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
    // Only for the "same build after re-configuration" comparison below; the
    // accepted set lives in PitFireTeamVersions and is applied on first configure.
    private static string _configuredVersion;

    private const string BossTypeName = "pitTeam.Components.pitAIBossPlayer";
    private const string FollowerTypeName = "pitTeam.Components.BotFollowerPlayer";
    private const string BossPlayersTypeName = "pitTeam.Modules.BossPlayers";
    private const string BotsPatchTypeName = "pitTeam.Patches.BotsControllerPatch";
    private const string ManualUpdatePatchTypeName = "pitTeam.Patches.BotOwnerManualUpdatePatch";
    private const string ActivateBotAtPositionMethodName = "ActivateBotAtPosition";
    private const string FollowersFieldName = "_followers";
    private const string FollowersByProfileIdFieldName = "_followersByProfileId";
    private const string BossControllerFieldName = "_botsController";
    private const string ShallBeFollowerFieldName = "_shallBeFollower";
    private const string ManualUpdateFieldName = "BotOwnerUpdate";

    private static readonly object StateLock = new();
    private static readonly Harmony SpawnLeaseHarmony =
        new(Plugin.PluginGuid + ".pitfireteam-round-spawn-lease");
    private static readonly AccessTools.FieldRef<EftInventory, InventoryEquipment> EquipmentField =
        AccessTools.FieldRefAccess<EftInventory, InventoryEquipment>(nameof(EftInventory.Equipment));

    /// <summary>
    /// Readonly in the native type, so a field reference is the only way to keep
    /// a cloned follower's encyclopedia semantics. See TryPreserveEncyclopediaUnsafe.
    /// </summary>
    private static readonly AccessTools.FieldRef<Profile, Dictionary<MongoID, bool>> EncyclopediaField =
        AccessTools.FieldRefAccess<Profile, Dictionary<MongoID, bool>>(nameof(Profile.Encyclopedia));

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
    private static FieldInfo _followersField;
    private static FieldInfo _followersByProfileIdField;
    private static FieldInfo _bossRealPlayerField;
    private static FieldInfo _bossBotsControllerField;
    private static PropertyInfo _bossFollowersProperty;
    private static PropertyInfo _bossGroupProperty;
    private static FieldInfo _botsPatchInstanceField;
    private static FieldInfo _botsControllerField;
    private static FieldInfo _spawnedPlayersField;
    private static FieldInfo _followerCreationTasksField;
    private static MethodInfo _createFollowerProfilesMethod;
    private static MethodInfo _spawnGroupBotsMethod;
    private static MethodInfo _botCreationDataCreateMethod;
    private static MethodInfo _localGameCreateBotByProfileMethod;
    private static FieldInfo _localGameBotsField;
    private static MethodInfo _activateBotAtPositionMethod;
    private static MethodInfo _shallBeFollowerMethod;
    private static FieldInfo _shallBeFollowerField;
    private static FieldInfo _manualUpdateField;
    private static bool _spawnLeasePatchInstalled;

    private static Player _oldLeader;
    private static string _oldLeaderProfileId;
    private static object _oldBoss;
    private static Player _currentLeader;
    private static object _currentBoss;
    private static Task _profileTask;
    private static bool _profileCacheWasCleanAtTeardown;
    private static bool _adoptedRespawnBoss;
    private static Task _spawnTask;
    private static IDictionary _preparedProfileDictionary;
    private static List<BotOwner> _retiringBodies = new();
    private static List<BotOwner> _currentFollowers = new();
    private static List<BotOwner> _lastStableFollowers = new();
    private static int _stableFollowerPasses;
    private static bool _loggedIncompleteFollowerAssembly;
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
    private static readonly Dictionary<string, HistoricalSpawnRecord> HistoricalSpawnRecords =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<PendingRawPlayerCompletion> PendingRawPlayerCompletions = new();
    private static FollowerSpawnLease _spawnLease;
    [ThreadStatic]
    private static FollowerSpawnLease _spawnGroupInvocationLease;

    /// <summary>
    /// Strong raid references exist only while this Arena-owned spawn is live.
    /// Historical records retain weak spawner references so an arbitrarily late
    /// PFT async lambda can still undo its immediately preceding counter increment
    /// without retaining an ended raid or touching a later raid's BotSpawner.
    /// </summary>
    private sealed class FollowerSpawnLease
    {
        public int Epoch { get; set; }
        public int RaidEpoch { get; set; }
        public object PatchInstance { get; set; }
        public LocalGame Game { get; set; }
        public GameWorld World { get; set; }
        public Player Leader { get; set; }
        public object Boss { get; set; }
        public BotsController Controller { get; set; }
        public BotSpawner Spawner { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
        public Dictionary<string, Profile> ProfilesById { get; set; }
        public HashSet<string> StartedProfileIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ActivatedProfileIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SettledProfileIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BotOwner> ObservedBodiesByProfileId { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpawnInvocationState> InvocationsByProfileId { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public BotCreationInvocationState BotCreationInvocation { get; set; }
        public bool Completed { get; set; }
        public bool Revoked { get; set; }
        public string RevocationReason { get; set; } = string.Empty;
    }

    private sealed class HistoricalSpawnRecord
    {
        public WeakReference<BotSpawner> Spawner { get; set; }
        public WeakReference<Profile> Profile { get; set; }
        public WeakReference<LocalGame> Game { get; set; }
        public WeakReference<GameWorld> World { get; set; }
        public WeakReference<SpawnInvocationState> Invocation { get; set; }
    }

    private sealed class SpawnInvocationState
    {
        public FollowerSpawnLease Lease { get; set; }
        public string ProfileId { get; set; }
        public BotSpawner Spawner { get; set; }
        public BotOwner Body { get; set; }
        public RawPlayerCreationState RawPlayerCreation { get; set; }
        public bool CallbackCompleted { get; set; }
        public int CounterSettled;
    }

    private sealed class SpawnGroupInvocationState
    {
        public FollowerSpawnLease Lease { get; set; }
        public FollowerSpawnLease PreviousLease { get; set; }
        public bool Restored { get; set; }
    }

    private sealed class BotCreationInvocationState
    {
        public FollowerSpawnLease Lease { get; set; }
        public IGetProfileData ProfileData { get; set; }
        public IBotCreator BotCreator { get; set; }
        public ITokenGetter Token { get; set; }
        public SynchronizationContext ContinuationContext { get; set; }
        public Task<BotCreationData> OriginalTask { get; set; }
        public TaskCompletionSource<BotCreationData> Completion { get; } = new();
        public CancellationTokenRegistration CancellationRegistration;
        public int Settled;
    }

    private sealed class RawPlayerCreationState
    {
        public SpawnInvocationState Invocation { get; set; }
        public LocalGame Game { get; set; }
        public GameWorld World { get; set; }
        public GameWorld RequestedWorld { get; set; }
        public Profile Profile { get; set; }
        public IDictionary LocalBots { get; set; }
        public SynchronizationContext ContinuationContext { get; set; }
        public Task<LocalPlayer> OriginalTask { get; set; }
        public TaskCompletionSource<LocalPlayer> Completion { get; } = new();
        public string RejectionReason { get; set; } = string.Empty;
        public int OwnerThreadId { get; set; }
        public int Settled;
    }

    private sealed class PendingRawPlayerCompletion
    {
        public RawPlayerCreationState State { get; set; }
        public LocalPlayer Player { get; set; }
        public Exception Failure { get; set; }
        public int OwnerThreadId { get; set; }
    }

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
                    && string.Equals(pluginVersion, _configuredVersion, StringComparison.Ordinal))
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

            if (!PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion))
            {
                SetUnavailableUnsafe(
                    $"PitFireTeam {pluginVersion ?? "unknown"} is unsupported; exact {PitFireTeamVersions.Describe} is required.");
                return _capability;
            }

            AssemblyName assemblyName = pitFireTeamAssembly.GetName();
            if (!string.Equals(assemblyName.Name, "pitFireTeam", StringComparison.Ordinal)
                || !PitFireTeamVersions.IsSupportedAssemblyVersion(assemblyName.Version))
            {
                SetUnavailableUnsafe(
                    $"PitFireTeam assembly identity '{assemblyName.FullName}' is unsupported; exact pitFireTeam {PitFireTeamVersions.Describe} is required.");
                return _capability;
            }

            try
            {
                DiscoverExactContract(pitFireTeamAssembly);
                _assembly = pitFireTeamAssembly;
                _configuredVersion = pluginVersion;
                _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
                _capability = PitFireTeamRoundCapability.Available();
                ClearRaidStateUnsafe(clearHistory: true);
                _stage = PitFireTeamRoundLifecycleStage.ContractReady;
                _diagnostic = $"PitFireTeam {pluginVersion} round lifecycle contract is ready.";
            }
            catch (Exception ex)
            {
                ClearContractUnsafe();
                SetUnavailableUnsafe(
                    $"PitFireTeam {pluginVersion} round lifecycle contract failed: "
                    + ex.GetBaseException().Message);
            }

            return _capability;
        }
    }

    /// <summary>
    /// Detach the old PFT ownership synchronously, then request ordinary EFT
    /// deaths for any surviving old follower body. PollRetirement owns the later
    /// registry barrier. The caller must provide every body frozen by the
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

            if (!PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(
                    canonicalProfilesByAccountId, out reason))
            {
                FailUnsafe(reason);
                return false;
            }
            if (_stage != PitFireTeamRoundLifecycleStage.ContractReady
                && !PitFireTeamRoundLifecyclePolicy.HasSameCanonicalRoster(
                    _canonicalProfilesByAccountId, canonicalProfilesByAccountId))
            {
                reason = "the authenticated companion roster changed between protected rounds";
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

            if (!TryValidateBoundFollowersUnsafe(
                    boundFollowers,
                    canonicalProfilesByAccountId,
                    out var oldBodies,
                    out reason))
            {
                FailUnsafe(reason);
                return false;
            }

            _profileCacheWasCleanAtTeardown = false;
            try
            {
                IDictionary completedRoundProfileTasks = ReadProfileTaskCacheUnsafe();
                if (completedRoundProfileTasks.Contains(oldLeader.ProfileId))
                {
                    throw new InvalidOperationException(
                        "PFT retained the completed round's follower-profile task");
                }

                _profileCacheWasCleanAtTeardown = true;
            }
            catch (Exception ex)
            {
                Exception root = ex.GetBaseException();
                reason = $"PFT old-round teardown failed during follower-profile cache preflight for {oldLeader.ProfileId}: "
                         + $"{root.GetType().Name}: {root.Message}";
                Plugin.ModLogger.LogError($"Arena {reason}\n{ex}");
                FailUnsafe(reason);
                return false;
            }

            // A ready lease has observed all selected private task returns, all
            // detached callbacks and both physical registry passes. Revoke its
            // IDs before advancing the lifecycle epoch, but let the ordinary
            // teardown below retire the already frozen bodies.
            RevokeFollowerSpawnLeaseUnsafe(
                "the completed round advanced to teardown",
                cleanupBodies: false);

            _epoch++;
            _stage = PitFireTeamRoundLifecycleStage.TearingDown;
            _diagnostic = $"PitFireTeam round epoch {_epoch}: tearing down old ownership.";
            _deadlineUtc = Deadline(timeout);
            _canonicalProfilesByAccountId = canonicalProfilesByAccountId.ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.Ordinal);
            _oldLeader = oldLeader;
            _oldLeaderProfileId = oldLeader.ProfileId;
            _oldBoss = null;
            _currentLeader = null;
            _currentBoss = null;
            _profileTask = null;
            _adoptedRespawnBoss = false;
            _spawnTask = null;
            _preparedProfileDictionary = null;
            _preparedProfilesByAccountId.Clear();
            _currentFollowers.Clear();
            _lastStableFollowers.Clear();
            _stableFollowerPasses = 0;

            string teardownStep = "resolve the old PFT boss";
            string teardownProfileId = oldLeader.ProfileId;
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
                teardownStep = "validate the old boss follower list";
                if (_bossFollowersProperty.GetValue(oldBoss) is not IEnumerable bossFollowers)
                    throw new InvalidOperationException("PFT old boss follower list is unavailable");
                foreach (object follower in bossFollowers)
                {
                    if (follower is not BotOwner bossFollower
                        || !oldBodies.Any(expected => ReferenceEquals(expected, bossFollower)))
                    {
                        throw new InvalidOperationException(
                            "PFT's old boss owns a physical follower outside the frozen body roster");
                    }
                }

                teardownStep = "read the old PFT follower wrappers";
                List<object> wrappers = ReadFollowerWrappersUnsafe()
                    .Where(wrapper => ReferenceEquals(
                        _getFollowerBossMethod.Invoke(wrapper, null),
                        oldBoss))
                    .ToList();

                if (wrappers.Count != oldBodies.Count)
                {
                    throw new InvalidOperationException(
                        $"PFT exposed {wrappers.Count}/{_canonicalProfilesByAccountId.Count} wrappers for the frozen old follower roster");
                }

                HashSet<BotOwner> wrappedBodies =
                    new(ReferenceEqualityComparer<BotOwner>.Instance);
                foreach (object wrapper in wrappers)
                {
                    BotOwner bot = _getFollowerBotMethod.Invoke(wrapper, null) as BotOwner;
                    teardownProfileId = bot?.ProfileId ?? oldLeader.ProfileId;
                    if (ReferenceEquals(bot, null)
                        || !oldBodies.Any(expected => ReferenceEquals(expected, bot))
                        || !wrappedBodies.Add(bot))
                    {
                        throw new InvalidOperationException(
                            "PFT's old boss owns a missing, duplicate or foreign wrapper body");
                    }
                }
                if (oldBodies.Any(body => !wrappedBodies.Contains(body)))
                {
                    throw new InvalidOperationException(
                        "PFT did not expose one exact wrapper for every frozen old follower body");
                }

                object bossPlayers = _bossPlayersInstanceProperty.GetValue(null)
                                     ?? throw new InvalidOperationException("PFT BossPlayers.Instance is unavailable");
                teardownStep = "validate the old PFT follower registry";
                foreach (object wrapper in wrappers)
                {
                    BotOwner bot = (BotOwner)_getFollowerBotMethod.Invoke(wrapper, null);
                    teardownProfileId = bot.ProfileId;
                    ValidateFollowerRegistryEntryUnsafe(bossPlayers, wrapper, bot);
                }

                // Unhook every known body first. Dead wrappers can already be absent
                // from boss.Followers while their delegate remains in the boss map.
                foreach (BotOwner bot in oldBodies)
                {
                    teardownStep = "unhook the old follower death handler";
                    teardownProfileId = bot.ProfileId;
                    _unhookFollowerDeathMethod.Invoke(oldBoss, new object[] { bot });
                }

                // PFT's public per-follower remover assumes BotFollower still exists.
                // EFT nulls it while disposing a dead bot, but PFT retains that bot's
                // wrapper and profile index. Use PFT's normal path for live wrappers;
                // prune an already disposed wrapper from the exact validated stores.
                // Dismiss always follows unlinking, matching PFT's own boss teardown.
                foreach (object wrapper in wrappers)
                {
                    BotOwner bot = (BotOwner)_getFollowerBotMethod.Invoke(wrapper, null);
                    teardownStep = "detach the old follower wrapper";
                    teardownProfileId = bot.ProfileId;
                    DetachFollowerWrapperUnsafe(bossPlayers, oldBoss, wrapper, bot);
                }

                teardownStep = "remove the old PFT boss";
                teardownProfileId = oldLeader.ProfileId;
                if (_removeBossPlayerMethod.Invoke(
                        bossPlayers,
                        new object[] { oldLeader.ProfileId, false }) is not bool removed
                    || !removed)
                {
                    throw new InvalidOperationException(
                        "PFT did not remove the old boss with died=false");
                }

                teardownStep = "verify the old PFT ownership was removed";
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

                if (_getBossMethod.Invoke(null, new object[] { oldLeader.ProfileId }) != null)
                {
                    throw new InvalidOperationException(
                        "PFT retained the old boss after removal with died=false");
                }

                _retiringBodies = oldBodies;
                foreach (BotOwner bot in _retiringBodies)
                {
                    teardownStep = "retire a surviving old follower body";
                    teardownProfileId = bot.ProfileId;
                    var health = bot?.GetPlayer?.ActiveHealthController;
                    if (health != null && health.IsAlive)
                    {
                        using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                            health.Kill(EDamageType.Undefined);
                    }
                }

                _stage = PitFireTeamRoundLifecycleStage.AwaitingRetirement;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: waiting for {_canonicalProfilesByAccountId.Count} old bodies to leave active registries.";
                return true;
            }
            catch (Exception ex)
            {
                Exception root = ex.GetBaseException();
                reason = $"PFT old-round teardown failed during {teardownStep} for {teardownProfileId}: "
                         + $"{root.GetType().Name}: {root.Message}";
                Plugin.ModLogger.LogError($"Arena {reason}\n{ex}");
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

                object existingBoss = _getBossMethod.Invoke(
                    null,
                    new object[] { currentLeader.ProfileId });
                if (existingBoss != null && !_bossType.IsInstanceOfType(existingBoss))
                    throw new InvalidOperationException("PFT returned an unexpected local boss type");

                IDictionary profileTasks = ReadProfileTaskCacheUnsafe();
                bool cachedProfileTaskPresent = profileTasks.Contains(currentLeader.ProfileId);
                object cachedProfileTaskValue = cachedProfileTaskPresent
                    ? profileTasks[currentLeader.ProfileId]
                    : null;
                Task cachedProfileTask = cachedProfileTaskValue as Task;

                int existingFollowerCount = 0;
                int existingWrapperCount = 0;
                bool existingBossGroupPresent = false;
                bool existingBossWrapsCurrentLeader = false;
                bool existingBossUsesCurrentController = false;
                if (existingBoss != null)
                {
                    if (_bossFollowersProperty.GetValue(existingBoss) is not IEnumerable existingFollowers)
                    {
                        throw new InvalidOperationException(
                            "PFT's existing local boss follower list is unavailable");
                    }

                    existingFollowerCount = existingFollowers.Cast<object>().Count();
                    existingWrapperCount = ReadFollowerWrappersUnsafe().Count(wrapper =>
                        ReferenceEquals(_getFollowerBossMethod.Invoke(wrapper, null), existingBoss));
                    existingBossGroupPresent = _bossGroupProperty.GetValue(existingBoss) != null;
                    existingBossWrapsCurrentLeader = ReferenceEquals(_bossRealPlayerField.GetValue(existingBoss), currentLeader);
                    existingBossUsesCurrentController = ReferenceEquals(_bossBotsControllerField.GetValue(existingBoss), botsController);
                }

                bool isExactReplacementBody = !ReferenceEquals(_oldLeader, null)
                                              && !ReferenceEquals(currentLeader, _oldLeader)
                                              && string.Equals(
                                                  currentLeader.ProfileId,
                                                  _oldLeaderProfileId,
                                                  StringComparison.Ordinal);
                PitFireTeamLeaderBindingPolicyDecision leaderDecision =
                    PitFireTeamRoundLifecyclePolicy.EvaluateLeaderBinding(
                        new PitFireTeamLeaderBindingObservation
                        {
                            ProfileCacheWasCleanAtTeardown = _profileCacheWasCleanAtTeardown,
                            ExistingBossPresent = existingBoss != null,
                            IsExactReplacementBody = isExactReplacementBody,
                            BossWrapsCurrentLeader = existingBossWrapsCurrentLeader,
                            BossUsesCurrentController = existingBossUsesCurrentController,
                            BossFollowerCount = existingFollowerCount,
                            BossWrapperCount = existingWrapperCount,
                            BossGroupPresent = existingBossGroupPresent,
                            CachedProfileTaskPresent = cachedProfileTaskPresent,
                            CachedProfileTaskIsTask = cachedProfileTask != null
                        });
                if (leaderDecision.Decision == PitFireTeamLeaderBindingDecision.Failed)
                    throw new InvalidOperationException(leaderDecision.Reason);

                bool adoptedRespawnBoss =
                    leaderDecision.Decision == PitFireTeamLeaderBindingDecision.Adopt;
                object newBoss;
                if (adoptedRespawnBoss)
                {
                    newBoss = existingBoss;
                }
                else
                {
                    newBoss = _addPlayerAsBossMethod.Invoke(
                        null,
                        new object[] { currentLeader, botsController });
                }

                if (newBoss == null
                    || !ReferenceEquals(_bossRealPlayerField.GetValue(newBoss), currentLeader)
                    || !ReferenceEquals(_bossBotsControllerField.GetValue(newBoss), botsController)
                    || !ReferenceEquals(
                        _getBossMethod.Invoke(null, new object[] { currentLeader.ProfileId }),
                        newBoss))
                {
                    throw new InvalidOperationException(
                        "PFT did not expose one boss wrapper around the exact current local body");
                }

                if (_bossFollowersProperty.GetValue(newBoss) is not IEnumerable newBossFollowers
                    || newBossFollowers.Cast<object>().Any()
                    || _bossGroupProperty.GetValue(newBoss) != null
                    || ReadFollowerWrappersUnsafe().Any(wrapper => ReferenceEquals(
                        _getFollowerBossMethod.Invoke(wrapper, null),
                        newBoss)))
                {
                    throw new InvalidOperationException(
                        "PFT's current local boss was not empty before follower preparation");
                }

                ReplaceSpawnedBossUnsafe(newBoss, currentLeader);
                profileTasks = ReadProfileTaskCacheUnsafe();
                if (cachedProfileTask != null)
                {
                    if (!profileTasks.Contains(currentLeader.ProfileId)
                        || !ReferenceEquals(profileTasks[currentLeader.ProfileId], cachedProfileTask))
                    {
                        throw new InvalidOperationException(
                            "PFT replaced or removed the exact auto-prefetched replacement task during leader adoption");
                    }
                }
                else if (profileTasks.Contains(currentLeader.ProfileId))
                {
                    throw new InvalidOperationException(
                        "PFT follower-profile task state changed during leader binding");
                }

                _currentLeader = currentLeader;
                _currentBoss = newBoss;
                _adoptedRespawnBoss = adoptedRespawnBoss;
                if (adoptedRespawnBoss)
                    _profileTask = cachedProfileTask;
                else
                    _profileTask = null;
                _stage = PitFireTeamRoundLifecycleStage.LeaderReady;
                _diagnostic = adoptedRespawnBoss
                    ? cachedProfileTask == null
                        ? $"PitFireTeam round epoch {_epoch}: adopted PFT's exact respawn-created leader wrapper."
                        : $"PitFireTeam round epoch {_epoch}: adopted PFT's exact respawn-created leader wrapper and profile task."
                    : $"PitFireTeam round epoch {_epoch}: rebuilt the leader wrapper.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "PFT leader binding failed: " + ex.GetBaseException().Message;
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
                Task task;
                bool adoptedAutoPrefetch = _adoptedRespawnBoss && _profileTask != null;
                if (_adoptedRespawnBoss && _profileTask != null)
                {
                    if (!cache.Contains(_currentLeader.ProfileId)
                        || !ReferenceEquals(cache[_currentLeader.ProfileId], _profileTask))
                    {
                        throw new InvalidOperationException(
                            "PFT replaced or removed the adopted replacement follower-profile task");
                    }

                    task = _createFollowerProfilesMethod.Invoke(
                        patchInstance,
                        new[] { _currentBoss }) as Task;
                    if (task == null || !ReferenceEquals(task, _profileTask))
                    {
                        throw new InvalidOperationException(
                            "PFT did not return the exact auto-prefetched replacement task");
                    }
                }
                else
                {
                    if (cache.Contains(_currentLeader.ProfileId))
                    {
                        throw new InvalidOperationException(
                            "PFT retained a stale follower-profile task for the local profile");
                    }

                    task = _createFollowerProfilesMethod.Invoke(
                        patchInstance,
                        new[] { _currentBoss }) as Task;
                    if (task == null)
                        throw new InvalidOperationException("PFT returned no follower-profile task");
                }

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
                _diagnostic = adoptedAutoPrefetch
                    ? $"PitFireTeam round epoch {_epoch}: waiting for PFT's exact respawn-prefetched follower profiles."
                    : $"PitFireTeam round epoch {_epoch}: waiting for {_canonicalProfilesByAccountId.Count} canonical follower profiles.";
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
                if (!_spawnLeasePatchInstalled || _activateBotAtPositionMethod == null)
                {
                    throw new InvalidOperationException(
                        "Arena's exact PitFireTeam activation lease patch is unavailable");
                }

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
                _diagnostic = $"PitFireTeam round epoch {_epoch}: {_canonicalProfilesByAccountId.Count} fresh runtime profiles are ready before activation.";
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
                if (patchInstance == null)
                    throw new InvalidOperationException("PFT BotsControllerPatch.Instance is unavailable");
                if (_spawnLease != null)
                    throw new InvalidOperationException("a previous Arena follower spawn lease is still active");
                if (!Singleton<IBotGame>.Instantiated
                    || Singleton<IBotGame>.Instance is not LocalGame game
                    || game.Status != GameStatus.Started)
                {
                    throw new InvalidOperationException(
                        "the exact active LocalGame is unavailable for the follower spawn lease");
                }
                if (!Singleton<GameWorld>.Instantiated
                    || Singleton<GameWorld>.Instance is not GameWorld world
                    || !ReferenceEquals(world.MainPlayer, _currentLeader)
                    || !ReferenceEquals(game.LocalPlayer, _currentLeader))
                {
                    throw new InvalidOperationException(
                        "the exact active GameWorld/local leader binding is unavailable for the follower spawn lease");
                }
                if (_botsControllerField.GetValue(null) is not BotsController controller
                    || !ReferenceEquals(((IBotGame)game).BotsController, controller)
                    || controller.BotSpawner == null)
                {
                    throw new InvalidOperationException(
                        "PFT, LocalGame and Arena do not share one exact BotsController/BotSpawner");
                }

                int raidEpoch = ArenaRespawnDriver.CaptureRaidEpoch();
                if (!ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)
                    || !ArenaSession.IsExactActiveRaid(game, world))
                {
                    throw new InvalidOperationException(
                        "the Arena raid epoch is no longer active while opening the follower spawn lease");
                }

                Dictionary<string, Profile> profilesById = _preparedProfilesByAccountId.Values
                    .ToDictionary(profile => profile.ProfileId, profile => profile,
                        StringComparer.OrdinalIgnoreCase);
                if (!PitFireTeamRoundLifecyclePolicy.IsSupportedCompanionCount(_canonicalProfilesByAccountId.Count)
                    || profilesById.Count != _canonicalProfilesByAccountId.Count)
                {
                    throw new InvalidOperationException(
                        "the follower spawn lease does not cover every authenticated remapped runtime profile");
                }

                var lease = new FollowerSpawnLease
                {
                    Epoch = _epoch,
                    RaidEpoch = raidEpoch,
                    PatchInstance = patchInstance,
                    Game = game,
                    World = world,
                    Leader = _currentLeader,
                    Boss = _currentBoss,
                    Controller = controller,
                    Spawner = controller.BotSpawner,
                    Cancellation = new CancellationTokenSource(),
                    ProfilesById = profilesById
                };
                if (!TryGetExactBotCreatorBindingUnsafe(lease, out _, out string creatorProblem))
                {
                    throw new InvalidOperationException(
                        "the follower spawn lease has no exact LocalGame BotCreator binding: "
                        + creatorProblem);
                }
                foreach (string profileId in profilesById.Keys)
                {
                    HistoricalSpawnRecords[profileId] = new HistoricalSpawnRecord
                    {
                        Spawner = new WeakReference<BotSpawner>(lease.Spawner),
                        Profile = new WeakReference<Profile>(profilesById[profileId]),
                        Game = new WeakReference<LocalGame>(lease.Game),
                        World = new WeakReference<GameWorld>(lease.World)
                    };
                }

                _spawnLease = lease;
                _deadlineUtc = Deadline(timeout);
                _lastStableFollowers.Clear();
                _stableFollowerPasses = 0;
                _loggedIncompleteFollowerAssembly = false;
                _stage = PitFireTeamRoundLifecycleStage.SpawnRequested;
                Task spawnTask = _spawnGroupBotsMethod.Invoke(
                    patchInstance,
                    new[] { _currentBoss }) as Task;
                if (spawnTask == null)
                    throw new InvalidOperationException("PFT returned no SpawnGroupBots task");

                if (_stage != PitFireTeamRoundLifecycleStage.SpawnRequested
                    || !ReferenceEquals(_spawnLease, lease)
                    || lease.Revoked)
                {
                    reason = !string.IsNullOrWhiteSpace(lease.RevocationReason)
                        ? lease.RevocationReason
                        : !string.IsNullOrWhiteSpace(_diagnostic)
                            ? _diagnostic
                            : "the PFT SpawnGroupBots call invalidated Arena's exact follower lease before returning";
                    if (_stage != PitFireTeamRoundLifecycleStage.Failed)
                        FailUnsafe(reason);
                    return false;
                }

                _spawnTask = spawnTask;
                _diagnostic = $"PitFireTeam round epoch {_epoch}: leased {_canonicalProfilesByAccountId.Count} individual follower activations; polling physical wrappers.";
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

            // The canonical snapshot owns cardinality; a partial physical roster
            // must never redefine what "complete" means after a follower loss.
            int expected = _canonicalProfilesByAccountId.Count;
            if (!PitFireTeamRoundLifecyclePolicy.IsSupportedCompanionCount(expected))
                return FailDecisionUnsafe("the frozen authenticated companion roster is unavailable");

            bool spawnFaulted = _spawnTask == null || _spawnTask.IsCanceled || _spawnTask.IsFaulted;
            string spawnProblem = _spawnTask?.Exception?.GetBaseException().Message;

            try
            {
                List<BotOwner> candidates = new();
                HashSet<string> accounts = new(StringComparer.Ordinal);
                HashSet<string> profiles = new(StringComparer.Ordinal);
                bool invalid = false;
                string invalidProblem = string.Empty;

                bool leasedActivationsComplete = wasReady;
                if (!wasReady)
                {
                    FollowerSpawnLease lease = _spawnLease;
                    if (lease == null
                        || lease.Revoked
                        || lease.Epoch != _epoch
                        || !ReferenceEquals(lease.World, world)
                        || !HasExactLeaseRosterUnsafe(lease))
                    {
                        invalid = true;
                        invalidProblem = "the exact Arena follower spawn lease disappeared before binding";
                    }
                    else
                    {
                        if (lease.StartedProfileIds.Count > expected
                            || lease.ActivatedProfileIds.Count > expected
                            || lease.SettledProfileIds.Count > expected
                            || lease.InvocationsByProfileId.Count > expected)
                        {
                            invalid = true;
                            invalidProblem = $"the Arena follower spawn lease observed more than {expected} activations";
                        }
                        else
                        {
                            leasedActivationsComplete = lease.StartedProfileIds.Count == expected
                                                        && lease.ActivatedProfileIds.Count == expected
                                                        && lease.SettledProfileIds.Count == expected
                                                        && lease.InvocationsByProfileId.Count == expected
                                                        && lease.InvocationsByProfileId.Values.All(
                                                            invocation => invocation.CallbackCompleted
                                                                          && Volatile.Read(ref invocation.CounterSettled) == 1);
                            if (!leasedActivationsComplete && utcNow >= _deadlineUtc)
                            {
                                spawnFaulted = true;
                                spawnProblem =
                                    $"Arena follower activation lease timed out (started={lease.StartedProfileIds.Count}, callbacks={lease.ActivatedProfileIds.Count}, settled={lease.SettledProfileIds.Count})";
                            }
                        }
                    }
                }

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

                // PFT's activation callback only installs a BotOwnerUpdate
                // watcher. A later UpdateManual publishes each wrapper and its
                // boss/group ownership, so an exact partial set is still a
                // normal pending state even after all activation tasks settled.
                bool pftOwnershipComplete = false;
                if (!invalid)
                {
                    if (_bossFollowersProperty.GetValue(_currentBoss) is not IEnumerable bossFollowers)
                    {
                        invalid = true;
                        invalidProblem = "the current PFT boss follower list is unavailable";
                    }
                    else
                    {
                        List<BotOwner> ownedBodies = bossFollowers.Cast<object>()
                            .Select(item => item as BotOwner)
                            .ToList();
                        if (ownedBodies.Count > expected
                            || ownedBodies.Any(item => ReferenceEquals(item, null))
                            || PitFireTeamRoundLifecyclePolicy.HasDuplicateReferences(ownedBodies)
                            || ownedBodies.Any(item => !candidates.Any(candidate => ReferenceEquals(candidate, item)))
                            || candidates.Any(candidate => !ownedBodies.Any(item => ReferenceEquals(item, candidate))))
                        {
                            invalid = true;
                            invalidProblem = "the current PFT boss owns bodies that contradict its published follower wrappers";
                        }
                        else
                        {
                            BotsGroup bossGroup = _bossGroupProperty.GetValue(_currentBoss) as BotsGroup;
                            if (candidates.Count > 0 && bossGroup == null)
                            {
                                invalid = true;
                                invalidProblem = "the current PFT boss group is unavailable for a published follower";
                            }
                            else if (bossGroup != null)
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

                            pftOwnershipComplete = !invalid
                                                   && leasedActivationsComplete
                                                   && ownedBodies.Count == expected
                                                   && candidates.Count == expected
                                                   && bossGroup != null;
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
                                          && leasedActivationsComplete
                                          && pftOwnershipComplete
                                          && candidates.Count == expected
                                          && activeCount == expected
                                          && registeredCount == expected;
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

                bool settledUnarmed = false;
                string incompleteDetail = wasReady
                    ? string.Empty
                    : DescribeIncompleteFollowerAssemblyUnsafe(
                        _spawnLease,
                        profiles,
                        out settledUnarmed);
                // The detail still travels with a timeout regardless; this warning
                // is only for the shape nothing native will resolve on its own.
                if (!wasReady
                    && leasedActivationsComplete
                    && settledUnarmed
                    && !string.IsNullOrWhiteSpace(incompleteDetail)
                    && !_loggedIncompleteFollowerAssembly)
                {
                    _loggedIncompleteFollowerAssembly = true;
                    Plugin.ModLogger.LogWarning(
                        $"PitFireTeam follower assembly stalled with an unarmed body: {incompleteDetail}");
                }

                PitFireTeamRoundPolicyDecision decision =
                    PitFireTeamRoundLifecyclePolicy.EvaluateFollowers(
                        new PitFireTeamFollowerObservation
                        {
                            ExpectedCount = expected,
                            ActivationLeaseComplete = leasedActivationsComplete,
                            PhysicalOwnershipComplete = pftOwnershipComplete,
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
                            TimedOut = utcNow >= _deadlineUtc,
                            IncompleteDetail = incompleteDetail
                        });

                _diagnostic = decision.Reason;
                if (decision.Decision == PitFireTeamRoundPollDecision.Ready)
                {
                    _currentFollowers = _lastStableFollowers.ToList();
                    if (_spawnLease == null
                        || _spawnLease.Revoked
                        || _spawnLease.Epoch != _epoch
                        || !HasExactLeaseRosterUnsafe(_spawnLease)
                        || _spawnLease.StartedProfileIds.Count != expected
                        || _spawnLease.ActivatedProfileIds.Count != expected
                        || _spawnLease.SettledProfileIds.Count != expected)
                    {
                        return FailDecisionUnsafe(
                            "the physical follower barrier passed without one complete exact activation lease");
                    }

                    _spawnLease.Completed = true;
                    _stage = PitFireTeamRoundLifecycleStage.FollowersReady;
                    _diagnostic = $"PitFireTeam round epoch {_epoch}: {expected} fresh followers are stable and registered.";
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
    /// Idempotent fail-closed boundary for Tactical transition failure and raid
    /// end. Every started detached activation loses its token, its outstanding
    /// BotSpawner counter is settled once, and any body already delivered by EFT
    /// is removed through the exact old raid objects captured by the lease.
    /// </summary>
    public static void CancelFollowerSpawnLease(string reason)
    {
        lock (StateLock)
        {
            RevokeFollowerSpawnLeaseUnsafe(
                string.IsNullOrWhiteSpace(reason)
                    ? "the protected team transition ended"
                    : reason,
                cleanupBodies: true);
        }
    }

    /// <summary>
    /// Completes the rare fallback queued when Unity's captured synchronization
    /// context rejects Post after the raw LocalPlayer task has already returned.
    /// The queue intentionally survives raid Reset and contract rollback: its
    /// exact old-game/player references exist only until this next owner-thread
    /// tick can reject and retire that Arena-owned body.
    /// </summary>
    internal static void TickPendingRawPlayerCompletions()
    {
        while (true)
        {
            PendingRawPlayerCompletion pending;
            lock (StateLock)
            {
                if (PendingRawPlayerCompletions.Count == 0)
                    return;

                pending = PendingRawPlayerCompletions.Peek();
                if (pending.OwnerThreadId == 0
                    || Thread.CurrentThread.ManagedThreadId != pending.OwnerThreadId)
                {
                    return;
                }

                PendingRawPlayerCompletions.Dequeue();
            }

            CompleteLeasedRawLocalPlayerTaskSafely(
                pending.State,
                pending.Player,
                pending.Failure);
        }
    }

    /// <summary>
    /// Roll back the separately owned dynamic patch when the encompassing exact
    /// PitFireTeam contract rejects initialization after lifecycle discovery.
    /// </summary>
    internal static void RollbackConfiguration(string reason)
    {
        lock (StateLock)
        {
            RevokeFollowerSpawnLeaseUnsafe(
                "the PitFireTeam compatibility contract rolled back",
                cleanupBodies: true);
            ClearRaidStateUnsafe(clearHistory: false);
            ClearContractUnsafe();
            SetUnavailableUnsafe(
                "PitFireTeam round lifecycle configuration rolled back: "
                + (string.IsNullOrWhiteSpace(reason) ? "unknown compatibility failure" : reason));
        }
    }

    /// <summary>
    /// Idempotently forget only Arena's round-local state. PFT owns its raid-stop
    /// cleanup; Reset never edits PFT collections or destroys world objects.
    /// </summary>
    public static void Reset()
    {
        lock (StateLock)
        {
            RevokeFollowerSpawnLeaseUnsafe(
                "the PitFireTeam Arena lifecycle reset",
                cleanupBodies: true);
            // Runtime IDs remain permanently revoked for this process. PFT's
            // detached List.ForEach(async ...) work can resume after GameEnd;
            // forgetting those IDs would let it publish into a later ordinary raid.
            ClearRaidStateUnsafe(clearHistory: false);
            if (_capability.CanStartAdditionalRound)
            {
                _stage = PitFireTeamRoundLifecycleStage.ContractReady;
                _diagnostic = $"PitFireTeam {_configuredVersion} round lifecycle contract is ready.";
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

    private static void RevokeFollowerSpawnLeaseUnsafe(
        string reason,
        bool cleanupBodies)
    {
        FollowerSpawnLease lease = _spawnLease;
        if (lease == null)
            return;

        if (!cleanupBodies && !lease.Completed)
        {
            cleanupBodies = true;
            reason = (reason ?? string.Empty)
                     + "; an incomplete lease cannot release its bodies to ordinary teardown";
        }

        if (!lease.Revoked)
        {
            lease.Revoked = true;
            lease.RevocationReason = reason ?? string.Empty;
            try
            {
                lease.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A fully completed lease may already have released its CTS.
            }
        }

        // PFT increments immediately before each private activation call. Its
        // detached BotCreator callback is the only normal decrement. Cancellation
        // can suppress that callback, so Arena settles only invocations whose
        // wrapped PFT callback has not already settled the same counter.
        foreach (SpawnInvocationState invocation in lease.InvocationsByProfileId.Values)
        {
            if (!invocation.CallbackCompleted)
                SettleSpawnCounterUnsafe(invocation);
        }

        if (cleanupBodies)
            CleanupRuntimeProfilesUnsafe(lease.ProfilesById.Keys, lease);
        else
        {
            foreach (string profileId in lease.ProfilesById.Keys)
                RemovePftBookkeepingUnsafe(profileId);
        }

        _spawnLease = null;
        Plugin.ModLogger.LogInfo(
            $"Arena revoked PitFireTeam follower spawn lease {lease.Epoch} ({reason}).");
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
        _followersField = RequireField(
            _bossPlayersType,
            FollowersFieldName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(List<>).MakeGenericType(_followerType));
        _followersByProfileIdField = RequireField(
            _bossPlayersType,
            FollowersByProfileIdFieldName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(Dictionary<,>).MakeGenericType(typeof(string), _followerType));
        _bossRealPlayerField = RequireField(
            _bossType,
            "realPlayer",
            BindingFlags.Public | BindingFlags.Instance,
            typeof(Player));
        _bossBotsControllerField = RequireField(
            _bossType,
            BossControllerFieldName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(BotsController));
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
        _botCreationDataCreateMethod = RequireMethod(
            typeof(BotCreationData),
            nameof(BotCreationData.Create),
            BindingFlags.Public | BindingFlags.Static,
            typeof(Task<BotCreationData>),
            typeof(IGetProfileData),
            typeof(IBotCreator),
            typeof(int),
            typeof(ITokenGetter));
        if (!_botCreationDataCreateMethod.GetParameters()
                .Select(parameter => parameter.Name)
                .SequenceEqual(new[]
                {
                    "profileData",
                    "botCreator",
                    "count",
                    "token"
                }, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "BotCreationData.Create is not the exact SPT 4.1 creation seam used by PitFireTeam 0.10.0");
        }
        _localGameCreateBotByProfileMethod = RequireMethod(
            typeof(LocalGame),
            nameof(LocalGame.CreateBotByProfile),
            BindingFlags.Public | BindingFlags.Instance,
            typeof(Task<LocalPlayer>),
            typeof(GameWorld),
            typeof(Profile),
            typeof(UnityEngine.Vector3));
        if (!_localGameCreateBotByProfileMethod.GetParameters()
                .Select(parameter => parameter.Name)
                .SequenceEqual(new[]
                {
                    "gw",
                    "profile",
                    "position"
                }, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "LocalGame.CreateBotByProfile is not the exact SPT 4.1 local-player creation seam");
        }
        _localGameBotsField = RequireField(
            typeof(LocalGame),
            "_bots",
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(Dictionary<string, Player>));

        _shallBeFollowerField = RequireField(
            _bossPlayersType,
            ShallBeFollowerFieldName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(List<string>));
        _shallBeFollowerMethod = RequireMethod(
            _bossPlayersType,
            "ShallBeFollower",
            BindingFlags.Public | BindingFlags.Static,
            typeof(void),
            typeof(string));
        Type manualUpdatePatchType = RequireType(assembly, ManualUpdatePatchTypeName);
        _manualUpdateField = RequireField(
            manualUpdatePatchType,
            ManualUpdateFieldName,
            BindingFlags.Public | BindingFlags.Static,
            typeof(Dictionary<string, Action<BotOwner>>));

        _activateBotAtPositionMethod = RequireMethod(
            _botsPatchType,
            ActivateBotAtPositionMethodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            typeof(Task<bool>),
            typeof(BotCreatorClient),
            typeof(Profile),
            typeof(PositionNote),
            typeof(BotZone),
            typeof(bool),
            typeof(Func<BotOwner, BotZone, BotsGroup>),
            typeof(Action<BotOwner>),
            typeof(CancellationToken));
        if (!_activateBotAtPositionMethod.IsPrivate
            || !_activateBotAtPositionMethod.GetParameters()
                .Select(parameter => parameter.Name)
                .SequenceEqual(new[]
                {
                    "botCreator",
                    "profile",
                    "position",
                    "zone",
                    "shallBeGroup",
                    "GroupAction",
                    "OnActivate",
                    "token"
                }, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "BotsControllerPatch.ActivateBotAtPosition is not the exact private 0.10.0 activation seam");
        }

        MethodInfo spawnPrefix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(ActivateBotAtPositionPrefix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(ActivateBotAtPositionPrefix));
        MethodInfo spawnPostfix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(ActivateBotAtPositionPostfix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(ActivateBotAtPositionPostfix));
        MethodInfo markerPrefix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(ShallBeFollowerPrefix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(ShallBeFollowerPrefix));
        MethodInfo groupPrefix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(SpawnGroupBotsPrefix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(SpawnGroupBotsPrefix));
        MethodInfo groupPostfix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(SpawnGroupBotsPostfix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(SpawnGroupBotsPostfix));
        MethodInfo groupFinalizer = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(SpawnGroupBotsFinalizer),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(SpawnGroupBotsFinalizer));
        MethodInfo creationPostfix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(BotCreationDataCreatePostfix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(BotCreationDataCreatePostfix));
        MethodInfo rawPlayerPostfix = typeof(PitFireTeamRoundLifecycle).GetMethod(
            nameof(LocalGameCreateBotByProfilePostfix),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(PitFireTeamRoundLifecycle).FullName,
                nameof(LocalGameCreateBotByProfilePostfix));

        try
        {
            SpawnLeaseHarmony.Patch(
                _botCreationDataCreateMethod,
                postfix: new HarmonyMethod(creationPostfix));
            SpawnLeaseHarmony.Patch(
                _spawnGroupBotsMethod,
                prefix: new HarmonyMethod(groupPrefix),
                postfix: new HarmonyMethod(groupPostfix),
                finalizer: new HarmonyMethod(groupFinalizer));
            SpawnLeaseHarmony.Patch(
                _localGameCreateBotByProfileMethod,
                postfix: new HarmonyMethod(rawPlayerPostfix));
            SpawnLeaseHarmony.Patch(
                _shallBeFollowerMethod,
                prefix: new HarmonyMethod(markerPrefix));
            SpawnLeaseHarmony.Patch(
                _activateBotAtPositionMethod,
                prefix: new HarmonyMethod(spawnPrefix),
                postfix: new HarmonyMethod(spawnPostfix));
            _spawnLeasePatchInstalled = true;
        }
        catch
        {
            SpawnLeaseHarmony.UnpatchSelf();
            _spawnLeasePatchInstalled = false;
            throw;
        }
    }

    /// <summary>
    /// Marks only the exact Arena-owned SpawnGroupBots call while its async
    /// state machine is executing synchronously. PFT reaches the count-zero
    /// BotCreationData.Create call before its first incomplete await because
    /// Arena has already supplied the completed CreateFollowerProfiles task.
    /// </summary>
    private static bool SpawnGroupBotsPrefix(
        object __instance,
        object player,
        ref Task __result,
        out SpawnGroupInvocationState __state)
    {
        __state = null;
        lock (StateLock)
        {
            FollowerSpawnLease lease = _spawnLease;
            if (lease == null
                || !ReferenceEquals(lease.PatchInstance, __instance)
                || !ReferenceEquals(lease.Boss, player))
            {
                return true;
            }

            string problem = string.Empty;
            if (_spawnGroupInvocationLease != null
                || !TryValidateBotCreationLeaseIdentityUnsafe(lease, out problem))
            {
                string reason = _spawnGroupInvocationLease != null
                    ? "a nested PFT SpawnGroupBots call entered Arena's creation lease"
                    : "the PFT SpawnGroupBots creation lease drifted before entry: " + problem;
                FailUnsafe(reason);
                __result = Task.FromException(new InvalidOperationException(reason));
                return false;
            }

            __state = new SpawnGroupInvocationState
            {
                Lease = lease,
                PreviousLease = _spawnGroupInvocationLease
            };
            _spawnGroupInvocationLease = lease;
            return true;
        }
    }

    private static void SpawnGroupBotsPostfix(
        ref Task __result,
        SpawnGroupInvocationState __state)
    {
        string problem = RestoreSpawnGroupInvocation(__state);
        if (string.IsNullOrEmpty(problem))
            return;

        __result = Task.FromException(new InvalidOperationException(problem));
    }

    private static Exception SpawnGroupBotsFinalizer(
        Exception __exception,
        SpawnGroupInvocationState __state)
    {
        RestoreSpawnGroupInvocation(__state);
        return __exception;
    }

    private static string RestoreSpawnGroupInvocation(SpawnGroupInvocationState state)
    {
        if (state == null || state.Restored)
            return string.Empty;

        lock (StateLock)
        {
            if (state.Restored)
                return string.Empty;

            state.Restored = true;
            bool markerChanged = !ReferenceEquals(_spawnGroupInvocationLease, state.Lease);
            _spawnGroupInvocationLease = state.PreviousLease;

            if (markerChanged)
            {
                const string markerProblem =
                    "Arena's exact PFT SpawnGroupBots invocation marker changed before return";
                if (ReferenceEquals(_spawnLease, state.Lease) && !state.Lease.Revoked)
                    FailUnsafe(markerProblem);
                return markerProblem;
            }

            if (state.Lease.BotCreationInvocation != null)
                return string.Empty;

            const string captureProblem =
                "PFT SpawnGroupBots returned without the exact count-zero BotCreationData.Create lease";
            if (ReferenceEquals(_spawnLease, state.Lease) && !state.Lease.Revoked)
                FailUnsafe(captureProblem);
            return captureProblem;
        }
    }

    /// <summary>
    /// Replaces only PFT 0.10.0's exact count-zero creation task while the
    /// Arena-owned SpawnGroupBots invocation marker is live. Every other EFT or
    /// PFT BotCreationData.Create call receives its original Task unchanged.
    /// </summary>
    private static void BotCreationDataCreatePostfix(
        IGetProfileData profileData,
        IBotCreator botCreator,
        int count,
        ITokenGetter token,
        ref Task<BotCreationData> __result)
    {
        FollowerSpawnLease markedLease = _spawnGroupInvocationLease;
        if (markedLease == null)
            return;

        Task<BotCreationData> originalTask = __result;
        BotCreationInvocationState state = null;
        string problem;
        lock (StateLock)
        {
            if (!ReferenceEquals(markedLease, _spawnGroupInvocationLease))
                return;

            if (markedLease.BotCreationInvocation != null)
            {
                problem = "PFT made more than one BotCreationData.Create call inside Arena's SpawnGroupBots lease";
            }
            else if (!TryValidateExactBotCreationCallUnsafe(
                         markedLease,
                         profileData,
                         botCreator,
                         count,
                         token,
                         out problem))
            {
                problem = "PFT count-zero BotCreationData.Create contract drifted: " + problem;
            }
            else if (originalTask == null)
            {
                problem = "PFT's count-zero BotCreationData.Create returned no Task";
            }
            else
            {
                state = new BotCreationInvocationState
                {
                    Lease = markedLease,
                    ProfileData = profileData,
                    BotCreator = botCreator,
                    Token = token,
                    ContinuationContext = SynchronizationContext.Current,
                    OriginalTask = originalTask
                };
                markedLease.BotCreationInvocation = state;
                problem = string.Empty;
            }

            if (state == null
                && ReferenceEquals(_spawnLease, markedLease)
                && !markedLease.Revoked)
            {
                FailUnsafe(problem);
            }
        }

        if (state == null)
        {
            __result = RejectPftBotCreationTask(originalTask, problem);
            return;
        }

        try
        {
            __result = LeasePftBotCreationTask(state);
        }
        catch (Exception ex)
        {
            string setupProblem =
                "Arena could not install PFT's count-zero creation-task lease: "
                + ex.GetBaseException().Message;
            lock (StateLock)
            {
                if (ReferenceEquals(_spawnLease, markedLease) && !markedLease.Revoked)
                    FailUnsafe(setupProblem);
            }
            __result = RejectPftBotCreationTask(originalTask, setupProblem);
        }
    }

    private static Task<BotCreationData> LeasePftBotCreationTask(
        BotCreationInvocationState state)
    {
        // Keep synchronous Task continuations enabled. The observer yields once
        // before releasing a potentially already-completed count-zero result,
        // so Harmony can first return this still-open replacement task to PFT.
        // Its registered continuation then enters all selected activation prefixes
        // under StateLock before the successful release finishes.
        state.CancellationRegistration = state.Lease.Cancellation.Token.Register(
            () => CancelLeasedBotCreationTask(state));
        _ = ObserveLeasedBotCreationTask(state);
        return state.Completion.Task;
    }

    private static void CancelLeasedBotCreationTask(BotCreationInvocationState state)
    {
        lock (StateLock)
        {
            if (Interlocked.CompareExchange(ref state.Settled, 1, 0) != 0)
                return;

            state.Completion.TrySetCanceled();
        }
    }

    private static async Task ObserveLeasedBotCreationTask(
        BotCreationInvocationState state)
    {
        // BotCreationData.Create completes synchronously for PFT's exact
        // BotCreatorClient/count-zero call. Defer observation until Harmony has
        // returned the replacement Task and PFT has registered its await.
        await Task.Yield();

        BotCreationData data = null;
        Exception failure = null;
        try
        {
            data = await state.OriginalTask;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        bool stopData = false;
        string lateFailure = string.Empty;
        lock (StateLock)
        {
            if (Volatile.Read(ref state.Settled) != 0)
            {
                stopData = data != null;
                if (failure != null && failure is not OperationCanceledException)
                {
                    lateFailure =
                        "Arena observed a late fault from revoked PFT BotCreationData.Create: "
                        + failure.GetBaseException().Message;
                }
            }
            else if (failure != null)
            {
                Interlocked.Exchange(ref state.Settled, 1);
                if (ReferenceEquals(_spawnLease, state.Lease) && !state.Lease.Revoked)
                {
                    FailUnsafe(
                        "PFT's leased count-zero BotCreationData.Create task failed: "
                        + failure.GetBaseException().Message);
                }
                state.Completion.TrySetException(failure);
            }
            else if (data == null)
            {
                Interlocked.Exchange(ref state.Settled, 1);
                const string nullProblem =
                    "PFT's leased count-zero BotCreationData.Create task returned null";
                if (ReferenceEquals(_spawnLease, state.Lease) && !state.Lease.Revoked)
                    FailUnsafe(nullProblem);
                state.Completion.TrySetException(new InvalidOperationException(nullProblem));
            }
            else if (!TryValidateBotCreationResultUnsafe(state, data, out string problem))
            {
                Interlocked.Exchange(ref state.Settled, 1);
                stopData = true;
                if (ReferenceEquals(_spawnLease, state.Lease) && !state.Lease.Revoked)
                    FailUnsafe("PFT's creation-task lease expired before release: " + problem);
                state.Completion.TrySetCanceled();
            }
            else
            {
                Interlocked.Exchange(ref state.Settled, 1);
                state.Completion.TrySetResult(data);

                if (!PitFireTeamRoundLifecyclePolicy.HasExactActivationSeams(
                        state.Lease.ProfilesById.Keys,
                        state.Lease.StartedProfileIds,
                        state.Lease.InvocationsByProfileId.Keys)
                    && ReferenceEquals(_spawnLease, state.Lease)
                    && !state.Lease.Revoked)
                {
                    FailUnsafe(
                        "PFT did not enter every exact follower activation seam "
                        + "during the deferred BotCreationData task release");
                }
            }
        }

        state.CancellationRegistration.Dispose();
        if (stopData)
            StopPftBotCreationData(data, "revoked creation-task completion");
        if (!string.IsNullOrEmpty(lateFailure))
            Plugin.ModLogger.LogWarning(lateFailure);
    }

    private static Task<BotCreationData> RejectPftBotCreationTask(
        Task<BotCreationData> originalTask,
        string reason)
    {
        _ = ObserveRejectedPftBotCreationTask(originalTask, reason);
        return Task.FromException<BotCreationData>(
            new InvalidOperationException(reason ?? "PFT BotCreationData.Create lease rejected"));
    }

    private static async Task ObserveRejectedPftBotCreationTask(
        Task<BotCreationData> originalTask,
        string reason)
    {
        if (originalTask == null)
            return;

        try
        {
            BotCreationData data = await originalTask;
            if (data != null)
                StopPftBotCreationData(data, reason);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected terminal state for a rejected call.
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "Arena observed a fault from rejected PFT BotCreationData.Create: "
                + ex.GetBaseException().Message);
        }
    }

    private static void StopPftBotCreationData(BotCreationData data, string reason)
    {
        try
        {
            data?.StopSpawn();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena could not stop late PFT BotCreationData ({reason}): "
                + ex.GetBaseException().Message);
        }
    }

    private static bool TryValidateExactBotCreationCallUnsafe(
        FollowerSpawnLease lease,
        IGetProfileData profileData,
        IBotCreator botCreator,
        int count,
        ITokenGetter token,
        out string reason)
    {
        if (!TryValidateBotCreationLeaseIdentityUnsafe(lease, out reason))
            return false;
        if (count != 0
            || !ReferenceEquals(botCreator, lease.Spawner._botCreator)
            || !ReferenceEquals(token, lease.Spawner))
        {
            reason = "the count, BotCreator or ITokenGetter argument changed";
            return false;
        }
        if (profileData is not GetProfileDataParams parameters)
        {
            reason = "the profile-data argument is not GetProfileDataParams";
            return false;
        }

        WildSpawnType expectedRole = lease.Leader.Side == EPlayerSide.Bear
            ? WildSpawnType.pmcBEAR
            : lease.Leader.Side == EPlayerSide.Usec
                ? WildSpawnType.pmcUSEC
                : WildSpawnType.assault;
        ShallBeGroupParams group = parameters.SpawnParams?.ShallBeGroup;
        int expectedGroupCount = lease.ProfilesById.Count + 1;
        if (parameters.Side != lease.Leader.Side
            || parameters.Role != expectedRole
            || parameters.BotDifficulty != BotDifficulty.hard
            || parameters._spawnTime != 0f
            || group == null
            || !group.Group
            || group.StartCount != expectedGroupCount
            || group.RemainCount != expectedGroupCount)
        {
            reason = "the exact PFT PMC side, role, difficulty, spawn time or group parameters changed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateBotCreationResultUnsafe(
        BotCreationInvocationState state,
        BotCreationData data,
        out string reason)
    {
        if (!TryValidateBotCreationLeaseIdentityUnsafe(state.Lease, out reason))
            return false;
        if (state.ContinuationContext == null
            || !ReferenceEquals(SynchronizationContext.Current, state.ContinuationContext)
            || !ReferenceEquals(state.Lease.BotCreationInvocation, state)
            || !ReferenceEquals(data._profileData, state.ProfileData)
            || !ReferenceEquals(data._botCreator, state.BotCreator)
            || !ReferenceEquals(data._tokenGetter, state.Token)
            || data.SpawnStopped)
        {
            reason = "the exact leased BotCreationData identity or spawn state changed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateBotCreationLeaseIdentityUnsafe(
        FollowerSpawnLease lease,
        out string reason)
    {
        reason = string.Empty;
        if (lease == null || lease.Revoked || !ReferenceEquals(_spawnLease, lease))
        {
            reason = lease?.RevocationReason ?? "the follower spawn lease is missing";
            return false;
        }
        if (!HasExactLeaseRosterUnsafe(lease))
        {
            reason = "the follower creation lease no longer covers the frozen canonical roster";
            return false;
        }
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId
            || SynchronizationContext.Current == null
            || _stage != PitFireTeamRoundLifecycleStage.SpawnRequested
            || lease.Epoch != _epoch
            || !ReferenceEquals(_botsPatchInstanceField.GetValue(null), lease.PatchInstance))
        {
            reason = "the Unity owner thread/context, PFT patch instance, lifecycle stage or epoch changed";
            return false;
        }
        if (!ArenaSession.IsExactActiveRaid(lease.Game, lease.World)
            || !ArenaRespawnDriver.IsSameActiveRaid(lease.Game, lease.RaidEpoch)
            || lease.Game.Status != GameStatus.Started
            || !Singleton<IBotGame>.Instantiated
            || !ReferenceEquals(Singleton<IBotGame>.Instance, lease.Game)
            || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, lease.World))
        {
            reason = "the exact confirmed Arena raid, LocalGame or GameWorld changed";
            return false;
        }
        if (!ReferenceEquals(lease.World.MainPlayer, lease.Leader)
            || !ReferenceEquals(lease.Game.LocalPlayer, lease.Leader)
            || !ReferenceEquals(_currentLeader, lease.Leader)
            || !ReferenceEquals(_currentBoss, lease.Boss)
            || !ReferenceEquals(
                _getBossMethod.Invoke(null, new object[] { lease.Leader.ProfileId }),
                lease.Boss))
        {
            reason = "the exact current leader or PFT boss wrapper changed";
            return false;
        }
        if (!ReferenceEquals(((IBotGame)lease.Game).BotsController, lease.Controller)
            || !ReferenceEquals(_botsControllerField.GetValue(null), lease.Controller)
            || !ReferenceEquals(lease.Controller.BotSpawner, lease.Spawner))
        {
            reason = "the exact BotsController or BotSpawner changed";
            return false;
        }
        if (!TryGetExactBotCreatorBindingUnsafe(lease, out _, out reason))
            return false;

        return true;
    }

    // No arriving body, mutable PFT selection or reduced native collection may
    // change the count. This exact profile-object lease derives from the frozen
    // canonical account set and the already validated per-round identity remap.
    private static bool HasExactLeaseRosterUnsafe(FollowerSpawnLease lease)
    {
        int expected = _canonicalProfilesByAccountId.Count;
        if (!PitFireTeamRoundLifecyclePolicy.IsSupportedCompanionCount(expected)
            || lease?.ProfilesById == null || lease.ProfilesById.Count != expected
            || _runtimeProfilesByAccountId.Count != expected
            || _preparedProfilesByAccountId.Count != expected)
            return false;
        foreach (string account in _canonicalProfilesByAccountId.Keys)
        {
            if (!_runtimeProfilesByAccountId.TryGetValue(account, out string runtimeId)
                || !_preparedProfilesByAccountId.TryGetValue(account, out Profile prepared)
                || ReferenceEquals(prepared, null)
                || !string.Equals(prepared.AccountId?.Trim(), account, StringComparison.Ordinal)
                || !string.Equals(prepared.ProfileId, runtimeId, StringComparison.Ordinal)
                || !lease.ProfilesById.TryGetValue(runtimeId, out Profile leased)
                || !ReferenceEquals(prepared, leased))
                return false;
        }
        return true;
    }

    /// <summary>
    /// BotCreatorClient awaits LocalGame.CreateBotByProfile before checking the
    /// activation token. If that token was cancelled while LocalPlayer.Create
    /// was running, SPT otherwise leaves the returned Player registered and in
    /// LocalGame._bots without ever constructing a BotOwner or invoking PFT's
    /// callback. This postfix leases only the exact runtime Profile/LocalGame
    /// pair already recorded by Arena's activation prefix.
    /// </summary>
    private static void LocalGameCreateBotByProfilePostfix(
        LocalGame __instance,
        GameWorld gw,
        Profile profile,
        ref Task<LocalPlayer> __result)
    {
        string profileId = profile?.ProfileId?.Trim();
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        RawPlayerCreationState rawState;
        string problem = string.Empty;
        lock (StateLock)
        {
            if (!HistoricalSpawnRecords.TryGetValue(profileId, out HistoricalSpawnRecord record)
                || record.Profile == null
                || !record.Profile.TryGetTarget(out Profile recordedProfile)
                || !ReferenceEquals(recordedProfile, profile)
                || record.Game == null
                || !record.Game.TryGetTarget(out LocalGame recordedGame)
                || !ReferenceEquals(recordedGame, __instance)
                || record.World == null
                || !record.World.TryGetTarget(out GameWorld recordedWorld)
                || record.Invocation == null
                || !record.Invocation.TryGetTarget(out SpawnInvocationState invocation)
                || invocation?.Lease == null
                || !invocation.Lease.ProfilesById.TryGetValue(profileId, out Profile expectedProfile)
                || !ReferenceEquals(expectedProfile, profile)
                || !ReferenceEquals(invocation.Lease.Game, __instance)
                || !ReferenceEquals(invocation.Lease.World, recordedWorld))
            {
                return;
            }

            IDictionary localBots = _localGameBotsField?.GetValue(__instance) as IDictionary;
            if (invocation.RawPlayerCreation != null)
            {
                problem = $"PFT requested a duplicate LocalPlayer for runtime follower {profileId}";
            }
            else if (__result == null)
            {
                problem = $"LocalGame.CreateBotByProfile returned no Task for runtime follower {profileId}";
            }
            else if (!TryValidateRawPlayerCreationCallUnsafe(
                         invocation,
                         __instance,
                         gw,
                         profile,
                         localBots,
                         out problem))
            {
                problem = $"PFT runtime follower {profileId} entered an invalid LocalPlayer lease: " + problem;
            }

            rawState = new RawPlayerCreationState
            {
                Invocation = invocation,
                Game = __instance,
                World = invocation.Lease.World,
                RequestedWorld = gw,
                Profile = profile,
                LocalBots = localBots,
                ContinuationContext = SynchronizationContext.Current,
                OriginalTask = __result,
                RejectionReason = problem,
                OwnerThreadId = _ownerThreadId
            };
            if (invocation.RawPlayerCreation == null)
                invocation.RawPlayerCreation = rawState;

            if (!string.IsNullOrEmpty(problem)
                && ReferenceEquals(_spawnLease, invocation.Lease)
                && !invocation.Lease.Revoked)
            {
                FailUnsafe(problem);
            }
        }

        if (rawState.OriginalTask == null)
        {
            __result = Task.FromResult<LocalPlayer>(null);
            return;
        }

        __result = LeasePftRawLocalPlayerTask(rawState);
    }

    private static Task<LocalPlayer> LeasePftRawLocalPlayerTask(
        RawPlayerCreationState state)
    {
        _ = ObserveLeasedRawLocalPlayerTask(state);
        return state.Completion.Task;
    }

    private static async Task ObserveLeasedRawLocalPlayerTask(
        RawPlayerCreationState state)
    {
        // LocalGame.CreateBotByProfile can likewise complete inside its Harmony
        // postfix. Give BotCreatorClient the still-open replacement first, so
        // its await can publish the BotOwner during the guarded task release.
        await Task.Yield();

        LocalPlayer player = null;
        Exception failure = null;
        try
        {
            player = await state.OriginalTask;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (state.ContinuationContext != null
            && !ReferenceEquals(SynchronizationContext.Current, state.ContinuationContext))
        {
            try
            {
                state.ContinuationContext.Post(
                    _ => CompleteLeasedRawLocalPlayerTaskSafely(state, player, failure),
                    null);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError(
                    $"Arena could not return raw PFT LocalPlayer {state.Profile.ProfileId} to its Unity context: "
                    + ex.GetBaseException().Message
                    + "; queued for the next Unity owner-thread tick.");
                lock (StateLock)
                {
                    PendingRawPlayerCompletions.Enqueue(new PendingRawPlayerCompletion
                    {
                        State = state,
                        Player = player,
                        Failure = failure,
                        OwnerThreadId = state.OwnerThreadId
                    });
                }
            }
            return;
        }

        CompleteLeasedRawLocalPlayerTaskSafely(state, player, failure);
    }

    private static void CompleteLeasedRawLocalPlayerTaskSafely(
        RawPlayerCreationState state,
        LocalPlayer player,
        Exception failure)
    {
        try
        {
            CompleteLeasedRawLocalPlayerTask(state, player, failure);
        }
        catch (Exception ex)
        {
            lock (StateLock)
            {
                if (ReferenceEquals(_spawnLease, state.Invocation.Lease)
                    && !state.Invocation.Lease.Revoked)
                {
                    FailUnsafe(
                        $"Arena's raw PFT LocalPlayer completion guard failed for {state.Profile.ProfileId}: "
                        + ex.GetBaseException().Message);
                }
            }

            if (player != null && player.AIData?.BotOwner == null)
            {
                RetireLeasedRawLocalPlayer(
                    state,
                    player,
                    "the LocalPlayer completion guard threw");
            }
            state.Completion.TrySetException(ex);
        }
    }

    private static void CompleteLeasedRawLocalPlayerTask(
        RawPlayerCreationState state,
        LocalPlayer player,
        Exception failure)
    {

        bool retireRawPlayer = false;
        bool completeWithNull = false;
        string retirementReason = state.RejectionReason;
        lock (StateLock)
        {
            if (Interlocked.CompareExchange(ref state.Settled, 1, 0) != 0)
                return;

            if (failure != null)
            {
                if (ReferenceEquals(_spawnLease, state.Invocation.Lease)
                    && !state.Invocation.Lease.Revoked)
                {
                    FailUnsafe(
                        $"LocalGame.CreateBotByProfile failed for runtime follower {state.Profile.ProfileId}: "
                        + failure.GetBaseException().Message);
                }
                state.Completion.TrySetException(failure);
                return;
            }

            if (player == null)
            {
                state.Completion.TrySetResult(null);
                return;
            }

            string problem = string.Empty;
            if (!string.IsNullOrEmpty(state.RejectionReason)
                || !TryValidateRawPlayerResultUnsafe(state, player, out problem))
            {
                completeWithNull = true;
                BotOwner existingOwner = player.AIData?.BotOwner;
                retireRawPlayer = existingOwner == null;
                retirementReason = string.IsNullOrEmpty(state.RejectionReason)
                    ? problem
                    : state.RejectionReason;
                if (existingOwner != null)
                {
                    state.Invocation.Body ??= existingOwner;
                    state.Invocation.Lease.ObservedBodiesByProfileId[state.Profile.ProfileId] = existingOwner;
                }
                if (ReferenceEquals(_spawnLease, state.Invocation.Lease)
                    && !state.Invocation.Lease.Revoked)
                {
                    FailUnsafe(
                        $"PFT raw LocalPlayer lease expired for {state.Profile.ProfileId}: "
                        + retirementReason);
                }
                else if (existingOwner != null)
                {
                    CleanupRuntimeProfilesUnsafe(
                        new[] { state.Profile.ProfileId },
                        state.Invocation.Lease,
                        existingOwner);
                }
            }
            else
            {
                // Keep synchronous continuations enabled and release under the
                // lifecycle lock on the exact captured Unity context. This runs
                // BotCreatorClient's token check, BotOwner.Create and Arena's
                // wrapped callback before Cancel/Reset can acquire StateLock.
                state.Completion.TrySetResult(player);
                if (!ReferenceEquals(state.Invocation.Body?.GetPlayer, player))
                {
                    BotOwner existingOwner = player.AIData?.BotOwner;
                    retireRawPlayer = existingOwner == null;
                    retirementReason =
                        "BotCreatorClient returned from the leased LocalPlayer await without publishing its BotOwner callback";
                    if (ReferenceEquals(_spawnLease, state.Invocation.Lease)
                        && !state.Invocation.Lease.Revoked)
                    {
                        FailUnsafe(retirementReason);
                    }
                    else if (existingOwner != null)
                    {
                        CleanupRuntimeProfilesUnsafe(
                            new[] { state.Profile.ProfileId },
                            state.Invocation.Lease,
                            existingOwner);
                    }
                }
            }
        }

        if (retireRawPlayer)
            RetireLeasedRawLocalPlayer(state, player, retirementReason);
        if (completeWithNull)
            state.Completion.TrySetResult(null);
    }

    private static bool TryValidateRawPlayerCreationCallUnsafe(
        SpawnInvocationState invocation,
        LocalGame game,
        GameWorld world,
        Profile profile,
        IDictionary localBots,
        out string reason)
    {
        FollowerSpawnLease lease = invocation.Lease;
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId
            || SynchronizationContext.Current == null)
        {
            reason = "the call did not enter on Arena's Unity owner thread/context";
            return false;
        }
        if (localBots == null
            || !ReferenceEquals(game, lease.Game)
            || !ReferenceEquals(world, lease.World)
            || !lease.ProfilesById.TryGetValue(profile.ProfileId, out Profile expectedProfile)
            || !ReferenceEquals(profile, expectedProfile)
            || !lease.InvocationsByProfileId.TryGetValue(profile.ProfileId, out SpawnInvocationState expectedInvocation)
            || !ReferenceEquals(invocation, expectedInvocation))
        {
            reason = "the exact LocalGame, GameWorld, Profile, registry or activation invocation changed";
            return false;
        }
        if (!TryGetExactBotCreatorBindingUnsafe(lease, out BotCreatorClient botCreator, out reason))
            return false;

        if (!lease.Revoked
            && !TryValidateSpawnLeaseUnsafe(
                lease,
                lease.PatchInstance,
                botCreator,
                out reason))
        {
            return false;
        }

        reason = lease.Revoked ? lease.RevocationReason : string.Empty;
        return !lease.Revoked;
    }

    private static bool TryValidateRawPlayerResultUnsafe(
        RawPlayerCreationState state,
        LocalPlayer player,
        out string reason)
    {
        if (state.ContinuationContext == null
            || !ReferenceEquals(SynchronizationContext.Current, state.ContinuationContext))
        {
            reason = "the LocalPlayer task left its captured Unity synchronization context";
            return false;
        }
        if (!TryValidateRawPlayerCreationCallUnsafe(
                state.Invocation,
                state.Game,
                state.RequestedWorld,
                state.Profile,
                state.LocalBots,
                out reason))
        {
            return false;
        }
        if (!ReferenceEquals(player.Profile, state.Profile)
            || !string.Equals(player.ProfileId, state.Profile.ProfileId, StringComparison.OrdinalIgnoreCase)
            || !ReferenceEquals(player.GameWorld, state.World)
            || !state.LocalBots.Contains(state.Profile.ProfileId)
            || !ReferenceEquals(state.LocalBots[state.Profile.ProfileId], player)
            || !state.World.RegisteredPlayers.Any(candidate => ReferenceEquals(candidate, player))
            || state.Invocation.Body != null)
        {
            reason = "the raw LocalPlayer identity or its exact old-raid registrations changed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryGetExactBotCreatorBindingUnsafe(
        FollowerSpawnLease lease,
        out BotCreatorClient botCreator,
        out string reason)
    {
        botCreator = lease?.Spawner?._botCreator as BotCreatorClient;
        if (botCreator == null
            || !ReferenceEquals(botCreator._game, lease.Game)
            || botCreator._playerCreator == null
            || !ReferenceEquals(botCreator._playerCreator.Target, lease.Game)
            || !Equals(botCreator._playerCreator.Method, _localGameCreateBotByProfileMethod))
        {
            reason = "BotCreatorClient is not bound to the exact leased LocalGame.CreateBotByProfile delegate";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static void RetireLeasedRawLocalPlayer(
        RawPlayerCreationState state,
        LocalPlayer player,
        string reason)
    {
        bool ownedByOldGame = false;
        try
        {
            if (state.LocalBots != null
                && state.LocalBots.Contains(state.Profile.ProfileId)
                && ReferenceEquals(state.LocalBots[state.Profile.ProfileId], player))
            {
                state.LocalBots.Remove(state.Profile.ProfileId);
                ownedByOldGame = true;
            }
            if (state.World?.RegisteredPlayers?.Any(candidate => ReferenceEquals(candidate, player)) == true)
                ownedByOldGame = true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not detach raw PFT LocalPlayer {state.Profile.ProfileId}: "
                + ex.GetBaseException().Message);
        }

        if (!ownedByOldGame)
            return;

        try
        {
            if (state.World?.RegisteredPlayers?.Any(candidate => ReferenceEquals(candidate, player)) == true)
                state.World.UnregisterPlayer(player);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not unregister raw PFT LocalPlayer {state.Profile.ProfileId}: "
                + ex.GetBaseException().Message);
        }

        try
        {
            player.Dispose();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not dispose raw PFT LocalPlayer {state.Profile.ProfileId}: "
                + ex.GetBaseException().Message);
        }

        try
        {
            AssetPoolObject.ReturnToPool(player.gameObject);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not return raw PFT LocalPlayer {state.Profile.ProfileId} to its pool: "
                + ex.GetBaseException().Message);
        }

        Plugin.ModLogger.LogInfo(
            $"Arena retired raw PFT LocalPlayer {state.Profile.ProfileId} ({reason}).");
    }

    /// <summary>
    /// PFT increments BotSpawner._inSpawnProcess immediately before this marker
    /// call and invokes ActivateBotAtPosition immediately after it. Skipping the
    /// marker for a revoked Arena ID prevents a disposed BossPlayers.Instance
    /// from throwing in that narrow gap; the activation prefix then performs the
    /// one counter settlement. Ordinary and current leased IDs remain unchanged.
    /// </summary>
    private static bool ShallBeFollowerPrefix(string profileId)
    {
        profileId = profileId?.Trim();
        if (string.IsNullOrWhiteSpace(profileId))
            return true;

        lock (StateLock)
        {
            if (!HistoricalRuntimeProfileIds.Contains(profileId))
                return true;

            FollowerSpawnLease lease = _spawnLease;
            bool current = lease != null
                           && !lease.Revoked
                           && lease.Epoch == _epoch
                           && lease.ProfilesById.ContainsKey(profileId)
                           && _bossPlayersInstanceProperty?.GetValue(null) != null;
            if (!current)
                RemovePftBookkeepingUnsafe(profileId);
            return current;
        }
    }

    /// <summary>
    /// Harmony seam used only for profile IDs minted by this lifecycle. PFT's
    /// ordinary and first-round canonical spawns pass through byte-for-byte.
    /// A historical Arena ID with no current exact lease is permanently revoked.
    /// </summary>
    private static bool ActivateBotAtPositionPrefix(
        object __instance,
        BotCreatorClient botCreator,
        Profile profile,
        ref Action<BotOwner> OnActivate,
        ref CancellationToken token,
        ref Task<bool> __result,
        out SpawnInvocationState __state)
    {
        __state = null;
        string profileId = profile?.ProfileId?.Trim();
        if (string.IsNullOrWhiteSpace(profileId))
            return true;

        lock (StateLock)
        {
            if (!HistoricalRuntimeProfileIds.Contains(profileId))
                return true;

            try
            {
                FollowerSpawnLease lease = _spawnLease;
                if (lease == null
                    || !lease.ProfilesById.TryGetValue(profileId, out Profile expectedProfile))
                {
                    RejectHistoricalActivationUnsafe(
                        profileId,
                        "the runtime profile has no current exact Arena spawn lease");
                    __result = Task.FromResult(false);
                    return false;
                }
                if (!ReferenceEquals(expectedProfile, profile))
                {
                    var wrongProfile = new SpawnInvocationState
                    {
                        Lease = lease,
                        ProfileId = profileId,
                        Spawner = lease.Spawner
                    };
                    SettleSpawnCounterUnsafe(wrongProfile);
                    FailUnsafe(
                        $"PFT replaced Arena's exact remapped Profile object for runtime follower {profileId}");
                    __result = Task.FromResult(false);
                    return false;
                }

                if (!TryValidateSpawnLeaseUnsafe(
                        lease,
                        __instance,
                        botCreator,
                        out string leaseProblem))
                {
                    var rejected = new SpawnInvocationState
                    {
                        Lease = lease,
                        ProfileId = profileId,
                        Spawner = lease.Spawner
                    };
                    SettleSpawnCounterUnsafe(rejected);
                    FailUnsafe("PFT follower activation lease drifted: " + leaseProblem);
                    __result = Task.FromResult(false);
                    return false;
                }

                if (OnActivate == null)
                {
                    var rejected = new SpawnInvocationState
                    {
                        Lease = lease,
                        ProfileId = profileId,
                        Spawner = lease.Spawner
                    };
                    SettleSpawnCounterUnsafe(rejected);
                    FailUnsafe("PFT supplied no activation callback for a leased runtime follower");
                    __result = Task.FromResult(false);
                    return false;
                }

                if (!lease.StartedProfileIds.Add(profileId))
                {
                    var duplicate = new SpawnInvocationState
                    {
                        Lease = lease,
                        ProfileId = profileId,
                        Spawner = lease.Spawner
                    };
                    SettleSpawnCounterUnsafe(duplicate);
                    FailUnsafe($"PFT attempted a duplicate activation for runtime follower {profileId}");
                    __result = Task.FromResult(false);
                    return false;
                }

                var state = new SpawnInvocationState
                {
                    Lease = lease,
                    ProfileId = profileId,
                    Spawner = lease.Spawner
                };
                lease.InvocationsByProfileId.Add(profileId, state);
                if (HistoricalSpawnRecords.TryGetValue(profileId, out HistoricalSpawnRecord record))
                    record.Invocation = new WeakReference<SpawnInvocationState>(state);
                Action<BotOwner> originalOnActivate = OnActivate;
                OnActivate = owner => OnLeasedFollowerActivated(state, originalOnActivate, owner);
                token = lease.Cancellation.Token;
                __state = state;
                return true;
            }
            catch (Exception ex)
            {
                if (_spawnLease != null
                    && _spawnLease.InvocationsByProfileId.TryGetValue(
                        profileId,
                        out SpawnInvocationState startedInvocation))
                {
                    SettleSpawnCounterUnsafe(startedInvocation);
                    FailUnsafe(
                        "the activation prefix failed after leasing a runtime follower: "
                        + ex.GetBaseException().Message);
                }
                else
                {
                    RejectHistoricalActivationUnsafe(
                        profileId,
                        "the activation prefix failed closed: " + ex.GetBaseException().Message);
                }
                __result = Task.FromResult(false);
                return false;
            }
        }
    }

    private static void ActivateBotAtPositionPostfix(
        ref Task<bool> __result,
        SpawnInvocationState __state)
    {
        if (__state == null)
            return;

        __result = ObserveLeasedActivationTask(__result, __state);
    }

    /// <summary>
    /// PFT 0.10.0's private task, and EFT's BotCreator task beneath it, both
    /// return before the detached CreateBot callback necessarily runs. This
    /// observer records only task failure/settlement; physical readiness still
    /// requires the wrapped callback, exact PFT wrapper and two registry passes.
    /// </summary>
    private static async Task<bool> ObserveLeasedActivationTask(
        Task<bool> activationTask,
        SpawnInvocationState state)
    {
        bool activated;
        try
        {
            activated = activationTask != null && await activationTask;
        }
        catch
        {
            lock (StateLock)
            {
                SettleSpawnCounterUnsafe(state);
                FailUnsafe($"PFT runtime follower {state.ProfileId} activation task threw");
            }
            throw;
        }

        lock (StateLock)
        {
            state.Lease.SettledProfileIds.Add(state.ProfileId);
            if (!activated)
            {
                SettleSpawnCounterUnsafe(state);
                FailUnsafe($"PFT runtime follower {state.ProfileId} activation task returned false");
            }
            else if (state.Lease.Revoked && state.Body != null)
            {
                CleanupRuntimeProfilesUnsafe(
                    new[] { state.ProfileId },
                    state.Lease,
                    state.Body);
            }
        }

        return activated;
    }

    private static void OnLeasedFollowerActivated(
        SpawnInvocationState state,
        Action<BotOwner> originalOnActivate,
        BotOwner owner)
    {
        lock (StateLock)
        {
            state.Body = owner;
            state.Lease.ObservedBodiesByProfileId[state.ProfileId] = owner;

            if (!TryValidateSpawnLeaseUnsafe(
                    state.Lease,
                    state.Lease.PatchInstance,
                    state.Lease.Spawner?._botCreator as BotCreatorClient,
                    out string leaseProblem)
                || owner == null
                || !string.Equals(owner.ProfileId, state.ProfileId, StringComparison.OrdinalIgnoreCase)
                || !state.Lease.ProfilesById.TryGetValue(state.ProfileId, out Profile expectedProfile)
                || !ReferenceEquals(owner.Profile, expectedProfile))
            {
                SettleSpawnCounterUnsafe(state);
                CleanupRuntimeProfilesUnsafe(
                    new[] { state.ProfileId },
                    state.Lease,
                    owner);
                FailUnsafe(
                    "PFT delivered a runtime follower after its exact Arena lease became invalid: "
                    + (string.IsNullOrWhiteSpace(leaseProblem)
                        ? state.ProfileId
                        : leaseProblem));
                return;
            }

            int counterBeforeCallback = state.Spawner._inSpawnProcess;
            try
            {
                // Keep cancellation and PFT watcher installation in one lock scope:
                // CancelFollowerSpawnLease cannot return between the validity
                // check and publication of PFT's manual-update callback.
                originalOnActivate(owner);
                state.CallbackCompleted = true;
                state.Lease.ActivatedProfileIds.Add(state.ProfileId);
            }
            catch (Exception ex)
            {
                int counterAfterFailure = state.Spawner._inSpawnProcess;
                if (counterAfterFailure < counterBeforeCallback)
                    Interlocked.Exchange(ref state.CounterSettled, 1);
                else
                    SettleSpawnCounterUnsafe(state);

                CleanupRuntimeProfilesUnsafe(
                    new[] { state.ProfileId },
                    state.Lease,
                    owner);
                FailUnsafe(
                    $"PFT runtime follower {state.ProfileId} activation callback failed: "
                    + ex.GetBaseException().Message);
                return;
            }

            int counterAfterCallback = state.Spawner._inSpawnProcess;
            if (counterAfterCallback < counterBeforeCallback)
            {
                Interlocked.Exchange(ref state.CounterSettled, 1);
            }
            else
            {
                // Exact PFT 0.10.0 normally decrements in ActivateBotCallback.
                // If it returned without doing so, Arena owns the one settlement.
                SettleSpawnCounterUnsafe(state);
                FailUnsafe(
                    $"PFT runtime follower {state.ProfileId} did not settle BotSpawner._inSpawnProcess");
            }
        }
    }

    private static bool TryValidateSpawnLeaseUnsafe(
        FollowerSpawnLease lease,
        object patchInstance,
        BotCreatorClient botCreator,
        out string reason)
    {
        reason = string.Empty;
        if (lease == null || lease.Revoked || !ReferenceEquals(_spawnLease, lease))
        {
            reason = lease?.RevocationReason ?? "the follower spawn lease is missing";
            return false;
        }
        if (!HasExactLeaseRosterUnsafe(lease))
        {
            reason = "the follower activation lease no longer covers the frozen canonical roster";
            return false;
        }
        if (_stage != PitFireTeamRoundLifecycleStage.SpawnRequested
            || lease.Epoch != _epoch
            || !ReferenceEquals(lease.PatchInstance, patchInstance)
            || !ReferenceEquals(_botsPatchInstanceField.GetValue(null), lease.PatchInstance))
        {
            reason = "the PFT patch instance, lifecycle stage or epoch changed";
            return false;
        }
        if (!ArenaSession.IsActive
            || !ArenaSession.IsExactActiveRaid(lease.Game, lease.World)
            || !ArenaRespawnDriver.IsSameActiveRaid(lease.Game, lease.RaidEpoch)
            || lease.Game.Status != GameStatus.Started
            || !Singleton<IBotGame>.Instantiated
            || !ReferenceEquals(Singleton<IBotGame>.Instance, lease.Game)
            || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, lease.World))
        {
            reason = "the exact Arena raid, LocalGame or GameWorld changed";
            return false;
        }
        if (!ReferenceEquals(lease.World.MainPlayer, lease.Leader)
            || !ReferenceEquals(lease.Game.LocalPlayer, lease.Leader)
            || !ReferenceEquals(_currentLeader, lease.Leader)
            || !ReferenceEquals(_currentBoss, lease.Boss)
            || !ReferenceEquals(
                _getBossMethod.Invoke(null, new object[] { lease.Leader.ProfileId }),
                lease.Boss))
        {
            reason = "the exact current leader or PFT boss wrapper changed";
            return false;
        }
        if (!ReferenceEquals(((IBotGame)lease.Game).BotsController, lease.Controller)
            || !ReferenceEquals(_botsControllerField.GetValue(null), lease.Controller)
            || !ReferenceEquals(lease.Controller.BotSpawner, lease.Spawner)
            || !ReferenceEquals(lease.Spawner._botCreator, botCreator)
            || lease.Spawner._inSpawnProcess <= 0)
        {
            reason = "the exact BotsController, BotSpawner, BotCreator or pre-incremented spawn counter changed";
            return false;
        }
        if (!TryGetExactBotCreatorBindingUnsafe(lease, out BotCreatorClient expectedCreator, out reason))
            return false;
        if (!ReferenceEquals(expectedCreator, botCreator))
        {
            reason = "the activation BotCreator is not the exact leased LocalGame binding";
            return false;
        }

        return true;
    }

    private static void RejectHistoricalActivationUnsafe(string profileId, string reason)
    {
        RemovePftBookkeepingUnsafe(profileId);
        if (HistoricalSpawnRecords.TryGetValue(profileId, out HistoricalSpawnRecord record)
            && record.Spawner != null
            && record.Spawner.TryGetTarget(out BotSpawner historicalSpawner))
        {
            SettleSpawnCounterUnsafe(new SpawnInvocationState
            {
                ProfileId = profileId,
                Spawner = historicalSpawner
            });
        }

        Plugin.ModLogger.LogWarning(
            $"Arena blocked revoked PitFireTeam runtime follower {profileId}: {reason}.");
    }

    private static void SettleSpawnCounterUnsafe(SpawnInvocationState state)
    {
        if (state == null
            || Interlocked.Exchange(ref state.CounterSettled, 1) != 0
            || state.Spawner == null)
        {
            return;
        }

        state.Spawner._inSpawnProcess = Math.Max(0, state.Spawner._inSpawnProcess - 1);
    }

    private static void RemovePftBookkeepingUnsafe(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        try
        {
            if (_manualUpdateField?.GetValue(null) is IDictionary manualUpdates)
                manualUpdates.Remove(profileId);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not remove PFT's manual-update callback for {profileId}: {ex.GetBaseException().Message}");
        }

        try
        {
            object bossPlayers = _bossPlayersInstanceProperty?.GetValue(null);
            if (bossPlayers != null
                && _shallBeFollowerField?.GetValue(bossPlayers) is IList shallBeFollower)
            {
                for (int index = shallBeFollower.Count - 1; index >= 0; index--)
                {
                    if (string.Equals(
                            shallBeFollower[index] as string,
                            profileId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        shallBeFollower.RemoveAt(index);
                    }
                }
            }
        }
        catch (Exception ex)
        {
                Plugin.ModLogger.LogError(
                    $"Arena could not remove PFT's shall-be-follower marker for {profileId}: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// <paramref name="anySettledUnarmed"/> separates a normal mid-flight assembly
    /// from one worth a warning. A healthy round is caught here about eighty
    /// milliseconds after the spawn request, when all selected bodies are legitimately
    /// PreActive with their weapon takes still open, and warning about that in
    /// every won round trains the reader to skip the one line that ever mattered.
    /// The suspicious shape is a take that has SETTLED without arming the body:
    /// no change in flight and BotWeaponManager.IsReady still false.
    /// </summary>
    private static string DescribeIncompleteFollowerAssemblyUnsafe(
        FollowerSpawnLease lease,
        HashSet<string> publishedProfileIds,
        out bool anySettledUnarmed)
    {
        anySettledUnarmed = false;
        if (lease?.ProfilesById == null || lease.ProfilesById.Count == 0)
            return string.Empty;

        int bundles = 0;
        try
        {
            if (lease.Spawner?._botCreator != null)
                bundles = lease.Spawner._botCreator.BundlesLoading;
        }
        catch (Exception)
        {
            bundles = 0;
        }

        List<string> missing = new();
        foreach (var pair in lease.ProfilesById.OrderBy(
                     item => item.Value?.Nickname ?? item.Key,
                     StringComparer.Ordinal))
        {
            string profileId = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(profileId)
                || (publishedProfileIds != null && publishedProfileIds.Contains(profileId)))
            {
                continue;
            }

            Profile profile = pair.Value;
            string nickname = profile?.Nickname ?? profile?.Info?.Nickname ?? "?";
            string accountId = profile?.AccountId?.Trim() ?? "?";
            bool started = lease.StartedProfileIds.Contains(profileId);
            bool activated = lease.ActivatedProfileIds.Contains(profileId);
            bool settled = lease.SettledProfileIds.Contains(profileId);
            lease.ObservedBodiesByProfileId.TryGetValue(profileId, out BotOwner body);
            string botState = ReferenceEquals(body, null) || body == null
                ? "no-body"
                : body.BotState.ToString();
            bool watcher = false;
            try
            {
                if (_manualUpdateField?.GetValue(null) is IDictionary manualUpdates)
                    watcher = manualUpdates.Contains(profileId);
            }
            catch (Exception)
            {
                watcher = false;
            }

            anySettledUnarmed |= IsSettledWithoutFirearmUnsafe(body);
            missing.Add(
                $"{nickname}/{accountId}/{profileId} BotState={botState} {DescribeWeaponReadinessUnsafe(body)} "
                + $"watcher={watcher} started={started} activated={activated} settled={settled} bundles={bundles}");
        }

        return missing.Count == 0
            ? string.Empty
            : "missing " + string.Join("; ", missing);
    }

    /// <summary>
    /// A body whose weapon take is finished and still did not arm it. Nothing
    /// native will move it from here: OnWeaponTaken schedules no retry while
    /// PreActive, and re-issuing the take was measured to change nothing.
    /// </summary>
    private static bool IsSettledWithoutFirearmUnsafe(BotOwner bot)
    {
        if (ReferenceEquals(bot, null) || bot == null)
            return false;

        try
        {
            return bot.BotState == EBotState.PreActive
                   && bot.WeaponManager?.IsReady == false
                   && bot.GetPlayer?.InventoryController?.IsChangingWeapon == false;
        }
        catch (Exception)
        {
            // An unreadable body is not evidence of the terminal shape.
            return false;
        }
    }

    /// <summary>
    /// The exact native reason a PreActive follower never reaches Active.
    ///
    /// BotOwner.UpdateManual only calls its own activation once
    /// WeaponManager.IsReady, which BotWeaponManager sets in
    /// UpdateFirearmsController — reached only when the FIRST weapon take lands a
    /// FIREARM. Land a knife instead and UpdateHandsController still reports
    /// allFine while BotMeleeWeaponData sets only Selector.IsWeaponReady, so
    /// selectorReady=True with weaponReady=False is the melee dead end. Re-issuing
    /// the take was measured and changed nothing, which leaves the refusal
    /// upstream: Player.TryProceed diverts an unreachable item to
    /// SetFirstAvailableItem, and for a companion carrying one machine gun and no
    /// sidearm the only thing still available is EFT's default bayonet.
    ///
    /// So report every clause of InventoryController.IsAtReachablePlace for the
    /// slot the take reads. Each is a separate, silent veto in native code, and
    /// only naming the one that fires distinguishes a lost part from an
    /// unexamined clone from a slot the engine will not bind from.
    /// </summary>
    private static string DescribeWeaponReadinessUnsafe(BotOwner bot)
    {
        if (ReferenceEquals(bot, null) || bot == null)
            return "weapon=no-body";

        try
        {
            BotWeaponManager weapons = bot.WeaponManager;
            if (weapons == null)
                return "weapon=no-manager";

            Player player = bot.GetPlayer;
            string hands = player?.HandsController?.Item?.TemplateId.ToString() ?? "none";
            return $"weaponReady={weapons.IsReady} selectorReady={weapons.Selector?.IsWeaponReady} "
                   + $"hands={hands} {DescribePrimaryReachUnsafe(player)}";
        }
        catch (Exception ex)
        {
            return "weapon=unreadable:" + ex.GetBaseException().GetType().Name;
        }
    }

    /// <summary>
    /// IsAtReachablePlace, clause by clause, for the primary weapon slot. Read
    /// only: every member here is a public native query and none of them mutates.
    /// </summary>
    private static string DescribePrimaryReachUnsafe(Player player)
    {
        EftInventory inventory = player?.InventoryController?.Inventory;
        Item primary = inventory?.Equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem;
        if (primary == null)
            return "primary=none";

        string report = $"primary={primary.TemplateId} "
                        + $"changing={player.InventoryController.IsChangingWeapon} "
                        + $"reach={player.InventoryController.IsAtReachablePlace(primary)} "
                        + $"addr={primary.CurrentAddress != null}";

        // The clause that would explain a diverted take: a required slot the
        // clone left empty makes the weapon unreachable and nothing says so.
        if (primary is CompoundItem compound)
        {
            string[] missingVital = compound.MissingVitalParts
                .Select(slot => slot?.ID ?? "?")
                .ToArray();
            report += $" vital={(missingVital.Length == 0 ? "ok" : string.Join("+", missingVital))}";
        }

        report += $" bindable={inventory.GetItemsInSlots(EftInventory.BindAvailableSlotsExtended).Contains(primary)}"
                  + $" examined={player.InventoryController.Examined(primary)}";
        return report;
    }

    private static void ValidateFollowerRegistryEntryUnsafe(
        object bossPlayers,
        object wrapper,
        BotOwner bot)
    {
        if (bossPlayers == null
            || wrapper == null
            || !_followerType.IsInstanceOfType(wrapper)
            || ReferenceEquals(bot, null)
            || string.IsNullOrWhiteSpace(bot.ProfileId))
        {
            throw new InvalidOperationException(
                "PFT follower registry validation received an incomplete wrapper identity");
        }

        if (_followersField.GetValue(bossPlayers) is not IList followers)
            throw new InvalidOperationException("PFT _followers registry is unavailable");
        if (_followersByProfileIdField.GetValue(bossPlayers) is not IDictionary followersByProfileId)
            throw new InvalidOperationException("PFT _followersByProfileId registry is unavailable");
        if (!PitFireTeamFollowerDetachPolicy.TryValidateRegistryEntry(
                followers,
                followersByProfileId,
                wrapper,
                bot.ProfileId,
                out string registryProblem))
        {
            throw new InvalidOperationException(registryProblem);
        }
    }

    private static void DetachFollowerWrapperUnsafe(
        object bossPlayers,
        object boss,
        object wrapper,
        BotOwner bot)
    {
        ValidateFollowerRegistryEntryUnsafe(bossPlayers, wrapper, bot);

        // PFT 0.10.0's RemoveBotFollower performs the complete live unlink but
        // unconditionally dereferences BotFollower at its final step. EFT sets
        // BotFollower to null while disposing a dead bot, before PFT releases
        // that bot's wrapper. Mirror the remaining registry and boss-list
        // removals for that disposed state without touching the missing component.
        if (_followersField.GetValue(bossPlayers) is not IList followers)
            throw new InvalidOperationException("PFT _followers registry is unavailable during unlink");
        if (_followersByProfileIdField.GetValue(bossPlayers) is not IDictionary followersByProfileId)
            throw new InvalidOperationException("PFT _followersByProfileId registry is unavailable during unlink");
        if (_bossFollowersProperty.GetValue(boss) is not IList bossFollowers)
            throw new InvalidOperationException("PFT boss follower list is unavailable during unlink");

        bool nativeFollowerAvailable = bot.BotFollower != null;
        var health = bot.GetPlayer?.HealthController;
        PitFireTeamFollowerDetachDecision decision = PitFireTeamFollowerDetachPolicy.Classify(
            nativeFollowerAvailable,
            bot.IsDead,
            bot.BotState == EBotState.Active,
            health != null,
            health?.IsAlive == true);
        if (decision == PitFireTeamFollowerDetachDecision.Invalid)
        {
            throw new InvalidOperationException(
                $"PFT follower {bot.ProfileId} has an unsafe teardown state "
                + $"(nativeFollower={nativeFollowerAvailable}, dead={bot.IsDead}, "
                + $"botState={bot.BotState}, healthReadable={health != null}, alive={health?.IsAlive == true})");
        }

        if (decision == PitFireTeamFollowerDetachDecision.Native
            && (!bot.BotFollower.HaveBoss
                || !ReferenceEquals(bot.BotFollower.BossToFollow, boss)))
        {
            throw new InvalidOperationException(
                $"active PFT follower {bot.ProfileId} is not bound to the exact old boss");
        }
        if (decision == PitFireTeamFollowerDetachDecision.Native
            && (_bossGroupProperty.GetValue(boss) is not BotsGroup activeBossGroup
                || !activeBossGroup.Contains(bot)))
        {
            throw new InvalidOperationException(
                $"active PFT follower {bot.ProfileId} is not in the exact old boss group");
        }
        if (decision == PitFireTeamFollowerDetachDecision.Native
            && bossFollowers.Cast<object>().Count(candidate => ReferenceEquals(candidate, bot)) != 1)
        {
            throw new InvalidOperationException(
                $"active PFT follower {bot.ProfileId} is not present exactly once on the old boss");
        }
        if (decision == PitFireTeamFollowerDetachDecision.Disposed
            && _bossGroupProperty.GetValue(boss) is BotsGroup disposedBossGroup
            && disposedBossGroup.Contains(bot))
        {
            // The ordinary EFT death path removes group membership before it
            // disposes BotFollower. Calling RemoveAlly on a partially disposed
            // group can dereference other stale members, so fail before changing
            // PFT's registries if that native ordering was not observed.
            throw new InvalidOperationException(
                $"disposed PFT follower {bot.ProfileId} is still in the old boss group");
        }

        if (decision == PitFireTeamFollowerDetachDecision.Native)
        {
            _removeFollowerMethod.Invoke(null, new object[] { bot, boss });
        }
        else
        {
            if (!PitFireTeamFollowerDetachPolicy.TryUnlinkDisposed(
                    followers,
                    followersByProfileId,
                    bossFollowers,
                    wrapper,
                    bot,
                    bot.ProfileId,
                    out string unlinkProblem))
            {
                throw new InvalidOperationException(unlinkProblem);
            }
        }

        _dismissFollowerMethod.Invoke(wrapper, new object[] { false });

        if (_followersField.GetValue(bossPlayers) is not IList remainingFollowers
            || remainingFollowers.Cast<object>().Any(candidate => ReferenceEquals(candidate, wrapper)))
        {
            throw new InvalidOperationException(
                $"PFT retained the detached wrapper for {bot.ProfileId}");
        }
        if (_followersByProfileIdField.GetValue(bossPlayers) is not IDictionary remainingByProfileId
            || remainingByProfileId.Contains(bot.ProfileId))
        {
            throw new InvalidOperationException(
                $"PFT retained the detached profile index for {bot.ProfileId}");
        }
        if (_bossFollowersProperty.GetValue(boss) is not IEnumerable remainingBossFollowers
            || remainingBossFollowers.Cast<object>().Any(candidate => ReferenceEquals(candidate, bot)))
        {
            throw new InvalidOperationException(
                $"PFT retained the detached physical follower {bot.ProfileId}");
        }
        if (_bossGroupProperty.GetValue(boss) is BotsGroup remainingBossGroup
            && remainingBossGroup.Contains(bot))
        {
            throw new InvalidOperationException(
                $"PFT retained {bot.ProfileId} in the old boss group");
        }
    }

    private static void CleanupRuntimeProfilesUnsafe(
        IEnumerable<string> profileIds,
        FollowerSpawnLease lease,
        BotOwner bodyHint = null)
    {
        HashSet<string> targets = new(
            (profileIds ?? Array.Empty<string>())
                .Where(profileId => !string.IsNullOrWhiteSpace(profileId)),
            StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
            return;

        foreach (string profileId in targets)
            RemovePftBookkeepingUnsafe(profileId);

        HashSet<BotOwner> bodies = new(ReferenceEqualityComparer<BotOwner>.Instance);
        if (bodyHint != null && targets.Contains(bodyHint.ProfileId))
            bodies.Add(bodyHint);
        if (lease != null)
        {
            foreach (var pair in lease.ObservedBodiesByProfileId)
            {
                if (targets.Contains(pair.Key) && pair.Value != null)
                    bodies.Add(pair.Value);
            }

            if (lease.Controller?.Bots?.BotOwners != null)
            {
                foreach (BotOwner bot in lease.Controller.Bots.BotOwners)
                {
                    if (bot != null && targets.Contains(bot.ProfileId))
                        bodies.Add(bot);
                }
            }

            if (lease.World?.RegisteredPlayers != null)
            {
                foreach (IPlayer player in lease.World.RegisteredPlayers)
                {
                    BotOwner bot = player?.AIData?.BotOwner;
                    if (bot != null && targets.Contains(bot.ProfileId))
                        bodies.Add(bot);
                }
            }
        }

        try
        {
            foreach (object wrapper in ReadFollowerWrappersUnsafe())
            {
                BotOwner bot = _getFollowerBotMethod.Invoke(wrapper, null) as BotOwner;
                if (bot == null || !targets.Contains(bot.ProfileId))
                    continue;

                bodies.Add(bot);
                object boss = _getFollowerBossMethod.Invoke(wrapper, null);
                if (boss != null)
                {
                    try
                    {
                        _unhookFollowerDeathMethod.Invoke(boss, new object[] { bot });
                    }
                    catch (Exception ex)
                    {
                        Plugin.ModLogger.LogError(
                            $"Arena could not unhook revoked PFT follower {bot.ProfileId}: {ex.GetBaseException().Message}");
                    }

                    try
                    {
                        object bossPlayers = _bossPlayersInstanceProperty.GetValue(null)
                                             ?? throw new InvalidOperationException(
                                                 "PFT BossPlayers.Instance is unavailable");
                        DetachFollowerWrapperUnsafe(bossPlayers, boss, wrapper, bot);
                    }
                    catch (Exception ex)
                    {
                        Plugin.ModLogger.LogError(
                            $"Arena could not remove revoked PFT wrapper {bot.ProfileId}:\n{ex}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                "Arena could not enumerate PFT wrappers during revoked-spawn cleanup: "
                + ex.GetBaseException().Message);
        }

        foreach (BotOwner bot in bodies)
        {
            try
            {
                RetireRevokedRuntimeBodyUnsafe(bot, lease);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError(
                    $"Arena could not retire revoked runtime follower {bot?.ProfileId ?? "?"}: {ex.GetBaseException().Message}");
            }
        }
    }

    internal static bool IsCurrentBoundedActivation(Profile profile, BotCreatorClient creator)
    {
        lock (StateLock)
            return profile != null && _spawnLease != null && !_spawnLease.Revoked
                && _spawnLease.ProfilesById.TryGetValue(profile.ProfileId, out var expected)
                && ReferenceEquals(expected, profile)
                && _spawnLease.InvocationsByProfileId.ContainsKey(profile.ProfileId)
                && TryValidateSpawnLeaseUnsafe(_spawnLease, _spawnLease.PatchInstance, creator, out _);
    }

    internal static bool RejectBoundedActivation(Profile profile, BotOwner body, string reason)
    {
        lock (StateLock)
        {
            if (profile == null || _spawnLease == null
                || !_spawnLease.ProfilesById.TryGetValue(profile.ProfileId, out var expected)
                || !ReferenceEquals(expected, profile)
                || !_spawnLease.InvocationsByProfileId.TryGetValue(profile.ProfileId, out var state)) return false;
            if (body != null && ReferenceEquals(body.Profile, profile))
            {
                state.Body = body;
                _spawnLease.ObservedBodiesByProfileId[profile.ProfileId] = body;
            }
            SettleSpawnCounterUnsafe(state);
            FailUnsafe("bounded follower placement rejected: " + reason);
            return true;
        }
    }

    internal static void RetireBoundedFollower(BotOwner bot, BotsController controller, GameWorld world)
        => RetireUnpublishedFollowerUnsafe(bot, controller, world);

    private static void RetireRevokedRuntimeBodyUnsafe(
        BotOwner bot,
        FollowerSpawnLease lease)
        => RetireUnpublishedFollowerUnsafe(bot, lease?.Controller ?? bot?.BotsController, lease?.World);

    private static void RetireUnpublishedFollowerUnsafe(BotOwner bot, BotsController controller, GameWorld world)
    {
        if (bot == null)
            return;

        Player player = bot.GetPlayer;
        bool publishedBySpawner = controller?.Bots?.BotOwners?.Any(candidate =>
            ReferenceEquals(candidate, bot)) == true;

        if (publishedBySpawner && bot.LeaveData != null)
        {
            // ActivateBotCallback published this body, so its native removal path
            // owns bot counts, GameWorld cleanup, deactivation and pool return.
            try
            {
                bot.LeaveData.RemoveFromMap();
                return;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError(
                    $"Arena native removal failed for revoked follower {bot.ProfileId}; using exact pre-publication cleanup: {ex.GetBaseException().Message}");
            }
        }

        // A revoked wrapped callback deliberately skips PFT's original
        // ActivateBotCallback. The owner is already PreActive and its Player can
        // already be registered, but it was never added to BotSpawner.Bots; using
        // BotDied here would therefore underflow native bot counts.
        try
        {
            if (world != null
                && player != null
                && world.RegisteredPlayers.Any(candidate => ReferenceEquals(candidate, player)))
            {
                world.UnregisterPlayer(player);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not unregister revoked follower {bot.ProfileId}: {ex.GetBaseException().Message}");
        }

        try
        {
            if (controller != null && player?.AIData != null)
                controller.DestroyInfo(player);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not destroy revoked follower info {bot.ProfileId}: {ex.GetBaseException().Message}");
        }

        try
        {
            bot.Deactivate();
            bot.Dispose();
            AssetPoolObject.ReturnToPool(bot.gameObject, true);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError(
                $"Arena could not return revoked follower {bot.ProfileId} to its pool: {ex.GetBaseException().Message}");
            var health = player?.ActiveHealthController;
            if (health != null && health.IsAlive)
            {
                using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                    health.Kill(EDamageType.Undefined);
            }
        }
    }

    private static bool TryValidateBoundFollowersUnsafe(
        IReadOnlyCollection<BotOwner> boundFollowers,
        IReadOnlyDictionary<string, string> canonicalProfilesByAccountId,
        out List<BotOwner> bodies,
        out string reason)
    {
        bodies = new List<BotOwner>();
        reason = string.Empty;
        if (!PitFireTeamRoundLifecyclePolicy.TryValidateCanonicalRoster(canonicalProfilesByAccountId, out reason))
            return false;
        int expected = canonicalProfilesByAccountId.Count;
        if (boundFollowers == null || boundFollowers.Count != expected)
        {
            reason = $"the completed round did not provide exactly {expected} bound follower bodies";
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

        if (accounts.Count != expected)
        {
            reason = "the completed round does not cover every authenticated account";
            return false;
        }

        return true;
    }

    private static bool TryRemapProfilesUnsafe(IDictionary profiles, out string reason)
    {
        reason = string.Empty;
        int expected = _canonicalProfilesByAccountId.Count;
        if (!PitFireTeamRoundLifecyclePolicy.IsSupportedCompanionCount(expected)
            || profiles == null || profiles.Count != expected)
        {
            reason = $"PFT prefetched {profiles?.Count ?? 0}/{expected} follower profiles";
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

            if (!TryPreserveEncyclopediaUnsafe(source, runtime, canonicalProfileId, out reason))
                return false;

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
            reason = "PFT's prefetched profiles do not exactly cover the authenticated accounts";
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

    /// <summary>
    /// Restores the source's "knows everything" encyclopedia on the clone.
    ///
    /// Profile.Clone goes through ProfileDescriptor, whose encyclopedia copy
    /// answers a NULL dictionary with an EMPTY one. Those two are opposites at
    /// runtime: Profile.Examined(templateId) returns true for null and
    /// ContainsKey for empty. PitFireTeam's stored teammates ship a null
    /// encyclopedia, so cloning one silently turned "this bot knows every item"
    /// into "this bot knows none".
    ///
    /// That is invisible for almost everything, because InventoryController
    /// .Examined short-circuits on Item.ExaminedByDefault, which nearly every
    /// template sets. For a template that does not — the M60E6 among the measured
    /// loadouts — the consequence is a silently paralysed follower:
    /// IsAtReachablePlace requires Examined, Player.TryProceed answers an
    /// unreachable item by diverting to SetFirstAvailableItem, and a companion
    /// with no sidearm is handed EFT's default bayonet. A knife takes
    /// UpdateHandsController's melee branch, which sets Selector.IsWeaponReady but
    /// never BotWeaponManager.IsReady, and BotOwner.UpdateManual leaves PreActive
    /// only on the latter. The body then waits out the round with no error at all.
    ///
    /// Only the null case is repaired, and only on Arena's own clone: a source
    /// that already carries entries is copied by the native path correctly, and
    /// matching the source exactly is what keeps the clone as safe as the original
    /// that survives round one.
    /// </summary>
    private static bool TryPreserveEncyclopediaUnsafe(
        Profile source,
        Profile runtime,
        string canonicalProfileId,
        out string reason)
    {
        reason = string.Empty;
        if (source.Encyclopedia != null || runtime.Encyclopedia == null)
            return true;

        if (runtime.Encyclopedia.Count != 0)
        {
            reason = $"PFT profile {canonicalProfileId} gained {runtime.Encyclopedia.Count} "
                     + "encyclopedia entries its source did not have";
            return false;
        }

        try
        {
            EncyclopediaField(runtime) = null;
        }
        catch (Exception ex)
        {
            reason = $"PFT profile {canonicalProfileId} could not keep its source encyclopedia: "
                     + ex.GetBaseException().Message;
            return false;
        }

        return runtime.Encyclopedia == null
               || FailReason(out reason, canonicalProfileId);

        static bool FailReason(out string reason, string profileId)
        {
            reason = $"PFT profile {profileId} kept a substituted encyclopedia after the repair";
            return false;
        }
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
            throw new InvalidOperationException("PFT spawnedPlayers did not retain exactly one current boss");
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
        RevokeFollowerSpawnLeaseUnsafe(_diagnostic, cleanupBodies: true);
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
        _oldLeaderProfileId = null;
        _oldBoss = null;
        _currentLeader = null;
        _currentBoss = null;
        _profileTask = null;
        _profileCacheWasCleanAtTeardown = false;
        _adoptedRespawnBoss = false;
        _spawnTask = null;
        _preparedProfileDictionary = null;
        _retiringBodies.Clear();
        _currentFollowers.Clear();
        _lastStableFollowers.Clear();
        _stableFollowerPasses = 0;
        _loggedIncompleteFollowerAssembly = false;
        _canonicalProfilesByAccountId.Clear();
        _runtimeProfilesByAccountId.Clear();
        _preparedProfilesByAccountId.Clear();
        if (clearHistory)
        {
            HistoricalRuntimeProfileIds.Clear();
            HistoricalRuntimeItemIds.Clear();
            HistoricalSpawnRecords.Clear();
        }
        _spawnLease = null;
    }

    private static void ClearContractUnsafe()
    {
        if (_spawnLeasePatchInstalled)
        {
            SpawnLeaseHarmony.UnpatchSelf();
            _spawnLeasePatchInstalled = false;
        }
        _assembly = null;
        _configuredVersion = null;
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
        _followersField = null;
        _followersByProfileIdField = null;
        _bossRealPlayerField = null;
        _bossBotsControllerField = null;
        _bossGroupProperty = null;
        _bossFollowersProperty = null;
        _botsPatchInstanceField = null;
        _botsControllerField = null;
        _spawnedPlayersField = null;
        _followerCreationTasksField = null;
        _createFollowerProfilesMethod = null;
        _spawnGroupBotsMethod = null;
        _botCreationDataCreateMethod = null;
        _localGameCreateBotByProfileMethod = null;
        _localGameBotsField = null;
        _activateBotAtPositionMethod = null;
        _shallBeFollowerMethod = null;
        _shallBeFollowerField = null;
        _manualUpdateField = null;
        _spawnGroupInvocationLease = null;
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
