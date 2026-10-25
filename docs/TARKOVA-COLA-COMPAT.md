# Tarkova Cola compatibility — 2026-10-25

The user requested Zombie research and achievement rewards, with no bottle or
grenade refunds. This integration is optional, requires both parts of Tarkova Cola
2.2.1 or newer, and does not include its assets or VisitAPI storyline. Review target:
[official 2.2.1 source](https://github.com/Yendaxddd/TarkovaCola/tree/2.2.1), commit
`163bb9b1192d050b9e0da5aada8686e8c43877dd`. Release ZIP SHA256:
`9d5116b79e21301655135a157d680ee209b66aa1450b449b1a5da64cbc981607`.

## Gameplay and scope

- Preserve upstream Arena perks and Zombie research scaling. The upstream mod
  grants a random temporary perk every fifth wave; research runs at its own 25%
  Arena rate. ArenaMode does not add a second multiplier.
- Research XP uses the native earned XP counters across confirmed physical lives,
  before extraction bonuses or penalties. Arena's regular `Left` exit has a native
  zero multiplier, so using its final PMC XP would otherwise discard all research.
  Native statistics still finish normally; the adapter never calls their finish
  method early or changes PMC XP. Living PFT wave replacements and deaths freeze
  the old scalar before the shared counters reset, and commit it only once the
  exact replacement succeeds. A failed reset/replacement withholds unsafe rewards.
- Bind its physical-player caches and deferred drink effects to the current
  confirmed Arena body/world. A respawn gap is not a raid end. Living kit/wave
  changes do not remove active perks.
- Restore the pre-Arena client state and preexisting pending queues after the
  actual end. Block new foreign saves, native achievements and refunds throughout
  the protected interval, including result capture and delayed native callbacks.
- Ordinary PMC/Scav calls pass through. No foreign settings or files are changed
  merely by detecting the mod. The whitelist admits both parts under their actual
  common GUID `com.tarkovacola.server`.
- Required reflected methods/fields are checked. Newer builds are attempted with
  a warning; changed contracts or changed rewards for known achievements refuse
  unsafe integration. This is not a compatibility guarantee for future releases.

## Progress and payment

The existing profile-rewards preference controls transfer. A completed, validated
Zombie result may carry only a typed, bounded monotonic delta for perk research XP,
challenge progress/completions, synergy progress/completions and earned achievement
keys. Aborted/no-contest matches grant nothing. Other Arena modes do not transfer
Cola progression. Ordinary stats, choices, equipped augments, Classic/Eternal state
and inventory are never copied back. Bottle or grenade refunds are excluded;
bottles which are actual achievement prizes remain valid achievement rewards.

The server reads the foreign baseline itself at arm and compares the client's
synchronously saved state. Protected foreign loads use a shadow; writes are held.
Already signed earlier-match progress in this process feeds the next match's
shadow, without writing the ordinary Cola file. Native full-profile restoration
and durable readback remain mandatory before signing results. A private session
transfers the signed result, never a private Cola or PMC profile.

At ordinary startup, the existing kernel atomically records RUB, PMC XP, newly
earned native achievements, their fixed catalog prizes, one mail and the payment
ledger. The exact mail is checked during recovery; collecting it cannot reapply XP.
The frozen catalog covers 100 achievements, 47 research goals and 15 synergies.
Existing native achievement IDs suppress duplicate prizes, including grants made
outside Arena. Research-only transfers also have an informational mail and receipt.

The companion research file uses an immutable plan prepared before the native
transaction, then an atomic compare/write/readback after native payment is proven.
Its completion record prevents repeated deltas. Recovery can finish an already
paid transfer even after opt-out, but cannot issue a new payment while OFF. A
conflicting companion file after native payment stops startup for recovery; it
never overwrites intervening progress or rolls the PMC profile back. Keep the
normal profile, Cola profile and reward metadata together; do not delete journals
to resolve such a refusal. Before payment, missing mods or incompatible state
withhold the reward rather than modifying a live profile.

## Verification

Offline tests cover canonical deltas, unknown/malformed keys, catalog boundaries,
legacy RUB/XP serialization, native SPT profile serialization, duplicate rewards,
private signatures, interrupted transactions and companion recovery. Build,
deployment and native gameplay results are recorded separately in the completion
evidence; source inspection alone does not establish native acceptance.

Final offline verification on 2026-10-25: 3,092 unit tests passed; the existing
installed-APBS environment case remained skipped. This includes 29 client lease
cases for exact statistics timing, bounded earned XP and physical-body resets.
Repeated one-second timeouts in the existing listener fixture
`ArenaSessionHelperProcessTests.ALookupThatThrowsIsATableThatCannotBeRead` were
stabilized with an independent fresh positive-case socket and scheduling margin;
the negative ownership deadline and production code are unchanged. The final
complete suite passed and the no-deployment runner confirmed the installed
payload stayed unchanged.

The native-menu harness passed 861 assertions. The accepted-arm/start-error
harness passed 91 assertions and rejected all eleven compiled negative controls,
including incorrect Cola profile, mode, epoch and failed-arm cleanup. Separate
reward transfer, control transfer, menu state and client capture harnesses also
passed. Native results and test-mod cleanup are recorded in
[the GPU test record](GPU-TARKOVA-COLA-20261025.md).
