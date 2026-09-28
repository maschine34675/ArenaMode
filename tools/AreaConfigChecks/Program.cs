using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Configuration;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

int checks=0;
void Need(bool condition,string marker,string reason)
{checks++;if(!condition)throw new InvalidOperationException(marker+": "+reason);}
var scratch=Path.Combine(AppContext.BaseDirectory,"config",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
ConfigFile Bind(string name)
{
    var file=new ConfigFile(Path.Combine(scratch,name+".cfg"),false){SaveOnConfigSet=false};
    Settings.Init(file);return file;
}

try
{
    string probe=args.FirstOrDefault()??"all";
    Need(probe is "all" or "binding" or "selection","PROBE","Unknown test probe.");
    var config=Bind("initial");
    var acceptable=Settings.TeamDeathmatchArea.Description.AcceptableValues as AcceptableValueList<string>;
    const string eastId=ArenaAreaCatalog.CustomsFactoryEastPolygonId;
    if(probe=="selection")
    {
        Need(Settings.TrySelectArea(eastId)&&Settings.TeamDeathmatchArea.Value==eastId,
            "SELECT-AREA","The actually compiled Settings.TrySelectArea must accept offered Factory East.");
        Console.WriteLine("AREA CONFIG SELECTION PROBE PASSED");return 0;
    }
    Need(acceptable!=null&&acceptable.IsValid(eastId),"ACCEPTABLE-AREA",
        "Real BepInEx AcceptableValueList must accept offered Factory East.");
    if(probe=="binding") {Console.WriteLine("AREA CONFIG BINDING PROBE PASSED");return 0;}

    var offered=new HashSet<string>(StringComparer.Ordinal);
    foreach(string map in new[]{ArenaMapCatalog.FactoryDayId,ArenaMapCatalog.FactoryNightId,ArenaMapCatalog.LabyrinthId,
        ArenaMapCatalog.LaboratoryId,ArenaMapCatalog.CustomsId})
        foreach(var mode in ArenaModeCatalog.All)
            foreach(bool pitFireTeam in new[]{false,true})
            {
                var state=ArenaAreaSelectionState.Create(map,mode.Id,pitFireTeam,ArenaAreaCatalog.FullMapId,4,4);
                foreach(var option in state.Options)
                {
                    offered.Add(option.Id);
                    Need(acceptable.IsValid(option.Id),"ACCEPTABLE-AREA",
                        $"Menu offers {option.Id} on {map}/{mode.Id}, but actual config would reject or clamp it.");
                }
            }
    Need(offered.SetEquals(new[]{ArenaAreaCatalog.FullMapId,ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
        ArenaAreaCatalog.CustomsYardId,eastId}),"MENU-COVERAGE","Only whole map and three surveyed Customs choices remain offered.");
    Need(offered.SetEquals(acceptable.AcceptableValues),"CONFIG-MENU-PARITY","The real config list and native-menu proposals must expose the same protocol IDs.");
    Need(Settings.TeamDeathmatchArea.Definition.Section=="Team Deathmatch"&&Settings.TeamDeathmatchArea.Definition.Key=="ExperimentalArea",
        "CONFIG-KEY","Keep the existing user configuration address.");

    foreach(bool autoSave in new[]{false,true})
        foreach(string areaId in offered.OrderBy(id=>id,StringComparer.Ordinal))
        {
            string label=(autoSave?"auto-":"batch-")+areaId;
            config=Bind(label);
            string baseline=areaId==ArenaAreaCatalog.FullMapId?ArenaAreaCatalog.CustomsFactoryCenterTeamsId:ArenaAreaCatalog.FullMapId;
            Settings.TeamDeathmatchArea.Value=baseline;config.Save();
            config.SaveOnConfigSet=autoSave;
            int reports=SettingsSync.Reports;
            Need(Settings.TrySelectArea(areaId),"SELECT-AREA",$"Actual Settings selection rejected {areaId} (auto-save={autoSave}).");
            Need(Settings.TeamDeathmatchArea.Value==areaId,"SELECT-AREA","BepInEx silently clamped the supposedly selected area.");
            Need(SettingsSync.Reports==reports+1,"SETTINGS-REPORT","Accepted changed menu selection must be reported exactly once.");
            var saved=File.ReadAllBytes(config.ConfigFilePath);
            Need(File.ReadAllLines(config.ConfigFilePath).Contains("ExperimentalArea = "+areaId),
                "PERSISTENCE","Selection must reach the real config file without a test-issued Save.");
            Need(Settings.TrySelectArea(areaId)&&SettingsSync.Reports==reports+1,"UNCHANGED-SELECTION","Already selected area is accepted without a duplicate report.");
            Need(saved.SequenceEqual(File.ReadAllBytes(config.ConfigFilePath)),"UNCHANGED-SELECTION","Already selected area must not rewrite its file.");

            var reread=new ConfigFile(config.ConfigFilePath,false){SaveOnConfigSet=false};
            Settings.Init(reread);
            Need(Settings.TeamDeathmatchArea.Value==areaId,"RELOAD","Fresh ConfigFile plus full Settings.Init must retain the selected ID.");
            Need(saved.SequenceEqual(File.ReadAllBytes(config.ConfigFilePath)),"RELOAD","A valid saved choice must not be rewritten while binding.");
            Need(Settings.TeamDeathmatchArea.Description.AcceptableValues.IsValid(areaId),"RELOAD","The new binding must still accept its persisted value.");
        }

    config=Bind("unknown-selection");
    Need(Settings.TrySelectArea(eastId),"SELECT-AREA","East is the stable starting choice for invalid-input checks.");
    foreach(string invalid in new[]{ArenaAreaCatalog.FactorySouthId,ArenaAreaCatalog.CustomsFactoryCenterId,
        "not-an-area","customs-factory-east-polygon-v2",eastId.ToUpperInvariant()," "+eastId,eastId+" ","",null})
    {
        var before=File.ReadAllBytes(config.ConfigFilePath);int reports=SettingsSync.Reports;
        Need(!Settings.TeamDeathmatchArea.Description.AcceptableValues.IsValid(invalid),"UNKNOWN-AREA","Real config must reject unknown/nonexact area IDs.");
        Need(!Settings.TrySelectArea(invalid),"UNKNOWN-AREA","Menu must fail closed for unknown/null area IDs.");
        Need(Settings.TeamDeathmatchArea.Value==eastId&&before.SequenceEqual(File.ReadAllBytes(config.ConfigFilePath))
            &&SettingsSync.Reports==reports,"UNKNOWN-UNCHANGED","Rejected input cannot mutate, save or report a selection.");
    }

    // Exercise BepInEx's actual Reload on the same already-bound entries.
    string persisted=File.ReadAllText(config.ConfigFilePath);
    var areaLine=new Regex(@"^ExperimentalArea = [^\r\n]*",RegexOptions.Multiline);
    Need(areaLine.Matches(persisted).Count==1,"RELOAD-FIXTURE","Exactly one physical persisted area entry is expected.");
    File.WriteAllText(config.ConfigFilePath,areaLine.Replace(persisted,"ExperimentalArea = "+ArenaAreaCatalog.CustomsFactoryCenterTeamsId));
    config.Reload();
    Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.CustomsFactoryCenterTeamsId,"LIVE-RELOAD","Existing ConfigEntry must observe valid external file changes.");
    File.WriteAllText(config.ConfigFilePath,areaLine.Replace(persisted,"ExperimentalArea = "+eastId));
    config.Reload();Need(Settings.TeamDeathmatchArea.Value==eastId,"LIVE-RELOAD","Reload must not clamp the newly added area to full-map.");
    File.WriteAllText(config.ConfigFilePath,areaLine.Replace(persisted,"ExperimentalArea = unpublished-draft"));
    config.Reload();
    Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"INVALID-PERSISTED",
        "Invalid saved input follows real BepInEx's first-acceptable-value clamp; it never becomes an accepted unpublished area.");
    var corrupt=Path.Combine(scratch,"unknown-on-start.cfg");
    File.WriteAllText(corrupt,"[Team Deathmatch]\nExperimentalArea = unpublished-draft\n");
    Settings.Init(new ConfigFile(corrupt,false){SaveOnConfigSet=false});
    Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"INVALID-PERSISTED","Fresh binding also rejects an unknown saved ID.");

    foreach(string retired in new[]{ArenaAreaCatalog.FactorySouthId,ArenaAreaCatalog.CustomsFactoryCenterId})
    {
        var retiredPath=Path.Combine(scratch,retired+".cfg");
        File.WriteAllText(retiredPath,"[Team Deathmatch]\nExperimentalArea = "+retired+"\n");
        var retiredConfig=new ConfigFile(retiredPath,false){SaveOnConfigSet=false};
        Settings.Init(retiredConfig);
        Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"RETIRED-BIND","Actual BepInEx binding clamps obsolete saved IDs to full map.");
        var migrated=ArenaAreaSelectionState.Create(ArenaMapCatalog.CustomsId,"teamdeathmatch",false,
            Settings.TeamDeathmatchArea.Value,4,4);
        Need(migrated.SelectedId==ArenaAreaCatalog.CustomsFactoryCenterTeamsId,"RETIRED-MENU","Customs menu proposes its supported surveyed contour after old config migration.");
        Need(!ArenaAreaCatalog.TryResolve(retired,ArenaMapCatalog.CustomsId,"teamdeathmatch",false,out _,out _)
            &&!ArenaAreaCatalog.TryResolve(retired,ArenaMapCatalog.FactoryDayId,"teamdeathmatch",false,out _,out _),
            "RETIRED-PROTOCOL","Migration does not make an explicit retired arm ID valid.");
        Settings.TrySelectArea(eastId);
        File.WriteAllText(retiredPath,"[Team Deathmatch]\nExperimentalArea = "+retired+"\n");
        retiredConfig.Reload();
        Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"RETIRED-RELOAD","Live Reload also clamps an obsolete saved ID.");
    }

    // Actual Settings.Init against real persisted BepInEx entries, including
    // the boolean written by the already deployed Equator release.
    foreach (var migration in new[]
    {
        (Name: "equator-release", Maps: ArenaMapCatalog.EquatorDefaultClientAllowlist, Legacy: true, Revision: 0, Gain: true),
        (Name: "native-first-upgrade", Maps: ArenaMapCatalog.NativeDefaultClientAllowlist, Legacy: false, Revision: 0, Gain: true),
        (Name: "equator-removed", Maps: ArenaMapCatalog.NativeDefaultClientAllowlist, Legacy: true, Revision: 0, Gain: false),
        (Name: "custom-subset", Maps: "factory4_day,arena_equator", Legacy: true, Revision: 0, Gain: false),
        (Name: "custom-order", Maps: "arena_equator,"+ArenaMapCatalog.NativeDefaultClientAllowlist, Legacy: true, Revision: 0, Gain: false),
        (Name: "revision-one", Maps: ArenaMapCatalog.EquatorDefaultClientAllowlist, Legacy: true, Revision: 1, Gain: true),
        (Name: "bay5-removed", Maps: ArenaMapCatalog.EquatorDefaultClientAllowlist, Legacy: true, Revision: 2, Gain: false),
        (Name: "future-version", Maps: ArenaMapCatalog.EquatorDefaultClientAllowlist, Legacy: true, Revision: 3, Gain: false),
    })
    {
        string path=Path.Combine(scratch,"optional-"+migration.Name+".cfg");
        File.WriteAllText(path,"[General]\nMaps = "+migration.Maps+"\nOptionalMapsMigrated = "+migration.Legacy
            +(migration.Revision==0?"":"\nOptionalMapsRevision = "+migration.Revision)+"\n");
        var persistedConfig=new ConfigFile(path,false){SaveOnConfigSet=true};
        Settings.Init(persistedConfig);
        string expected=migration.Gain?ArenaMapCatalog.DefaultClientAllowlist:migration.Maps;
        Need(Settings.Maps.Value==expected,"OPTIONAL-MAP-UPGRADE",
            "Actual Settings.Init must honor the revision and saved Equator marker for "+migration.Name);
        Need((int)persistedConfig[new ConfigDefinition("General","OptionalMapsRevision")].BoxedValue==Math.Max(2,migration.Revision),
            "OPTIONAL-MAP-MARKER","Internal revision records the upgrade decision without reducing a future marker.");
        Settings.Init(new ConfigFile(path,false){SaveOnConfigSet=true});
        Need(Settings.Maps.Value==expected,"OPTIONAL-MAP-PERSISTED","Optional map upgrade survives a fresh real ConfigFile binding.");
        if (migration.Gain)
        {
            Settings.Maps.Value=ArenaMapCatalog.EquatorDefaultClientAllowlist;
            Settings.Init(new ConfigFile(path,false){SaveOnConfigSet=true});
            Need(Settings.Maps.Value==ArenaMapCatalog.EquatorDefaultClientAllowlist,"OPTIONAL-MAP-REMOVAL",
                "Removing Bay5 after migration must survive a real save and subsequent Settings.Init.");
        }
    }

    Console.WriteLine("Real BepInEx assembly: "+typeof(ConfigFile).Assembly.FullName);
    Console.WriteLine("BepInEx SHA256: "+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ConfigFile).Assembly.Location))));
    Console.WriteLine("Offered and persisted IDs: "+string.Join(", ",offered.OrderBy(id=>id,StringComparer.Ordinal)));
    foreach(var pair in new[]{("customs-factory-center-polygon-v1",ArenaAreaCatalog.CustomsFactoryCenterTeamsId),
        ("customs-factory-center-teams-v1",ArenaAreaCatalog.CustomsFactoryCenterTeamsId),
        ("customs-factory-east-polygon-v1",eastId)})
    {
        var path=Path.Combine(scratch,pair.Item1+".cfg");
        File.WriteAllText(path,"[Team Deathmatch]\nExperimentalArea = "+pair.Item1+"\n");
        Settings.Init(new ConfigFile(path,false){SaveOnConfigSet=false});
        Need(Settings.TeamDeathmatchArea.Value==pair.Item2,"MIGRATION","Saved legacy preference must select corresponding new contour.");
        Need(!Settings.TrySelectArea(pair.Item1)&&!ArenaAreaCatalog.TryResolve(pair.Item1,"bigmap","teamdeathmatch",false,out _,out _),
            "MIGRATION-PROTOCOL","Config migration must not accept old menu/wire IDs.");
    }
    Console.WriteLine($"AREA CONFIG PRODUCTION CHECKS PASSED ({checks} assertions; real BepInEx bind/save/reload)");
    Console.WriteLine("Scratch config files: "+scratch);
    return 0;
}
catch(Exception ex)
{Console.Error.WriteLine("AREA CONFIG REJECTED: "+ex.Message);return 1;}
