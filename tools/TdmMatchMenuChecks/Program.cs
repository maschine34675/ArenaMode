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
ArenaMode.Combat.Fire.ArenaFireSettings.Bind(config);
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
Need(!Settings.ZombieCrowdEnabled.Value, "new configurations opt out of the harder crowd behavior");
Need(Settings.ZombieMaxAlive.Value == 32 && Settings.ZombieWaveStartSize.Value == 50
    && Settings.ZombieWaveGrowth.Value == 20 && Settings.ZombieOwnGear.Value,
    "fresh Zombie preferences use tested 32/50/20 own-gear defaults");
var savedZombieConfig = new ConfigFile();
savedZombieConfig.Stored["Zombie/MaxAlive"] = 24;
savedZombieConfig.Stored["Zombie/WaveStartSize"] = 35;
savedZombieConfig.Stored["Zombie/WaveGrowth"] = 7;
savedZombieConfig.Stored["Zombie/OwnGear"] = false;
savedZombieConfig.Stored["Zombie/CrowdEnabled"] = true;
Settings.Init(savedZombieConfig);
Need(Settings.ZombieCrowdEnabled.Value, "saved explicit crowd choice survives default change");
Need(Settings.ZombieMaxAlive.Value == 24 && Settings.ZombieWaveStartSize.Value == 35
    && Settings.ZombieWaveGrowth.Value == 7 && !Settings.ZombieOwnGear.Value,
    "new Zombie defaults preserve every existing saved preference");
Settings.Init(config);
ArenaMode.Pickups.ArenaPickupSettings.Init(config);
Need(Settings.AutoQuickSlots.Value && Settings.InstantSearch.Value && Settings.CombatHudEnabled.Value
    && Settings.HudEnabled.Value, "new mutators preserve enabled defaults and the separate HUD master");
Need(Settings.TeamDeathmatchFriendlyFire.Value && Settings.TacticalFriendlyFire.Value
    && !Settings.ZombieFriendlyFire.Value, "friendly fire defaults off only for PFT Zombies");
foreach (string soloMode in new[] { "deathmatch", "gungame", "tactical", "zombie", "unknown" })
    Need(Settings.FriendlyFireEntry(soloMode, false) == null
        && Settings.GetFriendlyFireEnabled(soloMode, false),
        "solo/unknown modes have no friendly-fire immunity preference: " + soloMode);
Need(ReferenceEquals(Settings.FriendlyFireEntry("teamdeathmatch", false), Settings.TeamDeathmatchFriendlyFire)
    && ReferenceEquals(Settings.FriendlyFireEntry("tactical", true), Settings.TacticalFriendlyFire)
    && ReferenceEquals(Settings.FriendlyFireEntry("zombie", true), Settings.ZombieFriendlyFire),
    "friendly-fire mode routing keeps three independent team preferences");
Need(!Settings.CombineDamageNumbers.Value, "burst damage remains opt-in");
Need(Settings.GetReplaySeconds("deathmatch") == 0f && Settings.GetReplaySeconds("gungame") == 0f
    && Settings.GetReplaySeconds("teamdeathmatch") == 0f && Settings.GetReplaySeconds("tactical") == 3f,
    "killcam keeps instant modes off and Tactical legacy default on");
// Item 2 of the 2026-10-06 follow-up ("announcer mutator"): fresh per-mode
// defaults (Zombie off, everyone else on) and the dispatch that picks them.
Need(Settings.DeathmatchAnnouncerEnabled.Value && Settings.GunGameAnnouncerEnabled.Value
    && Settings.TacticalAnnouncerEnabled.Value && Settings.TeamDeathmatchAnnouncerEnabled.Value
    && !Settings.ZombieAnnouncerEnabled.Value,
    "announcer defaults on everywhere except Zombie");
Need(ReferenceEquals(Settings.AnnouncerEntry("deathmatch"), Settings.DeathmatchAnnouncerEnabled)
    && ReferenceEquals(Settings.AnnouncerEntry("gungame"), Settings.GunGameAnnouncerEnabled)
    && ReferenceEquals(Settings.AnnouncerEntry("tactical"), Settings.TacticalAnnouncerEnabled)
    && ReferenceEquals(Settings.AnnouncerEntry("teamdeathmatch"), Settings.TeamDeathmatchAnnouncerEnabled)
    && ReferenceEquals(Settings.AnnouncerEntry("zombie"), Settings.ZombieAnnouncerEnabled)
    && ReferenceEquals(Settings.AnnouncerEntry("bogus"), Settings.DeathmatchAnnouncerEnabled),
    "announcer per-mode dispatch picks the exact bound entry, unmapped mode normalizes to Deathmatch");
Need(Settings.IsAnnouncerEnabledFor("deathmatch") && !Settings.IsAnnouncerEnabledFor("zombie"),
    "the combined per-mode gate matches the per-mode table while the master switch is on");
Settings.AnnouncerEnabled.Value = false;
Need(!Settings.IsAnnouncerEnabledFor("deathmatch") && !Settings.IsAnnouncerEnabledFor("zombie"),
    "the master switch still gates a mode whose own per-mode default is on");
Settings.AnnouncerEnabled.Value = true;
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
Need(ArenaTdmMatchRulesState.FormatTime(7200) == "120 min" && ArenaTdmMatchRulesState.FormatTime(0) == "Off"
    && ArenaTdmMatchRulesState.FormatKills(0) == "Off", "zero limits use the same Off label as their buttons in every mode");
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
// 5 modes (Deathmatch/Gun Game/Tactical/Team DM/Zombie) + 9 TDM + 17 Deathmatch
// + 2 equipment-choice buttons; the equipment-choice row (item 1 of the
// 2026-10-06 follow-up) is built unconditionally in Create() just like the
// TDM/Deathmatch rules rows, only its own visibility differs by mode.
Need(Buttons().Length == 33 && RuleRoot().activeInHierarchy, "five modes, nine TDM, seventeen Deathmatch and two equipment-choice buttons exist without a mutators opener");
Need(Marked(KillButton(50)) && Marked(TimeButton(600)), "persisted defaults own markers");
Need(Label("OBJECTIVE label").gameObject.activeSelf && Label("EQUIPMENT value").gameObject.activeSelf,
    "TDM retains all common facts above its limit controls");
var panel = (RectTransform)view.Root.transform;
// 530/-168: the non-Deathmatch panel height and top offset, both +42 over
// their pre-Zombie values for the fifth mode row (see ArenaModeCatalog's
// ModePanelHeight/ModePanelTop and ArenaModeSelectionView.Create's own
// "every top offset below is +42" comment).
Need(panel.sizeDelta == new Vector2(360, 530) && panel.anchoredPosition == new Vector2(56, -168),
    "mode panel footprint does not grow toward area or native bottom buttons");
