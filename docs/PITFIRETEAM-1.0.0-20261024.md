# PitFireTeam 1.0.0 and Arena session version filtering

## Reproduced failure

The reported client log records two refused starts on 2026-10-24. The server's
acknowledgement gives the actual reason:

`PitFireTeam persistence contract mismatch at pitTeam.Server.Services.FriendlyTeammateService.CreateTeammate.`

PFT 1.0.0 changes that method's result from `SearchFriendResponse` to
`FriendlyTeammateBuyKitResponse`. It also replaces companion JSON persistence
with two LiteDB stores (hired and allegiance companions). Merely accepting the
new result type would leave the databases outside Arena's old FileUtil guards.

Reviewed upstream: [PFT 1.0.0 release](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/1.0.0),
tag source `f32a41b`. The compatibility harness uses the published server binary,
real Harmony patches and temporary databases. It never uses live player data.

## Protection changes

- Validate the new transaction and database methods before arming Arena.
- Keep the existing contracts for supported legacy PFT versions.
- Block protected database writes, deletion and wipe for the exact profile.
  Other profiles and ordinary raids retain PFT behavior.
- Open protected companion reads in read-only mode. Lazy initialization may
  use an existing, fully migrated database; it must not create a new one or
  move legacy files into a backup during Arena.
- Protect the new recruitment, insurance, cargo, karma and onboarding paths
  before their profile, cache or mail side effects.
- Protect solo Arena too when PFT is installed. PFT's new karma reporting runs
  without invited companions; persistence protection therefore has its own
  frozen state, separate from team gameplay, kit selection and roster rules.
- Suppress Arena reports on the client before PFT queues them. Retain report
  identity through delayed completion, while a new ordinary raid resumes its
  normal reporting.
- Retain the bounded post-raid protection window and exact-session deliberate
  management scope. Async transaction work must finish before a new Arena
  profile snapshot can be published.
- Release the snapshot write lock while waiting for outstanding async work,
  then atomically reacquire it and recheck the queue. This avoids a lock cycle
  with PFT 1.0's own request semaphore without opening a snapshot gap.

The reviewed-version policy remains at 0.10.3. Structural support for 1.0.0 is
not a promise of compatibility with every future PFT version or configuration.

## Version-filtered Arena session

A version difference remains advisory for direct Arena entry. The menu also
offers a session switch that excludes mods with differing reviewed versions.
The exclusion covers both sides of the same GUID and hard dependents. Normal
installation files and the user's allow/deny policy are not edited.

The switch uses a temporary plan bound to the source policy, effective policy
and restart request. The helper, preloader gate and client must agree on that
plan; an older helper must reject the new request instead of silently ignoring
the version filter. Existing acknowledgement, cancellation, private-profile
and process-identity boundaries remain mandatory.

Notes use mod identity and normalized installed/reviewed versions, so the client
name `PitAlex-PitFireTeam` and server name `PitFireTeam` no longer produce the
same note twice. Different version pairs remain visible.

## Arena-start errors

The concrete server refusal is passed through the exact start attempt, without
a shared last-error cache. EFT's `PreloaderUI` appends inner-exception messages
itself; Arena's outer exception now contains only the protection context.
Cancellation still uses the exact pending arm token, and an unsuccessful
cancellation retains the uncertainty rather than treating the profile as safe.

## Verification

Offline results on the integrated sources:

| Check | Result |
|---|---|
| Full unit suite | 2,317 passed; one existing environment-dependent APBS skip |
| PFT 1.0 protection harness | 97 checks, absent-mod control, 63/64 legacy metadata contracts |
| Existing PFT / round lifecycle harnesses | 370 / 500 assertions |
| Version-filtered session harness | 68 assertions; eight compiled mutants rejected |
| Existing session-client harness | 699 assertions; 171 compiled mutants rejected on their named assertions |
| Windows helper on .NET Framework 4.7.2 | Nine checks; six compiled mutants rejected |
| Start-error harness | 72 assertions; six compiled mutants rejected |
| Native-menu harness | 842 assertions |
| Preloader-gate harness | 17 scenarios, 705 assertions; 40 compiled mutants rejected |

The full Release build succeeded with four existing CS8632 warnings in
`ClientSettingsModels.cs`. Client, server, gate, helper and BigBrain companion
were deployed to the local installation and SPT-GPU-2; all five file hashes
matched. UiBridge confirmed that the running client's loaded assemblies matched
the deployed files. The public version stays at 0.2.0; no release was created.

Native checks on SPT-GPU-2, 2026-10-24 (SPT 4.1.6 / EFT 0.16.9.5, PFT 1.0.0):

| Path | Observed result |
|---|---|
| Normal installation, Tactical on Factory with one invited companion | Round 1 started as two allies versus five enemies. After the local player's death, the replacement leader and one fresh follower passed rebind and round 2 became live with five opponents. No no-contest or Arena/PFT error. |
| PFT persistence after that raid | All four existing hired/allegiance database files remained byte-identical to their pre-raid hashes, including the other profiles. |
| Version notice in character selection | One PFT version note, followed by the optional session action. No duplicated client/server alias. Direct NEXT remained available. |
| Version-filtered restart | The helper started the private server on port 6970. PFT was absent from both the client chainloader and the private server tree. A solo Factory Deathmatch started, spawned opponents and exited normally. |
| Return to normal session | Back to port 6969, PFT loaded again, no version-plan launch flag; the private server stopped. The normal whitelist file remained byte-identical and no user policy file was created. |
| Solo Arena with PFT installed | Factory Deathmatch started and exited; all four PFT databases again remained byte-identical. |
| Ordinary PMC immediately after solo Arena | Factory loaded and the normal raid-end flow completed. PFT's `/singleplayer/pitfireteam/pmc-karma` request succeeded, proving that the preceding Arena report suppression did not stick to the ordinary raid. |

The native runs logged the expected advisory PFT version warning, but no
Arena/PFT errors. Other logs contained EFT weapon `UtilityOperationEvent`,
Factory transit/collider and map-marker messages, plus SPT Fence/profile-lookup
warnings. This work does not attribute those unrelated messages to a new cause.

The test companion was removed from the group, all Arena configuration values
were restored, and the game and both test-started servers were closed. No
temporary test mod was installed. Raw receipts, hashes and temporary databases
are ignored under `artifacts/pft-100-contracts/`; they are not release payloads.

This is bounded native acceptance, not exhaustive PFT certification: Zombies,
larger squads, both companion-management modes, insurance courier delivery and
long matches were not replayed end-to-end. The released 1.0.0 binary and both
database paths are covered by the offline guards; older versions received
metadata regression checks, not new native gameplay tests.

Deployed SHA-256 identities:

| File | SHA-256 |
|---|---|
| `maschine-ArenaMode.Client.dll` | `51E95EC054D3EDDF08F6EB46FBC0146013791E501C800DEA8192B49DA2F69054` |
| `maschine-ArenaMode.Server.dll` | `4CD45C10C83E8A7E1CB3DA8DB2C12FA1CC6015FBF43FF066BABC69EC756771D0` |
| `maschine-ArenaMode.Gate.dll` | `B9AADA213A23287517CF19484A3B4444A43303B62A430798168B79D44AA3430E` |
| `maschine-ArenaMode.Session.exe` | `AADF0E7D49874F51F5B1EF6B238B66127D11305B23B536EC21880DBD2CB5235C` |
| `maschine-ArenaMode.BigBrainCompat.dll` | `22B5AE9B483B54DEC1F5B7097FC576F096C1985523D2115AE072299CDD582C1D` |
