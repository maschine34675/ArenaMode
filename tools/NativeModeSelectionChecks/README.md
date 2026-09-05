# Native Arena mode-selection checks

Run `dotnet run --project tools/NativeModeSelectionChecks -c Release` from the
repository root. The project links the production mode catalog and Unity view,
then supplies small stand-ins for the UI objects.

The checks cover the five stable mode ids, normalization and fallback, native
font reuse, bottom-screen placement, one picker per screen, click propagation,
description updates, teardown, and the Tactical-only PitFireTeam 5v5 state.
TEAM DM is a standalone mode and remains disabled in a selected PFT team flow.
All five buttons must stay within the original raised 820-pixel panel, with
eight-pixel gaps and at least 150 pixels of label width.
They do not render the real Unity scene or prove pointer delivery in EFT; that
still requires the user's GPU-equipped game client.

For mutation verification, copy either production source outside the repository,
change one required behavior, and pass the copy through `ProductionCatalogSource`
or `ProductionViewSource` with a separate configuration. The matching assertion
must fail; never edit or deploy a mutant production source.

`Verify-TeamDeathmatchRegression.ps1` also checks controlled failures of the
server faction policy, shared chance-record isolation, and ambient spawn gate.
It compiles temporary source copies in separate configurations and suppresses
server deployment through an explicit nonexistent `SPTUserMods` path. It never
modifies production sources or live mod files, then reruns the production menu
and server checks. These are offline checks, not runtime gameplay proof.
