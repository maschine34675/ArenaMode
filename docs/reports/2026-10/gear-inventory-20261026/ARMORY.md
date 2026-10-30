# WTT-Armory 3.0.0 — geplanter Zusatzbestand

Stand **26.10.2026**. **Nicht installiert.** Auswertung des entpackten Downloadpakets; keine Installation, keine Kit- oder Spieländerung. [Zur installierten Inventur](REPORT.md) · [Rohdaten](ARMORY.json).

## Vergleich der Waffenpools

| Bestand | West/NATO-Stil | Ostblock/Ost-Stil |
| --- | --- | --- |
| Installiert: SPT + ContentBackport | 82 | 81 |
| Armory zusätzlich, noch nicht installiert | 62 | 18 |
| Hypothetisch zusammen | 144 | 99 |

Gezählt werden Waffen-Templates einschließlich Farb-/Sondervarianten. Die IDs überschneiden sich nicht mit der untersuchten Basis und ContentBackport. 80 Templates bedeuten nicht 80 neue Waffenfamilien. **Die bisherige installierte Inventur bleibt unverändert**; Armory wird separat geführt.

## Inhalt des Pakets

| Kategorie | Anzahl |
| --- | --- |
| Waffen | 80 |
| Waffenteile inklusive Magazine | 705 |
| Munitionstypen | 27 |
| Questgegenstände | 10 |
| Poster | 11 |
| Gesamt | 833 |

Alle 80 Waffen haben jeweils ein eingebettetes Preset; die dort referenzierten Template-IDs sind im kombinierten Datensatz vorhanden. Dies ist keine Prüfung aller Slots, Konflikte, Pflichtteile oder tatsächlicher Spawnfähigkeit. Keine zusätzlichen Schutzwesten, Rigs, Helme, Headsets oder Rucksäcke.

Die sechs neuen Munitionskaliber sind **7,92×57, .300 Winchester Magnum, .338 Norma Magnum, .408 Cheyenne Tactical, .44 Magnum und 25×59 mm**. Dazu kommen zusätzliche Ladungen für .50 BMG und 9×18 PM.

## Aufteilung nach Plattform/Stil

Die zuvor bestätigte Regel gilt weiter. **Valmet M78 → Ost**, weil AK/RPK-basiert. **Bofors Ak5 → West**, weil FN-FNC-basiert; der Name bedeutet keine AK-Abstammung. **CZ 75B/Omega → Ost** als klassische tschechoslowakische Waffenfamilie; diese zwei Einträge bleiben ein ausdrücklich benannter Stil-Grenzfall. Moderne Scorpion EVO und MSBS Grot stehen im West-Pool. Bei anderer CZ-75-Zuordnung verschieben sich lediglich zwei Einträge.

| Waffengruppe | Armory West | Armory Ost |
| --- | --- | --- |
| DMR | 1 | 3 |
| Granatwerfer | 1 | 0 |
| Maschinengewehre | 4 | 1 |
| Pistolen/Revolver | 15 | 4 |
| Repetierer/Einzellader | 9 | 1 |
| SMG/PCC | 6 | 0 |
| Schrotflinten | 3 | 0 |
| Sturmgewehre/AR-Karabiner | 23 | 9 |

Zastava M76 wird für den Rollenvergleich als DMR und Barrett XM109 als Granatwerfer gezählt, obwohl ihre nativen Kategorien AssaultCarbine bzw. MarksmanRifle lauten.

## Bedeutung für Arena-Kits

Armory erweitert vor allem West. Für Ost sind AN-94, AEK-971/973, A-545, AK-107, SA-1 und AG-042/043 eine sinnvolle Abwechslung zum bisherigen AK-Angebot. Die zusätzlichen westlichen SMGs, Pistolen, schweren Präzisionswaffen und MGs vergrößern die bestehende Rollenauswahl.

Empfehlung: gleiche Zahl aktiver Kit-Auswahlplätze pro Seite/Tier; Farbvarianten innerhalb eines Platzes ziehen. Headsets, Medizin und universelles Zubehör weiterhin gemeinsam behandeln. Munition und Sonderrollen wie XM109, M107 und CheyTac bewusst abstimmen. Aus keiner Stückzahl folgt eine nachgewiesene Leistungsbalance.

## Quellen und Grenzen

