#nullable disable
using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat;

/// <summary>
/// Weak, physical-body identities and accepted contact history. Profile IDs are
/// not lives. Scopes describe the synchronous native shot -> player damage ->
/// health damage stack, including a death raised before any postfix has run.
/// No game objects are retained after their bodies disappear.
/// </summary>
internal static class ArenaHitHistory
{
    private static ConditionalWeakTable<object, Life> _lives = new();
    private static long _nextId;
    private static long _scopeEpoch;
    // Shot awards are deliberately narrower than the legacy headshot counter:
    // reentrant damage to any victim cannot borrow the surrounding projectile.
    private static int _shotDepth;
    private static int _damageDepth;

    private sealed class Life
    {
        internal long Id = ++_nextId;
        internal int Contacts;
        internal bool DeathReported;
        internal bool Initialized;
        internal Contact Shot;
        internal Contact Damage;
    }

    private sealed class Contact : IDisposable
    {
        internal Life Life;
        internal Contact Previous;
        internal object Shooter;
        internal bool Bullet;
        internal bool Head;
        internal bool IsShot;
        internal ArenaShotAwardFacts Awards;
        internal long ScopeEpoch;

        public void Dispose()
        {
            if (Life == null) return;
            if (IsShot && ReferenceEquals(Life.Shot, this)) Life.Shot = Previous;
            if (!IsShot && ReferenceEquals(Life.Damage, this)) Life.Damage = Previous;
            if (ScopeEpoch == _scopeEpoch)
            {
                if (IsShot) _shotDepth--;
                else _damageDepth--;
            }
            Life = null;
        }
    }

    internal static void Reset()
    {
        _lives = new ConditionalWeakTable<object, Life>();
        _scopeEpoch++;
        _shotDepth = _damageDepth = 0;
    }
    internal static long BodyId(object body) => body == null ? 0 : Get(body).Id;
    internal static bool NeedsInitialState(object body) => body != null && !Get(body).Initialized;
    internal static void InitializeBody(object body, bool demonstrablyUnwounded)
    {
        if (body == null) return;
        var life = Get(body);
        if (life.Initialized) return;
        life.Initialized = true;
        if (!demonstrablyUnwounded && life.Contacts == 0) life.Contacts = 1;
    }
    private static Life Get(object body) => _lives.GetValue(body, _ => new Life());

    internal static bool TryReportDeath(object body)
    {
        if (body == null) return false;
        var life = Get(body);
        if (life.DeathReported) return false;
        life.DeathReported = true;
        return true;
    }

    internal static IDisposable BeginShot(object body, object shooter, bool bullet, bool head,
        ArenaShotAwardFacts facts = default)
        => Begin(body, shooter, bullet, head, true, facts);

    internal static IDisposable BeginDamage(object body, object shooter, bool bullet, bool head,
        ArenaShotAwardFacts facts = default)
        => Begin(body, shooter, bullet, head, false, facts);

    private static IDisposable Begin(object body, object shooter, bool bullet, bool head, bool shot,
        ArenaShotAwardFacts facts)
    {
        if (body == null) return null;
        var life = Get(body);
        var contact = new Contact
        {
            Life = life, Previous = shot ? life.Shot : life.Damage,
            Shooter = shooter, Bullet = bullet, Head = head, IsShot = shot,
            Awards = facts, ScopeEpoch = _scopeEpoch
        };
        if (shot)
        {
            _shotDepth++;
            life.Shot = contact;
        }
        else
        {
            _damageDepth++;
            // ApplyDamageInfo is reached after native shot acceptance and armor
            // processing. Armor-only/ricochet contacts count even with zero HP.
            if (life.Contacts < int.MaxValue) life.Contacts++;
            life.Damage = contact;
        }
        return contact;
    }

    internal static ArenaShotAwardFacts ShotAwards(object body, object shooter, bool lethalBullet)
    {
        if (body == null || shooter == null || !lethalBullet
            || _shotDepth != 1 || _damageDepth != 1
            || !_lives.TryGetValue(body, out var life)
            || life.Shot is not { Bullet: true } shot
            || life.Damage is not { Bullet: true } contact
            || shot.Previous != null || contact.Previous != null
            || shot.ScopeEpoch != _scopeEpoch || contact.ScopeEpoch != _scopeEpoch
            || !ReferenceEquals(shot.Shooter, shooter)
            || !ReferenceEquals(contact.Shooter, shooter)
            || !ArenaShotAwardHistory.IsCurrent(shot.Awards)
            || !ArenaShotAwardHistory.IsCurrent(contact.Awards)
            || shot.Awards.ShooterBodyId != BodyId(shooter)
            || contact.Awards.ShooterBodyId != BodyId(shooter)
            || shot.Awards.ProjectileToken != contact.Awards.ProjectileToken) return default;
        return shot.Awards;
    }

    internal static void ObserveHealthDamage(object body, object shooter, bool bullet, float damage)
    {
        if (body == null || !(damage > 0f)) return;
        var life = Get(body);
        var contact = life.Damage;
        // Native spread damage within one accepted contact is not another hit.
        if (contact != null && contact.Bullet == bullet && ReferenceEquals(contact.Shooter, shooter))
            return;
        if (life.Contacts < int.MaxValue) life.Contacts++;
    }

    internal static bool IsSingleHitHeadshot(object body, object shooter, bool lethalBullet, bool head)
    {
        if (body == null || shooter == null || !lethalBullet || !head
            || !_lives.TryGetValue(body, out var life)) return false;
        return life.Contacts == 1
            && life.Shot is { Bullet: true, Head: true } shot
            && life.Damage is { Bullet: true, Head: true } contact
            && ReferenceEquals(shot.Shooter, shooter)
            && ReferenceEquals(contact.Shooter, shooter)
            && shot.Previous == null && contact.Previous == null;
    }
}
