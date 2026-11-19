using System.Diagnostics;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// Consumes explicitly approved receipts before native profiles/HTTP/timers start.
/// Fresh profiles default to rewards ON at this guarded boundary. Existing
/// choices/history remain authoritative; this is the only profile payout writer.
/// Priority <see cref="OnLoadOrder.Watermark"/> (0), the earliest slot SPT runs. Its pre-load runner
/// (ProgramExtensions.RunPreSptLoadCallbacks in the installed 4.1.6 IL) takes the <see cref="IOnLoad"/>
/// containers with a priority from 0 up to, not including, <see cref="OnLoadOrder.GameCallbacks"/> in the
/// order of the DI list and runs them one after the other; a negative priority never runs. That list is
/// a stable sort by priority over the registration order (SPT.Server, Reflection, Web, then the mods by
/// GUID, Core last), so a mod at the common "Watermark + 1" always runs after this step, and only a mod
/// that also chooses 0 and sorts before "com.maschine.ArenaMode" by GUID can precede it, which
/// <see cref="EarlierCallbacks"/> detects by position. From 0.3.0 up to and including 0.4.1 this step sat
/// at Watermark + 1 and refused every other mod in the range 0 to 1, before or after it, which switched
/// rewards off for good without naming the mod (maschine34675/ArenaMode#1, BetterFlea's item-table
/// snapshot at Watermark + 1).
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Watermark)]
public sealed class ArenaRewardStartup(
    SaveServer saves,
    IReadOnlyList<DependencyInjectionContainer> containers,
    ISptLogger<ArenaRewardStartup> logger) : IOnLoad, IDisposable
{
    // Tests: the coordinator this start informs; null is the process-wide one.
    internal RewardRuntimeCoordinator? Coordinator { get; init; }
    private RewardRuntimeCoordinator Rewards => Coordinator ?? RewardRuntimeCoordinator.Current;
    // Tests: the number of profiles SPT has loaded so far; null reads the SaveServer.
    internal Func<int>? LoadedProfiles { get; init; }
    // Tests: the pause between two attempts of a recovery that hit a file error.
    internal TimeSpan RecoveryRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    private int _entered;
    private RewardStartupLifetimeLock? _lifetimeLock;
    private RewardOfflinePrivate? _offlineOwner;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _entered, 1) != 0) return;
        var root = RewardStartupFiles.Canonical(Directory.GetCurrentDirectory());
        var admission = await AdmitAsync(root, cancellationToken);
        _offlineOwner = admission.Owner;
        _lifetimeLock = admission.Lifetime;
        if (admission.Unusable is { } unusable)
        {
            if (admission.StorageUnusable)
            {
                Rewards.ObserveStartupUnavailable(unusable, storageUnusable: true);
                logger.Warning($"[ArenaMode] Profile rewards are unavailable: {DescribeUnusable(unusable)} "
                    + "Rewards stay off and no reward data is written; Arena itself and normal raids are unaffected. See docs/REWARDS-RUNTIME.md, 'Lost reward authority'.");
            }
            else
            {
                // An unexpected failure before this start owned anything: rewards pause. No Arena session may start on top
                // of an unsettled one this start could not look at, but with no session on record Arena stays usable.
                bool pending = RewardOfflineSession.MayHaveUnsettledSession(root);
                Rewards.ObserveRewardsPaused(unusable, recoveryPending: pending);
                RewardPausedNote.Write(root, unusable, recoveryPending: pending);
                logger.Warning(ComposeAdmissionPauseWarning(unusable, pending));
            }
            return;
        }
        if (!admission.Proceed) return;
        using var startupGate = admission.Gate;
        // Only a proven or not-excludable live second writer stops SPT here (it throws). Every other recovery
        // problem pauses rewards for this start: nothing is paid, and the next start retries from the records.
        var pause = await RecoverOrPauseAsync(root, () => LoadedProfileCount() == 0,
            () => logger.Info("[ArenaMode] Old Arena session discarded; rewards set to OFF. Normal SPT will start."),
            cancellationToken, RecoveryRetryDelay,
            retrying: (attempt, e) => logger.Debug($"[ArenaMode] Offline Arena recovery hit {e.GetType().Name}; retry {attempt} of {RecoveryAttempts - 1}."));
        if (pause is not null)
        {
            bool pending = RewardOfflineSession.MayHaveUnsettledSession(root);
            Rewards.ObserveRewardsPaused(pause.Code, recoveryPending: pending);
            RewardPausedNote.Write(root, pause.Code, recoveryPending: pending);
            logger.Warning(ComposeRecoveryPauseWarning(pause, pending));
            return;
        }
        RewardPausedNote.Remove(root);
        var callbacks = containers.Select(c => new StartupCallback(c.ParentType, c.Type, c.InjectableAttribute.TypePriority)).ToList();
        try
        {
            var reason = GetAdmissionFailure(
                typeof(SaveServer).Assembly.GetName().Version,
                Assembly.GetEntryAssembly()?.GetName().Name,
                root,
                AppContext.BaseDirectory,
                LoadedProfileCount(),
                callbacks,
                FindConflictingServer(root));
            if (reason is not null) throw new RewardDataException(reason);
            if (!RewardStartupFiles.MarkerAbsent(root)) return;
            using var window = new RewardStartupWindow(root, () => LoadedProfileCount() == 0);
            var colaStop = await ProcessUntilColaStopAsync(window, outcome =>
            {
                Rewards.ObserveStartupOutcome(outcome);
                LogAuthority(outcome);
                if (outcome.Result.Status == RewardSettlementStatus.Paid)
                    logger.Info($"[ArenaMode] Arena reward: {outcome.Roubles} RUB delivered by mail; {outcome.Experience} XP credited.");
                else if (outcome.Result.Status is RewardSettlementStatus.Quarantined or RewardSettlementStatus.Rejected)
                    logger.Debug($"[ArenaMode] Reward skipped for profile {outcome.ProfileId} ({outcome.Result.Reason}).");
                else
                    logger.Debug($"[ArenaMode] Startup reward: {outcome.Result.Status} ({outcome.Result.Reason}).");
            }, cancellationToken);
            if (colaStop is not null)
            {
                // The Arena session itself is settled (recovery above finished): only rewards pause, Arena stays usable.
                Rewards.ObserveRewardsPaused(ColaPauseCode, recoveryPending: false);
                RewardPausedNote.Write(root, ColaPauseCode, recoveryPending: false);
                logger.Warning(ComposeColaPauseWarning(colaStop));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            // A refusal affects rewards, never ordinary server startup or raids.
            var reason = e is RewardDataException ? e.Message : e.GetType().Name;
            Rewards.ObserveStartupUnavailable(reason);
            logger.Warning(ComposeRefusalWarning(reason, callbacks));
        }
    }

    internal const string ColaPauseCode = "reward-cola-recovery-incomplete";

    /// <summary>
    /// Settles every profile in order. A Tarkova Cola recovery failure (a payment may exist while its companion write is
    /// unfinished) does not stop SPT: it ends the pass and is returned, so rewards pause; profiles settled before it were
    /// reported to <paramref name="onOutcome"/> and stay settled, and the next start retries the Cola recovery first.
    /// </summary>
    internal static async Task<RewardColaRecoveryException?> ProcessUntilColaStopAsync(RewardStartupWindow window,
        Action<RewardStartupOutcome> onOutcome, CancellationToken cancellationToken)
    {
        try
        {
            await RewardStartupProcessor.ProcessAsync(window, cancellationToken, onOutcome);
            return null;
        }
        catch (RewardColaRecoveryException e) { return e; }
    }

    private int LoadedProfileCount() => (LoadedProfiles ?? (() => saves.GetProfiles().Count))();

    internal const int RecoveryAttempts = 3;

    /// <summary>Why this start's recovery of the last Arena session paused rewards, and the failure behind it (for the Warning).</summary>
    internal sealed record RecoveryPause(string Code, Exception Cause);

    /// <summary>
    /// Runs the recovery of the last Arena session. Returns null when it finished (or there was nothing to do) and the pause
    /// when it could not: SPT then starts with rewards paused. Throws, and so stops SPT, only when another process may be
    /// writing: a live or unverifiable private process, a changed active pointer, a private server appearing under this
    /// root, or cancellation. A file error is retried (a virus scanner holds a file for a moment) before it pauses.
    /// </summary>
    internal static async Task<RecoveryPause?> RecoverOrPauseAsync(string root, Func<bool> profilesUnloaded, Action discarded,
        CancellationToken cancellationToken, TimeSpan? retryDelay = null, Action<string>? checkpoint = null,
        Action<int, Exception>? retrying = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var recovery = new RewardStartupWindow(root, profilesUnloaded);
                RewardOfflineSession.RecoverBeforeStartup(recovery, checkpoint, discarded);
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch (RewardLiveOwnerException e)
            {
                throw new RewardOfflineException($"Offline Arena recovery is incomplete ({e.Message}); startup stopped because another Arena or SPT process may still be writing.", e)
                { LiveWriter = true };
            }
            catch (RewardOfflineException e) when (e.LiveWriter) { throw; }
            catch (Exception) when (PrivateTreeAppeared(root)) { throw; }
            catch (RewardDataException e) when (e.Message == "native-startup-boundary-unavailable")
            {
                return new(e.Message, e);
            }
            catch (RewardOfflineException e)
            {
                if (attempt < RecoveryAttempts && e.InnerException is IOException or UnauthorizedAccessException)
                {
                    retrying?.Invoke(attempt, e.InnerException);
                    await Task.Delay(retryDelay ?? TimeSpan.FromMilliseconds(500), cancellationToken);
                    continue;
                }
                // Every recovery pause carries the "offline-" prefix: the card and the lease choose their sentence from it.
                string code = e.ReasonCode;
                return new(RewardUnavailableText.LeaseCode(code.StartsWith("offline-", StringComparison.Ordinal) ? code : "offline-recovery-" + code), e);
            }
        }
    }

    // Recovery runs on the normal tree. A private server's marker or lease file under this root means a private server
    // starts here: never a reason to pause.
    private static bool PrivateTreeAppeared(string root)
    {
        try { return !RewardStartupFiles.MarkerAbsent(root) || File.Exists(Path.Combine(root, RewardOfflineSession.LeaseFile)); }
        catch { return true; }
    }

    internal static string ComposeRecoveryPauseWarning(RecoveryPause pause, bool recoveryPending = true)
    {
        if (pause.Code == "native-startup-boundary-unavailable")
            return "[ArenaMode] Arena rewards are paused for this start: another mod loads the profiles before Arena's reward step (native-startup-boundary-unavailable). "
                + "SPT starts normally; the last Arena session, if any, stays unsettled and is retried at the next start."
                + (recoveryPending ? " Until then Arena cannot start a new session." : " No Arena session is on record, so Arena itself is unaffected.");
        string remedy = pause.Cause is RewardOfflineException { ExplainsItself: true } own ? own.Message
            : pause.Cause.InnerException is IOException or UnauthorizedAccessException
                ? "Close programs that may lock user\\arena-rewards (virus scanner, backup, sync) and restart SPT."
                : "Restart SPT once. If the same code returns, keep user\\arena-rewards as it is (do not delete or move it) and report the code.";
        // Recovery works in steps and a step may already have been written before a later one failed: the retry continues
        // from the records and never repeats a step, so the claim is only that no reward was paid.
        return $"[ArenaMode] Arena rewards are paused for this start: the last Arena session could not be settled ({pause.Code}: {DescribePauseCode(pause.Code)}). "
            + "SPT starts normally. No reward was paid, the Arena session is kept and is retried from its records at the next start; "
            + (recoveryPending ? "until then Arena cannot start a new session and the reward card says rewards are paused. "
                : "the reward card says rewards are paused. ")
            + $"Remedy: {remedy} See docs/REWARDS-RUNTIME.md, 'Rewards paused at start'.";
    }

    internal static string ComposeAdmissionPauseWarning(string code, bool recoveryPending = true)
        => $"[ArenaMode] Profile rewards are paused for this start: the reward step failed before it could take ownership ({code}). "
            + "SPT starts normally and nothing was paid or enrolled. "
            + (recoveryPending ? "No new Arena session starts on top of an unsettled one. " : "No Arena session is on record, so Arena itself is unaffected. ")
            + "This is retried at the next start. See docs/REWARDS-RUNTIME.md, 'Rewards paused at start'.";

    internal static string ComposeColaPauseWarning(RewardColaRecoveryException e)
        => DescribeColaStop(e) + $" Profile rewards are paused for this start ({ColaPauseCode}): SPT starts normally, nothing further is paid, "
            + "and the next start retries this recovery first. Arena itself is unaffected.";

    private static string DescribePauseCode(string code) => code switch
    {
        "offline-activation-proof-missing" => "a record of the session is missing",
        "offline-consent-conflict" => "the reward choice changed during the session",
        "offline-multiple-unclosed-sessions" => "more than one unfinished session",
        "offline-recovery-lease-mismatch" or "offline-history-binding-mismatch" => "the session was recorded by another installation folder",
        "offline-inbox-conflict" => "a stored reward receipt differs from the session's",
        _ => "see the code"
    };

    internal static string ComposeRefusalWarning(string reason, IEnumerable<StartupCallback> callbacks)
        => $"[ArenaMode] Startup rewards unavailable ({DescribeRefusal(reason, callbacks)}); profiles will load normally."
            + (RewardUnavailableText.Recurs(reason)
                ? " This recurs at every start while the installation stays as it is: no profile is enrolled, nothing is recorded or paid, and the reward card says 'Rewards are off'."
                : "");

    // The reason code stays bare (the menu text and the session lease are chosen from it); the log adds the callbacks
    // behind an order refusal, which is what a player needs to find the mod and what its author needs to change.
    internal static string DescribeRefusal(string reason, IEnumerable<StartupCallback> callbacks)
    {
        if (reason != "earlier-startup-callback") return reason;
        var earlier = EarlierCallbacks(callbacks);
        return earlier.Count == 0 ? reason : reason + ": " + string.Join(", ", earlier.Select(c => c.Describe()));
    }

    // The exception's own reason is written for the player (no stack, no path); a wrapped failure adds only its
    // reason code or type name, like the generic handler below.
    internal static string DescribeColaStop(RewardColaRecoveryException e)
    {
        string cause = e.InnerException is null ? "" : $" ({(e.InnerException is RewardDataException ? e.InnerException.Message : e.InnerException.GetType().Name)})";
        // Older reasons are bare codes or a sentence with its own full stop: one sentence end, the cause before it.
        string reason = e.Message.TrimEnd().TrimEnd('.');
        return $"[ArenaMode] The Tarkova Cola reward files are kept as they are: {reason}{cause}. "
            + "Do not delete or move the reward journal or the Cola plan files. See docs/REWARDS-RUNTIME.md, 'Lost reward authority'.";
    }

    internal sealed record StartupCallback(Type Parent, Type Contract, int Priority)
    {
        // "BetterFlea.Flea.ModItemsLoad in BetterFlea.dll (priority 1)": the type, its assembly and the priority are what
        // a player needs to find the mod and what its author needs to change.
        internal string Describe() => $"{Parent.FullName} in {Parent.Assembly.GetName().Name}.dll (priority {Priority})";
    }

    /// <summary>Proceed false with nothing else set: a private start or a bootstrap that declined; Unusable: skip rewards.</summary>
    internal sealed record StartupAdmission(bool Proceed, RewardOfflinePrivate? Owner = null,
        RewardStartupLifetimeLock? Gate = null, RewardStartupLifetimeLock? Lifetime = null, string? Unusable = null,
        bool StorageUnusable = false);

    // The ownership steps that must precede native profile loading. A genuine contention (a held .startup.lock
    // beyond two minutes, a held .server.lock, a live private owner, cancellation) throws and stops this
    // startup; a reward folder that merely cannot be used does not (see UnusableStorage).
    internal static async Task<StartupAdmission> AdmitAsync(string root, CancellationToken cancellationToken,
        TimeSpan? startupWait = null)
    {
        RewardStartupLifetimeLock? gate = null, lifetime = null;
        bool startupBusy = false, serverLockStage = false;
        try
        {
            // Offline private ownership must precede every native profile load. It
            // intentionally bypasses the ordinary payout/enrollment startup path.
            if (RewardOfflineSession.Requested(root)) return new(false, Owner: RewardOfflineSession.StartPrivate(root));
            // Bootstrap only the ordinary metadata directory, not a profile or
            // enrollment. New installs must join the same coordination gate before
            // admission; missing state must not bypass it while a peer is writing.
            if (!RewardStartupDefaults.PrepareDirectory(root)) return new(false);
            // Every participating startup must wait BEFORE checking peers/refusing
            // rewards. Otherwise a second server could continue into native load while
            // the first is committing.
            startupBusy = true;
            gate = await RewardStartupLifetimeLock.AcquireStartupAsync(root, cancellationToken, startupWait);
            startupBusy = false;
            // The handle is authoritative even if ownership metadata is lost. Never
            // downgrade a busy runtime to "rewards unavailable, load profiles anyway".
            serverLockStage = true;
            try { lifetime = RewardStartupLifetimeLock.Acquire(root); }
            catch (IOException exception) when (RewardStartupLifetimeLock.IsContention(exception))
            { throw new RewardOfflineException("Another SPT or Arena session owns this installation. Stop or return from that session before starting normal SPT.", exception); }
            return new(true, Gate: gate, Lifetime: lifetime);
        }
        catch (Exception exception) when (SoftAdmission(exception, root, startupBusy, serverLockStage) is { } soft)
        {
            // No process we know of owns anything here: the reward folder simply cannot be used (the SPT folder or
            // one of its parents is a link, access is denied, the path is unavailable), or the admission failed for a
            // reason that is no sign of another writer. That is a reason to do no reward work at all, not a reason to
            // keep SPT itself from starting. No reward data was written (the metadata folder or a lock file may have
            // been created before a later step failed).
            gate?.Dispose();
            lifetime?.Dispose();
            return new(false, Unusable: soft.Reason, StorageUnusable: soft.Storage);
        }
        catch
        {
            gate?.Dispose();
            lifetime?.Dispose();
            throw;
        }
    }

    // Which admission failures let SPT start with rewards off. Hard (null): cancellation, a held lock or live owner
    // (RewardOfflineException, the two-minute .startup.lock wait, a contention IOException), anything on a private tree
    // (marker, lease file or active pointer present, or unreadable), anything while taking .server.lock that is not a
    // plain unusable folder, and a Tarkova Cola recovery failure (its payment may exist). Everything else is soft:
    // Storage true is the unusable-folder case, false an unexpected failure ("reward-admission-<code>").
    internal static (string Reason, bool Storage)? SoftAdmission(Exception exception, string root, bool startupBusy, bool serverLockStage = false)
    {
        if (exception is OperationCanceledException or RewardOfflineException or RewardLiveOwnerException or RewardColaRecoveryException) return null;
        if (startupBusy && exception is InvalidOperationException) return null;
        if (exception is IOException contention && RewardStartupLifetimeLock.IsContention(contention)) return null;
        if (LooksPrivate(root)) return null;
        if (UnusableStorage(exception, root) is { } storage) return (storage, true);
        if (serverLockStage) return null;
        string raw = exception is RewardDataException ? exception.Message : exception.GetType().Name;
        return (RewardUnavailableText.LeaseCode("reward-admission-" + raw), false);
    }

    // The reward folder is unusable for a reason that has nothing to do with another process owning it.
    // Returns the coordinator reason, or null when the failure is contention/ownership (fatal by design),
    // a private-session start (its ownership cannot be skipped), or something else entirely.
    internal static string? UnusableStorage(Exception exception, string root)
    {
        string? reason = exception switch
        {
            RewardDataException { Message: "reward-root-is-linked" or "reward-path-is-linked" or "reward-path-outside-root" } => "reward-storage-linked",
            UnauthorizedAccessException => "reward-storage-access-denied",
            IOException io when !RewardStartupLifetimeLock.IsContention(io) => "reward-storage-unavailable",
            _ => null
        };
        return reason is not null && !LooksPrivate(root) ? reason : null;
    }

    private static string DescribeUnusable(string reason) => reason switch
    {
        "reward-storage-linked" => "the SPT folder, one of its parent folders or the reward folder is a link (junction, symbolic link, mount point or cloud placeholder). Start SPT from the real folder path to use rewards.",
        "reward-storage-access-denied" => "SPT may not read or write its user/arena-rewards folder. Give the account running SPT full access to it to use rewards.",
        _ => "the user/arena-rewards folder could not be reached (path or disk error)."
    };

    // A private Arena server owns the original runtime's lifetime lock; skipping that is never an option. The
    // active-session pointer is looked up WITHOUT the link refusal on purpose: when this path is a link to an
    // install that a private session runs from (reached by another spelling), the pointer is visible through it.
    // `attributes` is the file-system probe; tests substitute it because Windows cannot be made to deny an attribute query.
    internal static bool LooksPrivate(string root, Func<string, FileAttributes>? attributes = null)
    {
        attributes ??= File.GetAttributes;
        try
        {
            foreach (var name in new[] { "arena-session.json", RewardOfflineSession.LeaseFile })
            {
                try { _ = attributes(Path.Combine(root, name)); return true; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            // Unreadable here is the very case this check lets through (access denied to the reward folder).
            try { _ = attributes(RewardOfflineSession.ActivePath(root)); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
        catch { return true; }
    }

    private void LogAuthority(RewardStartupOutcome outcome)
    {
        if (outcome.Authority is not { } notice) return;
        string cause = DescribeCause(notice.Trigger);
        if (notice.Recovered)
            logger.Info(DescribeRecovered(outcome, cause, notice));
        else
            logger.Warning($"[ArenaMode] Profile {outcome.ProfileId}: rewards stay off, because {cause} and {notice.Detail}. "
                + "Nothing is paid and nothing is deleted. Arena sessions still work. Remedy: docs/REWARDS-RUNTIME.md, 'Lost reward authority'.");
    }

    private static string DescribeCause(string trigger) => trigger switch
    {
        "profile-reward-block-missing" => "the profile lost its reward ledger (profile wipe, restored backup or editor)",
        "install-root-changed" => "the installation was moved or copied",
        _ => trigger
    };

    // The archive is done when this is logged, the enrollment that follows in the same pass may still have failed
    // (for example on a freshly wiped profile that is not yet confirmed clean) or may not apply (rewards switched off).
    internal static string DescribeRecovered(RewardStartupOutcome outcome, string cause, RewardAuthorityNotice notice)
    {
        string head = $"[ArenaMode] Profile {outcome.ProfileId}: {cause}, and no reward was unsettled, so its old reward records were archived at {notice.Archive}"
            + (notice.Detail.Length == 0 ? "" : $" ({notice.Detail})");
        if (outcome.Result.Status is RewardSettlementStatus.Quarantined or RewardSettlementStatus.Rejected
            || outcome.Result.Reason == "startup-storage-unavailable")
            return head + $". Enrolling again did not succeed in this start ({outcome.Result.Reason}); it follows at the next successful start. Your rewards setting was kept.";
        if (outcome.Result.Reason == "rewards-not-enabled")
            return head + ". Rewards are switched off, so nothing was enrolled; your rewards setting was kept and switching them on enrolls with a fresh start.";
        return head + " and rewards were enrolled again. Your rewards setting was kept.";
    }

    internal static string? GetAdmissionFailure(Version? version, string? entryName, string root, string applicationRoot,
        int loadedProfiles, IEnumerable<StartupCallback> callbacks, bool conflictingServer)
    {
        // The early Program callback was verified against installed 4.1.6. Unknown
        // platform contracts defer rewards; they do not make Arena unavailable.
        if (version != new Version(4, 1, 6, 0)) return "unreviewed-native-startup-version";
        if (entryName is not ("SPT.Server" or "SPT.Server.Linux" or "SPT.Server.Mac")) return "not-native-server-startup";
        if (!string.Equals(RewardStartupFiles.Canonical(root), RewardStartupFiles.Canonical(applicationRoot), RewardStartupFiles.PathComparison))
            return "runtime-root-mismatch";
        if (loadedProfiles != 0) return "native-profiles-already-loaded";
        if (conflictingServer) return "another-server-or-unknown-process";
        if (!PreLoadCallbacks(callbacks).Any(c => c.Parent == typeof(ArenaRewardStartup))) return "startup-callback-unlisted";
        if (EarlierCallbacks(callbacks).Count > 0) return "earlier-startup-callback";
        return null;
    }

    // What SPT 4.1.6's pre-load runner takes from the DI list, in its run order: the IOnLoad contracts with a priority
    // from Watermark (0) up to, not including, GameCallbacks (200000). Later priorities run in the hosted service after
    // the profiles are loaded, negative ones never (verified in the installed IL; the version gate above keeps it so).
    internal static List<StartupCallback> PreLoadCallbacks(IEnumerable<StartupCallback> callbacks) =>
        callbacks.Where(c => c.Contract == typeof(IOnLoad) && c.Priority >= OnLoadOrder.Watermark && c.Priority < OnLoadOrder.GameCallbacks).ToList();

    /// <summary>
    /// The pre-load callbacks that run before this step, in run order, without SPT's own Watermark (it prints the
    /// startup banner and touches no profile). Position decides, not priority: at an equal priority the stable DI sort
    /// keeps the registration order, so a mod at the same priority may sit on either side of this step, and a callback
    /// that runs after it cannot race its pre-load writes and is no reason to refuse. Empty when this step is not in
    /// the list at all (GetAdmissionFailure refuses that separately: the order is then unknown).
    /// </summary>
    internal static List<StartupCallback> EarlierCallbacks(IEnumerable<StartupCallback> callbacks)
    {
        var preLoad = PreLoadCallbacks(callbacks);
        int self = preLoad.FindIndex(c => c.Parent == typeof(ArenaRewardStartup));
        return self < 0 ? new() : preLoad.Take(self).Where(c => c.Parent != typeof(Watermark)).ToList();
    }

    private static bool FindConflictingServer(string root)
    {
        // The normal SPT port availability check runs AFTER pre-load callbacks.
        // Inspect peers first, and refuse if a potentially relevant process cannot
        // be identified. The lifetime file lock additionally covers cooperating
        // Arena versions. Arbitrarily renamed external profile writers are outside
        // this contract, just like editors or foreign constructor-spawned threads.
        if (!CurrentImageOwnsRoot(root, Environment.ProcessPath)) return true;
        using var current = Process.GetCurrentProcess();
        var processName = current.ProcessName;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    // A peer's executable directory does not prove its CWD: a
                    // second copy could still use our profile tree. V1 defers for
                    // every recognized server peer, including independent roots.
                    if (IsServerPeer(process.ProcessName, processName)) return true;
                }
                catch (InvalidOperationException) { if (!process.HasExited) return true; }
                catch (System.ComponentModel.Win32Exception) { return true; }
                catch (NotSupportedException) { return true; }
            }
        }
        return false;
    }

    internal static bool CurrentImageOwnsRoot(string root, string? image)
    {
        if (string.IsNullOrWhiteSpace(image) || !Path.IsPathFullyQualified(image)
            || !Path.GetFileName(image).StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            RewardStartupFiles.RequireLocalPath(root, image);
            return string.Equals(RewardStartupFiles.Canonical(Path.GetDirectoryName(image)!),
                RewardStartupFiles.Canonical(root), RewardStartupFiles.PathComparison);
        }
        catch { return false; }
    }

    internal static bool IsServerPeer(string candidateName, string currentName) =>
        candidateName.StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase)
        || string.Equals(candidateName, currentName, StringComparison.OrdinalIgnoreCase);

    public void Dispose() { _offlineOwner?.Dispose(); _lifetimeLock?.Dispose(); }
}
