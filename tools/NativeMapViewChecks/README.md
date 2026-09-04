# Native map view checks

Run `dotnet run --project tools/NativeMapViewChecks -c Release` from the repository.
This project links the actual `ArenaLocationSelectionView.cs` and uses minimal
Unity/EFT stand-ins; the production helper is not duplicated.

The fixture records the installed EFT 0.16.9.5.40743 Unity scene `level48`:
`_locationButtonsContainer` and `_locationPathsContainer` on the native location
screen (component 25068) both reference Transform 15523, GameObject 269, named
`Paths Container`. Generated map buttons and generated `LocationPath` views share
that container. The inactive path template, GameObject 276, sits outside it.
Hiding the container therefore hides all maps, even when the three Arena base-map
buttons were created correctly. Factory day and night deliberately share one tile,
so those three buttons represent four allowed location IDs.

Checks require the container and map GameObjects to remain untouched while only
path instances and Marathon are hidden. They cover shared/separate containers,
inactive ancestry and paths, exact restoration, repeated entry, newly generated
paths, native changes between visits, screen replacement, duplicate references,
disposed Unity objects, null inputs, and keeping external templates unchanged.
The same executable links the production map catalog and checks the exact legacy
allowlist upgrade, one-tile Factory representation, day/night selection, and the
day-only, night-only and fully disabled cases.

For mutation verification, put a source copy outside this directory, replace the
individual-path hide with `Hide(pathsContainer.gameObject)`, and use the copy via
the `ProductionViewSource` MSBuild property with a separate configuration such as
`-c Mutant`. Catalog mutants can be supplied through `ProductionCatalogSource`.
The relevant visibility or Factory-policy assertion must fail. Compare production
source SHA-256 before and after; never edit or deploy a mutant production source.

These are offline regression checks of the production helper's hierarchy and
state handling. They do not execute Unity rendering, Harmony patches, native map
filtering or selection listeners, navigation, or gameplay. Restart the game and
verify Arena shows one selectable Factory tile with working day/night choices,
Labyrinth and Labs, and that ordinary PMC/Scav maps and Marathon are restored
after leaving Arena.
