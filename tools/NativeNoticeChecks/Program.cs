using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.UI;
using Redeploy.Core.Engine;
using UnityEngine;

var cases = new Action[] { HomeOwnership, RejectedRaid, LateAndReplacedLabel, MissedEnd, DestroyedWorld, ShutdownWhileMasked };
foreach (var run in cases)
{
    ArenaSessionMode.GateActive = false;
    ArenaSession.IsActive = false;
    Singleton<GameWorld>.Instance = null;
    Singleton<AbstractGame>.Instance = null;
    PreloaderUI.Instance = null;
    ArenaNativeNotices.Enable();
    try { run(); }
    finally { ArenaNativeNotices.Shutdown(); }
    Check(Canvas.Subscribers == 0, "render listener survived shutdown");
}
Console.WriteLine($"NATIVE_NOTICE_CHECKS_PASSED {cases.Length} cases");

static void Check(bool pass, string reason)
{
    if (!pass) throw new InvalidOperationException(reason);
}

static void HomeOwnership()
{
    var menu = new MenuScreen();
    var target = menu._alphaWarningGameObject;
    var foreign = target.AddComponent<CanvasGroup>();
    foreign.alpha = .4f;
    foreign.blocksRaycasts = false;
    target.SetActive(false);
    var first = new MenuScreen.MainMenuScreenController();
    ArenaNativeNotices.MenuShown(menu, first);
    Check(target.Groups.Count == 1, "normal home added a mask");
    ArenaSessionMode.GateActive = true;
    ArenaNativeNotices.MenuShown(menu, first);
    ArenaNativeNotices.MenuShown(menu, first);
    Check(target.Groups.Count == 2 && !target.activeSelf, "repeated show added two masks or reactivated another mod's banner");
    var owned = target.Groups[1];
    Check(owned.enabled && owned.alpha == 0f && !owned.interactable && !owned.blocksRaycasts, "private banner mask");
    foreign.enabled = false; // another mod changes its own decision while Arena owns a mask
    var second = new MenuScreen.MainMenuScreenController();
    ArenaNativeNotices.MenuShown(menu, second);
    first.Close();
    Check(owned.Destroyed && !target.Groups[2].Destroyed && target.Groups[2].alpha == 0, "old controller closed its successor's mask");
    second.Close();
    second.Close();
    Check(target.Groups[2].Destroyed, "close did not release the current owner's mask");
    ArenaNativeNotices.MenuShown(menu, first);
    ArenaNativeNotices.MenuShown(menu, new MenuScreen.MainMenuReconnectScreenController());
    first.Close();
    Check(target.Groups.Count == 4 && target.Groups[3].Destroyed, "reconnect kept or created a home mask");
    Check(!target.activeSelf && !foreign.Destroyed && !foreign.enabled && foreign.alpha == .4f &&
        !foreign.blocksRaycasts && foreign.interactable, "other mod's banner visibility or group was overwritten");
}

static GameWorld CreateRaid()
{
    ArenaSession.ConfirmedGame = Singleton<AbstractGame>.Instance = new AbstractGame();
    ArenaSession.ConfirmedWorld = Singleton<GameWorld>.Instance = new GameWorld();
    ArenaSession.IsActive = true;
    return ArenaSession.ConfirmedWorld;
}

static void RejectedRaid()
{
    var world = CreateRaid();
    var label = new LocalizedText();
    PreloaderUI.Instance = new PreloaderUI { _alphaVersionLabel = label };
    ArenaSession.IsActive = false;
    ArenaSessionMode.GateActive = true; // process mode alone is insufficient
    RaidLifecycle.Start(world);
    Canvas.Render();
    Check(label.gameObject.Groups.Count == 0 && Canvas.Subscribers == 0, "unconfirmed raid acquired a mask");
    ArenaSession.IsActive = true;
    Singleton<AbstractGame>.Instance = new AbstractGame();
    RaidLifecycle.Start(world);
    Check(label.gameObject.Groups.Count == 0 && Canvas.Subscribers == 0, "different game acquired a mask");
}

static void LateAndReplacedLabel()
{
    RaidLifecycle.Start(CreateRaid()); // a confirmed direct Arena raid in an ordinary SPT process
    Canvas.Render();
    PreloaderUI.Instance = new PreloaderUI();
    Canvas.Render();
    var first = new LocalizedText { LocalizationKey = "SPT | LOCAL" };
    var foreign = first.gameObject.AddComponent<CanvasGroup>();
    foreign.alpha = .25f;
    first.gameObject.SetActive(false);
    PreloaderUI.Instance._alphaVersionLabel = first;
    Canvas.Render();
    Canvas.Render();
    Check(first.gameObject.Groups.Count == 2 && first.gameObject.Groups[1].alpha == 0f &&
        !first.gameObject.activeSelf && first.LocalizationKey == "SPT | LOCAL", "late label or repeated render");
    first.LocalizationKey = "SPT | updated";
    var next = PreloaderUI.Instance._alphaVersionLabel = new LocalizedText();
    Canvas.Render();
    Check(first.gameObject.Groups[1].Destroyed && !foreign.Destroyed && foreign.alpha == .25f &&
        first.LocalizationKey == "SPT | updated" && !first.gameObject.activeSelf, "replaced label restoration");
    Check(next.gameObject.Groups.Count == 1 && next.gameObject.Groups[0].alpha == 0f, "replacement label missing mask");
    RaidLifecycle.End();
    RaidLifecycle.End();
    Check(next.gameObject.Groups[0].Destroyed && Canvas.Subscribers == 0, "end did not release replacement");
}

static void MissedEnd() => LoseOwner(destroy: false);
static void DestroyedWorld() => LoseOwner(destroy: true);
static void LoseOwner(bool destroy)
{
    var world = CreateRaid();
    var label = new LocalizedText();
    PreloaderUI.Instance = new PreloaderUI { _alphaVersionLabel = label };
    RaidLifecycle.Start(world);
    var mask = label.gameObject.Groups.Single();
    if (destroy) UnityEngine.Object.Destroy(world); // destroyed Unity object compares equal to null
    else Singleton<GameWorld>.Instance = new GameWorld();
    Canvas.Render(); // Arena's IsActive can still be true when native GameEnd was missed
    Check(mask.Destroyed && !mask.enabled && mask.alpha == 1f && Canvas.Subscribers == 0, "stale world retained mask or render callback");
}

static void ShutdownWhileMasked()
{
    var world = CreateRaid();
    var label = new LocalizedText();
    PreloaderUI.Instance = new PreloaderUI { _alphaVersionLabel = label };
    RaidLifecycle.Start(world);
    ArenaNativeNotices.Shutdown();
    Canvas.Render();
    RaidLifecycle.Start(world);
    Check(label.gameObject.Groups.Single().Destroyed && Canvas.Subscribers == 0, "shutdown allowed a later raid to reacquire");
}
