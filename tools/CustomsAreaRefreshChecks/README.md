# Refined Customs area verification

Run `Verify.ps1`, `Verify-Server.ps1`, `Verify-Integration.ps1` from this directory
or the repository root. `Build-Staged.ps1` builds both halves into ignored staging,
without deploying. All checks use the installed SPT 4.1 corpus; see the root
CUSTOMS-AREAS-REVIEW.md for contracts, draft provenance and native-test limitations.

Fixtures preserve the three original September 28 exports unchanged. Production
geometry deliberately ignores their old suggested height and team-size fields;
user instructions require unlimited height and explicit team starts up to 5v5.
No new runtime draft loader, menu slider or permission bypass is added.
