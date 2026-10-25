using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>Read-only until the player explicitly presses the consent control.</summary>
internal static class ArenaRewardMenuState
{
    private static Task<RewardStatusResponse> _request;
    private static float _nextRefresh;
    private static bool _hasResponse, _consentRequest;
    private static bool? _pendingConsent;
    internal static string CaptureProblem { get; set; }
    internal static RewardStatusResponse Status { get; private set; } = new RewardStatusResponse();
    // A background GET keeps the last confirmed presentation. Only the first
    // load and an explicit consent change need a visible waiting state.
    internal static bool Busy => _pendingConsent.HasValue || _request != null && (_consentRequest || !_hasResponse);
    internal static bool CanToggle => !Busy && Status.Available && Status.CanChange
        && !ArenaSession.ArmedForNextRaid && !ArenaSession.IsActive;

    internal static void Refresh(bool force = false)
    {
        if (_request != null)
        {
            if (!_request.IsCompleted) return;
            if (_request.Status == TaskStatus.RanToCompletion && _request.Result != null)
                Status = _request.Result;
            else
            {
                var observed = _request.Exception;
                Status = new RewardStatusResponse { Detail = _consentRequest
                    ? "Reward change could not be confirmed. Checking the current status."
                    : "Reward status unavailable. Existing consent is unchanged." };
            }
            bool recheckConsent = _consentRequest && !Status.Available;
            _request = null;
            _consentRequest = false;
            _hasResponse = true;
            _nextRefresh = Time.unscaledTime + (recheckConsent ? 0f : 10f);
        }
        if (_pendingConsent.HasValue)
        {
            bool enable = _pendingConsent.Value;
            _pendingConsent = null;
            // A click during a GET means this desired value, not "invert the
            // later response". Recheck native/server admission before writing;
            // one in-flight operation prevents a stale GET overwriting a POST.
            if (CanToggle && Status.Enabled != enable)
            {
                _consentRequest = true;
                _request = Task.Run(() => JsonConvert.DeserializeObject<RewardStatusResponse>(RequestHandler.PostJson(
                    RewardWire.ConsentRoute, JsonConvert.SerializeObject(new RewardConsentRequest { Enabled = enable, Revision = Status.ConsentRevision }))));
            }
            return;
        }
        if (!force && Time.unscaledTime < _nextRefresh) return;
        // Poll through SPT's underlying client, as the settings poll does:
        // preserve authentication/transport without two Info lines every 10 s.
        _request = Task.Run(async () => JsonConvert.DeserializeObject<RewardStatusResponse>(
            Encoding.UTF8.GetString(await RequestHandler.HttpClient.GetAsync(RewardWire.StatusRoute))));
    }

    internal static void Toggle()
    {
        if (!CanToggle) return;
        _pendingConsent = !Status.Enabled;
        Refresh();
    }

    // Closing the owning view cancels only a click still waiting behind a GET.
    // A submitted POST must finish and be reconciled rather than sent twice.
    internal static void CancelQueuedToggle() => _pendingConsent = null;

    internal static string Label => Busy ? "PROFILE REWARDS: …"
        : !Status.Available ? "PROFILE REWARDS: UNAVAILABLE"
        : Status.Enabled ? "PROFILE REWARDS: ON" : "PROFILE REWARDS: OFF";

    internal static string Detail
    {
        get
        {
            if (!Status.Available) return !string.IsNullOrWhiteSpace(Status.Detail) ? Status.Detail
                : "Optional rewards are unavailable on this server.";
            string state = !string.IsNullOrEmpty(CaptureProblem) ? CaptureProblem
                : (Status.State == "withheld" || Status.State == "changed") && !string.IsNullOrWhiteSpace(Status.Detail) ? Status.Detail
                : Status.Enabled && !Status.Enrolled ? Status.PrivateSession && Status.AutomaticReturn
                    ? "Activation pending — return to the normal session, then re-enter Arena."
                    : "Activation pending — restart the normal SPT server."
                : !Status.Enabled ? "Optional RUB and XP for your normal profile; paid on normal server start."
                : Status.PendingRoubles > 0 || Status.PendingCola ? $"{Status.PendingRoubles:N0} ₽ + {Status.PendingExperience:N0} XP"
                    + (Status.PendingCola ? " + Tarkova Cola progress" : "") + " pending — "
                    + (Status.PrivateSession && Status.AutomaticReturn ? "credited when you return to the normal session." : "next normal server start.")
                : Status.LastPaidRoubles > 0 || Status.LastPaidCola ? $"Last payout: {Status.LastPaidRoubles:N0} ₽ by mail; {Status.LastPaidExperience:N0} XP already credited."
                    + (Status.LastPaidCola ? " Tarkova Cola progress transferred." : "")
                : Status.PrivateSession && Status.AutomaticReturn ? "On return to the normal session: RUB by mail, XP credited directly."
                : !string.IsNullOrWhiteSpace(Status.Detail) ? Status.Detail
                : "Next normal server start: RUB by mail, XP credited directly.";
            return state + $"\nKill: {Status.RoublesPerKill:N0} ₽ + {Status.ExperiencePerKill:N0} XP · Win: {Status.RoublesPerRoundWin:N0} ₽ + {Status.ExperiencePerRoundWin:N0} XP"
                + $"\nMax. per match: {Status.MaxRoubles:N0} ₽ + {Status.MaxExperience:N0} XP";
        }
    }
}
