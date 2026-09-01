using System.Threading;

namespace ArenaMode.Respawn;

/// <summary>
/// Invalidates coroutine leases whenever Arena's per-raid state is reset. A
/// Unity coroutine cannot be canceled reliably once an EFT/Redeploy operation
/// owns it, so every continuation must prove that it still belongs to the same
/// active, started raid before it mutates player or UI state.
/// </summary>
internal sealed class ArenaRaidEpoch
{
    private int _current;

    public int Capture() => Volatile.Read(ref _current);

    public void Advance() => Interlocked.Increment(ref _current);

    public bool IsEpochCurrent(int captured)
        => captured == Volatile.Read(ref _current);

    public bool IsCurrent(int captured, bool arenaActive, bool gameStarted)
        => arenaActive && gameStarted && IsEpochCurrent(captured);
}
