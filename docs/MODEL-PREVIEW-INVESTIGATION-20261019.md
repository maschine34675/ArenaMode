# Native menu model investigation — 19 October 2026

Source audit against Arena `36e72b9`, Redeploy `d1d7240`, Killcam `8c8ee45`.
This leaf changed documentation only: no production edit, build, deployment,
profile/configuration change, GPU action or commit. Native comparison belongs
to the parent and is not claimed by this report.

## Result and scope decision

The four undisposed confirmation bodies have a concrete upstream PFT lifecycle
explanation, verified against installed PFT 0.10.3 and installed EFT: PFT forces
the native local branch, which does not register group-preview disposal, then
reopens that group in its postfix without registering it. Arena does not own
those four widgets. The same PFT branch is available in an ordinary PMC setup.
This is strong source attribution, not yet a native ordinary-PMC reproduction.
It is not evidence that Arena, Redeploy or Killcam caused or worsened the defect.
Do not expand the current repair scope into a global PFT fix on this evidence.

The menu `WeaponPrefab.Parent` NRE is still unattributed. Its exact native
`PlayerModelLoader.Load -> PlayerModelView.Show` stack excludes the direct
Arena mannequin load as its caller. It does not exclude an indirect shared-pool
interaction. No concrete own-mod mutation of the failing prefab fields or
cross-owner pool return was found; a null guard or exception suppression would
therefore conceal an unresolved error rather than repair an evidenced cause.

## Evidence identity

- Native source: `../_decompiled`, EFT 0.16.9.5.40743. Installed
  `D:\SPT41\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll` SHA256
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.
- Installed `D:\SPT41\BepInEx\plugins\pitFireTeam\pitFireTeam.dll` file/product
  version 0.10.3, SHA256
  `0F875F824215F68A47FFD9183FB0BDD92CBA72762E1702F458A0EE614CC6DBB9`.
  This matches the previous installed-contract record in
  `docs/PFT-PORTED-MAPS-REVIEW.md:4`.
- The reference tree `../_reference/SPT-PitFireTeam-main` declares 0.10.2 in
  `client/pitFireTeam.csproj:7`; it is a navigation aid, not accepted version
  authority. The relevant installed types were independently decompiled to
  stdout with `ilspycmd -t pitTeam.Patches.MatchMakerAcceptScreenPatch`,
  `pitTeam.Patches.MatchMakerGroupPreviewClosePlayerPatch`, and
  `pitTeam.Patches.SyntheticTeammateRaidGuard` against the exact DLL above.
  No PFT or decompiled source file was changed.
- Installed EFT `EFT.UI.Matchmaker.MatchMakerAcceptScreen` and `WeaponPrefab`
  were also independently decompiled to stdout. Their relevant branches match
  the line-anchored native source below.
- Recovered historical evidence:
  `artifacts/model-errors-20261019/historical-errors.json` and
  `historical-context.json`, obtained by the parent from the original GPU2
  session. This leaf inspected those recovered entries, not just the old report.
  Full-session export extends beyond the earlier report's bounded capture;
  counts below concern these two families only.

## Four undisposed bodies: exact lifecycle

Historical body contexts at 15:05:44.359–.363 identify `MenuPlayer` descendants
under `MatchMaker AcceptScreen/PreviewsPanel/GroupPreview/Player2` through
`Player5`. The four PlayerBody instance IDs are `-1702360`, `-1701392`,
`-1705750`, `-1700312`. Each has `activeSelf=True`, `activeHierarchy=False` and
native `_active=true`; the containing Accept screen is inactive. The stack
reaches `TarkovApplication.LocalGameCreate -> Object.DestroyImmediate ->
PlayerBody.OnDestroy`. The context's `bodyProfile=none` is expected for these
menu bodies and is not a missing live combatant.

The causal source chain is:

