using System.Globalization;
using System.Text;
using System.Security;
using UnityEngine;

// Offline view of the real presentation helper's recorded draw rectangles.
// The harness font metrics are estimates, not Unity/TMP rendering evidence.
internal static class Preview
{
 internal static void WriteFeed(GameObject root,string output)
 {
  string E(string s)=>SecurityElement.Escape(s??"");
  var lines=root.GetComponentsInChildren<CanvasGroup>().Where(g=>g.gameObject.activeSelf).OrderByDescending(g=>((RectTransform)g.transform).anchoredPosition.y).ToArray();
  var svg=new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='560' height='"+(lines.Sum(l=>((RectTransform)l.transform).sizeDelta.y+4)+60)+"'><rect width='100%' height='100%' fill='#293130'/><text x='20' y='22' fill='#d0d4d8' font-family='Arial' font-size='12'>OFFLINE KILLFEED LAYOUT — estimated font metrics</text><g transform='translate(20 40)'>");
  float y=0;
  foreach(var line in lines)
  {
   float h=((RectTransform)line.transform).sizeDelta.y;
   svg.Append($"<rect x='0' y='{y}' width='520' height='{h}' fill='#0e1217' fill-opacity='.94'/><rect x='0' y='{y}' width='56' height='{h}' fill='#1a2026'/><path d='M0 {y}h520' stroke='#58636e' stroke-opacity='.3'/>");
   foreach(var image in line.gameObject.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i=>i.enabled&&i.sprite!=null))
   {
    var r=(RectTransform)image.transform;var t=image.sprite.texture;string file=t.name["Arena UI ".Length..];
    using var stream=typeof(Preview).Assembly.GetManifestResourceStream("ArenaMode.Ui."+file);using var bytes=new MemoryStream();stream.CopyTo(bytes);
    svg.Append($"<image x='{r.anchoredPosition.x-r.sizeDelta.x/2}' y='{y-r.anchoredPosition.y-r.sizeDelta.y/2}' width='{r.sizeDelta.x}' height='{r.sizeDelta.y}' href='data:image/png;base64,{Convert.ToBase64String(bytes.ToArray())}'/>");
   }
   var text=line.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single().text;
   string[] parts=text.Split('\n');
   for(int n=0;n<parts.Length;n++)
   {
    string plain=System.Text.RegularExpressions.Regex.Replace(parts[n],"<[^>]*>","");
    int length=n==0?44:56;int count=0;
    while(plain.Length>0){int take=Math.Min(length,plain.Length);string segment=plain[..take];plain=plain[take..];
     svg.Append($"<text x='72' y='{y+23+(n==0?count*20:40+count*16)}' fill='{(n==0?"#d5dae0":"#a8b5c2")}' font-family='Arial' font-size='{(n==0?18:14)}'>{E(segment)}</text>");count++;}
   }
   y+=h+4;
  }
  svg.Append("</g></svg>");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,svg.ToString());
 }
 internal static void Write(string output)
 {
  string N(float f)=>f.ToString("0.###",CultureInfo.InvariantCulture);
  string E(string s)=>SecurityElement.Escape(s??"");
  string Hex(Color c)=>$"#{(int)(c.r*255):X2}{(int)(c.g*255):X2}{(int)(c.b*255):X2}";
  var svg=new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='854' height='{N(GUILayoutUtility.Y+84)}' viewBox='0 0 854 {N(GUILayoutUtility.Y+84)}'><rect width='100%' height='100%' fill='#0e1217'/><text x='20' y='25' fill='#a8b5c2' font-family='Arial' font-size='12'>OFFLINE LAYOUT — original sprites, estimated font metrics; no EFT screenshot</text><g transform='translate(20 44)'>");
  int id=0;
  foreach(var draw in GUI.Images)
  {
   var r=draw.Rect;string geometry=$"x='{N(r.x)}' y='{N(r.y)}' width='{N(r.width)}' height='{N(r.height)}'";
   if(ReferenceEquals(draw.Texture,Texture2D.whiteTexture)){svg.Append($"<rect {geometry} fill='{Hex(draw.Color)}' opacity='{N(draw.Color.a)}'/>");continue;}
   string file=draw.Texture.name["Arena UI ".Length..];using var source=typeof(Preview).Assembly.GetManifestResourceStream("ArenaMode.Ui."+file);
   using var bytes=new MemoryStream();source.CopyTo(bytes);
   string filter="";
   if(file=="team-panel.png")
   {
    string f="tint"+(id++);svg.Append($"<defs><filter id='{f}' color-interpolation-filters='sRGB'><feColorMatrix type='saturate' values='0'/><feComponentTransfer><feFuncR type='linear' slope='{N(draw.Color.r)}'/><feFuncG type='linear' slope='{N(draw.Color.g)}'/><feFuncB type='linear' slope='{N(draw.Color.b)}'/></feComponentTransfer></filter></defs>");filter=$" filter='url(#{f})'";
   }
   svg.Append($"<image {geometry} opacity='{N(draw.Color.a)}' preserveAspectRatio='none'{filter} href='data:image/png;base64,{Convert.ToBase64String(bytes.ToArray())}'/>");
  }
  foreach(var draw in GUI.Labels)
  {
   var r=draw.Rect;int length=Math.Max(1,(int)(r.width/(draw.FontSize*.55f)));var lines=new List<string>();string remaining=draw.Text;
   while(remaining.Length>length){int take=remaining.LastIndexOf(' ',Math.Min(length,remaining.Length-1));if(take<1)take=length;lines.Add(remaining[..take]);remaining=remaining[take..].TrimStart();}lines.Add(remaining);
   string align=draw.Alignment==TextAnchor.MiddleCenter?"middle":draw.Alignment==TextAnchor.MiddleRight?"end":"start";
   float x=draw.Alignment==TextAnchor.MiddleCenter?r.x+r.width/2:draw.Alignment==TextAnchor.MiddleRight?r.x+r.width:r.x;
   float y=r.y+(r.height-lines.Count*(draw.FontSize+2))/2+draw.FontSize;
   for(int i=0;i<lines.Count;i++)svg.Append($"<text x='{N(x)}' y='{N(y+i*(draw.FontSize+2))}' text-anchor='{align}' fill='{Hex(draw.Color)}' font-family='Arial' font-size='{draw.FontSize}'>{E(lines[i])}</text>");
  }
  svg.Append("</g></svg>");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,svg.ToString());
 }
}
