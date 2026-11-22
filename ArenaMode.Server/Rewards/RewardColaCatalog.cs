using System.Collections.Frozen;

namespace ArenaMode.Server.Rewards;

// Reviewed data from https://github.com/Yendaxddd/TarkovaCola/tree/2.2.1
// Commit 163bb9b1192d050b9e0da5aada8686e8c43877dd; Server/Achievements*.cs,
// Server/Mod.cs, Client/PerkDefs.cs and Client/Synergy.cs. No artwork or audio.
// MIT License
//
// Copyright (c) 2026 yendaxddd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
internal sealed record RewardColaItem(string Template, int Count);
internal sealed record RewardColaAchievement(string Id, string Key, string ServerKey, IReadOnlyList<RewardColaItem> Items);
internal sealed record RewardColaChallenge(string Perk, int Goal);

internal static class RewardColaCatalog
{
    internal const string Version = "tarkova-cola-2.2.1";
    internal const string SourceCommit = "163bb9b1192d050b9e0da5aada8686e8c43877dd";
    internal const string GpCoin = "5d235b4d86f7742e017bc88a";
    internal static readonly FrozenSet<string> Perks = new[]
        { "speedcola", "staminup", "juggernog", "phdflopper", "widowswine", "deadshot" }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenDictionary<string, RewardColaChallenge> Challenges =
        new Dictionary<string, RewardColaChallenge>(StringComparer.Ordinal)
        {
            ["a_quick"] = new("speedcola", 3),
            ["a_bolt"] = new("speedcola", 5),
            ["a_rush"] = new("speedcola", 4),
            ["a_steady"] = new("speedcola", 4),
            ["a_check"] = new("speedcola", 20),
            ["a_mags"] = new("speedcola", 20),
            ["a_count"] = new("speedcola", 15),
            ["s_slot"] = new("speedcola", 1),
            ["st_rush"] = new("staminup", 1),
            ["st_shooter"] = new("staminup", 15),
            ["st_wind"] = new("staminup", 5),
            ["st_feet"] = new("staminup", 5000),
            ["st_recover"] = new("staminup", 10),
            ["st_land"] = new("staminup", 3),
            ["st_lungs"] = new("staminup", 20),
            ["st_slot"] = new("staminup", 1),
            ["jg_patch"] = new("juggernog", 10),
            ["jg_plates"] = new("juggernog", 60),
            ["jg_bastion"] = new("juggernog", 5),
            ["jg_skull"] = new("juggernog", 1500),
            ["jg_thick"] = new("juggernog", 5),
            ["jg_bones"] = new("juggernog", 5),
            ["jg_keep"] = new("juggernog", 15),
            ["jg_slot"] = new("juggernog", 1),
            ["pf_absorb"] = new("phdflopper", 500),
            ["pf_shock"] = new("phdflopper", 5),
            ["pf_soles"] = new("phdflopper", 10),
            ["pf_stop"] = new("phdflopper", 300),
            ["pf_ears"] = new("phdflopper", 5),
            ["pf_terrain"] = new("phdflopper", 5000),
            ["pf_slot"] = new("phdflopper", 1),
            ["ww_spin"] = new("widowswine", 25),
            ["ww_fuse"] = new("widowswine", 1),
            ["ww_burst"] = new("widowswine", 3),
            ["ww_venom"] = new("widowswine", 5),
            ["ww_quick"] = new("widowswine", 10),
            ["ww_sticky"] = new("widowswine", 10),
            ["ww_bite"] = new("widowswine", 10),
            ["ww_slot"] = new("widowswine", 1),
            ["dd_aim"] = new("deadshot", 20),
            ["dd_pen"] = new("deadshot", 15),
            ["dd_sky"] = new("deadshot", 1500),
            ["dd_quick"] = new("deadshot", 10),
            ["dd_steady"] = new("deadshot", 25),
            ["dd_head"] = new("deadshot", 15),
            ["dd_still"] = new("deadshot", 10),
            ["dd_slot"] = new("deadshot", 1),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // Preserve the legacy catalog and receipt bytes. The 3.0.1 revision changed
    // a_bolt from 5 to 10 and added three complete perk trees plus pf_drop.
    internal const string CurrentVersion = "tarkova-cola-3.0.1";
    internal const string CurrentSourceCommit = "8d4294d4410425bad27f1d019db4f7dc8fa17631";
    internal static readonly FrozenSet<string> CurrentPerks = Perks.Concat(
        new[] { "bandolier", "vultureaid", "meleemacchiato" }).ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenDictionary<string, RewardColaChallenge> CurrentChallenges =
        CreateCurrentChallenges();

    private static FrozenDictionary<string, RewardColaChallenge> CreateCurrentChallenges()
    {
        var values = Challenges.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        values["a_bolt"] = new("speedcola", 10);
        values["pf_drop"] = new("phdflopper", 15);
        values["bb_sleek"] = new("bandolier", 5);
        values["bb_disc"] = new("bandolier", 5);
        values["bb_bring"] = new("bandolier", 10);
        values["bb_pouch"] = new("bandolier", 5);
        values["bb_cold"] = new("bandolier", 1000);
        values["bb_leech"] = new("bandolier", 15);
        values["bb_strap"] = new("bandolier", 3000);
        values["bb_slot"] = new("bandolier", 1);
        values["va_scent"] = new("vultureaid", 10);
        values["va_bonus"] = new("vultureaid", 25);
        values["va_reroll"] = new("vultureaid", 40);
        values["va_sharp"] = new("vultureaid", 20);
        values["va_leftover"] = new("vultureaid", 15);
        values["va_kit"] = new("vultureaid", 10);
        values["va_lucky"] = new("vultureaid", 15);
        values["va_slot"] = new("vultureaid", 1);
        values["mm_charge"] = new("meleemacchiato", 2000);
        values["mm_frenzy"] = new("meleemacchiato", 10);
        values["mm_iron"] = new("meleemacchiato", 10);
        values["mm_quick"] = new("meleemacchiato", 30);
        values["mm_hone"] = new("meleemacchiato", 50);
        values["mm_flash"] = new("meleemacchiato", 200);
        values["mm_wind"] = new("meleemacchiato", 25);
        values["mm_slot"] = new("meleemacchiato", 1);
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static bool IsKnownVersion(string version) => version is Version or CurrentVersion or LatestVersion;
    internal static FrozenSet<string> PerksFor(string version) => version switch
    {
        Version => Perks, CurrentVersion => CurrentPerks, LatestVersion => LatestPerks,
        _ => throw new RewardDataException("cola-delta-version")
    };
    internal static FrozenDictionary<string, RewardColaChallenge> ChallengesFor(string version) => version switch
    {
        Version => Challenges, CurrentVersion => CurrentChallenges, LatestVersion => LatestChallenges,
        _ => throw new RewardDataException("cola-delta-version")
    };

    internal static readonly FrozenDictionary<string, int> Synergies =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["syn_cardio"] = 3000,
            ["syn_medic"] = 10,
            ["syn_blast"] = 5,
            ["syn_pin"] = 10,
            ["syn_trigger"] = 15,
            ["syn_tank"] = 2000,
            ["syn_bounce"] = 8,
            ["syn_getaway"] = 10,
            ["syn_breath"] = 20,
            ["syn_shock"] = 5,
            ["syn_iron"] = 5,
            ["syn_sights"] = 10,
            ["syn_webblast"] = 15,
            ["syn_above"] = 3,
            ["syn_spider"] = 8,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static readonly FrozenDictionary<string, RewardColaAchievement> Achievements = new RewardColaAchievement[]
    {
        new("6a1c00000000000000000a01", "a_first", "first", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a02", "a_lvl5", "lvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a03", "a_all", "all", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c01", 3)])),
        new("6a1c00000000000000000a04", "a_found", "found", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c01", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a05", "a_die", "die", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c01", 1)])),
        new("6a1c00000000000000000a06", "a_hand", "hand", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a07", "st_fresh", "stfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a08", "st_rushb", "strushb", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a09", "st_marathon", "stmarathon", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c02", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a0a", "st_lvl5", "stlvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a0b", "st_cardio", "stcardio", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c02", 3)])),
        new("6a1c00000000000000000a0c", "st_double", "stdouble", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a0d", "jg_fresh", "jgfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a0e", "jg_rude", "jgrude", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c03", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a0f", "jg_red", "jgred", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a10", "jg_lvl5", "jglvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a11", "jg_all", "jgall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c03", 3)])),
        new("6a1c00000000000000000a12", "jg_secret", "jgsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a13", "pf_flop", "pfflop", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a14", "pf_hugs", "pfhugs", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c04", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a15", "pf_gravity", "pfgravity", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a16", "pf_lvl5", "pflvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a17", "pf_all", "pfall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c04", 3)])),
        new("6a1c00000000000000000a18", "pf_secret", "pfsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a19", "ww_first", "wwfirst", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a1a", "ww_webbed", "wwwebbed", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c05", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a1b", "ww_grenades", "wwgrenades", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a1c", "ww_lvl5", "wwlvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a1d", "ww_all", "wwall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c05", 3)])),
        new("6a1c00000000000000000a1e", "ww_secret", "wwsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a1f", "dd_fresh", "ddfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a20", "dd_heads", "ddheads", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c06", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a21", "dd_far", "ddfar", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a22", "dd_lvl5", "ddlvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a23", "dd_all", "ddall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c06", 3)])),
        new("6a1c00000000000000000a24", "dd_secret", "ddsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a25", "syn_cardio", "syncardio", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a26", "syn_medic", "synmedic", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a27", "syn_blast", "synblast", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a28", "syn_pin", "synpin", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a29", "syn_trigger", "syntrigger", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2a", "syn_tank", "syntank", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2b", "syn_bounce", "synbounce", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2c", "syn_getaway", "syngetaway", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2d", "syn_breath", "synbreath", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2e", "syn_shock", "synshock", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a2f", "syn_iron", "syniron", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a30", "syn_sights", "synsights", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a31", "syn_webblast", "synwebblast", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a32", "syn_above", "synabove", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a33", "syn_spider", "synspider", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a34", "syn_five", "synfive", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a35", "syn_all", "synall", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 8)])),
        new("6a1c00000000000000000a36", "syn_extract", "synextract", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a37", "q_01", "q01", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a38", "q_02", "q02", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 30000)])),
        new("6a1c00000000000000000a39", "q_03", "q03", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 35000)])),
        new("6a1c00000000000000000a3a", "q_04", "q04", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a3b", "q_05", "q05", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a3c", "q_06", "q06", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a3d", "q_07", "q07", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a3e", "q_08", "q08", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a3f", "q_09", "q09", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a40", "q_10", "q10", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 6)])),
        new("6a1c00000000000000000a41", "q_guards", "qguards", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a42", "q_boss", "qboss", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a43", "q_sober", "qsober", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 6)])),
        new("6a1c00000000000000000a44", "g_six", "gsix", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a45", "g_five", "gfive", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a46", "g_drinks50", "gdrinks50", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 30000)])),
        new("6a1c00000000000000000a47", "g_drinks150", "gdrinks150", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a48", "g_lvl5all", "glvl5all", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 8)])),
        new("6a1c00000000000000000a49", "g_augall", "gaugall", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 10)])),
        new("6a1c00000000000000000a4a", "g_aug20", "gaug20", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a4b", "g_rush", "grush", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a4c", "g_combo", "gcombo", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a4d", "g_bossall", "gbossall", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 6)])),
        new("6a1c00000000000000000a4e", "e_first", "efirst", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a4f", "e_three", "ethree", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a50", "e_all", "eall", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 8)])),
        new("6a1c00000000000000000a51", "e_back", "eback", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a52", "e_raids", "eraids", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a53", "p_first", "pfirst", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a54", "p_eternal", "peternal", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a55", "p_late", "plate", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a56", "c_full", "cfull", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a57", "c_chal", "cchal", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a58", "c_second", "csecond", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a59", "c_streak", "cstreak", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 6)])),
        new("6a1c00000000000000000a5a", "m_loaded", "mloaded", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 25000)])),
        new("6a1c00000000000000000a5b", "m_spread", "mspread", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a5c", "s_kills100", "skills100", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 30000)])),
        new("6a1c00000000000000000a5d", "s_kills500", "skills500", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a5e", "s_kills1500", "skills1500", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 8)])),
        new("6a1c00000000000000000a5f", "s_run", "srun", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a60", "s_absorb", "sabsorb", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 3)])),
        new("6a1c00000000000000000a61", "s_heads", "sheads", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a62", "s_webbed", "swebbed", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a63", "x_mirror", "xmirror", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 2)])),
        new("6a1c00000000000000000a64", "x_extract6", "xextract6", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 6)])),
    }.ToFrozenDictionary(a => a.Key, StringComparer.Ordinal);

    internal static readonly FrozenDictionary<string, RewardColaAchievement> ById =
        Achievements.Values.ToFrozenDictionary(a => a.Id, StringComparer.Ordinal);
    // Reviewed v4 data: old signed receipts retain their exact prior catalogs.
    internal const string LatestVersion = "tarkova-cola-4.0.0";
    internal const string LatestSourceCommit = "7163afb46ed8732d83dc26f3415e252bb5bf6c91";
    internal static readonly FrozenSet<string> LatestPerks = new[]
        { "speedcola", "staminup", "juggernog", "phdflopper", "widowswine", "deadshot", "bandolier", "vultureaid", "meleemacchiato", "quickrevive", "mulekick", "electriccherry", "deathperception", "tombstone", "doubletap" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenDictionary<string, RewardColaChallenge> LatestChallenges = CreateLatestChallenges();
    private static FrozenDictionary<string, RewardColaChallenge> CreateLatestChallenges()
    {
        var values = CurrentChallenges.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        values["st_rush"] = new("staminup", 3);
        values["st_feet"] = new("staminup", 3000);
        values["jg_plates"] = new("juggernog", 40);
        values["jg_skull"] = new("juggernog", 600);
        values["pf_terrain"] = new("phdflopper", 3000);
        values["ww_fuse"] = new("widowswine", 5);
        values["dd_sky"] = new("deadshot", 800);
        values["bb_cold"] = new("bandolier", 600);
        values["mm_charge"] = new("meleemacchiato", 1000);
        values["mm_flash"] = new("meleemacchiato", 100);
        values["qr_revive"] = new("quickrevive", 1000);
        values["qr_transfuse"] = new("quickrevive", 5);
        values["qr_medic"] = new("quickrevive", 2000);
        values["qr_patience"] = new("quickrevive", 50);
        values["qr_priority"] = new("quickrevive", 300);
        values["qr_clot"] = new("quickrevive", 10);
        values["qr_reserve"] = new("quickrevive", 20);
        values["qr_slot"] = new("quickrevive", 1);
        values["mk_kick"] = new("mulekick", 15);
        values["mk_bando"] = new("mulekick", 25);
        values["mk_change"] = new("mulekick", 30);
        values["mk_strap"] = new("mulekick", 3000);
        values["mk_chamber"] = new("mulekick", 30);
        values["mk_balance"] = new("mulekick", 10);
        values["mk_holster"] = new("mulekick", 100);
        values["mk_slot"] = new("mulekick", 1);
        values["ec_discharge"] = new("electriccherry", 30);
        values["ec_overcharge"] = new("electriccherry", 2000);
        values["ec_lightning"] = new("electriccherry", 20);
        values["ec_battery"] = new("electriccherry", 180);
        values["ec_voltage"] = new("electriccherry", 50);
        values["ec_feedback"] = new("electriccherry", 10);
        values["ec_steady"] = new("electriccherry", 1000);
        values["ec_slot"] = new("electriccherry", 1);
        values["de_sixth"] = new("deathperception", 25);
        values["de_hunter"] = new("deathperception", 10);
        values["de_instinct"] = new("deathperception", 15);
        values["de_persist"] = new("deathperception", 10);
        values["de_cat"] = new("deathperception", 15);
        values["de_shrapnel"] = new("deathperception", 10);
        values["de_reading"] = new("deathperception", 10);
        values["de_slot"] = new("deathperception", 1);
        values["ts_offering"] = new("tombstone", 5);
        values["ts_polter"] = new("tombstone", 10);
        values["ts_haunt"] = new("tombstone", 5);
        values["ts_long"] = new("tombstone", 60);
        values["ts_restless"] = new("tombstone", 3);
        values["ts_ecto"] = new("tombstone", 10);
        values["ts_trail"] = new("tombstone", 5);
        values["ts_slot"] = new("tombstone", 1);
        values["db_double"] = new("doubletap", 20);
        values["db_perf"] = new("doubletap", 15);
        values["db_momentum"] = new("doubletap", 30);
        values["db_trigger"] = new("doubletap", 15);
        values["db_cold"] = new("doubletap", 800);
        values["db_feed"] = new("doubletap", 10);
        values["db_metro"] = new("doubletap", 10);
        values["db_slot"] = new("doubletap", 1);
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // The 82 surviving old rewards are byte-for-byte unchanged in v4.
    internal static readonly FrozenDictionary<string, RewardColaAchievement> LatestAchievements =
        Achievements.Values.Where(a => !a.Key.StartsWith("syn_", StringComparison.Ordinal)).Concat(new RewardColaAchievement[]
    {
        new("6a1c00000000000000000a65", "bb_fresh", "bbfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a66", "bb_total", "bbtotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c07", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a67", "bb_lvl5", "bblvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a68", "bb_all", "bball", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c07", 3)])),
        new("6a1c00000000000000000a69", "bb_secret", "bbsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a6a", "va_fresh", "vafresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a6b", "va_total", "vatotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c08", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a6c", "va_lvl5", "valvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a6d", "va_all", "vaall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c08", 3)])),
        new("6a1c00000000000000000a6e", "va_secret", "vasecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a6f", "mm_fresh", "mmfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a70", "mm_total", "mmtotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c09", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a71", "mm_lvl5", "mmlvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a72", "mm_all", "mmall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c09", 3)])),
        new("6a1c00000000000000000a73", "mm_secret", "mmsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a74", "qr_fresh", "qrfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a75", "qr_total", "qrtotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0a", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a76", "qr_lvl5", "qrlvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a77", "qr_all", "qrall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0a", 3)])),
        new("6a1c00000000000000000a78", "qr_secret", "qrsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a79", "mk_fresh", "mkfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a7a", "mk_total", "mktotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0b", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a7b", "mk_lvl5", "mklvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a7c", "mk_all", "mkall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0b", 3)])),
        new("6a1c00000000000000000a7d", "mk_secret", "mksecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a7e", "ec_fresh", "ecfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a7f", "ec_total", "ectotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0c", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a80", "ec_lvl5", "eclvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a81", "ec_all", "ecall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0c", 3)])),
        new("6a1c00000000000000000a82", "ec_secret", "ecsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a83", "de_fresh", "defresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a84", "de_total", "detotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0d", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a85", "de_lvl5", "delvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a86", "de_all", "deall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0d", 3)])),
        new("6a1c00000000000000000a87", "de_secret", "desecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a88", "ts_fresh", "tsfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a89", "ts_total", "tstotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0e", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a8a", "ts_lvl5", "tslvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a8b", "ts_all", "tsall", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0e", 3)])),
        new("6a1c00000000000000000a8c", "ts_secret", "tssecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
        new("6a1c00000000000000000a8d", "db_fresh", "dbfresh", Array.AsReadOnly<RewardColaItem>([new("5449016a4bdc2d6f028b456f", 20000)])),
        new("6a1c00000000000000000a8e", "db_total", "dbtotal", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0f", 1), new("5449016a4bdc2d6f028b456f", 15000)])),
        new("6a1c00000000000000000a8f", "db_lvl5", "dblvl5", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 4)])),
        new("6a1c00000000000000000a90", "db_all", "dball", Array.AsReadOnly<RewardColaItem>([new("6a1c00000000000000000c0f", 3)])),
        new("6a1c00000000000000000a91", "db_secret", "dbsecret", Array.AsReadOnly<RewardColaItem>([new("5d235b4d86f7742e017bc88a", 5)])),
    }).ToFrozenDictionary(a => a.Key, StringComparer.Ordinal);
    internal static readonly FrozenDictionary<string, RewardColaAchievement> LatestById =
        LatestAchievements.Values.ToFrozenDictionary(a => a.Id, StringComparer.Ordinal);
    internal static readonly FrozenDictionary<string, RewardColaAchievement> AllAchievements =
        Achievements.Values.Concat(LatestAchievements.Values).GroupBy(a => a.Key, StringComparer.Ordinal)
            .Select(g => g.Last()).ToFrozenDictionary(a => a.Key, StringComparer.Ordinal);
    internal static FrozenDictionary<string, RewardColaAchievement> AchievementsFor(string version) => version switch
    {
        Version or CurrentVersion => Achievements, LatestVersion => LatestAchievements,
        _ => throw new RewardDataException("cola-delta-version")
    };
    internal static FrozenDictionary<string, RewardColaAchievement> ByIdFor(string version) => version switch
    {
        Version or CurrentVersion => ById, LatestVersion => LatestById,
        _ => throw new RewardDataException("cola-delta-version")
    };
    internal static int HistoricalGoal(string id) => Math.Max(Challenges.GetValueOrDefault(id)?.Goal ?? 0,
        Math.Max(CurrentChallenges.GetValueOrDefault(id)?.Goal ?? 0, LatestChallenges.GetValueOrDefault(id)?.Goal ?? 0));
}
