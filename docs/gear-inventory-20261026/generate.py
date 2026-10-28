# Static documentation survey. No game imports or installation writes.
import json, pathlib, collections, copy, re, hashlib, sys
sys.stdout.reconfigure(encoding='utf-8')
import argparse
parser=argparse.ArgumentParser(description="Read-only SPT + WTT gear inventory; writes only the chosen documentation directory.")
parser.add_argument("--runtime",type=pathlib.Path,default=pathlib.Path(r"D:\SPT41\SPT_Runtime"))
parser.add_argument("--output",type=pathlib.Path,default=pathlib.Path(__file__).resolve().parent)
args=parser.parse_args()
root=args.runtime
out=args.output
source_paths=[root/'SPT_Data/database/templates/items.json',root/'SPT_Data/database/templates/handbook.json',root/'SPT_Data/database/locales/global/en.json',root/'user/mods/WTT-ContentBackport/db/CustomLocales/en.json',root/'user/mods/WTT-ContentBackport/WTT-ContentBackport.dll']
source_paths += sorted((root/'user/mods/WTT-ContentBackport/db/CustomItems').rglob('*.json'))
source_hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in source_paths}
# This dated survey contains manually reviewed interpretation. Fail before any
# output when the source dataset changed, rather than silently recycling it.
reviewed_fingerprint='18c4ba2a16c0c9724aacbdc422f3d59c51bfaabd00c357565739701b59988790'
assert hashlib.sha256(json.dumps(source_hashes,sort_keys=True).encode()).hexdigest()==reviewed_fingerprint, 'Source dataset changed; review the classification and narrative before producing a new dated survey'
base_path=root/'SPT_Data/database/templates/items.json'
base=json.loads(base_path.read_text(encoding='utf-8-sig'))
loc=json.loads((root/'SPT_Data/database/locales/global/en.json').read_text(encoding='utf-8-sig'))
wtt=root/'user/mods/WTT-ContentBackport/db'
loc.update(json.loads((wtt/'CustomLocales/en.json').read_text(encoding='utf-8-sig')))
defs={}; files={}; dupes=[]
for f in sorted((wtt/'CustomItems').rglob('*.json')):
    data=json.loads(f.read_text(encoding='utf-8-sig'))
    if not isinstance(data,dict): raise Exception(str(f))
    for k,v in data.items():
        if not isinstance(v,dict) or 'itemTplToClone' not in v: raise Exception((str(f),k))
        if k in defs: dupes.append((k, files[k],str(f.relative_to(wtt))))
        defs[k]=v;files[k]=str(f.relative_to(wtt))
items=copy.deepcopy(base)
def resolve(k,seen=None):
    if k not in defs: return items[k]
    if items.get(k,{}).get('_wtt'):return items[k]
    seen=set() if seen is None else seen
    if k in seen: raise Exception('cycle '+k)
    seen.add(k);v=defs[k];clone=v['itemTplToClone']
    original=base[k] if clone==k else resolve(clone,seen)
    x=copy.deepcopy(original);x['_id']=k;x['_parent']=v.get('parentId') or x['_parent']
    x['_props'].update(v.get('overrideProperties',{}));x['_wtt']=True
    items[k]=x;return x
for k in defs:
    resolve(k)
    english=defs[k].get('locales',{}).get('en',{})
    for key in ['name','shortName','description']:
        loc.setdefault(k+' '+key[0].upper()+key[1:],english.get(key,''))
def name(k):return loc.get(k+' Name',items[k].get('_props',{}).get('Name',items[k].get('_name',k)))
def cat(k):return items.get(items[k].get('_parent'),{}).get('_name','Unknown')
def ancestors(k):
    a=[];p=items[k].get('_parent')
    while p and p in items:
        a.append(items[p].get('_name'));p=items[p].get('_parent')
    return a
weapons=[k for k,x in items.items() if x.get('_type')=='Item' and 'Weapon' in ancestors(k)]

# Proposed gameplay pools, not geopolitical or manufacturer-origin assertions.
WEST = "West/NATO-Stil"
EAST = "Ostblock/Ost-Stil"
SHARED = "Gemeinsam/neutral"
OPEN = "Offen"
EXCLUDED = "Ausserhalb Kitvergleich"
handbook_path=root/'SPT_Data/database/templates/handbook.json'
handbook=json.loads(handbook_path.read_text(encoding='utf-8-sig'))
hids={x['Id'] for x in handbook['Items']}
prices={x['Id']:x.get('Price') for x in handbook['Items']}
for k,v in defs.items():
    if v.get('registerInHandbook', True): hids.add(k)
    prices[k]=v.get('handbookPriceRoubles')
def real_name(k):
    return loc.get(k+' Name') or items[k].get('_props',{}).get('Name') or items[k].get('_name') or k
name=real_name
def contains(pattern,text):return re.search(pattern,text,re.I) is not None
def weapon_side(k):
    n=name(k)
    if contains(r'^(ADAR|Soyuz-TM|Custom Guns NL545)',n):
        return WEST,"AR-Plattform; Herkunft nicht ausschlaggebend"
    if contains(r'^(Rifle Dynamics RD-704)',n):
        return EAST,"AK-Plattform; US-Hersteller nicht ausschlaggebend"
    if contains(r'^(AS VAL|ASh-12|KBP |Molot |SR-3M |TOZ |TOZ-|Tokarev |Kalashnikov |SAG |SVDS |TKPD |VSS |Lebedev |Makarov |PB |Serdyukov |Stechkin |TT-33 |Yarygin |RSh-|RShG-|MP-1[358]|MP-43|Saiga-|PP-|PPSh-|SR-2M |Lobaev |Mosin |ORSIS |SV-98|RPK-|Degtyarev |MTs-|Norinco )',n):
        return EAST,"Oestliche Waffenfamilie; moderne/chinesische Modelle eingeschlossen"
    if contains(r'^(Aklys |CMMG |Colt |DS Arms |Desert Tech |FN |FN40|HK |Howa |Lone Star |Radian |SIG |Steyr |U.S. Ordnance |Kel-Tec |Knight|Remington |SWORD |Springfield |TheAKGuy |Beretta |Glock |Magnum Research |Chiappa |Milkor |Benelli |MPS |Mossberg |B&T |IWI |TDI |Accuracy |Marlin |Sako )',n):
        return WEST,"Westliche Waffenfamilie; nicht auf NATO-Mitgliedslaender begrenzt"
    return OPEN,"Waffenfamilie noch nicht zugeordnet"
