# SAIN search tempo contract checks

Run `pwsh -NoProfile -File tools/TdmSearchTempoChecks/Verify.ps1` from the repo.
Production `TdmSearchTempo`, `TeamDeathmatchRelations`, `TdmSearchAwareness` and
`ArenaPopulationPolicy`
are linked. Small explicit EFT/SAIN/Harmony doubles model contract outcomes,
not AI or runtime detours. The fixture assembly is deliberately named SAIN with
version 4.5.1.0 so optional discovery and both reflected method bindings execute.

Checks cover the 3-second cap at both predicates, adjacent float boundaries,
unchanged short/nonfinite inputs and native
result/reason, private-state preservation, actual relationship-pair rejection,
adopted roster/phase/PFT/stop gates, bot searches without a local player, bounded
logging and partial-patch rollback. The mode double supplies roster and phase;
the separate TDM runtime suite links the actual mode and flow.
Both factions and the TDM/Domination mode ids exercise the same native cap;
Zombies remains excluded. A small current local-body side facade supplements
the shared fixture's partial session double, without duplicating its types.

Named source mutations must compile and fail their specified behavioral assertion.
They include a regression to the former 5-second cap.
Mutations live only in ignored artifacts; production source is never overwritten.
The production suite is rebuilt and rerun in `finally`. Live GPU validation is
still required. See `TDM-SEARCH-TEMPO-REVIEW.md` for installed contract evidence.
