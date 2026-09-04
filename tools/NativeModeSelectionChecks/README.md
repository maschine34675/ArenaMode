# Native Arena mode-selection checks

Run `dotnet run --project tools/NativeModeSelectionChecks -c Release` from the
repository root. The project links the production mode catalog and Unity view,
then supplies small stand-ins for the UI objects.

The checks cover the four stable mode ids, normalization and fallback, native
font reuse, bottom-screen placement, one picker per screen, click propagation,
description updates, teardown, and the Tactical-only PitFireTeam 5v5 state.
They do not render the real Unity scene or prove pointer delivery in EFT; that
still requires the user's GPU-equipped game client.

For mutation verification, copy either production source outside the repository,
change one required behavior, and pass the copy through `ProductionCatalogSource`
or `ProductionViewSource` with a separate configuration. The matching assertion
must fail; never edit or deploy a mutant production source.
