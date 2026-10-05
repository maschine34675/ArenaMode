using System;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Identity-bound prototype lifetime; no profile IDs or replacement-body recovery.</summary>
internal sealed class AkimboLease
{
    internal const string SupportedPistol = "5448bd6b4bdc2dfc2f8b4569"; // PM, first visual prototype.
    internal const float Duration = 20f;
    private readonly object _body, _hands, _weapon, _health, _world, _match;
    private readonly int _epoch, _round;
    private readonly float _born, _until;
    private bool _emitting, _retired;

    internal AkimboLease(object body, object hands, object weapon, object health, object world, object match,
        int epoch, int round, float now)
    {
        _body = body; _hands = hands; _weapon = weapon; _health = health; _world = world; _match = match;
        _epoch = epoch; _round = round; _born = now; _until = now + Duration;
        _retired = body == null || hands == null || weapon == null || health == null || world == null
            || match == null || !Finite(now) || now < 0f || !Finite(_until) || _until <= now;
    }

    internal bool Current(object body, object hands, object weapon, object health, object world, object match,
        int epoch, int round, float now, bool live)
        => !_retired && live && Finite(now) && now >= _born && now < _until
            && ReferenceEquals(body, _body) && ReferenceEquals(hands, _hands)
            && ReferenceEquals(weapon, _weapon) && ReferenceEquals(health, _health)
            && ReferenceEquals(world, _world) && ReferenceEquals(match, _match) && epoch == _epoch && round == _round;

    internal bool BeginShot(bool current, bool runOriginal, bool ready, bool sourceValid, bool muzzleClear)
    {
        if (_retired || _emitting || !current || !runOriginal || !ready || !sourceValid || !muzzleClear) return false;
        _emitting = true;
        return true;
    }
    internal void EndShot() => _emitting = false;
    internal void Retire() { _retired = true; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
