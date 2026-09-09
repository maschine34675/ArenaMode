# SAIN search tempo contract checks

Run `pwsh -NoProfile -File tools/TdmSearchTempoChecks/Verify.ps1` from the repo.
Production `TdmSearchTempo`, `TeamDeathmatchRelations` and `TdmSearchAwareness`
are linked. Small explicit EFT/SAIN/Harmony doubles model contract outcomes,
not AI or runtime detours. The fixture assembly is deliberately named SAIN with
version 4.5.1.0 so optional discovery and both reflected method bindings execute.

Checks cover both applied arguments, unchanged short/nonfinite inputs and native
result/reason, private-state preservation, actual relationship-pair rejection,
adopted roster/phase/PFT/stop gates, bot searches without a local player, bounded
logging and partial-patch rollback. The mode double supplies roster and phase;
the separate TDM runtime suite links the actual mode and flow.

Named source mutations must compile and fail their specified behavioral assertion.
Mutations live only in ignored artifacts; production source is never overwritten.
The production suite is rebuilt and rerun in `finally`. Live GPU validation is
still required. See `TDM-SEARCH-TEMPO-REVIEW.md` for installed contract evidence.
