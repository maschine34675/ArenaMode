# Protected PFT 0.10.0 lifecycle checks

Run `dotnet run --project tools/PitFireTeamRoundLifecycleChecks -c Release`.

These source-contract checks target **installed PFT 0.10.0** and Arena's exact
version adapter. They must not read the moving `SPT-PitFireTeam-main` reference:
newer upstream code adds a seventh `AddFollower` argument, among other changes,
and produces a false regression against the unchanged 0.10.0 adapter.

Required external reference folder:
`../_reference/SPT-PitFireTeam-beta-0.10.0` (relative to the ArenaMode repository).
Use the [official beta-0.10.0 release](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/beta-0.10.0),
commit **109760c7a5aad3fdb181941e10ce035c3840bb4d**. To prepare a new folder:

```powershell
git clone --depth 1 --branch beta-0.10.0 https://github.com/pitAlex/SPT-PitFireTeam.git ../_reference/SPT-PitFireTeam-beta-0.10.0
git -C ../_reference/SPT-PitFireTeam-beta-0.10.0 rev-parse HEAD
```

The second command must report the commit above. Do not overwrite the newer
reference tree or change installed PFT. Upstream source remains external and
is neither copied into this repository nor bundled with Arena.

All existing behavioral/source assertions remain unchanged. The corrected
reference input passes **367 assertions**; the former moving input failed on
the six-argument `BossPlayers.AddFollower` assertion. No live PFT raid behavior
is proven by this test harness.

## Variable protected rosters (2026-10-13)

The current harness passes **500 assertions**. It executes the production policy
for each supported count (1–4), unchanged canonical account/profile mappings
across rounds, exact activation/invocation identity sets, missing/extra follower
barriers and both stable physical-observation passes. The synchronous completion
simulation also covers all four counts. Zero and five companions are rejected;
an incomplete selected squad cannot become a smaller valid roster.

Source contracts additionally require the runtime to derive expected counts from
its frozen canonical dictionary, bind every leased Profile object to the exact
prepared account/runtime ID, and demand native group size `leased followers + 1`.
These are source contracts, not execution of Unity or the reflection adapter.

The optional `ProductionPolicySource` MSBuild property supports compiled negative
controls. Replacing only the supported-count predicate with the former four-only
rule compiles successfully and fails the first one-companion assertion; the
unmodified production rerun passes. Evidence is recorded under ignored
`artifacts/pft-variable-roster-lifecycle-20261013/`. Native first-round assembly,
round/wave restoration and profile/PFT-store neutrality remain separate tests.
