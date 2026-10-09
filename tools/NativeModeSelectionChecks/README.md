# Native Arena mode-selection state checks

Run `dotnet run --project tools/NativeModeSelectionChecks -c Release` from the
repository root. The project links the production mode catalog and selection
state directly, with no Unity stand-ins or game assembly references.

The checks exercise all five stable mode ids and their selected definitions,
index bounds, normalization, explicit initial repair, accepted/rejected writes,
callback exceptions, and the protected Tactical-only PitFireTeam 5v5 restriction.
Construction must not write settings. A changed choice becomes selected only
after its writer accepts it; a current selection does not trigger another write.
Initial repairs remain retryable after a rejected or throwing writer and become
idempotent after success. An accepted user choice supersedes a pending initial
fallback. TEAM DM is independent and cannot be selected in a protected PFT team
flow.

This harness does not instantiate the Unity view, inspect source-text fragments,
or claim coverage of rendering, layout, fonts, pointer delivery or teardown.
Those checks require the native game window and recorded visual/menu checks.
No raid needs to be started for the menu checks; actual raid tests belong to the
user.

For mutation verification, copy a production source outside the repository,
change one required behavior, and pass the copy through `ProductionCatalogSource`
or `ProductionStateSource` with a separate configuration. The matching assertion
must fail; never edit or deploy a mutant production source.

`Verify-TeamDeathmatchRegression.ps1` removes the state-level selection guard in
a temporary source copy and requires the protected-team write-rejection check to
fail. It also retains controlled failures of the server faction policy, shared
chance-record isolation, and ambient spawn gate. Temporary source copies compile
in separate configurations; server deployment is suppressed through an explicit
nonexistent `SPTUserMods` path. The script checks that production sources did not
change, then reruns the production state and server checks. These are offline
checks, not runtime gameplay proof.

`CopySchema` (2026-10-08) enforces one copy schema on every catalog entry: RULES
and EQUIPMENT at most three words and 18 characters, RESPAWN exactly `Instant`, `None`
or `Next round`, the description one sentence of at most 80 characters ending
with a period, and no roster in Tactical's RULES. The limits are what fits the
mode panel's 218 px fact column at 17 pt. `Verify-CatalogSchema.ps1` hands five
off-schema copies of `ArenaModeCatalog.cs` (Zombie's old long facts, a long
RESPAWN, a long EQUIPMENT, a description without period, a roster in RULES) to
the harness through `ProductionCatalogSource`; each must fail its named
assertion, then the production catalog is rerun and hash-checked.
