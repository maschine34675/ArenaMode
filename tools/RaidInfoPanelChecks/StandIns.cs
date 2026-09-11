// External contracts only. Production context, refresh, migration and ownership
// logic are linked unchanged by the project. These are not native Unity/EFT tests.
using System.Globalization;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal static int DestroyCalls;
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            DestroyCalls++;
            value.Destroyed = true;
        }
        public static bool operator ==(Object left, Object right)
        {
            bool l = ReferenceEquals(left, null) || left.Destroyed;
            bool r = ReferenceEquals(right, null) || right.Destroyed;
            return l || r ? l == r : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    internal struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
    }
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    internal struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    internal class Transform : Object
    {
        public GameObject gameObject;
        public Transform Parent;
        public bool ActiveInHierarchy => (gameObject == null || gameObject.activeSelf) && (Parent == null || Parent.ActiveInHierarchy);
        public void SetParent(Transform parent, bool world) => Parent = parent;
    }
    internal class RectTransform : Transform { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax; }
    internal class GameObject : Object
    {
        internal static readonly List<GameObject> Created = new();
        public readonly string name;
        public Transform transform = new RectTransform();
        public bool activeSelf = true;
        public bool ThrowOnSetActive;
        public GameObject(string name, params Type[] types) { this.name = name; transform.gameObject = this; Created.Add(this); }
        public void SetActive(bool active)
        { if (ThrowOnSetActive) throw new InvalidOperationException("injected activation failure"); activeSelf = active; }
    }
    internal static class Time { public static float unscaledTime; }
}

