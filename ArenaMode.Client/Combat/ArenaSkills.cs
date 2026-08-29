using ArenaMode.Config;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Combat;

/// <summary>
/// Runs the player at elite skill level for the duration of an arena raid.
///
/// The boost is applied through each skill's BUFF, never by writing its stored
/// progress. That choice is load-bearing:
///  - Progress above level 50 makes the game UNSUBSCRIBE the skill from its own
///    XP sources, and only a constructor ever subscribes them again. One arena
///    raid would have left every skill permanently deaf to its XP actions, so a
///    later normal raid could end with no skill progress at all and nothing in
///    the log to explain it.
///  - The buff field is not serialised, so it cannot travel anywhere.
///  - Its setter recomputes the buffs AND raises the level-up event the stamina
///    and weight limits listen to, without raising the level-CHANGED event that
///    would spray four dozen "skill increased" toasts across the screen.
///
/// Summary level lands on 51: that is the elite threshold, and the linear
/// bonuses are evaluated at min(level, 50) anyway. Deliberately not pushed to
/// the absolute 60 ceiling — that would raise the base movement speed, which
/// the arena movement patches already multiply.
///
/// The saved profile is never at risk (the server snapshots it when the arena
/// is armed and skips the post-raid processing), but the CLIENT shares one
/// profile object with the menu, so everything is put back at raid end.
/// </summary>
internal static class ArenaSkills
{
    /// <summary>Elite threshold: above 50, at the level where nothing more is gained.</summary>
    private const int EliteSummaryLevel = 51;

    private const float MasteryTickSeconds = 3f;

    private static readonly Dictionary<Skill, int> BuffSnapshot = new Dictionary<Skill, int>();
    private static readonly Dictionary<Mastering, float> MasterySnapshot = new Dictionary<Mastering, float>();

    /// <summary>
    /// Elite bonuses that the game can raise but never lower: their rules only
    /// exist for the elite case, so dropping back below the threshold leaves
    /// them standing. They have to be put back by hand.
    /// </summary>
    private static readonly List<Tuple<SkillManager.FloatBuff, float>> StickyEliteBuffs =
        new List<Tuple<SkillManager.FloatBuff, float>>();

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
            SnapshotStickyEliteBuffs(skills);

            var applied = 0;
            foreach (var skill in EnumerateSkills(skills))
            {
                if (skill == null)
                    continue;

                var boost = EliteSummaryLevel - skill.Level;
                if (boost <= 0)
                    continue;

                BuffSnapshot[skill] = skill.Buff;
                skill.Buff = boost;
                applied++;
            }

            ApplyMastering(skills);
            RefreshCachedConsumers(player);

            Plugin.ModLogger.LogInfo($"Arena: {applied} skills raised to elite for this raid.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena skill boost failed: {ex.Message}");
            Restore();
        }
    }

    public static void Restore()
    {
        if (BuffSnapshot.Count == 0 && MasterySnapshot.Count == 0 && StickyEliteBuffs.Count == 0)
            return;

        try
        {
            // The setter recomputes every rule, so this also walks the ordinary
            // buffs back down on its own.
            foreach (var pair in BuffSnapshot)
            {
                if (pair.Key != null)
                    pair.Key.Buff = pair.Value;
            }

            // AFTER the recompute: these are the ones it cannot undo.
            foreach (var sticky in StickyEliteBuffs)
            {
                if (sticky.Item1 != null)
                    sticky.Item1.Value = sticky.Item2;
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
            BuffSnapshot.Clear();
            MasterySnapshot.Clear();
            StickyEliteBuffs.Clear();
            _nextMasteryTickAt = 0f;
        }
    }

    /// <summary>
    /// Elite bonuses without a non-elite rule. Everything else reverts by itself
    /// when the level drops, either through a base rule or because the buff is a
    /// boolean whose default rule clears it.
    /// </summary>
    private static void SnapshotStickyEliteBuffs(SkillManager skills)
    {
        Snap(skills.ImmunityAvoidPoisonChance);
        Snap(skills.ImmunityAvoidMiscEffectsChance);
        Snap(skills.CharismaEliteScavCaseDiscount);
        Snap(skills.CharismaEliteFenceRepPenaltyReduction);
        Snap(skills.CharismaEliteAdditionalDailyQuests);
    }

    private static void Snap(SkillManager.FloatBuff buff)
    {
        if (buff != null)
            StickyEliteBuffs.Add(Tuple.Create(buff, buff.Value));
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
        if (BuffSnapshot.Count == 0 || !Settings.MaxSkills.Value || Time.time < _nextMasteryTickAt)
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
    /// The buff setter already refreshes stamina and weight limits through the
    /// level-up event. The weapon buff snapshot rides on the level-CHANGED event
    /// instead, which is deliberately not raised, so it is refreshed by hand.
    /// </summary>
    private static void RefreshCachedConsumers(Player player)
    {
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
