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
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Config load failed, using defaults: {ex}");
            ArenaContext.Config = new ArenaModeConfig();
        }

        return Task.CompletedTask;
    }
}
