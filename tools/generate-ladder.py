"""Offline fallback generator for the Gun Game weapon pool.

NOT the authority any more. ArenaMode.Server/Services/LadderGenerator.cs builds
the pool at server load, where content mods have already injected their items —
which is the only place it can see them. This script reads the database FILE and
therefore only ever sees what BSG ships.

It stays for two jobs: producing the ladder.json that is shipped as the fallback
for a server where generation fails, and inspecting the ordering by hand without
starting a server. Keep the two in step; the C# side is the one that runs.
"""
_ORIGINAL_DOC = """Builds the Gun Game weapon POOL from the live SPT database.

Every weapon the game can build a working example of, emitted as a weapon-only
entry in ArenaMode's kit format and ordered weakest to strongest. The server
draws the raid's ladder from this pool, one rung per power band, so no two
raids climb the same twenty-four guns.

The hard part stays here rather than in the server: walking the preset trees,
loading magazines (and revolver cylinders, which hold a round per chamber and
ignore the cartridge stack), chambering a round, and fitting a sight to the
weapons whose own presets ship without one. Picking from a sorted list is the
easy half, and that is all the server has to do per raid.
"""
import json, io, collections, uuid

D = r'D:\SPT41\SPT_Runtime\SPT_Data\database'
OUT = r'D:\SPT41\Development\ArenaMode\ArenaMode.Server\defaults\ladder.json'

items = json.load(io.open(D + r'\templates\items.json', encoding='utf-8-sig'))
glob = json.load(io.open(D + r'\globals.json', encoding='utf-8-sig'))
hb = json.load(io.open(D + r'\templates\handbook.json', encoding='utf-8-sig'))
price = {r['Id']: r['Price'] for r in hb['Items']}
loc = json.load(io.open(D + r'\locales\global\en.json', encoding='utf-8-sig'))

WEAPON = '5422acb9af1c889c16000029'
AMMO = '5485a8684bdc2da71d8b4567'
EQUIP_ROOT = '55d7217a4bdc2d86028b456d'
RUNGS = 24

def chain(tpl):
    n, out = tpl, []
    while n in items and len(out) < 12:
        out.append(n); n = items[n].get('_parent')
    return out

def is_kind(tpl, base):
    return base in chain(tpl)[1:]

def name_of(tpl):
    return loc.get(tpl + ' Name') or items.get(tpl, {}).get('_name', tpl)

# ---- ammunition, per caliber, sorted weakest to strongest -------------------
ammo_by_cal = collections.defaultdict(list)
for tpl, t in items.items():
    p = t.get('_props', {})
    if p.get('Caliber') and is_kind(tpl, AMMO):
        pen = p.get('PenetrationPower', 0)
        dmg = p.get('Damage', 0)
        shots = max(1, p.get('ProjectileCount', 1))
        # Ranked on what a round actually DOES, which is not its penetration.
        # Sorting on penetration alone put buckshot at the bottom of every
        # shotgun's list and handed out slugs every time — 8.5mm Magnum throws
        # eight pellets for four hundred damage and reads as "penetration 2",
        # while a Poleva slug reads as 17 and does a third of the work at the
        # range this arena is fought at.
        score = pen * 2.0 + dmg * shots * 0.25
        ammo_by_cal[p['Caliber']].append((tpl, pen, dmg, shots, score))

for cal in ammo_by_cal:
    ammo_by_cal[cal].sort(key=lambda a: a[4])


def accepted_rounds(mag_tpl, weapon_tpl):
    """What the magazine will actually take.

    Calibre alone is not a filter. Grenade shrapnel carries a calibre field and
    a high projectile count, so ranking by what a round DOES promoted F1 and
    VOG-30 fragments above real cartridges and would have handed out a pistol
    loaded with grenade splinters. The magazine's own filter is the authority on
    what fits, so ask it.
    """
    for tpl in (mag_tpl, weapon_tpl):
        carts = items.get(tpl, {}).get('_props', {}).get('Cartridges')
        if carts:
            allowed = carts[0].get('_props', {}).get('filters', [{}])[0].get('Filter')
            if allowed:
                return set(allowed)
    return None