Quelle ist das lokale Paket unter C:/Users/Administrator/Downloads/WTT-Armory-3.0.0. Die [Forge-Seite](https://sp-mod.com/mod/2246/wtt-armory) führt Paket 3.0.0 für SPT 4.1.6 und CommonLib 3.0.6. Die untersuchten Client-/Server-DLLs tragen intern 2.0.5; das Paket wird deshalb nach seinem Release-Namen separat gekennzeichnet. Die bereits installierte CommonLib-DLL ist 3.0.6.0 (Prüfung am 26.10.2026).

Der Client ergänzt AN-94-Hyperburst-Logik. Der Server registriert neben Items auch Quests, Händlerangebote, Bot-Loadouts, Loot, Erfolge und einen Chatbot. Diese Inventur bestätigt weder Arena-Isolation noch Respawn-/Hyperburst-Kompatibilität. Keine Mod installiert oder dafür getestet.

ARMORY.json enthält alle 833 IDs, Herkunftsdateien, Pools, den Status not-installed sowie SHA-256 der 68 ausgewerteten Paketdateien und die Bindung an den installierten Inventurstand. Rohdaten und Seitenvorschlag sind getrennt vom bisherigen inventory.json gespeichert. generate.py reproduziert weiterhin nur den installierten Bestand; dieser Armory-Anhang ist eine separat geprüfte Momentaufnahme.

Bei Munition folgt der Pool den Kalibern der hypothetisch kombinierten Waffen. Bei Zubehör folgt er potenziellen gerichteten Slot-Verbindungen einschließlich deklarierter WTT-Klon-Erweiterungen. Gemeinsam bedeutet beidseitige potenzielle Nutzung; Offen bedeutet fehlender statischer Pfad. Loaderreihenfolge, Konfliktfilter und vollständige Waffenaufbauten wurden nicht geprüft.

## Vollständige Liste: Waffen

### West/NATO-Stil — 62

| Name | Rolle | Kaliber | Feuermodi | Template-ID |
| --- | --- | --- | --- | --- |
| Accuracy International AXMC .300 WM bolt-action sniper rifle | Repetierer/Einzellader | Caliber762x67B | single | 68a3836826dffa87b5767c04 |
| Barrett M107A1 .50 BMG anti-material rifle | DMR | Caliber127x99 | single | 693742e7eab3af9d89349410 |
| Barrett XM109 25x59 anti-material payload rifle | Granatwerfer | Caliber25x59 | single | 6945dc69fe52c2415de357f7 |
| Beretta 92FS 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 6868d249cdee524f8c0ba45f |
| Bofors Ak5C 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 6671ebcdd32bd95eb398e920 |
| Bofors Ak5C 5.56x45 assault rifle (custom) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 682d460e951a926af552d764 |
| Bofors Ak5D 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 682d3dd16900cb35564c8825 |
| Bofors Ak5G 5.56x45 light machine gun | Maschinengewehre | Caliber556x45NATO | single, fullauto | 682d4820e48f3c573ec332b5 |
| Browning Auto-5 12ga shotgun | Schrotflinten | Caliber12g | semiauto | 669fca7f9ed4916116c76d5e |
| Browning Auto-5 12ga shotgun (Custom) | Schrotflinten | Caliber12g | semiauto | 6840ebf5b8687ba34f8dfbca |
| CZ Scorpion EVO 3 A1 9x19 submachine gun | SMG/PCC | Caliber9x19PARA | single, burst, fullauto | 687afda52dc9fd6c0e14c602 |
| CheyTac M200 Intervention .408 CT bolt-action sniper rifle | Repetierer/Einzellader | Caliber1036x77 | single | 68fd4feab87d77a5aaf6bf64 |
| CheyTac M200 Intervention .408 CT bolt-action sniper rifle (Camo) | Repetierer/Einzellader | Caliber1036x77 | single | 68fd7d14ff5a09197b5ab82a |
| CheyTac M200 Intervention .408 CT bolt-action sniper rifle (FDE) | Repetierer/Einzellader | Caliber1036x77 | single | 68fd7a61f2b8d96bb3119b19 |
| Colt RO991 9x19 submachine gun | SMG/PCC | Caliber9x19PARA | single, fullauto | 690ebacfc047a9a9f1a98782 |
| FN EVOLYS 5.56x45 light machine gun | Maschinengewehre | Caliber556x45NATO | fullauto | 6a6ce6dd8ca3e7453cc71850 |
| FN EVOLYS 7.62x51 light machine gun | Maschinengewehre | Caliber762x51 | fullauto | 6a6cd61a8ca3e7453cc71847 |
| FN F2000 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 6962f22fddc6698c6309b620 |
| FN F2000 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 6969867592a994a633084f70 |
| HK 417 7.62x51 assault rifle | Sturmgewehre/AR-Karabiner | Caliber762x51 | single, fullauto | 664a5b945636ce820472f225 |
| HK Mark 23 .45 ACP pistol | Pistolen/Revolver | Caliber1143x23ACP | single | 68b7f4060a4536984f82cf4b |
| HK UCP 4.6x30 pistol | Pistolen/Revolver | Caliber46x30 | single | 68433b58a8f9a618b11082d4 |
| HK UMP 9x19 submachine gun | SMG/PCC | Caliber9x19PARA | single, fullauto | 67b05e25d83f07b7b587c0b5 |
| HK USC .45 ACP carbine | SMG/PCC | Caliber1143x23ACP | single | 6958447bfc5cad58acf95c0f |
| HK XM8 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, burst, fullauto | 6920b28eabc4f9d229cb7e49 |
| HK XM8 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, burst, fullauto | 6920a431c8f2ed5000c540a0 |
| Henry Big Boy Steel .357 lever-action rifle | Repetierer/Einzellader | Caliber9x33R | single | 6924c2f800ff4b8d9d689a5c |
| Hi-Point Model C-9 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 679f2453d1970258c1df3fce |
| IWI Carmel 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 66ba249b102a9dd6040a6e7e |
| IWI Carmel 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 66ba26a6925f9921573224c9 |
| IWI Tavor X95 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 66a47e98c486ec9d1af3a4da |
| IWI Tavor X95 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 66a544c956621d3364f6085e |
| IWI Tavor X95 5.56x45 assault rifle (OD) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 66a545898022784400d6c836 |
| IWI Tavor X95 9x19 submachine gun | SMG/PCC | Caliber9x19PARA | single, fullauto | 67f425638b8cbfdc0cd1b5f2 |
| Knight's Armament Company PDW .300 Blackout assault rifle | Sturmgewehre/AR-Karabiner | Caliber762x35 | single, fullauto | 6761b213607f9a6f79017c7e |
| LAR Grizzly 50 Mark V .50 AE pistol | Pistolen/Revolver | Caliber127x33 | single | 677c9a47baecf3c4b2453365 |
| LAR Grizzly 50 Mark V .50 AE pistol (Gold) | Pistolen/Revolver | Caliber127x33 | single | 677ca3e62e9e964a11a55d8e |
| LAR Grizzly 50 Mark V .50 AE pistol (Stainless) | Pistolen/Revolver | Caliber127x33 | single | 677ca334da2787e0538c882d |
| M249 SAW 5.56x45 light machine gun | Maschinengewehre | Caliber556x45NATO | fullauto | 66e718dc498d978477e0ba75 |
| MSBS Grot 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 696ce75f73805e693401aba0 |
| MSBS Grot 5.56x45 assault rifle FDE | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 696fe2ebcf7469bf3805173f |
| Magpul Masada .300 Blackout assault rifle (Custom) | Sturmgewehre/AR-Karabiner | Caliber762x35 | single, fullauto | 69161a1d649768162e8219ef |
| Magpul Masada 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 69066bef905ee9e06c462009 |
| Magpul Masada 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 6906a1aeef59ca68d128e8b7 |
| Marlin M1984 Dark-series .44 Magnum lever-action rifle | Repetierer/Einzellader | Caliber11x33R | single | 69417a3f787395b8cc0ab13e |
| Minebea PM-9 9x19 submachine gun | SMG/PCC | Caliber9x19PARA | single, fullauto | 6761b213607f9a6f79017cd8 |
| PSA 5.7 Rock 5.7x28 pistol | Pistolen/Revolver | Caliber57x28 | single | 68bf41bbb786f6e9315a5cab |
| Patriot M4A1 5.56x45 carbine | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single | 66839591f4d0cba7b041b2af |
| Remington MSR .300 WM bolt-action sniper rifle | Repetierer/Einzellader | Caliber762x67B | single | 684e32eaec9f5eb3cacc7ca7 |
| Ruger 5.7 5.7x28 pistol | Pistolen/Revolver | Caliber57x28 | single | 692f77045371aadbbb0ada2e |
| Serbu Super Shorty 12ga shotgun | Schrotflinten | Caliber12g | single | 6761b213607f9a6f79017d23 |
| Singuard Arms "Wages of Sin" 5.56x45 assault rifle | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 676b4e2ff185a450a0b300b4 |
| Singuard Arms "Wages of Sin" 5.56x45 assault rifle (Anodized Red) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 6761b213607f9a6f79017d40 |
| Singuard Arms "Wages of Sin" 5.56x45 assault rifle (FDE) | Sturmgewehre/AR-Karabiner | Caliber556x45NATO | single, fullauto | 676c3cac01023283e5f6a562 |
| Springfield Armory DS Prodigy 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 665fe0e865683281eb8e7ed6 |
| Springfield Armory DS Prodigy 9x19 pistol (FDE) | Pistolen/Revolver | Caliber9x19PARA | single | 675ceb83e25d80bc676e15a6 |
| Staccato XC 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 68452c3da87156b67d9ec538 |
| Surgeon Rifles 1581 XL action .338 LM bolt-action sniper rifle | Repetierer/Einzellader | Caliber86x70 | single | deee28079e76d537f532021c |
| Surgeon Rifles 1581 XL action .338 Norma Magnum bolt-action sniper rifle | Repetierer/Einzellader | Caliber86x63 | single | 1bf618e47cce6d69bec01e9f |
| Taran Tactical JW4 Pit Viper 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 684b257022b8260501141308 |
| Taran Tactical JW4 Pit Viper 9x19 pistol (Stainless) | Pistolen/Revolver | Caliber9x19PARA | single | 684b64658c4c5f37f2c4e56a |
| Wilson Combat EDC X9 9x19 pistol | Pistolen/Revolver | Caliber9x19PARA | single | 6761b213607f9a6f79017d52 |

### Ostblock/Ost-Stil — 18

| Name | Rolle | Kaliber | Feuermodi | Template-ID |
| --- | --- | --- | --- | --- |
| AN-94 5.45x39 "Kirov's Custom" assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, burst, fullauto | 679a6a534f3d279c99b135b9 |
| CZ 75B 9x19mm Pistol | Pistolen/Revolver | Caliber9x19PARA | single | 6661012d16fbd2fb75408f87 |
| CZ 75B Omega 9x19mm Pistol | Pistolen/Revolver | Caliber9x19PARA | single | 67748ec2e6a045dc97e2f978 |
| KMZ AEK-971 5.45x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, burst, fullauto | 0cb4a36dd2e587b46e813dbe |
| KMZ AEK-973 7.62x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber762x39 | single, burst, fullauto | 686093d590c3dce07984c38a |
| Kalashnikov AK-107 5.45x39 Assault Rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, burst, fullauto | 695ab544a219218449041700 |
| Kalashnikov AN-94 5.45x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, burst, fullauto | 678fe4a4906c7bd23722c71f |
| Konstantinov SA-1 7.62x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber762x39 | single, fullauto | 69777201f57d45a2ec02aafc |
| PMM-12 9x18 pistol | Pistolen/Revolver | Caliber9x18PM | single | 68d40fb07130ef271f60991f |
| SR-2 Udav 9x21 pistol | Pistolen/Revolver | Caliber9x21 | single | 68677d09339b397ed3d37522 |
| SV-98M 7.62x54R bolt-action sniper rifle | Repetierer/Einzellader | Caliber762x54R | single | 69236a0b2d1260dbca41ef92 |
| SVD 7.62x54R sniper rifle | DMR | Caliber762x54R | single | 6657bc8faeddd6b0a9b40224 |
| SVD 7.62x54R sniper rifle (Camo) | DMR | Caliber762x54R | single | 6657bd4d3a4d6e7c33fd2fdc |
| Simonov AG-042 5.45x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, fullauto | 68c23b960f579b9b5fe021e7 |
| Simonov AG-043 5.45x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, fullauto | 68cf56067ff6ceab0c2fd49e |
| Valmet M78 7.62x51 light machine gun | Maschinengewehre | Caliber762x51 | single, fullauto | 68e0002a0d6a4dab56810fbd |
| Zastava M76 7.92x57 marksman rifle | DMR | Caliber792x57 | single | 66e88596febdcf9daade16a8 |
| ZiD A-545 5.45x39 assault rifle | Sturmgewehre/AR-Karabiner | Caliber545x39 | single, burst, fullauto | 69555be939fb0a1a6f0ad12e |

## Vollständige Liste: Munition

### West/NATO-Stil — 23

| Name | Kaliber | Schaden | Penetration | Template-ID |
| --- | --- | --- | --- | --- |
| .300 Winchester Magnum AP | Caliber762x67B | 102 | 74 | 684e2de88bb7e9f52bfab35c |
| .300 Winchester Magnum FMJ | Caliber762x67B | 116 | 43 | 684e2ded3b64f7187cf14fae |
| .300 Winchester Magnum HP | Caliber762x67B | 141 | 28 | 684e2de4c90977ea0b7b83fe |
| .338 Norma Magnum Golden Target HPBT | Caliber86x63 | 134 | 44 | c556f40e464c7f09d4c757fc |
| .338 Norma Magnum Sierra MatchKing HPBT | Caliber86x63 | 123 | 56 | ea4cdb6fc3dd4f799eeef33e |
| .408 Cheyenne Tactical AP | Caliber1036x77 | 141 | 91 | 68fd53b6087e82ccf8586c51 |
| .408 Cheyenne Tactical DTM Match | Caliber1036x77 | 199 | 36 | 68fd53be7254b4b0b21f25b9 |
| .408 Cheyenne Tactical FMJ | Caliber1036x77 | 161 | 52 | 68fd537f2965f74cf4a87894 |
| .44 Magnum FMJ | Caliber11x33R | 84 | 30 | 6941ef6424ad69dcab0d9a64 |
| .44 Magnum JHP | Caliber11x33R | 98 | 21 | 6941ef6424ad69dcab0d9a6d |
| .44 Magnum Red Tracer | Caliber11x33R | 70 | 9 | 694316154eb853dd5707e76e |
| .44 Magnum Soft Point | Caliber11x33R | 118 | 9 | 694316154eb853dd5707e76a |
| .44 Magnum Xtreme Penetrator | Caliber11x33R | 109 | 45 | 694316154eb853dd5707e76c |
| .50 BMG M1 Incendiary | Caliber127x99 | 205 | 51 | 687daf1f7353335188000001 |
| .50 BMG M2 AP | Caliber127x99 | 176 | 82 | 687daf1f7353335188000002 |
| .50 BMG M20 APIT | Caliber127x99 | 179 | 68 | 687daf1f7353335188000004 |
| .50 BMG M48 | Caliber127x99 | 187 | 57 | 687daf1f7353335188000005 |
| .50 BMG M8 API | Caliber127x99 | 181 | 82 | 687daf1f7353335188000003 |
| .50 BMG MK 211 Mod 0 (Raufoss) | Caliber127x99 | 195 | 88 | 687daf1f7353335188000006 |
| 25x59mm XM1019 timed fuse | Caliber25x59 | 175 | 1 | 6938bc0b6e96bcf17932873e |
| 25x59mm XM1049 AP | Caliber25x59 | 199 | 35 | 6938bcbdd8c13e10cd328740 |
| 25x59mm XM1099 API | Caliber25x59 | 199 | 20 | 6938bca4c4ab39c99232873f |
| 25x59mm XM1103 | Caliber25x59 | 199 | 1 | 69389a69a516eeec6b6a5b72 |

### Ostblock/Ost-Stil — 4

| Name | Kaliber | Schaden | Penetration | Template-ID |
| --- | --- | --- | --- | --- |
| 7.92x57mm FMJ Surplus | Caliber792x57 | 90 | 39 | 6861847dfd5210a3e64677a3 |
| 7.92x57mm RLA FMJ | Caliber792x57 | 88 | 43 | 66d0c1319aba7b6bff460a37 |
| 7.92x57mm RLA SP | Caliber792x57 | 107 | 29 | 682e8c4bc098b59f14dde097 |
| 9x18mm PM Underwood HT | Caliber9x18PM | 63 | 22 | 68d3f708c61fce2651e9e9ac |

## Vollständige Liste: Waffenteile

### West/NATO-Stil — 543

| Name | Native Kategorie | Quelldatei | Template-ID |
| --- | --- | --- | --- |
| 2011 Staccato XC competition trigger | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 68460625a5a59f05b08d55ba |
| 2011 Staccato XC lightweight trigger | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 6846057d217f48b33569b109 |
| 2011 Staccato XC stainless trigger | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 684605bdc3d585685ded6759 |
| 2011 Staccato XC trigger | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 684602de00667a8b3a8c1881 |
| AAC Ti-RANT custom .50 AE sound suppressor | Silencer | db/CustomItems/WeaponGrizzly.json | 677ca04bc5fcd4555728b18c |
| AI AXMC .300 WM 10-round magazine | Magazine | db/CustomItems/WeaponAXMC_300WM.json | 68a387672d70af2b1e359602 |
| AI AXMC .300 WM upper receiver (.300WM) | Receiver | db/CustomItems/WeaponAXMC_300WM.json | 68a397372b4bebb5474a0bb2 |
| AI AXMC .300WM 28 inch barrel | Barrel | db/CustomItems/WeaponAXMC_300WM.json | 68a3862e3a5eb2bd63235d07 |
| AI AXMC .300WM bolt assembly | Charge | db/CustomItems/WeaponAXMC_300WM.json | 68a388b9e802bd78f8d945c6 |
| AI AXMC AT X Top Rail (Black) | Mount | db/CustomItems/WeaponAXMC_300WM.json | 68a73465322d08c3199b94ff |
| AI AXMC AX KeySlot 16 inch handguard (Black) | Handguard | db/CustomItems/WeaponAXMC_300WM.json | 68a39f96b225dcbbc8387d2c |
| AI AXMC AX buttstock (Black) | Stock | db/CustomItems/WeaponAXMC_300WM.json | 68a38a4e1586e6d768c4730b |
| AI AXMC Adapter Kit medium length rail (Black) | Mount | db/CustomItems/WeaponAXMC_300WM.json | 68a73563a634aa2a969dc629 |
| AI AXMC Adapter Kit short length rail (Black) | Mount | db/CustomItems/WeaponAXMC_300WM.json | 68a73560e0b8a9d684145f83 |
| AI AXMC GTAC AR-type pistol grip adapter (Black) | Stock | db/CustomItems/WeaponAXMC_300WM.json | 68a3985b0d24229aa6cecad1 |
| AI AXMC Multi-Caliber chassis (Black) | Receiver | db/CustomItems/WeaponAXMC_300WM.json | 68a395a145790fc8225720d3 |
| AI AXMC padded handguard grip (Black) | Foregrip | db/CustomItems/WeaponAXMC_300WM.json | 68a39ecf2120cea9dcf3f1df |
| AI AXMC pistol grip (Black) | Stock | db/CustomItems/WeaponAXMC_300WM.json | 68a389b483e34ba38a2b51ac |
| AN/PEQ-6 Integrated Laser Light Module | TacticalCombo | db/CustomItems/WeaponMK23.json | 68b9cf84c826940d2b684dad |
| AR-15 5.56x45 Colt STANAG 30-round magazine (Camo Tape) | Magazine | db/CustomItems/Attachment_Magazines.json | 682b90d74ce95cc49a6e369b |
| AR-15 5.56x45 Colt STANAG 30-round magazine (Tape) | Magazine | db/CustomItems/Attachment_Magazines.json | 682b88530f05e7746693f920 |
| AR-15 5.56x45 Colt STANAG 30-round magazine (Tape) | Magazine | db/CustomItems/Attachment_Magazines.json | 682b90a1bb914d953d95c7fd |
| AR-15 5.56x45 Global Ordnance STANAG 40-round magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 682b861888721c164979159f |
| Accuracy International AT AICS chassis for Remington 700 long action | Stock | db/CustomItems/M700XL.json | c0a00711f992a1aecdc53ae0 |
| Aimpoint CGA5P 2x telescopic sight with carryhandle | AssaultScope | db/CustomItems/Attachment_Scopes.json | 682be7bf16812803987c3715 |
| Ak5 Gen-1 polymer handguard | Handguard | db/CustomItems/WeaponAK5C.json | 682cfc17f7e9b13d49029312 |
| Ak5C 5.56x45 Automatkarbin STANAG 30-round magazine | Magazine | db/CustomItems/WeaponAK5C.json | 6828b1226ef8b54f3f63a678 |
| Ak5C 5.56x45 custom muzzle brake | FlashHider | db/CustomItems/WeaponAK5C.json | 682b9953603e1510278c750b |
| Ak5C 5.56x45 custom muzzle brake (camo) | FlashHider | db/CustomItems/WeaponAK5C.json | 682b9bdc170bdea2c25d6cb2 |
| Ak5C 5.56x45 ergonomic PMAG 20-round magazine | Magazine | db/CustomItems/WeaponAK5C.json | 682b91f64347b3787a632960 |
| Ak5C 5.56x45 flash hider | FlashHider | db/CustomItems/WeaponAK5C.json | 6671ed5805ef46250c47e46a |
| Ak5C RIS handguard | Handguard | db/CustomItems/WeaponAK5C.json | 6671ece3365d10b3362eb40f |
| Ak5C RIS handguard rail cover (side) | AuxiliaryMod | db/CustomItems/WeaponAK5C.json | 682b9cb8150b5a86eeaed3fa |
| Ak5C advanced ergonomic stock | Stock | db/CustomItems/WeaponAK5C.json | 682ba92584d56dc5db9a3685 |
| Ak5C custom polymer handguard with foregrip | Handguard | db/CustomItems/WeaponAK5C.json | 682cfe84aa392e42d1cb5882 |
| Ak5C ergonomic grooved pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 682b9369a66773ee54219101 |
| Ak5C ergonomic pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 682b93398666a4746cd08575 |
| Ak5C foldable stock | Stock | db/CustomItems/WeaponAK5C.json | 6671ecb42225b07c8afc4ce3 |
| Ak5C grooved pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 682b9415c5e1bc01f034f7ad |
| Ak5C pistol grip (Wrapped) | PistolGrip | db/CustomItems/WeaponAK5C.json | 682b94a07dd1fd93a60d0054 |
| Ak5C polymer pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 6671ec6e0bebf6ee2d5abf61 |
| Ak5C slim-line pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 682cfb5d01263131c5c1b1de |
| Ak5C tactical pistol grip | PistolGrip | db/CustomItems/WeaponAK5C.json | 682b94e1362393814780df6f |
| Ak5C tactical polymer stock | Stock | db/CustomItems/WeaponAK5C.json | 682babc3560778885a2d946a |
| Ak5C wire stock | Stock | db/CustomItems/WeaponAK5C.json | 682baa522feb8daa643b92ca |
| Ak5C wire stock cheek pad | Stock | db/CustomItems/WeaponAK5C.json | 682baac5d66c1da62f72a40a |
| Ak5D custom polymer handguard | Handguard | db/CustomItems/WeaponAK5C.json | 682d4045079d711a1462af2e |
| Ak5D custom polymer handguard with foregrip | Handguard | db/CustomItems/WeaponAK5C.json | 682d411bb39be396f8fc2dd5 |
| Ak5G Bipod | Bipod | db/CustomItems/WeaponAK5C.json | 682aaf066682ec0bfe5000e8 |
| Ak5G handguard rail | Mount | db/CustomItems/WeaponAK5C.json | 682d4e58cabfe180bea4e231 |
| Ak5G metal handguard with heatshield | Handguard | db/CustomItems/WeaponAK5C.json | 682d49d1a02a06db42af2642 |
| Ak5G tactical barrel mount | Mount | db/CustomItems/WeaponAK5C.json | 682d4c5c3d80d3692ba0208a |
| Armaco Industries 7.62x51mm 50-Round SCAR-H Casket Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 5fa85819d2d2bfc0079e6b09 |
| B5 Systems P-Grip 23 AR-15 pistol grip | PistolGrip | db/CustomItems/Attachment_PistolGrips.json | 66ba253030ea88abba95c7ed |
| B5 Systems P-Grip 23 AR-15 pistol grip (FDE) | PistolGrip | db/CustomItems/Attachment_PistolGrips.json | 66ba25755bd539b9238365b0 |
| Badger Ordnance IMUNS for AICS 2.0 chassis | Mount | db/CustomItems/M700XL.json | c572ff3cb1b04b5dd73e615b |
| Badger Ordnance IMUNS side rail | Mount | db/CustomItems/M700XL.json | 70493dc2459f53ff14b14175 |
| Barrett M107A1 20 inch fluted barrel  | Barrel | db/CustomItems/WeaponM107.json | 693b4f1d90104a2dd9abc084 |
| Barrett M107A1 29 inch fluted barrel | Barrel | db/CustomItems/WeaponM107.json | 693747879a9ebdab31a1eddb |
| Barrett M107A1 QDL muzzle brake | FlashHider | db/CustomItems/WeaponM107.json | 693746d0f61abc0b20e57648 |
| Barrett M107A1 bipod | Bipod | db/CustomItems/WeaponM107.json | 693745d99ef35610fd3bf4ad |
| Barrett M107A1 cheek rest | Stock | db/CustomItems/WeaponM107.json | 693b4dc302ec6ac27fb92e3d |
| Barrett M107A1 folding front sight post | IronSight | db/CustomItems/WeaponM107.json | 6937485ce543594692aab677 |
| Barrett M107A1 folding rear sight | IronSight | db/CustomItems/WeaponM107.json | 6937485fb8feebf8aff61b85 |
| Barrett M107A1 recoil pad | Stock | db/CustomItems/WeaponM107.json | 693b4dc16e9c45ea3d88c4aa |
| Barrett M107A1 recoil reducing stock attachment | Stock | db/CustomItems/WeaponM107.json | 693b4dc5cd3b6c5b732d1f39 |
| Barrett M82A1 muzzle brake | FlashHider | db/CustomItems/WeaponM107.json | 693b505c0fd91209360ebfa0 |
| Barrett QDL .50 bmg suppressor | Silencer | db/CustomItems/WeaponM107.json | 693745e76a10f1e0e2cf7853 |
| Barrett XM109 17 inch fluted barrel | Barrel | db/CustomItems/WeaponXM109.json | 69475894dcb467228bcd3fb0 |
| Barrett XM109 25 inch fluted barrel | Barrel | db/CustomItems/WeaponXM109.json | 69475859861a5fc94441d17b |
| Barrett XM109 25x59 5-round magazine | Magazine | db/CustomItems/WeaponXM109.json | 6945e2a4ab7c6d358c6e41fb |
| Barrett XM109 25x59 sound suppressor | Silencer | db/CustomItems/WeaponXM109.json | 69464ff61b0557c6882268ba |
| Barrett XM109 QDL muzzle brake | FlashHider | db/CustomItems/WeaponXM109.json | 694755c06cb8cad0abfba87f |
| Barrett XM109 cheek rest | Stock | db/CustomItems/WeaponXM109.json | 6945dec48fb00469cf6e589b |
| Barrett XM109 dual-port muzzle brake | FlashHider | db/CustomItems/WeaponXM109.json | 694756c1c61e561e10ddc6f0 |
| Beretta 92FS 9x19 15-round magazine | Magazine | db/CustomItems/Weapon92fs.json | 6868d3e47515a679f2e06288 |
| Beretta 92FS 9x19 barrel | Barrel | db/CustomItems/Weapon92fs.json | 6868d4f7ee6d5f8907d2e355 |
| Beretta 92FS 9x19 threaded barrel | Barrel | db/CustomItems/Weapon92fs.json | 6868d414e67828905d765b10 |
| Beretta 92FS custom weighted compensator | FlashHider | db/CustomItems/Weapon92fs.json | 68b7466b07fc88a7f1c6d520 |
| Beretta 92FS pistol slide | Receiver | db/CustomItems/Weapon92fs.json | 6868d55c5a39fb04c76e5bd0 |
| Browning Auto-5 12ga 12 inch barrel (Camo) | Barrel | db/CustomItems/WeaponAuto5.json | 6840eb5bce5018a937eb9527 |
| Browning Auto-5 12ga 27.9 inch barrel | Barrel | db/CustomItems/WeaponAuto5.json | 669fcadce0f0111308174c93 |
| Browning Auto-5 12ga 4-shell magazine cap | Magazine | db/CustomItems/WeaponAuto5.json | 669fcb6ed34f850baaa66446 |
| Browning Auto-5 12ga extended 5-shell magazine cap (Camo) | Magazine | db/CustomItems/WeaponAuto5.json | 6840eb96a62a326f79b6f8e8 |
| Browning Auto-5 12ga extended 6-shell tube | Magazine | db/CustomItems/WeaponAuto5.json | 669fcf5d38f1b8ba9bd0ff6c |
| Browning Auto-5 Heritage Ordnance 12ga 24 inch barrel | Barrel | db/CustomItems/WeaponAuto5.json | 669fcbd899f4dc2f8ed7d113 |
| Browning Auto-5 custom wooden shotgun stock (Camo) | Stock | db/CustomItems/WeaponAuto5.json | 6840eb240f1930925f180a3f |
| Browning Auto-5 makeshift wooden foregrip | Foregrip | db/CustomItems/WeaponAuto5.json | 669fcb37000ef6ab0a278da1 |
| Browning Auto-5 shortened 12ga barrel | Barrel | db/CustomItems/WeaponAuto5.json | 669fcc072ce7ed70082a6272 |
| Browning Auto-5 wooden pistol grip | Stock | db/CustomItems/WeaponAuto5.json | 669fcbe98880c4b9c174db01 |
| Browning Auto-5 wooden shotgun stock | Stock | db/CustomItems/WeaponAuto5.json | 669fcbc7d1b799623b40679a |
| CZ Scorpion EVO 3 8 inch barrel | Barrel | db/CustomItems/WeaponCZScorpion.json | 687fb6c8734e9a8e27d6b054 |
| CZ Scorpion EVO 3 A1 5 inch integral barrel | Barrel | db/CustomItems/WeaponCZScorpion.json | 688445ff01082f16b3c42d50 |
| CZ Scorpion EVO 3 A1 9x19 20-round polymer transparent magazine | Magazine | db/CustomItems/WeaponCZScorpion.json | 6886543e0db4a816ef78a062 |
| CZ Scorpion EVO 3 A1 Advanced Combat stock | Stock | db/CustomItems/WeaponCZScorpion.json | 6881bc5b5211d1361dea6586 |
| CZ Scorpion EVO 3 A1 Magpul Zhukov stock adapter | Stock | db/CustomItems/WeaponCZScorpion.json | 687c449bb184bc8da37d45d2 |
| CZ Scorpion EVO 3 A1 PDW collapsible stock | Stock | db/CustomItems/WeaponCZScorpion.json | 687c44030594b2dc3318bfca |
| CZ Scorpion EVO 3 A1 carbine compensator | FlashHider | db/CustomItems/WeaponCZScorpion.json | 687b03bc8b8590063f73f01c |
| CZ Scorpion EVO 3 A1 carbine handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 687b02e550e15d9a28c5681e |
| CZ Scorpion EVO 3 A1 carbine length 12 inch barrel | Barrel | db/CustomItems/WeaponCZScorpion.json | 687afeb41db9f3580324752a |
| CZ Scorpion EVO 3 A1 carbine length 14 inch barrel | Barrel | db/CustomItems/WeaponCZScorpion.json | 6881bc7a6b3db974a22e848d |
| CZ Scorpion EVO 3 A1 extended tactical handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 68827b0e8586d3f82920066a |
| CZ Scorpion EVO 3 A1 flash hider | FlashHider | db/CustomItems/WeaponCZScorpion.json | 687fb49322e3cb02683967bf |
| CZ Scorpion EVO 3 A1 polymer handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 687fb46ac2f7551f2ce2e4b1 |
| CZ Scorpion EVO 3 A1 polymer stock | Stock | db/CustomItems/WeaponCZScorpion.json | 687aff780a062c4f04628738 |
| CZ Scorpion EVO 3 A1 railed PDW handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 6881c18aaa553ed3873788da |
| CZ Scorpion EVO 3 A1 tactical mid-length railed handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 6881c17e9ece0d7057d4347a |
| CZ Scorpion EVO 3 A1 tactical stock | Stock | db/CustomItems/WeaponCZScorpion.json | 6882840d22fda64f12632532 |
| CZ Scorpion EVO 3 PDW 5 inch barrel | Barrel | db/CustomItems/WeaponCZScorpion.json | 6881bd52cc6ccf1b747db2ce |
| CZ Scorpion EVO 3 Plus 9x19 20-round polymer magazine | Magazine | db/CustomItems/WeaponCZScorpion.json | 6881c0dd2f0aaea90068aa12 |
| CZ Scorpion EVO 3 Plus 9x19 30-round polymer magazine | Magazine | db/CustomItems/WeaponCZScorpion.json | 6881c0fa3be04213015b7e34 |
| CZ Scorpion EVO 3 barrel shroud | FlashHider | db/CustomItems/WeaponCZScorpion.json | 6884464a25d2c3593b5ae0e4 |
| CZ Scorpion EVO 3 charging handle | Charge | db/CustomItems/WeaponCZScorpion.json | 687b00468b4fafd5ebb3a763 |
| CZ Scorpion EVO 3 folding buffer tube adapter | Stock | db/CustomItems/WeaponCZScorpion.json | 6881bc42bd69c9741111f5b7 |
| CZ Scorpion EVO 3 picatinny hand stop | Foregrip | db/CustomItems/WeaponCZScorpion.json | 687fb676e4996d45517fe236 |
| CZ Scorpion EVO 3 pistol grip | PistolGrip | db/CustomItems/WeaponCZScorpion.json | 687b001103b1d64f98f261e7 |
| CZ Scorpion EVO 9x19 50-round drum magazine | Magazine | db/CustomItems/WeaponCZScorpion.json | 6888e4dd5a5e2e5dd16409f9 |
| CZ Scorpion EVO 9x19 Magpul PMAG EV9 35-round magazine | Magazine | db/CustomItems/WeaponCZScorpion.json | 687c41fb7d3612ea41c3a822 |
| CZ Scorpion EVO 9x19 Magpul PMAG EV9 35-round magazine (Taped) | Magazine | db/CustomItems/WeaponCZScorpion.json | 687c430e91f5db8685fbbae6 |
| CheyTac M200 Intervention .408 CT 29 inch barrel | Barrel | db/CustomItems/WeaponCheytacM200.json | 68fd5f9247fb510030b67e9c |
| CheyTac M200 Intervention .408 CT 29 inch barrel (FDE) | Barrel | db/CustomItems/WeaponCheytacM200.json | 68fd5fd0196cfaff433daa9f |
| CheyTac M200 Intervention .408 CT 5-round magazine | Magazine | db/CustomItems/WeaponCheytacM200.json | 68fd5bd30b534a46e5a0c6c2 |
| CheyTac M200 Intervention .408 CT 7-round magazine | Magazine | db/CustomItems/WeaponCheytacM200.json | 68fd5afbf79eba961169cd92 |
| CheyTac M200 Intervention .408 CT 7-round magazine (FDE) | Magazine | db/CustomItems/WeaponCheytacM200.json | 68fd5c3dd4f0cdc4cad9f19c |
| CheyTac M200 Intervention .408 CT custom 20 inch barrel | Barrel | db/CustomItems/WeaponCheytacM200.json | 68fd5ff9985a9f825affa88f |
| CheyTac M200 Intervention .408 CT custom 20 inch barrel (Camo) | Barrel | db/CustomItems/WeaponCheytacM200.json | 68fd609fd746b88631211f6a |
| CheyTac M200 Intervention .408 CT muzzle brake | FlashHider | db/CustomItems/WeaponCheytacM200.json | 68fd61c1fe2ac956b9c5ef92 |
| CheyTac M200 Intervention .408 CT muzzle brake (FDE) | FlashHider | db/CustomItems/WeaponCheytacM200.json | 68fd6241ba57f1aa0638bdce |
| CheyTac M200 Intervention carry handle | Foregrip | db/CustomItems/WeaponCheytacM200.json | 68fd779336afbe413653f9a3 |
| CheyTac M200 Intervention carry handle (FDE) | Foregrip | db/CustomItems/WeaponCheytacM200.json | 68fd78cb3a0d215355758ca0 |
| CheyTac M200 Intervention collapsible stock | Stock | db/CustomItems/WeaponCheytacM200.json | 68fd5646922a78a67473d3b4 |
| CheyTac M200 Intervention collapsible stock (FDE) | Stock | db/CustomItems/WeaponCheytacM200.json | 68fd56435b8191dd387da9ff |
| CheyTac M200 Intervention custom handguard (Camo) | Handguard | db/CustomItems/WeaponCheytacM200.json | 68fd75e399c0ae29b3d0d2ae |
| CheyTac M200 Intervention skeletonized carry handle | Foregrip | db/CustomItems/WeaponCheytacM200.json | 68fd7987adf46ad6d9ee6c29 |
| CheyTac M200 Intervention tactical carry handle | Foregrip | db/CustomItems/WeaponCheytacM200.json | 68fd798ae98cfd549152a62a |
| CheyTac M200 Intervention tubular handguard | Handguard | db/CustomItems/WeaponCheytacM200.json | 68fd73dd4b2e7a40cca969b4 |
| CheyTac M200 Intervention tubular handguard (FDE) | Handguard | db/CustomItems/WeaponCheytacM200.json | 68fd74c33e4a9ddcac041e36 |
| CheyTac M200 Intervention wrapped carry handle | Foregrip | db/CustomItems/WeaponCheytacM200.json | 68fd79a09bd9b65b910edb4a |
| CheyTac M200 Invervention bipod | Bipod | db/CustomItems/WeaponCheytacM200.json | 68fd727675ed7e422a8e89c2 |
| CheyTac M200 Invervention bipod (Camo) | Bipod | db/CustomItems/WeaponCheytacM200.json | 68fd72acc8fe574cbf6cb70a |
| CheyTac M200 Invervention bipod (FDE) | Bipod | db/CustomItems/WeaponCheytacM200.json | 68fd72a7dbb26b84c33b8a91 |
| Colt RO991 9x19 10.3 inch barrel | Receiver | db/CustomItems/WeaponRO991.json | 690ec4df1bfed0b4e6db5181 |
| Colt RO991 9x19 13.8 inch barrel | Silencer | db/CustomItems/WeaponRO991.json | 699c9f633ba4c14a55002933 |
| Colt RO991 9x19 32-round magazine | Magazine | db/CustomItems/WeaponRO991.json | 690ec1e42825e346696d28ab |
| Colt RO991 9x19 8 inch barrel | Receiver | db/CustomItems/WeaponRO991.json | 699c9f633ba4c14a55002932 |
| Colt RO991 9x19 D.E.A 13.8 inch handguard | Handguard | db/CustomItems/WeaponRO991.json | 699c9f633ba4c14a55002934 |
| Colt RO991 9x19 Special LaFrance handguard | Handguard | db/CustomItems/WeaponRO991.json | 699c9f633ba4c14a55002935 |
| Colt RO991 9x19 upper receiver | Receiver | db/CustomItems/WeaponRO991.json | 690ec46edbfedf019246b350 |
| Custom 5.7x28 direct thread sound suppressor | Silencer | db/CustomItems/WeaponPSARock57.json | 68bf486151b56d9652d15b69 |
| EVOLYS bipod | Bipod | db/CustomItems/WeaponEvolys.json | 6a6cd7488ca3e7453cc7184e |
| Elite Iron Windtalker AMF .408 CT sound suppressor | Silencer | db/CustomItems/WeaponCheytacM200.json | 68fd62d343a142b14be0c3ec |
| Elite Iron Windtalker AMF .408 CT sound suppressor (FDE) | Silencer | db/CustomItems/WeaponCheytacM200.json | 68fd63e8842f704b09062fd1 |
| Elite Iron Windtalker AMF .408 CT sound suppressor (Titanium) | Silencer | db/CustomItems/WeaponCheytacM200.json | 68fd6401e11fcc19f3a8d365 |
| FN EVOLYS 5.56x45 100-round magazine | Magazine | db/CustomItems/WeaponEvolys.json | 6a6ce7248ca3e7453cc71852 |
| FN EVOLYS 7.62x51 100-round magazine | Magazine | db/CustomItems/WeaponEvolys.json | 6a6cd6298ca3e7453cc71848 |
| FN EVOLYS 7.62x51 barrel | Barrel | db/CustomItems/WeaponEvolys.json | 6a6cd6888ca3e7453cc7184a |
| FN EVOLYS 7.62x51 extended barrel | Barrel | db/CustomItems/WeaponEvolys.json | 6a6ce74f8ca3e7453cc71854 |
| FN EVOLYS 7.62x51 heavy barrel | Barrel | db/CustomItems/WeaponEvolys.json | 6a6ce76d8ca3e7453cc71856 |
| FN EVOLYS 7.62x51 short barrel | Barrel | db/CustomItems/WeaponEvolys.json | 6a6ce7aa8ca3e7453cc71858 |
| FN EVOLYS Polymer stock | Stock | db/CustomItems/WeaponEvolys.json | 6a6cd6df8ca3e7453cc7184c |
| FN EVOLYS buffer tube adapter | Stock | db/CustomItems/WeaponEvolys.json | 6a6ce7fe8ca3e7453cc7185a |
| FN F2000 5.56x45 14 inch Barrel | Barrel | db/CustomItems/WeaponF2000.json | 69680a1b5966cfdf3708c754 |
| FN F2000 5.56x45 15 inch Barrel | Barrel | db/CustomItems/WeaponF2000.json | 6967fd67fc321bda050f0281 |
| FN F2000 5.56x45 17 inch Barrel | Barrel | db/CustomItems/WeaponF2000.json | 6968098d5966cfdf3708c752 |
| FN F2000 5.56x45 Receiver | Receiver | db/CustomItems/WeaponF2000.json | 696801005966cfdf3708c742 |
| FN F2000 5.56x45 Standard Top Rail | Mount | db/CustomItems/WeaponF2000.json | 696801005966cfdf3708c740 |
| FN F2000 Monolith Handguard | Handguard | db/CustomItems/WeaponF2000.json | 6968098d5966cfdf3708c750 |
| FN F2000 Rear Flip-Up Sight | IronSight | db/CustomItems/WeaponF2000.json | 696801b85966cfdf3708c746 |
| FN F2000 Standard Handguard | Handguard | db/CustomItems/WeaponF2000.json | 696801b85966cfdf3708c744 |
| FN F2000 Tri-Rail Handguard | Handguard | db/CustomItems/WeaponF2000.json | 696808315966cfdf3708c74e |
| FN F2000S 5.56x45 Slovenian Top Rail | Mount | db/CustomItems/WeaponF2000.json | 6968029b5966cfdf3708c74a |
| FN F2000S Rear Flip-Up Sight | IronSight | db/CustomItems/WeaponF2000.json | 69680a1b5966cfdf3708c756 |
| Glock 9x19mm 100-Round Beta C-Mag | Magazine | db/CustomItems/Attachment_Magazines.json | 0672adb8be2d7d5a552ab4f2 |
| HB Industries CZ Scorpion EVO 3 A1 angled M-LOK handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 6884454536540bf509854155 |
| HB Industries CZ Scorpion EVO 3 A1 micro M-LOK handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 688444f512ee012a2d264c9f |
| HB Industries CZ Scorpion EVO 3 A1 mid-length M-LOK handguard | Handguard | db/CustomItems/WeaponCZScorpion.json | 688445a5a3ee7a07104c6047 |
| HB Industries CZ Scorpion EVO 3 DELTA extended charging handle | Charge | db/CustomItems/WeaponCZScorpion.json | 6881bbc20fda45bcc61e32f7 |
| HB Industries CZ Scorpion EVO 3 DELTA extended charging handle (Red) | Charge | db/CustomItems/WeaponCZScorpion.json | 6881bc0e9bbdcb7cd92236e0 |
| HB Industries CZ Scorpion EVO 3 ProStock charging handle | Charge | db/CustomItems/WeaponCZScorpion.json | 6881bc2d07381a49162f8194 |
| HK 417 7.62x51 upper receiver | Receiver | db/CustomItems/WeaponHK417.json | 664a5fdb05fd8090a514f00f |
| HK 417 M110A1 Carbine-length M-LOK handguard | Handguard | db/CustomItems/WeaponHK417.json | 68d031b9dadab8b4e2ac9f56 |
| HK 417 M110A1 M-LOK handguard | Handguard | db/CustomItems/WeaponHK417.json | 68d03088e031ff364882e0d1 |
| HK 417 Quad-Rail handguard | Handguard | db/CustomItems/WeaponHK417.json | 664a6025593a58418347808f |
| HK 417 buffer tube | Stock | db/CustomItems/WeaponHK417.json | 664a5ee0831d8e0a1c8db4c2 |
| HK 417 standard 7.62x51 flash hider | FlashHider | db/CustomItems/WeaponHK417.json | 664a5ebcc29c027e11d12f31 |
| HK MG36 5.56x45 100-round drum magazine | Magazine | db/CustomItems/Attachment_Magazines.json | ccab703b048be433c03a671c |
| HK MK23 .45 ACP 12-round magazine | Magazine | db/CustomItems/WeaponMK23.json | 68b7f582e203edda8374ddba |
| HK MK23 .45 ACP 17-round extended magazine | Magazine | db/CustomItems/WeaponMK23.json | 68b9c6cbf83285d088054b3b |
| HK MK23 .45 ACP 5.8 inch threaded barrel | Barrel | db/CustomItems/WeaponMK23.json | 68b7f613687ce0b4e6f3e64c |
| HK MK23 .45 ACP pistol slide | Receiver | db/CustomItems/WeaponMK23.json | 68b7f985dd6565e47eb9ee44 |
| HK MK23 Trijicon RMR mount system | Mount | db/CustomItems/WeaponMK23.json | 68b9ca430428f3ba43bfe630 |
| HK MK23 front sight | IronSight | db/CustomItems/WeaponMK23.json | 68b7f7e1154441e5f4447192 |
| HK MK23 grip tape | PistolGrip | db/CustomItems/WeaponMK23.json | 68b9c62416c896cf350f94ef |
| HK MK23 hammer | AuxiliaryMod | db/CustomItems/WeaponMK23.json | 68b7f9242936eb3c214e1dd9 |
| HK MK23 performance trigger | AuxiliaryMod | db/CustomItems/WeaponMK23.json | 68b9c66b1b3e1ec9414d1983 |
| HK MK23 picatinny rail adapter | Mount | db/CustomItems/WeaponMK23.json | 68b9cb8a59d59d6131028a1e |
| HK MK23 rear sight | IronSight | db/CustomItems/WeaponMK23.json | 68b7f7317385935fb8a06488 |
| HK MK23 rear sight bearing | IronSight | db/CustomItems/WeaponMK23.json | 68b9ce4e6ac219f4b866616d |
| HK MK23 rubberized pistol grip | PistolGrip | db/CustomItems/WeaponMK23.json | 68b9c609e3d2f5545ff25fec |
| HK MK23 skeletonized hammer | AuxiliaryMod | db/CustomItems/WeaponMK23.json | 68b9c5271bda9c05ec4843c1 |
| HK MK23 thread protector | MuzzleCombo | db/CustomItems/WeaponMK23.json | 68b7f87a17e0b4da91a230e6 |
| HK MK23 three port custom muzzle brake | FlashHider | db/CustomItems/WeaponMK23.json | 68b9c90f4c6d780e871a9e89 |
| HK MK23 trigger | AuxiliaryMod | db/CustomItems/WeaponMK23.json | 68b7f8d25cc65007af7be8de |
| HK MK23 tritium front sight | IronSight | db/CustomItems/WeaponMK23.json | 68b7f7ac290421dd052d8def |
| HK MK23 tritium rear sight | IronSight | db/CustomItems/WeaponMK23.json | 68b7f777a2a0395b35545877 |
| HK MK23 weighted muzzle brake | FlashHider | db/CustomItems/WeaponMK23.json | 68b9cc3737f7a7e5da9e41cf |
| HK UCP 4.6x30 20-round magazine | Magazine | db/CustomItems/WeaponUCP.json | 68433b836de7d8ce99c642c6 |
| HK UCP 4.6x30 5.1 inch barrel | Barrel | db/CustomItems/WeaponUCP.json | 6843749a0fba658861377651 |
| HK UCP 4.6x30mm threaded pistol barrel | Barrel | db/CustomItems/WeaponUCP.json | 6843749dcc3ddb0f04d2455c |
| HK UCP custom 4.6x30 compensator | Mount | db/CustomItems/WeaponUCP.json | 684374be10b9487f8bd998cc |
| HK UCP rear sight | IronSight | db/CustomItems/WeaponUCP.json | 684374b008d0b9c0aa535a62 |
| HK UCP rear sight rail | Mount | db/CustomItems/WeaponUCP.json | 684374b3ba4ad4561eb645ca |
| HK UCP tactical 4.6x30 sound suppressor | Silencer | db/CustomItems/WeaponUCP.json | 684388df68faf2369eeba68f |
| HK UCP tactical rubber grip glove | PistolGrip | db/CustomItems/WeaponUCP.json | 6843784a0370f6e229715c2b |
| HK UMP 9 9x19 20-round magazine | Magazine | db/CustomItems/WeaponUMP9.json | 67b103c4de263ed687dc57df |
| HK UMP 9 9x19 30-round magazine | Magazine | db/CustomItems/WeaponUMP9.json | 67b05e48729b9634d308785b |
| HK UMP 9 9x19 42-round magazine | Magazine | db/CustomItems/WeaponUMP9.json | 67b103d5de03be3c18660473 |
| HK UMP 9x19 8 inch barrel | Barrel | db/CustomItems/WeaponUMP9.json | 67b177b22bb79fe63fea01d8 |
| HK UMP 9x19 8 inch threaded barrel | Barrel | db/CustomItems/WeaponUMP9.json | 67b177a799cc45072ce66e17 |
| HK USC .45 ACP 10-round magazine | Magazine | db/CustomItems/WeaponUSC.json | 6958448831c055f568f7c1d3 |
| HK USC .45 ACP 16 inch barrel | Barrel | db/CustomItems/WeaponUSC.json | 695844d6ef501d713eb50cc2 |
| HK XM8 5.56x45 10.5 inch barrel | Barrel | db/CustomItems/WeaponXM8.json | 6920a6b08dfe980d89d94df4 |
| HK XM8 5.56x45 12.5 inch barrel | Barrel | db/CustomItems/WeaponXM8.json | 6920a673b4d99e2e53e8603a |
| HK XM8 5.56x45 14.5 inch barrel | Barrel | db/CustomItems/WeaponXM8.json | 6920a5935f7acaf6252fb6df |
| HK XM8 5.56x45 18.5 inch barrel | Barrel | db/CustomItems/WeaponXM8.json | 6920a538c17ecee467617c8c |
| HK XM8 PCAP-picatinny adapter (Foregrip) | Mount | db/CustomItems/WeaponXM8.json | 6920ac1bdf89f86bd80a7fab |
| HK XM8 PCAP-picatinny adapter (PDW Foregrip) | Mount | db/CustomItems/WeaponXM8.json | 6920ab953441812d3e21ca02 |
| HK XM8 PCAP-picatinny adapter (Side) | Mount | db/CustomItems/WeaponXM8.json | 6920ab11d0871a1e648537a2 |
| HK XM8 PDW collapsible stock | Stock | db/CustomItems/WeaponXM8.json | 6920b021f151b7aba3f99341 |
| HK XM8 PDW collapsible stock (FDE) | Stock | db/CustomItems/WeaponXM8.json | 6920b030f7c491a1f4fb344b |
| HK XM8 PDW length handguard | Handguard | db/CustomItems/WeaponXM8.json | 6920a984e029afb5f903bc42 |
| HK XM8 PDW length handguard (FDE) | Handguard | db/CustomItems/WeaponXM8.json | 6920ad9d2ac0e4eedf575708 |
| HK XM8 bipod | Bipod | db/CustomItems/WeaponXM8.json | 69221bec95c6fbc0a4a88340 |
| HK XM8 carbine length handguard | Handguard | db/CustomItems/WeaponXM8.json | 6920a76e1cfe52219e3de69d |
| HK XM8 carbine length handguard (FDE) | Handguard | db/CustomItems/WeaponXM8.json | 6920aa4f836ff80290857b1f |
| HK XM8 carbine top rail | Mount | db/CustomItems/WeaponXM8.json | 6920b0749d500a4b9cc54d97 |
| HK XM8 full-length top rail | Mount | db/CustomItems/WeaponXM8.json | 6920b0dc50776307c3a94978 |
| HK XM8 lmg handguard | Handguard | db/CustomItems/WeaponXM8.json | 6920aa1a180357c341f46152 |
| HK XM8 lmg handguard (FDE) | Handguard | db/CustomItems/WeaponXM8.json | 6920acb7ae53665dbdb3227e |
| HK XM8 polymer end cap | Stock | db/CustomItems/WeaponXM8.json | 6920aea4cc3a8ebb7572e1c0 |
| HK XM8 polymer end cap (FDE) | Stock | db/CustomItems/WeaponXM8.json | 6920aedbd4ca2e433cf09a50 |
| HK XM8 polymer folding stock | Stock | db/CustomItems/WeaponXM8.json | 6920af14ab459c7fe0a9f413 |
| HK XM8 polymer folding stock (FDE) | Stock | db/CustomItems/WeaponXM8.json | 6920af7f71e0f6cb3095c4e7 |
| HK XM8 polymer stock | Stock | db/CustomItems/WeaponXM8.json | 6920ae3f880e33bc2a7f3696 |
| HK XM8 polymer stock (FDE) | Stock | db/CustomItems/WeaponXM8.json | 6920ae7e7ef6fb4783d4c1eb |
| HK XM8 rifle length handguard | Handguard | db/CustomItems/WeaponXM8.json | 6920a93320601fc76c8de492 |
| HK XM8 rifle length handguard (FDE) | Handguard | db/CustomItems/WeaponXM8.json | 6920adc519d0f248cbd1721f |
| HK417 E1 extended charging handle | Charge | db/CustomItems/WeaponHK417.json | 664a5e58d5d39447e1d0d77d |
| HK417 E2 buttstock | Stock | db/CustomItems/WeaponHK417.json | 664a5fafbbe56744b29baf61 |
| HK417/G28 7.62x51 10-round magazine | Magazine | db/CustomItems/WeaponHK417.json | 664a5dd804b731a2ed042705 |
| HK417/G28 7.62x51 20-round magazine | Magazine | db/CustomItems/WeaponHK417.json | 664a5e1de6e9691c9c64f7d3 |
| Henry Big Boy Steel .357 10-round internal tube magazine | Magazine | db/CustomItems/WeaponHenryBBS.json | 6924c3911b0f259ee27a95f1 |
| Henry Big Boy Steel leather buttstock ammo holder | Stock | db/CustomItems/WeaponHenryBBS.json | 6924c38650310c56c1aec96e |
| Henry Big Boy Steel rail mount | Mount | db/CustomItems/WeaponHenryBBS.json | 692662f440a8e88017858d23 |
| Heritage Ordnance 7.62x51mm 60-Round FAL Retro Drum Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 6687512d9e062645579695cd |
| Heritage Ordnance HK MP5 60-Round Casket Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | ca525c0ecc13667bd1ef357c |
| Hi-Point C-9 9x19 3.5 inch barrel | Barrel | db/CustomItems/WeaponC9.json | 679f24d15ff75ca48f6462a2 |
| Hi-Point Model C-9 pistol slide | Receiver | db/CustomItems/WeaponC9.json | 679f24e723451339c041f28d |
| Hi-Point Model C-9 side grips | PistolGrip | db/CustomItems/WeaponC9.json | 679f24e0434b197ed7a10468 |
| Hi-Point Model C9 9x19 8-round magazine | Magazine | db/CustomItems/WeaponC9.json | 679f24edc0aba3ffa139b03f |
| IWI Carmel 5.56x45mm Carbine Length Barrel | Barrel | db/CustomItems/WeaponCarmel.json | 66ba264700cd37059ee31f36 |
| IWI Carmel 5.56x45mm Rifle Length Barrel | Barrel | db/CustomItems/WeaponCarmel.json | 66ba263161fd22d37d90b085 |
| IWI Carmel Bottom Rail Cover | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba262124e3cd6bbf4715a8 |
| IWI Carmel Bottom Rail Cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba2607dea198b6d7a42ef1 |
| IWI Carmel Folding Stock | Stock | db/CustomItems/WeaponCarmel.json | 66ba268d7f9d8cfb6d0fbf77 |
| IWI Carmel Folding Stock (FDE) | Stock | db/CustomItems/WeaponCarmel.json | 66ba267c60c3c6c47de99a0d |
| IWI Carmel Left Rail Cover | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba25ed9195d94a8f131b2d |
| IWI Carmel Left Rail Cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba25bec683a649a094b04e |
| IWI Carmel Right Rail Cover | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba25d71a26d54d7504d785 |
| IWI Carmel Right Rail Cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponCarmel.json | 66ba25a68e5fcb139f5085e8 |
| IWI Tavor X95 9x19 32-round magazine | Magazine | db/CustomItems/WeaponX95.json | 67f4468ed11366239967ae80 |
| IWI Tavor X95 9x19 45-round magazine | Magazine | db/CustomItems/WeaponX95.json | 67f4474195c2cf6ca66af881 |
| IWI Tavor X95 bottom rail cover (Black) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2de04c3ee93d678d753fb |
| IWI Tavor X95 bottom rail cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2dedcf0b1e2576dbe8304 |
| IWI Tavor X95 bottom rail cover (OD) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2df38a28138128f98665f |
| IWI Tavor X95 cheek pad | Stock | db/CustomItems/WeaponX95.json | 66a47eecd5592cc7768ac044 |
| IWI Tavor X95 front sight | IronSight | db/CustomItems/WeaponX95.json | 66a47ed0df2ffbca58ebe5ed |
| IWI Tavor X95 left rail cover (Black) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2dd6203d3b7ec166970ef |
| IWI Tavor X95 left rail cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2deed9766bbb4658a7338 |
| IWI Tavor X95 left rail cover (OD) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2df4294fbcf035bd31803 |
| IWI Tavor X95 rear sight | IronSight | db/CustomItems/WeaponX95.json | 66a47ed3f97061a20b99f188 |
| IWI Tavor X95 recoil pad | Stock | db/CustomItems/WeaponX95.json | 66a47ef16376a8b51f090f38 |
| IWI Tavor X95 right rail cover (Black) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2ddcd74c13fb90c2d7f1b |
| IWI Tavor X95 right rail cover (FDE) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2dee76a5dd6ae06f6b4e5 |
| IWI Tavor X95 right rail cover (OD) | AuxiliaryMod | db/CustomItems/WeaponX95.json | 67f2df3c5579fd26965c5156 |
| Innovative Arms IA-SC9 integral suppressor | Silencer | db/CustomItems/WeaponCZScorpion.json | 6884465f8615db2a21955aa1 |
| KAC PDW .300 blackout 30-round magazine | Magazine | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c80 |
| KAC PDW .300 blackout 30-round magazine | Magazine | db/CustomItems/WeaponKACPDW.json | 6876b80b37f01b91d94b927c |
| Knight's Armament Company .300 Blackout PDW barrel | Barrel | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c7f |
| Knight's Armament Company .300 Blackout muzzle brake | FlashHider | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c82 |
| Knight's Armament Company MK23 .45 ACP sound suppressor | Silencer | db/CustomItems/WeaponMK23.json | 68b9e668a786e0e3b54ba0b9 |
| Knight's Armament Company PDW .300 Blackout ambidextrous charging handle | Charge | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c85 |
| Knight's Armament Company PDW .300 Blackout charging handle | Charge | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c84 |
| Knight's Armament Company PDW .300 Blackout upper reciever | Receiver | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c83 |
| Knight's Armament Company PDW folding stock | Stock | db/CustomItems/WeaponKACPDW.json | 6761b213607f9a6f79017c81 |
| LAR Grizzly 50 Mark V .50 AE 6-round magazine | Magazine | db/CustomItems/WeaponGrizzly.json | 677ca700833bedda3926e5c4 |
| LAR Grizzly 50 Mark V .50 AE 6-round magazine (Stainless) | Magazine | db/CustomItems/WeaponGrizzly.json | 677c9e00a9432b8c3a019a15 |
| LAR Grizzly 50 Mark V .50 AE custom compensator | FlashHider | db/CustomItems/WeaponGrizzly.json | 677ca2211cbf2a608adfc741 |
| LAR Grizzly 50 Mark V 5.5 inch barrel (Black) | Barrel | db/CustomItems/WeaponGrizzly.json | 677c9c0cb4a2eed292d7cada |
| LAR Grizzly 50 Mark V 5.5 inch barrel (Stainless) | Barrel | db/CustomItems/WeaponGrizzly.json | 677c9c38e92069ca8c68b6e1 |
| LAR Grizzly 50 Mark V 5.9 inch Threaded Barrel | Barrel | db/CustomItems/WeaponGrizzly.json | 677c9c3105cb23362086eee2 |
| LAR Grizzly 50 Mark V 6.4 inch extended barrel | Barrel | db/CustomItems/WeaponGrizzly.json | 677c9c1b5841775a87375607 |
| LAR Grizzly 50 Mark V commemorative pistol grips (Gold) | PistolGrip | db/CustomItems/WeaponGrizzly.json | 677ca165c546ba3e77379706 |
| LAR Grizzly 50 Mark V hammer | AuxiliaryMod | db/CustomItems/WeaponGrizzly.json | 677c9e9d8949d89b64a3baa5 |
| LAR Grizzly 50 Mark V pistol grips (Black) | PistolGrip | db/CustomItems/WeaponGrizzly.json | 677ca12f7599509c79311ade |
| LAR Grizzly 50 Mark V pistol grips (Oak) | PistolGrip | db/CustomItems/WeaponGrizzly.json | 67868f05ddcab8166d4e7a31 |
| LAR Grizzly 50 Mark V pistol grips (Rosewood) | PistolGrip | db/CustomItems/WeaponGrizzly.json | 67868f356f3797e70074d898 |
| LAR Grizzly 50 Mark V pistol slide | Receiver | db/CustomItems/WeaponGrizzly.json | 677c9c6fe5cf70ff4c9e1338 |
| LAR Grizzly 50 Mark V pistol slide (Gold) | Receiver | db/CustomItems/WeaponGrizzly.json | 677c9ff40e9b640153417d1a |
| LAR Grizzly 50 Mark V pistol slide (Stainless) | Receiver | db/CustomItems/WeaponGrizzly.json | 677c9fd38e377bba81c0760f |
| LAR Grizzly 50 Mark V slide stop | AuxiliaryMod | db/CustomItems/WeaponGrizzly.json | 677c9e6dc4018154af5b0599 |
| LAR Grizzly 50 Mark V tactical rail system | Mount | db/CustomItems/WeaponGrizzly.json | 677ca5064c772ce35dfdbc80 |
| LAR Grizzly 50 Mark V trigger | AuxiliaryMod | db/CustomItems/WeaponGrizzly.json | 677c9eccdfcac18ddc7c6b5b |
| LAR Grizzly Tritium rear sight (Green) | IronSight | db/CustomItems/WeaponGrizzly.json | 677ca78263bc534f0b830a42 |
| LAR Grizzly Tritium rear sight (Red) | IronSight | db/CustomItems/WeaponGrizzly.json | 677c9e4595c08963e941a146 |
| LAR Grizzly front sight | IronSight | db/CustomItems/WeaponGrizzly.json | 677c9d4ee9edb9191668af01 |
| LAR Grizzly front sight (Green) | IronSight | db/CustomItems/WeaponGrizzly.json | 677c9d30ca0b19fe954b4ed2 |
| LAR Grizzly front sight (Red) | IronSight | db/CustomItems/WeaponGrizzly.json | 677c9d5ecdee47b41baa92ac |
| LAR Grizzly rear sight | IronSight | db/CustomItems/WeaponGrizzly.json | 677c9e554dde113597355fad |
| M1894 Point Precision aluminum stock | Stock | db/CustomItems/WeaponM1894.json | 694162205fffc8182d00526c |
| M249 5.56x45 200-round box magazine | Magazine | db/CustomItems/WeaponM249.json | 66e71a7ffe3fc79ff68b582b |
| M249 5.56x45 soft pack 100-round magazine | Magazine | db/CustomItems/WeaponM249.json | 66e71a9d187b580f24ea582f |
| M249 5.56x45 standard flash hider | FlashHider | db/CustomItems/WeaponM249.json | 66e71acd9318284a609c3792 |
| M249 Bipod | Bipod | db/CustomItems/WeaponM249.json | 684741ff7650c89e76c13e2c |
| M249 Custom Buffer Tube | Stock | db/CustomItems/WeaponM249.json | 66f4418f3d6c8ae36da20f1e |
| M249 Front Sight Post | IronSight | db/CustomItems/WeaponM249.json | 66e71ab0796060d309b7b244 |
| M249 Moloc Armament "Sleek" handguard (OD) | Handguard | db/CustomItems/WeaponM249.json | 66e71b6cb468c3edc99fc21e |
| M249 Moloc Armament Heavy handguard | Handguard | db/CustomItems/WeaponM249.json | 66e71d9c49892c1cf24d5b1a |
| M249 Moloc Armament Mini Handguard | Handguard | db/CustomItems/WeaponM249.json | 66e71dea56f10f22730b6a2d |
| M249 Moloc Armament S-RAS handguard | Handguard | db/CustomItems/WeaponM249.json | 66e71dc8fa3ebda7f1034a2b |
| M249 PARA Stock | Stock | db/CustomItems/WeaponM249.json | 66e71d720d3fb3600096d1a9 |
| M249 Paratrooper 5.56x45 barrel assembly | Barrel | db/CustomItems/WeaponM249.json | 66e71b8f65fdd1ff1cdf2777 |
| M249 polymer handguard | Handguard | db/CustomItems/WeaponM249.json | 66e71d858f16f30037d06807 |
| M249 polymer stock | Stock | db/CustomItems/WeaponM249.json | 66e71afecdde3cfc1146a53c |
| M249 standard 5.56x45 barrel sssembly | Barrel | db/CustomItems/WeaponM249.json | 66e71adf394bded29410a37e |
| M82A1 FosTecH .50 BMG 20-round drum magazine | Magazine | db/CustomItems/WeaponM107.json | 693745328f01011d008cd325 |
| MDT AICS style 3.715 inch 10-round magazine | Magazine | db/CustomItems/M700XL.json | 05d4400c84ab3eb0e7722625 |
| MDT AICS style 3.715 inch 5-round magazine | Magazine | db/CustomItems/M700XL.json | f1b1a0db4986e718711a8e80 |
| MDT ESS 12 inch forend | Handguard | db/CustomItems/M700XL.json | 4efd57bf8ef993ea989884cd |
| MDT ESS 15 inch forend | Handguard | db/CustomItems/M700XL.json | 8bf881390b6747006aae77a3 |
| MDT ESS 18 inch forend | Handguard | db/CustomItems/M700XL.json | ff31402cb7b0b1d43491337c |
| MDT ESS chassis for Remington 700 long action | Stock | db/CustomItems/M700XL.json | 275dd0d822b1fcaeb831ff87 |
| MDT SRS-X stock | Stock | db/CustomItems/M700XL.json | 48c722b422ad38933164b39c |
| MDT XTN to buffer tube adapter | Stock | db/CustomItems/M700XL.json | ec7a054379216e7ccf2d35f7 |
| MSBS Grot 5.56x45 10.5 inch receiver | Barrel | db/CustomItems/WeaponMSBS.json | 696e452a1a038d893a04a429 |
| MSBS Grot 5.56x45 16 inch receiver | Barrel | db/CustomItems/WeaponMSBS.json | 696fc24464572d85f30aac0e |
| MSBS Grot 5.56x45 30-round magazine (Blue Polycarbonate) | Magazine | db/CustomItems/WeaponMSBS.json | 696feac2cf7469bf38051e34 |
| MSBS Grot 5.56x45 30-round magazine (FDE) | Magazine | db/CustomItems/WeaponMSBS.json | 696fea0fcf7469bf38051e32 |
| MSBS Grot 5.56x45 receiver | Receiver | db/CustomItems/WeaponMSBS.json | 696e43e31a038d893a04a424 |
| MSBS Grot 5.56x45 receiver (FDE) | Receiver | db/CustomItems/WeaponMSBS.json | 696fe281cf7469bf3805173d |
| MSBS Grot 5.56x45mm 30-round Magazine | Magazine | db/CustomItems/WeaponMSBS.json | 696fea0fcf7469bf38051e30 |
| MSBS Grot MLOK Handguard | Handguard | db/CustomItems/WeaponMSBS.json | 696e417f1a038d893a04a422 |
| MSBS Grot MLOK Handguard FDE | Handguard | db/CustomItems/WeaponMSBS.json | 696fe221cf7469bf38051736 |
| MSBS Grot buffer tube adapter | Stock | db/CustomItems/WeaponMSBS.json | 696fc24464572d85f30aac0b |
| MSBS Grot buffer tube adapter (FDE) | Stock | db/CustomItems/WeaponMSBS.json | 696fe281cf7469bf3805173a |
| MSBS Grot pistol grip | PistolGrip | db/CustomItems/WeaponMSBS.json | 696e46a11a038d893a04a42d |
| MSBS Grot pistol grip (FDE) | PistolGrip | db/CustomItems/WeaponMSBS.json | 696fe221cf7469bf38051738 |
| MSBS Grot polymer folding stock | Stock | db/CustomItems/WeaponMSBS.json | 696e417f1a038d893a04a420 |
| MSBS Grot polymer folding stock FDE | Stock | db/CustomItems/WeaponMSBS.json | 696fc803cf7469bf38051733 |
| Magpul M-LOK hand stop | Foregrip | db/CustomItems/WeaponCZScorpion.json | 68827aaba06fa1cf759ec88d |
| Magpul MOE-EVO CZ Scorpion EVO 3 pistol grip | PistolGrip | db/CustomItems/WeaponCZScorpion.json | 687c437b2c398db525b8d0e8 |
| Magpul Masada .300 BLK 10.5 inch barrel assembly | Barrel | db/CustomItems/WeaponMasada.json | 6916120d4852f062fe869d9d |
| Magpul Masada 5.56x45 10.5 inch barrel assembly | Barrel | db/CustomItems/WeaponMasada.json | 6910e2e858598dec26c13d13 |
| Magpul Masada 5.56x45 14.5 inch barrel assembly | Barrel | db/CustomItems/WeaponMasada.json | 69066cdb8f5104ba9d130c5f |
| Magpul Masada 5.56x45 18.5 inch barrel assembly | Barrel | db/CustomItems/WeaponMasada.json | 6910e33e89a05bdedbc30d88 |
| Magpul Masada 5.56x45 flash hider | FlashHider | db/CustomItems/WeaponMasada.json | 6910e43cb5ba11c5d57ff373 |
| Magpul Masada DMR fixed stock | Stock | db/CustomItems/WeaponMasada.json | 69135bc4c3af7137ba00bd98 |
| Magpul Masada DMR fixed stock (FDE) | Stock | db/CustomItems/WeaponMasada.json | 69135be3f3bd044c799378e1 |
| Magpul Masada M-LOK DMR handguard | Handguard | db/CustomItems/WeaponMasada.json | 6913fcc0bbdaffdf2398de93 |
| Magpul Masada M-LOK DMR handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 6913fd2540e7d9282674b48a |
| Magpul Masada M-LOK mid-length handguard | Handguard | db/CustomItems/WeaponMasada.json | 691410a4991a22c2782fe0a6 |
| Magpul Masada M-LOK mid-length handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 69141131ab15cbd47ee148b7 |
| Magpul Masada M-LOK minimalist handguard | Handguard | db/CustomItems/WeaponMasada.json | 69140391a2ffb7e8e0f320b1 |
| Magpul Masada M-LOK minimalist handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 691403d630e88f815a0eb65f |
| Magpul Masada M-LOK polymer handguard | Handguard | db/CustomItems/WeaponMasada.json | 69066d9a1072a5b68f32ca8e |
| Magpul Masada M-LOK polymer handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 691225bcb5266532f914655a |
| Magpul Masada PDW collapsing stock | Stock | db/CustomItems/WeaponMasada.json | 69066d4ff84c09f2aee26602 |
| Magpul Masada PDW collapsing stock (Camo) | Stock | db/CustomItems/WeaponMasada.json | 691359e9856292352c4c96e5 |
| Magpul Masada PDW collapsing stock (FDE) | Stock | db/CustomItems/WeaponMasada.json | 6906a17a1a915892e5b22f76 |
| Magpul Masada RAHG handguard | Handguard | db/CustomItems/WeaponMasada.json | 6916183c4556f899f8658e08 |
| Magpul Masada RAHG handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 691618d83af6725048cc02f6 |
| Magpul Masada angled M-LOK handguard | Handguard | db/CustomItems/WeaponMasada.json | 69161926101e09f9a44a334a |
| Magpul Masada angled M-LOK handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 6916197a55ad8590aaef2d29 |
| Magpul Masada buffer tube adapter | Stock | db/CustomItems/WeaponMasada.json | 69135c0bfc195df74e8b4592 |
| Magpul Masada custom M-LOK micro handguard (Camo) | Handguard | db/CustomItems/WeaponMasada.json | 691610a32a16d7557fc9bf3d |
| Magpul Masada custom M-LOK mid-length handguard (Camo) | Handguard | db/CustomItems/WeaponMasada.json | 69161150f5bffc3b2da93fde |
| Magpul Masada extended KeyMod handguard | Handguard | db/CustomItems/WeaponMasada.json | 69140a1b2d23f5cfe36bf7b2 |
| Magpul Masada extended KeyMod handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 69140f886648853180548434 |
| Magpul Masada extended hybrid handguard | Handguard | db/CustomItems/WeaponMasada.json | 6913ff570c18ac678e2c7e7f |
| Magpul Masada extended hybrid handguard | Handguard | db/CustomItems/WeaponMSBS.json | 696fc24464572d85f30aac0d |
| Magpul Masada extended hybrid handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 6913ffae5f8c4cd1fc523a8d |
| Magpul Masada extended hybrid handguard FDE | Handguard | db/CustomItems/WeaponMSBS.json | 696fe281cf7469bf3805173b |
| Magpul Masada extended quad rail handguard | Handguard | db/CustomItems/WeaponMasada.json | 6916138ba4d10131d13c63b0 |
| Magpul Masada extended quad rail handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 69161451e951cccc2355deef |
| Magpul Masada flip-up front sight | IronSight | db/CustomItems/WeaponMasada.json | 6912276c0545f6fef92580ca |
| Magpul Masada flip-up rear sight | IronSight | db/CustomItems/WeaponMasada.json | 691226c24afdbfd9b59f5092 |
| Magpul Masada flip-up rear sight (FDE) | IronSight | db/CustomItems/WeaponMasada.json | 691226dc484b05df2aa39d50 |
| Magpul Masada micro tri-rail handguard | Handguard | db/CustomItems/WeaponMasada.json | 6913fd67321c124d34d73afc |
| Magpul Masada micro tri-rail handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 6913fe5e597fc50652c10a9e |
| Magpul Masada mid-length KeyMod handguard | Handguard | db/CustomItems/WeaponMasada.json | 6916175c3882b02e89b5cfcd |
| Magpul Masada mid-length KeyMod handguard (FDE) | Handguard | db/CustomItems/WeaponMasada.json | 691617ec4a1edaefd126bbdd |
| Magpul Masada polymer fixed stock | Stock | db/CustomItems/WeaponMasada.json | 69135af0aeea321f11c7c2d1 |
| Magpul Masada polymer fixed stock (FDE) | Stock | db/CustomItems/WeaponMasada.json | 69135b6c141a752c95a76088 |
| Magpul Masada polymer folding stock | Stock | db/CustomItems/WeaponMasada.json | 69066d06894f333007e620b5 |
| Magpul Masada polymer folding stock (FDE) | Stock | db/CustomItems/WeaponMasada.json | 6906a162d418daade71fd113 |
| Magpul Masada upper receiver | Receiver | db/CustomItems/WeaponMasada.json | 69101c13a363be1fac6e260e |
| Magpul Masada upper receiver (Camo) | Receiver | db/CustomItems/WeaponMasada.json | 69101cf471c52daba356641c |
| Magpul Masada upper receiver (FDE) | Receiver | db/CustomItems/WeaponMasada.json | 69101c65d4c0a6aa81d18453 |
| Manticore Arms CZ Scorpion EVO 3 A1 Slider stock | Stock | db/CustomItems/WeaponCZScorpion.json | 687b12fbb0bffe0925ad34b2 |
| Marlin 1894 .44 Magnum 16 inch Barrel | Barrel | db/CustomItems/WeaponM1894.json | 694162205fffc8182d00526a |
| Marlin 1894 .44 Magnum 8 round Magazine | Magazine | db/CustomItems/WeaponM1894.json | 694162205fffc8182d005268 |
| Marlin 1894 Hammer | AuxiliaryMod | db/CustomItems/WeaponM1894.json | 694162205fffc8182d005264 |
| Marlin 1894 Handguard | Handguard | db/CustomItems/WeaponM1894.json | 69417a3f787395b8cc0ab136 |
| Marlin 1894 Rail Mount | Mount | db/CustomItems/WeaponM1894.json | 694162205fffc8182d005266 |
| Marlin 1894 front sight | AuxiliaryMod | db/CustomItems/WeaponM1894.json | 69417a3f787395b8cc0ab13a |
| Marlin 1894 rear sight | AuxiliaryMod | db/CustomItems/WeaponM1894.json | 69417a3f787395b8cc0ab138 |
| McMillan A5 stock for Remington 700 long action (BLK) | Stock | db/CustomItems/M700XL.json | 356e3ea40ac16f18c938ac53 |
| McMillan A5 stock for Remington 700 long action (FDE) | Stock | db/CustomItems/M700XL.json | 2fc4f74291323723f4e2b05d |
| McMillan A5 stock for Remington 700 long action (ODG) | Stock | db/CustomItems/M700XL.json | ef91b747041c9337fd9a711b |
| Minebea PM-9 25mm accessory ring mount | Mount | db/CustomItems/WeaponPM9.json | 6830dd0abbaa5e243b9fc47d |
| Minebea PM-9 9x19 custom flash hider | FlashHider | db/CustomItems/WeaponPM9.json | 6830d1675529ed4d7d5a91b9 |
| Minebea PM-9 9x19 integral suppressor system | Silencer | db/CustomItems/WeaponPM9.json | 6830f15212b2c07a8f31a42a |
| Minebea PM-9 9x19mm 25-round magazine | Magazine | db/CustomItems/WeaponPM9.json | 6761b213607f9a6f79017cdb |
| Minebea PM-9 9x19mm 32-round magazine | Magazine | db/CustomItems/WeaponPM9.json | 6761b213607f9a6f79017cdc |
| Minebea PM-9 9x19mm 40-round extended magazine | Magazine | db/CustomItems/WeaponPM9.json | 6830ce1ecee1ea420a42506a |
| Minebea PM-9 9x19mm barrel with flash hider | Barrel | db/CustomItems/WeaponPM9.json | 6761b213607f9a6f79017cd9 |
| Minebea PM-9 9x19mm shortened barrel | Barrel | db/CustomItems/WeaponPM9.json | 6761b213607f9a6f79017cda |
| Minebea PM-9 custom wire stock | Stock | db/CustomItems/WeaponPM9.json | 6830ce7c5c89e078a29ac193 |
| OmniTech Systems 5.7x28mm 55-Round P90 Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | d4fa8040a5b69d46bf8819e3 |
| OmniTech Systems CheyTac M200 Intervention tactical handguard | Handguard | db/CustomItems/WeaponCheytacM200.json | 68fd7531f17e10355836323c |
| OmniTech Systems CheyTac M200 MKI skeletonized stock | Stock | db/CustomItems/WeaponCheytacM200.json | 68fd56409c78353ecc08f32c |
| OmniTech Systems CheyTac M200 MKII skeletonized stock | Stock | db/CustomItems/WeaponCheytacM200.json | 68fd563cbd798f9a26afcdca |
| OmniTech Systems IWI Carmel Ergo Stock | Stock | db/CustomItems/WeaponCarmel.json | 66ba266aac4c9f4525d562fa |
| OmniTech Systems Sabre-5 .300 WM sound suppressor | Silencer | db/CustomItems/WeaponMSR.json | 684e49af525c602815316f8d |
| OmniTech UMP .45 ACP 50-Round Casket Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 064a8c271a037b0c6a1b86f5 |
| OmniTech V-50 Kriss Vector .45 ACP 50-Round Drum Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 9a5e3fa7c8205cf253799070 |
| PGW Defense C14 .338 LM muzzle brake | FlashHider | db/CustomItems/M700XL.json | 8c0343f717834fd6d4825a58 |
| PGW Defense C14 Timberwolf .338 LM sound suppressor | Silencer | db/CustomItems/M700XL.json | c8548cdf0e490383177f37c1 |
| PGW Defense Timberwolf .338 LM 26 inch barrel | Barrel | db/CustomItems/M700XL.json | 277727478d7eb1da0e5b3354 |
| PGW Defense Timberwolf .338 Norma Mag 26 inch barrel | Barrel | db/CustomItems/M700XL.json | 203c45db79fbca33622069f7 |
| PSA 5.7 Rock 5.7x28 "Base Pad +7" 30-round magazine | Magazine | db/CustomItems/WeaponPSARock57.json | 68bf43cc203f27f48fee9bc7 |
| PSA 5.7 Rock 5.7x28 23-round magazine | Magazine | db/CustomItems/WeaponPSARock57.json | 68bf4357e9feedf30b88015b |
| PSA 5.7 Rock 5.7x28 5.2 inch threaded barrel | Barrel | db/CustomItems/WeaponPSARock57.json | 68bf4443bddcae2d902b41cc |
| PSA 5.7 Rock RK1 pistol slide | Receiver | db/CustomItems/WeaponPSARock57.json | 68bf473184358e175b34bbd5 |
| PSA 5.7 Rock RK1 pistol slide (FDE) | Receiver | db/CustomItems/WeaponPSARock57.json | 68bf480bd16b24dea69469c7 |
| PSA 5.7 Rock RK1 pistol slide (Olive Drab) | Receiver | db/CustomItems/WeaponPSARock57.json | 68bf4827920c30deef7b96b3 |
| PSA 5.7 Rock RMR mount | Mount | db/CustomItems/WeaponPSARock57.json | 68bf4653cb23058364f3fd40 |
| PSA 5.7 Rock front sight | IronSight | db/CustomItems/WeaponPSARock57.json | 68bf453529c58e5188c0bcbe |
| PSA 5.7 Rock rear sight | IronSight | db/CustomItems/WeaponPSARock57.json | 68bf450e899a7ddb21e01d0f |
| PSA 5.7 Rock sight mount cap | Mount | db/CustomItems/WeaponPSARock57.json | 68bf471013cd0729f735bdb3 |
| PSA 5.7 Rock thread protector | MuzzleCombo | db/CustomItems/WeaponPSARock57.json | 68bf458d6ef1526e6e02bd9e |
| Patriot rear sight carry handle | IronSight | db/CustomItems/WeaponPatriot.json | 6683957f2e516f6146af483d |
| Remington MRS single-port muzzle brake | FlashHider | db/CustomItems/WeaponMSR.json | 684e48393dabb9b1547f82f5 |
| Remington MSR .300 WM 10-round magazine | Magazine | db/CustomItems/WeaponMSR.json | 684e3e6ef0a716d13d331aae |
| Remington MSR .300 WM 20 inch barrel | Barrel | db/CustomItems/WeaponMSR.json | 684e467450949bbffc1088ea |
| Remington MSR .300 WM 24 inch barrel | Barrel | db/CustomItems/WeaponMSR.json | 684e466f7b0ffe0ac7770bd7 |
| Remington MSR .300 WM 5-round magazine | Magazine | db/CustomItems/WeaponMSR.json | 684e3e64b3bb65a5ffdfd7ea |
| Remington MSR dual-port muzzle brake | FlashHider | db/CustomItems/WeaponMSR.json | 684e48ed749c4553645ba56c |
| Remington MSR heavy marksman rifle stock | Stock | db/CustomItems/WeaponMSR.json | 684e4333ac51c5cdc8a96b7f |
| Remington MSR rifle stock | Stock | db/CustomItems/WeaponMSR.json | 684e427881b3bec74bf97c28 |
| Remington MSR rubberized pistol grip | PistolGrip | db/CustomItems/WeaponMSR.json | 684e4c6792bdb7cd32ad3fb1 |
| Remington MSR skeletonized ergonomic rifle stock | Stock | db/CustomItems/WeaponMSR.json | 684e432fb35726bd5da41faa |
| Remington MSR tactical grooved pistol grip | PistolGrip | db/CustomItems/WeaponMSR.json | 684e4c702c1e2bd41597e967 |
| Ruger 5.7 5.7x28 125mm threaded barrel | Barrel | db/CustomItems/WeaponRuger57.json | 692f77045371aadbbb0ada32 |
| Ruger 5.7 Front Sight | IronSight | db/CustomItems/WeaponRuger57.json | 692f704e5371aadbbb0ada22 |
| Ruger 5.7 Rear Sight | IronSight | db/CustomItems/WeaponRuger57.json | 692f704e5371aadbbb0ada20 |
| Ruger 5.7 Slide | Receiver | db/CustomItems/WeaponRuger57.json | 692f77045371aadbbb0ada30 |
| Ruger 57 Magazine | Magazine | db/CustomItems/WeaponRuger57.json | 692f72085371aadbbb0ada24 |
| Serbu Super Shorty 3-shell 12ga magazine tube | Magazine | db/CustomItems/WeaponSerbuSuperShorty.json | 683222ec8d737be9715552a9 |
| Singuard Arms "Wages of Sin" 5.56x45 upper receiver (Anodized Red) | Receiver | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d43 |
| Singuard Arms "Wages of Sin" AR-15 charging handle (Black) | Charge | db/CustomItems/WeaponWagesOfSin.json | 676b5367fe34b0539b0f96f5 |
| Singuard Arms "Wages of Sin" AR-15 charging handle (FDE) | Charge | db/CustomItems/WeaponWagesOfSin.json | 676c3d73e56673a0ed44ec14 |
| Singuard Arms "Wages of Sin" Handguard (Anodized Red) | Handguard | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d42 |
| Singuard Arms "Wages of Sin" Handguard (FDE) | Handguard | db/CustomItems/WeaponWagesOfSin.json | 676c3d1dcf722dad840e520b |
| Singuard Arms "Wages of Sin" charging handle (Anodized Red) | Charge | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d48 |
| Singuard Arms "Wages of Sin" handguard | Handguard | db/CustomItems/WeaponWagesOfSin.json | 676b5088bc973cdc82247b3f |
| Singuard Arms "Wages of Sin" upper receiver | Receiver | db/CustomItems/WeaponWagesOfSin.json | 676b5124f0892bbed71bc377 |
| Springfield Armory DS Prodigy 17-Round 9x19mm Magazine | Magazine | db/CustomItems/WeaponProdigy.json | 665fe5ed631d6b7dfcd7a4a9 |
| Springfield Armory DS Prodigy Front Sight | IronSight | db/CustomItems/WeaponProdigy.json | 665fe70a14a35e1ed07facaf |
| Springfield Armory DS Prodigy Front Sight | IronSight | db/CustomItems/WeaponProdigy.json | 675ce99bcb144fc338c092e3 |
| Springfield Armory DS Prodigy Pistol Slide | Receiver | db/CustomItems/WeaponProdigy.json | 665fe62bfdf6dce07f16e726 |
| Springfield Armory DS Prodigy Pistol Slide | Receiver | db/CustomItems/WeaponProdigy.json | 675ce9f95e28aaf887778a4b |
| Springfield Armory DS Prodigy Rear Pistol Sight | IronSight | db/CustomItems/WeaponProdigy.json | 665fe63876838d24231faa26 |
| Springfield Armory DS Prodigy Skeletonized Hammer | AuxiliaryMod | db/CustomItems/WeaponProdigy.json | 665fe6a117ff1ba8218d680c |
| Springfield Armory DS Prodigy Slide Stop | AuxiliaryMod | db/CustomItems/WeaponProdigy.json | 665fe6b9c3f47f67713858df |
| Springfield Armory DS Prodigy Trigger | AuxiliaryMod | db/CustomItems/WeaponProdigy.json | 665fe64b7b6c44d885b755a8 |
| Springfield DS Prodigy 4.25" Barrel | Barrel | db/CustomItems/WeaponProdigy.json | 665fe61369cb629f514cec21 |
| Springfield DS Prodigy 4.25" Barrel | Barrel | db/CustomItems/WeaponProdigy.json | 675ce8e8f1bd745f74ef5cd8 |
| Springfield DS Prodigy 4.50" Threaded Barrel | Barrel | db/CustomItems/WeaponProdigy.json | 675cea42c3b11186fbf69b52 |
| Staccato XC 9x19 17-round magazine | Magazine | db/CustomItems/WeaponStaccatoXC.json | 68452ea1e41a7fbeb0b2ff4e |
| Staccato XC 9x19 20-round magazine | Magazine | db/CustomItems/WeaponStaccatoXC.json | 68452ec3db6152a97e5f902a |
| Staccato XC 9x19 barrel | Barrel | db/CustomItems/WeaponStaccatoXC.json | 68452da6051e580c48ede835 |
| Staccato XC 9x19 extended barrel | Barrel | db/CustomItems/WeaponStaccatoXC.json | 684606c63dc00b27e95bb5fa |
| Staccato XC 9x19 extended pistol slide | Receiver | db/CustomItems/WeaponStaccatoXC.json | 68460744bf2da4e1fb5b4195 |
| Staccato XC 9x19 pistol slide | Receiver | db/CustomItems/WeaponStaccatoXC.json | 68452ddf08572b1e3c35081c |
| Staccato XC RMR sight plate | Mount | db/CustomItems/WeaponStaccatoXC.json | 6846034815241b9ba61936c6 |
| Staccato XC competition hammer | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 684606542ae9fafc455c4360 |
| Staccato XC competition magwell | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 6846082cc557d5abb028d41f |
| Staccato XC front sight | IronSight | db/CustomItems/WeaponStaccatoXC.json | 6846049fd008e00428409f24 |
| Staccato XC hammer | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 684602c4cb2dfb57366dbea3 |
| Staccato XC rear sight with sight mount cap | IronSight | db/CustomItems/WeaponStaccatoXC.json | 6846039a06cd8ba4da4b764c |
| Staccato XC stainless hammer | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 68460695d918c58b53030c31 |
| Staccato XC tactical lightweight hammer | AuxiliaryMod | db/CustomItems/WeaponStaccatoXC.json | 6846069e5a768dfe9bf56af1 |
| Swampfox Kingslayer RMR Mount | Mount | db/CustomItems/WeaponRuger57.json | 692f93612d8e1a750403fce0 |
| TTI Pit Viper 21-round 9x19mm magazine | Magazine | db/CustomItems/WeaponPitViper.json | 684b261ee900b79b5be17c74 |
| TTI Pit Viper 9x19mm barrel | Barrel | db/CustomItems/WeaponPitViper.json | 684b2662bab8ebd87f683c57 |
| TTI Pit Viper 9x19mm pistol slide | Receiver | db/CustomItems/WeaponPitViper.json | 684b2623dd333cf908b7c695 |
| TTI Pit Viper 9x19mm slide (Stainless) | Receiver | db/CustomItems/WeaponPitViper.json | 684b65e1f87ada48dcd107c4 |
| TTI Pit Viper hammer | AuxiliaryMod | db/CustomItems/WeaponPitViper.json | 684b262a5aa7a321ae3a7b5b |
| TTI Pit Viper rear sight | IronSight | db/CustomItems/WeaponPitViper.json | 684b271ce72d12cf50b2a0dd |
| TTI Pit Viper rear sight (Stainless) | IronSight | db/CustomItems/WeaponPitViper.json | 684b66d1a91f8b28868236a4 |
| TTI Pit Viper trigger | AuxiliaryMod | db/CustomItems/WeaponPitViper.json | 684b2626a61732cce6be2fdc |
| TTI Pit Viper trigger (Stainless) | AuxiliaryMod | db/CustomItems/WeaponPitViper.json | 684b662b25b4dc1be645d3ab |
| Wilson Combat EDC X9 9x19 15-round magazine | Magazine | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d59 |
| Wilson Combat EDC X9 9x19 20-round magazine | Magazine | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d63 |
| Wilson Combat EDC X9 9x19 4 inch barrel | Barrel | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d53 |
| Wilson Combat EDC X9 9x19 5 inch barrel | Barrel | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d54 |
| Wilson Combat EDC X9 competition magwell | AuxiliaryMod | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d66 |
| Wilson Combat EDC X9 custom 9x19 single-port compensator | FlashHider | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d60 |
| Wilson Combat EDC X9 extended pistol slide | Receiver | db/CustomItems/WeaponWilsonCombatEDCX9.json | 683a4db6867acb7f16bcfd26 |
| Wilson Combat EDC X9 pistol slide | Receiver | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d55 |
| Wilson Combat EDC X9 skeletonized trigger | AuxiliaryMod | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d61 |
| Wilson Combat EDC X9 slide plate for extended slide | IronSight | db/CustomItems/WeaponWilsonCombatEDCX9.json | 683a4e266a0c93630610d794 |
| Wilson Combat EDC X9 tactical pistol slide | Receiver | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d56 |
| Wilson Combat EDC X9 tactical slide plate | IronSight | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d5b |
| Wilson Combat EDC X9 trigger | AuxiliaryMod | db/CustomItems/WeaponWilsonCombatEDCX9.json | 6761b213607f9a6f79017d5e |
| X-Caliber 416R Stainless Fluted .338 LM 14 inch barrel | Barrel | db/CustomItems/M700XL.json | 5bf36d856d9d529d60cfabb4 |
| X-Caliber 416R Stainless Fluted .338 LM 16 inch barrel | Barrel | db/CustomItems/M700XL.json | 67e8c043679ab98cf5a9a894 |
| X-Caliber 416R Stainless Fluted .338 LM 18 inch barrel | Barrel | db/CustomItems/M700XL.json | 0ecb517dd3c38760877469df |
| X-Caliber 416R Stainless Fluted .338 LM 20 inch barrel | Barrel | db/CustomItems/M700XL.json | edf82ccea7041e055852e7dc |
| X-Caliber 416R Stainless Fluted .338 LM 22 inch barrel | Barrel | db/CustomItems/M700XL.json | c2fb28cafc78fbd58e9ada1d |
| X-Caliber 416R Stainless Fluted .338 LM 24 inch barrel | Barrel | db/CustomItems/M700XL.json | aab94c09a091574e55810eba |
| X-Caliber 416R Stainless Fluted .338 LM 26 inch barrel | Barrel | db/CustomItems/M700XL.json | f8c59c78b4d984a2ce1d298d |
| X-Caliber 416R Stainless Fluted .338 LM 28 inch barrel | Barrel | db/CustomItems/M700XL.json | 549343528ae0e70c0d7d2786 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 14 inch barrel | Barrel | db/CustomItems/M700XL.json | c68564cf5a0d02bc54ea5725 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 16 inch barrel | Barrel | db/CustomItems/M700XL.json | 924d08fd140d56bc7e395091 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 18 inch barrel | Barrel | db/CustomItems/M700XL.json | 2094b55ae444de6c8a241d48 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 20 inch barrel | Barrel | db/CustomItems/M700XL.json | 0e60e51a2dce673d5ed173bf |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 22 inch barrel | Barrel | db/CustomItems/M700XL.json | b5764983c9a6fb507af2aeb3 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 24 inch barrel | Barrel | db/CustomItems/M700XL.json | 51c4d8a19eeb65f225154688 |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 26 inch barrel | Barrel | db/CustomItems/M700XL.json | 5ad7771228e921777b30536f |
| X-Caliber 416R Stainless Fluted .338 Norma Mag 28 inch barrel | Barrel | db/CustomItems/M700XL.json | 07514b8fc5d75c3adb78c64c |
| Yeti Wurks Switchback CZ Scorpion EVO 3 pistol grip | PistolGrip | db/CustomItems/WeaponCZScorpion.json | 687c43775884a8f022f3ce2e |

### Ostblock/Ost-Stil — 108

| Name | Native Kategorie | Quelldatei | Template-ID |
| --- | --- | --- | --- |
| A-545 Flash Hider | FlashHider | db/CustomItems/WeaponA545.json | 69555c7739fb0a1a6f0ad134 |
| A-545 Folding Stock | Stock | db/CustomItems/WeaponA545.json | 69555be939fb0a1a6f0ad130 |
| A-545 Rear Sight | IronSight | db/CustomItems/WeaponA545.json | 69555c7739fb0a1a6f0ad132 |
| AEK series DMR rifle stock | Stock | db/CustomItems/WeaponAEK.json | 686099dc2ff59869ec0d7ca1 |
| AEK series PKM style rifle stock | Stock | db/CustomItems/WeaponAEK.json | 6860a91633cb3c5b813cd05c |
| AEK series ergonomic polymer pistol grip | PistolGrip | db/CustomItems/WeaponAEK.json | 68609be0a3966c5ced08dd83 |
| AEK series grooved polymer pistol grip | PistolGrip | db/CustomItems/WeaponAEK.json | 68609c51885e2fc4ce1bad89 |
| AEK series metal skeletonized stock | Stock | db/CustomItems/WeaponAEK.json | c9dc0b769bd36d06524b9bde |
| AEK series polymer pistol grip | PistolGrip | db/CustomItems/WeaponAEK.json | 68609a328595f9559a666233 |
| AEK series polymer pistol grip (camo) | PistolGrip | db/CustomItems/WeaponAEK.json | 68609b5b0cb4ff845fe560fd |
| AEK series prototype polymer pistol grip | PistolGrip | db/CustomItems/WeaponAEK.json | 68609b7ffc46de766d4c1427 |
| AEK series prototype polymer stock | Stock | db/CustomItems/WeaponAEK.json | 686098acf58016f8d1da57f1 |
| AEK series prototype polymer stock (camo) | Stock | db/CustomItems/WeaponAEK.json | 686098c9e76985dada23754a |
| AEK-971 5.45x39 "GRU" front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 6860a1bf254365b93f2b84cd |
| AEK-971 5.45x39 experimental front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 68609edf4c2ae9848e9424d3 |
| AEK-971 5.45x39 front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 68609dc9fd6af45456e7d11c |
| AEK-971 5.45x39 muzzle brake-compensator | FlashHider | db/CustomItems/WeaponAEK.json | 5fef472e70dc85c1e6fb9e23 |
| AEK-971 5.45x39 prototype front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 6860a1263dc643331f7fac8d |
| AEK-971 5.45x39 tactical front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 6860a1c747b4a81fe5f9ae32 |
| AEK-971 tactical light mount | Mount | db/CustomItems/WeaponAEK.json | 6860a514734dd7c1447966ca |
| AEK-973 7.62x39 experimental muzzle brake-compensator | FlashHider | db/CustomItems/WeaponAEK.json | 686095833d3bd21b2828e767 |
| AEK-973 custom 7.62x39 front-end assembly | Handguard | db/CustomItems/WeaponAEK.json | 68609fd8eca031127ee6038e |
| AK 7.62x39 6L50 experimental polymer 50-round magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 6862b3757fa915587fb36148 |
| AK series CQB rear sight | IronSight | db/CustomItems/WeaponAEK.json | 68609cdd6100b1899111ff9f |
| AN-94 Buffer Tube | Stock | db/CustomItems/WeaponAN94.json | 679a6ee24e1bd9c184e7f307 |
| AN-94 Marksman Stock | Stock | db/CustomItems/WeaponAN94.json | 679a6e9a0f37be945558c4d3 |
| AN-94 Polymer Folding Stock | Stock | db/CustomItems/WeaponAN94.json | 678fe67f710e3e53145bc5e0 |
| AN-94 Skeletonized Folding Stock | Stock | db/CustomItems/WeaponAN94.json | 679a6e58085b54fdd56f5d0d |
| CZ 75 16-Round 9x19mm Magazine | Magazine | db/CustomItems/WeaponCZ75.json | 666101d005b02d7b36e6e958 |
| CZ 75 9x19mm Barrel | Barrel | db/CustomItems/WeaponCZ75.json | 666102059eca4774fd5888a6 |
| CZ 75 9x19mm Threaded Barrel | Barrel | db/CustomItems/WeaponCZ75.json | 666101e8704063719a3d9d62 |
| CZ 75B 9x19mm Pistol Slide | Receiver | db/CustomItems/WeaponCZ75.json | 6661023c34cb7dd29de7f941 |
| CZ 75B Omega 9x19mm Pistol Slide | Receiver | db/CustomItems/WeaponCZ75.json | 67748e8f9837491f8c2c36ae |
| Custom 9x18 single port compensator | FlashHider | db/CustomItems/WeaponPMM12.json | 68d72b647018b13256726238 |
| Custom 9x18 sound suppressor | Silencer | db/CustomItems/WeaponPMM12.json | 68d4192eb4411d527334f35f |
| Custom 9x18 weighted compensator | FlashHider | db/CustomItems/WeaponPMM12.json | 68d72bc442bb7ad7e98b3945 |
| Makeshift 7.62x54R 55-Round Drum Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | e8aa1ef743f71f3e03b858f2 |
| OmniTech Systems SVD 25-Round Polymer Magazine | Magazine | db/CustomItems/WeaponDragunov.json | 6657be3d8876d16a5866f413 |
| OmniTech Systems polymer SVD 10-round magazine | Magazine | db/CustomItems/WeaponDragunov.json | 6657be33ed0d0e6d65f1ba00 |
| PBS-1N 7.62x39 experimental sound suppressor | Silencer | db/CustomItems/WeaponAEK.json | 686092fd52a7a52a320b81b8 |
| PBS-2N 5.45x39 experimental sound suppressor | Silencer | db/CustomItems/WeaponAEK.json | 3777aadf41ebb6d92d108931 |
| PM 9x18PM 18-Round Extended Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | a10805748f12422327de49a8 |
| PMM-12 9x18 12-round magazine | Magazine | db/CustomItems/WeaponPMM12.json | 68d4106532390e96f5a8f664 |
| PMM-12 9x18 17-round extended magazine | Magazine | db/CustomItems/WeaponPMM12.json | 68dd61e02d8ca3d84120bb2a |
| PMM-12 custom weighted pistol slide | Receiver | db/CustomItems/WeaponPMM12.json | 68d70444b480beeadf9b6ffa |
| PMM-12 ergo grips | PistolGrip | db/CustomItems/WeaponPMM12.json | 68d4991f2a947108d6e5d85a |
| PMM-12 ergo grips (plum) | PistolGrip | db/CustomItems/WeaponPMM12.json | 68d5bd9f06a3279b96fb0d3c |
| PMM-12 extended pistol slide | Receiver | db/CustomItems/WeaponPMM12.json | 68d60cbe2fcd09bfc84bd29f |
| PMM-12 laser aiming module | TacticalCombo | db/CustomItems/WeaponPMM12.json | 68d739c05eadd47b0e4617a7 |
| PMM-12 optic mount | Mount | db/CustomItems/WeaponPMM12.json | 68d593bf2809950ce8e620de |
| PMM-12 pistol slide | Receiver | db/CustomItems/WeaponPMM12.json | 68d411fe0fe5406a6566ce17 |
| PMM-12 side grips | PistolGrip | db/CustomItems/WeaponPMM12.json | 68d416ce1bc7491da67d7880 |
| PMM-12 tactical rail | Mount | db/CustomItems/WeaponPMM12.json | 68d5b3d19fbee23bd61f9779 |
| PP-91 Kedr 9x18PM 55-Round Makeshift Drum Magazine | Magazine | db/CustomItems/Attachment_Magazines.json | a483029913455f81127fd070 |
| SA-1 rear sight | IronSight | db/CustomItems/WeaponSA01.json | 69777201f57d45a2ec02aafe |
| SR-2 Udav 9x21 18-round magazine | Magazine | db/CustomItems/WeaponUdav.json | 68677d6e0eeee2ef7f1f4576 |
| SR-2 Udav 9x21mm barrel | Barrel | db/CustomItems/WeaponUdav.json | 68677fe993c9c9a5cf82dc05 |
| SR-2 Udav 9x21mm threaded barrel | Barrel | db/CustomItems/WeaponUdav.json | 68677f839282a3b5a7f1e838 |
| SR-2 Udav prototype 9x21 sound suppressor | Silencer | db/CustomItems/WeaponUdav.json | 686780cea875e915fec121ec |
| SR-2 Udav tritium front pistol sight | IronSight | db/CustomItems/WeaponUdav.json | 6867821cdea16277c803a584 |
| SR-2 Udav tritium rear pistol sight | IronSight | db/CustomItems/WeaponUdav.json | 68678220f740d20e8625095f |
| SVD 7.62x54R 10-round magazine (Camo) | Magazine | db/CustomItems/WeaponDragunov.json | 6657be2ae0a6ff466db575de |
| SVD 7.62x54R Flash Hider | FlashHider | db/CustomItems/WeaponDragunov.json | 6657bde9e47c756d6b149a5b |
| SVD Camo Handguard | Handguard | db/CustomItems/WeaponDragunov.json | 6657bd80a635c01603bd9ef7 |
| SVD Custom 2-Port Muzzle Brake | FlashHider | db/CustomItems/WeaponDragunov.json | 6657bdde5fb53ba8b0657bd6 |
| SVD Custom 3-Port Muzzle Brake | FlashHider | db/CustomItems/WeaponDragunov.json | 6657bdd3ceb56099bc50c188 |
| SVD Wooden Stock | PistolGrip | db/CustomItems/WeaponDragunov.json | 6657bd29491b4baa8790dd91 |
| SVD buffer tube adapter system | PistolGrip | db/CustomItems/WeaponDragunov.json | 6657bdb47618210e20db4064 |
| SVD carbine length barrel | Barrel | db/CustomItems/WeaponDragunov.json | 6657bdc55b6dea8c9e65dc93 |
| SVD dust cover (Camo) | Receiver | db/CustomItems/WeaponDragunov.json | 6657be1f9014a9663b39bc4b |
| SVD ghillie handguard | Handguard | db/CustomItems/WeaponDragunov.json | 6657bd891b74ff27b6a64fae |
| SVD polymer marksman stock | PistolGrip | db/CustomItems/WeaponDragunov.json | 6657bd990e17e7fb2bab32e6 |
| SVD polymer marksman stock (Green) | PistolGrip | db/CustomItems/WeaponDragunov.json | 6657bda8ebfed767215c15d4 |
| SVD wooden handguard | Handguard | db/CustomItems/WeaponDragunov.json | 6657bd663a5a69b08786fecc |
| Simonov AG-042/043 muzzle brake-compensator | FlashHider | db/CustomItems/WeaponAGSeries.json | 68c63ee6dcb5f65309eb4fcc |
| Simonov AG-042/043 rear sight | IronSight | db/CustomItems/WeaponAGSeries.json | 68c23b3d4a286357245eb641 |
| Simonov AG-043 collapsible wire stock | Stock | db/CustomItems/WeaponAGSeries.json | 68cf53ddb8f10c637706563c |
| VSS-M "Ublyudok" 9x39 integral barrel-suppressor | Silencer | db/CustomItems/Attachment_VSSM.json | 68cae1561e2d372b839a3fed |
| Valmet M78 7.61x51 sound suppressor | Silencer | db/CustomItems/WeaponM78.json | 68e00ec3cddba35e72a8c0be |
| Valmet M78 7.62x51 20-round magazine | Magazine | db/CustomItems/WeaponM78.json | 68e00cc25e5ce571985cdca8 |
| Valmet M78 7.62x51 30-round extended magazine | Magazine | db/CustomItems/WeaponM78.json | 68e00d2b14eecfef9ea7f214 |
| Valmet M78 Bipod | Bipod | db/CustomItems/WeaponM78.json | 68e00daaa838761ebcba47cb |
| Valmet M78 DMR polymer stock | PistolGrip | db/CustomItems/WeaponM78.json | 68e001dda53d86c80780200e |
| Valmet M78 DMR wooden stock | PistolGrip | db/CustomItems/WeaponM78.json | 68e001e3a86734a7aa8bea88 |
| Valmet M78 picatinny rail mount | Mount | db/CustomItems/WeaponM78.json | 68e0717d51eff1d09e8a2eba |
| Valmet M78 polymer handguard | Handguard | db/CustomItems/WeaponM78.json | 68e002780a54c33a2dc35208 |
| Valmet M78 polymer pistol grip | PistolGrip | db/CustomItems/WeaponM78.json | 68e00c2a03050b5a4665ac9a |
| Valmet M78 quad rail handguard | Handguard | db/CustomItems/WeaponM78.json | 68e03bc3709d4e16c56555ce |
| Valmet M78 ribbed pistol grip | PistolGrip | db/CustomItems/WeaponM78.json | 68e00c5d3b678411f10c8705 |
| Valmet M78 tactical barrel mount | Mount | db/CustomItems/WeaponM78.json | 68e00e6f68065099baaf5791 |
| Valmet M78 tubular stock | Stock | db/CustomItems/WeaponM78.json | 68e000ef47a1ced5ac0aa625 |
| Valmet M78 wooden clubfoot stock | Stock | db/CustomItems/WeaponM78.json | 68e00099973addbbabfac4c5 |
| Valmet M78 wooden handguard | Handguard | db/CustomItems/WeaponM78.json | 68e00215885abec1d3b2f360 |
| Zastava M76 7.92x57 10-round magazine | Magazine | db/CustomItems/WeaponM76.json | 66e887e85c11853b81a538f4 |
| Zastava M76 7.92x57 makeshift 44-round drum magazine | Magazine | db/CustomItems/WeaponM76.json | 682e97bcbbfa734064983a89 |
| Zastava M76 Custom Gas Tube | Handguard | db/CustomItems/WeaponM76.json | 66e888c270c3285b5d91fadb |
| Zastava M76 Moloc Armament 7.92x57 sound suppressor | Silencer | db/CustomItems/WeaponM76.json | 66e88961b65d524acd112c0a |
| Zastava M76 PKM-Style wooden marksman stock | Stock | db/CustomItems/WeaponM76.json | 66e888b781e5751af50ce6bd |
| Zastava M76 RPK-style wooden rifle stock | Stock | db/CustomItems/WeaponM76.json | 66e888aaa157baa64d8338d1 |
| Zastava M76 ergonomic pistol grip | PistolGrip | db/CustomItems/WeaponM76.json | 66e887c36b0f8c1fc0684f18 |
| Zastava M76 fixed wooden stock | Stock | db/CustomItems/WeaponM76.json | 66e8888f8fa84b78297cd8da |
| Zastava M76 flash hider | FlashHider | db/CustomItems/WeaponM76.json | 66e8894237d122e047db9483 |
| Zastava M76 gas block handguard assembly | Handguard | db/CustomItems/WeaponM76.json | 66e888c270c3285b5d91fada |
| Zastava M76 gas block handguard assembly with foregrip | Handguard | db/CustomItems/WeaponM76.json | 66e888e39ace35080ccb02bd |
| Zastava M76 railed dust cover | Receiver | db/CustomItems/WeaponM76.json | 66e8891c2072d5483a2595e9 |
| Zastava M76 rear sight | IronSight | db/CustomItems/WeaponM76.json | 66e887cf09ede6c7c42def11 |
| Zastava M76 standard dust cover | Receiver | db/CustomItems/WeaponM76.json | 66e888fe06f561b136f47117 |
| Zastava M76 wooden pistol grip | PistolGrip | db/CustomItems/WeaponM76.json | 66e887960e41855970b03be2 |

### Gemeinsam/neutral — 54

| Name | Native Kategorie | Quelldatei | Template-ID |
| --- | --- | --- | --- |
| AK 7.62x39 Hungarian "Tanker" 20-round magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 6862b435bfb984348be4f59b |
| AK-103 7.62x39 30-round magazine (camo) | Magazine | db/CustomItems/WeaponAEK.json | 686097621aa8418da42c62eb |
| AK-103 7.62x39 30-round magazine (plum) | Magazine | db/CustomItems/Attachment_Magazines.json | 686096ffbb9dec119f64f4b3 |
| AK-74 5.45x39 6L23 30-round magazine (Camo) | Magazine | db/CustomItems/WeaponAN94.json | 679a702c47bb7fa666fe618e |
| AK-74 polymer pistol grip (Camo) | PistolGrip | db/CustomItems/WeaponAN94.json | 679a766855f7e9fa7b1abfdf |
| AR-15 Colt A2 pistol grip (wrapped) | PistolGrip | db/CustomItems/WeaponCheytacM200.json | 68fd5abc5c7f36f8d8c208c3 |
| AR-15 MDT Elite pistol grip | PistolGrip | db/CustomItems/M700XL.json | ebd108b0a8c45420f7624a3f |
| AR-15 Magpul MOE custom carbine stock | Stock | db/CustomItems/WeaponM249.json | 66f4a8d75a44076e9b2909d2 |
| Aimpoint CS front lens cap | AuxiliaryMod | db/CustomItems/Attachment_Scopes.json | 186b5c3e8ace3d9a1343bfeb |
| Aimpoint CS reflex sight | Collimator | db/CustomItems/Attachment_Scopes.json | 03f6c86c56945862820e1006 |
| Ak5C RIS handguard rail cover (foregrip) | AuxiliaryMod | db/CustomItems/WeaponAK5C.json | 682b9dd126018d7470776a59 |
| Ak5C RIS handguard rail covers (bottom) | AuxiliaryMod | db/CustomItems/WeaponAK5C.json | 682b9d50a05fef25589376d4 |
| Arsenal Circle 10 7.62x39 polymer 40-round magazine | Magazine | db/CustomItems/Attachment_Magazines.json | 6862b46837dc96b767cda666 |
| BAE Systems SUSAT 4x32 scope | AssaultScope | db/CustomItems/Attachment_Scopes.json | 2322ee962f6c77a1284abc1e |
| CZ Scorpion EVO 3 front sight | IronSight | db/CustomItems/WeaponCZScorpion.json | 687fb51f2756d21d3145a094 |
| CZ Scorpion EVO 3 rear sight | IronSight | db/CustomItems/WeaponCZScorpion.json | 687fb5224b7bf5a5702898ee |
| FN F2000 1.6x Scope | AssaultScope | db/CustomItems/WeaponF2000.json | 696958420d2309811a0fd1d5 |
| FN F2000 5.56x45 Negative flash hider | FlashHider | db/CustomItems/WeaponF2000.json | 69694eed0d2309811a0fd1d3 |
| FN F2000 5.56x45 Scope Cover | IronSight | db/CustomItems/WeaponF2000.json | 696958420d2309811a0fd1d7 |
| FN F2000 5.56x45 flash hider | FlashHider | db/CustomItems/WeaponF2000.json | 69694eed0d2309811a0fd1d1 |
| FN F2000 Front Sight | IronSight | db/CustomItems/WeaponF2000.json | 6968029b5966cfdf3708c748 |
| FN F2000S Front Sight | IronSight | db/CustomItems/WeaponF2000.json | 696808315966cfdf3708c74c |
| Gemtech Tundra 9x19 sound suppressor | Silencer | db/CustomItems/Attachment_Suppressors.json | 666102ef2d367ec85ddd2030 |
| HK 417 Drum rear sight | IronSight | db/CustomItems/WeaponHK417.json | 664a5ff61c4610dc01e84917 |
| HK 417 front sight | IronSight | db/CustomItems/WeaponHK417.json | 664a600b58753b43b7fea3af |
| HK Sturmgriff foregrip (Black) | Foregrip | db/CustomItems/Attachment_Foregrips.json | 668070f11aff0d6b0cd7fb13 |
| ITL MARS reflex sight | Collimator | db/CustomItems/Attachment_Scopes.json | 5ae3d18efa17cbd51e898ced |
| IWI Carmel Flash Hider | FlashHider | db/CustomItems/WeaponCarmel.json | 66ba2659b5b76e8fa30000c0 |
| MDT Carbine buffer tube | Stock | db/CustomItems/M700XL.json | 29ac2a6601d5e966724ccb69 |
| MSBS Grot 5.56x45 flash hider | FlashHider | db/CustomItems/WeaponMSBS.json | 696e452a1a038d893a04a42b |
| MSBS Grot A2 M-LOK Foregrip | Foregrip | db/CustomItems/WeaponMSBS.json | 696fc803cf7469bf38051731 |
| MSBS Grot A2 M-LOK Foregrip (FDE) | Foregrip | db/CustomItems/WeaponMSBS.json | 696fe281cf7469bf3805173c |
| Magpul MBUS Gen3 flip-up front sight | IronSight | db/CustomItems/Attachment_IronSights.json | 66ba2a6be18ed84221edd72d |
| Magpul MBUS Gen3 flip-up front sight | IronSight | db/CustomItems/Attachment_IronSights.json | 66ba2a7583204e25695233c0 |
| Magpul MBUS Gen3 flip-up rear sight | IronSight | db/CustomItems/Attachment_IronSights.json | 66ba297b859e57cf91e38a5d |
| Magpul MBUS Gen3 flip-up rear sight | IronSight | db/CustomItems/Attachment_IronSights.json | 66ba29d44744d98a4df0073b |
| OSS 7.62x51 QD Muzzle Brake | FlashHider | db/CustomItems/Attachment_Muzzles.json | 664b9a705538c7da4f026f22 |
| OSS Helix QD 7.62x51 sound suppressor | Silencer | db/CustomItems/Attachment_Suppressors.json | 664b9a4798222d80ce536df6 |
| OmniTech Systems S4 Skeletonized AK Pistol Grip | PistolGrip | db/CustomItems/WeaponAN94.json | 679a73b003d8055ba8385ad5 |
| SAW-Style Polymer Foregrip | Foregrip | db/CustomItems/WeaponM249.json | 66ea3770d7c670434b6ed74f |
| SIG Sauer SRD762Ti 7.62x51 sound suppressor (Camo) | Silencer | db/CustomItems/WeaponMasada.json | 691612be65f7a3e248c8be2a |
| STEEL Pegasus 5.56x45 Silencer | Silencer | db/CustomItems/WeaponMSBS.json | 696fc5ed64572d85f30aac10 |
| Singuard Arms "Wages of Sin" 5.56x45 muzzle brake | FlashHider | db/CustomItems/WeaponWagesOfSin.json | 676b52d48148b7479845358a |
| Singuard Arms "Wages of Sin" 5.56x45 muzzle brake (Anodized Red) | FlashHider | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d44 |
| Singuard Arms "Wages of Sin" 5.56x45 muzzle brake (FDE) | FlashHider | db/CustomItems/WeaponWagesOfSin.json | 676c3d512d4ab6764b8d793c |
| Singuard Arms "Wages of Sin" Pistol Grip | PistolGrip | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d46 |
| Singuard Arms "Wages of Sin" pistol grip | PistolGrip | db/CustomItems/WeaponWagesOfSin.json | 676b5258816c8784c7495dc5 |
| Singuard Arms "Wages of Sin" pistol grip (FDE) | PistolGrip | db/CustomItems/WeaponWagesOfSin.json | 676c3d67d69e478b3d69cd21 |
| Singuard Arms "Wages of Sin" stock | Stock | db/CustomItems/WeaponWagesOfSin.json | 676b53186763e3e3811d6488 |
| Singuard Arms "Wages of Sin" stock (Anodized Red) | Stock | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d47 |
| Singuard Arms "Wages of Sin" stock (FDE) | Stock | db/CustomItems/WeaponWagesOfSin.json | 676c3d6c1c64d6eacd7b8c16 |
| Singuard Arms AR-15 buffer tube | Stock | db/CustomItems/WeaponWagesOfSin.json | 676b4fbf3cb5dd69a908ccd0 |
| Singuard Arms AR-15 buffer tube (Anodized Red) | Stock | db/CustomItems/WeaponWagesOfSin.json | 6761b213607f9a6f79017d41 |
| TangoDown vertical foregrip (OD) | Foregrip | db/CustomItems/Attachment_Foregrips.json | 682d014f0f0eb62eb4269012 |

## Vollständige Liste: Questgegenstaende

### Gemeinsam/neutral — 10

| Name | Native Kategorie | Quelldatei | Template-ID |
| --- | --- | --- | --- |
| AEK-971 Prototype | Jewelry | db/CustomItems/QuestItems.json | 68716c83eb31cf77ff9f9fd8 |
| Antique Pistol Display | Jewelry | db/CustomItems/QuestItems.json | 685618fbcc160182de00bfc4 |
| Crate of OFZ 30x165mm shells | Other | db/CustomItems/QuestItems.json | 694da5f84825e8c609000001 |
| Dogtag UNTAR | Info | db/CustomItems/QuestItems.json | 6834f82e5cab6617274214a5 |
| Harmy's Star Wars Despecialized Edition | Electronics | db/CustomItems/QuestItems.json | 687306abe00a4125a64a80a8 |
| Note to Jaeger | Info | db/CustomItems/QuestItems.json | 6879e7fa4c184bd0590dbab6 |
| Playstation 2 console with Game | Electronics | db/CustomItems/QuestItems.json | 687308686fa170bea044d21d |
| Shipping Manifest | Jewelry | db/CustomItems/QuestItems.json | 686c1f53c57631545685f81c |
| Swedish Intel hard drive | Electronics | db/CustomItems/QuestItems.json | 683647bb7afb4f25023acf7a |
| Tim Burton's Batman Movie Poster | Jewelry | db/CustomItems/QuestItems.json | 687464af51ed3be7e4f6f525 |

## Vollständige Liste: Poster

### Gemeinsam/neutral — 11

| Name | Native Kategorie | Quelldatei | Template-ID |
| --- | --- | --- | --- |
| Colt 733 Poster | Flyer | db/CustomItems/WTT_Posters.json | 6fe34081c815d875ddca8322 |
| Cowboy Bebop Poster 1 | Flyer | db/CustomItems/WTT_Posters.json | 929ee8d8dea876224fe8daa6 |
| Cowboy Bebop Poster 2 | Flyer | db/CustomItems/WTT_Posters.json | 6f21f29f125d6da3953f4438 |
| Galil Poster | Flyer | db/CustomItems/WTT_Posters.json | 484236f4faa2cd6a4bffcee8 |
| M16 Patent Poster | Flyer | db/CustomItems/WTT_Posters.json | 9b6d3042e08188ba7ccb2e36 |
| M16A2 Poster | Flyer | db/CustomItems/WTT_Posters.json | 6bff49546a0a2a8d75cea190 |
| Missing Texture Poster | Flyer | db/CustomItems/WTT_Posters.json | f5329885c0d82effc2d4930c |
| Pulp Fiction 2 Poster | Flyer | db/CustomItems/WTT_Posters.json | ae64bd806a5e9ca6d8ec656c |
| Pulp Fiction Poster | Flyer | db/CustomItems/WTT_Posters.json | fd1dc8b28bf5a17bbc218dc0 |
| Uzi Synthwave Poster | Flyer | db/CustomItems/WTT_Posters.json | 1df16fcdbecde4b7fd772028 |
| Whiskey Project Poster | Flyer | db/CustomItems/WTT_Posters.json | 5a5061c950ff1642f5f79de0 |
