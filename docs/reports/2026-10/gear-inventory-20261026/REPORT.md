# Gear-Inventur: West/NATO-Stil und Ostblock/Ost-Stil

Stand **26.10.2026** · installierte **SPT 4.1.6**-Dateidatenbank + **WTT-ContentBackport 2.0.5**. Planungsgrundlage für spätere USEC/BEAR-Kits; keine Änderung an Spiel, Profilen, Kits, Konfiguration oder laufenden Prozessen.

## Ergebnis

Die reine Waffenanzahl ist nahezu ausgeglichen: **82 westliche zu 81 östlichen Waffen-Templates**. Bereinigt um Namens-Farb-/Skinvarianten sind es **73 zu 78 Modelle**; bei der im Generator offengelegten gröberen Familienbildung **48 zu 42 Familien**. Diese Bereinigungen sind Orientierung, keine Gleichsetzung von Handhabung oder Stärke. Ein großer Teil der östlichen Varianten stammt aus der AK-Familie.

Bei Körperschutz stehen **51 westliche** (21 Westen + 30 gepanzerte Rigs) **56 östlichen** (27 + 29) gegenüber, dazu eine neutrale Sonderweste. Die Gesamtauswahl reicht für zwei eigenständige, abwechslungsreiche Kit-Pools. Die Klassen und Rollen sind jedoch unterschiedlich verteilt.

**Empfehlung:** Waffen und sichtbare Grundausrüstung nach Stil trennen, Headsets, Medizin und universelle Optiken gemeinsam anbieten. Konkrete Kits je Rolle/Tier paarweise ausbalancieren; nicht sämtliche Templates mit derselben Wahrscheinlichkeit ziehen.

## Geplanter Zusatzbestand: WTT-Armory 3.0.0

**Noch nicht installiert.** Der [separate Armory-Anhang](ARMORY.md) enthält alle 833 Einträge: 80 Waffen-Templates, 705 Waffenteile, 27 Munitionstypen, zehn Questgegenstände und elf Poster. Keine zusätzliche Rüstung oder Headsets. [Maschinenlesbare Armory-Liste](ARMORY.json).

| Waffen-Templates inklusive Varianten | West/NATO-Stil | Ostblock/Ost-Stil |
| --- | --- | --- |
| Installiert: SPT + ContentBackport | 82 | 81 |
| Armory zusätzlich, geplant | 62 | 18 |
| Hypothetisch zusammen | 144 | 99 |

Die weiteren Tabellen dieses Berichts und inventory.json bleiben der installierte Bestand. Armory wird nur im Anhang geführt; die hypothetische Zusammenrechnung bedeutet weder eine Installation noch eine geprüfte Laufzeitkompatibilität. Dort sind auch die Stil-Grenzfälle Valmet M78, Bofors Ak5 und CZ 75 dokumentiert.

## Dateien

- [Vollständige Gegenstandsliste mit IDs und WTT-Kennzeichnung](ITEMS.md)
- [Maschinenlesbare Inventur mit Zuordnungsgrund, Werten und Quellen-Hashes](inventory.json)
- [Reproduzierbarer Generator mit allen Klassifikationsregeln](generate.py)

## Zählweise und Reichweite

- 4553 Item-Templates in der installierten Basis, 764 zusätzliche WTT-CustomItems; keine überschneidenden IDs. Node-Kategorien zählen nicht als Gegenstände.
- Davon **3947 Gear-Datensätze** erfasst, **3465** im direkten Vergleich und **482** separat ausgewiesen. Feste Panzerungseinsätze, interne Waffen-Controller, stationäre Waffen, Signal-/Spielzeugwaffen sowie interne Splittermunition zählen nicht als zusätzliche Kit-Auswahl.
- Alle erfassten Gegenstände bleiben in inventory.json und ITEMS.md nachvollziehbar. Kein Handbook-Eintrag führt vorsichtshalber in die separate Liste; das bedeutet nicht automatisch nicht benutzbar.
- **Statische Inventur, kein Export eines laufenden Servers.** Installierte JSONs werden ausgewertet und WTT-Klone mit ihren Properties-Overrides rekonstruiert. Erfolgreiches Laden, Händlerfreischaltungen, APBS-/SVM-Anpassungen, Spawnchancen und vollständige Presetbaubarkeit sind damit nicht nachgewiesen.
- Ausgenommen sind Quest-/Tauschloot, Essen/Trinken, Geld, Schlüssel und Behälter, Bekleidung aus der separaten Customization-Datenbank, Köpfe/Stimmen und andere dynamisch erzeugte Templates. Arena-Molotov steht unten gesondert.
- Englische Gegenstandsnamen bleiben absichtlich erhalten, damit IDs und Namen mit der Installation vergleichbar sind.

