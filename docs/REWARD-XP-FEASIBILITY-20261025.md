# XP and other mail rewards — feasibility, 2026-10-25

Historical investigation. Additive startup XP is now implemented separately;
see [REWARDS-XP.md](REWARDS-XP.md). The native mail-event limitations below still apply.

Read-only investigation. No XP, skill, reputation or item reward implementation,
rates, live profile edits, deployment or native collection test was performed.

## Result

Give-UI uses SPT's native profile-change events attached to system mail for its
level/skill controls. These are distinct from inventory-item attachments and
take effect when accepted, not when the message arrives. However, Give-UI's
level command and the installed SPT 4.1.6 collection handler set **absolute**
XP/skill/standing values; they do not implement additive XP rewards. Copying this
mechanism would therefore be unsafe for delayed Arena bonuses: claiming an
older message could overwrite newer normal progress.

For a first XP extension, use a bounded server-calculated XP delta in Arena's
existing atomic startup settlement, together with the permanent receipt ledger,
and explain the credited XP in the reward mail. A separate manual claim button
is feasible but needs its own guarded, persistent, idempotent claim protocol.

## Evidence and versions

Local source roots:

- `D:\SPT41\Development\_reference\server-csharp-main\Libraries\SPTarkov.Server.Core`
- `D:\SPT41\Development\_reference\give-ui-master`
- `D:\SPT41\Development\_decompiled` for client call-path navigation.

The reference directories are extracted source archives without Git metadata;
their branch names are not proof of the installed version. Relevant installed
methods/types were therefore independently decompiled with ILSpy 11.0:

| Installed artifact | Version / identity | SHA-256 |
| --- | --- | --- |
| `SPT_Runtime/SPTarkov.Server.Core.dll` | 4.1.6, product commit `731d7a2a4418865c37141862ec5ad2451525f7f8` | `490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27` |
| `SPT_Runtime/user/mods/give-ui/GiveUI.dll` | Mod 5.0.0; assembly/file version 1.0.0.0; product commit `42636ddab5bc61ea7c50eb03a45b43cab3580821` | `E8DE4139EC8A927307BA082A4F59E9A69BA67AE83BB0BED34B43B95760C21C0D` |
| `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` | current installed client; `ProfileChangeEvent` verified directly | `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436` |