var ruleRect = (RectTransform)RuleRoot().transform;
Need(-ruleRect.anchoredPosition.y >= 432 && -ruleRect.anchoredPosition.y + ruleRect.sizeDelta.y <= 530,
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
Need(!RuleRoot().activeSelf && Label("OBJECTIVE label").gameObject.activeSelf
    && Settings.TeamDeathmatchKillLimit.Value == 25, "other mode restores facts and rejects hidden rule events");
view.Synchronize("tactical", true);
Need(!RuleRoot().activeSelf && ((RectTransform)view.Root.transform).sizeDelta.y == 530
    && Label("EQUIPMENT value").text == "PMC entry gear", "PFT layout and entry equipment preserved");
view.Synchronize("deathmatch", false);
var dmRoot = view.Root.transform.Children.Single(x => x.name == "Arena Deathmatch Match Rules").gameObject;
Button Dm(int row, int value) => Buttons().Single(x => x.name == "Arena Deathmatch " + row + " " + value);
Need(dmRoot.activeSelf && !RuleRoot().activeSelf && Label("OBJECTIVE label").gameObject.activeSelf,
    "Deathmatch retains common facts above its own controls");
Need(((RectTransform)view.Root.transform).sizeDelta.y == 596, "Deathmatch panel contains all four rule rows");
Dm(1,20).onClick.Invoke(); Dm(0,3).onClick.Invoke(); Dm(3,1).onClick.Invoke();
Need(Settings.DeathmatchRounds.Value == 3 && Settings.DeathmatchKills.Value == 20
    && !Settings.DeathmatchFreeForAll.Value && Marked(Dm(0,3)) && Marked(Dm(3,1)), "actual Deathmatch controls persist rules and markers");
Need(Label("Mode description").text.StartsWith("All opponents hunt you.", StringComparison.Ordinal),
    "opponent choice updates the visible Deathmatch description immediately");
Dm(3,0).onClick.Invoke();
Need(Label("Mode description").text.StartsWith("Everyone fights everyone.", StringComparison.Ordinal),
    "FFA choice restores the visible Deathmatch description immediately");
Dm(3,1).onClick.Invoke();
var frozen = Settings.FreezeDeathmatchRules();
Settings.DeathmatchKills.Value = 50;
Need(frozen.Kills == 20 && !frozen.FreeForAll, "frozen match cannot drift with later settings");
ArenaSession.ArmedForNextRaid = true;
view.Synchronize("deathmatch", false); Dm(1,100).onClick.Invoke();
Need(Settings.DeathmatchKills.Value == 50 && !Dm(1,100).interactable, "actual Deathmatch view blocks armed click");
ArenaSession.ArmedForNextRaid = false;
view.Synchronize("tactical", false);
// 2026-10-08 menu polish: RULES is the objective for every mode ("Round
// elimination"), no longer the roster. The roster stays visible in the row
// label and the PFT requirement in the 5v5 note.
Need(Label("OBJECTIVE value").text == "Round elimination" && Label("Mode description").gameObject.activeSelf,
    "solo Tactical RULES shows the objective, not the roster, and keeps the PFT requirement note");
Need(Label("Mode description").text.Contains("PitFireTeam supports 1-4 companions.", StringComparison.Ordinal),
    "solo Tactical visible description names the flexible PFT squad support");
string TacticalRowLabel() => Buttons().Single(x => x.name == "Arena Mode tactical").GetComponentInChildren<TextMeshProUGUI>(true).text;
Need(TacticalRowLabel() == "TACTICAL / 1v" + Settings.TacticalSquadSize.Value + " (WIP)",
    "solo Tactical roster stays in the mode row label");
view.Synchronize("tactical", true);
Need(Label("OBJECTIVE value").text == "Round elimination" && TacticalRowLabel() == "TACTICAL / 5v5 PFT (WIP)",
    "PFT Tactical shows the same objective and the roster in the mode row label");
Need(Label("Mode description").text.Contains("Fallen allies return next round.", StringComparison.Ordinal),
    "protected Tactical description explains round-delayed restoration");
for (int companions = 1; companions <= 4; companions++)
{
    ArenaMode.Compat.PitFireTeamCompat.SelectedCompanionCount = companions;
    view.Synchronize("tactical", true);
    Need(TacticalRowLabel() == $"TACTICAL / {companions + 1}v5 PFT (WIP)",
        "Tactical label reflects selected allies without reducing the opposing five");
}
ArenaMode.Compat.PitFireTeamCompat.SelectedCompanionCount = 4;
view.Synchronize("zombie", false);
Need(Label("Mode description").text.Contains("Ammo refills each wave.", StringComparison.Ordinal)
    && Label("Mode description").text.Contains("PitFireTeam", StringComparison.Ordinal)
    && Label("RESPAWN value").text == "None", "solo Zombie explains resupply and optional PFT without promising a respawn");
view.Synchronize("zombie", true);
Need(Label("Mode description").text.Contains("All entry gear refills each wave.", StringComparison.Ordinal)
    && Label("Mode description").text.Contains("team wipe", StringComparison.Ordinal),
    "PFT Zombie resupply note includes the surviving PMC and explains defeat");
view.Synchronize("tactical", false);
var dmRetired = Dm(0,5).onClick.Snapshot();
view.Synchronize("teamdeathmatch", false);
foreach (var callback in dmRetired) callback();
Need(Settings.DeathmatchRounds.Value == 3, "hidden Deathmatch callbacks cannot change other mode");

// Item 1 of the 2026-10-06 follow-up ("own gear to the left"): the equipment
// choice row now lives in the LEFT mode panel (ArenaEquipmentChoiceView),
// replacing the plain EQUIPMENT fact while visible. Today only Zombie offers
// it; every other mode keeps the plain fact, same HIDE-when-inapplicable
// convention as the TDM/Deathmatch rules rows.
Button EquipmentChoiceButton(bool ownGear) =>
    Buttons().Single(x => x.name == "Arena Equipment Choice " + (ownGear ? "OwnGear" : "ArenaKits"));
GameObject EquipmentChoiceRoot() =>
    view.Root.transform.Children.Single(x => x.name == "Arena Equipment Choice").gameObject;
Settings.ZombieOwnGear.Value = false;
foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch" })
{
    view.Synchronize(mode, mode == "tactical");
    Need(!EquipmentChoiceRoot().activeSelf, "equipment choice stays hidden for mode " + mode);
    Need(Label("EQUIPMENT label").gameObject.activeSelf && Label("EQUIPMENT value").gameObject.activeSelf,
            "the plain EQUIPMENT fact remains for mode " + mode);
    EquipmentChoiceButton(true).onClick.Invoke();
    Need(!Settings.ZombieOwnGear.Value, "a hidden equipment choice row cannot change the setting on mode " + mode);
}
view.Synchronize("zombie", false);
Need(EquipmentChoiceRoot().activeSelf && Label("EQUIPMENT label").gameObject.activeSelf
    && !Label("EQUIPMENT value").gameObject.activeSelf,
    "Zombie keeps the common equipment label and substitutes only its value");
Need(Marked(EquipmentChoiceButton(false))
    && !Marked(EquipmentChoiceButton(true)), "Arena kits is the persisted default, marked and labelled");
EquipmentChoiceButton(true).onClick.Invoke();
Need(Settings.ZombieOwnGear.Value && Marked(EquipmentChoiceButton(true)) && !Marked(EquipmentChoiceButton(false)),
    "real equipment choice control persists own gear and the marker/label follow it");
view.Synchronize("deathmatch", false);
Need(Label("EQUIPMENT value").text == "Arena kits", "leaving Zombie restores the plain fact for Deathmatch");
view.Synchronize("zombie", false);
Need(Label("EQUIPMENT value").text == "Own gear",
    "the hidden EQUIPMENT fact value keeps tracking the inline choice");
ArenaSession.ArmedForNextRaid = true;
view.Synchronize("zombie", false);
EquipmentChoiceButton(false).onClick.Invoke();
Need(!EquipmentChoiceButton(false).interactable && !EquipmentChoiceButton(true).interactable
    && Settings.ZombieOwnGear.Value && EquipmentChoiceRoot().activeSelf,
    "armed equipment choice row stays visible but frozen, same as DM/TDM rules");
ArenaSession.ArmedForNextRaid = false;
view.Synchronize("zombie", false);
EquipmentChoiceButton(false).onClick.Invoke();
Need(!Settings.ZombieOwnGear.Value && Marked(EquipmentChoiceButton(false)),
    "unarmed equipment choice row can restore Arena kits through the real control");
// A protected Zombie squad always keeps PMC entry gear. Hidden/stale callbacks
// must not rewrite the solo preference or permit a kit/body swap for its leader.
foreach (bool ownGear in new[] { false, true })
{
    Settings.ZombieOwnGear.Value = ownGear;
    view.Synchronize("zombie", false);
    var staleEquipmentCallbacks = EquipmentChoiceButton(!ownGear).onClick.Snapshot();
    view.Synchronize("zombie", true);
    Need(!EquipmentChoiceRoot().activeSelf && Label("EQUIPMENT value").gameObject.activeSelf
        && Label("EQUIPMENT value").text == "PMC entry gear",
        "protected Zombie hides kit choices and displays entry gear independently of the solo preference");
    Need(Label("RESPAWN value").text == "Next wave" && Label("OBJECTIVE value").text == "Survive the waves",
        "protected Zombie explains the cleared-wave return instead of solo terminal death");
    Need(Label("Mode description").gameObject.activeSelf
        && Label("Mode description").text.Contains("team wipe ends play", StringComparison.Ordinal)
        && Label("Mode description").text.Contains("Fallen allies return", StringComparison.Ordinal)
        && Label("Mode description").text.Contains("All entry gear refills each wave", StringComparison.Ordinal),
        "protected Zombie states elimination and cleared-wave restoration semantics");
    Need(Buttons().Single(x => x.name == "Arena Mode zombie").GetComponentInChildren<TextMeshProUGUI>(true).text
            == "ZOMBIES / PFT (WIP)",
        "Zombie row labels the protected companion variant");
    foreach (var callback in staleEquipmentCallbacks) callback();
    EquipmentChoiceButton(!ownGear).onClick.Invoke();
    Need(Settings.ZombieOwnGear.Value == ownGear,
        "stale or direct hidden equipment callbacks cannot change the saved solo choice in team Zombie");
    view.Synchronize("zombie", false);
    Need(EquipmentChoiceRoot().activeSelf && Settings.ZombieOwnGear.Value == ownGear,
        "leaving protected Zombie restores the independent solo equipment selection");
}
// 2026-10-09: inspect actual visibility and placement, not merely a hidden
// label's string. Every variant has one description and the same common facts;
// optional controls occupy only the dedicated value column or the area below.
// String bounds are a copy budget, not proof of native TMP glyph layout.
foreach (var (schemaMode, schemaTeam) in new[] { ("deathmatch", false), ("gungame", false), ("tactical", false),
    ("tactical", true), ("teamdeathmatch", false), ("zombie", false), ("zombie", true) })
{
    foreach (bool ownGear in schemaMode == "zombie" ? new[] { false, true } : new[] { false })
    {
        Settings.ZombieOwnGear.Value = ownGear;
        view.Synchronize(schemaMode, schemaTeam);
        string scope = schemaMode + (schemaTeam ? " (PFT)" : "") + (ownGear ? " own gear" : "");
        foreach (string fact in new[] { "OBJECTIVE", "RESPAWN", "EQUIPMENT" })
        {
            Need(Label(fact + " label").gameObject.activeInHierarchy,
                "common fact labels are visible for every mode: " + scope + " " + fact);
            Need(fact == "EQUIPMENT" && EquipmentChoiceRoot().activeInHierarchy
                || Label(fact + " value").gameObject.activeInHierarchy,
                "common fact values or their inline choice are visible for every mode: " + scope + " " + fact);
            string rendered = Label(fact + " value").text;
            Need(rendered.Length is > 0 and <= 18
                    && rendered.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(t => t.Any(char.IsLetterOrDigit)) <= 3,
                fact + " fact fits the value column without an ellipsis: " + scope + " '" + rendered + "'");
        }
        Need(Label("RESPAWN value").text is "Instant" or "Next round" or "Next wave" or "After killcam" or "None",
            "RESPAWN fact stays inside the known vocabulary: " + scope);
        Need(Label("Mode description").gameObject.activeInHierarchy
                && Label("Mode description").enableWordWrapping
                && Label("Mode description").text.Length is > 0 and <= 80
                && Label("Mode description").text.EndsWith('.'),
            "every mode has an actual visible two-line description, not an unavailable tooltip: " + scope);
        foreach (var (name, top) in new[] { ("OBJECTIVE", 330f), ("RESPAWN", 360f), ("EQUIPMENT", 390f) })
            Need(Label(name + " label").rectTransform.anchoredPosition == new Vector2(20f, -top)
                && Label(name + " value").rectTransform.anchoredPosition == new Vector2(122f, -top),
                "common fact positions do not jump between modes: " + scope + " " + name);
        var description = Label("Mode description").rectTransform;
        Need(-description.anchoredPosition.y + description.sizeDelta.y <= 330f,
            "description has its own space above the common facts: " + scope);
        foreach (var child in new[] { dmRoot, RuleRoot() }.Where(x => x.activeInHierarchy))
        {
            var childRect = (RectTransform)child.transform;
            Need(-childRect.anchoredPosition.y >= 420f
                    && -childRect.anchoredPosition.y + childRect.sizeDelta.y <= ((RectTransform)view.Root.transform).sizeDelta.y,
                "limit controls are below all common facts and inside the panel: " + scope);
            foreach (var button in child.GetComponentsInChildren<Button>(false))
            {
                var buttonRect = (RectTransform)button.transform;
                var textRect = button.GetComponentInChildren<TMP_Text>(true).rectTransform;
                Need(-buttonRect.anchoredPosition.y >= 0
                        && -buttonRect.anchoredPosition.y + buttonRect.sizeDelta.y <= childRect.sizeDelta.y
                        && textRect.sizeDelta.y <= buttonRect.sizeDelta.y,
                    "limit button and label remain inside their own row: " + scope + " " + button.name);
            }
        }
        if (EquipmentChoiceRoot().activeInHierarchy)
        {
            var equipmentRect = (RectTransform)EquipmentChoiceRoot().transform;
            Need(equipmentRect.anchoredPosition == Label("EQUIPMENT value").rectTransform.anchoredPosition
                    && equipmentRect.sizeDelta == Label("EQUIPMENT value").rectTransform.sizeDelta,
                "equipment choices occupy only the shared value column: " + scope);
        }
    }
}
Settings.ZombieOwnGear.Value = false;
view.Synchronize("zombie", false);
Need(Label("OBJECTIVE value").text == "Survive the waves" && Label("RESPAWN value").text == "None"
    && Label("EQUIPMENT value").text == "Arena kits", "Zombie facts fit the box: objective, None, Arena kits");
Need(Label("RESPAWN value").text == "None" && Label("Mode description").text.Contains("Ammo refills each wave.", StringComparison.Ordinal),
    "solo Zombie retains terminal death and describes the between-wave refill");
var staleMode = Buttons().Single(x => x.name == "Arena Mode gungame").onClick.Snapshot();
Button MutatorButton(ArenaMutator id) => screen.GetComponentsInChildren<Button>(true).Single(x => x.name == "Arena Mutator " + id);
GameObject MutatorsRoot() => screen.transform.Children.Single(x => x.name == "Arena Mutators").gameObject;
Button KillcamButton() => MutatorButton(ArenaMutator.Killcam);
string MutatorStatus(ArenaMutator id) => MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Mutator status " + id).text;
string KillcamText() => "KILLCAM: " + MutatorStatus(ArenaMutator.Killcam);
Need(MutatorsRoot().activeInHierarchy, "mutators are visible immediately without an opener");
Need(MutatorsRoot().GetComponentsInChildren<Button>(true).Count(b => b.gameObject.activeSelf) == 22,
    "inline panel shows twenty-two switches; Crowd shares the unsupported Fast Bots slot in Zombies");
// 2026-10-08 menu polish, item 1: the ANNOUNCER row names the kill callouts it
// now hides, and every description still fits the one-line description cell.
Need(ArenaMutatorSelection.Description(ArenaMutator.Announcer) == "Stingers, cues and kill callouts.",
    "ANNOUNCER description names stingers, cues and the on-screen kill callouts");
foreach (ArenaMutator id in Enum.GetValues(typeof(ArenaMutator)))
    Need(ArenaMutatorSelection.Description(id).Length is > 0 and <= 36,
        "mutator description fits the one-line description cell: " + id);
Need(!Settings.AkimboEnabled.Value, "akimbo remains an explicit opt-in");
foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch" })
{
    view.Synchronize(mode, mode == "tactical");
    Need(MutatorButton(ArenaMutator.Akimbo).interactable && MutatorStatus(ArenaMutator.Akimbo) == "OFF",
        "akimbo available across Arena modes " + mode);
    MutatorButton(ArenaMutator.Akimbo).onClick.Invoke();
    Need(Settings.AkimboEnabled.Value && MutatorStatus(ArenaMutator.Akimbo) == "ON", "akimbo button persists original config entry");
    MutatorButton(ArenaMutator.Akimbo).onClick.Invoke();
}
var mutatorRect = (RectTransform)MutatorsRoot().transform;
Need(mutatorRect.anchorMax == Vector2.one && mutatorRect.anchoredPosition == new Vector2(-56,-480)
    && mutatorRect.sizeDelta == new Vector2(600,500), "inline panel always reserves the full thumbnail or Customs preview slot");
// 2026-10-08 menu polish, item 2: no legend subtitle, no "saved with F12" footer.
// The rows start 8 px under the title and the last description ends 8 px above
// the panel bottom, in two columns of ten.
Need(!MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Any(t => t.name is "Mutator legend" or "Mutators saved note"
        || t.text.Contains("Saved with F12") || t.text.Contains("Green: ON") || t.text.Contains("Killcam/tracers")),
    "mutator panel carries no legend or saved-with-F12 footer");
Need(MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Where(t => t.name == "Title").Single().text == "MUTATORS",
    "mutator panel keeps its title");
{
    var mutatorIds = Enum.GetValues(typeof(ArenaMutator)).Cast<ArenaMutator>()
        .Where(id => MutatorButton(id).gameObject.activeSelf).ToArray();
    float RowTop(ArenaMutator id) => -((RectTransform)MutatorButton(id).transform).anchoredPosition.y;
    RectTransform Detail(ArenaMutator id) => MutatorsRoot().GetComponentsInChildren<TMP_Text>(true)
        .Single(t => t.name == "Mutator detail " + id).rectTransform;
    float pitch = (500f - 52f - 8f) / 11f;
    Need(mutatorIds.Length == 22 && Math.Abs(RowTop(mutatorIds[0]) - 52f) < .01f
        && Math.Abs(RowTop(mutatorIds[11]) - 52f) < .01f,
        "both mutator columns start 8 px below the title");
    Need(Math.Abs(RowTop(mutatorIds[1]) - RowTop(mutatorIds[0]) - pitch) < .01f,
        "mutator rows reflow without moving the panel");
    Need(mutatorIds.All(id => Detail(id).sizeDelta.y >= 18f), "all descriptions retain one readable line");
    Need(mutatorIds.All(id => Math.Abs(((RectTransform)MutatorButton(id).transform).sizeDelta.y - 22f) < .01f
        && Math.Abs(Detail(id).sizeDelta.y - 18f) < .01f),
        "twenty-two mutators retain 22px buttons and 18px description cells");
    foreach (var lastId in new[] { mutatorIds[10], mutatorIds[21] })
    {
        var lastDetail = Detail(lastId);
        Need(Math.Abs(-lastDetail.anchoredPosition.y + lastDetail.sizeDelta.y - 492f) < .01f
            && Math.Abs(lastDetail.sizeDelta.y - (pitch - 22f)) < .01f,
            "last description ends 8 px above the panel bottom and fills the row pitch");
    }
}
// 2026-10-08 menu polish, item 5: colored tracers are team-only, exactly like the
// team laser colors. Settings.TracerEntry has an entry (the one Effects/
// TeamTracers) only for Team DM and protected Tactical/Zombie squads; every other mode
// shows N/A with the lasers' reason and nothing can be written.
string MutatorDetail(ArenaMutator id) => MutatorsRoot().GetComponentsInChildren<TMP_Text>(true)
    .Single(t => t.name == "Mutator detail " + id).text;
foreach (var (mode, team, expected) in new[] {
    ("teamdeathmatch", false, true), ("tactical", true, true), ("zombie", true, false) })
{
    view.Synchronize(mode, team);
    Need(MutatorButton(ArenaMutator.FriendlyFire).interactable
        && MutatorStatus(ArenaMutator.FriendlyFire) == (expected ? "ON" : "OFF"),
        "friendly-fire button displays the actual per-mode default: " + mode);
    var target = Settings.FriendlyFireEntry(mode, team);
    MutatorButton(ArenaMutator.FriendlyFire).onClick.Invoke();
    Need(target.Value == !expected && Settings.GetFriendlyFireEnabled(mode, team) == !expected
        && MutatorStatus(ArenaMutator.FriendlyFire) == (!expected ? "ON" : "OFF"),
        "visible friendly-fire control saves and displays its own preference: " + mode);
    foreach (var other in new[] { Settings.TeamDeathmatchFriendlyFire, Settings.TacticalFriendlyFire, Settings.ZombieFriendlyFire })
        if (!ReferenceEquals(other, target))
            Need(other.Value == !ReferenceEquals(other, Settings.ZombieFriendlyFire),
                "toggling one friendly-fire mode preserves other modes");
    foreach (bool armed in new[] { false, true })
    {
        ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
        MutatorButton(ArenaMutator.FriendlyFire).onClick.Invoke();
        Need(target.Value == !expected, "friendly-fire control cannot change an armed/live match");
    }
    ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
    view.Synchronize(mode, team);
    MutatorButton(ArenaMutator.FriendlyFire).onClick.Invoke();
    Need(target.Value == expected, "friendly-fire control restores this mode's default");
}
foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "zombie" })
{
    view.Synchronize(mode, false);
    Need(MutatorStatus(ArenaMutator.FriendlyFire) == "N/A"
        && !MutatorButton(ArenaMutator.FriendlyFire).interactable
        && MutatorDetail(ArenaMutator.FriendlyFire) == ArenaMutatorSelection.TeamModesOnly,
        "friendly-fire switch is unavailable in solo mode: " + mode);
    MutatorButton(ArenaMutator.FriendlyFire).onClick.Invoke();
    Need(Settings.TeamDeathmatchFriendlyFire.Value && Settings.TacticalFriendlyFire.Value
        && !Settings.ZombieFriendlyFire.Value, "unavailable friendly-fire callback cannot alter preferences");
}
Settings.TeamTracers.Value = true;
foreach (var (soloMode, soloTeam) in new[] { ("deathmatch", false), ("gungame", false), ("tactical", false), ("zombie", false) })
{
    view.Synchronize(soloMode, soloTeam);
    Need(MutatorStatus(ArenaMutator.Tracers) == "N/A" && !MutatorButton(ArenaMutator.Tracers).interactable,
        "colored tracers are N/A outside team modes: " + soloMode);
    Need(MutatorDetail(ArenaMutator.Tracers) == ArenaMutatorSelection.TeamModesOnly
        && MutatorDetail(ArenaMutator.Tracers) == MutatorDetail(ArenaMutator.Lasers),
        "colored tracers give the lasers' team-only reason: " + soloMode);
    MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
    Need(Settings.TeamTracers.Value && MutatorStatus(ArenaMutator.Tracers) == "N/A",
        "unavailable tracers preserve the team preference on direct callback: " + soloMode);
}
foreach (var (teamMode, team) in new[] { ("teamdeathmatch", false), ("tactical", true), ("zombie", true) })
{
    Settings.TeamTracers.Value = true;
    view.Synchronize(teamMode, team);
    Need(MutatorStatus(ArenaMutator.Tracers) == "ON" && MutatorButton(ArenaMutator.Tracers).interactable
        && MutatorDetail(ArenaMutator.Tracers) == "Team colors for every bullet.",
        "colored tracers are available and ON by default in team modes: " + teamMode);
    MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
    Need(!Settings.TeamTracers.Value && MutatorStatus(ArenaMutator.Tracers) == "OFF",
        "team tracer switch persists Effects/TeamTracers OFF: " + teamMode);
    MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
    Need(Settings.TeamTracers.Value && MutatorStatus(ArenaMutator.Tracers) == "ON",
        "team tracer switch persists Effects/TeamTracers ON: " + teamMode);
    foreach (bool armed in new[] { false, true })
    {
        ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
        MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
        Need(Settings.TeamTracers.Value, "team tracer writes respect active and armed locks: " + teamMode);
    }
    ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
}
foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical" })
{
    view.Synchronize(mode, mode == "tactical");
    Need(mutatorRect.anchoredPosition == new Vector2(-56,-480), "mode changes never move the mutator panel");
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
foreach (string instantMode in new[] { "deathmatch", "teamdeathmatch" })
{
    view.Synchronize(instantMode, false);
    KillcamButton().onClick.Invoke();
    Need(Label("RESPAWN value").gameObject.activeInHierarchy && Label("RESPAWN value").text == "After killcam",
        "Deathmatch and TDM actually show replay-delayed respawn: " + instantMode);
    ArenaMode.Spectator.KillcamReplayCompat.Available = false;
    view.Synchronize(instantMode, false);
    Need(Label("RESPAWN value").text == "Instant", "unavailable replay never delays the advertised respawn: " + instantMode);
    ArenaMode.Spectator.KillcamReplayCompat.Available = true;
    view.Synchronize(instantMode, false);
    KillcamButton().onClick.Invoke();
    Need(Label("RESPAWN value").text == "Instant", "disabled replay restores the advertised instant respawn: " + instantMode);
}
Need(Settings.GetReplaySeconds("deathmatch") == 0f && Settings.GetReplaySeconds("teamdeathmatch") == 0f
    && Settings.GetReplaySeconds("tactical") == 3f, "selecting one mode leaves the other replay choices untouched");
Settings.ZombieReplaySeconds.Value = 3f;
view.Synchronize("zombie", false);
Need(KillcamText() == "KILLCAM: ON" && Label("RESPAWN value").text == "None",
    "solo Zombie killcam does not imply a respawn");
view.Synchronize("zombie", true);
Need(Label("RESPAWN value").text == "Next wave", "protected Zombie team keeps next-wave respawn");
view.Synchronize("gungame", false);
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
    (ArenaMutator.FastMovement, Settings.MovementEnabled),
    (ArenaMutator.FastBots, Settings.BotMovementEnabled),
    (ArenaMutator.Molotov, ArenaMode.Combat.Fire.ArenaFireSettings.Enabled),
    (ArenaMutator.Akimbo, Settings.AkimboEnabled),
    (ArenaMutator.Bunnyhop, Settings.BunnyhopEnabled),
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
    (ArenaMutator.Lasers, Settings.TeamLasers),
    (ArenaMutator.AutoQuickslots, Settings.AutoQuickSlots),
    (ArenaMutator.InstantSearch, Settings.InstantSearch),
    (ArenaMutator.ArenaHud, Settings.CombatHudEnabled),
    // Item 2 of the 2026-10-06 follow-up ("announcer mutator"): per-mode like
    // Tracers, toggled through the exact same generic Entry()/TryToggle()
    // path — view is synchronized to "teamdeathmatch" here, so its own entry
    // is TeamDeathmatchAnnouncerEnabled (see Settings.AnnouncerEntry).
    (ArenaMutator.Announcer, Settings.TeamDeathmatchAnnouncerEnabled) };
