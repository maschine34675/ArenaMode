using System.Text;
using System.Text.Json;
using ArenaMode.Server.Rewards;

namespace ArenaMode.Tests;

/// <summary>
/// TEST ONLY. Owns newly generated profiles in a unique temp directory. This is
/// not a native SaveServer adapter: the exclusive file lease only coordinates
/// writers which explicitly participate in this synthetic protocol.
/// </summary>
internal sealed class SyntheticRewardStore : IRewardSettlementStore, IDisposable
{
    private readonly bool _ownsDirectory;
    public string DirectoryPath { get; }
    public string ProfilePath => Path.Combine(DirectoryPath, "synthetic-profile.json");
    public string JournalPath => Path.Combine(DirectoryPath, "synthetic-journal.json");
    public RewardBinding Binding { get; }
    public Action<string>? Checkpoint { get; set; }
    public bool Available { get; set; } = true;
    public bool SkipProfileWrite { get; set; }
    public RewardJournalPhase? SkipJournalPhase { get; set; }
    public int ProfileWrites { get; private set; }

    public SyntheticRewardStore(RewardBinding binding, string profileJson)
    {
        Binding = binding;
        DirectoryPath = Path.Combine(Path.GetTempPath(), "ArenaMode.Rewards.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _ownsDirectory = true;
        File.WriteAllText(Path.Combine(DirectoryPath, ".synthetic-owner"), "ArenaMode reward test fixture");
        File.WriteAllText(ProfilePath, RewardProfileDocument.Enroll(profileJson, binding));
        File.WriteAllText(JournalPath, JsonSerializer.Serialize(new RewardJournal(1, binding, []), RewardSettlementCore.JournalJson));
    }

    private SyntheticRewardStore(SyntheticRewardStore original)
    {
        Binding = original.Binding;
        DirectoryPath = original.DirectoryPath;
    }

    // A separate adapter instance has no shared in-memory lock or receipt cache.
    public SyntheticRewardStore Fork() => new(this);

    public async ValueTask<IRewardSettlementLease?> TryAcquireAsync(RewardBinding binding, CancellationToken cancellationToken)
    {
        if (!Available || binding != Binding) return null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream fileLock;
            try
            {
                fileLock = new FileStream(Path.Combine(DirectoryPath, "transaction.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(2, cancellationToken);
                continue;
            }
            return new Lease(this, fileLock);
        }
    }

    public RewardJournal Journal => JsonSerializer.Deserialize<RewardJournal>(File.ReadAllText(JournalPath), RewardSettlementCore.JournalJson)!;
    public string Profile => File.ReadAllText(ProfilePath);

    public void FailOnce(string point, Exception failure)
    {
        Checkpoint = current =>
        {
            if (current != point) return;
            Checkpoint = null;
            throw failure;
        };
    }

    private void AtomicReplace(string path, string json, string prefix)
    {
        Checkpoint?.Invoke(prefix + ":before-temp");
        var temporary = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            stream.Write(Encoding.UTF8.GetBytes(json));
            stream.Flush(flushToDisk: true);
        }
        Checkpoint?.Invoke(prefix + ":after-temp");
        Checkpoint?.Invoke(prefix + ":before-replace");
        File.Move(temporary, path, overwrite: true);
        Checkpoint?.Invoke(prefix + ":after-replace");
    }

    private sealed class Lease(SyntheticRewardStore store, FileStream fileLock) : IRewardSettlementLease
    {
        public RewardProfileSnapshot ReadProfile()
        {
            store.Checkpoint?.Invoke("profile:read");
            return new(File.ReadAllText(store.ProfilePath));
        }

        public string ReadJournal()
        {
            store.Checkpoint?.Invoke("journal:read");
            return File.ReadAllText(store.JournalPath);
        }

        public void WriteJournal(string json)
        {
            var journal = JsonSerializer.Deserialize<RewardJournal>(json, RewardSettlementCore.JournalJson)!;
            var phase = journal.Entries.Last().Phase;
            if (store.SkipJournalPhase == phase) return;
            store.AtomicReplace(store.JournalPath, json, "journal:" + phase);
        }

        public bool TryWriteProfile(string expectedRevision, string json)
        {
            if (RewardIdentity.Hash(File.ReadAllText(store.ProfilePath)) != expectedRevision) return false;
            store.ProfileWrites++;
            if (!store.SkipProfileWrite) store.AtomicReplace(store.ProfilePath, json, "profile");
            return true;
        }

        public void Dispose() => fileLock.Dispose();
    }

    public void Dispose()
    {
        if (!_ownsDirectory) return;
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.Rewards.Tests"));
        var target = Path.GetFullPath(DirectoryPath);
        if (Path.GetDirectoryName(target) != expectedParent || !RewardIdentity.IsGuid(Path.GetFileName(target))
            || File.ReadAllText(Path.Combine(target, ".synthetic-owner")) != "ArenaMode reward test fixture")
            throw new InvalidOperationException("Refusing to remove a directory not owned by the synthetic fixture.");
        Directory.Delete(target, recursive: true);
    }
}

internal sealed class SyntheticRewardCrash : Exception;
