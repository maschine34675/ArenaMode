# Arena visible laser beam brightness

## Diagnosis from the installed game

The previous `VisibleLaserBrightness` control raised the native `IntensityFactor`
minimum. That reliably strengthens the point and spot light, but is not a uniform
beam multiplier. This explains the user's observation without assuming that team
coloring or another mod overrode the setting.

The installed `EscapeFromTarkov_Data/StreamingAssets/Windows/shaders` bundle was
read with UnityPy. Its DX11 bytecode was decompressed and disassembled in memory
using Windows `D3DDisassemble`; no GPU, shader source export or copied asset was
used. The local native `../_decompiled/LaserBeam.cs` defines mesh Y as 0 at the
weapon and 1 at the far end. `Custom/LaserBeam`'s vertex program computes:

```
vertex gain = 4 + meshY * (_Intensity - 4)
```

Its fragment program multiplies that interpolated gain by `_Color`, native noise
and texture terms, and the visible `_Factor`. Night vision selects `_IkFactor`
instead, and thermal output is zero. The state is additive One/One blending,
depth writes off and the existing LessEqual depth test. `Custom/LaserPoint`'s
fragment program multiplies its output directly by `_Intensity`.

Consequently, for `_Intensity` 1 changing to 2, the beam gain at the weapon remains
4, the halfway gain changes only 2.5 to 3, while the point gain doubles. Increasing
visible `_Factor` instead scales every beam position equally. These are shader
input relationships, not a claim that perceived brightness is linear after the
camera's tone mapping, noise, bloom or exposure.

Reviewed installed programs (SHA256):

| Shader / stage | DXBC SHA256 |
|---|---|
| LaserBeam vertex | `08afd29b01fc28e6b0c90199e5657d74fee2a538e019a5b23b62a57bf81e5b1f` |
| LaserBeam fragment | `b49673673613d59ad3a554eac41bd24ddbd68938c1a68f7e8f88acbc3959870a` |
| LaserPoint vertex | `d53a65b7f5f02fd689f0f17790add5685472368c6100c75111df58877e21d18e` |
| LaserPoint fragment | `15c7240a40b11e31f6f140067f7503aea7242d8819970c207e8b39c7b1b80e5c` |

The beam global constant-buffer offsets are `_Color` 32, vertex `_Intensity` 48,
fragment `_Factor` 52, `_IkFactor` 56, `_NightVisionOn` 88 and `_ThermalVisionOn` 92.
The point fragment `_Intensity` is at 48. All six inspected standard red/green/blue
beam/point materials have `_Factor` 1 and `_IkFactor` 80. Previously inspected
Holosun LS321, AN/PEQ-15 and Klesch IR materials use `_Factor` 0. Unknown shaders
and invisible materials remain excluded.

## Change and compatibility

`Effects / VisibleLaserBeamBrightness` is a separate F12 control, default 2,
range 1..5. Value 1 disables Arena's additional beam brightness. The existing
`VisibleLaserBrightness` setting, default and point/spot behavior are unchanged.
A separate control is necessary to improve the beam without making the already
visible point even brighter.

Arena captures the original visible beam factor before native Awake/CreateLight,
then observes the exact resulting material after initialization. The existing
read-only observation can happen during preload or the hideout: neither callback
writes a native value or render state. The component is a ConditionalWeakTable key
and the initialized material is a WeakReference. No owner/world/material is kept
alive by the baseline. Observations survive Arena effect reset for pooled reuse.
An unknown baseline, failed initialization observation or unrelated later material
replacement receives no additional factor; later observation cannot recapture it.

For an exact current held active laser and live Arena body, the beam's private
per-draw property block receives only this minimum when it exceeds the material:

```
max(current visible material factor, original visible factor * beam setting)
```

BrightLasers 3.0.0's previously inspected CreateLight postfix clones BeamMaterial
and scales its factor before Arena's Awake postfix observes that final material.
Thus Bright factor 2 plus Arena beam 2 stays 2; Arena beam 4 raises it to 4.
Bright's material color changes do not replace Arena's submitted team hue.
There is no frame accumulation or second multiplication of an existing boost.
The current installation no longer contains BrightLasers, so coexistence is
verified against the reviewed 3.0.0 contract replay unless it is installed again.
This change does not install, remove, restore or modify BrightLasers or its config.
The implementation uses native APIs and contains no copied BrightLasers code/assets.

The original beam/point property-block references are restored in the existing
finalizer, including exceptions. Only the beam block gets a visible factor
override, so the point block, material factors, shared materials, deferred light,
beam width, activation, tactical mode, flashlight and IR factors are untouched by
the new control. Existing team hue/alpha and deferred light leases remain as before.
In night vision the native shader selects `_IkFactor`, so this visible-only beam
setting does not additionally strengthen that path.

All writes still pass the exact active Arena game/world/match and started-game
gate, a currently alive registered physical body, its exact held weapon and active
mounted laser. TDM and protected Tactical retain their existing exact current team
classification; solo Arena has brightness without invented team colors. The
protected Tactical classifier rejects staging/rebind/aftermath and old bodies.
Inactive, pooled or retired render state is pruned by the existing cleanup. The
new factor itself cannot persist beyond its draw because no persistent material
or native scalar is changed. Normal raids get no render-state change.

## Verification and limits

`tools/TeamLaserChecks/Verify-Beam.ps1` runs the existing installed assembly/material
contracts and thirteen negative controls, plus an installed shader oracle and six
beam-specific scratch-source mutations. It links the actual production brightness,
draw/restore and Tactical classifier code. The modeled rendering boundary applies
the independently inspected beam and point formulas with fixed texture/noise inputs.
It demonstrates the old point-versus-beam difference, then checks all five beam
positions scale while point/spot output does not change further. It also exercises
Bright coexistence, per-frame reuse, weak material retention, foreign materials,
disabled settings, scope exclusions, native exceptions and pool cleanup. Each
negative control must fail its intended assertion, not merely fail compilation.

The shader oracle pins all four DXBC hashes and checks the relevant metadata and
instructions. Different installed shader bytecode fails for manual review. Shader
programs remain in memory; the script exports no proprietary shader or asset.

Root executed the complete `Verify-Beam.ps1` gate successfully: installed native
assembly/material contracts, the pinned installed beam/point shader contracts,
the production-linked replay baseline, all thirteen existing regression mutations
and all six new beam mutations passed their intended checks. An initial harness
compile failure caused by a shadowed local variable was corrected before this
successful run; that compile failure was not counted as a rejected mutation.
The author did not run builds, tests or deployment; root serialized verification.
An independent adversarial review also completed without open P0–P2 findings,
covering material writes, shared state, double amplification, weak-reference
lifetime, normal-raid and current-body boundaries.

GPU handoff remains required: compare beam visibility at near/mid/far distances
with beam setting 1/2/4 while holding point brightness fixed; both team colors in
TDM and protected Tactical, local death/rebind, weapon switching and pooled reuse,
flashlight-only and IR/NVG modes, an ordinary raid, and optional BrightLasers
override/RGB/coexistence. These offline checks cannot prove actual Unity rendering,
Harmony order under arbitrary third-party patches or perceived visibility.
