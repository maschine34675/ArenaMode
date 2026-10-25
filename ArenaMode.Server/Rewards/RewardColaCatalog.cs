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
}
