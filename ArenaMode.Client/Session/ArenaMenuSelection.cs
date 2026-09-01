using EFT;
using JsonType;
using System;

namespace ArenaMode.Session;

/// <summary>
/// A choice in one native raid-setup flow, never a persisted setting. The actual
/// raid stays PMC: only that post-raid path has the server's profile protection.
/// This is intentionally separate from ArenaSession's server-acknowledged latch.
/// </summary>
internal static class ArenaMenuSelection
{
    private static object _controller;
    private static RaidSettings _settings;
    private static LocationSettings.Location _previousLocation;
    private static EDateTime _previousTime;
    private static ELocationTransition _previousTransition;
    private static string _previousKey;
    private static Func<bool> _canStart;
    private static Func<Eligibility> _eligibility;
    private static Func<Eligibility, string> _freezeRoster;
    private static Action<string> _compatibilityCleanup;
    private static Func<bool> _teamArenaActive;

    /// <summary>
    /// Adapter-neutral result used by every point in the native flow. State is
    /// an opaque discovery snapshot; ArenaMenuSelection never needs to load the
    /// optional mod's types or understand its identities.
    /// </summary>
    internal sealed class Eligibility
    {
        public Eligibility(bool allowed, bool isTeam, string reason, object state = null)
        {
            Allowed = allowed;
            IsTeam = isTeam;
            Reason = reason ?? string.Empty;
            State = state;
        }

        public bool Allowed { get; }
        public bool IsTeam { get; }
        public string Reason { get; }
        public object State { get; }
    }

    public static bool IsSelected { get; private set; }
    public static bool RestoringSideScreen { get; set; }

    public static void Begin(object controller, RaidSettings settings, Func<bool> canStart = null)
    {
        // Back from the map screen reuses this controller. A new click on
        // "Escape from Tarkov" creates a new one, even with the same settings.
        if (!ReferenceEquals(_controller, controller))
        {
            Clear();
            _controller = controller;
            _settings = settings;
            _canStart = canStart;
            _eligibility = null;
            _freezeRoster = null;
        }
    }

    /// <summary>
    /// Installs a live eligibility source for the current native flow. The
    /// provider is deliberately re-run for display, click, claim and raid start;
    /// only the final result is frozen, immediately before server arming.
    /// </summary>
    public static void ConfigureEligibility(Func<Eligibility> eligibility,
        Func<Eligibility, string> freezeRoster)
    {
        _eligibility = eligibility;
        _freezeRoster = freezeRoster;
    }

    /// <summary>
    /// Process-lifetime cleanup hook registered by an optional adapter. It is
    /// deliberately separate from the screen-flow delegates: closing a screen
    /// after final validation must not make failure cleanup unreachable.
    /// </summary>
    public static void RegisterCompatibilityCleanup(Action<string> cleanup)
        => _compatibilityCleanup = cleanup;

    public static void RegisterTeamArenaQuery(Func<bool> teamArenaActive)
        => _teamArenaActive = teamArenaActive;

    public static bool CompatibilityTeamArenaActive => _teamArenaActive?.Invoke() == true;

    public static void ClearCompatibilityState(string reason)
        => _compatibilityCleanup?.Invoke(reason);

    public static void Select()
    {
        if (IsSelected || _settings == null)
            return;

        _previousLocation = _settings.SelectedLocation;
        _previousTime = _settings.SelectedDateTime;
        _previousTransition = _settings.transitionType;
        _previousKey = _settings.KeyId;
        IsSelected = true;
    }

    public static bool IsSelectedFor(RaidSettings settings)
        => IsSelected && settings != null && ReferenceEquals(_settings, settings) && settings.IsPmc;

    public static Eligibility CurrentEligibility
    {
        get
        {
            try
            {
                if (_eligibility != null)
                    return _eligibility() ?? new Eligibility(false, false,
                        "Arena could not inspect the current raid group.");

                return _canStart?.Invoke() == false
                    ? new Eligibility(false, false, "Arena is available for solo play only.")
                    : new Eligibility(true, false, string.Empty);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger?.LogWarning($"Arena eligibility check failed: {ex.Message}");
                return new Eligibility(false, false,
                    "Arena could not inspect the current raid group.");
            }
        }
    }

