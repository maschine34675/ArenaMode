// Offline boundary stand-ins, not copies of native implementations. Real kit
// templates are projected below; production KitSummary is compiled unchanged.
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;

public readonly record struct MongoID(string Value);
[Flags] public enum EArmorPlateCollider : short
{
    Plate_Granit_SAPI_chest=1, Plate_Granit_SAPI_back=2,
    Plate_Granit_SSAPI_side_left_high=4, Plate_Granit_SSAPI_side_left_low=8,
    Plate_Granit_SSAPI_side_right_high=16, Plate_Granit_SSAPI_side_right_low=32,
    Plate_Korund_chest=64, Plate_6B13_back=128,
    Plate_Korund_side_left_high=256, Plate_Korund_side_left_low=512,
    Plate_Korund_side_right_high=1024, Plate_Korund_side_right_low=2048
}
public enum EBodyPartColliderType
{
    None=-1,HeadCommon=0,RibcageUp=1,Pelvis=3,LeftUpperArm=4,LeftForearm=5,
    RightUpperArm=6,RightForearm=7,LeftThigh=8,LeftCalf=9,RightThigh=10,RightCalf=11,
    ParietalHead=12,BackHead=13,Ears=14,Eyes=15,Jaw=16,NeckFront=17,NeckBack=18,
    RightSideChestUp=19,LeftSideChestUp=20,SpineTop=21,SpineDown=22,PelvisBack=23,
    RightSideChestDown=24,LeftSideChestDown=25,RibcageLow=26
}
public class ArmorSlot : Slot { }
public static class Localization { public static string Localized(this string text) => text; }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode
{
    public static class Plugin { public static readonly TestLogger ModLogger = new(); }
    public class TestLogger
    {
        public readonly List<string> Warnings = new(), Infos = new();
        public void LogWarning(string text) => Warnings.Add(text);
        public void LogInfo(string text) => Infos.Add(text);
    }
}
namespace EFT
{
    public class ItemFactory { public Dictionary<MongoID, ItemTemplate> ItemTemplates = new(); }
}
namespace EFT.InventoryLogic
{
    public class ItemTemplate { public string Name, ShortNameLocalizationKey; }
    public class CompoundItemTemplate : ItemTemplate { public Slot[] Slots = []; }
    public interface IArmorComponentTemplate
    {
        public int ArmorClass { get; }
        public EBodyPartColliderType[] ArmorColliders { get; }
        public EArmorPlateCollider ArmorPlateColliders { get; }
    }
    public class ArmorTemplate : CompoundItemTemplate, IArmorComponentTemplate
    {
        public virtual int ArmorClass { get; set; }
        public EBodyPartColliderType[] ArmorColliders { get; set; } = [];
        public EArmorPlateCollider ArmorPlateColliders { get; set; }
    }
    // Native VestTemplate differs from ArmoredEquipmentTemplate: its optional
    // plate array is not normalized, and reading the mask with null throws.
    // This boundary behavior is checked against the installed native IL.
    public class VestTemplate : CompoundItemTemplate, IArmorComponentTemplate
    {
        public int armorClass;
        public EBodyPartColliderType[] armorColliders;
        public EArmorPlateCollider[] armorPlateColliders;
        public int ArmorClass => armorClass;
        public EBodyPartColliderType[] ArmorColliders => armorColliders;
        public EArmorPlateCollider ArmorPlateColliders
        {
            get
            {
                var mask = (EArmorPlateCollider)0;
                for (int i = 0; i < armorPlateColliders.Length; i++) mask |= armorPlateColliders[i];
                return mask;
            }
        }
    }
    public class BrokenArmorTemplate : ArmorTemplate
    {
        public override int ArmorClass { get => throw new InvalidOperationException("broken custom armor metadata"); set { } }
    }
    public class Slot
    {
        public string ID;
        public EBodyPartColliderType[] ArmorColliders = [];
        public EArmorPlateCollider ArmorPlateColliderMask;
    }
}
internal static class DatabaseBoundary
{
    public static void Install(JObject db)
    {
        var factory = new EFT.ItemFactory();
        foreach (var entry in db.Properties())
        {
            var p = entry.Value["_props"];
            var armor = new ArmorTemplate
            {
                Name = entry.Value["_name"]?.Value<string>(),
                ArmorClass = int.TryParse(p?["armorClass"]?.ToString(), out int level) ? level : 0,
                ArmorColliders = Enums<EBodyPartColliderType>(p?["armorColliders"]),
                ArmorPlateColliders = Mask(p?["armorPlateColliders"]),
                Slots = (p?["Slots"] as JArray ?? []).Select(s =>
                {
                    Slot slot = s["_proto"]?.Value<string>() == "64479fdf9731c8fadc0642c1" ? new ArmorSlot() : new Slot();
                    slot.ID = s["_name"]?.Value<string>();
                    var filters = s["_props"]?["filters"] as JArray ?? [];
                    slot.ArmorColliders = filters.SelectMany(f => Enums<EBodyPartColliderType>(f["armorColliders"])).ToArray();
                    slot.ArmorPlateColliderMask = filters.Aggregate((EArmorPlateCollider)0, (mask, f) => mask | Mask(f["armorPlateColliders"]));
                    return slot;
                }).ToArray()
            };
            CompoundItemTemplate template = armor;
            if (entry.Value["_parent"]?.Value<string>() == "5448e5284bdc2dcb718b4567")
                template = new VestTemplate
                {
                    Name = armor.Name, Slots = armor.Slots, armorClass = armor.ArmorClass,
                    armorColliders = p?["armorColliders"] is JArray ? armor.ArmorColliders : null,
                    armorPlateColliders = p?["armorPlateColliders"] is JArray ? Enums<EArmorPlateCollider>(p["armorPlateColliders"]) : null
                };
            factory.ItemTemplates[new MongoID(entry.Name)] = template;
        }
        Comfort.Common.Singleton<EFT.ItemFactory>.Instance = factory;
    }
    private static T[] Enums<T>(JToken values) where T : struct, Enum
        => (values as JArray ?? []).Select(v => Enum.TryParse<T>(v.ToString(), out var e) ? e : default).ToArray();
    private static EArmorPlateCollider Mask(JToken values)
        => Enums<EArmorPlateCollider>(values).Aggregate((EArmorPlateCollider)0, (m, e) => m | e);
}
