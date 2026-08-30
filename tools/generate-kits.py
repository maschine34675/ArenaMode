"""Rebuilds the arena kit catalogue with real armour variety.

Keeps every kit's WEAPON exactly as it was — that half is curated — and
replaces the protection around it with sets generated from the game's own
armour, rig and helmet presets. Those presets ship with their plates already
installed, in BSG's own slot casing, which is precisely the class of mistake
that had to be hand-repaired in the first corpus.

Two mechanical rules the generator exists to enforce:
  * an armoured rig blocks body armour (BlocksArmorVest), so a set is either
    "body armour + plain rig" or "armoured rig alone", never both;
  * spare magazines have to FIT — a plate carrier with eight cells cannot hold
    what a twenty-five cell chest rig can, and the old corpus asked every kit
    for the same three regardless.
"""
import json, io, collections, uuid, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

DB = r'D:\SPT41\SPT_Runtime\SPT_Data\database'
KITS = r'D:\SPT41\Development\ArenaMode\ArenaMode.Server\defaults\kits.json'

items = json.load(io.open(DB + r'\templates\items.json', encoding='utf-8-sig'))
glob = json.load(io.open(DB + r'\globals.json', encoding='utf-8-sig'))
loc = json.load(io.open(DB + r'\locales\global\en.json', encoding='utf-8-sig'))
_handbook = json.load(io.open(DB + r'\templates\handbook.json', encoding='utf-8-sig'))
PRICES = {e['Id']: e['Price'] for e in _handbook['Items']}


def handbook_price(tpl):
    return PRICES.get(tpl, 0)

# The default inventory template: every kit is grafted onto one, and its slot
# filters are the authority on what each slot accepts.
ROOT_TPL = '55d7217a4bdc2d86028b456d'

ARMOR = '5448e54d4bdc2dcc718b4568'
VEST = '5448e5284bdc2dcb718b4567'
HEAD = '5a341c4086f77401f2541505'
# Hearing matters in a corridor arena, so a kit gets the best ear protection its
# helmet allows — and a DIFFERENT one from the kit before it where possible.
#
# The catalogue used to hold two headsets across twenty kits: the GSSh-01, which
# is the cheapest in the game, wherever a helmet permitted anything, and the
# TEP-300 earplug everywhere else. Both halves of that were a mistake. The
# GSSh-01 won because it has the fewest conflicts, which optimised for "always
# fits" rather than "worth wearing"; the TEP-300 won eleven kits only because
# those kits had closed helmets, which the helmet picker never knew to avoid.
#
# Ranked by handbook price rather than by an opinion of mine about how each one
# sounds — it tracks the community ordering closely enough and it comes from the
# game's own data. The TEP-300 is forced last regardless: it is in no trader
# assort and no loot table in this version, so it stays a last resort rather
# than something the catalogue leans on.
EARPIECE_LAST_RESORT = '68bf405779c8186398099017'    # Peltor TEP-300 earplugs
# The Transformer Bag, as the hand-built corpus had it: nine cells, under a
# kilo, no penalties. The bag this generator first reached for was the SSO
# Attack 2 — thirty-five cells, 2.8 kg, and -5% movement with -3 mouse control,
# which is a strange thing to hand every kit in an arena built around moving.
BACKPACK = '56e33680d2720be2748b4576'

# Kept from the original kit rather than generated: the medical loadout lives in
# the pockets, and the secure container and armband are the surgeon's business.
CARRY_OVER_SLOTS = {'Pockets', 'SecuredContainer', 'Scabbard', 'ArmBand'}


def chain(tpl):
    n, out = tpl, []
    while n in items and len(out) < 12:
        out.append(n)
        n = items[n].get('_parent')
    return out


def is_kind(tpl, base):
    return base in chain(tpl)[1:]


def nm(tpl):
    return loc.get(tpl + ' Name', items.get(tpl, {}).get('_name', tpl))


def armor_class(tpl):
    v = items.get(tpl, {}).get('_props', {}).get('armorClass', 0)
    try:
        return int(v)
    except (TypeError, ValueError):
        return 0


def preset_class(preset):
    return max((armor_class(n['_tpl']) for n in preset['_items']), default=0)


