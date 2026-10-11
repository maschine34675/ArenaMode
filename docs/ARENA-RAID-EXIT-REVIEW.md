# Arena raid leave without the item-loss confirmation

Only an exact active Arena `LocalGame`/`GameWorld` intercepts the native owner's
leave callback (`BaseLocalGame<EftGamePlayerOwner>.vmethod_4`). Ordinary/armed-only
raids retain the native confirmation. This changes the raid menu's leave action,
not application shutdown, extraction, Give Up, or match-result auto-exit.

The skipped screen is `ReconnectionScreen`. Its controller's `Leave` hides the
taskbar and invokes `CG_method_18`, which calls virtual `Stop(Profile.Id, Left,
null, 0)`. Arena performs those same two operations without constructing the
confirmation/model/banner. The LocalGame override, native teardown, server exact
raid-end interception and protected profile restore are unchanged.

A pending exit belongs to the exact game/world/profile, not the old dead body.
While Core's actual respawn pipeline runs, the owner is absent/mismatched, or
timeScale is zero, it waits without changing StandDown or taskbar visibility.
Once the current owner, LocalPlayer and world.MainPlayer agree, it sets StandDown
before the native continuation. Dead spectators may exit too; no live-health or
PFT round-phase condition can trap them in a failed round transition.
Stale raid identity, stopped games and profile changes discard the request.
Repeated clicks cannot enqueue a second continuation. A precommit fault restores
the prior StandDown value and requires an explicit retry; a postcommit fault never
rearms the death hold or invokes teardown again. Notifications are best effort.

If a native respawn factory permanently fails to restore its owner, safe exit
still cannot dereference that missing owner. The request logs its wait reason;
it does not fabricate an owner or cancel a running native factory. A third-party
pause must be released before native Stop's time-dependent fade can finish.

Verification: ArenaUiInteractionChecks links the entire production coordinator
and Harmony patch against native boundary doubles, exercises scope, deferred
rebind, duplicate/stale requests, pre/postcommit failures and notification faults,
and inspects this installation's native IL callback contract. Compiled negative
controls prove the unsafe paths are rejected. Independent review verified the
native chain, PFT transition guards and KillcamReplay's Killed-only interception;
its notification-fault finding was corrected. Native rendering/raid-end behavior
still needs a GPU test; build/offline checks alone are not that evidence.

GPU acceptance: in Arena leave through Esc -> Disconnect while alive, during
countdown, and while spectating after death (including PFT). If clicked during
respawn, verify a single exit after rebind. No red loss screen should appear;
the original protected profile should return unchanged. In a normal PMC/Scav raid,
verify that Disconnect still opens the vanilla confirmation and can be cancelled.