1. Installed PFT `MatchMakerAcceptScreenPatch.PatchPrefix` runs on the three-
   argument `Show(IEftSession, RaidSettings, RaidSettings)`. It refreshes PFT
   visuals and, when `SyntheticTeammateRaidGuard.HasSyntheticTeammates()` is
   true, sets `raidSettings.RaidMode=(ERaidMode)1`, i.e. `Local`. It populates
   the controller's group before native Show. The guard depends on PFT's
   `MainMenuControllerPatch.GroupPlayers.Count > 0`; it has no Arena selection,
   Redeploy or Killcam condition. Reference anchors matching this installed
   behavior: `client/Patches/RaidStartPatch.cs:39`, `:1142`, `:1155`.
2. Native `EFT.UI.Matchmaker/MatchMakerAcceptScreen.cs:340` snapshots that mode.
   Its local branch at `:356` calls `_groupPreview.Close()`. Only the nonlocal
   branch registers `UI.AddDisposable(_groupPreview)` at `:388`.
3. Installed PFT `PatchPostfix` then calls `groupPreview.Show(...)` with the
   screen's stored settings, controller and interaction factory. It never
   adds the reopened group to the screen disposal owner. Matching reference
   anchor: `client/Patches/RaidStartPatch.cs:1191`, especially `:1208`.
4. Native `MatchMakerGroupPreview.cs:27` registers its own group-list
   subscription; this does not register the group with its parent screen.
   `UpdateGroup` creates/loads peers at `:51–55`; `method_1` instantiates their
   template under the serialized comrades positions at `:71`.
5. Native `MatchMakerPlayerPreview.cs:99` registers its inner model view with
   its own UI lifetime. Its `UpdateVisual` calls native model Show at `:113`.
   Those inner lifetimes work only when their owning preview is closed.
6. Native Accept `Close` at `:741` closes the coop window and calls base.
   `EFT.UI.Screens/UIScreen.cs:100` reaches `EFT.UI/UIElement.cs:58`, which
   disposes registered UI children and hides the screen. Hiding ancestors does
   not invoke the unregistered group's `Close`.
7. Had `MatchMakerGroupPreview.Close` run, `:88` would call `method_2(0)`,
   which calls the child preview's Close (`:79`), before destroying it at
   `:90`. Installed PFT's replacement for `method_2` also calls `preview.Close`
   and merely catches stale-preview exceptions; it does not fill the missing
   parent registration. No evidence of its close-failure warning is needed
   to explain the currently captured objects.
8. Native `PlayerModelView.Close` at `EFT.UI/PlayerModelView.cs:108` normally
   calls `Destroy -> PlayerModelLoader.Clear`. Clear calls `PlayerBody.Dispose`
   before `DestroyImmediate` (`PlayerModelLoader.cs:301–310`). Native body
   Dispose clears `_active` at `EFT/PlayerBody.cs:906`.
9. Instead, after `await localGame.Run`, `EFT/TarkovApplication.cs:3015` destroys
   the whole MenuUI hierarchy. Native `PlayerBody.OnDestroy` at `:915–919`
   reports precisely a still-active body. This matches the captured owners,
   inactive ancestor and destruction stack without requiring a timing race.

Counterexamples and boundaries:

- The normal nonlocal native branch does register the group. This disproves a
  claim that every native group inherently lacks cleanup.
- The native local branch alone closes its unused group and does not reopen it.
  PFT's subsequent Show is the relevant change.
- A later explicit group close, reopening Accept, removal of all companions or
  another mod's cleanup can mask the leak. Absence in a single run is not proof
  that the registration exists.
- The three earlier weapon exceptions are not needed for this chain. Four
  undisposed bodies do not imply four failed weapons, and the NRE records do
  not identify any of these four bodies.

## Arena's actual preview responsibility

`ArenaPreviewFlowPatch.cs:67–81` observes the controller Show and prefixes
`MatchMakerPlayerPreview.UpdateVisual`. `ArenaPreviewFlow.cs:78–88` admits
only the exact `screen._playerModelView`, `_isMainCharacter=true`, the selected
flow and an active widget. Group-member widgets never qualify. Its native
equipment decision (`:145–179`) affects only the local model choice.

