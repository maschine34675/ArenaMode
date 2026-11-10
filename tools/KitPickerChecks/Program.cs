using ArenaMode.Kits;

// Offline checks of the kit screen's rules (docs/reviews/KIT-PICKER-20261110.md): the choice
// and its per-raid hold, the favourite list, and the two card presenters. The production
// sources are linked; Verify.ps1 runs this once as it is and once per mutant.
int checks = 0;
void Need(bool ok, string reason) { checks++; if (!ok) throw new Exception("FAIL: " + reason); }
string Ids(List<ArenaKitCard> cards) => string.Join(",", cards.Select(c => c.IsRandom ? "<random>" : c.Id));
ArenaKitCardSource K(string id, string name, int price, int order) => new() { Id = id, Name = name, Price = price, Order = order };

// --- the choice
var padded = new ArenaKitChoice(false, "  Spartan ");
Need(padded.KitId == "Spartan", "choice trims the id");
Need(new ArenaKitChoice(false, null).KitId == "", "a missing id is empty");
Need(!new ArenaKitChoice(true, "spartan").Selects("spartan"), "random choice selects no kit");
Need(!new ArenaKitChoice(false, "").Selects("") && !new ArenaKitChoice(false, " ").Selects(null), "empty id selects nothing");
Need(padded.Selects(" SPARTAN ") && !padded.Selects("spartans"), "selection ignores case and padding");
Need(new ArenaKitChoice(false, "a").Equals(new ArenaKitChoice(false, "A"))
     && !new ArenaKitChoice(true, "a").Equals(new ArenaKitChoice(false, "a")), "choices compare without case");
Need(new ArenaKitChoice(false, "a").GetHashCode() == new ArenaKitChoice(false, "A").GetHashCode(), "equal choices hash alike");

// --- held for a raid
var random = new ArenaKitChoice(true, "");
var spartan = new ArenaKitChoice(false, "spartan");
var warlord = new ArenaKitChoice(false, "warlord");
var latch = new ArenaKitChoiceLatch();
Need(latch.Resolve(false, 1, random).Equals(random) && latch.Resolve(false, 1, spartan).Equals(spartan), "menu follows the live settings");
Need(latch.Resolve(true, 1, spartan).Equals(spartan), "arming takes the live choice");
Need(latch.Resolve(true, 1, warlord).Equals(spartan) && latch.Resolve(true, 1, random).Equals(spartan), "armed raid keeps its first answer");
Need(latch.Resolve(true, 2, warlord).Equals(warlord), "a new arm reads the settings again");
Need(latch.Resolve(true, 2, random).Equals(warlord), "the new arm is held too");
Need(latch.Resolve(false, 2, random).Equals(random), "the menu is live again after the raid");
Need(latch.Resolve(true, 2, spartan).Equals(spartan), "leaving the raid releases the hold");

// --- writing a choice into the two settings
bool storedRandom = true; string storedId = "old"; var writes = new List<string>(); string throwOn = null; int failures = 0;
bool Write(bool locked, bool r, string id)
{
    writes.Clear(); failures = 0;
    return ArenaKitChoiceWrite.Apply(locked, r, id,
        // A setting's change handler throws after the value is set, so a write that fails has
        // still changed the setting: only a rollback brings it back.
        () => storedRandom, value => { writes.Add("random=" + value); storedRandom = value; if (throwOn == "random" && !value) throw new InvalidOperationException(); },
        () => storedId, value => { writes.Add("id=" + value); storedId = value; if (throwOn == "id" && value != "old") throw new InvalidOperationException(); },
        _ => failures++);
}
Need(!Write(true, false, "spartan") && writes.Count == 0 && storedRandom && storedId == "old", "locked choice writes nothing");
Need(!Write(false, false, "bad id") && writes.Count == 0 && !Write(false, false, "") && writes.Count == 0, "a fixed choice needs a plain kit id");
Need(Write(false, false, " spartan ") && string.Join(";", writes) == "id=spartan;random=False"
     && !storedRandom && storedId == "spartan", "fixed choice writes the id before switching the draw off");
Need(Write(false, true, null) && string.Join(";", writes) == "random=True" && storedRandom && storedId == "spartan",
     "random choice keeps the last fixed id");
storedRandom = true; storedId = "old"; throwOn = "random";
Need(!Write(false, false, "warlord") && failures == 1 && storedRandom && storedId == "old", "a failed write restores both settings");
storedRandom = true; storedId = "old"; throwOn = "id";
Need(!Write(false, false, "warlord") && failures == 1 && storedRandom && storedId == "old", "a failed write restores both settings");
throwOn = null;
bool liar = false;
Need(!ArenaKitChoiceWrite.Apply(false, false, "warlord", () => liar, value => { }, () => "other", value => { }, null),
     "a write that does not read back is no success");

