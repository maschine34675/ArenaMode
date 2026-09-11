using ArenaMode.Hud;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.UI;
using Redeploy.Core.Engine;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("CHECK FAILED: " + message);
    checks++;
}
CanvasGroup Group(ExtractionTimersPanel panel) => panel._timersPanel.GetComponent<CanvasGroup>();
var probe = new ExtractionTimersPanel();
Check(probe._timersPanel.gameObject.AddComponent<CanvasGroup>() == null
    && probe._timersPanel.gameObject.DuplicateCanvasErrors == 1,
    "Stand-in enforces observed duplicate CanvasGroup rejection");

var game = new AbstractGame(); var world = new GameWorld();
Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
ArenaSession.Game = game; ArenaSession.World = world;
var panel = game.GameUi.TimerPanel;
var native = Group(panel);
native.alpha = .6f; native.ignoreParentGroups = true; native.interactable = false;
int initialAdds = panel._timersPanel.gameObject.AddCalls;
ArenaExtractionList.Enable(); ArenaExtractionList.Enable();
Check(RaidLifecycle.Starts == 1 && RaidLifecycle.Ends == 1 && HarmonyLib.Harmony.Patches == 1, "Enable is idempotent");
ArenaExtractionList.Tick(); RaidLifecycle.Start(world);
Check(native.alpha == .6f && native.enabled, "Ordinary startup leaves native state unchanged");
Check(panel._mainDescription.gameObject.activeSelf && panel._mainTimerPanel.TimerText.gameObject.activeSelf,
    "Ordinary startup retains banner and numeric clock");
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && ArenaExtractionList.BeforeShowTimer(panel, false), "Ordinary show requests remain native");

// Native SetTime already started its list coroutine before confirmation.
ArenaSession.IsActive = true; RaidLifecycle.Start(world);
Check(native.alpha == 0 && native.enabled, "Confirmed startup hides existing native list");
Check(!panel._mainDescription.gameObject.activeSelf && panel._mainTimerPanel.TimerText.gameObject.activeSelf,
    "Confirmed startup hides the entire banner while retaining the clock");
Check(!native.interactable && native.blocksRaycasts && native.ignoreParentGroups, "Unowned input and hierarchy properties remain untouched");
Check(!ArenaExtractionList.BeforeShowTimer(panel, true), "Arena double-O exit list is suppressed");
Check(ArenaExtractionList.BeforeShowTimer(panel, false), "Single-O clock remains native");
Check(ArenaExtractionList.BeforeShowTimer(new ExtractionTimersPanel(), true), "Foreign UI instance remains native");
Check(ArenaExtractionList.BeforeShowTimer(null, true), "Null UI instance remains native");
for (int i = 0; i < 100; i++) ArenaExtractionList.Tick();
Check(panel._timersPanel.gameObject.AddCalls == initialAdds && panel._timersPanel.gameObject.DuplicateCanvasErrors == 0,
    "Repeated frames never add a CanvasGroup or generate engine spam");
Check(ArenaMode.Plugin.ModLogger.Infos.Count == 2, "Successful list and header hiding are each logged once per raid");
native.alpha = 1;
Check(ArenaExtractionList.BeforeShowTimer(panel, true), "A no-longer-hidden mask fails open until refreshed");
ArenaExtractionList.Tick();
Check(native.alpha == 0, "Native list remains hidden after a presentation update");

Singleton<GameWorld>.Instance = new GameWorld();
Check(ArenaExtractionList.BeforeShowTimer(panel, true), "Foreign world with stale latch stays native");
ArenaExtractionList.Tick();
Check(native.alpha == .6f && native.enabled && !native.Destroyed, "Foreign world restores native group synchronously without destruction");
Check(panel._mainDescription.gameObject.activeSelf, "Foreign world restores banner synchronously");
Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = new AbstractGame();
ArenaExtractionList.Tick();
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && native.alpha == .6f, "Foreign game stays native");
Singleton<AbstractGame>.Instance = game; ArenaExtractionList.Tick();
var replacement = new ExtractionTimersPanel(); game.GameUi.TimerPanel = replacement;
replacement._mainDescription.gameObject.SetActive(false);
var replacementGroup = Group(replacement); replacementGroup.alpha = .3f; replacementGroup.enabled = false;
ArenaExtractionList.Tick();
Check(native.alpha == .6f && !native.Destroyed && replacementGroup.alpha == 0 && replacementGroup.enabled,
    "Replacement UI restores old group and temporarily enables only new mask");
Check(panel._mainDescription.gameObject.activeSelf && !replacement._mainDescription.gameObject.activeSelf,
    "Replacement UI restores old banner without activating a pre-hidden new banner");
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && !ArenaExtractionList.BeforeShowTimer(replacement, true), "Only replacement panel owns the prefix");
ArenaSession.IsActive = false; RaidLifecycle.End();
Check(replacementGroup.alpha == .3f && !replacementGroup.enabled && !replacementGroup.Destroyed, "End restores initially disabled native group");
Check(!replacement._mainDescription.gameObject.activeSelf, "End preserves initially hidden native banner");
Check(native.alpha == .6f && native.ignoreParentGroups && !native.interactable, "End leaves native group settings intact");

