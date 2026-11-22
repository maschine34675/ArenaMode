using System.Security.Cryptography;
using ArenaMode.Shared;
using ArenaMode.Server.Rewards;
using Mono.Cecil.Cil;
using Mono.Cecil;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
while (!File.Exists(Path.Combine(repo, "AGENTS.md"))) repo = Directory.GetParent(repo)?.FullName ?? throw new Exception("Repository missing");
repo = Path.TrimEndingDirectorySeparator(repo);
string release = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(repo, "artifacts/optional-mod-releases/cola-4.0.0");
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL ColaReleaseChecks: " + message); checks++; }
bool latest = Directory.Exists(Path.Combine(release, "BepInEx"));
AssemblyDefinition Read(string name, string hash)
{
    string path = latest ? Path.Combine(release, name.Contains("Client", StringComparison.Ordinal)
        ? "BepInEx/plugins/TarkovaCola/TarkovaCola.Client.dll" : "SPT_Runtime/user/mods/TarkovaCola/TarkovaCola.Server.dll") : Path.Combine(release, name);
    Check(File.Exists(path), "reviewed release DLL is available: " + name);
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(hash, StringComparison.OrdinalIgnoreCase), "exact released DLL: " + name);
    return AssemblyDefinition.ReadAssembly(path);
}
using var client = Read("TarkovaCola.Client.dll", latest ? "0642739761e954b7e893d3a84cde350c8305f8b246dc0d43c16679157b12ba0d" : "cf36bc95d165bf771bdad8e03c02f89ccd6d80f76bf75220000f40c1bb748a4c");
using var server = Read("TarkovaCola.Server.dll", latest ? "875b01b23cd9ec42a17d5fbdb6b3f9f0c1a0e92959b01782e231a131b25c511f" : "6f293522adf19a8f91dc0bb336855e93aba5c44d2982af5c15fa90a56b0369b3");
TypeDefinition Client(string name) => client.MainModule.GetType("TarkovaCola.Client." + name) ?? throw new Exception("Missing " + name);
void Method(TypeDefinition type, string name, string result, bool isStatic, params string[] args)
{
    Check(type.Methods.Count(m => m.Name == name && m.IsStatic == isStatic && !m.HasGenericParameters
        && m.ReturnType.FullName == result && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args)) == 1, type.Name + "." + name);
}
var plugin = Client("Plugin").CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
Check((string)plugin.ConstructorArguments[0].Value == TarkovaColaPolicy.PluginGuid, "released plugin GUID");
Check((string)plugin.ConstructorArguments[2].Value == (latest ? TarkovaColaPolicy.ReviewedVersion : "3.0.1"), "released plugin version");
foreach (string name in new[] { "Tick", "Save", "ApplyBottles" }) Method(Client("PitPerks"), name, "System.Void", true);
foreach (string name in new[] { "Equip", "Unequip" }) Method(Client("PitPerks"), name, "System.Void", false, "System.String");
Method(Client("PitPerks"), "OnDeath", "System.Void", true, "EFT.Player");
Method(Client("Machines"), "OnPlayerReady", "System.Void", true, "EFT.Player");
foreach (string name in new[] { "Save", "Place" }) Method(Client("Machines"), name, "System.Void", true);
Method(Client("Machines"), "DeleteNearest", "System.Void", true, "UnityEngine.Vector3");
foreach (string name in new[] { "Bandolier", "VultureAid", "MeleeMacchiato", "NewPerkUtil" }) Method(Client(name), "Reset", "System.Void", true);
Method(Client("Reroll"), "Reset", "System.Void", true);
Method(Client("Reroll"), "Request", "System.Void", true, "EFT.Interactive.LootableContainer");
Method(Client("Reroll"), "Apply", "System.Void", true, "Newtonsoft.Json.Linq.JObject", "EFT.Interactive.LootableContainer");
var closure = Client("Reroll").NestedTypes.Single(t => t.Methods.Any(m => m.Name.StartsWith("<Request>b__", StringComparison.Ordinal)));
Check(closure.Methods.Count(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0) == 1, "released request closure constructor");
Check(Client("Reroll").Fields.Single(f => f.Name == "_pending").FieldType.FullName == "Newtonsoft.Json.Linq.JObject", "released pending response");
Check(Client("Reroll").Fields.Single(f => f.Name == "_pendingFor").FieldType.FullName == "EFT.Interactive.LootableContainer", "released pending container");
var callback = closure.Methods.Single(m => m.Name.StartsWith("<Request>b__", StringComparison.Ordinal));
Check(callback.Parameters.Count == 1 && callback.Parameters[0].ParameterType.FullName == "System.Threading.Tasks.Task`1<System.String>" && callback.ReturnType.FullName == "System.Void", "released reroll callback");
Check(closure.Fields.Single(f => f.Name == "c").FieldType.FullName == "EFT.Interactive.LootableContainer", "released reroll captured container");
Method(server.MainModule.GetType("TarkovaCola.Server.PerkStore"), "Save", "System.Void", false, "System.String", "System.String");
Method(server.MainModule.GetType("TarkovaCola.Server.PerkStore"), "Load", "System.String", false, "System.String");
// Verify actual released IL data, not only a copied source fixture. Each AugDef
// initialization stores Id before Goal. This reviewed compiler shape is bounded.
var il = Client("Perks").Methods.Single(m => m.Name == ".cctor").Body.Instructions;
var descriptions = new List<string>(); string? perk = null, aug = null;
for (int i = 1; i < il.Count; i++)
{
    if (il[i].OpCode.Code != Mono.Cecil.Cil.Code.Stfld || il[i].Operand is not FieldReference f) continue;
    if (f.DeclaringType.Name == "PerkDef" && f.Name == "Id") { perk = (string)il[i - 1].Operand; descriptions.Add(TarkovaColaResearchCatalog.Perk(perk)); }
    if (f.DeclaringType.Name == "AugDef" && f.Name == "Id") aug = (string)il[i - 1].Operand;
    if (f.DeclaringType.Name == "AugDef" && f.Name == "Goal")
    {
        var op = il[i - 1]; int n = op.OpCode.Code switch
        { Mono.Cecil.Cil.Code.Ldc_I4 => (int)op.Operand, Mono.Cecil.Cil.Code.Ldc_I4_S => (sbyte)op.Operand,
          Mono.Cecil.Cil.Code.Ldc_I4_0 => 0, Mono.Cecil.Cil.Code.Ldc_I4_1 => 1, Mono.Cecil.Cil.Code.Ldc_I4_2 => 2,
          Mono.Cecil.Cil.Code.Ldc_I4_3 => 3, Mono.Cecil.Cil.Code.Ldc_I4_4 => 4, Mono.Cecil.Cil.Code.Ldc_I4_5 => 5,
          Mono.Cecil.Cil.Code.Ldc_I4_6 => 6, Mono.Cecil.Cil.Code.Ldc_I4_7 => 7, Mono.Cecil.Cil.Code.Ldc_I4_8 => 8, _ => throw new Exception("Unknown goal IL") };
        descriptions.Add(TarkovaColaResearchCatalog.Challenge(perk!, aug!, n));
    }
}
if (!latest)
{
var syn = Client("Synergy").Methods.Single(m => m.Name == ".cctor").Body.Instructions; string? sid = null;
for (int i = 1; i < syn.Count; i++)
{
    if (syn[i].OpCode.Code != Mono.Cecil.Cil.Code.Stfld || syn[i].Operand is not FieldReference f) continue;
    if (f.DeclaringType.Name == "SynDef" && f.Name == "Id") sid = (string)syn[i - 1].Operand;
    if (f.DeclaringType.Name == "SynDef" && f.Name == "Goal")
    {
        var op = syn[i - 1]; int n = op.Operand is int v ? v : op.Operand is sbyte b ? b :
            int.Parse(op.OpCode.Code.ToString().Replace("Ldc_I4_", "", StringComparison.Ordinal));
        descriptions.Add(TarkovaColaResearchCatalog.Synergy(sid!, n));
    }
}
}
Check(descriptions.Count == (latest ? 135 : 96), "released perk/challenge/synergy counts");
Check(TarkovaColaResearchCatalog.Fingerprint(descriptions) == (latest ? TarkovaColaResearchCatalog.LatestResearchFingerprint : TarkovaColaResearchCatalog.CurrentResearchFingerprint), "released IL matches reviewed research catalog");
TarkovaColaResearchCatalog.RequireResearch(descriptions);
// Independently read the released client identities and server rewards. Source
// generation is not the oracle: every supported entry must match actual DLL IL.
int Number(Instruction op) => op.OpCode.Code switch
{
    Code.Ldc_I4 => (int)op.Operand, Code.Ldc_I4_S => (sbyte)op.Operand,
    Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3,
    Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8,
    _ => throw new Exception("Unreviewed integer IL")
};
string? achKey = null;
var achRecords = new List<string>();
var achIL = Client("Achievements").Methods.Single(m => m.Name == ".cctor").Body.Instructions;
for (int i = 1; i < achIL.Count; i++)
{
    if (achIL[i].OpCode.Code != Code.Stfld || achIL[i].Operand is not FieldReference f || f.DeclaringType.Name != "Def") continue;
    if (f.Name == "Key") achKey = (string)achIL[i - 1].Operand;
    if (f.Name == "ServerId") achRecords.Add(TarkovaColaResearchCatalog.Achievement(achKey!, (string)achIL[i - 1].Operand));
}
Check(achRecords.Count == (latest ? 127 : 100), "released achievement count");
TarkovaColaResearchCatalog.RequirePair(descriptions, achRecords);
var expectedCatalog = latest ? RewardColaCatalog.LatestVersion : RewardColaCatalog.CurrentVersion;
Check(TarkovaColaResearchCatalog.Fingerprint(achRecords) == TarkovaColaResearchCatalog.Fingerprint(
    RewardColaCatalog.AchievementsFor(expectedCatalog).Values.Select(a => TarkovaColaResearchCatalog.Achievement(a.Key, a.Id))), "released achievement identities match reward catalog");
