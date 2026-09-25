using System.Reflection;
using ArenaMode.Hud.InfoPanels;
using ArenaMode.Hud.InfoPanels.UI;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Respawn;
using ArenaMode.Spectator;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using ArenaMode.Hud;
using EFT.UI.Screens;

internal static class Program
{
    private static int _checks;
    private static string _scratch;
    private static void Check(bool value, string why)
    { if (!value) throw new InvalidOperationException("CHECK FAILED: " + why); _checks++; }
    private static void Near(float value, float expected, string why)
        => Check(Math.Abs(value - expected) < 0.00001f, why);
    private static string Temp(string name) => Path.Combine(_scratch, name + "-" + Guid.NewGuid().ToString("N") + ".cfg");

    private sealed record Setting(string Field, string Section, string Key, object Expected, string Text);
    private static readonly Setting[] LegacySettings =
    {
        new("Enabled", "General", "Enabled", false, "false"),
        new("UpdateIntervalSeconds", "General", "UpdateIntervalSeconds", 0.75f, "0.75"),
        new("ShowWeaponPanel", "Weapon", "Enabled", false, "false"),
        new("ShowStatusPanel", "Status", "Enabled", false, "false"),
        new("UseShortWeaponName", "Weapon", "UseShortName", false, "false"),
        new("ShowSpareMagCount", "Weapon", "ShowSpareMagCount", false, "false"),
        new("WeaponPanelOffset", "Weapon", "PanelOffset", new Vector2(-80, 250), "-80,250"),
        new("StatusPanelOffset", "Status", "PanelOffset", new Vector2(70, 260), "70,260"),
        new("WeaponTextColor", "Weapon", "TextColor", new Color(0.1f,0.2f,0.3f,0.4f), "0.1,0.2,0.3,0.4"),
        new("HealthTextColor", "Status", "HealthColor", new Color(0.2f,0.3f,0.4f,0.5f), "0.2,0.3,0.4,0.5"),
        new("PanelOpacity", "Appearance", "PanelOpacity", 0.25f, "0.25"),
        new("UiScale", "Appearance", "Scale", 1.75f, "1.75"),
        new("WarningTextColor", "General", "WarningColor", new Color(0.3f,0.4f,0.5f,0.6f), "0.3,0.4,0.5,0.6"),
        new("HealthWarningPercent", "Status", "HealthWarningPercent", 20f, "20"),
        new("ShowWeight", "Status", "ShowWeight", false, "false"),
        new("WeightTextColor", "Status", "WeightColor", new Color(0.4f,0.5f,0.6f,0.7f), "0.4,0.5,0.6,0.7"),
        new("ArmorWarningPercent", "Status", "ArmorWarningPercent", 25f, "25"),
    };

    private static object SettingValue(string field)
    {
        object entry = typeof(InfoPanelSettings).GetField(field, BindingFlags.Public | BindingFlags.Static).GetValue(null);
        return entry.GetType().GetProperty("Value").GetValue(entry);
    }

    private static void Migration()
    {
        string legacy = Temp("legacy");
        File.WriteAllLines(legacy, LegacySettings.GroupBy(s => s.Section)
            .SelectMany(group => new[] { "[" + group.Key + "]" }.Concat(group.Select(s => s.Key + " = " + s.Text))));
        byte[] original = File.ReadAllBytes(legacy);
        var target = new ConfigFile(Temp("arena"), false) { SaveOnConfigSet = false };
        InfoPanelSettings.Init(target, legacy);
        Check(LegacySettings.Length == 17 && target.Bound.Count == 17, "Exactly 17 absorbed settings, no unrelated config keys");
        foreach (var setting in LegacySettings)
        {
            Check(Equals(SettingValue(setting.Field), setting.Expected), "Legacy value migrated: " + setting.Section + "/" + setting.Key);
            Check(target.Bound.ContainsKey("HUD Panels - " + setting.Section + "/" + setting.Key), "Arena namespace for " + setting.Field);
        }
        var legacyReader = ConfigFile.Instances.Last(c => c.Path == legacy);
        Check(legacyReader.BindCalls.Count == 17 && legacyReader.Bound.Count == 17, "Every legacy key read once");
        Check(legacyReader.WriteCalls == 0 && !legacyReader.SaveOnConfigSet, "Legacy bind cannot write config");
        Check(original.SequenceEqual(File.ReadAllBytes(legacy)), "Legacy file byte-identical after migration");

        string existing = Temp("existing-arena");
        File.WriteAllText(existing, "[HUD Panels - General]\nEnabled = true\n[HUD Panels - Appearance]\nScale = 1.25\n");
        var orphaned = new ConfigFile(existing, false) { SaveOnConfigSet = false };
        InfoPanelSettings.Init(orphaned, legacy);
        Check(InfoPanelSettings.Enabled.Value, "Existing unbound Arena enabled key beats legacy false");
        Near(InfoPanelSettings.UiScale.Value, 1.25f, "Existing unbound Arena scale beats legacy");
        Check(!InfoPanelSettings.ShowWeight.Value, "Other absent Arena key still imports legacy");
        var firstEntry = InfoPanelSettings.Enabled;
        firstEntry.Value = false;
        InfoPanelSettings.Init(orphaned, legacy);
        Check(ReferenceEquals(firstEntry, InfoPanelSettings.Enabled) && !InfoPanelSettings.Enabled.Value,
            "Already-bound current Arena value wins on repeated initialization");
        Check(ConfigFile.Instances.Where(c => c.Path == legacy).All(c => c.WriteCalls == 0), "No legacy writer in repeated migrations");
        Check(original.SequenceEqual(File.ReadAllBytes(legacy)), "Repeated migration never modifies legacy bytes");

        string absent = Temp("absent-legacy");
        var defaults = new ConfigFile(Temp("default-arena"), false) { SaveOnConfigSet = false };
        InfoPanelSettings.Init(defaults, absent);
        Check(!File.Exists(absent), "Missing legacy config is not created");
        Check(InfoPanelSettings.Enabled.Value && InfoPanelSettings.ShowWeaponPanel.Value && InfoPanelSettings.ShowStatusPanel.Value,
            "Missing legacy uses enabled defaults");
        Near(InfoPanelSettings.UpdateIntervalSeconds.Value, 0.1f, "Default interval");

        string invalid = Temp("invalid-legacy");
        File.WriteAllText(invalid, "[General]\nEnabled = not-a-bool\n[Appearance]\nScale = 1.5\n");
        var recovery = new ConfigFile(Temp("recovery"), false) { SaveOnConfigSet = false };
        InfoPanelSettings.Init(recovery, invalid);
        Check(InfoPanelSettings.Enabled.Value, "Failed legacy conversion falls back for that setting");
        Near(InfoPanelSettings.UiScale.Value, 1.5f, "Other legacy keys survive one conversion failure");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly ArenaMatch Match = new();
        public readonly AbstractGame Game = new();
        public readonly GameWorld World = new();
        public Player Player;
        public readonly ArenaInfoPanels Panels;
        public readonly GameObject Root;
        public WeaponInfoPanel Weapon => WeaponInfoPanel.Latest;
        public PlayerStatusPanel Status => PlayerStatusPanel.Latest;
        public Fixture(bool createPanels = true)
        {
            InfoPanelSettings.Init(new ConfigFile(Temp("fixture"), false) { SaveOnConfigSet = false }, Temp("missing"));
            Time.unscaledTime = 0;
            ArenaMode.Plugin.ModLogger.Warnings.Clear();
            ArenaMode.Config.Settings.HudEnabled.Value = true;
            EftScreenManager.Instance = new EftScreenManager();
            ArenaSession.IsActive = true;
            ArenaRespawnDriver.IsRespawnInProgress = false;
            ArenaSpectatorService.IsPending = ArenaSpectatorService.IsActive = false;
            Singleton<GameWorld>.Instance = World;
            Singleton<AbstractGame>.Instance = Game;
            ArenaSession.ExactGame = Game; ArenaSession.ExactWorld = World;
            ArenaMatchService.Current = Match;
            Player = NewBody("first"); World.MainPlayer = Player;
            if (createPanels)
            {
                Panels = new ArenaInfoPanels(new RectTransform(), Match);
                Root = GameObject.Created.Last(o => o.name == "ArenaInfoPanels");
            }
        }
        public Player NewBody(string name)
        { var body = new Player { Name = name }; World.AllAlivePlayersList.Add(body); return body; }
        public void Tick(float advance = 0) { Time.unscaledTime += advance; Panels.Tick(); }
        public void Dispose() => Panels?.Dispose();
    }

