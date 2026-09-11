using EFT;

using EFT.HealthSystem;

using EFT.InventoryLogic;

using EFT.UI;

using UnityEngine;



namespace ArenaMode.Hud.InfoPanels.UI

{

    internal static class GameIconLoader

    {

        // EFTHardSettings.Instance can be null before a raid starts, so these can't be
        // pre-warmed in a static initializer; cache lazily on first successful lookup instead.
        // This also avoids boxing the enum argument on every call once cached.
        private static Sprite _healthIcon;
        private static Sprite _weightIcon;
        internal static void Reset() { _healthIcon = null; _weightIcon = null; }

        public static Sprite GetHealthFactorIcon(EHealthFactorType type)

        {

            switch (type)
            {
                case EHealthFactorType.Health:
                    return _healthIcon != null ? _healthIcon : (_healthIcon = GetAttributeIcon(type));
                default:
                    return GetAttributeIcon(type);
            }

        }



        public static Sprite GetWeightIcon()

        {

            return _weightIcon != null ? _weightIcon : (_weightIcon = GetAttributeIcon(EItemAttributeId.Weight));

        }



        public static Sprite GetMagazineIcon()

        {

            return GetItemTypeIcon(EItemType.Magazine);

        }



        private static Sprite GetAttributeIcon(System.Enum id)

        {

            StaticIcons icons = GetStaticIcons();

            return icons != null ? icons.GetAttributeIcon(id) : null;

        }



        private static Sprite GetItemTypeIcon(EItemType type)

        {

            StaticIcons icons = GetStaticIcons();

            return icons != null ? icons.GetItemTypeIcon(type) : null;

        }



        private static StaticIcons GetStaticIcons()

        {

            EFTHardSettings settings = EFTHardSettings.Instance;

            return settings != null ? settings.StaticIcons : null;

        }

    }

}
