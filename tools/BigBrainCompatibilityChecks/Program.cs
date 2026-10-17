using System.Reflection;
using System.Runtime.Loader;

string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string probePath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/Probe/bin", configuration, "net10.0/Probe.dll");
string apiPath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/BigBrainApi/bin", configuration, "net10.0/DrakiaXYZ-BigBrain.dll");
string brokenApiPath = Path.Combine(root, "tools/BigBrainCompatibilityChecks/BrokenBigBrainApi/bin", configuration, "net10.0/DrakiaXYZ-BigBrain.dll");
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }

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
    Check((bool)Call("SoftDependency"), "companion loader dependency is optional");
    var boundary = entry.Assembly.GetType("ArenaMode.BigBrainCompat.BigBrainRuntime")!.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!;
    Check((boundary.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0, "optional typed boundary cannot inline into bootstrap");
    Call("Start", version, scenario);
    string[] state = (string[])Call("State");
    Check(state[0] == expectedReady.ToString() && state[2] == state[0] && state[3] == state[0], version + "/" + scenario + " capabilities");
    Check(state[1] == expectedReady.ToString(), version + "/" + scenario + " update disabled unless ready");
    Check(state[4] == (expectedReady ? "com.maschine.ArenaMode.BigBrainCompat" : scenario == "foreign-owner" ? "foreign" : "none"), version + "/" + scenario + " exclusive consumer and rollback");
    if (!allowApi) Check(context.ApiResolutionAttempts == 0, version + "/" + scenario + " never resolves optional assembly");
    if (version == "absent") Check(!state[7].Contains("WARN") && !state[7].Contains("ERROR"), "absence is quiet");
    if (expectedReady)
    {
        int warnings = state[7].Split('\n').Count(x => x.StartsWith("WARN"));
        Check(warnings == (version == "1.5.0" ? 0 : 1), "newer version warns once; tested version is quiet");
    }
    if (allowApi && !expectedReady) Check(state[7].Contains("ERROR"), "actual registration failures are reported");
    if (scenario == "missing-api-method") Check(state[7].Contains(nameof(MissingMethodException)), "missing future API member is caught at the typed JIT boundary");
    Call("Invoke", "Update"); Call("Invoke", "Update");
    state = (string[])Call("State");
    Check(state[5] == (expectedReady ? "1" : "0"), "maintenance is active-only and throttled");
    Call("Invoke", "OnDestroy");
    state = (string[])Call("State");
    Check(state[0] == "False" && state[2] == "False" && state[3] == "False", "teardown clears all capabilities");
    Check(state[4] == (scenario == "foreign-owner" ? "foreign" : "none"), "teardown retains foreign ownership");
    if (!allowApi) Check(context.ApiResolutionAttempts == 0, "disabled teardown never resolves BigBrain");
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