// --- favourites
Need(ArenaKitFavorites.IsKitId("Aa0._-") && !ArenaKitFavorites.IsKitId("a b") && !ArenaKitFavorites.IsKitId("a,b")
     && !ArenaKitFavorites.IsKitId("") && !ArenaKitFavorites.IsKitId(null) && !ArenaKitFavorites.IsKitId("ä"), "kit id alphabet");
Need(!ArenaKitFavorites.IsKitId(new string('a', 65)) && ArenaKitFavorites.IsKitId(new string('a', 64)), "overlong ids are dropped");
var parsed = ArenaKitFavorites.Parse(" spartan, ,WARLORD,spartan,bad id,a/b,Spartan ,titan.v2_x-1");
Need(string.Join("|", parsed) == "spartan|WARLORD|titan.v2_x-1", "favourites parse trims, dedupes and drops junk");
Need(ArenaKitFavorites.Parse(null).Count == 0 && ArenaKitFavorites.Parse("  ").Count == 0, "no favourites from nothing");
string many = string.Join(",", Enumerable.Range(0, 80).Select(i => "kit" + i));
Need(ArenaKitFavorites.Parse(many).Count == ArenaKitFavorites.MaxEntries, "favourites are capped");
Need(ArenaKitFavorites.Toggle("", "spartan") == "spartan"
     && ArenaKitFavorites.Toggle("spartan,warlord", "titan") == "spartan,warlord,titan", "toggle adds");
Need(ArenaKitFavorites.Toggle("spartan,warlord", "spartan") == "warlord", "toggle adds then removes");
Need(ArenaKitFavorites.Toggle("Spartan,warlord", " SPARTAN ") == "warlord", "toggle ignores case");
string full = string.Join(",", Enumerable.Range(0, ArenaKitFavorites.MaxEntries).Select(i => "kit" + i));
Need(ArenaKitFavorites.Toggle(full, "extra") == full, "toggle leaves a full list alone");
Need(ArenaKitFavorites.Toggle(full, "kit3").Split(',').Length == ArenaKitFavorites.MaxEntries - 1, "a full list can still lose an entry");
Need(ArenaKitFavorites.Toggle("spartan, junk id ,warlord", "bad id") == "spartan,warlord", "toggle drops a bad id but keeps the list");
Need(ArenaKitFavorites.Contains(parsed, " warlord ") && !ArenaKitFavorites.Contains(parsed, "")
     && !ArenaKitFavorites.Contains(null, "x"), "favourite lookup ignores case");

// --- the picker's cards
var kits = new List<ArenaKitCardSource>
{
    K("titan", "Titan", 669000, 3), K("drifter", "Drifter", 180000, 9), null, K(" ", "Ghost", 1, 0),
    K("beta", null, 200000, 2), K("alpha", "  ", 200000, 2), K("gamma", "Gamma", 200000, 1), K("free", "Free", 0, 5),
};
var picker = ArenaKitCards.ForPicker(kits, random, new[] { "TITAN" }, false, false);
Need(picker.Count > 0 && picker[0].IsRandom && picker[0].Id == null && picker[0].Title == ArenaKitCards.RandomTitle, "picker opens with the random card");
Need(picker.Count == 7 && picker.All(c => c.IsRandom || !string.IsNullOrWhiteSpace(c.Id)), "blank ids are skipped");
Need(Ids(picker).StartsWith("<random>,free,drifter,") && Ids(picker).EndsWith(",titan"), "cards are cheapest first");
Need(Ids(picker) == "<random>,free,drifter,gamma,alpha,beta,titan", "equal prices fall back to authored order then id");
Need(picker[0].Selected && picker[0].Tag == ArenaKitCards.SelectedTag && picker.Skip(1).All(c => !c.Selected && c.Tag == ""), "random card is selected when random");
Need(picker.Single(c => c.Id == "titan").Favorite && picker.Count(c => c.Favorite) == 1, "favourite mark follows the list");
Need(picker.Single(c => c.Id == "beta").Title == "beta" && picker.Single(c => c.Id == "alpha").Title == "alpha"
     && picker.Single(c => c.Id == "gamma").Title == "Gamma", "title falls back to the id");
Need(picker.Single(c => c.Id == "drifter").PriceText == "180,000 ₽" && picker.Single(c => c.Id == "free").PriceText == "free"
     && picker[0].PriceText == "", "money formats with thousands separators");
