using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;

namespace ArenaMode.Hud.InfoPanels
{
    internal static class PlayerWeightUtil
    {
        internal struct WeightInfo
        {
            public float CurrentKg;
            public float OverweightThresholdKg;
            public bool IsOverweight;
        }

        public static bool TryGetWeightInfo(Player player, IHealthController healthController, out WeightInfo info)
        {
            info = default;
            Inventory inventory = player?.Inventory;
            if (inventory == null || healthController == null)
            {
                return false;
            }

            bool eliteStrength = player.Skills?.StrengthBuffElite ?? false;
            info.CurrentKg = eliteStrength ? inventory.TotalWeightEliteSkill : inventory.TotalWeight;

            if (player.Physical != null)
            {
                // Arena changes these per-body limits, never the global config.
                info.OverweightThresholdKg = player.Physical.BaseOverweightLimits.x;
                info.IsOverweight = player.Physical.Overweight > 0f;
            }

            return true;
        }
    }
}
