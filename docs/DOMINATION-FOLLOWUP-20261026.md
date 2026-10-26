# Domination feedback follow-up — 2026-10-26

This follows the [initial prototype](DOMINATION-PROTOTYPE-20261026.md). Capture
and match rules, point locations, ordinary TDM and persistent reward exclusions
are unchanged. There is no public version bump.

## Changes

- The map screen shares eligibility within one display pass instead of repeating
  costly body-compatibility checks for every row. Native click/final-start gates
  remain fresh. GPU-2 improved from 14 to 56 average FPS, with P95 frame time
  reduced from 228.79 to 27.27 ms in matched ten-second idle windows. This is not
  a locked-60-FPS claim. [Cause, tests and measurements](MAP-MENU-PERFORMANCE-20261026.md).
- Pickup notices and active effects occupy their own row below A/B/C, with a
  16-unit gap in the UI layout. Both panels use one visibility/position calculation; offset
  changes move them together even during the powerup text refresh throttle.
- Pickup beams stay visible. Placement rejects positions within six horizontal
  metres of any control point when the height difference is at most two metres.
  Original candidates and repaired floors are checked during initial placement,
  rotation, cooldown rearm and reciprocal position exchanges. Unprepared or
  disposed objective geometry cannot authorize a pickup.
- Three of the eight authored Factory slots coincide with control points. For
  built-in Domination layouts only, up to 16 distinct candidates now combine
  remaining authored slots with filtered native bot-spawn positions. Existing
  floor, navigation, obstacle and pickup-spacing checks still decide placement.
  Custom files are not expanded. The navigation anchor still uses the original
  Factory seeds and is cleared when the match resets.
- Ground rings set both LineRenderer endpoint colors explicitly. They show
  neutral grey, USEC blue, BEAR red or contested yellow. The normal sprite shader
  uses a white material to avoid multiplying the tint twice; the unlit fallback
  receives material color as well. Ring width increases from 7 to 12 cm. Scene
  depth remains enabled. World rings use darker, saturated colors separately
  from the bright HUD/label palette: the first native check still showed white
  rings with correct CPU-side pastel colors, which the offline renderer doubles
  cannot detect. The subsequent native report distinguishes those two candidates.

## Capture speed

Additional players/bots do not accelerate capture. A neutral point takes five
seconds, and an enemy point takes five seconds to neutralize plus five seconds
to capture. Any opposing occupancy contests the point and freezes progress.
The implementation continues to test whether each side has at least one valid
occupant; it does not scale elapsed progress by the count.

## Verification

The real client and companion built with `NoDeploy.targets` and a sentinel
server destination: zero errors, four existing nullable-context warnings.
Installed files stayed unchanged during offline checks; deployment was explicit.

| Check | Result |
|---|---|
| Shared unit suite | 3,213 passed; one existing APBS-version-specific skip |
| Native menu / native area hooks | 876 / 202 assertions |
| Original TDM and Domination lifecycle | 439 assertions |
| Announcements | 60 assertions + 9 rejected source mutants |
| Menu refresh / final admission | 13 assertions + 4 mutants |
| Actual HUD views and layout | 677 assertions + 9 mutants |
| Objective-aware pickup placement | 37 assertions + 12 mutants |
| Pickup view / ownership rings, final world palette | 79 assertions + 11 mutants |
| Existing pickup regression | 513 assertions |

Independent source reviews covered menu admission and world placement/lifetime.
Parent rechecks ran the menu/HUD mutation gates and placement/view baselines.
Native shader output, resolved pickup positions and actual collection are
separate evidence in the [GPU follow-up](GPU-DOMINATION-FOLLOWUP-20261026.md).

The native follow-up observed all six pickup types and 24 live rotations across
36 placement/rotation records, with no objective-volume violations. A parent
calculation from the recorded floors independently found the same result (the
smallest observed horizontal distance was 13.083 m). Actual Jump Boots collection
showed its notice and timed effect below A/B/C without overlap; available pickup
beacons retained their colors. The final palette passed matched views of point B
before and after normal capture: neutral grey, BEAR red and USEC blue. Contested
yellow is covered by the renderer-input checks, not a separate final screenshot.

Those tests used Factory day. The map-menu measurement and pickup/HUD observations
used the first candidate; the final candidate changed only the ring/label color
selection. Night rendering and broader placement balance are not newly certified
by this bounded follow-up. The GPU report also records an isolated native medical
callback exception at a match end, without claiming its causal origin is known.

## Deployment

Both client assemblies were explicitly deployed and EFT restarted on GPU-2.
GPU-run verified all 16 tracked host/GPU/loaded identities. Server, Core and map
assets are unchanged. The exact initial Arena config was saved before testing;
the native report records cleanup and final host state.

| Assembly | SHA-256 |
|---|---|
| Client | `AAD46D00745FFEF666E286267D7A10E61CCB1D55ADC5A1D5BDCE1444FB0E9870` |
| BigBrain companion | `7D06BAE14BA8E5D5AB8DFE8900046E682A2B5733DA68A012DAD8B7EF6A03D4CC` |
| Server (unchanged) | `12127A1B6F61801F939E26EBAE37D39149772B00AE3FE62EA45532349D9C7A8F` |

Raw evidence lives under `artifacts/domination-followup-20261026/` and can contain
private profile details. Do not publish it wholesale. Pickup distribution and
objective placement balance still need normal gameplay feedback.
