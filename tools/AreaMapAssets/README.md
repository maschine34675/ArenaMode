# Customs preview assets: offline tools

These tools only read/write `ArenaMode.Client/Assets/MapPreviews`. They never
build the mod, run the game, contact the network, or deploy to an SPT installation.
The verifier performs all negative controls in memory and writes nothing.

Run the standalone verifier with an existing Node installation:

```powershell
node tools/AreaMapAssets/verify-assets.mjs
```

Reproduce the reviewed PNG with Node and an existing **sharp 0.35.4** installation
(reviewed librsvg 2.62.91). The script can resolve normal `node_modules/sharp`, or
take an explicitly supplied package directory without installing anything:

```powershell
node tools/AreaMapAssets/build-customs.mjs --sharp '<existing sharp package directory>'
node tools/AreaMapAssets/verify-assets.mjs
```

The conversion checks the original source hash before applying its explicit
palette and removing only the source's sniper hazard annotation. It retains the
entire original viewBox, adds no text or markers, and uses a flat RGB background.
The reviewed PNG and original files are pinned in `MAP-SOURCES.json`. Changing
the generator or native raster libraries requires reviewing the new output and
updating that record deliberately; the build does not silently refresh hashes.

Calibration is **image space with a top-left origin**, not Unity texture UV space:

```
u     = (698 - worldX) / 1070
vDown = (worldZ + 306) / 541
vUp   = 1 - vDown              // Unity texture UV y
```

The complete PNG has the same normalized axes as the source SVG. The runtime
must use the exact authoritative Arena polygon and must use the same transform
for image crop and polygon overlay. Height does not affect this ground overview.
Only Customs/bigmap is calibrated in this milestone. This is offline projection
evidence, not native rendering or landmark acceptance.

Read the asset directory's `ATTRIBUTION.md` before packaging. **CC BY-NC-SA 4.0
is non-commercial only**. Include attribution, license and provenance with the
embedded image; the DynamicMaps runtime is neither copied nor required.