Need(picker.All(c => c.Enabled), "an open picker enables every card");
var odd = ArenaKitCards.ForPicker(new[] { K("my kit", "Mine", 5, 0), K("fine", "Fine", 6, 0) }, random, null, false, false);
Need(odd.Count == 3 && !odd.Single(c => c.Id == "my kit").Enabled && odd.Single(c => c.Id == "fine").Enabled,
     "a kit id the choice cannot store is shown but not clickable");
Need(!odd.Single(c => c.Id == "my kit").CanFavorite && odd.Single(c => c.Id == "fine").CanFavorite && !odd[0].CanFavorite
     && ArenaKitCards.ForPicker(kits, random, null, false, true).Where(c => !c.IsRandom).All(c => c.CanFavorite),
     "only a storable kit id gets a favourite mark, locked or not");

var fixedPick = ArenaKitCards.ForPicker(kits, new ArenaKitChoice(false, "GAMMA"), null, false, false);
Need(!fixedPick[0].Selected && fixedPick[0].Tag == "" && fixedPick.Count(c => c.Selected) == 1
     && fixedPick.Single(c => c.Selected).Id == "gamma"
     && fixedPick.Single(c => c.Selected).Tag == ArenaKitCards.SelectedTag, "fixed choice selects exactly one kit card");

var favoritesOnly = ArenaKitCards.ForPicker(kits, random, new[] { "titan", "drifter", "gone" }, true, false);
Need(favoritesOnly.All(c => !c.IsRandom), "favourites view has no random card");
Need(Ids(favoritesOnly) == "drifter,titan", "favourites view shows only favourites");
Need(ArenaKitCards.ForPicker(kits, random, null, true, false).Count == 0, "no favourites, no cards");

var locked = ArenaKitCards.ForPicker(kits, spartan, null, false, true);
Need(locked.Count == 7 && locked.All(c => !c.Enabled), "locked picker disables every card");
Need(ArenaKitCards.ForPicker(null, random, null, false, false).Count == 1, "an empty catalogue still offers the random card");

// --- the shop's cards
var shop = ArenaKitCards.ForShop(kits, 200000, "GAMMA", " drifter ");
Need(Ids(shop) == "free,drifter,gamma,alpha,beta,titan", "the shop lists every kit, cheapest first");
var titan = shop.Single(c => c.Id == "titan");
Need(!titan.Enabled && titan.PriceText == "669,000 ₽  (short)" && !titan.Selected && titan.Tag == "", "unaffordable kit is visible, disabled and marked short");
var alpha = shop.Single(c => c.Id == "alpha");
Need(alpha.Enabled && alpha.PriceText == "200,000 ₽", "exact purse affords the kit");
var gamma = shop.Single(c => c.Id == "gamma");
Need(gamma.Selected && gamma.Tag == ArenaKitCards.BuyingTag && !gamma.Enabled, "bought kit is selected, tagged and not buyable twice");
var drifter = shop.Single(c => c.Id == "drifter");
Need(drifter.Tag == ArenaKitCards.WearingTag && drifter.Enabled && !drifter.Selected, "worn kit is tagged but still buyable");
var both = ArenaKitCards.ForShop(kits, 200000, "drifter", "drifter").Single(c => c.Id == "drifter");
Need(both.Tag == ArenaKitCards.BuyingTag && both.Selected && !both.Enabled, "bought wins over worn tag");
Need(shop.Single(c => c.Id == "free").PriceText == "free" && shop.Single(c => c.Id == "free").Enabled, "free kit reads free");
Need(ArenaKitCards.ForShop(kits, 0, null, null).Count(c => c.Enabled) == 1, "an empty purse buys only the free kit");
Need(ArenaKitCards.ForShop(kits, 200000, "", " ").All(c => !c.Selected && c.Tag == ""), "blank ids mark nothing");
Need(ArenaKitCards.ForShop(null, 5, null, null).Count == 0, "no catalogue, no shop cards");

Need(ArenaKitCards.Purse(0) == "0 \u20bd" && ArenaKitCards.Purse(1234567) == "1,234,567 \u20bd" && ArenaKitCards.Purse(-5) == "0 \u20bd",
     "an empty purse reads 0, never free");

// --- the line above the picker
Need(ArenaKitCards.ChoiceLine(random, kits) == "A random kit for every life", "choice line: random");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "Gamma"), kits) == "Gamma, every life", "choice line: named kit");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, ""), kits) == "The first kit of the catalogue, every life", "choice line: empty id");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "gone"), kits).StartsWith("'gone' is not in the catalogue"), "choice line: missing kit");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "gone"), null).StartsWith("'gone'"), "choice line: no catalogue");

Console.WriteLine($"KIT_PICKER_CHECKS_PASSED ({checks} assertions)");
