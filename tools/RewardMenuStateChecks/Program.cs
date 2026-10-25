using ArenaMode.Session;
using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Diagnostics;
using System.Threading;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }

    private static RewardStatusResponse Ready(bool enabled = false, bool canChange = true) => new()
    {
        Available = true, Enabled = enabled, CanChange = canChange, Enrolled = true, State = "ready",
        RoublesPerKill = 1000, RoublesPerRoundWin = 10000, MaxRoubles = 200000, ConsentRevision = 7
    };

    private static void PumpUntil(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            ArenaRewardMenuState.Refresh();
            if (complete()) return;
            Thread.Sleep(1);
        }
        throw new InvalidOperationException("Menu did not settle its transport response");
    }

    private static PendingRequest Background()
    {
        int before = RequestHandler.Gets.Count;
        ArenaRewardMenuState.Refresh(force: true);
        return RequestHandler.WaitFor(RequestHandler.Gets, before);
    }

    public static void Main()
    {
        // First load is visibly pending and cannot create consent implicitly.
        ArenaRewardMenuState.Refresh();
        var first = RequestHandler.WaitFor(RequestHandler.Gets, 0);
        Check(first.Route == RewardWire.StatusRoute, "Initial load used another endpoint");
        Check(ArenaRewardMenuState.Busy && !ArenaRewardMenuState.CanToggle, "Initial load did not block interaction");
        Check(ArenaRewardMenuState.Label.Contains("…"), "Initial loading state was not visible");
        ArenaRewardMenuState.Toggle();
        Check(RequestHandler.Posts.Count == 0, "Loading implicitly changed consent");
        first.Complete(Ready());
        PumpUntil(() => ArenaRewardMenuState.CanToggle);
        Check(ArenaRewardMenuState.Label == "PROFILE REWARDS: OFF", "Initial confirmed state was not presented");

        UnityEngine.Time.unscaledTime = 9f;
        ArenaRewardMenuState.Refresh();
        Check(RequestHandler.Gets.Count == 1, "Background refresh ignored its interval");
        string stableDetail = ArenaRewardMenuState.Detail;
        UnityEngine.Time.unscaledTime = 11f;
        ArenaRewardMenuState.Refresh();
        var poll = RequestHandler.WaitFor(RequestHandler.Gets, 1);
        Check(!ArenaRewardMenuState.Busy && ArenaRewardMenuState.CanToggle, "Background poll disabled the button");
        Check(ArenaRewardMenuState.Label == "PROFILE REWARDS: OFF", "Background poll blinked the label");
        Check(ArenaRewardMenuState.Detail == stableDetail, "Background poll replaced confirmed detail");
        ArenaRewardMenuState.Refresh(force: true);
        Check(RequestHandler.Gets.Count == 2, "Refresh opened a second parallel GET");

        // An explicit click during the read queues a value and waits for its answer.
        ArenaRewardMenuState.Toggle();
        Check(ArenaRewardMenuState.Busy && !ArenaRewardMenuState.CanToggle, "Queued consent did not disable duplicate clicks");
        Check(RequestHandler.Posts.Count == 0, "Consent write overlapped a stale background read");
        ArenaRewardMenuState.Toggle();
        poll.Complete(Ready());
        PumpUntil(() => RequestHandler.Posts.Count == 1);
        var enable = RequestHandler.WaitFor(RequestHandler.Posts, 0);
        Check(enable.Route == RewardWire.ConsentRoute, "Consent used another endpoint");
        Check(JsonConvert.DeserializeObject<RewardConsentRequest>(enable.Body).Enabled, "Queued enable was inverted");
        Check(JsonConvert.DeserializeObject<RewardConsentRequest>(enable.Body).Revision == 7,
            "Consent did not preserve the exact confirmed revision for transport retries");
        Check(ArenaRewardMenuState.Busy, "Consent POST did not keep its visible pending state");
        ArenaRewardMenuState.Toggle();
        Check(RequestHandler.Posts.Count == 1, "Double click submitted consent twice");
        enable.Complete(Ready(enabled: true));
        PumpUntil(() => !ArenaRewardMenuState.Busy);
        Check(ArenaRewardMenuState.Status.Enabled && ArenaRewardMenuState.CanToggle, "Consent response was not applied");
        Check(ArenaRewardMenuState.Label == "PROFILE REWARDS: ON", "Confirmed consent stayed pending");

        // If the read already reports the desired state, do not invert it or POST again.
        poll = Background();
        ArenaRewardMenuState.Toggle(); // desired false
        poll.Complete(Ready());
        PumpUntil(() => !ArenaRewardMenuState.Busy);
        Check(!ArenaRewardMenuState.Status.Enabled && RequestHandler.Posts.Count == 1,
            "Already-satisfied consent became an unintended inverse write");

        // Admission may close after the click while its GET is still pending.
        poll = Background();
        ArenaRewardMenuState.Toggle();
        ArenaSession.ArmedForNextRaid = true;
        poll.Complete(Ready());
        PumpUntil(() => !ArenaRewardMenuState.Busy);
        Check(RequestHandler.Posts.Count == 1 && !ArenaRewardMenuState.CanToggle, "Queued request crossed the raid arm boundary");
        ArenaSession.ArmedForNextRaid = false;
        ArenaSession.IsActive = true;
        ArenaRewardMenuState.Toggle();
        Check(RequestHandler.Posts.Count == 1, "Active raid admitted a consent change");
        ArenaSession.IsActive = false;
        poll = Background();
        ArenaRewardMenuState.Toggle();
        poll.Complete(Ready(canChange: false));
        PumpUntil(() => !ArenaRewardMenuState.Busy);
        Check(RequestHandler.Posts.Count == 1 && !ArenaRewardMenuState.CanToggle, "Queued request ignored refreshed server admission");

        // A failed read reports unavailable once; periodic retries must not blink either.
        poll = Background();
        poll.Complete(Ready());
        PumpUntil(() => ArenaRewardMenuState.CanToggle);
        poll = Background();
        ArenaRewardMenuState.Toggle();
        poll.Reply.SetException(new InvalidOperationException("Synthetic GET failure"));
        PumpUntil(() => !ArenaRewardMenuState.Busy);
        Check(!ArenaRewardMenuState.CanToggle && !ArenaRewardMenuState.Status.Available, "Failed GET retained writable stale consent");
        Check(RequestHandler.Posts.Count == 1, "Failed GET allowed its queued consent write");
        Check(ArenaRewardMenuState.Detail.Contains("status unavailable"), "Failed status did not explain itself");
        poll = Background();
        Check(!ArenaRewardMenuState.Busy && ArenaRewardMenuState.Label == "PROFILE REWARDS: UNAVAILABLE",
            "Unavailable-status retry blinked or reentered initial load");
        poll.Complete(Ready());
        PumpUntil(() => ArenaRewardMenuState.CanToggle);

        // Leaving the page cancels an unsent click; reopening cannot submit it later.
        poll = Background();
        ArenaRewardMenuState.Toggle();
        ArenaRewardMenuState.CancelQueuedToggle();
        Check(!ArenaRewardMenuState.Busy, "Closing view retained an unsent consent request");
        var afterClose = Ready(); afterClose.PendingRoubles = 123;
        poll.Complete(afterClose);
        PumpUntil(() => ArenaRewardMenuState.Status.PendingRoubles == 123);
        Check(RequestHandler.Posts.Count == 1 && !ArenaRewardMenuState.Status.Enabled,
            "A queued consent change survived its owning page");

        // An ambiguous POST is never automatically repeated: reconcile with a GET.
        ArenaRewardMenuState.Toggle();
        var ambiguous = RequestHandler.WaitFor(RequestHandler.Posts, 1);
        ArenaRewardMenuState.CancelQueuedToggle();
        Check(ArenaRewardMenuState.Busy, "Closing view cancelled a submitted write locally");
        int previousReads = RequestHandler.Gets.Count;
        ambiguous.Reply.SetException(new InvalidOperationException("Synthetic lost POST response"));
        PumpUntil(() => RequestHandler.Gets.Count > previousReads);
        poll = RequestHandler.WaitFor(RequestHandler.Gets, previousReads);
        Check(!ArenaRewardMenuState.CanToggle, "Unconfirmed write permitted another toggle");
        Check(ArenaRewardMenuState.Detail.Contains("could not be confirmed"), "Ambiguous consent response was presented as unchanged");
        Check(RequestHandler.Posts.Count == 2, "Ambiguous consent response retried the POST");
        poll.Complete(Ready(enabled: true));
        PumpUntil(() => ArenaRewardMenuState.CanToggle);
        Check(ArenaRewardMenuState.Status.Enabled && RequestHandler.Posts.Count == 2, "Reconciliation lost the applied consent or resubmitted it");

        // Cancellation and server-declared unavailability remain read-only failures.
        poll = Background();
        poll.Reply.SetCanceled();
        PumpUntil(() => !ArenaRewardMenuState.Status.Available);
        Check(!ArenaRewardMenuState.CanToggle, "Cancelled refresh remained writable");
        poll = Background();
        poll.Complete(new RewardStatusResponse { Detail = "Server unavailable" });
        PumpUntil(() => ArenaRewardMenuState.Detail == "Server unavailable");
        Check(!ArenaRewardMenuState.Busy && !ArenaRewardMenuState.CanToggle, "Unavailable response did not settle read-only");
        Check(RequestHandler.Posts.Count == 2, "Read-only failure handling submitted consent");

        poll = Background();
        var privatePending = Ready(enabled: true);
        privatePending.PrivateSession = true;
        privatePending.AutomaticReturn = true;
        privatePending.Enrolled = false;
        poll.Complete(privatePending);
        PumpUntil(() => ArenaRewardMenuState.CanToggle);
        Check(ArenaRewardMenuState.Detail.Contains("return to the normal session") && ArenaRewardMenuState.Detail.Contains("re-enter Arena")
            && !ArenaRewardMenuState.Detail.Contains("restart the normal SPT server"),
            "First private opt-in did not explain the safe enrollment round trip");
        poll = Background();
        privatePending.Enrolled = true;
        privatePending.Detail = "At the next normal server start: RUB by mail, XP credited directly.";
        poll.Complete(privatePending);
        PumpUntil(() => ArenaRewardMenuState.Status.Enrolled);
        Check(ArenaRewardMenuState.Detail.StartsWith("On return to the normal session"), "Private ready text retained the outdated manual server-start instruction");
        poll = Background();
        privatePending.PendingRoubles = 11000;
        privatePending.PendingExperience = 1100;
        poll.Complete(privatePending);
        PumpUntil(() => ArenaRewardMenuState.Status.PendingRoubles == 11000);
        Check(ArenaRewardMenuState.Detail.Contains("credited when you return"), "Automatic private payout was not explained");
        poll = Background();
        privatePending.AutomaticReturn = false;
        poll.Complete(privatePending);
        PumpUntil(() => !ArenaRewardMenuState.Status.AutomaticReturn);
        Check(ArenaRewardMenuState.Detail.Contains("next normal server start") && !ArenaRewardMenuState.Detail.Contains("credited when you return"),
            "Missing return capability still promised immediate payout");
        poll = Background();
        var stale = Ready(); stale.State = "changed"; stale.Detail = "Changed elsewhere; review the current setting.";
        poll.Complete(stale);
        PumpUntil(() => ArenaRewardMenuState.Status.State == "changed");
        Check(ArenaRewardMenuState.Detail.StartsWith(stale.Detail), "Stale consent feedback was hidden by generic OFF text");
        Console.WriteLine($"REWARD_MENU_STATE_CHECKS_PASSED ({_checks} assertions)");
    }
}
