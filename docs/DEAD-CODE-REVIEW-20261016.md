# Unused-code review — 16 October 2026

**Implementation correction, 16 October:** The subsequent full unit build found
that `ArenaEquipmentChoiceState.Label` is read by two tests. It was restored and
excluded, leaving 34 of the original 35 candidates. The completed cleanup also
removed two directly orphaned Client dependencies (36 reviewed targets total).
See [implementation and final checks](DEAD-CODE-CLEANUP-20261016.md). The original
audit figures below describe the earlier analysis, not current acceptance.

Reviewed source: `cfdaef71c6fe4bc68bfc56f11c97d5f4a0b6f7f7`. **Thirty-five
declarations have no identified consumer in the audited sources: one old Client
mode class, twenty-seven further Client members, and seven Server methods.**
No production code was deleted by this review. These are maintenance candidates,
not a promised performance improvement or a diagnosis of a gameplay problem.

This follows the [announcer asset maintenance](ASSET-MAINTENANCE-20261016.md).
Only the preceding report cleanup `9f51e8e` was pushed to the existing private
repository; the asset change and this review remain local. No publication or
deployment was performed.

## Scope and evidence

- Parsed all 818 tracked C# files, including production, tests and tooling.
  Client inventory: 386 files and 8,390 named declarations.
- Client semantic analysis: 399 production/linked sources, 179 installed
  reference assemblies, 8,364 source symbols, zero compilation errors. The 189
  initial zero-bound-reference candidates were reviewed against tests, strings,
  delegates, interfaces, attributes and companion-project consumers. They are
  not 189 deletion recommendations.
- Removing the final 28 Client declarations **only in an in-memory syntax
  tree** still produced zero compilation errors. No product DLL or source file
  was generated/modified by that trial. The parent review reran the analyzer and
  trial, and independently checked representative overloads and mode wiring.
- Server/Shared/BigBrain inventory: 68 files, 14,007 lines, 1,186 declarations.
  References covered 454 production C#/Razor files and 757 test/tool source,
  project and script files. There were zero C# parse errors. All 51 initial
  zero-identifier candidates were classified: 37 framework contracts, nine
  test/tool consumers, five unused ordinary methods. Receiver/overload review
  identified two further unused methods. The parent reran this inventory and
  checked all seven candidates against their actual replacement call sites.
- Source fingerprints remained unchanged during the audits. Detailed private
  inventories, classifiers, reproducible analysis helpers and parent rerun logs
  are under `artifacts/asset-maintenance-20261016/{client-audit,server-audit}/`.
  Those helpers depend on this host's installed SDK/EFT references; they are
  local evidence, not a portable shipped test suite.

## Client candidates: 28 declarations

Paths below are relative to `ArenaMode.Client`; line numbers refer to the
reviewed commit. Signatures distinguish unused adapters from their live
overloads. Removing a whole file/member family based on its name is unsafe.

