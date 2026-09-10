using ArenaMode.Config;
using ArenaMode.Movement;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Reflection.Emit;

int checks = 0;
void Need(bool ok, string message) { if (!ok) throw new Exception("ASSERT: " + message); checks++; }
bool Same(float a, float b) => Math.Abs(a - b) < .00001f;
Player local = null;
Player.FirearmController controller = null;
NativeAnimator arms = null, body = null;
void Fresh()
{
    ArenaReloadHandling.ResetPerRaidState();
    Settings.WeaponReloadSpeedMultiplier.Value = 1.5f;
    local = new Player(); controller = new(); local.HandsController = controller;
    ArenaMovementScope.Local = local; ArenaMovementScope.Enabled = true;
    arms = (NativeAnimator)controller.FirearmsAnimator.Animator;
    body = (NativeAnimator)local.MovementContext.PlayerAnimator.Animator;
}
Player.FirearmController.FirearmOperation Start(Type type)
{
    var operation = (Player.FirearmController.FirearmOperation)Activator.CreateInstance(type);
    operation.Player = local; operation.Controller = controller; operation.FirearmsAnimator = controller.FirearmsAnimator;
    controller.CurrentOperation = operation; operation.State = Player.EOperationState.Executing;
    return operation;
}
void Tick() => ArenaReloadHandling.OnControllerUpdate(controller);
float A() => arms.GetFloat(12);
float B() => body.GetFloat(12);
void Native() => Need(Same(A(), 1.2f) && Same(B(), 1.4f), "native arm and body baselines preserved");
void Boost() => Need(Same(A(), 1.8f) && Same(B(), 2.1f), "both reload animators use their own baseline");
Type[] families = [typeof(Player.FirearmController.ReloadExternalMagOperation), typeof(Player.FirearmController.ReloadInternalMagOperation),
    typeof(Player.FirearmController.ReloadInternalMagWithOpenBoltOperation), typeof(Player.FirearmController.ReloadCylinderMagOperation),
    typeof(Player.FirearmController.ReloadSingleBarrelOperation), typeof(Player.FirearmController.ReloadMultiBarrelOperation)];
