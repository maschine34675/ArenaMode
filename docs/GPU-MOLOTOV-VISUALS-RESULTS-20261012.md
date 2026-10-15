# Molotov fire visuals — native review, 2026-10-12

**V3 has a limited visual acceptance for the clear hall/stairwell view.** Its
lower, translucent base leaves the individual flame shapes visible without
V2's excessive yellow overexposure. The close downward capture is obstructed
and remains **unassessed**, not a second visual pass. V2 was rejected and is
retained below as diagnostic evidence. No further combat or acoustic acceptance
is claimed; the earlier dedicated-item report's open gates remain open.

Host 1 (`192.168.178.114`), profile `SPT-GPU-1`, UiBridge
`0.8.0+1f62c6375543487c8c518f40369664029712fd4e`, EFT
`0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`. Root separately managed host 2's
idle restarts for synchronization; this leaf performed no host-2 tests.

## Exact native builds

| Iteration | Client source | SHA-256 | Loaded MVID |
|---|---|---|---|
| V2, rejected | `26b8ff8a5640ed1240a4241814b4e04fd313015c` | `682366C6A26A065207B324CE907C1336B5AD545879DA2C46EAB87B843E245319` | `1f7e5911-f6d2-4ce1-9b18-c010f64aa2df` |
| V3, bounded acceptance | `19c8fb07303e306e3809ae3a7789344245d08ee8` | `65BED593E76F07161712554B18884EC87EEF239DD3CDB03F7E63B30A18F0BAA5` | `fd62569d-bc98-4864-8fab-9b979688183a` |

Only the client was replaced. The unchanged companion and server are from
`4fd79815d6a8dcd15f4d06d4cf5e0d1543d0b998`, SHA-256 respectively
`0C4AC3763CCB170CBD69733AE6765C342136FA9C2CCE8124A2EF4722D95F7AE1` and
`BFDD201F1BF6B8DC2BE868E23780234AA79B8E0DDA970A46B52D560C58676A60`.
Redeploy.Core remained unchanged. Each fresh process reported all 19 plugins
and Core identical across loaded code, host disk and local installation.
Manifests, expected MVIDs and full build-proof reports are in the ignored
`artifacts/molotov-visual-v2/` and `artifacts/molotov-visual-v3/` directories.

The tests used protected Factory Deathmatch, Spartan and two participants.
Each run confirmed the normally supplied exact Molotov template
`a12e00000000000000000001` in the rig beside two ordinary RGD-5s. Native slot 4
binding selected the bottle; all fires came from actual native underhand throws.
Godmode protected setup, with no bot mutations or synthetic damage/kill scoring
claims. The one explicit setup respawn in V3 is documented below.

## V2: broad base overexposed, correction required

Process `aeeb855d01a74331bea1f98992ecf04b`, started 12:11:34 CEST. Server
registration at 10:11:00.478 UTC and `Molotov ready` at 10:11:07.287 UTC
preceded the raid. The finished bottle icon is visible in
`artifacts/molotov-visual-v2/inventory/frame-0000-f5661.png`.

The first, normally supplied bottle ignited at 12:15:18.101: 38 floor cells at
`(70.07, 0.23, -75.33)`, view yaw 180 / pitch 14. A single extra protected-raid
bottle supplied the second throw, ignition 12:17:34.578: 45 cells at
`(70.03, 0.23, -73.56)`, pitch 31. Both post-expiry inspector reads found
`ArenaFireRuntime.Fires.Count == 0`.

Both views show a much broader base, but overlapping yellow patches overwhelm
the thinner tongues. Some patch edges appear rectangular. The testing leaf and
parent independently rejected this appearance; the cause of those apparent
edges was not established from pixels alone.

Evidence under `artifacts/molotov-visual-v2/`:

- `shallow/`: 250 original frames 12142–12391, `shallow.mp4`, 2.634 seconds.
- `down/`: 250 original frames 26300–26549, `down.mp4`, 2.937 seconds.
- Representative fire: `shallow/frame-0190-f12332.png` and
  `down/frame-0190-f26490.png`.
- Full logs: `LogOutput-r1.log`, `server-r1-complete.log`; bridge export
  `client-r1.json` is supplementary, with one truncated entry.

## V3: accepted hall view, close view remains unassessed

Process `e33297e3398c4e0cb994607018a7df98`, started 12:21:55 CEST. Server
registration at 10:21:21.353 UTC and `Molotov ready` at 10:21:28.174 UTC
preceded the raid. V3 changes only base height, color and transparency; damage,
runtime, audio, flame tongues and emission cadence were not changed.

