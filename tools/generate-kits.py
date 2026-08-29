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

ARMOR = '5448e54d4bdc2dcc718b4568'
VEST = '5448e5284bdc2dcb718b4567'
HEAD = '5a341c4086f77401f2541505'
EARPIECE_TEP300 = '68bf405779c8186398099017'   # in-ear, conflicts with no helmet
BACKPACK = '5ab8ebf186f7742d8b372e80'          # Transformer bag, as before

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


def pick_helmet(target):
    """A helmet near the body's class, so a set reads as one idea."""
    ranked = sorted(helmets, key=lambda h: (abs(preset_class(h[1]) - target), len(h[1]['_items'])))
    return ranked[0] if ranked else None


# ------------------------------------------------------------- gear sets
def build_sets():
    """Light to heavy, alternating the two legal shapes."""
    out = []
    for cls in (2, 3, 4, 5, 6):
        # shape A: body armour of this class + the roomiest plain rig
        pool = [a for a in armors if preset_class(a[1]) == cls]
        if pool:
            pool.sort(key=lambda a: len(a[1]['_items']))
            # The trade-off that gives a set its character: the better protected
            # you are, the less you carry. Weight barely bites in this arena, so
            # without this every kit would just be "the good one, plus a gun".
            want = 26 - (cls - 2) * 4
            rig = min(plain_rigs, key=lambda t: abs(cells(t) - want))
            out.append(dict(cls=cls, armor=pool[0], rig=rig, rig_preset=None,
                            helmet=pick_helmet(cls)))
        # shape B: an armoured rig of this class, carrying its own protection
        pool = [v for v in armored_rigs if preset_class(v[1]) == cls]
        if pool:
            # Same idea from the other side: among rigs of one class, the
            # roomiest for the light ones and the leanest for the heavy ones.
            pool.sort(key=lambda v: cells(v[0]) if cls >= 5 else -cells(v[0]))
            out.append(dict(cls=cls, armor=None, rig=pool[0][0], rig_preset=pool[0][1],
                            helmet=pick_helmet(cls)))
    return out


# ------------------------------------------------------------ assembling
def graft(out, preset, root_id, slot):
    """Copy a preset tree in, reparented onto the equipment root."""
    remap = {n['_id']: uuid.uuid4().hex[:24] for n in preset['_items']}
    proot = next(i for i in preset['_items'] if not i.get('parentId'))
    for node in preset['_items']:
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
        out = [dict(_id=root_id, _tpl='55d7217a4bdc2d86028b456d')]
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

        out.append(dict(_id=uuid.uuid4().hex[:24], _tpl=EARPIECE_TEP300,
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
