using ArenaMode;
using ArenaMode.Areas;
using EFT.Game.Spawning;
using UnityEngine;
using UnityEngine.AI;

int assertions=0;
void Need(bool ok,string name) { assertions++; if(!ok) throw new Exception("ASSERT: "+name); }
ArenaAreaConnectivityGraph Scan(int count,Func<int,int,bool> edges,int budget=3)
{
    var graph=new ArenaAreaConnectivityGraph(count);
    int attempts=0;
    while(!graph.Complete && attempts++<10000)
    {
        int calls=0;
        int used=graph.Step(budget,(a,b)=>{ calls++;return edges(a,b); });
        Need(used==calls && calls<=budget,"directed query budget is never exceeded");
    }
    Need(graph.Complete,"finite scans terminate"); return graph;
}
try
{
    var split=new ArenaAreaConnectivityGraph(2);
    int forwardCalls=0;
    split.Step(1,(_,_)=>{forwardCalls++;return true;});
    Need(forwardCalls==1 && !split.Reached(1),"forward alone never publishes a vertex");
    split.Step(0,(_,_)=>throw new Exception("zero budget invoked query"));
    Need(!split.Reached(1),"zero budget preserves pending reverse query");
    split.Step(1,(a,b)=>{Need(a==1 && b==0,"reverse query has independent direction");return true;});
    Need(split.Reached(1),"bidirectional pair publishes vertex");

    var oneWay=Scan(2,(a,b)=>a==0 && b==1);
    Need(!oneWay.Reached(1),"one way edge is rejected");
    var reverseOnly=Scan(2,(a,b)=>a==1 && b==0);
    Need(!reverseOnly.Reached(1),"reverse only edge is rejected");
    var directedCycle=Scan(3,(a,b)=>(a+1)%3==b);
    Need(directedCycle.ReachedCount==1,"one way cycle cannot become bidirectional tree");
    var chain=Scan(5,(a,b)=>Math.Abs(a-b)==1);
    Need(chain.ReachedCount==5 && chain.Parent(4)==3,"waypoint chain recovers non direct vertices");
    var disconnected=Scan(5,(a,b)=>Math.Abs(a-b)==1 && a!=2 && b!=2);
    Need(disconnected.ReachedCount==2,"disconnected component remains excluded");
    Need(chain.ValidateChain(4,_=>true,(a,b)=>Math.Abs(a-b)==1),"fresh complete chain is accepted");
    Need(!chain.ValidateChain(4,_=>true,(a,b)=>!(a==2 && b==3)),"stale forward chain edge is rejected");
    Need(!chain.ValidateChain(4,_=>true,(a,b)=>!(a==3 && b==2)),"stale reverse chain edge is rejected");
    Need(!chain.ValidateChain(4,i=>i!=2,(_,_)=>true),"moved parent vertex is rejected");
    Need(!chain.ValidateChain(4,i=>i!=0,(_,_)=>true),"stale root vertex is rejected");
    Need(!disconnected.ValidateChain(4,_=>true,(_,_)=>true),"new path cannot promote an unverified member");
    var fresh=chain.VerifiedSnapshot(_=>true,(a,b)=>!(a==1 && b==2));
    Need(fresh[1] && !fresh[2] && !fresh[3] && !fresh[4],"final snapshot prunes descendants of changed forward edge");
    fresh=chain.VerifiedSnapshot(_=>true,(a,b)=>!(a==2 && b==1));
    Need(!fresh[2] && !fresh[3] && !fresh[4],"final snapshot independently validates reverse edge");
    fresh=chain.VerifiedSnapshot(i=>i!=1,(_,_)=>true);
    Need(!fresh[1] && !fresh[2] && !fresh[4],"final snapshot prunes moved parent descendants");
    int finalQueries=0;
    fresh=chain.VerifiedSnapshot(_=>true,(_,_)=>{finalQueries++;return true;});
    Need(finalQueries==2*(chain.ReachedCount-1) && fresh.All(v=>v),"final snapshot has linear directed query bound");
    var empty=new ArenaAreaConnectivityGraph(1);
    Need(empty.Complete && empty.ReachedCount==1,"root only graph has no phantom markers");
    for(int budget=1;budget<=24;budget++)
    {
        var g=Scan(12,(a,b)=>Math.Abs(a-b)==1,budget);
        Need(g.ReachedCount==12,"query budget does not change connected membership");
    }

    var a=new Point {Id="a",Position=new(10,0,0)};
    var b=new Point {Id="b",Position=new(20,0,0)};
    var c=new Point {Id="c",Position=new(30,0,0)};
    bool blockedReverse=false, partial=false, outside=false;
    void Native(Vector3 origin,Vector3 target,NavMeshPath path)
    {
        path.status=partial?NavMeshPathStatus.PathPartial:NavMeshPathStatus.PathComplete;
        path.corners=new[]{origin,target};
    }
    void SetNative()
    {
        NavMesh.Query=(origin,target,path)=>
        {
            Native(origin,target,path);
            return Math.Abs(origin.x-target.x)<=10 && !(blockedReverse && origin.x==20 && target.x==10);
        };
        ArenaAreaRuntime.CornersOK=(_,_)=>!outside;
    }
    bool Finish(int frames=100)
    {
        for(int i=0;i<frames;i++) { Time.frameCount++; if(ArenaAreaRuntime.Advance()) return true; }
        return false;
    }
    ArenaAreaRuntime.SetUp(a,b,c); SetNative();
    Need(Finish(),"native adapter recovers contained waypoint route");
    Need(ArenaAreaRuntime.ConnectedCount==3,"only actual markers enter connected set");
    Need(Plugin.ModLogger.Lines.Any(s=>s.Contains("recovered-via-waypoint 2")),"successful recovery is diagnosed");
    Need(ArenaAreaRuntime.Placement(c.Position),"fresh native chain permits marker placement");
    blockedReverse=true;
    Need(!ArenaAreaRuntime.Placement(c.Position),"runtime placement rechecks reverse edge");
    blockedReverse=false; partial=true;
    Need(!ArenaAreaRuntime.Placement(c.Position),"partial native path never proves connectivity");
    partial=false; outside=true;
    Need(!ArenaAreaRuntime.Placement(c.Position),"actual outside corner path never proves connectivity");
    outside=false;
    b.Position=new(21,0,0);
    Need(!ArenaAreaRuntime.Placement(c.Position),"moved native parent marker rejects placement");
    b.Position=new(20,0,0);
    b.Id="changed-id";
    Need(!ArenaAreaRuntime.Placement(c.Position),"changed native parent identity rejects placement");
    b.Id="b";
    var replacement=new Point {Id=b.Id,Position=b.Position};
    ArenaAreaRuntime.ReplaceCandidate(b,replacement);
    Need(!ArenaAreaRuntime.Placement(c.Position),"replacement marker object cannot inherit old proof");
    ArenaAreaRuntime.ReplaceCandidate(replacement,b);
    ArenaAreaRuntime.SampleResult=p=>new(p.x,p.y+0.02f,p.z);
    Need(!ArenaAreaRuntime.Placement(c.Position),"changed sampled floor rejects placement");
    ArenaAreaRuntime.SampleResult=p=>p;
    ArenaAreaRuntime.DifferentToken();
    Need(!ArenaAreaRuntime.Placement(c.Position),"old graph cannot authorize new raid token");
    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); Need(Finish(),"fresh raid rebuilds connectivity");
    ArenaAreaRuntime.Inactive();
    Need(!ArenaAreaRuntime.Placement(c.Position),"inactive raid cannot authorize placement");
    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); Need(Finish(),"fresh graph prepares before reset");
    ArenaAreaRuntime.ClearGraph();
    Need(!ArenaAreaRuntime.Placement(c.Position),"reset removes connectivity certificate");

    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); ArenaAreaRuntime.Legacy();
    Need(ArenaAreaRuntime.Placement(a.Position),"legacy retains direct native connectivity");
    Need(!ArenaAreaRuntime.Placement(c.Position),"legacy does not silently gain waypoint behavior");
    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); Need(Finish(),"prepare for close separated position check");
    NavMesh.Query=(origin,target,path)=>
    { Native(origin,target,path);return origin.x%10==0 && target.x%10==0 && Math.Abs(origin.x-target.x)<=10; };
    Need(!ArenaAreaRuntime.Placement(new Vector3(0.05f,0,0)),"nearby position still needs actual directed path");
    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); blockedReverse=true;
    Time.frameCount++;
    ArenaAreaRuntime.Advance();
    ArenaAreaRuntime.Timeout();
    Need(Plugin.ModLogger.Lines.Any(s=>s.Contains("forward=") && s.Contains("reverse=")),"timeout reports both directed failures");
    blockedReverse=false;
    ArenaAreaRuntime.SetUp(a,b,c); SetNative(); ArenaAreaRuntime.SampleOK=p=>p.x!=20;
    Finish(1); ArenaAreaRuntime.Timeout();
    Need(Plugin.ModLogger.Lines.Any(s=>s.Contains("marker b: sample rejected")),"sample failures identify exact marker");

    var many=Enumerable.Range(1,28).Select(i=>new Point{Id="p"+i,Position=new(i,0,0)}).ToArray();
    ArenaAreaRuntime.SetUp(many); NavMesh.Query=(_,_,p)=>{p.status=NavMeshPathStatus.PathInvalid;return false;};
    Time.frameCount++;
    ArenaAreaRuntime.Advance();
    int firstCalls=NavMesh.Calls;
    Need(firstCalls<=24,"adapter preparation caps native directed queries per frame");
    ArenaAreaRuntime.Advance();
    Need(NavMesh.Calls==firstCalls,"repeated same frame does not replenish query budget");
    Time.frameCount++;
    ArenaAreaRuntime.Advance();
    Need(NavMesh.Calls-firstCalls<=24,"next frame receives one bounded query budget");

    var longChain=Enumerable.Range(1,12).Select(i=>new Point{Id="chain"+i,Position=new(i*10,0,0)}).ToArray();
    ArenaAreaRuntime.SetUp(longChain);
    bool firstEdgeClosed=false;
    NavMesh.Query=(origin,target,path)=>
    {
        path.status=NavMeshPathStatus.PathComplete; path.corners=new[]{origin,target};
        return Math.Abs(origin.x-target.x)<=10 && !(firstEdgeClosed && origin.x==0 && target.x==10);
    };
    int prepFrames=0;
    while(!ArenaAreaRuntime.Discovered(2) && prepFrames++<100) { Time.frameCount++;ArenaAreaRuntime.Advance(); }
    Need(ArenaAreaRuntime.Discovered(2) && !ArenaAreaRuntime.ScanComplete,"preparation fixture spans discovery frames");
    firstEdgeClosed=true;
    while(!ArenaAreaRuntime.ScanComplete && prepFrames++<200) { Time.frameCount++;ArenaAreaRuntime.Advance(); }
    Need(ArenaAreaRuntime.ScanComplete && ArenaAreaRuntime.ConnectedCount==0,"preparation never publishes descendants of stale edge");

    Console.WriteLine($"AREA_CONNECTIVITY_CHECKS_PASSED: {assertions} assertions");
    return 0;
}
catch(Exception e) { Console.Error.WriteLine(e.Message); return 1; }
