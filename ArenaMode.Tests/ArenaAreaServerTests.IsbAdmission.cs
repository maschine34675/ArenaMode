using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Compat;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;

namespace ArenaMode.Tests;

public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData("com.samc137.isbspecialforces", "ISB Special Forces")]
    [InlineData("com.samc137.aishi", "ISB Aishi")]
    public async Task ServerOnlyIsbRejectsBeforeTouchingSnapshotOrArming(string id, string name)
    {
        var router = RouterWithSnapshot();
        var snapshot = ArenaContext.Snapshots;
        try
        {
            ArenaIsbServerAdmission.Initialize([id]);
            using var response = JsonDocument.Parse(await InvokeStart(router, new ArenaRaidStartRequest
            {
                Map = Map, Mode = "deathmatch", RequestId = Token,
            }));
            Assert.False(response.RootElement.GetProperty("armed").GetBoolean());
            Assert.Contains(name, response.RootElement.GetProperty("reason").GetString());
            Assert.Contains("Ordinary PMC/Scav", response.RootElement.GetProperty("reason").GetString());
            Assert.True(snapshot!.HasSnapshot(Session));
            Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
            // No SaveServer/cloner was supplied: recovery/capture would fail.
            // The exact existing snapshot and ordinary session stay untouched.
            Assert.Equal(ArenaLocalRaidStartDisposition.Ordinary,
                Sessions.ProcessLocalRaidStart(Other, Map, "ordinary-native-start"));
        }
        finally { ArenaIsbServerAdmission.Initialize([]); }
    }

    [Fact]
    public void ServerIsbAdmissionDoesNotBlockUnrelatedContent()
    {
        try
        {
            ArenaIsbServerAdmission.Initialize(["com.samc137.unrelated", "com.morebotsapi.tacticaltoaster"]);
            Assert.Null(ArenaIsbServerAdmission.RejectionReason);
            ArenaIsbServerAdmission.Initialize(["com.samc137.aishi", "com.samc137.isbspecialforces"]);
            Assert.Contains("ISB Special Forces", ArenaIsbServerAdmission.RejectionReason);
            ArenaIsbServerAdmission.Initialize([]);
            Assert.Null(ArenaIsbServerAdmission.RejectionReason);
        }
        finally { ArenaIsbServerAdmission.Initialize([]); }
    }
}
