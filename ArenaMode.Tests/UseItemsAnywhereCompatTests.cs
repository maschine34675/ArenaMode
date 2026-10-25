using ArenaMode.Compat;
using Xunit;

namespace ArenaMode.Tests;

public class UseItemsAnywhereCompatTests
{
    [Fact]
    public void OneNativeQueryIsRetainedAndExtended() => Assert.True(UseItemsAnywhereQueryPolicy.ShouldAppend(1, 0));

    [Fact]
    public void OneForeignQueryOwnsTheCandidateSet() => Assert.False(UseItemsAnywhereQueryPolicy.ShouldAppend(0, 1));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void DriftAndMixedOwnershipAreRejectedBeforeEditingIl(int native, int foreign)
        => Assert.Throws<InvalidOperationException>(() => UseItemsAnywhereQueryPolicy.ShouldAppend(native, foreign));
}