    private static void Context()
    {
        using var f = new Fixture(false);
        void Denies(Action change, Action restore, string why)
        {
            change();
            int exactChecks = ArenaSession.ExactChecks;
            Check(!RaidPlayerContext.TryGetLocalPlayer(out var result) && result == null, why + " clears out player");
            if (why == "Ordinary raid")
                Check(ArenaSession.ExactChecks == exactChecks, "Inactive Arena guard avoids accessing exact-raid state");
            restore();
            Check(RaidPlayerContext.TryGetLocalPlayer(out result) && ReferenceEquals(result, f.Player), why + " recovers exact local body");
        }
        Check(RaidPlayerContext.TryGetLocalPlayer(out var first) && ReferenceEquals(first, f.Player), "Fully initialized live local context accepted");
        Denies(() => ArenaSession.IsActive = false, () => ArenaSession.IsActive = true, "Ordinary raid");
        Denies(() => f.Player.IsInventoryOpened = true, () => f.Player.IsInventoryOpened = false, "Inventory opening before screen switch");
        Denies(() => EftScreenManager.Instance.CurrentScreen = EEftScreenType.Inventory,
            () => EftScreenManager.Instance.CurrentScreen = EEftScreenType.BattleUI, "Inventory screen after flag reset");
        Denies(() => ArenaRespawnDriver.IsRespawnInProgress = true, () => ArenaRespawnDriver.IsRespawnInProgress = false, "Respawn in flight");
        Denies(() => ArenaSpectatorService.IsPending = true, () => ArenaSpectatorService.IsPending = false, "Spectator pending");
        Denies(() => ArenaSpectatorService.IsActive = true, () => ArenaSpectatorService.IsActive = false, "Spectator active");
        Denies(() => Singleton<GameWorld>.Instance = null, () => Singleton<GameWorld>.Instance = f.World, "No world singleton");
        Denies(() => Singleton<AbstractGame>.Instance = null, () => Singleton<AbstractGame>.Instance = f.Game, "No game singleton");
        foreach (var status in new[] { GameStatus.Starting, GameStatus.Stopping, GameStatus.Stopped })
            Denies(() => f.Game.Status = status, () => f.Game.Status = GameStatus.Started, "Non-started game " + status);
        Denies(() => Singleton<AbstractGame>.Instance = new AbstractGame(), () => Singleton<AbstractGame>.Instance = f.Game, "Foreign started game");
        var foreignWorld = new GameWorld { MainPlayer = f.Player }; foreignWorld.AllAlivePlayersList.Add(f.Player);
        Denies(() => Singleton<GameWorld>.Instance = foreignWorld, () => Singleton<GameWorld>.Instance = f.World, "Foreign world with same local body");
        Denies(() => f.World.MainPlayer = null, () => f.World.MainPlayer = f.Player, "No main player");
        Denies(() => f.Player.IsYourPlayer = false, () => f.Player.IsYourPlayer = true, "Remote/AI main player");
        Denies(() => f.Player.PlayerBody = null, () => f.Player.PlayerBody = new(), "Body not initialized");
        Denies(() => f.Player.HandsController = null, () => f.Player.HandsController = new(), "Hands not initialized");
        Denies(() => f.Player.HealthController = null, () => f.Player.HealthController = new(), "Health not initialized");
        Denies(() => f.Player.HealthController.IsAlive = false, () => f.Player.HealthController.IsAlive = true, "Dead local body");
        Denies(() => f.Player.Inventory = null, () => f.Player.Inventory = new(), "Inventory not initialized");
        Denies(() => f.Player.Inventory.Equipment = null, () => f.Player.Inventory.Equipment = new(), "Equipment not initialized");
        Denies(() => f.World.AllAlivePlayersList.Remove(f.Player), () => f.World.AllAlivePlayersList.Add(f.Player), "Body absent from alive roster");
        Denies(() => f.Player.PointOfView = EPointOfView.ThirdPerson, () => f.Player.PointOfView = EPointOfView.FirstPerson, "Third-person body");
        Denies(() => f.Player.Destroyed = true, () => f.Player.Destroyed = false, "Destroyed Unity body wrapper");
    }