foreach (var (id, entry) in preferencePairs)
{
    bool before = entry.Value;
    var othersBefore = preferencePairs.Where(p => p.Item1 != id).Select(p => p.Item2.Value).ToArray();
    int mutatorSaves = config.Saves;
    MutatorButton(id).onClick.Invoke();
    Need(entry.Value != before && config.Saves > mutatorSaves, "mutator persists original F12 entry: " + id);
    Need(othersBefore.SequenceEqual(preferencePairs.Where(p => p.Item1 != id).Select(p => p.Item2.Value)),
        "mutator changes only its own saved preference: " + id);
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
var newMutatorPairs = preferencePairs.Where(p => p.Item1 is ArenaMutator.AutoQuickslots
    or ArenaMutator.InstantSearch or ArenaMutator.ArenaHud).ToArray();
config.SaveOnConfigSet = false;
foreach (var (id, entry) in newMutatorPairs)
{
    bool before = entry.Value;
    int beforeSaves = config.Saves;
    MutatorButton(id).onClick.Invoke();
    Need(entry.Value != before && config.Saves == beforeSaves + 1,
        "new mutator saves exactly once when automatic saving is disabled: " + id);
    MutatorButton(id).onClick.Invoke();
    Need(entry.Value == before && config.Saves == beforeSaves + 2,
        "new mutator restored preference is saved exactly once: " + id);
}
config.SaveOnConfigSet = true;
Settings.ShowKillfeed.Value = false;
Settings.ShowHitmarker.Value = true;
Settings.ShowDamageNumbers.Value = false;
Settings.ShowFriendlyMarkers.Value = false;
var individualHudEntries = new[] { Settings.ShowKillfeed, Settings.ShowHitmarker, Settings.ShowDamageNumbers,
    Settings.ShowFriendlyMarkers, Settings.AnnouncerEnabled };
var individualHudBefore = individualHudEntries.Select(e => e.Value).ToArray();
float pointBrightness = Settings.VisibleLaserBrightness.Value, beamBrightness = Settings.VisibleLaserBeamBrightness.Value;
MutatorButton(ArenaMutator.Lasers).onClick.Invoke();
Need(Settings.VisibleLaserBrightness.Value == pointBrightness && Settings.VisibleLaserBeamBrightness.Value == beamBrightness,
    "laser color toggle does not alter brightness");
foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie" })
{
    foreach (bool team in mode is "tactical" or "zombie" ? new[] { false, true } : new[] { false })
    {
        view.Synchronize(mode, team);
        foreach (var (id, entry) in newMutatorPairs)
        {
            bool before = entry.Value;
            Need(MutatorButton(id).interactable && MutatorStatus(id) == (before ? "ON" : "OFF"),
                "new mutator available in every Arena mode: " + id + " / " + mode + " / " + team);
            MutatorButton(id).onClick.Invoke();
            Need(entry.Value != before && MutatorStatus(id) == (!before ? "ON" : "OFF"),
                "new mutator callback changes its saved value in every mode: " + id);
            Need(Settings.HudEnabled.Value && individualHudBefore.SequenceEqual(individualHudEntries.Select(e => e.Value)),
                "new mutators preserve the HUD master and all individual HUD preferences");
            MutatorButton(id).onClick.Invoke();
            Need(entry.Value == before && individualHudBefore.SequenceEqual(individualHudEntries.Select(e => e.Value)),
                "new mutator roundtrip restores only its own preference: " + id);
        }
        bool pickupAllowed = mode == "deathmatch" || mode == "teamdeathmatch";
        bool laserAllowed = mode == "teamdeathmatch" || (mode is "tactical" or "zombie" && team);
        Need(MutatorButton(ArenaMutator.Pickups).interactable == pickupAllowed, "pickup availability matches runtime modes");
        Need(MutatorButton(ArenaMutator.Lasers).interactable == laserAllowed, "laser availability matches team modes");
        Need(MutatorButton(ArenaMutator.Tracers).interactable == laserAllowed,
            "tracer availability is exactly the laser availability (team modes only)");
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
            bool tracersBefore = Settings.TeamTracers.Value;
            MutatorButton(ArenaMutator.Tracers).onClick.Invoke();
            Need(Settings.TeamTracers.Value == tracersBefore, "unavailable tracers preserve preference on direct callback");
        }
    }
}
view.Synchronize("teamdeathmatch", false);
foreach (bool combatBefore in new[] { false, true })
{
    Settings.CombatHudEnabled.Value = combatBefore;
    Settings.HudEnabled.Value = false;
    view.Synchronize("teamdeathmatch", false);
    Need(MutatorStatus(ArenaMutator.ArenaHud) == "N/A" && !MutatorButton(ArenaMutator.ArenaHud).interactable
        && MutatorDetail(ArenaMutator.ArenaHud) == "Disabled by HUD master in F12.",
        "HUD master disables the combat mutator with an explicit reason");
    int disabledSaves = config.Saves;
    MutatorButton(ArenaMutator.ArenaHud).onClick.Invoke();
    Need(!Settings.HudEnabled.Value && Settings.CombatHudEnabled.Value == combatBefore && config.Saves == disabledSaves,
        "disabled HUD master blocks direct combat callbacks without overwriting either preference");
    Need(MutatorButton(ArenaMutator.AutoQuickslots).interactable && MutatorButton(ArenaMutator.InstantSearch).interactable,
        "HUD master does not gate quickslots or instant search");
    Settings.HudEnabled.Value = true;
    view.Synchronize("teamdeathmatch", false);
    Need(MutatorButton(ArenaMutator.ArenaHud).interactable
        && MutatorStatus(ArenaMutator.ArenaHud) == (combatBefore ? "ON" : "OFF")
        && individualHudBefore.SequenceEqual(individualHudEntries.Select(e => e.Value)),
        "HUD master recovery shows the preserved combat and individual preferences");
}
var movementAbilities = new[] { ArenaMutator.DoubleJump, ArenaMutator.Bunnyhop, ArenaMutator.AirControl,
    ArenaMutator.AirFire, ArenaMutator.AirVault, ArenaMutator.Slide, ArenaMutator.InfiniteStamina, ArenaMutator.NoFallDamage };
