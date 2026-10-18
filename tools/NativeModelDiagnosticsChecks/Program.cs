using ArenaMode.Diagnostics;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

int checks = 0;
void Check(bool value, string reason)
{
    checks++;
    if (!value) throw new InvalidOperationException(reason);
}
object Hook(string name, params object[] arguments)
    => typeof(ArenaNativeModelDiagnostics).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
void ClearLogs() { ArenaLog.Lines.Clear(); Component.HierarchyReads = 0; }

// Execute the production transpiler on a tiny branch-equivalent method. Both
// original Error calls remain; ordinary frames do not call the observer at all.
var dynamic = new DynamicMethod("UtilityTimeoutFixture", typeof(void), new[] { typeof(Player.FirearmController.FirearmOperation), typeof(bool), typeof(bool) }, typeof(Program).Module, true);
var il = dynamic.GetILGenerator(); var noAnimator = il.DefineLabel(); var done = il.DefineLabel();
var noAnimatorText = new CodeInstruction(OpCodes.Ldstr, "UtilityOperationEvent not found. No animator!"); noAnimatorText.labels.Add(noAnimator);
var ret = new CodeInstruction(OpCodes.Ret); ret.labels.Add(done);
var originalError = typeof(NativeAlarm).GetMethod(nameof(NativeAlarm.Error));
var instructions = new[] {
    new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Brfalse, done),
    new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Brfalse, noAnimator),
    new CodeInstruction(OpCodes.Ldstr, "UtilityOperationEvent not found on "), new CodeInstruction(OpCodes.Call, originalError),
    new CodeInstruction(OpCodes.Br, done), noAnimatorText, new CodeInstruction(OpCodes.Call, originalError), ret
};
var transformed = ((IEnumerable<CodeInstruction>)Hook("UtilityTranspiler", (object)instructions)).ToList();
Check(transformed.Count == instructions.Length + 4 && instructions.All(x => transformed.Contains(x)), "exactly two observational calls; all original instructions retained");
foreach (var instruction in transformed)
{
    foreach (var label in instruction.labels) il.MarkLabel(label);
    if (instruction.operand == null) il.Emit(instruction.opcode);
    else if (instruction.operand is Label label) il.Emit(instruction.opcode, label);
    else if (instruction.operand is string text) il.Emit(instruction.opcode, text);
    else if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method);
    else throw new InvalidOperationException("Unexpected fixture operand");
}
var utilityCall = (Action<Player.FirearmController.FirearmOperation, bool, bool>)dynamic.CreateDelegate(typeof(Action<Player.FirearmController.FirearmOperation, bool, bool>));
try
{
    ((IEnumerable<CodeInstruction>)Hook("UtilityTranspiler", (object)new[] { new CodeInstruction(OpCodes.Ret) })).ToList();
    Check(false, "changed anchors must refuse installation");
}
catch (InvalidOperationException ex) { Check(ex.Message.Contains("anchors changed"), "changed utility IL refused"); }

// Production source is linked above. These stand-ins permit deterministic
// faults without manufacturing broken player models in a real raid. Native
// method/field signatures and error branches are separately verified below.
ArenaNativeModelDiagnostics.Enable();
Check(Harmony.Hooks.Count == 7 && ArenaMode.Plugin.ModLogger.Warnings.Count == 0, "all seven explicit bindings installed");
ArenaNativeModelDiagnostics.Enable();
Check(Harmony.Hooks.Count == 7, "registration idempotent");
var game = new LocalGame();
var world = new GameWorld();
Singleton<AbstractGame>.Instance = game;
Singleton<GameWorld>.Instance = world;
ArenaSession.ExactGame = game; ArenaSession.ExactWorld = world; ArenaSession.IsActive = true;
var body = new PlayerBody();
var prefab = new WeaponPrefab();
var original = new NullReferenceException("original native fault");
var missingMaterial = new FirearmsEffects();
var muzzle = new MuzzleManager(); missingMaterial.SetMuzzle(muzzle);
var utility = new Player.FirearmController.UtilityOperation { State = Player.EOperationState.Executing, _utilityTime = 2.6f, Player = new Player(), Weapon = new() };
var launcher = new Player.FirearmController.LauncherUtilityOperation { State = Player.EOperationState.Executing, _utilityTime = 2.6f, Player = new Player(), Weapon = new() };
var custom = new CustomItem(); var skin = new CustomItemSkin();

