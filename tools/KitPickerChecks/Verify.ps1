$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
# Offline checks of the kit screen's rules (docs/reviews/KIT-PICKER-20261110.md): the baseline,
# then one mutant of the production source per rule; each must fail its named assertion.
$choice = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Kits/ArenaKitChoice.cs')).Path
$choiceFaults = @(
    @{ Name='untrimmed-id'; Find='KitId = (kitId ?? string.Empty).Trim();'; Replace='KitId = kitId ?? string.Empty;'; Failure='choice trims the id' },
    @{ Name='random-still-selects'; Find='=> !Random && KitId.Length > 0 && string.Equals(KitId,'; Replace='=> KitId.Length > 0 && string.Equals(KitId,'; Failure='random choice selects no kit' },
    @{ Name='case-sensitive-selection'; Find='(kitId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)'; Replace='(kitId ?? string.Empty).Trim(), StringComparison.Ordinal)'; Failure='selection ignores case and padding' },
    @{ Name='raid-follows-live-settings'; Find='return _choice;'; Replace='return live;'; Failure='armed raid keeps its first answer' },
    @{ Name='new-arm-keeps-old-choice'; Find='if (!_held || _epoch != armEpoch)'; Replace='if (!_held)'; Failure='a new arm reads the settings again' },
    @{ Name='hold-outlives-the-raid'; Find='_held = false;'; Replace=''; Failure='leaving the raid releases the hold' },
    @{ Name='narrow-id-alphabet'; Find="&& c != '-' && c != '_' && c != '.') return false;"; Replace=') return false;'; Failure='kit id alphabet' },
    @{ Name='unbounded-id-length'; Find='if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;'; Replace='if (string.IsNullOrEmpty(id)) return false;'; Failure='overlong ids are dropped' },
    @{ Name='duplicate-favourites'; Find='if (!IsKitId(id) || Contains(result, id)) continue;'; Replace='if (!IsKitId(id)) continue;'; Failure='favourites parse trims, dedupes and drops junk' },
    @{ Name='unbounded-favourites'; Find='if (result.Count >= MaxEntries) break;'; Replace=''; Failure='favourites are capped' },
    @{ Name='toggle-never-removes'; Find='if (at >= 0) list.RemoveAt(at);'; Replace='if (at >= 0) { }'; Failure='toggle adds then removes' },
    @{ Name='toggle-overfills'; Find='else if (list.Count < MaxEntries) list.Add(id);'; Replace='else list.Add(id);'; Failure='toggle leaves a full list alone' },
    @{ Name='write-ignores-lock'; Find='if (locked) return false;'; Replace=''; Failure='locked choice writes nothing' },
    @{ Name='write-any-id'; Find='if (!random && !ArenaKitFavorites.IsKitId(id)) return false;'; Replace=''; Failure='a fixed choice needs a plain kit id' },
    @{ Name='write-switch-first'; Find="            if (!random) writeId(id);`n            writeRandom(random);"; Replace="            writeRandom(random);`n            if (!random) writeId(id);"; Failure='fixed choice writes the id before switching the draw off' },
    @{ Name='random-clears-id'; Find='if (!random) writeId(id);'; Replace='writeId(id);'; Failure='random choice keeps the last fixed id' },
    @{ Name='no-rollback'; Find='            try { writeRandom(oldRandom); } catch { }'; Replace=''; Failure='a failed write restores both settings' },
    @{ Name='no-id-rollback'; Find='            try { writeId(oldId); } catch { }'; Replace=''; Failure='a failed write restores both settings' },
    @{ Name='no-readback'; Find="        return readRandom() == random`n               && (random || string.Equals(readId(), id, StringComparison.Ordinal));"; Replace='        return true;'; Failure='a write that does not read back is no success' }
)
foreach ($fault in $choiceFaults) { $fault.Path = $choice; $fault.Property = 'ChoiceSource' }
$cards = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Kits/ArenaKitCards.cs')).Path
$cardFaults = @(
    @{ Name='blank-id-card'; Find='if (kit != null && !string.IsNullOrWhiteSpace(kit.Id)) sorted.Add(kit);'; Replace='if (kit != null) sorted.Add(kit);'; Failure='blank ids are skipped' },
    @{ Name='price-order-lost'; Find='if (byPrice != 0) return byPrice;'; Replace=''; Failure='cards are cheapest first' },
    @{ Name='authored-order-lost'; Find='return byOrder != 0 ? byOrder : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);'; Replace='return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);'; Failure='equal prices fall back to authored order then id' },
    @{ Name='local-number-format'; Find='roubles.ToString("N0", CultureInfo.InvariantCulture)'; Replace='roubles.ToString("N0", CultureInfo.GetCultureInfo("de-DE"))'; Failure='money formats with thousands separators' },
    @{ Name='every-kit-selected'; Find='bool selected = choice.Selects(kit.Id);'; Replace='bool selected = !choice.Random;'; Failure='fixed choice selects exactly one kit card' },
    @{ Name='random-card-among-favourites'; Find='if (!favoritesOnly)'; Replace='if (true)'; Failure='favourites view has no random card' },
    @{ Name='favourites-view-unfiltered'; Find='if (favoritesOnly && !favorite) continue;'; Replace=''; Failure='favourites view shows only favourites' },
    @{ Name='locked-picker-clickable'; Find='Enabled = !locked,'; Replace='Enabled = true,'; Failure='locked picker disables every card' },
    @{ Name='everything-affordable'; Find='bool affordable = kit.Price <= money;'; Replace='bool affordable = true;'; Failure='unaffordable kit is visible, disabled and marked short' },
    @{ Name='exact-purse-short'; Find='bool affordable = kit.Price <= money;'; Replace='bool affordable = kit.Price < money;'; Failure='exact purse affords the kit' },
    @{ Name='buy-twice'; Find='Enabled = affordable && !bought,'; Replace='Enabled = affordable,'; Failure='bought kit is selected, tagged and not buyable twice' },
    @{ Name='worn-tag-hides-purchase'; Find='Tag = bought ? BuyingTag : worn ? WearingTag : string.Empty,'; Replace='Tag = worn ? WearingTag : bought ? BuyingTag : string.Empty,'; Failure='bought wins over worn tag' },
    @{ Name='random-line-lost'; Find='if (choice.Random) return "A random kit for every life";'; Replace=''; Failure='choice line: random' },
    @{ Name='unstorable-id-clickable'; Find='Enabled = !locked && ArenaKitFavorites.IsKitId(kit.Id),'; Replace='Enabled = !locked,'; Failure='a kit id the choice cannot store is shown but not clickable' },
    @{ Name='unstorable-id-favourite'; Find='CanFavorite = ArenaKitFavorites.IsKitId(kit.Id),'; Replace='CanFavorite = true,'; Failure='only a storable kit id gets a favourite mark, locked or not' },
    @{ Name='favourite-follows-lock'; Find='CanFavorite = ArenaKitFavorites.IsKitId(kit.Id),'; Replace='CanFavorite = !locked && ArenaKitFavorites.IsKitId(kit.Id),'; Failure='only a storable kit id gets a favourite mark, locked or not' },
    @{ Name='custom-untagged'; Find='if (kit.Custom && string.IsNullOrEmpty(card.Tag)) card.Tag = CustomTag;'; Replace=''; Failure='an own kit is tagged CUSTOM and pickable' },
    @{ Name='blocked-clickable'; Find='        card.Enabled = false;'; Replace=''; Failure='a blocked own kit is greyed with its reason and keeps its SELECTED tag' },
    @{ Name='blocked-silent'; Find='card.PriceText = (card.PriceText ?? string.Empty) + "  \u00b7  " + kit.Blocked;'; Replace=''; Failure='a blocked own kit is greyed with its reason and keeps its SELECTED tag' },
    @{ Name='choice-line-hides-block'; Find='return kit.Blocked == null ? TitleOf(kit) + ", every life"'; Replace='return true ? TitleOf(kit) + ", every life"'; Failure='choice line: a blocked own kit says what is used instead' },
    @{ Name='shop-ignores-block'; Find='Mark(cards[cards.Count - 1], kit); // a custom kit the shop cannot sell is greyed too'; Replace=''; Failure='the shop sells an own kit above the cap' },
    @{ Name='purse-reads-free'; Find='=> Math.Max(0, roubles).ToString("N0", CultureInfo.InvariantCulture) + " ₽";'; Replace='=> Money(roubles);'; Failure='an empty purse reads 0, never free' }
)
foreach ($fault in $cardFaults) { $fault.Path = $cards; $fault.Property = 'CardsSource' }
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'KitPickerChecks.csproj') -Token 'KIT_PICKER_CHECKS_PASSED' -Properties @() -Faults ($choiceFaults + $cardFaults)
