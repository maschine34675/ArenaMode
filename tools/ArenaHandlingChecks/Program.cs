using ArenaMode.Config;
using ArenaMode.Movement;
using EFT;
using HarmonyLib;
using Redeploy.Core.Engine;
using System.Reflection;
using System.Reflection.Emit;

int checks = 0;
void Need(bool condition, string text) { if (!condition) throw new Exception("ASSERT: " + text); checks++; }
bool Same(float a, float b) => Math.Abs(a - b) < .00001f;
var local = new Player { MovementContext = new() };
ArenaMovementScope.Local = local;
ArenaHandlingPatches.Enable();
Need(SPT.Reflection.Patching.ModulePatch.Targets.Count == 7, "seven exact native hook targets register");
var target = typeof(MovementContext).GetMethod(nameof(MovementContext.ChangeSpeedLimit));
List<CodeInstruction> LandingInstructions() => [new(OpCodes.Ldarg_0), new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2), new(OpCodes.Ldarg_3), new(OpCodes.Callvirt, target), new(OpCodes.Ret)];
var original = LandingInstructions();
var patched = ((IEnumerable<CodeInstruction>)Hook.Call("ArenaHandlingLandingPatch", "Transpiler", original)).ToArray();
Need(patched.Length == original.Count && patched[4].operand is MethodInfo m && m.Name == "ApplyLandingLimit",
    "landing transpiler replaces its single duration-bearing call");
var call = new DynamicMethod("native_landing_call", typeof(void), [typeof(MovementContext), typeof(float), typeof(Player.ESpeedLimit), typeof(float)], typeof(Program).Module, true);
var il = call.GetILGenerator();
foreach (var instruction in patched)
    if (instruction.operand is MethodInfo method) il.Emit(instruction.opcode, method); else il.Emit(instruction.opcode);
var landing = (Action<MovementContext, float, Player.ESpeedLimit, float>)call.CreateDelegate(typeof(Action<MovementContext, float, Player.ESpeedLimit, float>));
foreach (var (patchName, expectedCalls) in new[] { ("ArenaHandlingDrawStartPatch", 1), ("ArenaHandlingHolsterStartPatch", 2) })
{
    var startCall = typeof(Player.ObjectInHandsOperation).GetMethod("Start");
    var startInstructions = Enumerable.Range(0, expectedCalls).SelectMany(_ => new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, startCall) }).ToArray();
    var transformed = ((IEnumerable<CodeInstruction>)Hook.Call(patchName, "Transpiler", (object)startInstructions)).ToArray();
    Need(transformed.Count(x => x.operand is MethodInfo info && info.Name == "BeginHandlingOperation") == expectedCalls,
        "all reviewed draw and holster start branches are wrapped");
    bool refused = false;
    try { Hook.Call(patchName, "Transpiler", (object)new[] { new CodeInstruction(OpCodes.Ret) }); } catch (InvalidOperationException) { refused = true; }
    Need(refused, "drifted operation start IL fails closed at patch installation");
}
foreach (var malformed in new[] { new List<CodeInstruction> { new(OpCodes.Ret) }, new List<CodeInstruction> { new(OpCodes.Call, target), new(OpCodes.Call, target) } })
{
    bool refused = false;
    try { Hook.Call("ArenaHandlingLandingPatch", "Transpiler", malformed); } catch (InvalidOperationException) { refused = true; }
    Need(refused, "changed landing IL fails instead of patching a guessed site");
}
landing(local.MovementContext, .35f, Player.ESpeedLimit.Fall, 4f);
Need(Same(local.MovementContext.Duration, 2f) && Same(local.MovementContext.Speed, .35f), "hard landing halves only duration");
local.MovementContext.ChangeSpeedLimit(.35f, Player.ESpeedLimit.Fall, 4f);
Need(Same(local.MovementContext.Duration, 4f), "ordinary OnJumpEnd call remains unscaled");
foreach (var cause in new[] { Player.ESpeedLimit.Weight, Player.ESpeedLimit.Aiming, Player.ESpeedLimit.Swamp })
{
    landing(local.MovementContext, .4f, cause, 4);
    Need(local.MovementContext.Duration == 4 && local.MovementContext.Cause == cause, "unrelated speed-limit causes unchanged");
}
var foreignContext = new MovementContext();
landing(foreignContext, .5f, Player.ESpeedLimit.Fall, 4);
Need(foreignContext.Duration == 4, "foreign movement context excluded");
ArenaMovementScope.Enabled = false;
landing(local.MovementContext, .5f, Player.ESpeedLimit.Fall, 4);
Need(local.MovementContext.Duration == 4, "scope-off landing remains native");
ArenaMovementScope.Enabled = true;
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -.5f, 0f, .05f, 1.5f })
{
    Settings.LandingRecoveryMultiplier.Value = invalid;
    landing(local.MovementContext, .5f, Player.ESpeedLimit.Fall, 4);
    Need(local.MovementContext.Duration == 4, "invalid recovery factor fails open");
}
Settings.LandingRecoveryMultiplier.Value = .5f;
foreach (float duration in new[] { float.NaN, float.PositiveInfinity, -1f, 0f })
{
    landing(local.MovementContext, .5f, Player.ESpeedLimit.Fall, duration);
    Need(local.MovementContext.Duration.Equals(duration), "invalid native duration is not invented or sanitized");
}

