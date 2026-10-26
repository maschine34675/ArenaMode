using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using Comfort.Common;
using EFT;
using TMPro;
using UnityEngine;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception("FAIL: " + reason); }
void Near(float value, float expected, string reason) => Need(Math.Abs(value - expected) < .001f, reason);
var canvas = new GameObject("HUD canvas");
var objective = new DominationPanel();
var powerups = new ArenaPowerupPanel();
objective.Tick(null); powerups.Tick();
objective.Create(canvas.transform); powerups.Create(canvas.transform);
var objectiveRoot = GameObject.All.Single(x => x.name == "ArenaDominationPanel");
var powerupRoot = GameObject.All.Single(x => x.name == "ArenaPowerupPanel");
var objectiveRect = objectiveRoot.transform;
var powerupRect = powerupRoot.transform;
var objectiveText = GameObject.All.Single(x => x.name == "ControlPoints").GetComponent<TextMeshProUGUI>();
var powerupText = GameObject.All.Single(x => x.name == "Powerups").GetComponent<TextMeshProUGUI>();
Singleton<GameWorld>.Instance = new();
ArenaPowerups.State = new() { DamageSeconds = 10 };
var mode = new TeamDeathmatchMode { ObjectiveHudVisible = true, IsSearchActive = true };
var match = new ArenaMatch { Mode = mode };
ArenaMatchService.Current = match;
Need(!objectiveRoot.activeSelf && !powerupRoot.activeSelf, "construction does not publish either panel");
Need(objectiveRect.pivot.y == 1 && powerupRect.pivot.y == 1,
    "both panels grow downward from their shared top anchor");
Near(objectiveRect.sizeDelta.y, ArenaHudStackLayout.ObjectiveHeight,
    "objective reserves the same height that its actual view draws");

foreach (float x in new[] { -320f, 0f, 147.5f })
foreach (float y in new[] { -40f, -100f, -275.25f })
foreach (int count in new[] { 0, 1, 2, 4 })
foreach (bool notice in new[] { false, true })
{
    ScorePanel.Position = new(x, y);
    Time.unscaledTime += 1;
    ArenaPowerups.State = new()
    {
        DamageSeconds = count >= 1 ? 10 : 0,
        ShieldSeconds = count >= 2 ? 9 : 0, ShieldPoints = count >= 2 ? 220 : 0,
        JumpSeconds = count >= 3 ? 8 : 0, AmmoSeconds = count >= 4 ? 7 : 0
    };
    ArenaPickupRuntime.NoticeUntil = notice ? Time.unscaledTime + 1 : 0;
    // Deliberately tick powerups first: stacking must not depend on a stale
    // activeSelf left by the previous objective render.
    powerups.Tick(); objective.Tick(match);
    Need(objectiveRoot.activeSelf, "live objective stays visible");
    Need(powerupRoot.activeSelf == (count > 0 || notice), "effects and notice retain their existing visibility");
    Near(objectiveRect.anchoredPosition.x, x, "objective tracks score X offset");
    Near(powerupRect.anchoredPosition.x, x, "powerups track score X offset");
    Near(objectiveRect.anchoredPosition.y, y - ScorePanel.PanelHeight - 20,
        "objective retains original score gap and follows Y offset");
    Near(objectiveRect.anchoredPosition.y - objectiveRect.sizeDelta.y - powerupRect.anchoredPosition.y, 16,
        "powerups are below actual objective bottom with a visible gap");
    Near(powerupRect.sizeDelta.y, count > 2 ? 78 : 52, "multi-effect height is retained");
    Need(objectiveText.text == mode.ObjectiveStatusLine, "objective status text remains intact");
    Need(!notice || powerupText.text.Contains(ArenaPickupRuntime.LastNotice), "pickup notice remains readable content");
}

