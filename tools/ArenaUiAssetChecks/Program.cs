using ArenaMode.Combat;
using ArenaMode.Hud;
using ArenaMode.Modes;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
int checks=0;
void Need(bool value,string reason){if(!value)throw new Exception("CHECK: "+reason);checks++;}
checks+=AssetContracts.Verify(args.Length>1?args[1]:null);
var icons=Enum.GetValues<ArenaUiIcon>();
var assets=new ArenaUiAssets();
foreach(var icon in icons)
{
 var spec=ArenaUiAssets.Describe(icon);var texture=assets.Texture(icon);
 Need(texture!=null&&texture.width==spec.Width&&texture.height==spec.Height,"embedded PNG header dimensions "+icon);
 Need(texture.MarkedNonReadable,"native texture finalised "+icon);
 Need(ReferenceEquals(texture,assets.Texture(icon))&&ReferenceEquals(assets.Sprite(icon),assets.Sprite(icon)),"cached identity "+icon);
 using var stream=typeof(ArenaUiAssets).Assembly.GetManifestResourceStream("ArenaMode.Ui."+spec.File);
 var bytes=new byte[stream.Length];stream.ReadExactly(bytes);
 Need(ArenaUiAssets.MatchesHeader(bytes,spec),"valid header "+icon);
 bytes[16]^=1;Need(!ArenaUiAssets.MatchesHeader(bytes,spec),"dimension drift rejected "+icon);
}
// Real alpha/neutral pixels are decoded separately by Verify-Artwork.py. These
// doubles only prove the loader leaves decoded pixels untouched and releases CPU data.
Need(Texture2D.PixelReads==0&&Texture2D.PixelWrites==0,"neutral source art requires no pixel rewrite");
var held=assets.Sprite(ArenaUiIcon.Headshot);var heldTexture=held.texture;
assets.Dispose();assets.Dispose();Object.EndFrame();
Need(held.Destroyed&&heldTexture.Destroyed&&assets.Sprite(ArenaUiIcon.Headshot)==null,"disposal releases sprites and textures and prevents reload");
int attempts=0;using(var missing=new ArenaUiAssets(_=>{attempts++;return null;}))
{Need(missing.Texture(ArenaUiIcon.Kill)==null&&missing.Texture(ArenaUiIcon.Kill)==null&&attempts==1,"failure cached once");}
using(var broken=new ArenaUiAssets())
{Texture2D.FailNextDecode=true;int live=Object.Live<Texture2D>();Need(broken.Texture(ArenaUiIcon.Kill)==null,"decode fails softly");Object.EndFrame();Need(Object.Live<Texture2D>()==live,"failed decode disposes allocation");}
foreach(var damage in new[]{"HeavyBleeding","LightBleeding","Barbed","GrenadeFragment","Melee","Flame","Explosion","Landmine","Undefined",null,"Bullet, GrenadeFragment","Barbed, Bullet"})
{
 var expected=damage is "HeavyBleeding" or "LightBleeding"?ArenaUiIcon.Bleeding:damage=="Barbed"?ArenaUiIcon.BarbedWire:damage=="GrenadeFragment"?ArenaUiIcon.Grenade:damage=="Melee"?ArenaUiIcon.Melee:damage=="Flame"?ArenaUiIcon.Fire:ArenaUiIcon.Kill;
 Need(KillfeedIcon.Select(new(){DamageKind=damage,WeaponClass="sniperRifle",WeaponName="Pistol"})==expected,"final cause takes precedence "+damage);
}
foreach(var pair in new[]{("sniperRifle",ArenaUiIcon.Sniper),("assaultRifle",ArenaUiIcon.AssaultRifle),("assaultCarbine",ArenaUiIcon.Carbine),("marksmanRifle",ArenaUiIcon.Marksman),("machinegun",ArenaUiIcon.Machinegun),("shotgun",ArenaUiIcon.Shotgun),("pistol",ArenaUiIcon.Pistol),("smg",ArenaUiIcon.Smg),("unknown",ArenaUiIcon.Kill)})
 Need(KillfeedIcon.Select(new(){DamageKind="Bullet",WeaponClass=pair.Item1,WeaponName="wrong localized name"})==pair.Item2,"native class mapping "+pair.Item1);

