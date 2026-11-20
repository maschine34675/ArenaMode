using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

// A file in a profile's decision history that Arena did not write. On 2026-11-13 two GPU hosts that share one synchronized
// install both settled the same receipts; Syncthing kept one side's decisions and renamed the other side's to
// "<run>.sync-conflict-<date>-<time>-<device>.json". Every later start stopped that profile's rewards as
// invalid-result-history, logged on Debug only, while the card said "temporarily unavailable".
public sealed partial class RewardRuntimeTests
{
    private const string ConflictSuffix = ".sync-conflict-20261113-123043-LJEZTQP.json";

    [Fact]
    public async Task ASyncConflictCopyInTheHistoryStopsRewardsByNameUntilItIsMovedOut()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        string first = Directory.GetFiles(f.PathOf("decisions")).Single();
        string copy = Path.GetFileNameWithoutExtension(first) + ConflictSuffix;
        File.Copy(first, f.PathOf("decisions/" + copy));
        var queued = f.Capture();
        f.RestoreAndPublish(queued);
        string profile = f.Profile;

        var outcome = await f.Process();

        Assert.Equal(RewardSettlementStatus.Quarantined, outcome.Result.Status);
        Assert.Equal(RewardHistoryForeignEntryException.Code, outcome.Result.Reason);
        Assert.Equal(copy, outcome.Detail);
        Assert.Equal(profile, f.Profile);
        Assert.Single(f.Ledger);
        Assert.False(File.Exists(f.PathOf("allocations/" + queued.RunId + ".json")));
        Assert.True(File.Exists(f.PathOf("inbox/" + queued.RunId + ".json")));   // the match stays queued
        f.Coordinator.ObserveStartupOutcome(outcome);
        var card = f.Coordinator.GetStatus(Fixture.ProfileId);
        Assert.Equal("withheld", card.State);
        Assert.Equal(RewardUnavailableText.HistoryAttention, card.Detail);
        string warning = ArenaRewardStartup.ComposeHistoryWarning(outcome);
        Assert.Contains("'" + copy + "'", warning);
        Assert.Contains("move that file out of the SPT folder", warning);
        Assert.EndsWith("Code: " + RewardHistoryForeignEntryException.Code + ".", warning);

        File.Move(f.PathOf("decisions/" + copy), Path.Combine(f.Root, copy));
        var paid = await f.Process();

        Assert.Equal(RewardSettlementStatus.Paid, paid.Result.Status);
        Assert.Equal(2, f.Ledger.Count);
        Assert.Equal(new long[] { 1, 2 }, Directory.GetFiles(f.PathOf("decisions")).Select(p => RewardStartupFiles.Read<RewardApprovalDecision>(p).Sequence).Order());
    }

    [Theory]
    [InlineData("desktop.ini")]
    [InlineData("notes.txt")]
    [InlineData("{run}.json.bak")]
    [InlineData("{new}.JSON")]
    [InlineData("{NEW}.json")]
    [InlineData("{new}.json/")]   // a directory with a decision's name
    public async Task EveryEntryArenaDidNotWriteIsNamedNotReadOrSkipped(string shape)
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        string run = Path.GetFileNameWithoutExtension(Directory.GetFiles(f.PathOf("decisions")).Single());
        string fresh = Guid.NewGuid().ToString("N");
        string name = shape.Replace("{run}", run).Replace("{new}", fresh).Replace("{NEW}", fresh.ToUpperInvariant());
        if (name.EndsWith('/')) Directory.CreateDirectory(f.PathOf("decisions/" + (name = name.TrimEnd('/'))));
        else File.WriteAllText(f.PathOf("decisions/" + name), "{}");
        f.RestoreAndPublish(f.Capture());
        string profile = f.Profile;

        var outcome = await f.Process();

        Assert.Equal(RewardHistoryForeignEntryException.Code, outcome.Result.Reason);
        Assert.Equal(name, outcome.Detail);
        Assert.Equal(profile, f.Profile);
        Assert.Single(f.Ledger);
    }

    // The allocation is written once and never rebased, so the next sequence must come from the validated history:
    // counting "*.json" took a foreign backup.json as a decision and allocated sequence 3 after a history of one.
    [Fact]
    public async Task AForeignJsonInTheHistoryNeverShiftsTheNextAllocatedSequence()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        File.WriteAllText(f.PathOf("decisions/backup.json"), "{}");
        var queued = f.Capture();
        f.RestoreAndPublish(queued);
        var enrollment = RewardStartupFiles.Read<RewardStartupEnrollment>(f.PathOf("enrollment.json"), 16384);
        bool settled = false;

        var refused = await Assert.ThrowsAsync<RewardHistoryForeignEntryException>(() => RewardRuntimeInbox.ProcessAsync(
            f.Window, enrollment, CancellationToken.None, () => { settled = true; return f.Process(); }));

        Assert.Equal("backup.json", refused.Entry);
        Assert.False(settled);
        Assert.False(File.Exists(f.PathOf("allocations/" + queued.RunId + ".json")));
    }

    [Fact]
    public void TheHistoryWarningStaysOneLineAndOnlyHistoryCodesLeaveTemporary()
    {
        var foreign = new RewardStartupOutcome(Fixture.ProfileId,
            new(RewardSettlementStatus.Quarantined, RewardHistoryForeignEntryException.Code), 0, Detail: "a\nb" + new string('x', 200) + ".json");
        string text = ArenaRewardStartup.ComposeHistoryWarning(foreign);
        Assert.DoesNotContain('\n', text);
        Assert.Contains("'a?b" + new string('x', 117) + "...'", text);
        Assert.Contains("user/arena-rewards/" + Fixture.ProfileId + "/decisions", text);

        foreach (string code in new[] { "invalid-result-history", "result-history-gap-or-duplicate" })
        {
            string damaged = ArenaRewardStartup.ComposeHistoryWarning(foreign with { Result = new(RewardSettlementStatus.Quarantined, code), Detail = null });
            Assert.Contains("is damaged or out of sequence", damaged);
            Assert.Contains("Arena does not repair it", damaged);
            Assert.EndsWith("Code: " + code + ".", damaged);
            Assert.Equal(RewardUnavailableText.HistoryAttention, RewardUnavailableText.ForProfileReason(code));
        }
        Assert.Equal(RewardUnavailableText.Temporary, RewardUnavailableText.ForProfileReason("enrolled-state-missing"));
        Assert.Equal(RewardUnavailableText.Temporary, RewardUnavailableText.ForProfileReason("result-decision-conflict"));
    }
}
