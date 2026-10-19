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
AnimationSystem.UnityAnimatorWrapper ConfigureUtility(Player.FirearmController.FirearmOperation operation)
{
    var animator = new AnimationSystem.UnityAnimatorWrapper();
    var firearms = new FirearmsAnimator { Animator = animator };
    var emitter = new AnimationEventSystem.AnimationEventsEmitter { _animator = animator };
    emitter._delayedEvents.Enqueue(new()); emitter._animationEventsStateBehaviours.Add(new());
    var controller = new Player.FirearmController { CurrentOperation = operation, Item = operation.Weapon, FirearmsAnimator = firearms, AnimationEventsEmitter = emitter };
    firearms._eventsConsumers.Add(controller);
    operation.Controller = controller; operation.FirearmsAnimator = firearms;
    operation.Player.HandsController = controller;
    operation.Player.ArmsUpdateMode = Player.EUpdateMode.Manual;
    operation.Player.ArmsUpdateQueue = EUpdateQueue.FixedUpdate;
    operation._utilityOperationType = "CheckMagazine";
    return animator;
}
var utilityAnimator = ConfigureUtility(utility);
var launcherAnimator = ConfigureUtility(launcher);
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
Check(utilityAnimator.Reads == 0 && launcherAnimator.Reads == 0 && world.IdentityReads == 0, "denied contexts perform no utility or identity reads");
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
var utilityLine = ArenaLog.Lines.Single(x => x.Contains("family=utility-event-timeout;"));
Check(utilityLine.Contains("operation=UtilityOperation/CheckMagazine; elapsed=2.6; operationState=Executing")
    && utilityLine.Contains("armsMode=Manual; armsQueue=FixedUpdate; handsCurrent=True"), "fault snapshot includes operation and actual arms scheduling");
Check(utilityLine.Contains("operationCurrent=True") && utilityLine.Contains("itemCurrent=True") && utilityLine.Contains("animatorOwnerCurrent=True")
    && utilityLine.Contains("controllerConsumer=True") && utilityLine.Contains("emitterAnimatorCurrent=True"), "fault snapshot identifies controller item and event ownership");
Check(utilityLine.Contains("eventConsumers=1") && utilityLine.Contains("queuedEvents=1; boundBehaviours=1")
    && utilityLine.Contains("animatorEnabled=False; animatorUpdate=Normal; culling=CullUpdateTransforms; animatorSpeed=0.75"), "fault snapshot captures emitter counts and animator settings");
Check(utilityLine.Contains("initialized=True; animatorEnabled=") && utilityLine.Contains("layers=8; sampledLayers=2; layer0=123/0.42/transition=False; layer1=124/0.42/transition=True/next=456/0.09")
    && utilityAnimator.MaxLayerRead == 1 && utilityAnimator.Reads == 3, "only first two animator layers sampled with transition target");
Check(utility.State == Player.EOperationState.Executing && utility._utilityTime == 2.6f, "diagnostic does not perform utility fallback or reset timer");
Check(ReferenceEquals(utility.Controller.CurrentOperation, utility) && ReferenceEquals(utility.Controller.Item, utility.Weapon)
    && utility.Controller.AnimationEventsEmitter._delayedEvents.Count == 1 && utility.FirearmsAnimator._eventsConsumers.Count == 1,
    "diagnostic does not replace operation item or consume animation events");
Check(NativeAlarm.Errors > 0, "original utility errors execute with and without probes");
body.OnDestroy(); Check(body.OriginalErrors == 1, "native body alarm still executes after probe");
AllFaults(); ClearLogs(); AllFaults();
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "each object/family limited to two reports before costly reads");
Check(utilityAnimator.Reads == 6 && launcherAnimator.Reads == 6, "signature budget rejects utility before animator reads");

ArenaSession.ArmEpoch++;
for (int i = 0; i < 80; ++i) Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count(x => x.Contains("family=")) == 24 && ArenaLog.Lines.Count(x => x.Contains("capped at")) == 1, "object churn bounded to 24 contexts and one notice per raid");
ClearLogs(); world.MainPlayer = new Player(); Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count == 0 && Component.HierarchyReads == 0, "local respawn does not reset raid budget");
utilityCall(utility, true, true);
Check(utilityAnimator.Reads == 6, "raid budget rejects utility before animator reads");
ArenaSession.ArmEpoch++; Hook("BodyDestroyPrefix", new PlayerBody());
Check(ArenaLog.Lines.Count == 1, "new confirmed arm receives a new budget");