| File:line | Unused declaration | Why it is a candidate / what remains live |
|---|---|---|
| `Modes/EndlessDeathmatchMode.cs:10` | `EndlessDeathmatchMode` | No construction, type-name lookup or test consumer found. `ModeRegistry.cs:14` creates `RoundsMode` for unified Deathmatch. Legacy `endless` configuration IDs still serve migration and must remain. |
| `Bots/ZombieSpawnService.cs:86` | `IsReady` | Constant legacy readiness facade; actual wave admission uses pending/request state. |
| `Combat/Fire/ArenaFirePolicy.cs:9` | `Radius` | Current fire collision policy uses `CellRadius` and authored geometry. Remove only this variable, not the co-declared live constants. |
| `Compat/PitFireTeamCompat.cs:126` | `ContractState` | Unread outward accessor; internal contract checks remain live. |
| `Compat/PitFireTeamCompat.cs:135` | `ContractProblem` | Unread outward accessor; active admission/error paths propagate their own reasons. |
| `Compat/PitFireTeamCompat.cs:637` | `BeginRoundTransition(out string)` | Unused short overload. Tactical/Zombie callers use the overload returning leader and followers. |
| `Compat/PitFireTeamCompat.cs:926` | `TryAdoptRoundRoster(Player, out string)` | Unused short overload. Live callers also receive the follower roster. |
| `Compat/PitFireTeamRoundLifecycle.cs:301` | `Initialize()` | Old discovery entry; the actual owner calls `Configure` directly. |
| `Compat/PitFireTeamRoundLifecycle.cs:1616` | `GetRetiringBodies()` | Unused array-copy accessor; retirement and its `Status` snapshot remain active. |
| `Hud/InfoPanels/WeaponInfoPanel.cs:74` | `Clear()` | Unused whole-panel entry; updates and `ClearWeaponLines` handle actual state. |
| `Kits/KitBuilder.cs:197` | `AddToAnyGridOf(List<CompoundItem>, Item)` | Unused two-argument adapter. WeaponSwapService uses the reason-returning overload. |
| `Kits/KitBuilder.cs:626` | `AddToAnyGrid(List<CompoundItem>, Item)` | Unused private adapter; calls use the overload with `out reason`. |
| `Kits/LadderCatalog.cs:34` | `Preload()` | Unused eager-load wrapper; `Rungs` and config refresh own actual loading. |
| `Kits/TeamArenaLoadoutSnapshot.cs:61` | `ProtectedItemCount` | Unread diagnostic property; protected-item validation remains active. |
| `Kits/TeamArenaLoadoutSnapshot.cs:64` | `SerializedCharacterCount` | Unread diagnostic property; the snapshot storage remains necessary. |
| `Kits/TeamArenaLoadoutSnapshot.cs:66` | `IsPoisoned` | Unread snapshot facade. The separate restore-gate property is live/tested. |
| `Modes/GunGameMode.cs:70` | `Tier` | Unread outward property; actual progression keeps its state. |
| `Modes/GunGameMode.cs:72` | `TierCount` | Unread outward property; ladder/progression remain active. |
| `Modes/RoundsMode.cs:48` | `Options` | Unread outward property; private frozen options/rules remain active. |
| `Modes/TacticalMode.cs:245` | `IsTeamStaging` | Unread outward property; lifecycle staging checks remain active. |
| `Session/ArenaCharacterPreview.cs:36` | `Poser` | Unread facade; the implementation uses its owned loader directly. |
| `Session/ArenaClientConfig.cs:27` | `Preload()` | Unused eager-load wrapper; current consumers use `Refresh`. |
| `Session/ArenaClientConfig.cs:77` | `GetSpawnZones(string, string)` | Unused Client getter. This does not make the Server spawn-zone configuration obsolete. |
| `Session/ArenaEquipmentChoiceState.cs:49` | `Label` — **RETAINED after correction** | Read by `ArenaEquipmentChoiceTests.cs:151,153`; the initial audit missed these consumers. |
| `Session/ArenaMenuSelection.cs:162` | `CannotStartReason` | Unread forwarding property; callers consume `CurrentEligibility`. |
| `Session/ArenaModeSelectionView.cs:37` | `SelectedMode` | Unread view property; the separate selection-state property is active. |
| `Session/Preview/ArenaPreviewFlowPatch.cs:27` | `Reset()` | Unused facade; active hooks call `ArenaPreviewFlow.Reset` directly. |
| `Spectator/ArenaSpectatorService.cs:61` | `IsReleased` | Unread status property; camera ownership/release logic remains active. |

## Server candidates: seven methods

Paths below are relative to `ArenaMode.Server`.

