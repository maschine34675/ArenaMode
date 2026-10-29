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
    private readonly RewardShutdownReservation _reservation = new();
    public bool CanHandle(HttpContext context) => context.Request.Path == RewardReturnControl.Route
        || context.Request.Path == RewardEntryControl.Route;

    public async Task HandleAsync(MongoId id, HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        RewardReturnRequest? request = null;
        RewardEntryRequest? entryRequest = null;
        RewardEntryGrant? entryGrant = null;
        RewardReturnReply reply;
        bool entering = context.Request.Path == RewardEntryControl.Route;
        bool authorized = false;
        bool reserved = false;
        string stage = "admission";
        string refusal = "admission-failed";
        try
        {
            if (!RewardReturnQuiescence.Enabled) throw Refused("native-gates-disabled");
            if (context.Request.Method != "POST") throw Refused("request-method");
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
                throw Refused("request-not-loopback");
            if (context.Request.ContentLength > RewardConsentTransport.MaxBytes) throw Refused("request-content-length");
            if (!saves.ProfileExists(id)) throw Refused("profile-not-loaded");
            stage = "read-request";
            refusal = "request-body-read-failed";
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            string body = await RewardConsentTransport.ReadBoundedAsync(context.Request.Body, readTimeout.Token);
            stage = "parse-request";
            refusal = "request-json-invalid";
            using var document = JsonDocument.Parse(body);
            refusal = "request-properties-invalid";
            RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
            stage = "resolve-control";
            refusal = "runtime-control-unavailable";
            var control = RewardRuntimeCoordinator.Current.ReturnControl;
            stage = "deserialize-request";
            refusal = entering ? "entry-request-invalid" : "return-request-invalid";
            if (entering)
            {
                entryRequest = document.RootElement.Deserialize<RewardEntryRequest>(RewardSettlementCore.JournalJson)
                    ?? throw Refused("entry-empty-request");
                stage = "authorize-request";
                refusal = "entry-authorization-failed";
                if (!RewardEntryControl.Current.Authorize(id.ToString(), entryRequest, out entryGrant))
                    throw Refused("entry-not-authorized");
            }
            else
            {
                request = document.RootElement.Deserialize<RewardReturnRequest>(RewardSettlementCore.JournalJson)
                    ?? throw Refused("return-empty-request");
                stage = "authorize-request";
                refusal = "return-authorization-failed";
                if (!control.Authorize(id.ToString(), request)) throw Refused("return-not-authorized");
            }
            authorized = true;
            if (!_reservation.Reserve(entering, id.ToString(), (object?)entryRequest ?? request!))
                throw Refused("shutdown-operation-busy");
            reserved = true;
            stage = "native-lifecycle";
            refusal = "native-lifecycle-failed";
            if (!entering && !RewardReturnQuiescence.Sequence.UpdatesStopped
                && !NeedsStartup(RewardRuntimeCoordinator.Current.GetStatus(id.ToString())))
            {
                Release();
                await context.Response.WriteAsync(RewardRuntimeFiles.Json(new RewardReturnReply(false, "no-pending-rewards")), cancellationToken);
                return;
            }

            var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
            var hostedServices = context.RequestServices.GetServices<IHostedService>().ToArray();
            if (hostedServices.Any(service => !IsKnownHostedService(service.GetType())))
                throw Refused("return-unreviewed-background-service");
            var updates = hostedServices.OfType<SPTStartupHostedService>().Single();
            if (lifetime.ApplicationStopping.IsCancellationRequested || lifetime.ApplicationStopped.IsCancellationRequested)
                throw Refused("return-server-already-stopping");

            // Registers the graceful exit once the reply has been written. Used by every outcome of a stop
            // that got past the point of no return (see RewardEntryPublication).
            void ScheduleShutdown(bool succeeded) => context.Response.OnCompleted(() =>
            {
                if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
                {
                    ArenaContext.Logger?.Info(!succeeded
                        ? "[ArenaMode] The normal server is stopping; no Arena session was started."
                        : entering
                            ? "[ArenaMode] Starting a private Arena session; the normal server has saved and will stop."
                            : "[ArenaMode] Returning to the normal session; restarting the local server to deliver Arena rewards.");
                    lifetime.StopApplication();
                }
                return Task.CompletedTask;
            });

            string? preflightUnavailable = null;
            if (entering && !RewardReturnQuiescence.Sequence.UpdatesStopped)
            {
                // Everything the proof publication could refuse is decided NOW, while pausing is still undoable.
                // A damaged reward authority is no reason to refuse (the session simply runs without rewards);
                // anything else declines the entry and the helper reopens the normal session.
                stage = "entry-preflight";
                refusal = "entry-preflight-failed";
                try { preflightUnavailable = RewardOfflineSession.PreflightReady(Directory.GetCurrentDirectory(), entryGrant!, entryRequest!); }
                catch (Exception preflight)
                {
                    string why = preflight is RewardDataException ? preflight.Message : preflight.GetType().Name;
                    ArenaContext.Logger?.Warning($"[ArenaMode] Arena session entry declined before the server was paused ({why}); the normal session continues untouched.");
                    Release();
                    reply = RewardEntryPublication.Declined(why,
                        !lifetime.ApplicationStopping.IsCancellationRequested && !lifetime.ApplicationStopped.IsCancellationRequested);
                    stage = "write-response";
                    refusal = "response-write-failed";
                    await context.Response.WriteAsync(RewardRuntimeFiles.Json(reply), CancellationToken.None);
                    return;
                }
                if (preflightUnavailable is not null) WarnAuthorityUnavailable(id.ToString(), preflightUnavailable);
            }

            // A dropped HTTP client cannot cancel a half-stopped server. The operation has its own
            // bound and reports a held state if a stopped update loop cannot safely be followed by exit.
            using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            stage = "save-and-drain";
            refusal = "save-and-drain-failed";
            reply = await RewardReturnQuiescence.Sequence.PrepareAsync(
                () => (entering ? RewardEntryControl.Current.Authorize(id.ToString(), entryRequest!, out _)
                    : control.Authorize(id.ToString(), request!)) && IsIdle(),
                SaveAndVerify,
                async ct =>
                {
                    await updates.StopAsync(ct);
                    if (updates.ExecuteTask is not { IsCompleted: true })
                        throw Refused("return-update-loop-still-running");
                    // A fault is not proof of a healthy completed update. Native graceful cancellation
                    // is caught inside ExecuteAsync, so a faulted task refuses automatic shutdown.
                    if (updates.ExecuteTask.IsFaulted) throw Refused("return-update-loop-faulted");
                }, operationTimeout.Token);

            if (!reply.Accepted && !reply.Held) Release();
            reply = reply with { CanResumeNormal = entering && reply.CanResumeNormal
                && !RewardReturnQuiescence.Requests.Held && !RewardReturnQuiescence.Updates.Held
                && !lifetime.ApplicationStopping.IsCancellationRequested && !lifetime.ApplicationStopped.IsCancellationRequested };
            // Register the exit after durable save proof, before writing the acknowledgement. The helper
            // also pins the old process and waits for its actual exit before starting a successor.
            if (reply.Accepted)
            {
                if (entering)
                {
                    stage = "publish-entry-proof";
                    refusal = "entry-proof-publication-failed";
                    // Durable proof is mandatory even when the HTTP reply is lost. A process exit
                    // alone could also be a crash before its final native save finished. The native
                    // update loop is stopped for good by now, so a failure here must still end in the
                    // graceful exit (RewardEntryPublication), never in a paused server without one.
                    reply = await RewardEntryPublication.PublishAsync(
                        // A read error that only shows up now (the preflight read was fine or differed) is reported here, once.
                        () => RewardOfflineSession.CreateReady(Directory.GetCurrentDirectory(), entryGrant!, entryRequest!,
                            reason => { if (reason != preflightUnavailable) WarnAuthorityUnavailable(id.ToString(), reason); }),
                        reply, ScheduleShutdown, message => ArenaContext.Logger?.Warning("[ArenaMode] " + message));
                }
                else ScheduleShutdown(true);
            }
            else if (reply.Held)
                ArenaContext.Logger?.Warning("[ArenaMode] Session switch paused: the local server retained its profiles in memory because a safe final save could not be confirmed. Do not close it; retry the session switch.");
            stage = "write-response";
            refusal = "response-write-failed";
            await context.Response.WriteAsync(RewardRuntimeFiles.Json(reply), CancellationToken.None);
        }
        catch (Exception exception)
        {
            bool held = ReturnHeld(RewardReturnQuiescence.Sequence, RewardReturnQuiescence.Requests);
            // Before authorization, only fixed local codes are safe to log. Keep the
            // existing native-save diagnostics once the local capability was verified.
            string diagnostic = authorized
                ? exception is RewardDataException ? exception.Message : exception.GetType().Name
                : refusal;
            try { ArenaContext.Logger?.Debug($"[ArenaMode] Session shutdown refused at {stage}: {diagnostic}; authorized={authorized}; held={held}."); }
            catch { } // Diagnostics must not change the response or held-state handling.
            if (authorized && !held) Release();
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = authorized ? 200 : 400;
                await context.Response.WriteAsync(RewardRuntimeFiles.Json(new RewardReturnReply(false,
                    held ? "return-held-preserving-profile" : "return-unavailable", held)), CancellationToken.None);
            }
        }

        RewardDataException Refused(string reason)
        {
            refusal = reason;
            return new RewardDataException(reason);
        }

        void Release()
        {
            if (entryRequest is not null) RewardEntryControl.Current.ReleaseBeforeQuiescence(id.ToString(), entryRequest);
            else if (request is not null) RewardRuntimeCoordinator.Current.ReturnControl.ReleaseBeforeQuiescence(id.ToString(), request);
            if (reserved) _reservation.Release(entering, id.ToString(), (object?)entryRequest ?? request!);
        }
    }

    private static void WarnAuthorityUnavailable(string profile, string reason)
        => ArenaContext.Logger?.Warning($"[ArenaMode] Profile rewards are unavailable for this Arena session (profile {profile}): "
            + $"the reward records do not match this installation or profile ({reason}). The session starts normally without rewards. "
            + "Remedy: docs/REWARDS-RUNTIME.md, 'Lost reward authority'.");

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
