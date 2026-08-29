using ArenaMode.Config;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>
/// Runs the player at elite skill level for the duration of an arena raid.
///
/// The profile on disk is never at risk — the server snapshots it when the
/// arena is armed and skips the post-raid processing entirely. But the CLIENT
/// keeps ONE Profile object for the whole session and the raid shares it with
/// the menu, so maxed skills would leak straight into the next normal raid.
/// That is what the snapshot and restore here are for.
///
/// Level 51 is the ceiling that matters: progress is clamped at 5100 (= level
/// 51), the linear bonuses are evaluated at min(level, 50), and 51 is exactly
/// the elite threshold. Anything above buys nothing.
/// </summary>
internal static class ArenaSkills
{
    /// <summary>Progress value for level 51 — the game's own clamp ceiling.</summary>
    private const float EliteCurrent = 5100f;

    private sealed class SkillSnapshot
    {
        public float Current;
        public DateTime LastCall;
    }

    private static readonly Dictionary<Skill, SkillSnapshot> Snapshot = new Dictionary<Skill, SkillSnapshot>();
    private static readonly Dictionary<Mastering, float> MasterySnapshot = new Dictionary<Mastering, float>();

    private const float MasteryTickSeconds = 3f;

    private static float _nextMasteryTickAt;

    public static void Apply(GameWorld world)
    {
        if (!Settings.MaxSkills.Value)
            return;

        try
        {
            var player = world?.MainPlayer;
            var skills = player?.Skills;
            if (skills == null)
                return;

            // A raid that ended abnormally must not stack on top of its own boost.
            Restore();

            var applied = 0;
            foreach (var skill in EnumerateSkills(skills))
            {
                if (skill == null)
                    continue;

                Snapshot[skill] = new SkillSnapshot { Current = skill.Current, LastCall = skill.LastCall };

                // Written directly rather than through SetLevel: that path raises
                // the level-up event for every single skill, which would bury the
                // screen under four dozen "skill increased" toasts.
                skill.Current = EliteCurrent;
                skill.SetPointsEarnedInSession(0f);
                skill.UpdateRules();
                applied++;
            }

            ApplyMastering(skills);
            RefreshCachedConsumers(player);

            Plugin.ModLogger.LogInfo($"Arena: {applied} skills set to elite for this raid.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena skill boost failed: {ex.Message}");
            Restore();
        }
    }

    public static void Restore()
    {
        if (Snapshot.Count == 0 && MasterySnapshot.Count == 0)
            return;

        try
        {
            foreach (var pair in Snapshot)
            {
                var skill = pair.Key;
                if (skill == null)
                    continue;

                skill.Current = pair.Value.Current;
                skill.LastCall = pair.Value.LastCall;
                skill.UpdateRules();
            }

            foreach (var pair in MasterySnapshot)
            {
                if (pair.Key != null)
                    pair.Key.Current = pair.Value;
            }

            Plugin.ModLogger.LogInfo("Arena: skills restored to their profile values.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena skill restore failed: {ex.Message}");
        }
        finally
        {
            Snapshot.Clear();
            MasterySnapshot.Clear();
            _nextMasteryTickAt = 0f;
        }
    }

    /// <summary>
    /// The displayed skills PLUS RecoilControl, which sits in neither of the
    /// game's own skill arrays and still feeds a live gunplay buff. The two
    /// bot-only entries at the head of SkillManager.Skills are deliberately
    /// skipped — one silences footsteps, the other rewrites reload speed.
    /// </summary>
    private static IEnumerable<Skill> EnumerateSkills(SkillManager skills)
    {
        var displayList = skills.DisplayList;
        if (displayList != null)
        {
            foreach (var skill in displayList)
                yield return skill;
        }

        yield return skills.RecoilControl;
    }

    /// <summary>
    /// Kits hand out weapons the profile has never fired, so without this the
    /// arena would punish every kit weapon with unmastered recoil and handling.
    /// Entries only exist once a weapon has been drawn, hence the periodic re-run.
    /// </summary>
    private static void ApplyMastering(SkillManager skills)
    {
        if (skills.Mastering == null)
            return;

        foreach (var pair in skills.Mastering)
        {
            var mastering = pair.Value;
            if (mastering == null || MasterySnapshot.ContainsKey(mastering))
                continue;

            MasterySnapshot[mastering] = mastering.Current;
            mastering.Current = mastering.Lvl1 + mastering.Lvl2;
        }
    }

    /// <summary>
    /// Catches weapons drawn after the raid started (kit swaps, respawns): a
    /// mastering entry only exists once its weapon has been in hand.
    /// </summary>
    public static void Tick()
    {
        if (Snapshot.Count == 0 || !Settings.MaxSkills.Value || Time.time < _nextMasteryTickAt)
            return;

        _nextMasteryTickAt = Time.time + MasteryTickSeconds;

        try
        {
            var skills = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer?.Skills
                : null;

            if (skills != null)
                ApplyMastering(skills);
        }
        catch (Exception)
        {
            // Cosmetic at worst — a kit weapon keeps vanilla handling.
        }
    }

    /// <summary>
    /// Most consumers read the buffs live, but stamina and weight limits, armor
    /// speed penalties and the weapon buff snapshot are cached — and the game's
    /// own refresh rides on the level-up events this deliberately does not raise.
    /// </summary>
    private static void RefreshCachedConsumers(Player player)
    {
        TryRefresh(() => (player.Physical as Physical)?.OnEnduranceLevelUp(), "stamina limits");
        TryRefresh(() => player.Physical?.OnWeightLimitsUpdated(), "weight limits");
        TryRefresh(player.RecalculateEquipmentParams, "equipment params");
        TryRefresh(() => (player.HandsController as Player.FirearmController)?.SyncWithCharacterSkills(),
            "weapon buffs");
    }

    private static void TryRefresh(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena skill refresh ({what}) failed: {ex.Message}");
        }
    }
}