ArenaReloadHandling.Enable();
Need(ModulePatch.Targets.Count == 3, "three exact reload lifecycle refresh and death hooks install");
foreach (Type family in families)
{
    Fresh(); var operation = (Player.FirearmController.BaseReloadOperation)Start(family); Tick(); Boost();
    for (int i = 0; i < 100; i++) { Tick(); Boost(); }
    Need(arms.GetFloat(11) == 1.1f && body.GetFloat(11) == 1.1f && arms.GetFloat(13) == 1.7f
        && arms.GlobalSpeed == 1.05f && body.GlobalSpeed == 1.05f && arms.FireRate == 9f, "draw repair global speed and firing remain native");
    operation.BeforeNativeStateChanged = Native;
    operation.State = Player.EOperationState.Finished; Native();
    Tick(); Native();
}
foreach (Type excluded in new[] { typeof(Player.FirearmController.LauncherReload), typeof(Player.FirearmController.FireOperation),
    typeof(Player.FirearmController.BoltActionFireOperation), typeof(Player.FirearmController.Malfunction), typeof(Player.FirearmController.InsertMagOperation),
    typeof(Player.FirearmController.Remove), typeof(Player.FirearmController.UnknownReload) })
{ Fresh(); Start(excluded); Tick(); Native(); }
Fresh(); var current = Start(families[0]); current.Player = new Player { HandsController = controller }; Tick();
Need(A() == 1.2f && B() == 1.4f, "foreign player reload is excluded");
Fresh(); current = Start(families[0]); current.Player.HandsController = new(); Tick(); Native();
Fresh(); current = Start(families[0]); current.Controller = new(); Tick(); Native();
Fresh(); current = Start(families[0]); current.FirearmsAnimator = new(); Tick(); Native();
Fresh(); current = Start(families[0]); current.Weapon.IsStationaryWeapon = true; Tick(); Native();
Fresh(); current = Start(families[0]); current.State = Player.EOperationState.Ready; Tick(); Native();
Fresh(); Start(families[0]); ArenaMovementScope.Enabled = false; Tick(); Native();
foreach (float factor in new[] { float.NaN, float.PositiveInfinity, 0f, -1f, .5f, 1f, 3.01f })
{ Fresh(); Start(families[0]); Settings.WeaponReloadSpeedMultiplier.Value = factor; Tick(); Native(); }
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.MaxValue, -1f, 0f })
{
    Fresh(); Start(families[0]); arms.Values[12] = invalid; Tick();
    Need(A().Equals(invalid) && B() == 1.4f, "invalid baseline never writes either animator");
}
Fresh(); Start(families[0]); body.HasReload = false; Tick(); Native();
Fresh(); Start(families[0]); local.MovementContext.PlayerAnimator.Animator = arms; Tick(); Native();
Fresh(); current = Start(families[0]); Tick(); Settings.WeaponReloadSpeedMultiplier.Value = 2f; Tick();
Need(Same(A(), 2.4f) && Same(B(), 2.8f), "live factor refresh never compounds current speed");
for (int i = 0; i < 100; i++) Tick();
Need(Same(A(), 2.4f) && Same(B(), 2.8f), "repeated refresh does not compound reload speed");
ArenaReloadHandling.SetFirearmSpeeds(controller.FirearmsAnimator, 1.7f, 2.2f);
ArenaReloadHandling.SetBodySpeeds(local.MovementContext.PlayerAnimator, 1.3f, 2.4f);
Need(Same(A(), 3.4f) && Same(B(), 2.6f), "native skill refresh replaces each baseline");
Need(arms.GetFloat(11) == 2.2f && body.GetFloat(11) == 2.4f, "native draw arguments remain unscaled");
current.State = Player.EOperationState.Finished;
Need(A() == 1.7f && B() == 1.3f, "fresh native baselines restore at completion");
Fresh(); Start(families[0]); Tick();
ArenaReloadHandling.SetFirearmSpeeds(controller.FirearmsAnimator, 1, 1);
ArenaReloadHandling.SetBodySpeeds(local.MovementContext.PlayerAnimator, 1, 1);
Need(A() == 1.5f && B() == 1.5f, "arm injury retains native relative penalty");
controller.CurrentOperation.State = Player.EOperationState.Ready;
Need(A() == 1f && B() == 1f, "cancellation restores refreshed injury speed");
foreach (Action change in new Action[] { () => ArenaMovementScope.Enabled = false, () => local.HandsController = new(),
    () => Settings.WeaponReloadSpeedMultiplier.Value = 1, () => controller.CurrentOperation = new Player.FirearmController.FireOperation() })
{ Fresh(); Start(families[0]); Tick(); change(); Tick(); Native(); }
Fresh(); Start(families[0]); Tick(); arms.SetFloat(12, 7); Tick();
Need(A() == 7f && B() == 1.4f, "foreign parameter writer is preserved while owned body restores");
for (int i = 0; i < 100; i++) Tick();
Need(A() == 7f && B() == 1.4f, "foreign writer is not reclaimed by later frames");
body.SetFloat(12, 2.1f); ArenaMovementScope.Enabled = false; Tick();
Need(B() == 2.1f, "already released values are not restored a second time");
Fresh(); Start(families[0]); Tick(); controller.FirearmsAnimator.Animator = new NativeAnimator(9); Tick();
Need(((NativeAnimator)controller.FirearmsAnimator.Animator).GetFloat(12) == 9f, "pooled replacement animator never receives prior baseline");
Fresh(); Start(families[0]); Tick(); local.MovementContext.PlayerAnimator = new(); Tick();
Need(((NativeAnimator)local.MovementContext.PlayerAnimator.Animator).GetFloat(12) == 1.4f, "replacement body animator remains native");
foreach (bool after in new[] { false, true })
{
    Fresh(); Start(families[0]); body.ThrowBefore = !after; body.ThrowAfter = after; Tick(); Native(); Tick(); Native();
    Fresh(); Start(families[0]); Tick(); Settings.WeaponReloadSpeedMultiplier.Value = 2;
    arms.ThrowBefore = !after; arms.ThrowAfter = after; Tick(); Native();
    Fresh(); Start(families[0]); Tick(); arms.ThrowBefore = !after; arms.ThrowAfter = after;
    bool threw = false;
    try { ArenaReloadHandling.SetFirearmSpeeds(controller.FirearmsAnimator, 1.8f, 2); }
    catch (InvalidOperationException) { threw = true; }
    Need(threw && Same(A(), after ? 1.8f : 1.2f) && Same(B(), 1.4f), "native exception preserves native error and restores known baseline");
}
Fresh(); Start(families[0]); Tick(); controller.FirearmsAnimator.ThrowAfterReload = true;
bool failed = false;
try { ArenaReloadHandling.SetFirearmSpeeds(controller.FirearmsAnimator, 1.8f, 2); } catch (InvalidOperationException) { failed = true; }
Need(failed && A() == 1.8f && B() == 1.4f, "native draw failure after reload restores fresh reload baseline");
Fresh(); Start(families[0]); Tick(); arms.ThrowRead = true;
ArenaReloadHandling.SetFirearmSpeeds(controller.FirearmsAnimator, 1.7f, 2);
Need(A() == 1.7f && B() == 1.4f, "hook read failure retains native input and releases both values");
Fresh(); Start(families[0]); Tick(); body.ThrowRead = true;
ArenaReloadHandling.SetBodySpeeds(local.MovementContext.PlayerAnimator, 1.7f, 2);
Need(A() == 1.2f && B() == 1.7f, "body hook read failure retains native input and releases both values");
Fresh(); Start(families[0]); Tick(); ArenaReloadHandling.Release(controller); Native(); Tick(); Boost();
ArenaReloadHandling.ResetPerRaidState(); Native();
Fresh(); Start(families[0]); Tick(); controller.NativeDeath = Native; controller.OnPlayerDead(); Native();
Fresh(); Start(families[0]); Tick(); ArenaReloadHandling.Shutdown(); Native(); Tick(); Native();
Need(ModulePatch.Targets.Count == 0, "shutdown removes reload patches");
foreach (int failAt in new[] { 1, 2, 3 })
{
    ModulePatch.FailEnableAt = failAt; ArenaReloadHandling.Enable();
    Need(ModulePatch.Targets.Count == 0, "partial patch installation rolls back");
    Fresh(); Start(families[0]); Tick(); Native();
}
ModulePatch.FailEnableAt = -1; ArenaReloadHandling.Enable();
ModulePatch.FailDisableCount = 2; ArenaReloadHandling.Shutdown(); ArenaReloadHandling.Enable();
Need(ModulePatch.Targets.Count == 3, "failed removal does not install duplicate reload hooks");
ArenaReloadHandling.Shutdown(); ArenaReloadHandling.Enable();
Need(ModulePatch.Targets.Count == 3, "clean retry installs one copy of each patch");