namespace Comfort.Common
{
    internal static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    internal enum GameStatus { Starting, Started, Stopping, Stopped }
    internal enum EPointOfView { FirstPerson, ThirdPerson }
    internal class AbstractGame : UnityEngine.Object { public GameStatus Status = GameStatus.Started; }
    internal class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public readonly List<Player> AllAlivePlayersList = new();
    }
    internal class Player : UnityEngine.Object
    {
        public string Name;
        public bool IsYourPlayer = true;
        public object PlayerBody = new();
        public bool ThrowOnHands;
        private object _hands = new();
        public object HandsController
        { get => ThrowOnHands ? throw new InvalidOperationException("injected body read failure") : _hands; set => _hands = value; }
        public Health HealthController = new();
        public EPointOfView PointOfView = EPointOfView.FirstPerson;
        public bool WithoutController;
        private readonly EFT.InventoryLogic.InventoryController _controller = new();
        public EFT.InventoryLogic.InventoryController InventoryController => WithoutController ? null : _controller;
        // Native Player.Inventory is `InventoryController.Inventory` (Player.cs:24511):
        // it throws when the controller is missing. The setter only lets the
        // context checks simulate an uninitialized inventory.
        public Inventory Inventory { get => InventoryController.Inventory; set => InventoryController.Inventory = value; }
    }
    internal class Health { public bool IsAlive = true; }
    internal class Inventory
    {
        public EFT.InventoryLogic.InventoryEquipment Equipment = new();
        // Static and mutable like the native field (Inventory.cs:193-197), which
        // the installed UseItemsAnywhere 1.4.0 rewrites at startup.
        public static EFT.InventoryLogic.EquipmentSlot[] FastAccessSlots = Vanilla;
        public static EFT.InventoryLogic.EquipmentSlot[] Vanilla
            => new[] { EFT.InventoryLogic.EquipmentSlot.Pockets, EFT.InventoryLogic.EquipmentSlot.TacticalVest };
    }
}
namespace EFT.InventoryLogic
{
    internal enum EquipmentSlot { TacticalVest, Pockets, Backpack, ArmorVest, SecuredContainer, FirstPrimaryWeapon, SecondPrimaryWeapon, ArmBand }
    internal class Item
    {
        public Item Parent;
        public bool MergedWithParent;
        public bool IsChildOf(Item ancestor)
        { for (Item p = Parent; p != null; p = p.Parent) if (ReferenceEquals(p, ancestor)) return true; return false; }
        // Native ItemExtensions.GetRootItem follows only merged parents. A mag
        // inserted in a weapon merges with it; inventory grid contents do not.
        public Item GetRootItem()
        { Item p = this; while (p.MergedWithParent && p.Parent != null) p = p.Parent; return p; }
    }
    internal class CompoundItem : Item
    {
        public Grid[] Grids = Array.Empty<Grid>(); public Slot[] Slots = Array.Empty<Slot>();
        // Models a SearchableItem (Vest/Pockets) that the local body's search
        // controller has not marked searched (PlayerSearchController.IsSearched).
        public bool Unsearched;
    }
    internal sealed class ArmoredRig : CompoundItem { } // Type must not change TacticalVest eligibility.
    internal sealed class Grid { public List<Item> Items = new(); }
    internal class Magazine : Item { public int Count, MaxCount = 30; public bool Compatible = true; }
    internal class Slot
    {
        public Item ContainedItem;
        public bool ThrowAccept;
        public bool CanAccept(Magazine magazine)
            => ThrowAccept ? throw new InvalidOperationException("injected compatibility failure") : magazine.Compatible;
    }
    internal class Weapon : CompoundItem
    {
        public enum EReloadMode { ExternalMagazine, OnlyBarrel }
        public EReloadMode ReloadMode;
        public Slot MagazineSlot = new();
        public Slot GetMagazineSlot() => MagazineSlot;
    }
    internal enum ThrowWeapType
    { frag_grenade = 0, flash_grenade = 1, stun_grenade = 2, smoke_grenade = 3, gas_grenade = 4, incendiary_grenade = 5, sonar = 6 }
    // Native values live on ThrowWeapTemplate (ThrowWeapTemplate.cs:5-42); only
    // the fields the classifier reads are modeled.
    internal class ThrowWeap : Item
    {
        private ThrowWeapType _throwType;
        public ThrowWeapType ThrowType
        {
            get
            {
                if (!ThrowOnClassify) return _throwType;
                BeforeClassifyThrow?.Invoke();
                throw new InvalidOperationException("injected classification failure");
            }
            set => _throwType = value;
        }
        public int FragmentsCount;
        public float MaxExplosionDistance, EmitTime;
        public UnityEngine.Vector3 Blindness;
        public bool IsExamined = true;
        public bool ThrowOnClassify;
        public static Action BeforeClassifyThrow;
    }
    internal sealed class InventoryController
    {
        // Auto-property natively (InventoryController.cs:168).
        public EFT.Inventory Inventory = new();
        public int ExaminesBeforeThrow = -1;
        private int _examines;
        // Mirrors the local body's controller: the base helper
        // (InventoryController.cs:882-907) over the static, mutable
        // Inventory.FastAccessSlots, each container's slots before its grids
        // (CompoundItem.cs:144-157), DIRECT children only, appending to the
        // caller's list without clearing it; plus the PlayerOwnerInventoryController
        // override that skips an unsearched SearchableItem container
        // (Player.cs:22345-22360). UnknownItem is not modeled: an `is ThrowWeap`
        // match already excludes it.
        public void GetReachableItemsOfTypeNonAlloc<TItem>(IList<TItem> list, Predicate<TItem> predicate = null) where TItem : Item
        {
            foreach (var slotName in EFT.Inventory.FastAccessSlots)
            {
                if (Inventory.Equipment.GetSlot(slotName).ContainedItem is not CompoundItem container || container.Unsearched) continue;
                foreach (Slot slot in container.Slots)
                    if (slot.ContainedItem is TItem a && (predicate == null || predicate(a))) list.Add(a);
                foreach (Grid grid in container.Grids)
                    foreach (Item item in grid.Items)
                        if (item is TItem b && (predicate == null || predicate(b))) list.Add(b);
            }
        }
        public bool Examined(Item item)
        {
            if (ExaminesBeforeThrow >= 0 && _examines++ >= ExaminesBeforeThrow)
                throw new InvalidOperationException("injected examine failure");
            return item is not ThrowWeap grenade || grenade.IsExamined;
        }
        public void ResetExamineFault() { ExaminesBeforeThrow = -1; _examines = 0; }
    }
    internal sealed class InventoryEquipment : CompoundItem
    {
        private readonly Dictionary<EquipmentSlot, Slot> _slots = Enum.GetValues<EquipmentSlot>().ToDictionary(s => s, _ => new Slot());
        public InventoryEquipment() => Slots = _slots.Values.ToArray();
        public Slot GetSlot(EquipmentSlot slot) => _slots[slot];
        public T Put<T>(EquipmentSlot slot, T item) where T : Item
        { GetSlot(slot).ContainedItem = item; item.Parent = this; return item; }
    }
}
namespace ArenaMode
{
    internal static class Plugin { public static readonly Logger ModLogger = new(); }
    internal sealed class Logger
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Infos = new();
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogInfo(string message) => Infos.Add(message);
    }
}
namespace ArenaMode.Config
{
    internal static class Settings
    { public static readonly BepInEx.Configuration.ConfigEntry<bool> HudEnabled = new(true); }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.AbstractGame ExactGame;
        public static EFT.GameWorld ExactWorld;
        public static int ExactChecks;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
        {
            ExactChecks++;
            return IsActive && ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
        }
    }
}
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode.Spectator { internal static class ArenaSpectatorService { public static bool IsPending, IsActive; } }
namespace ArenaMode.Modes
{
    internal sealed class ArenaMatch { }
    internal static class ArenaMatchService { public static ArenaMatch Current; }
}