    public static bool CanStart => CurrentEligibility.Allowed;

    public static string CannotStartReason => CurrentEligibility.Reason;

    public static bool IsMapAvailable(string map)
        => Config.Settings.IsArenaMap(map) && ArenaClientConfig.IsMapEnabledOnServer(map);

    public static bool MatchesRequest(LocalRaidSettings settings)
        => MatchesRequest(settings, CurrentEligibility);

    /// <summary>
    /// Final request validation and roster freeze are one operation. The same
    /// freshly evaluated snapshot is checked and frozen before the server sees
    /// /raid-start, so a changed or mixed group cannot inherit an old decision.
    /// </summary>
    public static bool TryValidateAndFreezeRequest(LocalRaidSettings settings, out string reason)
    {
        var eligibility = CurrentEligibility;
        if (!eligibility.Allowed)
        {
            reason = eligibility.Reason;
            return false;
        }

        if (!MatchesRequest(settings, eligibility))
        {
            Plugin.ModLogger?.LogWarning(
                "Arena final request mismatch: " + DescribeRequest(settings, eligibility));
            reason = "Arena selection is no longer valid. Please select Arena and a supported map again.";
            return false;
        }

        reason = _freezeRoster?.Invoke(eligibility) ?? string.Empty;
        return string.IsNullOrEmpty(reason);
    }

    private static bool MatchesRequest(LocalRaidSettings settings, Eligibility eligibility)
        => IsSelectedFor(_settings)
           && eligibility?.Allowed == true
           && settings != null
           && settings.playerSide == ESideType.Pmc
           && settings.transitionType == ELocationTransition.Common
           && string.Equals(settings.location, _settings.SelectedLocation?.Id, StringComparison.OrdinalIgnoreCase)
           && IsMapAvailable(settings.location);

    /// <summary>
    /// Repair only the exact Common-to-None loss caused by EFT applying an
    /// offline RaidSettings clone whose Clone method omitted transitionType.
    /// The destination must be this flow's selected settings by reference, and
    /// both the captured pre-Apply value and clone value prove the known rewrite.
    /// </summary>
    public static bool RestoreArenaTransitionAfterCloneApply(
        RaidSettings destination,
        RaidSettings source,
        ELocationTransition transitionBeforeApply)
    {
        if (!IsSelectedFor(destination)
            || transitionBeforeApply != ELocationTransition.Common
            || source?.transitionType != ELocationTransition.None
            || destination.transitionType != ELocationTransition.None)
        {
            return false;
        }

        destination.transitionType = ELocationTransition.Common;
        return true;
    }

    private static string DescribeRequest(LocalRaidSettings settings, Eligibility eligibility)
        => $"selected={IsSelected}, menuSide={_settings?.Side.ToString() ?? "<none>"}, "
           + $"requestSide={settings?.playerSide.ToString() ?? "<none>"}, "
           + $"menuMap={_settings?.SelectedLocation?.Id ?? "<none>"}, "
           + $"requestMap={settings?.location ?? "<none>"}, "
           + $"transition={settings?.transitionType.ToString() ?? "<none>"}, "
           + $"team={eligibility?.IsTeam == true}, "
           + $"mapEnabled={settings != null && IsMapAvailable(settings.location)}";

    public static void Clear()
    {
        if (IsSelected && _settings != null)
        {
            // Only our menu changes are restored. Never alter the shared
            // LocationSettings database or restore anything into a profile.
            _settings.SelectedLocation = _previousLocation;
            _settings.SelectedDateTime = _previousTime;
            _settings.transitionType = _previousTransition;
            _settings.KeyId = _previousKey;
        }

        IsSelected = false;
        _previousLocation = null;
    }

    public static void EndFlow()
    {
        Clear();
        _settings = null;
        _controller = null;
        _canStart = null;
        _eligibility = null;
        _freezeRoster = null;
    }
}
