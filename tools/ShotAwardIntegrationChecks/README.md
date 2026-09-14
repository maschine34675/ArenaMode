# Shot award native adapter checks

Run `pwsh -NoProfile -File tools/ShotAwardIntegrationChecks/Verify.ps1` from the
repository. The default SPT path is inferred from the repository's Development
layout; use `-SptPath` for another installation.

The executable links the complete production `ArenaShotAwardHooks`,
`ArenaShotAwardHistory`, and `ArenaHitHistory` files. Explicit boundary doubles
provide game state and hook registration only. The checks execute real firing,
creation, impact, current-contact, and lethal-conjunction code; they do not copy
the award policy. Private Harmony prefix/postfix/finalizer adapters are invoked
directly to verify target selection, cleanup, and exception preservation.

Coverage includes the complete aim/ground/bolt condition matrix, exact local
Arena admission, late landing and weapon switching, same-profile replacement
bodies, wrong shot/cartridge/fragment/weapon/fire/source identities, direct lethal
bullet conjunction, nested contexts, copied fragment facts, pooled reuse during
impact, raid and round resets, and native exception cleanup. Five mutations are
compiled into scratch copies; each must fail its intended behavioral assertion.
The clean production suite passes before and after those controls.

`Verify-Native.ps1` reads installed Assembly-CSharp metadata/IL with Mono.Cecil.
It verifies the actual controller owner, aim and bolt fields, emission and pool
contracts, `ClientLocalGameWorld` inheritance, the non-chaining
`ClientGameWorld.ShotDelegate` override, and the synchronous damage/death path.
Separately labeled source checks bind the new hooks to `CombatPatches` and the
award-round reset in `ArenaMatch`. Those outer production classes are inspected,
not compiled into this small harness.

The verifier records source hashes, native assembly hash, build/run logs, and
the installed Arena client/server/Core hashes under ignored
`artifacts/shot-award-integration/`. It fails if any bound source or installed
binary changes during verification. It never builds the client/server project,
deploys a mod, starts the game, or changes installed files.

Passing demonstrates executable adapter behavior and native/source contracts.
It is **not Unity/Harmony runtime, raid, timing, audibility, or deployment proof**.
The user still needs the separately bound native tests after integration.