void AllFaults()
{
    Hook("BodyDestroyPrefix", body);
    Hook("EffectsInitPostfix", missingMaterial);
    Check(ReferenceEquals(Hook("WeaponParentFinalizer", prefab, null, original), original), "original exception instance returned");
    utilityCall(utility, true, true); utilityCall(launcher, true, false);
    Hook("CustomItemPrefix", custom, body); Hook("CustomSkinPrefix", skin, body);
}
AllFaults();
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "disabled probes perform no hierarchy reads or output on faults");
ArenaLog.DeveloperEnabled = true;
ArenaSession.IsActive = false;
AllFaults();
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "ordinary raid untouched with developer enabled");
ArenaSession.IsActive = true; ArenaSession.ExactWorld = new();
AllFaults();
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "stale Arena latch does not authorize another world");
ArenaSession.ExactWorld = world; ArenaSession.ExactGame = new LocalGame();
AllFaults();
Check(ArenaLog.Lines.Count == 0, "stale Arena game denied");
ArenaSession.ExactGame = game;

var disposed = new PlayerBody(); disposed.Dispose();
Hook("BodyDestroyPrefix", disposed);
muzzle.JetMaterial = new object(); Hook("EffectsInitPostfix", missingMaterial);
Hook("WeaponParentFinalizer", prefab, null, null);
utilityCall(utility, false, true); utilityCall(launcher, false, false);
custom._itemType = EClippingCustoms.Hat; Hook("CustomItemPrefix", custom, body);
skin._itemType = EClippingCustoms.Hat; Hook("CustomSkinPrefix", skin, body);
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "healthy model/setup paths and non-timeout operations are silent");

muzzle.JetMaterial = null;
custom._itemType = skin._itemType = EClippingCustoms.None;
AllFaults();
Check(ArenaLog.Lines.Count == 7 && ArenaLog.Lines.All(x => x.Contains("map=factory4_day") && x.Contains("phase=active/Started")), "all fault families carry exact map/phase context");
Check(ArenaLog.Lines.Any(x => x.Contains("bodyProfile=body-fixture-profile") && x.Contains("destroyStack=")), "undisposed body has ID and bounded destroy stack");
Check(ArenaLog.Lines.Any(x => x.Contains("weaponTemplate=weapon-template") && x.Contains("original native fault")), "weapon exception carries prefab template and original failure");
Check(utility.State == Player.EOperationState.Executing && utility._utilityTime == 2.6f, "diagnostic does not perform utility fallback or reset timer");
Check(NativeAlarm.Errors > 0, "original utility errors execute with and without probes");
body.OnDestroy(); Check(body.OriginalErrors == 1, "native body alarm still executes after probe");
AllFaults(); ClearLogs(); AllFaults();
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "each object/family limited to two reports before costly reads");

ArenaSession.ArmEpoch++;
for (int i = 0; i < 80; ++i) Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count(x => x.Contains("family=")) == 24 && ArenaLog.Lines.Count(x => x.Contains("capped at")) == 1, "object churn bounded to 24 contexts and one notice per raid");
ClearLogs(); world.MainPlayer = new Player(); Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "local respawn does not reset raid budget");
ArenaSession.ArmEpoch++; Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count == 1, "new confirmed arm receives a new budget");

ClearLogs();
var worldBody = new PlayerBody();
world.RegisteredPlayer = new Player { PlayerBody = worldBody }; worldBody.ParentComponent = world.RegisteredPlayer;
world.MainPlayer = world.RegisteredPlayer;
Hook("BodyDestroyPrefix", worldBody);
Check(ArenaLog.Lines.Single().Contains("currentWorldBody=same") && ArenaLog.Lines.Single().Contains("role=main-player"), "actual registered local body distinguishable");
ClearLogs();
var cloneBody = new PlayerBody { ParentComponent = new Player { IsAI = true } };
Hook("BodyDestroyPrefix", cloneBody);
Check(ArenaLog.Lines.Single().Contains("currentWorldBody=different"), "same-profile clone or former body is not called current world body");
ClearLogs();
var destroyed = new PlayerBody { Destroyed = true };
Hook("BodyDestroyPrefix", destroyed);
Check(ArenaLog.Lines.Single().Contains("PlayerBody#" + destroyed.GetInstanceID() + "/destroyed") && ArenaLog.Lines.Single().Contains("bodyProfile=body-fixture-profile"), "destroyed Unity wrapper retains managed instance/profile context");
Check(ArenaLog.Lines.Single().Contains("bodyCustomization=fixture-body-template"), "body customization identifies clipping assets");

