// External engine surfaces only. Real production projection, mesh and preview
// classes are linked unchanged. This models UI ownership and submitted meshes;
// it does not simulate native drawing, RectMask2D shaders or PNG decoding.
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        internal bool DestroyQueued;
        public string name;
        private static readonly List<Object> Pending = new();
        public static readonly List<Object> Created = new();
        protected Object() { Created.Add(this); }
        public static void Destroy(Object value)
        { if (value == null || value.DestroyQueued) return; value.DestroyQueued=true; Pending.Add(value); }
        public static void DestroyImmediate(Object value)
        {
            if (value == null) return;
            if (value is GameObject game)
            {
                foreach (var child in game.transform.Children.ToArray()) DestroyImmediate(child.gameObject);
                foreach (var component in game.Components.ToArray()) { component.GetType().GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(component,null); component.Destroyed=true; }
                game.transform.SetParent(null,false);
            }
            else if (value is Component component) component.gameObject.Components.Remove(component);
            value.Destroyed=true;
            Pending.Remove(value);
        }
        public static void EndFrame()
        { while (Pending.Count > 0) { var batch=Pending.ToArray();Pending.Clear();foreach(var value in batch)DestroyImmediate(value); } }
        public static int Live<T>() where T:Object => Created.OfType<T>().Count(o=>!o.Destroyed);
        public static bool operator ==(Object a,Object b)
        {
            bool an=ReferenceEquals(a,null)||a.Destroyed, bn=ReferenceEquals(b,null)||b.Destroyed;
            return an||bn ? an==bn : ReferenceEquals(a,b);
        }
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public override bool Equals(object other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
    }
    public class MonoBehaviour:Component{}
    public class Transform:Component
    {
        public Transform parent;
        public readonly List<Transform> Children=new();
        public void SetParent(Transform target,bool world)
        { parent?.Children.Remove(this);parent=target;parent?.Children.Add(this); }
        public void SetAsLastSibling()
        { if(parent!=null){parent.Children.Remove(this);parent.Children.Add(this);} }
        public void SetAsFirstSibling()
        { if(parent!=null){parent.Children.Remove(this);parent.Children.Insert(0,this);} }
    }
    public class RectTransform:Transform
    {
        public Vector2 anchorMin,anchorMax,pivot=new(.5f,.5f),anchoredPosition,sizeDelta,offsetMin,offsetMax;
        public Rect rect
        {
            get
            {
                var parentRect=parent is RectTransform p?p.rect:default;
                float width=anchorMax.x!=anchorMin.x ? parentRect.width*(anchorMax.x-anchorMin.x)+offsetMax.x-offsetMin.x : sizeDelta.x;
                float height=anchorMax.y!=anchorMin.y ? parentRect.height*(anchorMax.y-anchorMin.y)+offsetMax.y-offsetMin.y : sizeDelta.y;
                return new Rect(-pivot.x*width,-pivot.y*height,width,height);
            }
        }
    }
    public readonly record struct Vector2(float x,float y)
    { public static Vector2 zero=>new(0,0);public static Vector2 one=>new(1,1); }
    public readonly record struct Vector3(float x,float y,float z);
    public record struct Rect(float x,float y,float width,float height)
    { public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height; }
    public record struct Color(float r,float g,float b,float a)
    { public static Color white=>new(1,1,1,1); public static Color Lerp(Color a,Color b,float t)=>new(a.r+(b.r-a.r)*t,a.g+(b.g-a.g)*t,a.b+(b.b-a.b)*t,a.a+(b.a-a.a)*t); }
    public static class Mathf { public const float Infinity=float.PositiveInfinity; public static float Sqrt(float value)=>MathF.Sqrt(value); public static float Max(float a,float b)=>MathF.Max(a,b); public static float Min(float a,float b)=>MathF.Min(a,b); public static float Ceil(float a)=>MathF.Ceiling(a); public static float Clamp01(float a)=>Math.Clamp(a,0f,1f); }
    public class CanvasRenderer:Component{}
    public class CanvasGroup:Component
    { public float alpha=1f;public bool interactable=true,blocksRaycasts=true; }
    public class GameObject:Object
    {
        public Transform transform;
        public bool activeSelf=true;
        public bool activeInHierarchy=>!Destroyed&&activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
        internal readonly List<Component> Components=new();
        public GameObject(string label,params Type[] types)
        {
            name=label;
            transform=(Transform)Activator.CreateInstance(types.Contains(typeof(RectTransform))?typeof(RectTransform):typeof(Transform));
            transform.gameObject=this;Components.Add(transform);
            foreach(var type in types.Where(t=>!typeof(Transform).IsAssignableFrom(t))) AddComponent(type);
        }
        private Component AddComponent(Type type)
        {
            if(type==typeof(CanvasGroup)&&GetComponent<CanvasGroup>()!=null)
                throw new InvalidOperationException("Unity rejects duplicate CanvasGroup components.");
            var value=(Component)Activator.CreateInstance(type);value.gameObject=this;Components.Add(value);return value;
        }
        public T AddComponent<T>() where T:Component=>(T)AddComponent(typeof(T));
        public void SetActive(bool value)=>activeSelf=value;
        public T GetComponent<T>() where T:Component=>Components.OfType<T>().FirstOrDefault(c=>!c.Destroyed);
        public T[] GetComponentsInChildren<T>(bool inactive=true) where T:Component
        {
            if(Destroyed||!inactive&&!activeInHierarchy)return [];
            return Components.OfType<T>().Where(c=>!c.Destroyed)
                .Concat(transform.Children.SelectMany(t=>t.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
        }
    }
    public class Texture:Object{}
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public class Texture2D:Texture
    {
        public static bool FailNextDecode; public static readonly Texture2D whiteTexture=new(1,1,TextureFormat.RGBA32,false); public Color32[] Pixels; public Color32[] GetPixels32()=>Pixels??=Enumerable.Repeat(new Color32(20,60,120,90),width*height).ToArray(); public void SetPixels32(Color32[] p)=>Pixels=p; public void Apply(bool mipmaps,bool unreadable){MarkedNonReadable=unreadable;}
        public static int LoadCalls;
        public static byte[] LastLoadedBytes;
        public int width,height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public bool MarkedNonReadable;
        public Texture2D(int w,int h,TextureFormat format,bool mipmaps){width=w;height=h;}
        public bool LoadImage(byte[] bytes,bool markNonReadable)
        {
            LoadCalls++;LastLoadedBytes=bytes;
            if(FailNextDecode){FailNextDecode=false;return false;}
            if(bytes.Length<24||!bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return false;
            width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
            height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            MarkedNonReadable=markNonReadable;return width>0&&height>0;
        }
    }
}
namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace UnityEngine.UI
{
    public readonly record struct Vertex(UnityEngine.Vector3 Position,UnityEngine.Color Color,UnityEngine.Vector2 Uv);
    public class VertexHelper
    {
        public readonly List<Vertex> Vertices=new();public readonly List<int> Indices=new();
        public int currentVertCount=>Vertices.Count;
        public void Clear(){Vertices.Clear();Indices.Clear();}
        public void AddVert(UnityEngine.Vector3 position,UnityEngine.Color color,UnityEngine.Vector2 uv)=>Vertices.Add(new(position,color,uv));
        public void AddTriangle(int a,int b,int c){Indices.Add(a);Indices.Add(b);Indices.Add(c);}
    }
    public class Graphic:UnityEngine.Component
    {
        public UnityEngine.Color RendererTint;
        public void CrossFadeColor(UnityEngine.Color color,float duration,bool ignoreTimeScale,bool useAlpha) => RendererTint=color;
        public bool raycastTarget=true;public UnityEngine.Color color;
        public UnityEngine.RectTransform rectTransform=>(UnityEngine.RectTransform)transform;
        public virtual UnityEngine.Texture mainTexture=>null;
        public int DirtyCount;
        protected virtual void OnPopulateMesh(VertexHelper mesh){}
        public VertexHelper SubmitMesh(){var mesh=new VertexHelper();OnPopulateMesh(mesh);return mesh;}
        public void SetAllDirty()=>DirtyCount++;
        public void SetVerticesDirty()=>DirtyCount++;
    }
    public class MaskableGraphic:Graphic{}
    public class Image:MaskableGraphic { public Sprite sprite; public bool preserveAspect,enabled=true; }
    public class RectMask2D:UnityEngine.Component{}
    public struct Navigation { public enum Mode { None } public Mode mode; }
    public class Selectable:UnityEngine.Component { public enum Transition { None,ColorTint } public Transition transition; }
    public struct ColorBlock { public UnityEngine.Color normalColor,highlightedColor,pressedColor,selectedColor,disabledColor; public float colorMultiplier,fadeDuration; }
    public class Button:Selectable
    {
        public bool interactable=true;public Navigation navigation;public Graphic targetGraphic;
        public ColorBlock colors;
        public ButtonClickedEvent onClick=new();
        public class ButtonClickedEvent
        {
            private readonly List<UnityEngine.Events.UnityAction> callbacks=new();
            public void AddListener(UnityEngine.Events.UnityAction action)=>callbacks.Add(action);
            public void RemoveAllListeners()=>callbacks.Clear();
            public UnityEngine.Events.UnityAction[] Snapshot()=>callbacks.ToArray();
            public void Invoke(){foreach(var action in Snapshot())action();}
        }
    }
}
namespace TMPro
{
    public enum FontStyles { Normal }
    public enum TextOverflowModes { Ellipsis, Overflow }
    public enum TextAlignmentOptions { MidlineLeft,Midline,TopLeft }
    public class TMP_Text:UnityEngine.UI.MaskableGraphic
    {
        public string text;public object font,fontSharedMaterial;public float fontSize;
        public FontStyles fontStyle;
        public bool enableWordWrapping;
        public TextOverflowModes overflowMode;
        public TextAlignmentOptions alignment;
    }
    public class TextMeshProUGUI:TMP_Text { public float MeasuredWidth; public UnityEngine.Vector2 GetPreferredValues(float width,float height) { MeasuredWidth=width; return new(width,text.Split('\n').Sum(s=>Math.Max(1,(int)Math.Ceiling(s.Length*8f/width)))*20f); } }
    public static class TMP_Settings { public static object defaultFontAsset=new(); }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger=new(); }
    public class Logger
    { public readonly List<string> Warnings=new();public void LogWarning(string text)=>Warnings.Add(text); }
}
