param([string]$Source, [string]$Destination)
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText($Source).Replace("`r`n", "`n")
function Slice([string]$Start,[string]$End) {
    $a = $text.IndexOf($Start,[StringComparison]::Ordinal)
    $b = $text.IndexOf($End,$a,[StringComparison]::Ordinal)
    if ($a -lt 0 -or $b -le $a) { throw "Production transaction boundary not found: $Start" }
    return $text.Substring($a,$b-$a)
}
$header = @'
// Generated from exact production transaction methods. No transaction logic is doubled.
using EFT; using EFT.InventoryLogic; using HarmonyLib; using Redeploy.Core.Inventory;
using EftFastAccess = EFT.InventoryLogic.FastAccess;
using EftInventory = EFT.InventoryLogic.Inventory;
namespace ArenaMode.Kits;
public sealed class TeamArenaLoadoutSnapshot {
private static readonly AccessTools.FieldRef<EftInventory, InventoryEquipment> EquipmentField = (EftInventory i) => ref i.Equipment;
private static readonly AccessTools.FieldRef<EftInventory, EftFastAccess> FastAccessField = (EftInventory i) => ref i.FastAccess;
private static readonly EquipmentSlot[] RetainedEquipmentSlots = { EquipmentSlot.SecuredContainer, EquipmentSlot.Scabbard, EquipmentSlot.ArmBand };
'@
$methods = Slice '    internal static void CommitEntryEquipment(' '    private static HashSet<string> FindProtectedRootIds('
$types = Slice '    private sealed class SlotTransfer' "`n}`n`n/// <summary>`n/// Fully materialized"
$path = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
[IO.File]::WriteAllText($path, $header + "`n" + $methods + $types + "`n}`n")