def pick_ammo(cal, accepted=None):
    """A few capable choices for this calibre, not one.

    The pool is generated once and a weapon would otherwise carry the same
    cartridge in every raid it ever appears in. Handing the server a short list
    lets it draw one per raid, so a shotgun is buckshot one evening and slugs
    the next. Taken from the upper middle of the calibre: capable, without
    dealing out the rarest armour-piercing round every time.
    """
    pool = ammo_by_cal.get(cal)
    if not pool:
        return None

    if accepted:
        narrowed = [c for c in pool if c[0] in accepted]
        if narrowed:
            pool = narrowed

    top = min(len(pool) - 1, int(len(pool) * 0.75))
    low = max(0, top - 2)
    return pool[top], [c[0] for c in pool[low:top + 1]]

# ---- one preset per weapon: the plainest build ------------------------------
presets_by_weapon = collections.defaultdict(list)
for p in glob['ItemPresets'].values():
    root = p.get('_encyclopedia') or next((i['_tpl'] for i in p['_items'] if not i.get('parentId')), None)
    if root and is_kind(root, WEAPON):
        presets_by_weapon[root].append(p)

def plainest(ps):
    return sorted(ps, key=lambda p: ('default' not in p['_name'].lower(), len(p['_items'])))[0]

# ---- score and build --------------------------------------------------------
EXCLUDE_CAL = {'Caliber40mmRU', 'Caliber26x75', 'Caliber30x29'}   # launchers/flares
BOLT_ACTION = '5447b6254bdc2dc3278b4568'   # SniperRifle: the category BSG ships without irons
candidates = []
for weap, ps in presets_by_weapon.items():
    props = items[weap].get('_props', {})
    cal = props.get('ammoCaliber')
    if not cal or cal in EXCLUDE_CAL:
        continue
    preset = plainest(ps)
    mag = next((i for i in preset['_items'] if i.get('slotId') == 'mod_magazine'), None)

    picked = pick_ammo(cal, accepted_rounds(mag['_tpl'] if mag else None, weap))
    if not picked:
        continue
    ammo, ammo_choices = picked
    cap = 0
    if mag:
        carts = items.get(mag['_tpl'], {}).get('_props', {}).get('Cartridges')
        if carts:
            cap = carts[0].get('_max_count', 0)
    internal = not mag and bool(props.get('Cartridges'))
    if internal:
        cap = props['Cartridges'][0].get('_max_count', 0)
    if cap <= 0:
        continue

    ammo_tpl, pen, dmg, shots, _score = ammo
    # A rung you cannot get a kill with is a rung the ladder never leaves —
    # the toy gun (damage 1) would end the mode at tier one.
    if dmg < 20:
        continue
    score = pen * 2.0 + dmg * 0.4 + price.get(weap, 0) / 12000.0
    candidates.append(dict(weapon=weap, preset=preset, cal=cal, ammo=ammo_tpl,
                           ammo_choices=ammo_choices, pen=pen, dmg=dmg,
                           cap=cap, mag=mag, internal=internal, score=score,
                           price=price.get(weap, 0), name=name_of(weap)))

candidates.sort(key=lambda c: c['score'])

# The whole pool, weakest first. One entry per weapon: several presets of the
# same gun would otherwise crowd a band and make a "random" ladder keep dealing
# the same rifle in three finishes.
seen, rungs = set(), []
for c in candidates:
    if c['weapon'] in seen:
        continue
    seen.add(c['weapon'])
    rungs.append(c)