ClearLogs();
var worldBody = new PlayerBody();
world.RegisteredPlayer = new Player { PlayerBody = worldBody }; worldBody.ParentComponent = world.RegisteredPlayer;
world.MainPlayer = world.RegisteredPlayer;
world.AlivePlayer = world.RegisteredPlayer; world.RegisteredPlayers.Add(world.RegisteredPlayer);
Hook("BodyDestroyPrefix", worldBody);
Check(ArenaLog.Lines.Single().Contains("currentWorldBody=same") && ArenaLog.Lines.Single().Contains("role=main-player"), "actual registered local body distinguishable");
Check(ArenaLog.Lines.Single().Contains("aliveWorldBody=same; registeredBody=True"), "alive and registered body evidence recorded separately");
ClearLogs();
var cloneBody = new PlayerBody { ParentComponent = new Player { IsAI = true } };
Hook("BodyDestroyPrefix", cloneBody);
Check(ArenaLog.Lines.Single().Contains("currentWorldBody=different"), "same-profile clone or former body is not called current world body");
ClearLogs();
var destroyed = new PlayerBody { Destroyed = true };
Hook("BodyDestroyPrefix", destroyed);
Check(ArenaLog.Lines.Single().Contains("PlayerBody#" + destroyed.GetInstanceID() + "/destroyed") && ArenaLog.Lines.Single().Contains("bodyProfile=body-fixture-profile"), "destroyed Unity wrapper retains managed instance/profile context");
Check(ArenaLog.Lines.Single().Contains("bodyCustomization=fixture-body-template"), "body customization identifies clipping assets");
Check(ArenaLog.Lines.Single().Contains("currentWorldBody=not-checked; aliveWorldBody=not-checked; registeredBody=not-checked"), "destroyed body cannot imply failed registration");

// Exercise actual fault admission with independent identity scenarios, not a
// copied identity algorithm. Empty local-body IDs are a native Player.Init case.
string Identity(PlayerBody subject, Player owner, string registeredId, PlayerBody registeredBody, bool registeredDestroyed = false)
{
    ClearLogs(); ArenaSession.ArmEpoch++; world.IdentityReads = 0;
    world.RegisteredPlayer = registeredId == null ? null : new Player { ProfileId = registeredId, PlayerBody = registeredBody };
    if (!ReferenceEquals(world.RegisteredPlayer, null)) world.RegisteredPlayer.Destroyed = registeredDestroyed;
    world.AlivePlayer = world.RegisteredPlayer; world.RegisteredPlayers.Clear();
    if (!ReferenceEquals(world.RegisteredPlayer, null)) world.RegisteredPlayers.Add(world.RegisteredPlayer);
    Hook("Report", "identity-fixture", subject, subject, owner, null, (Func<string>)(() => "fixture=true"));
    return ArenaLog.Lines.Single();
}
var emptyBody = new PlayerBody(); emptyBody.SetProfileId("");
var liveOwner = new Player { ProfileId = "live-owner", PlayerBody = emptyBody };
string identity = Identity(emptyBody, liveOwner, "live-owner", emptyBody);
Check(identity.Contains("bodyProfile=none; ownerProfile=live-owner; ownerBodyMatch=True; lookupProfile=live-owner; lookupSource=owner")
    && identity.Contains("currentWorldBody=same; aliveWorldBody=same; registeredBody=True") && world.IdentityReads == 2,
    "empty body ID uses exact live owner identity and matches world");
identity = Identity(emptyBody, liveOwner, "live-owner", emptyBody, registeredDestroyed: true);
Check(identity.Contains("lookupSource=owner; currentWorldBody=destroyed-player; aliveWorldBody=destroyed-player; registeredBody=True") && world.IdentityReads == 2,
    "lookup returning a destroyed player is distinct from no entry and retains exact list membership");
emptyBody.SetProfileId("raw-disagrees");
identity = Identity(emptyBody, liveOwner, "live-owner", emptyBody);
Check(identity.Contains("bodyProfile=raw-disagrees") && identity.Contains("lookupProfile=live-owner; lookupSource=owner; currentWorldBody=same"), "matching owner wins while raw body mismatch remains visible");
emptyBody.SetProfileId(""); liveOwner.PlayerBody = new PlayerBody();
identity = Identity(emptyBody, liveOwner, "live-owner", liveOwner.PlayerBody);
Check(identity.Contains("ownerBodyMatch=False; lookupProfile=none; lookupSource=none; currentWorldBody=not-checked") && world.IdentityReads == 0,
    "stale owner cannot lend identity to an empty former body");