var armSetter = typeof(FirearmsAnimator).GetMethod("SetSpeedParameters");
var bodySetter = typeof(PlayerAnimator).GetMethod("SetSpeedParameters");
List<CodeInstruction> Calls(int armCount, int bodyCount) => Enumerable.Range(0, armCount).Select(_ => new CodeInstruction(OpCodes.Callvirt, armSetter))
    .Concat(Enumerable.Range(0, bodyCount).Select(_ => new CodeInstruction(OpCodes.Callvirt, bodySetter))).ToList();
var nativeCalls = Calls(3, 2);
var transformed = ((IEnumerable<CodeInstruction>)Hook.Call("RefreshPatch", "Transpiler", nativeCalls)).ToArray();
Need(transformed.Count(x => x.operand is MethodInfo m && m.Name == "SetFirearmSpeeds") == 3
    && transformed.Count(x => x.operand is MethodInfo m && m.Name == "SetBodySpeeds") == 2, "all native refresh branches use exact wrappers");
foreach (var bad in new[] { Calls(2, 2), Calls(3, 1), Calls(4, 2), Calls(3, 3) })
{
    bool rejected = false;
    try { Hook.Call("RefreshPatch", "Transpiler", bad); } catch (InvalidOperationException) { rejected = true; }
    Need(rejected, "drifted native refresh IL is rejected");
}
Fresh(); ArenaMode.Plugin.ModLogger = new();
current = Start(families[0]); Tick(); Tick(); Boost();
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("write-requested") && x.Contains("weapon=weapon-template"))
    && ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("readback-tick-2")), "diagnostic distinguishes request from subsequent readback");
