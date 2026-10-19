using ArenaMode.Server.Services;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Services.Commerce;
using System.Reflection;
using System.Reflection.Emit;

namespace ArenaMode.Server.Patches;

/// <summary>
/// Protects both SPT's main profile and the separate teammate store of the
/// PitFireTeam builds listed in PitFireTeamVersions, while Arena owns the raid
/// result. Discovery is intentionally limited
/// to assemblies already loaded by SPT: ArenaMode never loads or references the
/// optional mod itself. Every expected boundary is validated before Harmony
/// changes any of them, so a changed PFT layout rejects team Arena as a whole.
/// </summary>
public static class PitFireTeamDynamicPersistencePatches
{
    private const string HarmonyId = "com.maschine.ArenaMode.server.pitfireteam-persistence";
    private static readonly Lock EnableLock = new();
    private static bool _enableAttempted;

    public static bool IsInstalled { get; private set; }
    public static bool IsCompatible { get; private set; }
    public static bool FileMutationPatchesReady { get; private set; }
    public static bool ProtectionReady => IsInstalled && IsCompatible && FileMutationPatchesReady;
    public static string IncompatibilityReason { get; private set; } =
        "PitFireTeam persistence protection has not been initialized.";

    /// <summary>
    /// <paramref name="RequiredFromAssemblyVersion"/> null means every admitted
    /// PitFireTeam build must expose this method. Otherwise that build and all
    /// later builds MUST expose it: opening version admission must never drop a
    /// previously protected transaction from a newer build.
    /// </summary>
    private sealed record MethodContract(
        string TypeName,
        string MethodName,
        string ReturnTypeName,
        int SessionParameterIndex = 0,
        bool IsPublic = true,
        Version? RequiredFromAssemblyVersion = null);

    /// <summary>Automatic raid-result work affecting profile, mail, queues, or PFT caches.</summary>
    private static readonly MethodContract[] RaidPersistenceContracts =
    [
        new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleReturnItems", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleTeamEscaped", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "RecordKillMessage", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "RegisterProtectedRaidItems", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "RegisterProtectedRaidItemIds", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "RemoveProtectedTeammateItemsFromExtractedProfile", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleEndLocalRaidKillMessages", "System.Void"),
        new("pitTeam.Server.Services.FriendlyPostRaidService", "HandleDeathEscapeSummary", "System.Void"),
        new("pitTeam.Server.Services.FriendlyRecruitService", "QueueRecruitPickups", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "PersistFollowerProgress", "System.Void"),
        new(
            "pitTeam.Server.Services.FriendlyTeammateService",
            "PersistDeathEscapeOutcomes",
            "pitTeam.Server.Models.FriendlyTeammateDeathEscapeSummary"),
    ];

