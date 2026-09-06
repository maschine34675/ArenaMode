# Mounted Arena laser verification

Run `pwsh -NoProfile -File tools/TeamLaserChecks/Verify-Brightness.ps1` from the repository.
It runs installed contracts, production-linked draw replays and targeted mutations.
Contracts require PowerShell 7, installed EFT assemblies/bundles and Python with
UnityPy; only metadata is read, no extracted assets are packaged. The harness
requires .NET 10. It never builds or deploys the game plugin.

The executable links the actual color service, hue/restore policy, brightness
service, Tactical laser classifier and tracer palette with a modeled Unity/EFT
boundary. It tests TDM affiliation
as a collaborator supplied by `TeamDeathmatchMode.TryClassifyCurrentBody`; that
method's exact native body/roster validation is covered by TeamGiveUpChecks,
which links the real mode and ArenaMatch.
Checks execute actual Begin/End/Tick/Reset, original-block reference restoration,
per-component allocation reuse, visible/IR/unknown shader gates, current weapon
ownership, team hue, alpha/HDR preservation, disabled/pool/owner-change cleanup,
and native/third-party replacement precedence. It additionally executes the real
Tactical classifier against exact ten-body fixtures, deceased local players,
rebinds, replacements and malformed rosters. Thirteen scratch-source mutations
must fail their specific intended assertions; production files are never altered.
Brightness checks cover native/Bright baselines, settings 1/2/4, invalid values,
no compounding, 1,000 frames with no new blocks, missing baselines, active/held/live
ownership, exceptions before/after light writes, raycast misses, deferred-light
restoration and external scalar changes.

## Native contracts and installed compatibility

Verified in `../_decompiled/LaserBeam.cs`, `TacticalComboVisualController.cs`,
`WeaponPrefab.cs`, `EFT/Player.cs`, installed Assembly-CSharp metadata and bundles:

- `WeaponPrefab.Init(IPlayer,bool)` stores `_player`; an exact currently wielded
  `FirearmController.ControllerGameObject.GetComponent<WeaponPrefab>()` must match.
  The active laser must also be below a mounted, active TacticalCombo controller.
- `LaserBeam.LateUpdate` draws beam and dot with separate property blocks. Every
  float consumed by its draws is written by that invocation. Arena's reused blocks
  contain the team `_Color` plus those native writes. A finalizer restores the exact
  original block references even if drawing throws. Only the draw's temporary
  `IntensityFactor` can increase, and it is restored in that same finalizer.
  Materials, keywords, shaders, modes, width, `LightIntensity` and activation are
  never changed.
- Native `Custom/LaserBeam` / `Custom/LaserPoint` materials use `_Factor > 0` for
  visible light. Inspected Holosun LS321, AN/PEQ-15 and Klesch IR materials instead
  have `_Factor = 0` and `_IkFactor > 0`; either invisible material makes the whole
  beam stay native. Unknown shaders stay native too.
- Laser hue uses the existing tracer palette but preserves each beam/dot's peak
  RGB magnitude and alpha. `_Factor` stays unchanged. The intensity setting is a
  minimum relative to the original prefab, not another multiplier: Bright 2 plus
  Arena 2 stays 2; Arena 4 raises that to 4. Bright's factor/width/color settings
  remain its own. Setting 1 adds nothing even if another mod dimmed the laser.
- Only the private laser spot light is colored; flashlights are never inspected.
  It stays colored until camera rendering and restores on inactive/pool/owner,
  setting, mode or raid cleanup. A later external color write wins over our stale
  restore. Native spot-light intensity follows the temporary draw intensity; its
  exact observed output is restored to the unboosted value on exit. Cleanup uses
  a 0.25-second sweep for beams that no longer render. Read-only weak prefab
  baselines survive raid resets without retaining the prefab, owner or world.
- Installed BrightLasers 3.0.0 changes width in Awake, clones/recolors BeamMaterial
  and scales intensity in CreateLight, and may recolor materials in LateUpdate's
  postfix. Its material colors do not replace a submitted per-draw property-block
  override. No BrightLasers code, DLL, settings or material are changed/copied.

GPU handoff is still required: both teams' weapon lasers and dots in TDM and
protected Tactical (including local death and round rebind), quick weapon
switch/drop/pickup, flashlight-only and IR modes with/without night vision,
BrightLasers override/RGB options, changing F12 team colors/TeamLasers and
VisibleLaserBrightness (1/2/4), respawn,
and then a normal raid. The modeled renderer cannot prove Unity rendering,
actual component hierarchy, Harmony execution order or every custom attachment.

The previous harness had 79 assertions and deliberately excluded Tactical. That
scope omission, rather than an observed Bright material override, explains why
the recent protected Tactical rounds could not receive Arena team laser colors.
See `../../LASER-BRIGHTNESS-REVIEW.md` for the diagnosis, design and review limits.
The scripts print current installed hashes when the optional Bright plugin is
present; its absence does not disable the replay of its reviewed 3.0.0 contract.