## Vereinbarte Aufteilung

Nach Bestätigung des Nutzers zählt **Plattform/Stil**, nicht Herstellerland und nicht allein das Kaliber. West/NATO ist ein Gameplay-Pool einschließlich westlich geprägter Schweizer, österreichischer, japanischer und israelischer Systeme; Ostblock/Ost umfasst auch moderne russische und chinesische Systeme. Das sind keine Behauptungen über historische Bündnismitgliedschaft.

| Sonderfall | Pool | Grund |
| --- | --- | --- |
| ADAR 2-15, STM-9, NL545 DI/GP | West | AR-Familie trotz russischer Hersteller |
| RD-704 | Ost | AK-Familie trotz US-Hersteller |
| Mk47 Mutant | West | AR-basierte Hybridplattform trotz AK-Magazinen |
| AK-101/102, AK-308, VPO-101, DVL-10, T-5000M | Ost | Plattform bleibt östlich, auch bei westlichem Kaliber |
| Howa Type 20, UZI, DevTac Ronin | West | Stil-/Poolentscheidung; keine NATO-Mitgliedschaft unterstellt |
| QBZ-191, Type 56 Chicom | Ost | Ost-Pool umfasst chinesische Ausrüstung |
| Medizin und Werkzeuge | Gemeinsam | Keine künstlichen funktionalen Lücken |
| Universelle Zubehörteile | Gemeinsam, soweit potenziell beidseitig passend | Kompatibilität hat Vorrang vor Herstellerland |

