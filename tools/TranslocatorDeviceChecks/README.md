# Translocator device checks

Run `pwsh -NoProfile -File tools/TranslocatorDeviceChecks/Verify.ps1` from the ArenaMode repository.

The check compiles the three production device sources against installed native assemblies, then executes those sources with narrow native/root doubles and verifies installed IL contracts. It runs 26 independently compiled source faults; every fault must compile and fail its intended behavioral assertion. No mod build/deploy hook or game/server action is reachable.

The local native assembly path defaults to `D:/SPT41`. Source/lifecycle review and exact installed asset metadata are recorded under `artifacts/translocator-20261115/device`. In-game appearance, native item operation and profile preservation require separate root acceptance.
