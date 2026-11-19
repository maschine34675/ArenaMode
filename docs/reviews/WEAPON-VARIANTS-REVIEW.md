# Arena weapon variants

Scope: first-stage automatic additions to the existing twenty kit concepts and
Gun Game weapon builds. No authored builds are required from the player. Weapons,
existing attachments (apart from the explicit plain-cover exception below), magazines, chamber/cartridge data, armor, prices, order,
fill instructions and custom metadata remain untouched. Nothing writes kits.json,
ladder.json, BotConfig, APBS data or a player profile. Existing kit prices deliberately
remain the prices of the kit concepts; this change does not rebalance Tactical economy.

## Generation and isolation

`ArenaWeaponVariants` owns a compact immutable snapshot of the **loaded server** item
templates. The existing late `LadderGenerator` load boundary creates that index after
content mods. There is no dependency on bot-generation services, current PMC level,
SPT/APBS randomisation pools, mod DLLs or their runtime caches. Failed index creation
leaves original builds available.

The typed server adapter uses existing `JsonUtil.JsonSerializerOptionsNoIndent`,
including SPT's MongoId and other registered converters. Default System.Text.Json
would turn slot/conflict MongoIds into objects and silently disable the index; missing
native options therefore trigger the controlled base-build fallback. Tests construct
equivalent **local** options from `SptJsonConverterRegistrator`, without resetting the
server's static options. Full property serialization retains filter ExtensionData,
including exclusions that are not explicit CLR properties in this SPT version.

For each valid weapon, an absent optic is filled whenever a compatible free path can
be built. CQB collimators carry relative weight 12, conventional optics up to 4x weight
2. Existing optics (including a deliberately authored unusual one) are respected.
Optional additions: foregrip 75%, muzzle attachment 55%, laser/combined tactical unit
60%. Grip/muzzle choices mildly favor ergonomics/recoil improvements. A stock flash
hider does not block attaching a compatible suppressor to its free downstream socket;
an existing suppressor is retained. No laser activation/IR/flashlight behavior is
changed by this generator.

Free attachment/mount sockets are preferred. Handguards, barrels and stocks are
never replaced to manufacture a rail. A narrowly authorized receiver exception covers
the stock AKM `59d6507c86f7741b846413a2`, AK74 `5649af094bdc2df8348b4586`,
VPO `59e6449086f7746c9f75e822`, and Vityaz `59985a8086f77414ec448d1a` dust covers:
only without children, without any `upd`, and without an existing optic. An allowed
Bastion/PDC/Dog Leg/Vityaz-SN cover must fit the native parent socket, retain the
original node's other metadata, and obtain a valid optic **under that cover** in
the same transaction. Otherwise the original cover is retained. This supports the
Loyalist, Conscript, Drifter and Outlaw kit concepts without generic receiver rebuilding.
A weapon with no usable path can still retain iron sights. This is diagnosed
per build, rather than silently claimed as upgraded. New thermal/special/night scopes,
rangefinders, optics above 4x, quest parts and parts without a prefab are excluded.
Limits: four newly added nodes per chain, 4,096 search candidates and 48 candidate
transactions per category, 180 total weapon nodes, maximum 1 kg **total** added to the
original weapon. Individual new parts are at most 0.7 kg. Complex adapters requiring
multiple new sibling branches are rejected rather than incompletely populated.

Selection is weighted by final attachment first and then by mounting path; many
equivalent mount paths must not accidentally make an optic more common. Drawn item
identities are deterministic for the source/index/seed. The builder still generates
fresh live IDs when instantiating a kit.

All candidate chains are prefiltered against existing and newly selected templates'
conflicts in both directions **before** weighting. This fixes the actual M4/Spartan
seed-1 regression: the stock rear sight conflicted with a frequently chosen optic
which exhausted the 48-attempt budget through alternative mounts. The complete
post-generation validator remains in place; the prefilter does not relax it.

## Validation

Each candidate starts on a detached JSON copy. The complete weapon must satisfy:

- known templates; unique item identities; one root/primary; no orphan or parent cycle;
- parent socket exists and **all** native inclusion/exclusion filter records match;
- exclusions also apply to descendant contents;
- single occupancy for normal sockets; cartridge stacks respect their sum capacity;
- every required socket throughout the weapon, including newly added adapters, filled;
- conflicts checked in both directions, against the weapon root and every child.