def weapon_exclusion(k):
    p=items[k]['_props'];n=name(k)
    if k=='5ae083b25acfc4001a5fc702':return "Master Hand: internes Template"
    if p.get('IsStationaryWeapon'):return "Stationaere Waffe"
    if k in {'639af924d0446708ee62294e','639c3fbbd0446708ee622ee9'}:return "Interner Unterlaufwerfer-Controller; kein zusaetzliches Waffenmodell"
    if k=='657857faeff4c850222dff1b':return "PKTM-Fahrzeugwaffe; fehlt im Handbook"
    if p.get('IsFlareGun') or p.get('ammoCaliber')=='Caliber26x75':return "Signal-/Leuchtwaffe"
    if 'toy gun' in n.lower():return "Spielzeug"
    return ""
play_weapons=[k for k in weapons if not weapon_exclusion(k)]
wside={k:weapon_side(k)[0] for k in play_weapons}
assert all(s in {WEST,EAST} for s in wside.values()), [(name(k),v) for k,v in wside.items() if v==OPEN]
weapon_categories={"AssaultRifle":"Sturmgewehre/AR-Karabiner","AssaultCarbine":"Karabiner/Spezialgewehre","Smg":"SMG/PCC","Shotgun":"Schrotflinten","MarksmanRifle":"DMR","SniperRifle":"Repetierer/Einzellader","Pistol":"Pistolen/Revolver","Revolver":"Pistolen/Revolver","MachineGun":"Maschinengewehre","GrenadeLauncher":"Granatwerfer","RocketLauncher":"Raketenwerfer"}
def weapon_kind(k):
    if k=='60db29ce99594040e04c4a27':return "Schrotflinten"
    if k=='6275303a9f372d6ea97f9ec7':return "Granatwerfer"
    if k=='61f7c9e189e6fb1a5e3ea78d':return "Repetierer/Einzellader"
    return weapon_categories.get(cat(k),cat(k))
cosmetic_re=re.compile(r'\s*\((?!DI\)|GP\)|Assault Kit|Full Protection Kit|High Mobility Kit|General Purpose|Medic|Assault\)|Protection\)|Recon\)|Lightweight|Sniper\)|Infantry\))[^)]*\)',re.I)
def model(k):
    n=cosmetic_re.sub('',name(k)).strip()
    return re.sub(r'\s+',' ',n).casefold()
def family(k):
    n=name(k)
    rules=[
      (r'Kalashnikov AK-10[12]','AK 5.56'),(r'Kalashnikov AK-30','AK 7.62x51'),
      (r'Kalashnikov AK-10[34]|Kalashnikov AKM|RD-704|VPO-136','AK 7.62x39'),
      (r'Kalashnikov AK-105|Kalashnikov AK-12|Kalashnikov AK[ -]74|Kalashnikov AKS-74|SAG AK-545','AK 5.45'),
      (r'Colt M[14]|ADAR |TX-15|Radian ','AR-15 5.56'),(r'NL545','AR-15 5.45 (NL545)'),
      (r'SCAR-H','SCAR-H'),(r'SCAR-L','SCAR-L'),(r'AUG','AUG'),(r'MDR','MDR'),
      (r'AS VAL|VSS Vintorez','VAL/VSS'),(r'9A-91|VSK-94','9A-91/VSK-94'),
      (r'Simonov','SKS'),(r'Tokarev [AS]VT','SVT/AVT'),(r'M60','M60'),
      (r'Degtyarev RPD','RPD'),(r'Kalashnikov PK[MP]','PKM/PKP'),(r'SR-25','SR-25'),
      (r'SVDS|TKPD','SVD/TKPD'),(r'Glock','Glock'),(r'M1911|M45A1','1911'),
      (r'Desert Eagle','Desert Eagle'),(r'Five-seveN','Five-seveN'),(r'Makarov PM','Makarov'),
      (r'Stechkin','APS/APB'),(r'TT-33','TT-33'),(r'Chiappa','Rhino'),
      (r'MP-133|MP-153|MP-155','MP-133/153/155'),(r'MP-43','MP-43'),(r'Assault-12','AA-12'),
      (r'Saiga-12','Saiga-12'),(r'B&T MP9','MP9'),(r'P90','P90'),(r'HK MP5','MP5'),
      (r'HK MP7','MP7'),(r'IWI UZI','UZI'),(r'PP-9 ','Kedr/Klin'),(r'PP-91','Kedr/Klin'),
      (r'PP-19|Saiga-9','Vityaz/Saiga-9'),(r'KRISS Vector','Vector'),(r'Mosin','Mosin'),
      (r'HK 416','HK416')]
    for pat,label in rules:
        if contains(pat,n):return label
    return model(k)
