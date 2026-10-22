using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx.Bootstrap;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            CheckAbsentAndAdapters();
            CheckServerAdmission();
            CheckWhitelistFirst();
            CheckNoCodeLevelModRefusals();
            Console.WriteLine($"OPTIONAL_MOD_ADMISSION_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"OPTIONAL_MOD_ADMISSION_CHECKS_FAILED after {_checks} assertions: {error}");
            return 1;
        }
    }

    private static void CheckAbsentAndAdapters()
    {
        Reset();
        Allowed("No optional mods leaves Arena available");
        Require(ArenaContinuousHealingCompat.Calls == 1 && ArenaContinuousLoadAmmoCompat.Calls == 1
            && ArenaRecoilLegacyCompat.Calls == 1,
            "All three remaining optional adapters are inspected when no earlier rejection exists");

        ArenaContinuousHealingCompat.Reason = "Continuous Healing contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == ArenaContinuousHealingCompat.Reason,
            "Continuous Healing adapter rejection reaches admission unchanged");
        Require(ArenaContinuousLoadAmmoCompat.Calls == 1 && ArenaRecoilLegacyCompat.Calls == 1,
            "Continuous Healing rejection short-circuits the later providers");
        ArenaContinuousHealingCompat.Reason = null;

        ArenaContinuousLoadAmmoCompat.Reason = "Continuous Load Ammo lifecycle unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == ArenaContinuousLoadAmmoCompat.Reason,
            "Continuous Load Ammo rejection reaches admission unchanged");
        Require(ArenaRecoilLegacyCompat.Calls == 1,
            "Continuous Load Ammo rejection short-circuits Recoil Rework Legacy");
        ArenaContinuousLoadAmmoCompat.Reason = null;

        ArenaRecoilLegacyCompat.Reason = "Recoil Rework Legacy body handoff unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == ArenaRecoilLegacyCompat.Reason,
            "Recoil Rework Legacy rejection reaches admission unchanged");
        ArenaRecoilLegacyCompat.Reason = null;

        Allowed("An adapter becoming ready is rechecked, not cached as a permanent rejection");
    }

    private static void CheckServerAdmission()
    {
        Reset();
        ArenaServerClient.ServerAdmissionFailure = "Server-only rejection";
        Require(ArenaOptionalMods.GetRejectionReason() == "Server-only rejection",
            "A failure the server reported in its ping reaches the existing menu and final-start provider");
        Require(ArenaContinuousHealingCompat.Calls == 0 && ArenaContinuousLoadAmmoCompat.Calls == 0
            && ArenaRecoilLegacyCompat.Calls == 0,
            "A server rejection short-circuits every adapter provider");

        // The server's reason outranks an adapter reason: it is the broader refusal.
        ArenaContinuousHealingCompat.Reason = "Continuous Healing contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == "Server-only rejection",
            "The server rejection is reported ahead of an adapter rejection");
        ArenaContinuousHealingCompat.Reason = null;

        ArenaServerClient.ServerAdmissionFailure = null;
        Allowed("A successful refreshed server response clears its previous restriction");
    }

    private static void CheckWhitelistFirst()
    {
        // Arena session whitelist admission (docs/ARENA-SESSION-20261021.md, section 7) is the first
        // provider: its refusal reaches the caller unchanged and no other provider runs.
        Reset();
        Allowed("A whitelist verdict of null leaves the rest of the chain in charge");
        Require(ArenaWhitelistAdmission.Calls == 1, "Admission consults the whitelist exactly once per query");

        Reset();
        ArenaWhitelistAdmission.Reason = "Arena runs only with whitelisted mods.";
        ArenaServerClient.ServerAdmissionFailure = "Server-only blocker";
        ArenaContinuousHealingCompat.Reason = "Continuous Healing contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == "Arena runs only with whitelisted mods.",
            "The whitelist refusal reaches admission unchanged and ahead of the server and adapter reasons");
        Require(ArenaContinuousHealingCompat.Calls == 0 && ArenaContinuousLoadAmmoCompat.Calls == 0
            && ArenaRecoilLegacyCompat.Calls == 0,
            "A whitelist refusal short-circuits every adapter provider");

        ArenaWhitelistAdmission.Reason = null;
        Require(ArenaOptionalMods.GetRejectionReason() == "Server-only blocker",
            "Without a whitelist refusal the server rejection still applies");
        ArenaServerClient.ServerAdmissionFailure = null;
        Require(ArenaOptionalMods.GetRejectionReason() == "Continuous Healing contract unavailable",
            "Without a whitelist or server refusal the adapters still apply");
    }

    private static void CheckNoCodeLevelModRefusals()
    {
        // User decision 2026-10-22: the whitelist (shipped default deny list and the user's file) refuses foreign
        // mods; admission carries no code specific to one of them. The mods the old code refused or adapted stay
        // listed as installed here and must change nothing.
        foreach (string guid in new[]
        {
            "com.chazut.orbit", "com.danw.questingbots", "com.hysocs.adaptivebotculling",
            "com.samc137.isbspecialforces", "com.samc137.aishi", "com.morebotsapi.tacticaltoaster",
            "com.dvize.ailimit", "me.skwizzy.lootingbots", "com.jbs4bmx.RemoveTheDead", "com.tominocz.smoothie",
            "com.danw.latetotheparty", "com.boogle.classicmovement", "com.acidphantasm.stattrack",
            "com.hazelify.deminvincibility", "com.cj.skillsextended",
        })
        {
            Reset();
            Chainloader.PluginInfos[guid] = new PluginInfo();
            Allowed($"{guid} loaded: admission leaves the decision to the whitelist");
            Require(ArenaWhitelistAdmission.Calls == 1 && ArenaContinuousHealingCompat.Calls == 1
                && ArenaContinuousLoadAmmoCompat.Calls == 1 && ArenaRecoilLegacyCompat.Calls == 1,
                $"{guid} loaded: the usual providers ran and nothing else decided");
        }
        Reset();
    }

    private static void Reset()
    {
        ArenaServerClient.ServerAdmissionFailure = null;
        ArenaWhitelistAdmission.Reason = null;
        ArenaWhitelistAdmission.Calls = 0;
        Chainloader.PluginInfos.Clear();
        ArenaContinuousHealingCompat.Reason = null;
        ArenaContinuousHealingCompat.Calls = 0;
        ArenaContinuousLoadAmmoCompat.Reason = null;
        ArenaContinuousLoadAmmoCompat.Calls = 0;
        ArenaRecoilLegacyCompat.Reason = null;
        ArenaRecoilLegacyCompat.Calls = 0;
    }

    private static void Allowed(string message) => Require(ArenaOptionalMods.GetRejectionReason() == null, message);

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
