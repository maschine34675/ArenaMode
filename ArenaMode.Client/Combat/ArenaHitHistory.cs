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

        public void Dispose()
        {
            if (Life == null) return;
            if (IsShot && ReferenceEquals(Life.Shot, this)) Life.Shot = Previous;
            if (!IsShot && ReferenceEquals(Life.Damage, this)) Life.Damage = Previous;
            Life = null;
        }
    }

    internal static void Reset() => _lives = new ConditionalWeakTable<object, Life>();
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

    internal static IDisposable BeginShot(object body, object shooter, bool bullet, bool head)
        => Begin(body, shooter, bullet, head, true);

    internal static IDisposable BeginDamage(object body, object shooter, bool bullet, bool head)
        => Begin(body, shooter, bullet, head, false);

    private static IDisposable Begin(object body, object shooter, bool bullet, bool head, bool shot)
    {
        if (body == null) return null;
        var life = Get(body);
        var contact = new Contact
        {
            Life = life, Previous = shot ? life.Shot : life.Damage,
            Shooter = shooter, Bullet = bullet, Head = head, IsShot = shot
        };
        if (shot) life.Shot = contact;
        else
        {
            // ApplyDamageInfo is reached after native shot acceptance and armor
            // processing. Armor-only/ricochet contacts count even with zero HP.
            if (life.Contacts < int.MaxValue) life.Contacts++;
            life.Damage = contact;
        }
        return contact;
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
