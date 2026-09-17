using System.Text.RegularExpressions;
using ArenaMode.Compat;
using ArenaMode.Shared;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Server.Services;
using Mono.Cecil;

internal static class Program
{
    private const string Local = "local-aid";
    private static readonly string[] Four = { "pit-d", "pit-b", "pit-a", "pit-c" };
    private static readonly string[] FourProfiles =
        { "profile-d", "profile-b", "profile-a", "profile-c" };
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckSoloPolicy();
            CheckAuthenticatedTeamPolicy();
            CheckAuthoritativeIdentityPolicy();
            CheckInvalidRosterPolicy();
            CheckLiveRevalidationPolicy();
            CheckTacticalOnlyPolicy();
            CheckPendingModeSelectionPolicy();
            CheckServerNegotiationPolicy();
            CheckRespawnEpochRacePolicy();
            CheckTeamOpponentActivationPolicy();
            CheckFrozenArmContract();
            CheckSourceContract();
            CheckInstalledRuntimeContract();
            Console.WriteLine($"PITFIRETEAM_COMPAT_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PITFIRETEAM_COMPAT_CHECKS_FAILED after {_assertions} assertions: {exception}");
            return 1;
        }
    }

    private static void CheckRespawnEpochRacePolicy()
    {
        var epoch = new ArenaRaidEpoch();
        int firstRaid = epoch.Capture();
        Assert(epoch.IsCurrent(firstRaid, arenaActive: true, gameStarted: true),
            "a captured active Started raid owns its respawn continuation");
        Assert(!epoch.IsCurrent(firstRaid, arenaActive: false, gameStarted: true),
            "GameEnd invalidates respawn work even before a new epoch is observed");
        Assert(!epoch.IsCurrent(firstRaid, arenaActive: true, gameStarted: false),
            "a Stopping/Stopped LocalGame cannot resume a respawn continuation");

        epoch.Advance();
        Assert(!epoch.IsEpochCurrent(firstRaid),
            "ResetSession invalidates the stopped raid's coroutine lease");
        Assert(!epoch.IsCurrent(firstRaid, arenaActive: true, gameStarted: true),
            "an old coroutine cannot become valid again when the next Arena raid starts");

        int secondRaid = epoch.Capture();
        Assert(epoch.IsEpochCurrent(secondRaid)
               && epoch.IsCurrent(secondRaid, arenaActive: true, gameStarted: true),
            "the next Arena raid receives a distinct valid respawn lease");
    }

    private static void CheckTeamOpponentActivationPolicy()
    {
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: true,
                   healthReadable: true,
                   alive: true,
                   isPreActive: true,
                   isActive: false) == TeamOpponentActivationDecision.Pending,
            "a living PreActive Arena opponent remains pending during EFT's normal activation window");
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: true,
                   healthReadable: true,
                   alive: true,
                   isPreActive: false,
                   isActive: true) == TeamOpponentActivationDecision.Ready,
            "a living Active Arena opponent is ready for the frozen roster");
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: true,
                   healthReadable: true,
                   alive: true,
                   isPreActive: false,
                   isActive: false) == TeamOpponentActivationDecision.Invalid,
            "NonActive, ActiveFail and Disposed Arena opponents are terminal rather than pending");
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: false,
                   healthReadable: true,
                   alive: true,
                   isPreActive: true,
                   isActive: false) == TeamOpponentActivationDecision.Invalid,
            "a pre-activated opponent without its physical player body is invalid");
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: true,
                   healthReadable: false,
                   alive: true,
                   isPreActive: true,
                   isActive: false) == TeamOpponentActivationDecision.Invalid,
            "a pre-activated opponent without readable health is invalid");
        Assert(TeamOpponentActivationPolicy.Evaluate(
                   playerPresent: true,
                   healthReadable: true,
                   alive: false,
                   isPreActive: true,
                   isActive: false) == TeamOpponentActivationDecision.Invalid,
            "a dead pre-activated opponent is invalid");
    }

    private static void CheckSoloPolicy()
    {
        AssertSolo(Evaluate(PitFireTeamContractState.Absent),
            "absent plugin preserves solo Arena");
        AssertSolo(Evaluate(PitFireTeamContractState.Compatible),
            "installed plugin with empty selection preserves solo Arena");
        AssertSolo(Evaluate(PitFireTeamContractState.Incompatible),
            "unsupported plugin stays invisible to a genuine solo Arena");

        AssertRejected(Evaluate(PitFireTeamContractState.Absent,
                native: Native(Four), selected: Four),
            "absent plugin cannot authenticate a native party as a Pit team");
        var incompatibleTeam = Evaluate(PitFireTeamContractState.Incompatible,
            native: Native(Four), selected: Four);
        AssertRejected(incompatibleTeam,
            "unsupported version or layout cannot authenticate team Arena");
        Assert(PitFireTeamVersions.SupportedPluginVersions.All(
                   version => incompatibleTeam.Reason.Contains(version, StringComparison.Ordinal))
               && incompatibleTeam.Reason.Contains("layout", StringComparison.OrdinalIgnoreCase),
            "unsupported contract rejection names every pinned version and the runtime layout");
        Assert(PitFireTeamVersions.SupportedPluginVersions.SequenceEqual(new[] { "0.10.0", "0.10.3" })
               && PitFireTeamVersions.SupportedAssemblyVersions.SequenceEqual(
                   new[] { new Version(0, 10, 0, 0), new Version(0, 10, 3, 0) })
               && PitFireTeamVersions.IsSupportedPluginVersion("0.10.0")
               && PitFireTeamVersions.IsSupportedPluginVersion("0.10.3")
               && PitFireTeamVersions.IsSupportedAssemblyVersion(new Version(0, 10, 3, 0)),
            "the accepted set is exactly the two diffed builds");
        // A set, not a range: the builds between them were never compared, and a
        // newer one cannot be assumed to keep a private reflected signature.
        foreach (var unsupported in new[] { "0.10.1", "0.10.2", "0.10.4", "0.11.0", "0.10", "0.10.0.0", " ", "", null })
            Assert(!PitFireTeamVersions.IsSupportedPluginVersion(unsupported),
                "an undiffed or malformed plugin version is rejected: " + (unsupported ?? "null"));
        foreach (var unsupported in new[]
                 {
                     new Version(0, 10, 1, 0), new Version(0, 10, 2, 0), new Version(0, 10, 4, 0),
                     new Version(0, 10, 3, 1), new Version(0, 10, 3), new Version(0, 11, 0, 0)
                 })
            Assert(!PitFireTeamVersions.IsSupportedAssemblyVersion(unsupported),
                "an undiffed assembly version is rejected: " + unsupported);
        Assert(!PitFireTeamVersions.IsSupportedAssemblyVersion(null),
            "a missing assembly version is rejected");

        foreach (var mode in new[] { "deathmatch", "teamdeathmatch", "endless", "rounds", "gungame", "tactical" })
        {
            AssertSolo(Evaluate(PitFireTeamContractState.Compatible, mode: mode),
                $"solo Arena mode '{mode}' remains eligible");
        }
    }

    private static void CheckAuthenticatedTeamPolicy()
    {
        var decision = Evaluate(PitFireTeamContractState.Compatible,
            native: Native(Four.Reverse()), selected: Four, mode: "TaCtIcAl");
        Assert(decision.Allowed && decision.IsTeam,
            "exactly four authenticated companions enable Tactical team Arena");
        Assert(decision.Reason.Length == 0,
            "accepted team has no rejection reason");
        Assert(decision.CompanionAccountIds.SequenceEqual(Four.OrderBy(id => id, StringComparer.Ordinal)),
            "authenticated roster snapshot is stable and sorted");
        Assert(decision.CompanionProfilesByAccountId.Count == 4
               && Four.All(account => decision.CompanionProfilesByAccountId[account] == "profile-" + account),
            "authenticated roster preserves each selected account-to-profile pairing");
        Assert(PitFireTeamSelectionPolicy.RequiredCompanionCount == 4,
            "production policy requires exactly four companions");
    }

    private static void CheckAuthoritativeIdentityPolicy()
    {
        const string accountId = "1579201";
        const string storedProfileId = "6a965cbe1e1cd422c0fcfa65";
        Assert(PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                accountId,
                accountId,
                storedProfileId),
            "PFT account identity plus its distinct Mongo profile id is authoritative");
        Assert(!PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                accountId,
                accountId,
                accountId),
            "PFT synthetic RaidPlayer Id=AID is not mistaken for the stored profile id");
        Assert(!PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                accountId,
                "1579202",
                storedProfileId),
            "profile response must return the exact requested account id");
        Assert(!PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                accountId,
                accountId,
                "not-a-mongo-id"),
            "stored teammate profile identity must be a Mongo id");
        Assert(!PitFireTeamSelectionPolicy.IsAuthoritativeStoredProfileIdentity(
                accountId,
                accountId,
                null),
            "missing stored teammate profile identity is rejected");
    }

    private static void CheckInvalidRosterPolicy()
    {
        foreach (var count in new[] { 1, 3 })
        {
            var partial = Four.Take(count).ToArray();
            AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                    native: Native(partial), selected: partial),
                $"partial roster with {count} companion(s) is rejected");
        }

        var five = Four.Append("pit-e").ToArray();
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(five), selected: five),
            "oversized five-companion roster is rejected");

        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: new[] { "pit-a", "pit-b", "pit-c", "pit-c" }),
            "duplicate Pit selection is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: new[] { Local, "pit-a", "pit-b", "pit-c", "pit-c" }, selected: Four),
            "duplicate native group identity is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(new[] { "pit-a", "pit-b", "pit-c", "unknown" }), selected: Four),
            "mixed Pit and unknown native group is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: new[] { "pit-a", "pit-b", "pit-c", "stale" }),
            "stale selected identity is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(new[] { "pit-a" }), selected: Array.Empty<string>()),
            "unknown native extra cannot fall through as solo");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four, fika: true),
            "Fika team Arena is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: new[] { Local }, selected: Array.Empty<string>(), fika: true),
            "Fika solo Arena is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four, coop: true),
            "co-op team Arena is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: new[] { Local }, selected: Array.Empty<string>(), coop: true),
            "co-op solo Arena is rejected");

        AssertRejected(PitFireTeamSelectionPolicy.Evaluate(new PitFireTeamSelectionInput
            {
                Contract = PitFireTeamContractState.Compatible,
                GameMode = "tactical",
                CurrentPlayerAccountId = Local,
                NativeGroupAccountIds = null,
                SelectedPitAccountIds = Four
            }),
            "missing native group is rejected");
        AssertRejected(PitFireTeamSelectionPolicy.Evaluate(new PitFireTeamSelectionInput
            {
                Contract = PitFireTeamContractState.Compatible,
                GameMode = "tactical",
                CurrentPlayerAccountId = Local,
                NativeGroupAccountIds = Native(Four),
                SelectedPitAccountIds = null
            }),
            "missing selected-member set is rejected for compatible plugin");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: new[] { "someone-else" }, selected: Array.Empty<string>()),
            "native group without local account is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: new[] { Local, " " }, selected: Array.Empty<string>()),
            "blank native identity is rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four.Append(Local).ToArray()),
            "local player cannot appear in Pit companion selection");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four,
                selectedProfiles: FourProfiles.Take(3).ToArray()),
            "mismatched selected account/profile lists are rejected");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four,
                selectedProfiles: new[] { "profile-a", "profile-b", "profile-c", "profile-c" }),
            "duplicate selected profile identity is rejected");
        foreach (var goonProfile in new[]
                 {
                     "677c4e0cc7a538c4210d4d47",
                     "677c4e0cc7a538c4210d4d48",
                     "677c4e0cc7a538c4210d4d49"
                 })
        {
            AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                    native: Native(Four), selected: Four,
                    selectedProfiles: new[] { "profile-a", "profile-b", "profile-c", goonProfile }),
                $"special PitFireTeam profile {goonProfile} is rejected");
        }
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four,
                selectedProfiles: new[]
                {
                    "profile-a", "profile-b", "profile-c", "677C4E0CC7A538C4210D4D47"
                }),
            "special PitFireTeam profile identity is rejected case-insensitively");
        AssertRejected(PitFireTeamSelectionPolicy.Evaluate(null),
            "missing selection input is rejected");
    }

    private static void CheckLiveRevalidationPolicy()
    {
        var display = Evaluate(PitFireTeamContractState.Compatible,
            native: Native(Four), selected: Four);
        Assert(display.Allowed && display.IsTeam,
            "display-time exact roster is eligible");

        var changedSelection = new[] { "pit-a", "pit-b", "pit-c", "pit-new" };
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: changedSelection),
            "selection changed before raid start fails final live evaluation");

        var changedNativeGroup = Native(new[] { "pit-a", "pit-b", "pit-c", "pit-new" });
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: changedNativeGroup, selected: Four),
            "native group changed before raid start fails final live evaluation");

        AssertRejected(Evaluate(PitFireTeamContractState.Incompatible,
                native: Native(Four), selected: Four),
            "runtime becoming unsupported before raid start fails final evaluation");
    }

    private static void CheckTacticalOnlyPolicy()
    {
        foreach (var mode in new[] { "deathmatch", "teamdeathmatch", "endless", "rounds", "gungame", "", null })
        {
            AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                    native: Native(Four), selected: Four, mode: mode),
                $"team roster in non-Tactical mode '{mode ?? "<null>"}' is rejected");
        }
    }

    private static void CheckPendingModeSelectionPolicy()
    {
        foreach (var mode in new[] { "deathmatch", "teamdeathmatch", "endless", "rounds", "gungame", "", null })
        {
            var decision = Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four, mode: mode,
                allowPendingModeSelection: true);
            Assert(decision.Allowed && decision.IsTeam,
                $"the side screen admits an authenticated team before the map picker replaces '{mode ?? "<null>"}' with Tactical");
        }

        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four.Take(3).ToArray()),
                selected: Four.Take(3).ToArray(),
                mode: "endless",
                allowPendingModeSelection: true),
            "mode-pending admission must not relax the exact four-companion roster");
        AssertRejected(Evaluate(PitFireTeamContractState.Compatible,
                native: Native(Four), selected: Four, mode: "endless", fika: true,
                allowPendingModeSelection: true),
            "mode-pending admission must not relax the Fika exclusion");
    }

    private static void CheckServerNegotiationPolicy()
    {
        Assert(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: false,
                arenaMode: "tactical",
                persistenceProtectionReady: false,
                incompatibilityReason: "simulated unavailable guard") == null,
            "solo Tactical negotiation ignores optional Pit persistence readiness");
        Assert(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: false,
                arenaMode: "endless",
                persistenceProtectionReady: false,
                incompatibilityReason: null) == null,
            "ordinary solo Arena mode remains unaffected by the team guard");
        Assert(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: "tactical",
                persistenceProtectionReady: true,
                incompatibilityReason: null) == null,
            "team Tactical negotiation is accepted only with complete persistence protection");
        Assert(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: "TACTICAL",
                persistenceProtectionReady: true,
                incompatibilityReason: null) == null,
            "team Tactical mode comparison is case-insensitive");

        foreach (var nonTacticalMode in new[] { "deathmatch", "teamdeathmatch", "endless", "rounds", "gungame" })
        {
            var wrongMode = PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: nonTacticalMode,
                persistenceProtectionReady: true,
                incompatibilityReason: null);
            Assert(!string.IsNullOrWhiteSpace(wrongMode)
                   && wrongMode.Contains("Tactical", StringComparison.Ordinal),
                $"server rejects a protected team flag outside Tactical mode: {nonTacticalMode}");
        }

        const string contractFailure = "simulated incomplete Pit persistence contract";
        Assert(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: "tactical",
                persistenceProtectionReady: false,
                incompatibilityReason: contractFailure) == contractFailure,
            "server returns the concrete protection-contract failure for team Arena");
        Assert(!string.IsNullOrWhiteSpace(PitFireTeamRaidStartPolicy.GetRejectionReason(
                pitFireTeamTeamRequested: true,
                arenaMode: "tactical",
                persistenceProtectionReady: false,
                incompatibilityReason: " ")),
            "server fails closed with a fallback reason when readiness is false");
    }

    private static bool HasFrozenArmCall(string source)
        => Regex.Matches(source, @"ArenaSession\.MarkArmed\s*\(").Count == 1
           && Regex.Matches(source,
            @"ArenaSession\.MarkArmed\s*\(\s*settings\.location\s*,\s*requestedMode\s*,\s*deathmatch\s*\)").Count == 1;

    private static void CheckFrozenArmContract()
    {
        Assert(HasFrozenArmCall("ArenaSession.MarkArmed( settings.location,\n requestedMode, deathmatch );"),
            "frozen mode and Deathmatch rules are required independent of formatting");
        foreach (string call in new[]
                 {
                     "ArenaSession.MarkArmed(settings.location, requestedMode)",
                     "ArenaSession.MarkArmed(settings.location, Settings.SelectedGameModeId, deathmatch)",
                     "ArenaSession.MarkArmed(settings.location, requestedMode, null)",
                     "ArenaSession.MarkArmed(otherLocation, requestedMode, deathmatch)",
                     "ArenaSession.MarkArmed(settings.location, requestedMode, deathmatch); ArenaSession.MarkArmed(settings.location, requestedMode, null)",
                     "ArenaSession.MarkArmed(settings.location, requestedMode, deathmatch); ArenaSession.MarkArmed(settings.location, requestedMode, deathmatch)"
                 })
            Assert(!HasFrozenArmCall(call), "missing, mutable, foreign or duplicate arm contract is rejected: " + call);
    }

    private static void CheckSourceContract()
    {
        var repo = FindRepositoryRoot();
        var plugin = Read(repo, "ArenaMode.Client/Plugin.cs");
        var controller = Read(repo, "ArenaMode.Client/ArenaController.cs");
        var kitPreloader = Read(repo, "ArenaMode.Client/Kits/KitBundlePreloader.cs");
        var skip = Read(repo, "ArenaMode.Client/Session/SkipPreRaidScreensPatch.cs");
        var settingsApply = Read(repo, "ArenaMode.Client/Session/ArenaRaidSettingsApplyPatch.cs");
        var localStart = Read(repo, "ArenaMode.Client/Session/LocalRaidStartedPatch.cs");
        var menu = Read(repo, "ArenaMode.Client/Session/ArenaMenuSelection.cs");
        var side = Read(repo, "ArenaMode.Client/Session/ArenaSideSelectionPatch.cs");
        var session = Read(repo, "ArenaMode.Client/Session/ArenaSession.cs");
        var tactical = Read(repo, "ArenaMode.Client/Modes/TacticalMode.cs");
        var burstSpawn = Read(repo, "ArenaMode.Client/Bots/BurstSpawnService.cs");
        var combat = Read(repo, "ArenaMode.Client/Combat/CombatPatches.cs");
        var arenaMatch = Read(repo, "ArenaMode.Client/Modes/ArenaMatch.cs");
        var matchService = Read(repo, "ArenaMode.Client/Modes/ArenaMatchService.cs");
        var modeInterface = Read(repo, "ArenaMode.Client/Modes/IArenaMode.cs");
        var respawn = Read(repo, "ArenaMode.Client/Respawn/ArenaRespawnDriver.cs");
        var liveRespawn = Read(repo, "ArenaMode.Client/Respawn/LiveKitRespawn.cs");
        var respawnEpoch = Read(repo, "ArenaMode.Client/Respawn/ArenaRaidEpoch.cs");
        var gunGame = Read(repo, "ArenaMode.Client/Modes/GunGameMode.cs");
        var weaponSwap = Read(repo, "ArenaMode.Client/Combat/WeaponSwapService.cs");
        var compat = Read(repo, "ArenaMode.Client/Compat/PitFireTeamCompat.cs");
        var roundLifecycle = Read(repo,
            "ArenaMode.Client/Compat/PitFireTeamRoundLifecycle.cs");
        var selectionPolicy = Read(repo, "ArenaMode.Client/Compat/PitFireTeamSelectionPolicy.cs");
        var project = Read(repo, "ArenaMode.Client/ArenaMode.Client.csproj");
        var serverClient = Read(repo, "ArenaMode.Client/Session/ArenaServerClient.cs");
        var router = Read(repo, "ArenaMode.Server/Routers/ArenaModeRouter.cs");
        var dynamicPersistence = Read(repo,
            "ArenaMode.Server/Patches/PitFireTeamDynamicPersistencePatches.cs");
        var filePersistence = Read(repo,
            "ArenaMode.Server/Patches/PitFireTeamFileMutationPatches.cs");
        var managementScope = Read(repo,
            "ArenaMode.Server/Services/PitFireTeamManagementScope.cs");
        var operationGate = Read(repo,
            "ArenaMode.Server/Services/PitFireTeamOperationGate.cs");
        var sessionService = Read(repo,
            "ArenaMode.Server/Services/ArenaSessionService.cs");
        var postRaid = Read(repo,
            "ArenaMode.Server/Patches/PostRaidPmcSkipPatch.cs");
        var protectedEnd = Read(repo,
            "ArenaMode.Server/Services/ArenaProtectedRaidEndService.cs");
        var protectedStart = Read(repo,
            "ArenaMode.Server/Services/ArenaProtectedRaidStartService.cs");
        var startLocalRaid = Read(repo,
            "ArenaMode.Server/Patches/StartLocalRaidPatch.cs");
        var endLocalRaid = Read(repo,
            "ArenaMode.Server/Patches/EndLocalRaidPatch.cs");
        var ordinaryLifecycle = Read(repo,
            "ArenaMode.Server/Services/OrdinaryRaidLifecycleService.cs");
        var protocol = Read(repo, "ArenaMode.Shared/Protocol.cs");
        var snapshots = Read(repo,
            "ArenaMode.Server/Services/ProfileSnapshotService.cs");
        var nativeEndAdmission = Read(repo,
            "ArenaMode.Server/Services/ArenaNativeEndAdmission.cs");
        var locationPatch = Read(repo,
            "ArenaMode.Server/Patches/GenerateLocationAndLootPatch.cs");
        var patchManager = Read(repo, "ArenaMode.Server/PatchManager.cs");
        var mapMenu = Read(repo, "ArenaMode.Client/Session/ArenaLocationSelectionPatch.cs");
        var boost = Read(repo, "ArenaMode.Client/Bots/BotLoadBoost.cs");

        Assert(Regex.IsMatch(plugin,
                @"BepInDependency\s*\([^\)]*PitFireTeamCompat\.PluginGuid[^\)]*SoftDependency",
                RegexOptions.Singleline),
            "PitFireTeam is declared only as a soft BepInEx dependency");
        Assert(!Regex.IsMatch(project,
                @"<(?:Reference|ProjectReference)\b[^>]*(?:pitFireTeam|SPT-PitFireTeam)",
                RegexOptions.IgnoreCase),
            "client project has no compile-time PitFireTeam reference");
        var versions = Read(repo, "ArenaMode.Shared/PitFireTeamVersions.cs");
        var goalContext = Read(repo, "ArenaMode.Client/Compat/PitFireTeamGoalContext.cs");
        var serverPersistence = Read(repo,
            "ArenaMode.Server/Patches/PitFireTeamDynamicPersistencePatches.cs");
        Assert(compat.Contains("PluginGuid = \"xyz.pit.fireteam\"", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamVersions.IsSupportedAssemblyVersion(assembly.GetName().Version)", StringComparison.Ordinal),
            "runtime adapter pins the supported PitFireTeam identity and checks both version identities");
        // Every gate reads the one list, and none of them keeps a private copy:
        // six places accepting "a version" independently is how a build slips in
        // through the half of the contract nobody remembered to widen.
        Assert(versions.Contains("SupportedPluginVersions = { \"0.10.0\", \"0.10.3\" }", StringComparison.Ordinal)
               && roundLifecycle.Contains("PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)", StringComparison.Ordinal)
               && roundLifecycle.Contains("PitFireTeamVersions.IsSupportedAssemblyVersion(assemblyName.Version)", StringComparison.Ordinal)
               && goalContext.Contains("PitFireTeamVersions.IsSupportedAssemblyVersion(", StringComparison.Ordinal)
               && serverPersistence.Contains("PitFireTeamVersions.IsSupportedAssemblyVersion(version)", StringComparison.Ordinal)
               // 0.10.3's new teammate-settings transaction is gated like its
               // aggression/tactic siblings, and mandatorily so on that build.
               && serverPersistence.Contains(
                   "\"SetTeammateProficiency\", \"System.Void\",", StringComparison.Ordinal)
               && serverPersistence.Contains(
                   "RequiredForAssemblyVersions: [new Version(0, 10, 3, 0)]", StringComparison.Ordinal)
               && serverPersistence.Contains(
                   "contract.RequiredForAssemblyVersions.Contains(installed)", StringComparison.Ordinal)
               && serverPersistence.Contains("AppliesToInstalledBuild(contract, version)", StringComparison.Ordinal)
               && serverPersistence.IndexOf("AppliesToInstalledBuild(contract, version)", StringComparison.Ordinal)
                  < serverPersistence.IndexOf("ResolveContract(pitFireTeam, contract)", StringComparison.Ordinal)
               && !compat.Contains("new System.Version(0, 10", StringComparison.Ordinal)
               && !roundLifecycle.Contains("new System.Version(0, 10", StringComparison.Ordinal)
               && !goalContext.Contains("new Version(0, 10", StringComparison.Ordinal)
               && !serverPersistence.Contains("new(0, 10", StringComparison.Ordinal),
            "all six version gates read the shared accepted set instead of their own literal");
        // Arena reflects private members and patches a private activation seam;
        // the lifecycle must re-check the build it was handed, not the list.
        Assert(compat.Contains("PitFireTeamRoundLifecycle.Configure(assembly, pluginVersion)", StringComparison.Ordinal)
               && !compat.Contains("Configure(assembly, SupportedVersion)", StringComparison.Ordinal),
            "the round lifecycle receives the actual installed version, not the accepted list");

        Assert(skip.Contains("PitFireTeamCompat.IsInstalled", StringComparison.Ordinal)
               && skip.IndexOf("PitFireTeamCompat.IsInstalled", StringComparison.Ordinal)
                  < skip.IndexOf("__instance.method_27()", StringComparison.Ordinal),
            "confirmation shortcut yields to PitFireTeam's roster callback when installed");
        Assert(plugin.Contains("ArenaRaidSettingsApplyPatch.EnablePatch", StringComparison.Ordinal)
               && settingsApply.Contains("nameof(RaidSettings.Apply)", StringComparison.Ordinal)
               && settingsApply.Contains("PatchPrefix", StringComparison.Ordinal)
               && settingsApply.Contains("PatchPostfix", StringComparison.Ordinal)
               && settingsApply.Contains("RestoreArenaTransitionAfterCloneApply", StringComparison.Ordinal)
               && menu.Contains("transitionBeforeApply != ELocationTransition.Common", StringComparison.Ordinal)
               && menu.Contains("source?.transitionType != ELocationTransition.None", StringComparison.Ordinal)
               && menu.Contains("destination.transitionType != ELocationTransition.None", StringComparison.Ordinal)
               && menu.Contains("settings.transitionType == ELocationTransition.Common", StringComparison.Ordinal),
            "Arena repairs only the proven Common-to-None clone omission and keeps the final transition gate strict");
        Assert(side.Contains("PitFireTeamCompat.IsSquadManagementScreen", StringComparison.Ordinal)
               && !side.Contains("_randomButton?.gameObject?.activeSelf == false", StringComparison.Ordinal)
               && side.Contains("HarmonyAfter(\"MatchMakerSideSelectionScreenShowPatch\")", StringComparison.Ordinal)
               && compat.Contains("SquadModeActive", StringComparison.Ordinal)
               && compat.Contains("PitSquadOpenPrefix", StringComparison.Ordinal)
               && compat.Contains("_squadOpenDepth", StringComparison.Ordinal)
               && compat.Contains("if (_squadModeActiveProperty == null)", StringComparison.Ordinal),
            "Arena uses PFT's own My Squad marker and never vanilla Random visibility as a fallback");
        Assert(side.Contains("PitFireTeamCompat.EvaluateSelection", StringComparison.Ordinal)
               && side.Contains("ArenaMenuSelection.ConfigureEligibility", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamSelectionPolicy.Evaluate", StringComparison.Ordinal),
            "menu eligibility reaches the production PitFireTeam selection policy through the optional adapter");
        Assert(localStart.Contains("TryValidateAndFreezeRequest", StringComparison.Ordinal)
               && menu.Contains("_freezeRoster?.Invoke", StringComparison.Ordinal)
               && side.Contains("PitFireTeamCompat.TryFreezeTeamRoster", StringComparison.Ordinal),
            "raid start revalidates and freezes the live Pit roster");
        string freezeRoster = Slice(
            compat,
            "public static bool TryFreezeTeamRoster(",
            "public static bool TryBindLocalPlayerBody(");
        string resolveProfiles = Slice(
            compat,
            "private static bool TryResolveSelectedProfileIds(",
            "private static bool TryGetCurrentProfileId(");
        string clearCompat = Slice(
            compat,
            "public static void Clear(string reason)",
            "private static void EnsureInitialized(");
        Assert(freezeRoster.Contains("InvalidateStoredProfileCache()", StringComparison.Ordinal)
               && freezeRoster.IndexOf("InvalidateStoredProfileCache()", StringComparison.Ordinal)
                  < freezeRoster.IndexOf("TryResolveSelectedProfileIds(", StringComparison.Ordinal)
               && freezeRoster.Contains("TryMatchLiveRosterToDecision(", StringComparison.Ordinal)
               && compat.Contains(
                   "the PitFireTeam roster changed between map display and raid start.",
                   StringComparison.Ordinal)
               && resolveProfiles.Contains("TryGetCachedSelectedProfileIds(", StringComparison.Ordinal)
               && resolveProfiles.Contains("StoreSelectedProfileCache(", StringComparison.Ordinal)
               && resolveProfiles.Contains("TeammateProfileRoute", StringComparison.Ordinal)
               && clearCompat.Contains("InvalidateStoredProfileCache()", StringComparison.Ordinal)
               && mapMenu.Contains("Display-time stored-profile HTTP is", StringComparison.Ordinal),
            "map-display stored-profile HTTP is cached, freeze re-authenticates the live roster, and leaving setup drops the cache");
        Assert(compat.Contains("GetField(nameof(RaidPlayer.AccountId)", StringComparison.Ordinal)
               && compat.Contains("GetField(nameof(RaidPlayer.Id)", StringComparison.Ordinal)
               && compat.Contains("TeammateProfileRoute", StringComparison.Ordinal)
               && compat.Contains("data?[\"id\"]", StringComparison.Ordinal)
               && compat.Contains("IsAuthoritativeStoredProfileIdentity", StringComparison.Ordinal)
               && compat.Contains("_selectedRaidIdField", StringComparison.Ordinal)
               && !compat.Contains("GetProperty(nameof(RaidPlayer.AccountId)", StringComparison.Ordinal)
               && !compat.Contains("GetProperty(nameof(RaidPlayer.Id)", StringComparison.Ordinal)
               && compat.Contains("SelectedPitProfileIds", StringComparison.Ordinal)
               && selectionPolicy.Contains("677c4e0cc7a538c4210d4d47", StringComparison.Ordinal)
               && selectionPolicy.Contains("677c4e0cc7a538c4210d4d48", StringComparison.Ordinal)
               && selectionPolicy.Contains("677c4e0cc7a538c4210d4d49", StringComparison.Ordinal),
            "selection authenticates physical profile identities and rejects PitFireTeam's fixed Goon profiles");
        Assert(localStart.IndexOf("TryValidateAndFreezeRequest", StringComparison.Ordinal)
                  < localStart.IndexOf("ArenaServerClient.PostRaidStart", StringComparison.Ordinal),
            "live roster is validated before Arena profile protection is armed");
        Assert(localStart.Contains("ArenaMenuSelection.ClearCompatibilityState", StringComparison.Ordinal)
               && plugin.Contains("RegisterCompatibilityCleanup(PitFireTeamCompat.Clear", StringComparison.Ordinal),
            "failed raid negotiation clears Arena's transient Pit roster state");
        Assert(!menu.Contains("GroupPlayers.Clear", StringComparison.Ordinal)
               && !menu.Contains("SelectedPitAccountIds.Clear", StringComparison.Ordinal),
            "Arena selection cleanup does not mutate PitFireTeam's selected group");

        Assert(session.Contains("PitFireTeamCompat.HasFrozenTeamRoster", StringComparison.Ordinal)
               && session.Contains("LiveKitRespawn.AtRaidStart", StringComparison.Ordinal),
            "raid-start code has explicit team-Arena and kit-swap branches");
        Assert(controller.Contains("PitFireTeamCompat.TeamArenaActive", StringComparison.Ordinal)
               && controller.Contains("PitFireTeamCompat.IsFinalizing", StringComparison.Ordinal)
               && controller.IndexOf("PitFireTeamCompat.TeamArenaActive", StringComparison.Ordinal)
                  < controller.IndexOf("PreloadAll()", StringComparison.Ordinal)
               && kitPreloader.Contains("_raidEpoch", StringComparison.Ordinal)
               && kitPreloader.Contains("task?.IsCanceled == true", StringComparison.Ordinal)
               && kitPreloader.Contains("IsCurrentRaid(epoch)", StringComparison.Ordinal)
               && kitPreloader.Contains("Session.ArenaSession.IsActive", StringComparison.Ordinal),
            "team Tactical skips kit warmup and every remaining preload is raid-epoch/cancellation bound");
        Assert(respawnEpoch.Contains("Interlocked.Increment", StringComparison.Ordinal)
               && respawn.Contains("RaidEpoch.Advance()", StringComparison.Ordinal)
               && respawn.Contains("IsSameActiveRaid(game, raidEpoch)", StringComparison.Ordinal)
               && respawn.IndexOf("yield return KitBundlePreloader.Preload(kit)", StringComparison.Ordinal)
                  < respawn.LastIndexOf("IsSameActiveRaid(game, raidEpoch)", StringComparison.Ordinal)
               && liveRespawn.Contains("CaptureRaidEpoch()", StringComparison.Ordinal)
               && liveRespawn.Contains("BeginPipelineUse(game, raidEpoch)", StringComparison.Ordinal)
               && liveRespawn.Contains("EndPipelineUse(raidEpoch)", StringComparison.Ordinal)
               && Regex.IsMatch(liveRespawn,
                   @"if\s*\(\s*!sameActiveRaid\s*\)\s*\{(?:(?!Report\s*\().)*?yield break;",
                   RegexOptions.Singleline),
            "respawn continuations require the same active Started raid epoch and stale completions invoke no old callback");
        Assert(gunGame.Contains("CaptureRaidEpoch()", StringComparison.Ordinal)
               && gunGame.Contains("IsCurrentRaidWork(host, player, raidEpoch)", StringComparison.Ordinal)
               && gunGame.IndexOf("yield return KitBundlePreloader.Preload(kit)", StringComparison.Ordinal)
                  < gunGame.LastIndexOf("IsCurrentRaidWork(host, player, raidEpoch)", StringComparison.Ordinal)
               && Regex.IsMatch(gunGame,
                   @"WeaponSwapService\.Swap\s*\(\s*host\s*,\s*raidEpoch",
                   RegexOptions.Singleline),
            "Gun Game preload and promotion carry one active Started-raid lease into the weapon swap");
        Assert(weaponSwap.Contains("ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)", StringComparison.Ordinal)
               && weaponSwap.Contains("Interlocked.Increment(ref _operationGeneration)", StringComparison.Ordinal)
               && weaponSwap.Contains("Volatile.Read(ref _operationGeneration) == operationGeneration", StringComparison.Ordinal)
               && Regex.IsMatch(weaponSwap,
                   @"if\s*\(\s*!OwnsOperation\(game,\s*raidEpoch,\s*operationGeneration\)\s*\)\s*yield break;[\s\S]{0,300}if\s*\(\s*!Exchange",
                   RegexOptions.Singleline)
               && weaponSwap.Contains("An old routine must not clear", StringComparison.Ordinal),
            "live weapon mutation and callback completion are raid- and operation-generation bound");
        Assert(session.Contains("if (!ArenaServerClient.ConfirmRaidStarted())", StringComparison.Ordinal)
               && session.IndexOf("if (!ArenaServerClient.ConfirmRaidStarted())", StringComparison.Ordinal)
                  < session.IndexOf("IsActive = true", StringComparison.Ordinal)
               && serverClient.Contains("clientRaidStarted", StringComparison.Ordinal)
               && serverClient.Contains("_activeArmToken", StringComparison.Ordinal)
               && serverClient.Contains("_boundNativeEndIdentity", StringComparison.Ordinal)
               && router.Contains("info.NativeEndIdentity", StringComparison.Ordinal)
               && sessionService.Contains("entry.NativeEndIdentity", StringComparison.Ordinal)
               && router.Contains("clientRaidStarted = Sessions?.IsClientRaidStarted", StringComparison.Ordinal),
            "the exact arm token and received native end identity are confirmed before Arena gameplay becomes active");
        Assert(Regex.IsMatch(session,
                @"if\s*\(\s*PitFireTeamCompat\.HasFrozenTeamRoster\s*\)\s*\{[\s\S]{0,600}\}\s*else\s+if\s*\([^\)]*LocalGame\s+game\s*\)\s*\{[\s\S]{0,300}LiveKitRespawn\.AtRaidStart\s*\(\s*game\s*\)"),
            "team Arena bypasses initial player-body replacement while solo keeps it");
        Assert(localStart.Contains("var requestedMode = Settings.SelectedGameModeId", StringComparison.Ordinal)
               && HasFrozenArmCall(localStart)
               && session.Contains("ActiveMode = armedMode", StringComparison.Ordinal)
               && matchService.Contains("PitFireTeamCompat.TeamArenaActive", StringComparison.Ordinal)
               && matchService.Contains("modeId = \"tactical\"", StringComparison.Ordinal)
               && matchService.Contains("ModeRegistry.Create(modeId)", StringComparison.Ordinal),
            "client freezes the server-armed mode across loading and forces Tactical for a protected team roster");
        Assert(matchService.Contains("Current != null || TeamStartAbortPending", StringComparison.Ordinal)
               && matchService.Contains("game.Status != GameStatus.Started", StringComparison.Ordinal)
               && matchService.IndexOf("TeamStartAbortPending", StringComparison.Ordinal)
                  < matchService.IndexOf("var protectedTeam", StringComparison.Ordinal),
            "duplicate lifecycle dispatch cannot restart a committed failed team start in a Stopping raid");
        string raidStart = Slice(
            session,
            "private static void OnRaidStarted(GameWorld world)",
            "private static void ForceExistingGroupsHostile(GameWorld world)");
        string beforeArmedBranch = raidStart[..raidStart.IndexOf(
            "if (!ArmedForNextRaid)",
            StringComparison.Ordinal)];
        Assert(session.Contains("private static GameWorld _startedWorld", StringComparison.Ordinal)
               && session.Contains("private static AbstractGame _startedGame", StringComparison.Ordinal)
               && session.Contains("internal static bool IsExactActiveRaid(AbstractGame game, GameWorld world)", StringComparison.Ordinal)
               && session.Contains("ReferenceEquals(_startedGame, game)", StringComparison.Ordinal)
               && session.Contains("ReferenceEquals(_startedWorld, world)", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("previousRaidTracked", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("ReferenceEquals(_startedWorld, world)", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("ReferenceEquals(_startedGame, currentGame)", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("ArenaMatchService.RecoverAtNewRaidStart()", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("ArenaRespawnDriver.ResetSession()", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("IsActive = false", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("_startedWorld = world", StringComparison.Ordinal)
               && beforeArmedBranch.Contains("_startedGame = currentGame", StringComparison.Ordinal)
               && !beforeArmedBranch.Contains("ArmedForNextRaid = false", StringComparison.Ordinal)
               && !beforeArmedBranch.Contains("PitFireTeamCompat.Clear(", StringComparison.Ordinal),
            "every different exact GameWorld/game pair clears stale match and raid-epoch state before preserving and processing a new arming");
        var ordinaryClientStart = session.IndexOf("if (!ArmedForNextRaid)", StringComparison.Ordinal);
        var ordinaryClientConfirm = session.IndexOf(
            "ArenaServerClient.ConfirmOrdinaryRaidStarted()",
            ordinaryClientStart,
            StringComparison.Ordinal);
        Assert(ordinaryClientStart >= 0
               && ordinaryClientConfirm > ordinaryClientStart
               && session.IndexOf("ArenaMatchService.RecoverAtNewRaidStart()",
                      ordinaryClientStart,
                      StringComparison.Ordinal) is var matchRecovery
               && matchRecovery > ordinaryClientStart
               && matchRecovery < ordinaryClientConfirm
               && session.IndexOf("ArenaRespawnDriver.ResetSession()",
                      ordinaryClientStart,
                      StringComparison.Ordinal) is var respawnRecovery
               && respawnRecovery > ordinaryClientStart
               && respawnRecovery < ordinaryClientConfirm
               && session.IndexOf("_rejectedServerRaidPendingEnd = false",
                      ordinaryClientStart,
                      StringComparison.Ordinal) is var rejectedRecovery
               && rejectedRecovery > ordinaryClientStart
               && rejectedRecovery < ordinaryClientConfirm
               && matchService.Contains("internal static void RecoverAtNewRaidStart()",
                    StringComparison.Ordinal)
               && Slice(matchService, "internal static void RecoverAtNewRaidStart()",
                   "internal static void ScheduleFailedStartAbort(").Contains("OnRaidEnded();", StringComparison.Ordinal),
            "a proven new ordinary raid boundary clears stale match, abort, respawn and rejected-end state before combat");

        Assert(tactical.Contains("TeamArenaRoundPolicy.Evaluate(", StringComparison.Ordinal)
               && tactical.Contains("_teamRoundsToWin", StringComparison.Ordinal)
               && tactical.Contains("TeamArenaRoundContinuation.RebindNextRound", StringComparison.Ordinal)
               && tactical.Contains("EnterTeamRebinding()", StringComparison.Ordinal)
               && tactical.Contains("EndTeamRaid()", StringComparison.Ordinal),
            "protected team Tactical scores first-to-N rounds and rebuilds before ending the overall match");
        Assert(tactical.Contains("if (_teamArena)", StringComparison.Ordinal)
               && tactical.Contains("PitFireTeamCompat.TeamArenaActive", StringComparison.Ordinal)
               && tactical.Contains("LiveKitRespawn.AtRoundBreak", StringComparison.Ordinal)
               && tactical.Contains("TeamArenaLoadoutSnapshot.TryCapture(", StringComparison.Ordinal),
            "team and solo Tactical paths keep distinct loadout and body lifecycle contracts");
        Assert(tactical.Contains("public bool HoldRespawn => _playerDown", StringComparison.Ordinal)
               && tactical.Contains("_teamArena && _phase == Phase.Aftermath", StringComparison.Ordinal)
               && tactical.Contains("_teamArena && _phase == Phase.Rebinding", StringComparison.Ordinal)
               && tactical.Contains("EndTeamRound(null, \"the local player died before both complete teams were ready\")",
                    StringComparison.Ordinal)
               && tactical.Contains("TryStartTeamSpectator()", StringComparison.Ordinal),
            "local death is held while surviving teams decide the round and live deaths may start spectator mode");
        Assert(compat.Contains("TryGetLiveFollowers", StringComparison.Ordinal)
               && compat.Contains("active unselected PitFireTeam follower", StringComparison.Ordinal)
               && compat.Contains("duplicate active follower profile identity", StringComparison.Ordinal)
               && compat.Contains("spawned unexpected profile", StringComparison.Ordinal)
               && compat.Contains("CurrentProfilesByAccountId", StringComparison.Ordinal)
               && compat.Contains("CurrentBodiesByAccountId", StringComparison.Ordinal)
               && compat.Contains("outside a round transition", StringComparison.Ordinal)
               && compat.Contains("GetEverExistedPlayerByID", StringComparison.Ordinal)
               && compat.Contains("registered a replacement body", StringComparison.Ordinal)
               && compat.Contains("TryBindLocalPlayerBody", StringComparison.Ordinal)
               && compat.Contains("GetBossMethodName", StringComparison.Ordinal)
               && compat.Contains("BossRealPlayerFieldName", StringComparison.Ordinal)
               && compat.Contains("ReferenceEquals(leaderPlayer, currentLocalPlayer)", StringComparison.Ordinal)
               && compat.Contains("split the four Arena followers", StringComparison.Ordinal)
               && tactical.Contains("TryBindLocalPlayerBody(_boundLocalPlayer", StringComparison.Ordinal)
               && tactical.Contains("EndTeamRound(null, rosterProblem)", StringComparison.Ordinal),
            "each current-round physical roster rejects replacement bodies and stale, split or wrong PFT leader wrappers as no-contest");
        Assert(compat.Contains("CanonicalProfilesByAccountId", StringComparison.Ordinal)
               && compat.Contains("CurrentProfilesByAccountId", StringComparison.Ordinal)
               && compat.Contains("KnownProfileIds", StringComparison.Ordinal)
               && compat.Contains("CanonicalCompanionProfilesByAccountId", StringComparison.Ordinal)
               && compat.Contains("CurrentCompanionProfilesByAccountId", StringComparison.Ordinal)
               && Regex.IsMatch(compat,
                   @"HasCompleteCanonicalRosterUnsafe\s*\(\s*\)[\s\S]{0,300}CanonicalAccountIds\.SetEquals\(CanonicalProfilesByAccountId\.Keys\)")
               && !Regex.IsMatch(compat,
                   @"HasCompleteCanonicalRosterUnsafe\s*\(\s*\)[\s\S]{0,300}KnownProfileIds\.Count"),
            "the immutable authenticated map, current-round identities and cross-round profile history are separate invariants");
        Assert(compat.Contains("BeginRoundTransition(", StringComparison.Ordinal)
               && compat.Contains("RoundTransitionActive", StringComparison.Ordinal)
               && compat.Contains("if (transitioning)", StringComparison.Ordinal)
               && compat.Contains("LiveTeamArena && !RoundTransitionActive", StringComparison.Ordinal)
               && compat.Contains("CurrentBodiesByAccountId", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycleStage.ContractReady", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycleStage.FollowersReady", StringComparison.Ordinal),
            "an explicit lifecycle-ready transition snapshots all four old bodies and suppresses stale-round discovery latches");
        int adoptionHistoryCommit = compat.IndexOf(
            "KnownProfileIds.Add(pair.Value)",
            StringComparison.Ordinal);
        int adoptionTransitionClose = compat.IndexOf(
            "_roundTransitionActive = false",
            adoptionHistoryCommit,
            StringComparison.Ordinal);
        Assert(compat.Contains("TryAdoptRoundRoster(", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycle.Status", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycle.GetCurrentFollowers", StringComparison.Ordinal)
               && compat.Contains("lifecycle.Stage != PitFireTeamRoundLifecycleStage.FollowersReady", StringComparison.Ordinal)
               && compat.Contains("CanonicalAccountIds.SetEquals(runtimeProfiles.Keys)", StringComparison.Ordinal)
               && compat.Contains("KnownProfileIds.Contains(profileId)", StringComparison.Ordinal)
               && compat.Contains("bool reusesCurrentLeader", StringComparison.Ordinal)
               && compat.Contains("bool adoptsPendingLeader", StringComparison.Ordinal)
               && Regex.IsMatch(compat,
                   @"bool\s+reusesCurrentLeader\s*=\s*ReferenceEquals\(_pendingLocalPlayer,\s*null\)[\s\S]{0,220}ReferenceEquals\(_currentLocalPlayer,\s*currentLeader\)")
               && compat.Contains("!reusesCurrentLeader && !adoptsPendingLeader", StringComparison.Ordinal)
               && compat.Contains("ReferenceEquals(world.MainPlayer, currentLeader)", StringComparison.Ordinal)
               && compat.Contains("world.RegisteredPlayers.Any(player => ReferenceEquals(player, currentLeader))", StringComparison.Ordinal)
               && compat.Contains("world.GetEverExistedPlayerByID(currentLeaderProfileId)", StringComparison.Ordinal)
               && compat.Contains("world.GetEverExistedPlayerByID(profileId)", StringComparison.Ordinal)
               && compat.Contains("new followers do not share the exact current local PFT leader wrapper", StringComparison.Ordinal)
               && adoptionHistoryCommit >= 0
               && adoptionTransitionClose > adoptionHistoryCommit,
            "new runtime identities become current/history only after FollowersReady and independent body, registry and leader proof");
        Assert(compat.Contains("PersistenceSuppressionActive", StringComparison.Ordinal)
               && compat.Contains("=> TeamArenaActive || IsFinalizing || ProtectedAbortPending", StringComparison.Ordinal)
               && compat.Contains("if (!PersistenceSuppressionActive)", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycle.Configure(assembly, pluginVersion)", StringComparison.Ordinal)
               && compat.Contains("roundCapability.CanStartAdditionalRound", StringComparison.Ordinal)
               && roundLifecycle.Contains("RequirePersistenceSuppressionUnsafe", StringComparison.Ordinal)
               && roundLifecycle.Contains("PitFireTeam persistence suppression", StringComparison.Ordinal),
            "the exact round contract and active persistence guard are required by both compatibility and lifecycle paths");
        Assert(compat.Contains("CanonicalAccountIds.Clear()", StringComparison.Ordinal)
               && compat.Contains("CanonicalProfilesByAccountId.Clear()", StringComparison.Ordinal)
               && compat.Contains("CurrentProfilesByAccountId.Clear()", StringComparison.Ordinal)
               && compat.Contains("CurrentBodiesByAccountId.Clear()", StringComparison.Ordinal)
               && compat.Contains("KnownProfileIds.Clear()", StringComparison.Ordinal)
               && compat.Contains("_pendingLocalPlayer = null", StringComparison.Ordinal)
               && compat.Contains("_roundTransitionActive = false", StringComparison.Ordinal)
               && compat.Contains("PitFireTeamRoundLifecycle.Reset()", StringComparison.Ordinal),
            "freeze/clear boundaries reset canonical, current, historical, pending and lifecycle round state");

        int beginRoundTransition = tactical.IndexOf(
            "PitFireTeamCompat.BeginRoundTransition(",
            StringComparison.Ordinal);
        int beginTeardown = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.BeginTeardown(",
            beginRoundTransition,
            StringComparison.Ordinal);
        int pollRetirement = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.PollRetirement(",
            beginTeardown,
            StringComparison.Ordinal);
        int rebuildLeader = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.TryRebuildBoss(",
            pollRetirement,
            StringComparison.Ordinal);
        int beginProfiles = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.BeginFollowerProfilePreparation(",
            rebuildLeader,
            StringComparison.Ordinal);
        int pollProfiles = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.PollFollowerProfilePreparation(",
            beginProfiles,
            StringComparison.Ordinal);
        int startFollowers = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.TryStartFollowerSpawn(",
            pollProfiles,
            StringComparison.Ordinal);
        int pollFollowers = tactical.IndexOf(
            "PitFireTeamRoundLifecycle.PollFollowers(",
            startFollowers,
            StringComparison.Ordinal);
        int adoptFollowers = tactical.IndexOf(
            "PitFireTeamCompat.TryAdoptRoundRoster(",
            pollFollowers,
            StringComparison.Ordinal);
        Assert(beginRoundTransition >= 0
               && beginTeardown > beginRoundTransition
               && pollRetirement > beginTeardown
               && rebuildLeader > pollRetirement
               && beginProfiles > rebuildLeader
               && pollProfiles > beginProfiles
               && startFollowers > pollProfiles
               && pollFollowers > startFollowers
               && adoptFollowers > pollFollowers,
            "Tactical advances monotonically through PFT teardown, retirement, leader rebuild, fresh follower spawn and adoption");
        string startTeamFollowers = Slice(
            tactical,
            "private void StartTeamFollowers()",
            "private void TickTeamFollowers()");
        Assert(startTeamFollowers.Contains("TeamFollowerAssemblyTimeout", StringComparison.Ordinal)
               && !startTeamFollowers.Contains("TeamLifecycleTimeout", StringComparison.Ordinal)
               && tactical.Contains(
                   "private static readonly TimeSpan TeamFollowerAssemblyTimeout = TimeSpan.FromSeconds(20);",
                   StringComparison.Ordinal)
               && roundLifecycle.Contains("DescribeIncompleteFollowerAssemblyUnsafe(", StringComparison.Ordinal)
               && roundLifecycle.Contains("DescribeWeaponReadinessUnsafe(", StringComparison.Ordinal)
               && roundLifecycle.Contains("weaponReady={weapons.IsReady}", StringComparison.Ordinal)
               && roundLifecycle.Contains(
                   "PitFireTeam follower assembly stalled with an unarmed body:",
                   StringComparison.Ordinal)
               && roundLifecycle.Contains("IsSettledWithoutFirearmUnsafe(", StringComparison.Ordinal)
               // The measured wait is about four seconds; a ninety-second hold of
               // the scheduler's force mode over a body that never activates was
               // tried and bought nothing, so the boost calls no follower API.
               && !boost.Contains("PitFireTeamRoundLifecycle", StringComparison.Ordinal)
               && !boost.Contains("using ArenaMode.Compat;", StringComparison.Ordinal),
            "the follower wait is bounded near the measured assembly time, names the missing body with its native weapon readiness, and does not hold the load boost");
        Assert(tactical.Contains("HaveExactBodySet(oldFollowers, _boundTeamFollowers)", StringComparison.Ordinal)
               && tactical.Contains("PitFireTeamCompat.CanonicalCompanionProfilesByAccountId", StringComparison.Ordinal)
               && tactical.Contains("status.Epoch != _expectedPitLifecycleEpoch", StringComparison.Ordinal)
               && tactical.Contains("status.Capability?.CanStartAdditionalRound != true", StringComparison.Ordinal),
            "each rebuild binds the exact completed-round bodies, canonical accounts and one expected lifecycle epoch");
        Assert(tactical.Contains("_boundLocalPlayer", StringComparison.Ordinal)
               && tactical.Contains("ReferenceEquals(current, _boundLocalPlayer)", StringComparison.Ordinal)
               && tactical.Contains("_boundTeamEnemies", StringComparison.Ordinal)
               && tactical.Contains("TryValidateBoundTeamEnemies", StringComparison.Ordinal)
               && tactical.Contains("replacement body for opponent", StringComparison.Ordinal)
               && tactical.Contains("GetEverExistedPlayerByID(_boundLocalProfileId)", StringComparison.Ordinal)
               && tactical.Contains("previously registered replacement body for opponent", StringComparison.Ordinal),
            "each live round stays bound to its exact current local PMC, four followers and five opponent bodies");
        var pendingActivationBranch = tactical.IndexOf(
            "if (freezeDecision == TeamOpponentActivationDecision.Pending)",
            StringComparison.Ordinal);
        var readyActivationBranch = tactical.IndexOf(
            "else if (PitFireTeamCompat.EnsureHostility(_boundTeamEnemies, _boundLocalPlayer))",
            pendingActivationBranch,
            StringComparison.Ordinal);
        Assert(pendingActivationBranch >= 0
               && readyActivationBranch > pendingActivationBranch
               && !tactical.Substring(
                       pendingActivationBranch,
                       readyActivationBranch - pendingActivationBranch)
                   .Contains("EndTeamRound", StringComparison.Ordinal)
               && tactical.Contains("enemy.BotState == EBotState.PreActive", StringComparison.Ordinal)
               && tactical.Contains("enemy.BotState == EBotState.Active", StringComparison.Ordinal)
               && tactical.Contains("TeamOpponentActivationDecision.Invalid", StringComparison.Ordinal)
               && tactical.Contains("registered.Any(candidate => ReferenceEquals(candidate, bodies[index]))",
                   StringComparison.Ordinal)
               && tactical.Contains("world.GetEverExistedPlayerByID(profiles[index])", StringComparison.Ordinal)
               && tactical.Contains("ReferenceEquals(everExisted, players[index])", StringComparison.Ordinal)
               && tactical.Contains("Time.time + TeamOpponentActivationSeconds", StringComparison.Ordinal)
               && tactical.Contains("last check: {_lastTeamReadinessDiagnostic}", StringComparison.Ordinal),
            "PreActive opponents receive a bounded activation window without ending staging while terminal or replaced bodies fail closed and timeout stays diagnostic");
        string requestTeamEnemies = Slice(
            tactical,
            "private void RequestTeamEnemies()",
            "private void TickTeamLive()");
        Assert(requestTeamEnemies.Contains(
                   "_phaseEndsAt = Time.time + TeamAssemblySeconds",
                   StringComparison.Ordinal)
               && !requestTeamEnemies.Contains(
                   "Mathf.Max(20f, SquadGraceSeconds)",
                   StringComparison.Ordinal),
            "the exact-five opponent request retains the full team assembly window while profile generation is in flight");
        Assert(burstSpawn.Contains("profile {profileId}, state {bot.BotState}", StringComparison.Ordinal)
               && burstSpawn.Contains("callback normally observes", StringComparison.Ordinal)
               && burstSpawn.Contains("includeLifecycleIdentity: requireExactCount", StringComparison.Ordinal),
            "exact-squad outcome logs expose callback profile and lifecycle state without expanding ambient burst logs");
        Assert(combat.Contains("new TeamStagingShotPatch().Enable()", StringComparison.Ordinal)
               && combat.Contains("new TeamStagingPlayerDamagePatch().Enable()", StringComparison.Ordinal)
               && combat.Contains("new TeamStagingExplosionArmorPatch().Enable()", StringComparison.Ordinal)
               && combat.Contains("new TeamStagingDamagePatch().Enable()", StringComparison.Ordinal)
               && combat.Contains("new TeamStagingContusionPatch().Enable()", StringComparison.Ordinal)
               && combat.Contains("nameof(Player.ApplyShot)", StringComparison.Ordinal)
               && combat.Contains("nameof(Player.ApplyDamageInfo)", StringComparison.Ordinal)
               && combat.Contains("nameof(Player.ApplyExplosionDamageToArmor)", StringComparison.Ordinal)
               && combat.Contains("nameof(ActiveHealthController.ApplyDamage)", StringComparison.Ordinal)
               && combat.Contains("nameof(ActiveHealthController.DoContusion)", StringComparison.Ordinal)
               && combat.Contains("ShouldBlockProtectedTeamMutation", StringComparison.Ordinal)
               && tactical.Contains("ShouldBlockStagingDamage", StringComparison.Ordinal),
            "team staging/aftermath blocks shots, Player side effects, explosion armor, health and contusion before mutation");
        Assert(Regex.IsMatch(tactical,
                   @"ShouldBlockStagingDamage\s*\([^\)]*\)[\s\S]{0,500}Phase\.Staging[\s\S]{0,120}Phase\.Aftermath[\s\S]{0,120}Phase\.Rebinding")
               && Regex.IsMatch(tactical,
                   @"FailClosedStagingGuard\s*\([^\)]*\)[\s\S]{0,900}Phase\.Rebinding[\s\S]{0,200}FailTeamTransition\s*\(")
               && Regex.IsMatch(tactical,
                   @"Aftermath already has\s*//\s*its one immutable result"),
            "damage stays blocked throughout staging, result presentation and rebuild while a rebind guard failure becomes no-contest");
        Assert(Regex.IsMatch(tactical,
                @"TickTeamLive\s*\(\)[\s\S]{0,4000}if\s*\(\s*!PitFireTeamCompat\.EnsureHostility\s*\(\s*_boundTeamEnemies\s*,\s*_boundLocalPlayer\s*\)\s*\)[\s\S]{0,180}EndTeamRound\s*\(\s*null"),
            "loss of the live bidirectional hostility matrix ends as no-contest");
        string tacticalTick = Slice(
            tactical,
            "public void Tick(float deltaTime)",
            "private void TickLive()");
        Assert(tacticalTick.Contains(
                   "_teamContinuation == TeamArenaRoundContinuation.RebindNextRound",
                   StringComparison.Ordinal)
               && tacticalTick.Contains("EnterTeamRebinding()", StringComparison.Ordinal)
               && tacticalTick.Contains("EndTeamRaid()", StringComparison.Ordinal)
               && tacticalTick.Contains("EnterBuy()", StringComparison.Ordinal),
            "team aftermath rebuilds after a non-terminal result, ends only the overall match, and leaves solo buy flow intact");

        string respawnRoutine = Slice(
            respawn,
            "private static IEnumerator RespawnRoutine(",
            "private static IEnumerator RespawnProtectedTeamPlayer(");
        int stoppedRaidExit = respawnRoutine.IndexOf(
            "if (!IsSameActiveRaid(game, raidEpoch) || StandDown)",
            StringComparison.Ordinal);
        int frozenRosterGate = respawnRoutine.IndexOf(
            "if (PitFireTeamCompat.HasFrozenTeamRoster)",
            StringComparison.Ordinal);
        int takeAuthorization = respawnRoutine.IndexOf(
            "ArenaMatchService.TryTakeTeamRespawnAuthorization(",
            StringComparison.Ordinal);
        int protectedRespawn = respawnRoutine.IndexOf(
            "RespawnProtectedTeamPlayer(",
            takeAuthorization,
            StringComparison.Ordinal);
        int soloKitSelection = respawnRoutine.IndexOf(
            "KitCatalog.GetSelectedKit()",
            StringComparison.Ordinal);
        Assert(stoppedRaidExit >= 0
               && frozenRosterGate > stoppedRaidExit
               && takeAuthorization > frozenRosterGate
               && protectedRespawn > takeAuthorization
               && soloKitSelection > protectedRespawn,
            "only a live frozen roster with Tactical's one-shot lease reaches protected respawn before the untouched solo kit path");

        string protectedTeamRespawn = Slice(
            respawn,
            "private static IEnumerator RespawnProtectedTeamPlayer(",
            "private static void AbortProtectedTeamRespawn(");
        Assert(protectedTeamRespawn.Contains(
                   "prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result)",
                   StringComparison.Ordinal)
               && protectedTeamRespawn.Contains("AbortProtectedTeamRespawn(", StringComparison.Ordinal)
               && protectedTeamRespawn.Contains("ArenaEvents.RaisePlayerRespawned(respawnedPlayer)", StringComparison.Ordinal)
               && !protectedTeamRespawn.Contains("KitCatalog", StringComparison.Ordinal)
               && !protectedTeamRespawn.Contains("KitBundlePreloader", StringComparison.Ordinal)
               && !protectedTeamRespawn.Contains("ReplaceEquipment", StringComparison.Ordinal)
               && !protectedTeamRespawn.Contains("InvokeDeathScreen", StringComparison.Ordinal),
            "protected Redeploy commits only the prepared entry loadout and fails through the protected abort coordinator");

        string teamRetirement = Slice(
            tactical,
            "private void TickTeamRetirement()",
            "private TeamRetirementDecision ObserveTeamEnemyRetirement");
        int validateDeadLeader = teamRetirement.IndexOf(
            "TryValidateDeadTeamLeaderBody(_teamGame, deadPlayer",
            StringComparison.Ordinal);
        int armPreparedLoadout = teamRetirement.IndexOf(
            "_teamLoadoutSnapshot.TryArm(_teamRoundEpoch",
            StringComparison.Ordinal);
        int prepareLoadout = teamRetirement.IndexOf(
            "_teamLoadoutSnapshot.TryPrepare(",
            StringComparison.Ordinal);
        int publishAuthorization = teamRetirement.IndexOf(
            "_teamRespawnAuthorizationAvailable = true",
            StringComparison.Ordinal);
        int releaseRespawn = teamRetirement.IndexOf(
            "_playerDown = false",
            StringComparison.Ordinal);
        Assert(validateDeadLeader >= 0
               && armPreparedLoadout > validateDeadLeader
               && prepareLoadout > armPreparedLoadout
               && publishAuthorization > prepareLoadout
               && releaseRespawn > publishAuthorization,
            "the complete prepared loadout and exact lease are published before Tactical releases the held respawn coroutine");

        string authorization = Slice(
            tactical,
            "internal bool TryTakeTeamRespawnAuthorization(",
            "public void OnMatchEnd()");
        Assert(authorization.Contains("!ReferenceEquals(game, _teamGame)", StringComparison.Ordinal)
               && authorization.Contains("!ReferenceEquals(deadPlayer, _authorizedDeadTeamPlayer)", StringComparison.Ordinal)
               && authorization.Contains("_preparedTeamRespawn.RoundEpoch != _teamRoundEpoch", StringComparison.Ordinal)
               && authorization.Contains("lifecycle.Epoch != _expectedPitLifecycleEpoch", StringComparison.Ordinal)
               && authorization.Contains("TryValidateDeadTeamLeaderBody(game, deadPlayer", StringComparison.Ordinal)
               && authorization.Contains("_teamRespawnAuthorizationAvailable = false", StringComparison.Ordinal)
               && authorization.Contains("_teamRespawnAuthorizationConsumed = true", StringComparison.Ordinal)
               && matchService.Contains("Current?.Mode is not TacticalMode tactical", StringComparison.Ordinal)
               && matchService.Contains("tactical.TryTakeTeamRespawnAuthorization(", StringComparison.Ordinal),
            "respawn authorization is exact-body, exact-game, exact-round, exact-PFT-epoch and one-shot through Tactical only");
        Assert(tactical.Contains("TryValidateCurrentTeamLeaderBody(player, out string bodyProblem)", StringComparison.Ordinal)
               && tactical.Contains("the current local PMC drifted before PitFireTeam leader rebind", StringComparison.Ordinal)
               && tactical.Contains("the current local PMC drifted before PitFireTeam follower adoption", StringComparison.Ordinal)
               && tactical.Contains("world.RegisteredPlayers.Count(candidate =>", StringComparison.Ordinal)
               && tactical.Contains("world.GetEverExistedPlayerByID(profileId)", StringComparison.Ordinal)
               && tactical.Contains("ReferenceEquals(world.MainPlayer, deadPlayer)", StringComparison.Ordinal)
               && tactical.Contains("ReferenceEquals(world.MainPlayer, expectedPlayer)", StringComparison.Ordinal),
            "dead and living local bodies are re-proven against MainPlayer, active registrations and persistent profile identity at every destructive boundary");
        Assert(modeInterface.Contains("void OnLocalRespawn(Player player);", StringComparison.Ordinal)
               && arenaMatch.Contains("Mode.OnLocalRespawn(player)", StringComparison.Ordinal)
               && matchService.Contains("Current?.ReportLocalRespawn(player)", StringComparison.Ordinal),
            "the actual replacement body is propagated from the respawn event into Tactical's rebind proof");

        string endTeamRaid = Slice(
            tactical,
            "private void EndTeamRaid()",
            "private KitDefinition Affordable()");
        int standDown = endTeamRaid.IndexOf("ArenaRespawnDriver.StandDown = true", StringComparison.Ordinal);
        int beginFinalizing = endTeamRaid.IndexOf("PitFireTeamCompat.BeginFinalizing()", StringComparison.Ordinal);
        int nativeStop = endTeamRaid.IndexOf("game.Stop(", StringComparison.Ordinal);
        Assert(endTeamRaid.Contains("ReferenceEquals(localGame, _teamGame)", StringComparison.Ordinal)
               && endTeamRaid.Contains("_teamMatchWon == true", StringComparison.Ordinal)
               && !endTeamRaid.Contains("_teamWon == true", StringComparison.Ordinal)
               && endTeamRaid.Contains("ExitStatus.Survived", StringComparison.Ordinal)
               && endTeamRaid.Contains("ExitStatus.MissingInAction", StringComparison.Ordinal)
               && endTeamRaid.Contains("ExitStatus.Killed", StringComparison.Ordinal)
               && standDown >= 0
               && beginFinalizing > standDown
               && nativeStop > beginFinalizing
               && !endTeamRaid.Contains("single elimination round", StringComparison.Ordinal),
            "only the frozen LocalGame exits, using the overall best-of-N result after respawn and persistence guards stand down");
        Assert(tactical.Contains("bool committed = game != null && game.Status != GameStatus.Started", StringComparison.Ordinal)
               && tactical.Contains("PitFireTeamCompat.CancelFinalizingAfterFailedStop", StringComparison.Ordinal)
               && tactical.Contains("ArenaRespawnDriver.StandDown = false", StringComparison.Ordinal)
               && compat.Contains("BotsControllerStopPatch", StringComparison.Ordinal)
               && compat.Contains("PitStopCleanupFinalizer", StringComparison.Ordinal)
               && compat.Contains("BuildStopCleanupContract", StringComparison.Ordinal)
               && compat.Contains("LocalGameCtorPatch", StringComparison.Ordinal)
               && compat.Contains("FollowerTransitStateCache", StringComparison.Ordinal),
            "pre-commit stop failures retry while committed teardown remains protected and contains PFT cleanup errors");
        Assert(Regex.IsMatch(compat,
                @"IsTeamProfileId\s*\([^\)]*\)[\s\S]{0,350}!LiveTeamArena\s*&&\s*!IsFinalizing"),
            "known teammate corpses remain protected through bounded finalization");

        Assert(matchService.Contains("ValidateProtectedTeamRaid", StringComparison.Ordinal)
               && matchService.Contains("ScheduleFailedStartAbort", StringComparison.Ordinal)
               && matchService.Contains("TeamStartAbortPending", StringComparison.Ordinal)
               && matchService.Contains("localGame.ProfileId", StringComparison.Ordinal)
               && matchService.Contains("PitFireTeamCompat.BeginFinalizing()", StringComparison.Ordinal)
               && combat.Contains("ArenaMatchService.TeamStartAbortPending", StringComparison.Ordinal)
               && compat.Contains("ProtectedAbortPending", StringComparison.Ordinal)
               && session.Contains("PitFireTeamCompat.HasFrozenTeamRoster", StringComparison.Ordinal),
            "failed protected initialization uses a stable profile id and keeps damage/PFT guards live until Stop commits");
        Assert(locationPatch.Contains("MarkLocationMutationFailed", StringComparison.Ordinal)
               && locationPatch.Contains("MarkLocationMutationReady", StringComparison.Ordinal)
               && router.Contains("locationReady", StringComparison.Ordinal)
               && serverClient.Contains("ValidateProtectedTeamRaid", StringComparison.Ordinal),
            "server location mutation failure is signalled and prevents a protected 5v5 with vanilla waves");

        Assert(serverClient.Contains("pitFireTeamTeam", StringComparison.Ordinal)
               && localStart.Contains("ArenaMenuSelection.CompatibilityTeamArenaActive", StringComparison.Ordinal)
               && plugin.Contains("RegisterTeamArenaQuery(() => PitFireTeamCompat.TeamArenaActive)",
                   StringComparison.Ordinal),
            "client sends the frozen team-Arena flag in the raid-start request");
        Assert(router.Contains("[JsonPropertyName(\"pitFireTeamTeam\")]", StringComparison.Ordinal)
               && router.Contains("public bool PitFireTeamTeam", StringComparison.Ordinal),
            "server request binds the explicit PitFireTeam team flag");
        var policyGate = router.IndexOf("PitFireTeamRaidStartPolicy.GetRejectionReason", StringComparison.Ordinal);
        var preparation = router.IndexOf("Sessions.BeginArmPreparation", StringComparison.Ordinal);
        var snapshot = router.IndexOf("Snapshots.TakeSnapshot", StringComparison.Ordinal);
        var protectedArm = router.IndexOf("Sessions.CompleteArmPreparation", StringComparison.Ordinal);
        Assert(policyGate >= 0 && preparation > policyGate && snapshot > preparation
               && protectedArm > snapshot,
            "server reserves PFT protection before snapshot and completes the arm only afterwards");
        Assert(router.Contains("PitFireTeamDynamicPersistencePatches.ProtectionReady", StringComparison.Ordinal)
               && router.Contains("PitFireTeamDynamicPersistencePatches.IncompatibilityReason", StringComparison.Ordinal),
            "raid-start router evaluates the complete dynamic persistence contract");
        Assert(router.Contains("Sessions.BeginArmPreparation", StringComparison.Ordinal)
               && router.Contains("Sessions.CompleteArmPreparation", StringComparison.Ordinal)
               && sessionService.Contains("ArenaSessionState.Preparing", StringComparison.Ordinal)
               && sessionService.Contains("ProtectPitFireTeam", StringComparison.Ordinal)
               && protectedEnd.Contains("CompleteNativeEndFinalization", StringComparison.Ordinal),
            "every Arena arm publishes Preparing before snapshot while only an explicit team arm enables PFT protection");
        Assert(operationGate.Contains("ReaderWriterLockSlim", StringComparison.Ordinal)
               && operationGate.Contains("ReserveProfileBoundary", StringComparison.Ordinal)
               && dynamicPersistence.Contains("PitFireTeamOperationBoundaryPatch", StringComparison.Ordinal)
               && dynamicPersistence.IndexOf("PitFireTeamOperationBoundaryPatch", StringComparison.Ordinal)
                  < dynamicPersistence.IndexOf("foreach (MethodInfo target in raidTargets)", StringComparison.Ordinal),
            "all exact PFT operations drain before the pre-snapshot protection reservation");
        Assert(operationGate.Contains("PitFireTeamInviteTaskRunPatch", StringComparison.Ordinal)
               && operationGate.Contains("TryWrapCurrentTaskRun", StringComparison.Ordinal)
               && operationGate.Contains("CurrentTaskRunIsTracked", StringComparison.Ordinal)
               && dynamicPersistence.Contains("inviteTaskRun", StringComparison.Ordinal)
               && dynamicPersistence.Contains("PitFireTeamInviteTaskRunPatch", StringComparison.Ordinal),
            "PFT's discarded delayed invite Task is registered before scheduling and drained through mail completion");
        Assert(sessionService.Contains("RefreshOpenProtectedBoundariesLocked", StringComparison.Ordinal)
               && sessionService.Contains("entry.ProtectPitFireTeam", StringComparison.Ordinal)
               && sessionService.Contains("entry.State != ArenaSessionState.Idle", StringComparison.Ordinal),
            "an open protected session remains cross-store guarded after the low-level TTL");
        Assert(snapshots.Contains("profiles.TryUpdate(sessionId, snapshot, current)", StringComparison.Ordinal)
               && snapshots.Contains("retry retained", StringComparison.Ordinal)
               && !snapshots.Contains("saveServer.DeleteProfileById(sessionId)", StringComparison.Ordinal)
               && startLocalRaid.Contains("explicit token-bound recovery", StringComparison.Ordinal)
               && protectedStart.Contains("StartOrdinaryAsync", StringComparison.Ordinal)
               && protectedStart.Contains("CutOverToOrdinaryRaid", StringComparison.Ordinal)
               && sessionService.Contains("ClearExternalProfileProtection", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("OrdinaryRaidPhase.Starting", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("OrdinaryRaidPhase.InRaid", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("OrdinaryRaidPhase.Ending", StringComparison.Ordinal)
               && postRaid.Contains("local-end remains retryable", StringComparison.Ordinal),
            "snapshot restoration is atomic, retryable, and every stale/failed transition blocks a new raid");
        Assert(protocol.Contains("NativeStartIdentityPrefix", StringComparison.Ordinal)
               && protocol.Contains("NativeEndIdentitySeparator", StringComparison.Ordinal)
               && protocol.Contains("HasNativeStartMarker", StringComparison.Ordinal)
               && protocol.Contains("HasNativeEndMarker", StringComparison.Ordinal)
               && localStart.Contains("BindNativeStartIdentity(settings)", StringComparison.Ordinal)
               && localStart.Contains("BindNativeEndIdentity(result)", StringComparison.Ordinal)
               && startLocalRaid.Contains("typeof(MatchCallbacks)", StringComparison.Ordinal)
               && startLocalRaid.Contains("transaction.WaitAsync(cancellationToken)", StringComparison.Ordinal)
               && protectedStart.Contains("ArenaRaidStartGate.EnterAsync(sessionId, CancellationToken.None)", StringComparison.Ordinal)
               && protectedStart.Contains("ProtectedStartTimeout", StringComparison.Ordinal)
               && protectedStart.Contains("MatchController", StringComparison.Ordinal)
                && protectedStart.Contains("BindNativeEndIdentity", StringComparison.Ordinal)
                && protectedStart.Contains("response.ServerId = exactNativeEndIdentity", StringComparison.Ordinal)
                && protectedStart.Contains("sessions.OwnsNativeStartTransaction", StringComparison.Ordinal)
                && router.Contains("TryReplayArmedRequest", StringComparison.Ordinal)
                && router.Contains("replayedArmToken", StringComparison.Ordinal)
                && sessionService.Contains("public bool TryReplayArmedRequest", StringComparison.Ordinal)
                && serverClient.Contains("A false/malformed/lost response is not proof", StringComparison.Ordinal)
                && sessionService.Contains("RejectedArenaStart", StringComparison.Ordinal)
                && sessionService.Contains("NativeStartTaskSettled", StringComparison.Ordinal)
                && sessionService.Contains("public bool OwnsNativeStartTransaction", StringComparison.Ordinal),
            "the complete async native start is shared, caller-cancellation-safe, fail-closed, preserves the owner response cache and binds SPT's exact returned server id");
        Assert(protocol.Contains("NativeOrdinaryStartIdentityPrefix", StringComparison.Ordinal)
               && localStart.Contains("BindOrdinaryNativeStartIdentity(settings)", StringComparison.Ordinal)
               && serverClient.Contains("if (!IsServerModKnownAvailable)", StringComparison.Ordinal)
               && startLocalRaid.Contains("ordinaryRaids.GetOrStart", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("A different ordinary raid generation", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("_retiredStarts", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("CurrentEndExecution", StringComparison.Ordinal)
               && endLocalRaid.Contains("nameof(MatchCallbacks.EndLocalRaidAsync)", StringComparison.Ordinal)
               && endLocalRaid.Contains("TryGetOrStartEnd", StringComparison.Ordinal)
               && router.Contains("OrdinaryRaidActive", StringComparison.Ordinal)
               && router.Contains("HandleOrdinaryRecovery", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("the owning client process cannot abandon", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("failed and remains exact-retryable", StringComparison.Ordinal)
               && !ordinaryLifecycle.Contains("Released stale ordinary raid lifecycle", StringComparison.Ordinal),
            "ordinary start/end retries are exact-generation serialized, server-absent fail-open, crash-recoverable and never tombstone failed vanilla persistence");
        Assert(protectedStart.Contains("sessions.CutOverToOrdinaryRaid(", StringComparison.Ordinal)
               && ordinaryLifecycle.Contains("sessions.CutOverToOrdinaryRaid(", StringComparison.Ordinal)
               && sessionService.Contains("internal void CutOverToOrdinaryRaid(", StringComparison.Ordinal)
               && Regex.IsMatch(sessionService,
                   @"CutOverToOrdinaryRaid[\s\S]{0,600}ReserveProfileBoundary\s*\(\s*\)[\s\S]{0,600}IsArmedOrInRaid[\s\S]{0,300}hasSnapshot\s*\(\s*\)[\s\S]{0,400}ClearExternalProfileProtection")
               && protectedStart.IndexOf("sessions.CutOverToOrdinaryRaid(", StringComparison.Ordinal)
                  < protectedStart.IndexOf("await matchController.StartLocalRaidAsync(", StringComparison.Ordinal)
               && ordinaryLifecycle.IndexOf("sessions.CutOverToOrdinaryRaid(", StringComparison.Ordinal)
                  < ordinaryLifecycle.IndexOf("await matchController.StartLocalRaidAsync(", StringComparison.Ordinal),
            "both ordinary starts drain older PFT readers and release the thread-affine writer before native async work");
        var ordinaryEndMethod = ordinaryLifecycle.IndexOf(
            "public bool TryGetOrStartEnd(",
            StringComparison.Ordinal);
        var ordinaryActiveEndClaim = ordinaryLifecycle.IndexOf(
            "if (!_entries.TryGetValue(key, out Entry? entry))",
            ordinaryEndMethod,
            StringComparison.Ordinal);
        var ordinaryCompletedEnd = ordinaryLifecycle.IndexOf(
            "if (IsCompletedEndLocked(key, identity))",
            ordinaryEndMethod,
            StringComparison.Ordinal);
        Assert(ordinaryEndMethod >= 0
               && ordinaryActiveEndClaim > ordinaryEndMethod
               && ordinaryCompletedEnd > ordinaryActiveEndClaim
               && ordinaryLifecycle.Contains(
                   "An active generation always outranks an older success",
                   StringComparison.Ordinal),
            "a current ordinary generation outranks a reused native-id tombstone so its vanilla end cannot be skipped");
        Assert(postRaid.Contains("nameof(LocationLifecycleService.EndLocalRaidAsync)", StringComparison.Ordinal)
               && postRaid.Contains("ClassifyLocalRaidEnd", StringComparison.Ordinal)
               && postRaid.Contains("ArenaLocalRaidEndDisposition.CompletedArenaEndRetry", StringComparison.Ordinal)
               && postRaid.Contains("ArenaRaidStartGate.Enter(sessionId)", StringComparison.Ordinal)
               && postRaid.Contains("WaitAsync(cancellationToken)", StringComparison.Ordinal)
               && sessionService.Contains("_completedNativeEnds", StringComparison.Ordinal)
               && sessionService.Contains("BeginNativeEndFinalization", StringComparison.Ordinal)
               && sessionService.Contains("IsCompletedNativeEndArm", StringComparison.Ordinal)
               && sessionService.Contains("TryGetFinalizingNativeEnd", StringComparison.Ordinal)
               && sessionService.Contains("entry.NativeEndIdentity", StringComparison.Ordinal)
               && protectedEnd.Contains("SaveProfileAsync", StringComparison.Ordinal)
               && protectedEnd.Contains("ProtectedEndTimeout", StringComparison.Ordinal)
               && protectedEnd.Contains("transactionTimeout.Token", StringComparison.Ordinal)
               && protectedEnd.Contains("RunIntervalValues.OutOfRaid", StringComparison.Ordinal)
               && protectedEnd.Contains("CompleteNativeEndFinalization", StringComparison.Ordinal)
               && router.Contains("GetOrStart(sessionId, finalizingIdentity!)", StringComparison.Ordinal)
               && router.Contains("ResetNonProfileLifecycle", StringComparison.Ordinal)
               && router.Contains("PersistRestoredProfileAfterFailedStartAsync", StringComparison.Ordinal)
               && matchService.Contains("_teamStartAbortCommitted", StringComparison.Ordinal)
               && serverClient.Contains("HasPendingEnd", StringComparison.Ordinal)
               && serverClient.Contains("completed", StringComparison.Ordinal),
            "native local-end is an exact-id serialized transaction with cancellation-independent persistence, recovery and retained acknowledgement");
        Assert(nativeEndAdmission.IndexOf("BeginNativeEndFinalization", StringComparison.Ordinal)
               < nativeEndAdmission.IndexOf("RestoreSnapshot", StringComparison.Ordinal)
               && nativeEndAdmission.Contains("AbortNativeEndFinalization", StringComparison.Ordinal)
               && postRaid.Contains("ArenaNativeEndAdmission.TryClaimAndRestore", StringComparison.Ordinal)
               && router.Contains("ArenaNativeEndAdmission.TryClaimAndRestore", StringComparison.Ordinal)
               && sessionService.Contains("AbortNativeEndFinalization", StringComparison.Ordinal)
               && Regex.IsMatch(
                   protocol,
                   @"HasNativeEndMarker\([\s\S]*?NativeEndIdentitySeparator"),
            "native Arena end claims the transaction before consuming the snapshot and tags ends by the exact separator");
        Assert(!postRaid.Contains("HandlePostRaidPmc", StringComparison.Ordinal)
               && protectedEnd.Contains("ordinary post-raid processing was skipped", StringComparison.Ordinal),
            "protected Arena end skips the whole ordinary reward, transfer, health and inventory pipeline");
        Assert(Regex.IsMatch(dynamicPersistence,
                @"ProtectionReady\s*=>\s*IsInstalled\s*&&\s*IsCompatible\s*&&\s*FileMutationPatchesReady"),
            "server protection readiness requires installed, compatible and file-guard-ready PitFireTeam");
        foreach (var method in new[]
                 {
                     "RegisterProtectedRaidItemIds",
                     "HandleReturnItems",
                     "HandleTeamEscaped",
                     "RecordKillMessage",
                     "RegisterProtectedRaidItems",
                     "RemoveProtectedTeammateItemsFromExtractedProfile",
                     "HandleEndLocalRaidKillMessages",
                     "HandleDeathEscapeSummary",
                     "QueueRecruitPickups",
                     "PersistFollowerProgress",
                     "PersistDeathEscapeOutcomes"
                 })
        {
            Assert(dynamicPersistence.Contains($"\"{method}\"", StringComparison.Ordinal),
                $"dynamic persistence contract includes {method}");
        }
        foreach (var method in new[]
                 {
                     "CreateTeammate",
                     "CreateTeammateFromRecruitCandidate",
                     "SelectDefaultLoadoutForAllTeammates",
                     "SetTeammateSuit",
                     "RenameTeammate",
                     "SetTeammateLoadout",
                     "SaveTeammateDefaultEquipment",
                     "BuyTeammateKit",
                     "RepairTeammateDefaultEquipment",
                     "SetTeammateAggression",
                     "SetTeammateTactic",
                     "SetTeammateAutoJoin",
                     "DeleteTeammate",
                     "DeleteTeammateByProfileId",
                     "AcceptRecruitRequest",
                     "AcceptAllRecruitRequests",
                     "DeclineRecruitRequest",
                     "SetServerSettings",
                     "SendGroupInvite"
                 })
        {
            Assert(dynamicPersistence.Contains($"\"{method}\"", StringComparison.Ordinal),
                $"whole-transaction management contract includes {method}");
        }
        foreach (var method in new[]
                 {
                     "RecoverTeammateProfileIfNeeded",
                     "RecoverDuplicateTeammateItemsForSession",
                     "EnsureRecruitRequestAccountIds",
                     "BackupFileBeforeRecovery",
                     "SaveAndApply"
                 })
        {
            Assert(dynamicPersistence.Contains($"\"{method}\"", StringComparison.Ordinal),
                $"hidden/direct persistence contract includes {method}");
        }
        Assert(dynamicPersistence.Contains("ManagementServicePrefix", StringComparison.Ordinal)
               && dynamicPersistence.Contains("ManagementServiceFinalizer", StringComparison.Ordinal)
               && dynamicPersistence.Contains("PitFireTeamManagementScope.Enter", StringComparison.Ordinal)
               && filePersistence.Contains("PitFireTeamManagementScope.Authorizes", StringComparison.Ordinal),
            "postraid menu management is authorized only as a complete exact-session transaction");
        Assert(managementScope.Contains("AsyncLocal", StringComparison.Ordinal)
               && managementScope.Contains("Interlocked.Exchange", StringComparison.Ordinal)
               && managementScope.Contains("string.Equals(token.SessionId, sessionId", StringComparison.Ordinal),
            "management scope follows nested calls, invalidates captured contexts, and binds the exact session");
        Assert(dynamicPersistence.Contains("IsGlobalSettingsTransaction", StringComparison.Ordinal)
               && dynamicPersistence.Contains("IsSoleExternalProfileProtection", StringComparison.Ordinal)
               && dynamicPersistence.Contains("IsGlobalExternalMutationAuthorized", StringComparison.Ordinal),
            "global PFT settings are either authorized for every protected session or blocked as a whole callback");
        foreach (var patch in new[]
                 {
                     "PitFireTeamTextWritePatch",
                     "PitFireTeamByteWritePatch",
                     "PitFireTeamAsyncTextWritePatch",
                     "PitFireTeamAsyncByteWritePatch",
                     "PitFireTeamDeleteFilePatch"
                 })
        {
            Assert(patchManager.Contains($"new {patch}()", StringComparison.Ordinal),
                $"all-or-nothing FileUtil boundary includes {patch}");
        }
        Assert(patchManager.Contains("PitFireTeamDynamicPersistencePatches.TryEnable(", StringComparison.Ordinal)
               && patchManager.Contains("pitFireTeamFilesReady", StringComparison.Ordinal)
               && patchManager.Contains("pitFireTeamFileFailure", StringComparison.Ordinal),
            "server passes the all-or-nothing FileUtil patch result into dynamic readiness");
    }

    private static PitFireTeamSelectionDecision Evaluate(
        PitFireTeamContractState contract,
        IReadOnlyList<string>? native = null,
        IReadOnlyList<string>? selected = null,
        IReadOnlyList<string>? selectedProfiles = null,
        string? mode = "tactical",
        bool fika = false,
        bool coop = false,
        bool allowPendingModeSelection = false)
    {
        return PitFireTeamSelectionPolicy.Evaluate(new PitFireTeamSelectionInput
        {
            Contract = contract,
            IsFika = fika,
            IsCoop = coop,
            GameMode = mode,
            CurrentPlayerAccountId = Local,
            NativeGroupAccountIds = native ?? new[] { Local },
            SelectedPitAccountIds = selected ?? (contract == PitFireTeamContractState.Compatible
                ? Array.Empty<string>()
                : null),
            SelectedPitProfileIds = selectedProfiles ?? (selected != null
                ? selected.Select(id => "profile-" + id).ToArray()
                : contract == PitFireTeamContractState.Compatible
                    ? Array.Empty<string>()
                    : null)
        }, allowPendingModeSelection);
    }

    private static void CheckInstalledRuntimeContract()
    {
        var repo = FindRepositoryRoot();
        var spt = Directory.GetParent(Directory.GetParent(repo)!.FullName)!.FullName;
        var pitPath = Path.Combine(spt, "BepInEx", "plugins", "pitFireTeam", "pitFireTeam.dll");
        var eftPath = Path.Combine(spt, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll");
        var pitServerPath = Path.Combine(
            spt,
            "SPT_Runtime",
            "user",
            "mods",
            "pitFireTeam-ServerMod",
            "pitFireTeam.Server.dll");
        if (!File.Exists(pitPath))
        {
            Console.WriteLine("PITFIRETEAM_RUNTIME_CONTRACT_SKIPPED (optional client plugin is not installed)");
            return;
        }

        if (!File.Exists(eftPath))
            throw new FileNotFoundException("Installed EFT managed assembly is missing.", eftPath);
        if (!File.Exists(pitServerPath))
            throw new FileNotFoundException(
                "Installed PitFireTeam server assembly is missing.",
                pitServerPath);

        using var pit = AssemblyDefinition.ReadAssembly(pitPath);
        using var eft = AssemblyDefinition.ReadAssembly(eftPath);
        using var pitServer = AssemblyDefinition.ReadAssembly(pitServerPath);

        Assert(pit.Name.Name == "pitFireTeam"
               && PitFireTeamVersions.IsSupportedAssemblyVersion(pit.Name.Version),
            "installed PitFireTeam client assembly has one of the exact supported identities");

        var plugin = pit.MainModule.GetType("pitTeam.pitFireTeam")
                     ?? throw new InvalidOperationException("Installed PitFireTeam plugin type is missing.");
        var bepin = plugin.CustomAttributes.Single(attribute =>
            attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
        Assert(bepin.ConstructorArguments.Count == 3
               && (string)bepin.ConstructorArguments[0].Value == "xyz.pit.fireteam"
               && PitFireTeamVersions.IsSupportedPluginVersion((string)bepin.ConstructorArguments[2].Value),
            "installed BepInEx PitFireTeam GUID and metadata version match the adapter");
        // The two identities are forgeable separately, so the installed build must
        // agree with itself before Arena trusts either against its accepted set.
        Assert($"{pit.Name.Version.Major}.{pit.Name.Version.Minor}.{pit.Name.Version.Build}"
               == (string)bepin.ConstructorArguments[2].Value
               && pit.Name.Version.Revision == 0,
            "installed PitFireTeam metadata and assembly versions describe one build");

        var selectedType = pit.MainModule.GetType("pitTeam.Patches.MainMenuControllerPatch")
                           ?? throw new InvalidOperationException("Installed PFT selected-roster type is missing.");
        var selectedField = selectedType.Fields.Single(field => field.Name == "GroupPlayers");
        Assert(selectedField.IsPublic && selectedField.IsStatic
               && selectedField.FieldType is GenericInstanceType selectedList
               && selectedList.GenericArguments.Count == 1
               && selectedList.GenericArguments[0].FullName == "EFT.UI.Matchmaker.RaidPlayer",
            "installed PFT selected roster is the expected public static RaidPlayer list");

        var followerType = pit.MainModule.GetType("pitTeam.Components.BotFollowerPlayer")
                           ?? throw new InvalidOperationException("Installed PFT follower-wrapper type is missing.");
        var bossPlayerType = pit.MainModule.GetType("pitTeam.Components.pitAIBossPlayer")
                             ?? throw new InvalidOperationException("Installed PFT player-boss type is missing.");
        var bossPlayersType = pit.MainModule.GetType("pitTeam.Modules.BossPlayers")
                              ?? throw new InvalidOperationException("Installed PFT BossPlayers type is missing.");
        var getBot = followerType.Methods.Single(method => method.Name == "GetBot" && method.Parameters.Count == 0);
        var getBoss = followerType.Methods.Single(method => method.Name == "GetBoss" && method.Parameters.Count == 0);
        var realPlayer = bossPlayerType.Fields.Single(field => field.Name == "realPlayer");
        var getFollowers = bossPlayersType.Methods.Single(method =>
            method.Name == "GetFollowers" && method.Parameters.Count == 0);
        var followersField = bossPlayersType.Fields.Single(field => field.Name == "_followers");
        var followersByProfileIdField = bossPlayersType.Fields.Single(field =>
            field.Name == "_followersByProfileId");
        Assert(getBot.IsPublic && !getBot.IsStatic
               && getBot.ReturnType.FullName == "EFT.BotOwner"
               && getBoss.IsPublic && !getBoss.IsStatic
               && getBoss.ReturnType.FullName == bossPlayerType.FullName
               && realPlayer.IsPublic && realPlayer.IsInitOnly
               && realPlayer.FieldType.FullName == "EFT.Player"
               && getFollowers.IsPublic && getFollowers.IsStatic
               && getFollowers.ReturnType is GenericInstanceType followerList
               && followerList.GenericArguments.Count == 1
               && followerList.GenericArguments[0].FullName == followerType.FullName,
            "installed PFT exposes the exact follower-to-leader-wrapper and readonly real-player contract");
        Assert(followersField.IsPrivate
               && followersField.FieldType is GenericInstanceType installedFollowerList
               && installedFollowerList.ElementType.FullName == "System.Collections.Generic.List`1"
               && installedFollowerList.GenericArguments.Count == 1
               && installedFollowerList.GenericArguments[0].FullName == followerType.FullName
               && followersByProfileIdField.IsPrivate
               && followersByProfileIdField.FieldType is GenericInstanceType installedFollowerIndex
               && installedFollowerIndex.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
               && installedFollowerIndex.GenericArguments.Count == 2
               && installedFollowerIndex.GenericArguments[0].FullName == "System.String"
               && installedFollowerIndex.GenericArguments[1].FullName == followerType.FullName,
            "installed PFT exposes the exact private follower stores required for disposed-wrapper teardown");

        var raidPlayer = eft.MainModule.GetType("EFT.UI.Matchmaker.RaidPlayer")
                         ?? throw new InvalidOperationException("Installed EFT RaidPlayer type is missing.");
        var groupPlayer = eft.MainModule.GetType("EFT.GroupPlayer")
                          ?? throw new InvalidOperationException("Installed EFT GroupPlayer type is missing.");
        Assert(raidPlayer.BaseType?.FullName == groupPlayer.FullName
               && groupPlayer.Fields.Any(field => field.Name == "AccountId"
                                                   && field.IsPublic
                                                   && field.FieldType.FullName == "System.String")
               && groupPlayer.Fields.Any(field => field.Name == "Id"
                                                   && field.IsPublic
                                                   && field.FieldType.FullName == "System.String")
               && raidPlayer.Properties.All(property => property.Name is not "AccountId" and not "Id"),
            "installed EFT exposes RaidPlayer AccountId and Id as inherited public fields");

        var raidSettings = eft.MainModule.GetType("EFT.RaidSettings")
                           ?? throw new InvalidOperationException("Installed EFT RaidSettings type is missing.");
        var cloneSettings = raidSettings.Methods.Single(method =>
            method.Name == "Clone"
            && method.Parameters.Count == 0
            && method.ReturnType.FullName == raidSettings.FullName);
        var applySettings = raidSettings.Methods.Single(method =>
            method.Name == "Apply"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == raidSettings.FullName);
        static bool WritesTransitionType(MethodDefinition method)
            => method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Name == "stfld"
                && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == "EFT.RaidSettings"
                && field.Name == "transitionType");
        Assert(!WritesTransitionType(cloneSettings) && WritesTransitionType(applySettings),
            "installed EFT has the exact transitionType Clone omission and Apply write repaired by Arena");

        var eftPlayer = eft.MainModule.GetType("EFT.Player")
                        ?? throw new InvalidOperationException("Installed EFT Player type is missing.");
        var activeHealth = eft.MainModule.GetType("EFT.HealthSystem.ActiveHealthController")
                           ?? throw new InvalidOperationException(
                               "Installed EFT ActiveHealthController type is missing.");
        var effectsController = eft.MainModule.GetType("EffectsController")
                                ?? throw new InvalidOperationException(
                                    "Installed EFT EffectsController type is missing.");
        var effectAccumulator = effectsController.NestedTypes.Single(type =>
            type.Name == "EffectAccumulator");
        var bloodOnScreen = eft.MainModule.GetType("BloodOnScreen")
                            ?? throw new InvalidOperationException(
                                "Installed EFT BloodOnScreen type is missing.");
        var bloodDropsController = eft.MainModule.GetType("BloodDropsController")
                                   ?? throw new InvalidOperationException(
                                       "Installed EFT BloodDropsController type is missing.");
        Assert(effectsController.Fields.Any(field =>
                   field.Name == "player" && field.FieldType.FullName == "EFT.Player")
               && effectsController.Fields.Any(field =>
                   field.Name == "_effectAccumulators"
                   && field.FieldType.FullName
                   == "System.Collections.Generic.List`1<EffectsController/EffectAccumulator>")
               && effectAccumulator.Fields.Any(field =>
                   field.Name == "ActiveEffects"
                   && field.FieldType.FullName
                   == "System.Collections.Generic.List`1<EFT.HealthSystem.IHealthEffect>")
               && effectAccumulator.Methods.Any(method =>
                   method.Name == "DeleteEffect"
                   && method.IsPublic
                   && method.IsVirtual
                   && method.Parameters.Count == 1
                   && method.Parameters[0].ParameterType.FullName
                   == "EFT.HealthSystem.IHealthEffect"),
            "installed EFT exposes the exact bound-player and native visual-effect deletion contract");
        Assert(bloodOnScreen.Fields.Any(field =>
                   field.Name == "_bloodDropsController"
                   && field.FieldType.FullName == "BloodDropsController")
               && bloodOnScreen.Fields.Any(field =>
                   field.Name == "_duDvMap"
                   && field.FieldType.FullName == "UnityEngine.RenderTexture")
               && bloodOnScreen.Fields.Any(field =>
                   field.Name == "_lastHitTime"
                   && field.FieldType.FullName == "System.Single")
               && bloodOnScreen.Fields.Any(field =>
                   field.Name == "_lastHeavyBleedingHitTime"
                   && field.FieldType.FullName == "System.Single")
               && bloodOnScreen.Fields.Any(field =>
                   field.Name == "_locked"
                   && field.FieldType.FullName == "System.Boolean")
               && bloodDropsController.Fields.Any(field => field.Name == "_dropsToRender")
               && bloodDropsController.Fields.Any(field => field.Name == "_freeDrops")
               && bloodDropsController.Fields.Any(field =>
                   field.Name == "_dropsTex"
                   && field.FieldType.FullName == "UnityEngine.RenderTexture"),
            "installed EFT exposes the reversible blood-overlay queue, texture and timing contract");
        Assert(eftPlayer.Methods.Any(method =>
                   method.Name == "ApplyDamageInfo"
                   && method.IsPublic
                   && method.IsVirtual
                   && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(new[]
                        {
                            "EFT.Ballistics.DamageInfo",
                            "EBodyPart",
                            "EBodyPartColliderType",
                            "System.Single",
                        }))
               && eftPlayer.Methods.Any(method =>
                   method.Name == "ApplyExplosionDamageToArmor"
                   && method.IsPublic
                   && method.IsVirtual
                   && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                       .SequenceEqual(new[]
                        {
                            "System.Collections.Generic.Dictionary`2<EFT.ExplosionDamageInfo,System.Single>",
                            "EFT.Ballistics.DamageInfo",
                        }))
               && activeHealth.Methods.Any(method =>
                   method.Name == "DoContusion"
                   && method.IsPublic
                   && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                       .SequenceEqual(new[] { "System.Single", "System.Single" })),
            "installed EFT 4.1 exposes the exact pre-side-effect, explosion-armor and contusion guard signatures");

        var pftDamageOverrides = DescendantTypes(pit.MainModule.Types)
            .SelectMany(type => type.Methods)
            .Where(method => (method.Name is "ApplyDamageInfo" or "ApplyExplosionDamageToArmor")
                              && method.IsVirtual
                              && !method.IsNewSlot)
            .ToArray();
        Assert(pftDamageOverrides.Length == 0,
            "installed PFT derived player bodies do not bypass the guarded EFT virtual methods");

        var localPlayer = eft.MainModule.GetType("EFT.LocalPlayer")
                          ?? throw new InvalidOperationException("Installed EFT LocalPlayer type is missing.");
        Assert(localPlayer.BaseType?.FullName == eftPlayer.FullName
               && localPlayer.Methods.All(method =>
                   method.Name is not "ApplyShot"
                       and not "ApplyDamageInfo"
                       and not "ApplyExplosionDamageToArmor"),
            "installed offline LocalPlayer inherits every guarded EFT Player damage surface without an override");

        var squadFlow = pit.MainModule.GetType("pitTeam.Modules.SquadSideSelectionFlow")
                        ?? throw new InvalidOperationException("Installed PFT squad-flow type is missing.");
        var squadMode = squadFlow.Properties.Single(property => property.Name == "SquadModeActive");
        Assert(squadMode.PropertyType.FullName == "System.Boolean"
               && squadMode.GetMethod?.IsPublic == true
               && squadMode.GetMethod.IsStatic,
            "installed PFT exposes its own public static My Squad marker");
        Assert(squadFlow.Methods.Any(method => method.Name == "Open"
                                               && method.IsPublic
                                               && method.IsStatic
                                               && method.ReturnType.FullName == "System.Void"
                                               && method.Parameters.Count == 0),
            "installed PFT exposes the independent synchronous My Squad Open boundary");

        var inviteCallbacks = pitServer.MainModule.GetType(
                                  "pitTeam.Server.Callbacks.FriendlyTeammateMatchCallbacks")
                              ?? throw new InvalidOperationException(
                                  "Installed PFT invite callback type is missing.");
        var sendGroupInvite = inviteCallbacks.Methods.Single(method =>
            method.Name == "SendGroupInvite" && method.Parameters.Count == 4);
        var taskRunCalls = sendGroupInvite.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(method => method.DeclaringType.FullName == "System.Threading.Tasks.Task"
                             && method.Name == "Run")
            .ToArray();
        Assert(taskRunCalls.Length == 2
               && taskRunCalls.All(method => method.Parameters.Count == 1
                                              && method.Parameters[0].ParameterType.FullName
                                              == "System.Func`1<System.Threading.Tasks.Task>"),
            "installed PFT SendGroupInvite uses exactly the two tracked Task.Run(Func<Task>) boundaries");

        foreach (var (typeName, methodName) in new[]
                 {
                     ("pitTeam.Modules.InteractableObjects", "Dispose"),
                     ("pitTeam.Modules.NpcMessage", "Dispose"),
                     ("pitTeam.Modules.BossPlayers", "Dispose"),
                     ("pitTeam.Utils.PingTeamates", "Disable"),
                     ("pitTeam.Utils.Enemy", "ClearEnemiesLocations"),
                     ("pitTeam.Utils.Utils", "FlagsClear"),
                     ("pitTeam.Utils.Utils", "ValuesClear"),
                     ("pitTeam.Modules.TeammateAutoJoinRuntime", "ClearAllSuppression"),
                     ("pitTeam.Modules.FollowerTransitStateCache", "Clear"),
                     ("pitTeam.Modules.BattleRecorder", "EndRaid"),
                 })
        {
            var type = pit.MainModule.GetType(typeName)
                       ?? throw new InvalidOperationException($"Installed PFT cleanup type {typeName} is missing.");
            Assert(type.Methods.Any(method => method.Name == methodName && method.IsStatic),
                $"installed PFT stop-recovery contract includes {typeName}.{methodName}");
        }

        foreach (var (typeName, fieldName) in new[]
                 {
                     ("pitTeam.Patches.BotsControllerPatch", "spawnedPlayers"),
                     ("pitTeam.Patches.BotsControllerPatch", "followerCreationTask"),
                     ("pitTeam.Patches.BotsControllerPatch", "alliesCreationTask"),
                     ("pitTeam.Patches.BotsControllerPatch", "bossCreationTask"),
                     ("pitTeam.Patches.BotsControllerPatch", "Controller"),
                     ("pitTeam.Patches.BotOwnerManualUpdatePatch", "BotOwnerUpdate"),
                     ("pitTeam.Patches.MainMenuControllerPatch", "GroupPlayers"),
                     ("pitTeam.Patches.MainMenuControllerPatch", "TransitPlayers"),
                     ("pitTeam.Utils.SpawnHelper", "spawnMemberIds"),
                     ("pitTeam.Utils.SpawnHelper", "spawnMemberIdsScav"),
                     ("pitTeam.Utils.SpawnHelper", "spawnMemberIdsBoss"),
                     ("pitTeam.Patches.BotsEventsControllerSpawnPatch", "squadSpawned"),
                     ("pitTeam.Patches.LocalGameCtorPatch", "Instance"),
                 })
        {
            var type = pit.MainModule.GetType(typeName)
                       ?? throw new InvalidOperationException($"Installed PFT cleanup type {typeName} is missing.");
            Assert(type.Fields.Any(field => field.Name == fieldName && field.IsStatic),
                $"installed PFT stop-recovery contract includes {typeName}.{fieldName}");
        }

        var bossPlayers = pit.MainModule.GetType("pitTeam.Modules.BossPlayers")!;
        var bossInstance = bossPlayers.Properties.Single(property => property.Name == "Instance");
        Assert(bossInstance.SetMethod?.IsStatic == true,
            "installed PFT BossPlayers singleton can be force-reset after a failed Dispose");
    }

    private static string[] Native(IEnumerable<string> companions)
        => new[] { Local }.Concat(companions).ToArray();

    private static IEnumerable<TypeDefinition> DescendantTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in DescendantTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private static void AssertSolo(PitFireTeamSelectionDecision decision, string scenario)
    {
        Assert(decision.Allowed && !decision.IsTeam
               && decision.CompanionAccountIds.Count == 0,
            scenario);
    }

    private static void AssertRejected(PitFireTeamSelectionDecision decision, string scenario)
    {
        Assert(!decision.Allowed && !decision.IsTeam
               && decision.CompanionAccountIds.Count == 0
               && !string.IsNullOrWhiteSpace(decision.Reason),
            scenario);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var origin in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(origin);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate ArenaMode.sln from the working or output directory.");
    }

    private static string Read(string repo, string relativePath)
    {
        var path = Path.Combine(repo, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required production source is missing: {relativePath}", path);
        return File.ReadAllText(path);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Production source lost method marker: {startMarker}");

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"Production source lost following marker: {endMarker}");

        return source.Substring(start, end - start);
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException(scenario);
        _assertions++;
    }
}