var expectedResearch = RewardColaCatalog.PerksFor(expectedCatalog).Select(TarkovaColaResearchCatalog.Perk)
    .Concat(RewardColaCatalog.ChallengesFor(expectedCatalog).Select(c => TarkovaColaResearchCatalog.Challenge(c.Value.Perk, c.Key, c.Value.Goal)))
    .Concat(latest ? Array.Empty<string>() : RewardColaCatalog.Synergies.Select(c => TarkovaColaResearchCatalog.Synergy(c.Key, c.Value)));
Check(TarkovaColaResearchCatalog.Fingerprint(expectedResearch) == TarkovaColaResearchCatalog.Fingerprint(descriptions), "released goals match server reward catalog");
var rewardIL = server.MainModule.GetType("TarkovaCola.Server.AchievementData").Methods.Single(m => m.Name == ".cctor").Body.Instructions;
string? rewardId = null, serverKey = null;
var rewards = new List<RewardColaItem>(); int rewardCount = 0;
for (int i = 2; i < rewardIL.Count; i++)
{
    var op = rewardIL[i];
    if (op.OpCode.Code == Code.Newobj && op.Operand is MethodReference ctor && ctor.DeclaringType.FullName.StartsWith("System.ValueTuple`2<", StringComparison.Ordinal))
        rewards.Add(new((string)rewardIL[i - 2].Operand, Number(rewardIL[i - 1])));
    if (op.OpCode.Code != Code.Stfld || op.Operand is not FieldReference f || f.DeclaringType.Name != "Def") continue;
    if (f.Name == "Id") rewardId = (string)rewardIL[i - 1].Operand;
    if (f.Name == "Key") serverKey = (string)rewardIL[i - 1].Operand;
    if (f.Name == "Rewards")
    {
        var expected = RewardColaCatalog.ByIdFor(expectedCatalog)[rewardId!];
        Check(expected.ServerKey == serverKey && expected.Items.SequenceEqual(rewards), "released achievement rewards " + rewardId);
        rewards.Clear(); rewardCount++;
    }
}
Check(rewardCount == RewardColaCatalog.ByIdFor(expectedCatalog).Count, "all release rewards independently matched");
string adapter = File.ReadAllText(Environment.GetEnvironmentVariable("ARENA_COLA_ADAPTER_SOURCE") ?? Path.Combine(repo, "ArenaMode.Client/Compat/ArenaTarkovaColaCompat.cs"));
Check(adapter.Contains("Patch(contract.MachinesReady, nameof(PlayerReadyPrefix))", StringComparison.Ordinal), "machine ready hook uses existing physical-rebind guard");
Check(adapter.Contains("foreach (var method in contract.ProtectedMutations) Patch(method, nameof(PersistPrefix))", StringComparison.Ordinal), "new mutation boundaries use protected lease");
Check(adapter.Contains("private static bool PersistPrefix() => !Blocks;", StringComparison.Ordinal), "ordinary persistence passes through");
Check(adapter.Contains("foreach (var reset in _contract.NewPerkCleanup) Invoke(reset);", StringComparison.Ordinal), "shared fuse/container cleanup runs before release");
Check(adapter.Contains("Patch(contract.RerollCallback, nameof(RerollCallbackPrefix), nameof(RerollCallbackPostfix), finalizer: nameof(RerollCallbackFinalizer))", StringComparison.Ordinal), "late HTTP continuation bound");
Check(adapter.Contains("TarkovaColaPolicy.AllowsDelayedAction(_lease, owner.Lease,", StringComparison.Ordinal), "late HTTP uses physical body lease");
Check(adapter.Contains("Patch(contract.RerollClosureCtor, postfix: nameof(RerollClosurePostfix))", StringComparison.Ordinal), "exact request closure tagged at construction");
Check(adapter.Contains("ConditionalWeakTable<JObject, RerollResponse> RerollResponses", StringComparison.Ordinal), "responses have their own ownership identity");
var callbackSource = adapter.Split("private static bool RerollCallbackPrefix", StringSplitOptions.None)[1].Split("private static bool RerollApplyPrefix", StringSplitOptions.None)[0];
Check(!callbackSource.Contains("CurrentWorld()", StringComparison.Ordinal) && !callbackSource.Contains("HealthController", StringComparison.Ordinal)
    && !callbackSource.Contains("RespawnApi.IsReplacing", StringComparison.Ordinal), "background callback contains no Unity or replacement-state queries");
