using ArenaMode.Audio;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// The pure half of ArenaAudioContainerReturnGuardPatch: a return must be skipped exactly when the very container
/// object already sits in a slot of the pool's array.
/// </summary>
public sealed class AudioContainerReturnPolicyTests
{
    /// <summary>Value equality on purpose: two containers with one id are equal and still two containers.</summary>
    private sealed record Container(int Id);

    [Fact]
    public void AContainerInAnySlotIsPooled()
    {
        var a = new Container(1);
        var b = new Container(2);
        var last = new Container(3);
        var slots = new[] { a, null, b, null, last };

        Assert.True(AudioContainerReturnPolicy.IsAlreadyPooled(slots, a));
        Assert.True(AudioContainerReturnPolicy.IsAlreadyPooled(slots, b));
        Assert.True(AudioContainerReturnPolicy.IsAlreadyPooled(slots, last));
    }

    [Fact]
    public void AContainerTheArrayDoesNotHoldIsNotPooled()
    {
        var slots = new[] { new Container(1), null, new Container(2) };

        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(slots, new Container(3)));
    }

    [Fact]
    public void IdentityIsTheReferenceNotEquality()
    {
        var held = new Container(7);
        var other = new Container(7);
        Assert.Equal(held, other);

        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(new[] { held }, other));
    }

    [Fact]
    public void NullsAreNeverPooled()
    {
        var a = new Container(1);

        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(new Container[] { null, null }, null));
        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(null, a));
        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(new Container[0], a));
        Assert.False(AudioContainerReturnPolicy.IsAlreadyPooled(new Container[] { null, null }, a));
    }

    [Fact]
    public void TheArrayIsNotChanged()
    {
        var a = new Container(1);
        var b = new Container(2);
        var slots = new[] { a, null, b };

        AudioContainerReturnPolicy.IsAlreadyPooled(slots, b);

        Assert.Same(a, slots[0]);
        Assert.Null(slots[1]);
        Assert.Same(b, slots[2]);
    }
}
