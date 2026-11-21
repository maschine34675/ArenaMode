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

    foreach(string map in new[]{ArenaMapCatalog.FactoryDayId,ArenaMapCatalog.FactoryNightId,
        ArenaMapCatalog.LaboratoryId,ArenaMapCatalog.LabyrinthId,ArenaMapCatalog.CustomsId,
        ArenaMapCatalog.EquatorId,ArenaMapCatalog.Bay5Id,ArenaMapCatalog.AirPitId,ArenaMapCatalog.BowlId,ArenaMapCatalog.IcebergId,ArenaMapCatalog.AutoServiceId,ArenaMapCatalog.YardId})
        foreach(var mode in ArenaModeCatalog.All)
        {
            var team=ArenaAreaSelectionState.Create(map,mode.Id,true,ArenaAreaCatalog.FullMapId,4,4);
            // Written out on purpose (ArenaMapPort rows of 2026-11-12): squad Zombies are refused by
            // space on Equator and Chop Shop; squad Tactical only on Chop Shop, whose yard centre
            // has too few distant rows for the exact five a squad round needs. Block carries no
            // refusal: it admits PitFireTeam Tactical and PitFireTeam Zombies, so it is no exception here.
            bool allowed=(mode.Id is ArenaModeCatalog.TacticalId or ArenaModeCatalog.ZombieId)
                && !(mode.Id==ArenaModeCatalog.ZombieId&&(ArenaMapCatalog.IsEquator(map)||ArenaMapCatalog.IsAutoService(map)))
                && !(mode.Id==ArenaModeCatalog.TacticalId&&ArenaMapCatalog.IsAutoService(map));
            Need(team.Available==allowed,"PFT-MAP-ADMISSION",
                $"{map}/{mode.Id}: protected squad map choices must agree with the supported team modes.");
            if(!allowed)continue;
            string[] expectedAreas = map == ArenaMapCatalog.CustomsId
                ? new[]{ArenaAreaCatalog.CustomsFactoryCenterTeamsId,ArenaAreaCatalog.CustomsFactoryEastPolygonId,ArenaAreaCatalog.CustomsYardId}
                : new[]{ArenaAreaCatalog.FullMapId};
            Need(team.Options.Select(option=>option.Id).SequenceEqual(expectedAreas)
                &&team.SelectedId==expectedAreas[0]&&Settings.TrySelectArea(team.SelectedId),"PFT-MAP-CONFIG",
                "Protected squads preserve the three Customs areas and whole-map choice elsewhere through real Settings.");
            Need(team.Summary==(mode.Id==ArenaModeCatalog.ZombieId?"PitFireTeam squad":"PitFireTeam squad vs 5"),
                "PFT-MAP-SUMMARY","Zombie squad has no fixed opposing team; live view supplies exact allied count.");
        }

    // Map picker hints in a mode without a participant choice (solo and squad Zombies).
    // Expected texts are literals, not read from the production state.
    foreach(var (map,pitFireTeam,hint) in new[]{
        (ArenaMapCatalog.IcebergId,false,""),
        (ArenaMapCatalog.IcebergId,true,"PitFireTeam squad"),
        (ArenaMapCatalog.BowlId,false,""),
        // Block has no map hint of its own (no refusal); with a squad it shows only the generic PitFireTeam text.
        (ArenaMapCatalog.YardId,false,""),
        (ArenaMapCatalog.YardId,true,"PitFireTeam squad"),
        (ArenaMapCatalog.EquatorId,false,"Limited space: no squad Zombies"),
        (ArenaMapCatalog.AutoServiceId,false,"Small map: no squad Tactical / Zombies")})
    {
        var hinted=ArenaAreaSelectionState.Create(map,ArenaModeCatalog.ZombieId,pitFireTeam,ArenaAreaCatalog.FullMapId,4,4);
        Need(hinted.Available&&hinted.ModeHint==hint,"MAP-MODE-HINT",
            $"{map}/zombie (PFT={pitFireTeam}) must hint \"{hint}\", got \"{hinted.ModeHint}\".");
    }

    // Solo Tactical is offered on all four; the squad format on Equator, Iceberg and Block, not on Chop Shop.
    foreach(var (map,pitFireTeam,offeredHere) in new[]{
        (ArenaMapCatalog.EquatorId,false,true),(ArenaMapCatalog.EquatorId,true,true),
        (ArenaMapCatalog.IcebergId,false,true),(ArenaMapCatalog.IcebergId,true,true),
        (ArenaMapCatalog.AutoServiceId,false,true),(ArenaMapCatalog.AutoServiceId,true,false),
        (ArenaMapCatalog.YardId,false,true),(ArenaMapCatalog.YardId,true,true)})
        Need(ArenaAreaSelectionState.Create(map,ArenaModeCatalog.TacticalId,pitFireTeam,ArenaAreaCatalog.FullMapId,4,4).Available==offeredHere,
            "TACTICAL-MAP-OFFER",$"{map}/tactical (PFT={pitFireTeam}) must be {(offeredHere?"offered":"refused")}.");

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
        Need(migrated.SelectedId==ArenaAreaCatalog.CustomsFactoryCenterTeamsId
            &&Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"RETIRED-MENU",
            "Customs proposes its first supported contour without persisting over the migrated preference.");
        Need(!ArenaAreaCatalog.TryResolve(retired,ArenaMapCatalog.CustomsId,"teamdeathmatch",false,out _,out _)
            &&!ArenaAreaCatalog.TryResolve(retired,ArenaMapCatalog.FactoryDayId,"teamdeathmatch",false,out _,out _),
            "RETIRED-PROTOCOL","Migration does not make an explicit retired arm ID valid.");
        Settings.TrySelectArea(eastId);
        File.WriteAllText(retiredPath,"[Team Deathmatch]\nExperimentalArea = "+retired+"\n");
        retiredConfig.Reload();
        Need(Settings.TeamDeathmatchArea.Value==ArenaAreaCatalog.FullMapId,"RETIRED-RELOAD","Live Reload also clamps an obsolete saved ID.");
    }

    // The difficulty migration against the REAL BepInEx ConfigFile: the retired [Difficulty] lines are
    // read from the file after ~100 Binds, so every Bind that saves (SaveOnConfigSet true, the BepInEx
    // default) must keep the unbound lines (ConfigFile.Save writes OrphanedEntries). A legacy file selects
    // Reduced once, changes no other value, and a second start neither repeats nor loses the choice.