    private static void RefreshAndLifecycle()
    {
        using var f = new Fixture();
        Check(!f.Root.Destroyed && ArenaMode.Plugin.ModLogger.Warnings.Count == 0,
            "Active-hierarchy panel construction completes before the root is hidden");
        Check(!f.Root.activeSelf, "Constructed panel root initially hidden");
        f.Tick();
        Check(f.Root.activeSelf && f.Weapon.Visible && f.Status.Visible, "Live body shows both valid panels");
        Check(f.Weapon.Updates == 1 && f.Status.Updates == 1, "Initial refresh once per module");
        f.Tick(0.03f); f.Tick(0.03f);
        Check(f.Weapon.Updates == 1 && f.Status.Updates == 1, "Per-frame Tick does not recompute before deadline");
        f.Tick(0.05f);
        Check(f.Weapon.Updates == 2 && f.Status.Updates == 2, "Refresh resumes after deadline");
        int statusCount = f.Status.Updates;
        InfoPanelSettings.ShowWeaponPanel.Value = false; f.Tick();
        Check(!f.Weapon.Visible && f.Status.Visible && f.Status.Updates == statusCount, "Weapon toggle independent and immediate");
        InfoPanelSettings.ShowWeaponPanel.Value = true; f.Tick();
        Check(f.Weapon.Visible && f.Weapon.Updates == 3 && f.Status.Updates == statusCount, "Re-enabling weapon refreshes only weapon immediately");
        InfoPanelSettings.ShowStatusPanel.Value = false; f.Tick();
        Check(f.Weapon.Visible && !f.Status.Visible, "Status toggle independent and immediate");
        InfoPanelSettings.ShowStatusPanel.Value = true; f.Tick();
        Check(f.Status.Visible && f.Status.Updates == statusCount + 1, "Re-enabling status refreshes immediately");

        void HideAndRecover(Action change, Action restore, string reason)
        {
            int weapon = f.Weapon.Updates, status = f.Status.Updates;
            change(); f.Tick();
            Check(!f.Root.activeSelf && f.Weapon.Updates == weapon && f.Status.Updates == status,
                reason + " hides before throttle deadline without reading new stats");
            restore(); f.Tick();
            Check(f.Root.activeSelf && f.Weapon.Updates == weapon + 1 && f.Status.Updates == status + 1,
                reason + " recovery forces fresh stats even on same frame");
        }
        HideAndRecover(() => ArenaMode.Config.Settings.HudEnabled.Value = false, () => ArenaMode.Config.Settings.HudEnabled.Value = true, "Arena HUD master off");
        HideAndRecover(() => InfoPanelSettings.Enabled.Value = false, () => InfoPanelSettings.Enabled.Value = true, "Info panels master off");
        HideAndRecover(() => f.Player.IsInventoryOpened = true, () => f.Player.IsInventoryOpened = false, "Inventory or loot opened");
        foreach (EEftScreenType? screen in new EEftScreenType?[] { EEftScreenType.Inventory, EEftScreenType.Menu, EEftScreenType.Settings, null })
            HideAndRecover(() => EftScreenManager.Instance.CurrentScreen = screen,
                () => EftScreenManager.Instance.CurrentScreen = EEftScreenType.BattleUI, "Native screen " + screen);
        HideAndRecover(() => ArenaSession.IsActive = false, () => ArenaSession.IsActive = true, "Ordinary session");
        HideAndRecover(() => ArenaMatchService.Current = new ArenaMatch(), () => ArenaMatchService.Current = f.Match, "Replaced match");
        HideAndRecover(() => f.Player.HealthController.IsAlive = false, () => f.Player.HealthController.IsAlive = true, "Death");
        HideAndRecover(() => ArenaRespawnDriver.IsRespawnInProgress = true, () => ArenaRespawnDriver.IsRespawnInProgress = false, "Respawn");
        HideAndRecover(() => ArenaSpectatorService.IsPending = true, () => ArenaSpectatorService.IsPending = false, "Killcam pending");
        HideAndRecover(() => ArenaSpectatorService.IsActive = true, () => ArenaSpectatorService.IsActive = false, "Spectator");

        int previousUpdates = f.Weapon.Updates;
        var replacement = f.NewBody("replacement"); f.World.MainPlayer = replacement; f.Tick();
        Check(f.Weapon.Updates == previousUpdates + 1 && ReferenceEquals(f.Weapon.LastPlayer, replacement)
            && ReferenceEquals(f.Status.LastPlayer, replacement), "Physical body replacement refreshes immediately without old data");
        replacement.HealthController.IsAlive = false; f.Tick();
        Check(!f.Root.activeSelf, "Replacement death also hides immediately");

        Near(ArenaInfoPanels.RefreshInterval(float.NaN), 0.1f, "NaN interval fallback");
        Near(ArenaInfoPanels.RefreshInterval(float.PositiveInfinity), 0.1f, "Positive infinity interval fallback");
        Near(ArenaInfoPanels.RefreshInterval(float.NegativeInfinity), 0.1f, "Negative infinity interval fallback");
        Near(ArenaInfoPanels.RefreshInterval(-1), 0.05f, "Negative interval clamped");
        Near(ArenaInfoPanels.RefreshInterval(0), 0.05f, "Zero interval clamped");
        Near(ArenaInfoPanels.RefreshInterval(50), 2f, "Large interval clamped");
        Near(ArenaInfoPanels.RefreshInterval(0.3f), 0.3f, "Valid interval preserved");
    }

    private static void ErrorIsolation()
    {
        using var f = new Fixture(); f.Tick();
        f.Weapon.ThrowUpdate = true; f.Tick(0.11f);
        Check(!f.Weapon.Visible && f.Status.Visible, "Failed weapon update hides stale weapon only");
        f.Tick(0.01f);
        Check(!f.Weapon.Visible && f.Status.Visible, "Throttled frame must not resurrect stale weapon data");
        f.Tick(0.11f);
        Check(ArenaMode.Plugin.ModLogger.Warnings.Count(m => m.Contains("weapon panel update failed")) == 1,
            "Repeated weapon faults log once");
        f.Weapon.ThrowUpdate = false; f.Tick();
        Check(!f.Weapon.Visible, "Cleared exception waits for successful fresh update");
        f.Tick(0.11f); Check(f.Weapon.Visible && f.Status.Visible, "Weapon recovers after valid update");
        f.Status.ThrowUpdate = true; f.Tick(0.11f);
        Check(f.Weapon.Visible && !f.Status.Visible, "Failed status update hides stale status only");
        f.Tick(0.01f); Check(!f.Status.Visible, "Status does not resurrect during throttle interval");
        f.Status.ThrowUpdate = false; f.Tick(0.11f); Check(f.Status.Visible, "Status recovers after valid update");
        f.Weapon.ThrowLayout = true; f.Tick();
        Check(!f.Weapon.Visible && f.Status.Visible, "Layout failure also hides affected module only");
        f.Weapon.ThrowLayout = false; f.Tick(); Check(!f.Weapon.Visible, "Layout recovery alone cannot expose invalid content");
        f.Tick(0.11f); Check(f.Weapon.Visible, "Successful data refresh restores layout-failed module");
        f.Player.ThrowOnHands = true; f.Tick();
        Check(!f.Root.activeSelf, "Context exception hides entire root immediately");
        f.Tick();
        Check(ArenaMode.Plugin.ModLogger.Warnings.Count(m => m.Contains("context panel update failed")) == 1,
            "Repeated context exceptions log once");
        f.Player.ThrowOnHands = false; int before = f.Weapon.Updates; f.Tick();
        Check(f.Root.activeSelf && f.Weapon.Updates == before + 1, "Context recovery refreshes fresh values");

        f.Weapon.ThrowUpdate = true;
        var fresh = f.NewBody("new-failing-body"); f.World.MainPlayer = fresh; f.Tick();
        Check(!f.Weapon.Visible && f.Status.Visible && ReferenceEquals(f.Status.LastPlayer, fresh),
            "New-body update failure cannot display prior body's weapon");
    }

