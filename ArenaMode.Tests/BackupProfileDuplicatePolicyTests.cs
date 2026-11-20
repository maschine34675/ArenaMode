using System.Collections.Generic;
using ArenaMode.Bots;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// The pure half of ArenaBackupProfileDuplicatePatch: a backup load hands the pool the very objects
/// the loader already stored, and only those must be left out.
/// </summary>
public sealed class BackupProfileDuplicatePolicyTests
{
    /// <summary>Value equality on purpose: two instances with one id are equal and still two profiles.</summary>
    private sealed record Entry(string Id);

    [Fact]
    public void AReloadThePoolAlreadyHoldsAddsNothing()
    {
        var a = new Entry("a");
        var b = new Entry("b");
        var pool = new List<Entry> { new("old"), a, b };

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(new List<Entry> { a, b }, pool, out int duplicates);

        Assert.Empty(fresh);
        Assert.Equal(2, duplicates);
    }

    [Fact]
    public void AReloadThePoolDoesNotHoldIsAddedWhole()
    {
        var a = new Entry("a");
        var b = new Entry("b");

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(
            new List<Entry> { a, b }, new List<Entry> { new("old") }, out int duplicates);

        Assert.Equal(new[] { a, b }, fresh);
        Assert.Same(a, fresh[0]);
        Assert.Same(b, fresh[1]);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void OnlyTheHeldEntriesAreLeftOutAndTheOrderStays()
    {
        var a = new Entry("a");
        var b = new Entry("b");
        var c = new Entry("c");
        var d = new Entry("d");

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(
            new List<Entry> { a, b, c, d }, new List<Entry> { c, a }, out int duplicates);

        Assert.Equal(2, fresh.Count);
        Assert.Same(b, fresh[0]);
        Assert.Same(d, fresh[1]);
        Assert.Equal(2, duplicates);
    }

    [Fact]
    public void AnEqualButDistinctProfileIsNotADuplicate()
    {
        // A freshly generated profile must never be dropped because it compares equal to a pooled one.
        var pooled = new Entry("same");
        var generated = new Entry("same");
        Assert.Equal(pooled, generated);

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(
            new List<Entry> { generated }, new List<Entry> { pooled }, out int duplicates);

        Assert.Single(fresh);
        Assert.Same(generated, fresh[0]);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void AReferenceTwiceInOneReloadIsKeptOnce()
    {
        var a = new Entry("a");

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(
            new List<Entry> { a, a }, new List<Entry>(), out int duplicates);

        Assert.Single(fresh);
        Assert.Same(a, fresh[0]);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void NullEntriesPassThroughUncounted()
    {
        var a = new Entry("a");

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(
            new List<Entry> { null, a, null }, new List<Entry> { null, a }, out int duplicates);

        Assert.Equal(new Entry[] { null, null }, fresh);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public void MissingListsAreEmptyLists()
    {
        var a = new Entry("a");

        Assert.Empty(BackupProfileDuplicatePolicy.NotYetPooled<Entry>(null, new List<Entry> { a }, out int none));
        Assert.Equal(0, none);

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(new List<Entry> { a }, null, out int duplicates);
        Assert.Single(fresh);
        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void NeitherListIsChanged()
    {
        var a = new Entry("a");
        var b = new Entry("b");
        var reloaded = new List<Entry> { a, b };
        var pool = new List<Entry> { a };

        var fresh = BackupProfileDuplicatePolicy.NotYetPooled(reloaded, pool, out _);

        Assert.NotSame(reloaded, fresh);
        Assert.Equal(new[] { a, b }, reloaded);
        Assert.Equal(new[] { a }, pool);
    }
}
