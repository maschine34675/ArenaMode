using ArenaMode.Shared;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// The Gun Game weapon ladder, fetched once per game session from the server
/// (user/mods/ArenaMode/config/ladder.json).
///
/// A rung has the same shape as a kit but carries only a primary weapon, loaded
/// and chambered. It is generated from the game's OWN weapon presets — the ones
/// the trader and the encyclopedia use — so every rung is a build the game
/// already considers valid, and the ladder keeps working when weapon mods are
/// installed. An empty ladder is not an error: Gun Game then walks the kit
/// catalog instead, which is what it did before the ladder existed.
/// </summary>
public static class LadderCatalog
{
    private static List<KitDefinition> _rungs;

    public static IReadOnlyList<KitDefinition> Rungs
    {
        get
        {
            EnsureLoaded();
            return _rungs;
        }
    }

    /// <summary>Fetch during a loading screen instead of a gameplay frame.</summary>
    public static void Preload() => EnsureLoaded();

    private static void EnsureLoaded()
    {
        if (_rungs != null)
            return;

        try
        {
            var json = RequestHandler.GetJson(Protocol.LadderRoute);
            var rungs = JsonConvert.DeserializeObject<List<KitDefinition>>(json);
            if (rungs != null)
            {
                rungs.Sort((a, b) => a.Order.CompareTo(b.Order));
                _rungs = rungs;
            }
            else
            {
                _rungs = new List<KitDefinition>();
            }

            Plugin.ModLogger.LogInfo($"Gun Game ladder loaded: {_rungs.Count} rungs.");
        }
        catch (Exception ex)
        {
            // Left null so a transient server hiccup is retried rather than
            // downgrading every later raid this session to the kit catalog.
            Plugin.ModLogger.LogWarning($"Gun Game ladder load failed ({ex.Message}) — falling back to the kit catalog.");
        }
    }
}
