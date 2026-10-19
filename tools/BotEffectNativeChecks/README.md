# Bounded native bot-effect fixture (temporary, not shipped)

This standalone BepInEx tool is for the **SPT-GPU-2 test profile only**. It creates
no patches and has no deployment target, production project reference or payload
membership. The parent owns all deployment and GPU actions; building this folder
does not permit installing or running it on a host another session is using.

```powershell
dotnet build tools/BotEffectNativeChecks/BotEffectNativeChecks.csproj -c Release
```

The only output to install temporarily is
`bin/Release/netstandard2.1/maschine-BotEffectNativeChecks.dll`, in a dedicated
`BepInEx/plugins/maschine-BotEffectNativeChecks/` folder. Record its SHA-256/MVID.
Do not copy EFT/BepInEx dependencies. Start/restart only through UiBridge's
`gpu-run` recipe; verify loaded identities before using the fixture.

The tool remains inert unless Windows machine name **and** local nickname are
`SPT-GPU-2`. Every command except owned-cover cleanup requires the exact local
profile ID, current started `LocalGame`/`GameWorld` accepted by Arena, and only
one registered human. Only Arena DM/TDM are admitted, and both UiBridge action
switches must already be on. No settings file is created. The canonical test
profile is currently `6ab7db1c64f04b565cf78be9`; verify it through UiBridge and the
parent's profile manifest, not through a nickname-matching sync-conflict file.

The coordinator must hold the UiBridge control lease throughout fixture work.
This file-command fixture does not acquire or bypass that lease; its separate
commands must never be used during another session's host ownership.

## Commands

`Command.ps1` writes one fresh request by atomic rename under the host's
`C:\Temp\ArenaBotEffectNativeChecks` folder (outside the synchronized install).
It prints an ID, **not an action result**. Wait via UiBridge, then run the script
separately with `-ReadId <ID>`; optional `-SaveAs` preserves the JSON under the
task evidence folder. Never combine an action and its verification in one tool
call. Use `-GpuHost 192.168.178.109` if SMB name resolution is unavailable.

```powershell
# Discover current bodies/effects; no automatic target selection.
./tools/BotEffectNativeChecks/Command.ps1 -Action status -ProfileId 6ab7db1c64f04b565cf78be9
# After a separate UiBridge wait:
./tools/BotEffectNativeChecks/Command.ps1 -ReadId '<printed 32-character ID>'

# Replace BOT with the exact current live profile ID from that snapshot.
./tools/BotEffectNativeChecks/Command.ps1 -Action observe -ProfileId 6ab7db1c64f04b565cf78be9 -BotId BOT
./tools/BotEffectNativeChecks/Command.ps1 -Action buff -ProfileId 6ab7db1c64f04b565cf78be9 -BotId BOT -Effect both -Seconds 15
./tools/BotEffectNativeChecks/Command.ps1 -Action cover -ProfileId 6ab7db1c64f04b565cf78be9 -BotId BOT -Cover full -Seconds 15
./tools/BotEffectNativeChecks/Command.ps1 -Action cover -ProfileId 6ab7db1c64f04b565cf78be9 -BotId BOT -Cover lower-half -Seconds 15
./tools/BotEffectNativeChecks/Command.ps1 -Action clear -ProfileId 6ab7db1c64f04b565cf78be9
```

Available effects: `shield`, `damage`, `both`. Cover: `full`, `lower-half`, `none`.
At most 80 requests per exact raid, input at most 4096 bytes, request age at most
20 seconds, and every duration at most 15 seconds. Old request files are moved
aside at plugin startup and malformed requests are consumed without retries.

**Injected buffs are real short-lived production buffs, not a fake contour.**
They use `ArenaPowerups.GrantShield/GrantDamage`; the target must pass the existing
`ArenaPickupRuntime.IsLiveActor` and exact active body/health registration gates.
The tool refuses an already buffed bot instead of replacing its natural state.
It never calls Reset/Forget or rewrites timers. Accepted grants expire naturally
within 15 seconds (or clear through normal death/raid teardown), even if a later
part of a `both` grant fails. A later natural pickup may legitimately extend the
effect; the fixture never erases that state. A refused request is **not** evidence
that the production renderer failed.

Cover is a new private opaque `Standard` cube with a private neutral material;
its collider is immediately disabled and destroyed. No collision, AI path,
camera transform, bot transform, native mesh/material or health coefficient is
modified. The cube follows the target's real projected renderer bounds at camera
pre-cull and sits strictly between the current camera and the nearest body corner.
`full` adds a 15% margin; `lower-half` covers the lower half. Targets must fit fully
inside the viewport and not intersect the camera. Missing opaque shader, invalid
bounds, a changed camera/body/health/game/world, death or expiry removes the cube.
The cube may still affect lighting/postprocessing as any opaque test surface;
**a collider-free test cover is not natural map cover acceptance**.

## Observations and limits

Snapshots include real body/health IDs, native renderer/material IDs, projected
corners, effect timers, TDM protection readback and frozen mode shield duration,
camera/screen/scaled dimensions, rendering path and target texture,
the owned cover's shader/depth-write/corners, and the production contour owner's
camera, buffer bytes, effect flags and native source list if that implementation
exists. The current global `_CameraDepthTexture` and its texel-size vector are
included read-only to diagnose a fail-closed empty-depth contour. A command
snapshot runs in Update and can see the last published render frame's global
texture; it is not a GPU readback or a guarantee about this frame's bindings.
The `LegacyNamedRingCount` is an exact old-name heuristic, not an exhaustive
scene census. Reflection diagnostics cannot certify correct pixels. Failed optional
introspection is not silently converted into success.

Capture actual frames through UiBridge (`gpu-run still` or `uibridge_capture`).
Inspect full/partial cover in those frames; do not infer occlusion from linecasts
(the deliberate cube has no collider), `Renderer.isVisible`, command-buffer byte
counts, a zero ring count or projected bounds. Projection metadata is normalized
camera-space; validate the displayed position when DLSS changes the viewport.
Natural gameplay positive controls and the native test matrix live in
`docs/GPU-BOT-EFFECTS-20261019.md`.

## Cleanup

Send `clear` while the owning process is reachable. Wait out any injected buff or
leave the protected Arena raid, then verify native teardown. Exit EFT, preserve
the task's response JSON/screenshots/logs, and remove **only the installed temporary
DLL/folder you created**, after verifying its absolute path. Do not recursively
delete a computed/shared folder. Keep evidence under the task artifact root.
Restore only settings changed by this test from the parent's ledger; do not
overwrite all synchronized config files or another session's profile changes.
Remove no other plugin and do not weaken normal-raid gates to get test admission.

