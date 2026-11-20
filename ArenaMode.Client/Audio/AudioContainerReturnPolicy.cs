namespace ArenaMode.Audio;

/// <summary>
/// The pure half of <c>ArenaAudioContainerReturnGuardPatch</c>: whether the game's sound container pool already holds
/// the container that is being returned to it.
///
/// Identity is the object reference and nothing else. The game's duplicate is the SAME container object put into a
/// second slot (see the patch), so a reference comparison finds exactly that and can never mistake another container
/// for it, whatever an Equals override or its id says (ids are kept by <c>Clear()</c> and are no identity).
///
/// No EFT/Unity types, so ArenaMode.Tests links this file directly.
/// </summary>
internal static class AudioContainerReturnPolicy
{
    /// <summary>
    /// True when <paramref name="slots"/> (the pool's backing array, null for an empty slot) holds
    /// <paramref name="container"/> in any slot. A null container or a null array is never pooled: the caller then
    /// leaves the return to the game. Nothing is changed.
    /// </summary>
    internal static bool IsAlreadyPooled<T>(T[] slots, T container) where T : class
    {
        if (container == null || slots == null)
            return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (ReferenceEquals(slots[i], container))
                return true;
        }

        return false;
    }
}