Outside that exact plain-cover transaction, no accepted candidate may replace an authored item. A malformed or already invalid
base is left untouched and reported. Compatibility rejection/search exhaustion keeps
the last validated build. Validation is not a claim of visually correct eye relief,
clear sight picture or practical handling for arbitrary third-party content.

Native contracts were checked in `_decompiled/EFT.InventoryLogic/ItemFilter.cs`,
`ItemFilterExtension.cs`, `Slot.cs` and `StackSlot.cs`. Native filters are conjunctive,
and cartridge stacks are not ordinary single-occupancy slots. SPT's own
`BotWeaponGenerator.IsWeaponValid` does not substitute for this complete audit.
Category filters are limited to 53 verified `JsonTypes.TypeTable` identifiers;
unknown content-mod Node IDs do not become native CLR filter categories. Their
concrete item templates remain usable through explicit slot whitelists.

## Raid lifecycle and loading

The first catalogue request in the exact live **Armed** session freezes source data,
kit variants, ladder and rung count in its `SessionEntry`. This is before
`KitCatalog.Preload`, which itself runs before native local/start. Native start
retains those payloads; if the client made no early request it creates them there.
Ladder draws are retained per session; requests do not redraw. Explicit live ladder
completion redraws from that raid's frozen pool. Runtime
config reloads therefore cannot replace already selected/preloaded builds. Ordinary
requests do not generate variants. Session end clears retained catalogue strings.

The client receives the unchanged kit DTO shape: all new nested attachment nodes are
visible to the existing template-based `KitBundlePreloader`. Gun Game's existing
preload-before-swap and respawn preload remain authoritative; no late client-side
mutation or new bundle-loading mechanism is introduced.

## Preset guard (weapon pool, 2026-11-19)

The Gun Game pool is built by `LadderGenerator` from the item database's weapon presets, inside one try. Before
the guard, ONE preset it could not build (a part whose parent is in no node, a duplicate part id, a slot a weapon
mod removed from the weapon) threw there and the server fell back to the shipped 24-entry ladder instead of about
140 weapons; other defects did not throw and gave the player an incomplete gun (the client logs `Kit: no slot`
and skips the part). The credit for the failure analysis belongs to Sankese's BEAQO add-on (THIRD-PARTY-NOTICES).

- **First net: `ArenaPresetGuard.Problem`**, pure, on the RAW preset BEFORE `Plainer` picks one preset per weapon
  (after it, a broken plainest preset would lose a weapon whose other preset is fine). Rules, each mapped to a
  throw site or a build the client cannot complete, argued in the class comment: no-parts, no-root /
  several-roots, duplicate-id, bad-parent / no-slot / orphan, cycle, root-mismatch (the generator keys name,
  calibre and ammunition by the Encyclopedia but builds from the root node), unknown-template, slot-missing
  (Slots, Chambers or Cartridges of the parent), required-empty (required `Slots` only). Parent ids compare as
  `MongoId` (case-insensitive like `BuildEntry`), slot ids ordinally. Deliberately NOT rules: duplicate slots
  (vanilla `saiga12_nerf` repeats a stack slot 20 times), slot filters, a part cap.
- **Which presets are weapon candidates.** The generator resolves a preset's weapon from its Encyclopedia, else
  from its first non-null parentless node, else from the node `Preset.Parent` names (the declared root part), so
  a weapon preset whose root node was damaged (null element, no parentless node) is reported with a reason
  (`no-parts`, `no-root`) instead of vanishing. What still cannot be attributed to a weapon (no Encyclopedia and no
  usable root node at all, or a null preset) is not reported: it is not known to be a weapon.
- **Second net:** each preset, candidate and entry is built in its own try; the rung number follows the pool
  position, so a skipped candidate leaves no hole. One Warning per start (first three, details at Debug); the
  report has its own try so it can never discard the pool.
- **`base-invalid` is the later, softer net and stays as it is.** `CreateCatalog` keeps an entry whose base fails
  `TryValidate` byte-identical and only reports it, and it also serves `kits.json` (authored, shown in the kit
  UI): dropping entries there would silently shorten the player's kit list, and for the Gun Game it would undo
  the draw's guarantee of `rungs` entries. Pinned by `tools/WeaponVariantChecks/Program.cs` ("invalid base left
  exactly unchanged"). With the guard in place the structural defects of a generated preset no longer reach it.
