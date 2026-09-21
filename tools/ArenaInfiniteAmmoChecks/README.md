# Infinite-ammo offline checks

Run `pwsh -NoProfile -File tools/ArenaInfiniteAmmoChecks/Verify.ps1` from the repo.
The suite source-links the actual shot adapter and powerup state. Explicit native
boundary doubles exercise chambered and chamberless shot data, retained ammo,
fresh detached clones, expiry, callback races, unsupported weapons and exact
hands/operation identity. Four compiled mutants must fail their named assertions.
Installed Assembly-CSharp IL checks pin the hook and its native callers/fields.

The shared runner also supports `-Mode Backpack`; that suite has a separate wrapper
and source project under `../BackpackReloadChecks`. Builds and fault copies live in
unique ignored `artifacts/inventory-extension-checks` directories. Source and installed
binary hashes must stay unchanged. No plugin deployment occurs.

This is not Unity/Harmony/Mono execution or native gameplay acceptance. The ID-source
double models native MongoID's implicit string conversion; installed IL pins the
real return type and the normal client build checks compilation against real EFT.
Actual powerup life/world admission is tested separately by ArenaPowerupChecks;
the shot harness uses an explicit boundary for that adapter. See PICKUPS-REVIEW.md
for native limitations and the GPU acceptance checklist.
