using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Created = new();
        public Object() { Created.Add(this); }
        public virtual string name { get; set; }
        public bool Destroyed;
        public static void Destroy(Object o)
        {
            if (o == null || o.Destroyed) return;
            o.Destroyed = true;
            if (o is GameObject g) foreach (var c in g.Components.ToArray()) Destroy(c);
            else o.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(o, null);
        }
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy=true;
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public GameObject(string name = "object") { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool all) where T : class => Components.OfType<T>().Concat(transform.Children.SelectMany(x => x.gameObject.GetComponentsInChildren<T>(all))).ToArray();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public override string name { get => gameObject?.name; set { if (gameObject != null) gameObject.name = value; } }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool all) where T : class => gameObject.GetComponentsInChildren<T>(all);
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform value) { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
    }
    public class MeshFilter : Component
    {
        private Mesh _mesh;
        public bool FailNextAssignment;
        public Mesh sharedMesh { get=>_mesh; set { if(FailNextAssignment) { FailNextAssignment=false;throw new Exception("injected native mesh setter failure"); } _mesh=value; } }
    }
    public class MeshRenderer : Component
    {
        public bool enabled = true;
        private Material[] _materials = Array.Empty<Material>();
        public bool FailNextAssignment;
        public Material[] sharedMaterials { get => _materials.ToArray(); set { if (FailNextAssignment) { FailNextAssignment = false; throw new Exception("injected native material setter failure"); } _materials = value.ToArray(); } }
    }
    public class Shader : Object
    {
        public static bool Missing;
        public static Shader Find(string name) => Missing ? null : new Shader { name = name };
    }
    public class Material : Object
    {
        public static bool FailNextProperty;
        public readonly Shader shader;
        public Color color;
        public readonly Dictionary<string, float> Values = new();
        public Material(Shader shader) { this.shader = shader; }
        public bool HasProperty(string property) => true;
        public void SetFloat(string property, float value) { if(FailNextProperty) { FailNextProperty=false;throw new Exception("native material property failed"); } Values[property]=value; }
        public void EnableKeyword(string keyword) { }
        public void SetColor(string property, Color color) { }
    }
    public class Mesh : Object
    {
        public static bool FailNextUpload;
        public int subMeshCount;
        public Bounds bounds;
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector2> Uv = new();
        public readonly Dictionary<int, List<int>> Triangles = new();
        public void SetVertices(List<Vector3> vertices) { if(FailNextUpload) { FailNextUpload=false;throw new Exception("native mesh upload failed"); } Vertices.Clear(); Vertices.AddRange(vertices); }
        public void SetUVs(int channel, List<Vector2> uv) { Uv.Clear(); Uv.AddRange(uv); }
        public void SetTriangles(List<int> triangles, int submesh) => Triangles[submesh] = new List<int>(triangles);
        public void RecalculateNormals() { }
        public void RecalculateBounds()
        {
            var low = new Vector3(Vertices.Min(x => x.x), Vertices.Min(x => x.y), Vertices.Min(x => x.z));
            var high = new Vector3(Vertices.Max(x => x.x), Vertices.Max(x => x.y), Vertices.Max(x => x.z));
            bounds = new Bounds { center = (low + high) * .5f, extents = (high - low) * .5f };
        }
    }
    public struct Bounds { public Vector3 center, extents; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float k) => new(a.x*k,a.y*k,a.z*k);
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Quaternion { }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1) { this.r=r; this.g=g; this.b=b; this.a=a; } }
    public static class Mathf { public const float PI = MathF.PI; public static float Cos(float x)=>MathF.Cos(x); public static float Sin(float x)=>MathF.Sin(x); }
    public static class Time { public static int frameCount; }
}

namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { TacticalVest, Pockets, Backpack }
    public enum EBoundItem { Item4, Item5, Item6, Item7, Item8, Item9, Item10 }
    public enum CommandStatus { Begin, Succeed }
    public struct ResourceKey { public string path; }
    public class ItemTemplate { }
    public class PortableRangeFinderTemplate : ItemTemplate
    {
        public string _id, ParentId;
        public int Width=1, Height=1, StackMaxSize=1;
        public bool ExaminedByDefault=true, CanSellOnRagfair, CanRequireOnRagfair, LeftHandItem;
        public ResourceKey Prefab, UsePrefab;
    }
    public class Item
    {
        public string TemplateId, Id;
        public Item Parent;
        public bool Reachable=true, Bindable=true, ThrowOnGraph;
        public bool IsChildOf(Item ancestor) { if (ThrowOnGraph) throw new Exception("retired graph"); for(var p=Parent;p!=null;p=p.Parent) if(ReferenceEquals(p,ancestor)) return true; return false; }
    }
    public class PortableRangeFinder : Item { }
    public class CompoundItem : Item { public Grid[] Grids; }
    public class InventoryEquipment : CompoundItem
    {
        public Dictionary<EquipmentSlot,Slot> Slots=new();
        public Slot GetSlot(EquipmentSlot slot) => Slots.GetValueOrDefault(slot);
    }
    public class Slot { public Item ContainedItem; }
    public class Location { }
    public class Grid
    {
        public CompoundItem Container;
        public List<Item> Items=new();
        public int Capacity=10;
        public Location FindFreeSpace(Item item) => Items.Count<Capacity ? new Location() : null;
        public Address CreateItemAddress(Location location) => new() { Grid=this };
    }
    public class Address { public Grid Grid; }
    public class Inventory { public InventoryEquipment Equipment; public FastAccess FastAccess=new(); }
    public class FastAccess { public Dictionary<EBoundItem,Item> BoundItems=new(); }
    public class Operation
    {
        public Action<CommandStatus> Event;
        public void RaiseEvents(InventoryController inventory, CommandStatus status) { Event?.Invoke(status); }
    }
    public class Result { public bool Failed; public Operation Value=new(); }
    public class InventoryController
    {
        public Inventory Inventory=new();
        public bool IsChangingWeapon, RejectOperations, HandsAction;
        public bool HasAnyHandsAction()=>HandsAction;
        public bool CanExecute(Operation operation) => !RejectOperations;
        public bool IsAtReachablePlace(Item item) => item.Reachable;
        public bool IsAtBindablePlace(Item item) => item.Bindable;
    }
    public static class ItemManipulator
    {
        public static Action<CommandStatus> OnEvent;
        public static bool RejectSimulation, RejectAdd;
        public static int Added, Simulated;
        public static Result Add(Item item, Address address, InventoryController inventory, bool simulate=false)
        {
            if(simulate) { Simulated++; return new() { Failed=RejectSimulation }; }
            if(RejectAdd) return new() { Failed=true };
            Added++; address.Grid.Items.Add(item); item.Parent=address.Grid.Container;
            return new() { Value=new() { Event=OnEvent } };
        }
    }
    public static class BindResult
    {
        public static Action<InventoryController, Item, EBoundItem> OnSimulation;
        public static int Applied;
        public static Result Run(InventoryController inventory, Item item, EBoundItem slot, bool simulate)
        {
            if(!inventory.IsAtBindablePlace(item)) return new() { Failed=true };
            if(simulate) OnSimulation?.Invoke(inventory,item,slot);
            else
            {
                Applied++;
                var map=inventory.Inventory.FastAccess.BoundItems;
                foreach(var old in map.Where(x=>ReferenceEquals(x.Value,item)).Select(x=>x.Key).ToArray()) map.Remove(old);
                map[slot]=item; // Native intentionally can evict: caller must prove the slot is free.
            }
            return new();
        }
    }
}
namespace EFT.AssetsManager
{
    public class AssetPoolObject : UnityEngine.MonoBehaviour
    {
        public bool IsInPool, IsStub;
        public virtual void ReturnToPool() { IsInPool=true; }
        public virtual void OnGetFromPool() { IsInPool=false; }
    }
}
public class TransformLinks : UnityEngine.MonoBehaviour { }
public class WeaponPrefab : EFT.AssetsManager.AssetPoolObject { public TransformLinks Hierarchy; public FirearmsAnimator FirearmsAnimator; }
namespace EFT
{
    public class ObjectsFactory
    {
        public Task<UnityEngine.GameObject> CreateItemAsync(InventoryLogic.Item item,int camera,object player,bool animated,object yield,CancellationToken cancellation)=>null;
    }
    public class ItemFactory
    {
        public Dictionary<string,InventoryLogic.ItemTemplate> ItemTemplates=new();
        public int Created;
        public string NextId => Guid.NewGuid().ToString();
        public InventoryLogic.Item CreateItem(string id,string tpl,object diff) { Created++; return new InventoryLogic.PortableRangeFinder { Id=id,TemplateId=tpl }; }
    }
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame
    {
        public GameWorld GameWorld;
        public List<System.Collections.IEnumerator> Pending=new();
        public void StartCoroutine(System.Collections.IEnumerator routine) { Pending.Add(routine); }
    }
    public class Player
    {
        public InventoryLogic.InventoryController InventoryController;
        public ItemHandsController HandsController;
        public bool IsInventoryOpened;
        public FirearmsAnimator HandsAnimator;
        public State CurrentState=new();
        public class State { public string Name="Idle"; }
        public enum EOperationState { Ready, Executing, Finished }
        public class Operation { public EOperationState State=EOperationState.Executing; }
        public class SinglePlayerInventoryController : InventoryLogic.InventoryController { }
        public class ItemHandsController { public Player _player; public InventoryLogic.Item Item; }
        public class UsableItemController : ItemHandsController
        {
            public bool Transition, IsAiming;
            public FirearmsAnimator FirearmsAnimator=new();
            public Operation CurrentHandsOperation;
            public bool CanInteract()=>FirearmsAnimator.IsIdling()&&AnimationControllerParametersTable.GetBoolActive(FirearmsAnimator.Animator);
            public bool IsInInteractionStrictCheck()=>Transition;
            public static WeaponPrefab GetWeaponPrefab(PortableRangeFinderController controller)=>controller.Prefab;
        }
    }
    public class UsableItemInputTranslator
    {
        public Player.UsableItemController _controller;
        public InputSystem.InputNode.ETranslateResult TranslateCommand(InputSystem.ECommand command)=>InputSystem.InputNode.ETranslateResult.Ignore;
    }
}
namespace EFT.InputSystem
{
    public enum ECommand { ToggleShooting,EndShooting,ToggleAlternativeShooting,EndAlternativeShooting,ReloadWeapon }
    public class InputNode { public enum ETranslateResult { BlockAll,Ignore } }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated=>Instance!=null; }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession { public static bool IsActive,ArmedForNextRaid; public static int ArmEpoch; }
    internal static class ArenaServerClient { public static bool TranslocatorAvailableForConfirmedRaid=true; }
}
namespace ArenaMode.Modes
{
    internal class ArenaMatch { }
    internal static class ArenaMatchService { public static ArenaMatch Current; }
    internal static class ArenaToasts { public static List<string> Messages=new(); public static void Show(string message)=>Messages.Add(message); }
}
namespace ArenaMode.Kits
{
    internal class KitDefinition { public string Id; public KitFill Fill; }
    internal class KitFill { public List<KitStackedItem> RigContents; }
    internal class KitStackedItem { public string Tpl;public int Count; }
    internal static class KitBundlePreloader
    {
        public static bool Warmed=true,IsPreloading;
        public static bool IsTemplateWarmed(string tpl)=>Warmed;
        public static System.Collections.IEnumerator Preload(KitDefinition kit) { yield break; }
    }
}
namespace ArenaMode.Combat
{
    internal static class WeaponSwapService { public static bool IsSwapping; }
}
namespace ArenaMode.Combat.Translocator
{
    internal static class ArenaTranslocatorRuntime
    {
        public static bool Enabled=true;
        public static int PrimaryCount,SecondaryCount;
        public static bool IsLivePlayer(EFT.Player player)=>Enabled && Session.ArenaSession.IsActive && ReferenceEquals(Comfort.Common.Singleton<EFT.GameWorld>.Instance?.MainPlayer,player);
        public static void Primary(EFT.Player player,EFT.InventoryLogic.Item item) { PrimaryCount++; }
        public static void Secondary(EFT.Player player,EFT.InventoryLogic.Item item) { SecondaryCount++; }
    }
}
namespace ArenaMode.Hud { internal static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { public static bool IsRespawnInProgress; } }
namespace ArenaMode
{
    internal static class Plugin { public const string PluginGuid="device-check"; public static readonly Log ModLogger = new(); }
    internal class Log { public List<string> Warnings=new(); public void LogWarning(string message)=>Warnings.Add(message); public void LogDebug(string message) { } }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static List<MethodInfo> GetDeclaredMethods(Type t)=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).ToList();
        public static MethodInfo Method(Type t,string name,Type[] p)=>t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,p,null);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { if(GetTargetMethod()==null) throw new Exception("patch missing"); } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute:Attribute { }
}