    /// <summary>
    /// Whole user-facing transactions. Blocking only their final FileUtil write
    /// would let money/items/main-profile changes happen while the teammate file
    /// stayed unchanged. Suppress the complete entry point while the raid is
    /// Armed/InRaid. During bounded postraid finalization, an explicit request
    /// receives an exact-session scope so all of its writes either run together
    /// or fail by PFT's own transaction behavior.
    /// </summary>
    private static readonly MethodContract[] ManagementContracts =
    [
        new("pitTeam.Server.Services.FriendlyTeammateService", "CreateTeammate", "SPTarkov.Server.Core.Models.Eft.Profile.SearchFriendResponse"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "CreateTeammateFromRecruitCandidate", "SPTarkov.Server.Core.Models.Eft.Profile.SearchFriendResponse"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SelectDefaultLoadoutForAllTeammates", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateSuit", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "RenameTeammate", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateLoadout", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SaveTeammateDefaultEquipment", "pitTeam.Server.Models.FriendlyTeammateDefaultEquipmentResponse"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "BuyTeammateKit", "pitTeam.Server.Models.FriendlyTeammateBuyKitResponse"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "RepairTeammateDefaultEquipment", "pitTeam.Server.Models.FriendlyTeammateRepairEquipmentResponse"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateAggression", "System.Void"),
        // 0.10.3 added follower proficiency as a direct sibling of aggression and
        // tactic: same session-scoped settings write, same store. Leaving it out
        // would let one teammate-settings edit skip the scope the other two get.
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateProficiency", "System.Void",
            RequiredFromAssemblyVersion: ArenaMode.Shared.PitFireTeamVersions.MinimumForwardVersion),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateTactic", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "SetTeammateAutoJoin", "System.Void"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "DeleteTeammate", "System.Boolean"),
        new("pitTeam.Server.Services.FriendlyTeammateService", "DeleteTeammateByProfileId", "System.Boolean"),
        new("pitTeam.Server.Services.FriendlyRecruitService", "AcceptRecruitRequest", "System.Boolean"),
        new("pitTeam.Server.Services.FriendlyRecruitService", "AcceptAllRecruitRequests", "System.Boolean"),
        new("pitTeam.Server.Services.FriendlyRecruitService", "DeclineRecruitRequest", "System.Boolean"),
        // SaveAndApply writes global settings before the teammate loadout update;
        // patch the session-aware callback so the pair remains one transaction.
        new(
            "pitTeam.Server.Callbacks.FriendlyTeammateCallbacks",
            "SetServerSettings",
            "System.Threading.Tasks.ValueTask<System.String>",
            SessionParameterIndex: 2),
        // This callback owns its delayed notification/mail side effect and has
        // no lower service boundary. Group construction happens before Arena is
        // armed, so the normal PFT teammate-selection flow remains available.
        new(
            "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks",
            "SendGroupInvite",
            "System.Threading.Tasks.ValueTask<System.String>",
            SessionParameterIndex: 2),
    ];

    /// <summary>
    /// HTTP callbacks that form the outer transaction boundary around one or
    /// more protected services. Holding the operation read lease here prevents
    /// Arena preparation from splitting EndLocalRaid, RaidOutcomes, settings or
    /// another multi-step callback between two lower service calls.
    /// </summary>
    private static readonly MethodContract[] OperationBoundaryContracts =
    [
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "ReturnItems", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "TeamEscaped", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RecruitPickup", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RecordKillMessage", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RegisterProtectedItems", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "EndLocalRaid", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "RaidOutcomes", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyPostRaidCallbacks", "DeathEscape", "System.Threading.Tasks.ValueTask<System.String>", 2),

        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Create", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetServerSettings", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "AcknowledgeStartupRecoveryNotice", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetSuit", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Rename", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetLoadout", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SaveDefaultEquipment", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "BuyKit", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "RepairDefaultEquipment", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetAggression", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetTactic", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "SetAutoJoin", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateCallbacks", "Delete", "System.Threading.Tasks.ValueTask<System.String>", 2),

        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "DeleteFriend", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "AcceptFriendRequest", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "AcceptAllFriendRequests", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "DeclineFriendRequest", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "MergeFriendRequestInbox", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateSocialCallbacks", "MergeProfileView", "System.Threading.Tasks.ValueTask<System.String>", 2),