Arena's early `UIScreen.Close` prefix is void and never skips native Close
(`ArenaPreviewFlowPatch.cs:27–39`). `BeforeScreenClose` detaches only the
currently owned image host/subtree (`ArenaPreviewFlow.cs:253–276`), restores
its saved raw-image/camera flags and removes Arena's host callback. It neither
closes nor replaces the PFT group and does not register a substitute lifetime
for it. `Surface.HideNative` at `:354` changes the local image/camera, not
the group model or its loader. Closing the group as an Arena-owned teardown
would therefore introduce a new compatibility repair, not repair an owner
already retained by this code.

The independent mannequin owns one `PlayerModelLoader`, persistent world root
and retained bundle token. Its load calls `CreatePlayerBody` and `CreateWeapon`
directly (`ArenaCharacterPreview.cs:147–169`), catches its own failure (`:180`),
and releases after an outstanding load finishes (`:186–188`, `:257–279`,
`Preview/ArenaPreviewLifetimeState.cs:12–35`). Its root is
`ArenaCharacterPreviewWorld`, not a GroupPreview/Player2–5 hierarchy.

No Core or Killcam code path was found that creates, reparents or disposes these
menu group widgets. The parallel pool audit also found no concrete bridge from
their native combat-body/mesh-clone handling to these preview owners. That is
a source exclusion of these specific mechanisms, not a blanket claim that
other interactions are impossible.

## WeaponPrefab.Parent NRE: what is established

The recovered EFT trace has three identical records at 15:05:20.823, original
trace lines 1911, 1918 and 1925. Each stack is
`WeaponPrefab.Parent -> Init -> PlayerModelLoader.CreateWeapon ->
PlayerModelLoader.Load -> PlayerModelView.Show(PlayerVisualRepresentation)`.
The records supply no template, object ID, preview hierarchy or exact faulting
IL offset in the dynamically patched Parent. They cannot establish three
distinct weapons or three distinct preview bodies.

Native `PlayerModelLoader.cs:184–192` creates an animated item, parents its
WeaponPrefab to the menu poser, activates it and calls `Init(null, true)`.
`WeaponPrefab.cs:271–279` first invokes `CacheInternalObjects` and then Parent.
The null-player path is intentional native menu behavior, not an invalid caller.

Parent can encounter invalid internal state at the object transform (`:382`),
the animator/wrapped Animator (`:401`), or called initialization (`:380`, `:403`).
`ObjectInHands.OnWeaponInit` is empty by default but `EFT/Firearms.cs:955`
updates muzzle effects. Without the exact offset/field state it is unjustified
to name one of these as the historical null. The emitter is newly allocated
inside Parent (`:385`), so a normal pool return clearing an older emitter alone
does not explain this failure.

`CacheInternalObjects` skips reconstruction once its cache flag is true
(`WeaponPrefab.cs:588`); otherwise it creates the object and wraps its Animator
at `:592–606`. `AnimationSystem/UnityAnimatorWrapper.cs:268–283` constructs a
wrapper and sets the Unity Animator; `enabled` delegates to that Animator at
`:59–68`. Merely finding a nonnull interface wrapper would therefore be an
insufficient health check. These are diagnostic candidates, not evidence that
Arena destroyed either field or that a particular weapon asset is malformed.

The native Load invokes its completion event before CreateWeapon
(`PlayerModelLoader.cs:157–159`); `PlayerModelView.OnLoadingCompleted` sets
`LoadingComplete=true` (`PlayerModelView.cs:74–78`). A subsequent weapon
exception can leave a completed-looking preview body, but `Destroy` still
calls Clear regardless of that flag (`:91–105`). This is not a substitute
explanation for PFT's missing group Close.

Native repeated Show/cancellation shares a mutable loader (`PlayerModelView.cs:
39–64`, `PlayerModelLoader.cs:151–155`), which is a possible asynchronous
reentry hazard. The historical stack contains neither a competing Show nor
Clear; it does not prove this hazard occurred, or that Arena provoked it.
Do not globally serialize or replace native model loading on that hypothesis.

