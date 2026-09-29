using ArenaMode.Combat;

namespace ArenaMode.Hud;

/// <summary>Only captured native metadata, never names, current equipment or retained Player objects.</summary>
internal static class KillfeedIcon
{
    internal static ArenaUiIcon Select(ArenaKill kill)
    {
        switch (kill.DamageKind)
        {
            case "GrenadeFragment": return ArenaUiIcon.Grenade;
            case "Melee": return ArenaUiIcon.Melee;
            case "LightBleeding": case "HeavyBleeding": return ArenaUiIcon.Bleeding;
            case "Flame": return ArenaUiIcon.Fire;
            // LastDamageInfo may still refer to the earlier attacker and weapon.
            // Only the final bullet death can use that captured weapon class.
            case "Bullet":
                switch (kill.WeaponClass)
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