# Expand declared clone-slot additions into a potential final compatibility view.
# Closure avoids depending on JSON enumeration order. It deliberately does NOT
# emulate a running server or validate conflicting parts / assembled presets.
clone_slot_additions=0
for _ in range(len(defs)+1):
    changed=False
    for new_id,config in defs.items():
        if not config.get('addtoModSlots'):continue
        target_names={n.lower() for n in config.get('modSlot',[])}
        donor=config['itemTplToClone']
        for parent in items.values():
            for slot in parent.get('_props',{}).get('Slots',[]):
                if slot.get('_name','').lower() not in target_names:continue
                filters=slot.get('_props',{}).get('filters',[])
                if not filters:continue
                allowed=filters[0].get('Filter',[])
                if donor in allowed and new_id not in allowed:
                    allowed.append(new_id);changed=True;clone_slot_additions+=1
    if not changed:break
else:raise AssertionError("Slot expansion did not converge")
# Potential reachability through static slot filters. This is NOT a validated build.
edges={k:set() for k in items}
for k,x in items.items():
    for slot in x.get('_props',{}).get('Slots',[]):
        for f in slot.get('_props',{}).get('filters',[]):
            edges[k].update(t for t in f.get('Filter',[]) if t in items and items[t].get('_type')=='Item')
reach_sides=collections.defaultdict(set)
reach_weapons=collections.defaultdict(set)
for w in play_weapons:
    todo=list(edges[w]);seen=set()
    while todo:
        k=todo.pop()
        if k in seen:continue
        seen.add(k);reach_sides[k].add(wside[w]);reach_weapons[k].add(w)
        todo.extend(edges[k]-seen)
caliber_sides=collections.defaultdict(set)
for k in play_weapons:
    caliber_sides[items[k]['_props'].get('ammoCaliber')].add(wside[k])
# PMM shares the native PM calibre templates; not a separate ammo catalogue.
caliber_sides['Caliber9x18PM'].update(caliber_sides.get('Caliber9x18PMM',set()))
# GP-25 is an attachment, not a separate handheld rifle.
caliber_sides['Caliber40mmRU'].add(EAST)
west_gear=r'\b(5\.11|3V Gear|BlackHawk|BlackRock|Blue Force|Camelbak|CQC|Crye|Direct Action|Eberlestock|ECLiPSE|Eagle |Ferro |First Spear|Haley |Hexatac|HighCom|IOTV|Interceptor|LBT-|NFM |PACA|S&S |Shellback|SOE |Spiritus|Tasmanian|Velocity Systems|Vertx|Zulu Nylon|Mystery Ranch|Hazard 4|Oakley|Ballistic Armor|DevTac|Diamond Age|Galvion|MSA |MTEK|Ops-Core|Team Wendy|TW EXFIL|Peltor|Safariland|Walker|CENS|USEC|Wilcox|Armasight|FLIR|Trijicon|EOTech|Aimpoint|Leupold|Vortex|Nightforce|Schmidt|Steiner|Burris|Valday REAP|AN/PVS|GPNVG|L3Harris|Torrey Pines|ER FULCRUM|KA-BAR|Microtech|Miller Bros|SOG |SP-8 |United Cutlery)'
east_gear=r'\b(6B[0-9]|6Sh|ANA Tactical|Ars Arma|Azimut|BNTI|FORT |NPP |WARTECH|Stich Profi|Splav|Gear Craft|Poyas|UMTBS|Type 56|Dynaforce|Gruppa 99|SSO |Tehinkom|VKBO|Altyn|Atlant Armour|Kiver|Kolpak|LShZ|Maska|PSh-97|Rys-T|SSh-|SSSh-|Tor-2|Vulkan|ZSh-|TSh-|GSSh|BEAR|Ushanka|6Kh5|Bars A-2607|NKVD|MPL-50|PR-Taran|Taiga-1|Zenit|BelOMO|NPZ |KMZ |Shakhin|Arbalet|Dedal|PNV-|1PN|NSPU|PSO-|OKP-|Kobra|Valday|VOMZ|Pilad|Ekran)'
def pool_from_sides(sides):
    if len(sides)==2:return SHARED
    return next(iter(sides)) if sides else OPEN
def static_armor(k):
    p=items[k]['_props'];slots=[]
    for s in p.get('Slots',[]):
        for f in s.get('_props',{}).get('filters',[]):
            q=f.get('Plate')
            if q in items:
                slots.append({'slot':s.get('_name'), 'templateId':q,'class':int(items[q]['_props'].get('armorClass') or 0),'locked':bool(f.get('locked'))})
    return slots
def default_class(k):
    p=items[k]['_props'];a=static_armor(k)
    for wanted in ['front_plate','soft_armor_front','helmet_top']:
        hit=[x['class'] for x in a if x['slot'].lower()==wanted]
        if hit:return max(hit)
    return int(p.get('armorClass') or 0)
def equipment_category(k):
    c=cat(k);a=ancestors(k);p=items[k]['_props']
    if 'Weapon' in a:return "Waffen"
    if c=='Armor':return "Schutzwesten"
    if c=='Vest':return "Gepanzerte Rigs" if p.get('BlocksArmorVest') or static_armor(k) else "Ungepanzerte Rigs"
    if c=='Headwear':return "Helme" if static_armor(k) or contains(r'\bhelmet\b',name(k)) else "Kopfbedeckungen"
    if c=='Backpack':return "Rucksaecke"
    if c=='Headphones':return "Headsets"
    if c=='BuiltInInserts':return "Feste Panzerungseinsaetze"
    if c=='ArmorPlate':return "Wechselplatten"
    if c=='ArmoredEquipment':return "Helmzubehoer/Panzerteile"
    if c=='Visors':return "Brillen"
    if c=='FaceCover':return "Gesichtsbedeckungen"
    if c=='ArmBand':return "Armbaender"
    if c=='ThrowWeap':return "Wurfgranaten"
    if c=='Knife':return "Nahkampf"
    if 'Meds' in a:return "Medizin"
    if c in ['Ammo','Rocket']:return "Munition"
    if 'Magazine' in a or c=='Magazine':return "Magazine"
    if 'Mod' in a:
        if any(q in a+[c] for q in ['Sights','NightVision','ThermalVision']):return "Visiere/Nacht-/Waermesicht"
        return "Waffenzubehoer"
    if c in ['SpecItem','PortableRangeFinder','RepairKits','Compass','RadioTransmitter','Multitools']:return "Werkzeuge/Spezial"
    return None
