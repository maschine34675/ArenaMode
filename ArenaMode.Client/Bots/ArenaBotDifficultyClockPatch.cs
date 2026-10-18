using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ArenaMode.Bots;

/// <summary>
/// Decorate the final native generation request, after wave optimization/copies.
/// The server reads per-condition extension data; no polling or global timers.
/// </summary>
internal sealed class ArenaBotDifficultyClockPatch : ModulePatch
{
    private static volatile bool _ready;
    internal static bool IsReady => _ready;
    internal static void EnablePatch()
    {
        _ready = false;
        ModulePatch[] patches = { new ArenaBotDifficultyClockDemandPatch(), new ArenaBotDifficultyClockBackupPatch(), new ArenaBotDifficultyClockPatch() };
        try
        {
            foreach (var patch in patches) patch.Enable();
            _ready = true;
        }
        catch (Exception ex)
        {
            // A partial install must not discard caches while failing to attach
            // a clock. Keep all surviving hooks inert even if rollback itself fails.
            for (int i = patches.Length - 1; i >= 0; i--)
                try { patches[i].Disable(); } catch { }
            Plugin.ModLogger.LogWarning($"Arena timed bot difficulty unavailable; native generation retained: {ex.Message}");
        }
    }
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(ClientBackendSession), nameof(ClientBackendSession.LoadBots), new[] { typeof(List<CountTypeBotWave>) });

    internal sealed class TimedWave : CountTypeBotWave
    {
        [JsonProperty(ArenaDifficultyClock.WireKey)]
        public ArenaDifficultyClock Clock { get; }
        internal TimedWave(CountTypeBotWave original, ArenaDifficultyClock clock)
            : base(1, original.Role, original.Difficulty) => Clock = clock;
    }

    // CreateProfile calls the backend before its first await. Own only that
    // synchronous call, never an async ambient context or a reusable backend.
    [ThreadStatic] private static RequestScope _request;
    internal sealed class RequestScope
    {
        internal BotProfileClient Provider;
        internal object Match;
        internal object Mode;
        internal ArenaDifficultyClock Clock;
        internal WildSpawnType Role;
        internal BotDifficulty Difficulty;
    }
    internal sealed class RestoreScope
    {
        private readonly RequestScope _previous = _request;
        private bool _restored;
        internal void Restore()
        {
            if (_restored) return;
            _restored = true;
            _request = _previous;
        }
    }

    private static bool TryCurrent(BotProfileClient provider, out ArenaDifficultyClock clock, BotCreationData data = null)
    {
        clock = null;
        if (!ArenaSession.IsActive || provider == null) return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        if (!ArenaSession.IsExactActiveRaid(game, world) || game?.Status != GameStatus.Started
            || ((IBotGame)game).BotsController?.BotSpawner?._botCreator is not BotCreatorClient creator
            || !ReferenceEquals(creator._game, game) || !ReferenceEquals(creator._creatorProfile, provider)
            || (data != null && !ReferenceEquals(data._botCreator, creator))
            || ArenaMatchService.Current?.Mode is not TeamDeathmatchMode teams
            || Compat.PitFireTeamCompat.HasFrozenTeamRoster || Respawn.ArenaRespawnDriver.StandDown
            || !teams.TryGetDifficultyClock(out float elapsed, out int duration) || duration <= 0) return false;
        clock = new ArenaDifficultyClock
        {
            Version = ArenaDifficultyClock.CurrentVersion, ArmToken = ArenaServerClient.ActiveArmToken,
            TimeLimitSeconds = duration, PlayingElapsedSeconds = elapsed
        };
        return clock.IsValid();
    }

    private static bool IsPmc(WildSpawnType role)
        => role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR;

    private sealed class ArenaBotDifficultyClockDemandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotProfileClient), nameof(BotProfileClient.CreateProfile),
                new[] { typeof(BotCreationData), typeof(CancellationToken), typeof(bool) });

        [PatchPrefix]
        internal static void Prefix(BotProfileClient __instance, BotCreationData data,
            CancellationToken cancellationToken, bool withDelete, out RestoreScope __state)
        {
            __state = new RestoreScope();
            _request = null; // A nested foreign/stale demand must shadow its caller.
            if (!ArenaSession.IsActive || !_ready) return;
            try
            {
                if (!withDelete || cancellationToken.IsCancellationRequested || data == null || data.SpawnStopped
                    || data._profileData?.TryGetRole(out var role, out var difficulty) != true || !IsPmc(role)
                    || !TryCurrent(__instance, out var clock, data)) return;
                // Only unused matching cache entries. Never touch assigned profiles,
                // bodies, inventories or another provider. This also excludes native
                // startup preloads made from disabled boss waves before our match existed.
                __instance._simpleProfiles.RemoveAll(profile => profile?.Info?.Settings != null
                    && profile.Info.Settings.Role == role && profile.Info.Settings.BotDifficulty == difficulty);
                _request = new RequestScope { Provider = __instance, Match = ArenaMatchService.Current,
                    Mode = ArenaMatchService.Current?.Mode, Clock = clock, Role = role, Difficulty = difficulty };
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena timed bot demand unavailable; native generation retained: {ex.Message}");
            }
        }
        [PatchPostfix] internal static void Postfix(RestoreScope __state) => __state?.Restore();
        [PatchFinalizer] internal static Exception Finalizer(Exception __exception, RestoreScope __state)
        {
            __state?.Restore();
            return __exception;
        }
    }

    private sealed class ArenaBotDifficultyClockBackupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotProfileBackuploader), nameof(BotProfileBackuploader.AddProfileForBackup), new[] { typeof(BotCreationData) });
        [PatchPrefix]
        internal static bool Prefix(BotProfileBackuploader __instance, BotCreationData data)
        {
            if (!ArenaSession.IsActive || !_ready) return true;
            try
            {
                return data?._profileData?.TryGetRole(out var role, out _) != true || !IsPmc(role)
                    || __instance?._profileCreator is not BotProfileClient provider
                    || !ReferenceEquals(provider._backupLoader, __instance) || !TryCurrent(provider, out _, data);
            }
            catch { return true; }
        }
    }

    [PatchPrefix]
    private static void Prefix(ClientBackendSession __instance, ref List<CountTypeBotWave> conditions)
    {
        if (!ArenaSession.IsActive || !_ready || conditions == null || conditions.Count == 0) return;
        try
        {
            var request = _request;
            if (request == null || !ReferenceEquals(request.Provider._backEndSession, __instance)
                || !ReferenceEquals(request.Match, ArenaMatchService.Current)
                || !ReferenceEquals(request.Mode, ArenaMatchService.Current?.Mode)
                || !TryCurrent(request.Provider, out var currentClock)
                || currentClock.ArmToken != request.Clock.ArmToken) return;
            var copy = new List<CountTypeBotWave>(conditions.Count);
            foreach (var wave in conditions)
            {
                // Preserve unknown derived DTOs belonging to another mod. Native
                // CountTypeBotWave has exactly these three fields and no other state.
                if (wave == null || (wave.GetType() != typeof(CountTypeBotWave) && wave is not TimedWave)) return;
                if (wave.Limit <= 0 || wave.Role != request.Role || wave.Difficulty != request.Difficulty) return;
                copy.Add(new TimedWave(wave, request.Clock));
            }
            conditions = copy;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena bot difficulty clock unavailable; native request retained: {ex.Message}");
        }
    }
}