# ---- sights ----------------------------------------------------------------
SIGHT_BASES = {t for t, v in items.items()
               if v.get('_type') == 'Node' and any(k in v.get('_name', '') for k in
                  ('Collimator', 'OpticScope', 'AssaultScope', 'SpecialScope', 'IronSight', 'Sights'))}

def is_sight(tpl):
    return bool(SIGHT_BASES & set(chain(tpl)[1:]))

def ensure_sight(out, weapon_tpl):
    """A weapon you cannot aim is a rung nobody wants to reach.

    Some of the game's own presets ship with every sight slot empty — the RSASS
    is the notorious one, and our ladder had the MDR and the Mk-18. Nothing in
    the data forbids it: on those weapons the sight slots are optional, so a
    build is 'complete' without any way to aim. Weapons with fixed iron sights
    have no such slot at all, which is exactly how they are told apart here.
    """
    if any(is_sight(n['_tpl']) for n in out):
        return None

    fitted = fit_sight(out)
    if fitted:
        return fitted

    # Nothing took directly. Some weapons have no sight slot of their own and
    # reach an optic only through a RAIL — the Remington 700 is the one in this
    # pool, and its default preset ships without the rail. Bolt it on, then try
    # again: the rail brings the slot the weapon lacks.
    #
    # This is also where the usual discriminator breaks down. Everywhere else
    # "no sight slot at all" means fixed iron sights and nothing to do; here it
    # means no sights and no way to fit any, which looks identical from the
    # slot list and is the opposite situation.
    # Only for bolt-action rifles. Everywhere else "no sight slot" really does
    # mean fixed irons, and bolting a rail and a red dot onto a Kedr or an UZI
    # would change sixty weapons to fix one.
    if is_kind(weapon_tpl, BOLT_ACTION) and fit_mount(out):
        return fit_sight(out)

    return None


def fit_mount(out):
    """Adds the cheapest rail that carries an optic slot of its own."""
    for node in list(out):
        for s in items.get(node['_tpl'], {}).get('_props', {}).get('Slots', []):
            if not s['_name'].startswith('mod_mount'):
                continue
            if any(n.get('parentId') == node['_id'] and n.get('slotId') == s['_name'] for n in out):
                continue

            cands = []
            for f in s['_props']['filters'][0]['Filter']:
                slots = items.get(f, {}).get('_props', {}).get('Slots', [])
                if not any(x['_name'] == 'mod_scope' for x in slots):
                    continue
                if [x for x in slots if x.get('_required')]:
                    continue
                cands.append((price.get(f, 10 ** 9), f))

            if not cands:
                continue

            cands.sort()
            out.append(dict(_id=str(uuid.uuid4().hex[:24]), _tpl=cands[0][1],
                            parentId=node['_id'], slotId=s['_name']))
            return cands[0][1]
    return None


def fit_sight(out):
    """Puts an optic into the first sight slot anything in the build offers."""
    for slot_name in ('mod_scope', 'mod_sight_rear'):
        for node in list(out):
            for s in items.get(node['_tpl'], {}).get('_props', {}).get('Slots', []):
                if s['_name'] != slot_name:
                    continue
                if any(n.get('parentId') == node['_id'] and n.get('slotId') == slot_name for n in out):
                    continue

                # Reflex sights first, then price: this is a close-quarters
                # arena, where magnification is a handicap, not a reward.
                COLLIMATORS = {t for t in SIGHT_BASES
                               if 'Collimator' in items[t].get('_name', '')}
                cands = []
                for f in s['_props']['filters'][0]['Filter']:
                    if not is_sight(f):
                        continue
                    # A mount that needs its own optic just moves the hole down.
                    if [x for x in items.get(f, {}).get('_props', {}).get('Slots', []) if x.get('_required')]:
                        continue
                    reflex = bool(COLLIMATORS & set(chain(f)[1:]))
                    cands.append((not reflex, price.get(f, 10 ** 9), f))

                if not cands:
                    continue

                cands.sort()
                out.append(dict(_id=str(uuid.uuid4().hex[:24]), _tpl=cands[0][2],
                                parentId=node['_id'], slotId=slot_name))
                return cands[0][2]
    return None