var savedPreferences = preferencePairs.Skip(1).Select(p => p.Item2.Value).ToArray();
var savedWalk = Settings.WalkSpeedMult.Value;
var savedSprint = Settings.SprintSpeedMult.Value;
var savedJump = Settings.JumpHeightMult.Value;
var savedBots = Settings.BotMovementEnabled.Value;
MutatorButton(ArenaMutator.FastMovement).onClick.Invoke();
Need(!Settings.MovementEnabled.Value && MutatorStatus(ArenaMutator.FastMovement) == "OFF"
    && MutatorButton(ArenaMutator.FastMovement).interactable, "master stays usable after switching movement off");
foreach (var (id, entry) in preferencePairs.Where(p => movementAbilities.Contains(p.Item1)))
{
    bool before = entry.Value;
    MutatorButton(id).onClick.Invoke();
    Need(!MutatorButton(id).interactable && MutatorStatus(id) == "N/A" && entry.Value == before,
        "movement master gates mutator without changing saved preference: " + id);
}
Need(MutatorButton(ArenaMutator.Backpack).interactable && MutatorButton(ArenaMutator.Pickups).interactable
    && MutatorButton(ArenaMutator.Tracers).interactable && MutatorButton(ArenaMutator.Akimbo).interactable
    && newMutatorPairs.All(p => MutatorButton(p.Item1).interactable),
    "movement master leaves other mutators usable");