## Discriminating native controls and repair conditions

1. **PFT ownership control:** fresh process, same four invited PFT companions,
   ordinary PMC Local raid with Arena unselected. Record loaded module identities
   and the unchanged PFT hash. After Accept closes but before MenuUI destruction,
   inspect the inactive Accept instance's `_groupPreview.list_0`, each child
   `_playerModelView.PlayerBody`, and body `_active` by read-only reflection.
   The predicted failure marker is four surviving active-flag bodies under the
   inactive Accept hierarchy; after fully loaded raid inspect the four native
   disposal errors because MenuUI no longer exists. Check the actual destroy
   stack/owner rather than count alone. No deliberate invalid prefab is needed.
2. **Paired Arena control:** same companions and weapon visuals, Arena Tactical
   issued gear versus own gear. Group peers should stay identical in both;
   the local mannequin is the expected difference. A repeated upstream defect
   in ordinary PMC and Arena does not authorize an Arena repair by itself.
3. **NRE observation:** capture the failing native preview's identity and
   call generation, item/template, loader/body/weapon instance IDs, hierarchy,
   pool state, `_objectInstance`, `_animator` concrete type/wrapped Unity object,
   cache flag and installed Harmony owners. Instrument only read-only bounded
   callbacks, preserve the original exception, and gate expensive context on
   DeveloperMode. The existing native-model observer deliberately rejects menus,
   so its absence here is expected (`NATIVE-MODEL-DIAGNOSTICS-20261018.md`).
   Do not simply broaden active-Arena gameplay admission to menus.
4. **NRE baseline caveat:** Arena builds its third mannequin during ordinary
   side-screen Show before any final selection (`ArenaSideSelectionPatch.cs:80`,
   `ArenaSideSelectionLayout.cs:132`). Thus “Arena not selected/no replay yet”
   distinguishes group ownership but does not eliminate all Arena shared-pool
   exposure. Compare exact prefab identities in a fresh menu, then after the
   relevant native/Arena lifecycle. An isolated no-Arena startup is stronger
   only if the parent elects to run it without disturbing protected data.
5. **If own-mod cause emerges:** repair the exact mutation/reuse or missed owned
   release at its origin, preserving normal PMC, native errors, profile objects
   and pool contracts. Require a native positive failure/control and absence
   under the corrected same scenario, including usable visible previews and
   ordinary cleanup. A clean log without attempted previews is not acceptance.
6. **Upstream PFT repair candidate, outside current attribution scope:** enroll
   each group Show under the exact screen's Close lifetime when PFT reopens it
   after Local Show. Native group Close must still own child loader cleanup;
   do not call `PlayerBody.Dispose` globally from OnDestroy. Guard repeated Show,
   both existing native nonlocal ownership and async cancellation/reentry.
   This is a precise future compatibility fix if separately authorized, not
   an implemented or currently justified Arena correction.

## Four-pass review and leaf gates

1. Complete: traced native creation, disposal and outer MenuUI destruction;
   compared Arena, PFT reference and installed PFT/EFT behavior.
2. Expert: rejected source-tree version as authority, verified enum Local and
   the nested Show overload; distinguished missing parent registration from
   child cleanup and inactive hierarchy from disposed state.
3. Adversarial: preserved ordinary-PMC/PFT counterexample; excluded direct
   mannequin stack without excluding indirect pool reuse; accounted for wrapper
   nulls, async reentry, duplicate error records and the preselection mannequin.
4. Polish/evidence: rechecked line anchors, exact DLL hashes and historical
   record IDs; no production change or claim of unperformed native testing.

Manual P1 met: both families traced through exact native and own-mod/PFT
paths above, with historical owner evidence and installed-contract verification.
Manual P2 met: upstream repair candidate, own-mod repair conditions,
counterexamples and native discriminators are explicit; no causal ownership is
invented. Leaf audit gates: **2 met, 0 unmet, 0 abandoned**. Root native and
integration gates remain separate and open.
