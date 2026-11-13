using EFT;

namespace ArenaMode.Modes;

// The scene survey and physical volume are verified natively. This fixture
// supplies explicit samples to the real capture and match policies so the
// shared lifecycle can be tested without pretending Unity physics is real.
internal sealed class DominationRuntime : IDisposable
{
    internal static DominationOccupancy[] Sample = new DominationOccupancy[3];
    private readonly DominationMatchPolicy _rules;
    internal DominationRuntime(TeamDeathmatchMode mode, DominationMatchPolicy rules, GameWorld world) => _rules = rules;
    // The real runtime resolves its layout by (map, active area); a whole-map fixture is enough here.
    internal static int PointCount(GameWorld world) => DominationMapLayout.ResolveDomination(world?.LocationId, null).Points.Count;
    internal string StatusLine => "points";
    internal void Prepare() { }
    internal void VerifyTeamPaths() { }
    internal bool AllowsPickup(UnityEngine.Vector3 floor) => true;
    internal void Tick(Player local, IReadOnlyList<BotOwner> bots, bool localUsec, float now)
        => _rules.Advance(now, Sample, localUsec);
    public void Dispose() { }
}
