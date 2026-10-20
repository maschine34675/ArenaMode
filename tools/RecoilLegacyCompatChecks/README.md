# Recoil Legacy compatibility checks

Run `pwsh -NoProfile -File tools/RecoilLegacyCompatChecks/Verify.ps1` from the
repository. Pass `-PublishedDll` if the unmodified official 2.0.0 client is not
already in the ignored investigation directory.

The executable links all three production adapter files. It supplies minimal
Unity, EFT, Core and Harmony boundaries plus a dynamically generated foreign
assembly with the reviewed public identity. Calls execute production prefix,
finalizer, lifecycle and cache logic; the boundary does not install Mono
detours or emulate the full game.

The fixture deliberately retains the upstream singleton's whole-GameObject
destruction behavior and previous-controller recoil bug. Positive/negative
cases cover initial loading, repeated swaps, pending/retired updates, cache
rollback, partial creation failure, old/new body recovery, ordinary raids,
unrelated ownership, expired world, weak late-call tombstones and admission
contract failure. Cecil separately verifies the published DLL hash, identities
and critical IL assumptions without loading it into the harness.

`Verify.ps1` changes isolated copies of production sources under ignored
artifacts. Four mutations must fail specific behavioral assertions, not merely
fail compilation. It finishes by rerunning unmodified production code. No
client build, deployment, config mutation or native game execution occurs.
