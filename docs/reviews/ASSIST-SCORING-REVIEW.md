# Match-local damage assists

Implemented 2026-09-14 after the user confirmed: the killer retains the full
existing reward; assistants receive additional proportional points.

## Rules

- An assistant receives `floor(victim base points * their HP damage / all HP damage)`.
  Example: 30 of 100 actual HP lost against a PMC worth 200 points earns 60 points.
- The denominator is the actual negative HP changes during this physical victim
  life and scoring round. Armor absorption, blocked damage and overkill do not
  count. Healing does not subtract earlier damage; damage after healing adds to
  the same life total. Ownerless bleeding, falls, friendly damage and unknown
  sources stay in the denominator but receive no contribution credit.
- Only attributable hostile direct combat damage contributes: bullets, blunt
  ballistic damage, melee, explosions and grenade fragments. No LastAggressor
  fallback. An ownerless bleed or environmental final death does not turn an old
  aggressor into a new assist-paying kill.
- The final kill must pass existing score and direct-combat award admission.
  Self-kills, administrative deaths, team kills and rejected staging/result
  kills cannot pay assists. The killer and victim cannot assist themselves,
  including their earlier logical identity before a respawn.
- The killer keeps all current base/headshot/streak points. Assist points carry
  no headshot or streak bonus, add no kills or kill-limit progress, and do not
  advance Gun Game or award Tactical buy money. There is no new win condition.
- A positive integer reward increments that participant's assist counter once.
  Fractions below one point are dropped. Several assistants can be rewarded.
  These points and counters remain match-local; profile protection is untouched.

## Native observation and lifecycle

`ArenaAssistCapture` prefixes the exact native
`Player.OnHealthChanged(EBodyPart, float, DamageInfo)` method. The installed
`ActiveHealthController.ChangeHealth` clamps body-part health and supplies the
actual delta through `HealthChangedEvent`. Player subscribes this method to its
own controller. The event runs before the lethal-body-part check calls Kill and
the existing kill capture. A final hit therefore enters the total before payout,
without nested ApplyShot/ApplyDamageInfo envelopes counting it twice. Separate
spillover body-part losses are real additional HP losses and count once each.

Observation requires an enabled patch, active exact Arena LocalGame/GameWorld,
the current non-AI local body, a registered living victim, no administrative
death, and the current mode's live scoring phase. Pending area/team-start aborts
reject observation. Non-Arena health/death behavior is never modified. Optional
capture failure logs once and leaves native health, deaths and exceptions alone.

Hostility uses the frozen Deathmatch rules, exact bound Tactical teams, or TDM's
existing physical-body affiliations. Gun Game has no frozen FFA option and
requires explicit native group hostility, never a possibly changed F12 setting.
Known TDM posthumous grenade sources can retain their affiliation; unknown or
replaced sources cannot inherit a successor's team merely through a profile ID.

The victim ledger uses `ArenaHitHistory.BodyId`, not a nickname or profile alias.
It is consumed before each kill settlement, even a rejected settlement. A native
OnDead finalizer then forgets any remaining entry after kill callbacks, including
self/environmental deaths. It captures the owning match at death entry and cannot
clear a later match. Round starts and match end reset the ledger. Contributor
death alone does not erase damage already dealt to another still-living victim.

Immediately before existing kill scoring, `PrepareKill` applies the same hostile
source rules to the terminal hit. A friendly bot-versus-bot final hit in
all-vs-player Deathmatch or Gun Game clears only the pending assists; it does not
change the older killer-scoring/announcement rules for those modes.

## Scoreboard identity and bounds

Damage can credit only an already established scoreboard participant. It does not
create a speculative bot row or bind a PFT identity. A bot damaged before its
roster identity exists contributes denominator-only damage; this conservative
case cannot inflate the other helpers' shares. Local/friendly metadata must agree
with the established row. FFA bots may share the presentation side `Opponent`
while still being enemies; presentation color is not a friendly-fire test.

Contributions retain only managed scoreboard entries and numbers, never Player,
BotOwner or native health objects. Authenticated PFT and local identities survive
respawn; ordinary bot replacements remain distinct. If an ordinary helper has
already been compacted out of the visible history, a later reward updates the
archived team totals once, without recreating the old row.
If a delayed projectile has independently recreated that same logical row,
contributions from before and after compaction are combined by the immutable
key, rounded once and credited once to its current row. Excluding the killer and
victim also uses their immutable keys, preventing self-assists after compaction.

There are at most 128 tracked victims and 128 contributors per victim. Exceeding
the victim bound disables optional assists until the next round reset; exceeding
the contributor bound invalidates that victim's assists. In neither case is a
partial denominator used to overpay another assistant. Ordinary kill scoring is
unaffected. There is no arbitrary short damage timeout.

## Presentation and extension point

The top score panel shows an assist count and a local `ASSIST +N` indication for
2.5 seconds. Closely spaced rewards combine in that indication. The scoreboard
adds an `A` column. These reuse the existing panels/offsets. Assists do not create
killfeed entries or spoken callouts and cannot suppress a multi-kill announcement.

`ArenaKill.Assists` contains immutable settled rewards; `ArenaMatch.OnAssist` is a
separate event for later Tactical systems. Each reward has a logical contributor
key, display name, local flag, physical victim ID, points and damage share. It
does not call the mode's OnKill again or change the current money rules.

## Verification and user-run acceptance

The focused source-linked checks exercise production scoring/match code and the
capture adapter with explicit native boundary doubles. Installed IL contracts
check target signatures and actual HP/death ordering. Compiled negative controls
must reject omitted unknown damage, killer self-assists, stale rounds, replaced
bodies, friendly attribution and premature cleanup. Relevant existing mode,
score, announcer and unit checks are run separately. Exact final counts/build
and deployment identity are recorded in the dated HANDOFF entry.

Independent adversarial review reproduced and closed archived-row self-assists
and split awards for a recreated helper. Follow-up review also checked the final
hostility filter and captured-old-match cleanup. No remaining concrete blocker
was found within this reviewed/offline scope. The Tactical polish harness gained
only the new scoreboard-reset boundary; actual reset behavior is tested against
the full production match and scoreboard in AssistChecks.

Offline checks and a native client build are not live raid acceptance. Later GPU
checks should cover:

1. TDM or PFT 5v5: damage an enemy and let a teammate finish it. Local score and
   assists rise; kills, streak and Tactical money do not. The killer still earns
   its ordinary reward. Check `ASSIST +N` and the `A` table column.
2. Finish your own damaged target: one ordinary kill, no self-assist. Kill a
   fresh target: no phantom assistant. Multiple teammates can share a victim.
3. Die before a teammate finishes the enemy: your earlier contribution can still
   earn points. The new victim life/next round must not inherit old damage.
4. FFA bot-vs-bot kills can pay other hostile contributors. All-vs-player bots
   cannot assist one another against a bot; team/friendly/self damage cannot
   earn a contribution. Giving up is not an assist-paying combat kill.
5. Check normal PMC raids remain unchanged and the score text fits at the user's
   resolution/offsets. No new audio files, server update or Core update is needed.