#if !HISTORICAL_SETTINGS
    int legacyIndex=0;
    foreach (bool saveOnSet in new[]{true,false})
    foreach (var legacy in new[]{("ScaleDifficulty = false\nMaxBotDifficulty = impossible",true),("MaxBotDifficulty = hard",true),
        ("ScaleDifficulty = true\nMaxBotDifficulty = impossible",false),("PressureRampMinutes = 5",false)})
    {
        var legacyPath=Path.Combine(scratch,"legacy-"+(legacyIndex++)+".cfg");
        File.WriteAllText(legacyPath,"[Difficulty]\n"+legacy.Item1+"\n\n[General]\nRespawnMinDistance = 77\n");
        var first=new ConfigFile(legacyPath,false){SaveOnConfigSet=saveOnSet};
        Settings.Init(first);
        var expectedStep=legacy.Item2?ArenaDifficultyStep.Reduced:ArenaDifficultyStep.Maximum;
        Need(Settings.DifficultyStep.Value==expectedStep,"DIFFICULTY-MIGRATION","Legacy difficulty lines select the step through a real ConfigFile (saveOnSet="+saveOnSet+"): "+legacy.Item1);
        Need(first.TryGetEntry<int>("Difficulty","DifficultyRevision",out var recorded)&&recorded.Value==ArenaDifficultyMigration.Revision,
            "DIFFICULTY-MIGRATION-REVISION","The first start records the migration revision.");
        Need(Settings.RespawnMinDistance.Value==77,"DIFFICULTY-MIGRATION-OTHER","The migration changes no other saved value.");
        var again=new ConfigFile(legacyPath,false){SaveOnConfigSet=saveOnSet};
        Settings.Init(again);
        Need(Settings.DifficultyStep.Value==expectedStep,"DIFFICULTY-MIGRATION-ONCE","A second start keeps the migrated step.");
        again.Save();
        Settings.Init(new ConfigFile(legacyPath,false){SaveOnConfigSet=saveOnSet});
        Need(Settings.DifficultyStep.Value==expectedStep,"DIFFICULTY-MIGRATION-SAVED","The step survives a save and another start.");
        // The retired lines stay in the file for good, so only the recorded revision keeps a later
        // start from selecting Reduced again: a Maximum chosen by hand afterwards has to survive.
        var hand=new ConfigFile(legacyPath,false){SaveOnConfigSet=saveOnSet};
        Settings.Init(hand);
        Settings.DifficultyStep.Value=ArenaDifficultyStep.Maximum;
        hand.Save();
        Need(File.ReadAllText(legacyPath).Contains(legacy.Item1.Split('\n')[0]),"DIFFICULTY-MIGRATION-ORPHAN",
            "The retired lines are still in the saved file, so this case does test the revision.");
        Settings.Init(new ConfigFile(legacyPath,false){SaveOnConfigSet=saveOnSet});
        Need(Settings.DifficultyStep.Value==ArenaDifficultyStep.Maximum,"DIFFICULTY-MIGRATION-HANDSET",
            "A Maximum chosen by hand after the migration survives the next start.");
    }
