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
    var sharedContent = new[] {
        "EscapeFromTarkov_Data/sharedassets0.assets", "EscapeFromTarkov_Data/sharedassets0.assets.resS",
        "EscapeFromTarkov_Data/resources.resource", "EscapeFromTarkov_Data/Resources/unity default resources",
        "EscapeFromTarkov_Data/StreamingAssets/Windows/map.bundle",
        "EscapeFromTarkov_Data/StreamingAssets/Windows/assets/texture",
        "EscapeFromTarkov_Data/StreamingAssets/Acoustics/map.xrageo",
        "EscapeFromTarkov_Data/StreamingAssets/Culling_Data/map.bytes"
    };
    foreach (var file in sharedContent) Put(file, new string('x', 8192));
    Put("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "patched");
    Put("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", "native backup");
    Put("SPT_Runtime/SPT_Data/Launcher/Patches/SPT-core/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.delta", "delta");
    Put("BepInEx/plugins/maschine-UiBridge.dll", "diagnostic bridge");
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
    // EFT's LogConfigurator.Reconfig indexes these variables unconditionally.
    // Missing either prevents ClientApplication.Awake and leaves the splash hung.
    void CheckNativeLog(string xml)
    {
        var log = System.Xml.Linq.XDocument.Parse(xml);
        var variables = log.Root!.Elements().Where(e => e.Name.LocalName == "variable")
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Attribute("value")?.Value);
        if (!variables.ContainsKey("clientId") || !variables.ContainsKey("gameVersion"))
            throw new InvalidDataException("Native log startup contract missing.");
        Check(log.Descendants().Where(e => e.Name.LocalName == "target")
            .All(e => e.Attribute("fileName")!.Value.StartsWith("Logs/", StringComparison.Ordinal)),
            "native log target escaped private working directory");
    }
    CheckNativeLog(File.ReadAllText(Path.Combine(destination, "NLog/NLog.config")));
    Reject(() => CheckNativeLog(EnvironmentCopy.NativeLogConfig.Replace("<variable name=\"clientId\" value=\"undefined\" />", "")),
        "missing native clientId regression control");
    Reject(() => CheckNativeLog(EnvironmentCopy.NativeLogConfig.Replace("<variable name=\"gameVersion\" value=\"undefined\" />", "")),
        "missing native gameVersion regression control");
    var loaderPath = Path.Combine(destination, "BepInEx/config/BepInEx.cfg");
    var loaderConfig = File.ReadAllText(loaderPath);
    Check(loaderConfig == EnvironmentCopy.NativeLoaderConfig, "native scene lifetime setting not generated");
    File.WriteAllText(loaderPath, loaderConfig + "\n[Logging]\nUnityLogListening = true\n");
    EnvironmentCopy.Verify(destination); // Native BepInEx normalization is legitimate.
    foreach (var invalid in new[] { "", "[Other]\nHideManagerGameObject = true",
        loaderConfig.Replace("true", "false"), loaderConfig + "\nHideManagerGameObject = false" })
    {
        File.WriteAllText(loaderPath, invalid);
        Reject(() => EnvironmentCopy.Verify(destination), "native plugin scene lifetime setting absent/ambiguous");
    }
    File.WriteAllText(loaderPath, loaderConfig);
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

    var thin = ManagedEnvironment.Destination(source);
    var nativeAssembly = Path.Combine(source, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
    File.WriteAllText(nativeAssembly, "native backup");
    Reject(() => EnvironmentCopy.Plan(source, thin, shareAssets: true), "unpatched installation");
    File.WriteAllText(nativeAssembly, "patched");
    var thinPlan = EnvironmentCopy.Plan(source, thin, shareAssets: true);
    Check(thinPlan.SharedBytes == sharedContent.Length * 8192L, "content selection/size estimate");
    Check(thinPlan.PrivateBytes + thinPlan.SharedBytes == thinPlan.Bytes, "physical-size accounting");
    foreach (var path in new[] { "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll",
        "EscapeFromTarkov_Data/sharedassets0.assets.spt-bak", "EscapeFromTarkov_Data/StreamingAssets/Windows/evil.dll",
        "EscapeFromTarkov_Data/StreamingAssets/Windows/../evil", "SPT_Runtime/user/profiles/x.json" })
        Check(!SharedAssets.IsContent(path), "unsafe content classification: " + path);
    var badBackup = "EscapeFromTarkov_Data/StreamingAssets/Windows/map.bundle.spt-bak";
    Put(badBackup);
    Reject(() => EnvironmentCopy.Plan(source, thin, shareAssets: true), "shared content patch backup");
    File.Delete(Path.Combine(source, badBackup));
    var badDelta = "SPT_Runtime/SPT_Data/Launcher/Patches/SPT-core/EscapeFromTarkov_Data/sharedassets0.assets.delta";
    Put(badDelta);
    Reject(() => EnvironmentCopy.Plan(source, thin, shareAssets: true), "new launcher patch targeting shared data");
    File.Delete(Path.Combine(source, badDelta));
    Reject(() => EnvironmentCopy.Prepare(thinPlan with { SharedBytes = thinPlan.Bytes }), "tampered physical size plan");
    Check(ManagedEnvironment.Ensure(source) == thin, "automatic preparation destination");
    var thinManifest = EnvironmentCopy.Verify(thin);
    Check(thinManifest.Schema == 2 && thinManifest.Files.Count(f => f.Shared) == sharedContent.Length, "thin manifest");
    Check(!File.Exists(Path.Combine(thin, "BepInEx/plugins/maschine-UiBridge.dll")), "diagnostics imported by default");
    Check(ManagedEnvironment.Ensure(source) == thin, "automatic reuse");
    Reject(() => ManagedEnvironment.Ensure(source, diagnostics: true), "silent diagnostics enable on existing runtime");
    var rootManifest = Path.Combine(thin, EnvironmentCopy.ManifestPath);
    var originalManifest = File.ReadAllText(rootManifest);
    File.WriteAllText(rootManifest, originalManifest.Replace("\"Shared\": false", "\"Shared\": true"));
    Reject(() => EnvironmentCopy.Verify(thin), "manifest attempts to share executable/config files");
    File.WriteAllText(rootManifest, originalManifest);
    var asset = Path.Combine(thin, sharedContent[0]);
    var originalAsset = Path.Combine(source, sharedContent[0]);
    var content = File.ReadAllBytes(asset);
    var attributes = File.GetAttributes(originalAsset);
    using (var held = new AssetLease())
    {
        EnvironmentCopy.Verify(thin, assetLease: held);
        Check(held.Count == sharedContent.Length, "asset leases missing");
        foreach (var path in new[] { asset, originalAsset })
        {
            Check(File.ReadAllBytes(path).SequenceEqual(content), "normal reads under lease");
            Reject(() => File.WriteAllText(path, "wrong"), "write through linked/source path");
            Reject(() => File.Delete(path), "delete through linked/source path: " + path);
            Reject(() => File.Move(path, path + ".moved"), "rename through linked/source path: " + path);
        }
    }
    Check(File.GetAttributes(originalAsset) == attributes, "source attributes changed");
    using (var writer = new FileStream(originalAsset, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        Reject(() => EnvironmentCopy.Verify(thin), "writer already present before lock acquisition");
    File.Delete(asset);
    File.WriteAllBytes(asset, content);
    Reject(() => EnvironmentCopy.Verify(thin), "identical independent copy is not the declared hardlink");
    File.Delete(asset);
    Check(NativeLinks.CreateHardLink(asset, originalAsset, IntPtr.Zero), "restore controlled asset link");
    File.AppendAllText(originalAsset, "source update");
    Reject(() => EnvironmentCopy.Verify(thin), "shared source in-place drift");
    File.WriteAllBytes(originalAsset, content);
    File.Delete(originalAsset);
    File.WriteAllBytes(originalAsset, content);
    Reject(() => EnvironmentCopy.Verify(thin), "identical source replacement has different identity");
    File.Delete(asset);
    Check(NativeLinks.CreateHardLink(asset, originalAsset, IntPtr.Zero), "restore source-replacement fixture");
    File.WriteAllText(Path.Combine(thin, badBackup), "private injected backup");
    Reject(() => EnvironmentCopy.Verify(thin), "injected restore backup after preparation");
    File.Delete(Path.Combine(thin, badBackup));
    var versionFile = Path.Combine(source, "UnityPlayer.dll");
    var versionBytes = File.ReadAllBytes(versionFile);
    File.AppendAllText(versionFile, "updated");
    Reject(() => EnvironmentCopy.Verify(thin), "game source version changed");
    File.WriteAllBytes(versionFile, versionBytes);
    var mutableLink = Path.Combine(thin, "BepInEx/config/shared.cfg");
    Directory.CreateDirectory(Path.GetDirectoryName(mutableLink)!);
    Check(NativeLinks.CreateHardLink(mutableLink, originalAsset, IntPtr.Zero), "create mutable link control");
    Reject(() => EnvironmentCopy.Verify(thin), "shared content alias injected into writable configuration");
    File.Delete(mutableLink);
    EnvironmentCopy.Verify(thin);
    Check(original.All(x => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x.Key))) == x.Value),
        "source bytes differ after thin-runtime checks");
    var diagPlan = EnvironmentCopy.Plan(source, Path.Combine(scratch, "diagnostic"), shareAssets: true, diagnostics: true);
    Check(diagPlan.Files.Contains("BepInEx/plugins/maschine-UiBridge.dll"), "explicit diagnostic catalog missing");
    Console.WriteLine($"ARENA_START_CHECKS_PASS ({count} assertions; copy + thin + managed reuse + actual filesystem failure controls)");
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
