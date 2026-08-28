using EFT;

namespace ArenaMode.Combat;

/// <summary>
/// WildSpawnType → score bucket + short killfeed label. Condensed from the
/// proven AutoIFF switch (same author); PMC humans/bots read the profile side.
/// Buckets are deliberately coarse — labels carry the flavor.
/// </summary>
internal static class RoleClassifier
{
    public static ArenaRole Bucket(IPlayer player)
    {
        var settings = player?.Profile?.Info?.Settings;
        if (settings == null)
            return ArenaRole.Other;

        var role = settings.Role;
        switch (role)
        {
            case WildSpawnType.pmcBEAR:
            case WildSpawnType.pmcUSEC:
                return ArenaRole.Pmc;

            case WildSpawnType.pmcBot:
            case WildSpawnType.exUsec:
            case WildSpawnType.arenaFighter:
            case WildSpawnType.arenaFighterEvent:
                return ArenaRole.Raider;

            case WildSpawnType.assault:
            case WildSpawnType.assaultGroup:
            case WildSpawnType.marksman:
            case WildSpawnType.cursedAssault:
            case WildSpawnType.crazyAssaultEvent:
                return ArenaRole.Scav;

            // The Goons register as followers but fight (and score) like bosses.
            case WildSpawnType.followerBigPipe:
            case WildSpawnType.followerBirdEye:
                return ArenaRole.Boss;
        }

        var name = role.ToString();
        if (name.StartsWith("boss"))
            return ArenaRole.Boss;
        if (name.StartsWith("follower") || name.StartsWith("sectant"))
            return ArenaRole.Follower;

        return ArenaRole.Other;
    }

    public static string Label(IPlayer player)
    {
        var settings = player?.Profile?.Info?.Settings;
        if (settings == null)
            return "?";

        switch (settings.Role)
        {
            case WildSpawnType.pmcBEAR: return "BEAR";
            case WildSpawnType.pmcUSEC: return "USEC";
            case WildSpawnType.pmcBot: return "Raider";
            case WildSpawnType.exUsec: return "Rogue";
            case WildSpawnType.assault:
            case WildSpawnType.assaultGroup: return "Scav";
            case WildSpawnType.marksman: return "Sniper Scav";
            case WildSpawnType.cursedAssault: return "Scav";
            case WildSpawnType.crazyAssaultEvent: return "Scav";
            case WildSpawnType.arenaFighter:
            case WildSpawnType.arenaFighterEvent: return "Arena Fighter";
            case WildSpawnType.sectantPriest:
            case WildSpawnType.sectactPriestEvent: return "Cultist Priest";
            case WildSpawnType.shooterBTR: return "BTR Gunner";
        }

        var bucket = Bucket(player);
        switch (bucket)
        {
            case ArenaRole.Boss: return "Boss";
            case ArenaRole.Follower:
                return settings.Role.ToString().StartsWith("sectant") ? "Cultist" : "Guard";
            default:
                return settings.Role.ToString();
        }
    }

    public static string DisplayName(IPlayer player)
    {
        var nickname = player?.Profile?.Info?.Nickname;
        return string.IsNullOrEmpty(nickname) ? "?" : nickname;
    }
}
