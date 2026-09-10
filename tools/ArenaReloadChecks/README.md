# Arena reload checks

`pwsh -NoProfile -File tools/ArenaReloadChecks/Verify.ps1` performs read-only
installed Assembly-CSharp contract checks, runs the production-linked host test,
rejects six temporary source faults, and reruns the clean source. All build output
is confined to this check project and ignored `artifacts/arena-reload-checks/`.
It does not build or deploy Arena client/server DLLs.

The external API doubles intentionally omit Unity clip evaluation and real
inventory operations. The shared `ArenaMovementScope` is stubbed at its public
boundary; its actual source is covered by the pre-existing movement checks. See
`../../RELOAD-HANDLING-REVIEW.md` for native contracts and required user raid tests.

Optional `-SptPath` overrides the installed game path (default `D:\SPT41`).
