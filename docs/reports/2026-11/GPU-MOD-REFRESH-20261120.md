# Mod refresh: native verification — 2026-11-20

Development after ArenaMode 0.6.0 and Redeploy 1.1.3. This is a scoped
compatibility test, not a release or a claim about every feature of these mods.
The user asked to retain the three updated official mods after testing.

## Reviewed releases

| Mod | Official release | Client SHA256 |
|---|---|---|
| PitFireTeam | [1.1.2](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/1.1.2) | `F0C01D472379ECD3F79D8F799400992093187162E75FB3716A2B6E133891B05D` |
| TarkovaCola | [3.0.1](https://github.com/Yendaxddd/TarkovaCola/releases/tag/3.0.1) | `CF36BC95D165BF771BDAD8E03C02F89CCD6D80F76BF75220000F40C1BB748A4C` |
| VisualAssist | [v1.2.0](https://github.com/SleepingPills/VisualAssist/releases/tag/v1.2.0) | `D0150D2FCC93639933F3075EF80CC303CA3E7E8B6C47539A20488C4FB6AF3EC9` |

Server hashes: PFT `D8E1EC74FD5EC85BF599BBE2886F46F73487623FDA10BE97983E328BC93B5BDC`,
Cola `6F293522ADF19A8F91DC0BB336855E93ABA5C44D2982AF5C15FA90A56B0369B3`.
VisualAssist's plugin version is 1.2.0; its generated assembly revision is
1.2.0.23951. No foreign implementation or DLL is bundled in ArenaMode.

## Candidate identity

GPU-2 used isolated deployment while its Syncthing folder was paused. Its disk
and loaded assemblies were compared through UiBridge. The ordinary VM build
remained unchanged during the offline checks and intentionally differed from
the test host at this stage.

The Arena development candidate was built before its commit and is stamped
`0.6.0+e57c85fe`; a source manifest binds its 814 production inputs across Arena
and Core. Tests and documentation were refined later without changing those
production bytes. All 814 inputs were rechecked with zero differences.

| Component | SHA256 |
|---|---|
| Arena Client | `6CBD35AD0E83B4B35AAEA73A50CBDDE6AC9CE3825298BF0E56DA8B4B648EAC4C` |
| Arena BigBrain companion | `5422A22C33DAEEE54D99918B07E90CF2A8C1A3CD65BA64059584ED31ECD67B43` |
| Arena Server | `8A542D95C4CC3B8B56BB7708BF2FAB5C4E84AD608D92F41E47DA0713B892EEA4` |
| Core 1.0.25 during PFT/Cola tests, precommit | `D9D0885FA6342466605562DC59D949EE3A7FA7CAED0A30D147AC5A39D2F06ACB` |
| Core 1.0.25 rebuilt from commit `430657c` for VisualAssist | `F90C3EBBBBC94FC4341F39F6C0B8B7527F669A57F1977F15B341DEE300516609` |

The two Core builds have the same production source; their build/commit identity
differs. PFT/Cola observations belong to the first binary; VisualAssist observations
and final delivery must identify the second explicitly.

## PitFireTeam 1.1.2

Two companions were invited through the actual MY SQUAD screen. Protected
Tactical on Customs Industrial Side deferred follower dispatch until the bounded
area's navigation was ready, then reached Live with two followers and five
opponents. The next round worked with the surviving leader. A native player
death then continued the match with the squad; the following round created a
new physical player, two followers and five opponents. The replacement completed
a real movement command. Bay5 separately passed the initial two-plus-five spawn
and normal exit. No no-contest, client error or NRE occurred in these scenarios.

After exit the PFT leader was null and the prefetch cache was empty. All 54
captured PFT storage files remained byte-identical. Compared with the
post-startup, post-invitation baseline captured immediately before these Arena
raids, other canonical profiles were unchanged; the selected profile differed
only in the Hideout bookkeeping timestamp. This does not describe changes
between the earlier pre-startup backup and the first menu. The group was cleared
and temporary timing pins restored.

The shortened test round deliberately produced the objective-timer warning.
Four upstream PFT roster-preview requests fell back to the own profile; there
was no corresponding server error or persistence difference. One existing log
message says "all nine old bot bodies" even with fewer invited companions; the
actual roster and retirement counts were correct.

Not exercised: every companion count, Allegiance gameplay or additional Bay5
round transitions. The following Cola scenario additionally exercised PFT Zombies.

## TarkovaCola 3.0.1

Factory Zombies with one PFT follower exercised real Macchiato consumption,
living wave body replacement, native death and the next wave's replacement.
Cola's player reference and Arena lease followed the exact new body without a
compatibility failure. The perk reset on death as upstream intends and could be
enabled again by a second actual drink on the replacement.

Machine count stayed at ten before and after replacements. Individual machine
identity was not established, so this proves no population duplication in the
observed run, not object-by-object retention. Cola's ordinary profile,
`machines.json` and configuration stayed byte-identical; `pit_perks.json` stayed
absent. Against this scenario's pre-raid, post-startup baseline, the full
canonical player profile differed only in the Hideout timestamp. PROFILE
REWARDS was temporarily switched OFF through the real menu and restored to ON;
the post-raid live check recorded `PendingCola=false` and `PendingRoubles=0`.

Native research progress was inconclusive: the unchanged Arena factor requires
four actual knife-controller transitions for one integer increment; the test
did not establish those four transitions or inspect fractional carry. Actual
integer research progress, an unlocked Vulture Aid reroll and a PitPerks editor
attempt are not native acceptance claims. Their catalog, persistence and delayed
reply boundaries have focused offline tests. Optional VisitAPI story content was
not installed as part of this refresh.

## VisualAssist standalone

Core `430657c` and the matching rebuilt Redeploy.Client ran on a disposable
profile with Arena Client, Gate and BigBrain companion removed from discovery.
UiBridge verified the new Core/Client disk and loaded identity. The original
profile was not used for the ordinary PMC raid.

Two native deaths produced explicit body-change events (player 1 to 2 to 3).
After each change, the Core scope and VisualAssist arc referenced the exact new
player, and the previous line and sphere were destroyed. The replacement could
walk and equip fresh grenades. High/low previews were visible; switching assist
OFF disabled both renderers and cleared prediction, and ON restored it. Actual
regular low and high throws completed. The low throw has visible flight evidence;
the high throw immediately met a nearby wall, so no high-throw range is claimed.
After normal exit, Scope.Body and the published arc were null and the scope was
disabled. No error was recorded in the test window. The TEDD marker was a Cola
trial NPC, not evidence of an invited PFT squad.

## Offline verification

`Run-ArenaVerification.ps1 -ChangedSince e57c85fe` completed with exit 0:
74 successful steps (73 harnesses and the unit suite), zero failures, 38 results
reused only because their recorded inputs were unchanged. The suite passed
4,413 tests; three existing environment-gated cases were skipped (two historical
reward-recovery baselines and the installed-APBS native pipeline). Live installed
files remained unchanged throughout verification.

The new release fixtures are registered inputs and mirrored into workers. The
final run also passed the four initially failing test-infrastructure cases:
the VisualAssist admission double, shared-cache fixture arrangement and two
checks that require the current PFT decompile. A targeted mutation rejects
omitting the optional-release input prefix.

Core checks separately passed 637 unit tests, 61 release/lifecycle assertions,
three isolated creation-failure cases with 16 assertions each, 996 coordinator
assertions, source guards and three targeted mutations. Independent review
resolved the partial-Awake rollback and shared-material ownership findings.

## Arena VisualAssist

Protected Factory Deathmatch ran with the same Arena candidate and the committed
Core/Client pair. The temporary official Fast Grenade Throw 2.0.0 DLL enabled
the actual raw-key/quick-hands path. A quick throw with VisualAssist position and
force still at zero completed and visibly launched an RGD-5 from the player.
Another quick throw after a real five-metre walk worked while the previously
computed preview point remained stale. Regular high throw and low preview also
worked. No direct private-method invocation substituted for these throws.

The input probe temporarily moved FGT's hotkey to F4. An earlier F6 attempt
really threw but also opened the SAIN editor, obscuring the image; it is not used
as visual proof. The editor was closed without saving, and runtime key settings
were restored. Missing native command acknowledgements alone were not treated as
proof that FGT had not seen its raw input.

A native death during the preview produced body-change event 5 (player 2 to 4).
The new scope/arc referenced body 4, its predecessor's line and sphere were
destroyed, and the compatibility failure remained null. On the replacement,
Molotov low preview, a visibly flying bottle, burning ground and another FGT
throw passed. Independent parent image review confirmed the zero-cache RGD-5
and post-respawn Molotov flight/fire frames.

## Final delivery and restoration

The three official packages remain in the regular installation as requested:
26 PFT files, 194 Cola files and the VisualAssist DLL. The optional VisitAPI pack
was not installed. Existing user state was preserved; the three absent Cola
seed files were copied only because no user file existed at those destinations.
Foreign packages remain installation inputs, not repository/release payloads.

The final manifest contains 231 files including the exact accepted Arena
client/BigBrain/server, whitelist, Core/Client and matching PDBs. All 231 hashes
matched on the VM, GPU-1 and GPU-2, including a final repeat after synchronization.
Core and Redeploy.Client are the `430657c` pair; the Client DLL hash is
`D5F9BB7DE8904919D4800A82BB83109B2E102093DCCD4C5C65DBAE0E510B1A76`.
The final whitelist hash is
`7424469640D6D646FD559AA1DC9F310D917F5E3032F7B661C3F73E6A4ED3A5C4`.

A fresh GPU-2 process `17607ed7` reached MainMenu and verified identical loaded
and installed assemblies for all three optional mods, Arena, Redeploy.Client
and Core. Their plugin-load diagnostics contained no errors or warnings; the
merged startup logs contained no error. Existing Fika-adapter exclusions and
Harmony declaration warnings, a missing-trader fallback and four PFT roster
preview fallbacks remained visible. This is startup evidence, not another raid
or evidence that GPU-1 exercised the changes. Both GPU games and servers were
then closed, and GPU-2's originally unpaused Syncthing folder was restored to
unpaused.

Temporary Fast Grenade Throw and its configuration were archived and removed.
The disposable standalone profile's 14 owned files and three exact registry
keys were removed without restoring any complete original profile. All original
Arena/PFT config bytes were restored on all three installations; the final
normal startup's generated headers/line endings were archived before restoration.

The final comparison accounts for all 475 captured files on each installation:
317 byte-identical files, three updated official PFT language resources, one
profile with the reviewed ordinary-startup transition, and 154 rotated automatic
backup files whose originals remain in the evidence archive. No unexplained
change or temporary plugin/profile residue remains.

The original pre-first-start backup and the first PFT pre-raid baseline are
different boundaries. Before the first Arena raid, SPT normalized the selected
profile's level against its unchanged XP, generated expired daily quests and
recorded the two updated server mods. The log and server source explain these
changes, and every later full-profile snapshot matches that exact post-startup
baseline except Health/Hideout bookkeeping timestamps. The final audit pins the
saved reference by SHA256; it does not broadly allow future level, quest or mod
metadata changes. XP, inventory and the entire Arena-rewards profile extension
also match the original pre-first-start backup. No profile rollback was used.

One zero-value Arena test receipt had been archived and removed while it was
unimported on GPU-2. The same bytes subsequently reappeared on the synchronized
installations just before the final start; synchronization is the likely cause,
but no per-file transport event was captured. That start imported and allocated
it, recording `no-eligible-reward`: zero kills/wins, zero RUB/XP, no Cola and no
payment approval. Its now-linked receipt/import/allocation/decision records are
retained as audit history, not deleted using the obsolete unimported cleanup
authorization. Their exact current hashes were rechecked on all three machines.
The journal has no reference to this run and its write time predates the tests;
there is no original journal byte snapshot, so historical journal byte equality
is not claimed. The unchanged profile reward state and inventory provide the
separate no-payment check.

Arena source commits are `ec574c17` (verification fixtures), `6645c27d` (PFT),
`d7681d4b` (Cola), `6b46fde8` (Arena throw guard) and `08a09104` (whitelist).
Shared Core implementation is `430657c`, with documentation in `ecf5659`.
No push, release or Arena version bump was performed. The 618 current release
and PFT decompile fixtures were also copied into the primary checkout and
hash-verified, with the earlier ignored fixture archived; this ran no builds.

## Evidence

Local, ignored evidence: `artifacts/mod-refresh-20261120/native/`, including
candidate/source manifests, release hashes, per-scenario status/events,
PFT/Cola comparisons and full logs. Public reports quote only relevant facts;
profile snapshots, complete logs and official third-party packages remain local.
