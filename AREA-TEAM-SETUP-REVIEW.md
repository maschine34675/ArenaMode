# Customs team starts and selectable team sizes

## Current additions (2026-09-13)

Factory Side (formerly displayed as Factory East) now uses the same opposing
team-start and enemy-aware respawn framework at 2v2–5v5. Its stable ID remains
`customs-factory-east-polygon-v1`; both client and server require an explicit
team size. USEC has five alternative PMC/eight bot starts on the map-left side;
BEAR has five PMC/six bot starts on the map-right side. Installed native marker
survey gives at least 28.876m opposing spacing and 1.885/1.710m simultaneous
same-team spacing, without changing the existing 25m/1.5m requirements. These
are candidate points, not proof of scene NavMesh connectivity or balanced cover.
The exact surveyed contour and unlimited height are unchanged.

The old Center rectangle and Factory South are retired. Center contour remains
fixed 2v2; Center team starts remains selectable 2v2–5v5. See
`AREA-MAP-PREVIEW-REVIEW.md` for name/config migration and
`tools/CustomsEastAreaChecks/README.md` for the marker provenance and checks.
The earlier evidence below belongs to the original Center implementation.

## Original Center implementation

2026-09-08, feature/area-team-setup, based on62d610e. User authorized scoped optic preload protection, two opposing start sectors, enemy-aware respawns and native2v2–5v5 selection. CTF and an area editor remain later work.

## Player-facing behavior

Choose Arena → Team Deathmatch → Customs → Customs contour (team starts), then2v2,3v3,4v4 or5v5. Default is2v2. The previous Factory Center rectangle and surveyed contour remain separate fixed2v2 choices. Other maps, full-map team settings, normal raids and PFT Tactical retain their previous paths.

USEC initially starts on the western side, BEAR on the eastern side, regardless of the player's faction. Exact surveyed PMC/bot IDs define the starts; no internal wall separates the sectors. Each side has five bot-capable markers, with two western and five eastern alternative PMC markers. Closest opposing pair is35.063m; the closest western bot pair is2.031m. These are broad sectors (roughly85–89m span), not compact formations or a claim of balanced cover. The measured outer contour, unlimited height semantics and exact sniper exclusion are unchanged.

After the first complete roster is active, replacements use all connected eligible points inside the outer contour. They reject live-body/pending-reservation overlap and maximize the minimum horizontal distance to all live enemies, then use distance from the old body as a tie-breaker for the local player. This is distance-based safety, not line-of-sight/cover scoring or a guarantee of a fixed enemy-free radius. It does not override SAIN/PFT AI movement.

## Contracts and failure behavior

The new versioned preset is customs-factory-center-teams-v1. Its MaxTeamSize5 is capacity; nullable request areaTeamSize is the required explicit selected count2..5. Server validation precedes snapshot preparation, freezes the value under the exact arm token and rejects replay drift. Both success replies echo the exact integer, and the client refuses missing/coerced/different replies while retaining the cancellation identity. Legacy/full-map requests use no override (null/0), so old behavior cannot silently expand.

Static capacities are checked on the per-raid location clone and again on the exact final response before native release. In scene, the initial PMC hook owns only the exact loading game/world/filtered collection, category, profile ID and faction. Readiness verifies enclosure integrity, carving, complete in-area paths, native dispatchable zone membership, each side's PMC and selected number of bot points, simultaneous1.5m spacing and25m opposing spacing. Insufficient live capacity aborts with a specific reason; there is no smaller-team or out-of-area fallback.

Bot requests replan after asynchronous profile loading. Exactly one reserved point goes to one faction profile. Token/object identity and a30-second expiry protect pending positions; stale/wrong/displaced/outside/overlapping arrivals are retired before roster admission. Initial faction starts no longer inherit the legacy25m distance exclusion from the local player, which could otherwise block friendly starts. Later positions remain enemy-aware. Failed preflight dispatches defer through TDM's existing bounded retry timing.

Player respawns reserve their chosen PMC marker before Redeploy's body/profile handover, retain the exact position rather than drawing again, and recheck lease/occupancy/navigation at final release. The exact replacement-body callback releases the reservation. An unavailable or changed player position fails closed through the existing protected abort path; arbitrary native fallback is never used. In-place kit swaps retain their existing fixed-position behavior.

## Review and checks

Separate evidence documents describe the survey, server, menu, runtime and optic scopes. Review found and corrected delayed-arrival reservation retention, pending-ledger cleanup, displaced-body acceptance and the preflight/activation player-spawn overlap race. Native source verified BotPmc hydration into BotZone.SpawnPoints and unchanged position delivery to Player.Create. Real client compilation checks public native API access through IBotGame. The optic fix is a separate commit2407351; it does not swallow camera exceptions globally.

Final integrated results and delivery hashes are recorded below after verification. Standalone production-linked tests use explicit game/Unity substitutes for engine behavior; no automated GPU raid has been performed.

## User GPU acceptance

Restart game and server after installing both DLLs. Start the new Customs option at2v2, then5v5;3v3/4v4 are also offered. Confirm the initial player/friendly and opposing groups use their assigned sides, the roster reaches the exact selected size, and several deaths/give-ups refill within the contour without repeated activation failures. Repeat with the other PMC faction if practical. Check native selection at the actual display resolution, including switching area/map/mode and back. A legacy contour2v2 and ordinary PMC start remain regression checks.

Leave background kit preloading running while using an optic, then respawn/switch kits and verify ADS. Watch for the narrow optic-guard diagnostic and new camera exceptions. Distance-only respawn ranking and wide start sectors should be assessed for gameplay fairness before using this setup as a basis for objectives such as CTF.

## Integrated verification (root)

The final integration command passed on2026-09-08:444/444 server tests, no skips;337 production-linked team-runtime assertions with15 named source faults;397 native-menu,71 native-hook and21 team-view assertions with4 faults;233 actual TDM mode/relations checks,13 prepared-respawn adapter checks, enclosure regressions, existing spawn/give-up/capture/scope checks and their area/polygon negative controls. Separately,29 native side-view checks passed. The optic commit was reverified with67 assertions and11 source faults. Independent server/shared, menu/transport and runtime reviews have no unresolvedP0–P2 findings.

Both Release DLLs compiled in isolated staging and their staged/build hashes matched. Installed client, server and Redeploy.Core bytes remained unchanged during verification. Existing nullable annotations/server warnings were not hidden. Delivery and the code commit used for the final build are recorded in HANDOFF.md; no user GPU raid is claimed for this update.
