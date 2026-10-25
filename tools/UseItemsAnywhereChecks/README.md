# Use Items Anywhere coexistence checks

Run `pwsh -NoProfile -File tools/UseItemsAnywhereChecks/Verify.ps1` from the
ArenaMode repository. `-SptPath` selects the installed native/Harmony reference
files; `-ForeignDll` selects the previously downloaded, reviewed UIA 2.1.6 DLL.
Nothing is installed or patched in a game process. The wrapper checks the
installation snapshot before/after and writes logs plus source hashes under
`artifacts/use-items-anywhere-checks/<run>/`.

The production magazine transpiler retains its native query call and appends
Arena's candidate extension after it. UIA can still replace the one native
call if its transpiler runs later. If UIA ran first, Arena validates the exact
foreign query identity/signature and leaves the stream unchanged. Both
runtime helpers defer to UIA while its exact plugin GUID is loaded, including
when its features are disabled. Reach/bind postfixes use the same ownership
rule. Arena's grenade-pick repair remains Arena scoped and asks the effective
reach rule; it does not widen foreign slots.

The optional bridge only binds UIA's existing candidate query for HUD reads.
It writes no configuration and installs no foreign or generic-query patch.
Identity/signature/version drift warns once and leaves foreign access
authoritative; the HUD cannot claim a candidate count when that query fails.
Newer versions with the same contract warn once per process.

The executable links production Arena reload, reach and compatibility sources.
It uses the installed Harmony binary on desktop .NET Framework 4.7.2 to patch
executable consumer methods against explicit EFT/loader doubles. The foreign
transpiler boundary is independently authored from the reviewed exact-one-call
contract; it contains no copied foreign implementation. The real reviewed DLL
is separately SHA-256 pinned and inspected with Cecil, alongside installed EFT
call sites. This distinguishes actual Harmony execution from native Unity/EFT
gameplay, which remains a separate acceptance step.

Coverage:

- Both plugin load orders crossed with both transpiler/postfix priority orders.
- All three overlapping reload consumers, preserved labels/exception blocks,
  and rejection of missing, duplicate or mixed native/foreign queries.
- The old destructive composition fails in both load orders.
- Ordinary/Arena and both Arena/foreign feature switches; live foreign slots,
  predicates, HUD delegation, absent foreign mod, contract drift and newer version.
- Two source mutations prove foreign append/reach ownership assertions fail.

`BackpackReloadChecks` retains the no-mod identity/predicate/backpack regressions
and six source mutations. `RaidInfoPanelChecks` links the real counter and tests
foreign slot changes, disabled feature fallback and failed-query cleanup.