MutatorButton(ArenaMutator.FastMovement).onClick.Invoke();
Need(Settings.MovementEnabled.Value && MutatorStatus(ArenaMutator.FastMovement) == "ON",
    "master can re-enable movement through the same menu control");
Need(savedPreferences.SequenceEqual(preferencePairs.Skip(1).Select(p => p.Item2.Value))
    && savedWalk == Settings.WalkSpeedMult.Value && savedSprint == Settings.SprintSpeedMult.Value
    && savedJump == Settings.JumpHeightMult.Value && savedBots == Settings.BotMovementEnabled.Value,
    "master roundtrip preserves individual preferences, movement multipliers and bot setting");
foreach (var (id, entry) in preferencePairs.Where(p => movementAbilities.Contains(p.Item1)))
    Need(MutatorButton(id).interactable && MutatorStatus(id) == (entry.Value ? "ON" : "OFF"),
        "master re-enable restores saved ability display: " + id);
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
    // READY begins at reference Y=984 in the observed native 1080p layout.
    Need(-current.anchoredPosition.y + current.sizeDelta.y * current.localScale.y <= 980f * size.y / 1080f + .01f,
        "right column leaves bottom native navigation clear at supported canvas sizes");
    Need(size.x + current.anchoredPosition.x - current.sizeDelta.x * current.localScale.x > size.x * .65f,
        "right column stays beyond the central map and time strip");
    foreach (ArenaMutator id in Enum.GetValues(typeof(ArenaMutator)))
    {
        var buttonRect = (RectTransform)MutatorButton(id).transform;
        var detail = MutatorsRoot().GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "Mutator detail " + id).rectTransform;
        bool separate = -detail.anchoredPosition.y >= -buttonRect.anchoredPosition.y + buttonRect.sizeDelta.y;
        // Eleven rows per column retain separate descriptions and an 8-px
        // bottom margin in the 500-px panel.
        Need(separate && -detail.anchoredPosition.y + detail.sizeDelta.y <= 492f + .01f,
            "all mutator descriptions fit outside their button and inside the panel");
        foreach (ArenaMutator other in Enum.GetValues(typeof(ArenaMutator)))
        {
            if (other == id) continue;
            var otherRect = (RectTransform)MutatorButton(other).transform;
            bool horizontalGap = detail.anchoredPosition.x >= otherRect.anchoredPosition.x + otherRect.sizeDelta.x
                || detail.anchoredPosition.x + detail.sizeDelta.x <= otherRect.anchoredPosition.x;
            // 41.2px row pitch has float roundoff at shared edges (340.40002 vs 340.4).
            const float edgeTolerance = .001f;
            bool verticalGap = -detail.anchoredPosition.y + edgeTolerance >= -otherRect.anchoredPosition.y + otherRect.sizeDelta.y
                || -detail.anchoredPosition.y + detail.sizeDelta.y <= -otherRect.anchoredPosition.y + edgeTolerance;
            Need(horizontalGap || verticalGap, "mutator descriptions never overlap another button: "
                + id + " bottom=" + (-detail.anchoredPosition.y + detail.sizeDelta.y).ToString("R")
                + " / " + other + " top=" + (-otherRect.anchoredPosition.y).ToString("R"));
        }
    }
}
// New menu controls must use the real config entries, not copied defaults or
// independent state; direct retained callbacks must obey the same raid locks.
{
    Need(ArenaMutatorSelection.Title(ArenaMutator.Akimbo).EndsWith(" (WIP)")
        && ArenaMutatorSelection.Title(ArenaMutator.Molotov).EndsWith(" (WIP)"), "unfinished mutators visibly end in WIP");
    foreach (bool team in new[] { false, true })
    {
        view.Synchronize("zombie", team);
        Need(MutatorStatus(ArenaMutator.Molotov) == "N/A" && MutatorStatus(ArenaMutator.FastBots) == "N/A",
            "Zombie excludes the two unsupported runtime mutators");
        bool saved = ArenaMode.Combat.Fire.ArenaFireSettings.Enabled.Value;
        MutatorButton(ArenaMutator.Molotov).onClick.Invoke();
        Need(ArenaMode.Combat.Fire.ArenaFireSettings.Enabled.Value == saved, "unsupported Molotov callback preserves preference");
        foreach (string mode in new[] { "zombie", "tactical" })
        {
            view.Synchronize(mode, team);
            Need(Buttons().Single(b => b.name == "Arena Mode " + mode).GetComponentInChildren<TMP_Text>(true).text.EndsWith(" (WIP)"),
                "dynamic solo and PFT mode labels retain visible WIP suffix");
        }
    }
    view.Synchronize("deathmatch", false);
    ArenaMode.Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = false;
    view.Synchronize("deathmatch", false);
    Need(!MutatorButton(ArenaMutator.Molotov).interactable && MutatorStatus(ArenaMutator.Molotov) == "N/A",
        "Molotov requires a registered companion before menu activation");
    ArenaMode.Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = true;

    Slider ZombieSlider(ArenaZombieRule rule) => view.Root.GetComponentsInChildren<Slider>(true).Single(s => s.name == "Arena Zombie " + rule);
    GameObject ZombieRoot() => view.Root.transform.Children.Single(t => t.name == "Arena Zombie Rules").gameObject;
    var zombieEntries = new[] { (ArenaZombieRule.MaxAlive, Settings.ZombieMaxAlive, 4, 96),
        (ArenaZombieRule.WaveStartSize, Settings.ZombieWaveStartSize, 1, 200),
        (ArenaZombieRule.WaveGrowth, Settings.ZombieWaveGrowth, 0, 100) };
    var originalZombieValues = zombieEntries.Select(x => x.Item2.Value).ToArray();
    var zombieState = new ArenaZombieRulesState();
    zombieState.Synchronize("zombie");
    foreach (bool team in new[] { false, true })
    {
        view.Synchronize("zombie", team);
        Need(ZombieRoot().activeInHierarchy && ((RectTransform)view.Root.transform).sizeDelta.y == 596,
            "Zombie sliders fit their mode panel in solo and PFT");
        foreach (var (rule, entry, minimum, maximum) in zombieEntries)
        {
            var slider = ZombieSlider(rule);
            Need(slider.wholeNumbers && slider.minValue == minimum && slider.maxValue == maximum,
                "Zombie slider uses original integer F12 bounds: " + rule);
            foreach (int requested in new[] { minimum, maximum, (minimum + maximum) / 2 })
            {
                slider.value = requested;
                Need(entry.Value == requested && Label("Zombie value " + rule).text == requested.ToString(),
                    "Zombie slider persists and shows exact config value: " + rule);
            }
            int before = entry.Value;
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, minimum - 1f, maximum + 1f, minimum + .5f })
            {
                slider.onValueChanged.Invoke(invalid);
                Need(entry.Value == before, "Zombie callback rejects invalid value: " + rule);
            }
            Need(!zombieState.TrySelect(rule, minimum - 1) && !zombieState.TrySelect(rule, maximum + 1) && entry.Value == before,
                "Zombie state rejects range violations rather than config clamping");
            foreach (bool armed in new[] { false, true })
            {
                ArenaSession.IsActive = !armed; ArenaSession.ArmedForNextRaid = armed;
                slider.onValueChanged.Invoke(minimum);
                Need(entry.Value == before && !zombieState.TrySelect(rule, minimum), "Zombie writes reject armed and active raids");
                ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
            }
            entry.Value = minimum;
            int beforeRefreshSaves = config.Saves;
            view.Synchronize("zombie", team);
            Need(slider.value == minimum && config.Saves == beforeRefreshSaves, "F12 synchronization does not write Zombie settings");
        }
    }
    foreach (string mode in new[] { "gungame", "deathmatch", "teamdeathmatch", "tactical" })
    {
        view.Synchronize(mode, false);
        int saved = Settings.ZombieMaxAlive.Value;
        ZombieSlider(ArenaZombieRule.MaxAlive).onValueChanged.Invoke(56f);
        Need(!ZombieRoot().activeInHierarchy && Settings.ZombieMaxAlive.Value == saved,
            "non-Zombie mode hides and rejects stale Zombie controls: " + mode);
    }
    view.Synchronize("zombie", false);
    var staleZombieCallbacks = ZombieSlider(ArenaZombieRule.MaxAlive).onValueChanged.Snapshot();
    Show("zombie", false);
    int beforeStaleZombie = Settings.ZombieMaxAlive.Value;
    foreach (var callback in staleZombieCallbacks) callback(56f);
    Need(Settings.ZombieMaxAlive.Value == beforeStaleZombie, "retired Zombie slider cannot mutate reopened screen");
    config.SaveOnConfigSet = false;
    int beforeManualSave = config.Saves;
    ZombieSlider(ArenaZombieRule.MaxAlive).value = 56;
    Need(Settings.ZombieMaxAlive.Value == 56 && config.Saves == beforeManualSave + 1, "Zombie slider explicitly saves when autosave is off");
    config.SaveOnConfigSet = true;
    for (int i = 0; i < zombieEntries.Length; i++) zombieEntries[i].Item2.Value = originalZombieValues[i];
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
    savedConfig.Stored["Effects/TeamTracers"] = saved;
    // 2026-10-08: the former solo keys are orphans now. A saved cfg that still
    // carries them (even as true) must neither be read nor enable any tracer.
    savedConfig.Stored["Deathmatch/ColoredTracers"] = true;
    savedConfig.Stored["Gun Game/ColoredTracers"] = true;
    savedConfig.Stored["Tactical/SoloColoredTracers"] = true;
    savedConfig.Stored["Zombie/ColoredTracers"] = true;
    Settings.Init(savedConfig);
    Need(Settings.TeamTracers.Value == saved, "saved team tracer choice survives configuration reload");
    Need(ReferenceEquals(Settings.TracerEntry("teamdeathmatch", false), Settings.TeamTracers)
        && ReferenceEquals(Settings.TracerEntry(" TeamDeathmatch ", false), Settings.TeamTracers)
        && ReferenceEquals(Settings.TracerEntry("tactical", true), Settings.TeamTracers)
        && ReferenceEquals(Settings.TracerEntry("zombie", true), Settings.TeamTracers)
        && ReferenceEquals(Settings.TracerEntry(" ZOMBIE ", true), Settings.TeamTracers),
        "team modes (Team DM, protected Tactical/Zombie) map to the one Effects/TeamTracers entry");
    foreach (var (mode, protectedTeam) in new[] { ("deathmatch", false), ("deathmatch", true), ("endless", false), ("rounds", false),
        ("gungame", false), ("gungame", true), ("tactical", false), ("zombie", false), ("unknown", false), (null, false) })
        Need(Settings.TracerEntry(mode, protectedTeam) == null,
            "solo modes have no tracer entry, orphaned solo keys enable nothing: " + (mode ?? "<null>") + (protectedTeam ? " (team flag)" : ""));
}
foreach (bool quickslots in new[] { false, true })
foreach (bool search in new[] { false, true })
foreach (bool combat in new[] { false, true })
foreach (bool hudMaster in new[] { false, true })
{
    var savedConfig = new ConfigFile();
    savedConfig.Stored["Kits/AutoQuickSlots"] = quickslots;
    savedConfig.Stored["Loot/InstantSearch"] = search;
    savedConfig.Stored["HUD/CombatElements"] = combat;
    savedConfig.Stored["HUD/Enabled"] = hudMaster;
    savedConfig.Stored["HUD/Killfeed"] = false;
    savedConfig.Stored["HUD/Hitmarker"] = true;
    savedConfig.Stored["HUD/DamageNumbers"] = false;
    Settings.Init(savedConfig);
    Need(Settings.AutoQuickSlots.Value == quickslots && Settings.InstantSearch.Value == search
        && Settings.CombatHudEnabled.Value == combat && Settings.HudEnabled.Value == hudMaster,
        "new mutator config keys reload independently of each other and the HUD master");
    Need(!Settings.ShowKillfeed.Value && Settings.ShowHitmarker.Value && !Settings.ShowDamageNumbers.Value,
        "combat HUD config reload preserves individual HUD choices");
}
Settings.Init(new ConfigFile());
Show();
foreach (bool team in new[] { false, true })
{
    view.Synchronize("zombie", team);
    Need(MutatorButton(ArenaMutator.ZombieCrowd).gameObject.activeSelf
        && !MutatorButton(ArenaMutator.FastBots).gameObject.activeSelf,
        "Crowd replaces unsupported Fast Bots without adding a visible row");
    Need(MutatorsRoot().GetComponentsInChildren<Button>(true).Count(b => b.gameObject.activeSelf) == 22,
        "Zombie keeps the same twenty-two readable rows");
    bool bots = Settings.BotMovementEnabled.Value;
    Need(MutatorStatus(ArenaMutator.ZombieCrowd) == "OFF", "Crowd starts visibly OFF");
    MutatorButton(ArenaMutator.ZombieCrowd).onClick.Invoke();
    Need(Settings.ZombieCrowdEnabled.Value && MutatorStatus(ArenaMutator.ZombieCrowd) == "ON",
        "Crowd menu saves the actual F12 entry and shows ON");
    foreach (string other in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical" })
    {
        view.Synchronize(other, team);
        Need(!MutatorButton(ArenaMutator.ZombieCrowd).gameObject.activeSelf,
            "Crowd is hidden outside Zombies");
        MutatorButton(ArenaMutator.ZombieCrowd).onClick.Invoke();
        Need(Settings.ZombieCrowdEnabled.Value, "retained hidden callback cannot change Crowd");
    }
    view.Synchronize("zombie", team);
    ArenaSession.ArmedForNextRaid = true;
    MutatorButton(ArenaMutator.ZombieCrowd).onClick.Invoke();
    Need(Settings.ZombieCrowdEnabled.Value, "armed raid locks menu Crowd preference");
    ArenaSession.ArmedForNextRaid = false;
    view.Synchronize("zombie", team);
    MutatorButton(ArenaMutator.ZombieCrowd).onClick.Invoke();
    Need(!Settings.ZombieCrowdEnabled.Value && Settings.BotMovementEnabled.Value == bots,
        "Crowd toggles independently of normal bot movement");
}
view.Close();
Console.WriteLine($"TDM_MATCH_MENU_CHECKS_PASSED ({checks} assertions)");
