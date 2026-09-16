using EFT;
using EFT.HealthSystem;
using ArenaMode.Hud.InfoPanels.UI;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels
{
    // Health, armor and active status effects in one panel. Hydration and
    // energy are deliberately absent: this HUD targets ArenaMode, where neither
    // matters.
    internal sealed class PlayerStatusPanel
    {
        private const float PanelWidth = 250f;
        private const float PanelHeight = 202f;
        private const float FigureWidth = 60f;
        private const float FigureHeight = 118f;
        private const float ValueColumnLeft = 136f;
        private const int MaxDamageLines = 4;

        // Vertical bands, measured from the panel's top edge. The figures must
        // clear the header row: they used to be pinned at -8 like everything
        // else, which put the doll's head right behind the health value.
        private const float HeaderTop = -8f;
        private const float FiguresTop = -38f;
        private const float StatusStripTop = -172f;

        // Indexed by FineArmorZone. Kept to ~6 characters: the value column is
        // only about 110px wide, so longer labels get ellipsized ("STOMACH…").
        // The paper doll already shows where the zone is, the text just names it.
        private static readonly string[] ZoneNames =
        {
            "HEAD", "THRT", "THRX", "STOM", "PELV",
            "SD L", "SD R",
            "ARM L", "FARM L", "ARM R", "FARM R",
            "LEG L", "CALF L", "LEG R", "CALF R",
            "THRT B", "THRX B", "STOM B", "PELV B"
        };

        private static readonly Color HealthyColor = new Color(0.35f, 0.78f, 0.36f, 0.95f);
        private static readonly Color WornColor = new Color(0.91f, 0.79f, 0.24f, 0.95f);
        private static readonly Color DamagedColor = new Color(0.88f, 0.29f, 0.23f, 0.95f);

        private readonly RectTransform _panelRect;
        private readonly StatRowView _healthRow;
        private readonly StatRowView _weightRow;
        private readonly ArmorPuppetView _frontFigure;
        private readonly ArmorPuppetView _backFigure;
        private readonly TextMeshProUGUI[] _damageLines = new TextMeshProUGUI[MaxDamageLines];
        private readonly StatusStripView _statusStrip;

        private readonly ArmorDurabilityCounter.ZoneSummary[] _zones =
            new ArmorDurabilityCounter.ZoneSummary[ArmorDurabilityCounter.ZoneCount];
        private readonly int[] _worstZones = new int[MaxDamageLines];
        private readonly BodyStatusEntry[] _statusEntries = new BodyStatusEntry[StatusStripView.MaxEntries];

        public PlayerStatusPanel(Transform parent)
        {
            _panelRect = PanelStyle.CreatePanel(
                parent,
                "PlayerStatusPanel",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                InfoPanelSettings.StatusPanelOffset.Value,
                new Vector2(PanelWidth, PanelHeight));

            _healthRow = new StatRowView(_panelRect, "HealthLine", 12f, HeaderTop, 118f, 24f);
            _weightRow = new StatRowView(_panelRect, "WeightLine", 134f, HeaderTop, 104f, 24f);

            _frontFigure = new ArmorPuppetView(_panelRect, 8f, FiguresTop, FigureWidth, FigureHeight, "FRONT", true);
            _backFigure = new ArmorPuppetView(_panelRect, 70f, FiguresTop, FigureWidth, FigureHeight, "BACK", false);

            for (int i = 0; i < MaxDamageLines; i++)
            {
                TextMeshProUGUI line = PanelStyle.CreateLine(_panelRect, $"DamageLine{i}", FiguresTop - 2f - i * 28f, 26f);
                // Shrink instead of ellipsizing: overlapping armor sums up, so a
                // line can reach three digits on both sides ("STOM B 135/150").
                line.enableAutoSizing = true;
                line.fontSizeMax = 12f;
                line.fontSizeMin = 9f;
                RectTransform rect = line.rectTransform;
                rect.offsetMin = new Vector2(ValueColumnLeft, rect.offsetMin.y);
                rect.offsetMax = new Vector2(-6f, rect.offsetMax.y);
                _damageLines[i] = line;
            }

            _statusStrip = new StatusStripView(_panelRect, StatusStripTop, 22f);
        }

        public void SetVisible(bool visible)
        {
            _panelRect.gameObject.SetActive(visible);
        }

        public void ApplyLayout()
        {
            Vector2 offset = InfoPanelSettings.StatusPanelOffset.Value;
            float scale = InfoPanelSettings.UiScale != null
                ? Mathf.Clamp(InfoPanelSettings.UiScale.Value, 0.5f, 2f) : 1f;
            if (_panelRect.anchoredPosition != offset || _panelRect.localScale.x != scale)
                PanelStyle.ApplyPanelTransform(_panelRect, offset);
        }

        public void Clear()
        {
            _healthRow.Clear();
            _weightRow.Clear();
            _frontFigure.HideZones();
            _backFigure.HideZones();
            for (int i = 0; i < MaxDamageLines; i++)
            {
                _damageLines[i].text = string.Empty;
            }

            _statusStrip.Clear();
        }

        public void UpdateFromPlayer(Player player)
        {
            IHealthController healthController = player.HealthController;
            if (healthController == null)
            {
                Clear();
                return;
            }

            UpdateHealthRow(healthController);
            UpdateWeightRow(player, healthController);

            ArmorDurabilityCounter.FillSummary(player, _zones);
            for (int i = 0; i < _zones.Length; i++)
            {
                var zone = (FineArmorZone)i;
                bool hasArmor = _zones[i].HasArmor;
                Color color = hasArmor ? GradientColor(_zones[i].Normalized) : default;
                _frontFigure.SetZone(zone, hasArmor, color);
                _backFigure.SetZone(zone, hasArmor, color);
            }

            UpdateDamageLines();

            int statusCount = BodyStatusCounter.Fill(healthController, _statusEntries);
            _statusStrip.SetEntries(_statusEntries, statusCount);
        }

        private void UpdateHealthRow(IHealthController healthController)
        {
            ValueStruct health = healthController.GetBodyPartHealth(EBodyPart.Common, false);
            float percent = health.Normalized * 100f;
            Color color = percent <= InfoPanelSettings.HealthWarningPercent.Value
                ? InfoPanelSettings.WarningTextColor.Value
                : InfoPanelSettings.HealthTextColor.Value;

            _healthRow.Set(
                GameIconLoader.GetHealthFactorIcon(EHealthFactorType.Health),
                $"{Mathf.RoundToInt(health.Current)}/{Mathf.RoundToInt(health.Maximum)}",
                color);
        }

        private void UpdateWeightRow(Player player, IHealthController healthController)
        {
            if (!InfoPanelSettings.ShowWeight.Value || !PlayerWeightUtil.TryGetWeightInfo(player, healthController, out var weightInfo))
            {
                _weightRow.Clear();
                return;
            }

            Color color = weightInfo.IsOverweight
                ? InfoPanelSettings.WarningTextColor.Value
                : InfoPanelSettings.WeightTextColor.Value;

            _weightRow.Set(GameIconLoader.GetWeightIcon(), $"{weightInfo.CurrentKg:0.0} kg", color);
        }

        // The zone fills already tell the full story; text is reserved for the
        // zones that actually need attention (at or below the warning percent),
        // worst first.
        private void UpdateDamageLines()
        {
            float threshold = InfoPanelSettings.ArmorWarningPercent.Value / 100f;
            int count = 0;

            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].HasArmor || _zones[i].Normalized > threshold)
                {
                    continue;
                }

                // Insertion sort into the worst-N list, ascending by remaining percent.
                int insertAt = count < MaxDamageLines ? count : -1;
                for (int j = 0; j < count && j < MaxDamageLines; j++)
                {
                    if (_zones[i].Normalized < _zones[_worstZones[j]].Normalized)
                    {
                        insertAt = j;
                        break;
                    }
                }

                if (insertAt < 0)
                {
                    continue;
                }

                for (int j = Mathf.Min(count, MaxDamageLines - 1); j > insertAt; j--)
                {
                    _worstZones[j] = _worstZones[j - 1];
                }

                _worstZones[insertAt] = i;
                if (count < MaxDamageLines)
                {
                    count++;
                }
            }

            for (int i = 0; i < MaxDamageLines; i++)
            {
                if (i >= count)
                {
                    _damageLines[i].text = string.Empty;
                    continue;
                }

                int zoneIndex = _worstZones[i];
                int current = Mathf.RoundToInt(_zones[zoneIndex].CurrentDurability);
                int maximum = Mathf.RoundToInt(_zones[zoneIndex].MaxDurability);
                _damageLines[i].text = $"{ZoneNames[zoneIndex]} {current}/{maximum}";
                PanelStyle.ApplyTextColor(_damageLines[i], GradientColor(_zones[zoneIndex].Normalized));
            }
        }

        private static Color GradientColor(float normalized)
        {
            return normalized <= 0.5f
                ? Color.Lerp(DamagedColor, WornColor, normalized * 2f)
                : Color.Lerp(WornColor, HealthyColor, (normalized - 0.5f) * 2f);
        }
    }
}
