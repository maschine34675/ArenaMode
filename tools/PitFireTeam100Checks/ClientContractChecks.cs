using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using HarmonyLib;
using Mono.Cecil;
using pitTeam.Modules;

internal static class ClientContractChecks
{
    internal static async Task Verify(string file, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(file);
        check(assembly.Name.Version == new Version(1, 0, 0, 0), "Exact released PFT client identity.");
        var plugin = assembly.MainModule.GetType("pitTeam.pitFireTeam").CustomAttributes.Single(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin");
        check((string)plugin.ConstructorArguments[0].Value == "xyz.pit.fireteam" && (string)plugin.ConstructorArguments[2].Value == "1.0.0", "Exact released plugin GUID/version.");
        foreach ((string type, string method, string result, string[] parameters) in new[]
        {
            ("pitTeam.Modules.PmcKarmaRuntime", "BeginRaid", "System.Void", new[] { "System.Boolean" }),
            ("pitTeam.Modules.PmcKarmaRuntime", "Send", "System.Void", new[] { "pitTeam.Shared.PmcKarmaReport", "EFT.Player" }),
            ("pitTeam.Modules.FriendlyEncounterPenaltyRuntime", "SendKillReport", "System.Threading.Tasks.Task", new[] { "System.String", "System.String", "System.String", "System.Int64", "System.String" }),
            ("pitTeam.Modules.GiveGearInsurance", "Cancel", "System.Void", new[] { "EFT.IEftSession", "EFT.Profile", "System.Collections.Generic.IEnumerable`1<System.String>" }),
            ("pitTeam.Modules.GiveGearInsurance", "Track", "System.Void", new[] { "EFT.IEftSession", "EFT.Player", "EFT.BotOwner", "System.Collections.Generic.IEnumerable`1<System.String>" }),
            ("pitTeam.Modules.FollowerInsuranceRaidReports", "Prepare", "pitTeam.Modules.FollowerInsuranceRaidReports/Report", new[] { "System.String" }),
            ("pitTeam.Modules.FollowerInsuranceRaidReports", "Complete", "System.Void", Array.Empty<string>()),
            ("pitTeam.Modules.FollowerInsuranceRaidReports/Report", "Send", "System.String", new[] { "System.String" }),
            // Existing physical round lifecycle seams, checked against 1.0 bytes.
            ("pitTeam.Modules.BossPlayers", "RemoveBossPlayer", "System.Boolean", new[] { "System.String", "System.Boolean" }),
            ("pitTeam.Modules.BossPlayers", "RemoveFollower", "System.Void", new[] { "EFT.BotOwner", "pitTeam.Components.pitAIBossPlayer" }),
            ("pitTeam.Modules.BossPlayers", "AddPlayerAsBoss", "pitTeam.Components.pitAIBossPlayer", new[] { "EFT.Player", "EFT.BotsController" }),
            ("pitTeam.Components.BotFollowerPlayer", "Dismiss", "System.Void", new[] { "System.Boolean" }),
            ("pitTeam.Components.pitAIBossPlayer", "UnhookFollowerDeath", "System.Void", new[] { "EFT.BotOwner" }),
            ("pitTeam.Patches.BotsControllerPatch", "CreateFollowerProfiles", "System.Threading.Tasks.Task`1<System.Collections.Generic.Dictionary`2<System.String,EFT.Profile>>", new[] { "pitTeam.Components.pitAIBossPlayer" }),
            ("pitTeam.Patches.BotsControllerPatch", "SpawnGroupBots", "System.Threading.Tasks.Task", new[] { "pitTeam.Components.pitAIBossPlayer" }),
        })
        {
            var target = assembly.MainModule.GetType(type);
            check(target != null && target.Methods.Count(candidate => candidate.Name == method && candidate.ReturnType.FullName == result
                && candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)) == 1,
                "PFT 1.0 client contract: " + type + "." + method);
        }
        foreach ((string type, string field, string fieldType) in new[]
        {
            ("pitTeam.Modules.BossPlayers", "_followers", "System.Collections.Generic.List`1<pitTeam.Components.BotFollowerPlayer>"),
            ("pitTeam.Modules.BossPlayers", "_followersByProfileId", "System.Collections.Generic.Dictionary`2<System.String,pitTeam.Components.BotFollowerPlayer>"),
            ("pitTeam.Components.pitAIBossPlayer", "realPlayer", "EFT.Player"),
            ("pitTeam.Patches.BotsControllerPatch", "Controller", "EFT.BotsController"),
            ("pitTeam.Modules.FollowerInsuranceRaidReports/Report", "Finished", "System.Threading.Tasks.TaskCompletionSource`1<System.Boolean>"),
        })
            check(assembly.MainModule.GetType(type).Fields.Any(candidate => candidate.Name == field && candidate.FieldType.FullName == fieldType), "Client field: " + type + "." + field);

        check(PitFireTeamReportGuard.AdmissionProblem(false, "optional absent") == null
            && PitFireTeamReportGuard.AdmissionProblem(true, "missing capture boundary") != null,
            "Missing modern report guards reject solo Arena too; absent/legacy PFT is unaffected.");
        PitFireTeamReportGuard.Configure(Assembly.GetExecutingAssembly(), new Harmony("arena.pft100.client-fixture"));
        check(PitFireTeamReportGuard.AdmissionProblem(true, "ignored") == null, "Valid modern report guard permits selection.");
        PmcKarmaRuntime.BeginRaid();
        PmcKarmaRuntime.Capture();
        GiveGearInsurance.Cancel(null!, new(), []);
        var ordinary = FollowerInsuranceRaidReports.Prepare("ordinary");
        check(PmcKarmaRuntime.Sent == 1 && GiveGearInsurance.LocalMutations == 1, "Ordinary PMC/Scav karma and local insurance unchanged.");
        ArenaServerClient.HasPendingArm = true;
        PmcKarmaRuntime.BeginRaid();
        ArenaServerClient.HasPendingArm = false;
        ArenaSession.IsActive = true;
        check(PitFireTeamReportGuard.SuppressAutomaticReportCapture, "Solo Arena also suppresses legacy progress/recruit capture before its async worker.");
        PmcKarmaRuntime.Capture();
        GiveGearInsurance.Cancel(null!, new(), []);
        GiveGearInsurance.Track(null!, new(), new(), []);
        await FriendlyEncounterPenaltyRuntime.SendKillReport("", "", "", 0, "");
        FollowerInsuranceRaidReports.Complete();
        var protectedReport = FollowerInsuranceRaidReports.Prepare("arena");
        check(PmcKarmaRuntime.Sent == 1 && GiveGearInsurance.LocalMutations == 1 && FriendlyEncounterPenaltyRuntime.Sent == 0
            && FollowerInsuranceRaidReports.Completed == 0, "All new Arena queue entries/local insurance mutation are suppressed after pending-arm clears.");
        check(GiveGearInsurance.Tracked == 0, "Arena gifts cannot be retained for a delayed EndRaid insurance cancellation.");
        check(protectedReport.Finished.Task.IsCompletedSuccessfully, "Suppressed report completes PFT's local barrier.");
        ArenaSession.IsActive = false;
        GiveGearInsurance.Cancel(null!, new(), []);
        check(GiveGearInsurance.LocalMutations == 2, "Post-Arena deliberate menu insurance work is not held by the automatic-report latch.");
        check(GiveGearInsurance.LastReport.Send("menu insurance") == "ordinary", "Menu insurance response and local mutation remain one transaction.");
        ArenaServerClient.HasPendingEnd = true;
        PitFireTeamCompat.PersistenceSuppressionActive = true;
        PmcKarmaRuntime.BeginRaid(); // ordinary next raid resets even a prior finalization latch
        check(!PitFireTeamReportGuard.SuppressAutomaticReportCapture, "Ordinary next raid releases legacy progress/recruit capture.");
        PmcKarmaRuntime.Capture();
        string suppressed = protectedReport.Send("late");
        check(PmcKarmaRuntime.Sent == 2 && suppressed.Contains("\"err\":0") && FollowerInsuranceRaidReports.Sent == 1,
            "New ordinary raid resumes; an old Arena report remains suppressed by identity.");
        check(ordinary.Send("ordinary") == "ordinary" && FollowerInsuranceRaidReports.Sent == 2,
            "Previously captured ordinary report is never reclassified by current Arena scope.");
        ArenaServerClient.HasPendingEnd = false;
        PitFireTeamCompat.PersistenceSuppressionActive = false;
        PitFireTeamReportGuard.RollbackConfiguration();
        check(PitFireTeamReportGuard.AdmissionProblem(true, "later adapter rollback") != null,
            "An adapter rollback cannot leave modern solo Arena admitted without report hooks.");
    }
}
