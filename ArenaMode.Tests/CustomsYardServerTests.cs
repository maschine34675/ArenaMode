using System.Text.Json;
using ArenaMode.Server.Services;
using ArenaMode.Server.Patches;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace ArenaMode.Tests;
public sealed partial class ArenaAreaServerTests
{
    [SkippableTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void YardAddsIndependentNativeDerivedStartsWithoutEditingOriginalMarkers(int size)
    {
        var location=ReadAreaTeamLocation();
        var original=location.SpawnPointParams!.ToArray();
        string before=JsonSerializer.Serialize(original,CustomsJsonOptions);
        ArenaLocationMutator.Mutate(location,new(),new(),Mode,ArenaAreaCatalog.CustomsYardId,size);
        ArenaLocationMutator.ValidateAreaSpawns(location,Mode,ArenaAreaCatalog.CustomsYardId,size);
        Assert.Equal(before,JsonSerializer.Serialize(original,CustomsJsonOptions));
        Assert.Equal(32,location.SpawnPointParams!.Count()); // 12 native + 20 versioned extras
        foreach(var spec in ArenaAreaCatalog.CustomsYardSpawns)
        {
            var extra=location.SpawnPointParams.Single(p=>p.Id==spec.Id);
            var donor=original.Single(p=>p.Id==spec.SourceId);
            Assert.Equal(3,extra.CorePointId);
            Assert.Equal("ZoneCustoms",extra.BotZoneName);
            Assert.Equal(new[]{spec.Player?"Player":"BotPmc"},extra.Categories);
            Assert.Equal(new[]{"Pmc"},extra.Sides);
            Assert.NotSame(donor,extra);
            Assert.NotSame(donor.ColliderParams,extra.ColliderParams);
            Assert.Equal(JsonSerializer.Serialize(donor.ColliderParams),JsonSerializer.Serialize(extra.ColliderParams));
            Assert.Equal(donor.Rotation,extra.Rotation);
            Assert.Equal(donor.DelayToCanSpawnSec,extra.DelayToCanSpawnSec);
            Assert.True(ArenaAreaCatalog.CustomsYard.AllowsSpawn(extra.Position!.Value.X,extra.Position.Value.Y,extra.Position.Value.Z));
        }
        string once=JsonSerializer.Serialize(location,CustomsJsonOptions);
        ArenaLocationMutator.Mutate(location,new(),new(),Mode,ArenaAreaCatalog.CustomsYardId,size);
        Assert.Equal(once,JsonSerializer.Serialize(location,CustomsJsonOptions));
        Assert.Equal(before,JsonSerializer.Serialize(original,CustomsJsonOptions));
    }

    [SkippableTheory]
    [InlineData("position")] [InlineData("missing")] [InlineData("category")] [InlineData("zone")]
    [InlineData("side")] [InlineData("core")] [InlineData("collider")] [InlineData("duplicate")]
    public void YardFinalValidationRejectsChangedExtrasWithoutRepairAndRetainsRecovery(string fault)
    {
        var location=ReadAreaTeamLocation();
        Sessions.Arm(Session,Customs,Mode,armToken:Token,areaId:ArenaAreaCatalog.CustomsYardId,areaTeamSize:5);
        string nativeEnd=Start(Customs);
        GenerateLocationAndLootPatch.Postfix(Session,Customs,location);
        Assert.True(Sessions.ValidateAreaResponse(Session,location,out var reason),reason);
        var points=location.SpawnPointParams!.ToList();
        var extra=points.Single(p=>p.Id==ArenaAreaCatalog.CustomsYardSpawns[0].Id);
        switch(fault)
        {
            case "position": extra.Position=new Vector3(-180,1,-100);break;
            case "missing": points.Remove(extra);break;
            case "category": extra.Categories=["Boss"];break;
            case "zone": extra.BotZoneName="ZoneFactoryCenter";break;
            case "side": extra.Sides=["Savage"];break;
            case "core": extra.CorePointId=0;break;
            case "collider": extra.ColliderParams=null;break;
            case "duplicate": points.Add(extra with {});break;
        }
        location.SpawnPointParams=points;
        string changed=JsonSerializer.Serialize(location,CustomsJsonOptions);
        Assert.False(Sessions.ValidateAreaResponse(Session,location,out _));
        Assert.Equal(changed,JsonSerializer.Serialize(location,CustomsJsonOptions));
        Assert.False(Sessions.ConfirmClientRaidStarted(Session,Token,nativeEnd));
        Assert.True(Sessions.TryBeginFailedStartRecovery(Session,Token));
    }

    [SkippableTheory]
    [InlineData("missing")] [InlineData("position")] [InlineData("core")] [InlineData("zone")]
    [InlineData("collision")]
    public void YardRejectsChangedDonorOrCollidingIdentityBeforePublishingAnyMutation(string fault)
    {
        var location=ReadAreaTeamLocation();
        var points=location.SpawnPointParams!.ToList();
        var spec=ArenaAreaCatalog.CustomsYardSpawns[0];
        var donor=points.Single(p=>p.Id==spec.SourceId);
        switch(fault)
        {
            case "missing":points.Remove(donor);break;
            case "position":donor.Position=new Vector3(-180,1,-100);break;
            case "core":donor.CorePointId=0;break;
            case "zone":donor.BotZoneName="wrong";break;
            case "collision":points.Add(donor with {Id=spec.Id,Position=new Vector3(900,1,900)});break;
        }
        location.SpawnPointParams=points;
        string before=JsonSerializer.Serialize(location,CustomsJsonOptions);
        Assert.Throws<InvalidOperationException>(()=>ArenaLocationMutator.Mutate(location,new(),new(),Mode,ArenaAreaCatalog.CustomsYardId,5));
        Assert.Equal(before,JsonSerializer.Serialize(location,CustomsJsonOptions));
    }
}
