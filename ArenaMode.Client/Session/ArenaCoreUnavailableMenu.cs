using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Only the rejected-Core startup path. No gameplay, preview, PFT or Core types
/// may enter this dependency island; the normal plugin remains disabled.
/// </summary>
internal static class ArenaCoreUnavailableMenu
{
    private const string Reason = "The required Redeploy.Core library could not be loaded or is too old. "
        + "Reinstall the current ArenaMode package, including maschine-Redeploy.Core.dll, "
        + "keep only one Core copy and restart the game.";
    private static readonly ConditionalWeakTable<MatchMakerSideSelectionScreen, Placement> Placements = new();

    internal static void Enable()
    {
        var harmony = new Harmony(Plugin.PluginGuid + ".CoreUnavailableMenu");
        harmony.Patch(AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Show),
            new[] { typeof(MatchMakerSideSelectionScreen.RaidSideSelectionScreenController) }),
            postfix: new HarmonyMethod(typeof(ArenaCoreUnavailableMenu), nameof(Show)));
        harmony.Patch(AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.Close)),
            prefix: new HarmonyMethod(typeof(ArenaCoreUnavailableMenu), nameof(Close)));
        harmony.Patch(AccessTools.Method(typeof(MatchMakerSideSelectionScreen), nameof(MatchMakerSideSelectionScreen.SetSelectedSide)),
            prefix: new HarmonyMethod(typeof(ArenaCoreUnavailableMenu), nameof(Select)));
    }

    private static void Select(MatchMakerSideSelectionScreen __instance, ref ESideType side)
    {
        if (side == ESideType.Random && Placements.TryGetValue(__instance, out _) && IsRaidSelection())
            side = ESideType.Pmc;
    }

    private static void Show(MatchMakerSideSelectionScreen __instance)
    {
        try
        {
            Close(__instance);
            if (!IsRaidSelection()) return;
            var arena = __instance._randomButton;
            ArenaSideSelectionView.Show(__instance.transform, arena, false);
            var wrapper = arena.transform.parent;
            Placements.Add(__instance, new Placement { Wrapper = wrapper, Position = wrapper.localPosition });
            var a = arena.SpawnableToggle._headerLabel.rectTransform;
            var p = __instance._pmcButton.SpawnableToggle._headerLabel.rectTransform;
            wrapper.localPosition += new Vector3(0f,
                __instance.transform.InverseTransformPoint(p.TransformPoint(p.rect.center)).y
                - __instance.transform.InverseTransformPoint(a.TransformPoint(a.rect.center)).y, 0f);
            ArenaAvailabilityNotice.Show(__instance, Reason);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError("Arena dependency notice could not be displayed: " + ex.Message);
        }
    }

    private static void Close(MatchMakerSideSelectionScreen __instance)
    {
        ArenaAvailabilityNotice.Hide(__instance);
        if (!Placements.TryGetValue(__instance, out var placement)) return;
        Placements.Remove(__instance);
        if (placement.Wrapper != null) placement.Wrapper.localPosition = placement.Position;
    }

    private static bool IsRaidSelection()
    {
        // PFT reuses this screen for My Squad. Read only its presentation flag,
        // without invoking the normal adapter (which requires Core).
        if (!Chainloader.PluginInfos.TryGetValue("xyz.pit.fireteam", out var pft)) return true;
        try
        {
            var property = pft.Instance?.GetType().Assembly
                .GetType("pitTeam.Modules.SquadSideSelectionFlow")
                ?.GetProperty("SquadModeActive", BindingFlags.Static | BindingFlags.Public);
            return property?.PropertyType == typeof(bool) && property.GetValue(null) is false;
        }
        catch { return false; } // Unknown foreign UI is not ours to rearrange.
    }

    private sealed class Placement
    {
        internal Transform Wrapper;
        internal Vector3 Position;
    }
}
