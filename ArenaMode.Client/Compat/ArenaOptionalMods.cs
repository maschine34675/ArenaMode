using ArenaMode.Session;

namespace ArenaMode.Compat;

/// <summary>
/// Admission only. Never disables another plugin, changes its configuration,
/// or prevents the ordinary PMC/Scav path from running.
/// </summary>
internal static class ArenaOptionalMods
{
    internal static string GetRejectionReason()
    {
        // The whitelist comes first: Arena runs only with whitelisted mods (docs/ARENA-SESSION-20261021.md).
        // It never throws and yields to Fika's own refusal. Which foreign mods Arena refuses is that list's
        // business (the shipped default deny list and the user's file), never code specific to one mod.
        var whitelistFailure = ArenaWhitelistAdmission.GetRejectionReason();
        if (whitelistFailure != null) return whitelistFailure;

        // Cached by explicit server discovery, never perform HTTP from UI polling.
        if (ArenaServerClient.ServerAdmissionFailure != null)
            return ArenaServerClient.ServerAdmissionFailure;

        // The three adapters that remain, each for a mod the whitelist allows.
        return ArenaContinuousHealingCompat.GetRejectionReason()
            ?? ArenaContinuousLoadAmmoCompat.GetRejectionReason()
            ?? ArenaRecoilLegacyCompat.GetRejectionReason();
    }
}
