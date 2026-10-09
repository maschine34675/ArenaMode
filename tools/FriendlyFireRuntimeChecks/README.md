# Friendly-fire runtime checks

Run from the repository root:

```powershell
dotnet run --project tools/FriendlyFireRuntimeChecks/FriendlyFireRuntimeChecks.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/FriendlyFireRuntimeChecks/Verify.ps1
```

The baseline links the production decision wrapper, weak physical roster, both
PFT mode partials and the explosion context/filter patches. Reflection executes
the actual explosion Prefix/Finalizer and flash/blast Prefix methods. Native
Unity/EFT types are boundary doubles; this executable does not load the game.
Verify compiles named mutations of those linked production files, requires the
specific expected assertion failure (not a compiler error), then restores and
reruns the unmodified baseline. Logs and mutant inputs stay under ignored
`artifacts/friendly-fire-checks/<run>/`; no installed DLL is deployed.

Coverage includes frozen ON/OFF decisions, exact current victim and historical
source bodies, both Tactical teams, human-only Zombies, own replacement versus
another teammate, invalid roster admission, old victim generations, match/world
scope, native explosion argument copies, nested/suppressed/exception cleanup and
one diagnostic for actual local-attacker damage-hook vetoes. Effect candidates
and bot attacks must not consume that diagnostic.

The existing TDM relation is a delegation boundary here; run
`TeamDeathmatchRuntimeChecks` for its actual weak-body affiliation tests. The
three Zombie harnesses also link the real shared roster. `ZombieTeamChecks`
executes the actual initial and rebuilt wave Live transition bindings. Tactical
binding and the four CombatPatches integration points are source contracts here.
ArenaSession's full capture/cleanup pipeline is outside this harness: the frozen
bool is modeled, with menu/source and native separate-raid checks owned by the
parent task. See `FRIENDLY-FIRE-REVIEW.md` for native paths and explicit limits.
