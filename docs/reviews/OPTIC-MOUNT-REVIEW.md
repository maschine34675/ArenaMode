# Empty scope-ring reflex paths

## Cause and scope

The live item database makes the main optic slot of several scope rings optional.
JP 30 mm (`5a37ca54c4a282000d72296a`) accepts seven tube OpticScopes in
`mod_scope_000` and a DeltaPoint (`58d268fc86f774111273f8c2`) in secondary
`mod_scope_001`. Burris PEPR (`5b2389515acfc4771e1be0c0`) similarly has a
primary tube scope slot plus two reflex/adapter rails. Nightforce 34 mm multimount
(`5aa66c72e5b5b00016327c93`) and ERA-TAC 34/30 mm
(`61713cc4d8e3106d9806c109`, `6171407e50224f204c1da3c5`) follow the same pattern.

Geissele black/DDC (`618b9643526131765025ab35`, `618bab21526131765025ab3f`)
has an optional tube scope slot and a REQUIRED cap slot. A Reptilia ROF90 cap
(`618b9682a3884f56c957ca78` or `618ba92152ecee1505530bd3`) satisfies that required
slot and supports a reflex sight itself, without a tube scope occupying the ring.
Thus the old chain passed native compatibility and required-slot checks while
leaving an inappropriate empty ring below the only optic. This is a generation
quality rule, not a claim that EFT rejects those assemblies.

Source contracts: live `SPT_Runtime/SPT_Data/database/templates/items.json`;
`../_decompiled/EFT.InventoryLogic/ItemFilterExtension.cs:19-69` (all filter records,
exclusions), `ItemFilter.cs:52-69` (exact template/native type), and
`JsonTypes.cs:766-787` (distinct optic classes). Existing native validity checks
remain untouched, including required slots, ancestry, conflicts and ammo.

## Fix

The generator identifies a dedicated primary ring socket when its nonempty set
of actually permitted Item templates contains ONLY the native OpticScope class.
It uses the existing candidate/filter resolver, including all record intersections
and exclusions, without filtering out long scopes just because Arena does not
generate them. The classifier is cached on the immutable local index. It does
not infer ring geometry from localized names, zoom alone, mount size or IDs.

A new ring can be traversed through its primary scope socket, but not through a
secondary rail/cap while the primary socket is empty. Existing anchor paths are
checked up through every mount ancestor, so a preinstalled ROF90 cap cannot hide
an empty parent ring. A primary scope must be directly in the relevant mount's
socket; an unrelated optic elsewhere does not qualify. These checks run before
weighted selection and before useless new-mount branches consume search budget.
No extra random draws were introduced; identical source+seed remains stable.

Direct reflex, universal rail, T1/RM33 and MPR45 offset mounts remain eligible.
AssaultScope/prism adapters such as Kiba (`5c1cdd302e221602b3137250`) are not
blanket-classified as tube rings. Mixed slots with a real reflex/adapter alternative
are deliberately left alone; an empty or fully excluded candidate set provides
no evidence of a primary scope socket. This is a conservative item-data rule,
not a universal model/geometry analyzer for arbitrary future content mods.

Existing authored optics and unusual assemblies are preserved exactly. Native
TryValidate is not made stricter, catalog files/prices/profile data are not changed,
and the existing plain-cover exception, session freeze/preload and Gun Game
pipeline are unchanged. The bounded linear chain builder still does not invent
the Geissele two-child scope+cap build; it chooses another valid route or keeps
the source unchanged with existing coverage diagnostics.

## Evidence and checks

`OpticMountChecks` runs explicit synthetic counterexamples for new rings, authored
caps, a filled local primary scope, an unrelated scope, direct/offset routes,
prism and mixed/empty/excluded/multiple filter cases. Native-valid authored
empty-ring reflex assemblies remain valid and unchanged. The separate concrete
DB oracle records the seven verified main-slot contracts and checks every new
reflex sight's ancestors against them, without calling the production classifier.

The real mount tests use controlled weapon anchors with unmodified actual mount
subtrees and filters: seven ring designs plus T1, RM33, MPR45 and Kiba, across
12 seeds. Full shipped/installed kit and Gun Game draws also audit newly added
reflex paths, preserve authored nodes/ammo/metadata and validate complete trees.
The existing weighted distribution/determinism/coverage checks are retained.
An actual server xunit regression independently isolates the real JP/DeltaPoint
contract over 16 seeds and preserves its intentionally authored counterpart.

`Verify.ps1` retains the native-slot mutation and adds two targeted mutations:
bypass the empty-ring branch rule, then independently bypass authored-ancestor
checking. Each must fail at its named assertion. The script restores production
and reruns the normal harness/server tests afterward.

Read-only independent DB, production and final oracle review found no open P0-P2 findings.
Root executed the complete O1-G1 successfully (exit 0, expected marker): the
original native-filter mutant and both new ring mutants were rejected at their
named assertions, followed by a passing production recheck and all actual server
WeaponVariantTests. Existing real kit/Gun Game coverage and direct/offset/prism
positive assertions remained green. Root owns the final integrated build,
deployment verification and separate commit. No GPU raid/rendering result is
claimed.
