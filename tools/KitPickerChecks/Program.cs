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

// --- the player's own kits (docs/reviews/CUSTOM-KITS-20261110.md)
var mine = new List<ArenaKitCardSource>(kits) { new() { Id = "custom-00000000000000aa", Name = "Mine", Price = 150000, Order = 100001, Custom = true },
    new() { Id = "custom-00000000000000bb", Name = "Too dear", Price = 900000, Order = 100002, Custom = true, Blocked = "above the stock cap: solo Tactical only" } };
var minePicker = ArenaKitCards.ForPicker(mine, new ArenaKitChoice(false, "custom-00000000000000bb"), null, false, false);
var ownCard = minePicker.Single(c => c.Id == "custom-00000000000000aa");
Need(ownCard.Custom && ownCard.Tag == ArenaKitCards.CustomTag && ownCard.Enabled && ownCard.Blocked == null, "an own kit is tagged CUSTOM and pickable");
var dear = minePicker.Single(c => c.Id == "custom-00000000000000bb");
Need(!dear.Enabled && dear.Blocked != null && dear.PriceText.Contains("solo Tactical") && dear.Selected && dear.Tag == ArenaKitCards.SelectedTag,
     "a blocked own kit is greyed with its reason and keeps its SELECTED tag");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "custom-00000000000000bb"), mine).Contains("the first buildable kit is used")
     && ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "custom-00000000000000aa"), mine) == "Mine, every life", "choice line: a blocked own kit says what is used instead");
var mineShop = ArenaKitCards.ForShop(mine.Where(k => k != null).Select(k => k.Id == "custom-00000000000000bb" ? new ArenaKitCardSource { Id = k.Id, Name = k.Name, Price = k.Price, Order = k.Order, Custom = true } : k).ToList(), 1000000, null, null);
Need(mineShop.Single(c => c.Id == "custom-00000000000000bb").Enabled && mineShop.Single(c => c.Id == "custom-00000000000000bb").Custom,
     "the shop sells an own kit above the cap");
var unusable = ArenaKitCards.ForShop(new[] { new ArenaKitCardSource { Id = "custom-00000000000000cc", Name = "Broken", Price = 1000, Custom = true, Blocked = "unknown items" } }, 1000000, null, null);
Need(!unusable[0].Enabled && unusable[0].PriceText.Contains("unknown items"), "the shop greys an unusable own kit");
Need(Ids(ArenaKitCards.ForPicker(mine, random, null, false, false)).Contains("custom-00000000000000aa,drifter"), "own kits sort by price among the stock kits");

// --- which card the detail pane shows (ArenaKitHoverIntent): the browser's 4 x 4 grid of 236 x 150 cards
// with 12 px gaps, the pane right of it (x from 992 on); y grows downwards here, only its size matters
const float Frame = 1f / 60f;
string CardAt(float x, float y)
{
    if (x < 0f || y < 0f || x >= 4 * 248f || y >= 4 * 162f || x % 248f > 236f || y % 162f > 150f) return null;
    return "card" + ((int)(y / 162f) * 4 + (int)(x / 248f));
}
float clock = 0f; string under = null; int misreports = 0;
var intent = new ArenaKitHoverIntent<string>();
// One frame as the view runs it; Update's answer must say whether the shown card changed (the view redraws on it).
void Tick(float x, float y)
{
    string before = intent.Shown;
    if (intent.Update(clock, x, y) == ReferenceEquals(before, intent.Shown)) misreports++;
    clock += Frame;
}
// The pointer on (x, y) for one frame: the card under it gets its enter, the one it left its exit.
void Step(float x, float y)
{
    string card = CardAt(x, y);
    if (card != under) { if (under != null) intent.Exit(under); if (card != null) intent.Enter(card, clock); under = card; }
    Tick(x, y);
}
int Frames(float seconds) => Math.Max(1, (int)Math.Round(seconds / Frame));
// A straight line at a steady speed, one frame at a time.
void Glide(float x0, float y0, float x1, float y1, float seconds)
{
    int frames = Frames(seconds);
    for (int frame = 0; frame <= frames; frame++) Step(x0 + (x1 - x0) * frame / frames, y0 + (y1 - y0) * frame / frames);
}
void Hold(float x, float y, float seconds) => Glide(x, y, x, y, seconds);
// A hand on the mouse: a few pixels to either side every frame.
void Jitter(float x, float y, float amplitude, float seconds)
{
    for (int frame = 0; frame <= Frames(seconds); frame++) Step(x + (frame % 2 == 0 ? amplitude : -amplitude), y);
}
void Restart() { intent.Reset(); under = null; clock += 10f; }

