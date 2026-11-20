# Audio container guard checks

Run `pwsh -NoProfile -File tools/AudioContainerGuardChecks/Verify.ps1` from the repo (or the harness through
`tools/Run-ArenaVerification.ps1 -Harness AudioContainerGuardChecks`). Nothing here builds into or touches the game.

The program compiles the production `ArenaAudioContainerReturnGuardPatch` and `AudioContainerReturnPolicy` against the
stand-ins in `Stubs.cs`. The stand-ins copy the game's shapes from the decompile: `PoolWithoutAlloc<T>` hands out the
lowest occupied slot and fills the lowest empty one without looking for the item, and
`AudioSourceContainerData.ReturnContainer` clears the container and puts it into that pool. The stand-in
`ReturnContainer` is patched with real Harmony (Lib.Harmony 2.3.3, so the project targets net8.0: that Harmony version
fails on the net10.0 runtime), so the prefix's binding and its skip of the original are exercised for real.

Covered: the stand-in pool reproduces the game's bug without the guard (one container pooled twice and handed to two
owners); in the exact active raid a second return of a pooled container skips the game's method (no second `Clear()`, no
second slot) and logs one Debug line with the container's id, and two draws afterwards hand out two containers; a first
return, a return of a container drawn again in between and a return of a distinct container that compares equal all run
as in the game; every case outside the exact active raid (inactive session, another game or world, another or absent
world or game singleton) runs as in the game, and an inactive session reads none of the game singletons; a null
container and a container data without a pool are left to the game; an exception inside the prefix lets the game's method
run and is reported once per raid, `Reset` arms the report again; the policy (reference identity, every slot, nulls); the
Harmony target, the bool return, the `__instance` and by-value `__0` binding, `EnablePatch` never throwing, and the lines
in `Plugin.cs` and `ArenaRespawnDriver.cs`.

Not covered: the game's own callers (`ClearPlayerOcclusionWithDelay`, `method_26`/`UnregisterSourceContainer`), the
limiter that arms a container, whether the game's JIT inlines the two-call target into a caller (the native Debug line
of a skip is that proof), and the return the game nests inside another return of the same container at the raid's end
(`Dispose`), which the guard does not catch by design (see the patch's doc comment). The `Instantiated` tests of the two singletons are equivalent to the exact-raid test that follows
them (a missing singleton reads null there too) and have no mutant; neither has the `__instance == null` test, which
Harmony never meets for an instance method.

`Verify.ps1` runs the baseline and then the mutants through `Test-ArenaSourceFaults`; each must fail the assertion it names.
