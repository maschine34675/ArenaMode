using ArenaMode.Server.Routers;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Tests;

public sealed class RewardRecoveryApiTests
{
    [Fact]
    public void RewardRouterHasNoPlayerRecoveryWorkflow()
    {
        var router = new ArenaRewardRouter(new JsonUtil([]), null!, null!, null!, null!);
        Assert.True(router.CanHandle(RewardWire.StatusRoute));
        Assert.True(router.CanHandle(RewardWire.ConsentRoute));
        Assert.False(router.CanHandle("/arenamode/rewards/recovery/status"));
        Assert.False(router.CanHandle("/arenamode/rewards/recovery/request"));
    }
}
