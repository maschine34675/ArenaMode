# Mounted team laser verification

Run `pwsh -NoProfile -File tools/TeamLaserChecks/Verify-Contracts.ps1` and
`pwsh -NoProfile -File tools/TeamLaserChecks/Verify-Regression.ps1` from the repository.
Contracts require PowerShell 7, installed EFT assemblies/bundles and Python with
UnityPy; only metadata is read, no extracted assets are packaged. The harness
requires .NET 10. It never builds or deploys the game plugin.

The executable links the actual color service, hue/restore policy and tracer
palette with a minimal modeled Unity/EFT boundary. It tests live-team affiliation
as a collaborator supplied by `TeamDeathmatchMode.TryClassifyCurrentBody`; that
method's exact native body/roster validation is covered by TeamGiveUpChecks,
which links the real mode and ArenaMatch.
Checks execute actual Begin/End/Tick/Reset, original-block reference restoration,
per-component allocation reuse, visible/IR/unknown shader gates, current weapon
ownership, team hue, alpha/HDR preservation, disabled/pool/owner-change cleanup,
and native/third-party replacement precedence. Six scratch-source mutations must
fail their specific intended assertions; production files are never altered.

## Native contracts and installed compatibility

Verified in `../_decompiled/LaserBeam.cs`, `TacticalComboVisualController.cs`,
`WeaponPrefab.cs`, `EFT/Player.cs`, installed Assembly-CSharp metadata and bundles:

- `WeaponPrefab.Init(IPlayer,bool)` stores `_player`; an exact currently wielded
  `FirearmController.ControllerGameObject.GetComponent<WeaponPrefab>()` must match.
  The active laser must also be below a mounted, active TacticalCombo controller.
- `LaserBeam.LateUpdate` draws beam and dot with separate property blocks. Every
  float consumed by its draws is written by that invocation. Arena's reused blocks
  contain the team `_Color` plus those native writes. A finalizer restores the exact
  original block references even if drawing throws. Materials, keywords, shaders,
  modes, width, intensity and activation are never changed.
- Native `Custom/LaserBeam` / `Custom/LaserPoint` materials use `_Factor > 0` for
  visible light. Inspected Holosun LS321, AN/PEQ-15 and Klesch IR materials instead
  have `_Factor = 0` and `_IkFactor > 0`; either invisible material makes the whole
  beam stay native. Unknown shaders stay native too.
- Laser hue uses the existing tracer palette but preserves each beam/dot's peak
  RGB magnitude and alpha. Native intensity and `_Factor`, including BrightLasers
  multipliers, are untouched; perceived brightness can naturally differ by hue.
- Only the private laser spot light is colored; flashlights are never inspected.
  It stays colored until camera rendering and restores on inactive/pool/owner,
  setting, mode or raid cleanup. A later external color write wins over our stale
  restore. Cleanup uses a 0.25-second sweep for beams that no longer render.
- Installed BrightLasers 3.0.0 changes width in Awake, clones/recolors BeamMaterial
  and scales intensity in CreateLight, and may recolor materials in LateUpdate's
  postfix. Its material colors do not replace a submitted per-draw property-block
  override. No BrightLasers code, DLL, settings or material are changed/copied.

GPU handoff is still required: both teams' weapon lasers and dots, quick weapon
switch/drop/pickup, flashlight-only and IR modes with/without night vision,
BrightLasers override/RGB options, changing F12 team colors/TeamLasers, respawn,
and then a normal raid. The modeled renderer cannot prove Unity rendering,
actual component hierarchy, Harmony execution order or every custom attachment.

## Review record

1. Native source/assembly pass established draw blocks, owner binding and lifecycle;
   installed BrightLasers was decompiled read-only, with no source adaptation.
2. Bundle/shader pass verified visible `_Factor > 0` and IR `_Factor = 0` for the
   native color shaders and inspected three independent IR-capable attachments.
3. Production-linked adversarial pass exercised 79 assertions, including the actual
   Harmony finalizer entrypoint and two opposite owners sharing both materials;
   six deliberate implementation regressions are rejected by their intended checks.
4. Integration/lifecycle pass read the root-owned settings, plugin Enable/Reset,
   Controller Tick, and match start-failure/raid-end/new-raid resets independently.
   No open correctness finding. Root independently reviews this leaf before release.

Installed BrightLasers 3.0.0 at review: SHA256
`68F44E28B7BB5B7964CAC1F1399078E196B645EA972208D962FF5FEED93AAED6`.
