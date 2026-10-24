using ArenaMode.Session;
using EFT.UI;
using EFT.UI.Matchmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

int checks = 0, writes = 0, closes = 0, starts = 0, reads = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
var screen = new GameObject("Character screen", typeof(RectTransform), typeof(MatchMakerSideSelectionScreen))
    .GetComponent<MatchMakerSideSelectionScreen>();
var typography = new GameObject("Typography", typeof(RectTransform), typeof(TextMeshProUGUI));
screen._sideDescriptionText = typography.GetComponent<TextMeshProUGUI>();
screen._randomButton = new GameObject("Arena choice", typeof(RectTransform), typeof(Component)).GetComponent<Component>();
screen._randomButton.transform.SetParent(screen.transform, false);
GameObject Root() => screen.transform.Children.Single(x => x.name == "ArenaUnavailableNotice").gameObject;
Button Button(string name) => Root().GetComponentsInChildren<Button>(true).Single(b => b.name == name);
void Tick() => Root().GetComponent<ArenaAvailabilityNoticeAnchor>().Tick();

ArenaAvailabilityNotice.Show(screen, "Blocked", new ArenaNoticeAction("START", () => starts++));
Need(!Button("ArenaNoticePreference").gameObject.activeSelf, "Ordinary restart notice added a preference");
Button("ArenaNoticeAction").onClick.Invoke();
Need(starts == 0, "First restart click started a session");
Button("ArenaNoticeAction").onClick.Invoke();
Need(starts == 1, "Normal two-click restart was changed");

bool available = true, pending = false, enabled = false;
var option = new ArenaNoticeOption
{
    Label = () => "PROFILE REWARDS: " + (enabled ? "ON" : "OFF"),
    Detail = () => "Literal <status>\nActivation requires a normal-server restart.",
    CanClick = () => available && !pending,
    CanProceed = () => !pending,
    Refresh = () => reads++,
    Click = () => { writes++; pending = true; },
    Close = () => closes++
};
ArenaAvailabilityNotice.Show(screen, "Blocked", new ArenaNoticeAction("START", () => starts++, option: option));
var toggle = Button("ArenaNoticePreference");
Need(toggle.gameObject.activeSelf && toggle.interactable, "Blocked access did not expose an explicit preference");
Need(toggle.GetComponentInChildren<TextMeshProUGUI>().text == "PROFILE REWARDS: OFF", "Confirmed preference was not shown");
Need(toggle.GetComponent<HoverTooltipArea>().RawText && toggle.GetComponent<HoverTooltipArea>().Message == option.Detail(),
    "Preference status was missing or interpreted as markup");
Need(writes == 0 && reads == 1, "Showing the fallback changed consent");
Tick();
Need(writes == 0 && reads == 2 && toggle.interactable, "Passive refresh changed or disabled consent");
available = false;
Tick();
toggle.onClick.Invoke();
Need(!toggle.interactable && writes == 0, "A server-refused preference remained writable");
available = true;
Tick();
Button("ArenaNoticeAction").onClick.Invoke(); // arm restart
toggle.onClick.Invoke();
Need(writes == 1 && pending && !toggle.interactable, "Explicit click was not serialized");
Need(!Button("ArenaNoticeAction").interactable, "Restart could overtake a pending preference write");
Button("ArenaNoticeAction").onClick.Invoke();
Button("ArenaNoticeAction").onClick.Invoke();
Need(starts == 1, "Retained restart callback ignored pending preference");
toggle.onClick.Invoke();
Need(writes == 1, "Pending preference accepted a duplicate click");
pending = false; enabled = true;
Tick();
Need(toggle.interactable && toggle.GetComponentInChildren<TextMeshProUGUI>().text == "PROFILE REWARDS: ON", "Confirmed state did not update");
Button("ArenaNoticeAction").onClick.Invoke();
Need(starts == 1, "Changing preference retained an armed restart");
Button("ArenaNoticeAction").onClick.Invoke();
Need(starts == 2, "Restart failed after confirmed preference");

var staleOption = toggle.onClick.Snapshot();
var staleStart = Button("ArenaNoticeAction").onClick.Snapshot();
var staleTick = Root().GetComponent<ArenaAvailabilityNoticeAnchor>().Tick;
int readsBeforeClose = reads;
ArenaAvailabilityNotice.Hide(screen);
Need(closes == 1 && screen.transform.Children.All(x => x.name != "ArenaUnavailableNotice"), "Closing did not release preference ownership");
foreach (var action in staleOption) action();
foreach (var action in staleStart) action();
staleTick();
Need(writes == 1 && starts == 2 && reads == readsBeforeClose, "Retired view acted after closing");
ArenaAvailabilityNotice.Show(screen, "Blocked again", new ArenaNoticeAction("START", () => starts++, option: option));
foreach (var action in staleOption) action();
Need(writes == 1, "Old preference callback acted on replacement view");
ArenaAvailabilityNotice.ShowInfo(screen, "Helper is starting");
Need(closes == 2 && !Button("ArenaNoticePreference").gameObject.activeSelf, "Waiting notice retained a preference");
ArenaAvailabilityNotice.Hide(screen);
Need(closes == 2, "Removed preference closed twice");
Console.WriteLine($"REWARD_NOTICE_CHECKS_PASSED ({checks} assertions)");
