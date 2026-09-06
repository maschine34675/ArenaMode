# Arena laser brightness and protected Tactical colors

## Observed cause and installed contracts

The recent reported matches were protected Tactical 5v5. The previous
`ArenaTeamLaserColors.InScope` required `ActiveMode == "teamdeathmatch"` and a
`TeamDeathmatchMode`; Tactical could never enter the effect. The previous harness
explicitly asserted Tactical was excluded. This is a proven scope gap. No evidence
establishes BrightLasers overwriting an Arena team-colored draw.

The installed BrightLasers DLL was read with Mono.Cecil at
`D:/SPT41/BepInEx/plugins/acidphantasm-brightlasers/acidphantasm-brightlasers.dll`,
not inferred just from the reference repository. Its assembly version is 0.0.0.0;
the BepInPlugin/log version is 3.0.0. Installed IL showed:

- Awake prefix writes only `BeamSize` (width).
- CreateLight postfix clones BeamMaterial, adjusts its color and `_Factor`, and
  multiplies `IntensityFactor` and `LightIntensity`.
- LateUpdate postfix writes material `_Color` only with OverrideLaserColor or RGB
  enabled; it never writes a MaterialPropertyBlock. Current reported configuration
  had both options off, original-color shift on and multipliers 2.

EFT `LaserBeam.Awake` builds two meshes and property blocks before CreateLight.
LateUpdate sets distance/intensity/point size, updates the private spot light, then
submits point and beam via DrawMesh with distinct property blocks. It never writes
their color. MPB overrides therefore preserve Arena hue regardless of a later
Bright material recolor. Native shader/material metadata distinguishes visible
`Custom/LaserBeam` / `Custom/LaserPoint` (`_Factor > 0`) from the inspected IR
materials (`_Factor == 0`, `_IkFactor > 0`). No shader source or asset was copied.

The external DLL disappeared from that path during this work, after successful
IL inspection. This worker did not change or remove it. The oracle rechecks the
installed contract and hash if it is present; otherwise it explicitly tests the
reviewed behavior through a modeled replay. A historical hash is not presented
as a fresh installed-file verification.

## Implementation and boundaries

`Effects/VisibleLaserBrightness` defaults to 2, range 1–5. This is a minimum
relative to the original prefab `IntensityFactor`, not a second multiplicative
pass. With a prefab value of 1, Bright intensity 2 and Arena 2 produce 2; Arena 4
produces 4. Setting 1 completely disables Arena's brightness addition and preserves
all external changes. Width, material factor, color shift and LightIntensity
remain external/native choices. The setting does not promise perceptually equal
brightness across hues or a replacement for every BrightLasers option.

A Priority.First Awake prefix observes the original scalar before native
CreateLight and Bright's postfix. It is read-only even in the hideout, preload or
an ordinary raid: native objects are not changed. A ConditionalWeakTable stores
only a float, without owner/world references. Its weak key cannot retain a laser.
Keeping this baseline across raid ends handles prefabs reused from the pool.
Repeated observations never adopt an already modified value; missing/invalid
baselines fail open to the existing native intensity.

All writes require the exact confirmed active Arena GameWorld/AbstractGame pair,
a started game, matching current incomplete mode, live Alive/Ever registry body,
an active visible laser below an active mounted TacticalCombo, and the exact
WeaponPrefab held by that body's current FirearmController. TDM retains its real
mode classifier. Protected Tactical additionally validates the active live round,
bound local body, exact match/game and frozen 4+5 bot bodies/profile identities;
dead bodies, old-round wrappers and unknown actors are rejected. Live bots remain
valid after local death. Solo modes get brightness, without invented team colors.

Draw-local blocks and a temporary scalar are restored in the finalizer, including
native exceptions; shared materials are never written or instantiated by Arena.
Existing external replacements win over stale restoration. Native light output
is deferred until camera rendering, so a small lease recognizes the exact
`block intensity * LightIntensity` output and restores the unboosted value on
cleanup. The private spot light receives tint; flashlights, IR factors/shaders,
activation and selected device modes are never changed. Inactive beams are swept
within 0.25 seconds, and raid/mode/owner/weapon changes release their state.

## Verification and adversarial review

`pwsh -NoProfile -File tools/TeamLaserChecks/Verify-Brightness.ps1` runs installed
assembly/bundle contracts, production-linked policy/service/Tactical classifier
replays and thirteen targeted mutations. It does not build/deploy the game plugin.
The boundary models a DrawMesh submission; it is not Unity rendering or Harmony
execution. TDM's full classifier stays independently covered by TeamGiveUpChecks.

The replay includes the previously excluded Tactical case, exact ten-body teams,
local death, rebind/staging/aftermath rejection, owner/registry aliases and malformed
rosters; brightness alone in solo modes and with tint disabled; Bright 2+Arena 2,
4 and disabled behavior; native/no-hit/failing draw paths; private-light and scalar
restoration; external replacements; shared materials; and 1,000 frames without
additional render blocks. Mutations independently remove normal/IR/held/live body
gates, block/scalar/light restoration, correct brightness composition, Tactical
scope, round phase and physical roster identity, and HDR preservation.

Self-review specifically checked pool reuse, game/match replacement with a stale
Arena flag, baseline capture before Bright modifies creation, native raycast-miss
light handling, repeated intensity/color changes, exception restoration and no
dependency or global configuration mutation. The integrating root runs all tests,
builds and deployment serially and records the observed results; this worker ran
no builds, tests or commits. No GPU raid has been performed here. Required manual
checks are listed in tools/TeamLaserChecks/README.md.

Root verification completed: `Verify-Brightness.ps1` passed 1,191 assertions and
all thirteen targeted mutations failed their intended checks. The actual client
Release build completed with zero errors and four previously known CS8632 warnings
in shared source; client/Core deployment completed. A separate read-only review
by `tdm_exit_review` found no open P0–P2 issue. These tests, build and deployment
were executed by the integrating root, not this implementation worker; the GPU
visual checks remain outstanding.

## License

The reference client includes MIT License, copyright 2025–2026 acidphantasm, in
`../_reference/brightlasers-csharp-master/Client/LICENSE`. This implementation is
independent code against the native EFT/Unity APIs. No BrightLasers code, assets,
binary or configuration are copied, redistributed, bundled or modified.
