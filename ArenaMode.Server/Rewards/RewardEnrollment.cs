using System.Security.Cryptography;

namespace ArenaMode.Server.Rewards;

internal static class RewardEnrollment
{
    internal static bool WantsRewards(string root, string id)
    {
        var path = Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "consent.json");
        if (!RewardRuntimeFiles.Exists(root, path)) return false;
        var consent = RewardStartupFiles.Read<RewardConsent>(path, 1024);
        if (consent.Schema != 1) throw new RewardDataException("unknown-reward-consent");
        return consent.Enabled;
    }

    internal static void SetConsent(string root, string id, bool enabled)
    {
        if (!RewardStartupFiles.MarkerAbsent(root)) throw new RewardDataException("private-session-cannot-enroll");
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        var profilePath = Path.Combine(root, "user", "profiles", id + ".json");
        RewardStartupFiles.RequireLocalPath(root, profilePath);
        if (!File.Exists(profilePath)) throw new RewardDataException("reward-profile-missing");
        RewardRuntimeFiles.EnsureDirectory(root, directory);
        // Disable the delegatable authority before acknowledging opt-out. A
        // partial write can only keep rewards disabled; it cannot enable them.
        var handoffPath = Path.Combine(directory, "handoff.json");
        if (!enabled && RewardRuntimeFiles.Exists(root, handoffPath))
        {
            var handoff = RewardRuntimeFiles.ReadHandoff(root, handoffPath);
            RewardRuntimeFiles.Write(root, handoffPath, handoff with { Enabled = false });
        }
        RewardRuntimeFiles.Write(root, Path.Combine(directory, "consent.json"), new RewardConsent(1, enabled));
        // First enrollment deliberately waits for the native pre-load window.
        // Re-enabling an existing intact enrollment changes metadata only.
        if (enabled && RewardRuntimeFiles.Exists(root, Path.Combine(directory, "enrollment.json")))
        {
            var enrollment = Read(root, id);
            var profile = File.ReadAllText(profilePath);
            _ = RewardProfileDocument.ReadLedger(profile, enrollment.Binding, new());
            PublishHandoff(root, directory, enrollment);
        }
    }

    internal static RewardStartupEnrollment Read(string root, string id)
    {
        var path = Path.Combine(RewardRuntimeFiles.DirectoryFor(root, id), "enrollment.json");
        RewardStartupFiles.RequireLocalPath(root, path);
        var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(path, 16384);
        if (enrollment.Schema != 1 || enrollment.Binding?.ProfileId != id || !enrollment.Enabled || !enrollment.RuntimeManaged
            || !Path.IsPathFullyQualified(enrollment.RuntimeRoot)
            || !string.Equals(RewardStartupFiles.Canonical(enrollment.RuntimeRoot), root, RewardStartupFiles.PathComparison))
            throw new RewardDataException("runtime-enrollment-origin-mismatch");
        enrollment.Binding.Validate();
        return enrollment;
    }

    internal static void EnsureAtStartup(RewardStartupWindow window, string id)
    {
        window.RequireOpen();
        var root = window.Root;
        var directory = RewardRuntimeFiles.DirectoryFor(root, id);
        if (!WantsRewards(root, id)) return;
        var enrollmentPath = Path.Combine(directory, "enrollment.json");
        var profilePath = Path.Combine(root, "user", "profiles", id + ".json");
        RewardStartupFiles.RequireLocalPath(root, profilePath);
        var profile = File.ReadAllText(profilePath);
        if (RewardRuntimeFiles.Exists(root, enrollmentPath))
        {
            var existing = Read(root, id);
            RewardStartupFiles.RequireCleanProfile(profile, existing.Binding);
            _ = RewardProfileDocument.ReadLedger(profile, existing.Binding, new());
            var journalPath = Path.Combine(directory, "journal.json");
            RewardStartupFiles.RequireLocalPath(root, journalPath);
            _ = RewardSettlementCore.ParseJournal(File.ReadAllText(journalPath), existing.Binding, new());
            PublishHandoff(root, directory, existing);
            return;
        }

        var planPath = Path.Combine(directory, "enrollment-plan.json");
        RewardEnrollmentPlan plan;
        if (RewardRuntimeFiles.Exists(root, planPath))
        {
            plan = RewardStartupFiles.Read<RewardEnrollmentPlan>(planPath, 16384);
            if (plan.Schema != 1 || plan.Binding?.ProfileId != id || !RewardIdentity.IsHex(plan.BeforeRevision, 64)
                || !RewardIdentity.IsHex(plan.AfterRevision, 64)) throw new RewardDataException("invalid-enrollment-plan");
            plan.Binding.Validate();
        }
        else
        {
            // A missing enrollment never resets existing history. Any of these
            // files without an original plan means that authority was lost.
            if (RewardRuntimeFiles.Exists(root, Path.Combine(directory, "journal.json"))
                || RewardRuntimeFiles.Exists(root, Path.Combine(directory, "handoff.json"))
                || RewardRuntimeFiles.Exists(root, Path.Combine(directory, "decisions")))
                throw new RewardDataException("enrollment-history-without-plan");
            var binding = new RewardBinding(Guid.NewGuid().ToString("N"), id, Guid.NewGuid().ToString("N"));
            RewardStartupFiles.RequireCleanProfile(profile, binding);
            var enrolled = RewardProfileDocument.Enroll(profile, binding);
            plan = new(1, binding, RewardIdentity.Hash(profile), RewardIdentity.Hash(enrolled));
            RewardRuntimeFiles.WriteNew(root, planPath, plan);
        }

        RewardStartupFiles.RequireCleanProfile(profile, plan.Binding);
        var before = RewardIdentity.Hash(profile);
        if (before != plan.BeforeRevision && before != plan.AfterRevision)
            throw new RewardDataException("enrollment-profile-changed");
        var after = before == plan.BeforeRevision ? RewardProfileDocument.Enroll(profile, plan.Binding) : profile;
        if (RewardIdentity.Hash(after) != plan.AfterRevision) throw new RewardDataException("enrollment-plan-mismatch");
        _ = RewardProfileDocument.ReadLedger(after, plan.Binding, new());
        var empty = new RewardJournal(1, plan.Binding, []);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(directory, "journal.json"), empty);
        window.RequireOpen();
        if (File.ReadAllText(profilePath) != profile) throw new RewardDataException("enrollment-profile-changed");
        if (before != plan.AfterRevision) RewardStartupFiles.WriteAtomic(root, profilePath, after);
        if (File.ReadAllText(profilePath) != after) throw new IOException("Reward enrollment was not durable.");
        var enrollment = new RewardStartupEnrollment(1, true, root, plan.Binding, RuntimeManaged: true);
        RewardRuntimeFiles.WriteNew(root, enrollmentPath, enrollment);
        PublishHandoff(root, directory, enrollment);
    }

    private static void PublishHandoff(string root, string directory, RewardStartupEnrollment enrollment)
    {
        var path = Path.Combine(directory, "handoff.json");
        RewardHandoff handoff;
        if (RewardRuntimeFiles.Exists(root, path))
        {
            handoff = RewardRuntimeFiles.ReadHandoff(root, path);
            if (handoff.Binding != enrollment.Binding || !RewardRuntimeFiles.SameRoot(handoff.RuntimeRoot, root))
                throw new RewardDataException("handoff-enrollment-conflict");
            handoff = handoff with { Enabled = true };
        }
        else
        {
            var delegates = Path.Combine(directory, "delegations");
            var originals = RewardRuntimeFiles.Exists(root, delegates)
                ? Directory.EnumerateFileSystemEntries(delegates).Where(p => !System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(p), "\\A\\.arena-reward-[a-f0-9]{32}\\.tmp\\z")).Take(2).ToArray() : [];
            if (originals.Length > 1) throw new RewardDataException("handoff-authority-ambiguous");
            if (originals.Length == 1)
            {
                // Interrupted publication can recover the exact one immutable
                // authority. Never mint a fresh key from a lost handoff file.
                handoff = RewardRuntimeFiles.ReadHandoff(root, originals[0]);
                if (handoff.Binding != enrollment.Binding || !RewardRuntimeFiles.SameRoot(handoff.RuntimeRoot, root) || !handoff.Enabled
                    || Path.GetFileName(originals[0]) != handoff.DelegationId + ".json")
                    throw new RewardDataException("handoff-authority-conflict");
            }
            else handoff = new(1, true, root, enrollment.Binding, Guid.NewGuid().ToString("N"),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }
        var delegationDirectory = Path.Combine(directory, "delegations");
        RewardRuntimeFiles.EnsureDirectory(root, delegationDirectory);
        RewardRuntimeFiles.WriteNew(root, Path.Combine(delegationDirectory, handoff.DelegationId + ".json"), handoff);
        RewardRuntimeFiles.Write(root, path, handoff);
    }
}