def build_entry(order, c):
    """Weapon-only kit entry: synthetic equipment root + the preset subtree."""
    remap, out = {}, []
    root_id = str(uuid.uuid4().hex[:24])
    out.append(dict(_id=root_id, _tpl=EQUIP_ROOT))

    preset_root = next(i for i in c['preset']['_items'] if not i.get('parentId'))
    for node in c['preset']['_items']:
        remap[node['_id']] = str(uuid.uuid4().hex[:24])

    for node in c['preset']['_items']:
        entry = dict(_id=remap[node['_id']], _tpl=node['_tpl'])
        if node is preset_root or not node.get('parentId'):
            entry['parentId'] = root_id
            entry['slotId'] = 'FirstPrimaryWeapon'
        else:
            entry['parentId'] = remap[node['parentId']]
            entry['slotId'] = node['slotId']
        if node.get('upd'):
            entry['upd'] = node['upd']
        out.append(entry)

    # Presets ship empty: load the magazine and chamber a round.
    mag_node = next((n for n in out if n.get('slotId') == 'mod_magazine'), None)
    if mag_node:
        # A revolver cylinder holds one round per camora SLOT and never reads the
        # cartridge stack — a stack put there is invisible to the weapon, which
        # then arrives with an empty gun and no warning anywhere.
        mag_props = items[mag_node['_tpl']].get('_props', {})
        camoras = [s['_name'] for s in mag_props.get('Slots', [])
                   if s.get('_name', '').startswith('camora_')]

        if camoras:
            for cam in camoras:
                out.append(dict(_id=str(uuid.uuid4().hex[:24]), _tpl=c['ammo'],
                                parentId=mag_node['_id'], slotId=cam,
                                upd=dict(StackObjectsCount=1)))
        else:
            out.append(dict(_id=str(uuid.uuid4().hex[:24]), _tpl=c['ammo'],
                            parentId=mag_node['_id'], slotId='cartridges',
                            upd=dict(StackObjectsCount=c['cap'])))
    if items[c['weapon']].get('_props', {}).get('Chambers'):
        out.append(dict(_id=str(uuid.uuid4().hex[:24]), _tpl=c['ammo'],
                        parentId=remap[preset_root['_id']], slotId='patron_in_weapon',
                        upd=dict(StackObjectsCount=1)))

    added = ensure_sight(out, c['weapon'])
    if added:
        print(f"   Visier ergaenzt fuer {c['name'][:34]}: {name_of(added)}")

    spares = 3 if c['cap'] < 20 else 2
    return dict(id='rung' + str(order + 1).zfill(3), name=c['name'], order=order,
                items=out, fill=dict(spareMagazines=spares, rigContents=[]),
                # Interchangeable cartridges for this weapon; the server swaps
                # one in per raid so the same gun is not always loaded the same.
                ammoChoices=c['ammo_choices'])

ladder = [build_entry(i, c) for i, c in enumerate(rungs)]
io.open(OUT, 'w', encoding='utf-8').write(json.dumps(ladder, indent=2))

print(f"Kandidaten: {len(candidates)}  ->  {len(ladder)} Pool-Eintraege (je Waffe einer)\n")
bands = 24
print(f"So saehe eine gezogene Leiter aus ({bands} Sprossen, ein Band je Sprosse):")
for i in range(bands):
    lo = i * len(rungs) // bands
    hi = max(lo + 1, (i + 1) * len(rungs) // bands)
    band = rungs[lo:hi]
    names = ' | '.join(c['name'][:22] for c in band[:3])
    if len(band) > 3:
        names += f' | +{len(band) - 3}'
    print(f"  {i+1:>2}. aus {len(band)}: {names}")
print(f"\ngeschrieben: {OUT}")