    private static void Disposal()
    {
        var f = new Fixture(); f.Tick();
        var opacity = InfoPanelSettings.PanelOpacity;
        Check(opacity.Subscribers == 1, "One theme handler per owner");
        int themes = PanelStyle.ThemeCalls;
        opacity.Value += 0.01f;
        Check(PanelStyle.ThemeCalls == themes + 1, "Live theme change reaches panel style");
        int[] before = CleanupCounts();
        MagazineScratch().Add(new Magazine { Count = 30 });
        BackpackScratch().Add(new Magazine { Count = 30 });
        GrenadeScratch("_onGScratch").Add(new ThrowWeap());
        GrenadeScratch("_carriedScratch").Add(new ThrowWeap());
        ArmorScratch().Add(new ArmorComponent());
        f.Dispose();
        Check(opacity.Subscribers == 0, "Dispose unsubscribes theme handler");
        Check(f.Root == null, "Dispose destroys owned root");
        Check(CleanupCounts().Zip(before).All(p => p.First == p.Second + 1), "Every owned cache and legacy suppression disposed once");
        Check(MagazineScratch().Count == 0 && BackpackScratch().Count == 0, "Dispose clears real magazine scratch buffers");
        Check(GrenadeScratchEmpty(), "Dispose clears real grenade scratch buffers");
        Check(ArmorScratch().Count == 0, "Dispose clears the real armor component scratch buffer");
        int[] disposed = CleanupCounts();
        opacity.Value += 0.01f; f.Panels.Tick(); f.Dispose();
        Check(PanelStyle.ThemeCalls == themes + 1, "Disposed owner cannot receive theme changes");
        Check(CleanupCounts().SequenceEqual(disposed), "Repeated Dispose and Tick are inert");

        using (var broken = new Fixture())
        {
            broken.Tick(); broken.Root.ThrowOnSetActive = true;
            int legacy = LegacyPanelSuppression.Disposed;
            broken.Dispose();
            Check(broken.Root == null && InfoPanelSettings.PanelOpacity.Subscribers == 0
                && LegacyPanelSuppression.Disposed == legacy + 1, "Hide exception cannot skip remaining disposal");
        }
        foreach (string stage in new[] { "weapon", "status", "legacy" })
        {
            WeaponInfoPanel.ThrowConstructor = stage == "weapon";
            PlayerStatusPanel.ThrowConstructor = stage == "status";
            LegacyPanelSuppression.ThrowConstructor = stage == "legacy";
            using var partial = new Fixture();
            partial.Panels.Tick();
            Check(partial.Root == null && InfoPanelSettings.PanelOpacity.Subscribers == 0,
                "Partial construction cleanup at " + stage);
            WeaponInfoPanel.ThrowConstructor = PlayerStatusPanel.ThrowConstructor = LegacyPanelSuppression.ThrowConstructor = false;
        }
    }
    // ArmorDurabilityCounter is real production code (linked, not a stub): it
    // has no call counter of its own, so its Reset() is checked directly via
    // its scratch buffer above, the same way the magazine and grenade scratch
    // buffers are checked instead of counted here.
    private static int[] CleanupCounts() => new[]
    {
        PanelStyle.ResetCalls, EmbeddedIcons.ResetCalls, GameIconLoader.ResetCalls,
        LooseAmmoCounter.ResetCalls, LegacyPanelSuppression.Disposed,
    };

