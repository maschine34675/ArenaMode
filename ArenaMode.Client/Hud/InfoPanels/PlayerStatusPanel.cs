using EFT;
using EFT.HealthSystem;
using ArenaMode.Hud.InfoPanels.UI;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels
{
    // Health, armor and active status effects in one panel. Hydration and
    // energy are deliberately absent: this HUD targets ArenaMode, where neither
    // matters. Kept to a minimum by design (2026-09-25, "HUD minimal reserve"):
    // two armor percentages (head, body) replace the old 19-zone paper doll
    // and its damage-line text — see ArmorDurabilityCounter.
    internal sealed class PlayerStatusPanel
    {
        private const float PanelWidth = 250f;
        // The old panel's own bottom padding, kept unchanged: 8px between its
        // status strip and the panel's bottom edge (202 - 172 - 22 = 8). The
        // three rows above it are spaced 4px apart (header bottom -32, armor
        // row top -36; armor row bottom -60, status strip top -64), so the
        // minimum height that still fits everything is
        // 8 (padding) + 64 (status strip top) + 22 (status strip height) = 94.
        private const float PanelHeight = 94f;

        // Vertical bands, measured from the panel's top edge.
        private const float HeaderTop = -8f;
        private const float ArmorRowTop = -36f;
        private const float StatusStripTop = -64f;

        private readonly RectTransform _panelRect;
        private readonly StatRowView _healthRow;
        private readonly StatRowView _weightRow;
        private readonly StatRowView _bodyArmorRow;
        private readonly StatRowView _helmetRow;
        private readonly StatusStripView _statusStrip;

        private readonly ArmorDurabilityCounter.GroupSummary[] _armor =
            new ArmorDurabilityCounter.GroupSummary[ArmorDurabilityCounter.GroupCount];
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
            _bodyArmorRow = new StatRowView(_panelRect, "BodyArmorLine", 12f, ArmorRowTop, 118f, 24f);
            _helmetRow = new StatRowView(_panelRect, "HelmetLine", 134f, ArmorRowTop, 104f, 24f);

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
            _bodyArmorRow.Clear();
            _helmetRow.Clear();
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

            ArmorDurabilityCounter.FillSummary(player, _armor);
            UpdateArmorRow(_bodyArmorRow, _armor[(int)ArmorGroup.Body], EmbeddedIcons.Get("armor_body.png"));
            UpdateArmorRow(_helmetRow, _armor[(int)ArmorGroup.Head], EmbeddedIcons.Get("armor_helmet.png"));

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

        // A group with no armor at all hides its stat instead of showing 0%,
        // matching how the weight row hides when it has nothing to report.
        // The normal color is the weight row's neutral one on purpose (its
        // setting description says so): no extra setting for a secondary stat.
        private static void UpdateArmorRow(StatRowView row, ArmorDurabilityCounter.GroupSummary summary, Sprite icon)
        {
            if (!summary.HasArmor)
            {
                row.Clear();
                return;
            }

            float percent = summary.Normalized * 100f;
            Color color = percent <= InfoPanelSettings.ArmorWarningPercent.Value
                ? InfoPanelSettings.WarningTextColor.Value
                : InfoPanelSettings.WeightTextColor.Value;
            row.Set(icon, $"{Mathf.RoundToInt(percent)}%", color);
        }
    }
}