emptyBody.SetProfileId("raw-body");
identity = Identity(emptyBody, liveOwner, "raw-body", emptyBody);
Check(identity.Contains("lookupProfile=raw-body; lookupSource=body; currentWorldBody=same"), "stale owner falls back to available raw body identity");
identity = Identity(emptyBody, liveOwner, "live-owner", liveOwner.PlayerBody);
Check(identity.Contains("lookupSource=body; currentWorldBody=unregistered") && world.IdentityReads == 2, "known raw identity with no world match is checked unregistered");
identity = Identity(emptyBody, null, "raw-body", emptyBody);
Check(identity.Contains("ownerProfile=none; ownerBodyMatch=False") && identity.Contains("lookupSource=body; currentWorldBody=same"), "unowned body retains usable raw identity");
emptyBody.SetProfileId("");
identity = Identity(emptyBody, null, null, null);
Check(identity.Contains("lookupSource=none; currentWorldBody=not-checked; aliveWorldBody=not-checked; registeredBody=not-checked") && world.IdentityReads == 0, "unknown identity is not checked rather than called unregistered");
liveOwner.PlayerBody = emptyBody; liveOwner.ProfileId = "";
identity = Identity(emptyBody, liveOwner, null, null);
Check(identity.Contains("ownerBodyMatch=True; lookupProfile=none; lookupSource=none; currentWorldBody=not-checked"), "matched owner without profile is still unknown");
emptyBody.SetProfileId("raw-body");
identity = Identity(emptyBody, liveOwner, "raw-body", emptyBody);
Check(identity.Contains("lookupSource=body; currentWorldBody=same"), "matched owner without profile can use raw body identity");
liveOwner.ProfileId = "live-owner"; liveOwner.Destroyed = true; emptyBody.SetProfileId("");
identity = Identity(emptyBody, liveOwner, "live-owner", emptyBody);
Check(identity.Contains("ownerBodyMatch=False; lookupProfile=none; lookupSource=none; currentWorldBody=not-checked")
    && identity.Contains("owner=Player#" + liveOwner.GetInstanceID() + "/destroyed") && world.IdentityReads == 0, "destroyed owner cannot lend identity but remains visible");
liveOwner.Destroyed = false; emptyBody.SetProfileId("raw-body"); emptyBody.Destroyed = true;
identity = Identity(emptyBody, liveOwner, "raw-body", emptyBody);
Check(identity.Contains("ownerBodyMatch=False") && identity.Contains("lookupSource=body; currentWorldBody=not-checked") && world.IdentityReads == 0,
    "destroyed body never queries registration even with an available raw ID");
identity = Identity(null, liveOwner, "live-owner", emptyBody);
Check(identity.Contains("currentWorldBody=not-checked") && world.IdentityReads == 0, "owner's destroyed body cannot borrow a live identity");
liveOwner.PlayerBody = null;
identity = Identity(null, liveOwner, "live-owner", null);
Check(identity.Contains("ownerBodyMatch=False; lookupProfile=none; lookupSource=none; currentWorldBody=not-checked") && world.IdentityReads == 0,
    "owner without a body cannot authorize registration comparison");

// Fault-only detail remains observational when state is stale or unavailable.
string UtilitySnapshot()
{
    ClearLogs(); ArenaSession.ArmEpoch++;
    utilityCall(utility, true, true);
    return ArenaLog.Lines.Single();
}
utility.Player.HandsController = new(); utility.Controller.CurrentOperation = null;
utility.Controller.Item = new(); utility.Controller.FirearmsAnimator = new();
utility.FirearmsAnimator._eventsConsumers.Clear(); utility.Controller.AnimationEventsEmitter._animator = null;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("handsCurrent=False") && utilityLine.Contains("operationCurrent=False; currentOperation=none; itemCurrent=False; animatorOwnerCurrent=False")
    && utilityLine.Contains("controllerConsumer=False") && utilityLine.Contains("emitterAnimatorCurrent=False"), "stale hands operation item and animator ownership are visible without repair");
int beforeReads = utilityAnimator.Reads;
utilityAnimator.Animator.isInitialized = false;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("initialized=False; animatorState=not-initialized") && utilityAnimator.Reads == beforeReads,
    "uninitialized Unity animator is never queried for layer state");
utilityAnimator.Animator.Destroyed = true;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("animatorState=unavailable") && utilityAnimator.Reads == beforeReads,
    "destroyed Unity animator is never queried for layer state");
