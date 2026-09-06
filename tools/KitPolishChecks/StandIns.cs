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
        public int ArmorClass { get; set; }
        public EBodyPartColliderType[] ArmorColliders { get; set; } = [];
        public EArmorPlateCollider ArmorPlateColliders { get; set; }
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
            var template = new ArmorTemplate
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
            factory.ItemTemplates[new MongoID(entry.Name)] = template;
        }
        Comfort.Common.Singleton<EFT.ItemFactory>.Instance = factory;
    }
    private static T[] Enums<T>(JToken values) where T : struct, Enum
        => (values as JArray ?? []).Select(v => Enum.TryParse<T>(v.ToString(), out var e) ? e : default).ToArray();
    private static EArmorPlateCollider Mask(JToken values)
        => Enums<EArmorPlateCollider>(values).Aggregate((EArmorPlateCollider)0, (m, e) => m | e);
}
