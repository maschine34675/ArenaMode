using ArenaMode.Server.Services;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Utils;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// FileUtil is PitFireTeam 0.10.0's final write boundary. Intercepting every
/// write/delete overload here protects follower profiles even when persistence
/// was queued asynchronously during client teardown. The path policy still
/// fails open unless it resolves into the exact protected session directory.
/// </summary>
internal static class PitFireTeamFileMutationPatch
{
    private const string PitFireTeamModFolder = "pitFireTeam-ServerMod";

    public static bool ShouldRunOriginal(FileUtil fileUtil, string? filePath, string operation)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null
            || !sessions.AnyExternalProfileWriteProtection()
            || string.IsNullOrWhiteSpace(filePath))
            return true;

        string modPath;
        try
        {
            modPath = fileUtil.GetModPath(PitFireTeamModFolder);
        }
        catch (Exception ex)
        {
            // Optional-mod discovery and path resolution must never break an
            // ordinary raid or an installation without PitFireTeam.
            ArenaContext.Logger?.Warning(
                $"[ArenaMode] PitFireTeam {operation} path check failed open: {ex.Message}");
            return true;
        }

        if (!sessions.ShouldBlockPitFireTeamFileMutation(
                filePath,
                modPath,
                out string? protectedSessionId))
        {
            return true;
        }

        // A deliberate menu transaction after the raid has returned to Idle is
        // allowed as one whole unit. The prefix that created this exact-session
        // scope is blocked while Armed/InRaid, so it cannot authorize raid work.
        if (PitFireTeamManagementScope.Authorizes(protectedSessionId))
            return true;

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed PitFireTeam {operation} for protected Arena session {protectedSessionId}.");
        return false;
    }
}

public sealed class ArenaPitFireTeamTextWritePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(FileUtil).GetMethod(
        nameof(FileUtil.WriteFile),
        BindingFlags.Instance | BindingFlags.Public,
        Type.DefaultBinder,
        [typeof(string), typeof(string)],
        null)!;

    [PatchPrefix]
    public static bool Prefix(FileUtil __instance, string __0)
        => PitFireTeamFileMutationPatch.ShouldRunOriginal(__instance, __0, "text write");
}

public sealed class ArenaPitFireTeamByteWritePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(FileUtil).GetMethod(
        nameof(FileUtil.WriteFile),
        BindingFlags.Instance | BindingFlags.Public,
        Type.DefaultBinder,
        [typeof(string), typeof(byte[])],
        null)!;

    [PatchPrefix]
    public static bool Prefix(FileUtil __instance, string __0)
        => PitFireTeamFileMutationPatch.ShouldRunOriginal(__instance, __0, "byte write");
}

public sealed class ArenaPitFireTeamAsyncTextWritePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(FileUtil).GetMethod(
        nameof(FileUtil.WriteFileAsync),
        BindingFlags.Instance | BindingFlags.Public,
        Type.DefaultBinder,
        [typeof(string), typeof(string), typeof(CancellationToken)],
        null)!;

    [PatchPrefix]
    public static bool Prefix(FileUtil __instance, string __0, ref Task __result)
    {
        if (PitFireTeamFileMutationPatch.ShouldRunOriginal(__instance, __0, "async text write"))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

public sealed class ArenaPitFireTeamAsyncByteWritePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(FileUtil).GetMethod(
        nameof(FileUtil.WriteFileAsync),
        BindingFlags.Instance | BindingFlags.Public,
        Type.DefaultBinder,
        [typeof(string), typeof(byte[]), typeof(CancellationToken)],
        null)!;

    [PatchPrefix]
    public static bool Prefix(FileUtil __instance, string __0, ref Task __result)
    {
        if (PitFireTeamFileMutationPatch.ShouldRunOriginal(__instance, __0, "async byte write"))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

public sealed class ArenaPitFireTeamDeleteFilePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(FileUtil).GetMethod(
        nameof(FileUtil.DeleteFile),
        BindingFlags.Instance | BindingFlags.Public,
        Type.DefaultBinder,
        [typeof(string)],
        null)!;

    [PatchPrefix]
    public static bool Prefix(FileUtil __instance, string __0, ref bool __result)
    {
        if (PitFireTeamFileMutationPatch.ShouldRunOriginal(__instance, __0, "delete"))
            return true;

        __result = false;
        return false;
    }
}
