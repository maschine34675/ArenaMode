using System.Net;
using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Servers.Http;

namespace ArenaMode.Server.Routers;

// Intentionally not a StaticRouter: the generic SPT listener may log request
// and response bodies in debug builds. This bounded loopback channel never does.
[Injectable]
public sealed class ArenaRewardControlListener(SaveServer saves, ArenaSessionService sessions,
    ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary) : IHttpListener
{
    public bool CanHandle(HttpContext context) => context.Request.Path == RewardConsentControl.Route;

    public async Task HandleAsync(MongoId id, HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Method != "POST" || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
                || context.Request.ContentLength > RewardConsentTransport.MaxBytes || saves.GetProfile(id) is null)
                throw new RewardDataException("reward-control-unavailable");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            string body = await RewardConsentTransport.ReadBoundedAsync(context.Request.Body, timeout.Token);
            using var doc = JsonDocument.Parse(body);
            RewardStartupFiles.RejectDuplicateProperties(doc.RootElement);
            var request = doc.RootElement.Deserialize<RewardControlRequest>(RewardSettlementCore.JournalJson)
                ?? throw new RewardDataException("reward-control-empty-request");
            RewardControlReply reply;
            using (await ArenaRaidStartGate.EnterAsync(id, timeout.Token))
                reply = RewardRuntimeCoordinator.Current.ExchangeControl(id.ToString(), request,
                    ArenaRewardRouter.CanChange(id, saves, sessions, snapshots, ordinary));
            if (reply.Status is not null)
            {
                try
                {
                    reply.Status.AutomaticReturn &= context.RequestServices.GetServices<IHostedService>()
                        .All(service => ArenaRewardReturnListener.IsKnownHostedService(service.GetType()));
                }
                catch { reply.Status.AutomaticReturn = false; }
            }
            await context.Response.WriteAsync(RewardRuntimeFiles.Json(reply), timeout.Token);
        }
        catch (Exception)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("{\"error\":\"reward-control-unavailable\"}", cancellationToken);
            }
        }
    }
}
