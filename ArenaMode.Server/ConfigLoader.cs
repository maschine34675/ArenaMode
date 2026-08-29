using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace ArenaMode.Server;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public class ConfigLoader(ModHelper modHelper, ISptLogger<ConfigLoader> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var configDir = Path.Combine(modPath, "config");
            var configFile = Path.Combine(configDir, "config.json");

            if (!File.Exists(configFile))
            {
                Directory.CreateDirectory(configDir);
                File.WriteAllText(
                    configFile,
                    JsonSerializer.Serialize(new ArenaModeConfig(), new JsonSerializerOptions { WriteIndented = true }));
                logger.Info("[ArenaMode] Default config written to config/config.json");
            }

            ArenaContext.Config = modHelper.GetJsonDataFromModFile<ArenaModeConfig>(configDir, "config.json")
                                  ?? new ArenaModeConfig();
            logger.Info($"[ArenaMode] Config loaded — maps: {string.Join(", ", ArenaContext.Config.Maps.Keys)}");

            // Kit catalog: served verbatim to the client. The default file is deployed
            // by the build (never overwriting user edits); missing file = empty catalog.
            var kitsFile = Path.Combine(configDir, "kits.json");
            if (File.Exists(kitsFile))
            {
                ArenaContext.KitsJson = File.ReadAllText(kitsFile);
                logger.Info("[ArenaMode] Kit catalog loaded from config/kits.json");
            }
            else
            {
                logger.Warning("[ArenaMode] config/kits.json missing — kit catalog is empty, arena respawns will be naked.");
            }

            // Gun Game's weapon ladder. Optional: without it the mode falls back
            // to the kit catalog, which is what it used before the ladder existed.
            var ladderFile = Path.Combine(configDir, "ladder.json");
            if (File.Exists(ladderFile))
            {
                ArenaContext.LadderJson = File.ReadAllText(ladderFile);
                logger.Info("[ArenaMode] Gun Game ladder loaded from config/ladder.json");
            }
            else
            {
                logger.Info("[ArenaMode] config/ladder.json missing — Gun Game will ladder through the kit catalog.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Config load failed, using defaults: {ex}");
            ArenaContext.Config = new ArenaModeConfig();
        }

        return Task.CompletedTask;
    }
}
