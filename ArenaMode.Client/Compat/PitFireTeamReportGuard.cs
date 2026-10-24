using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ArenaMode.Session;
using HarmonyLib;

namespace ArenaMode.Compat;

/// <summary>
/// PFT 1.0 can queue account-wide karma and insurance reports, including in solo
/// PMC Arena. Stop those at capture, before a worker can outlive server
/// finalization. Reports already captured for Arena keep their identity marker
/// even after the next ordinary raid starts. Ordinary reports remain untouched.
/// </summary>
internal static class PitFireTeamReportGuard
{
    private sealed class ArenaReport { }
    private static readonly ConditionalWeakTable<object, ArenaReport> SuppressedReports = new();
    private static FieldInfo _reportFinished = null!;
    private static bool _arenaRaid;
    [ThreadStatic] private static int _ordinaryInsuranceDepth;
    internal static bool IsReady { get; private set; }

    internal static void RollbackConfiguration() => IsReady = false;

    internal static string AdmissionProblem(bool required, string problem)
        => required && !IsReady
            ? "PitFireTeam report protection is unavailable for Arena. " + problem
            : null!;

    private static bool CurrentProtection
        => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid
           || ArenaServerClient.HasPendingArm
           || (_arenaRaid && (ArenaServerClient.HasPendingEnd || PitFireTeamCompat.PersistenceSuppressionActive));
    private static bool SuppressCurrent => _arenaRaid || CurrentProtection;
    internal static bool SuppressAutomaticReportCapture => IsReady && SuppressCurrent;

    internal static void Configure(Assembly assembly, Harmony harmony)
    {
        if (assembly.GetName().Version < new Version(1, 0, 0, 0)) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        MethodInfo Require(string typeName, string name, string result, params string[] arguments)
        {
            Type type = assembly.GetType("pitTeam.Modules." + typeName, true)!;
            MethodInfo[] matches = type.GetMethods(flags | BindingFlags.Instance).Where(method => method.Name == name
                && method.ReturnType.FullName == result
                && method.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(arguments)).ToArray();
            return matches.Length == 1 ? matches[0] : throw new MissingMethodException(type.FullName, name);
        }
        MethodInfo begin = Require("PmcKarmaRuntime", "BeginRaid", "System.Void", "System.Boolean");
        MethodInfo karma = Require("PmcKarmaRuntime", "Send", "System.Void", "pitTeam.Shared.PmcKarmaReport", "EFT.Player");
        MethodInfo penalty = Require("FriendlyEncounterPenaltyRuntime", "SendKillReport", "System.Threading.Tasks.Task",
            "System.String", "System.String", "System.String", "System.Int64", "System.String");
        MethodInfo insurance = Require("GiveGearInsurance", "Cancel", "System.Void", "EFT.IEftSession", "EFT.Profile",
            typeof(System.Collections.Generic.IEnumerable<string>).FullName!);
        MethodInfo trackInsurance = Require("GiveGearInsurance", "Track", "System.Void", "EFT.IEftSession", "EFT.Player", "EFT.BotOwner",
            typeof(System.Collections.Generic.IEnumerable<string>).FullName!);
        MethodInfo prepare = Require("FollowerInsuranceRaidReports", "Prepare", "pitTeam.Modules.FollowerInsuranceRaidReports+Report", "System.String");
        MethodInfo send = Require("FollowerInsuranceRaidReports+Report", "Send", "System.String", "System.String");
        MethodInfo complete = Require("FollowerInsuranceRaidReports", "Complete", "System.Void");
        _reportFinished = prepare.ReturnType.GetField("Finished", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(prepare.ReturnType.FullName, "Finished");
        if (_reportFinished.FieldType != typeof(TaskCompletionSource<bool>))
            throw new MissingFieldException(prepare.ReturnType.FullName, "Finished");
        HarmonyMethod Patch(string name) => new(typeof(PitFireTeamReportGuard).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(begin, prefix: Patch(nameof(BeginPrefix)));
        harmony.Patch(karma, prefix: Patch(nameof(VoidPrefix)));
        harmony.Patch(insurance, prefix: Patch(nameof(InsurancePrefix)), finalizer: Patch(nameof(InsuranceFinalizer)));
        harmony.Patch(trackInsurance, prefix: Patch(nameof(CurrentProtectionPrefix)));
        harmony.Patch(complete, prefix: Patch(nameof(VoidPrefix)));
        harmony.Patch(penalty, prefix: Patch(nameof(TaskPrefix)));
        harmony.Patch(prepare, postfix: Patch(nameof(PreparePostfix)));
        harmony.Patch(send, prefix: Patch(nameof(SendPrefix)));
        IsReady = true;
    }

    private static void BeginPrefix()
    {
        // Re-evaluate without the prior raid's marker. PFT resets at every new
        // deployment; an ordinary PMC/Scav raid must immediately regain its flow.
        _arenaRaid = ArenaSession.IsActive || ArenaSession.ArmedForNextRaid
                     || ArenaServerClient.HasPendingArm;
    }

    private static bool VoidPrefix() => !SuppressCurrent;
    // Never retain Arena gifts for EndRaid. Unlike automatic report capture,
    // deliberate menu inventory work must resume when current protection ends.
    private static bool CurrentProtectionPrefix() => !CurrentProtection;

    private static bool InsurancePrefix(out bool __state)
    {
        __state = !CurrentProtection;
        if (__state) _ordinaryInsuranceDepth++;
        return __state;
    }

    private static Exception InsuranceFinalizer(Exception __exception, bool __state)
    {
        if (__state) _ordinaryInsuranceDepth--;
        return __exception;
    }

    private static bool TaskPrefix(ref Task __result)
    {
        if (!SuppressCurrent) return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static void PreparePostfix(object __result)
    {
        if (__result == null || !SuppressCurrent || _ordinaryInsuranceDepth > 0) return;
        SuppressedReports.GetValue(__result, _ => new ArenaReport());
        // Complete PFT's local barrier as well, even if its worker is delayed.
        (_reportFinished.GetValue(__result) as TaskCompletionSource<bool>)?.TrySetResult(true);
    }

    private static bool SendPrefix(object __instance, ref string __result)
    {
        if (!SuppressedReports.TryGetValue(__instance, out _)) return true;
        __result = "{\"err\":0,\"errmsg\":null,\"data\":null}";
        return false;
    }
}
