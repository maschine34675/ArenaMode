using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Common;

string release = Path.GetFullPath(args[0]);
if (args.Length > 1 && args[1] == "--absent")
{
    PitFireTeamDynamicPersistencePatches.TryEnable();
    if (PitFireTeamDynamicPersistencePatches.IsInstalled || PitFireTeamDynamicPersistencePatches.ProtectionReady
        || AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "pitFireTeam.Server"))
        throw new Exception("Optional discovery must not load absent PFT.");
    Console.WriteLine("PFT_ABSENT_CONTRACT_VERIFIED (no foreign assembly loaded)");
    return;
}
bool legacyRun = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]);
string serverFolder = legacyRun ? Path.GetDirectoryName(Path.GetFullPath(args[1]))!
    : Path.Combine(release, "SPT_Runtime/user/mods/pitFireTeam-ServerMod");
if (legacyRun)
{
    LegacyContractChecks.Verify(Path.Combine(serverFolder, "pitFireTeam.Server.dll"));
    return;
}
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string dependency = Path.Combine(serverFolder, name.Name + ".dll");
    if (File.Exists(dependency)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency);
    return null;
};
Assembly pft = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(serverFolder, "pitFireTeam.Server.dll"));
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
Check(pft.GetName().Version == new Version(1, 0, 0, 0), "The fixture must be the exact 1.0.0 release.");
PitFireTeamDynamicPersistencePatches.TryEnable();
Check(PitFireTeamDynamicPersistencePatches.ProtectionReady, PitFireTeamDynamicPersistencePatches.IncompatibilityReason);
await ClientContractChecks.Verify(Path.Combine(release, "BepInEx/plugins/pitFireTeam/pitFireTeam.dll"), Check);
OperationGateChecks.Verify(Check);
const string protectedId = "aaaaaaaaaaaaaaaaaaaaaaaa";
const string otherId = "bbbbbbbbbbbbbbbbbbbbbbbb";
Type databaseType = pft.GetType("pitTeam.Server.Persistence.TeammateDatabase", true)!;
string testRoot = Path.Combine(Path.GetFullPath("artifacts/pft-100-contracts"), "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
object MakeDatabase(string id, string? root = null) => Activator.CreateInstance(databaseType, root ?? testRoot, id)!;
object? Call(object owner, string method, params object?[] parameters) => owner.GetType().GetMethod(method)!.Invoke(owner, parameters);
var legacy = new Func<IReadOnlyDictionary<string, string>>(() => new Dictionary<string, string> { ["fixture.json"] = "original" });
object database = MakeDatabase(protectedId);
object other = MakeDatabase(otherId);
Call(database, "Initialize", legacy);
Call(other, "Initialize", legacy);
Check((string?)Call(database, "Read", "fixture.json") == "original", "Ordinary PFT initialization/read is unchanged.");
Call(database, "WriteBatch", new Dictionary<string, string> { ["fixture.json"] = "ordinary" }, false);
Check((string?)Call(database, "Read", "fixture.json") == "ordinary", "Ordinary PFT writes remain enabled.");
string databasePath = Path.Combine(testRoot, protectedId + ".db");
byte[] before = SHA256.HashData(File.ReadAllBytes(databasePath));
ArenaContext.Sessions.Protection.BeginArmed(protectedId, TimeSpan.FromMinutes(10));
ArenaContext.Sessions.State = ArenaSessionState.Armed;
Call(database, "WriteBatch", new Dictionary<string, string> { ["fixture.json"] = "bad" }, false);
Call(database, "Wipe");
Check(Call(database, "DeleteTeammate", 123, null) is false, "Protected delete returns false.");
Check((string?)Call(database, "Read", "fixture.json") == "ordinary", "Protected mutation suppression preserves documents.");
Check(Call(database, "Initialize", new Func<IReadOnlyDictionary<string, string>>(() => throw new Exception("import ran"))) is 0,
    "Protected existing migration receipt validates without invoking importer.");
Check(SHA256.HashData(File.ReadAllBytes(databasePath)).SequenceEqual(before), "Protected reads/initialization are physically read-only.");
Call(other, "WriteBatch", new Dictionary<string, string> { ["fixture.json"] = "other changed" }, false);
Check((string?)Call(other, "Read", "fixture.json") == "other changed", "Other profile stays mutable.");
object missing = MakeDatabase(protectedId, Path.Combine(testRoot, "missing"));
try { Call(missing, "Initialize", legacy); throw new Exception("Missing DB was accepted."); }
catch (TargetInvocationException exception) when (exception.InnerException is FileNotFoundException) { checks++; }
Check(!Directory.Exists(Path.Combine(testRoot, "missing")), "Protected lazy initialization creates no directory or DB.");
string originalDirectory = Path.Combine(testRoot, protectedId);
Directory.CreateDirectory(originalDirectory);
File.WriteAllText(Path.Combine(originalDirectory, "proof.txt"), "preserved");
Type storageType = pft.GetType("pitTeam.Server.Services.FriendlyTeammateStorage", true)!;
object storage = RuntimeHelpers.GetUninitializedObject(storageType);
MethodInfo backup = storageType.GetMethod("TryBackupImportedDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
backup.Invoke(storage, [database]);
Check(Directory.Exists(originalDirectory) && !Directory.Exists(originalDirectory + ".backup"), "Protected import-directory move is suppressed.");

// Every modern service is executed on a deliberately unconstructed instance:
// suppression must happen before any profile/cache/mail dependency is reached.
var patchType = typeof(PitFireTeamDynamicPersistencePatches);
foreach (string table in new[] { "ModernRaidContracts", "ModernRecoveryContracts", "ModernManagementContracts" })
{
    foreach (object contract in (Array)patchType.GetField(table, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
    {
        var method = (MethodInfo)patchType.GetMethod("ResolveContract", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [pft, contract])!;
        object?[] callArgs = method.GetParameters().Select(parameter => parameter.ParameterType == typeof(MongoId)
            ? (object)new MongoId(protectedId) : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null).ToArray();
        object target = RuntimeHelpers.GetUninitializedObject(method.DeclaringType!);
        object? result = method.Invoke(target, callArgs);
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            await (Task)method.ReturnType.GetMethod("AsTask")!.Invoke(result, null)!;
        checks++;
    }
}

ArenaContext.Sessions.Protection.BeginPostRaidFinalization(protectedId);
ArenaContext.Sessions.State = ArenaSessionState.Idle;
Call(database, "WriteBatch", new Dictionary<string, string> { ["fixture.json"] = "late automatic" }, false);
Check((string?)Call(database, "Read", "fixture.json") == "ordinary", "Late automatic writes remain blocked.");
object scope = PitFireTeamManagementScope.Enter(protectedId);
try { Call(database, "WriteBatch", new Dictionary<string, string> { ["fixture.json"] = "explicit management" }, false); }
finally { PitFireTeamManagementScope.Exit(scope); }
Check((string?)Call(database, "Read", "fixture.json") == "explicit management", "Exact finalization management scope permits a complete transaction.");

// Logical leases must survive asynchronous yield, including a fault.
var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
PitFireTeamAsyncTransactionPatch<int>.Prefix(out var lease);
ValueTask<int> pending = new(completion.Task);
PitFireTeamAsyncTransactionPatch<int>.Postfix(ref pending, lease);
Check(!PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromMilliseconds(1)), "Async transaction remains leased after synchronous return.");
completion.SetResult(42);
Check(await pending == 42 && PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromSeconds(1)), "Async transaction releases only after completion.");
var failed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
PitFireTeamAsyncTransactionPatch<int>.Prefix(out var failedLease);
ValueTask<int> failedPending = new(failed.Task);
PitFireTeamAsyncTransactionPatch<int>.Postfix(ref failedPending, failedLease);
failed.SetException(new IOException("expected test fault"));
try { await failedPending; throw new Exception("Fault was swallowed."); }
catch (IOException) { checks++; }
Check(PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromSeconds(1)), "Faulted async transactions release their lease.");
ArenaContext.Sessions.Protection.Clear(protectedId);
backup.Invoke(storage, [database]);
Check(!Directory.Exists(originalDirectory) && Directory.Exists(originalDirectory + ".backup"), "Ordinary import-directory move still works.");

// Mutate only copied release metadata. Discovery must reject changed boundaries
// before any prefix can claim protection. No foreign implementation is copied.
foreach (string mutation in new[] { "missing-create", "changed-create-result", "missing-database-write" })
{
    string mutatedPath = Path.Combine(testRoot, mutation + ".dll");
    using (var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(pft.Location))
    {
        var type = definition.MainModule.GetType(mutation == "missing-database-write"
            ? "pitTeam.Server.Persistence.TeammateDatabase" : "pitTeam.Server.Services.FriendlyTeammateService");
        var method = type.Methods.Single(candidate => candidate.Name == (mutation == "missing-database-write" ? "WriteBatch" : "CreateTeammate"));
        if (mutation == "changed-create-result") method.ReturnType = definition.MainModule.TypeSystem.String;
        else method.Name += "Changed";
        definition.Write(mutatedPath);
    }
    var context = new AssemblyLoadContext(mutation, isCollectible: true);
    context.Resolving += (_, name) => AssemblyLoadContext.Default.LoadFromAssemblyName(name);
    Assembly changed = context.LoadFromAssemblyPath(mutatedPath);
    if (mutation == "missing-database-write")
    {
        object?[] call = [changed, null];
        Check((bool)patchType.GetMethod("TryResolveModernDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, call)! == false,
            "Missing database write must reject protection.");
    }
    else
    {
        Array contracts = (Array)patchType.GetField("ModernManagementContracts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object contract = contracts.GetValue(0)!;
        Check(patchType.GetMethod("ResolveContract", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [changed, contract]) == null,
            mutation + " must reject the changed transaction.");
    }
    context.Unload();
}
Console.WriteLine($"PFT_100_CONTRACTS_VERIFIED ({checks} assertions; exact release DLL, production-linked patches; no live profile or gameplay test)");
