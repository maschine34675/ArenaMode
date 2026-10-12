# Dedicated Molotov presentation and integration

2026-10-12. Supersedes the September opt-in conversion of ordinary RGD-5s.

The private `a12e00000000000000000001` item is an additional optional Arena kit
supply. Existing two RGD-5s remain real explosive grenades. The private catalog
adds the Molotov before native kit preload; Tactical receives it through the
purchased kit. PFT own equipment is not supplemented, custom throwable policies
opt out, and Zombies does not receive it because its fire runtime is not admitted.
Native rig/pocket packing remains authoritative: no backpack spill or guaranteed
space is invented. See `MOLOTOV-ITEM-REVIEW.md` for registration and failure paths.

`Arena incendiary / Enabled` defaults to true. The obsolete prototype key no
longer converts RGD-5s. Only this custom item can create fire, with a live supported
Arena match, exact local throw and matching BigBrain companion. Otherwise it
expires harmlessly; native template blast/fragment/effect fields are inert even
without a functioning client. Bots are not supplied this item. It is not a new
trader, flea, world-loot or permanent-profile item.

The original procedural bottle replaces only meshes/materials on exact generated
instances; existing native hand rig, throw animation and physical colliders stay.
Native inventory icon rendering uses the same bottle. There is no new lighting
or ignition hand animation, no transparent-liquid simulation and no external
bundle dependency. See `MOLOTOV-MODEL-REVIEW.md` for pool restoration and offset
handling. Ordinary native prefab assets are referenced in the installed game,
never copied into this repository or redistributed by this change.

Ground fire now uses the broader layered presentation described below: a soft
camera-facing base, three original tongue shapes, sparse embers and one bounded
light. Particle positions are world-relative; tongues grow above the floor.
Expiry reduces opacity, height, lifetime and light during the final half second.
No opaque smoke curtain, global brightness edit or shared native material mutation.

Three original synthesized mono sounds cover brittle impact/ignition, a looping
burn and a short extinguish. `ArenaFireSound.cs` is their complete source; no
downloaded recording was sampled, converted, copied or packaged. The reference
files remain outside the repo in the user's Downloads/molotov directory and are
not redistribution-cleared. No new third-party audio attribution is required
for the synthesis. Voices and their independent volume settings are unchanged.

Fire sound uses two owned 3D AudioSources routed to the exact native Master
group, with listener/master pause/mute preserved and a linear 18m range. It does
not use interface volume, change a mixer parameter or reuse an announcer source.
The host/group is revalidated during playback. Missing audio stops only audio;
construction, tick and cleanup failures in optional presentation cannot erase
the separately owned fire damage or bot hazard.

Friendly fire delegates to the current common damage rules. Attributed fire
teamkills additionally require opponent award admission before scoreboard base
points: no reward for PFT ally kills with friendly fire on. This extra gate is
restricted to exact owned fire; normal damage scoring is unchanged. Assists
continue to use actual HP loss and the existing hostile-source filters.

Offline evidence and native acceptance are distinct. The production-linked PCM
check measures duration, finite samples, energy, DC, headroom and loop boundary;
its invalid-audio controls reject silence, NaN and clipping. This cannot audition
the result or certify the Unity mix. Native bottle appearance, throw, fire
readability and sound remain part of the exact-build GPU report. No native result
is implied by this design/review document.

Independent reviewers inspected model resource ownership, common FF admission,
the scoped score gate, original PCM and Master routing. The review caught partial
third-party material replacement retaining destroyed materials; per-slot restore
and native setter retry were added and fault-tested. Root also corrected the
flame pivot so the sprite's lower half does not remain below ground.

## Broader fire presentation, 2026-10-12

The user preferred the older, fuller fire. The first dedicated-item presentation
had reduced emission from 12.5 to 8.3 particles per cell/second, mean lifetime
from 0.60 to 0.41 seconds and width from 0.55-0.85 to 0.30-0.50 metres. Its upright
tongues, transparent start and shrinking to 20 percent further reduced apparent
coverage. These are source-backed explanations, not a matched visual A/B result.

The new low layer restores the original soft circular texture principle using
an original 64px texture and ordinary camera-facing billboards, visible from
both shallow and downward angles. Each cell emits ten per second, width
0.72-1.00m and lifetime 0.55-0.70s, with limited lateral jitter/drift. The upper
layer uses three original 96px procedural silhouettes, vertical billboards,
independent shape selection and varied size, lean, drift and rise. Its emission
interval varies between 0.16 and 0.20s; particle lifetime is 0.48-0.65s. Shapes
have separate owned materials rather than an atlas, preventing adjacent-frame
sampling and unwanted animation. Basis and embers share one owned material.
All particles taper less severely; no extra world light or shadow is added.
The existing light now fluctuates smoothly with noise within a similar intensity
range, rather than following two fixed sine waves.

Hard budget is 384 base + 3*96 tongues + 32 embers = 704 particles/fire, at most
2,816 across four fires, below the original prototype's 768/fire. Geometry has
at most 49 cells. Four owned textures/materials and five renderers per fire add
draw calls and translucent overdraw; a particle cap alone is not a performance
measurement. No frame-hitch emission catch-up was added, lifetimes stay bounded
by remaining fire duration, and all partially constructed textures/materials
are registered for cleanup before configuration/upload can fail.

Only ArenaFireView changes: damage, geometry, bot avoidance, attribution, item
supply, bottle model and audio methods are unchanged. No code or asset from
Sage's CC BY-NC-ND mod was incorporated. Its layered design was examined as a
conceptual reference only. Independent adversarial review found no open P0-P2,
including shared-resource lifetime and native Unity rotation-unit verification.
The installed-API client build passes with the same four existing CS8632 warnings.
The runtime harness does not render this view; native visual evidence belongs
in GPU-MOLOTOV-VISUALS-RESULTS-20261012.md, separately from the earlier combat report.

The first native v2 capture rejected the initially bright yellow base: overlapping
sprites became glowing blobs which hid the tongue detail. The visual follow-up
retains its width, cell emission and lifetime, but lowers its alpha peak from
0.90 to 0.30, uses subdued orange instead of yellow, and reduces height to
0.18-0.30m, initial elevation to 0.08-0.12m and rise to 0.08-0.20m/s. Tongue
shapes, particle budgets and all gameplay/audio remain unchanged. Final native
evidence must identify the follow-up build, not treat the rejected v2 as accepted.
