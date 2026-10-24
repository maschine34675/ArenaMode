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
    internal static string CaptureProblem { get; set; }
    internal static RewardStatusResponse Status { get; private set; } = new RewardStatusResponse();
    internal static bool Busy => _request != null;

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
                Status = new RewardStatusResponse { Detail = "Reward status unavailable. Existing consent is unchanged." };
            }
            _request = null;
            _nextRefresh = Time.unscaledTime + 10f;
        }
        if (!force && Time.unscaledTime < _nextRefresh) return;
        // Poll through SPT's underlying client, as the settings poll does:
        // preserve authentication/transport without two Info lines every 10 s.
        _request = Task.Run(async () => JsonConvert.DeserializeObject<RewardStatusResponse>(
            Encoding.UTF8.GetString(await RequestHandler.HttpClient.GetAsync(RewardWire.StatusRoute))));
    }

    internal static void Toggle()
    {
        if (Busy || !Status.Available || !Status.CanChange || ArenaSession.ArmedForNextRaid || ArenaSession.IsActive) return;
        bool enable = !Status.Enabled;
        _request = Task.Run(() => JsonConvert.DeserializeObject<RewardStatusResponse>(RequestHandler.PostJson(
            RewardWire.ConsentRoute, JsonConvert.SerializeObject(new RewardConsentRequest { Enabled = enable }))));
    }

    internal static string Label => Busy ? "PROFILE REWARDS: …"
        : !Status.Available ? "PROFILE REWARDS: UNAVAILABLE"
        : Status.Enabled ? "PROFILE REWARDS: ON" : "PROFILE REWARDS: OFF";

    internal static string Detail
    {
        get
        {
            if (!Status.Available) return "Optional rewards are unavailable on this server.";
            string state = !string.IsNullOrEmpty(CaptureProblem) ? CaptureProblem
                : Status.State == "withheld" && !string.IsNullOrWhiteSpace(Status.Detail) ? Status.Detail
                : Status.Enabled && !Status.Enrolled ? "Activation pending — restart the normal SPT server."
                : !Status.Enabled ? "Optional RUB mail for your normal profile; activation and payout on normal server start."
                : Status.PendingRoubles > 0 ? $"{Status.PendingRoubles:N0} ₽ pending — paid on the next normal server start."
                : Status.LastPaidRoubles > 0 ? $"Last payout: {Status.LastPaidRoubles:N0} ₽ by mail."
                : !string.IsNullOrWhiteSpace(Status.Detail) ? Status.Detail
                : "Rewards enabled; paid by mail on the next normal server start.";
            return state + $"\n{Status.RoublesPerKill:N0} ₽ / kill · {Status.RoublesPerRoundWin:N0} ₽ / round win · max. {Status.MaxRoubles:N0} ₽ / match";
        }
    }
}
