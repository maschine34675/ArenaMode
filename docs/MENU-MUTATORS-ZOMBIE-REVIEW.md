# Map menu: Molotov, mode order and Zombie sliders — 2026-10-12

## Contract

- Mode order: Gun Game, Deathmatch, Team DM, Tactical, Zombies. IDs, aliases,
  saved selections and the default Deathmatch fallback do not change.
- Tactical, Zombies, Akimbo and Molotov carry a `(WIP)` suffix. Tactical still
  identifies solo 1vN or 5v5 PFT; Zombies still identifies the PFT squad.
- Two additional mutators bring the total to 22, eleven per column:
  **MOLOTOV (WIP)** uses the existing incendiary setting; **FAST BOTS** exposes
  the existing bot movement master. No duplicate preference or new gameplay
  implementation is introduced. Molotov defaults on for fresh settings; saved
  opt-outs remain intact. Both are unavailable in Zombies, matching their
  existing runtime restrictions. Molotov also requires the active matching
  Arena BigBrain companion, rather than merely an installed file.
- Zombie mode exposes integer sliders backed by the original F12 preferences:
  MaxAlive 4–56 (default 20), WaveStartSize 1–200 (default 30), WaveGrowth 0–100
  (default 10). Labels and current values appear above each slider. The ranges
  come from the existing ConfigEntry constraints, not duplicated menu constants.
- Settings apply to the next match through the existing Zombie director capture.
  Hidden, retired, armed and active views cannot edit these preferences. Refresh
  does not save or turn a native bound/value event into a selection. Ordinary
  PMC/Scav screens and game behavior remain unchanged.

## Layout and ownership

The right-hand preview shrinks from 320 to 292 logical pixels. Its title and
thumbnail/Customs polygon remain visible; the 500-pixel mutator panel starts at
480 and still ends at 980, above native READY. Rows retain their 22-pixel buttons
and separate 18-pixel descriptions.

Zombies uses the 596-pixel mode panel, with three 50-pixel slider rows below the
common facts. Area selection starts below the actual selected mode even with
PFT present; treating every PFT choice as Tactical would overlap the sliders.
The native Slider owns fill/handle anchors, while its transparent full-height
hit target accepts clicks/drags. New listeners and objects are released on Close.

## Verification and limits

The production-linked menu suite passes 2,320 assertions and 35 compiled negative
controls for all mappings, 22 controls, suffixes, order, limits, callbacks,
persistence and armed/live locks. Mode selection passes 158 checks; related
suites cover aliases (35), area layout (34), participants (3,730) and previews (4,422).
Molotov passes 60 assertions and eight compiled negative controls, preserving
runtime ownership/cleanup and verifying fresh-on, saved-off and Zombie exclusion.
The client and server staged successfully without altering installed binaries or
configs. Independent review found the PFT area overlap described
above; it was corrected and given a regression assertion.

The clean source commit is `a4ebe18c31386bd6da46f6c60e73326e3aa71cf2`.
Staged client evidence: `artifacts/double-jump-build/41442346e5924a3aaa810faa62f77d1d/manifest.json`;
client SHA256 `EB3939749E4701264090BEDF67B85163A84D7643E112693B757E1988BA2AB4DB`.
Only this client DLL was deployed locally; GPU1's disk and fresh loaded process
were verified identical. Server, Core and companion were not rebuilt/deployed
as part of delivery. The server was only compiled in the earlier isolated stage.

Native menu testing at 1920x1080 is recorded in
[GPU results](GPU-MENU-POLISH-RESULTS-20261012.md): real clicks, all slider endpoints,
save/reopen and repeated mode changes. A suspected missing-glyph defect was
withdrawn after inspecting lossless crops of the unchanged source screenshots;
the full-frame image preview was misleading. No speculative material/mesh fix
was added. Native PFT-party layout and other resolutions remain outside this run;
the PFT geometry/selection regression is covered offline.

A menu test does not recertify Molotov combat or Zombie performance at the maximum
56 active bots. No raid was entered or claimed for this menu-only follow-up.
