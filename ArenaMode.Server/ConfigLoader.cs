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

            // The web page writes through this, so it has to know where the
            // files are before anything can be saved or reloaded.
            Services.ConfigStore.Initialize(configDir);

            if (!File.Exists(configFile))
            {
                Directory.CreateDirectory(configDir);
                File.WriteAllText(
                    configFile,
                    JsonSerializer.Serialize(new ArenaModeConfig(), new JsonSerializerOptions { WriteIndented = true }));
                logger.Info("[ArenaMode] Default config written to config/config.json");
            }

            // Through ConfigStore rather than straight off disk: it tops up keys
            // this version added and the saved file predates. A per-map field is
            // the case that needs it — the Maps dictionary is replaced wholesale
            // by the file, so an old section keeps the generic default rather
            // than that map's override, silently.
            if (Services.ConfigStore.Reload(out var repaired) != Services.ConfigResult.Success)
                logger.Warning($"[ArenaMode] {repaired}");
            logger.Debug($"[ArenaMode] Config loaded — maps: {string.Join(", ", ArenaContext.Config.Maps.Keys)}");

            // Kit catalog: served verbatim to the client. The default file is deployed
            // by the build (never overwriting user edits); missing file = empty catalog.
            var kitsFile = Path.Combine(configDir, "kits.json");
            if (File.Exists(kitsFile))
            {
                ArenaContext.KitsJson = File.ReadAllText(kitsFile);
                logger.Debug("[ArenaMode] Kit catalog loaded from config/kits.json");
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
                logger.Debug("[ArenaMode] Gun Game ladder loaded from config/ladder.json");
            }
            else
            {
                logger.Debug("[ArenaMode] config/ladder.json missing — Gun Game will ladder through the kit catalog.");
            }

            // The client's own settings, as last reported. Restored here so the
            // config page has something to show before the game is even started.
            Services.ClientSettingsStore.LoadFromDisk();
        }
        catch (Exception ex)
        {
            logger.Error($"[ArenaMode] Config load failed, using defaults: {ex}");
            ArenaContext.Config = new ArenaModeConfig();
        }

        return Task.CompletedTask;
    }
}
