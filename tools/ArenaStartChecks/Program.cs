using ArenaStart;
using System.Text.Json;

var scratch = Path.Combine(Path.GetTempPath(), "ArenaStartChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var source = Path.Combine(scratch, "source");
var destination = Path.Combine(scratch, "arena");
var count = 0;
void Check(bool condition, string message)
{
    count++;
    if (!condition) throw new Exception(message);
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException) { count++; return; }
    throw new Exception("Accepted negative control: " + message);
}
void Put(string relative, string text = "fixture")
{
    var path = Path.Combine(source, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}

try
{
    foreach (var file in EnvironmentCopy.RuntimeFiles) Put("SPT_Runtime/" + file);
    File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, Path.Combine(source, "SPT_Runtime/SPT.Server.exe"), true);
    foreach (var file in EnvironmentCopy.CorePlugins) Put("BepInEx/plugins/spt/" + file);
    foreach (var file in new[] {
        "EscapeFromTarkov.exe", "UnityPlayer.dll", "winhttp.dll", "EscapeFromTarkov_Data/Managed/native.dll",
        "MonoBleedingEdge/EmbedRuntime/mono.dll", "BepInEx/core/BepInEx.dll",
        "SPT_Runtime/SPT_Data/configs/http.json", "SPT_Runtime/SPT_Data/database/templates/profiles.json",
        "BepInEx/plugins/spt/ConfigurationManager/ConfigurationManager.dll",
        "BepInEx/patchers/spt-prepatch.dll", "BepInEx/patchers/FixPluginTypesSerialization.dll",
        "BepInEx/plugins/maschine-Redeploy.Core.dll", "BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll",
        "BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll",
        "BepInEx/plugins/maschine-ArenaMode/announcements/first.ogg", "BepInEx/plugins/maschine-ArenaMode/sounds/hit.wav",
        "BepInEx/plugins/maschine-ArenaMode/docs/licenses/audio.txt",
        "SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll",
        "SPT_Runtime/user/mods/ArenaMode/config/kits.json", "SPT_Runtime/user/mods/ArenaMode/config/ladder.json"
    }) Put(file);
    var forbidden = new[] {
        "SPT_Runtime/user/profiles/private.json", "SPT_Runtime/user/Launcher/LauncherSettings.json",
        "SPT_Runtime/user/mods/PFT/companion.json", "SPT_Runtime/user/mods/Other/Other.dll",
        "SPT_Runtime/user/mods/ArenaMode/config/client-settings.json", "BepInEx/config/main.cfg",
        "BepInEx/patchers/Other.dll", "BepInEx/plugins/Other/Other.dll",
        "BepInEx/plugins/maschine-ArenaMode/AreaDrafts/private.json", "SPT_Runtime/user/certs/server.key"
    };
    foreach (var file in forbidden) Put(file, "PRIVATE ORIGINAL DATA");
    Put("SPT_Runtime/sptLogger.json", """{"loggers":[{"filePath":"C:/original-to-delete"}]}""");
    Put("SPT_Runtime/SPT_Data/configs/backup.json", """{"enabled":true,"directory":"C:/original-to-delete"}""");
    var original = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
        .ToDictionary(p => p, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));

    Reject(() => EnvironmentCopy.Plan(source, source), "same directory");
    Reject(() => EnvironmentCopy.Plan(source, Path.Combine(source, "nested")), "nested destination");
    Reject(() => EnvironmentCopy.Plan(source, scratch), "ancestor destination");
    foreach (var path in new[] { "../private", @"..\private", "C:/private", "/private", "a:stream", "a//b" })
        Reject(() => EnvironmentCopy.Inside(source, path), "path escape " + path);
    var plan = EnvironmentCopy.Plan(source, destination);
    foreach (var path in forbidden) Check(!plan.Files.Contains(path), "private file entered plan: " + path);
    Reject(() => EnvironmentCopy.Prepare(plan with { Files = [.. plan.Files, forbidden[0]] }), "caller-injected profile");
    EnvironmentCopy.Prepare(plan);
    Check(File.Exists(Path.Combine(destination, EnvironmentCopy.ManifestPath)), "final manifest absent");
    EnvironmentCopy.Verify(destination);
    Check(!File.ReadAllText(Path.Combine(destination, "SPT_Runtime/sptLogger.json")).Contains("original-to-delete"), "inherited logger path");
    Check(File.ReadAllText(Path.Combine(destination, "SPT_Runtime/sptLogger.json")).Contains("\"Information\""), "native logging enum name");
    var backupConfig = File.ReadAllText(Path.Combine(destination, "SPT_Runtime/SPT_Data/configs/backup.json"));
    Check(!backupConfig.Contains("original-to-delete") && backupConfig.Contains("arena-profile-backups"), "unsafe backup destination");
    Reject(() => EnvironmentCopy.Prepare(plan), "overwriting existing destination");
    foreach (var path in forbidden) Check(!File.Exists(Path.Combine(destination, path)), "private source file copied");
    foreach (var (path, hash) in original)
        Check(EnvironmentCopy.Hash(path) == hash, "source changed: " + path);
    var extraDll = Path.Combine(destination, "BepInEx/plugins/Unexpected.dll");
    File.WriteAllText(extraDll, "bad");
    Reject(() => EnvironmentCopy.Verify(destination), "foreign client plugin");
    File.Delete(extraDll);
    var extraServer = Path.Combine(destination, "SPT_Runtime/user/mods/Other");
    Directory.CreateDirectory(extraServer);
    File.WriteAllText(Path.Combine(extraServer, "other.dll"), "bad");
    Reject(() => EnvironmentCopy.Verify(destination), "foreign server plugin");
    Directory.Delete(extraServer, true);
    var foreignProfile = Path.Combine(destination, "SPT_Runtime/user/profiles/foreign.json");
    File.WriteAllText(foreignProfile, "{}");
    Reject(() => EnvironmentCopy.Verify(destination), "prebootstrap profile import");
    File.Delete(foreignProfile);
    const string id = "0123456789abcdef01234567";
    EnvironmentCopy.WriteNew(Path.Combine(destination, EnvironmentCopy.AccountPath),
        JsonSerializer.Serialize(new { username = RuntimeHost.Username, profileId = id }));
    var profilePath = Path.Combine(destination, $"SPT_Runtime/user/profiles/{id}.json");
    File.WriteAllText(profilePath, JsonSerializer.Serialize(new { info = new { id, username = RuntimeHost.Username } }));
    EnvironmentCopy.Verify(destination);
    var backup = Path.Combine(destination, "SPT_Runtime/user/arena-profile-backups/native-backup.json");
    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
    File.WriteAllText(backup, "{}");
    EnvironmentCopy.Verify(destination);
    Check(File.Exists(backup), "legitimate native backup removed");
    RuntimeHost.WriteLauncherSettings(destination);
    var launcher = File.ReadAllText(Path.Combine(destination, "SPT_Runtime/user/Launcher/LauncherSettings.json"));
    Check(!launcher.Contains(source.Replace("\\", "\\\\")), "launcher points at source");
    Check(launcher.Contains(id), "launcher doesn't select private account");
    File.WriteAllText(foreignProfile, "{}");
    Reject(() => EnvironmentCopy.Verify(destination), "second imported profile");
    File.Delete(foreignProfile);
    File.WriteAllText(profilePath, "{}");
    Reject(() => EnvironmentCopy.Verify(destination), "profile identity replacement");
    File.WriteAllText(profilePath, JsonSerializer.Serialize(new { info = new { id, username = RuntimeHost.Username } }));
    var originalEnvironment = Environment.GetEnvironmentVariable("BEPINEX_TEST_OVERRIDE");
    try
    {
        Environment.SetEnvironmentVariable("BEPINEX_TEST_OVERRIDE", "outside");
        Reject(RuntimeHost.RequireIdle, "loader override");
    }
    finally { Environment.SetEnvironmentVariable("BEPINEX_TEST_OVERRIDE", originalEnvironment); }
    if (OperatingSystem.IsWindows())
    {
        var linkedProfile = Path.Combine(scratch, "profile-hardlink.json");
        Check(NativeLinks.CreateHardLink(linkedProfile, profilePath, IntPtr.Zero), "couldn't create hardlink control");
        Reject(() => EnvironmentCopy.Verify(destination), "hard-linked private profile");
        File.Delete(linkedProfile);
        EnvironmentCopy.Verify(destination);
        var linkedLog = Path.Combine(destination, ".arena-start/server-start.log");
        Check(NativeLinks.CreateHardLink(linkedLog, Path.Combine(source, forbidden[0]), IntPtr.Zero), "couldn't create log hardlink control");
        Reject(() => EnvironmentCopy.Verify(destination), "hard-linked mutable log");
        File.Delete(linkedLog);
        EnvironmentCopy.Verify(destination);
    }
    var linked = Path.Combine(destination, "linked-profiles");
    try
    {
        Directory.CreateSymbolicLink(linked, Path.Combine(source, "SPT_Runtime/user/profiles"));
        Reject(() => EnvironmentCopy.Verify(destination), "linked original profiles");
    }
    finally { if (Directory.Exists(linked)) Directory.Delete(linked); }
    var binary = Path.Combine(destination, "BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll");
    File.AppendAllText(binary, "changed");
    Reject(() => EnvironmentCopy.Verify(destination), "changed owned binary");
    Check(original.All(x => EnvironmentCopy.Hash(x.Key) == x.Value), "original modified during rejection tests");
    Console.WriteLine($"ARENA_START_CHECKS_PASS ({count} assertions; positive copy/bootstrap fixture and negative controls)");
}

finally
{
    // Exactly the freshly created unique fixture directory, never a caller path.
    if (Path.GetDirectoryName(scratch) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()) &&
        Path.GetFileName(scratch).StartsWith("ArenaStartChecks-", StringComparison.Ordinal))
        Directory.Delete(scratch, true);
}

internal static class NativeLinks
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