// Visibility changes and offsets must be honored even between text updates.
Time.unscaledTime += 1;
ArenaPowerups.State = new() { ShieldSeconds = 10, ShieldPoints = 220 };
ArenaPickupRuntime.NoticeUntil = 0;
powerups.Tick(); objective.Tick(match);
string previousText = powerupText.text;
ScorePanel.Position = new(32, -187);
Time.unscaledTime += .01f;
powerups.Tick(); objective.Tick(match);
Near(powerupRect.anchoredPosition.y, objectiveRect.anchoredPosition.y - objectiveRect.sizeDelta.y - 16,
    "offset refresh precedes throttled powerup text");
Near(powerupRect.anchoredPosition.x, 32, "throttled panel still follows X offset");
Need(powerupText.text == previousText, "layout update does not resample or replace effect text");

foreach (bool contextVisible in new[] { false, true })
foreach (bool live in new[] { false, true })
{
    mode.ObjectiveHudVisible = contextVisible; mode.IsSearchActive = live;
    powerups.Tick(); objective.Tick(match);
    bool shown = contextVisible && live;
    Need(objectiveRoot.activeSelf == shown, "objective visibility requires current context and live phase");
    float expected = shown ? objectiveRect.anchoredPosition.y - objectiveRect.sizeDelta.y - 16
        : ScorePanel.Position.y - ScorePanel.PanelHeight - 16;
    Near(powerupRect.anchoredPosition.y, expected, "hidden objective releases reserved row immediately");
}

// Every other mode and a detached/foreign match keep the old powerup anchor.
foreach (object ordinary in new object[] { new object(), new TeamDeathmatchMode { IsSearchActive = true }, null })
{
    var otherMatch = new ArenaMatch { Mode = ordinary };
    ArenaMatchService.Current = otherMatch;
    powerups.Tick(); objective.Tick(otherMatch);
    Need(!objectiveRoot.activeSelf, "ordinary mode has no objective row");
    Near(powerupRect.anchoredPosition.y, ScorePanel.Position.y - ScorePanel.PanelHeight - 16,
        "ordinary mode preserves original powerup placement");
}
ArenaMatchService.Current = new ArenaMatch { Mode = mode };
mode.ObjectiveHudVisible = mode.IsSearchActive = true;
objective.Tick(match);
Need(!objectiveRoot.activeSelf, "foreign match cannot publish another match objective row");
ArenaMatchService.Current = null;
powerups.Tick(); objective.Tick(null);
Need(!objectiveRoot.activeSelf, "detached match hides objective row");
Near(powerupRect.anchoredPosition.y, ScorePanel.Position.y - ScorePanel.PanelHeight - 16,
    "detached match retains legacy powerup anchor without a stale gap");

ArenaMatchService.Current = match;
canvas.SetActive(false);
ScorePanel.Position = new(-75, -90);
powerups.Tick(); objective.Tick(match);
Need(!objectiveRoot.activeInHierarchy && !powerupRoot.activeInHierarchy, "hidden HUD hides both stacked rows");
canvas.SetActive(true);
powerups.Tick(); objective.Tick(match);
Need(objectiveRoot.activeInHierarchy && powerupRoot.activeInHierarchy, "restoring HUD restores both rows");
Near(powerupRect.anchoredPosition.y, objectiveRect.anchoredPosition.y - objectiveRect.sizeDelta.y - 16,
    "restored HUD uses current stack without overlap");
Near(powerupRect.anchoredPosition.x, -75, "restored HUD uses current horizontal offset");

Time.unscaledTime += 1; ArenaPickupRuntime.Live = false;
powerups.Tick(); objective.Tick(match);
Need(!powerupRoot.activeSelf && objectiveRoot.activeSelf, "dead actor hides effects without hiding team objectives");
Time.unscaledTime += 1; ArenaPickupRuntime.Live = true;
powerups.Tick(); objective.Tick(match);
Need(powerupRoot.activeSelf && objectiveRoot.activeSelf, "replacement actor restores effect panel in objective stack");
Console.WriteLine($"DOMINATION_HUD_CHECKS_PASSED ({checks} assertions)");