| File:line | Unused declaration | Active path that must remain |
|---|---|---|
| `Services/LadderPicker.cs:42` | `DrawForRaid(string, int, Action<string>?)` | Session ladder requests call `Redraw` directly (`ArenaSessionService.cs:1638,1682`). |
| `Services/ArenaSessionService.cs:354` | `CancelTeamArmPreparation(MongoId)` | Pure adapter over `CancelArmPreparation`, which the router uses directly. |
| `Services/ArenaSessionService.cs:388` | `TryConsumeArm(MongoId, string?, string?)` | Old Boolean adapter. Protected start consumes the richer `ProcessLocalRaidStart` disposition. |
| `Services/ArenaSessionService.cs:1403` | `IsStashedClone(object)` | Hostility patch uses `TryGetStashedCloneMode` with mode and hostility outputs. |
| `Services/ArenaSessionService.cs:1446` | Four-argument `TryGetBotTierContext` | Bot tier patch uses the five-argument overload with immutable raid identity. |
| `Services/ArenaSessionService.cs:1565` | `AnyArmedOrInRaid()` | Exact-session/map checks remain active, as does shared `IsLiveRaid`. |
| `Services/OrdinaryRaidLifecycleService.cs:195` | `GetStatus(MongoId)` | Router uses `GetRecoveryStatus`. Same-name calls on `ArenaSessionService` are a different live method. |

## Retain or handle separately

**Used by tests, not globally dead:**

- Retired `FactorySouth`, `CustomsFactoryCenter` and
  `CustomsFactoryCenterPolygon` geometry is deliberately retained for area
  regression tests. Moving it into test fixtures is possible; deleting it
  without migrating tests and retaining old-ID rejection coverage is not.
- Shared `UpgradeLegacyClientAllowlist`, Server Begin/CompleteTeamArmPreparation,
  the short TryGetStashedCloneMode, `HasActiveScope`, `IsExcluded`,
  `ValidateAreaLocation`, and BigBrain's short TryBindLeaseTarget overload have
  real test/tool consumers.
- `LadderPicker.Current` is only read by tests/tools, but its backing state is
  still written by live `Redraw`. This is not wholly unexecuted code.
- Client area draft clearing, UI `Root` accessors, scoreboard counters, restore
  gate state, population counts and several policy convenience overloads are
  actual regression-test observations, not unused everywhere.

**Dynamic/framework contracts are not deletion candidates:** Unity lifecycle
callbacks; Harmony prefixes/postfixes/transpiler targets and `nameof` lookups;
SPT `[Injectable]`/`IOnLoad` and metadata interfaces; Blazor routes/events;
JSON/wire/config members; BigBrain registered layers/actions; Redeploy's death
driver interface. Client bridge methods consumed only by BigBrain were checked
across projects. Optional integration and legacy migration paths remain relevant
even when a local test does not activate them.

**Historical manual tools, separate from the 35 production findings:**

- `tools/Convert-WarlordAdditions.ps1` has no current automated consumer and
  targets the replaced pack. If manually run, it overwrites seven current cues
  and recreates the obsolete WARLORD manifest. Retire it in a future tooling
  cleanup; do not use it for the current Creator pack.
- `tools/AnnouncerChecks/split_local_trial.py` is an old optional audition helper.
  It only writes ignored historical trial output. It is not a runtime dependency.

Neither tool was executed, removed or changed by this audit. Historical reports
and old deployment manifests remain dated evidence, not current loader inputs.

## Build verification and limits

The asset-maintenance source `cfdaef7` passed the isolated Client, Server and
BigBrain build: zero errors, four existing Client nullable-context warnings and
two Server warnings (nullable enumerable and MudTabs PanelClass). The files
producing those warnings were unchanged. No normal deploying build was used.
All 74 staged asset files match their source bytes (69 audio/notice files plus
five separately packaged map-preview files); installed DLL hashes remain as
recorded before the build. See the private staged manifest under
`artifacts/double-jump-build/cf477f05ace44354b737a590afac5894/manifest.json`.

This audit does not prove every unreachable branch in the entire application,
all preprocessor variants, every installed third-party binary, or unknown
external reflection/API consumers. The Client in-memory compilation proves
static consistency only; the Server audit is syntax/reference analysis plus
manual receiver/overload review, not an equivalent removal compilation. No
native gameplay acceptance was performed or implied.

Recommended follow-up: remove the superseded class and clearly private/unused
adapters in small commits, freshly check public/compatibility surfaces, then run
the affected harnesses and isolated build. Keep test-fixture migration and
historical-tool retirement separate from behavior-preserving runtime cleanup.
