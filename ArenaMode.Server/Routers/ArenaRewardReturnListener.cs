using System.Net;
using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Services.Hosted;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Routers;

// Separate from SPT's debug-logging router: the capability is never a public client DTO.
[Injectable]
public sealed class ArenaRewardReturnListener(SaveServer saves, ArenaSessionService sessions,
    ProfileSnapshotService snapshots, OrdinaryRaidLifecycleService ordinary, JsonUtil json, CoreConfig config) : IHttpListener
{
    private int _shutdownRequested;
    public bool CanHandle(HttpContext context) => context.Request.Path == RewardReturnControl.Route;

    public async Task HandleAsync(MongoId id, HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        RewardReturnRequest? request = null;
        bool authorized = false;
        try
        {
            if (!RewardReturnQuiescence.Enabled || context.Request.Method != "POST"
                || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
                || context.Request.ContentLength > RewardConsentTransport.MaxBytes
                || !saves.ProfileExists(id)) throw new RewardDataException("return-unavailable");
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            string body = await RewardConsentTransport.ReadBoundedAsync(context.Request.Body, readTimeout.Token);
            using var document = JsonDocument.Parse(body);
            RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
            request = document.RootElement.Deserialize<RewardReturnRequest>(RewardSettlementCore.JournalJson)
                ?? throw new RewardDataException("return-empty-request");
            var control = RewardRuntimeCoordinator.Current.ReturnControl;
            if (!control.Authorize(id.ToString(), request)) throw new RewardDataException("return-not-authorized");
            authorized = true;
            if (!RewardReturnQuiescence.Sequence.UpdatesStopped
                && !NeedsStartup(RewardRuntimeCoordinator.Current.GetStatus(id.ToString())))
            {
                control.ReleaseBeforeQuiescence(id.ToString(), request);
                await context.Response.WriteAsync(RewardRuntimeFiles.Json(new RewardReturnReply(false, "no-pending-rewards")), cancellationToken);
                return;
            }

            var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
            var hostedServices = context.RequestServices.GetServices<IHostedService>().ToArray();
            if (hostedServices.Any(service => !IsKnownHostedService(service.GetType())))
                throw new RewardDataException("return-unreviewed-background-service");
            var updates = hostedServices.OfType<SPTStartupHostedService>().Single();
            if (lifetime.ApplicationStopping.IsCancellationRequested || lifetime.ApplicationStopped.IsCancellationRequested)
                throw new RewardDataException("return-server-already-stopping");

            // A dropped HTTP client cannot cancel a half-stopped server. The operation has its own
            // bound and reports a held state if a stopped update loop cannot safely be followed by exit.
            using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var reply = await RewardReturnQuiescence.Sequence.PrepareAsync(
                () => control.Authorize(id.ToString(), request) && IsIdle(),
                SaveAndVerify,
                async ct =>
                {
                    await updates.StopAsync(ct);
                    if (updates.ExecuteTask is not { IsCompleted: true })
                        throw new RewardDataException("return-update-loop-still-running");
                    // A fault is not proof of a healthy completed update. Native graceful cancellation
                    // is caught inside ExecuteAsync, so a faulted task refuses automatic shutdown.
                    if (updates.ExecuteTask.IsFaulted) throw new RewardDataException("return-update-loop-faulted");
                }, operationTimeout.Token);

            if (!reply.Accepted && !reply.Held) control.ReleaseBeforeQuiescence(id.ToString(), request);
            if (reply.Accepted)
            {
                // Register after durable save proof, before writing the acknowledgement. The helper
                // also pins the old process and waits for its actual exit before starting a successor.
                context.Response.OnCompleted(() =>
                {
                    if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
                    {
                        ArenaContext.Logger?.Info("[ArenaMode] Returning to the normal session; restarting the local server to deliver Arena rewards.");
                        lifetime.StopApplication();
                    }
                    return Task.CompletedTask;
                });
            }
            else if (reply.Held)
                ArenaContext.Logger?.Warning("[ArenaMode] Return paused: the local server retained its profiles in memory because a safe final save could not be confirmed. Do not close it; retry the Arena return.");
            await context.Response.WriteAsync(RewardRuntimeFiles.Json(reply), CancellationToken.None);
        }
        catch (Exception)
        {
            bool held = ReturnHeld(RewardReturnQuiescence.Sequence, RewardReturnQuiescence.Requests);
            if (authorized && !held && request is not null)
                RewardRuntimeCoordinator.Current.ReturnControl.ReleaseBeforeQuiescence(id.ToString(), request);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = authorized ? 200 : 400;
                await context.Response.WriteAsync(RewardRuntimeFiles.Json(new RewardReturnReply(false,
                    held ? "return-held-preserving-profile" : "return-unavailable", held)), CancellationToken.None);
            }
        }
    }

    private bool IsIdle()
    {
        if (!RewardReturnControl.NoOtherGameOrServerProcesses()) return false;
        var profiles = saves.GetProfiles();
        if (profiles.Count is 0 or > 1024) return false;
        foreach (var pair in profiles)
        {
            if (!ArenaRewardRouter.CanChange(pair.Key, saves, sessions, snapshots, ordinary)) return false;
            using var document = JsonDocument.Parse(Serialize(pair.Value));
            if (!IdleProfile(document.RootElement)) return false;
        }
        return true;
    }

    internal static bool IdleProfile(JsonElement profile)
    {
        if (!profile.TryGetProperty("inraid", out var inraid) || inraid.ValueKind != JsonValueKind.Object) return false;
        foreach (string name in new[] { "location", "character" })
            if (inraid.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null
                && (field.ValueKind != JsonValueKind.String || field.GetString() != "none")) return false;
        return true;
    }

    internal static bool NeedsStartup(RewardStatusResponse status) => status.Enabled
        && (!status.Enrolled || status.PendingRoubles > 0 || status.PendingExperience > 0);

    internal static bool ReturnHeld(RewardReturnSequence sequence, RewardReturnRequestGate requests)
        => sequence.UpdatesStopped || requests.Held;

    internal static bool IsKnownHostedService(Type type)
    {
        if (type == typeof(SPTStartupHostedService)) return true;
        var assembly = type.Assembly.GetName();
        if (Convert.ToHexStringLower(assembly.GetPublicKeyToken() ?? []) != "adb9793829ddae60") return false;
        return type.FullName == "Microsoft.AspNetCore.Hosting.GenericWebHostService"
                && assembly.Name == "Microsoft.AspNetCore.Hosting"
            // Native cookie authentication registers this service. It warms the key ring
            // on startup; StopAsync is completed and never writes a player profile.
            || type.FullName == "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService"
                && assembly.Name == "Microsoft.AspNetCore.DataProtection";
    }

    private async Task SaveAndVerify(CancellationToken ct)
    {
        var profiles = saves.GetProfiles();
        if (profiles.Count is 0 or > 1024) throw new RewardDataException("return-profile-set-unavailable");
        await SaveWithoutAborting(saves.SaveAsync, ct);
        string root = RewardStartupFiles.Canonical(Directory.GetCurrentDirectory());
        foreach (var pair in profiles)
        {
            ct.ThrowIfCancellationRequested();
            string id = pair.Key.ToString();
            if (!RewardIdentity.IsHex(id, 24) || saves.IsProfileInvalidOrUnloadable(pair.Key))
                throw new RewardDataException("return-profile-unavailable");
            string expected = Serialize(pair.Value);
            string path = Path.Combine(root, "user", "profiles", id + ".json");
            RewardStartupFiles.RequireLocalPath(root, path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new RewardDataException("return-profile-size");
            using var reader = new StreamReader(input);
            string actual = await reader.ReadToEndAsync(ct);
            string after = Serialize(saves.GetProfile(pair.Key));
            try { RequireSavedProfile(expected, actual, after); }
            catch (RewardDataException)
            {
                ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return: profile readback refused (profile {RewardIdentity.Hash(id)[..8]}).");
                throw;
            }
        }
        if (!profiles.Keys.ToHashSet().SetEquals(saves.GetProfiles().Keys))
            throw new RewardDataException("return-profile-set-changed");
    }

    internal static void RequireSavedProfile(string before, string disk, string after)
    {
        if (!RewardRuntimeFiles.SameProfile(before, disk) || !RewardRuntimeFiles.SameProfile(before, after))
            throw new RewardDataException("return-profile-save-not-confirmed");
    }

    internal static async Task SaveWithoutAborting(Func<CancellationToken, Task> save, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Native SaveProfileAsync caches the new hash before its atomic file write.
        // Cancelling that write could make a later save skip unsaved RAM. Keep the
        // gates and await completion; a timed-out helper must retry the held operation.
        await save(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
    }

    private string Serialize<T>(T profile) => json.Serialize(profile, !config.Features.CompressProfile)
        ?? throw new RewardDataException("return-profile-serialization-failed");
}