def coverage(preset):
    """Which body zones the preset's armour actually protects.

    The class number alone is a trap: it is the best plate ANYWHERE in the
    build, so a bare two-plate carrier and a full vest both read "class 6".
    Sorting by component count then quietly preferred the emptiest one at every
    rung — the class went up and the protection did not."""
    zones = set()
    for n in preset['_items']:
        p = items.get(n['_tpl'], {}).get('_props', {})
        for key in ('armorColliders', 'armorPlateColliders'):
            zones.update(p.get(key) or [])
    return len(zones)


def cells(tpl):
    grids = items.get(tpl, {}).get('_props', {}).get('Grids') or []
    return sum(g['_props']['cellsH'] * g['_props']['cellsV'] for g in grids)


# ---------------------------------------------------------------- presets
presets = collections.defaultdict(list)
for p in glob['ItemPresets'].values():
    root = p.get('_encyclopedia') or next((i['_tpl'] for i in p['_items'] if not i.get('parentId')), None)
    if not root:
        continue
    ch = chain(root)[1:]
    kind = 'armor' if ARMOR in ch else 'vest' if VEST in ch else 'head' if HEAD in ch else None
    if kind:
        presets[kind].append((root, p))


def plainest(candidates):
    """One preset per item, the most ordinary build of it."""
    best = {}
    for root, p in candidates:
        key = root
        score = ('default' not in p['_name'].lower(), len(p['_items']))
        if key not in best or score < best[key][0]:
            best[key] = (score, p)
    return [(k, v[1]) for k, v in best.items()]


armors = plainest(presets['armor'])
armored_rigs = [(r, p) for r, p in plainest(presets['vest'])
                if items[r]['_props'].get('BlocksArmorVest')]
helmets = plainest(presets['head'])

plain_rigs = [t for t, v in items.items()
              if is_kind(t, VEST) and not v['_props'].get('BlocksArmorVest') and cells(t) >= 8]
plain_rigs.sort(key=cells, reverse=True)


# Every headset the Earpiece slot accepts, best first. Taken from the slot's own
# filter so a headset added by a mod joins the rotation without an edit here.
_ear_slot = next(sl for sl in items[ROOT_TPL]['_props']['Slots'] if sl['_name'] == 'Earpiece')
EARPIECES = [t for t in _ear_slot['_props']['filters'][0]['Filter'] if t in items]
EARPIECES.sort(key=lambda t: (t == EARPIECE_LAST_RESORT, -handbook_price(t)))


def compatible(candidate, worn):
    """Whether a headset can be worn with what the kit already has on.

    Both directions matter: a helmet may refuse a headset that lists no
    objection to the helmet.
    """
    if set(items[candidate]['_props'].get('ConflictingItems') or []) & worn:
        return False

    return not any(candidate in set(items.get(t, {}).get('_props', {}).get('ConflictingItems') or [])
                   for t in worn)


def open_eared(preset):
    """How many real headsets this helmet build would still let you wear.

    Counted over the whole preset, not just its base item: an ear cover fitted
    to a helmet is what does the blocking, and it is a separate template.
    """
    worn = {i['_tpl'] for i in preset['_items']}
    return sum(1 for e in EARPIECES if e != EARPIECE_LAST_RESORT and compatible(e, worn))


def pick_helmet(target, used):
    """A helmet near the body's class that still leaves the ears free.

    Closed ears cost one and a half classes, which is a deliberate price rather
    than a tie-break. In a corridor arena with instant respawn, hearing someone
    come round the corner is worth more than one step of helmet class: a headshot
    tends to end you at these ranges whatever you are wearing. So an ear-free
    helmet one class off the target wins over a closed one that hits it exactly,
    and an ear-free helmet two classes off does not.

    That is not a free win everywhere. Among the helmets BSG ships a preset for,
    classes 2, 5 and 6 have NO ear-free option at all — the pool there is the
    Kolpak, the Rys-T, the Altyn and the Maska. Classes 2 and 5 can reach a
    neighbouring class and get their hearing back; class 6 cannot, and keeps its
    closed helmet. At the top of the ladder that reads correctly: wearing a Maska
    means wearing an earplug, and the kit is honest about the trade.

    Then variety: a helmet this run has already used loses to one it has not.
    """
    ranked = sorted(helmets, key=lambda h: (
        abs(preset_class(h[1]) - target) + (0 if open_eared(h[1]) else 1.5),
        1 if h[0] in used else 0,
        -coverage(h[1]),
        len(h[1]['_items'])))

    if not ranked:
        return None

    used.add(ranked[0][0])
    return ranked[0]


