# Gates: PitFireTeam protected multi-round 5v5

Scope: extend the already verified PitFireTeam 0.10.0 Tactical 5v5 path to a
configurable first-to-N match. Between rounds Arena must retire both exact old
bot rosters, rebuild PitFireTeam around the surviving or respawned PMC, adopt
four fresh companion bodies, and spawn five fresh opponents. A dead PMC receives
the process-local raid-entry loadout; protected inventory roots remain those of
the just-dead runtime profile. Ordinary raids and solo Arena remain unchanged.

The original one-round milestones remain recorded in `PITFIRETEAM-GATES.md`.
These gates supersede that document only for the new multi-round behavior.

## Offline gates

- [x] R1: Best-of-N scoring increments exactly once, continues below the frozen
  threshold, ends at the threshold, and preserves the score on a technical
  no-contest.
  CHECK: `dotnet run --project tools/TeamArenaExpansionChecks/TeamArenaExpansionChecks.csproj -c Release`
  EXPECT: `TEAM_ARENA_EXPANSION_CHECKS_PASSED`

- [x] R2: The transition order is monotonic: exact roster snapshot, PFT teardown,
  old follower and opponent retirement barriers, leader rebuild, profile preparation,
  follower spawn, two stable registry passes, follower adoption, then a fresh
  five-opponent staging gate. Damage remains blocked until all ten new actors and
  their bidirectional hostility are proven. PFT's preceding asynchronous profile-data
  creation and each of its four detached activation calls are leased to the exact raid,
  round, game, spawner, leader, and runtime profile; abort/reset stops late creation,
  settles every native spawn counter exactly once, and retires every late body,
  including a LocalPlayer returned before EFT constructs its BotOwner.
  CHECK: `dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release`
  EXPECT: `PITFIRETEAM_COMPAT_CHECKS_PASSED`

- [x] R3: A dead PMC's entry snapshot is fully reconstructed before HoldRespawn is
  released. Redeploy receives a one-shot authorization tied to the exact LocalGame,
  corpse, Arena round epoch, PFT lifecycle epoch, and prepared transaction. Its
  equipment callback can only commit that transaction; it cannot select/preload an
  Arena kit or fall through to the ordinary death screen.
  CHECK: `dotnet run --project tools/TeamArenaLoadoutChecks/TeamArenaLoadoutChecks.csproj -c Release`
  EXPECT: `Team Arena loadout checks passed`
  CHECK: `dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release`
  EXPECT: `PITFIRETEAM_COMPAT_CHECKS_PASSED`

- [x] R4: Spectator and pursuit leases are limited to the exact nine non-local
  current-round identities. Spectator camera ownership is released before body
  retirement; after local death, both living sides receive refreshed native/SAIN
  opponent knowledge without forced movement, visibility, or retained bot bodies.
  CHECK: `dotnet run --project tools/TeamArenaExpansionChecks/TeamArenaExpansionChecks.csproj -c Release`
  EXPECT: `TEAM_ARENA_EXPANSION_CHECKS_PASSED`

- [x] R5: Server tests, client build, deployment copy, and all dedicated compatibility
  harnesses pass, including the PFT activation-lease, prepared-loadout, team-expansion,
  and Labs-spawn checks. A build alone does not complete any live gate.
  CHECK: `dotnet test ArenaMode.sln -c Release`
  EXPECT: all tests pass
  CHECK: `dotnet build ArenaMode.sln -c Release`
  EXPECT: zero errors and deployed DLL hashes match their build outputs

## Live gates

For the first pass set `Tactical Team / RoundsToWin` to `2`, leave
`SpectatorAfterDeath` enabled, and copy/hash the main SPT profile plus the complete
PitFireTeam teammate store before starting.

- [ ] M7: The Arena selection description shows a first-to-two Tactical 5v5. The
  first round still reaches exactly five friendly actors and five hostile opponents;
  no initial PMC kit/body swap occurs. Opponent levels/loadouts use SPT's native
  PMC-relative generation rather than Arena's level-one solo progression.
  EXPECT LOG: `Tactical team Arena: first to 2`, four authenticated followers,
  exactly five opponent callbacks, then `round 1 live`.

- [ ] M8: Die while at least one ally and one opponent remain. The death screen does
  not open. A third-person camera follows a living fixed participant; LMB/RMB cycles
  only among the surviving four companions/five opponents. Combat continues and both
  teams actively seek known opponents instead of waiting indefinitely. The round ends
  only when one complete side is eliminated or the existing round timer expires.
  EXPECT LOG: spectator pending/following, no camera-ownership error, no pursuit
  contract error, and one round result.

- [ ] M9: Let the team finish a round after the PMC died. After the result delay the
  old camera lease ends, all nine old bot bodies leave active registries, and the PMC
  respawns once with the raid-entry combat loadout. Secure container, scabbard,
  armband, special pockets, quick slots, examined/favorite state, and discard limits
  remain coherent. Four fresh PFT followers and five fresh opponents appear; no old
  profile/body is counted in round two.
  EXPECT LOG ORDER: retiring old bodies; prepared entry loadout/released respawn;
  exact replacement PMC ready; leader rebuilt; follower profiles prepared; four
  followers adopted; five opponents requested; `round 2 live`.

- [ ] M10: Repeat a transition with the PMC alive. The local body and its remaining
  equipment stay in place while both bot rosters are fresh. Followers still obey PFT
  commands, all ten actors have correct ally/enemy relations, and score/HUD advance
  exactly once.

- [ ] M11: Reach two wins and separately two losses. The match continues after the
  first scored round and stops exactly once after the second result for one side.
  Victory uses native `Survived`; a living PMC on overall defeat/no-contest uses
  `MissingInAction`, and a dead PMC uses `Killed`. No extra round begins after the
  threshold.

- [ ] M12: After each outcome, wait at least 35 seconds and compare the saved main
  profile and PitFireTeam teammate store with the pre-raid copies. Both are
  byte-identical and Arena produced no rewards, mail, insurance, quest/progress,
  inventory, death/escape, recruit, or follower persistence.

- [ ] M13: Run one ordinary PitFireTeam raid, one ordinary PMC/Scav raid, and one solo
  Arena raid. Their previous behavior, persistence, kit swaps, respawns, bot tiers,
  and camera flow remain unchanged.

Environment limit: this server has no GPU. EFT must not be launched here; M7-M13
remain open until the deployed build is tested on a gaming system.
