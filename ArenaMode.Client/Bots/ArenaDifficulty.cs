using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// The client reader of the shared difficulty policy (docs/reviews/DIFFICULTY-MODEL-20261118.md).
///
/// The strength of the bots is one frozen setting, not a curve the raid climbs. The step is
/// read once on the arm path, acknowledged by the server and kept in <see cref="ArenaSession"/>
/// until the raid ends; everything here reads that value, so the AI level and the server's gear
/// band of one raid come from the same step whatever the F12 menu says meanwhile.
///
/// Only Gun Game has a progress below 1: its match clock over the map's ramp minutes (the same
/// per-map value the server's gear ramp uses). Every other mode is at progress 1 from the first
/// spawn request, so nothing here reads the kill streak, the match clock or the Team
/// Deathmatch clock outside the Gun Game branch.
///
/// Difficulty is baked into a bot when it spawns; nothing re-rates one that is already walking
/// around, so in Gun Game the climb reaches only the bots spawned after it.
/// </summary>
internal static class ArenaDifficulty
{
    /// <summary>The ramp length used when the server's per-map value is absent.</summary>
    public const int FallbackRampMinutes = 15;

    /// <summary>
    /// The frozen step of the active raid. Outside an armed or active raid (the F9 line,
    /// harness reads) there is nothing frozen and the live setting answers.
    /// </summary>
    public static ArenaDifficultyStep Step
        => ArenaSession.Difficulty ?? Settings.DifficultyStep?.Value ?? ArenaDifficultyStep.Maximum;

    /// <summary>True in the one mode whose difficulty builds up over the raid.</summary>
    public static bool IsGunGame => ArenaDifficultyPolicy.ScalesWithTime(ArenaSession.ActiveMode);

    /// <summary>
    /// 1 everywhere except Gun Game; there the match clock over the map's ramp minutes,
    /// clamped to 0..1. A missing match or a throwing read counts as the opening.
    /// </summary>
    public static float Progress
    {
        get
        {
            if (!IsGunGame)
                return 1f;

            var match = ArenaMatchService.Current;
            if (match == null)
                return 0f;

            try
            {
                var map = Comfort.Common.Singleton<GameWorld>.Instantiated
                    ? Comfort.Common.Singleton<GameWorld>.Instance.LocationId
                    : ArenaSession.ArmedMap;
                int ramp = ArenaClientConfig.GetBotTierRampMinutes(map, FallbackRampMinutes);
                return (float)ArenaDifficultyPolicy.Progress(ArenaSession.ActiveMode, match.Elapsed, ramp);
            }
            catch (Exception)
            {
                // A difficulty curve is never worth a thrown frame.
                return 0f;
            }
        }
    }

    /// <summary>The AI level to spawn at: the policy's row for the frozen step.</summary>
    public static BotDifficulty DifficultyFor(bool isPmc)
        => (BotDifficulty)(int)ArenaDifficultyPolicy.BotLevel(Step, isPmc, Progress);

    /// <summary>
    /// The PMC share to keep alive: the configured floor plus the policy's bonus (Gun Game by
    /// progress). A floor of zero is the panel slider's way of saying "stop forcing PMC top-ups
    /// at all"; adding a bonus on top would quietly override the one control a player reaches
    /// for when a raid has become too much.
    /// </summary>
    public static int PmcFloor(int configured)
        => configured <= 0 ? 0 : configured + ArenaDifficultyPolicy.PmcBonusAt(Step, Progress);

    /// <summary>The legacy refill wait, scaled by the policy's factor for the frozen step.</summary>
    public static float BurstCooldown(float configured)
        => configured * (float)ArenaDifficultyPolicy.LegacyCooldownFactor(Step);

    /// <summary>One line for the panel: the frozen step, in Gun Game with the build-up so far.</summary>
    public static string Describe()
        => IsGunGame ? $"{Step}, build-up {Progress * 100f:0}%" : Step.ToString();
}