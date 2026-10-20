using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpHDiffPatch.Core;

namespace ArenaStart;

// Native protocol reference: SPT Launcher GameHelper and SessionHelper.
// Preserve SPT's installed prepatch/Core validation; never replace it with a UI decision.
public static class DirectGame
{
    public const string Backend = "https://127.0.0.1:6969";
    internal const string AssemblyPath = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll";
    internal const string DeltaPath = "SPT_Runtime/SPT_Data/Launcher/Patches/SPT-core/" + AssemblyPath + ".delta";

    internal static async Task<string> Login(string root, HttpClient http)
    {
        EnvironmentCopy.ValidateProfileFiles(root);
        var account = JsonNode.Parse(EnvironmentCopy.ReadPrivate(EnvironmentCopy.Inside(root, EnvironmentCopy.AccountPath)))!;
        var id = account["profileId"]!.GetValue<string>();
        var payload = new { username = RuntimeHost.Username };
        var login = await RuntimeHost.Request(http, "/launcher/v2/login", payload);
        if (RuntimeHost.Value(login, "response")?.GetValue<bool>() != true)
            throw new InvalidDataException("The private Arena account could not log in.");
        var details = await RuntimeHost.Request(http, "/launcher/v2/profile", payload);
        ValidateLoginProfile(RuntimeHost.Value(details, "response"), id);
        EnvironmentCopy.ValidateProfileFiles(root);
        return id;
    }

    internal static void ValidateLoginProfile(JsonNode? profile, string expectedId)
    {
        if (!IsProfileId(expectedId) || profile is not JsonObject ||
            RuntimeHost.Value(profile, "profileId")?.GetValue<string>() != expectedId ||
            RuntimeHost.Value(profile, "username")?.GetValue<string>() != RuntimeHost.Username ||
            RuntimeHost.Value(profile, "invalidOrUnloadableProfile")?.GetValue<bool>() == true)
            throw new InvalidDataException("Native login returned an unexpected or unloadable private profile.");
        // A fresh native account can legitimately report wipe=true before character creation.
        // SPT's nullable error flag is normally omitted for healthy profiles; only
        // true reports a load failure. Wrong JSON types still fail GetValue<bool>.
    }

    internal static async Task ValidateVersion(string root, HttpClient http)
    {
        var response = await RuntimeHost.Request(http, "/launcher/v2/version");
        var version = RuntimeHost.Value(response, "response")?.GetValue<string>();
        var core = FileVersionInfo.GetVersionInfo(EnvironmentCopy.Inside(root, "BepInEx/plugins/spt/spt-core.dll"));
        ValidateVersions(version, new Version(core.FileMajorPart, core.FileMinorPart, core.FileBuildPart));
    }

    internal static void ValidateVersions(string? server, Version core)
    {
        if (server == null || !Version.TryParse(server.Split('-')[0].Trim(), out var parsed) ||
            parsed.Major != 4 || parsed.Minor != 1 || parsed.Build < 0 || parsed.Revision >= 0 ||
            parsed.Major != core.Major || parsed.Minor != core.Minor || parsed.Build != core.Build)
            throw new InvalidDataException("SPT server and spt-core.dll versions do not match the supported 4.1 installation.");
    }

    public static void ValidatePatch(string root)
    {
        root = EnvironmentCopy.Root(root);
        var assembly = EnvironmentCopy.Inside(root, AssemblyPath);
        var backup = EnvironmentCopy.Inside(root, AssemblyPath + ".spt-bak");
        var delta = EnvironmentCopy.Inside(root, DeltaPath);
        using var original = SharedAssets.OpenReadLease(backup);
        using var patch = SharedAssets.OpenReadLease(delta);
        using var installed = SharedAssets.OpenReadLease(assembly);
        foreach (var file in new[] { original, patch, installed }) EnvironmentCopy.RequireSingleLink(file.SafeFileHandle);

        var header = HDiffPatch.GetHDiffHeaderInfo(delta).headerInfo;
        if (header.isInputDir || header.isOutputDir || header.oldDataSize != original.Length ||
            header.newDataSize != installed.Length || installed.Length == 0 || installed.Length > 1024L * 1024 * 1024)
            throw new InvalidDataException("The native SPT patch does not match the private game assembly.");

        // Unique private scratch file, never the actual assembly. An interrupted check may
        // leave a .bin here; it cannot be discovered as a game/mod executable.
        var output = EnvironmentCopy.Inside(root, ".arena-start/patch-check-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            using (new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var patcher = new HDiffPatch();
            HDiffPatch.LogVerbosity = Verbosity.Quiet;
            patcher.Initialize(delta);
            patcher.Patch(backup, output, false);
            using var generated = SharedAssets.OpenReadLease(output);
            EnvironmentCopy.RequireSingleLink(generated.SafeFileHandle);
            if (generated.Length != installed.Length || SharedAssets.Hash(generated) != SharedAssets.Hash(installed))
                throw new InvalidDataException("The private game assembly differs from the native SPT patch output. Prepare a fresh environment from a working SPT installation.");
        }
        finally
        {
            // Exact owned file only, no recursive restore/cleanup and no source writes.
            if (File.Exists(output))
            {
                EnvironmentCopy.AssertAncestors(output);
                EnvironmentCopy.InspectPrivate(output);
                File.Delete(output);
            }
        }
    }

    internal static ProcessStartInfo StartInfo(string root, string profileId)
    {
        root = EnvironmentCopy.Root(root);
        if (!IsProfileId(profileId)) throw new InvalidDataException("Invalid private login identity.");
        var start = new ProcessStartInfo(EnvironmentCopy.Inside(root, "EscapeFromTarkov.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false
        };
        start.ArgumentList.Add("-force-gfx-jobs");
        start.ArgumentList.Add("native");
        start.ArgumentList.Add("-token=" + profileId);
        start.ArgumentList.Add("-config=" + JsonSerializer.Serialize(new
        {
            BackendUrl = Backend, Version = "live", MatchingVersion = "live"
        }));
        return start;
    }

    internal static void RemovePrivateHwecho(string root)
    {
        var file = EnvironmentCopy.Inside(root, "EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll");
        if (!File.Exists(file)) return;
        EnvironmentCopy.InspectPrivate(file);
        File.Delete(file);
    }

    private static bool IsProfileId(string value) => value.Length == 24 && value.All(Uri.IsHexDigit);
}
