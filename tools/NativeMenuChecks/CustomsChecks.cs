using System;
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void CheckCustomsNegotiation()
    {
        const string map = ArenaMapCatalog.CustomsId;
        ConfigureAreaNegotiation();
        Settings.Allowlist = map;
        Settings.GameMode.Value = "endless";
        Assert(ArenaMenuSelection.HasConfiguredMap(false) && ArenaMenuSelection.HasConfiguredMap(true),
            "Customs-only server permits solo and PFT entry into the picker");
        Assert(ArenaMenuSelection.IsMapAvailable(map), "Deathmatch uses whole Customs regardless of the saved TDM contour");
        Settings.GameMode.Value = "teamdeathmatch";
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
        Select(map);
        Assert(ArenaMenuSelection.IsMapAvailable(map), "Exact Customs preset and TDM enable the configured map");

        bool visible = true;
        bool selected = true;
        int changes = 0, clears = 0;
        var row = new ArenaConditionalMapView(true, value => { visible = value; changes++; },
            () => { selected = false; clears++; });
        void Refresh() => row.Refresh(ArenaMenuSelection.IsMapDiscoverable(map), selected);
        Refresh();
        Assert(visible && changes == 0 && clears == 0, "Valid Customs selection remains untouched");
        Settings.GameMode.Value = "gungame"; // native sidebar and F12 share this saved entry
        Refresh();
        Assert(visible && selected && clears == 0 && changes == 0,
            "Changing mode preserves Customs because Gun Game uses its whole map");
        Refresh();
        Assert(changes == 0 && clears == 0, "Unchanged polling cannot toggle or clear the native row repeatedly");
        Settings.GameMode.Value = "teamdeathmatch";
        Refresh();
        Assert(visible && selected, "Returning to TDM preserves its existing valid contour");
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterId;
        selected = true;
        Refresh();
        Assert(visible && selected && !ArenaMenuSelection.IsMapAvailable(map),
            "Customs stays discoverable after F12 area downgrade, but final start still requires preparation");
        row.Close();
        Assert(visible, "Closing restores the native row's original visibility");
        changes = clears = 0;
        row.Refresh(false, true);
        row.Close();
        Assert(changes == 0 && clears == 0, "A disposed view cannot change another screen or selection");

        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch", "zombie" })
        foreach (string area in new[] { null, "", ArenaAreaCatalog.FullMapId, ArenaAreaCatalog.FactorySouthId, ArenaAreaCatalog.CustomsFactoryCenterId,
                     "customs-factory-center-v2", ArenaAreaCatalog.CustomsFactoryCenterTeamsId })
        {
            ConfigureAreaNegotiation();
            Select(map);
            Settings.GameMode.Value = mode;
            Settings.TeamDeathmatchArea.Value = area;
            int starts = RequestHandler.StartCount;
            bool bounded = mode == "teamdeathmatch" && area == ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
            bool expected = mode != "teamdeathmatch" || bounded || ArenaAreaCatalog.NormalizeId(area) == ArenaAreaCatalog.FullMapId;
            Assert(ArenaMenuSelection.IsMapAvailable(map) == expected, "Customs menu admission follows exact mode/area contract");
            bool accepted = Start(Request(map: map), out var task);
            Assert(accepted == expected, "Actual final local-start prefix matches Customs admission");
            if (!expected)
                Assert(task.IsFaulted && starts == RequestHandler.StartCount && ArenaAreaRuntime.ArmCalls == 0,
                    "Invalid Customs start never reaches profile snapshot announcement or runtime arming");
            else
                Assert(ArenaAreaRuntime.ArmedArea == (bounded ? ArenaAreaCatalog.CustomsFactoryCenterTeams : null)
                    && JObject.Parse(RequestHandler.LastPostBody)["areaId"].Value<string>() == (bounded ? area : ArenaAreaCatalog.FullMapId),
                    "Accepted Customs request freezes the same exact server/client area");
        }

        ConfigureAreaNegotiation();
        Select(map);
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(true, true, ""), null);
        int beforeTeam = RequestHandler.StartCount;
        Assert(!ArenaMenuSelection.IsMapAvailable(map) && !Start(Request(map: map), out var denied)
            && denied.IsFaulted && RequestHandler.StartCount == beforeTeam,
            "Live PFT eligibility still rejects independent bounded TDM before a roster is frozen");
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(true, false, ""), null);
        ArenaClientConfig.Enabled = _ => false;
        Assert(!ArenaMenuSelection.IsMapAvailable(map) && !ArenaMenuSelection.HasConfiguredMap(false),
            "Explicit server disabling cannot be bypassed by the Customs preset");
        ArenaClientConfig.Enabled = Settings.IsArenaMap;
        Settings.Allowlist = ArenaMapCatalog.PreviousDefaultClientAllowlist;
        Assert(!ArenaMenuSelection.IsMapAvailable(map), "A custom allowlist that excludes Customs remains authoritative");
        Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
        ConfigureAreaNegotiation();
    }
}