Player.FirearmController controller = null;
NativeAnimator native = null;
T Begin<T>(Player player = null) where T : Player.FirearmController.FirearmOperation, new()
{
    player ??= local;
    controller ??= new();
    if (controller.CurrentOperation is Player.FirearmController.SpawnOperation previous) previous.WeaponAppeared();
    player.HandsController = controller;
    native = (NativeAnimator)controller.FirearmsAnimator.Animator;
    var operation = new T { Player = player, Controller = controller, FirearmsAnimator = controller.FirearmsAnimator };
    controller.CurrentOperation = operation;
    if (operation is Player.FirearmController.SpawnOperation draw) draw.Start(null);
    else if (operation is Player.FirearmController.Remove hide) hide.Start(null);
    else { controller.ManualUpdate(.01f); operation.Start(); }
    return operation;
}
float Draw() => native.GetFloat(11);
void UnrelatedUnchanged() => Need(native.GetFloat(12) == 1.25f && native.GetFloat(13) == 1.75f
    && native.GlobalSpeed == 1.1f && native.FireRate == 9f, "reload fix global animator and fire speed untouched");
var spawn = Begin<Player.FirearmController.SpawnOperation>();
Need(Same(Draw(), 1.8f), "draw operation accelerates its native baseline");
UnrelatedUnchanged();
for (int i = 0; i < 100; i++) { controller.ManualUpdate(.01f); Need(Same(Draw(), 1.8f), "frame updates never compound draw multiplier"); }
spawn.WeaponAppeared();
Need(Same(Draw(), 1.2f), "draw completion restores before native follow-up callbacks");
spawn = Begin<Player.FirearmController.SpawnOperation>();
spawn.Start(null);
Need(Same(Draw(), 1.8f), "reentered start does not compound");
Settings.WeaponDrawSpeedMultiplier.Value = 2;
controller.ManualUpdate(.01f);
Need(Same(Draw(), 2.4f), "live factor change uses original native baseline");
controller.FirearmsAnimator.SetSpeedParameters(1.25f, 1.4f);
Need(Same(Draw(), 2.8f), "native parameter refresh uses fresh draw value once");
controller.ManualUpdate(.01f);
Need(Same(Draw(), 2.8f), "native refreshed draw remains stable");
Begin<Player.FirearmController.Reload>();
Need(Same(Draw(), 1.4f), "reload start restores native draw parameter");
UnrelatedUnchanged();
Begin<Player.FirearmController.Fire>(); Begin<Player.FirearmController.Malfunction>();
Need(Same(Draw(), 1.4f), "fire and malfunction operations receive no multiplier");
Settings.WeaponHolsterSpeedMultiplier.Value = 3f;
var remove = Begin<Player.FirearmController.Remove>();
Need(Same(Draw(), 4.2f), "holster uses separate factor");
UnrelatedUnchanged();
remove.State = Player.EOperationState.Finished;
controller.ManualUpdate(.01f);
Need(Same(Draw(), 1.4f), "finished operation restores baseline");
Begin<Player.FirearmController.OtherDraw>();
Need(Same(Draw(), 1.4f), "unreviewed derived operation remains native");
var foreign = new Player { MovementContext = new() };
Begin<Player.FirearmController.SpawnOperation>(foreign);
Need(Same(Draw(), 1.4f), "foreign player draw is excluded");
Begin<Player.FirearmController.SpawnOperation>();
ArenaMovementScope.Enabled = false;
controller.ManualUpdate(.01f);
Need(Same(Draw(), 1.4f), "scope loss restores own parameter");
ArenaMovementScope.Enabled = true;
Begin<Player.FirearmController.SpawnOperation>();
local.HandsController = new();
controller.ManualUpdate(.01f);
Need(Same(Draw(), 1.4f), "changed physical hands owner restores lease");
var stationary = new Player.FirearmController.SpawnOperation { Player = local, Controller = controller, FirearmsAnimator = controller.FirearmsAnimator };
local.HandsController = controller; controller.CurrentOperation = stationary; stationary.Weapon.IsStationaryWeapon = true;
stationary.Start(null);
Need(Same(Draw(), 1.4f), "stationary firearm excluded");
foreach (float factor in new[] { float.NaN, float.PositiveInfinity, 0f, .5f, 4f })
{
    Settings.WeaponDrawSpeedMultiplier.Value = factor;
    Begin<Player.FirearmController.SpawnOperation>();
    Need(Same(Draw(), 1.4f), "invalid draw factor fails open");
}
Settings.WeaponDrawSpeedMultiplier.Value = 1.5f;
Begin<Player.FirearmController.SpawnOperation>();
controller.FirearmsAnimator.ThrowReload = true;
bool nativeError = false;
try { controller.FirearmsAnimator.SetSpeedParameters(1.25f, 2f); }
catch (InvalidOperationException) { nativeError = true; }
Need(nativeError && Same(Draw(), 1.4f), "native parameter failure before draw restores prior baseline and preserves exception");
Begin<Player.FirearmController.SpawnOperation>();
native.ThrowAfterDraw = true;
nativeError = false;
try { controller.FirearmsAnimator.SetSpeedParameters(1.25f, 2f); }
catch (InvalidOperationException) { nativeError = true; }
Need(nativeError && Same(Draw(), 2f), "native parameter failure after draw restores fresh baseline");
native.SetFloat(11, 1.4f);
foreach (bool after in new[] { false, true })
{
    Begin<Player.FirearmController.SpawnOperation>();
    Settings.WeaponDrawSpeedMultiplier.Value = 2f;
    native.ThrowBeforeDraw = !after; native.ThrowAfterDraw = after;
    controller.ManualUpdate(.01f);
    Need(Same(Draw(), 1.4f), "failed hot factor write restores original owned parameter");
    Settings.WeaponDrawSpeedMultiplier.Value = 1.5f;
}
Begin<Player.FirearmController.SpawnOperation>();
native.SetFloat(11, 7f);
controller.ManualUpdate(.01f);
Need(Draw() == 7f, "foreign parameter writer is preserved");
controller.ManualUpdate(.01f);
Need(Draw() == 7f, "lease relinquishment does not reclaim external value");
Begin<Player.FirearmController.SpawnOperation>();
Need(Same(Draw(), 10.5f), "new operation may acquire current native baseline");
controller.Destroy();
Need(Draw() == 7f && controller.Destroyed, "pool destruction restores before native teardown");
Begin<Player.FirearmController.SpawnOperation>();
var previousNative = native;
controller.FirearmsAnimator.Animator = new NativeAnimator();
controller.ManualUpdate(.01f);
Need(((NativeAnimator)controller.FirearmsAnimator.Animator).GetFloat(11) == 1.2f,
    "pooled animator replacement never receives old baseline");
native = (NativeAnimator)controller.FirearmsAnimator.Animator;
Begin<Player.FirearmController.SpawnOperation>();
RaidLifecycle.End();
Need(Same(Draw(), 1.2f), "raid end restores owned draw value");
Begin<Player.FirearmController.SpawnOperation>();
ArenaHandlingPatches.ResetPerRaidState();
Need(Same(Draw(), 1.2f), "master-off reset restores owned draw value");
Begin<Player.FirearmController.SpawnOperation>();
ArenaHandlingPatches.Shutdown();
Need(Same(Draw(), 1.2f), "shutdown restores owned draw value");
Begin<Player.FirearmController.SpawnOperation>();
Need(Same(Draw(), 1.2f), "disabled hooks cannot reacquire draw ownership");
Console.WriteLine($"ARENA_HANDLING_CHECKS_PASSED ({checks} assertions)");
