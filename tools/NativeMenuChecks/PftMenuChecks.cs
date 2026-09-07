using System;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Session;

internal static partial class Program
{
    private static void CheckLivePftModeRepair()
    {
        ConfigureAreaNegotiation();
        Select();
        Settings.GameMode.Value = "tactical";
        bool invalidRoster = false;
        var companions = new[] { "p1", "p2", "p3", "p4" };
        ArenaMenuSelection.Eligibility Evaluate(bool pendingMode)
        {
            var result = PitFireTeamSelectionPolicy.Evaluate(new PitFireTeamSelectionInput
            {
                Contract = PitFireTeamContractState.Compatible,
                CurrentPlayerAccountId = "local",
                GameMode = Settings.SelectedGameModeId,
                NativeGroupAccountIds = invalidRoster ? new[] { "local", "foreign" }
                    : new[] { "local", "p1", "p2", "p3", "p4" },
                SelectedPitAccountIds = companions,
                SelectedPitProfileIds = new[] { "profile1", "profile2", "profile3", "profile4" },
            }, pendingMode);
            return new ArenaMenuSelection.Eligibility(result.Allowed, result.IsTeam, result.Reason);
        }
        ArenaMenuSelection.ConfigureEligibility(() => Evaluate(false), null, () => Evaluate(true));
        var state = new ArenaModeSelectionState(Settings.GameMode.Value, true,
            mode => { Settings.GameMode.Value = mode; return true; });
        Settings.GameMode.Value = "gungame";
        Assert(!ArenaMenuSelection.CurrentEligibility.Allowed && !ArenaMenuSelection.CurrentEligibility.IsTeam,
            "The real strict PFT policy intentionally loses team identity when the saved mode is incompatible");
        var picker = ArenaMenuSelection.CurrentModeEligibility;
        Assert(picker.Allowed && picker.IsTeam && !ArenaMenuSelection.MatchesRequest(Request()),
            "Live picker eligibility retains the real roster but cannot authorize the incompatible final request");
        Assert(state.Synchronize(Settings.GameMode.Value, picker.Allowed && picker.IsTeam)
            && Settings.GameMode.Value == "tactical" && ArenaMenuSelection.MatchesRequest(Request()),
            "The real provider-to-menu path repairs F12 mode before strict final validation succeeds");
        invalidRoster = true;
        Assert(!ArenaMenuSelection.CurrentModeEligibility.Allowed && !ArenaMenuSelection.CurrentEligibility.Allowed,
            "Presentation mode tolerance cannot bypass changed/mixed native roster membership");
        ArenaMenuSelection.EndFlow();
        Assert(!ArenaMenuSelection.CurrentModeEligibility.IsTeam,
            "Leaving the native setup flow drops its live picker roster provider");
        ConfigureAreaNegotiation();
    }
}
