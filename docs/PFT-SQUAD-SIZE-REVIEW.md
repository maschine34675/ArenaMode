# Variable protected PitFireTeam squads

## Contract (2026-10-13)

Arena admits one to four ordinary authenticated PFT companions. Tactical keeps
five opponents: the friendly team contains the PMC and the selected companions.
Zombies keeps its existing cooperative survival rules. Zero companions remains
solo, unsupported versions/special followers/mixed groups remain rejected.

The account-to-stored-profile mapping and its count freeze before arming. Every
later body, activation lease, teardown, rebind, spectator and presentation roster
must exactly match that frozen count and identity set. A partially arrived or
newly smaller group must never become a new complete roster. Existing world,
epoch, body, persistence and prepared-respawn guards remain mandatory.

Area capacity and the protected server transaction stay unchanged. Tactical still
needs five enemy starts. Equator's existing PFT exclusions remain in place.

Zombie defaults become 32 alive, 50 first-wave infected, growth 20, own gear.
Saved user settings are preserved. Solo replenishes ammunition each wave; PFT
reconstructs companions but leaves a surviving PMC's equipment and ammunition
unchanged. Menu text must distinguish these paths.

## Acceptance checks

- Selection accepts exact authenticated groups of 1, 2, 3 and 4; rejects missing,
  duplicate, extra, mismatched, special and unsupported members as before.
- Lifecycle/activation readiness uses exactly the frozen count, including native
  group creation (companions plus leader), stable observations and retirement.
- Tactical retains five enemies. Zombie assembly, wipe and subsequent wave
  reconstruction preserve the frozen companion count and local respawn lease.
- Spectator, friendly fire and tracer consumers reject incomplete/mismatched
  rosters; ordinary raids and solo behavior remain unchanged.
- Menu defaults/texts and removed redundant area hints have regression coverage.
- Staged build must not modify installed files. Independent adversarial review
  precedes delivery; native verification is reported separately from offline
  evidence, including any untested death/rebind/persistence scenarios.

## Offline evidence and review

The client and server staged build passed without changing installed DLLs,
settings or audio; server/Core code remains unchanged. Independent reviews of
selection/coordinators and lifecycle/consumers/UI found no remaining P0-P2 issue.
The review caught fixed Tactical HUD/log denominators, corrected to the frozen N.

Production-linked checks: selection 340; lifecycle 500 plus a compiled old-four
regression; Zombie coordinator 112 plus 11 compiled mutants; tracers 313 plus 15
mutants; friendly fire 229 plus 38 mutants; team expansion 892; spectator health
49; menu 2328 plus 35 mutants; native mode selection 160; area preview 4424 plus
5 mutants; area menu/native hooks 640/195/66 plus 5 mutants; area config 259 plus
2 historical controls; participants 3730; bounded PFT placement 234; give-up 626;
assembly notice 50 plus regression controls; solo Zombies 85 plus 13 mutants. Pure policies and native test doubles
cannot establish actual reflected PFT activation or in-game animation behavior.
Native results and the final delivered build identity are recorded separately.

## Delivered identity

Implementation commit `8b5c772`, rebuilt from a clean worktree. Client SHA-256:
`786C6643A53C0CB4830F54598E71C33BA1EAE7D3AF808ABAEBEE4AD5B3824377`.
Delivered to the local installation; server and Core hashes remain unchanged.
The full unit suite passed 958 tests with two skips (960 total), with server build
output redirected into artifacts and the installed server hash verified unchanged.
Client build has four existing nullable-annotation warnings; server build has the
existing nullable UnionWith and MudTabs PanelClass warnings, no errors.

See [native squad test instructions](GPU-PFT-SQUAD-TEST-INSTRUCTIONS-20261013.md)
for pending real PFT gameplay acceptance. GPU1 can verify menus but its designated
profile has no configured PFT companions; no other profile/store was copied.
