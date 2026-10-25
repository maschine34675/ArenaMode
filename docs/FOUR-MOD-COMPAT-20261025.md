# Four compatibility candidates — 2026-10-25

Scope: Tarkova-Cola, Fast Grenade Throw, Fontaine's FOV Fix and Manimal's Ammo
Loading Animations. Native checks use SPT-GPU-1, SPT 4.1.6, Factory daytime and
protected supplied-kit Deathmatch. GPU-2 is reserved for another session.

## Baseline observations

The baseline loaded Arena 0.2.1 at `7119f1f` and Redeploy.Core 1.0.10 at
`44adc56`. Temporary test-only user-whitelist entries admitted the candidates;
those entries are not the shipped compatibility policy. Manimal and WTT
CommonLib were already installed and must remain installed after cleanup.

| Candidate | Exact reviewed version | Baseline result |
|---|---|---|
| Fontaine FOV Fix | 4.1.0 | Native failure after death: current replacement aims, but the controller still reads the dead predecessor. With unmagnified ADS multiplier 0.7, camera FOV remains 50 instead of the pre-death 35. |
| Manimal Ammo Loading Animations | 2.0.0, client and server | Actual detached STANAG packing creates a process-static session per physical player. Two packed bodies leave two sessions after respawn and menu return, retaining destroyed players and their magazines. |
| Fast Grenade Throw | 2.0.0 | A real quick-throw key selects and consumes an RGD-5 while the native selected grenade is Arena's Molotov. A throw interrupted by death did recover, and the next body could throw. |
| Tarkova-Cola | 2.2.1 | Released-binary review found an external achievement/reward path outside Arena's profile restoration. No live reward payout was deliberately triggered. |

FOV and Manimal corrections belong to **Redeploy.Core**, shared by Arena and
standalone Redeploy. Arena must not grow duplicate body-replacement adapters.
Core's optional integrations must stay inactive when the corresponding mod is
absent and must not claim ordinary players until Core owns a replacement.

## Why Tarkova-Cola is not admitted

The released 2.2.1 code intentionally supports Arena Zombie perk progression.
Its Arena backup, however, covers Classic/Eternal perk ownership rather than
achievement state and pending claims. Drinking Stamin-Up can unlock `st_fresh`,
whose ordinary reward is 20,000 RUB. After leaving the raid, the client flushes
the pending claim through `/tarkovacola/ach/claim`; the server accesses the normal
profile, adds achievements and sends rewards without checking Arena ownership.
Completed achievements also persist in the mod's external profile file, allowing
claims to survive process restart. Setting Zombie progress to zero does not
disable this generic achievement path.

This is a source/IL finding, not an observed native payout. Sixteen checks against
the exact released client/server verified the relevant call chain, including a
positive control which detects an existing Arena guard elsewhere in the mod.
It is separate from Arena's own optional, explicitly controlled reward system.

## Why Fast Grenade Throw is not admitted

Its selection routine takes the first item from its own sorted throwable list
and does not consult `Equipment.TopPriorityGrenade`. The native test confirmed
the resulting Molotov/RGD-5 mismatch. No respawn correction is justified by the
interrupted-throw test: the static throw flags cleared and subsequent throwing
worked. Countdown/input-state edge cases remain unestablished.

Neither this review nor a missing whitelist entry globally disables these mods
in ordinary PMC/Scav raids. The review does not add a default deny or change a
user's explicit allow/deny choices.

## Accepted fixes and native retest

FOV Fix 4.1.0 and Manimal 2.0.0 (both client and server) are added to the shipped
whitelist with the fixes in **Redeploy.Core 1.0.11**. They are not bundled.
The accepted Core is `a061c78`, SHA-256
`73016DBEC5CC1C45F7AFDDAE2FA2B95CAFE457D1D2F2F8C8BE99BDA40EEFFD73`.
Disk and loaded-process identities were checked after a fresh game start.

In the final Factory Deathmatch retest:

- Two real deaths produced distinct replacement bodies. ADS stayed at FOV 35
  with multiplier 0.7; enabled FOV Scale 0.85 also survived the second death.
- Actual detached STANAG packing displayed Manimal's hand/magazine animation
  on two Core-owned bodies. One death interrupted the active custom animation;
  the replacement had no old player sessions and could aim/use its firearm.
- The second magazine reached 30/30, restored the firearm controller and
  allowed two real RD-704 shots. Five iterator-factory stamps and zero unknown
  owned iterators were observed.