        new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "SendGroupInvite", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "GenerateFollowerProfile", "System.Threading.Tasks.ValueTask<System.String>", 2),
        new("pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks", "PersistFollowerProgress", "System.Threading.Tasks.ValueTask<System.String>", 2),
    ];

    /// <summary>
    /// Reads needed for follower spawning call these private recovery/migration
    /// methods. Suppress just the hidden writes, leaving LoadTeammates and
    /// TryGetSpawnProfile available to construct all four companions.
    /// </summary>
    private static readonly MethodContract[] RecoveryContracts =
    [
        new(
            "pitTeam.Server.Services.FriendlyTeammateService",
            "RecoverTeammateProfileIfNeeded",
            "System.Void",
            IsPublic: false),
        new(
            "pitTeam.Server.Services.FriendlyTeammateService",
            "RecoverDuplicateTeammateItemsForSession",
            "System.Void",
            IsPublic: false),
        new(
            "pitTeam.Server.Services.FriendlyRecruitService",
            "EnsureRecruitRequestAccountIds",
            "System.Boolean",
            IsPublic: false),
    ];

    public static void TryEnable(
        bool fileMutationPatchesReady = true,
        string? fileMutationFailureReason = null)
    {
        lock (EnableLock)
        {
            if (_enableAttempted)
                return;

            _enableAttempted = true;
            FileMutationPatchesReady = fileMutationPatchesReady;
        }

        try
        {
            EnableLoadedContract(fileMutationPatchesReady, fileMutationFailureReason);
        }
        catch (Exception ex)
        {
            IsCompatible = false;
            IncompatibilityReason = $"PitFireTeam persistence discovery failed: {ex.Message}";
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] {IncompatibilityReason} Optional compatibility fails open.");
        }
    }

    private static void EnableLoadedContract(
        bool fileMutationPatchesReady,
        string? fileMutationFailureReason)
    {
        Assembly? pitFireTeam = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(
                assembly.GetName().Name,
                "pitFireTeam.Server",
                StringComparison.OrdinalIgnoreCase));

        if (pitFireTeam == null)
        {
            IsInstalled = false;
            IsCompatible = false;
            IncompatibilityReason = "PitFireTeam server assembly is not installed.";
            ArenaContext.Logger?.Debug(
                "[ArenaMode] PitFireTeam server assembly is absent; optional persistence service patches were not loaded.");
            return;
        }
        IsInstalled = true;

        if (!fileMutationPatchesReady)
        {
            IsCompatible = false;
            IncompatibilityReason = string.IsNullOrWhiteSpace(fileMutationFailureReason)
                ? "One or more SPT FileUtil persistence patches could not be enabled."
                : fileMutationFailureReason;
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] PitFireTeam persistence protection is unavailable: {IncompatibilityReason}");
            return;
        }

        Version? version = pitFireTeam.GetName().Version;
        if (!ArenaMode.Shared.PitFireTeamVersions.IsSupportedAssemblyVersion(version))
        {
            IsCompatible = false;
            IncompatibilityReason =
                $"PitFireTeam server {version} is unsupported; expected {ArenaMode.Shared.PitFireTeamVersions.Describe}.";
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] PitFireTeam server {version} is unsupported " +
                $"(expected {ArenaMode.Shared.PitFireTeamVersions.Describe}); " +
                "optional persistence service patches fail open.");
            return;
        }

        List<MethodInfo> raidTargets = [];
        List<MethodInfo> managementTargets = [];
        List<MethodInfo> recoveryTargets = [];
        List<MethodInfo> operationBoundaryTargets = [];
        foreach ((MethodContract[] contracts, List<MethodInfo> resolved) in new[]
                 {
                     (RaidPersistenceContracts, raidTargets),
                     (ManagementContracts, managementTargets),
                     (RecoveryContracts, recoveryTargets),
                 })
        {
            if (!TryResolveApplicableContracts(pitFireTeam, version, contracts, resolved))
                return;
        }

        foreach (MethodContract contract in OperationBoundaryContracts)
        {
            MethodInfo? target = ResolveContract(pitFireTeam, contract);
            if (target == null)
            {
                RejectContract(contract.TypeName, contract.MethodName);
                return;
            }

            operationBoundaryTargets.Add(target);
        }

        Type? teammateServiceType = pitFireTeam.GetType(
            "pitTeam.Server.Services.FriendlyTeammateService",
            throwOnError: false,
            ignoreCase: false);
        MethodInfo? recoveryBackup = teammateServiceType?.GetMethod(
            "BackupFileBeforeRecovery",
            BindingFlags.Instance | BindingFlags.NonPublic,
            Type.DefaultBinder,
            [typeof(string)],
            null);
        if (recoveryBackup == null || recoveryBackup.ReturnType != typeof(void))
        {
            RejectContract(
                "pitTeam.Server.Services.FriendlyTeammateService",
                "BackupFileBeforeRecovery");
            return;
        }

        Type? settingsServiceType = pitFireTeam.GetType(
            "pitTeam.Server.Services.FriendlyServerSettingsService",
            throwOnError: false,
            ignoreCase: false);
        MethodInfo[] saveAndApplyCandidates = settingsServiceType?
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.Name == "SaveAndApply"
                && method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType.FullName ==
                    "pitTeam.Server.Models.FriendlyServerSettingsRequest")
            .ToArray() ?? [];
        if (saveAndApplyCandidates.Length != 1)
        {
            RejectContract(
                "pitTeam.Server.Services.FriendlyServerSettingsService",
                "SaveAndApply");
            return;
        }
        MethodInfo saveAndApply = saveAndApplyCandidates[0];

        Type? inviteAcceptType = pitFireTeam.GetType(
            "pitTeam.Server.Models.FriendlyGroupMatchInviteAccept",
            throwOnError: false,
            ignoreCase: false);
        Type? inviteDeclineType = pitFireTeam.GetType(
            "pitTeam.Server.Models.FriendlyGroupMatchInviteDecline",
            throwOnError: false,
            ignoreCase: false);
        if (inviteAcceptType == null || inviteDeclineType == null)
        {
            RejectContract(
                "pitTeam.Server.Models",
                "FriendlyGroupMatchInviteAccept/FriendlyGroupMatchInviteDecline");
            return;
        }

        MethodInfo? notificationSend = ResolveExactDeclaredMethod(
            typeof(NotificationSendHelper),
            "SendMessageAsync",
            "System.Threading.Tasks.Task",
            "SPTarkov.Server.Core.Models.Common.MongoId",
            "SPTarkov.Server.Core.Models.Eft.Ws.WsNotificationEvent");
        MethodInfo? mailSend = ResolveExactDeclaredMethod(
            typeof(MailSendService),
            "SendSystemMessageToPlayer",
            "System.Void",
            "SPTarkov.Server.Core.Models.Common.MongoId",
            "System.String",
            "System.Collections.Generic.List`1[[SPTarkov.Server.Core.Models.Eft.Common.Tables.Item",
            "System.Nullable`1[[System.Int64",
            "System.Collections.Generic.List`1[[SPTarkov.Server.Core.Models.Spt.Dialog.ProfileChangeEvent");
        if (notificationSend == null || mailSend == null)
        {
            RejectContract(
                "SPTarkov.Server.Core",
                "PitFireTeam delayed invite side-effect targets");
            return;
        }

        MethodInfo? inviteTaskRun = typeof(Task)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SingleOrDefault(method =>
                method.Name == nameof(Task.Run)
                && method.ReturnType == typeof(Task)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(Func<Task>));
        if (inviteTaskRun == null)
        {
            RejectContract("System.Threading.Tasks.Task", "Run(Func<Task>)");
            return;
        }

        MethodInfo sendGroupInvite = managementTargets.Single(method =>
            method.DeclaringType?.FullName ==
                "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks"
            && method.Name == "SendGroupInvite");

        MethodInfo raidVoidPrefix = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(VoidServicePrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo managementPrefix = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(ManagementServicePrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo managementFinalizer = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(ManagementServiceFinalizer),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo recoveryServicePrefix = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(RecoveryServicePrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo recoveryPrefix = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(RecoveryBackupPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo globalSettingsPrefix = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            nameof(GlobalSettingsPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo operationPrefix = typeof(PitFireTeamOperationBoundaryPatch).GetMethod(
            nameof(PitFireTeamOperationBoundaryPatch.Prefix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo operationFinalizer = typeof(PitFireTeamOperationBoundaryPatch).GetMethod(
            nameof(PitFireTeamOperationBoundaryPatch.Finalizer),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo inviteOriginPrefix = typeof(PitFireTeamInviteOriginPatch).GetMethod(
            nameof(PitFireTeamInviteOriginPatch.Prefix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo inviteOriginFinalizer = typeof(PitFireTeamInviteOriginPatch).GetMethod(
            nameof(PitFireTeamInviteOriginPatch.Finalizer),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo inviteTaskRunPrefix = typeof(PitFireTeamInviteTaskRunPatch).GetMethod(
            nameof(PitFireTeamInviteTaskRunPatch.Prefix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo inviteTaskRunFinalizer = typeof(PitFireTeamInviteTaskRunPatch).GetMethod(
            nameof(PitFireTeamInviteTaskRunPatch.Finalizer),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo notificationPrefix = typeof(PitFireTeamDelayedInviteSideEffectPatch).GetMethod(
            nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationPrefix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo notificationPostfix = typeof(PitFireTeamDelayedInviteSideEffectPatch).GetMethod(
            nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationPostfix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo notificationFinalizer = typeof(PitFireTeamDelayedInviteSideEffectPatch).GetMethod(
            nameof(PitFireTeamDelayedInviteSideEffectPatch.NotificationFinalizer),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo mailPrefix = typeof(PitFireTeamDelayedInviteSideEffectPatch).GetMethod(
            nameof(PitFireTeamDelayedInviteSideEffectPatch.MailPrefix),
            BindingFlags.Static | BindingFlags.Public)!;
        MethodInfo mailFinalizer = typeof(PitFireTeamDelayedInviteSideEffectPatch).GetMethod(
            nameof(PitFireTeamDelayedInviteSideEffectPatch.MailFinalizer),
            BindingFlags.Static | BindingFlags.Public)!;
        var harmony = new Harmony(HarmonyId);

        try
        {
            // Acquire a read lease before ANY allow/block decision. Team Arena's
            // preparation takes the corresponding write lease, so an operation
            // that began before the snapshot must finish before protection is
            // published, and one that begins later sees protection atomically.
            foreach (MethodInfo target in raidTargets
                         .Concat(managementTargets)
                         .Concat(recoveryTargets)
                         .Concat(operationBoundaryTargets)
                         .Append(recoveryBackup)
                         .Append(saveAndApply)
                         .Distinct())
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(operationPrefix),
                    finalizer: new HarmonyMethod(operationFinalizer));
            }

            foreach (MethodInfo target in raidTargets)
            {
                MethodInfo prefix = target.ReturnType == typeof(void)
                    ? raidVoidPrefix
                    : CreateResultPrefix(target.ReturnType, nameof(ShouldRunOriginal));
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }

            foreach (MethodInfo target in managementTargets)
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(managementPrefix),
                    finalizer: new HarmonyMethod(managementFinalizer));
            }

            foreach (MethodInfo target in recoveryTargets)
            {
                MethodInfo prefix = target.ReturnType == typeof(void)
                    ? recoveryServicePrefix
                    : CreateResultPrefix(target.ReturnType, nameof(ShouldRunRecovery));
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }

            harmony.Patch(recoveryBackup, prefix: new HarmonyMethod(recoveryPrefix));
            harmony.Patch(saveAndApply, prefix: new HarmonyMethod(globalSettingsPrefix));
            harmony.Patch(
                sendGroupInvite,
                prefix: new HarmonyMethod(inviteOriginPrefix),
                finalizer: new HarmonyMethod(inviteOriginFinalizer));
            harmony.Patch(
                inviteTaskRun,
                prefix: new HarmonyMethod(inviteTaskRunPrefix),
                finalizer: new HarmonyMethod(inviteTaskRunFinalizer));
            harmony.Patch(
                notificationSend,
                prefix: new HarmonyMethod(notificationPrefix),
                postfix: new HarmonyMethod(notificationPostfix),
                finalizer: new HarmonyMethod(notificationFinalizer));
            harmony.Patch(
                mailSend,
                prefix: new HarmonyMethod(mailPrefix),
                finalizer: new HarmonyMethod(mailFinalizer));

            IsCompatible = true;
            IncompatibilityReason = string.Empty;
            if (ArenaMode.Shared.PitFireTeamVersions.IsNewerThanVerified(version))
                ArenaContext.Logger?.Warning(
                    $"[ArenaMode] Attempting PitFireTeam server {version}, newer than the verified 0.10.3 baseline; persistence contracts passed, but this version has not been tested.");
            // Report the RESOLVED management count, not the table length: a build
            // that predates a narrowed contract legitimately guards one fewer
            // transaction, and the log has to say which it actually patched.
            ArenaContext.Logger?.Debug(
                $"[ArenaMode] PitFireTeam {version} persistence guard enabled " +
                $"({raidTargets.Count} raid, {managementTargets.Count} management, " +
                $"{OperationBoundaryContracts.Length} outer operations, " +
                $"{recoveryTargets.Count + 1} recovery, and global settings boundaries).");
        }
        catch (Exception ex)
        {
            // Do not leave a partly patched transaction graph: a skipped inner
            // write underneath an unpatched outer operation is exactly the
            // partial-profile state this contract exists to prevent.
            harmony.UnpatchSelf();
            IsCompatible = false;
            IncompatibilityReason = $"PitFireTeam persistence service patch failed: {ex.Message}";
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] PitFireTeam persistence service patch failed open and was rolled back: {ex}");
        }
    }

    /// <summary>
    /// Whether the installed build must expose this contract. A method added
    /// after the legacy baseline remains mandatory for every admitted newer build.
    /// </summary>
    private static bool AppliesToInstalledBuild(MethodContract contract, Version? installed)
        => contract.RequiredFromAssemblyVersion == null
           || (installed != null && installed.Build >= 0
               && new Version(installed.Major, installed.Minor, installed.Build, Math.Max(0, installed.Revision))
                   .CompareTo(contract.RequiredFromAssemblyVersion) >= 0);

    private static bool TryResolveApplicableContracts(
        Assembly assembly, Version? version, MethodContract[] contracts, List<MethodInfo> resolved)
    {
        foreach (MethodContract contract in contracts)
        {
            if (!AppliesToInstalledBuild(contract, version)) continue;
            MethodInfo? target = ResolveContract(assembly, contract);
            if (target == null)
            {
                RejectContract(contract.TypeName, contract.MethodName);
                return false;
            }
            resolved.Add(target);
        }
        return true;
    }

    private static MethodInfo? ResolveContract(Assembly assembly, MethodContract contract)
    {
        Type? type = assembly.GetType(contract.TypeName, throwOnError: false, ignoreCase: false);
        BindingFlags visibility = contract.IsPublic ? BindingFlags.Public : BindingFlags.NonPublic;
        MethodInfo[] candidates = type?
            .GetMethods(BindingFlags.Instance | visibility | BindingFlags.DeclaredOnly)
            .Where(method =>
                string.Equals(method.Name, contract.MethodName, StringComparison.Ordinal)
                && ReturnTypeMatches(method.ReturnType, contract.ReturnTypeName)
                && method.GetParameters().Length > contract.SessionParameterIndex
                && method.GetParameters()[contract.SessionParameterIndex].ParameterType == typeof(MongoId))
            .ToArray() ?? [];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static MethodInfo? ResolveExactDeclaredMethod(
        Type type,
        string methodName,
        string returnTypeName,
        params string[] parameterTypeNames)
    {
        MethodInfo[] candidates = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.Name == methodName
                && string.Equals(method.ReturnType.FullName, returnTypeName, StringComparison.Ordinal)
                && ParametersMatch(method.GetParameters(), parameterTypeNames))
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool ParametersMatch(ParameterInfo[] actual, string[] expected)
    {
        if (actual.Length != expected.Length)
            return false;

        for (int index = 0; index < actual.Length; index++)
        {
            string? fullName = actual[index].ParameterType.FullName;
            if (fullName == null
                || (!string.Equals(fullName, expected[index], StringComparison.Ordinal)
                    && !fullName.StartsWith(expected[index], StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReturnTypeMatches(Type actual, string expected)
        => expected == "System.Threading.Tasks.ValueTask<System.String>"
            ? actual == typeof(ValueTask<string>)
            : string.Equals(actual.FullName, expected, StringComparison.Ordinal);

    private static void RejectContract(string typeName, string methodName)
    {
        IsCompatible = false;
        IncompatibilityReason =
            $"PitFireTeam persistence contract mismatch at {typeName}.{methodName}.";
        ArenaContext.Logger?.Warning(
            $"[ArenaMode] {IncompatibilityReason} Optional service patches fail open.");
    }

    private static bool VoidServicePrefix(object[] __args, MethodBase __originalMethod)
        => ShouldRunOriginal(__args, __originalMethod);

    private static bool RecoveryServicePrefix(object[] __args, MethodBase __originalMethod)
        => ShouldRunRecovery(__args, __originalMethod);

    internal static bool ManagementServicePrefix(
        object[] __args,
        MethodBase __originalMethod,
        out object? __state)
    {
        __state = null;
        if (!TryGetSessionId(__args, out MongoId sessionId))
            return true;

        var sessions = ArenaContext.Sessions;
        if (sessions == null)
            return true;

        var protectedSession = sessions.IsExternalProfileWriteProtected(sessionId);
        var globalSettingsTransaction = IsGlobalSettingsTransaction(__originalMethod);
        if (globalSettingsTransaction
            && sessions.AnyExternalProfileWriteProtection()
            && (!protectedSession || !sessions.IsSoleExternalProfileProtection(sessionId)))
        {
            // SaveAndApply changes global PFT state, then this callback may
            // migrate one session's teammate files. If another session owns an
            // Arena boundary, no exact-session scope can authorize the global
            // half safely. Suppress the complete callback instead of splitting
            // it at the inner SaveAndApply call.
            ArenaContext.Logger?.Debug(
                $"[ArenaMode] Suppressed global PitFireTeam settings transaction for session {sessionId} " +
                "while another Arena profile boundary is protected.");
            return false;
        }

        if (!protectedSession)
            return true;

        if (IsDelayedInviteTransaction(__originalMethod))
        {
            // SendGroupInvite reports before its Task.Run notification/mail has
            // finished. Letting it start inside the Idle finalization window
            // would make the HTTP half succeed and the delayed half hit the
            // file/profile guard. Suppress the complete request; the menu can
            // retry after the bounded window ends.
            ArenaContext.Logger?.Debug(
                $"[ArenaMode] Suppressed delayed PitFireTeam invite transaction for protected session {sessionId}.");
            return false;
        }

        // Armed/InRaid actions could alter the real PMC while Arena owns its
        // snapshot. Once postraid processing has returned the session to Idle,
        // an explicit menu action may run as one complete, exact-session unit.
        if (sessions.GetStatus(sessionId).state != ArenaSessionState.Idle)
        {
            ArenaContext.Logger?.Debug(
                $"[ArenaMode] Suppressed PitFireTeam {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} " +
                $"while protected Arena session {sessionId} is active.");
            return false;
        }

        __state = PitFireTeamManagementScope.Enter(sessionId.ToString());
        return true;
    }

    private static bool IsGlobalSettingsTransaction(MethodBase method)
        // Only exact methods from ManagementContracts reach this prefix; within
        // that closed set the name uniquely identifies PFT's settings callback.
        => string.Equals(method.Name, "SetServerSettings", StringComparison.Ordinal);

    private static bool IsDelayedInviteTransaction(MethodBase method)
        => method.DeclaringType?.FullName ==
               "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks"
           && string.Equals(method.Name, "SendGroupInvite", StringComparison.Ordinal);

    internal static Exception? ManagementServiceFinalizer(Exception? __exception, object? __state)
    {
        PitFireTeamManagementScope.Exit(__state);
        return __exception;
    }

    internal static bool GlobalSettingsPrefix(MethodBase __originalMethod)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions?.AnyExternalProfileWriteProtection() != true
            || sessions.IsGlobalExternalMutationAuthorized())
            return true;

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed PitFireTeam {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} " +
            "during protected Arena profile finalization.");
        return false;
    }

    private static bool RecoveryBackupPrefix(string __0)
    {
        if (ArenaContext.Sessions?.AnyExternalProfileWriteProtection() != true)
            return true;

        return PitFireTeamFileMutationPatch.ShouldRunOriginal(
            new SPTarkov.Server.Core.Utils.FileUtil(),
            __0,
            "recovery backup");
    }

    /// <summary>Public so the emitted result-prefix assemblies can call it.</summary>
    public static bool ShouldRunOriginal(object[] arguments, MethodBase originalMethod)
    {
        bool hasSession = TryGetSessionId(arguments, out MongoId sessionId);
        var sessions = ArenaContext.Sessions;
        if (!hasSession
            || sessions == null
            || !sessions.IsExternalProfileWriteProtected(sessionId))
        {
            return true;
        }

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed PitFireTeam {originalMethod.DeclaringType?.Name}.{originalMethod.Name} " +
            $"for protected Arena session {sessionId}.");
        return false;
    }

    /// <summary>Public so the emitted recovery-result prefix can call it.</summary>
    public static bool ShouldRunRecovery(object[] arguments, MethodBase originalMethod)
    {
        if (!TryGetSessionId(arguments, out MongoId sessionId))
            return true;

        var sessions = ArenaContext.Sessions;
        if (sessions == null || !sessions.IsExternalProfileWriteProtected(sessionId))
            return true;

        if (PitFireTeamManagementScope.Authorizes(sessionId.ToString()))
            return true;

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed hidden PitFireTeam recovery {originalMethod.Name} " +
            $"for protected Arena session {sessionId}.");
        return false;
    }

    private static bool TryGetSessionId(object[] arguments, out MongoId sessionId)
    {
        foreach (object? argument in arguments)
        {
            if (argument is MongoId candidate)
            {
                sessionId = candidate;
                return true;
            }
        }

        sessionId = default;
        return false;
    }

    /// <summary>
    /// Harmony needs a by-ref __result of the exact external return type. Emit
    /// that prefix at runtime so no PitFireTeam type enters ArenaMode metadata.
    /// Reference results get a fresh empty response when possible; structs such
    /// as bool and ValueTask are initialized to their safe default value.
    /// </summary>
    private static MethodInfo CreateResultPrefix(Type resultType, string decisionMethodName)
    {
        var assemblyName = new AssemblyName(
            $"ArenaMode.PitFireTeam.ResultPrefix.{Guid.NewGuid():N}");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "GeneratedPersistencePrefix",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod(
            "Prefix",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object[]), typeof(MethodBase), resultType.MakeByRefType()]);
        method.DefineParameter(1, ParameterAttributes.None, "__args");
        method.DefineParameter(2, ParameterAttributes.None, "__originalMethod");
        method.DefineParameter(3, ParameterAttributes.None, "__result");

        MethodInfo decision = typeof(PitFireTeamDynamicPersistencePatches).GetMethod(
            decisionMethodName,
            BindingFlags.Static | BindingFlags.Public)!;

        ILGenerator il = method.GetILGenerator();
        Label runOriginal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, decision);
        il.Emit(OpCodes.Brtrue_S, runOriginal);

        il.Emit(OpCodes.Ldarg_2);
        if (resultType.IsValueType)
        {
            il.Emit(OpCodes.Initobj, resultType);
        }
        else
        {
            ConstructorInfo? constructor = resultType.GetConstructor(Type.EmptyTypes);
            if (constructor != null)
                il.Emit(OpCodes.Newobj, constructor);
            else
                il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stind_Ref);
        }

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(runOriginal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        Type generated = type.CreateType()!;
        return generated.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public)!;
    }
}
