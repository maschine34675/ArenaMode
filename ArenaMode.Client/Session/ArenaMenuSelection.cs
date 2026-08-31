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
        }
    }

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

    public static bool CanStart => _canStart?.Invoke() != false;

    public static bool IsMapAvailable(string map)
        => Config.Settings.IsArenaMap(map) && ArenaClientConfig.IsMapEnabledOnServer(map);

    public static bool MatchesRequest(LocalRaidSettings settings)
        => IsSelectedFor(_settings)
           && CanStart
           && settings != null
           && settings.playerSide == ESideType.Pmc
           && settings.transitionType == ELocationTransition.Common
           && string.Equals(settings.location, _settings.SelectedLocation?.Id, StringComparison.OrdinalIgnoreCase)
           && IsMapAvailable(settings.location);

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
    }
}
