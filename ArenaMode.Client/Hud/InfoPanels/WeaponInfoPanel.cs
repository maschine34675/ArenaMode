using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using ArenaMode.Hud.InfoPanels.UI;
using TMPro;
using UnityEngine;

namespace ArenaMode.Hud.InfoPanels
{
    internal sealed class WeaponInfoPanel
    {
        private const float PanelWidth = 280f;
        private const float PanelHeight = 132f;
        private const float ReserveRowTop = -94f;
        // Only when magazines and grenades do not fit side by side; this is
        // the height the panel had with a permanent grenade row.
        private const float WrappedGrenadeRowTop = -124f;
        private const float WrappedPanelHeight = 160f;

        private readonly RectTransform _panelRect;
        private readonly TextMeshProUGUI _titleLine;
        private readonly TextMeshProUGUI _ammoLine;
        private readonly TextMeshProUGUI _metaLine;
        private readonly SpareMagazineBarView _spareMagBar;
        private readonly GrenadeBarView _grenadeBar;
        private Player _layoutBody;
        private bool _grenadesWrapped;

        public WeaponInfoPanel(Transform parent)
        {
            _panelRect = PanelStyle.CreatePanel(
                parent,
                "WeaponInfoPanel",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                InfoPanelSettings.WeaponPanelOffset.Value,
                new Vector2(PanelWidth, PanelHeight));

            _titleLine = PanelStyle.CreateLine(_panelRect, "WeaponTitle", -10f, 24f);
            _ammoLine = PanelStyle.CreateLine(_panelRect, "WeaponAmmo", -38f, 24f);
            _metaLine = PanelStyle.CreateLine(_panelRect, "WeaponMeta", -66f, 24f);
            _spareMagBar = new SpareMagazineBarView(_panelRect, ReserveRowTop);
            _grenadeBar = new GrenadeBarView(_panelRect, ReserveRowTop);

            PanelStyle.ApplyTextColor(_titleLine, InfoPanelSettings.WeaponTextColor.Value);
            PanelStyle.ApplyTextColor(_ammoLine, InfoPanelSettings.WeaponTextColor.Value);
            PanelStyle.ApplyTextColor(_metaLine, InfoPanelSettings.WeaponTextColor.Value);

            _titleLine.fontSize = 19f;
            _titleLine.fontStyle = FontStyles.Bold;
            _ammoLine.fontSize = 22f;
            _ammoLine.enableAutoSizing = true;
            _ammoLine.fontSizeMin = 18f;
            _ammoLine.fontSizeMax = 22f;
            _metaLine.fontSize = 15f;
        }

        public void SetVisible(bool visible)
        {
            _panelRect.gameObject.SetActive(visible);
        }

        public void ApplyLayout()
        {
            Vector2 offset = InfoPanelSettings.WeaponPanelOffset.Value;
            float scale = InfoPanelSettings.UiScale != null
                ? Mathf.Clamp(InfoPanelSettings.UiScale.Value, 0.5f, 2f) : 1f;
            Color color = InfoPanelSettings.WeaponTextColor.Value;
            if (_panelRect.anchoredPosition != offset || _panelRect.localScale.x != scale)
                PanelStyle.ApplyPanelTransform(_panelRect, offset);
            if (_titleLine.color != color)
            {
                PanelStyle.ApplyTextColor(_titleLine, color);
                PanelStyle.ApplyTextColor(_ammoLine, color);
                PanelStyle.ApplyTextColor(_metaLine, color);
            }
        }

        public void Clear()
        {
            ClearWeaponLines();
            _grenadeBar.Hide();
        }

        // Everything that describes the firearm in hand. The grenade counts
        // are carried inventory and stay: holding a grenade (or a knife or
        // meds) is exactly when the remaining grenade count matters.
        private void ClearWeaponLines()
        {
            _titleLine.text = string.Empty;
            _ammoLine.text = string.Empty;
            _metaLine.text = string.Empty;
            _spareMagBar.Hide();
        }

        public void UpdateFromPlayer(Player player)
        {
            if (!ReferenceEquals(player, _layoutBody))
            {
                _layoutBody = player;
                _grenadesWrapped = false;
            }

            UpdateGrenadeLine(player);
            UpdateWeaponLines(player);
            ArrangeReserveRow();
        }

        // Magazines keep the reserve row's left side; the grenades take its
        // right side when they fit beside them, otherwise the row below.
        // The panel is bottom-anchored, so wrapping lifts every line by one
        // row. Once a body needed the wrap it keeps it until the next body:
        // drawing a grenade or knife hides the magazines, and a count crossing
        // a digit boundary changes their width, and neither may make the
        // whole panel jump back and forth.
        private void ArrangeReserveRow()
        {
            _grenadesWrapped = ReserveRowLayout.GrenadesWrapped(
                _grenadesWrapped, _spareMagBar.ContentRight, _grenadeBar.GroupWidth);
            _grenadeBar.PlaceOnRow(_grenadesWrapped ? WrappedGrenadeRowTop : ReserveRowTop);
            float height = _grenadesWrapped ? WrappedPanelHeight : PanelHeight;
            if (_panelRect.sizeDelta.y != height)
            {
                _panelRect.sizeDelta = new Vector2(PanelWidth, height);
            }
        }

