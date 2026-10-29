using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// What startup did about a reward authority that no longer matches. Recovered: the old records were
/// archived and a fresh enrollment follows in the same pass. Unresolved: nothing was changed, no payment is
/// possible, the server log carries the cause and the remedy.
/// </summary>
internal sealed record RewardAuthorityNotice(bool Recovered, string Trigger, string Detail, string? Archive)
{
    // The status card reads this prefix to say "does not match" instead of "temporarily unavailable".
    internal string Reason => RewardAuthorityRecovery.ReasonPrefix + Trigger;
}

/// <summary>
/// Startup only, inside the admitted pre-load window. The reward ledger lives in the profile while its
/// authority lives beside it, so the two can part: SPT's profile wipe builds a new profile (same info.id,
/// no extension block), a restored pre-enrollment backup or an editor drops the block, and a moved or
/// copied install keeps an enrollment bound to the old root. Nothing re-planned any of that, so rewards
/// stayed dead until the reward directory was removed by hand.
///
/// Recovery is deliberately narrow. It acts only when NOTHING is unsettled in the old directory: every
/// journal entry committed, every received receipt imported, no pending run or restoration, no external
/// (Tarkova Cola) progression, nothing unrecognised. Then the old directory is ARCHIVED by rename (never
/// deleted), the player's consent choice is carried over unchanged, and the ordinary enrollment path
/// creates a fresh epoch. A fresh epoch makes every receipt and key of the old one unusable, so nothing
/// can be paid twice, whichever copy of an install keeps the old authority. Anything unsettled keeps the
/// fail-closed quarantine and is reported at Warning level with the cause.
/// A profile that still carries a ledger block of ANOTHER epoch, or an older revision of its own ledger
/// (restored backup behind the journal), is never touched: the settlement core's existing divergence
/// checks keep refusing payment for it.
/// </summary>
internal static class RewardAuthorityRecovery
{
    internal const string ReasonPrefix = "authority-lost-";
    internal const string ArchivedLedgerFile = "lost-ledger-extension.json";
    private static readonly Regex TemporaryName = new("\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> KnownEntries = new(StringComparer.Ordinal)
    {
        "consent.json", "enrollment.json", "enrollment-plan.json", "handoff.json", "journal.json", "entry.json",
        "control.json", "return.json", "acknowledged.json", "candidate.json", "approved.json", ".approval.lock",
        ArchivedLedgerFile, "delegations", "offline-delegations", "inbox", "imported", "allocations", "decisions", "recovery"
    };

    // The profile-keyed run, restoration and checkpoint evidence lives beside the reward directory and is
    // tied to the old epoch's delegations; left in place it would make every later startup quarantine the
    // fresh enrollment. It is archived together with the records.
    private static readonly string[] EvidenceFolders = ["arena-reward-snapshots", "arena-reward-checkpoints", "arena-reward-runs"];
    private const string ArchivedMarker = ".archived-", StagingMarker = ".reenroll-";

    internal static string Suffix(DateTimeOffset now, string unique)
        => now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + unique;

    internal static RewardAuthorityNotice? Reconcile(RewardStartupWindow window, string id, Func<DateTimeOffset>? now = null)
    {
        window.RequireOpen();
        var root = window.Root;
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        CompleteInterrupted(root, id, directory);

        RewardStartupEnrollment enrollment;
        string profilePath = Path.Combine(root, "user", "profiles", id + ".json"), profile;
        JsonObject profileObject;
        RewardConsent consent;
        string trigger;
        IReadOnlyList<RewardPayment>? ledger = null;
        try
        {
            var enrollmentPath = Path.Combine(directory, "enrollment.json");
            if (!RewardRuntimeFiles.Exists(root, enrollmentPath)) return null;
            enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(enrollmentPath, 16384);
            // Anything but a healthy runtime enrollment of this very profile is some other fault; the
            // ordinary quarantine reports it and this recovery stays out of it.
            if (enrollment.Schema != 1 || enrollment.Binding?.ProfileId != id || !enrollment.Enabled || !enrollment.RuntimeManaged
                || string.IsNullOrWhiteSpace(enrollment.RuntimeRoot)) return null;
            enrollment.Binding.Validate();
            if (!RewardRuntimeFiles.Exists(root, Path.Combine(directory, "consent.json"))) return null;
            RewardStartupFiles.RequireLocalPath(root, profilePath);
            if (!File.Exists(profilePath)) return null;
            profile = File.ReadAllText(profilePath);
            using (var document = JsonDocument.Parse(profile)) RewardStartupFiles.RejectDuplicateProperties(document.RootElement);
            profileObject = JsonNode.Parse(profile) as JsonObject ?? throw new RewardDataException("invalid-profile");
            // "info.id still matches": the profile is the very one this authority was created for.
            if (profileObject["info"] is not JsonObject info || info["id"]?.GetValue<string>() != id) return null;
            bool lostBlock = !profileObject.ContainsKey(RewardProfileDocument.ExtensionKey);
            bool movedRoot = !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
                || !string.Equals(RewardStartupFiles.Canonical(enrollment.RuntimeRoot), root, RewardStartupFiles.PathComparison);
            if (!lostBlock && !movedRoot) return null;
            trigger = lostBlock ? "profile-reward-block-missing" : "install-root-changed";
            consent = RewardEnrollment.ReadConsent(root, id);
            if (!lostBlock)
            {
                try { ledger = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new()); }
                catch (RewardDataException e)
                {
                    // Another epoch, or damage: not this recovery's business.
                    return Unresolved(trigger, "the profile's reward ledger is not this enrollment's (" + e.Message + ")");
                }
            }
        }
        catch (Exception e) when (e is RewardDataException or JsonException or FormatException or ArgumentException
            or InvalidOperationException)
        { return null; }

        if (Unsettled(root, id, directory, enrollment.Binding, ledger) is { } unsettled) return Unresolved(trigger, unsettled);

        // --- the old authority is fully settled: archive it and let the ordinary enrollment start fresh ---
        window.RequireOpen();
        string suffix = Suffix((now ?? (() => DateTimeOffset.UtcNow))(), Guid.NewGuid().ToString("N")[..8]);
        string parent = Path.GetDirectoryName(directory)!;
        string staging = Path.Combine(parent, id + StagingMarker + suffix), archive = Path.Combine(parent, id + ArchivedMarker + suffix);
        RewardRuntimeFiles.EnsureDirectory(root, staging);
        // The choice travels first: a crash after the archive rename must never let the default (ON) apply
        // to a player who had switched rewards off. CompleteInterrupted finishes the remaining steps.
        RewardRuntimeFiles.Write(root, Path.Combine(staging, "consent.json"), consent);
        if (ledger is not null)
        {
            // The profile still carries the old ledger block. Keep a copy beside the old records, then take it
            // out so that the fresh enrollment can add its own; a crash in between simply meets "block missing".
            string extension = profileObject[RewardProfileDocument.ExtensionKey]!.ToJsonString();
            RewardStartupFiles.WriteAtomic(root, Path.Combine(directory, ArchivedLedgerFile), extension);
            profileObject.Remove(RewardProfileDocument.ExtensionKey);
            string stripped = profileObject.ToJsonString();
            window.RequireOpen();
            if (File.ReadAllText(profilePath) != profile) throw new RewardDataException("authority-lost-profile-changed");
            RewardStartupFiles.WriteAtomic(root, profilePath, stripped);
            if (File.ReadAllText(profilePath) != stripped) throw new IOException("Reward ledger removal was not durable.");
        }
        RewardStartupFiles.RequireLocalPath(root, directory);
        RewardStartupFiles.RequireLocalPath(root, archive, allowMissingLeaf: true);
        window.RequireOpen();
        Directory.Move(directory, archive);
        Finish(root, id, directory, staging, archive);
        return new(true, trigger, "", Path.GetRelativePath(root, archive).Replace(Path.DirectorySeparatorChar, '/'));
    }

