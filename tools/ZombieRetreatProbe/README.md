# Temporary native retreat observer

Build this standalone project; it imports no Arena deployment targets and has
no project references. Install only its DLL on the chosen idle GPU test host,
record its hash, then restart EFT. Do not ship it with the mod. Remove that
exact DLL after ending the test and exiting EFT.

It observes three native BotDogFight methods only for active exact Arena Zombie
raids and current live generic-infected bodies, with the native infected strategy.
It never changes native return values, exceptions, arguments, fields or decisions.
Counters are emitted every 30 seconds, grouped by actual zombie mode and whether
the attack layer owns the brain. Close-visible calls and live entry/exit ranges
provide positive evidence that the costly branch could be reached. Difficulty
counts distinguish configured normal/hard even if the actual mode changes.

Method times exclude their own admission/counter work but include nested child
instrumentation. CheckSpaceForRetreat is nested; do not add its time to its
parents. Tiny method totals cannot support a positive FPS claim. No ModProfiler
is needed for this measurement. No gameplay-changing retreat optimization is
contained here; this is a tool to decide whether such a change is worthwhile.