ArenaMode.Compat.ArenaTarkovaColaCompat.CheckRerolls(Check);
if (latest)
{
    Method(Client("PerkService"), "RequestOffering", "System.Void", true, "System.String");
    Method(Client("PerkService"), "FlushRefunds", "System.Void", true);
    Check(Client("PerkService").Fields.Single(f => f.Name == "_pendingOffering").FieldType.FullName == "System.String", "released pending offering contract");
    Method(server.MainModule.GetType("TarkovaCola.Server.OfferingStore"), "Send", "System.Boolean", false, "SPTarkov.Server.Core.Models.Common.MongoId", "System.String");
    Check(adapter.Contains("ProtectedMutations.Add(M(\"PerkService\", \"RequestOffering\", typeof(void), typeof(string)))", StringComparison.Ordinal), "offering creation protected");
    Check(adapter.Contains("ProtectedMutations.Add(M(\"PerkService\", \"FlushRefunds\", typeof(void)))", StringComparison.Ordinal), "pending ordinary offerings cannot drain during lease");
    Check(adapter.Contains("CaptureOffering();", StringComparison.Ordinal) && adapter.Contains("RestoreOffering();", StringComparison.Ordinal), "offering preservation wired");
    string serverAdapter = File.ReadAllText(Environment.GetEnvironmentVariable("ARENA_COLA_SERVER_SOURCE") ?? Path.Combine(repo, "ArenaMode.Server/Compat/ArenaTarkovaColaState.cs"));
    Check(serverAdapter.Contains("Patch(offering, \"Send\", typeof(bool), [typeof(MongoId), typeof(string)], nameof(Refund))", StringComparison.Ordinal), "server offering uses scoped item-refund lock");
    foreach (string name in new[] { "DoubleTap", "ElectricCherry", "QuickRevive", "DeathPerception", "Tombstone", "MuleKick" })
        Method(Client(name), "Reset", "System.Void", true);
}
ArenaMode.Compat.ArenaTarkovaColaCompat.CheckOfferings(Check);
Console.WriteLine($"PASS ColaReleaseChecks ({checks} assertions; official {(latest ? "4.0.0" : "3.0.1")} metadata/IL, source wiring and extracted lifecycle; no native gameplay)");
