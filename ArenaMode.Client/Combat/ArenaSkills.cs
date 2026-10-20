using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
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
/// The server snapshots the profile when Arena arms and replaces native end
/// processing. The CLIENT also shares one profile object with the menu, so its
/// boosts must be restored at raid end, retaining failed writes for retry.
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
    private static SkillManager _boostedSkills;
    private static bool _boostActive, _restoreWarned;

    private static bool HasSnapshot => BuffSnapshot.Count != 0 || MasterySnapshot.Count != 0 || StickyEliteBuffs.Count != 0;

    private static bool IsActiveWorld(GameWorld world)
        => ArenaSession.IsActive && world != null && Singleton<GameWorld>.Instantiated
            && ReferenceEquals(Singleton<GameWorld>.Instance, world)
            && Singleton<AbstractGame>.Instantiated
            && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world);

    public static void Apply(GameWorld world)
    {
        if (!Settings.MaxSkills.Value || !IsActiveWorld(world))
            return;

        try
        {
            var player = world?.MainPlayer;
            var skills = player?.Skills;
            if (skills == null || !player.IsYourPlayer || player.IsAI)
                return;

            // A raid that ended abnormally must not stack on top of its own boost.
            Restore();
            if (HasSnapshot) return; // Never replace an unrecovered baseline with a boosted value.
            _boostedSkills = skills;
            _boostActive = true;
            SnapshotStickyEliteBuffs(skills);

            var applied = 0;
            foreach (var skill in EnumerateSkills(skills))
            {
                if (skill == null)
                    continue;

                var boost = EliteSummaryLevel - skill.Level;
                if (boost <= 0)
                    continue;

                // Effects Manager owns a separate, body-bound contribution.
                // Do not absorb it into the Arena baseline or erase it on boost.
                int effectsBonus = Compat.ArenaEffectsCompat.SkillBonusFor(skill);
                BuffSnapshot[skill] = skill.Buff - effectsBonus;
                skill.Buff = boost + effectsBonus;
                applied++;
            }

            ApplyMastering(skills);
            RefreshCachedConsumers(player);

            Plugin.ModLogger.LogDebug($"Arena: {applied} skills raised to elite for this raid.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena skill boost failed: {ex.Message}");
            Restore();
        }
    }

    public static void Restore()
    {
        _boostActive = false;
        _nextMasteryTickAt = Time.realtimeSinceStartup + MasteryTickSeconds;
        if (!HasSnapshot)
        {
            _boostedSkills = null;
            return;
        }

        Exception failure = null;
        // Serializable progression comes first. A throwing skill event must not
        // prevent its restoration, nor let later retries erase ordinary raid XP.
        foreach (var mastery in new List<Mastering>(MasterySnapshot.Keys))
        {
            try
            {
                mastery.Current = MasterySnapshot[mastery];
                MasterySnapshot.Remove(mastery);
            }
            catch (Exception ex) { failure ??= ex; }
        }

        foreach (var skill in new List<Skill>(BuffSnapshot.Keys))
        {
            try
            {
                skill.Buff = BuffSnapshot[skill] + Compat.ArenaEffectsCompat.SkillBonusFor(skill);
                BuffSnapshot.Remove(skill);
            }
            catch (Exception ex) { failure ??= ex; }
        }

        // Run AFTER every remaining skill recompute. Keep sticky baselines while
        // a failed skill setter could recompute them again during the next retry.
        for (int i = StickyEliteBuffs.Count - 1; i >= 0; i--)
        {
            var sticky = StickyEliteBuffs[i];
            try
            {
                if (sticky.Item1 != null) sticky.Item1.Value = sticky.Item2;
                if (BuffSnapshot.Count == 0) StickyEliteBuffs.RemoveAt(i);
            }
            catch (Exception ex) { failure ??= ex; }
        }

        var succeeded = failure == null && !HasSnapshot;
        if (!succeeded && !_restoreWarned)
        {
            _restoreWarned = true;
            Plugin.ModLogger.LogWarning($"Arena skill restore incomplete; boost stopped and remaining values retained for retry: {failure?.Message}");
        }

        if (!ArenaRestoreCommit.ShouldDropRetryState(succeeded))
            return;

        BuffSnapshot.Clear();
        MasterySnapshot.Clear();
        StickyEliteBuffs.Clear();
        _nextMasteryTickAt = 0f;
        _boostedSkills = null;
        _restoreWarned = false;
        Plugin.ModLogger.LogDebug("Arena: skills restored to their profile values.");
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
        if (!HasSnapshot || Time.realtimeSinceStartup < _nextMasteryTickAt)
            return;

        _nextMasteryTickAt = Time.realtimeSinceStartup + MasteryTickSeconds;

        try
        {
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (!_boostActive || !Settings.MaxSkills.Value || !IsActiveWorld(world))
            {
                Restore();
                return;
            }
            // A respawn may temporarily have no local body. Do not end the boost
            // for that gap, but never touch a different profile's skill manager.
            var player = world.MainPlayer;
            if (player == null || !player.IsYourPlayer || player.IsAI) return;
            if (!ReferenceEquals(player.Skills, _boostedSkills)) { Restore(); return; }
            ApplyMastering(_boostedSkills);
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
