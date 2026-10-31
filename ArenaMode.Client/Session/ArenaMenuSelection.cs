using EFT;
using JsonType;
using System;
using ArenaMode.Shared;

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
    private static Func<Eligibility> _modeEligibility;
    private static Func<Eligibility, string> _freezeRoster;
    private static Action<string> _compatibilityCleanup;
    private static Func<bool> _teamArenaActive;
    private static Func<string> _compatibilityAdmission;
    private static bool _suspended;

    // Display pass state (BeginDisplayPass). Main thread only; the verdicts live exactly as long as
    // the outermost pass of the thread that opened it.
    private static int _displayPassDepth, _displayPassThread;
    private static bool _passHasCompatibility;
    private static string _passCompatibility, _passEligibilityMode, _passModeEligibilityMode;
    private static Eligibility _passEligibility, _passModeEligibility;

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
            _modeEligibility = null;
            _freezeRoster = null;
            ForgetDisplayPassVerdicts();
        }
        else if (_suspended)
            ResumeAfterMenu(settings);
    }

    /// <summary>
    /// A queued menu is a detour, not a new raid flow. Retain only its exact
    /// settings/controller and original restore values; all admission remains off.
    /// The caller separately revokes the server arm and any frozen roster.
    /// </summary>
    internal static void SuspendForMenu()
    {
        if (!IsSelected || _settings == null) return;
        _suspended = true;
        IsSelected = false;
    }

    /// <summary>Only the same native setup may resume, after fresh admission.</summary>
    internal static bool ResumeAfterMenu(RaidSettings settings)
    {
        if (!_suspended) return IsSelectedFor(settings);
        if (settings == null || !ReferenceEquals(_settings, settings) || !settings.IsPmc
            || !CurrentModeEligibility.Allowed)
        {
            EndFlow();
            return false;
        }
        _suspended = false;
        IsSelected = true;
        return true;
    }

    /// <summary>
    /// Installs a live eligibility source for the current native flow. The
    /// provider is deliberately re-run for display, click, claim and raid start;
    /// only the final result is frozen, immediately before server arming.
    /// </summary>
    public static void ConfigureEligibility(Func<Eligibility> eligibility,
        Func<Eligibility, string> freezeRoster, Func<Eligibility> modeEligibility = null)
    {
        _eligibility = eligibility;
        _modeEligibility = modeEligibility;
        _freezeRoster = freezeRoster;
        // A verdict of the old providers must not answer for the new ones, even inside a pass.
        ForgetDisplayPassVerdicts();
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

    // Process lifetime, not screen lifetime: stale selections must be checked
    // again at claim/start, and EndFlow must not drop optional-mod protection.
    public static void RegisterCompatibilityAdmission(Func<string> admission)
    {
        _compatibilityAdmission = admission;
        ForgetDisplayPassVerdicts();
    }

    /// <summary>
    /// Opens one synchronous display pass (2026-10-31). While it is open, CompatibilityRejection,
    /// CurrentEligibility and CurrentModeEligibility each evaluate on first use and answer every
    /// later read of the same pass from that verdict. The map screen's refresh, driven every 0.2 s,
    /// read the optional-mod admission four times (each eligibility directly and again inside the
    /// side screen's provider), and every read walked all optional-mod adapters: about 12 ms each
    /// on GPU-2, a 45-130 ms frame per tick.
    /// <para>Outside a pass nothing is memoised: every native click, the claim, the selection
    /// setters, the final roster freeze and the raid start evaluate fresh. That keeps the 26.10
    /// contract of ArenaMapAvailabilitySnapshot ("one synchronous display pass, not an admission
    /// cache"). A pass may wrap synchronous display-only work, never a path that persists a user's
    /// selection, claims, freezes or starts. Open it with <c>using</c> so it ends in a finally; a
    /// nested pass shares the outer verdicts and they are dropped when the outermost one ends.
    /// Another thread neither consumes nor fills them, and a pass begun there is a no-op.</para>
    /// </summary>
    internal static IDisposable BeginDisplayPass()
    {
        int thread = Environment.CurrentManagedThreadId;
        if (_displayPassDepth > 0 && thread != _displayPassThread) return NoDisplayPass.Instance;
        if (_displayPassDepth++ == 0) _displayPassThread = thread;
        return new DisplayPass();
    }

    private static bool InDisplayPass
        => _displayPassDepth > 0 && Environment.CurrentManagedThreadId == _displayPassThread;

    private static void ForgetDisplayPassVerdicts()
    {
        _passHasCompatibility = false;
        _passCompatibility = _passEligibilityMode = _passModeEligibilityMode = null;
        _passEligibility = _passModeEligibility = null;
    }

    private sealed class DisplayPass : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended) return;
            _ended = true;
            if (--_displayPassDepth == 0) ForgetDisplayPassVerdicts();
        }
    }

    private sealed class NoDisplayPass : IDisposable
    {
        internal static readonly NoDisplayPass Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// DeveloperMode-only call-time accumulator (count, average, maximum in ms) for one code
    /// path; the map-screen refresh probe (ArenaLocationSelectionPatch.RefreshTiming) prints
    /// and resets it every five seconds. Begin returns 0 while the mode is off and End ignores
    /// a 0 start, so an instrumented path pays one setting read when nothing is measured.
    /// Nested here, gated by a delegate the plugin installs (Enabled), and registering
    /// every instance, so that harnesses linking this file compile without extra links
    /// and the probe reports all timers without naming their owners.
    /// </summary>
    internal sealed class CallTiming
    {
        private static readonly double TicksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private static readonly System.Collections.Generic.List<CallTiming> All = new();
        /// <summary>Installed by Plugin: DeveloperMode on. Null (harnesses, early start) means off.</summary>
        internal static Func<bool> Enabled;
        private readonly string _name;
        private int _count, _total;
        private double _sum, _max;

        internal CallTiming(string name) { _name = name; lock (All) All.Add(this); }

        internal static bool IsEnabled => Enabled?.Invoke() == true;

        internal static long Begin()
            => IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        /// <summary>Every timer that measured something since the last report, joined with "; ", then reset.</summary>
        internal static string ReportAllAndReset()
        {
            var parts = new System.Text.StringBuilder();
            lock (All)
                foreach (var timer in All)
                {
                    string text = timer.ReportAndReset();
                    if (text.Length == 0) continue;
                    parts.Append(parts.Length == 0 ? string.Empty : "; ").Append(text);
                }
            return parts.ToString();
        }

        internal void End(long started)
        {
            if (started == 0) return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * TicksToMs;
            _count++;
            _total++;
            _sum += ms;
            if (ms > _max) _max = ms;
        }

        /// <summary>Every call measured since start, never reset by the probe's report (harnesses
        /// read it with Enabled set, so a five-second report cannot fall between two readings).</summary>
        internal int TotalCalls => _total;

        /// <summary>"name n calls avg a/max m" and reset; empty when nothing was measured.</summary>
        internal string ReportAndReset()
        {
            if (_count == 0) return string.Empty;
            string text = $"{_name} {_count} calls avg {_sum / _count:F1}/max {_max:F1}";
            _count = 0; _sum = 0; _max = 0;
            return text;
        }
    }

    // The two per-refresh costs of the map screen, measured in DeveloperMode only. "compat" counts
    // real evaluations only (one per display pass); the two "reused" timers count the reads a display
    // pass answered from its first verdict, so the probe line shows what the pass absorbed.
    internal static readonly CallTiming CompatibilityTiming = new("compat");
    internal static readonly CallTiming RosterTiming = new("pft");
    internal static readonly CallTiming CompatibilityReuseTiming = new("compat reused");
    internal static readonly CallTiming EligibilityReuseTiming = new("eligibility reused");

    public static string CompatibilityRejection
    {
        get
        {
            if (_passHasCompatibility && InDisplayPass)
            {
                CompatibilityReuseTiming.End(CallTiming.Begin());
                return _passCompatibility;
            }
            string rejection;
            long started = CallTiming.Begin();
            try { rejection = _compatibilityAdmission?.Invoke(); }
            catch (Exception)
            {
                rejection = "Arena could not verify optional-mod compatibility. Restart the game and check the log.";
            }
            finally { CompatibilityTiming.End(started); }
            if (InDisplayPass) { _passCompatibility = rejection; _passHasCompatibility = true; }
            return rejection;
        }
    }

    public static bool CompatibilityTeamArenaActive => _teamArenaActive?.Invoke() == true;

    internal static string ModeAvailabilityRejection(string mode)
    {
        if (mode != Modes.ArenaModeCatalog.DominationId || Bots.DominationObjectiveBridge.Available) return null;
        const string need = "Domination needs BigBrain 1.5.0 or newer (separate download).";
        // The companion DLL always ships; with BigBrain loaded the cause is a too old
        // version or a failed registration, which the BepInEx log names.
        return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain")
            ? need + " BigBrain is loaded, but Arena's companion did not start; see the BepInEx log."
            : need;
    }

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
        // Preload while the player is still choosing a map. The native loading
        // screens then provide ample time for START without loading audio for a
        // process that only runs ordinary raids.
        Audio.AnnouncerService.EnsureLoaded();
    }

    public static bool IsSelectedFor(RaidSettings settings)
        => IsSelected && settings != null && ReferenceEquals(_settings, settings) && settings.IsPmc;

    // Inside a display pass the strict and the roster-only verdicts are kept per selected mode:
    // ScreenState.Refresh repairs a saved PFT mode between its two reads (Modes.Synchronize) and the
    // strict PFT policy depends on that mode, so a repair inside the pass is seen by the next read.
    // Compatibility does not depend on the mode and is kept for the whole pass.
    public static Eligibility CurrentEligibility
    {
        get
        {
            string mode = InDisplayPass ? Config.Settings.SelectedGameModeId : null;
            if (mode != null && _passEligibility != null && _passEligibilityMode == mode)
            {
                EligibilityReuseTiming.End(CallTiming.Begin());
                return _passEligibility;
            }
            var eligibility = EvaluateEligibility();
            if (mode != null && InDisplayPass) { _passEligibility = eligibility; _passEligibilityMode = mode; }
            return eligibility;
        }
    }

    private static Eligibility EvaluateEligibility()
    {
        try
        {
            var rejection = CompatibilityRejection;
            if (!string.IsNullOrEmpty(rejection)) return new Eligibility(false, false, rejection);
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

    public static bool CanStart => CurrentEligibility.Allowed;

    // The strict PFT policy deliberately rejects a wrong saved mode with
    // IsTeam=false. The picker needs a fresh roster-only decision so it can
    // repair that mode; this presentation result never authorizes raid start.
    public static Eligibility CurrentModeEligibility
    {
        get
        {
            if (_modeEligibility == null) return CurrentEligibility;
            string mode = InDisplayPass ? Config.Settings.SelectedGameModeId : null;
            if (mode != null && _passModeEligibility != null && _passModeEligibilityMode == mode)
            {
                EligibilityReuseTiming.End(CallTiming.Begin());
                return _passModeEligibility;
            }
            var eligibility = EvaluateModeEligibility();
            if (mode != null && InDisplayPass) { _passModeEligibility = eligibility; _passModeEligibilityMode = mode; }
            return eligibility;
        }
    }

    private static Eligibility EvaluateModeEligibility()
    {
        try
        {
            var rejection = CompatibilityRejection;
            if (!string.IsNullOrEmpty(rejection)) return new Eligibility(false, false, rejection);
            return _modeEligibility() ?? new Eligibility(false, false,
                "Arena could not inspect the current raid group.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena mode eligibility check failed: {ex.Message}");
            return new Eligibility(false, false, "Arena could not inspect the current raid group.");
        }
    }

    public static bool IsMapConfigured(string map)
        => Config.Settings.IsArenaMap(map) && ArenaClientConfig.IsMapEnabledOnServer(map)
           && (!ArenaMapCatalog.IsOptionalMap(map)
               || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ArenaMapCatalog.ArenaMapPortPluginId));

    // Discovery must not depend on a previous saved mode, but a configured map
    // still needs at least one supported PFT format (Equator has neither).
    // Actual roster/mode eligibility remains the later selection/start guard.
    public static bool HasConfiguredMap(bool pitFireTeam)
        => ArenaMapCatalog.AnyMapAvailable(map => IsMapConfigured(map) && (!pitFireTeam
            || ArenaAreaSelectionState.Create(map, Modes.ArenaModeCatalog.TacticalId, true,
                ArenaAreaCatalog.FullMapId, 5).Available
            || ArenaAreaSelectionState.Create(map, Modes.ArenaModeCatalog.ZombieId, true,
                ArenaAreaCatalog.FullMapId, 5).Available));

    public static bool IsMapAvailable(string map) => IsMapAvailable(map, CurrentEligibility);

    internal static bool IsMapAvailable(string map, Eligibility eligibility)
        => IsMapConfigured(map) && eligibility?.Allowed == true
           && ModeAvailabilityRejection(Config.Settings.SelectedGameModeId) == null
           && ArenaAreaCatalog.TryResolve(
               Config.Settings.TeamDeathmatchArea?.Value,
               map, Config.Settings.SelectedGameModeId, eligibility.IsTeam, out _, out _);

    public static ArenaAreaSelectionState AreaSelection(string map)
        => AreaSelection(map, CurrentEligibility);

    // Display passes may share a single freshly captured decision across all
    // rows. Public click/claim/start entry points still evaluate CurrentEligibility.
    internal static ArenaAreaSelectionState AreaSelection(string map, Eligibility eligibility)
    {
        // Keep entering the Arena picker possible when a saved mode lost its
        // optional companion. Only that mode's maps/start are blocked, so the
        // player can select another working format without editing F12.
        return ArenaAreaSelectionState.Create(IsMapConfigured(map) && eligibility.Allowed
                && ModeAvailabilityRejection(Config.Settings.SelectedGameModeId) == null ? map : null,
            Config.Settings.SelectedGameModeId, eligibility.IsTeam, Config.Settings.TeamDeathmatchArea?.Value,
            Config.Settings.TeamDeathmatchSize?.Value ?? 5, Config.Settings.AreaTeamSize?.Value ?? 0);
    }

    public static bool IsMapDiscoverable(string map) => AreaSelection(map).Available;

    // Repair only during this exact native setup flow, never at claim/start.
    // The caller supplies the incoming map: RaidSettings still holds the old
    // location in the native SelectedLocation prefix, including time changes.
    public static bool TryPrepareMapArea(RaidSettings owner, string map)
    {
        if (!IsSelectedFor(owner)) return false;
        var proposal = AreaSelection(map);
        return proposal.Available && Config.Settings.TrySelectArea(proposal.SelectedId)
            && IsMapAvailable(map);
    }

    internal static bool TryPrepareMapArea(RaidSettings owner, string map, Eligibility eligibility)
    {
        if (!IsSelectedFor(owner)) return false;
        var proposal = AreaSelection(map, eligibility);
        return proposal.Available && Config.Settings.TrySelectArea(proposal.SelectedId)
            && IsMapAvailable(map, eligibility);
    }

    public static bool TrySelectArea(RaidSettings owner, string map, string areaId)
    {
        if (!IsSelectedFor(owner)) return false;
        var proposal = AreaSelection(map);
        bool offered = false;
        foreach (var option in proposal.Options) if (option.Id == areaId) offered = true;
        return offered && Config.Settings.TrySelectArea(areaId) && IsMapAvailable(map);
    }

    public static bool MatchesRequest(LocalRaidSettings settings)
        => MatchesRequest(settings, CurrentEligibility);

    public static bool TrySelectAreaTeamSize(RaidSettings owner, string map, string areaId, int size)
    {
        if (!IsSelectedFor(owner) || !string.Equals(map, owner.SelectedLocation?.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Config.Settings.TeamDeathmatchArea?.Value, areaId, StringComparison.Ordinal)) return false;
        var proposal = AreaSelection(map);
        if (!proposal.Available || !proposal.HasTeamSizeChoice || proposal.SelectedId != areaId) return false;
        bool offered = false;
        foreach (int option in proposal.TeamSizeOptions) if (option == size) offered = true;
        return offered && Config.Settings.TrySelectAreaTeamSize(size) && IsMapAvailable(map);
    }

    /// <summary>
    /// Final request validation and roster freeze are one operation. The same
    /// freshly evaluated snapshot is checked and frozen before the server sees
    /// /raid-start, so a changed or mixed group cannot inherit an old decision.
    /// </summary>
    public static bool TryValidateAndFreezeRequest(LocalRaidSettings settings, out string reason)
    {
        // Recheck immediately before any roster freeze/server arm. A saved
        // selection or direct native start cannot bypass the disabled menu row.
        reason = ModeAvailabilityRejection(Config.Settings.SelectedGameModeId);
        if (!string.IsNullOrEmpty(reason)) return false;
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
           && IsMapAvailable(settings.location, eligibility)
           && HasValidAreaTeamSize(settings.location, eligibility);

    private static bool HasValidAreaTeamSize(string map, Eligibility eligibility)
    {
        var mode = Config.Settings.SelectedGameModeId;
        var id = Config.Settings.TeamDeathmatchArea?.Value;
        return ArenaAreaCatalog.TryResolve(id, map, mode, eligibility.IsTeam, out var area, out _)
            && ArenaAreaCatalog.TryResolveTeamSize(area, mode, eligibility.IsTeam,
                area?.TeamStarts != null && mode == Modes.ArenaModeCatalog.TeamDeathmatchId
                    ? Config.Settings.AreaTeamSize?.Value : null, out _, out _);
    }

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
        if ((IsSelected || _suspended) && _settings != null)
        {
            // Only our menu changes are restored. Never alter the shared
            // LocationSettings database or restore anything into a profile.
            _settings.SelectedLocation = _previousLocation;
            _settings.SelectedDateTime = _previousTime;
            _settings.transitionType = _previousTransition;
            _settings.KeyId = _previousKey;
        }

        IsSelected = false;
        _suspended = false;
        _previousLocation = null;
    }

    public static void EndFlow()
    {
        Clear();
        _settings = null;
        _controller = null;
        _canStart = null;
        _eligibility = null;
        _modeEligibility = null;
        _freezeRoster = null;
        ForgetDisplayPassVerdicts();
    }
}