#endif
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
        (Name: "bay5-release", Maps: ArenaMapCatalog.Bay5DefaultClientAllowlist, Legacy: true, Revision: 2, Gain: true),
        (Name: "airpit-removed", Maps: ArenaMapCatalog.Bay5DefaultClientAllowlist, Legacy: true, Revision: 3, Gain: false),
        (Name: "airpit-release", Maps: ArenaMapCatalog.AirPitDefaultClientAllowlist, Legacy: true, Revision: 3, Gain: true),
        (Name: "bowl-removed", Maps: ArenaMapCatalog.AirPitDefaultClientAllowlist, Legacy: true, Revision: 4, Gain: false),
        (Name: "bowl-release", Maps: ArenaMapCatalog.BowlDefaultClientAllowlist, Legacy: true, Revision: 4, Gain: true),
        (Name: "iceberg-removed", Maps: ArenaMapCatalog.BowlDefaultClientAllowlist, Legacy: true, Revision: 5, Gain: false),
        // Gain means "becomes the current default": the exact Iceberg default at revision 5 gains Chop Shop and Block.
        (Name: "iceberg-release", Maps: ArenaMapCatalog.IcebergDefaultClientAllowlist, Legacy: true, Revision: 5, Gain: true),
        // The Iceberg default at revision 6 means Chop Shop was removed on purpose: it gains neither Chop Shop nor Block.
        (Name: "chopshop-removed", Maps: ArenaMapCatalog.IcebergDefaultClientAllowlist, Legacy: true, Revision: 6, Gain: false),
        (Name: "block-release", Maps: ArenaMapCatalog.AutoServiceDefaultClientAllowlist, Legacy: true, Revision: 6, Gain: true),
        (Name: "block-removed", Maps: ArenaMapCatalog.AutoServiceDefaultClientAllowlist, Legacy: true, Revision: 7, Gain: false),
        (Name: "future-version", Maps: ArenaMapCatalog.AutoServiceDefaultClientAllowlist, Legacy: true, Revision: 8, Gain: false),
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
        Need((int)persistedConfig[new ConfigDefinition("General","OptionalMapsRevision")].BoxedValue==Math.Max(ArenaMapCatalog.OptionalMapsRevision,migration.Revision),
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
