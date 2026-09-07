using ArenaMode.Areas;
using EFT;
using EFT.Game.Spawning;

int checks = 0;
void Check(bool result, string name)
{
    if (!result) throw new InvalidOperationException("ASSERT: " + name);
    checks++;
}
bool Accept(ESpawnCategoryMask category, EPlayerSideMask mask, EPlayerSide? side)
    => ArenaAreaSpawnPolicy.IsPlayerPoint(new Point("test", category, mask), side);

// Real Customs category overlap caused the bug: the Player flag on a Savage
// BotPmc marker is not authorization to spawn the local PMC there.
Check(!Accept(ESpawnCategoryMask.Player | ESpawnCategoryMask.BotPmc, EPlayerSideMask.Savage, EPlayerSide.Usec),
    "Savage-only BotPmc player marker must not become a USEC entry or respawn");
Check(!Accept(ESpawnCategoryMask.Player | ESpawnCategoryMask.Bot, EPlayerSideMask.Savage, EPlayerSide.Bear),
    "Savage-only bot player marker must not become a BEAR entry or respawn");
Check(Accept(ESpawnCategoryMask.Player, EPlayerSideMask.Usec, EPlayerSide.Usec), "USEC-specific player marker works for USEC");
Check(Accept(ESpawnCategoryMask.Player, EPlayerSideMask.Bear, EPlayerSide.Bear), "BEAR-specific player marker works for BEAR");
Check(!Accept(ESpawnCategoryMask.Player, EPlayerSideMask.Usec, EPlayerSide.Bear), "USEC-specific marker must reject BEAR");
Check(!Accept(ESpawnCategoryMask.Player, EPlayerSideMask.Bear, EPlayerSide.Usec), "BEAR-specific marker must reject USEC");
foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
{
    Check(Accept(ESpawnCategoryMask.Player, EPlayerSideMask.Pmc, side), "Pmc mask accepts each PMC faction");
    Check(Accept(ESpawnCategoryMask.Player, EPlayerSideMask.All, side), "All mask preserves Factory's native PMC player markers");
    Check(!Accept(ESpawnCategoryMask.BotPmc, EPlayerSideMask.Pmc, side), "BotPmc without Player flag is not a local-player spawn");
    Check(!Accept(ESpawnCategoryMask.Coop | ESpawnCategoryMask.Group, EPlayerSideMask.All, side), "Group/Coop alone must not become Player");
    Check(!Accept(ESpawnCategoryMask.None, EPlayerSideMask.All, side), "Missing player category stays rejected");
    Check(!Accept(ESpawnCategoryMask.Player, EPlayerSideMask.None, side), "Missing side mask stays rejected");
    Check(!Accept(ESpawnCategoryMask.Player, (EPlayerSideMask)8, side), "Unknown side mask is rejected");
    Check(!Accept(ESpawnCategoryMask.Player, (EPlayerSideMask)15, side), "Unknown bits cannot authorize a known faction");
    Check(!ArenaAreaSpawnPolicy.IsPlayerPoint(null, side), "Missing marker is rejected");
}
foreach (EPlayerSide? side in new EPlayerSide?[] { null, EPlayerSide.Savage, (EPlayerSide)0, (EPlayerSide)3, (EPlayerSide)99 })
    Check(!Accept(ESpawnCategoryMask.Player, EPlayerSideMask.All, side), "Missing, Scav or unsupported profile side must fail closed");

// Review the complete supported native flag combinations, plus unknown bits.
// Expected values below use the verified native contract's numeric bit table,
// independently of the production enum-switch and mask selection expression.
foreach (EPlayerSide? side in new EPlayerSide?[] { EPlayerSide.Usec, EPlayerSide.Bear, EPlayerSide.Savage, null, (EPlayerSide)3 })
for (int mask = 0; mask < 16; mask++)
for (int category = 0; category < 128; category++)
{
    bool expected = category % 2 == 1 && mask < 8
        && (side == EPlayerSide.Usec && mask % 2 == 1
            || side == EPlayerSide.Bear && mask % 4 >= 2);
    Check(Accept((ESpawnCategoryMask)category, (EPlayerSideMask)mask, side) == expected,
        $"Native side/category matrix mismatch: side={side}, mask={mask}, category={category}");
}

var markers = new[]
{
    new Point("customs-savage", ESpawnCategoryMask.Player | ESpawnCategoryMask.BotPmc, EPlayerSideMask.Savage),
    new Point("customs-pmc", ESpawnCategoryMask.Player, EPlayerSideMask.Pmc),
    new Point("factory-all", ESpawnCategoryMask.Player, EPlayerSideMask.All),
    new Point("usec-only", ESpawnCategoryMask.Player, EPlayerSideMask.Usec),
    new Point("bear-only", ESpawnCategoryMask.Player, EPlayerSideMask.Bear),
};
var before = markers.Select(p => (p.Id, p.Categories, p.Sides)).ToArray();
Check(markers.Where(p => ArenaAreaSpawnPolicy.IsPlayerPoint(p, EPlayerSide.Usec)).Select(p => p.Id)
    .SequenceEqual(new[] { "customs-pmc", "factory-all", "usec-only" }), "USEC candidate filtering preserves the correct exact records");
Check(markers.Where(p => ArenaAreaSpawnPolicy.IsPlayerPoint(p, EPlayerSide.Bear)).Select(p => p.Id)
    .SequenceEqual(new[] { "customs-pmc", "factory-all", "bear-only" }), "BEAR candidate filtering preserves the correct exact records");
Check(!markers.Any(p => ArenaAreaSpawnPolicy.IsPlayerPoint(p, null)), "Unresolved loading profile must not select a default side");
Check(before.SequenceEqual(markers.Select(p => (p.Id, p.Categories, p.Sides))), "Shared marker category/side data must remain unchanged");
Console.WriteLine($"ARENA_CUSTOMS_SPAWN_CONTRACTS_PASSED checks={checks}");

sealed record Point(string Id, ESpawnCategoryMask Categories, EPlayerSideMask Sides) : ISpawnPoint;

// Narrow host doubles, not copied native implementations. Verify.ps1 checks
// these enum contracts and the two property signatures against local native
// source; the final client build independently uses the installed assemblies.
namespace EFT
{
    public enum EPlayerSide { Usec=1, Bear=2, Savage=4 }
    [Flags] public enum EPlayerSideMask { None=0, Usec=1, Bear=2, Savage=4, Pmc=3, All=7 }
}
namespace EFT.Game.Spawning
{
    [Flags] public enum ESpawnCategoryMask
    { None=0, Player=1, Bot=2, Boss=4, Coop=8, Group=0x10, Opposite=0x20, BotPmc=0x40, All=0x47 }
    public interface ISpawnPoint { ESpawnCategoryMask Categories { get; } EPlayerSideMask Sides { get; } }
}