Hold(100f, 75f, .2f);
Need(intent.Shown == "card0", "a card the pointer rests on takes the pane");
Glide(100f, 75f, 1300f, 75f, .6f);
Hold(1300f, 75f, 1f);
Need(intent.Shown == "card0", "crossing cards on the way to the pane leaves the pane on its kit");
Glide(1300f, 75f, 100f, 75f, .3f);
Hold(100f, 75f, .2f);
Glide(100f, 75f, 1300f, 75f, 4f);
Hold(1300f, 75f, .5f);
Need(intent.Shown == "card0", "creeping right towards the pane does not take it");
Restart();
Hold(845f, 75f, .2f);
Need(intent.Shown == "card3", "the rightmost card takes the pane");
Glide(845f, 75f, 1000f, 600f, 2.5f);
Need(intent.Shown == "card3", "creeping steeply down-right to the buttons does not take the pane");
Restart();
Hold(400f, 75f, .2f);
Glide(400f, 75f, 130f, 75f, .1f);
Glide(130f, 75f, 118f, 75f, .15f);
Need(intent.Shown == "card1", "a drift past the rest distance is no rest");
Restart();
Hold(400f, 75f, .2f);
Glide(400f, 75f, 120f, 75f, .1f);
Jitter(120f, 75f, 2.5f, .2f);
Need(intent.Shown == "card0", "a hand's jitter of a few pixels still rests");
Restart();
Hold(400f, 75f, .2f);
Glide(400f, 75f, 200f, 75f, .25f);
Glide(200f, 75f, 175f, 75f, .3f);
Need(intent.Shown == "card1", "a card is not shown before its dwell while the pointer creeps");
Glide(175f, 75f, 150f, 75f, .3f);
Need(intent.Shown == "card0", "a card the pointer creeps over without heading to the pane is shown after a while");
Restart();
Hold(400f, 75f, .2f);
Glide(400f, 75f, 120f, 75f, .1f);
Hold(120f, 75f, .05f);
Need(intent.Shown == "card1", "a stop shorter than a rest keeps the pane");
Hold(120f, 75f, .1f);
Need(intent.Shown == "card0", "a stop of a rest takes the pane");
Restart();
Hold(100f, 75f, .2f);
intent.Enter("card1", clock); intent.Exit("card0"); under = "card1";
Hold(300f, 75f, .2f);
Need(intent.Shown == "card1", "a late exit of the previous card does not drop the current one");
intent.Show("card2");
Need(intent.Shown == "card2", "a click shows its card at once");
intent.Exit("card1"); under = null;
Hold(1300f, 75f, 1f);
intent.Enter("card3", clock); under = "card3";
Tick(1300f, 75f);
Need(intent.Shown == "card3", "a card scrolled under a resting pointer is shown at once");
Restart();
Need(intent.Shown == null, "a rebuilt browser shows no hovered card");
intent.Enter("card0", clock); under = "card0";
Tick(100f, 75f);
Need(intent.Shown == null, "a pointer that has just jumped onto a card has to rest first");
Restart();
Hold(600f, 75f, .2f);
intent.Reset();
for (int frame = 0; frame < 12; frame++) Tick(600f, 75f);
Need(intent.Shown == null, "a rebuild forgets the card under the pointer (its exit never comes)");
intent.Enter("card2", clock);
Tick(600f, 75f);
Need(intent.Shown == "card2", "the rebuilt card under a resting pointer is shown at once");
Need(misreports == 0, "Update reports exactly the frames on which the shown card changes");

// --- the line above the picker
Need(ArenaKitCards.ChoiceLine(random, kits) == "A random kit for every life", "choice line: random");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "Gamma"), kits) == "Gamma, every life", "choice line: named kit");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, ""), kits) == "The first kit of the catalogue, every life", "choice line: empty id");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "gone"), kits).StartsWith("'gone' is not in the catalogue"), "choice line: missing kit");
Need(ArenaKitCards.ChoiceLine(new ArenaKitChoice(false, "gone"), null).StartsWith("'gone'"), "choice line: no catalogue");

Console.WriteLine($"KIT_PICKER_CHECKS_PASSED ({checks} assertions)");