        private void UpdateWeaponLines(Player player)
        {
            if (player.HandsController is not Player.FirearmController firearmController)
            {
                ClearWeaponLines();
                return;
            }

            if (firearmController.IsInLauncherMode())
            {
                UpdateLauncher(firearmController);
                return;
            }

            if (firearmController.Item is not Weapon weapon)
            {
                ClearWeaponLines();
                return;
            }

            _titleLine.text = GetWeaponName(weapon);
            _ammoLine.text = BuildAmmoLine(weapon);
            _metaLine.text = BuildMetaLine(weapon);
            UpdateSpareMagLines(player, weapon);
        }

        private void UpdateLauncher(Player.FirearmController firearmController)
        {
            if (firearmController.Item is not Weapon weapon)
            {
                ClearWeaponLines();
                return;
            }

            Launcher launcher = weapon.GetUnderbarrelWeapon();
            if (launcher == null)
            {
                ClearWeaponLines();
                return;
            }

            int chamberCount = launcher.ChamberAmmoCount;
            int maxChambers = launcher.Chambers.Length;

            _titleLine.text = launcher.ShortName.Localized(null);
            _ammoLine.text = AppendLoadedAmmo(
                $"<b>{chamberCount}</b>/{maxChambers}",
                GetLoadedAmmoName(launcher.Chambers, null));
            _metaLine.text = launcher.WeaponTemplate.ammoCaliber.Localized(null);
            _spareMagBar.Hide();
        }

        private string GetWeaponName(Weapon weapon)
        {
            if (InfoPanelSettings.UseShortWeaponName.Value)
            {
                return weapon.ShortName.Localized(null);
            }

            return weapon.Name.Localized(null);
        }

        // No "MAG"/"CH" prefix — like most shooter HUDs the numbers speak for
        // themselves, with the currently loaded round named right next to them
        // (chambered round first, else the next round the magazine will feed).
        private static string BuildAmmoLine(Weapon weapon)
        {
            int chamber = weapon.ChamberAmmoCount;
            string counts;

            if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                counts = $"<b>{chamber}</b>/{weapon.Chambers.Length}";
            }
            else
            {
                int magCount = weapon.GetCurrentMagazineCount();
                int maxMag = weapon.GetMaxMagazineCount();
                counts = chamber > 0
                    ? $"<b>{magCount}</b>/{maxMag} +{chamber}"
                    : $"<b>{magCount}</b>/{maxMag}";
            }

            return AppendLoadedAmmo(counts, GetLoadedAmmoName(weapon.Chambers, weapon.GetCurrentMagazine()));
        }

        private static string AppendLoadedAmmo(string counts, string loadedAmmoName)
        {
            return loadedAmmoName != null ? $"{counts}  <size=16>·  {loadedAmmoName}</size>" : counts;
        }

        // Deliberately not Weapon.CurrentAmmoTemplate: that reports a spent
        // casing still sitting in the chamber as loaded, and falls back to the
        // weapon's default ammo template, so it never returns null for an
        // empty weapon.
        private static string GetLoadedAmmoName(Slot[] chambers, Magazine magazine)
        {
            if (chambers != null)
            {
                for (int i = 0; i < chambers.Length; i++)
                {
                    if (chambers[i]?.ContainedItem is Ammo chambered && !chambered.IsUsed)
                    {
                        return chambered.ShortName.Localized(null);
                    }
                }
            }

            Item nextRound = magazine?.FirstRealAmmo();
            return nextRound?.ShortName.Localized(null);
        }

        private static string BuildMetaLine(Weapon weapon)
        {
            string caliber = weapon.AmmoCaliber.Localized(null);
            string fireMode = weapon.SelectedFireMode.ToString().Localized(null);
            return $"{caliber}  |  {fireMode}";
        }

        private void UpdateSpareMagLines(Player player, Weapon weapon)
        {
            Color color = InfoPanelSettings.WeaponTextColor.Value;

            if (!InfoPanelSettings.ShowSpareMagCount.Value)
            {
                _spareMagBar.Hide();
                return;
            }

            // Carrying at least one spare magazine makes magazines the reserve
            // worth showing. Weapons with an internal magazine also report a
            // magazine slot, so an all-zero summary is not the same as "no
            // magazine display applies" and must fall through as well.
            if (MagazineInventoryCounter.TryGetSpareMagSummary(player, weapon, out var summary)
                && (summary.FullCount > 0 || summary.PartialCount > 0 || summary.BackpackLoadedCount > 0))
            {
                _spareMagBar.SetSummary(summary.FullCount, summary.PartialCount, summary.BackpackLoadedCount, color);
                return;
            }

            // Break actions, internal magazines and simply being out of spare
            // mags all leave loose cartridges as the reserve that matters.
            if (LooseAmmoCounter.TryGetSummary(player, weapon, out var ammoSummary))
            {
                _spareMagBar.SetRoundSummary(ammoSummary.ReadyRounds, ammoSummary.BackpackRounds, color);
                return;
            }

            _spareMagBar.Hide();
        }

        // Independent of the hands state on purpose (see ClearWeaponLines).
        // Hidden while no grenade is carried (all thrown, a kit without
        // grenades, no rig space for the supplied RGD-5s).
        private void UpdateGrenadeLine(Player player)
        {
            if (GrenadeInventoryCounter.TryGetSummary(player, out var summary) && !summary.IsEmpty)
            {
                _grenadeBar.SetSummary(summary, InfoPanelSettings.WeaponTextColor.Value);
                return;
            }

            _grenadeBar.Hide();
        }
    }
}
