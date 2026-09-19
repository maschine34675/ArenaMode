using ArenaMode.Kits;
using ArenaMode.Pickups;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string message){_checks++;if(!value)throw new Exception("ASSERT: "+message);}
    private static JToken At(JToken token,params string[] path)
    {foreach(var key in path)token=(token as JObject)?.GetValue(key,StringComparison.OrdinalIgnoreCase);return token;}
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Tree(List<KitItemNode> items,string root)
    {
        var rows=new List<string>();
        void Visit(string id,string path)
        {
            if(rows.Count>64)throw new Exception("cyclic/oversized weapon tree");
            var item=items.Single(n=>n.Id==id);rows.Add(path+"="+item.Tpl);
            foreach(var child in items.Where(n=>n.ParentId==id&&n.SlotId!="cartridges"))Visit(child.Id,path+"/"+child.SlotId);
        }
        Visit(root,"weapon");return rows.Order().ToArray();
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length<1||args.Length>2)throw new ArgumentException("SPT root and optional read-only source profile expected");
            string spt=args[0];
            var kit=ArenaPickupWeapons.Find(ArenaPickupWeapons.M60Id);var other=ArenaPickupWeapons.CreateM60();
            Check(kit.Items.Count==15&&kit.Fill.SpareMagazines==0&&kit.Fill.RigContents.Count==0,"pickup includes only weapon tree and loaded ammo");
            var weapon=kit.Items.Single(n=>n.SlotId=="FirstPrimaryWeapon");
            Check(weapon.ParentId=="equipment"&&weapon.Tpl=="661cec09b2c6356b4d0c7a36","preload-visible M60 root under synthetic equipment");
            Check(KitPreloadPlan.TemplatesOf(kit).Contains(weapon.Tpl),"actual preloader includes weapon root");
            kit.Items[1].Upd["Repairable"]["Durability"]=1;
            Check((int)other.Items[1].Upd["Repairable"]["Durability"]==100,"definitions and mutable upd are fresh per request");
            kit=other;
            Check(KitCatalog.Kits.Count==1&&ArenaPickupWeapons.Find("custom")==KitCatalog.Kits[0]&&ArenaPickupWeapons.Find("missing")==null,"resolver keeps normal catalog and explicit custom selection intact");
            string dbPath=Path.Combine(spt,"SPT_Runtime/SPT_Data/database/templates/items.json"),dbHash=Hash(dbPath);
            var db=JObject.Parse(File.ReadAllText(dbPath));
            foreach(var node in kit.Items)
            {
                Check(db[node.Tpl]!=null,"every copied template exists");
                if(node.ParentId==null||node.ParentId=="equipment"||node.SlotId=="cartridges")continue;
                var parent=kit.Items.Single(n=>n.Id==node.ParentId);
                var slot=db[parent.Tpl]["_props"]["Slots"].Single(s=>(string)s["_name"]==node.SlotId);
                Check(slot["_props"]["filters"].Any(f=>f["Filter"].Values<string>().Contains(node.Tpl)),"copied attachment accepted by native slot");
            }
            foreach(var node in kit.Items.Where(n=>n.Id!="equipment"))
                foreach(var slot in db[node.Tpl]["_props"]["Slots"]??new JArray())
                    if((bool?)slot["_required"]==true)Check(kit.Items.Any(n=>n.ParentId==node.Id&&n.SlotId==(string)slot["_name"]),"all required native slots populated");
            var ammo=kit.Items.Where(n=>n.SlotId=="cartridges").ToList();
            Check(ammo.Sum(n=>(int)n.Upd["StackObjectsCount"])==100&&ammo.All(n=>n.Tpl=="5efb0c1bd79ff02a1f5e68d9"&&(int)n.Upd["StackObjectsCount"]<=(int)db[n.Tpl]["_props"]["StackMaxSize"]),"100 M993 rounds use legal native stacks");
            Check((int)db["660ea4453786cc0af808a1be"]["_props"]["Cartridges"][0]["_max_count"]==100,"ammo count fits Capco capacity");
            if(args.Length==2)
            {
                string path=args[1];
                string before=Hash(path);var profile=JObject.Parse(File.ReadAllText(path));
                Check((string)At(profile,"characters","pmc","Info","Nickname")=="spt41dev","requested source profile verified");
                var preset=(At(profile,"userbuilds","weaponBuilds")??new JArray()).Single(p=>(string)At(p,"Name")=="retzrestdfg");
                var source=At(preset,"Items").ToObject<List<KitItemNode>>();
                string root=(string)At(preset,"Root");
                Check(Tree(source,root).SequenceEqual(Tree(kit.Items,weapon.Id)),"copied attachment tree exactly matches retzrestdfg preset");
                Check(before==Hash(path),"source profile remains byte-identical");
                Console.WriteLine("Read-only preset source SHA256="+before);
            }
            Check(Hash(dbPath)==dbHash,"native database remains unchanged");
            Console.WriteLine($"ARENA M60 DEFINITION PASSED: {_checks} assertions");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
namespace ArenaMode.Kits { public static class KitCatalog { public static List<KitDefinition> Kits=[new(){Id="custom"}]; } }
namespace ArenaMode.Shared { public static class ArenaPopulationPolicy { public const string Tactical="tactical"; } }
