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
void CheckPhase(IReadOnlyList<string> messages, string prefix, int total)
{
    var steps = messages.Where(m => m.StartsWith(prefix, StringComparison.Ordinal))
        .Select(m => System.Text.RegularExpressions.Regex.Match(m,
            "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"(\d+) / (\d+)")).ToArray();
    Check(steps.Length > 0 && steps.All(m => m.Success), "missing/malformed progress: " + prefix);
    var current = steps.Select(m => int.Parse(m.Groups[1].Value)).ToArray();
    Check(current[0] == 0 && current[^1] == total, "progress must start at zero and finish even for small catalogs: " + prefix);
    Check(steps.All(m => int.Parse(m.Groups[2].Value) == total) &&
        current.All(n => n >= 0 && n <= total) && current.SequenceEqual(current.Order().Distinct()),
        "progress counts differ from the actual catalog or repeat/regress: " + prefix);
}
void CheckIncomplete(IReadOnlyList<string> messages, string prefix, int total)
{
    Check(messages.Any(m => m.StartsWith(prefix + "0 / " + total, StringComparison.Ordinal)) &&
        !messages.Any(m => m.StartsWith(prefix + total + " / " + total, StringComparison.Ordinal)),
        "failed phase claimed completion: " + prefix);
    Check(!messages.Any(m => m.StartsWith("Environment complete:", StringComparison.Ordinal) ||
        m.StartsWith("Verified ", StringComparison.Ordinal) && m.Contains("catalogued files", StringComparison.Ordinal)),
        "failed operation claimed overall success");
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
    var progress = new List<string>();
    EnvironmentCopy.Prepare(plan, progress.Add);
    Check(File.Exists(Path.Combine(destination, EnvironmentCopy.ManifestPath)), "final manifest absent");
    CheckPhase(progress, "Copied private source files: ", plan.Files.Count);
    CheckPhase(progress, "Hard-linked and verified source files: ", 0);
    Check(progress.Where(m => m.StartsWith("Copied private source files:", StringComparison.Ordinal))
        .All(m => m.EndsWith(" GiB", StringComparison.Ordinal)), "copy byte progress lost");
    progress.Clear();
    var copyManifest = EnvironmentCopy.Verify(destination, progress.Add);
    var copyPaths = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length;
    CheckPhase(progress, "Verified shared assets: ", 0);
    CheckPhase(progress, "Checking runtime file paths: ", copyPaths);
    CheckPhase(progress, "Checking file integrity: ", copyManifest.Files.Count);
    Check(!progress.Any(m => m.StartsWith("Checking source version files:", StringComparison.Ordinal)),
        "copy-only verification invented source bindings");
    Check(copyPaths > copyManifest.Files.Count && copyManifest.Files.Count > plan.Files.Count,
        "mutable runtime files/generated configs were not accounted separately");
    var finalCatalogFile = Path.Combine(destination, copyManifest.Files[^1].Path);
    var finalCatalogBytes = File.ReadAllBytes(finalCatalogFile);
    File.AppendAllText(finalCatalogFile, "last-entry integrity failure");
    progress.Clear();
    Reject(() => EnvironmentCopy.Verify(destination, progress.Add), "last catalog entry changed");
    CheckIncomplete(progress, "Checking file integrity: ", copyManifest.Files.Count);
    File.WriteAllBytes(finalCatalogFile, finalCatalogBytes);
    var failedPlan = EnvironmentCopy.Plan(source, Path.Combine(scratch, "failed-copy-progress"));
    progress.Clear();
    using (var blocked = new FileStream(Path.Combine(source, failedPlan.Files[^1]), FileMode.Open, FileAccess.Read, FileShare.None))
        Reject(() => EnvironmentCopy.Prepare(failedPlan, progress.Add), "blocked source copy");
    CheckIncomplete(progress, "Copied private source files: ", failedPlan.Files.Count);
    Check(!File.Exists(Path.Combine(failedPlan.Destination, EnvironmentCopy.ManifestPath)), "failed copy wrote a final manifest");
    // Exercise periodic path reports with real mutable files, without adding
    // hundreds of extra game binaries to every subsequent negative fixture.
    var progressDirectory = Path.Combine(destination, ".arena-start/progress-fixtures");
    Directory.CreateDirectory(progressDirectory);
    for (var i = 0; i < 501; i++) File.WriteAllText(Path.Combine(progressDirectory, i + ".log"), "");
    progress.Clear();
    EnvironmentCopy.Verify(destination, progress.Add);
    var expandedPaths = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length;
    CheckPhase(progress, "Checking runtime file paths: ", expandedPaths);
    Check(progress.Contains($"Checking runtime file paths: 500 / {expandedPaths}"), "periodic path progress absent");
    if (!Path.GetFullPath(progressDirectory).StartsWith(Path.GetFullPath(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Progress fixture escaped scratch directory.");
    Directory.Delete(progressDirectory, true);
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
    progress.Clear();
    Reject(() => EnvironmentCopy.Verify(destination, progress.Add), "changed owned binary");
    CheckIncomplete(progress, "Checking file integrity: ", copyManifest.Files.Count);
    Check(original.All(x => EnvironmentCopy.Hash(x.Key) == x.Value), "original modified during rejection tests");

    var thin = ManagedEnvironment.Destination(source);
    var nativeAssembly = Path.Combine(source, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
    File.WriteAllText(nativeAssembly, "native backup");
    Reject(() => EnvironmentCopy.Plan(source, thin, shareAssets: true), "unpatched installation");
    File.WriteAllText(nativeAssembly, "patched");
    var thinPlan = EnvironmentCopy.Plan(source, thin, shareAssets: true);
    var preview = ManagedEnvironment.Inspect(source);
    Check(!preview.Exists && preview.Destination == thin && preview.SharedBytes == thinPlan.SharedBytes &&
        preview.PrivateBytes == thinPlan.PrivateBytes, "new environment preview differs from actual plan");
    Check(!Directory.Exists(thin), "preview created a runtime");
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
    progress.Clear();
    Check(ManagedEnvironment.Ensure(source, report: progress.Add) == thin, "automatic preparation destination");
    CheckPhase(progress, "Hard-linked and verified source files: ", sharedContent.Length);
    CheckPhase(progress, "Copied private source files: ", thinPlan.Files.Count - sharedContent.Length);
    Check(progress.Any(m => m.StartsWith("Preparing managed ArenaMode environment:", StringComparison.Ordinal) &&
        m.Contains("NTFS hardlinks", StringComparison.Ordinal) && m.Contains("private copies", StringComparison.Ordinal)),
        "managed preparation does not explain hardlinks versus copies");
    var completion = progress.Single(m => m.StartsWith("Environment complete:", StringComparison.Ordinal));
    progress.Clear();
    var thinManifest = EnvironmentCopy.Verify(thin, progress.Add);
    var thinPaths = Directory.GetFiles(thin, "*", SearchOption.AllDirectories).Length;
    CheckPhase(progress, "Checking source version files: ", thinManifest.SourceBindings!.Count);
    CheckPhase(progress, "Verified shared assets: ", sharedContent.Length);
    CheckPhase(progress, "Checking runtime file paths: ", thinPaths);
    CheckPhase(progress, "Checking file integrity: ", thinManifest.Files.Count);
    Check(completion.Contains($"Generated {thinManifest.Files.Count - thinPlan.Files.Count} catalogued configuration files", StringComparison.Ordinal),
        "generated configuration files included in source transfer totals");
    Check(progress.Last().Contains($"{thinPaths} / {thinPaths} runtime file paths", StringComparison.Ordinal) &&
        progress.Last().Contains($"{thinManifest.Files.Count} / {thinManifest.Files.Count} catalogued files", StringComparison.Ordinal),
        "final report confuses manifest entries with mutable runtime files");
    Check(thinManifest.Schema == 2 && thinManifest.Files.Count(f => f.Shared) == sharedContent.Length, "thin manifest");
    Check(!File.Exists(Path.Combine(thin, "BepInEx/plugins/maschine-UiBridge.dll")), "diagnostics imported by default");
    Check(ManagedEnvironment.Ensure(source) == thin, "automatic reuse");
    preview = ManagedEnvironment.Inspect(source);
    Check(preview.Exists && preview.SharedBytes == thinManifest.Files.Where(f => f.Shared).Sum(f => f.Length) &&
        preview.PrivateBytes == thinManifest.Files.Where(f => !f.Shared).Sum(f => f.Length), "owned preview catalog sizes");
    Reject(() => ManagedEnvironment.Inspect(source, diagnostics: true), "preview silently adopts another mode");
    Reject(() => ManagedEnvironment.Ensure(source, diagnostics: true), "silent diagnostics enable on existing runtime");
    var rootManifest = Path.Combine(thin, EnvironmentCopy.ManifestPath);
    var originalManifest = File.ReadAllText(rootManifest);
    File.WriteAllText(rootManifest, originalManifest.Replace("\"Schema\": 2", "\"Schema\": 1"));
    Reject(() => ManagedEnvironment.Inspect(source), "preview adopts an unknown runtime format");
    Check(File.ReadAllText(rootManifest).Contains("\"Schema\": 1"), "preview repaired a rejected manifest");
    File.WriteAllText(rootManifest, originalManifest);
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
    progress.Clear();
    Reject(() => EnvironmentCopy.Verify(thin, progress.Add), "shared source in-place drift");
    CheckIncomplete(progress, "Verified shared assets: ", sharedContent.Length);
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
    progress.Clear();
    Reject(() => EnvironmentCopy.Verify(thin, progress.Add), "game source version changed");
    CheckIncomplete(progress, "Checking source version files: ", thinManifest.SourceBindings.Count);
    File.WriteAllBytes(versionFile, versionBytes);
    var mutableLink = Path.Combine(thin, "BepInEx/config/shared.cfg");
    Directory.CreateDirectory(Path.GetDirectoryName(mutableLink)!);
    Check(NativeLinks.CreateHardLink(mutableLink, originalAsset, IntPtr.Zero), "create mutable link control");
    progress.Clear();
    Reject(() => EnvironmentCopy.Verify(thin, progress.Add), "shared content alias injected into writable configuration");
    CheckIncomplete(progress, "Checking runtime file paths: ", Directory.GetFiles(thin, "*", SearchOption.AllDirectories).Length);
    File.Delete(mutableLink);
    EnvironmentCopy.Verify(thin);
    Check(original.All(x => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x.Key))) == x.Value),
        "source bytes differ after thin-runtime checks");
    var diagPlan = EnvironmentCopy.Plan(source, Path.Combine(scratch, "diagnostic"), shareAssets: true, diagnostics: true);
    Check(diagPlan.Files.Contains("BepInEx/plugins/maschine-UiBridge.dll"), "explicit diagnostic catalog missing");
    var reports = 0;
    var observer = new RuntimeReporter(_ => { reports++; throw new IOException("closed UI"); });
    observer.Write("server started");
    observer.Write("server cleanup");
    Check(reports == 2, "failed observer prevented subsequent runtime/cleanup reports");
    var clients = true;
    var delays = new System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource>();
    Task Delay()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        delays.Enqueue(signal);
        return signal.Task;
    }
    var waiting = RuntimeHost.WaitUntilClientsClose(() => clients, Delay);
    Check(!waiting.IsCompleted && delays.Count == 1, "live clients did not retain session");
    // Model launcher close/server failure with EFT still alive: anyClients remains true.
    Check(delays.TryDequeue(out var next), "initial polling delay absent");
    next!.SetResult();
    for (var i = 0; i < 100 && delays.Count == 0; i++) await Task.Delay(10);
    Check(!waiting.IsCompleted && delays.Count == 1, "remaining EFT client released session");
    clients = false;
    Check(delays.TryDequeue(out next), "subsequent polling delay absent");
    next!.SetResult();
    await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    Check(waiting.IsCompletedSuccessfully, "session did not release after final client closed");
    clients = true;
    int? serverExit = 73;
    var exitMessages = new List<string>();
    waiting = RuntimeHost.WaitForSessionEnd(() => clients, () => serverExit, new RuntimeReporter(exitMessages.Add), Delay);
    Check(!waiting.IsCompleted && exitMessages.Single().Contains("(73)"), "server failure did not retain live client/report reason");
    clients = false;
    Check(delays.TryDequeue(out next), "server failure polling delay absent");
    next!.SetResult();
    try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("server failure reported success"); }
    catch (IOException error) { Check(error.Message.Contains("(73)"), "unexpected server exit code lost"); }
    serverExit = null;
    await RuntimeHost.WaitForSessionEnd(() => false, () => serverExit, observer);
    Check(true, "healthy session end completed");
    count += await DirectGameChecks.Run(scratch, destination, args);
    Console.WriteLine($"ARENA_START_CHECKS_PASS ({count} assertions; copy + thin + managed reuse + direct game + actual failure controls)");
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
