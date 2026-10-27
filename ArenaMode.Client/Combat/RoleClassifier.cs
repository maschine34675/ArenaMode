using EFT;

namespace ArenaMode.Combat;

/// <summary>
/// WildSpawnType → score bucket + short killfeed label. Condensed from the
/// proven AutoIFF switch (same author); human PMCs use their round affiliation
/// when registered in independent team play, otherwise their profile faction,
/// while AI keeps its explicit spawn role (including raiders, rogues and bosses).
/// Buckets are deliberately coarse — labels carry the flavor.
/// </summary>
internal static class RoleClassifier
{
    public static ArenaRole Bucket(IPlayer player)
    {
        if (HumanPmcSide(player).HasValue)
            return ArenaRole.Pmc;

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

            // BSG typo ("sectact") defeats the StartsWith("sectant") fallback.
            case WildSpawnType.sectactPriestEvent:
                return ArenaRole.Follower;
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
        // Capture the exact body's frozen round side, including posthumous
        // projectiles. The native profile faction deliberately stays unchanged.
        var humanSide = Bots.TeamDeathmatchRelations.Faction(player) ?? HumanPmcSide(player);
        if (humanSide.HasValue)
            return humanSide.Value == EPlayerSide.Usec ? "USEC" : "BEAR";

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
            // The four generic Halloween-2024 infected roles Zombie mode uses,
            // plus the pure-boss fifth (infectedTagilla, not used in v0 but
            // still IsInfected — labelling it ZOMBIE too costs nothing and
            // avoids a surprise "infectedTagilla" string if it ever shows up).
            // Bucket deliberately stays Other (WildSpawnTypeExtension.IsInfected,
            // decompile-verified): these are not treated as bosses/followers.
            case var role when WildSpawnTypeExtension.IsInfected(role): return "ZOMBIE";
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

    private static EPlayerSide? HumanPmcSide(IPlayer player)
    {
        // Native ProfileSettings.Role defaults to assault even for the local
        // PMC. Only human PMC identity comes from faction: using faction for AI
        // would turn USEC-side rogues/raiders into PMCs and change their points.
        // A human Scav must still follow the existing spawn-role mapping.
        var info = player?.Profile?.Info;
        if (info == null || player.IsAI)
            return null;

        return info.Side == EPlayerSide.Usec || info.Side == EPlayerSide.Bear
            ? info.Side
            : (EPlayerSide?)null;
    }
}