Settings.WeaponReloadSpeedMultiplier.Value = 3; Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("factor-changed") && x.Contains("factor=3")), "diagnostic sees live factor changes");
arms.SetFloat(12, 7); Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("blocked-speed-readback-changed") && x.Contains("reload=7")), "foreign speed readback is reported before restore");
Need(A() == 7 && B() == 1.4f, "diagnostic preserves foreign writer and native restoration");
for (int i = 0; i < 1000; i++) Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Count < 12, "diagnostic does not repeat unchanged refusals per frame");
current.State = Player.EOperationState.Finished;
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("native-state-Finished")), "native completion ends the diagnostic");
Fresh(); ArenaMode.Plugin.ModLogger = new(); Start(families[0]); Settings.WeaponReloadSpeedMultiplier.Value = 1;
for (int i = 0; i < 50; i++) Tick(); Native();
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("factor=1") && x.Contains("readback-tick-30")), "native factor one remains an observable control");
Fresh(); ArenaMode.Plugin.ModLogger = new(); Start(families[0]);
controller.FirearmsAnimator.Animator = new AnimationSystem.UnityAnimatorWrapper(); Tick(); Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("/speed=0.73/mult=2.5")), "diagnostic retains real Unity speed multiplier separately from state speed");
Fresh(); ArenaMode.Plugin.ModLogger = new(); Start(families[0]);
var unityArms = new AnimationSystem.UnityAnimatorWrapper(); unityArms.Animator.ThrowState = true;
controller.FirearmsAnimator.Animator = unityArms; Tick(); Tick();
Need(Same(unityArms.GetFloat(12), 1.8f) && Same(B(), 2.1f)
    && ArenaMode.Plugin.ModLogger.Infos.Any(x => x.Contains("arms=[unavailable]")), "diagnostic-only state getter failure leaves speed lease intact");
controller.CurrentOperation.State = Player.EOperationState.Finished;
Need(unityArms.GetFloat(12) == 1.2f && B() == 1.4f, "diagnostic-only getter failure leaves native cleanup intact");
Fresh(); ArenaMode.Plugin.ModLogger = new();
for (int i = 0; i < 50; i++) { current = Start(families[0]); Tick(); Tick(); current.State = Player.EOperationState.Finished; }
Need(ArenaMode.Plugin.ModLogger.Infos.Count == 96 && ArenaMode.Plugin.ModLogger.Infos.Last().Contains("limit reached"), "diagnostic is capped per raid");
Fresh(); ArenaMode.Plugin.ModLogger = new(); Start(families[0]); current = controller.CurrentOperation;
current.Player = new Player(); Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Count == 0, "diagnostic excludes foreign players");
Fresh(); ArenaMode.Plugin.ModLogger = new(); Start(families[0]); ArenaMovementScope.Enabled = false; Tick();
Need(ArenaMode.Plugin.ModLogger.Infos.Count == 0, "diagnostic excludes ordinary or disabled movement scope");
Fresh(); ArenaMode.Plugin.ModLogger = new() { Throw = true }; Start(families[0]); Tick(); Tick(); Boost();
controller.CurrentOperation.State = Player.EOperationState.Finished; Native();
ArenaReloadHandling.Shutdown();
Console.WriteLine($"ARENA_RELOAD_CHECKS_PASSED ({checks} assertions)");