utilityAnimator.Animator = null;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("unityAnimator=none; animatorState=unavailable") && utilityAnimator.Reads == beforeReads, "missing Unity animator is never queried for layer state");
utilityAnimator.Animator = new(); utilityAnimator.FailReads = true;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("utilityReadFailure=fixture animator unavailable") && utilityLine.Contains("family=utility-event-timeout"), "animator read failure preserves original fault identity");
utilityAnimator.FailReads = false;
var priorCulture = System.Globalization.CultureInfo.CurrentCulture;
try { System.Globalization.CultureInfo.CurrentCulture = new("de-DE"); utilityLine = UtilitySnapshot(); }
finally { System.Globalization.CultureInfo.CurrentCulture = priorCulture; }
Check(utilityLine.Contains("animatorSpeed=0.75") && utilityLine.Contains("layer0=123/0.42"), "utility partial failure does not disable later context; numeric fields are culture independent");
utility.Controller.Destroyed = true; beforeReads = utilityAnimator.Reads;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("controllerDestroyed=True; controllerState=destroyed") && utilityAnimator.Reads == beforeReads, "disposed controller stops utility detail before animator reads");
utility.Controller.Destroyed = false; ((Component)utility.Controller).Destroyed = true;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("controllerState=unavailable") && utilityAnimator.Reads == beforeReads, "destroyed controller stops utility detail before animator reads");
((Component)utility.Controller).Destroyed = false; utility.FirearmsAnimator = null;
utilityLine = UtilitySnapshot();
Check(utilityLine.Contains("eventConsumers=unavailable") && utilityLine.Contains("animator=none"), "missing firearm animator is visible and safe");
Check(NativeAlarm.Errors > 0 && utility.State == Player.EOperationState.Executing && utility._utilityTime == 2.6f, "all unavailable-state controls retain native errors and utility state");

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
foreach (var item in new[] {
    ("EFT.Player/FirearmController/FirearmOperation", "Controller"), ("EFT.Player/FirearmController/FirearmOperation", "FirearmsAnimator"),
    ("ObjectInHandsAnimator", "_eventsConsumers"), ("EFT.GameWorld", "RegisteredPlayers"),
    ("AnimationEventSystem.AnimationEventsEmitter", "_animator"), ("AnimationEventSystem.AnimationEventsEmitter", "_emitType"),
    ("AnimationEventSystem.AnimationEventsEmitter", "_delayedEvents"), ("AnimationEventSystem.AnimationEventsEmitter", "_animationEventsStateBehaviours") })
    Check(types[item.Item1].Fields.Any(f => f.Name == item.Item2 && f.IsPublic), "native public context field " + item.Item1 + "." + item.Item2);
foreach (var item in new[] {
    ("EFT.Player", "ProfileId"), ("EFT.Player", "PlayerBody"), ("EFT.Player", "HandsController"), ("EFT.Player", "ArmsUpdateMode"), ("EFT.Player", "ArmsUpdateQueue"),
    ("EFT.Player/FirearmController", "CurrentOperation"), ("EFT.Player/AbstractHandsController", "AnimationEventsEmitter"), ("EFT.Player/AbstractHandsController", "Item"),
    ("EFT.Player/AbstractHandsController", "FirearmsAnimator"), ("EFT.Player/AbstractHandsController", "Destroyed"), ("ObjectInHandsAnimator", "Animator"), ("AnimationSystem.UnityAnimatorWrapper", "Animator") })
    Check(types[item.Item1].Properties.Any(p => p.Name == item.Item2 && p.GetMethod?.IsPublic == true), "native public context getter " + item.Item1 + "." + item.Item2);
using var animation = ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(nativePath), "AnimationSystem.Types.dll"));
var animatorInterface = AllTypes(animation.Types).Single(t => t.Name == "IAnimator");
foreach (string name in new[] { "name", "enabled", "updateMode", "cullingMode", "speed", "layerCount" })
    Check(animatorInterface.Properties.Any(p => p.Name == name && p.GetMethod != null), "native IAnimator property " + name);
foreach (string name in new[] { "GetCurrentAnimatorStateInfo", "GetNextAnimatorStateInfo", "IsInTransition" })
    Check(animatorInterface.Methods.Any(m => m.Name == name && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32"), "native IAnimator method " + name);
var animatorState = AllTypes(animation.Types).Single(t => t.Name == "AnimatorStateInfoWrapper");
foreach (string name in new[] { "fullPathHash", "normalizedTime" })
    Check(animatorState.Fields.Any(f => f.Name == name && f.IsPublic) || animatorState.Properties.Any(p => p.Name == name && p.GetMethod?.IsPublic == true), "native animator state field " + name);
using var unityAnimation = ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(nativePath), "UnityEngine.AnimationModule.dll"));
Check(unityAnimation.Types.Single(t => t.FullName == "UnityEngine.Animator").Properties.Any(p => p.Name == "isInitialized" && p.GetMethod?.IsPublic == true), "native Unity animator initialization getter");
Console.WriteLine($"NATIVE_MODEL_DIAGNOSTICS_CHECKS_PASSED ({checks})");

public static class NativeAlarm
{
    public static int Errors;
    public static void Error(string message) => Errors++;
}
