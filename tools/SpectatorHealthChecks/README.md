# Spectator health checks

Run `pwsh -NoProfile -File tools/SpectatorHealthChecks/Verify-Regression.ps1` from the repository root. This builds only the isolated console runner and does not deploy the mod.

The runner links the production `SpectatorHealthReadout` and `ArenaSpectatorRequest` sources. Small native API models stand in for EFT/Unity bodies and health controllers. It exercises changing damage/healing, target switches, a custom bot maximum, death/respawn, stop, round epoch drift, same-profile replacement bodies, inactive/destroyed bodies, absent/throwing controllers, invalid numbers, and controller/epoch changes during a read. Six deliberate regressions must fail their intended assertions. Mutations use temporary source copies; the production source hash must remain unchanged and the final production baseline must pass again.

These checks do not instantiate the actual spectator camera or Unity GUI. The service-to-reader binding and native contract are also reviewed against current source. A successful build and these checks do not prove layout, camera behavior, or HP accuracy in a running raid.

Native evidence in `../_decompiled`:

- `EFT/Player.cs:24864`: `HealthController` is the body's read-only `IHealthController` reference.
- `EFT.HealthSystem/IBaseHealthController.cs:13`: `GetBodyPartHealth(EBodyPart, bool rounded = false)` returns `ValueStruct`.
- `EFT.HealthSystem/BaseHealthController.cs:808`: `Common` sums each body part's current and maximum HP; it does not use a fixed PMC maximum.
- `EFT.HealthSystem/ValueStruct.cs`: float `Current` and `Maximum` fields.
- `EFT.UI.Health/HealthParametersPanel.cs:169`: the native total-health UI uses `Common` and rounds the aggregate current HP.

The existing bottom-center banner retains its 560-pixel maximum width and lower edge, expanding upward by 22 pixels to fit two lines: the selected bot's side/name, then `HP current / maximum` and the existing mouse-button switch hint. Rich text is disabled and line breaks in names become spaces. No health bar, killcam change, input change, health mutation, or event subscriptions are added. The service rechecks its existing exact camera ownership and retrieves the frozen body by the selected target ID; the readout independently requires that exact object, a current eligible lease, protected Arena, a dead local observer, and a live active target. It caches neither the name nor HP.

GPU handoff: in protected Tactical 5v5, die and let the replay finish; observe both ally and opponent bots, switch targets with LMB/RMB, watch HP change after damage/healing, and verify the banner disappears during round end and local respawn. Confirm readability at the user's resolution/UI scale. Missing/invalid data must never show the previous bot's HP.

Independent verification on 2026-09-05 re-ran 49 assertions and all six mutation controls. Source review checked the native total-health API, service/frozen-body binding, invalid-data handling and unchanged camera/input lifecycle with no unresolved findings. The client Release build passed against installed assemblies and its deployed DLL matched by SHA256. Existing protected-team, PFT, Team Deathmatch, alarm, tracer and announcer checks also passed. These are offline results; the GPU handoff above remains outstanding.

## Replay visual-effects scope (2026-09-30)

The runner additionally extracts the unchanged method text of production
`SpectatorHealthEffects.HasExactReplayContext` into its narrow executable fixture.
It admits cleanup only during an active Arena raid with a frozen enabled replay,
the exact started LocalGame/GameWorld pair and its exact dead local body in both
registries, and confirmed pending/playing/cleanup ownership by Killcam.
Thirty-one assertions reject ordinary/disabled/stopped contexts, living or
destroyed bodies, missing health, body replacement in either registry, stale
native identities, and absent/idle/unreadable replay state. Three compiled
negative controls remove ordinary-raid, world-body, or busy-camera guards and
must fail their named runtime assertions; compile failures do not count.

The PFT cleanup entry retains its original protected-team gate. The replay entry
reuses the same bounded two-pass visual cleanup, including the exact
EffectsController-bound-body check, without acquiring a spectator camera or
mutating actual health. Native `EffectsController.cs:1230` binds `player` to the
camera controller's body, `:48` is the visual accumulator removal path, and
`:1488` only starts death fade/blur. Killcam suppresses that death handler;
it does not clear concussion/blood accumulators itself.

Verification: 80 assertions and nine mutation controls passed on 2026-09-30.
The new tests execute the admission method, not Unity's visual components. A
native replay after concussion/blood effects and an ordinary-raid comparison
remain the acceptance checks for pixels and restored respawn visuals.