    private static List<Magazine> MagazineScratch()
        => (List<Magazine>)typeof(MagazineInventoryCounter).GetField("_magazineScratch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static List<Magazine> BackpackScratch()
        => (List<Magazine>)typeof(MagazineInventoryCounter).GetField("_backpackScratch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static void MagazineCounting()
    {
        using var f = new Fixture(false);
        var equipment = f.Player.Inventory.Equipment;
        var rig = equipment.Put(EquipmentSlot.TacticalVest, new ArmoredRig { Grids = new[] { new Grid() } });
        var pockets = equipment.Put(EquipmentSlot.Pockets, new CompoundItem { Grids = new[] { new Grid() } });
        var backpack = equipment.Put(EquipmentSlot.Backpack, new CompoundItem { Grids = new[] { new Grid() } });
        var armor = equipment.Put(EquipmentSlot.ArmorVest, new CompoundItem { Grids = new[] { new Grid() } });
        var secure = equipment.Put(EquipmentSlot.SecuredContainer, new CompoundItem { Grids = new[] { new Grid() } });
        var weapon = new Weapon();
        ArenaMode.Kits.ArenaBackpackReload.AppendsBackpack = false;
        Magazine Put(CompoundItem container, int rounds, bool compatible = true)
        {
            var mag = new Magazine { Parent = container, Count = rounds, Compatible = compatible };
            container.Grids[0].Items.Add(mag); return mag;
        }
        void Counts(int full, int partial, string why)
        {
            Check(MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, weapon, out var result), why + " produces summary");
            Check(result.FullCount == full && result.PartialCount == partial,
                why + $" expected {full}/{partial}, got {result.FullCount}/{result.PartialCount}");
            Check(MagazineScratch().Count == 0 && BackpackScratch().Count == 0, why + " releases scratch references");
        }

        Put(rig, 30); Put(rig, 8);
        Counts(1, 1, "TacticalVest spares count as full and partial via the native reachable query");
        Put(pockets, 30); Put(pockets, 3);
        Counts(2, 2, "Pockets join the same native query");
        Put(backpack, 30); Put(backpack, 2);
        Counts(2, 2, "Backpack magazines are not counted while Arena's backpack-reload hook is off");

        ArenaMode.Kits.ArenaBackpackReload.AppendsBackpack = true;
        Counts(3, 3, "With the hook on, backpack top-level magazines count as full/partial like any other spare");

        Put(armor, 30); Put(secure, 30); Put(rig, 0); Put(pockets, 30, compatible: false);
        Put(backpack, 0); Put(backpack, 30, compatible: false);
        Counts(3, 3, "ArmorVest, secure container, empty and incompatible magazines are excluded");

        // Nested pouches are never counted, in the rig or the backpack: neither
        // the native reload query nor Arena's backpack candidate source go
        // deeper than one level (InventoryController.cs:887-907), so a pouch's
        // contents are not loadable by either.
        var rigPouch = new CompoundItem { Parent = rig, Grids = new[] { new Grid() } };
        rig.Grids[0].Items.Add(rigPouch);
        Put(rigPouch, 25);
        var backpackPouch = new CompoundItem { Parent = backpack, Grids = new[] { new Grid() } };
        backpack.Grids[0].Items.Add(backpackPouch);
        Put(backpackPouch, 25);
        Counts(3, 3, "Nested pouch magazines, in the rig or the backpack, are never counted");

        // Artificial (a real magazine cannot be both a direct rig grid item and
        // merged into a weapon at once), but it isolates the display rule in
        // the one-level model: a candidate the query would find must still be
        // excluded if GetRootItem resolves to a Weapon.
        var insertedElsewhere = new Magazine { Parent = weapon, MergedWithParent = true, Count = 30 };
        rig.Grids[0].Items.Add(insertedElsewhere);
        Counts(3, 3, "A magazine merged into a weapon is excluded even where the query would otherwise find it");

        weapon.MagazineSlot.ThrowAccept = true;
        bool threw = false;
        try { MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, weapon, out _); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && MagazineScratch().Count == 0 && BackpackScratch().Count == 0,
            "Compatibility exception propagates to UI but finally clears every scratch reference");
        weapon.MagazineSlot.ThrowAccept = false;
        Counts(3, 3, "Counter recovers cleanly after compatibility fault");

        weapon.ReloadMode = Weapon.EReloadMode.OnlyBarrel;
        Check(!MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, weapon, out var empty)
            && empty.FullCount == 0 && empty.PartialCount == 0, "Barrel-only weapon has no spare-mag summary");
        weapon.ReloadMode = Weapon.EReloadMode.ExternalMagazine;
        weapon.MagazineSlot = null;
        Check(!MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, weapon, out _), "Missing magazine slot excluded");
        Check(!MagazineInventoryCounter.TryGetSpareMagSummary(null, weapon, out _), "Missing player safe");
        Check(!MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, null, out _), "Missing weapon safe");
        f.Player.Inventory.Equipment = null;
        Check(!MagazineInventoryCounter.TryGetSpareMagSummary(f.Player, weapon, out _), "Missing equipment safe");
    }

    private static List<ThrowWeap> GrenadeScratch(string field)
        => (List<ThrowWeap>)typeof(GrenadeInventoryCounter).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static bool GrenadeScratchEmpty()
        => GrenadeScratch("_onGScratch").Count == 0 && GrenadeScratch("_carriedScratch").Count == 0;

    private static List<ArmorComponent> ArmorScratch()
        => (List<ArmorComponent>)typeof(ArmorDurabilityCounter).GetField("_componentScratch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static void GrenadeCounting()
    {
        using var f = new Fixture(false);
        var equipment = f.Player.Inventory.Equipment;
        var rig = equipment.Put(EquipmentSlot.TacticalVest, new ArmoredRig { Grids = new[] { new Grid() } });
        var pockets = equipment.Put(EquipmentSlot.Pockets, new CompoundItem { Grids = new[] { new Grid() } });
        var backpack = equipment.Put(EquipmentSlot.Backpack, new CompoundItem { Grids = new[] { new Grid() } });
        var armor = equipment.Put(EquipmentSlot.ArmorVest, new CompoundItem { Grids = new[] { new Grid() } });
        var secure = equipment.Put(EquipmentSlot.SecuredContainer, new CompoundItem { Grids = new[] { new Grid() } });
        ThrowWeap Put(CompoundItem container, ThrowWeap grenade)
        {
            grenade.Parent = container;
            container.Grids[0].Items.Add(grenade); return grenade;
        }
        ThrowWeap Typed(ThrowWeapType type, bool examined = true) => new ThrowWeap { ThrowType = type, IsExamined = examined };
        void Counts(int uf, int ul, int us, int gf, int gl, int gs, string why)
        {
            Check(GrenadeInventoryCounter.TryGetSummary(f.Player, out var r), why + " produces summary");
            Check(r.UsableFrag == uf && r.UsableFlash == ul && r.UsableSmoke == us
                  && r.OnGFrag == gf && r.OnGFlash == gl && r.OnGSmoke == gs,
                why + $" expected usable {uf}/{ul}/{us} onG {gf}/{gl}/{gs}, got usable "
                    + $"{r.UsableFrag}/{r.UsableFlash}/{r.UsableSmoke} onG {r.OnGFrag}/{r.OnGFlash}/{r.OnGSmoke}");
            // The UI reads only these accessors, so they must agree with the fields.
            Check(r.Usable(GrenadeKind.Frag) == uf && r.Usable(GrenadeKind.Flash) == ul && r.Usable(GrenadeKind.Smoke) == us
                  && r.OnG(GrenadeKind.Frag) == gf && r.OnG(GrenadeKind.Flash) == gl && r.OnG(GrenadeKind.Smoke) == gs
                  && r.IsEmpty == (uf + ul + us == 0),
                why + " accessors agree with the fields");
            Check(GrenadeScratchEmpty(), why + " releases scratch references");
        }

        Counts(0, 0, 0, 0, 0, 0, "No grenades carried gives an empty summary");
        var rigFrag = Put(rig, Typed(ThrowWeapType.frag_grenade));
        Put(pockets, Typed(ThrowWeapType.flash_grenade));
        Counts(1, 1, 0, 1, 1, 0, "Direct rig and pocket grenades are on G and usable");
        // No blinding, fragments or emission: only the type can place it.
        Put(rig, Typed(ThrowWeapType.stun_grenade));
        Counts(1, 2, 0, 1, 2, 0, "Stun grenades count as flash by type alone");

        // A grenade nested in a pouch inside the rig is still within the
        // native IsAtReachablePlace recursion (InventoryController.cs:448-464
        // walks the whole subtree, unlike the direct-children-only reachable
        // query) even with no widening at all; a backpack grenade is outside
        // both without it.
        var pouch = new CompoundItem { Parent = rig, Grids = new[] { new Grid() } }; rig.Grids[0].Items.Add(pouch);
        Put(pouch, Typed(ThrowWeapType.frag_grenade));
        Put(backpack, Typed(ThrowWeapType.smoke_grenade));
        Counts(2, 2, 0, 1, 2, 0, "A grenade nested inside the rig is reachable without Arena's reach widening; a backpack grenade is not");

        Put(armor, Typed(ThrowWeapType.frag_grenade)); Put(secure, Typed(ThrowWeapType.smoke_grenade));
        Counts(2, 2, 0, 1, 2, 0, "ArmorVest and secure container grenades are excluded");

        Put(pockets, Typed(ThrowWeapType.frag_grenade, examined: false));
        Counts(2, 2, 0, 1, 2, 0, "An unexamined pocket grenade is neither on G nor usable");

        rig.Slots = new[] { new Slot { ContainedItem = new ThrowWeap { Parent = rig, ThrowType = ThrowWeapType.smoke_grenade } } };
        Counts(2, 2, 1, 1, 2, 1, "A grenade in a direct rig slot is on G and usable");
        Counts(2, 2, 1, 1, 2, 1, "Repeated refreshes do not accumulate the native appending list");

        // Player.cs:22345-22360: the local controller does not go into a rig
        // that has not been searched, so G's own list drops its contents — but
        // IsAtReachablePlace (InventoryController.cs:448-464) never checks
        // search state, so number-key usability is unaffected.
        rig.Unsearched = true;
        Counts(2, 2, 1, 0, 1, 0, "An unsearched rig drops out of G's own candidate list, but native reach does not check search state");
        rig.Unsearched = false;
        Counts(2, 2, 1, 1, 2, 1, "Re-searching the rig restores its candidates on G");

        // ArenaReachPatches.ReachableAnywherePatch: any worn, examined grenade
        // anywhere in Equipment becomes reachable, without changing what G's
        // own candidate list is built from.
        f.Player.InventoryController.ArenaWidenReach = true;
        Counts(2, 2, 2, 1, 2, 1, "Arena's reach widening makes an examined backpack grenade usable without changing G's own list");
        f.Player.InventoryController.ArenaWidenReach = false;
        Counts(2, 2, 1, 1, 2, 1, "Turning the widening off drops the backpack grenade back out of the usable count");

        // Classification by effect, placed directly in pockets so they surface
        // in the summary without relying on the widening just exercised above.
        Put(pockets, new ThrowWeap { ThrowType = ThrowWeapType.gas_grenade, EmitTime = 30f });
        Put(pockets, new ThrowWeap { ThrowType = ThrowWeapType.incendiary_grenade, MaxExplosionDistance = 4f });
        Put(pockets, new ThrowWeap { ThrowType = ThrowWeapType.sonar, Blindness = new Vector3(0f, 12f, 0f) });
        Put(pockets, new ThrowWeap { ThrowType = (ThrowWeapType)99 });
        Counts(3, 3, 2, 2, 3, 2, "Unlisted types classify by effect; an effectless unknown type is skipped");
        // The other half of the explosive rule: fragments without a blast radius.
        Put(pockets, new ThrowWeap { ThrowType = (ThrowWeapType)42, FragmentsCount = 50 });
        Counts(4, 3, 2, 3, 3, 2, "An unknown type that throws fragments counts as frag");

        // A fault AFTER both scratch lists are filled, so a leak is observable:
        // the hook records their sizes at the moment of the throw. rigFrag is
        // classified in the on-G loop, which runs first, so both lists are
        // already complete when it fires.
        int onGAtThrow = -1, carriedAtThrow = -1;
        ThrowWeap.BeforeClassifyThrow = () =>
        {
            onGAtThrow = GrenadeScratch("_onGScratch").Count;
            carriedAtThrow = GrenadeScratch("_carriedScratch").Count;
        };
        rigFrag.ThrowOnClassify = true;
        bool threw = false;
        try { GrenadeInventoryCounter.TryGetSummary(f.Player, out _); }
        catch (InvalidOperationException) { threw = true; }
        finally { rigFrag.ThrowOnClassify = false; ThrowWeap.BeforeClassifyThrow = null; }
        Check(threw && onGAtThrow > 0 && carriedAtThrow > 0, "Classification fault fires with both grenade scratch lists filled");
        Check(GrenadeScratchEmpty(), "Classification exception propagates to UI but finally clears grenade scratch");
        Counts(4, 3, 2, 3, 3, 2, "Grenade counter recovers after a classification fault");

        // The native helper's own fault, after one grenade was already
        // appended, so a cleanup that only ran on success would leave it.
        int onGAtExamineThrow = -1;
        InventoryController.BeforeExamineThrow = () => onGAtExamineThrow = GrenadeScratch("_onGScratch").Count;
        f.Player.InventoryController.ExaminesBeforeThrow = 1;
        threw = false;
        try { GrenadeInventoryCounter.TryGetSummary(f.Player, out _); }
        catch (InvalidOperationException) { threw = true; }
        finally { f.Player.InventoryController.ResetExamineFault(); InventoryController.BeforeExamineThrow = null; }
        Check(threw && onGAtExamineThrow > 0, "Examine fault fires after the on-G list already holds a grenade");
        Check(GrenadeScratchEmpty(), "Examine exception propagates to UI but finally clears grenade scratch");
        Counts(4, 3, 2, 3, 3, 2, "Grenade counter recovers after an examine fault");

        Check(!GrenadeInventoryCounter.TryGetSummary(null, out _), "Grenades: missing player safe");
        // Natively Player.Inventory throws without a controller; the counter must
        // decide before touching it.
        f.Player.WithoutController = true;
        Check(!GrenadeInventoryCounter.TryGetSummary(f.Player, out _), "Grenades: missing inventory controller safe");
        f.Player.WithoutController = false;
        f.Player.Inventory.Equipment = null;
        Check(!GrenadeInventoryCounter.TryGetSummary(f.Player, out _), "Grenades: missing equipment safe");
    }

    private static void ArmorCounting()
    {
        using var f = new Fixture(false);
        var equipment = f.Player.Inventory.Equipment;
        var groups = new ArmorDurabilityCounter.GroupSummary[ArmorDurabilityCounter.GroupCount];
        ArmorComponent Armor(float current, float max) => new() { Repairable = new Repairable { Durability = current, MaxDurability = max } };

        void Expect(float headCurrent, float headMax, float bodyCurrent, float bodyMax, string why)
        {
            ArmorDurabilityCounter.FillSummary(f.Player, groups);
            var head = groups[(int)ArmorGroup.Head];
            var body = groups[(int)ArmorGroup.Body];
            Check(head.CurrentDurability == headCurrent && head.MaxDurability == headMax
                && body.CurrentDurability == bodyCurrent && body.MaxDurability == bodyMax,
                why + $" expected head {headCurrent}/{headMax} body {bodyCurrent}/{bodyMax}, got head "
                    + $"{head.CurrentDurability}/{head.MaxDurability} body {body.CurrentDurability}/{body.MaxDurability}");
            Check(ArmorScratch().Count == 0, why + " releases scratch references");
        }

        Expect(0, 0, 0, 0, "No armor worn");
        Check(!groups[(int)ArmorGroup.Head].HasArmor && !groups[(int)ArmorGroup.Body].HasArmor,
            "No armor worn leaves both groups without armor");

        var headwear = equipment.Put(EquipmentSlot.Headwear, new CompoundItem { Component = Armor(30, 30) });
        var vest = equipment.Put(EquipmentSlot.TacticalVest, new CompoundItem { Component = Armor(50, 100), Grids = new[] { new Grid() } });
        Expect(30, 30, 50, 100, "Headwear and TacticalVest each contribute their own soft armor to separate groups");

        var faceCover = equipment.Put(EquipmentSlot.FaceCover, new CompoundItem { Component = Armor(12, 12) });
        var eyewear = equipment.Put(EquipmentSlot.Eyewear, new CompoundItem { Component = Armor(5, 5) });
        Expect(47, 47, 50, 100, "FaceCover and Eyewear both land in the head group alongside Headwear");

        // Old per-zone model credited a back plate's full durability to BOTH
        // ThoraxBack and StomachBack, i.e. summed it twice into any aggregate
        // built by adding zones together; the replacement must count it once.
        var frontPlate = new Item { Parent = vest, Component = Armor(40, 40) };
        var backPlate = new Item { Parent = vest, Component = Armor(38, 40) };
        var sidePlate = new Item { Parent = vest, Component = Armor(35, 35) };
        vest.Slots = new[]
        {
            new Slot { ContainedItem = frontPlate },
            new Slot { ContainedItem = backPlate },
            new Slot { ContainedItem = sidePlate },
        };
        Expect(47, 47, 163, 215, "Front, back and side plates each count once, including the back plate that used to cover two zones");

        var visor = new Item { Parent = headwear, Component = Armor(18, 20) };
        headwear.Slots = new[] { new Slot { ContainedItem = visor } };
        Expect(65, 67, 163, 215, "A helmet's visor mod counts in the head group");

        var armorVest = equipment.Put(EquipmentSlot.ArmorVest, new CompoundItem { Component = Armor(25, 25) });
        Expect(65, 67, 188, 240, "ArmorVest and an armored TacticalVest are both in the body group");

        // Pockets is not one of BSG's own armor slots (Inventory.ArmorSlots is
        // exactly Headwear/FaceCover/Eyewear/TacticalVest/ArmorVest) and must
        // stay outside both HeadSlots and BodySlots. Without this, a stray
        // slot added to either array would go unnoticed: see Verify.ps1's
        // 'armor-pockets-added-to-body' negative control, which mutates
        // BodySlots to include Pockets and expects the line below to fail.
        var pocketsArmor = equipment.Put(EquipmentSlot.Pockets, new CompoundItem { Component = Armor(99, 99) });
        Expect(65, 67, 188, 240, "An armor component sitting in Pockets, outside HeadSlots/BodySlots, is never scanned");

        var noRepairable = new Item { Parent = vest, Component = new ArmorComponent { Repairable = null } };
        var zeroMax = new Item { Parent = vest, Component = Armor(10, 0) };
        vest.Grids[0].Items.Add(noRepairable);
        vest.Grids[0].Items.Add(zeroMax);
        Expect(65, 67, 188, 240, "Components without Repairable or with zero max durability are ignored");

        var faultyPlate = new Item { Parent = vest, ThrowOnComponentRead = true };
        vest.Slots = vest.Slots.Append(new Slot { ContainedItem = faultyPlate }).ToArray();
        bool threw = false;
        try { ArmorDurabilityCounter.FillSummary(f.Player, groups); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && ArmorScratch().Count == 0,
            "An exception mid-collection propagates to the UI but finally clears the scratch list");
        vest.Slots = vest.Slots.Where(s => s.ContainedItem != faultyPlate).ToArray();
        Expect(65, 67, 188, 240, "Armor counter recovers after a collection fault");

        f.Player.Inventory.Equipment = null;
        ArmorDurabilityCounter.FillSummary(f.Player, groups);
        Check(!groups[(int)ArmorGroup.Head].HasArmor && !groups[(int)ArmorGroup.Body].HasArmor, "Missing equipment safe");
        ArmorDurabilityCounter.FillSummary(null, groups);
        Check(!groups[(int)ArmorGroup.Head].HasArmor && !groups[(int)ArmorGroup.Body].HasArmor, "Missing player safe");
    }

    // Widths model production: a hidden slot keeps the stale width of its last
    // shown count, so packing must skip it by visibility, not by width.
    private static void Slots(float[] widths, bool[] visible, float a, bool showA, float b, bool showB, float c, bool showC)
    {
        widths[0] = a; widths[1] = b; widths[2] = c;
        visible[0] = showA; visible[1] = showB; visible[2] = showC;
    }

    private static void ReserveRow()
    {
        float[] w = new float[3], at = new float[3];
        bool[] v = new bool[3];

        // A typical rifle kit: full and partial magazines, one frag kind.
        Slots(w, v, 40f, true, 40f, true, 45f, false);
        float magRight = ReserveRowLayout.PackFromLeft(w, v, at);
        Check(at[0] == 0f && at[1] == 52f && magRight == 92f, "Magazine slots pack from the left edge one spacing apart");
        Slots(w, v, 37f, true, 37f, false, 37f, false);
        float grenades = ReserveRowLayout.PackFromRight(w, v, at);
        Check(at[0] == 0f && grenades == 37f, "Frag is the outermost grenade slot at the right edge");
        Near(ReserveRowLayout.FitScale(magRight, grenades), 1f, "Two magazine kinds and frags share one row at full scale");

        Slots(w, v, 40f, false, 40f, true, 40f, true);
        magRight = ReserveRowLayout.PackFromLeft(w, v, at);
        Check(at[1] == 0f && at[2] == 52f && magRight == 92f,
            "A hidden full-magazine slot takes no room: partial starts at the left edge");
        Slots(w, v, 37f, false, 37f, true, 37f, false);
        Check(ReserveRowLayout.PackFromRight(w, v, at) == 37f && at[1] == 0f, "Without frags the flash slot moves to the right edge");
        Slots(w, v, 40f, false, 40f, false, 40f, false);
        Check(ReserveRowLayout.PackFromLeft(w, v, at) == 0f && ReserveRowLayout.PackFromRight(w, v, at) == 0f,
            "No visible slot reports no content");

        // PackFromLeft takes any slot count; 144 px of left slots (three 40 px
        // ones here, or two wide loose-round counts in play) with frags and
        // flashes still fits at full scale, and only the third grenade kind
        // forces a shrink.
        Slots(w, v, 40f, true, 40f, true, 40f, true);
        magRight = ReserveRowLayout.PackFromLeft(w, v, at);
        Slots(w, v, 37f, true, 37f, true, 37f, false);
        grenades = ReserveRowLayout.PackFromRight(w, v, at);
        Check(at[0] == 0f, "Frag stays outermost when flashes are carried too");
        Check(at[1] == 47f && grenades == 84f, "Flash packs one spacing inside the frag slot");
        Check(magRight == 144f, "Three 40 px left slots pack to 144 px");
        Near(ReserveRowLayout.FitScale(magRight, grenades), 1f,
            "144 px of left slots with frags and flashes share one row at full scale");
        Slots(w, v, 37f, true, 37f, true, 37f, true);
        grenades = ReserveRowLayout.PackFromRight(w, v, at);
        Check(grenades == 131f, "Three grenade kinds pack to 131 px");
        Near(ReserveRowLayout.FitScale(magRight, grenades), 248f / 289f,
            "A crowded row (144 px of magazine slots, three grenade kinds) shrinks instead of wrapping");

        // The group gap is part of the fit even when the two groups alone
        // would leave room (150 + 90 = 240 fits; + the 14 px gap it does not).
        Check(ReserveRowLayout.FitScale(150f, 90f) < 1f,
            "The group gap is part of the fit: 240 px of slots need to shrink");
        Near(ReserveRowLayout.FitScale(100f, 134f), 1f, "Fit boundary is the exact content width including the group gap");
        Check(ReserveRowLayout.FitScale(100f, 134.5f) < 1f, "Half a pixel past the boundary already shrinks");
        // 240 px of grenades would not need to shrink beside even a zero-width group.
        Check(ReserveRowLayout.FitScale(0f, 240f) == 1f && ReserveRowLayout.FitScale(240f, 0f) == 1f,
            "One empty group always returns full scale");
        // Wide enough that the raw ContentWidth/needed ratio would fall below
        // the documented floor.
        Near(ReserveRowLayout.FitScale(300f, 113f), ReserveRowLayout.MinFitScale,
            "Extreme combined width clamps to the minimum scale instead of shrinking further");

        Near(ReserveRowLayout.SlotWidth(30f, 10f, 1, 18f), 40f, "Measured count width follows the icon column");
        // Not below the HUD font's real digits (10 px at 18 pt), or estimated
        // slots would overlap.
        Check(ReserveRowLayout.SlotWidth(30f, 0f, 2, 18f) >= 30f + 2 * 18f * 0.6f,
            "An unmeasured count falls back to a per-glyph estimate");
        Check(ReserveRowLayout.SlotWidth(30f, 1f, 1, 18f) == ReserveRowLayout.SlotWidth(30f, 0f, 1, 18f),
            "A measurement far below any real glyph width falls back to the estimate");
        float estimate = ReserveRowLayout.SlotWidth(27f, 0f, 1, 17f);
        Check(ReserveRowLayout.SlotWidth(27f, float.NaN, 1, 17f) == estimate
            && ReserveRowLayout.SlotWidth(27f, float.PositiveInfinity, 1, 17f) == estimate,
            "NaN or infinite measurements fall back to the estimate");
        Near(ReserveRowLayout.SlotWidth(30f, 1e6f, 3, 18f), 30f + ReserveRowLayout.ContentWidth,
            "An absurd measurement is capped at the content width");

        // Every visibility combination and width order: packed slots keep
        // their spacing and the reported extent covers exactly the visible ones.
        bool packed = true;
        float[] sizes = { 30f, 44f, 61f };
        for (int mask = 0; mask < 8; mask++)
        {
            for (int shift = 0; shift < 3; shift++)
            {
                for (int i = 0; i < 3; i++)
                {
                    w[i] = sizes[(i + shift) % 3];
                    v[i] = (mask & (1 << i)) != 0;
                }

                float right = ReserveRowLayout.PackFromLeft(w, v, at);
                float[] lefts = (float[])at.Clone();
                float width = ReserveRowLayout.PackFromRight(w, v, at);
                float leftExtent = 0f, rightExtent = 0f;
                int last = -1;
                for (int i = 0; i < 3; i++)
                {
                    if (!v[i]) continue;
                    if (last >= 0)
                    {
                        packed &= lefts[i] >= lefts[last] + w[last] + ReserveRowLayout.MagazineSpacing - 0.001f
                            && at[i] >= at[last] + w[last] + ReserveRowLayout.GrenadeSpacing - 0.001f;
                    }

                    leftExtent = Math.Max(leftExtent, lefts[i] + w[i]);
                    rightExtent = Math.Max(rightExtent, at[i] + w[i]);
                    last = i;
                }

                packed &= right == leftExtent && width == rightExtent;
            }
        }

        Check(packed, "Packed slots never overlap in any visibility combination and the extents cover them");
    }

    private static void CanvasVisibility()
    {
        var canvas = new Canvas();
        var screens = new EftScreenManager();
        EftScreenManager.Instance = screens;
        ArenaHudVisibility.Apply(canvas);
        Check(canvas.enabled && canvas.gameObject.activeSelf, "BattleUI draws the existing canvas");
        int objects = GameObject.Created.Count;
        foreach (EEftScreenType? screen in new EEftScreenType?[] { EEftScreenType.Inventory, EEftScreenType.Menu, EEftScreenType.Settings, null })
        {
            screens.CurrentScreen = screen;
            ArenaHudVisibility.Apply(canvas);
            Check(!canvas.enabled && canvas.gameObject.activeSelf, "Native screens stop canvas drawing without disabling its object");
        }
        EftScreenManager.Instance = null;
        ArenaHudVisibility.Apply(canvas);
        Check(!canvas.enabled, "Missing screen manager leaves canvas hidden");
        EftScreenManager.Instance = screens; screens.CurrentScreen = EEftScreenType.BattleUI;
        ArenaHudVisibility.Apply(canvas); ArenaHudVisibility.Apply(null);
        Check(canvas.enabled && canvas.gameObject.activeSelf && GameObject.Created.Count == objects,
            "BattleUI return reuses the same active canvas without new objects");
    }

    private static int Main()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "ArenaInfoPanels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        try
        {
            Migration(); CanvasVisibility(); Context(); RefreshAndLifecycle(); ErrorIsolation(); Disposal(); MagazineCounting(); GrenadeCounting(); ArmorCounting(); ReserveRow();
            Console.WriteLine($"ARENA_INFO_PANEL_PRODUCTION_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            foreach (string file in Directory.GetFiles(_scratch)) File.Delete(file);
            Directory.Delete(_scratch);
        }
    }
}
