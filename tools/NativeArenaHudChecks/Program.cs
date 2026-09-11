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
CanvasGroup[] LiveGroups(ExtractionTimersPanel panel) => panel._timersPanel.gameObject.Components
    .OfType<CanvasGroup>().Where(c => c != null && c.enabled).ToArray();

var game = new AbstractGame(); var world = new GameWorld();
Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
ArenaSession.Game = game; ArenaSession.World = world;
var panel = game.GameUi.TimerPanel;
var borrowed = panel._timersPanel.gameObject.AddComponent<CanvasGroup>();
borrowed.alpha = .6f; borrowed.ignoreParentGroups = true;
ArenaExtractionList.Enable(); ArenaExtractionList.Enable();
Check(RaidLifecycle.Starts == 1 && RaidLifecycle.Ends == 1 && HarmonyLib.Harmony.Patches == 1, "Enable is idempotent");
ArenaExtractionList.Tick(); RaidLifecycle.Start(world);
Check(LiveGroups(panel).Length == 1, "Ordinary startup adds no mask");
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && ArenaExtractionList.BeforeShowTimer(panel, false), "Ordinary show requests remain native");

// Vanilla has already started its ShowTimer(true) coroutine before confirmation.
// The lifecycle event must catch it without another ShowTimer invocation.
ArenaSession.IsActive = true; RaidLifecycle.Start(world);
var mask = LiveGroups(panel).Single(c => !ReferenceEquals(c, borrowed));
Check(mask.alpha == 0 && !mask.interactable && !mask.blocksRaycasts && !mask.ignoreParentGroups, "Confirmed startup hides list through an owned mask");
Check(borrowed.alpha == .6f && borrowed.ignoreParentGroups && borrowed.enabled, "Borrowed group remains unchanged");
Check(!ArenaExtractionList.BeforeShowTimer(panel, true), "Arena double-O exit list is suppressed");
Check(ArenaExtractionList.BeforeShowTimer(panel, false), "Single-O clock remains native");
Check(ArenaExtractionList.BeforeShowTimer(new ExtractionTimersPanel(), true), "Foreign UI instance remains native");
Check(ArenaExtractionList.BeforeShowTimer(null, true), "Null UI instance remains native");
for (int i = 0; i < 5; i++) ArenaExtractionList.Tick();
Check(LiveGroups(panel).Length == 2, "Repeated frames never accumulate masks");
mask.alpha = 1; ArenaExtractionList.Tick();
Check(mask.alpha == 0, "Owned mask remains hidden if another presentation update alters it");

Singleton<GameWorld>.Instance = new GameWorld(); // Simulate missed end with stale IsActive.
Check(ArenaExtractionList.BeforeShowTimer(panel, true), "Foreign world with stale latch stays native");
ArenaExtractionList.Tick();
Check(!mask.enabled && mask.Destroyed && LiveGroups(panel).Length == 1, "Foreign world releases old mask immediately");
Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = new AbstractGame();
ArenaExtractionList.Tick();
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && LiveGroups(panel).Length == 1, "Foreign game stays native");
Singleton<AbstractGame>.Instance = game; ArenaExtractionList.Tick();
mask = LiveGroups(panel).Single(c => !ReferenceEquals(c, borrowed));
var replacement = new ExtractionTimersPanel(); game.GameUi.TimerPanel = replacement; ArenaExtractionList.Tick();
Check(mask.Destroyed && LiveGroups(panel).Length == 1 && LiveGroups(replacement).Length == 1, "Replacement UI releases old ownership and masks only new list");
Check(ArenaExtractionList.BeforeShowTimer(panel, true) && !ArenaExtractionList.BeforeShowTimer(replacement, true), "Only replacement panel owns the prefix");
mask = LiveGroups(replacement).Single();
ArenaSession.IsActive = false; RaidLifecycle.End();
Check(mask.Destroyed && !mask.enabled && LiveGroups(replacement).Length == 0, "End restores native visibility before deferred Destroy");
Check(borrowed.alpha == .6f && borrowed.enabled && !borrowed.Destroyed, "End never destroys borrowed canvas state");

ArenaSession.IsActive = true; replacement._timersPanel = null; RaidLifecycle.Start(world); ArenaExtractionList.Tick();
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 0, "Late UI availability is a normal state");
replacement._timersPanel = new GameObject().AddComponent<RectTransform>(); ArenaExtractionList.Tick();
Check(LiveGroups(replacement).Length == 1, "Late native list is caught without HUD visibility dependency");
mask = LiveGroups(replacement).Single();
Singleton<GameWorld>.Instance = null; ArenaExtractionList.Tick();
Check(mask.Destroyed, "Missing world cleans previous mask");
Singleton<GameWorld>.Instance = world;
replacement._timersPanel.gameObject.ThrowOnAdd = true; RaidLifecycle.Start(world); ArenaExtractionList.Tick(); ArenaExtractionList.Tick();
Check(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "Cosmetic construction failure is bounded, not a frame-log loop");
Check(ArenaExtractionList.BeforeShowTimer(replacement, true), "Mask failure leaves native show path open");
replacement._timersPanel.gameObject.ThrowOnAdd = false; ArenaExtractionList.Tick();
Check(LiveGroups(replacement).Length == 1, "Cosmetic failure can recover when native list is ready");
mask = LiveGroups(replacement).Single();
HarmonyLib.Harmony.ThrowOnUnpatch = true;
ArenaExtractionList.Shutdown(); ArenaExtractionList.Shutdown();
Check(mask.Destroyed && RaidLifecycle.Starts == 0 && RaidLifecycle.Ends == 0, "Shutdown removes mask and lifecycle subscribers");
Check(ArenaExtractionList.BeforeShowTimer(replacement, true), "Hook surviving failed unpatch remains disabled");
ArenaExtractionList.Tick(); RaidLifecycle.Start(world);
Check(LiveGroups(replacement).Length == 0, "Shutdown cannot create another mask");
HarmonyLib.Harmony.ThrowOnPatch = true;
ArenaExtractionList.Enable(); RaidLifecycle.Start(world); ArenaExtractionList.Tick();
Check(RaidLifecycle.Starts == 0 && RaidLifecycle.Ends == 0 && LiveGroups(replacement).Length == 0
    && ArenaExtractionList.BeforeShowTimer(replacement, true), "Patch and rollback failure leaves service disabled and unsubscribed");
HarmonyLib.Harmony.ThrowOnPatch = HarmonyLib.Harmony.ThrowOnUnpatch = false;
Console.WriteLine($"NATIVE_ARENA_HUD_PRODUCTION_PASSED ({checks} assertions)");
