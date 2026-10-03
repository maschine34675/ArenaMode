using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

int checks = 0;
void Need(bool condition, string reason) { if (!condition) throw new Exception(reason); checks++; }
var config = new ConfigFile();
// Presentation can add decoration, never consume input or replace callbacks.
var themeRoot = new GameObject("theme fixture", typeof(RectTransform));
ArenaMode.Hud.ArenaUiTheme.Fill((RectTransform)themeRoot.transform, "surface", ArenaMode.Hud.ArenaUiTheme.Surface);
ArenaMode.Hud.ArenaUiTheme.Decorate((RectTransform)themeRoot.transform);
Need(themeRoot.GetComponentsInChildren<Image>(true).All(i => !i.raycastTarget),
    "all theme decoration leaves gameplay/menu clicks untouched");
var themeButton = new GameObject("theme button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
themeButton.targetGraphic = themeButton.GetComponent<Image>();
var themeLabel = new GameObject("theme label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
themeLabel.text = "A very long preserved kit name";
themeButton.targetGraphic = themeLabel; // Native dropdowns may tint the text itself.
int themeClicks = 0;
themeButton.onClick.AddListener(() => themeClicks++);
themeButton.interactable = false;
ArenaMode.Hud.ArenaUiTheme.StyleButton(themeButton, themeLabel);
Need(themeButton.targetGraphic is Image ownedSurface && !ownedSurface.raycastTarget
    && ownedSurface.transform.parent == themeButton.transform
    && themeLabel.RendererTint.Equals(Color.white),
    "native text tint is reset and moves to an owned passive button surface");
Need(!themeButton.interactable && themeLabel.text == "A very long preserved kit name",
    "theme does not enable locked choices or replace content");
themeButton.onClick.Invoke();
Need(themeClicks == 1, "theme preserves exact caller-owned action callback");
float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
float Luminance(Color c) => .2126f * Linear(c.r) + .7152f * Linear(c.g) + .0722f * Linear(c.b);
foreach (var foreground in new[] { ArenaMode.Hud.ArenaUiTheme.Text, ArenaMode.Hud.ArenaUiTheme.Muted })
    Need((Luminance(foreground) + .05f) / (Luminance(ArenaMode.Hud.ArenaUiTheme.Raised) + .05f) >= 4.5f,
        "primary and secondary typography remain legible on the raised surface");
Settings.Init(config);
ArenaMode.Pickups.ArenaPickupSettings.Init(config);
Need(!Settings.CombineDamageNumbers.Value, "burst damage remains opt-in");
Need(Settings.GetReplaySeconds("deathmatch") == 0f && Settings.GetReplaySeconds("gungame") == 0f
    && Settings.GetReplaySeconds("teamdeathmatch") == 0f && Settings.GetReplaySeconds("tactical") == 3f,
    "killcam keeps instant modes off and Tactical legacy default on");
Need(Settings.TeamDeathmatchKillLimit.Value == 50 && Settings.TeamDeathmatchTimeLimitSeconds.Value == 600,
    "production defaults are 50 team kills and ten minutes");
var killsRange = (AcceptableValueRange<int>)Settings.TeamDeathmatchKillLimit.Description.AcceptableValues;
var timeRange = (AcceptableValueRange<int>)Settings.TeamDeathmatchTimeLimitSeconds.Description.AcceptableValues;
Need(killsRange.MinValue == 0 && killsRange.MaxValue == 1000 && timeRange.MinValue == 0 && timeRange.MaxValue == 7200,
    "production F12 ranges include independent zero limits and custom intermediate values");
foreach (int invalid in new[] { -1, 1001, int.MaxValue, int.MinValue })
    Need(!Settings.TrySelectTeamDeathmatchKillLimit(invalid) && Settings.TeamDeathmatchKillLimit.Value == 50,
        "settings rejects out-of-range kills without mutating config");
foreach (int invalid in new[] { -1, 7201, int.MaxValue, int.MinValue })
    Need(!Settings.TrySelectTeamDeathmatchTimeLimit(invalid) && Settings.TeamDeathmatchTimeLimitSeconds.Value == 600,
        "settings rejects out-of-range time without mutating config");
foreach (bool armed in new[] { false, true })
{
    ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
    Need(!Settings.TrySelectTeamDeathmatchKillLimit(25) && !Settings.TrySelectTeamDeathmatchTimeLimit(300),
        "settings rejects active and armed raid writes");
}
ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
int reports = SettingsSync.Reports;
config.SaveOnConfigSet = false;
int saves = config.Saves;
Need(Settings.TrySelectTeamDeathmatchKillLimit(0) && Settings.TrySelectTeamDeathmatchTimeLimit(0)
    && config.Saves == saves + 2 && SettingsSync.Reports == reports,
    "menu saves independent unlimited limits without server settings report");
Settings.TeamDeathmatchKillLimit.RejectWrites = true;
Need(!Settings.TrySelectTeamDeathmatchKillLimit(25), "rejected config setter cannot claim accepted value");
Settings.TeamDeathmatchKillLimit.RejectWrites = false;
Settings.TeamDeathmatchKillLimit.ThrowWrites = true;
Need(!Settings.TrySelectTeamDeathmatchKillLimit(25), "throwing config setter is contained");
Settings.TeamDeathmatchKillLimit.ThrowWrites = false;
config.ThrowSave = true;
Need(Settings.TrySelectTeamDeathmatchKillLimit(25) && Settings.TeamDeathmatchKillLimit.Value == 25,
    "failed disk save preserves truthful accepted in-memory config");
config.ThrowSave = false;

int selectedKills = 50, selectedTime = 600, calls = 0;
bool locked = false, accept = true;
var state = new ArenaTdmMatchRulesState(() => selectedKills, () => selectedTime,
    x => { calls++; if (accept) selectedKills = x; return accept; },
    x => { calls++; if (accept) selectedTime = x; return accept; }, () => locked);
foreach (var mode in new[] { null, "endless", "rounds", "gungame", "tactical", "bogus" })
{
    state.Synchronize(mode, false);
    Need(!state.Visible && !state.TrySelectKills(25) && !state.TrySelectTime(300), "rules scoped to independent TDM");
}
state.Synchronize("teamdeathmatch", true);
Need(!state.Visible && !state.TrySelectKills(25), "PFT cannot expose independent TDM rules");
state.Synchronize("teamdeathmatch", false);
Need(state.Visible && state.Editable && calls == 0, "hidden controls never call persisted setters");
locked = true;
Need(!state.TrySelectKills(25) && !state.TrySelectTime(300) && calls == 0, "state rechecks live raid lock on click");
locked = false;
foreach (int invalid in new[] { -1, 24, 51, 1000, int.MaxValue })
    Need(!state.TrySelectKills(invalid), "menu admits only explicit kill presets");
foreach (int invalid in new[] { -1, 301, 7200, int.MaxValue })
    Need(!state.TrySelectTime(invalid), "menu admits only explicit time presets");
foreach (int value in new[] { 0, 25, 50, 100 })
    Need(state.TrySelectKills(value) && state.KillLimit == value, "all kill presets persist");
foreach (int value in new[] { 0, 300, 600, 900, 1200 })
    Need(state.TrySelectTime(value) && state.TimeLimitSeconds == value, "all time presets persist");
accept = false;
Need(!state.TrySelectKills(25) && selectedKills == 100, "refused click does not invent selected state");
accept = true;
selectedKills = 77; selectedTime = 75;
Need(state.KillLabel == "77" && state.TimeLabel == "1:15", "nonpreset F12 values display exactly");
Need(ArenaTdmMatchRulesState.FormatTime(7200) == "120 min" && ArenaTdmMatchRulesState.FormatTime(0) == "Unlimited"
    && ArenaTdmMatchRulesState.FormatKills(0) == "Unlimited", "zero and maximum labels are explicit");
Need(ArenaTdmMatchRulesState.FormatTime(-1) == "Unavailable" && ArenaTdmMatchRulesState.FormatKills(1001) == "Unavailable",
    "missing or invalid values are not presented as unlimited");
var lying = new ArenaTdmMatchRulesState(() => 50, () => 600, _ => true, _ => true, () => false);
lying.Synchronize("teamdeathmatch", false);
Need(!lying.TrySelectKills(25) && !lying.TrySelectTime(300), "success callback must match persisted value");

// The actual mode view creates, synchronizes, displays and tears down the child.
Settings.TeamDeathmatchKillLimit.Value = 50;
Settings.TeamDeathmatchTimeLimitSeconds.Value = 600;
var screen = new GameObject("native map screen", typeof(RectTransform));
((RectTransform)screen.transform).sizeDelta = new Vector2(1920f,1080f);
var view = new ArenaModeSelectionView();
void Show(string mode = "teamdeathmatch", bool team = false)
    => view.Show(screen.transform, null, mode, team, Settings.TrySelectGameMode);
Button[] Buttons() => view.Root.GetComponentsInChildren<Button>(true);
Button KillButton(int value) => Buttons().Single(x => x.name == "Arena TDM Kills " + value);
Button TimeButton(int value) => Buttons().Single(x => x.name == "Arena TDM Seconds " + value);
GameObject RuleRoot() => view.Root.transform.Children.Single(x => x.name == "Arena TDM Match Rules").gameObject;
TMP_Text Label(string name) => view.Root.GetComponentsInChildren<TMP_Text>(true).Single(x => x.name == name);
bool Marked(Button b) => b.transform.Children.Single(x => x.name == "Selected").gameObject.activeSelf;
Show();
Need(Buttons().Length == 30 && RuleRoot().activeInHierarchy, "four modes, nine TDM and seventeen Deathmatch rule buttons exist without a mutators opener");
Need(Marked(KillButton(50)) && Marked(TimeButton(600)), "persisted defaults own markers");
Need(!Label("RULES label").gameObject.activeSelf && !Label("EQUIPMENT value").gameObject.activeSelf,
    "TDM rules replace ordinary fact rows rather than overlapping them");
var panel = (RectTransform)view.Root.transform;
Need(panel.sizeDelta == new Vector2(360, 488) && panel.anchoredPosition == new Vector2(56, -168),
    "mode panel footprint does not grow toward area or native bottom buttons");
var ruleRect = (RectTransform)RuleRoot().transform;
Need(-ruleRect.anchoredPosition.y >= 303 && -ruleRect.anchoredPosition.y + ruleRect.sizeDelta.y <= 488,
    "rules remain below heading and inside original mode panel");
foreach (var button in Buttons().Where(x => x.name.StartsWith("Arena TDM")))
{
    var rect = (RectTransform)button.transform;
    Need(rect.anchoredPosition.x >= 0 && rect.anchoredPosition.x + rect.sizeDelta.x <= ruleRect.sizeDelta.x
        && -rect.anchoredPosition.y >= 0 && -rect.anchoredPosition.y + rect.sizeDelta.y <= ruleRect.sizeDelta.y,
        "each limit button stays inside existing mode panel");
    Need(button.onClick.Snapshot().Length == 1, "native cloned row has only one owned listener");
}
var existing = Buttons();
Settings.TeamDeathmatchKillLimit.Value = 77;
Settings.TeamDeathmatchTimeLimitSeconds.Value = 75;
view.Synchronize("teamdeathmatch", false);
Need(Label("TDM kill limit").text.EndsWith("77") && Label("TDM time limit").text.EndsWith("1:15"),
    "unchanged-mode refresh displays new F12 values");
Need(!Buttons().Where(x => x.name.StartsWith("Arena TDM")).Any(Marked) && existing.SequenceEqual(Buttons()),
    "custom values produce no guessed preset and preserve button identity");
KillButton(25).onClick.Invoke(); TimeButton(300).onClick.Invoke();
Need(Settings.TeamDeathmatchKillLimit.Value == 25 && Settings.TeamDeathmatchTimeLimitSeconds.Value == 300
    && Marked(KillButton(25)) && Marked(TimeButton(300)), "real controls save and refresh accepted presets");
ArenaSession.ArmedForNextRaid = true;
view.Synchronize("teamdeathmatch", false);
KillButton(50).onClick.Invoke();
Need(!KillButton(50).interactable && Settings.TeamDeathmatchKillLimit.Value == 25, "armed native view rejects direct event dispatch");
ArenaSession.ArmedForNextRaid = false;
view.Synchronize("gungame", false);
KillButton(50).onClick.Invoke();
Need(!RuleRoot().activeSelf && Label("RULES label").gameObject.activeSelf
    && Settings.TeamDeathmatchKillLimit.Value == 25, "other mode restores facts and rejects hidden rule events");
view.Synchronize("tactical", true);
Need(!RuleRoot().activeSelf && ((RectTransform)view.Root.transform).sizeDelta.y == 488
    && Label("EQUIPMENT value").text == "PMC entry gear", "PFT layout and entry equipment preserved");
view.Synchronize("deathmatch", false);
var dmRoot = view.Root.transform.Children.Single(x => x.name == "Arena Deathmatch Match Rules").gameObject;
Button Dm(int row, int value) => Buttons().Single(x => x.name == "Arena Deathmatch " + row + " " + value);
Need(dmRoot.activeSelf && !RuleRoot().activeSelf && !Label("RULES label").gameObject.activeSelf,
    "Deathmatch rules replace facts and TDM controls");
Need(((RectTransform)view.Root.transform).sizeDelta.y == 554, "Deathmatch panel contains all four rule rows");
Dm(1,20).onClick.Invoke(); Dm(0,3).onClick.Invoke(); Dm(3,1).onClick.Invoke();
Need(Settings.DeathmatchRounds.Value == 3 && Settings.DeathmatchKills.Value == 20
    && !Settings.DeathmatchFreeForAll.Value && Marked(Dm(0,3)) && Marked(Dm(3,1)), "actual Deathmatch controls persist rules and markers");
var frozen = Settings.FreezeDeathmatchRules();
Settings.DeathmatchKills.Value = 50;
Need(frozen.Kills == 20 && !frozen.FreeForAll, "frozen match cannot drift with later settings");
ArenaSession.ArmedForNextRaid = true;
view.Synchronize("deathmatch", false); Dm(1,100).onClick.Invoke();
Need(Settings.DeathmatchKills.Value == 50 && !Dm(1,100).interactable, "actual Deathmatch view blocks armed click");
ArenaSession.ArmedForNextRaid = false;
view.Synchronize("tactical", false);
Need(Label("RULES value").text == "1v5 / Solo" && Label("Team restriction").gameObject.activeSelf,
    "solo Tactical displays actual squad and PFT requirement");
var dmRetired = Dm(0,5).onClick.Snapshot();
view.Synchronize("teamdeathmatch", false);
foreach (var callback in dmRetired) callback();
Need(Settings.DeathmatchRounds.Value == 3, "hidden Deathmatch callbacks cannot change other mode");
var staleMode = Buttons().Single(x => x.name == "Arena Mode gungame").onClick.Snapshot();
Button MutatorButton(ArenaMutator id) => screen.GetComponentsInChildren<Button>(true).Single(x => x.name == "Arena Mutator " + id);
GameObject MutatorsRoot() => screen.transform.Children.Single(x => x.name == "Arena Mutators").gameObject;
Button KillcamButton() => MutatorButton(ArenaMutator.Killcam);
string MutatorStatus(ArenaMutator id) => MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Mutator status " + id).text;
string KillcamText() => "KILLCAM: " + MutatorStatus(ArenaMutator.Killcam);
Need(MutatorsRoot().activeInHierarchy, "mutators are visible immediately without an opener");
Need(MutatorsRoot().GetComponentsInChildren<Button>(true).Length == 12, "inline panel has twelve switches, no backdrop or close buttons");
var mutatorRect = (RectTransform)MutatorsRoot().transform;
Need(mutatorRect.anchorMax == Vector2.one && mutatorRect.anchoredPosition == new Vector2(-56,-508)
    && mutatorRect.sizeDelta == new Vector2(600,496), "inline panel always reserves the full thumbnail or Customs preview slot");
Need(Settings.DeathmatchColoredTracers.Value == false && Settings.TeamTracers.Value,
    "fresh Deathmatch tracer preference is off while other modes keep their default");
Settings.TeamTracers.Value = true;
view.Synchronize("deathmatch", false);
Need(MutatorStatus(ArenaMutator.Tracers) == "OFF", "Deathmatch ignores the old enabled team preference");
MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
Need(Settings.DeathmatchColoredTracers.Value && Settings.TeamTracers.Value, "Deathmatch can explicitly enable tracers");
view.Synchronize("teamdeathmatch", false);
MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
Need(!Settings.TeamTracers.Value && Settings.DeathmatchColoredTracers.Value, "team tracer toggle preserves Deathmatch preference");
view.Synchronize("deathmatch", false);
Need(MutatorStatus(ArenaMutator.Tracers) == "ON", "switching modes preserves explicitly enabled Deathmatch tracers");
foreach (bool armed in new[] { false, true })
{
    ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
    MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
    Need(Settings.DeathmatchColoredTracers.Value, "Deathmatch tracer writes respect active and armed locks");
}
ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
Settings.DeathmatchColoredTracers.Value = false; Settings.TeamTracers.Value = true;
foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical" })
{
    view.Synchronize(mode, mode == "tactical");
    Need(mutatorRect.anchoredPosition == new Vector2(-56,-508), "mode changes never move the mutator panel");
    bool wasEnabled = Settings.GetReplaySeconds(mode) > 0f;
    Need(KillcamButton().interactable && KillcamText() == (wasEnabled ? "KILLCAM: ON" : "KILLCAM: OFF"),
        "per-mode killcam label reflects saved state " + mode);
    KillcamButton().onClick.Invoke();
    Need((Settings.GetReplaySeconds(mode) > 0f) != wasEnabled, "native killcam button persists current mode " + mode);
    KillcamButton().onClick.Invoke();
    Need((Settings.GetReplaySeconds(mode) > 0f) == wasEnabled, "native killcam toggle restores only current mode " + mode);
}
view.Synchronize("gungame", false);
KillcamButton().onClick.Invoke();
ArenaReplaySelection.Capture(Settings.GetReplaySeconds("gungame"), true);
Settings.GunGameReplaySeconds.Value = 7f;
Need(ArenaReplaySelection.Seconds == 3f, "armed replay duration is independent of later F12 changes");
ArenaReplaySelection.Clear();
foreach (bool armed in new[] { true, false })
{
    ArenaSession.ArmedForNextRaid = armed; ArenaSession.IsActive = !armed;
    view.Synchronize("gungame", false);
    KillcamButton().onClick.Invoke();
    Need(!KillcamButton().interactable && Settings.GunGameReplaySeconds.Value == 7f
        && !Settings.TrySelectKillcam("gungame", false), "armed and active killcam writes are rejected");
}
ArenaSession.ArmedForNextRaid = ArenaSession.IsActive = false;
ArenaMode.Spectator.KillcamReplayCompat.Available = false;
view.Synchronize("gungame", false);
KillcamButton().onClick.Invoke();
Need(KillcamText() == "KILLCAM: N/A" && !KillcamButton().interactable && Settings.GunGameReplaySeconds.Value == 7f,
    "unavailable mod is explicit and cannot change preference");
ArenaMode.Spectator.KillcamReplayCompat.Available = true;
view.Synchronize("gungame", false);
Need(KillcamText() == "KILLCAM: ON" && Label("RESPAWN value").text == "After killcam",
    "available replay refreshes actual respawn description");
Need(Settings.GetReplaySeconds("deathmatch") == 0f && Settings.GetReplaySeconds("teamdeathmatch") == 0f
    && Settings.GetReplaySeconds("tactical") == 3f, "selecting one mode leaves the other replay choices untouched");
var retiredKillcam = KillcamButton();
var staleKillcamActions = retiredKillcam.onClick.Snapshot();
view.Synchronize("teamdeathmatch", false);
var retiredButton = KillButton(100);
var retired = retiredButton.onClick.Snapshot();
view.Close();
Need(retiredKillcam.onClick.Snapshot().Length == 0, "close removes killcam callback");
Need(retiredButton.onClick.Snapshot().Length == 0, "close removes child listeners");
Settings.GameMode.Value = "teamdeathmatch";
Show();
foreach (var callback in staleKillcamActions) callback();
Need(Settings.TeamDeathmatchReplaySeconds.Value == 0f, "retired killcam callback cannot mutate reopened panel");
foreach (var callback in retired) callback();
foreach (var callback in staleMode) callback();
Need(Settings.GameMode.Value == "teamdeathmatch", "retired mode row cannot modify reopened panel");
Need(Settings.TeamDeathmatchKillLimit.Value == 25, "retired row cannot modify reopened panel");
// Exercise actual config bindings through the visible controls, not a copied policy.
var preferencePairs = new[] {
    (ArenaMutator.DoubleJump, Settings.DoubleJumpEnabled),
    (ArenaMutator.AirControl, Settings.AirControlEnabled),
    (ArenaMutator.AirFire, Settings.AirHipFireEnabled),
    (ArenaMutator.AirVault, Settings.AirVaultEnabled),
    (ArenaMutator.Slide, Settings.SlideEnabled),
    (ArenaMutator.InfiniteStamina, Settings.InfiniteStamina),
    (ArenaMutator.NoFallDamage, Settings.NoFallDamage),
    (ArenaMutator.Backpack, Settings.UseItemsAnywhere),
    (ArenaMutator.Pickups, ArenaMode.Pickups.ArenaPickupSettings.Enabled),
    (ArenaMutator.Tracers, Settings.TeamTracers),
    (ArenaMutator.Lasers, Settings.TeamLasers) };
foreach (var (id, entry) in preferencePairs)
{
    bool before = entry.Value;
    int mutatorSaves = config.Saves;
    MutatorButton(id).onClick.Invoke();
    Need(entry.Value != before && config.Saves > mutatorSaves, "mutator persists original F12 entry: " + id);
    entry.Value = before; // Simulated F12 change while panel remains visible.
    view.Synchronize("teamdeathmatch", false);
    Need(MutatorStatus(id) == (before ? "ON" : "OFF"),
        "F12 changes refresh mutators without rebuilding: " + id);
}
config.SaveOnConfigSet = false;
int manualSaves = config.Saves;
MutatorButton(ArenaMutator.Backpack).onClick.Invoke();
Need(config.Saves == manualSaves + 1, "mutators explicitly save when automatic saving is disabled");
config.SaveOnConfigSet = true;
float pointBrightness = Settings.VisibleLaserBrightness.Value, beamBrightness = Settings.VisibleLaserBeamBrightness.Value;
MutatorButton(ArenaMutator.Lasers).onClick.Invoke();
Need(Settings.VisibleLaserBrightness.Value == pointBrightness && Settings.VisibleLaserBeamBrightness.Value == beamBrightness,
    "laser color toggle does not alter brightness");
foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch" })
{
    foreach (bool team in mode == "tactical" ? new[] { false, true } : new[] { false })
    {
        view.Synchronize(mode, team);
        bool pickupAllowed = mode == "deathmatch" || mode == "teamdeathmatch";
        bool laserAllowed = mode == "teamdeathmatch" || (mode == "tactical" && team);
        Need(MutatorButton(ArenaMutator.Pickups).interactable == pickupAllowed, "pickup availability matches runtime modes");
        Need(MutatorButton(ArenaMutator.Lasers).interactable == laserAllowed, "laser availability matches team modes");
        if (!pickupAllowed)
        {
            bool before = ArenaMode.Pickups.ArenaPickupSettings.Enabled.Value;
            MutatorButton(ArenaMutator.Pickups).onClick.Invoke();
            Need(ArenaMode.Pickups.ArenaPickupSettings.Enabled.Value == before, "unavailable pickups preserve preference on direct callback");
        }
        if (!laserAllowed)
        {
            bool before = Settings.TeamLasers.Value;
            MutatorButton(ArenaMutator.Lasers).onClick.Invoke();
            Need(Settings.TeamLasers.Value == before, "unavailable lasers preserve preference on direct callback");
        }
    }
}
view.Synchronize("teamdeathmatch", false);
Settings.MovementEnabled.Value = false;
view.Synchronize("teamdeathmatch", false);
foreach (var (id, entry) in preferencePairs.Take(7))
{
    bool before = entry.Value;
    MutatorButton(id).onClick.Invoke();
    Need(!MutatorButton(id).interactable && entry.Value == before,
        "movement master gates mutator without changing saved preference: " + id);
}
Settings.MovementEnabled.Value = true;
view.Synchronize("teamdeathmatch", false);
foreach (bool armed in new[] { false, true })
{
    ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
    // Recheck lock in the callback, BEFORE the regular refresh hides the panel.
    foreach (var (id, entry) in preferencePairs)
    {
        bool before = entry.Value;
        MutatorButton(id).onClick.Invoke();
        Need(entry.Value == before, "live lock prevents mutator writes even before menu refresh: " + id);
    }
    view.Synchronize("teamdeathmatch", false);
    Need(!MutatorsRoot().activeSelf, "armed/active refresh hides inline mutators");
    ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
    view.Synchronize("teamdeathmatch", false);
}
view.Root.SetActive(false);
bool hiddenBefore = Settings.DoubleJumpEnabled.Value;
MutatorButton(ArenaMutator.DoubleJump).onClick.Invoke();
Need(Settings.DoubleJumpEnabled.Value == hiddenBefore, "hidden owner rejects mutator callbacks");
view.Root.SetActive(true);
Settings.DoubleJumpEnabled.RejectWrites = true;
bool doubleBefore = Settings.DoubleJumpEnabled.Value;
MutatorButton(ArenaMutator.DoubleJump).onClick.Invoke();
Need(Settings.DoubleJumpEnabled.Value == doubleBefore
    && MutatorStatus(ArenaMutator.DoubleJump) == (doubleBefore ? "ON" : "OFF"),
    "rejected setting writes display actual state");
Settings.DoubleJumpEnabled.RejectWrites = false;
Settings.DoubleJumpEnabled.ThrowWrites = true;
MutatorButton(ArenaMutator.DoubleJump).onClick.Invoke();
Need(Settings.DoubleJumpEnabled.Value == doubleBefore, "setting exceptions cannot escape menu callback");
Settings.DoubleJumpEnabled.ThrowWrites = false;
var stalePopup = screen.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Arena Mutator")).ToArray();
var stalePopupActions = stalePopup.SelectMany(b => b.onClick.Snapshot()).ToArray();
view.Close();
Need(stalePopup.All(b => b.onClick.Snapshot().Length == 0), "close removes all inline mutator listeners");
Show();
var beforeStale = preferencePairs.Select(p => p.Item2.Value).ToArray();
foreach (var callback in stalePopupActions) callback();
Need(MutatorsRoot().activeSelf && beforeStale.SequenceEqual(preferencePairs.Select(p => p.Item2.Value)),
    "retired mutator controls cannot modify or close a reopened panel");
Settings.DoubleJumpEnabled.Value = true;
view.Synchronize("teamdeathmatch", false);
Image StateMarker() => MutatorsRoot().GetComponentsInChildren<Image>(true).Single(i => i.name == "Mutator marker DoubleJump");
Need(StateMarker().color.Equals(ArenaMode.Hud.ArenaUiTheme.Positive)
    && !ReferenceEquals(MutatorButton(ArenaMutator.DoubleJump).targetGraphic, StateMarker()),
    "active marker is green and independent of button hover tint");
MutatorButton(ArenaMutator.DoubleJump).onClick.Invoke();
Need(!StateMarker().color.Equals(ArenaMode.Hud.ArenaUiTheme.Positive) && MutatorStatus(ArenaMutator.DoubleJump) == "OFF",
    "off marker differs visibly from enabled marker");
foreach (var size in new[] { new Vector2(1920,1080), new Vector2(1600,900), new Vector2(1280,720) })
{
    ((RectTransform)screen.transform).sizeDelta = size;
    view.Synchronize("deathmatch", false);
    var current = (RectTransform)MutatorsRoot().transform;
    Need(-current.anchoredPosition.y + current.sizeDelta.y * current.localScale.y < size.y * .94f,
        "right column leaves bottom native navigation clear at supported canvas sizes");
    Need(size.x + current.anchoredPosition.x - current.sizeDelta.x * current.localScale.x > size.x * .65f,
        "right column stays beyond the central map and time strip");
    foreach (ArenaMutator id in Enum.GetValues(typeof(ArenaMutator)))
    {
        var buttonRect = (RectTransform)MutatorButton(id).transform;
        var detail = MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Mutator detail " + id).rectTransform;
        Need(-detail.anchoredPosition.y >= -buttonRect.anchoredPosition.y + buttonRect.sizeDelta.y
            && -detail.anchoredPosition.y + detail.sizeDelta.y <= 448f,
            "all twelve descriptions fit below their button and above the footer");
    }
}
view.Close();
Need(view.Root == null && screen.transform.Children.Count == 0, "close destroys all owned view roots");
foreach (string legacy in new[] { "endless", "rounds" })
{
    var oldConfig = new ConfigFile();
    oldConfig.Stored["Match/GameMode"] = legacy;
    oldConfig.Stored["Match/RoundKillTarget"] = 37;
    oldConfig.Stored["Match/RoundTimeLimitSeconds"] = 240;
    Settings.Init(oldConfig);
    Need(Settings.GameMode.Value == "deathmatch" && Settings.DeathmatchRounds.Value == 0, "legacy mode normalized without new finite limit");
    Need(Settings.DeathmatchKills.Value == (legacy == "rounds" ? 37 : 0)
        && Settings.DeathmatchSeconds.Value == (legacy == "rounds" ? 240 : 0), "legacy Rounds preserves limits; Endless remains continuous");
}
foreach (bool saved in new[] { false, true })
{
    var savedConfig = new ConfigFile();
    savedConfig.Stored["Deathmatch/ColoredTracers"] = saved;
    savedConfig.Stored["Effects/TeamTracers"] = !saved;
    Settings.Init(savedConfig);
    Need(Settings.TracerEntry("deathmatch").Value == saved
        && Settings.TracerEntry("endless").Value == saved && Settings.TracerEntry("rounds").Value == saved,
        "saved Deathmatch tracer choice survives configuration reload including legacy modes");
    foreach (string mode in new[] { "teamdeathmatch", "tactical", "gungame" })
        Need(Settings.TracerEntry(mode).Value == !saved, "other modes keep the existing saved tracer choice");
}
Console.WriteLine($"TDM_MATCH_MENU_CHECKS_PASSED ({checks} assertions)");
