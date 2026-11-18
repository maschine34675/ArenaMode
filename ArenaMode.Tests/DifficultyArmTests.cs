using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

// The arm field "difficulty" follows the participants pattern: validated before any side
// effect, frozen in the session entry, echoed, replay-compared and reset with the entry.
public sealed partial class ArenaAreaServerTests
{
    [Theory]
    [InlineData("gungame", "reduced", "reduced")]
    [InlineData("gungame", "maximum", "maximum")]
    [InlineData("gungame", null, "maximum")]
    [InlineData("deathmatch", "reduced", "reduced")]
    [InlineData("teamdeathmatch", "reduced", "reduced")]
    public async Task DifficultyRouteEchoReplayAndStatusKeepTheFrozenStep(string mode, string? sent, string frozen)
    {
        var (router, _, _, cloner) = CustomsRouter();
        var request = ParticipantRequest(Map, mode, 4) with { Difficulty = sent };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(frozen, first.RootElement.GetProperty("difficulty").GetString());
        Assert.Equal(Enum.Parse<ArenaDifficultyStep>(frozen, ignoreCase: true), Sessions.GetDifficulty(Session));
        Assert.Equal(ArenaDifficultyStep.Maximum, Sessions.GetDifficulty(Other));

        using var replay = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(frozen, replay.RootElement.GetProperty("difficulty").GetString());

        // The other step is a different request and never replaces the frozen one.
        string other = frozen == "reduced" ? "maximum" : "reduced";
        using var rejected = JsonDocument.Parse(await InvokeStart(router, request with { Difficulty = other }));
        Assert.False(rejected.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(frozen, ArenaDifficultyPolicy.ToWire(Sessions.GetDifficulty(Session)));
        Assert.Equal(Token, Sessions.GetArmToken(Session));
        Assert.Equal(1, cloner.Calls);

        using var status = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal(frozen, status.RootElement.GetProperty("difficulty").GetString());
        Sessions.MarkIdle(Session);
        Assert.Equal(ArenaDifficultyStep.Maximum, Sessions.GetDifficulty(Session));
        using var idle = JsonDocument.Parse(await InvokeStatus(router));
        Assert.Equal("maximum", idle.RootElement.GetProperty("difficulty").GetString());
    }

    [Fact]
    public async Task DifficultyReplayWithTheFieldDroppedIsNotTheSameRequest()
    {
        var (router, _, _, _) = CustomsRouter();
        var request = ParticipantRequest(Map, "gungame", 4) with { Difficulty = "reduced" };
        using var first = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.True(first.RootElement.GetProperty("armed").GetBoolean());
        using var dropped = JsonDocument.Parse(await InvokeStart(router, request with { Difficulty = null }));
        Assert.False(dropped.RootElement.GetProperty("armed").GetBoolean());
        Assert.Equal(ArenaDifficultyStep.Reduced, Sessions.GetDifficulty(Session));
    }

    [Theory]
    [InlineData("extreme")]
    [InlineData("Reduced")]
    [InlineData("MAXIMUM")]
    [InlineData(" reduced")]
    [InlineData("1")]
    public async Task UnknownDifficultyIsRefusedBeforeAnySideEffect(string value)
    {
        var (router, saves, original, cloner) = CustomsRouter();
        var request = ParticipantRequest(Map, "gungame", 4) with { Difficulty = value };
        using var refused = JsonDocument.Parse(await InvokeStart(router, request));
        Assert.False(refused.RootElement.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(refused.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(0, cloner.Calls);
        Assert.False(ArenaContext.Snapshots!.HasSnapshot(Session));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
        Assert.Same(original, saves.GetProfile(Session));
        Assert.False(Sessions.BeginArmPreparation(Session, Map, "gungame", false, Token,
            participants: 4, difficulty: value));
        Assert.Throws<ArgumentException>(() => Sessions.Arm(Session, Map, "gungame", armToken: Token,
            participants: 4, difficulty: value));
        Assert.Equal(ArenaSessionState.Idle, Sessions.GetStatus(Session).state);
    }

    [Fact]
    public void DifficultyIsFrozenByArmAndPreparationAndResetByEveryRelease()
    {
        Sessions.Arm(Session, Map, "gungame", armToken: Token, participants: 4, difficulty: "reduced");
        Assert.Equal(ArenaDifficultyStep.Reduced, Sessions.GetDifficulty(Session));
        Assert.True(Sessions.TryReplayArmedRequest(Session, Map, "gungame", false, Token, out _,
            participants: 4, difficulty: "reduced"));
        Assert.False(Sessions.TryReplayArmedRequest(Session, Map, "gungame", false, Token, out _,
            participants: 4, difficulty: "maximum"));
        Assert.False(Sessions.TryReplayArmedRequest(Session, Map, "gungame", false, Token, out _,
            participants: 4, difficulty: "bogus"));
        Sessions.MarkIdle(Session);
        Assert.Equal(ArenaDifficultyStep.Maximum, Sessions.GetDifficulty(Session));

        Assert.True(Sessions.BeginArmPreparation(Session, Map, "gungame", false, Token,
            participants: 4, difficulty: "reduced"));
        Assert.Equal(ArenaDifficultyStep.Reduced, Sessions.GetDifficulty(Session));
        Sessions.CancelArmPreparation(Session);
        Assert.Equal(ArenaDifficultyStep.Maximum, Sessions.GetDifficulty(Session));
    }

    [Fact]
    public void DifficultyFieldDeserializesOptionalAndRefusesNonStrings()
    {
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{}")!.Difficulty);
        Assert.Null(JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"difficulty\":null}")!.Difficulty);
        Assert.Equal("reduced", JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"difficulty\":\"reduced\"}")!.Difficulty);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ArenaRaidStartRequest>("{\"difficulty\":1}"));
    }
}
