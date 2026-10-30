# Native acceptance: smaller protected PFT squads

Target: the GPU2 test profile with its existing ordinary PFT companions. Do not
copy another profile's companions or use a personal PMC. Coordinate the host,
read the UiBridge playbooks, and inspect status/loaded code before acting.

Build: `8b5c772`; client SHA-256
`786C6643A53C0CB4830F54598E71C33BA1EAE7D3AF808ABAEBEE4AD5B3824377`.
Server and Redeploy.Core are unchanged. Compare VM, host disk and loaded MVID;
restart only through gpu-run after synchronization. Never trust the file hash
alone. Preserve original settings in a restoration ledger and snapshot the test
profile and its PFT store privately; raw profile/session data must not be committed.

## Minimum gameplay gate

1. Invite exactly one existing ordinary stored companion through PFT. On Arena's
   map screen, Tactical should say **2v5 PFT (WIP)**. It must be selectable without
   asking for four allies; other PFT mode restrictions remain unchanged.
2. Choose Factory and start Tactical with own gear. Wait for one authenticated
   ally and **five** distinct active opponents before the round-start cue. HUD
   must display Allies `2/2`; incomplete arrival must not prematurely start play.
3. Finish one round with the player alive; verify next round reconstructs exactly
   one companion and five opponents, with new physical bodies and stable account
   identity. No activation, group-size or roster-drift no-contest is acceptable.
4. In a later round, die while the companion remains alive. Verify optional replay
   followed by spectator targets for the one ally and five opponents, Escape,
   then the prepared leader respawn and the same complete next-round roster.
5. Leave through the protected native path. Compare profile/PFT store to baseline;
   explain any normal menu/hideout timestamp change, never waive inventory drift.
6. Invite one companion for Zombies; own gear is enforced and default friendly
   fire is off. Temporarily lower wave size through a saved-settings ledger to
   make a cleared-wave test short. Verify one follower before infected release,
   a surviving player unchanged, rebuilt follower and next wave.
7. Repeat a wave with the player dead and follower alive. No mid-wave respawn;
   after clearance, local restoration precedes the exact one-follower rebuild.
   A full team wipe must end the match rather than replenish either participant.
8. Leave protected, repeat the store comparison and restore all temporary settings.
   Recheck a full four-companion Tactical/Zombie group when time permits. Two and
   three companions should also be admitted with Tactical labels 3v5 and 4v5.

Keep the Equator PFT exclusions. Bounded Customs continues to use surveyed areas,
not a whole-map fallback. Do not manufacture a roster merely to satisfy a test.
Record loaded build, mode/map/selected count, roster-ready/rebind logs, real
screenshots and exact untested cases. Offline matrices already cover counts 1–4,
missing/extra identities, count drift, spectator and damage classification; they
are not native activation evidence.