- Menu return left zero Manimal sessions and no FOV cached player. No warning
  or error appeared from raid start through the retest's menu return.

The earlier candidate `859d694` is deliberately **not** accepted: it removed
old sessions but suppressed the visible animation on replacement bodies.
Installing tiny iterator-factory hooks before their callers corrected this;
Mono inlining is the supported explanation, not a directly inspected JIT trace.
Successful ammunition transfer alone was insufficient acceptance evidence.

Core and its driver built without warnings/errors; **237/237** Redeploy tests
passed. The shared lifecycle harness passed **19 cases / 389 assertions** and
caught **9/9** deliberate mutations. Arena's suite passed **2,861** tests with
one known APBS skip. No Arena body-replacement adapter was added.

The ordinary PMC/standalone Redeploy control had Arena installed but inactive,
and exposed the first candidate's animation regression. It also confirmed
ordinary-body FOV rebinding in the same process. The final correction was
retested through Arena's Core replacement path; a separate installation without
the Arena DLL, every optic, PFT, Fika and all delayed-operation races were not
tested. These results are bounded compatibility evidence.

## Cleanup and deployment

Temporary FOV Fix, Fast Grenade Throw and Continuous Load Ammo test DLLs, their
generated configurations and the temporary user whitelist were removed from
GPU-1. Manimal's pre-existing client/Fika/server files and WTT CommonLib remain.
The temporary test directory is absent locally and on both GPU machines; no
GPU-2 process was controlled. The original GPU-1 synchronization exclusions
were restored byte-for-byte. Arena's effective configuration and the concurrent
canonical Arena client build were restored; no unrelated source edits were
built or staged by this task.

The final profile has 425 items versus 426 before all tests. Compared with the
initial archive, only the equipment root, pockets and lost dogtag changed;
the original knife and secure container point to the new equipment root.
Those changes already existed after the intentional ordinary Redeploy raid.
No test weapon, magazine, ammunition or virtual Manimal bundle remained after
the final protected Arena retest. Ordinary death/raid statistics and health are
expected to differ; no whole-profile backup was restored over this progress.

The exact accepted Core DLL and the reviewed whitelist were copied to the
local installation and GPU-1 and their hashes verified. The shipped-policy
SHA-256 is `4707D5827762395B8908FEEAA10149197803FEC5CBD3F9AECB3D935D89E06CFD`.
Game and server were stopped after testing, matching their initial state.
This is a development deployment; no GitHub release was created.

## Inputs and reproducibility

- [Tarkova-Cola 2.2.1](https://github.com/Yendaxddd/TarkovaCola/releases/tag/2.2.1), source `163bb9b1192d050b9e0da5aada8686e8c43877dd`.
- [FOV Fix 4.1.0](https://github.com/space-commits/SPT-FOV-Fix/releases/tag/v4.1.0), source `e3ece13848e1dbcb8a8979b97889f454fc19c629`.
- [Fast Grenade Throw 2.0.0](https://github.com/Vultify/FastGrenadeThrow/releases/tag/v2.0.0), source `f6e123a4bfd73c5ac7a28d4bf1193c13005e4a8f`.
- [Manimal 2.0.0](https://github.com/danauraborealis/ManimalAmmoLoadingAnimations/releases/tag/2.0.0), source `c8ff9619a51622ca3b4ecf3fe39dc09b3ee624c3`.

Released client DLL SHA-256 values:

| Candidate | SHA-256 |
|---|---|
| FOV Fix | `025A7816CAAF0756050F58202459C76D1392349D890038424BB465619B549B4F` |
| Manimal | `6AE333653DEE5570FFC52B4E3D5A5D658D4C9FD45F74BD0A8D2A8420F9AB5949` |
| Fast Grenade Throw | `1CF1FA49DDA94E9936053043E171A5EE1C2AAB6A9C6A696A7B532235A8D83AE4` |
| Tarkova-Cola | `418FB00CDCF6EE92E9C077DD4AF81723C3ED1F1BDEAB029D9C253E7D841839F6` |

Local detailed evidence is retained under the isolated checkout's ignored
`artifacts/compat-four/`: package identities/decompilation, source reviews,
profile/config backups, temporary-install manifest and GPU snapshots. The native
baseline is `gpu/baseline-evidence.json`, `baseline-menu.json` and
`baseline-status.json`. Foreign DLLs and profile backups are not distributed.