The initial native spawn was a narrow furnished room. A first real throw
ignited only three cells at `(34.86, 0.95, -32.36)` at 12:25:03.041. This is
setup evidence, not a useful broad-floor comparison (`setup-throw/`). A
movement request to another point failed the reachability check; no check was
bypassed. One authorized setup `kill` hook caused the usual Arena respawn,
which supplied a fresh native kit. This deliberate setup death, visible as one
death on the HUD, is not fire-damage evidence. Godmode was re-enabled afterwards.

From the new spawn, UiBridge successfully moved the player to
`(5.1, 0.0, -21.3)` after checking NavMesh, danger zones, full reachable path and
standing room. The fresh kit's Molotov (`6accb61a6f0d466683005c13`) was selected
natively and thrown. Ignition at **12:29:51.461** produced **32 cells** on the
visible lower hall/stairwell floor at `(13.24, -2.67, -21.41)`, viewed at yaw 90 /
pitch 14. Individual curved flames, varying heights and the wider warm base
remain readable. No obvious rectangular sprite outlines are visible in the
reviewed frames 100, 130 and 249. Both parent and testing leaf inspected the
original images and accepted this view as a limited prototype visual result.

This is **not a strict A/B comparison**: V2 was captured closer to a different
surface and background. It does not certify every floor, light condition,
camera angle, multiple concurrent fires or rendering cost.

A final extra protected-raid bottle was thrown at yaw 0 / pitch 31. It ignited
19 cells at `(5.51, -0.16, -20.12)` at 12:32:43.737, close to the player and
railing. Fire is mostly below the image edge and native contact blur/blood
effects obstruct the view despite godmode retaining 440 HP. This downward
capture is **not a second visual pass**. No further throws were made. Including
one unused bottle before the setup respawn, V3 used two temporary `giveItem`
insertions; neither substitutes for the two observed native kit supplies.

After the final fire expired, read-only inspection found the runtime fire list
empty. This confirms that runtime burn entry was retired; it is not an exhaustive
Unity-resource leak or performance test.

Evidence under `artifacts/molotov-visual-v3/`:

- `open-shallow/`: 250 original frames 35180–35429;
  `open-shallow.mp4`, 3.355 seconds. Key frame `frame-0130-f35310.png`, also
  preserved as `open-preview.png` for parent review.
- `open-down/`: 250 original frames 48768–49017;
  `open-down.mp4`, 3.647 seconds. Obstructed key frame
  `frame-0180-f48948.png`, also `down-preview.png`.
- `LogOutput.log`, `server-complete.log`, `client.json`, `build-proof/`.

Every capture retains its `complete=false, reason=frame_limit` metadata; each
listed set contains all 250 encoded PNGs. Clips preserve recorded frame timing
and contain no audio. They do not show the complete six-second fire lifetime.
No generated or reconstructed frames were used. The complete native log has no
incendiary failure; the microphone initialization error and installed deliberate
RespawnApiProbe subscriber warnings remain recorded, not dismissed as a
zero-error installation.

## Restore and full profile audit

Both iterations used fresh pre-login snapshots of the complete profile,
63 configuration files, 33 existing PFT files and 84 DLL hashes. No profile JSON
was edited or restored. Protected `--no-heal` exit completed in 39 seconds each.
All five temporary settings restored without conflicts or missing values;
GameMode remained the original deathmatch. MainMenu status confirmed godmode
off, with no walk or tracking active. All captures finished encoding before
shutdown. EFT quit gracefully and the server stopped normally through Ctrl-C.

V2 full post-login → protected exit / logout comparisons differ only in
Hideout timestamp `1791799927` → `1791800347`.
V3 full post-login → protected exit differs only in Hideout timestamp
`1791800548` → `1791801268`; after logout it is `1791801328`. The parent
independently checked the V3 diff. No other profile field changed, including
full inventory and HP. Before-login → login also advances native Health.UpdateTime;
that separate timestamp change remains recorded rather than silently excluded.

All **422 inventory IDs** and all **33 PFT files** are preserved in each final
audit, with **zero configuration differences**. V2's DLL audit differs only in
the intended client from its pre-deployment baseline. V3's baseline was taken
after deployment but before login, and all 84 hashes remain identical throughout
its native run. Evidence: V2 `r1-post-login-diff.json`, `logout-r1/audit.json`;
V3 `post-login-diff.json`, `protected-exit/audit.json`, `logout/audit.json`.
Private profile snapshots and original captures remain local, not committed.
