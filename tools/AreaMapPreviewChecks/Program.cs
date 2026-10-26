using ArenaMode;
using ArenaMode.Session;
using ArenaMode.Session.Maps;
using ArenaMode.Shared;
using System.Security.Cryptography;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UObject=UnityEngine.Object;

int assertions=0;
void Need(bool condition,string code,string reason)
{ assertions++;if(!condition)throw new InvalidOperationException(code+": "+reason); }
void Near(float actual,float expected,string code,string reason,float epsilon=.001f)
    =>Need(float.IsFinite(actual)&&Math.Abs(actual-expected)<epsilon,code,$"{reason}: {actual} vs {expected}");

try
{
    var selected=ArenaAreaCatalog.CustomsFactoryEastPolygon.Bounds;
    foreach(var (x,z,u,v) in new[]{(698f,-306f,0f,0f),(-372f,-306f,1f,0f),(698f,235f,0f,1f),(-372f,235f,1f,1f),(163f,-35.5f,.5f,.5f)})
    {
        var actual=ArenaAreaMapProjection.ToUv(x,z);
        Near(actual.X,u,"ORIENTATION","Calibrated map corner U");Near(actual.Y,v,"ORIENTATION","Calibrated map corner V");
    }
    // Real named positions from installed Customs_TarkovDev.jsonc, label section.
    // Independent fractions are derived from its world bounds, not production.
    foreach(var (name,x,z,u,v) in new[]{("Big Red",-215f,-119f,913f/1070f,187f/541f),
        ("Dorms",200f,150f,498f/1070f,456f/541f),("Streamer House",567f,-67f,131f/1070f,239f/541f),
        ("Factory Far Corner",654.53f,-160.49f,43.47f/1070f,145.51f/541f)})
    {
        var actual=ArenaAreaMapProjection.ToUv(x,z);
        Near(actual.X,u,"LANDMARK",name+" U");Near(actual.Y,v,"LANDMARK",name+" V");
    }
    Need(ArenaAreaMapProjection.ToUv(600,0).X<ArenaAreaMapProjection.ToUv(300,0).X,
        "ORIENTATION","Higher world X must lie to the left on this map asset.");
    foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
    {
        Need(!ArenaAreaMapProjection.TryCreate(selected,false,invalid,400,out _),"INVALID-VIEWPORT","Invalid width must reject.");
        Need(!ArenaAreaMapProjection.TryCreate(selected,true,600,invalid,out _),"INVALID-VIEWPORT","Invalid height must reject.");
    }
    Need(!ArenaAreaMapProjection.TryCreate(default,false,600,400,out _),"INVALID-AREA","Uninitialized geometry must reject.");
    var farOutside=new ArenaAreaBounds(2000,0,2000,2100,12,2100);
    Need(!ArenaAreaMapProjection.TryCreate(farOutside,false,600,400,out _),"INVALID-CROP","Area outside calibrated map cannot define a detail crop.");
    bool overflow=ArenaAreaMapProjection.TryCreate(selected,false,float.MaxValue,float.MaxValue,out var overflowProjection);
    Need(!overflow||new[]{overflowProjection.Left,overflowProjection.Top,overflowProjection.Width,overflowProjection.Height}.All(float.IsFinite),
        "FINITE-PROJECTION","A finite but huge viewport must never produce infinite submitted positions.");

    // An actual concave notch distinguishes the triangulated area from a box
    // approximation. The same fixture checks texture and boundary submission.
    var concave=new ArenaAreaBounds(new[]{new ArenaAreaVertex(0,0),new ArenaAreaVertex(100,0),new ArenaAreaVertex(100,100),
        new ArenaAreaVertex(60,100),new ArenaAreaVertex(60,40),new ArenaAreaVertex(40,40),new ArenaAreaVertex(40,100),new ArenaAreaVertex(0,100)},0,12,false);
    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
    foreach(var bounds in new[]{concave,ArenaAreaCatalog.CustomsFactoryCenterTeams.Bounds,selected})
        foreach(var (width,height) in new[]{(600f,400f),(300f,200f),(900f,320f)})
            foreach(bool overview in new[]{false,true})
            {
                Need(ArenaAreaMapProjection.TryCreate(bounds,overview,width,height,out var projection),"CROP","Usable map crop must resolve.");
                Near(projection.Width/projection.Height,1062.48f/535.17f,"ASPECT","Source map aspect ratio");
                if(overview)
                {
                    Need(projection.Left>=-.001f&&projection.Top>=-.001f&&projection.Left+projection.Width<=width+.001f
                        &&projection.Top+projection.Height<=height+.001f,"OVERVIEW-FIT","Overview must show complete calibrated image.");
                }
                else foreach(var vertex in bounds.Outline)
                {
                    var position=projection.Project(vertex);
                    Need(position.X>=0&&position.X<=width&&position.Y>=0&&position.Y<=height,"DETAIL-FIT","Selected outline must remain visible in detail crop.");
                }
                var map=NewGraphic<ArenaAreaMapGraphic>(width,height);
                map.Set(texture,bounds,overview);
                var mesh=map.SubmitMesh();
                Need(mesh.Vertices.Count==4+bounds.Outline.Count&&mesh.Indices.Count==6+bounds.FloorTriangles.Count,
                    "SELECTED-FILL","Map submits dim background plus exact selected polygon faces.");
                CheckIndices(mesh,Need);
                Need(mesh.Vertices.Take(4).All(v=>v.Color.r<.5f)&&mesh.Vertices.Skip(4).All(v=>v.Color==Color.white),
                    "OUTSIDE-DIM","Only the selected polygon receives full map brightness.");
                for(int i=0;i<bounds.Outline.Count;i++)
                {
                    var vertex=bounds.Outline[i];var actual=mesh.Vertices[4+i];
                    float imageU=(698f-vertex.X)/1070f,imageV=(vertex.Z+306f)/541f;
                    Near(actual.Uv.x,imageU,"TEXTURE-UV","Polygon U matches full image");
                    Near(actual.Uv.y,1f-imageV,"TEXTURE-UV","Unity texture V flips top-left image origin exactly once");
                    var topLeft=mesh.Vertices[0].Position;var bottomRight=mesh.Vertices[2].Position;
                    Near(actual.Position.x,topLeft.x+(bottomRight.x-topLeft.x)*imageU,"IMAGE-OVERLAY","Polygon/image horizontal registration",.005f);
                    Near(actual.Position.y,topLeft.y+(bottomRight.y-topLeft.y)*imageV,"IMAGE-OVERLAY","Polygon/image vertical registration",.005f);
                }
                var boundary=NewGraphic<ArenaAreaBoundaryGraphic>(width,height);
                boundary.Set(bounds,new[]{bounds},overview);
                var boundaryMesh=boundary.SubmitMesh();CheckIndices(boundaryMesh,Need);
                int count=bounds.Outline.Count;
                Need(boundaryMesh.Vertices.Count==count*9,"BOUNDARY-MESH","One polygon fill and two quad bands per edge, with selected outline deduplicated.");
                for(int i=0;i<count;i++)
                {
                    Near(Distance(boundaryMesh.Vertices[count+i*4].Position,boundaryMesh.Vertices[count+i*4+3].Position),5f,
                        "CONSTANT-WIDTH","Outline halo must not scale with map zoom",.002f);
                    Near(Distance(boundaryMesh.Vertices[count*5+i*4].Position,boundaryMesh.Vertices[count*5+i*4+3].Position),2.5f,
                        "CONSTANT-WIDTH","Blue edge must not scale with map zoom",.002f);
                    Near(boundaryMesh.Vertices[i].Position.x,mesh.Vertices[4+i].Position.x,"BOUNDARY-REGISTRATION","Boundary/fill X");
                    Near(boundaryMesh.Vertices[i].Position.y,mesh.Vertices[4+i].Position.y,"BOUNDARY-REGISTRATION","Boundary/fill Y");
                }
                if(ReferenceEquals(bounds.Outline,concave.Outline))
                {
                    var inside=ProjectToGraphic(projection,map.rectTransform.rect,50,20);
                    var notch=ProjectToGraphic(projection,map.rectTransform.rect,50,75);
                    Need(Covered(mesh,inside,6)&&!Covered(mesh,notch,6),"CONCAVE-FILL","Selected map fill must include interior but leave notch dim.");
                    Need(Covered(boundaryMesh,inside,0,bounds.FloorTriangles.Count)&&!Covered(boundaryMesh,notch,0,bounds.FloorTriangles.Count),
                        "CONCAVE-FILL","Translucent boundary fill must preserve the same concave notch.");
                }
                UObject.DestroyImmediate(map.gameObject);UObject.DestroyImmediate(boundary.gameObject);
            }
    var blank=NewGraphic<ArenaAreaMapGraphic>(0,400);blank.Set(texture,selected,false);
    Need(blank.SubmitMesh().Vertices.Count==0,"EMPTY-MESH","Zero viewport must submit no map mesh.");
    blank.Set(null,selected,false);Need(blank.SubmitMesh().Vertices.Count==0,"EMPTY-MESH","Absent texture must submit no map mesh.");
    UObject.DestroyImmediate(blank.gameObject);UObject.DestroyImmediate(texture);

    var east=State(ArenaAreaCatalog.CustomsFactoryEastPolygonId);
    var center=State(ArenaAreaCatalog.CustomsFactoryCenterTeamsId);
    var whole=ArenaAreaSelectionState.Create("factory4_day","deathmatch",false,"full-map",4);
    var tactical=ArenaAreaSelectionState.Create("bigmap","tactical",false,east.SelectedId,4);
    var unavailable=ArenaAreaSelectionState.Create("unsupported-map","tactical",false,east.SelectedId,4);
    var screen=NewScreen(1920,1080);
    var native=Child(screen,"native map info",typeof(RectTransform),typeof(CanvasGroup));
    var nativeGroup=native.GetComponent<CanvasGroup>();nativeGroup.alpha=.37f;nativeGroup.interactable=false;nativeGroup.blocksRaycasts=true;
    var typography=Child(screen,"native typography",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
    typography.font=new object();typography.fontSharedMaterial=new object();
    var preview=new ArenaAreaMapPreview();
    int initialTextures=UObject.Live<Texture2D>(),initialLoads=Texture2D.LoadCalls;
    preview.Show(screen.transform,native,typography);preview.Refresh(east);
    var firstRoot=preview.Root;var mapGraphic=firstRoot.GetComponentsInChildren<ArenaAreaMapGraphic>().Single();var firstTexture=(Texture2D)mapGraphic.mainTexture;
    Need(firstRoot.activeInHierarchy&&native.activeSelf&&nativeGroup.alpha==0&&!nativeGroup.interactable&&!nativeGroup.blocksRaycasts,
        "NATIVE-HIDE","Preview hides vanilla visuals and input without deactivating its native panel.");
    Need(firstTexture.width==2125&&firstTexture.height==1070&&firstTexture.MarkedNonReadable,
        "EMBEDDED-PNG","Real embedded PNG reaches loader with reviewed dimensions and non-readable request.");
    Need(Convert.ToHexString(SHA256.HashData(Texture2D.LastLoadedBytes))=="E1F75BA5D0101850C58509D3CFBF67847047966E38FD7C6567CE7E26B53F3BFE",
        "EMBEDDED-PNG","Embedded bytes must be the actual reviewed map asset.");
    var viewport=firstRoot.GetComponentsInChildren<RectMask2D>().Single();
    Need(!firstRoot.GetComponentsInChildren<TMP_Text>().Any(t=>t.gameObject.name=="Area map caption"),
        "NO-LEGEND","Self-evident blue-boundary and overview captions are absent.");
    Need(firstRoot.GetComponentsInChildren<TMP_Text>().Any(t=>t.gameObject.name=="Area map attribution"
        &&t.text.Contains("Shebuka")&&t.text.Contains("CC BY-NC-SA 4.0")),
        "ATTRIBUTION","Removing the visual legend preserves map attribution and license.");
    var boundaryGraphic=firstRoot.GetComponentsInChildren<ArenaAreaBoundaryGraphic>().Single();
    Need(ReferenceEquals(mapGraphic.transform.parent,viewport.transform)&&ReferenceEquals(boundaryGraphic.transform.parent,viewport.transform),
        "CLIPPED-LAYERS","Both zoomed layers must share the clipping parent.");
    Need(!mapGraphic.raycastTarget&&!boundaryGraphic.raycastTarget&&!firstRoot.GetComponent<Image>().raycastTarget
        &&firstRoot.GetComponentsInChildren<TMP_Text>().All(t=>!t.raycastTarget),"RAYCAST","Noninteractive preview surfaces must not steal clicks.");
    var buttons=firstRoot.GetComponentsInChildren<Button>();
    Need(buttons.Length==2&&buttons.All(b=>b.navigation.mode==Navigation.Mode.None),"BUTTON-NAVIGATION","Only explicit camera toggles are interactive.");
    Need(firstRoot.GetComponentsInChildren<TMP_Text>().All(t=>ReferenceEquals(t.font,typography.font)),"TYPOGRAPHY","Preview follows native font asset.");
    var detail=buttons.Single(b=>b.gameObject.name=="Area map AREA");var overviewButton=buttons.Single(b=>b.gameObject.name=="Area map OVERVIEW");
    var retiredOverviewCallbacks=overviewButton.onClick.Snapshot();
    Need(!detail.interactable&&overviewButton.interactable,"INITIAL-DETAIL","Each area starts in the detail view.");
    overviewButton.onClick.Invoke();
    Need(detail.interactable&&!overviewButton.interactable,"OVERVIEW-TOGGLE","Overview changes camera only.");
    var greyEdges=boundaryGraphic.SubmitMesh().Vertices.Where(v=>v.Color==new Color(.7f,.7f,.65f,.6f)).ToArray();
    Need(greyEdges.Length==4*(ArenaAreaCatalog.CustomsFactoryCenterTeams.Bounds.Outline.Count+ArenaAreaCatalog.CustomsYard.Bounds.Outline.Count),"OVERVIEW-OTHERS",
        "Overview omits the retired rectangle and shows center contour once; team-start variant must not duplicate its identical outline.");
    for(int i=0;i<greyEdges.Length;i+=4)
        Near(Distance(greyEdges[i].Position,greyEdges[i+3].Position),1.25f,"OVERVIEW-OTHERS","Other-area edge width",.002f);
    for(int i=0;i<20;i++)preview.Refresh(east);
    Need(ReferenceEquals(preview.Root,firstRoot)&&ReferenceEquals(mapGraphic.mainTexture,firstTexture)
        &&Texture2D.LoadCalls==initialLoads+1,"REFRESH-REUSE","Repeated refresh must reuse root and decoded texture.");
    Need(detail.interactable&&!overviewButton.interactable,"REFRESH-CAMERA","Same-area refresh preserves camera choice.");
    preview.Refresh(center);
    Need(ReferenceEquals(preview.Root,firstRoot)&&ReferenceEquals(mapGraphic.mainTexture,firstTexture)
        &&!detail.interactable&&overviewButton.interactable,"AREA-SWITCH","Area changes reuse resources but reset detail crop.");
    Need(!boundaryGraphic.SubmitMesh().Vertices.Any(v=>v.Color==new Color(.7f,.7f,.65f,.6f)),"DETAIL-OTHERS",
        "Detail view must not overlay neighboring area boundaries.");
    Need(firstRoot.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="CUSTOMS — Factory Center"),"AREA-TITLE","Selected area title follows the offered selection.");
    var rootRect=(RectTransform)firstRoot.transform;var viewportRect=(RectTransform)viewport.transform;
    Near(rootRect.rect.width,600,"RESIZE","1920-wide root width");Near(rootRect.rect.height,252,"RESIZE","Compact preview height");
    ((RectTransform)screen.transform).sizeDelta=new Vector2(1600,900);preview.Refresh(center);
    Near(rootRect.rect.width,600,"RESIZE","Resized logical root width");Near(rootRect.rect.height,252,"RESIZE","Resized logical root height");
    Near(rootRect.localScale.y,900f/1080f,"RESIZE","Smaller canvas scales complete right column");
    Near(viewportRect.rect.width,568,"RESIZE","Resized viewport width");Near(viewportRect.rect.height,130,"RESIZE","Resized viewport leaves readable mutator space");
    var resizedMesh=mapGraphic.SubmitMesh();Need(resizedMesh.Vertices.Count>4&&resizedMesh.Vertices.All(v=>float.IsFinite(v.Position.x)&&float.IsFinite(v.Position.y)),
        "RESIZE","Mesh must recompute against resized UI geometry.");
    preview.Refresh(tactical);
    Need(firstRoot.activeSelf&&nativeGroup.alpha==0,"TACTICAL-PREVIEW","Customs Tactical now supports the surveyed preview.");
    foreach(var fallback in new[]{whole,unavailable,null})
    {
        preview.Refresh(fallback);
        Need(!firstRoot.activeSelf&&native.activeSelf,"FALLBACK","Unsupported map/mode must return native information.");
        Near(nativeGroup.alpha,.37f,"NATIVE-RESTORE","Existing native alpha must be restored exactly");
        Need(!nativeGroup.interactable&&nativeGroup.blocksRaycasts,"NATIVE-RESTORE","Existing native input flags must be restored exactly.");
        preview.Refresh(east);
    }
    preview.Close();
    Need(preview.Root==null&&!firstRoot.activeSelf&&firstTexture.DestroyQueued,"CLOSE","Close releases active root and schedules owned texture disposal.");
    Near(nativeGroup.alpha,.37f,"NATIVE-RESTORE","Close restores original native alpha");
    Need(native.GetComponent<CanvasGroup>()==nativeGroup&&!nativeGroup.DestroyQueued,"NATIVE-OWNERSHIP","Native CanvasGroup must never be destroyed by preview.");
    Need(detail.onClick.Snapshot().Length==0&&overviewButton.onClick.Snapshot().Length==0,"RETIRED-LISTENERS","Closed buttons must release their listeners.");
    preview.Show(screen.transform,native,typography);preview.Refresh(east);
    var secondRoot=preview.Root;var secondButtons=secondRoot.GetComponentsInChildren<Button>();
    Need(!ReferenceEquals(firstRoot,secondRoot),"REOPEN","New screen lifecycle gets a distinct root.");
    foreach(var callback in retiredOverviewCallbacks)callback();
    Need(!secondButtons.Single(b=>b.gameObject.name=="Area map AREA").interactable,"RETIRED-CALLBACK","A queued old-button callback cannot change the reopened preview.");
    UObject.EndFrame();
    Need(secondRoot.activeInHierarchy&&nativeGroup.alpha==0&&UObject.Live<Texture2D>()==initialTextures+1,"REOPEN","Deferred old destruction must not retire the new texture.");
    preview.Close();UObject.EndFrame();
    Need(UObject.Live<Texture2D>()==initialTextures,"TEXTURE-LIFETIME","Every closed screen must release its decoded texture.");

    // Exercise an added group across Hide/Refresh and same-frame Close/Show.
    var addedNative=Child(screen,"native without group",typeof(RectTransform));
    var addedPreview=new ArenaAreaMapPreview();addedPreview.Show(screen.transform,addedNative,typography);addedPreview.Refresh(east);
    var addedGroup=addedNative.GetComponent<CanvasGroup>();
    Need(addedGroup!=null&&addedGroup.alpha==0,"ADDED-GROUP","Missing CanvasGroup is owned by preview.");
    for(int i=0;i<6;i++){addedPreview.Refresh(whole);addedPreview.Refresh(east);}
    Need(ReferenceEquals(addedGroup,addedNative.GetComponent<CanvasGroup>())&&!addedGroup.DestroyQueued,
        "ADDED-GROUP","Temporary fallback reuses a live owned group, without duplicate component spam.");
    addedPreview.Close();
    Need(addedNative.GetComponent<CanvasGroup>()==null,"SAME-FRAME-GROUP","Close must immediately remove only its added group.");
    addedPreview.Show(screen.transform,addedNative,typography);addedPreview.Refresh(east);UObject.EndFrame();
    var reopenedGroup=addedNative.GetComponent<CanvasGroup>();
    Need(reopenedGroup!=null&&reopenedGroup.alpha==0&&addedPreview.Root.activeInHierarchy&&!ReferenceEquals(addedGroup,reopenedGroup),
        "SAME-FRAME-GROUP","Immediate reopen must not reuse a group queued for destruction.");
    addedPreview.Close();UObject.EndFrame();Need(addedNative.GetComponent<CanvasGroup>()==null,"ADDED-GROUP","Final close leaves no added native component.");

    // Unity may destroy a native component before our screen-close callback.
    // Its fake-null state must not leave the next owner's hide latch set.
    var disappearingNative=Child(screen,"vanilla rebuilds this group",typeof(RectTransform),typeof(CanvasGroup));
    preview.Show(screen.transform,disappearingNative,typography);preview.Refresh(east);
    UObject.DestroyImmediate(disappearingNative.GetComponent<CanvasGroup>());
    preview.Show(screen.transform,addedNative,typography);preview.Refresh(east);
    Need(addedNative.GetComponent<CanvasGroup>() is { alpha:0f,interactable:false,blocksRaycasts:false },
        "DESTROYED-NATIVE-GROUP","A destroyed old native group cannot prevent hiding a fresh screen owner's info.");
    preview.Close();UObject.EndFrame();

    // Native active state can change independently; do not reactivate it.
    native.SetActive(false);preview.Show(screen.transform,native,typography);preview.Refresh(east);preview.Close();UObject.EndFrame();
    Need(!native.activeSelf,"NATIVE-ACTIVE","Preview must never own native GameObject activation.");native.SetActive(true);

    // Exercise both production owners together: the summary leases native info,
    // the preview leases the summary. Map switches must not reveal vanilla info.
    var compact=new ArenaCompactMapInfo();
    compact.Show(screen.transform,native,typography);
    var borrowedBanner=new Sprite();
    compact.Refresh("Factory",borrowedBanner);
    var summaryRoot=compact.Root;
    Need(native.activeSelf&&nativeGroup.alpha==0&&!nativeGroup.blocksRaycasts,"SUMMARY-HIDE","Compact summary hides original info without deactivating its owner.");
    Need(summaryRoot.GetComponentsInChildren<TMP_Text>().All(t=>!t.richText&&!t.raycastTarget),"SUMMARY-TEXT","Localized strings cannot inject markup or intercept input.");
    Need(summaryRoot.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Factory"),"SUMMARY-TEXT","Selected map name remains visible.");
    var thumbnail=summaryRoot.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Arena map thumbnail");
    Need(ReferenceEquals(thumbnail.sprite,borrowedBanner)&&thumbnail.preserveAspect&&!thumbnail.raycastTarget,"THUMBNAIL","Card borrows the native banner without stretching or intercepting input.");
    Need(summaryRoot.GetComponentsInChildren<TMP_Text>().Length==1,"THUMBNAIL","Only the map title remains; no description or vanilla statistics.");
    Near(((RectTransform)summaryRoot.transform).rect.height,rootRect.rect.height,"FIXED-SLOT","Thumbnail and Customs preview reserve identical height");
    compact.Refresh("Bay 5",null);
    Need(thumbnail.sprite==null&&!thumbnail.gameObject.activeSelf,"THUMBNAIL","Pending native banner reset hides the old thumbnail.");
    compact.Refresh("Bay 5",borrowedBanner);
    Need(thumbnail.gameObject.activeSelf&&thumbnail.sprite==borrowedBanner,"THUMBNAIL","Late native banner assignment appears on refresh.");
    compact.Refresh(null,borrowedBanner);
    Need(thumbnail.sprite==null&&!thumbnail.gameObject.activeSelf,"THUMBNAIL","Deselection cannot display the last map's stale banner.");
    compact.Refresh("Factory",borrowedBanner);
    preview.Show(screen.transform,summaryRoot,typography);preview.Refresh(east);
    Need(summaryRoot.GetComponent<CanvasGroup>()?.alpha==0&&nativeGroup.alpha==0,"SUMMARY-PREVIEW","Customs hides only the summary; original native info stays hidden.");
    preview.Refresh(whole);
    Need(summaryRoot.GetComponent<CanvasGroup>()?.alpha==1&&nativeGroup.alpha==0,"SUMMARY-FALLBACK","Leaving Customs restores the compact summary, not vanilla info.");
    preview.Refresh(east);preview.Close();
    Need(summaryRoot.GetComponent<CanvasGroup>()==null&&nativeGroup.alpha==0,"SUMMARY-OWNERSHIP","Preview close releases only its summary lease.");
    compact.Close();
    Need(!borrowedBanner.Destroyed&&!borrowedBanner.DestroyQueued&&thumbnail.sprite==null,"BORROWED-SPRITE","Closing releases references but never destroys the native sprite.");
    Need(nativeGroup.alpha==.37f&&!nativeGroup.interactable&&nativeGroup.blocksRaycasts,"SUMMARY-RESTORE","Arena exit restores all original native CanvasGroup values.");
    compact.Show(screen.transform,addedNative,typography);compact.Close();
    Need(addedNative.GetComponent<CanvasGroup>()==null,"SUMMARY-SAME-FRAME","Owned native group is removed before same-frame reopen.");
    compact.Show(screen.transform,addedNative,typography);UObject.EndFrame();
    Need(compact.Root.activeInHierarchy&&addedNative.GetComponent<CanvasGroup>()?.alpha==0,"SUMMARY-SAME-FRAME","A fresh summary survives old deferred root disposal.");
    addedNative.SetActive(false);compact.Close();UObject.EndFrame();
    Need(!addedNative.activeSelf&&addedNative.GetComponent<CanvasGroup>()==null,"SUMMARY-NATIVE-ACTIVE","Closing does not reactivate independently hidden native panels.");

    // Real presentation logic, external TMP/layout surfaces. No native map data
    // or event wiring is involved; both leases must exactly restore their rows.
    foreach(var id in new[]{ArenaMapCatalog.Bay5Id,ArenaMapCatalog.EquatorId,ArenaMapCatalog.AirPitId,"ARENA_AIRPIT",ArenaMapCatalog.BowlId,"ARENA_BOWL"})
    {
        var row=Child(screen,"native optional map",typeof(RectTransform));
        var rowRect=(RectTransform)row.transform;
        rowRect.anchorMin=new Vector2(.15f,.83f);rowRect.anchorMax=new Vector2(.16f,.84f);rowRect.anchoredPosition=new Vector2(2,3);
        var header=Child(row,"native header",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        var size=Child(row,"native sizing",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        header.text="ARENA: ORIGINAL";size.text="native size";header.fontSize=25;header.raycastTarget=true;
        bool selectedMarker=false;
        var marker=ArenaPortedMapPresentation.Create(id,rowRect,header,size,()=>selectedMarker);
        var gold=row.GetComponentsInChildren<TextMeshProUGUI>().Single(t=>t.gameObject.name.StartsWith("Arena ported map label"));
        var expected=id==ArenaMapCatalog.Bay5Id?"BAY 5":id==ArenaMapCatalog.EquatorId?"EQUATOR":ArenaMapCatalog.IsBowl(id)?"BOWL":"AIR PIT";
        Need(gold.text==expected&&size.text==expected&&header.enabled&&header.GetComponent<CanvasGroup>()?.alpha==0,
            "PORTED-LABEL","Hidden native TMP stays enabled for preferred-size layout; short text sizes the entire toggle.");
        Need(gold.color==ArenaPortedMapPresentation.Gold&&gold.raycastTarget==header.raycastTarget,"PORTED-LABEL","Gold is independent of native text tint while preserving a native header hit surface.");
        Need(gold.GetComponent<LayoutElement>().ignoreLayout,"PORTED-LABEL","New text does not add a second element to native layout calculation.");
        if (ArenaMapCatalog.IsAirPit(id) || ArenaMapCatalog.IsBowl(id))
        {
            Need(rowRect.anchorMin==new Vector2(.15f,.83f)&&rowRect.anchorMax==new Vector2(.16f,.84f)
                &&rowRect.anchoredPosition==new Vector2(2,3),"AIRPIT-NATIVE-POSITION","AirPit keeps the complete native MapPort geometry, not the Equator fallback.");
            // Later native placement changes likewise remain authoritative during refresh.
            rowRect.anchorMin=new Vector2(.2f,.7f);rowRect.anchorMax=new Vector2(.3f,.8f);rowRect.anchoredPosition=new Vector2(4,5);
            marker.Refresh();
            Need(rowRect.anchorMin==new Vector2(.2f,.7f)&&rowRect.anchorMax==new Vector2(.3f,.8f)
                &&rowRect.anchoredPosition==new Vector2(4,5),"AIRPIT-REFRESH-POSITION","Gold label refresh does not overwrite current native placement.");
        }
        else
        {
            Need(rowRect.anchorMin==rowRect.anchorMax&&rowRect.anchoredPosition==Vector2.zero,"PORTED-POSITION","New anchors retain native normalized positioning semantics.");
            Need(id==ArenaMapCatalog.Bay5Id?rowRect.anchorMin.y>.75f:rowRect.anchorMin.x>.66f&&rowRect.anchorMin.y<.36f,
                "PORTED-POSITION","Bay 5 moves north; Equator sits immediately south-east of native Interchange.");
        }
        header.color=new Color(0,0,0,1);marker.Refresh();
        Need(gold.color==ArenaPortedMapPresentation.Gold,"PORTED-COLOR","Native text animation cannot overwrite own gold label.");
        selectedMarker=true;marker.Refresh();
        Need(gold.color.r<ArenaPortedMapPresentation.Gold.r,"PORTED-COLOR","Selected label uses a darker gold on native light selection background.");
        marker.Close();UObject.EndFrame();
        if (ArenaMapCatalog.IsAirPit(id) || ArenaMapCatalog.IsBowl(id))
            Need(rowRect.anchorMin==new Vector2(.2f,.7f)&&rowRect.anchorMax==new Vector2(.3f,.8f)&&rowRect.anchoredPosition==new Vector2(4,5),
                "AIRPIT-CLOSE-POSITION","Closing a label-only lease leaves newer native coordinates untouched.");
        else
            Need(rowRect.anchorMin==new Vector2(.15f,.83f)&&rowRect.anchorMax==new Vector2(.16f,.84f)&&rowRect.anchoredPosition==new Vector2(2,3),"PORTED-RESTORE","Original native geometry is restored exactly.");
        Need(header.enabled&&header.GetComponent<CanvasGroup>()==null&&header.text=="ARENA: ORIGINAL"&&size.text=="native size"&&gold.Destroyed,"PORTED-RESTORE","Original labels restored; own label and visibility group released.");
        var existingHeaderGroup=header.gameObject.AddComponent<CanvasGroup>();
        existingHeaderGroup.alpha=.6f;existingHeaderGroup.interactable=false;existingHeaderGroup.blocksRaycasts=true;
        marker=ArenaPortedMapPresentation.Create(id,rowRect,header,size,()=>false);
        marker.Close();
        Need(header.GetComponent<CanvasGroup>()==existingHeaderGroup&&existingHeaderGroup.alpha==.6f&&!existingHeaderGroup.interactable&&existingHeaderGroup.blocksRaycasts,
            "PORTED-RESTORE","Existing native visibility/input values restored exactly.");
        marker.Close();
        Need(ArenaPortedMapPresentation.Create("factory4_day",rowRect,header,size,()=>false)==null,"PORTED-SCOPE","Ordinary map rows remain untouched.");
        UObject.DestroyImmediate(row);
    }

    int warningStart=Plugin.ModLogger.Warnings.Count,loadStart=Texture2D.LoadCalls;
    Texture2D.FailNextDecode=true;
    preview.Show(screen.transform,native,typography);preview.Refresh(east);
    for(int i=0;i<8;i++)preview.Refresh(east);
    Need(preview.Root==null&&Plugin.ModLogger.Warnings.Count==warningStart+1&&Texture2D.LoadCalls==loadStart+1,
        "DECODE-FALLBACK","Decode failure restores native info and warns once, without repeated allocations.");
    Near(nativeGroup.alpha,.37f,"DECODE-FALLBACK","Decode failure preserves native alpha");
    Need(!nativeGroup.interactable&&nativeGroup.blocksRaycasts,"DECODE-FALLBACK","Decode failure preserves native interactions.");
    UObject.EndFrame();Need(UObject.Live<Texture2D>()==initialTextures,"DECODE-CLEANUP","Failed decoder allocation must be disposed.");
    preview.Show(screen.transform,native,typography);preview.Refresh(east);
    Need(preview.Root!=null&&preview.Root.activeInHierarchy,"DECODE-RETRY","A fresh Show permits a new decode attempt.");
    preview.Close();UObject.EndFrame();UObject.DestroyImmediate(screen);
    Need(UObject.Live<Texture2D>()==initialTextures,"TEXTURE-LIFETIME","No test scenario leaks a map texture.");
    Console.WriteLine($"AREA MAP PREVIEW PRODUCTION CHECKS PASSED ({assertions} assertions)");
    Console.WriteLine("Evidence is projection, submitted mesh data and modeled engine lifecycle only; native GPU alignment, clipping and rendering remain a separate acceptance test.");
    return 0;
}
catch(Exception ex)
{ Console.Error.WriteLine("AREA MAP PREVIEW REJECTED: "+ex.Message);return 1; }

static ArenaAreaSelectionState State(string id)=>ArenaAreaSelectionState.Create("bigmap","teamdeathmatch",false,id,4,4);
static GameObject NewScreen(float width,float height)
{var screen=new GameObject("native location screen",typeof(RectTransform));((RectTransform)screen.transform).sizeDelta=new(width,height);return screen;}
static GameObject Child(GameObject parent,string name,params Type[] types)
{var child=new GameObject(name,types);child.transform.SetParent(parent.transform,false);return child;}
static T NewGraphic<T>(float width,float height) where T:MaskableGraphic
{var go=new GameObject(typeof(T).Name,typeof(RectTransform),typeof(T));((RectTransform)go.transform).sizeDelta=new(width,height);return go.GetComponent<T>();}
static void CheckIndices(VertexHelper mesh,Action<bool,string,string> need)
{need(mesh.Indices.Count%3==0&&mesh.Indices.All(i=>i>=0&&i<mesh.Vertices.Count),"MESH-INDICES","Submitted triangles must address actual vertices.");}
static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y));
static Vector3 ProjectToGraphic(ArenaAreaMapProjection projection,Rect rect,float x,float z)
{var p=projection.Project(new ArenaAreaVertex(x,z));return new(rect.xMin+p.X,rect.yMax-p.Y,0);}
static bool Covered(VertexHelper mesh,Vector3 point,int offset,int? count=null)
{
    int end=count.HasValue?offset+count.Value:mesh.Indices.Count;
    for(int i=offset;i<end;i+=3)
    {
        var a=mesh.Vertices[mesh.Indices[i]].Position;var b=mesh.Vertices[mesh.Indices[i+1]].Position;var c=mesh.Vertices[mesh.Indices[i+2]].Position;
        double aa=Cross(a,b,point),bb=Cross(b,c,point),cc=Cross(c,a,point);
        if(aa>=-.0001&&bb>=-.0001&&cc>=-.0001||aa<=.0001&&bb<=.0001&&cc<=.0001)return true;
    }
    return false;
}
static double Cross(Vector3 a,Vector3 b,Vector3 c)=>((double)b.x-a.x)*(c.y-a.y)-((double)b.y-a.y)*(c.x-a.x);