var root=new GameObject("hud",typeof(RectTransform));var feed=new KillfeedView();feed.Create((RectTransform)root.transform);
Need(root.GetComponentsInChildren<CanvasGroup>().Length==6,"exactly six pooled groups");
Need(root.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget),"all decoration passive");
var capture=new ArenaKill{KillerName="Long_attacker_name_123456789",VictimName="Long_victim_name_987654321",KillerRoleLabel="USEC",VictimRoleLabel="BEAR",WeaponName="HK 416A5",DamageKind="Bullet",WeaponClass="assaultRifle",Headshot=true,DistanceKnown=true,Distance=42};
for(int i=0;i<6;i++)feed.Push(capture);
int loadCount=Texture2D.LoadCalls;
capture.Headshot=false;capture.WeaponClass="unknown";feed.Push(capture);
var pool=root.GetComponentsInChildren<CanvasGroup>();
var reused=pool.Single(g=>g.gameObject.name=="Line0");
Need(!reused.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Headshot").enabled,"pooled headshot is cleared");
Need(reused.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Kill method").sprite.texture.name.EndsWith("kill.png"),"pooled method replaced by neutral fallback");
Need(reused.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Kill method").rectTransform.sizeDelta.x==44f,"fallback keeps original symbol dimensions after weapon reuse");
var text=reused.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single();
Need(text.text.Contains(capture.KillerName)&&text.text.Contains(capture.VictimName)&&text.text.Contains("42 m"),"feed retains complete text");
Need(text.MeasuredWidth==436f,"icon rail preserves original text width");
Need(Texture2D.LoadCalls==loadCount,"push reuses textures");
Preview.WriteFeed(root,Path.Combine(Path.GetDirectoryName(args.Length>0?args[0]:"artifacts/ui-assets-scoreboard.svg")!,"killfeed.svg"));
foreach(var bleeding in new[]{"LightBleeding","HeavyBleeding"})
{
 // Native LastBodyPart and Weapon can still describe an earlier headshot.
 capture.DamageKind=bleeding;capture.Headshot=true;capture.WeaponClass="sniperRifle";
 for(int i=0;i<6;i++)feed.Push(capture);
 foreach(var row in pool)
 {
  var images=row.gameObject.GetComponentsInChildren<Image>();
  var method=images.Single(i=>i.gameObject.name=="Kill method");
  Need(method.enabled&&method.sprite.texture.name.EndsWith("bleeding.png")&&method.color==ArenaUiTheme.Danger,"bleed death uses red blood drop");
  Need(method.rectTransform.sizeDelta.x==44f&&method.rectTransform.sizeDelta.y==22f,"hazard keeps original dimensions after weapon reuse");
  Need(!images.Single(i=>i.gameObject.name=="Headshot").enabled,"bleed death suppresses retained headshot");
 }
 Need(capture.Headshot,"bleed presentation leaves combat metadata unchanged");
}
capture.DamageKind="Bullet";
for(int i=0;i<6;i++)feed.Push(capture);
foreach(var row in pool)
{
 var images=row.gameObject.GetComponentsInChildren<Image>();
 var method=images.Single(i=>i.gameObject.name=="Kill method");
 Need(method.sprite.texture.name.EndsWith("sniper.png")&&method.color==Color.white&&images.Single(i=>i.gameObject.name=="Headshot").enabled,"pooled bleed resets to normal headshot");
 Need(method.rectTransform.sizeDelta.x==64f&&method.rectTransform.sizeDelta.y==32f,"weapon render is readable after hazard reuse");
}
Need(Texture2D.LoadCalls==loadCount,"bleed presentation reuses existing textures");
var wireDeath=new ArenaUncreditedDeath{VictimName="Wire_<b>victim</b>",VictimRoleLabel="BEAR",VictimProfileId="wire-bot",DamageKind="Barbed"};
for(int i=0;i<6;i++)feed.PushUncreditedDeath(wireDeath);
foreach(var row in pool)
{
 var images=row.gameObject.GetComponentsInChildren<Image>();
 var method=images.Single(i=>i.gameObject.name=="Kill method");
 var notice=row.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single().text;
 Need(method.enabled&&method.sprite.texture.name.EndsWith("barbed-wire.png")&&method.color==ArenaUiTheme.Danger,"wire death uses red categorical wire icon");
 Need(!images.Single(i=>i.gameObject.name=="Headshot").enabled,"wire notice clears pooled headshot");
 Need(notice.Contains("Wire_‹b›victim‹/b›")&&notice.Contains("BEAR · BARBED WIRE"),"wire notice escapes and identifies victim and cause");
 Need(!notice.Contains(capture.KillerName)&&!notice.Contains("42 m")&&!notice.Contains("HK 416A5")&&!notice.Contains("➔"),"wire notice has no invented attacker weapon or distance");
}
Preview.WriteFeed(root,Path.Combine(Path.GetDirectoryName(args.Length>0?args[0]:"artifacts/ui-assets-scoreboard.svg")!,"barbed-feed.svg"));
// A mod-supplied credited Barbed kill can still take the existing kill route.
capture.DamageKind="Barbed";capture.Headshot=true;
for(int i=0;i<6;i++)feed.Push(capture);
Need(pool.All(row=>!row.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Headshot").enabled),"credited wire death suppresses retained headshot");
capture.DamageKind="Bullet";
for(int i=0;i<6;i++)feed.Push(capture);
foreach(var row in pool)
{
 var images=row.gameObject.GetComponentsInChildren<Image>();
 var method=images.Single(i=>i.gameObject.name=="Kill method");
 Need(method.sprite.texture.name.EndsWith("sniper.png")&&method.color==Color.white&&images.Single(i=>i.gameObject.name=="Headshot").enabled,"pooled wire resets to normal headshot");
 Need(!row.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single().text.Contains("BARBED WIRE"),"pooled wire text replaced on normal kill");
}
foreach(var cause in new[]{"Undefined","GrenadeFragment","GiveUp"})
{
 var death=new ArenaUncreditedDeath{VictimName="Local",VictimRoleLabel="PMC",LocalIsVictim=true,DamageKind=cause};
 for(int i=0;i<6;i++)feed.PushUncreditedDeath(death);
 foreach(var row in pool)
 {
  var images=row.gameObject.GetComponentsInChildren<Image>();
  var method=images.Single(i=>i.gameObject.name=="Kill method");
  Need(method.enabled&&method.sprite.texture.name.EndsWith(cause=="GrenadeFragment"?"grenade.png":"kill.png"),"local death uses cause or skull icon");
  Need(!images.Single(i=>i.gameObject.name=="Headshot").enabled,"local death clears pooled headshot");
  var notice=row.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single().text;
  Need(notice.Contains("Local")&&!notice.Contains("➔")&&!notice.Contains("42 m"),"local death does not invent attacker or range");
 }
}
ArenaMode.Config.Settings.KillfeedOffsetX.Value=31;feed.Tick();Need(((RectTransform)reused.transform.parent).anchoredPosition.x==55,"offset moves complete feed");
Time.unscaledTime=5.25f;feed.Tick();Need(pool.All(g=>Math.Abs(g.alpha-.5f)<.001f),"pooled icon and text share fade");
Time.unscaledTime=6.1f;feed.Tick();Need(pool.All(g=>!g.gameObject.activeSelf),"all expired rows hidden");
var borrowed=root.GetComponentsInChildren<Image>().First(i=>i.sprite!=null).sprite;
Object.Destroy(root);Object.EndFrame();Need(borrowed.Destroyed&&borrowed.texture.Destroyed,"canvas destruction releases owned assets");

// Actual hitmarker class, not a duplicate timing model. Skull must outlive a
// faded crosshair and must not be reset by ordinary damage or other kills.
Time.unscaledTime=10f;
var hitRoot=new GameObject("hud",typeof(RectTransform));var marker=new HitmarkerElement();marker.Create((RectTransform)hitRoot.transform);
var skull=hitRoot.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="ArenaKillSkull");
var skullGroup=skull.GetComponent<CanvasGroup>();
var cross=hitRoot.GetComponentsInChildren<CanvasGroup>().Single(g=>g.gameObject.name=="ArenaHitmarker");
Need(skull.transform.parent==cross.transform.parent,"skull is independent sibling of crosshair");
Need(skullGroup.alpha==0&&!skullGroup.interactable&&!skullGroup.blocksRaycasts&&!skull.raycastTarget&&skull.preserveAspect,"skull starts hidden and passive");
Need(skull.sprite.texture.name.EndsWith("kill.png"),"skull uses embedded Arena kill asset");
var confirmed=new ArenaKill{KilledByLocal=true,AwardEligible=true,ScoreEligible=true};
marker.Show(new(){Kill=true});marker.Tick();
Need(skullGroup.alpha==0,"damage callback cannot invent a confirmed skull");
int skullLoads=Texture2D.LoadCalls;marker.ShowKill(confirmed);marker.Tick();
Need(skullGroup.alpha==1&&cross.alpha>0,"confirmed kill adds skull without replacing crosshair");
Time.unscaledTime=10.1f;marker.Show(new());marker.Tick();
Need(skullGroup.alpha==1&&cross.transform.localScale==Vector3.one,"ordinary hit preserves skull and clears old crosshair pulse");
Time.unscaledTime=10.7f;marker.Tick();
Need(cross.alpha==0&&skullGroup.alpha>0,"skull survives expired crosshair");
float beforeRejected=skullGroup.alpha;
foreach(var rejected in new ArenaKill[]{null,new(),new(){KilledByLocal=true,LocalIsVictim=true,ScoreEligible=true,AwardEligible=true},new(){KilledByLocal=true,ScoreEligible=true},new(){KilledByLocal=true,AwardEligible=true,ScoreEligible=false},new(){ScoreEligible=true,AwardEligible=true}})
{marker.ShowKill(rejected);marker.Tick();Need(Math.Abs(skullGroup.alpha-beforeRejected)<.0001f,"nonlocal self or ineligible kill does not refresh skull");}
Time.unscaledTime=10.81f;marker.Tick();Need(skullGroup.alpha==0,"skull expires independently");
marker.ShowKill(confirmed);Need(skullGroup.alpha==1&&Texture2D.LoadCalls==skullLoads,"next kill reuses cached skull");
var skullSprite=skull.sprite;Object.Destroy(hitRoot);Object.EndFrame();
Need(skullSprite.Destroyed&&skullSprite.texture.Destroyed,"hitmarker canvas releases skull sprite and texture");
var fallbackRoot=new GameObject("fallback hud",typeof(RectTransform));var fallbackMarker=new HitmarkerElement();Texture2D.FailNextDecode=true;fallbackMarker.Create((RectTransform)fallbackRoot.transform);
fallbackMarker.ShowKill(confirmed);fallbackMarker.Show(new());fallbackMarker.Tick();
Need(!fallbackRoot.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="ArenaKillSkull").enabled&&fallbackRoot.GetComponentsInChildren<CanvasGroup>().Single(g=>g.gameObject.name=="ArenaHitmarker").alpha==1,"missing skull retains procedural crosshair");
Object.Destroy(fallbackRoot);Object.EndFrame();

using var table=new ArenaScoreboardPresentation(new GUIStyle(),new GUIStyle());
var rows=new[]{new ArenaScoreboardEntry {DisplayName="Player with a very long readable display name",RoleLabel="PMC USEC",Side=ArenaScoreboardSide.Friendly,IsLocal=true,IsActive=true,Kills=23,Deaths=4,Assists=7,Headshots=12,Score=20450},
 new ArenaScoreboardEntry {DisplayName="Opponent",RoleLabel="PMC BEAR",Side=ArenaScoreboardSide.Opponent,Kills=3,Deaths=6,Assists=2,Headshots=1,Score=950}};
Color friendly=new(.2f,.65f,1f,1f),enemy=new(1f,.2f,.2f,1f),sentinel=new(.7f,.4f,.3f,.8f);GUI.color=sentinel;
foreach(float width in new[]{360f,560f,814f})
{
 Need(ArenaScoreboardPresentation.Columns(width).All(w=>w>0)&&Math.Abs(ArenaScoreboardPresentation.Columns(width).Sum()-width)<.01,"columns fit "+width);
 GUI.Reset(EventType.Layout);table.Draw("YOUR TEAM",rows,friendly,friendly,enemy,width);var reserved=GUILayoutUtility.Reserved.ToArray();
 Need(GUI.Labels.Count==0&&GUI.Images.Count==0,"layout has no draws");
 GUI.Reset(EventType.Repaint);table.Draw("YOUR TEAM",rows,friendly,friendly,enemy,width);
 Need(reserved.SequenceEqual(GUILayoutUtility.Reserved),"layout/repaint reserve same heights");
 Need(GUI.Labels.Select(l=>l.Text).Contains(rows[0].DisplayName+"  (YOU)")&&GUI.Labels.Any(l=>l.Text=="OUT"),"names and states retained");
 string[] expected={"23","4","7","12",20450.ToString("n0")};
 var values=GUI.Labels.Where(l=>expected.Contains(l.Text)).ToArray();
 Need(values.Select(l=>l.Text).SequenceEqual(expected),"K D A HS score order retained");
 Need(values.All(l=>l.Rect.x>=0&&l.Rect.x+l.Rect.width<=width),"counts inside table");
 Need(GUI.color==sentinel,"global tint restored");
}
GUI.Reset(EventType.Repaint);table.Draw("YOUR TEAM",rows.Take(1).ToArray(),friendly,friendly,enemy,814f);
table.Draw("OPPOSITION",rows.Skip(1).ToArray(),enemy,friendly,enemy,814f);
Preview.Write(args.Length>0?args[0]:"artifacts/ui-assets-scoreboard.svg");
using(var fallback=new ArenaScoreboardPresentation(new GUIStyle(),new GUIStyle(),new ArenaUiAssets(_=>null)))
{GUI.Reset(EventType.Repaint);fallback.Draw("TEAM",rows,friendly,friendly,enemy,814);Need(new[]{"K","D","A","HS","SCORE"}.All(s=>GUI.Labels.Any(l=>l.Text==s)),"missing asset textual headers survive");}
GUI.ThrowDraw=true;try{table.Draw("TEAM",rows,friendly,friendly,enemy,814);}catch(Exception){}finally{GUI.ThrowDraw=false;}
Need(GUI.color==sentinel,"global tint restored after render exception");
Console.WriteLine($"ARENA UI ASSET CONTRACTS PASSED ({checks} checks; native rendering still requires EFT)");