ArenaSession.IsActive = true; replacement._timersPanel = null; RaidLifecycle.Start(world); ArenaExtractionList.Tick();
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 0, "Late UI availability is a normal state");
replacement._timersPanel = new ExtractionTimersPanel()._timersPanel; ArenaExtractionList.Tick();
Check(Group(replacement).alpha == 0, "Late native list is caught without HUD visibility dependency");
var lateGroup = Group(replacement);
Singleton<GameWorld>.Instance = null; ArenaExtractionList.Tick();
Check(lateGroup.alpha == 1 && !lateGroup.Destroyed, "Missing world restores previous group");
Singleton<GameWorld>.Instance = world;

var broken = replacement._timersPanel.gameObject; broken.ThrowOnGet = true;
int reads = broken.GetCalls, adds = broken.AddCalls;
RaidLifecycle.Start(world);
for (int i = 0; i < 100; i++) ArenaExtractionList.Tick();
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 1 && broken.GetCalls == reads + 1 && broken.AddCalls == adds,
    "Native lookup failure is attempted once per list, not retried every frame");
Check(ArenaMode.Plugin.ModLogger.Warnings.Single().Contains("InvalidOperationException")
    && ArenaMode.Plugin.ModLogger.Warnings.Single().Contains("GetComponent"), "Bounded diagnostic includes exception type and stack");
Check(ArenaExtractionList.BeforeShowTimer(replacement, true), "Mask failure leaves native show path open");
broken.ThrowOnGet = false; ArenaExtractionList.Tick();
Check(lateGroup.alpha == 1, "Failed list stays quarantined for this raid");
replacement._timersPanel = new GameObject().AddComponent<RectTransform>();
var missing = replacement._timersPanel.gameObject;
ArenaExtractionList.Tick();
for (int i = 0; i < 100; i++) ArenaExtractionList.Tick();
Check(missing.AddCalls == 1 && missing.GetCalls == 1 && ArenaExtractionList.BeforeShowTimer(replacement, true),
    "Missing prefab group is left untouched, not created in polling");
replacement._timersPanel = new ExtractionTimersPanel()._timersPanel; ArenaExtractionList.Tick();
var finalGroup = Group(replacement);
Check(finalGroup.alpha == 0, "A fresh native list recovers after failed old list");
finalGroup.alpha = .8f; finalGroup.enabled = false;
ArenaExtractionList.Release();
Check(finalGroup.alpha == .8f && !finalGroup.enabled, "Release preserves externally replaced values");
ArenaExtractionList.Tick();
HarmonyLib.Harmony.ThrowOnUnpatch = true;
ArenaExtractionList.Shutdown(); ArenaExtractionList.Shutdown();
Check(finalGroup.alpha == .8f && !finalGroup.enabled && RaidLifecycle.Starts == 0 && RaidLifecycle.Ends == 0,
    "Shutdown restores native group and removes lifecycle subscribers");
Check(ArenaExtractionList.BeforeShowTimer(replacement, true), "Hook surviving failed unpatch remains disabled");
ArenaExtractionList.Tick(); RaidLifecycle.Start(world);
Check(finalGroup.alpha == .8f, "Shutdown cannot hide native UI again");
HarmonyLib.Harmony.ThrowOnPatch = true;
ArenaExtractionList.Enable(); RaidLifecycle.Start(world); ArenaExtractionList.Tick();
Check(RaidLifecycle.Starts == 0 && RaidLifecycle.Ends == 0 && finalGroup.alpha == .8f
    && ArenaExtractionList.BeforeShowTimer(replacement, true), "Patch and rollback failure leaves service disabled and unsubscribed");
HarmonyLib.Harmony.ThrowOnPatch = HarmonyLib.Harmony.ThrowOnUnpatch = false;
ArenaExtractionList.Enable();
var badHeader = new ExtractionTimersPanel();
game.GameUi.TimerPanel = badHeader;
badHeader._mainTimerPanel.TimerText.transform.Parent = badHeader._mainDescription;
int warnings = ArenaMode.Plugin.ModLogger.Warnings.Count;
RaidLifecycle.Start(world);
for (int i = 0; i < 100; i++) ArenaExtractionList.Tick();
Check(badHeader._mainDescription.gameObject.activeSelf && Group(badHeader).alpha == 0,
    "A banner containing the clock is never deactivated and list masking survives");
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == warnings + 1, "Invalid header is quarantined with one diagnostic");
var healthyHeader = new ExtractionTimersPanel(); game.GameUi.TimerPanel = healthyHeader;
ArenaExtractionList.Tick();
Check(!healthyHeader._mainDescription.gameObject.activeSelf, "Fresh valid header recovers after failed old header");
healthyHeader._mainTimerPanel.TimerText.transform.Parent = healthyHeader._mainDescription;
ArenaExtractionList.Tick();
Check(healthyHeader._mainDescription.gameObject.activeSelf, "Changed clock hierarchy immediately restores the hidden header");
ArenaExtractionList.Shutdown();
Console.WriteLine($"NATIVE_ARENA_HUD_PRODUCTION_PASSED ({checks} assertions)");