    // The evidence folders follow the records into the archive, then the carried choice becomes the live directory.
    private static void Finish(string root, string id, string directory, string staging, string archive)
    {
        foreach (var name in EvidenceFolders)
        {
            string source = Path.Combine(root, "user", name, id), target = Path.Combine(archive, "evidence", name);
            if (!RewardRuntimeFiles.Exists(root, source)) continue;
            RewardRuntimeFiles.EnsureDirectory(root, Path.GetDirectoryName(target)!);
            RewardStartupFiles.RequireLocalPath(root, source);
            RewardStartupFiles.RequireLocalPath(root, target, allowMissingLeaf: true);
            Directory.Move(source, target);
        }
        RewardStartupFiles.RequireLocalPath(root, staging);
        Directory.Move(staging, directory);
    }

    private static RewardAuthorityNotice Unresolved(string trigger, string detail) => new(false, trigger, detail, null);

    // A crash after the archive rename: the live directory is missing and only the staged choice remains.
    // Without finishing, the missing directory would read as a new profile and default to ON.
    private static void CompleteInterrupted(string root, string id, string directory)
    {
        if (RewardRuntimeFiles.Exists(root, directory)) return;
        string parent = Path.GetDirectoryName(directory)!;
        if (!RewardRuntimeFiles.Exists(root, parent)) return;
        foreach (var staging in Directory.EnumerateDirectories(parent, id + StagingMarker + "*").Order(StringComparer.Ordinal).Take(8))
        {
            RewardStartupFiles.RequireLocalPath(root, staging);
            string archive = Path.Combine(parent, id + ArchivedMarker + Path.GetFileName(staging)[(id.Length + StagingMarker.Length)..]);
            if (!RewardRuntimeFiles.Exists(root, archive)) continue; // a stale staging of an attempt that never got that far
            Finish(root, id, directory, staging, archive);
            return;
        }
    }

