# Native character selection row — 2026-09-07

Historical review of the initial row alignment (`328665d`). The subsequent own-PMC
preview also moves the complete native columns outward; its implementation and
separate GPU evidence are documented in [CHARACTER-PREVIEW-REVIEW.md](CHARACTER-PREVIEW-REVIEW.md).

Arena's existing third toggle is aligned vertically with SCAV and PMC, below
the two character previews. A small neutral `SEPARATE MATCH` caption uses the
native font and description tint. The selected style, hover, sounds and input
remain owned by EFT's original toggle.

## Implementation and contracts

- `ArenaSideSelectionLayout` measures the existing Arena and PMC header rectangles
  in the screen's coordinate space and moves only Arena's exclusive wrapper on Y.
  Character previews, large portrait click targets, Scav cooldown and PMC health
  UI remain unchanged. The original wrapper position is recorded before mutation.
- The caption is a noninteractive child of the existing Arena spawner, anchored
  below its final bounds with `LayoutElement.ignoreLayout`. This avoids relying on
  TMP bounds before native layout completes. AlphaBlocker's one-child ownership
  contract stays intact, and no native toggle is cloned or respawned.
- A per-screen layout owner restores on the next Show, Close, or Show failure.
  Restoration is idempotent and hides the caption before deferred destruction.
  The PFT squad-management path restores before its guard and adds no Arena layout.
- The native `MatchMakerSideSelectionScreen`, `UIAnimatedToggleSpawner`,
  `UISpawnableToggle`, `UISpawner` and PFT Show/Close contracts were checked locally.
  This scene uses `UIAnimatedToggleSpawner`, not the account-faction
  `SideSelectionToggle`. The checked decompile exposes `UIElement.AddDisposable`,
  but the actual reference assembly does not; the implementation uses the verified
  native Close patch instead. The deployed client compiled against that assembly.
- No new selection handler, settings write or raid-start behavior is introduced.

## Validation

- 29 existing native-side helper assertions and 53 menu-flow assertions passed.
  They cover ownership, input eligibility, identity, normal-route restoration and
  flow guards. They do not execute the new Unity layout or prove its geometry.
- Independent adversarial review found no actionable functional issue in the
  final layout/Show/Close changes.
- Release build succeeded with zero errors and four existing CS8632 warnings in
  the unchanged shared settings model. Redeploy.Core was rebuilt from unchanged
  source; the Arena server was not rebuilt.
- Final GPU menu run: SCAV/ARENA/PMC align on one row, both Arena and PMC captions
  fit above the descriptions, and the longer Arena copy does not cover its caption.
  PMC and SCAV label selection, the SCAV/Arena map routes and repeated Back passed.
  Native MY SQUAD with the existing empty roster showed no Arena toggle/caption;
  reopening character selection afterwards restored exactly one aligned caption.
  Nine unmodified 1920 x 945 captures record the final build.
- Final client log contains no Arena warning/error or client Error-level entry.
  Installed client SHA-256 matches Release output:
  `11A71C665D07E12EA736152319988DD32D524B335553FC41129EB7C7D382FA23`.
- No raid was started or Squad configuration changed. A populated PFT team view,
  other resolutions and gameplay remain outside this check; the user performs
  actual raid tests.

Private screenshots/report:
`C:\Users\Administrator\Documents\Codex\2026-09-07\lies-agents-md-und-handoff-md\outputs\Arena-Charakterauswahl\`.
Build, checks and run logs: sibling `work\arena-character-row\`.
