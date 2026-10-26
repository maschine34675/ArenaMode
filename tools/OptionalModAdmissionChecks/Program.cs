using ArenaMode.Compat;
using Redeploy.Core.Compat;
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
            CheckAdditionalAdapterContracts();
            CheckTarkovaColaAdmission();
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
        Require(BodyReplacementCompatibility.Calls == 1,
            "Arena delegates exactly once to the shared Core admission provider");
        foreach (var reason in new[] { "Continuous Healing contract unavailable", "Continuous Load Ammo lifecycle unavailable", "Recoil Rework Legacy body handoff unavailable" })
        {
            BodyReplacementCompatibility.Reason = reason;
            Require(ArenaOptionalMods.GetRejectionReason() == reason,
                "A shared Core rejection reaches Arena admission unchanged: " + reason);
        }
        BodyReplacementCompatibility.Reason = null;
        Allowed("An adapter becoming ready is rechecked, not cached as a permanent rejection");
    }
    private static void CheckServerAdmission()
    {
        Reset();
        ArenaServerClient.ServerAdmissionFailure = "Server-only rejection";
        Require(ArenaOptionalMods.GetRejectionReason() == "Server-only rejection",
            "A failure the server reported in its ping reaches the existing menu and final-start provider");
        Require(BodyReplacementCompatibility.Calls == 0,
            "A server rejection short-circuits every adapter provider");
        Require(ArenaTarkovaColaCompat.Calls == 0,
            "A server rejection does not query Tarkova Cola");

        // The server's reason outranks an adapter reason: it is the broader refusal.
        BodyReplacementCompatibility.Reason = "Continuous Healing contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == "Server-only rejection",
            "The server rejection is reported ahead of an adapter rejection");
        BodyReplacementCompatibility.Reason = null;

        ArenaServerClient.ServerAdmissionFailure = null;
        Allowed("A successful refreshed server response clears its previous restriction");
    }

    private static void CheckAdditionalAdapterContracts()
    {
        Reset();
        FastGrenadeThrowCompat.Reason = "Grenade body handoff unavailable";
        PipPreloadCompat.Reason = "PIP preload ownership unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == FastGrenadeThrowCompat.Reason,
            "FastGrenadeThrow rejection retains its position ahead of PIP");
        Require(PipPreloadCompat.Calls == 0 && ArenaTarkovaColaCompat.Calls == 0,
            "failed grenade contract short-circuits later providers");
        FastGrenadeThrowCompat.Reason = null;
        Require(ArenaOptionalMods.GetRejectionReason() == PipPreloadCompat.Reason,
            "PIP rejection reaches admission unchanged after grenade recovery");
        Require(ArenaTarkovaColaCompat.Calls == 0,
            "failed PIP contract short-circuits Tarkova Cola");
        PipPreloadCompat.Reason = null;
        Allowed("recovered grenade and PIP adapters are rechecked without stale refusal");
        Reset();
        BodyReplacementCompatibility.Reason = "Core unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == BodyReplacementCompatibility.Reason
            && FastGrenadeThrowCompat.Calls == 0 && PipPreloadCompat.Calls == 0,
            "Core refusal remains ahead of both newer adapters");
    }

    private static void CheckTarkovaColaAdmission()
    {
        Reset();
        Allowed("Absent or ready Tarkova Cola allows admission");
        Require(ArenaTarkovaColaCompat.Calls == 1, "Ready Core consults Tarkova Cola once");
        ArenaTarkovaColaCompat.Reason = "Tarkova Cola persistence protection unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == ArenaTarkovaColaCompat.Reason,
            "Tarkova Cola protection failure reaches admission unchanged");
        ArenaTarkovaColaCompat.Calls = 0;
        BodyReplacementCompatibility.Reason = "Shared body contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == BodyReplacementCompatibility.Reason,
            "Core refusal has priority over the Tarkova Cola refusal");
        Require(ArenaTarkovaColaCompat.Calls == 0, "Core rejection short-circuits Tarkova Cola");
        BodyReplacementCompatibility.Reason = null;
        ArenaTarkovaColaCompat.Reason = null;
        Allowed("A recovered Tarkova Cola contract is rechecked without a stale refusal");
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
        BodyReplacementCompatibility.Reason = "Continuous Healing contract unavailable";
        Require(ArenaOptionalMods.GetRejectionReason() == "Arena runs only with whitelisted mods.",
            "The whitelist refusal reaches admission unchanged and ahead of the server and adapter reasons");
        Require(BodyReplacementCompatibility.Calls == 0,
            "A whitelist refusal short-circuits every adapter provider");
        Require(ArenaTarkovaColaCompat.Calls == 0,
            "A whitelist refusal does not query Tarkova Cola");

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
        // mods; the separately authorized Tarkova Cola persistence integration is tested above. The mods the old code refused or adapted stay
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
            Require(ArenaWhitelistAdmission.Calls == 1 && BodyReplacementCompatibility.Calls == 1,
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
        BodyReplacementCompatibility.Reason = null;
        BodyReplacementCompatibility.Calls = 0;
        ArenaTarkovaColaCompat.Reason = null;
        ArenaTarkovaColaCompat.Calls = 0;
        FastGrenadeThrowCompat.Reason = PipPreloadCompat.Reason = null;
        FastGrenadeThrowCompat.Calls = PipPreloadCompat.Calls = 0;
    }

    private static void Allowed(string message) => Require(ArenaOptionalMods.GetRejectionReason() == null, message);

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