namespace BepInEx.Configuration
{
    // Models the public Bind contract: loaded/unbound values beat defaults;
    // SaveOnConfigSet controls writes. Vector/color parsing here is a fixture
    // format and is NOT evidence for BepInEx's real converter or disk syntax.
    internal sealed class ConfigEntry<T>
    {
        private T _value;
        private EventHandler _changed;
        public ConfigEntry(T value) => _value = value;
        public T Value { get => _value; set { _value = value; _changed?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler SettingChanged { add => _changed += value; remove => _changed -= value; }
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
    }
    internal sealed class ConfigFile
    {
        internal static readonly List<ConfigFile> Instances = new();
        public readonly string Path;
        public readonly Dictionary<string, string> Raw = new();
        public readonly Dictionary<string, object> Bound = new();
        public readonly List<string> BindCalls = new();
        public bool SaveOnConfigSet { get; set; } = true;
        public int WriteCalls;
        public ConfigFile(string path, bool saveOnInit)
        {
            Path = path; Instances.Add(this);
            string section = "";
            if (File.Exists(path)) foreach (string input in File.ReadAllLines(path))
            {
                string line = input.Trim();
                if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
                else if (line.Contains('='))
                { int split = line.IndexOf('='); Raw[section + "/" + line[..split].Trim()] = line[(split + 1)..].Trim(); }
            }
            if (saveOnInit) Save();
        }
        public ConfigEntry<T> Bind<T>(string section, string key, T fallback, string description = null)
        {
            string id = section + "/" + key; BindCalls.Add(id);
            if (Bound.TryGetValue(id, out var bound)) return (ConfigEntry<T>)bound;
            T value = Raw.TryGetValue(id, out string text) ? Parse<T>(text) : fallback;
            var result = new ConfigEntry<T>(value); Bound[id] = result;
            if (SaveOnConfigSet) Save();
            return result;
        }
        private void Save() { WriteCalls++; File.AppendAllText(Path, "\n# stub write\n"); }
        private static T Parse<T>(string text)
        {
            if (typeof(T) == typeof(bool)) return (T)(object)bool.Parse(text);
            if (typeof(T) == typeof(float)) return (T)(object)float.Parse(text, CultureInfo.InvariantCulture);
            float[] components = text.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (typeof(T) == typeof(UnityEngine.Vector2)) return (T)(object)new UnityEngine.Vector2(components[0], components[1]);
            if (typeof(T) == typeof(UnityEngine.Color)) return (T)(object)new UnityEngine.Color(components[0], components[1], components[2], components[3]);
            throw new NotSupportedException(typeof(T).FullName);
        }
    }
}

namespace ArenaMode.Hud.InfoPanels.UI
{
    internal abstract class PanelProbe
    {
        public bool Visible;
        public int Updates, Layouts;
        public EFT.Player LastPlayer;
        public bool ThrowUpdate, ThrowLayout;
        public void ApplyLayout() { Layouts++; if (ThrowLayout) throw new InvalidOperationException("layout failed"); }
        public void UpdateFromPlayer(EFT.Player player)
        { Updates++; if (ThrowUpdate) throw new InvalidOperationException("data failed"); LastPlayer = player; }
        public void SetVisible(bool value) => Visible = value;
    }
    internal sealed class WeaponInfoPanel : PanelProbe
    {
        public static WeaponInfoPanel Latest;
        public static bool ThrowConstructor;
        public WeaponInfoPanel(UnityEngine.RectTransform rect)
        {
            // Actual TMP Awake initializes CanvasRenderer only in an active
            // hierarchy; constructor-time outline setters require that field.
            if (!rect.ActiveInHierarchy) throw new NullReferenceException("TMP outline before active-hierarchy Awake");
            if (ThrowConstructor) throw new InvalidOperationException("weapon construction failed"); Latest = this;
        }
    }
    internal sealed class PlayerStatusPanel : PanelProbe
    {
        public static PlayerStatusPanel Latest;
        public static bool ThrowConstructor;
        public PlayerStatusPanel(UnityEngine.RectTransform rect)
        {
            if (!rect.ActiveInHierarchy) throw new NullReferenceException("TMP outline before active-hierarchy Awake");
            if (ThrowConstructor) throw new InvalidOperationException("status construction failed"); Latest = this;
        }
    }
    internal static class PanelStyle
    { public static int ThemeCalls, ResetCalls; public static void ApplyTheme() => ThemeCalls++; public static void Reset() => ResetCalls++; }
    internal static class EmbeddedIcons { public static int ResetCalls; public static void Reset() => ResetCalls++; }
    internal static class GameIconLoader { public static int ResetCalls; public static void Reset() => ResetCalls++; }
}
namespace ArenaMode.Hud.InfoPanels
{
    internal sealed class LegacyPanelSuppression : IDisposable
    {
        public static int Created, Disposed;
        public static bool ThrowConstructor;
        public LegacyPanelSuppression() { Created++; if (ThrowConstructor) throw new InvalidOperationException("legacy construction failed"); }
        public void Dispose() => Disposed++;
    }
    internal static class LooseAmmoCounter { public static int ResetCalls; public static void Reset() => ResetCalls++; }
    internal static class ArmorDurabilityCounter { public static int ResetCalls; public static void Reset() => ResetCalls++; }
}
