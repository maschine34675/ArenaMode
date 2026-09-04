# Arena magazine handling checks

Run from the repository root:

```powershell
dotnet run --project tools/ArenaMagazineChecks/ArenaMagazineChecks.csproj -c Release
```

The harness links the production timing policy and Harmony patch source against
small stand-ins. It also reads the installed SPT 4.1 `Assembly-CSharp.dll` with
Mono.Cecil to verify the two named process constructors, Harmony-bound parameter
names, timing-field assignments and identity fields.

It covers the 1–4x multiplier, 25 ms constructor floor, load and unload paths, idempotent
registration with plugin-owned Harmony IDs, cleanup after both normal shutdown and a
partial ModulePatch enable failure (including a failed first cleanup), recovery from
stale lifecycle state after a partial disable, re-enable, ordinary-raid and
actor/controller isolation, stale controller rejection after respawn, plugin shutdown,
configuration wiring, and the ban on editing global timings, item templates or skills.

For mutation checks, point `ProductionTimingSource` or `ProductionPatchSource`
at a temporary source copy outside the repository. The offline harness does not
execute EFT's asynchronous inventory operations or render their progress bars;
those remain part of the GPU-equipped live test.
