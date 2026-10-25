# Atomic XP rewards — 2026-10-25

Development change after 0.2.0. The existing optional reward setting controls both
RUB and XP. New `arena-v2` matches freeze **100 XP per eligible local kill,
1,000 XP per round win, capped at 20,000 XP per match**. RUB remains
1,000 / 10,000 / 200,000. Eligibility, no-contest rejection, default-on enrollment,
explicit OFF and private-session consent are unchanged. Gun Game and Zombies
currently award kills only, with no inferred ladder/wave victory.

## Payment and recovery

The supported local **RETURN TO NORMAL SESSION** now performs this server restart
automatically when payment is pending; see [the return protocol](REWARDS-RETURN.md).
The atomic payment boundary below is unchanged.

At the next admitted normal server startup, before native profiles are loaded,
the existing settlement kernel composes one detached document containing:

- the RUB system mail and its unread counters;
- an additive `characters.pmc.Info.Experience` delta;
- the permanent paid receipt including that XP amount.

All three commit together under the existing profile revision check and durable
Prepared/Attempting/Committed journal. No live `SaveServer` profile is replaced.
The mail explicitly says XP is already credited. Only RUB has a collectible
attachment; there is no absolute-XP mail event, delayed XP claim or new claim
handler. Collecting, deleting or expiring the mail cannot pay XP again.

Duplicate, lost-acknowledgement and interrupted-write checks cover both amounts.
An ambiguous write or divergent history is withheld, never repaired by restoring
an old profile. New allocations bind the latest clean ordinary profile, preserving
normal progress earned while a receipt waits. Existing allocations remain fixed.
Signed private results contain the frozen policy and combat/restoration evidence;
the private profile's XP, skills, quests and inventory are never imported.

The native XP field is nullable Int32. New XP payouts require an existing integral,
nonnegative value and a result no greater than `Int32.MaxValue`. Missing, null,
string/fractional, negative or overflowing values withhold the whole payment before
any journal/profile write. No guessed initial XP or silent saturation is allowed.
The kernel's separate engineering limit is 1,000,000 XP per receipt, while the
runtime policy caps actual matches at 20,000.

## Old receipts

Absent optional XP fields mean zero, and are omitted again when serializing old
policies, evaluations, claims and payments. This preserves old source hashes,
immutable decision comparisons, signed receipt reconstruction and journal records.
RUB-only claims retain the exact `arena-reward-v1` fingerprint and original mail
text. XP claims use a versioned fingerprint including XP. The profile ledger
accepts only the strict old shape or the strict new shape with positive XP.
Old `arena-v1` receipts remain RUB-only, even when first delivered after updating.
Old committed receipts are not repriced or given retroactive XP.

## Native progression

Installed SPT 4.1.6 and EFT 0.16.9.5 were inspected directly. Payout changes only
`Info.Experience`; it leaves derived Level, trader loyalty and skills untouched.
During normal login EFT awaits globals and `/client/trading/api/traderSettings`
before requesting profiles. Native `TraderHelper.GetAllTraders` calls `LevelUp`,
which calculates level using the current `GlobalTable` XP curve, then trader
loyalty. Client caching still sends that request; the installed route computes
its response and streams HTTP 200. Thus ordinary startup mods' XP-curve changes
are respected without caching a vanilla table or creating another Arena writer.

Without a client login, durable XP is already credited while stored Level/loyalty
can temporarily retain their previous values. Native login updates RAM before
the client receives its profile, and native saving persists the derived values.
Mods replacing that login route or changing the curve asynchronously fall outside
this reviewed native contract. Profile loading alone does not recalculate level.

Reviewed installed Core SHA-256:
`490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27`.
Reviewed EFT assembly SHA-256:
`EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.

## Verification

- 520 focused reward tests passed, including 45 dedicated XP settlement cases.
- Full suite: 2,853 passed, one known environment-gated APBS test skipped.
- Reward menu harness: 39 assertions passed; client/server Release builds passed.
- Tests cover crash checkpoints, duplicates, mail lifecycle, signed private
  transfer, intervening normal XP, mixed old/new histories, frozen caps, invalid
  native XP shapes and Int32 overflow. Literal v1 policy/evaluation/claim/payment
  serialization fixtures protect byte compatibility.
- Native referenced-package SaveServer migration/save/reload preserves XP,
  mail and ledger. This synthetic test is separate from installed-game acceptance.

Installed-game acceptance on GPU-2 passed: one private DM kill and win delivered
1,100 XP and 11,000 RUB, native login advanced level 2 to 3, mail collection added
only RUB, and a second server restart paid nothing again. The three existing
pre-update RUB-only histories remained unchanged. See
[native acceptance and deployment](GPU-REWARD-XP-20261025.md).