Give-UI's public
[command builder](https://github.com/angel-git/give-ui/blob/42636ddab5bc61ea7c50eb03a45b43cab3580821/backend/commands/command_builder.go)
and
[server router](https://github.com/angel-git/give-ui/blob/42636ddab5bc61ea7c50eb03a45b43cab3580821/server-mod/GiveUI/Router/GiveUIStaticRouter.cs)
were checked online at the commit recorded in the installed DLL. Its metadata
declares SPT `~4.1.0`. The public
[SPT InventoryController source](https://github.com/sp-tarkov/server-csharp/blob/main/Libraries/SPTarkov.Server.Core/Controllers/InventoryController.cs)
is supporting reference material; installed 4.1.6 behavior takes precedence.

## Give-UI command path

| Give-UI source evidence | Observed behavior |
| --- | --- |
| `components/templates.templ:462–472` | The profile-level slider submits `/level`; the button is labelled "Set level". |
| `app.go:386–398,803` | The route parses the selected level and calls `api.UpdateLevel`; its success message tells the user to accept the mail. |
| `backend/commands/command_builder.go:32–54` | Builds `spt profile level N`, `spt profile skill NAME N`, and native trader reputation/spend commands. The level payload is a target level, not an XP delta. |
| `backend/api/server.go:125–130,324–327` | Sends the level/skill command to `/give-ui/commando`. |
| `server-mod/GiveUI/Router/GiveUIStaticRouter.cs:88–109` | Wraps the command in a `SendMessageRequest` and delegates to native `CommandoDialogChatBot.HandleMessage`. |

The full installed GiveUI assembly confirms the same router delegation and mod
metadata. It contains no `ProfileChangeEvent`, `ProfileExperienceDelta`,
`ProfileLevel` or `RedeemProfileReward` implementation; its added commands cover
item and preset delivery. There is no additional Give-UI XP collection handler
that would convert the native absolute reward into a delta. The UI path is
verified from source; the installed GUI executable was not driven or reverse
engineered, and no message was sent to a live profile.

## Native delivery and collection

| Evidence within the source roots above | Observed behavior |
| --- | --- |
| `Helpers/Dialogue/Commando/SptCommands/ProfileCommand/ProfileSptCommand.cs:123–166` | The level/skill command sends a system message with one RUB plus a `ProfileChangeEvent`. `ProfileLevel` contains XP for the requested total level; `SkillPoints` contains the requested total skill progress. |
| `Services/Commerce/MailSendService.cs:275–322` | Creates the message, attaches items and profile events, stores it in the dialogue, then notifies the client. Delivery alone does not execute the profile event. |
| Client `EFT.UI.Chat/ChatScreen.cs:328–390`, `EFT/SocialNetwork.cs:805–808`, `EFT/EftClientBackendSession.cs:743–751` | The collection UI presents profile events and submits selected message/event IDs through `RedeemProfileReward`. The client marks its local events redeemed. |
| `Controllers/InventoryController.cs:178–263` (same decisive code in installed 4.1.6) | Looks up the named event in the hard-coded SYSTEM dialogue. `ProfileLevel` assigns total XP and updates level/trader requirements; `SkillPoints`, `TraderStanding` and `TraderSalesSum` assign absolute values. No `ProfileExperienceDelta` case. |
| Client `EFT.Communications/ProfileChangeEvent.cs:47–50,89–101` and `NotificationProfileExtDelta.cs:26` | The client knows additive XP, can display a positive delta and has a notification that adds it. This does not supply the missing server collection implementation. |
| `Services/Commerce/GiftService.cs:71–88,98–115,166` | Gift definitions can include profile events. A configured gift-count check prevents ordinary repeated *sending*; it is not a durable Arena result/collection ledger. |

The installed redemption method neither checks nor records a consumed event ID.
Its event model has an extension-data bag but no dedicated server redemption
state, and this method never updates such state. A client-side `Redeemed` flag
does not establish crash-safe, once-only additive rewards. This is a static
code finding; repeated native collection was not tested.

## Reward options

| Reward | Feasibility and boundary |
| --- | --- |
| RUB, EUR, USD, GP coins and ordinary equipment/items | Native inventory attachments. Arena's current policy/receipt/composer is RUB-only; other templates need a bounded, server-owned payload and validation rather than accepting arbitrary client items. |
| Additive PMC XP | Practical. Extend the existing detached payout document and receipt fingerprint/schema with a bounded delta, preserve intervening XP, and calculate the resulting level consistently with the reviewed native XP table. Mail can report the amount. |
| Skill XP / weapon mastery | Possible, but separate balancing and persistence work. Native mail's `SkillPoints` sets a total; the client's delta types alone are insufficient. Do not copy whole skills from Arena. |
| Trader reputation / sales totals | Possible. Native mail supports absolute setters. Bonuses need additive, once-only handling and loyalty recalculation; blindly using an old total can undo newer progress. |
| Quest progress, unlocks or hideout changes | Technically there are native event/reward handlers, but these are broad progression mutations and unsuitable as generic Arena payouts without a separately designed policy. |

`Helpers/Commerce/RewardHelper.cs:80–100` is a useful native reference for true
additive quest rewards: it adds XP, recalculates level and applies trader
standing deltas. Calling it directly during Arena restoration or the live
normal-server session would not inherit Arena's transactional guarantees.

## Recommended next implementation

1. Keep the signed result and ordinary-server startup payout model. Add an
   explicit server-owned XP policy and bounded reward fields to versioned receipts;
   older RUB-only receipts must remain payable exactly once.
2. Commit the XP delta, resulting level, payment record and explanatory mail in
   the same protected detached-profile transaction. No full Arena XP/skills copy.
   The current settlement runs before native database/profile loading: establish
   a validated source for the final XP curve and dependent level/trader rules,
   including other mods' changes, before implementing this calculation. Do not
   blindly invoke live `RewardHelper` or assume the vanilla XP table is final.
3. Test two queued rewards, normal progress between earning and payout, retries,
   interrupted saves, legacy receipts, opt-out, caps and overflow with synthetic
   profiles, then verify native profile/UI consistency on the designated host.
4. If manual XP collection is required instead, design a normal-profile-only
   claim handler with atomic consumed-event bookkeeping and replay protection.
   Mixed requests must retain vanilla behavior for unrelated mail. Do not ship a
   bare `ProfileExperienceDelta` event into the current native handler.

No XP amount or distribution formula is selected by this investigation.
