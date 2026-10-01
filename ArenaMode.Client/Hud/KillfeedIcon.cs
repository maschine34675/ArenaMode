using ArenaMode.Combat;

namespace ArenaMode.Hud;

/// <summary>Only captured native metadata, never names, current equipment or retained Player objects.</summary>
internal static class KillfeedIcon
{
    internal static ArenaUiIcon Select(ArenaKill kill)
        => SelectKind(kill.DamageKind, kill.WeaponClass);

    internal static ArenaUiIcon SelectDeath(ArenaUncreditedDeath death)
        => SelectKind(death.DamageKind, null);

    private static ArenaUiIcon SelectKind(string damageKind, string weaponClass)
    {
        switch (damageKind)
        {
            case "GrenadeFragment": return ArenaUiIcon.Grenade;
            case "Melee": return ArenaUiIcon.Melee;
            case "Barbed": return ArenaUiIcon.BarbedWire;
            case "LightBleeding": case "HeavyBleeding": return ArenaUiIcon.Bleeding;
            case "Flame": return ArenaUiIcon.Fire;
            // LastDamageInfo may still refer to the earlier attacker and weapon.
            // Only the final bullet death can use that captured weapon class.
            case "Bullet":
                switch (weaponClass)
                {
                    case "assaultRifle": return ArenaUiIcon.AssaultRifle;
                    case "assaultCarbine": return ArenaUiIcon.Carbine;
                    case "sniperRifle": return ArenaUiIcon.Sniper;
                    case "marksmanRifle": return ArenaUiIcon.Marksman;
                    case "machinegun": return ArenaUiIcon.Machinegun;
                    case "shotgun": return ArenaUiIcon.Shotgun;
                    case "pistol": case "revolver": return ArenaUiIcon.Pistol;
                    case "smg": return ArenaUiIcon.Smg;
                }
                break;
        }
        return ArenaUiIcon.Kill;
    }
}
