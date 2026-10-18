# F12 settings menu (ConfigurationManager schema, 2026-10-18)

ArenaMode's F12 entries follow the shared SPT mod menu schema: human-readable
names, a deliberate order (most important first), developer tools hidden by
default. Display only: no `.cfg` section or key, default, range or description
changed, so every existing `com.maschine.ArenaMode.cfg` keeps working.

## What the player sees

- Sections from **General**, Arena, Match and the five modes (Deathmatch,
  Rounds, Gun Game, Team Deathmatch, Tactical, Tactical Team, Zombie) through
  Kits, Respawn, Difficulty, Bots, Movement, Character, Loot, pickups, HUD,
  info panels, Audio and Effects to **Performance** last among the normal ones.
- Readable names with units, e.g. *Spawn Protection (s)*, *Max Infected Alive*,
  *Killcam Replay (s)*, *Bot Profile Build Budget (ms per frame)*.
- **Diagnostics**, **Development - Area outline** and **Development - Pickups**
  are advanced entries (user request 2026-10-18): hidden until F12's
  *Advanced settings* filter is ticked. ConfigurationManager skips that filter
  while a search term is entered, so searching "capture" or "developer" lists
  them too.
- Not shown at all: the internal one-time migration markers
  (`General/OptionalMapsMigrated`, `General/OptionalMapsRevision`,
  `Deathmatch/LegacyRulesMigrated`, `Arena pickups/M60DefaultMigrated`), the
  legacy-only `Match/RoundKillTarget` and `Match/RoundTimeLimitSeconds`, and the
  per-map participant totals of `Map participants` (chosen on the map screen).
  The latter had an anonymous `new { Browsable = false }` tag that never worked:
  the manager only reads tags whose type is named `ConfigurationManagerAttributes`
  and stops reading tags at the first other object.
- Absolute counts on a 0..100 range (`Zombie/WaveGrowth`, `Deathmatch/Rounds`)
  are no longer drawn as percentages; real shares (volumes, ShooterPercent,
  leg-injury reduction, streak pressure, ADS slowdown, strafe smoothing) keep the
  percentage display.

## How it works

SPT ships ConfigurationManager 18.4.0 (`BepInEx\plugins\spt\ConfigurationManager`).
Decompiled facts this relies on:

- `SettingEntryBase.SetFromAttributes` copies the public fields of a tag object
  whose type is named `ConfigurationManagerAttributes` onto the setting's
  same-named properties (`DispName`, `Order`, `Browsable`, `IsAdvanced`,
  `ShowRangeAsPercent`, ...). It runs after `GetAcceptableValues`, so an explicit
  `ShowRangeAsPercent` overrides the manager's own percent guess.
- A plugin's sections appear in the order of their first entry in the plugin's
  `ConfigFile` enumeration, which is bind order; inside a section entries are
  sorted by `Order` descending, then by display name.
- BepInEx's `ConfigFile.Save` writes sections alphabetically, independent of the
  entry order.

Implementation (`ArenaMode.Client/Config`):

- `ArenaConfigMenuLayout.cs` (pure, linked into ArenaMode.Tests): sections in
  display order, entries in display order with name, hidden and percent flags,
  advanced per section. `Order` is derived from the position (top = highest).
- `ArenaConfigMenu.Apply(Config)`, called as the last step of
  `Plugin.InitializeRuntime` (after `ArenaFireRuntime.Enable`, the last bind):
  for every entry of the plugin's ConfigFile it replaces the description with an
  equal one whose first tag carries the display data (description text, range and
  existing tags unchanged; idempotent), then reorders the in-memory entry table by
  section rank (stable inside a section; same entry objects, values and events;
  the original table is put back on any failure). Entries without a layout entry
  are named in one warning and keep their raw key. Everything fails open to the
  plain BepInEx presentation.
- The bind calls themselves stay plain `config.Bind`: six offline harnesses
  compile the settings files against stub ConfigFiles, and those files are edited
  by parallel work. Moving the bind blocks into display order was not done
  because migrations inside `Settings.Init` read entries bound earlier.

## Adding a setting

Add the entry to `ArenaConfigMenuLayout.Sections` in the section and position
where it should appear. The tests fail otherwise:

- every literal `config/file.Bind(section, key, ...)` in ArenaMode.Client, the
  per-mode helpers and the info-panel helper has a layout entry, and every layout
  entry is bound;
- any new non-literal Bind call must be listed deliberately;
- a percent-looking range (int 0|1..100, float 0..1) needs a decision: `Pct(...)`
  for a share, plain for an absolute value (the test lists the absolute ones);
- names are unique and readable per section, Order strictly descending, the
  advanced set is exactly Diagnostics and the two Development sections, seven
  hidden items.

UiBridge reads the same tags: `uibridge_config` reports the advanced flag, and
`uibridge_config_set`/`gpu-run config-pin` refuse the seven hidden entries
(403 config_protected). gpu-run sets only the Zombie sliders that way; participant
totals are chosen on the map screen, as before. Edit the `.cfg` by hand (EFT closed)
if a hidden marker ever has to be changed.

Descriptions (tooltips and `.cfg` comments) still refer to other settings by
their `.cfg` keys (e.g. "Overrides SelectedKit"); those keys remain the stored
identity and are searchable in F12.

## Evidence

- Unit tests: 1179 passed, 2 skipped (12 layout tests; mutants "entry removed"
  and "percent flag on WaveGrowth" fail two of them).
- Client build with all deployment targets suppressed
  (`-p:CustomAfterMicrosoftCommonTargets=tools/AirPitChecks/NoDeploy.targets`).
- AreaConfigChecks, TdmMatchMenuChecks and RaidInfoPanelChecks build unchanged
  (they compile the settings files, which this change leaves as they were).
- In-game check: see HANDOFF.