def exclude_reason(k):
    p=items[k]['_props'];c=cat(k);n=name(k)
    if 'Weapon' in ancestors(k):return weapon_exclusion(k)
    if 'Toy gun' in n or n=='PAG-17 scope':return 'Zubehoer zu Spielzeug/stationaerer Waffe'
    if c=='BuiltInInserts':return "Festes Teil einer Ruestung; kein eigenstaendiges Kit"
    if p.get('QuestItem'):return "Questgegenstand"
    if contains(r'DO[_ ]NOT[_ ]USE|shrapnel|^Infectious strike$|Master Hand',n):return "System-/unbenutztes Template"
    if c in ['Ammo','Rocket'] and (p.get('Caliber') in ['Caliber26x75','Caliber20x1mm','Caliber127x108','Caliber30x29'] or 'Airsoft' in n):
        return "Signal-/Spielzeug-/stationaere Munition"
    if not loc.get(k+' Name'):return "Keine sichtbare Bezeichnung; manuell pruefen"
    if k not in hids:return "Kein Handbook-Eintrag; nicht als regulaeres Angebot gezaehlt"
    return ""
def classify(k):
    c=cat(k);n=name(k);a=ancestors(k);p=items[k]['_props'];category=equipment_category(k)
    if 'Weapon' in a:return weapon_side(k)
    if category=='Munition':
        s=caliber_sides.get(p.get('Caliber'),set())
        return pool_from_sides(s),"Kaliber der erfassten Waffen; Patronentyp-Filter vor Kitbau pruefen"
    if category in ['Magazine','Waffenzubehoer','Visiere/Nacht-/Waermesicht']:
        if reach_sides[k]:return pool_from_sides(reach_sides[k]),"Statische Slot-Erreichbarkeit inkl. WTT-Klonerweiterungen; keine komplette Buildvalidierung"
        if c in ['NightVision','ThermalVision']:return SHARED,"Persoenliche Nachtsicht; fuer beide Seiten vorgesehen"
        if contains(r'AN/AVS|AN/PVS|PVS-7|Norotos|Ops-Core|PNV-10T|Wilcox L4G24|Armasight N-15',n):
            return SHARED,"Helm-/NVG-Zubehoer; nicht ueber den Waffen-Slotgraph bewertet"
        if contains(r'AR-10|AR-15|FN SCAR|HK G36|SilencerCo|Elcan|KAC',n):
            return WEST,"Benannte westliche Plattform; statischer Slotpfad nicht bestaetigt"
        if contains(r'^PKP|^GK-01',n):
            return EAST,"Benannte oestliche Plattform; statischer Slotpfad nicht bestaetigt"
        return OPEN,"Keine Verbindung zu erfassten Waffen in statischen Slotfiltern"
    if category in ['Medizin','Werkzeuge/Spezial']:return SHARED,"Gemeinsamer Verbrauchs-/Werkzeugpool empfohlen"
    if c=='ThrowWeap':
        if contains(r'^(M18|M67|Model 7290|V40)',n):return WEST,"Westliche Granatenfamilie"
        return EAST,"Oestliche Granatenfamilie"
    if c=='ArmorPlate':
        if contains(r'^(6B|Granit|Korund|Zhuk)',n):return EAST,"Oestliche Plattenform/Familie"
        if contains(r'^(ESAPI|ESBI|SAPI|SSAPI|Tac-Kek SAPI)',n):return WEST,"SAPI/ESAPI-Familie"
        return SHARED,"Gemeinsamer kommerzieller Plattenpool; passende Slots erforderlich"
    if contains(east_gear,n):return EAST,"Oestliche Gear-Familie/erkennbare Stilzuordnung"
    if contains(west_gear,n):return WEST,"Westliche Gear-Familie/erkennbare Stilzuordnung"
    if contains(r'Tac-Kek FAST|Tac-Kek JayPC|Flyye MBSS|MF-UNTAR|UNTAR helmet',n):return WEST,"Westliche Vorlage; Replik/UN-Variante"
    if category in ['Kopfbedeckungen','Gesichtsbedeckungen','Brillen','Armbaender','Nahkampf','Rucksaecke','Ungepanzerte Rigs']:
        return SHARED,"Zivil-/Neutral-/Sonderausruestung; kein exklusiver Pool vorgeschlagen"
    if contains(r"^(Champion's helmet|Jack-o'-lantern|SHIELD |ShPM Firefighter|DRD body armor)",n):return SHARED,"Sonder-/Zivilausruestung ohne exklusiven Pool"
    if n.startswith('OPSMEN'):return SHARED,"Kommerzielles Headset; gemeinsamer Pool empfohlen"
    return OPEN,"Stilzuordnung vor exklusiver Vergabe pruefen"
