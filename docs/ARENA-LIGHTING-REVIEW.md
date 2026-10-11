# Arena lighting director

## Scope

ArenaMode adds a lighting layer on Labs, Factory (day and night), and Labyrinth for visible round-state
feedback. It is client-side and does not require edited map bundles.

- The last three seconds of a solo Tactical buy phase or Rounds intermission
  pulse amber at the integer countdown boundaries.
- A round start produces a short flash using the configured friendly tracer
  color.
- Timed Rounds and Tactical combat gain a restrained red pulse during their
  final configured seconds.
- Tactical victory and defeat fade through the configured friendly and
  opponent tracer colors.

The feature is enabled by default and can be tuned in F12 under `Effects` with
`ArenaLighting`, `ArenaLightingStrength`, `ArenaLightingPressureSeconds`, and
`ArenaLightingOutcomeSeconds`.

## Runtime design

The director creates exactly one shadowless directional `Light`, parents it to
the exact active Arena `GameWorld`, and normally keeps it disabled. It changes
only that owned component's color and intensity. It does not enumerate or edit
map lamps, lamp controllers, emissive materials, `RenderSettings`,
`LevelSettings`, lightmaps, post-processing, weather, fog, or AI perception.

Every tick first requires the Arena session latch and the same `GameWorld`
instance captured at match start. Failure during partial match startup, native
raid end, stale-world recovery, and plugin shutdown all disable the light,
remove the match event subscription and destroy the owned object. The work per
frame is constant: a small number of state reads, dictionary lookups and light
property writes, with no scene or object enumeration.

Blackout (`com.vultify.blackout`) and Going Dark
(`com.senze.goingdark`) are treated as external lighting owners. With
`RespectExternalLightingMods` enabled, the Arena layer remains off if either
plugin is installed. The user can deliberately override this in F12 because
ArenaMode's layer is additive and does not mutate their state.

## Source verification and attribution

The installed SPT 4.1 decompilation confirms that `LevelSettings` reapplies its
ambient state from `Camera.onPreCull` and that EFT's light wrappers can rewrite
underlying light intensities. That is why this first version does not compete
with those systems and owns an independent Unity light instead.

The runtime approach was informed by the MIT-licensed
[Vultify/Blackout](https://github.com/Vultify/Blackout) and
[notSENZE/GoingDark](https://github.com/notSENZE/GoingDark) projects. No source,
map coordinates, audio, textures, bundles or scene-mutation tables were copied.
ArenaMode uses only the general technique of creating a runtime Unity light and
explicitly credits both projects here.

## Verification and limits

`tools/ArenaLightingChecks` links the production policy and director against
small Unity/EFT/BepInEx stand-ins. It covers phase precedence, fades, invalid
settings, Labs/Factory day/night/Labyrinth scope, exact-world ownership, external-mod suppression,
one-light ownership, duplicate-free re-entry, live disable and every cleanup
boundary. Its source assertions reject scene scans and mutation of existing map
lighting.

The deployment-free Release build and directly affected Arena regression suites
are run by `tools/ArenaLightingChecks/Verify-Regression.ps1`. This server has no
GPU, so the final intensity, spectator-camera appearance and real Unity render
path require a playtest. Factory day and night use the same effects and strength
as Labs; their brightness, including with night vision, still needs a GPU check.
Labyrinth uses that same additive layer and settings, including while spectating in
protected Tactical 5v5. It does not change the map's existing lamps or alarm.
Its final brightness and spectator-camera appearance also require a GPU check.

The Labyrinth follow-up passed 267 production-linked assertions, independently
re-run after reviewing the map admission, event subscriptions, external-owner
suppression and cleanup paths. The new map check fails against the old allowlist.
No unresolved source-review findings remain for this change.