Die lokalen Beschreibungen bestätigen die russische AR-Abstammung der NL545 und die AK-Basis der RD-704. Als externer Gegencheck dokumentiert [CMMG selbst die AK-Magazine der Mk47](https://support.cmmg.com/what-magazine-does-each-cmmg-platform-use); das [US-Army-Datenblatt zur Howa Type 20](https://odin.t2com.army.mil/WEG/Asset/Type_89_) benennt Japan und 5,56 mm. Die Poolzuordnung bleibt unsere Designentscheidung.

## Waffen nach Rolle

| Waffengruppe | West | Ost |
| --- | --- | --- |
| Sturmgewehre/AR-Karabiner | 27 | 26 |
| Karabiner/Spezialgewehre | 0 | 10 |
| SMG/PCC | 16 | 7 |
| Schrotflinten | 5 | 11 |
| DMR | 8 | 3 |
| Repetierer/Einzellader | 4 | 7 |
| Pistolen/Revolver | 17 | 11 |
| Maschinengewehre | 3 | 5 |
| Granatwerfer | 2 | 0 |
| Raketenwerfer | 0 | 1 |

Die native Datenbank ordnet MP-18 unter Shotgun, MTs-255 und M32 unter Revolver ein. Für diese Tabelle sind sie nach tatsächlicher Rolle korrigiert. Signalwaffen, Master Hand, Fahrzeug-/stationäre MGs sowie die zwei Unterlaufwerfer-Controller zählen nicht mit. GP-25/M203 bleiben Zubehör.

- **Westliche Auswahl:** mehr SMG/PCC (16:7), DMR (8:3) und Pistolen/Revolver (17:11). .338-LM-Systeme, MCX-SPEAR und AK-50 bieten besondere Rollen.
- **Östliche Auswahl:** mehr Schrotflinten (11:5), Karabiner/Spezialgewehre (10:0 in dieser nativen Kategorie) und MG-Templates (5:3). VAL/VSS/SR-3M, 9A-91/VSK-94, ASh-12 und KS-23 ergänzen AK-Systeme.
- Westliche semiautomatische AR-Karabiner zählen in der Sturmgewehrzeile mit. 10:0 bedeutet deshalb nicht, dass West keine Karabiner besitzt. Feuermodi stehen pro Waffe in der Detailinventur.
- Granat-/Raketenwerfer separat behandeln: 2 westliche Hand-Granatwerfer und ein östlicher RShG-2. Dessen Raketen-Template fehlt im Handbook und wird separat ausgewiesen; ein regulär funktionsfähiges Arena-Kit ist nicht bestätigt.

## WTT-Beitrag bei den Waffen

WTT ergänzt **14 Waffen-Templates: 9 West, 5 Ost**. Die Basis alleine liegt bei 73 West zu 76 Ost. Nicht alle Ergänzungen sind neue Familien: HK416 RAL8000, SR-25 Taupe, Saiga Redline und P90 Scourge sind benannte Varianten bestehender Systeme.

| WTT-Waffe | Pool |
| --- | --- |
| AS VAL MOD.4 9x39 special assault rifle | Ostblock/Ost-Stil |
| Kalashnikov AK-308 7.62x51 assault rifle | Ostblock/Ost-Stil |
| Norinco QBZ-191 5.8x42 assault rifle | Ostblock/Ost-Stil |
| Saiga-12K 12ga automatic shotgun (Redline) | Ostblock/Ost-Stil |
| TKPD 9.3x64 carbine | Ostblock/Ost-Stil |
| Colt M16A1 5.56x45 assault rifle | West/NATO-Stil |
| Colt M16A2 5.56x45 assault rifle | West/NATO-Stil |
| Custom Guns NL545 (DI) 5.45x39 assault rifle | West/NATO-Stil |
| Custom Guns NL545 (GP) 5.45x39 assault rifle | West/NATO-Stil |
| FN P90 5.7x28 submachine gun (Scourge) | West/NATO-Stil |
| HK 416A5 5.56x45 assault rifle (RAL 8000) | West/NATO-Stil |
| Howa Type 20 5.56x45 assault rifle | West/NATO-Stil |
| Knight's Armament Company SR-25 7.62x51 marksman rifle (Taupe) | West/NATO-Stil |
| Marlin MXLR .308 ME lever-action rifle | West/NATO-Stil |

## Gear nach Kategorie

Templates inklusive Farb-/Skinvarianten. Ein gemeinsames Teil erscheint einmal unter Gemeinsam, nicht zweimal.

| Kategorie | West | Ost | Gemeinsam | Offen |
| --- | --- | --- | --- | --- |
| Armbänder | 1 | 1 | 34 | 0 |
| Brillen | 4 | 2 | 27 | 0 |
| Gepanzerte Rigs | 30 | 29 | 0 | 0 |
| Gesichtsbedeckungen | 17 | 2 | 109 | 0 |
| Headsets | 25 | 1 | 2 | 0 |
| Helme | 57 | 29 | 5 | 0 |
| Helmzubehör/Panzerteile | 66 | 18 | 0 | 0 |
| Kopfbedeckungen | 4 | 6 | 48 | 0 |
| Magazine | 124 | 67 | 31 | 0 |
| Medizin | 0 | 0 | 43 | 0 |
| Munition | 50 | 73 | 67 | 0 |
| Nahkampf | 8 | 7 | 12 | 0 |
| Rucksäcke | 27 | 11 | 9 | 0 |
| Schutzwesten | 21 | 27 | 1 | 0 |
| Ungepanzerte Rigs | 16 | 23 | 5 | 0 |
| Visiere/Nacht-/Wärmesicht | 65 | 50 | 125 | 0 |
| Waffen | 82 | 81 | 0 | 0 |
| Waffenzubehör | 868 | 537 | 450 | 1 |
| Wechselplatten | 5 | 17 | 16 | 0 |
| Werkzeuge/Spezial | 0 | 0 | 16 | 0 |
| Wurfgranaten | 4 | 9 | 0 | 0 |

**Headsets sind der größte Engpass einer strikten Stiltrennung:** 25 westliche Varianten gegenüber einem GSSh-01 auf der östlichen Seite, plus zwei gemeinsame OPSMEN-Varianten. Diese Kategorie sollte für BEAR zugänglich bleiben.

Bei Helmen erzeugen viele westliche Skins einen großen Vorsprung. Ohne allgemeine Namenszusätze sind es 21 westliche und 17 östliche Namensgruppen, plus Sonderausrüstung. Das ist keine geprüfte Gleichheit von Schutz, Trefferzonen, Hörbeeinträchtigung oder Aufsatzmöglichkeiten.

## Körperschutz: deklarierte Standard-Frontklasse

Ausgewertet wird der deklarierte Standard der Frontplatte, ersatzweise der Frontweichpanzerung. Höherklassige Seitenteile erhöhen den Wert nicht. Das sind Slotvorgaben, kein tatsächlich erzeugtes Kit. Material, Abdeckung, Haltbarkeit, Gewicht und Bewegungseinschränkung bleiben weitere Balancefaktoren.

| Frontklasse | Westliche Westen/Rigs | Östliche Westen/Rigs | Gemeinsam |
| --- | --- | --- | --- |
| 1 | 2 | 0 | 0 |
| 2 | 8 | 2 | 0 |
| 3 | 2 | 11 | 1 |
| 4 | 15 | 17 | 0 |
| 5 | 16 | 21 | 0 |
| 6 | 8 | 5 | 0 |

Die Klassen 2–6 lassen sich grundsätzlich beidseitig bedienen. Klasse-1-Replikträger nicht als gleichwertige Alternative zu regulären Plattenträgern ziehen. Ein Kitgenerator sollte zunächst denselben Zielwert für Frontschutz und ähnliche Abdeckung wählen; Klasse allein ist kein vollständiges Rüstungsbudget.

## Munition und Zubehör

Munition wird nach Kalibern der vorgeschlagenen Waffenpools zugeordnet. 5,56×45, 7,62×51 und 9×19 sind beidseitig relevant; durch NL545/Mk47 auch 5,45×39 und 7,62×39. Schaden/Penetration stehen pro Patrone in der Inventur. Ein passendes Kaliber beweist nicht, dass jedes Waffen-/Magazin-Template jede Variante akzeptiert.

Zubehör und Magazine werden über gerichtete Slots verfolgt, ergänzt um deklarierte WTT-Klon-Slot-Erweiterungen. Eine Verbindung zu beiden Pools ergibt Gemeinsam. Bei fehlendem Pfad werden eindeutig benannte Plattformteile nach Stil zugeordnet, ausdrücklich ohne bestätigte Kompatibilität; Helm-/NVG-Halter bleiben gemeinsam. Diese potenzielle Endansicht ersetzt weder tatsächliche Loaderreihenfolge noch Konfliktregeln, zwingende Teile, Presets oder Tests im Weapon Builder. Der Eintrag Trophy bleibt Offen. Aus einer Stilzuordnung darf der Kitgenerator keine technische Freigabe ableiten.

Optiken, Laser, Griffe und Magazine sollten keine unnötige Herstellerbindung erhalten. Waffenbasis und sichtbares Tragegear reichen zur stilistischen Unterscheidung.

## Spätere Kit-Paare als Ausgangspunkt

| Rolle | Westlicher Kandidat | Östlicher Kandidat |
| --- | --- | --- |
| Sturmgewehr | M4A1/HK416 | AK-74M/AK-12 |
| 7,62-Allrounder | SCAR-H/SA58 | AK-308 |
| SMG | MP5/MPX | PP-19-01/SR-2M |
| DMR | SR-25/M1A | SVDS/TKPD |
| MG | M60E4/E6 | PKM/RPD |
| Pumpgun | M870/590A1 | MP-133 |
| Präzision | M700 | SV-98/T-5000M |

Diese Paare sind **noch keine geprüften Balance-Paare**. Pro Tier dieselbe Zahl aktiver Auswahlplätze anbieten, Skins innerhalb eines Platzes ziehen. Munition, Rückstoß, Magazine, Frontschutz und Mobilität gemeinsam abstimmen. Sonderrollen (.338, AK-50, ASh-12, TKPD, KS-23, Launcher) bewusst bepreisen oder separat freigeben.

## Arena-eigenes Gear

Der Arena-Molotov (Template a12e00000000000000000001) entsteht dynamisch durch ArenaMode und fehlt in Basis-/WTT-Dateien. Vorschlag: Gemeinsam/neutral. Er ist oben nicht mitgezählt. Quelle: ArenaMode.Shared/ArenaMolotovItem.cs. Ein späterer Live-Export müsste solche dynamischen Einträge ergänzen.

## Reproduzieren und prüfen

Der Generator benötigt nur die Python-Standardbibliothek. Er liest die Installation und schreibt nur den gewählten Dokumentationsordner. Aufruf aus dem Repo:

    python .\docs\gear-inventory-20261026\generate.py --runtime D:\SPT41\SPT_Runtime --output .\docs\gear-inventory-20261026

Prüfungen: eindeutige IDs, keine WTT/Basis-ID-Überlappung, vollständig klassifizierte gezählte Waffen, konsistente Ein-/Ausschlussflags, unveränderte SHA-256-Hashes aller gelesenen Quelldateien zwischen Beginn und Ende. inventory.json enthält Hashes von Basis-Templates, Handbook, englischen Locales, WTT-CustomItems und WTT-DLL. Der Generator ist absichtlich an den geprüften Quellen-Fingerprint dieses datierten Berichts gebunden. Nach einem Inhaltsupdate bricht er vor dem Schreiben ab: Regeln und Interpretation müssen für eine neue Inventur erneut geprüft werden.

Kein Build oder Raidtest durchgeführt. Die Inventur beschreibt den Datensatz und Stilvorschlag, nicht derzeitige Arena-Kits oder nachgewiesene Spielstärke.
