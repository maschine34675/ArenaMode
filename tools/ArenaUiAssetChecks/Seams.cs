// Engine and plain input data only. No asset-cache, icon-selection, table-layout
// or pooled killfeed algorithm is duplicated here. Not a native pixel oracle.
namespace UnityEngine
{
 public static class Time { public static float unscaledTime; }
 public record struct Color32(byte r,byte g,byte b,byte a);
 public enum SpriteMeshType { FullRect }
 public class Sprite:Object
 {
  public Texture2D texture;
  public static Sprite Create(Texture2D texture,Rect rect,Vector2 pivot,float units, uint extrude,SpriteMeshType mesh)
   =>new(){texture=texture};
 }
 public static class ColorUtility
 { public static string ToHtmlStringRGB(Color c)=>$"{(int)(c.r*255):X2}{(int)(c.g*255):X2}{(int)(c.b*255):X2}"; }
 public enum TextAnchor { MiddleLeft,MiddleCenter,MiddleRight }
 public enum FontStyle { Normal,Bold }
 public enum TextClipping { Clip }
 public enum ScaleMode { StretchToFill }
 public class RectOffset { }
 public class GUIStyleState { public Color textColor; }
 public class GUIContent(string text) { public string text=text; }
 public class GUIStyle
 {
  public int fontSize=14; public TextAnchor alignment;public bool wordWrap,richText;public TextClipping clipping;
  public FontStyle fontStyle; public RectOffset padding,margin;
  public GUIStyleState normal=new();
  public GUIStyle(){} public GUIStyle(GUIStyle source) {fontSize=source.fontSize;}
  public Vector2 CalcSize(GUIContent c)=>new((c.text??"").Length*fontSize*.55f,fontSize+2);
  public float CalcHeight(GUIContent c,float width)=>Math.Max(1,MathF.Ceiling(CalcSize(c).x/Math.Max(1,width)))*(fontSize+2);
 }
 public enum EventType { Layout,Repaint,MouseDown }
 public class Event { public static Event current=new();public EventType type; }
 public class GUILayoutOption { }
 public static class GUILayout
 { public static GUILayoutOption Width(float x)=>new();public static GUILayoutOption Height(float y)=>new(); }
 public static class GUILayoutUtility
 {
  public static float Y;public static readonly List<Rect> Reserved=new();
  public static Rect GetRect(float width,float height,params GUILayoutOption[] options)
  {var rect=new Rect(0,Y,width,height);Y+=height;Reserved.Add(rect);return rect;}
 }
 public static class GUI
 {
  public record LabelRecord(Rect Rect,string Text,int FontSize,TextAnchor Alignment,Color Color);
  public record ImageRecord(Rect Rect,Texture2D Texture,Color Color);
  public static Color color=Color.white;public static bool ThrowDraw;
  public static readonly List<LabelRecord> Labels=new();public static readonly List<ImageRecord> Images=new();
  public static void Label(Rect rect,string text,GUIStyle style){if(ThrowDraw)throw new Exception("draw fault");Labels.Add(new(rect,text,style.fontSize,style.alignment,color));}
  public static void DrawTexture(Rect rect,Texture2D texture,ScaleMode mode,bool alpha){if(ThrowDraw)throw new Exception("draw fault");Images.Add(new(rect,texture,color));}
  public static void Reset(EventType type){Labels.Clear();Images.Clear();GUILayoutUtility.Reserved.Clear();GUILayoutUtility.Y=0;Event.current.type=type;}
 }
}
namespace ArenaMode.Config
{
 public class ValueEntry<T>(T value){public T Value=value;}
 public static class Settings
 {
  public static ValueEntry<float> KillfeedOffsetX=new(0),KillfeedOffsetY=new(0);
  public static ValueEntry<ArenaMode.Effects.ArenaTracerColor> FriendlyTracerColor=new(ArenaMode.Effects.ArenaTracerColor.Cyan),OpponentTracerColor=new(ArenaMode.Effects.ArenaTracerColor.Red);
 }
}
namespace ArenaMode.Effects {internal static class ArenaTracerPatches {internal static void ResetDiagnostics(){}}}
namespace ArenaMode.Hud
{
 internal static class HudStyle
 {
  internal static TMPro.TextMeshProUGUI CreateText(UnityEngine.RectTransform parent,string name,float size,TMPro.TextAlignmentOptions alignment)
  {var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform));go.transform.SetParent(parent,false);return go.AddComponent<TMPro.TextMeshProUGUI>();}
 }
}
namespace ArenaMode.Modes
{
 public enum ArenaScoreboardSide {Friendly,Opponent}
 public class ArenaScoreboardEntry
 {public string DisplayName,RoleLabel;public bool IsLocal,IsActive;public ArenaScoreboardSide Side;public int Kills,Deaths,Assists,Headshots,Score;}
}
