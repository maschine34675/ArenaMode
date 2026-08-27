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
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Config load failed, using defaults: {ex}");
            ArenaContext.Config = new ArenaModeConfig();
        }

        return Task.CompletedTask;
    }
}
