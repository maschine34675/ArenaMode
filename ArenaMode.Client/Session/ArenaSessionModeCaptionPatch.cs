using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using TMPro;

namespace ArenaMode.Session;

/// <summary>
/// Shows "ARENA ZONE" instead of the native "PVE ZONE" on the matchmaker screens
/// of the Arena setup flow. Two native paths write that label (decompile EFT
/// 0.16.9.5): <c>MatchmakerEftScreen&lt;,&gt;.Show(controller)</c> (EFT.UI.Screens/
/// MatchmakerEftScreen.cs:17, the base of the side, location and accept screens)
/// sets <c>_subCaption.text = controller.SessionMode.LocalizedEnum()</c>, and
/// <c>MatchmakerTimeHasCome.Show(session, raidSettings, matchmaker)</c>
/// (EFT.UI.Matchmaker/MatchmakerTimeHasCome.cs:219, "Deploying to location")
/// does the same. Both run before the postfixes below, so each postfix only
/// has to overwrite the text once its own screen is shown:
/// <list type="bullet">
/// <item>location screen: <c>ShowInternal</c> (MatchMakerSelectionLocationScreen.cs:
/// 409), the call that finishes every <c>Show</c> (line 391-407);</item>
/// <item>accept screen: <c>Show(MatchmakerAcceptScreenController)</c>
/// (MatchMakerAcceptScreen.cs:321), whose controller exposes <c>RaidSettings</c>;</item>
/// <item>deploying screen: <c>Show(TimeHasComeScreenController)</c>
/// (MatchmakerTimeHasCome.cs:204), the override that calls the three-argument
/// overload (line 209, caption at 219). It is patched instead of that overload
/// because the controller tells a reconnect to an ordinary raid apart;</item>
/// <item>side screen: the ARENA toggle is the native third (Random) toggle, so
/// <c>ArenaSideSelectionPatch</c> calls <see cref="Apply"/> after
/// <c>Show</c> (MatchMakerSideSelectionScreen.cs:258) and <c>SetSelectedSide</c>
/// (line 448), both ways.</item>
/// </list>
/// Everything is gated on the Arena flow (<see cref="ArenaMenuSelection"/> for
/// the exact <c>RaidSettings</c> of that screen, plus <see cref="ArenaSession.
/// ArmedForNextRaid"/> on the deploying screen) and never throws: any doubt
/// leaves the native text. Outside the Arena flow nothing is touched and so
/// nothing needs restoring; only a caption this class overwrote is ever put
/// back (side screen, toggle switched off again).
/// </summary>
internal static class ArenaSessionModeCaptionPatch
{
    // The native text of every caption this class overwrote, so switching the
    // ARENA toggle off puts back exactly what EFT wrote (any language).
    private static readonly ConditionalWeakTable<TextMeshProUGUI, string> NativeText = new();

    public static void Enable()
    {
        new LocationPatch().Enable();
        new AcceptPatch().Enable();
        new DeployingPatch().Enable();
    }

    /// <summary>
    /// Sets the caption to the Arena text, or, when switched off, restores the
    /// native text this class replaced. A caption that was never overwritten (every
    /// ordinary flow) is not touched. Never throws.
    /// </summary>
    internal static void Apply(TextMeshProUGUI caption, bool arena)
    {
        try
        {
            if (caption == null)
                return;
            if (arena)
            {
                string current = caption.text;
                if (current == ArenaSessionModeCaption.ArenaText)
                    return;
                NativeText.Remove(caption);
                NativeText.Add(caption, current);
                caption.text = ArenaSessionModeCaption.Resolve(true, current);
            }
            else if (NativeText.TryGetValue(caption, out string native))
            {
                NativeText.Remove(caption);
                // A fresh native Show may already have rewritten the label.
                if (caption.text == ArenaSessionModeCaption.ArenaText)
                    caption.text = ArenaSessionModeCaption.Resolve(false, native);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena session caption left native: {ex.Message}");
        }
    }

    private sealed class LocationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerSelectionLocationScreen),
                nameof(MatchMakerSelectionLocationScreen.ShowInternal));

        [PatchPostfix]
        private static void Postfix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {
            try
            {
                if (ArenaMenuSelection.IsSelectedFor(raidSettings))
                    Apply(__instance._subCaption, true);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger?.LogWarning($"Arena location caption left native: {ex.Message}");
            }
        }
    }

    private sealed class AcceptPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchMakerAcceptScreen), nameof(MatchMakerAcceptScreen.Show),
                new[] { typeof(MatchMakerAcceptScreen.MatchmakerAcceptScreenController) });

        [PatchPostfix]
        private static void Postfix(MatchMakerAcceptScreen __instance,
            MatchMakerAcceptScreen.MatchmakerAcceptScreenController controller)
        {
            try
            {
                if (controller != null && ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))
                    Apply(__instance._subCaption, true);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger?.LogWarning($"Arena accept caption left native: {ex.Message}");
            }
        }
    }

    private sealed class DeployingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.Show),
                new[] { typeof(MatchmakerTimeHasCome.TimeHasComeScreenController) });

        [PatchPostfix]
        private static void Postfix(MatchmakerTimeHasCome __instance,
            MatchmakerTimeHasCome.TimeHasComeScreenController controller)
        {
            try
            {
                // A reconnect to a running ordinary raid is never the Arena flow.
                if (controller != null && !controller.IsReconnecting
                    && (ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid))
                    Apply(__instance._subCaption, true);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger?.LogWarning($"Arena deploying caption left native: {ex.Message}");
            }
        }
    }
}