    /// <summary>Why the old directory may not be archived, or null when nothing in it is unsettled.</summary>
    internal static string? Unsettled(string root, string id, string directory, RewardBinding binding,
        IReadOnlyList<RewardPayment>? ledger)
    {
        try { return UnsettledCore(root, id, directory, binding, ledger); }
        catch (Exception e) when (e is RewardDataException or JsonException or FormatException or ArgumentException
            or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return "a reward record could not be read (" + (e is RewardDataException ? e.Message : e.GetType().Name) + ")"; }
    }

    private static string? UnsettledCore(string root, string id, string directory, RewardBinding binding,
        IReadOnlyList<RewardPayment>? ledger)
    {
        if (RewardOfflineSession.HasActive(root)) return "an Arena session is still recorded as active";
        RewardStartupFiles.RequireLocalPath(root, directory);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Take(257))
        {
            string name = Path.GetFileName(path);
            if (TemporaryName.IsMatch(name)) continue;
            if (!KnownEntries.Contains(name)) return "unrecognised entry '" + name + "'";
        }

        var journalPath = Path.Combine(directory, "journal.json");
        if (!RewardRuntimeFiles.Exists(root, journalPath)) return "the reward journal is missing";
        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(journalPath), binding, new());
        foreach (var entry in journal.Entries)
        {
            if (entry.Phase != RewardJournalPhase.Committed) return "payment " + entry.Payment.ReceiptKey + " is not committed";
            if (entry.Payment.Cola is not null) return "payment " + entry.Payment.ReceiptKey + " carries Tarkova Cola progression";
        }
        // A moved install still holds the ledger: it must be exactly the journal. A ledger BEHIND the journal
        // is a restored older backup; the settlement core keeps refusing that one.
        if (ledger is not null && (ledger.Count != journal.Entries.Count
            || ledger.Any(p => !journal.Entries.Any(e => e.Payment == p))))
            return "the profile's ledger and the journal disagree";

        bool Imported(string name) => RewardRuntimeFiles.Exists(root, Path.Combine(directory, "imported", name));
        bool Journaled(RewardClaim claim) => journal.Entries.Any(e => e.Payment.ReceiptKey == claim.Key && e.Payment.Fingerprint == claim.Fingerprint);

        foreach (var folder in new[] { "inbox", "allocations" })
        {
            var path = Path.Combine(directory, folder);
            if (!RewardRuntimeFiles.Exists(root, path)) continue;
            foreach (var file in RewardRecoveryEvidence.Files(root, path, 4096))
                if (!Imported(Path.GetFileName(file))) return "a received reward (" + folder + "/" + Path.GetFileName(file) + ") is not imported yet";
        }
        var approvedPath = Path.Combine(directory, "approved.json");
        if (RewardRuntimeFiles.Exists(root, approvedPath))
        {
            var approval = RewardStartupFiles.Read<RewardStartupApproval>(approvedPath, RewardStartupFiles.MaxApprovalBytes);
            if (approval.Claim is null || !Journaled(approval.Claim)) return "an approved reward is not paid yet";
        }
        var candidatePath = Path.Combine(directory, "candidate.json");
        if (RewardRuntimeFiles.Exists(root, candidatePath))
        {
            var candidate = RewardStartupFiles.Read<RewardApprovalCandidate>(candidatePath);
            if (candidate.Authorization is null || !Imported(candidate.Authorization.RunId + ".json")) return "a reward candidate is not imported yet";
        }
        var decisions = Path.Combine(directory, "decisions");
        if (RewardRuntimeFiles.Exists(root, decisions))
            foreach (var decision in RewardApprovalProducer.ReadDecisions(root, binding, decisions))
                if (decision.Approval is { } approval && !Journaled(approval.Claim)) return "a reward decision is not paid yet";
        if (RewardRuntimeFiles.Exists(root, Path.Combine(directory, "recovery"))
            && RewardRecovery.ReadAttempts(root, id).Any(a => a.Completion is null)) return "a reward recovery request is pending";

        // Uncertain Arena runs and restorations are kept in profile-keyed folders OUTSIDE the directory.
        var snapshots = Path.Combine(root, "user", "arena-reward-snapshots", id);
        foreach (var file in RewardRecoveryEvidence.Files(root, snapshots, 4096))
            if (RewardStartupFiles.Read<RewardSnapshotGuard>(file, 1024).State == "pending") return "an Arena restoration is unresolved";
        var runs = Path.Combine(root, "user", "arena-reward-runs", id);
        foreach (var file in RewardRecoveryEvidence.Files(root, runs, 4096))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("state", out _)) return "an Arena run is unresolved";
        }
        return null;
    }
}
