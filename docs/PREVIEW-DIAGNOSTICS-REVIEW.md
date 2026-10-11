# Stationary equipment in Item Overlook

User evidence: `../Screenshot-2026-09-06-192735.png`, confirmed **after an Arena
match in the menu/Hideout**. Several helmets, armor and a weapon overlap the
selected NeoSteel helmet; foreign gear does not rotate with the selection.

## Findings and limits

The installed native `WeaponPreview.CG_SetupItemPreview.method_1` creates one
clean item prefab, parents it under its preview Rotator and assigns the Weapon
Preview layer. Rotate changes only that hierarchy. Its camera comes from a
serialized prefab; the screenshot does not establish which camera drew the
foreign objects. Item Overlook shares the weapon modding pipeline.

Native `PlayerBody.SlotView.Dispose` unskins and returns equipped models before
its body is destroyed. `Player.OnDestroy` calls `Dispose`, including PlayerBody
cleanup for a living retired body without a corpse. Regular pooled helmets are
deactivated; active weapon pool objects are moved to Y=-9999. These contracts do
not establish an Arena cleanup bug from the screenshot alone.

KillcamReplay's renderer restoration lacks ownership/reuse checks, and a failed
GhostPuppet constructor lacks complete holder cleanup. Independent source/installed
DLL inspection confirmed those risks, but neither establishes the missing bridge
to stationary foreign gear in this preview. No KillcamReplay/Redeploy changes.

## Diagnostic only

`ArenaPreviewDiagnostics` observes the native completed preview one frame later,
after verifying the same item, original model, preview, game and world. It runs
only in the exact active Arena or menu/Hideout after Arena. All other games,
including a normal LocalGame before raid start, are rejected. The menu exception
only reads diagnostic state; it does not extend any Arena gameplay feature.

At most once per ten seconds it enumerates active renderers, logs a summary and
up to 16 foreign candidates nearest the preview camera. It includes renderer
hierarchy/instance IDs, layer, bounds, visibility flags, nearest pool object and
pool state, parent player and skin root. Camera masks/positions/targets and the
current player's headwear/face cover distinguish preview contamination from
first-person gear visible through the background camera. Frustum candidates are
not confirmed visible pixels; occlusion, materials and special render passes are
not inferred. Counts show when detail output is truncated. No inventory/profile
serialization, network request, object destruction, pool return, reparenting or
renderer writes. Native object names and instance IDs appear in diagnostic paths.

Opening other items within ten seconds can be throttled. To reproduce, restart
the game, play an Arena match and reopen the affected Item Overlook afterwards.
Leave the preview open briefly. If necessary wait ten seconds and reopen it.
Search the client log for `Arena preview probe:`. Both a clean post-match preview
and the affected preview are useful; no speculative scene repair has been applied.

## Validation

- Installed-assembly contract check: completion creates, positions and assigns
  the preview layer before the diagnostic callback. No Unity runtime execution.
- Release client build and deployed client/Redeploy.Core SHA-256 comparison.
- Independent adversarial review of lifecycle, isolation and read-only behavior.
- Runtime output and a causal diagnosis still require the user's GPU reproduction.
  This is an investigation build, not a confirmed rendering fix.

The independent review found no P0-P2 issue. Its P3 robustness suggestion was
applied: use `SlotViews.TryGetByKey` so an already disposed main body with an empty
slot dictionary does not interrupt the remaining diagnostic rows. The outer
exception boundary still preserves the native preview on any diagnostic failure.

Final local verification (2026-09-06): installed contract and final Release build
re-run after that correction, both successful. Built and deployed hashes match:

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client | `D46FEA126844DE851B630231D12D5970489BFBC6BEE212A955C2672A97BC37A5` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

Server and other mod projects were not rebuilt. Final independent re-review
confirmed the disposed-slot correction; no findings remain. Runtime reproduction
is explicitly outstanding; the current client log contains no affected preview
with these diagnostics yet.