rows=[]
for k,x in items.items():
    if x.get('_type')!='Item':continue
    category=equipment_category(k)
    if not category:continue
    p=x['_props']; side,reason=classify(k);excluded=exclude_reason(k)
    row={'id':k,'name':name(k),'source':'WTT' if k in defs else 'SPT-Basis','sourceFile':files.get(k,'SPT_Data/database/templates/items.json'),'category':category,'nativeCategory':cat(k),'pool':side,'assignmentReason':reason,'counted':not bool(excluded),'exclusionReason':excluded,'modelWithoutCosmetics':model(k),'handbook':k in hids}
    if category=='Waffen':
        row.update(weaponClass=weapon_kind(k),family=family(k),caliber=p.get('ammoCaliber'),fireModes=p.get('weapFireType'),rpm=p.get('bFirerate'),singleRpm=p.get('SingleFireRate'))
    if category=='Munition':row.update(caliber=p.get('Caliber'),damage=p.get('Damage'),penetration=p.get('PenetrationPower'))
    if category in ['Schutzwesten','Gepanzerte Rigs','Helme','Wechselplatten','Helmzubehoer/Panzerteile','Feste Panzerungseinsaetze']:
        row.update(defaultFrontOrTopClass=default_class(k),armorSlots=static_armor(k))
    if category in ['Rucksaecke','Ungepanzerte Rigs','Gepanzerte Rigs']:
        row['storageCells']=sum(g.get('_props',{}).get('cellsH',0)*g.get('_props',{}).get('cellsV',0) for g in p.get('Grids',[]))
    if category in ['Magazine','Waffenzubehoer','Visiere/Nacht-/Waermesicht']:
        row['potentialWeaponSides']=sorted(reach_sides[k]);row['potentialWeaponCount']=len(reach_weapons[k])
    rows.append(row)
rows.sort(key=lambda r:(r['category'],r['pool'],r['name'],r['id']))

# Reproducible source checks; no game/server/profile operation is performed.
assert not dupes,dupes
assert len(rows)==len({r['id'] for r in rows})
assert set(defs).isdisjoint(base)
assert all(r['pool'] in [WEST,EAST,SHARED,OPEN] for r in rows)
assert all(r['counted'] != bool(r['exclusionReason']) for r in rows)
assert all(r['pool'] in [WEST,EAST] for r in rows if r['category']=='Waffen' and r['counted'])
assert all(hashlib.sha256((root/p).read_bytes()).hexdigest()==h for p,h in source_hashes.items()), "Input changed during inventory"
counted=[r for r in rows if r['counted']]
gunrows=[r for r in counted if r['category']=='Waffen']
pools=[WEST,EAST,SHARED,OPEN]
def counts(rr):return [sum(r['pool']==s for r in rr) for s in pools]
def pretty(c):
    return {'Armbaender':'Armbänder','Rucksaecke':'Rucksäcke','Waffenzubehoer':'Waffenzubehör','Helmzubehoer/Panzerteile':'Helmzubehör/Panzerteile','Visiere/Nacht-/Waermesicht':'Visiere/Nacht-/Wärmesicht','Feste Panzerungseinsaetze':'Feste Panzerungseinsätze'}.get(c,c)
def esc(s):return str(s if s is not None else '').replace('|','/').replace('\n',' ')
def table(headers,values):
    return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+['| '+' | '.join(esc(v) for v in row)+' |' for row in values])
metadata={
 'date':'2026-10-26','scope':'Installed SPT file database + WTT CustomItems, static design inventory; not a live post-load database export',
 'sptFileVersion':'4.1.6','wttDllFileVersion':'2.0.5.0',
 'baseTemplates':len(base),'baseItems':sum(x.get('_type')=='Item' for x in base.values()),
 'wttCustomItemDefinitions':len(defs),'wttInputFiles':len(list((wtt/'CustomItems').rglob('*.json'))),
 'overlappingIds':len(set(base)&set(defs)),'gearRows':len(rows),'comparisonRows':len(counted),
 'weaponRows':len(gunrows),'excludedGearRows':len(rows)-len(counted),
 'potentialCloneSlotAdditions':clone_slot_additions,
 'reviewedSourceFingerprint':reviewed_fingerprint,
 'sourceRoot':str(root),'sourceSha256':source_hashes,
 'classification':'User-approved platform/style split; not manufacturer-country or historical NATO membership',
 'notModelled':['Actual server load success and load order','APBS/SVM or other runtime stat/filter edits','WTT preset spawning and conflicts','Trader/quest unlocks','Cosmetic customization outfits/heads/voices','Food/drinks and quest/barter loot','Dynamic Arena Molotov template (documented separately)'],
 'sidePools':pools}
