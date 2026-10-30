# Reward menu placement — native acceptance, 2026-10-24

Result: passed on SPT-GPU-2 at 1920×1080, SPT 4.1.6 / EFT 0.16.9.5-40743.
This was a menu-only retest. No raid, payout, consent toggle, profile edit,
mod-policy change or server restart was performed.

## Loaded build and isolation

- Client SHA-256: `243A86CF9C39E2E867AE96E2824D026ACD5C39F466A80270A05B105E15AA11FC`.
- EFT instance `fe3cef95`, started 22:40:32 CEST. UiBridge confirmed all 16
  machine mods matched the development build, host disk and loaded process.
- Server stayed at SHA-256
  `FC7FE453A2F8D6EBDC63DEFF1B840C79E304C6A42FCEF033A4998DECC56A7FE4`,
  PID 17624, started 19:49:58 UTC; it was unchanged throughout.
- The game was closed normally through `gpu-run deploy-wait --game`. Because
  Syncthing still held the previous client, the explicitly approved DLL alone
  was copied after quit and its hash checked. The recipe then restarted EFT.

## Native observations

1. Ordinary character selection no longer displayed the full reward card. It
   also remained absent there after selecting ARENA. Normal Arena admission on
   this installation was not blocked, so no compact fallback was expected.
2. The Arena map screen displayed exactly one full reward card at the top right:
   x1264/y40, 600×112 pixels. Its button was x1276/y50, 576×28, interactable and
   hit-tested to itself. The card showed OFF, the ordinary-profile explanation,
   and the current 1,000/10,000/200,000 RUB policy without truncation.
3. The preview retained its y168 position, leaving 16 pixels below the reward
   card. The Mutators heading contained no second reward button. All 22 mutators
   retained their two-column layout.
4. With Factory selected, its thumbnail, native current/past time choices,
   NEXT and bottom MAP/READY navigation remained visible and unoccluded.
   Screenshots were inspected directly. READY was in its normal disabled state
   on this screen; the test did not proceed into raid confirmation.
5. Reward snapshots at 22:42:04, 22:42:25 and 22:42:53 had identical labels,
   details, button bounds, interactability and hit-test results. The server log
   independently records status GETs at approximately ten-second intervals,
   including four between the first and last snapshots. No waiting label or
   disabled switch was observed in these samples. This is sampled evidence,
   not a frame-by-frame flicker measurement.
6. BACK removed the card from character selection; NEXT recreated one card.
   Returning to the main menu, selecting PMC and opening its map screen showed
   neither the reward card nor leaked Arena UI. The final screen is MainMenu.

The two successful scenarios ran 14 steps / five expectations and 19 steps /
seven expectations. The first reopen attempt stopped before its first BACK
click because the runner requires that navigation label to be explicitly
allowed. The rerun allowed only BACK and passed. The failure screenshot in
`05-reopen-pmc` records that runner guard, not an Arena product failure.

Selecting Factory temporarily changed `Team Deathmatch/ExperimentalArea` from
`customs-factory-center-polygon-v2` to `full-map`. The original value was restored
through UiBridge's configuration setter in MainMenu. A comparison of all config
settings against the pre-test copy then found zero differences. Consent stayed
OFF, and its file predates the entire retest. The control lease was released.

The Arena warning scan contained only the existing advisory about PitFireTeam
1.0.0 being newer than the verified baseline; there was no reward-menu error.
The compact fallback under blocked Arena admission, other resolutions and
different consent/error states were not exercised natively in this bounded run.

## Evidence

Ignored evidence is under `artifacts/reward-menu/gpu/`:

- `01-placement/`: loaded identity, ordinary character selection and initial
  Arena map, scenario report and screenshots.
- `02-selected-map.png` and `02-selected-map-t0.json`: selected Factory layout.
- `03-map-poll1.json`, `04-map-poll2.json`, `07-poll-summary.json` and
  `09-status-polls.json`: stable sampled state across actual background polls.
- `06-reopen-pmc/`: successful reopen/PMC scenario and native screenshots.
- `08-final-loaded.json`, `10-arena-warnings.json`, `11-final-cleanup.json`:
  final process, unchanged server, warnings and restored settings.