- **Proof that valid presets are not lost.** On the installed vanilla database (SPT 4.1: 272 weapon presets,
  148 weapons) the guard rejects none of them (`GuardRejectsNoVanillaWeaponPreset`, with population floors) and
  `GuardDoesNotChangeTheVanillaPool` compares the guarded with the unguarded pool (node ids normalised by first
  appearance). A one-time golden comparison against the UNTOUCHED generator, taken before the change over the
  same database files, gave the same normalised-pool hash before and after (pool of 139 entries):
  `F02752ED57CFDD69D32B6AA53B257F3E0704FF6A52102EA8E8415FB339F36681`, with `items.json`
  `5093B348FD0D507CDDC1E957114A77B44FF9358846299CBC98AC5E0AD4967A34` and `globals.json`
  `8EDFD516F4969F97DB7BC7DF6EE727809850CBB7BE1300719C5FD16A02298AC0`. The hash is evidence, not pinned: it
  depends on the SPT data version, and the fixture (no handbook, no English locale) is not the live pool.
- **Injection:** `OneBrokenPresetCostsOnlyItself` injects a preset named "default" (so `Plainer` takes it) with an
  orphan part: guard on gives the baseline pool and one Warning with `orphan:`; guard off gives one entry fewer, a
  `build-threw:KeyNotFoundException` line and dense numbering. `tools/WeaponPoolGuardChecks` holds 33 mutants
  of the guard and of the wiring; the Consider net has no vanilla trigger and is exercised with a template that
  throws when read.
- **Not covered offline:** presets that content mods add at server load. Native acceptance is one server start
  with all mods (the Warning, if any, must name real defects) and one Gun Game raid to the first promotion.

## Checks and remaining runtime gate

`tools/WeaponVariantChecks/Verify.ps1` links the actual generator and LadderPicker,
runs synthetic contract/weighted-draw checks and real shipped/installed catalogue
coverage across fixed seeds, then proves a deliberate filter-bypass mutation fails
at the incompatible-slot assertion. The original production checks run again after
that negative control. The harness independently verifies every added real attachment
against its database parent's explicit whitelist and preserves all original nodes.
`ArenaMode.Tests/WeaponVariantTests.cs` also covers stable real draws, metadata/item
preservation with only the declared cover exception, missing parents/duplicate
identities and malformed-source fallback. Its executable lifecycle case follows
Armed preload -> native start -> config reload -> two active sessions/redraw ->
native end/cancel/new arm. A separate case calls the production typed
`TemplateTable -> JsonSerializer -> variant index` adapter and compares its output
to the raw database rule fixture while keeping every unrelated table unavailable.

The command prints actual per-catalog optic counts, improved models, unchanged builds
and their reasons. The root's serialized `Verify.ps1` run passed on 2026-09-06:

- 22,982 production-linked harness assertions; the deliberate incompatible-slot
  acceptance mutation failed at its intended assertion, followed by a clean production rerun.
- All 53 category identifiers confirmed within the local native `JsonTypes.TypeTable`.
- Shipped and installed twenty-kit catalogues: 20/20 builds with optics for **each**
  of four fixed seeds (80/80 per catalogue), including the recorded Spartan seed 1.
- Shipped fallback ladder: seed 0 improved 31 -> 113 of 139 weapons with optics;
  82 distinct weapon models improved across four seeds, totaling 328 upgrades.
- Five actual server tests passed, zero skipped, including the typed template adapter
  and Armed/preload/native-start/config-reload/multiple-session/end/cancel lifecycle.

The weapon tree/cover and server lifecycle/typed-adapter reviewers reported no remaining
P0-P2 findings after their requested fixes. The implementation worker did not build,
deploy or commit; root owns aggregate regression, deployed artifact verification and
the separate weapon commit. These offline results are not a GPU raid result and the
139-weapon coverage measurement is the shipped fallback pool, not a claim about every
possible live content-mod weapon.

GPU handoff remains required: aim through the generated optics, inspect mount/eye
relief and ergonomics on actual third-party models, complete a Gun Game ladder/redraw,
respawn through several kits, and check ordinary raids and protected PFT entry gear.
