using BepInEx.Configuration;
using System;
using System.IO;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels;

internal static class InfoPanelSettings
{
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> UpdateIntervalSeconds;
        public static ConfigEntry<bool> ShowWeaponPanel;
        public static ConfigEntry<bool> ShowStatusPanel;
        public static ConfigEntry<bool> UseShortWeaponName;
        public static ConfigEntry<bool> ShowSpareMagCount;

        public static ConfigEntry<Vector2> WeaponPanelOffset;
        public static ConfigEntry<Vector2> StatusPanelOffset;

        public static ConfigEntry<Color> WeaponTextColor;
        public static ConfigEntry<Color> HealthTextColor;
        public static ConfigEntry<Color> WarningTextColor;

        public static ConfigEntry<float> PanelOpacity;
        public static ConfigEntry<float> UiScale;

        public static ConfigEntry<float> HealthWarningPercent;
        public static ConfigEntry<float> ArmorWarningPercent;
        public static ConfigEntry<bool> ShowWeight;
        public static ConfigEntry<Color> WeightTextColor;


    internal static void Init(ConfigFile config, string legacyPath)
    {
        ConfigFile legacy = null;
        try
        {
            if (File.Exists(legacyPath))
                legacy = new ConfigFile(legacyPath, false) { SaveOnConfigSet = false };
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena panel legacy settings unavailable: {ex.Message}"); }
            Enabled = Bind(config, legacy, "General", "Enabled", true,
                "Master switch for all raid info panels.");
            UpdateIntervalSeconds = Bind(config, legacy, "General", "UpdateIntervalSeconds", 0.1f,
                "How often (in seconds) the panels recompute and redraw their values. Lower is smoother but costs more CPU/GC; the displayed stats don't need per-frame precision.");
            ShowWeaponPanel = Bind(config, legacy, "Weapon", "Enabled", true,
                "Show the weapon information panel.");
            ShowStatusPanel = Bind(config, legacy, "Status", "Enabled", true,
                "Show the combined health, armor and status-effect panel.");
            UseShortWeaponName = Bind(config, legacy, "Weapon", "UseShortName", true,
                "Use the weapon short name instead of the full name.");
            ShowSpareMagCount = Bind(config, legacy, "Weapon", "ShowSpareMagCount", true,
                "Show quick-reload spare mags (rig/pockets) and backpack mags separately.");

            WeaponPanelOffset = Bind(config, legacy, "Weapon", "PanelOffset", new Vector2(-24f, 140f),
                "Panel offset from the bottom-right corner (pixels).");
            StatusPanelOffset = Bind(config, legacy, "Status", "PanelOffset", new Vector2(24f, 200f),
                "Panel offset from the bottom-left corner (pixels).");

            WeaponTextColor = Bind(config, legacy, "Weapon", "TextColor",
                new Color(0.92f, 0.94f, 0.96f, 0.96f),
                "Primary text color for the weapon panel.");
            HealthTextColor = Bind(config, legacy, "Status", "HealthColor",
                new Color(0.35f, 0.95f, 0.45f, 0.96f),
                "Text color for overall health.");
            PanelOpacity = Bind(config, legacy, "Appearance", "PanelOpacity", 0.55f,
                "Opacity of the translucent backing behind each panel. Set to 0 for a completely borderless HUD.");
            UiScale = Bind(config, legacy, "Appearance", "Scale", 1f,
                "Overall size of the panels.");

            WarningTextColor = Bind(config, legacy, "General", "WarningColor",
                new Color(0.98f, 0.35f, 0.28f, 0.96f),
                "Text color when a value falls below its warning threshold.");

            HealthWarningPercent = Bind(config, legacy, "Status", "HealthWarningPercent", 35f,
                "Health percentage below which the value is shown in the warning color.");
            ShowWeight = Bind(config, legacy, "Status", "ShowWeight", true,
                "Show current carried weight next to the health value.");
            WeightTextColor = Bind(config, legacy, "Status", "WeightColor",
                new Color(0.82f, 0.86f, 0.90f, 0.96f),
                "Text color for carried weight.");
            ArmorWarningPercent = Bind(config, legacy, "Status", "ArmorWarningPercent", 35f,
                "Zones at or below this remaining-durability percentage are additionally listed as text next to the paper doll (the zone fills themselves always show the green-to-red state).");


    }

    private static ConfigEntry<T> Bind<T>(ConfigFile config, ConfigFile legacy, string section,
        string key, T fallback, string description)
    {
        // Bind preserves existing/orphaned Arena entries. Only new entries use
        // read-only legacy values as defaults; never rewrite the old CFG.
        T initial = fallback;
        if (legacy != null)
        {
            try { initial = legacy.Bind(section, key, fallback).Value; }
            catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena panel setting {section}/{key} not imported: {ex.Message}"); }
        }
        return config.Bind("HUD Panels - " + section, key, initial, description);
    }
}
