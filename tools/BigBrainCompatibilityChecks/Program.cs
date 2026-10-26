using System.Reflection;
using System.Runtime.Loader;

string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string probePath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/Probe/bin", configuration, "net10.0/Probe.dll");
string apiPath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/BigBrainApi/bin", configuration, "net10.0/DrakiaXYZ-BigBrain.dll");
string brokenApiPath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/BrokenBigBrainApi/bin", configuration, "net10.0/DrakiaXYZ-BigBrain.dll");
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }

if (args.Length == 3 && args[0] == "--host")
{
    // Each invocation is a fresh process: Assembly.LoadFrom uses the default
    // context, matching the production path rather than an injected type seam.
    string scenario = args[1];
    Environment.SetEnvironmentVariable("ARENA_BIGBRAIN_HOST_FIXTURE", scenario);
    Type entry = Assembly.LoadFrom(probePath).GetType("Entry", true)!;
    entry.GetMethod("StartHost")!.Invoke(null, new object[] { "1.5.0", args[2], false });
    string[] state = (string[])entry.GetMethod("State")!.Invoke(null, null)!;
    string log = state[7];
    int Count(string text) => log.Split('\n').Count(x => x == "DEBUG " + text);
    bool rejected = scenario is "bad-api" or "wrong-version" or "legacy-file" or "wrong-name";
    Check(Count("fixture initialize") == (rejected ? 0 : 1), "loader initializes compatible API exactly once");
    Check(Count("fixture tick") == (rejected || scenario is "init-false" or "init-throw" ? 0 : 1), "only initialized library receives ticks");
    Check(Count("fixture shutdown") == (rejected ? 0 : 1), "failure and repeated teardown release exactly once");
    Check(log.Split('\n').Count(x => x.StartsWith("ERROR")) == (scenario is "good" or "init-false" ? 0 : 1), "actionable failure reported once");
    if (scenario == "bad-api") Check(log.Contains("API is not supported"), "reject actual incompatible API constant");
    if (scenario is "wrong-version" or "wrong-name") Check(log.Contains("does not match"), "reject actual assembly identity mismatch before loading");
    Console.WriteLine($"BIGBRAIN_HOST_CHECKS_PASSED ({scenario}: {checks})");
    return;
}

