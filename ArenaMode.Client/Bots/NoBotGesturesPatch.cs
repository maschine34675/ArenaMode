using ArenaMode.Config;
using ArenaMode.Session;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Stops bots pausing for friendly hand signals during an arena raid.
///
/// BotFriendChecker pairs a bot with the NEAREST bot within 20 m — of any
/// side; BotsConnections is raid-wide and knows no teams — and about one check
/// in six or seven starts a "sign": BotPeacefulActions.StartDo books the pair action,
/// plays the "roger" bark, and calls SetActiveAction for each bot that rolls
/// its CHANCE_TO_PLAY_GESTURE_WHEN_CLOSE. SetActiveAction is where the visible
/// part begins — the bot turns to its partner and waves every few seconds
/// until the pair action ends — and where OnStartPeacefulMove is raised. That
/// event is BSG's item exchange: BotRandomPlanItemDropper throws a random item
/// out of the bot's SECURE CONTAINER for the partner to pick up. Two things
/// make that a problem here. The 60 s grace period the dropper was given after
/// activation compares Time.time against a field nothing ever assigns, so it
/// is dead once the process has run a minute and the FIRST gesture after a
/// spawn throws; and SPT keeps a PMC's healing items in its secure container
/// (pmc.json forceHealingItemsIntoSecure), so what flies out is the Surv12 or
/// the AFAK. Arena teams spawn within metres of each other, so kits lay on the
/// ground seconds into every round (log of 2026-09-13: four throws, 1.9–11.7 s
/// after the thrower's own spawn), and the partner — which may only pick up
/// into its backpack or secure container — mostly leaves them there.
///
/// The gate is on SetActiveAction, not on StartDo, so that StartDo's own
/// bookkeeping stays vanilla: pairData.SetAction still books the pair's
/// PERIOD_NEXT_ACTION_SEC cooldown (CanDoSign gates every friend action of
/// that pair, not just the sign), and the bark still plays. What is skipped is
/// exactly the gesture state, the delayed task that would raise it later, and
/// OnStartPeacefulMove — hence also the follower's stop-in-formation for its
/// boss and Knight's own IFAK drop to his escort, which subscribe to the same
/// event (Reserve is not an arena map, so that one is moot). SetActiveAction
/// is the only raiser of OnStartPeacefulMove and asks nothing that was cached,
/// so the call is vanilla again the moment the raid ends. The other friend
/// actions (tilt, look, aim, eat, watch the second weapon) are untouched.
/// Normal raids keep the vanilla behaviour — PMC squads throw their kits at
/// each other there as well, but rarely enough that nobody has minded.
///
/// Out of scope, checked: the reserve-way scripts drop through
/// BotItemDropper.RefreshItemToDrop, which draws only the templates in
/// Patrol.ITEMS_TO_DROP — empty for PMCs; GiftNode is the Christmas event.
///
/// This is deliberately NOT done by clearing Patrol.DO_RANDOM_DROP_ITEM on the
/// arena bots: BotSettingsComponents.Copy() is a shallow copy, and the Patrol
/// block is a process-wide cached instance per (difficulty, role), so a flag
/// cleared on one arena bot would stay cleared for every PMC of that difficulty
/// in every later normal raid of the session.
/// </summary>
internal sealed class NoBotGesturesPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotPeacefulActions), nameof(BotPeacefulActions.SetActiveAction));

    [PatchPrefix]
    private static bool Prefix()
    {
        // Armed too: bots spawn and tick during the deploy countdown, before
        // OnGameStarted latches IsActive, and two bots placed side by side can
        // roll their first sign in that window (the same one NoAccidentalBossPatch
        // closes). OnRaidStarted clears the armed flag before it decides, so a
        // refused arm hands an ordinary raid back to vanilla at that moment.
        if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)
            return true;

        return !Settings.NoBotGestures.Value;
    }
}
