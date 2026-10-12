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

Ground fire uses a tapered, bilinearly filtered original 128px flame sprite,
vertical tongues, sparse embers and one bounded light. Emission is 8.3 per cell
per second, maximum 384 flame/32 ember particles per fire, at most four fires.
Particle position is world-relative; the tongue pivot rests above the floor.
Expiry reduces opacity, height, lifetime and light during the final half second.
No opaque smoke curtain, global brightness edit or shared material mutation.

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