# ------------------------------------------------------------- gear sets
def build_sets():
    """Light to heavy, alternating the two legal shapes."""
    out = []
    used_helmets = set()
    for cls in (2, 3, 4, 5, 6):
        # shape A: body armour of this class + the roomiest plain rig
        pool = [a for a in armors if preset_class(a[1]) == cls]
        if pool:
            pool.sort(key=lambda a: (-coverage(a[1]), len(a[1]['_items'])))
            # The trade-off that gives a set its character: the better protected
            # you are, the less you carry. Weight barely bites in this arena, so
            # without this every kit would just be "the good one, plus a gun".
            want = 26 - (cls - 2) * 4
            rig = min(plain_rigs, key=lambda t: abs(cells(t) - want))
            out.append(dict(cls=cls, armor=pool[0], rig=rig, rig_preset=None,
                            helmet=pick_helmet(cls, used_helmets)))
        # shape B: an armoured rig of this class, carrying its own protection
        pool = [v for v in armored_rigs if preset_class(v[1]) == cls]
        if pool:
            # Same idea from the other side: among rigs of one class, the
            # roomiest for the light ones and the leanest for the heavy ones.
            pool.sort(key=lambda v: (-coverage(v[1]), cells(v[0]) if cls >= 5 else -cells(v[0])))
            out.append(dict(cls=cls, armor=None, rig=pool[0][0], rig_preset=pool[0][1],
                            helmet=pick_helmet(cls, used_helmets)))
    return out


# ------------------------------------------------------------ assembling
def covers_eyes(tpl):
    """Whether the item armours the eye zone — i.e. sits in front of them.

    BSG's Maska preset ships with its face shield fitted, and the generator took
    it along without asking what it was. It is genuine class 6 protection, and
    in an arena it is a bad trade: it stands between the player and the fight,
    most visibly while falling over dead, and it was never chosen. Detected by
    the zone it protects rather than by name, so a modded shield is caught too.
    """
    p = items.get(tpl, {}).get('_props', {})
    zones = set(p.get('armorColliders') or []) | set(p.get('armorPlateColliders') or [])
    return 'Eyes' in zones


def graft(out, preset, root_id, slot):
    """Copy a preset tree in, reparented onto the equipment root."""
    dropped = {n['_id'] for n in preset['_items'] if covers_eyes(n['_tpl'])}
    remap = {n['_id']: uuid.uuid4().hex[:24] for n in preset['_items']}
    proot = next(i for i in preset['_items'] if not i.get('parentId'))
    for node in preset['_items']:
        if node['_id'] in dropped or node.get('parentId') in dropped:
            continue
        entry = dict(_id=remap[node['_id']], _tpl=node['_tpl'])
        if node is proot:
            entry['parentId'] = root_id
            entry['slotId'] = slot
        else:
            entry['parentId'] = remap[node['parentId']]
            entry['slotId'] = node['slotId']
        if node.get('upd'):
            entry['upd'] = node['upd']
        out.append(entry)


def fill_empty_plates(out, root_id):
    """Some presets ship a carrier with slots still open.

    The generator's whole premise is that a preset arrives fully built, and for
    most of them it does — but the Osprey MK4A leaves both side plates out, and
    an unplated flank on an otherwise class 5 rig is the kind of hole nobody
    would notice until it killed them. Anything still empty gets the cheapest
    plate its own filter accepts.
    """
    added = []
    for node in list(out):
        for slot in items.get(node['_tpl'], {}).get('_props', {}).get('Slots', []):
            name = slot['_name']
            if 'plate' not in name.lower():
                continue
            if any(n.get('parentId') == node['_id'] and n.get('slotId') == name for n in out):
                continue

            candidates = slot['_props']['filters'][0].get('Filter') or []
            plates = [c for c in candidates if armor_class(c) > 0]
            if not plates:
                continue

            plates.sort(key=lambda c: (-armor_class(c), c))
            out.append(dict(_id=uuid.uuid4().hex[:24], _tpl=plates[0],
                            parentId=node['_id'], slotId=name))
            added.append(name)
    return added