out.mkdir(parents=True,exist_ok=True)
(out/'inventory.json').write_text(json.dumps({'metadata':metadata,'items':rows},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
detail=["# Gear-Inventur: vollständige Gegenstandsliste",
"Stand 26.10.2026. Methodik und Interpretation: [Bericht](REPORT.md). Rohdaten: [inventory.json](inventory.json).",
"Separater, **noch nicht installierter Zusatzbestand**: [WTT-Armory 3.0.0 — alle 833 Einträge](ARMORY.md). Die nachfolgenden Tabellen enthalten weiterhin nur die installierte Basis und ContentBackport.",
"Einzelne Templates; Namen entsprechen den englischen installierten Locales. WTT bezeichnet zusätzliche IDs aus WTT-ContentBackport. Offen wird nicht stillschweigend einer Seite zugeschlagen.",
"Klasse meint den deklarierten Standard-Frontplatten-/Frontweichpanzerungswert bzw. den Helmet-top-Einsatz. Bei Wechselplatten ist es deren eigene Klasse. Das ist keine Aussage über ein tatsächlich zusammengestelltes Kit.",
"Grund dokumentiert den Zuordnungsvorschlag; bei Zubehör ist die Erreichbarkeit nur eine potenzielle Slot-Verbindung, keine Freigabe für einen vollständigen Waffenbau."]
for c in sorted({r['category'] for r in counted}):
    detail+=['## '+pretty(c)]
    for s in pools:
        rr=[r for r in counted if r['category']==c and r['pool']==s]
        if not rr:continue
        detail+=['### '+s+' — '+str(len(rr))]
        if c=='Waffen':
            detail.append(table(['Gegenstand','Klasse','Kaliber','Modi','Quelle','Template-ID'],
             [[r['name'],pretty(r['weaponClass']),r.get('caliber'),', '.join(r.get('fireModes') or []),r['source'],r['id']] for r in rr]))
        elif c=='Munition':
            detail.append(table(['Gegenstand','Kaliber','Schaden','Penetration','Quelle','Template-ID'],
             [[r['name'],r.get('caliber'),r.get('damage'),r.get('penetration'),r['source'],r['id']] for r in rr]))
        else:
            detail.append(table(['Gegenstand','Klasse','Stauraum','Quelle','Template-ID','Grund'],
             [[r['name'],r.get('defaultFrontOrTopClass',''),r.get('storageCells',''),r['source'],r['id'],r['assignmentReason']] for r in rr]))
detail+=['## Aus dem direkten Kitvergleich herausgenommen',
"Diese Gegenstände bleiben in der Inventur erhalten. Ein fehlender Handbook-Eintrag ist nur ein konservativer Filter; er beweist keine technische Unbenutzbarkeit.",
table(['Gegenstand','Kategorie','Quelle','Grund','Template-ID'],
 [[r['name'],pretty(r['category']),r['source'],r['exclusionReason'],r['id']] for r in rows if not r['counted']])]
(out/'ITEMS.md').write_text('\n\n'.join(detail)+'\n',encoding='utf-8')
role_values=[]
for c in dict.fromkeys(weapon_categories.values()):
    rr=[r for r in gunrows if r['weaponClass']==c]
    if rr:role_values.append([pretty(c),sum(r['pool']==WEST for r in rr),sum(r['pool']==EAST for r in rr)])
main=[
"# Gear-Inventur: West/NATO-Stil und Ostblock/Ost-Stil",
"Stand **26.10.2026** · installierte **SPT 4.1.6**-Dateidatenbank + **WTT-ContentBackport 2.0.5**. Planungsgrundlage für spätere USEC/BEAR-Kits; keine Änderung an Spiel, Profilen, Kits, Konfiguration oder laufenden Prozessen.",
"## Ergebnis",
"Die reine Waffenanzahl ist nahezu ausgeglichen: **82 westliche zu 81 östlichen Waffen-Templates**. Bereinigt um Namens-Farb-/Skinvarianten sind es **73 zu 78 Modelle**; bei der im Generator offengelegten gröberen Familienbildung **48 zu 42 Familien**. Diese Bereinigungen sind Orientierung, keine Gleichsetzung von Handhabung oder Stärke. Ein großer Teil der östlichen Varianten stammt aus der AK-Familie.",
"Bei Körperschutz stehen **51 westliche** (21 Westen + 30 gepanzerte Rigs) **56 östlichen** (27 + 29) gegenüber, dazu eine neutrale Sonderweste. Die Gesamtauswahl reicht für zwei eigenständige, abwechslungsreiche Kit-Pools. Die Klassen und Rollen sind jedoch unterschiedlich verteilt.",
"**Empfehlung:** Waffen und sichtbare Grundausrüstung nach Stil trennen, Headsets, Medizin und universelle Optiken gemeinsam anbieten. Konkrete Kits je Rolle/Tier paarweise ausbalancieren; nicht sämtliche Templates mit derselben Wahrscheinlichkeit ziehen.",
"## Geplanter Zusatzbestand: WTT-Armory 3.0.0",
"**Noch nicht installiert.** Der [separate Armory-Anhang](ARMORY.md) enthält alle 833 Einträge: 80 Waffen-Templates, 705 Waffenteile, 27 Munitionstypen, zehn Questgegenstände und elf Poster. Keine zusätzliche Rüstung oder Headsets. [Maschinenlesbare Armory-Liste](ARMORY.json).",
table(['Waffen-Templates inklusive Varianten','West/NATO-Stil','Ostblock/Ost-Stil'],[['Installiert: SPT + ContentBackport',82,81],['Armory zusätzlich, geplant',62,18],['Hypothetisch zusammen',144,99]]),
"Die weiteren Tabellen dieses Berichts und inventory.json bleiben der installierte Bestand. Armory wird nur im Anhang geführt; die hypothetische Zusammenrechnung bedeutet weder eine Installation noch eine geprüfte Laufzeitkompatibilität. Dort sind auch die Stil-Grenzfälle Valmet M78, Bofors Ak5 und CZ 75 dokumentiert.",
"## Dateien",
"- [Vollständige Gegenstandsliste mit IDs und WTT-Kennzeichnung](ITEMS.md)\n- [Maschinenlesbare Inventur mit Zuordnungsgrund, Werten und Quellen-Hashes](inventory.json)\n- [Reproduzierbarer Generator mit allen Klassifikationsregeln](generate.py)",
"## Zählweise und Reichweite",
f"- {metadata['baseItems']} Item-Templates in der installierten Basis, {metadata['wttCustomItemDefinitions']} zusätzliche WTT-CustomItems; keine überschneidenden IDs. Node-Kategorien zählen nicht als Gegenstände.\n- Davon **{len(rows)} Gear-Datensätze** erfasst, **{len(counted)}** im direkten Vergleich und **{len(rows)-len(counted)}** separat ausgewiesen. Feste Panzerungseinsätze, interne Waffen-Controller, stationäre Waffen, Signal-/Spielzeugwaffen sowie interne Splittermunition zählen nicht als zusätzliche Kit-Auswahl.\n- Alle erfassten Gegenstände bleiben in inventory.json und ITEMS.md nachvollziehbar. Kein Handbook-Eintrag führt vorsichtshalber in die separate Liste; das bedeutet nicht automatisch nicht benutzbar.\n- **Statische Inventur, kein Export eines laufenden Servers.** Installierte JSONs werden ausgewertet und WTT-Klone mit ihren Properties-Overrides rekonstruiert. Erfolgreiches Laden, Händlerfreischaltungen, APBS-/SVM-Anpassungen, Spawnchancen und vollständige Presetbaubarkeit sind damit nicht nachgewiesen.\n- Ausgenommen sind Quest-/Tauschloot, Essen/Trinken, Geld, Schlüssel und Behälter, Bekleidung aus der separaten Customization-Datenbank, Köpfe/Stimmen und andere dynamisch erzeugte Templates. Arena-Molotov steht unten gesondert.\n- Englische Gegenstandsnamen bleiben absichtlich erhalten, damit IDs und Namen mit der Installation vergleichbar sind.",
"## Vereinbarte Aufteilung",
"Nach Bestätigung des Nutzers zählt **Plattform/Stil**, nicht Herstellerland und nicht allein das Kaliber. West/NATO ist ein Gameplay-Pool einschließlich westlich geprägter Schweizer, österreichischer, japanischer und israelischer Systeme; Ostblock/Ost umfasst auch moderne russische und chinesische Systeme. Das sind keine Behauptungen über historische Bündnismitgliedschaft.",
table(['Sonderfall','Pool','Grund'],[
 ['ADAR 2-15, STM-9, NL545 DI/GP','West','AR-Familie trotz russischer Hersteller'],
 ['RD-704','Ost','AK-Familie trotz US-Hersteller'],
 ['Mk47 Mutant','West','AR-basierte Hybridplattform trotz AK-Magazinen'],
 ['AK-101/102, AK-308, VPO-101, DVL-10, T-5000M','Ost','Plattform bleibt östlich, auch bei westlichem Kaliber'],
 ['Howa Type 20, UZI, DevTac Ronin','West','Stil-/Poolentscheidung; keine NATO-Mitgliedschaft unterstellt'],
 ['QBZ-191, Type 56 Chicom','Ost','Ost-Pool umfasst chinesische Ausrüstung'],
 ['Medizin und Werkzeuge','Gemeinsam','Keine künstlichen funktionalen Lücken'],
 ['Universelle Zubehörteile','Gemeinsam, soweit potenziell beidseitig passend','Kompatibilität hat Vorrang vor Herstellerland']]),
"Die lokalen Beschreibungen bestätigen die russische AR-Abstammung der NL545 und die AK-Basis der RD-704. Als externer Gegencheck dokumentiert [CMMG selbst die AK-Magazine der Mk47](https://support.cmmg.com/what-magazine-does-each-cmmg-platform-use); das [US-Army-Datenblatt zur Howa Type 20](https://odin.t2com.army.mil/WEG/Asset/Type_89_) benennt Japan und 5,56 mm. Die Poolzuordnung bleibt unsere Designentscheidung.",
"## Waffen nach Rolle",
table(['Waffengruppe','West','Ost'],role_values),
"Die native Datenbank ordnet MP-18 unter Shotgun, MTs-255 und M32 unter Revolver ein. Für diese Tabelle sind sie nach tatsächlicher Rolle korrigiert. Signalwaffen, Master Hand, Fahrzeug-/stationäre MGs sowie die zwei Unterlaufwerfer-Controller zählen nicht mit. GP-25/M203 bleiben Zubehör.",
"- **Westliche Auswahl:** mehr SMG/PCC (16:7), DMR (8:3) und Pistolen/Revolver (17:11). .338-LM-Systeme, MCX-SPEAR und AK-50 bieten besondere Rollen.\n- **Östliche Auswahl:** mehr Schrotflinten (11:5), Karabiner/Spezialgewehre (10:0 in dieser nativen Kategorie) und MG-Templates (5:3). VAL/VSS/SR-3M, 9A-91/VSK-94, ASh-12 und KS-23 ergänzen AK-Systeme.\n- Westliche semiautomatische AR-Karabiner zählen in der Sturmgewehrzeile mit. 10:0 bedeutet deshalb nicht, dass West keine Karabiner besitzt. Feuermodi stehen pro Waffe in der Detailinventur.\n- Granat-/Raketenwerfer separat behandeln: 2 westliche Hand-Granatwerfer und ein östlicher RShG-2. Dessen Raketen-Template fehlt im Handbook und wird separat ausgewiesen; ein regulär funktionsfähiges Arena-Kit ist nicht bestätigt.",
"## WTT-Beitrag bei den Waffen",
"WTT ergänzt **14 Waffen-Templates: 9 West, 5 Ost**. Die Basis alleine liegt bei 73 West zu 76 Ost. Nicht alle Ergänzungen sind neue Familien: HK416 RAL8000, SR-25 Taupe, Saiga Redline und P90 Scourge sind benannte Varianten bestehender Systeme.",
table(['WTT-Waffe','Pool'],[[r['name'],r['pool']] for r in gunrows if r['source']=='WTT']),
"## Gear nach Kategorie",
"Templates inklusive Farb-/Skinvarianten. Ein gemeinsames Teil erscheint einmal unter Gemeinsam, nicht zweimal.",
table(['Kategorie','West','Ost','Gemeinsam','Offen'],[[pretty(c)]+counts([r for r in counted if r['category']==c]) for c in sorted({r['category'] for r in counted})]),
"**Headsets sind der größte Engpass einer strikten Stiltrennung:** 25 westliche Varianten gegenüber einem GSSh-01 auf der östlichen Seite, plus zwei gemeinsame OPSMEN-Varianten. Diese Kategorie sollte für BEAR zugänglich bleiben.",
"Bei Helmen erzeugen viele westliche Skins einen großen Vorsprung. Ohne allgemeine Namenszusätze sind es 21 westliche und 17 östliche Namensgruppen, plus Sonderausrüstung. Das ist keine geprüfte Gleichheit von Schutz, Trefferzonen, Hörbeeinträchtigung oder Aufsatzmöglichkeiten.",
"## Körperschutz: deklarierte Standard-Frontklasse",
"Ausgewertet wird der deklarierte Standard der Frontplatte, ersatzweise der Frontweichpanzerung. Höherklassige Seitenteile erhöhen den Wert nicht. Das sind Slotvorgaben, kein tatsächlich erzeugtes Kit. Material, Abdeckung, Haltbarkeit, Gewicht und Bewegungseinschränkung bleiben weitere Balancefaktoren.",
table(['Frontklasse','Westliche Westen/Rigs','Östliche Westen/Rigs','Gemeinsam'],[
 [c]+[sum(r.get('defaultFrontOrTopClass')==c and r['pool']==s for r in counted if r['category'] in ['Schutzwesten','Gepanzerte Rigs']) for s in [WEST,EAST,SHARED]]
 for c in range(1,7)]),
"Die Klassen 2–6 lassen sich grundsätzlich beidseitig bedienen. Klasse-1-Replikträger nicht als gleichwertige Alternative zu regulären Plattenträgern ziehen. Ein Kitgenerator sollte zunächst denselben Zielwert für Frontschutz und ähnliche Abdeckung wählen; Klasse allein ist kein vollständiges Rüstungsbudget.",
"## Munition und Zubehör",
"Munition wird nach Kalibern der vorgeschlagenen Waffenpools zugeordnet. 5,56×45, 7,62×51 und 9×19 sind beidseitig relevant; durch NL545/Mk47 auch 5,45×39 und 7,62×39. Schaden/Penetration stehen pro Patrone in der Inventur. Ein passendes Kaliber beweist nicht, dass jedes Waffen-/Magazin-Template jede Variante akzeptiert.",
"Zubehör und Magazine werden über gerichtete Slots verfolgt, ergänzt um deklarierte WTT-Klon-Slot-Erweiterungen. Eine Verbindung zu beiden Pools ergibt Gemeinsam. Bei fehlendem Pfad werden eindeutig benannte Plattformteile nach Stil zugeordnet, ausdrücklich ohne bestätigte Kompatibilität; Helm-/NVG-Halter bleiben gemeinsam. Diese potenzielle Endansicht ersetzt weder tatsächliche Loaderreihenfolge noch Konfliktregeln, zwingende Teile, Presets oder Tests im Weapon Builder. Der Eintrag Trophy bleibt Offen. Aus einer Stilzuordnung darf der Kitgenerator keine technische Freigabe ableiten.",
"Optiken, Laser, Griffe und Magazine sollten keine unnötige Herstellerbindung erhalten. Waffenbasis und sichtbares Tragegear reichen zur stilistischen Unterscheidung.",
"## Spätere Kit-Paare als Ausgangspunkt",
table(['Rolle','Westlicher Kandidat','Östlicher Kandidat'],[
 ['Sturmgewehr','M4A1/HK416','AK-74M/AK-12'],
 ['7,62-Allrounder','SCAR-H/SA58','AK-308'],
 ['SMG','MP5/MPX','PP-19-01/SR-2M'],
 ['DMR','SR-25/M1A','SVDS/TKPD'],
 ['MG','M60E4/E6','PKM/RPD'],
 ['Pumpgun','M870/590A1','MP-133'],
 ['Präzision','M700','SV-98/T-5000M']]),
"Diese Paare sind **noch keine geprüften Balance-Paare**. Pro Tier dieselbe Zahl aktiver Auswahlplätze anbieten, Skins innerhalb eines Platzes ziehen. Munition, Rückstoß, Magazine, Frontschutz und Mobilität gemeinsam abstimmen. Sonderrollen (.338, AK-50, ASh-12, TKPD, KS-23, Launcher) bewusst bepreisen oder separat freigeben.",
"## Arena-eigenes Gear",
"Der Arena-Molotov (Template a12e00000000000000000001) entsteht dynamisch durch ArenaMode und fehlt in Basis-/WTT-Dateien. Vorschlag: Gemeinsam/neutral. Er ist oben nicht mitgezählt. Quelle: ArenaMode.Shared/ArenaMolotovItem.cs. Ein späterer Live-Export müsste solche dynamischen Einträge ergänzen.",
"## Reproduzieren und prüfen",
"Der Generator benötigt nur die Python-Standardbibliothek. Er liest die Installation und schreibt nur den gewählten Dokumentationsordner. Aufruf aus dem Repo:\n\n    python .\\docs\\gear-inventory-20261026\\generate.py --runtime D:\\SPT41\\SPT_Runtime --output .\\docs\\gear-inventory-20261026",
"Prüfungen: eindeutige IDs, keine WTT/Basis-ID-Überlappung, vollständig klassifizierte gezählte Waffen, konsistente Ein-/Ausschlussflags, unveränderte SHA-256-Hashes aller gelesenen Quelldateien zwischen Beginn und Ende. inventory.json enthält Hashes von Basis-Templates, Handbook, englischen Locales, WTT-CustomItems und WTT-DLL. Der Generator ist absichtlich an den geprüften Quellen-Fingerprint dieses datierten Berichts gebunden. Nach einem Inhaltsupdate bricht er vor dem Schreiben ab: Regeln und Interpretation müssen für eine neue Inventur erneut geprüft werden.",
"Kein Build oder Raidtest durchgeführt. Die Inventur beschreibt den Datensatz und Stilvorschlag, nicht derzeitige Arena-Kits oder nachgewiesene Spielstärke."
]
(out/'REPORT.md').write_text('\n\n'.join(main)+'\n',encoding='utf-8')
print(json.dumps({'rows':len(rows),'counted':len(counted),'excluded':len(rows)-len(counted),'weapons':counts(gunrows),'families':{s:len({r['family'] for r in gunrows if r['pool']==s}) for s in [WEST,EAST]}},ensure_ascii=False))

