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
        Assert(ArenaMenuSelection.HasConfiguredMap(false) && !ArenaMenuSelection.HasConfiguredMap(true),
            "Customs-only server permits solo entry into picker but does not offer PFT an unusable map");
        Assert(!ArenaMenuSelection.IsMapAvailable(map), "Customs must stay unselectable before choosing its mode and area");
        Settings.GameMode.Value = "teamdeathmatch";
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterId;
        Select(map);
        Assert(ArenaMenuSelection.IsMapAvailable(map), "Exact Customs preset and TDM enable the configured map");

        bool visible = true;
        bool selected = true;
        int changes = 0, clears = 0;
        var row = new ArenaConditionalMapView(true, value => { visible = value; changes++; },
            () => { selected = false; clears++; });
        void Refresh() => row.Refresh(ArenaMenuSelection.IsMapAvailable(map), selected);
        Refresh();
        Assert(visible && changes == 0 && clears == 0, "Valid Customs selection remains untouched");
        Settings.GameMode.Value = "gungame"; // native sidebar and F12 share this saved entry
        Refresh();
        Assert(!visible && !selected && clears == 1 && changes == 1,
            "Changing mode hides Customs and clears its stale native selection");
        Refresh();
        Assert(changes == 1 && clears == 1, "Unchanged polling cannot toggle or clear the native row repeatedly");
        Settings.GameMode.Value = "teamdeathmatch";
        Refresh();
        Assert(visible && !selected, "Returning to TDM restores its existing row without reselecting a stale map");
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        selected = true;
        Refresh();
        Assert(!visible && !selected, "F12 area downgrade also clears and hides Customs");
        row.Close();
        Assert(visible, "Closing restores the native row's original visibility");
        changes = clears = 0;
        row.Refresh(false, true);
        row.Close();
        Assert(changes == 0 && clears == 0, "A disposed view cannot change another screen or selection");

        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch" })
        foreach (string area in new[] { null, "", ArenaAreaCatalog.FullMapId, ArenaAreaCatalog.FactorySouthId,
                     "customs-factory-center-v2", ArenaAreaCatalog.CustomsFactoryCenterId })
        {
            ConfigureAreaNegotiation();
            Select(map);
            Settings.GameMode.Value = mode;
            Settings.TeamDeathmatchArea.Value = area;
            int starts = RequestHandler.StartCount;
            bool expected = mode == "teamdeathmatch" && area == ArenaAreaCatalog.CustomsFactoryCenterId;
            Assert(ArenaMenuSelection.IsMapAvailable(map) == expected, "Customs menu admission follows exact mode/area contract");
            bool accepted = Start(Request(map: map), out var task);
            Assert(accepted == expected, "Actual final local-start prefix matches Customs admission");
            if (!expected)
                Assert(task.IsFaulted && starts == RequestHandler.StartCount && ArenaAreaRuntime.ArmCalls == 0,
                    "Invalid Customs start never reaches profile snapshot announcement or runtime arming");
            else
                Assert(ArenaAreaRuntime.ArmedArea == ArenaAreaCatalog.CustomsFactoryCenter
                    && JObject.Parse(RequestHandler.LastPostBody)["areaId"].Value<string>() == area,
                    "Accepted Customs request freezes the same exact server/client area");
        }

        ConfigureAreaNegotiation();
        Select(map);
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterId;
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(true, true, ""), null);
        int beforeTeam = RequestHandler.StartCount;
        Assert(!ArenaMenuSelection.IsMapAvailable(map) && !Start(Request(map: map), out var denied)
            && denied.IsFaulted && RequestHandler.StartCount == beforeTeam,
            "Live PFT eligibility rejects Customs before a roster is frozen");
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