ClearLogs(); ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMap = "factory4_day";
ArenaServerClient.HasPendingArm = true; ArenaServerClient.PendingArmToken = "confirmed-token"; game.Status = GameStatus.Starting;
Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Single().Contains("armed-loading/Starting"), "confirmed matching loading map captured");
foreach (var invalid in new[] { "token", "server", "map", "empty-map", "ended", "menu" })
{
    ClearLogs();
    if (invalid == "token") ArenaServerClient.PendingArmToken = null;
    if (invalid == "server") ArenaServerClient.HasPendingArm = false;
    if (invalid == "map") world.LocationId = "woods";
    if (invalid == "empty-map") world.LocationId = "";
    if (invalid == "ended") game.Status = GameStatus.Stopping;
    if (invalid == "menu") Singleton<AbstractGame>.Instance = new AbstractGame();
    AllFaults();
    Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "reject loading mismatch: " + invalid);
    ArenaServerClient.PendingArmToken = "confirmed-token"; ArenaServerClient.HasPendingArm = true;
    world.LocationId = "factory4_day"; game.Status = GameStatus.Starting; Singleton<AbstractGame>.Instance = game;
}
ClearLogs();
var broken = new WeaponPrefab { FailHierarchy = true };
Check(ReferenceEquals(Hook("WeaponParentFinalizer", broken, null, original), original), "probe's own read error never replaces original exception");
Check(ArenaLog.Lines.Count == 1 && ArenaLog.Lines[0].Contains("its own read failure"), "own probe error is visible once, then disables probe");
ClearLogs(); AllFaults(); Check(ArenaLog.Lines.Count == 0, "read failure cannot cause recursive error storm");
ArenaNativeModelDiagnostics.Shutdown(); Check(Harmony.Hooks.Count == 0, "shutdown unpatches all diagnostic bindings");

// Validate the real installed EFT API and native error branch ownership without
// loading Unity into the test process. No decompiled-text-only contract checks.
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string nativePath = Path.GetFullPath(Path.Combine(repo, "../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
using var native = ModuleDefinition.ReadModule(nativePath);
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; }
}
var types = AllTypes(native.Types).ToDictionary(t => t.FullName);
MethodDefinition Native(string type, string name, params string[] args)
{
    var matches = types[type].Methods.Where(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args)).ToArray();
    Check(matches.Length == 1, "native exact signature " + type + "." + name);
    return matches.Single();
}
void HasString(MethodDefinition method, string text)
    => Check(method.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && ((string)i.Operand).Contains(text)), "native error branch owned by " + method.FullName);
HasString(Native("EFT.PlayerBody", "OnDestroy"), "PlayerBody destroyed without being disposed");
HasString(Native("FirearmsEffects", "Init", "UnityEngine.Transform"), "JetMaterial == null");
var parent = Native("WeaponPrefab", "Parent", "EFT.IPlayer");
Check(parent.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "SetAnimatorGetter"), "weapon parent owns animator setup seam");
foreach (string operation in new[] { "UtilityOperation", "LauncherUtilityOperation" })
{
    var update = Native("EFT.Player/FirearmController/" + operation, "Update", "System.Single");
    HasString(update, "UtilityOperationEvent not found");
    Check(update.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_R4 && (float)i.Operand == 2.5f), "native timeout remains 2.5 seconds");
    Check(update.Body.ExceptionHandlers.Count == 0, "native update has no unsupported exception block");
    Check(update.Body.Instructions.Count(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && ((string)i.Operand).StartsWith("UtilityOperationEvent not found")) == 2, "exact two native error anchors");
}
HasString(Native("CustomItem", "Init", "EFT.PlayerBody"), "Unknown custom item type");
HasString(Native("CustomItemSkin", "Init", "Diz.Skinning.Skeleton", "EFT.PlayerBody"), "Unknown custom item type");
foreach (var item in new[] { ("EFT.PlayerBody", "_active", "System.Boolean"), ("EFT.PlayerBody", "_playerProfileID", "System.String"), ("FirearmsEffects", "_muzzleManager", "MuzzleManager"), ("WeaponPrefab", "_weaponData", "EFT.InventoryLogic.Weapon") })
    Check(types[item.Item1].Fields.Any(f => f.Name == item.Item2 && f.FieldType.FullName == item.Item3), "native reflected field " + item.Item1 + "." + item.Item2);
Console.WriteLine($"NATIVE_MODEL_DIAGNOSTICS_CHECKS_PASSED ({checks})");

public static class NativeAlarm
{
    public static int Errors;
    public static void Error(string message) => Errors++;
}
