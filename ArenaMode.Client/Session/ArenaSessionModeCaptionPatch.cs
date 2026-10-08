using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
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
/// the exact <c>RaidSettings</c> of that screen) and never throws: any doubt
/// leaves the native text. Outside the Arena flow nothing is touched and so
/// nothing needs restoring; only a caption this class overwrote is ever put
/// back (side screen, toggle switched off again).
///
/// The deploying screen deliberately does NOT also accept <see cref="ArenaSession.
/// ArmedForNextRaid"/>: <c>TarkovApplication.LocalGameMatching</c> shows it
/// (decompile line 2525) before <c>LocalGameCreate</c> reaches
/// <c>LocalRaidStarted</c> (line 2991), which is where <c>LocalRaidStartedPatch</c>
/// arms, so a set flag at this moment can only be a stale arm left by an earlier
/// aborted load, and trusting it would label an ordinary raid "ARENA ZONE". The
/// menu selection is the reliable signal: the side, location, accept and
/// deploying controllers all carry one <c>RaidSettings</c> instance
/// (<c>TarkovApplication._raidSettings</c>, passed to <c>MainMenuShowOperation</c>
/// and to <c>TimeHasComeScreenController</c>).
///
/// Which screens the Arena flow shows depends on SkipPreRaidScreensPatch: with
/// the default <c>SkipPreRaidScreens</c> a solo raid skips Game Settings,
/// Insurance and Confirmation, but Confirmation (the accept screen) stays in
/// every Arena flow while PitFireTeam is installed. Game Settings, Insurance
/// and the other <c>MatchmakerEftScreen</c> subclasses are NOT patched: with
/// <c>SkipPreRaidScreens</c> off they show the native "PVE ZONE" inside the
/// Arena flow (known gap, NATIVE-MENU-REVIEW.md, section 2026-10-08).
/// </summary>
internal static class ArenaSessionModeCaptionPatch
{
    // The native text of every caption this class overwrote, so switching the
    // ARENA toggle off puts back exactly what EFT wrote (any language). The
    // rules live in the Unity-free ledger, which tools/NativeMenuChecks runs.
    private static readonly ArenaSessionModeCaptionLedger<TextMeshProUGUI> Ledger = new();

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
            // Unity's null: a destroyed label counts as missing.
            if (caption == null)
                return;
            Ledger.Apply(caption, arena, label => label.text, (label, text) => label.text = text);
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
                // (Operators end the line so a source mutant can flip exactly one.)
                // ArmedForNextRaid is not consulted: it is set after this screen.
                if (controller != null && !controller.IsReconnecting &&
                    ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))
                    Apply(__instance._subCaption, true);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger?.LogWarning($"Arena deploying caption left native: {ex.Message}");
            }
        }
    }
}
