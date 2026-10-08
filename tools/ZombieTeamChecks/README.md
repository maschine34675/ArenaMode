# Protected Zombie team checks

Run `pwsh -NoProfile -File tools/ZombieTeamChecks/Verify.ps1` from the repository.
It stages source copies under ignored `artifacts/zombie-team-checks/`, runs the
production source, then compiles and executes eleven deliberately broken variants.
A compilation failure is not accepted as detection of a mutant.

The harness links the real `ZombieMode`, `ZombieTeamRuntime` (including exact-body
validation and respawn authorization), `ZombieWavePolicy` and `ZombieRefillPolicy`.
It checks initial four-companion readiness, an empty infected roster, live
hostility admission, bounded observation, local death and ally-only spectating,
team wipe and protected stop, complete wave restoration with a surviving or dead
leader, single-use prepared respawn, body/epoch drift, replay ownership, pending
final infected, paused intermission, and unchanged solo own-gear refill.

EFT, Unity, the PFT reflection adapter, native inventory deserialization, audio,
camera rendering and native profile persistence are **stubbed boundaries**.
These checks cannot prove native PFT activation, a usable spectator camera,
correct restored gear, or that the profile files remain unchanged. Keep the
existing PFT identity/loadout/lifecycle suites, staged real client build and a
native two-wave test (including local death, follower death and total wipe) as
separate gates. Unity's destroyed-object equality is not reproduced here.

The manifest records the source commit, assertion count and compiled mutant
count; when the source tree is dirty it is not an installed-build identity.