foreach (var test in new[]
{
    ("absent", "normal", false, false), ("1.4.9", "normal", false, false),
    ("1.5.0", "normal", true, true), ("1.5.1", "normal", true, true), ("2.0.0", "normal", true, true),
    ("1.5.0", "not-loaded", false, false), ("1.5.0", "arena-mismatch", false, false),
    ("1.5.0", "unknown-version", false, false), ("1.6.0", "missing-api-method", true, false),
    ("1.6.0", "api-failure", true, false), ("1.6.0", "missing-registration", true, false),
    ("1.6.0", "wrong-type", true, false), ("1.6.0", "wrong-priority", true, false),
    ("1.6.0", "missing-brain", true, false), ("1.5.0", "foreign-owner", true, false)
})
{
    var (version, scenario, allowApi, expectedReady) = test;
    string selectedApi = scenario == "missing-api-method" ? brokenApiPath : apiPath;
    using var context = new IsolatedProbe(selectedApi, allowApi);
    if (allowApi)
        context.LoadFromAssemblyPath(selectedApi).GetType("DrakiaXYZ.BigBrain.Brains.BrainManager")!
            .GetField("Scenario")!.SetValue(null, scenario);
    Type entry = context.LoadFromAssemblyPath(probePath).GetType("Entry", true)!;
    object Call(string method, params object[] values) => entry.GetMethod(method)!.Invoke(null, values)!;
    Check((bool)Call("IsPlainLibrary"), "companion is a plain static library without plugin metadata");
    var boundary = entry.Assembly.GetType("ArenaMode.BigBrainCompat.BigBrainRuntime")!.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!;
    Check((boundary.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0, "optional typed boundary cannot inline into bootstrap");
    Call("Start", version, scenario);
    string[] state = (string[])Call("State");
    Check(state[0] == expectedReady.ToString() && state[2] == state[0] && state[3] == state[0] && state[8] == state[0], version + "/" + scenario + " capabilities including Domination");
    Check(state[1] == expectedReady.ToString(), version + "/" + scenario + " update disabled unless ready");
    Check(state[4] == (expectedReady ? "com.maschine.ArenaMode.BigBrainCompat" : scenario == "foreign-owner" ? "foreign" : "none"), version + "/" + scenario + " exclusive consumer and rollback");
    Check((bool)Call("InitializeAgain") == expectedReady, "repeated initialization retains its outcome");
    Check(((string[])Call("State")).SequenceEqual(state), "repeated initialization does not register, log or revoke anything");
    if (!allowApi) Check(context.ApiResolutionAttempts == 0, version + "/" + scenario + " never resolves optional assembly");
    if (version == "absent") Check(!state[7].Contains("WARN") && !state[7].Contains("ERROR"), "absence is quiet");
    if (expectedReady)
    {
        int warnings = state[7].Split('\n').Count(x => x.StartsWith("WARN"));
        Check(warnings == (version == "1.5.0" ? 0 : 1), "newer version warns once; tested version is quiet");
    }
    if (allowApi && !expectedReady) Check(state[7].Contains("ERROR"), "actual registration failures are reported");
    if (scenario == "missing-api-method") Check(state[7].Contains(nameof(MissingMethodException)), "missing future API member is caught at the typed JIT boundary");
    Call("Invoke", "Tick"); Call("Invoke", "Tick");
    state = (string[])Call("State");
    Check(state[5] == (expectedReady ? "1" : "0"), "maintenance is active-only and throttled");
    Call("Invoke", "Shutdown");
    state = (string[])Call("State");
    Check(state[0] == "False" && state[2] == "False" && state[3] == "False" && state[8] == "False", "teardown clears all capabilities including Domination");
    Check(state[4] == (scenario == "foreign-owner" ? "foreign" : "none"), "teardown retains foreign ownership");
    Call("Invoke", "Shutdown");
    Check(((string[])Call("State")).SequenceEqual(state), "repeated shutdown is idempotent");
    Check(!(bool)Call("InitializeAgain"), "shutdown cannot register a second set of process-lifetime layers");
    if (!allowApi) Check(context.ApiResolutionAttempts == 0, "disabled teardown never resolves BigBrain");
}

foreach (var host in new[] { ("absent", false, false), ("1.5.0", false, true), ("1.5.0", true, true) })
{
    using var context = new IsolatedProbe(apiPath, false);
    Type entry = context.LoadFromAssemblyPath(probePath).GetType("Entry", true)!;
    // A nonexistent path is a negative control: absence must not attempt file access.
    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "maschine-ArenaMode.Client.dll");
    entry.GetMethod("StartHost")!.Invoke(null, new object[] { host.Item1, path, host.Item2 });
    string[] state = (string[])entry.GetMethod("State")!.Invoke(null, null)!;
    Check(state[7].Contains("ERROR") == host.Item3, "host absence is quiet; missing library/legacy plugin is reported");
    Check(state[7].Split('\n').Count(x => x.StartsWith("ERROR")) == (host.Item3 ? 1 : 0), "host initializes once and cleanup does not spam");
    Check(state[0] == "False" && state[2] == "False" && state[3] == "False", "host failure advertises no capabilities");
    Check(context.ApiResolutionAttempts == 0, "host rejection never resolves BigBrain API");
}
Console.WriteLine($"BIGBRAIN_COMPATIBILITY_CHECKS_PASSED ({checks})");

sealed class IsolatedProbe(string apiPath, bool allowApi) : AssemblyLoadContext(isCollectible: true), IDisposable
{
    public int ApiResolutionAttempts;
    protected override Assembly Load(AssemblyName name)
    {
        if (name.Name != "DrakiaXYZ-BigBrain") return null;
        ApiResolutionAttempts++;
        if (!allowApi) throw new FileNotFoundException("BigBrain deliberately unavailable");
        return LoadFromAssemblyPath(apiPath);
    }
    public void Dispose() => Unload();
}
