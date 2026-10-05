using System;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Exact physical ownership; a profile ID is never a replacement-body identity.</summary>
internal readonly struct AkimboOwner
{
    private readonly object _body, _hands, _weapon, _health, _world, _match, _model;
    private readonly int _epoch, _round;
    internal AkimboOwner(object body, object hands, object weapon, object health, object world, object match,
        object model, int epoch, int round)
    {
        _body = body; _hands = hands; _weapon = weapon; _health = health; _world = world; _match = match;
        _model = model; _epoch = epoch; _round = round;
    }
    internal bool Valid => _body != null && _hands != null && _weapon != null && _health != null
        && _world != null && _match != null && _model != null;
    internal bool Matches(AkimboOwner other) => Valid && other.Valid
        && ReferenceEquals(_body, other._body) && ReferenceEquals(_hands, other._hands)
        && ReferenceEquals(_weapon, other._weapon) && ReferenceEquals(_health, other._health)
        && ReferenceEquals(_world, other._world) && ReferenceEquals(_match, other._match)
        && ReferenceEquals(_model, other._model) && _epoch == other._epoch && _round == other._round;
}

internal static class AkimboEligibility
{
    internal static bool Supports(bool handgunType, string weaponClass, bool flare, bool launcher)
        => handgunType && string.Equals(weaponClass, "pistol", StringComparison.Ordinal)
            && !flare && !launcher;
}

/// <summary>No timer: the preference lasts, this particular body's weapon binding does not.</summary>
internal sealed class AkimboLease
{
    internal readonly AkimboOwner Owner;
    private bool _emitting, _retired;
    internal AkimboLease(AkimboOwner owner) { Owner = owner; _retired = !owner.Valid; }
    internal bool Current(AkimboOwner owner, bool live) => !_retired && live && Owner.Matches(owner);
    internal bool BeginShot(bool current, bool runOriginal, bool ready, bool sourceValid, bool muzzleClear)
    {
        if (_retired || _emitting || !current || !runOriginal || !ready || !sourceValid || !muzzleClear) return false;
        _emitting = true;
        return true;
    }
    internal void EndShot() => _emitting = false;
    internal void Retire() => _retired = true;
}

/// <summary>A broken model/shot is attempted once per owner, not once per frame.</summary>
internal sealed class AkimboBindings
{
    private AkimboOwner _rejected;
    internal AkimboLease Lease { get; private set; }
    internal bool MayAttach(AkimboOwner owner) => Lease == null && owner.Valid && !_rejected.Matches(owner);
    internal AkimboLease Attach(AkimboOwner owner)
    {
        if (!MayAttach(owner)) return null;
        _rejected = default;
        return Lease = new AkimboLease(owner);
    }
    internal void Release() { Lease?.Retire(); Lease = null; }
    internal void Reject(AkimboOwner owner) { Release(); _rejected = owner; }
    internal void Clear() { Release(); _rejected = default; }
}
