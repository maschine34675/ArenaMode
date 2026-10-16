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

    /// <summary>
    /// Forget the ladder so the next raid asks again. The server cuts a new one
    /// per raid, and a catalogue cached for the whole game session would climb
    /// the same twenty-four weapons all evening — which was the point of
    /// drawing it in the first place.
    /// </summary>
    public static void Reset() => _rungs = null;

    /// <summary>
    /// Asks the server to cut a fresh ladder and installs it, for a player who
    /// has just climbed the whole of the last one.
    ///
    /// Synchronous, and deliberately so: it is called at the completion of a
    /// ladder, a moment that already stops to congratulate the player and swap
    /// their weapon, so a few milliseconds on the loopback disappear into it.
    /// Returns false and leaves the old ladder in place on any failure — the
    /// player keeps climbing something rather than nothing.
    /// </summary>
    public static bool Redraw()
    {
        try
        {
            var json = RequestHandler.PostJson(Protocol.LadderRedrawRoute, "{}");
            var rungs = JsonConvert.DeserializeObject<List<KitDefinition>>(json);
            if (rungs == null || rungs.Count == 0)
            {
                Plugin.ModLogger.LogWarning("Gun Game: the server returned no new ladder — keeping the current one.");
                return false;
            }

            rungs.Sort((a, b) => a.Order.CompareTo(b.Order));
            _rungs = rungs;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Gun Game: could not draw a new ladder ({ex.Message}) — keeping the current one.");
            return false;
        }
    }

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

            Plugin.ModLogger.LogDebug($"Gun Game ladder loaded: {_rungs.Count} rungs.");
        }
        catch (Exception ex)
        {
            // Left null so a transient server hiccup is retried rather than
            // downgrading every later raid this session to the kit catalog.
            Plugin.ModLogger.LogWarning($"Gun Game ladder load failed ({ex.Message}) — falling back to the kit catalog.");
        }
    }
}