def pick_earpiece(nodes, used):
    """The least-worn headset this kit can actually wear, quality breaking ties.

    Least-worn rather than first-unworn. Kits do not all offer the same choice —
    a helmet can rule out some headsets without ruling out all of them — so a
    "have we used this yet" flag empties unevenly and then has to be reset, and
    the resets bunch the top of the list onto the last few kits. Counting uses
    spreads them evenly whatever subset each kit allows, with no special case.

    `used` is a Counter carried across the whole run.
    """
    worn = {n['_tpl'] for n in nodes}
    fits = [e for e in EARPIECES if compatible(e, worn)]
    if not fits:
        return None

    chosen = min(fits, key=lambda e: (used[e], EARPIECES.index(e)))
    used[chosen] += 1
    return chosen


def spare_capacity(rig_tpl, mag_tpl):
    """How many spares fit in the RIG, which is where you reload from.

    The backpack is deliberately left out of the sum: rummaging through it
    mid-fight is not a reload, so counting its space would report a reserve
    the player does not really have.
    """
    p = items.get(mag_tpl, {}).get('_props', {})
    footprint = max(1, p.get('Width', 1) * p.get('Height', 1))
    room = max(0, cells(rig_tpl) - 2)   # a cell or two for the medkit
    return max(1, min(5, int(room * 0.5 // footprint)))


def rebuild():
    old = json.load(io.open(KITS, encoding='utf-8-sig'))
    sets = build_sets()
    print(f"{len(sets)} Ausruestungs-Saetze fuer {len(old)} Kits\n")
    print(f"{'Kit':12} {'Kl':>2} {'Panzerung / Panzer-Rig':34} {'Rig':26} {'Zellen':>6} {'Mags':>4}")

    result = []
    used_earpieces = collections.Counter()
    for i, kit in enumerate(old):
        gear = sets[i % len(sets)]
        weapon_root = next(n for n in kit['items'] if (n.get('slotId') or '') == 'FirstPrimaryWeapon')

        # The weapon subtree, untouched — plus everything this generator has no
        # business replacing. The pockets are the reason: they hold the whole
        # first aid kit, and rebuilding a kit around new armour must not quietly
        # take a player's bandages away.
        carried = [n for n in kit['items']
                   if (n.get('slotId') or '') in CARRY_OVER_SLOTS]
        keep = {weapon_root['_id']} | {n['_id'] for n in carried}
        roots = {weapon_root['_id']} | {n['_id'] for n in carried}

        grew = True
        while grew:
            grew = False
            for n in kit['items']:
                if n.get('parentId') in keep and n['_id'] not in keep:
                    keep.add(n['_id'])
                    grew = True

        root_id = uuid.uuid4().hex[:24]
        out = [dict(_id=root_id, _tpl=ROOT_TPL)]
        for n in kit['items']:
            if n['_id'] not in keep:
                continue
            e = dict(n)
            if n['_id'] in roots:
                e['parentId'] = root_id
            out.append(e)

        if gear['armor']:
            graft(out, gear['armor'][1], root_id, 'ArmorVest')
        if gear['rig_preset']:
            graft(out, gear['rig_preset'], root_id, 'TacticalVest')
        else:
            out.append(dict(_id=uuid.uuid4().hex[:24], _tpl=gear['rig'],
                            parentId=root_id, slotId='TacticalVest'))
        if gear['helmet']:
            graft(out, gear['helmet'][1], root_id, 'Headwear')

        fill_empty_plates(out, root_id)

        ear = pick_earpiece(out, used_earpieces)
        if ear:
            out.append(dict(_id=uuid.uuid4().hex[:24], _tpl=ear,
                            parentId=root_id, slotId='Earpiece'))
        out.append(dict(_id=uuid.uuid4().hex[:24], _tpl=BACKPACK,
                        parentId=root_id, slotId='Backpack'))

        mag = next((n['_tpl'] for n in out if n.get('slotId') == 'mod_magazine'), None)
        spares = spare_capacity(gear['rig'], mag) if mag else 2

        result.append(dict(id=kit['id'], name=kit['name'], order=kit['order'],
                           items=out,
                           fill=dict(spareMagazines=spares,
                                     rigContents=(kit.get('fill') or {}).get('rigContents', []))))

        armour_name = nm(gear['armor'][0]) if gear['armor'] else nm(gear['rig']) + ' (Rig)'
        print(f"{kit['id'][:12]:12} {gear['cls']:>2} {armour_name[:34]:34} "
              f"{nm(gear['rig'])[:26]:26} {cells(gear['rig']):>6} {spares:>4}")

    io.open(KITS, 'w', encoding='utf-8').write(json.dumps(result, indent=2))
    print(f"\ngeschrieben: {KITS}")


rebuild()
