# Native side view checks

Run `dotnet run --project tools/NativeSideViewChecks -c Release` from the repository.
The project links the actual `ArenaSideSelectionView.cs`; it does not duplicate the
production helper. Unity and EFT dependencies are minimal stand-ins.

The fixture records the hierarchy inspected in the installed EFT 0.16.9.5.40743
Unity scene `EscapeFromTarkov_Data/level48`: the Random toggle is the sole child of
an inactive `AlphaBlocker` under the side selection screen. That wrapper's
CanvasGroup starts at 0.3 alpha with interaction disabled, and its hover tooltip
says the feature is unavailable in alpha. The spawner itself has no CanvasGroup
reference. The native screen initializes its toggle in Awake and hides the
spawner during Show.

Checks cover ancestor visibility, canvas input/opacity, disabled eligibility,
obsolete tooltip suppression, identity/group preservation, repeated opening and
hiding, unchanged PMC/Scav siblings, and rejecting unsupported hierarchy before
mutation. Activating only the button reproduces the original missing-Arena bug.

For mutation verification, pass a temporary source copy through the
`ProductionViewSource` MSBuild property and use a separate configuration, such as
`-c Mutant`. Removing the wrapper activation/unlock from that copy must fail the
visibility assertion. Do not edit or deploy a mutant production source file.

These are offline checks of the view helper. They cannot verify Unity rendering,
Harmony binding, real native listeners or gameplay. A game restart and a live
check of Arena plus the ordinary PMC/Scav routes are still required.
