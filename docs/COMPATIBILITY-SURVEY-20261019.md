# Forge Top 200: ArenaMode-Konfliktprüfung

Stand: 19.10.2026. ArenaMode **0.1.1**, Quellstand `11d0488b1b64855367e96fa5d54680475167da27`.

## Ergebnis und Geltungsbereich

200 Forge-Einträge wurden einzeln gesichtet; auffällige aktuelle Kandidaten wurden gegen gepinnte Autorquellen und Arenas eigene Lebenszyklus-/Inventar-/Profilpfade geprüft. Dies ist eine **Quellcode- und Dokumentationsprüfung, kein neuer Ingame-Kompatibilitätstest**. Es wurden keine Mods installiert, keine GPU-Sitzung benutzt, keine Sperren ergänzt und keine Laufzeitdateien verändert.

Am ehesten lohnen sich jetzt gezielte Integrationsprüfungen für **Continuous Healing, Classic Movement, Late To The Party und StatTrack**. Dazu kommen begrenzte Wiederanbindungsprobleme bei **HollywoodFX, Accurate Circular Radar, Audio Accessibility Indicators und Simple Crosshair**. **SVM** ist vor allem mit bestimmten Raid-End-Einstellungen relevant; **Deminvincibility** mit eingeschalteter Unsterblichkeit/Second Chance. Ein ähnlicher Modzweck oder derselbe Harmony-Einstieg allein beweist noch keinen Fehler.

Die Einstufung „Hohe Priorität“ bedeutet einen besonders relevanten, konkret begründeten Kandidaten. Sie ist **keine nachträglich aktivierte Startsperre** und keine Aussage, dass jede Konfiguration der Mod fehlschlägt. „Kein konkreter Hinweis“ bedeutet nur, dass die begrenzte Sichtung keinen spezifischen Konflikt ergeben hat, nicht „vollständig kompatibel“.

## Erhebung

- Forge-Gesamtdownloads, absteigend; Gleichstände nach Name, **einschließlich Legacy-Einträgen**. Die Platzierung ist weder aktuelle 4.1-Nutzung noch Downloadrate der letzten Wochen. Alte und neue separate Forge-Einträge derselben Mod bleiben getrennt.
- Snapshot am **19.10.2026, 19:19:55 UTC / 21:19:55 MESZ**, 200 eindeutige IDs aus damals 2553 Katalogeinträgen. Vier API-Seiten à 50 Einträge. Die vier Seiten sind kein atomarer Datenbank-Snapshot; Reihenfolge und Eindeutigkeit wurden nach dem Abruf geprüft.
- Öffentliche [Forge-API](https://sp-mod.com/docs/index.html), Abfrage `sort=-downloads,name`, `filter[include_legacy]=true`, `include=versions,category,source_code_links`, `per_page=50`, Seiten 1–4. URLs, Rohantwort-Hashes und die eingefrorenen Downloadwerte stehen in der [Snapshotdatei](compatibility/forge-top200-snapshot-20261019.json).
- SPT-Angaben stammen aus den Autor-/Versionsmetadaten. Wo Quellstand und veröffentlichtes Paket voneinander abweichen oder ein Tag unklar ist, nennt der Einzelbefund diese Grenze. Ein Fika-Badge ist kein Arena-Kompatibilitätsnachweis.
- Bei den unauffälligen/alten Einträgen erfolgte ein Metadaten-/Funktionsscreening, kein Vollreview jedes Quellcodes. Bei vertieften Kandidaten sind konkrete Quellpfade und feste Commits verlinkt; eine Übereinstimmung mit jedem ausgelieferten DLL-Byte wurde nicht behauptet.
- Maschinenlesbare Entscheidungen mit Details und Empfehlungen: [alle 200 Einzelbefunde](compatibility/forge-top200-20261019.json). Heruntergeladener Fremdquellcode wurde nur gelesen, nicht ausgeführt oder ins Produkt übernommen.

## Empfohlene Reihenfolge

| Gruppe | Was ist konkret relevant? | Sinnvoller nächster Schritt |
|---|---|---|
| Continuous Healing | Statisches CancelRequested wird vor IsYourPlayer ausgewertet und kann dadurch auch Bot-Heilungsoperationen beenden. | Exakte lokale Arena-Abgrenzung und Bot-Regression entwerfen; frühere Beobachtung nicht mit neuer nativer Reproduktion verwechseln. |
| Classic Movement | Ersetzt konkrete Bewegungsklassen, die DoubleJump/Bunnyhop über exakte Typen erkennen. | Vollständigen Bewegungsvertrag fremder Zustände prüfen, exakte Typprüfungen nicht blind lockern; bis dahin Anwenderhinweis. |
| Late To The Party | Eigene Loot-Bereinigung kann native LootItems der Arena-Waffenpickups entfernen. | Nur Arena-eigene Pickup-Identitäten ausnehmen; normalen Maploot und PMC-Raids unberührt lassen. |
| StatTrack | Unregister des lokalen Körpers beendet/speichert Raidstatistik in mod-eigener Datei; Arena unregistert auch beim Respawn. | Lebenswechsel und tatsächlichen Raidabschluss trennen; entscheiden, ob Arena-Statistiken separat oder gar nicht gezählt werden sollen. Kein behaupteter SPT-Profilverlust. |
| Continuous Load Ammo | Alt-Controller-Cleanup beim lebenden Kitwechsel und ein verzögerter 800-ms-Task können auf den falschen Lebenszyklus treffen. | Registrierung, Alt-Cleanup und Task-Abbruch über Kit-/Rundenwechsel prüfen; danach eng begrenzt absichern. |
| SVM / Deminvincibility | Bestimmte SaveLoot/Ignore-Raid- bzw. Unsterblichkeitsoptionen überspringen wichtige Abschluss-/Todpfade. | Nur betroffene Einstellungen in Arena abgrenzen; keine pauschale Sperre aller SVM-/Gesundheitsfunktionen. |
| HollywoodFX / Radar / Audio Indicators / Simple Crosshair | Alter Körper bleibt gebunden oder HUD-Teile werden beim Unregister nicht wieder aufgebaut. | Gezielte Neubindung nach erfolgreichem Respawn, danach zwei Respawns und Rückkehr in normalen Raid testen. |
| Pack n Strap | Hotkey-Bindbarkeit kann bei ungünstiger Patchreihenfolge überschrieben werden; ArmBand wird Inventarcontainer. | Kombitest ohne Standalone UseItemsAnywhere, Rucksack-Hotkeys und geschützter gefüllter Gürtel. Reachable erhält ein vorhandenes true und ist kein gleichartiger Override. |
| Amands Sense / Raid Review | Freigegebene Leichen bzw. lange Körperhistorien werden weiter referenziert/aufgezeichnet. | Langen DM-Lauf mit vielen Bot-Toden, Körperbereinigung und mehreren eigenen Respawns prüfen. |
| ABPS / Skills Extended / Weapon Customizer | Zusätzliche Dateien außerhalb des normalen SPT-Profil-Snapshots. | Zuerst tatsächliche Dateidifferenzen und Auslöser prüfen. ABPS-Chancenänderung und dauerhafter Skills-XP-Gewinn sind gerade nicht bewiesen; manuelle Waffen-Anpassung ist ein anderer Fall als automatischer Statistikexport. |

## Bereits vorhandene Grenzen

Die [Abhängigkeitsübersicht](DEPENDENCIES.md) und die [letzte kombinierte Prüfung](OPTIONAL-COMPAT-INTEGRATION-20261019.md) bleiben die Quelle für vorhandene Integrationen. SAIN, BigBrain, APBS, PitFireTeam und DynamicMaps besitzen ausdrückliche Adapter/Versions- und API-Verträge; das zertifiziert nicht jede beliebige Einstellung oder künftige Version. Für MoreBotsAPI und AI Limit sind die vorhandenen Absicherungen von noch offenen nativen Testfällen zu unterscheiden. Looting Bots, Remove The Dead und Smoothie wurden zuvor in der dokumentierten Kombination geprüft, nicht in sämtlichen denkbaren Modi und Optionen.

**Aktive Questing Bots und geladenes ORBIT sperren Arena bereits; Fika bleibt ebenfalls ausgeschlossen.** Normale PMC-/Scav-Raids sind von diesen Arena-Startprüfungen nicht betroffen. Questing Bots hat die gesondert geprüfte Ausnahme einer bereits beim Prozessstart vollständig serverseitig deaktivierten Mod. Dieses Audit erweitert die Liste nicht. Fika wird als bestehende Randbedingung genannt, nicht als zusätzlicher Eintrag in die Top-200-Zählung eingeschoben.

ABPS ist nicht APBS: APBS erzeugt Bot-Ausrüstung, ABPS steuert Platzierung/Wellen. Arenas vorhandene ABPS-Spawnkontrolle deckt nicht automatisch jede mod-eigene Statistikdatei ab. Umgekehrt beweist eine solche Datei noch keinen unerwünschten Inhalt: leere native Bosswellen und nicht getrackte Agro-Bossrollen sind hier wesentliche Gegenargumente.

Bei Black Division bleibt der native Komplettnachweis offen. Der Detailbefund nennt neben dem vorhandenen MoreBots-Schutz einen eigenen NVG-Patch als zusätzlichen Prüfpunkt. UNTAR Go Home verwendet für den untersuchten Rachepfad ebenfalls MoreBots; daraus folgt nicht automatisch ein zweiter unabhängiger Persistenzfehler.

## Prüfplan für spätere Integration

1. Pro Kandidat exakte veröffentlichte Version und relevante Einstellungen sichern, nur eine Kombination zur Zeit. Ein Testprofil verwenden und SPT-Profil sowie mod-eigene Dateien vor/nachher vergleichen.
2. **Kontrolle normaler PMC-Raid → Arena → normaler PMC-Raid** im selben Prozess. Keine globale Konfigurationsänderung als dauerhafte Arena-Lösung.
3. Arena mindestens mit Start, lokalem Tod, zwei Respawns, Killcam, Give-up und echtem Raid-Ende prüfen. Bei PFT außerdem überlebenden Spieler und wiederhergestellten Spieler über eine Rundengrenze testen.
4. Loot-Cleaner: stehen gelassene Waffenpickups, tatsächlich eingesammelte Waffen, Rotation und Cleanup getrennt prüfen. Nicht nur eine Powerup-Leuchtsäule ansehen.
5. Inventar: Rucksack-Magazine/Granaten, G/Quickslots, gefüllter Belt/ArmBand, Verbrauch geschützter Items und Own-Gear-Restore. Niedriges Kit-/Profilrisiko nicht aus einem erfolgreichen normalen Respawn ableiten.
6. Statistik/Progression: tatsächliche Inhalte statt bloßer Dateizeitstempel vergleichen. Gewollte Raid-Aufzeichnungen sind kein Profil-Leak; ungewollte Fortschrittsänderung muss eigenständig belegt werden.

## Einstufungen

- **Hohe Priorität:** 5
- **Gezielt prüfen:** 17
- **Überschneidung:** 22
- **Bereits behandelt:** 12
- **Bereits gesperrt:** 3
- **Kein konkreter Hinweis:** 51
- **Älterer Eintrag:** 83
- **Außerhalb Raid/Katalog:** 7

**49 vertiefte Prüfungen** (einschließlich gezielter Gegenprüfungen bestehender Integrationen); insgesamt 200 Einzelentscheidungen.

## Vollständige Rangliste

Downloadzahlen und letzte Versionsangaben sind die eingefrorenen Werte des Abrufs. Die vollständigen Einschränkungen und Quellen stehen in den Detailbefunden/JSON.

| Rang | Mod | Downloads | Letzte Version / SPT-Angabe | Bewertung und Kurzgrund |
|---:|---|---:|---|---|
| 1 | [BigBrain](https://sp-mod.com/mod/902/bigbrain) | 1,372,854 | 1.5.0 / `~4.1.0` | **Bereits behandelt** – Optionale Arena-BigBrain-Integration und API-Prüfung vorhanden; kein neuer Konflikt. |
| 2 | [Waypoints - Expanded Navmesh](https://sp-mod.com/mod/827/waypoints-expanded-navmesh) | 1,344,892 | 1.9.0 / `~4.1.0` | **Bereits behandelt** – NavMesh-Erweiterung ist Teil der bisher beobachteten Arena-Umgebung; kein neuer Eingriff in Match-Besitz erkennbar. |
| 3 | [Server Value Modifier [SVM]](https://sp-mod.com/mod/236/server-value-modifier-svm) | 1,339,637 | 2.2.3 / `~4.1.3` | **Gezielt prüfen** – SVM kann den äußeren Raid-End-Callback vor Arenas geschütztem Abschluss abfangen. |
| 4 | [SAIN - Solarint's AI Modifications - Full AI Combat System Replacement](https://sp-mod.com/mod/791/sain-solarints-ai-modifications-full-ai-combat-system-replacement) | 1,329,707 | 4.5.1 / `~4.1.3` | **Bereits behandelt** – SAIN ist ausdrücklich integriert: Persönlichkeit, Verfolgung, Teambeziehungen und Bewegungsbrücken. |
| 5 | [Amands's Graphics](https://sp-mod.com/mod/592/amandss-graphics) | 874,168 | 1.8.0 / `~4.1` | **Überschneidung** – Postprocessing überschneidet sich mit Arena-Beleuchtung und Zuschaueransicht; kein belegter Start-/Profilkonflikt. |
| 6 | [Dynamic Maps](https://sp-mod.com/mod/1431/dynamic-maps) | 818,302 | 1.2.1 / `~4.1` | **Bereits behandelt** – Arena berücksichtigt Leichenmarker, Teams und Kamerawechsel ausdrücklich. |
| 7 | [Looting Bots](https://sp-mod.com/mod/812/looting-bots) | 752,541 | 1.8.0 / `~4.1.3` | **Bereits behandelt** – Arena pausiert autonome Loot-Suche in der eigenen Welt; echte PFT-Kommandos bleiben separat behandelt. |
| 8 | [SPT-AKI Profile Editor](https://sp-mod.com/mod/106/spt-aki-profile-editor) | 743,059 | 4.0.1 / `~4.0.0` | **Außerhalb Raid/Katalog** – Externes Profilbearbeitungswerkzeug, kein gleichzeitig laufender Raid-Patch. |
| 9 | [WTT - Content Backport](https://sp-mod.com/mod/2512/wtt-content-backport) | 570,583 | 2.0.5 / `~4.1.6` | **Kein konkreter Hinweis** – Content-/Rig-Erweiterung: generierte Kits können neue Templates erben; kein spezifischer Arena-Fehler belegt. |
| 10 | [Game Panel HUD](https://sp-mod.com/mod/456/game-panel-hud) | 530,758 | 3.4.0 / `4.0.x` | **Älterer Eintrag** – HUD-Überlappung denkbar, aber neueste Forge-Version ist für SPT 4.0.x ausgewiesen. |
| 11 | [MoreCheckmarks](https://sp-mod.com/mod/861/morecheckmarks) | 492,122 | 2.2.0 / `~4.0 <4.1.0` | **Älterer Eintrag** – Neueste Forge-Version schließt SPT 4.1 aus; primär Inventarhinweise. |
| 12 | [UI Fixes](https://sp-mod.com/mod/1342/ui-fixes) | 463,274 | 6.0.4 / `~4.1` | **Bereits behandelt** – Alternativer UIFixes-R-Nachladepfad ist bereits in Arenas Nachladebeschleunigung berücksichtigt. |
| 13 | [SWAG + Donuts - Dynamic Spawn Waves and Custom Spawn Points](https://sp-mod.com/mod/644/swag-donuts-dynamic-spawn-waves-and-custom-spawn-points) | 456,672 | 4.0.0 / `~3.10.0` | **Älterer Eintrag** – Eigenständige Spawn-Regie wäre relevant, aktuelle gelistete Veröffentlichung ist jedoch SPT 3.10. |
| 14 | [SPT Realism Mod](https://sp-mod.com/mod/416/spt-realism-mod) | 452,357 | 1.6.4 / `~3.11.0` | **Älterer Eintrag** – Gesundheit, Ballistik und Bewegung wären umfangreiche Konfliktflächen; gelistet bis SPT 3.11. |
| 15 | [WTT - CommonLib](https://sp-mod.com/mod/2310/wtt-commonlib) | 449,134 | 3.0.6 / `~4.1.3` | **Kein konkreter Hinweis** – Bibliothek zur Registrierung von Inhalten, kein pauschaler eigener Arena-Raid-Regisseur. |
| 16 | [Questing Bots](https://sp-mod.com/mod/1109/questing-bots) | 439,996 | 1.1.0 / `~4.1.0` | **Bereits gesperrt** – Aktive Questing Bots sind bereits nur für Arena gesperrt. |
| 17 | [Kmy T***** Api](https://sp-mod.com/mod/898/kmy-tarkov-api) | 399,997 | 1.5.0 / `4.0.x` | **Älterer Eintrag** – Client-Framework; neuester Eintrag SPT 4.0.x. |
| 18 | [Virtual's Custom Quest Loader](https://sp-mod.com/mod/649/virtuals-custom-quest-loader) | 388,885 | 2.0.4 / `~3.11.0` | **Älterer Eintrag** – Quest-Loader für SPT 3.11; kein aktueller 4.1-Kandidat in diesem Eintrag. |
| 19 | [Fontaine's FOV Fix](https://sp-mod.com/mod/701/fontaines-fov-fix) | 355,540 | 4.1.0 / `4.1.x` | **Überschneidung** – FOV Fix ersetzt die lokale Waffen-Kamerainterpolation; visuelle Überschneidung mit Killcam/Akimbo. |
| 20 | [Skipper](https://sp-mod.com/mod/1343/skipper) | 347,149 | 1.1.5 / `~4.1` | **Kein konkreter Hinweis** – Manuelles Überspringen von Quests; keine automatische Spawn-/Respawn-Regie aus Autorbeschreibung. |
| 21 | [Kaeno-TraderScrolling](https://sp-mod.com/mod/1089/kaeno-traderscrolling) | 324,290 | 4.1.0 / `~4.1.0` | **Kein konkreter Hinweis** – Trader-Menü-Scrolling, keine erkennbare Arena-Raid-Überschneidung. |
| 22 | [WTT- Artem](https://sp-mod.com/mod/1023/wtt-artem) | 320,794 | 3.0.2 / `~4.1.0` | **Kein konkreter Hinweis** – Trader mit Quests und Items; allgemeine Custom-Item-Prüfung statt konkreter Arena-Kollision. |
| 23 | [Painter](https://sp-mod.com/mod/1025/painter) | 317,575 | 3.0.0 / `~4.1.5` | **Kein konkreter Hinweis** – Trader und lackierte Waffen-/Ausrüstungsvarianten. |
| 24 | [Ref - SPT Friendly Quests](https://sp-mod.com/mod/1538/ref-spt-friendly-quests) | 313,065 | 2.1.0 / `~4.1` | **Kein konkreter Hinweis** – Ändert Refs Questdefinitionen in SPT; keine eigene externe Fortschrittsspeicherung belegt. |
| 25 | [ACCURATE CIRCULAR RADAR](https://sp-mod.com/mod/1100/accurate-circular-radar) | 312,039 | 1.3.4 / `4.1.3` | **Gezielt prüfen** – Radar behält den ursprünglichen MainPlayer und aktualisiert ihn nach Körperwechsel nicht. |
| 26 | [Tactical Gear Component](https://sp-mod.com/mod/1125/tactical-gear-component) | 307,247 | 3.0.0 / `~4.1.5` | **Kein konkreter Hinweis** – Zusätzliche Ausrüstung und Anbauteile; kein spezifischer Lifecycle-Patch belegt. |
| 27 | [Live Flea Prices](https://sp-mod.com/mod/1131/live-flea-prices) | 296,990 | 2.1.0 / `~4.1.1` | **Kein konkreter Hinweis** – Aktualisiert Flohmarktpreise; kein relevanter Kampf-/Respawn-Eingriff aus Beschreibung. |
| 28 | [WTT- Armory](https://sp-mod.com/mod/2246/wtt-armory) | 295,967 | 3.0.0 / `~4.1.5` | **Kein konkreter Hinweis** – Waffenpaket; Templates können in zufällig generierte Kits eingehen. |
| 29 | [UnityToolkit](https://sp-mod.com/mod/1426/unitytoolkit) | 282,909 | 2.0.2 / `4.1.5` | **Kein konkreter Hinweis** – Unity-Hilfsbibliothek; abhängige Mods tragen die konkrete Spiellogik. |
| 30 | [Epic's All in One](https://sp-mod.com/mod/1263/epics-all-in-one) | 281,369 | 5.0.2 / `~4.1.6` | **Kein konkreter Hinweis** – Waffen/Teile und Trader; Content-Integration statt eigener Match-Regie. |
| 31 | [Borkel's Realistic Night Vision Goggles (NVGs and T-7)](https://sp-mod.com/mod/954/borkels-realistic-night-vision-goggles-nvgs-and-t-7) | 268,512 | 3.0.4 / `~4.1` | **Überschneidung** – NVG-Mod verändert sichtbare Laserhelligkeit; Arena bearbeitet ebenfalls die Laser-Darstellung. |
| 32 | [APBS - Acid's Progressive Bot System](https://sp-mod.com/mod/1594/apbs-acids-progressive-bot-system) | 259,612 | 2.4.1 / `~4.1.0` | **Bereits behandelt** – APBS-Generator und aktuelle API sind ausdrücklich eingebunden. |
| 33 | [Lots of Loot Redux](https://sp-mod.com/mod/1619/lots-of-loot-redux) | 255,409 | 4.2.2 / `~4.1` | **Überschneidung** – Mehr natürlicher Loot wird durch Arenas Loot-Reduktion größtenteils wirkungslos. |
| 34 | [Priscilu: the trader](https://sp-mod.com/mod/365/priscilu-the-trader) | 238,552 | 5.1.1 / `~3.11.0` | **Älterer Eintrag** – Trader-Eintrag für SPT 3.11, kein aktueller 4.1-Nachweis. |
| 35 | [WTT - Pack 'n' Strap](https://sp-mod.com/mod/1278/wtt-pack-n-strap) | 237,524 | 2.1.1 / `~4.1.5` | **Gezielt prüfen** – Pack n Strap überschreibt Hotkey-Bindbarkeit und verwendet ArmBand als Inventarcontainer. |
| 36 | [ALP - Algorithmic Level Progression](https://sp-mod.com/mod/1015/alp-algorithmic-level-progression) | 234,812 | 5.5.0+beta / `~3.11.0` | **Älterer Eintrag** – Alternativer Bot-Kitgenerator wäre APBS-/Kit-Thema, gelistete Fassung ist SPT 3.11. |
| 37 | [No Bush ESP](https://sp-mod.com/mod/665/no-bush-esp) | 229,080 | 1.9.0 / `~3.9.0` | **Älterer Eintrag** – Älterer KI-Sichtpatch bis SPT 3.9; heutige SAIN-Integration ist separat. |
| 38 | [Boss Notifier](https://sp-mod.com/mod/1247/boss-notifier) | 225,873 | 1.6.0 / `~3.11.0` | **Älterer Eintrag** – Alter Boss-Notifier für SPT 3.11; aktueller eigener Forge-Eintrag wird an anderer Rangstelle bewertet. |
| 39 | [HollywoodFX](https://sp-mod.com/mod/2003/hollywoodfx) | 224,437 | 3.0.0 / `~4` | **Gezielt prüfen** – HollywoodFX speichert MainPlayer nur beim Weltstart; Effekte lesen nach Respawn weiter den alten Körper. |
| 40 | [Bosses Have Lega Medals](https://sp-mod.com/mod/1539/bosses-have-lega-medals) | 218,042 | 2.1.0 / `~4.1` | **Kein konkreter Hinweis** – Ergänzt Boss-Pocket-Loot; kein unabhängiger Spawn-Regisseur. |
| 41 | [Ammo Stats](https://sp-mod.com/mod/167/ammo-stats) | 213,775 | 4.1.0 / `~4.1.0` | **Kein konkreter Hinweis** – Munitionseigenschaften in Itemanzeigen; kein Kampfbesitz aus Metadaten erkennbar. |
| 42 | [Display Official Version](https://sp-mod.com/mod/841/display-official-version) | 212,742 | 1.1.1 / `~3.8.0` | **Älterer Eintrag** – Versionsanzeige für SPT 3.8; kein aktueller Gameplay-Kandidat. |
| 43 | [That's Lit - Logical AI Vision](https://sp-mod.com/mod/1055/thats-lit-logical-ai-vision) | 210,618 | 1.3100.3 / `~3.10.0` | **Älterer Eintrag** – KI-Sichtberechnung wäre relevant, gelistete Fassung ist SPT 3.10. |
| 44 | [LootValue](https://sp-mod.com/mod/1155/lootvalue) | 209,677 | 5.0.2 / `~3.11.0` | **Älterer Eintrag** – Lootpreis-/Schnellverkauf-Mod für SPT 3.11. |
| 45 | [MOAR + Bagels - Ultra lite spawn mod](https://sp-mod.com/mod/789/moar-bagels-ultra-lite-spawn-mod) | 196,972 | 3.1.6 / `~3.11.0` | **Älterer Eintrag** – Eigene Spawn-Wellen und Despawns wären relevant, neueste gelistete Fassung ist SPT 3.11. |
| 46 | [All The Clothes](https://sp-mod.com/mod/526/all-the-clothes) | 195,733 | 3.2.0 / `~4.1.0` | **Kein konkreter Hinweis** – Schaltet Kleidung frei; kein erkennbarer Arena-Match-Eingriff. |
| 47 | [ABPS - Acid's Bot Placement System](https://sp-mod.com/mod/2097/abps-acids-bot-placement-system) | 194,059 | 2.1.0 / `~4.1` | **Gezielt prüfen** – ABPS-Spawns sind berücksichtigt; separate Boss-Chancen-Dateien liegen aber außerhalb des Arena-Snapshots. |
| 48 | [Lotus](https://sp-mod.com/mod/1232/lotus) | 191,148 | 1.8.1 / `~4.1.6` | **Kein konkreter Hinweis** – Trader/Quests/Items und Labs-Zugang; Arena besitzt eigene Karten-/Startfreigabe. |
| 49 | [Search Open Containers](https://sp-mod.com/mod/934/search-open-containers) | 189,156 | 1.5.0 / `~4.1.0` | **Überschneidung** – Öffnet Suche an schon offenen Containern; überschneidet sich funktional mit Instant Search. |
| 50 | [BlackCore](https://sp-mod.com/mod/985/blackcore) | 185,726 | 1.1.8 / `~3.11.0` | **Älterer Eintrag** – Waffen-/Ausrüstungsvarianten für SPT 3.11. |
| 51 | [Gilded Key Storage](https://sp-mod.com/mod/865/gilded-key-storage) | 176,148 | 2.1.1 / `~4.1.0` | **Kein konkreter Hinweis** – Schlüsselaufbewahrung und Fortschrittsfreischaltung; kein konkreter Arena-Lebenszykluseingriff belegt. |
| 52 | [Amands's Hitmarker](https://sp-mod.com/mod/577/amandss-hitmarker) | 173,350 | 2.6.1 / `~3.8.0` | **Älterer Eintrag** – Trefferanzeige überschneidet sich mit Arena-Hitmarker, der gelistete Stand richtet sich aber an SPT 3.8. |
| 53 | [The Blacklist - flea market enhancements](https://sp-mod.com/mod/755/the-blacklist-flea-market-enhancements) | 170,566 | 3.1.1 / `~4.1` | **Kein konkreter Hinweis** – Flea-Blacklist, Preise und Verkaufszeiten; kein konkreter Hinweis auf Arena-Respawn- oder Matchsteuerung. |
| 54 | [HandsAreNotBusy](https://sp-mod.com/mod/1298/handsarenotbusy) | 166,818 | 1.7.1 / `>=4.1.3` | **Gezielt prüfen** – Manueller Hände-Reset prüft weder den aktuellen lebenden Körper noch Arenas Wechselphase. |
| 55 | [Quest Tracker](https://sp-mod.com/mod/1140/quest-tracker) | 163,846 | 1.7.0 / `~4.1.0` | **Kein konkreter Hinweis** – Quest-Overlay; kein konkreter Konflikt mit Arenas Match- oder Profilbesitz aus dem Forge-Funktionsumfang. |
| 56 | [Fin's AI Tweaks (FAIT)](https://sp-mod.com/mod/39/fins-ai-tweaks-fait) | 156,121 | 1.25.2 / `~3.6.0` | **Älterer Eintrag** – Alter KI-Umbau ohne gelistete SPT-4.1-Fassung. |
| 57 | [bluehead's AIO Trader](https://sp-mod.com/mod/374/blueheads-aio-trader) | 154,500 | 5.3.1 / `~4.1.0` | **Kein konkreter Hinweis** – Zusätzlicher Händler und Schlüsselkommando; kein konkreter Arena-Konflikt im beschriebenen Umfang. |
| 58 | [Backdoor Bandit (BB)](https://sp-mod.com/mod/858/backdoor-bandit-bb) | 149,270 | 1.9.0 / `~3.9.0` | **Älterer Eintrag** – Tür-Breaching verändert Karteninteraktion, aber der Forge-Eintrag endet bei SPT 3.9. |
| 59 | [AES](https://sp-mod.com/mod/874/aes) | 147,413 | 0.7.9 / `~4.0.2` | **Älterer Eintrag** – Händler-, Quest- und Waffenpaket mit gelistetem SPT-4.0-Ziel. |
| 60 | [Continuous Healing](https://sp-mod.com/mod/1884/continuous-healing) | 144,311 | 1.6.3 / `>=4.1.3` | **Hohe Priorität** – Globaler Heilungsabbruch wirkt vor der lokalen Spielerprüfung auch auf Bot-Operationen. |
| 61 | [Classic Movement](https://sp-mod.com/mod/1860/classic-movement) | 142,149 | 1.1.3 / `~4.1.0` | **Hohe Priorität** – Ersetzt native Bewegungstypen, die Arena für Doppelsprung und Bunnyhop ausdrücklich voraussetzt. |
| 62 | [Scorpion](https://sp-mod.com/mod/1348/scorpion) | 142,057 | 1.1.1 / `~4.1` | **Kein konkreter Hinweis** – Händler und Questreihen; derzeit kein konkreter Eingriff in Arena-Matches belegt. |
| 63 | [Quick Move To Containers](https://sp-mod.com/mod/1341/quick-move-to-containers) | 141,642 | 1.5.0 / `~4.1.0` | **Kein konkreter Hinweis** – Manuelle Inventarverschiebung; keine belegte automatische Manipulation eines Arena-Körperwechsels. |
| 64 | [Less Restricting Headwear](https://sp-mod.com/mod/922/less-restricting-headwear) | 136,130 | 4.0.0 / `~4.1.0` | **Überschneidung** – Erweiterte Kopf-Ausrüstungsregeln verändern zulässige Kit-Kombinationen, ohne belegten Lifecyclefehler. |
| 65 | [Weapon Customizer](https://sp-mod.com/mod/1950/weapon-customizer) | 134,237 | 4.0.0 / `~4.1` | **Gezielt prüfen** – Optionale Waffenänderungen im Raid speichern außerhalb des geschützten SPT-Profils. |
| 66 | [MoreBotsAPI](https://sp-mod.com/mod/2426/morebotsapi) | 133,059 | 2.1.1 / `~4.1.5` | **Bereits behandelt** – MoreBotsAPI 2.1.1 besitzt bereits einen gezielten Arena-Adapter. |
| 67 | [BrightLasers](https://sp-mod.com/mod/1358/brightlasers) | 130,092 | 3.0.0 / `~4.1` | **Bereits behandelt** – BrightLasers 3.0.0 ist bei Arena-Helligkeit und Teamfarben bereits gezielt berücksichtigt. |
| 68 | [Show Me The Money (Item Pricing)](https://sp-mod.com/mod/2299/show-me-the-money-item-pricing) | 129,572 | 2.7.0 / `~4.0.11` | **Älterer Eintrag** – Preis-Tooltip mit gelisteter SPT-4.0-Fassung; keine aktuelle 4.1-Ausgabe im Snapshot. |
| 69 | [WTT - Menu Overhaul](https://sp-mod.com/mod/1775/wtt-menu-overhaul) | 128,772 | 1.3.0 / `~4.1` | **Kein konkreter Hinweis** – Vertiefung zeigt explizite Hauptmenü-Grenzen; kein konkreter Arena-Startkonflikt belegt. |
| 70 | [Item Info](https://sp-mod.com/mod/733/item-info) | 128,399 | 4.4.0 / `~3.10.0` | **Älterer Eintrag** – Historische Itembeschreibungs- und Farbmod ohne aktuelle 4.1-Version. |
| 71 | [Increase Climb Height](https://sp-mod.com/mod/1575/increase-climb-height) | 126,206 | 2.1.1 / `~4.1` | **Überschneidung** – Server-Vaultingwerte bilden die Basis für Arenas eigenen Reichweitenmultiplikator. |
| 72 | [SP-EFT Manager](https://sp-mod.com/mod/1219/sp-eft-manager) | 125,498 | 0.4.5 / `~4 <4.1.0` | **Außerhalb Raid/Katalog** – Externer Modmanager; kein zusätzlicher Laufzeit-Hook von Arena. |
| 73 | [Use Items Anywhere](https://sp-mod.com/mod/2386/use-items-anywhere) | 123,546 | 2.1.6 / `~4.1` | **Überschneidung** – Eigene Slotregeln, Granatenauswahl und Item-Verzögerungen zusätzlich zu Arenas eigener Reichweite. |
| 74 | [Gunsmith](https://sp-mod.com/mod/761/gunsmith) | 122,883 | 2.2.0 / `~3.10.0` | **Älterer Eintrag** – Alter Händler für Quest-Presets ohne gelistete 4.1-Version. |
| 75 | [WTT - Black Division [REDACTED] Home](https://sp-mod.com/mod/2511/wtt-black-division-redacted-home) | 122,814 | 1.3.1 / `~4.1.5` | **Gezielt prüfen** – Ein separater globaler NVG-Prefix betrifft normale Arena-Bots; MoreBots-Adapter deckt diesen Content-Hook nicht ab. |
| 76 | [Never Lose Equipments](https://sp-mod.com/mod/152/never-lose-equipments) | 119,753 | 6.3.0 / `~3.8.0` | **Älterer Eintrag** – Alter Ausrüstungsschutz würde Tod- und Profilregeln überschneiden, bietet aber nur SPT-3.8-Stände. |
| 77 | [Mag Tape](https://sp-mod.com/mod/1018/mag-tape) | 119,014 | 1.0.8 / `~3.11.0` | **Älterer Eintrag** – Alte Magazin-Contentmod ohne gelistete 4.1-Ausgabe. |
| 78 | [Loot Radius](https://sp-mod.com/mod/1349/loot-radius) | 117,822 | 1.4.1 / `~3.11.0` | **Älterer Eintrag** – Loot-Reichweitenmod; die gelistete Version richtet sich an SPT 3.11. |
| 79 | [Josh Mate's Better Backpacks](https://sp-mod.com/mod/552/josh-mates-better-backpacks) | 114,226 | 27.0.0 / `~3.10.0` | **Älterer Eintrag** – Alte Rucksackgrößenmod ohne aktuelle 4.1-Version im Katalog. |
| 80 | [Trader Modding And Improved Weapon Building](https://sp-mod.com/mod/1283/trader-modding-and-improved-weapon-building) | 113,171 | 2.1.2 / `~4.1` | **Kein konkreter Hinweis** – Waffenbau- und Händler-UI; kein konkreter Arena-Matchkonflikt aus der Vorprüfung. |
| 81 | [Color Converter API](https://sp-mod.com/mod/1090/color-converter-api) | 112,843 | 1.2.0 / `>=4.1.3` | **Überschneidung** – Farb-API überschneidet sich mit Item-Hintergründen und Tracerpräsentation. |
| 82 | [Use Loose Loot](https://sp-mod.com/mod/933/use-loose-loot) | 112,330 | 1.6.0 / `~4.1.0` | **Kein konkreter Hinweis** – Vertiefte Quelle betrifft echtes loses Essen und Medizin, nicht Arenas abstrakte Powerup-Marker. |
| 83 | [Rai's Hidden Caches](https://sp-mod.com/mod/1265/rais-hidden-caches) | 109,755 | 1.4.0 / `~4.1.0` | **Kein konkreter Hinweis** – Effektmarkierung verborgener Caches; kein konkreter Arena-Lifecycle-Konflikt im beschriebenen Umfang. |
| 84 | [AutoDeposit](https://sp-mod.com/mod/1469/autodeposit) | 105,760 | 6.0.0 / `~4.1` | **Kein konkreter Hinweis** – Manuelles Einsortieren in Stashbehälter; kein konkreter Arena-Raidkonflikt belegt. |
| 85 | [Saria Shop](https://sp-mod.com/mod/1085/saria-shop) | 105,359 | 1.8.0 / `4.0.11` | **Älterer Eintrag** – Händlermod für SPT 4.0.11; keine 4.1-Fassung im Snapshot. |
| 86 | [Project Fika](https://sp-mod.com/mod/2326/project-fika) | 101,216 | 2.4.6 / `>=4.1.3` | **Bereits gesperrt** – Fika ist bereits ausdrücklich nicht unterstützt und Arena weist den Start zurück. |
| 87 | [Player Encumbrance Bar](https://sp-mod.com/mod/1374/player-encumbrance-bar) | 101,172 | 1.3.0 / `>=4.1.0` | **Kein konkreter Hinweis** – Inventar-Gewichtsbalken; kein konkreter Konflikt mit Arenas Körper- oder Profilbesitz belegt. |
| 88 | [Continuous Load Ammo](https://sp-mod.com/mod/2112/continuous-load-ammo) | 99,288 | 1.2.0 / `~4.1.0` | **Gezielt prüfen** – Controllerwechsel und verzögerte Entsperrung sind nicht durchgängig an den aktuellen Ersatzkörper gebunden. |
| 89 | [Hephaestus](https://sp-mod.com/mod/650/hephaestus) | 97,591 | 2.0.2 / `~4.0.3` | **Älterer Eintrag** – Preset-Händler mit SPT-4.0-Ziel; aktuelle 4.1-Laufzeit nicht belegt. |
| 90 | [Pity Loot](https://sp-mod.com/mod/1258/pity-loot) | 97,441 | 2.1.1 / `~3.11.0` | **Älterer Eintrag** – Progressionsabhängige Lootmod für SPT 3.11 ohne gelistete 4.1-Ausgabe. |
| 91 | [VRAM Cleaner](https://sp-mod.com/mod/2173/vram-cleaner) | 95,123 | 1.1.0 / `4.1.6` | **Kein konkreter Hinweis** – Vertiefte Quelle zeigt einmalige GC-/Assetbereinigung am Raidstart; kein konkreter Besitzverlust belegt. |
| 92 | [Dad Gamer Mode](https://sp-mod.com/mod/686/dad-gamer-mode) | 95,055 | 1.9.3 / `~3.9.0` | **Älterer Eintrag** – Godmode-, Heil- und Fallschadensregeln würden Arena stark überschneiden; gelistete Ausgabe nur für SPT 3.9. |
| 93 | [WTT - Head and Voice Selector](https://sp-mod.com/mod/1277/wtt-head-and-voice-selector) | 94,093 | 1.0.8 / `~4.1.0` | **Kein konkreter Hinweis** – Kopf- und Stimmenauswahl im Overall-Panel; kein konkreter Arena-Konflikt im veröffentlichten Umfang. |
| 94 | [Fontaine's Always Level Endurance (ALE)](https://sp-mod.com/mod/697/fontaines-always-level-endurance-ale) | 93,986 | 1.7.0 / `~3.11.0` | **Älterer Eintrag** – Endurance-Skill-Regel der SPT-3.11-Generation. |
| 95 | [EFT SCOPE AND RED DOT SIGHT OVERHAUL BY GEFF HANNIGAN - Reupload](https://sp-mod.com/mod/974/eft-scope-and-red-dot-sight-overhaul-by-geff-hannigan-reupload) | 93,703 | 1.0.4 / `~3.9.0` | **Älterer Eintrag** – Historische Optik- und Reticle-Überarbeitung ohne aktuelle 4.1-Version. |
| 96 | [LOE (Load Order Editor)](https://sp-mod.com/mod/803/loe-load-order-editor) | 91,816 | 2.5.0 / `~3.11.0` | **Außerhalb Raid/Katalog** – Externer Server-Loadorder-Editor; keine laufende Arena-Spielkomponente. |
| 97 | [Healing Autocancel](https://sp-mod.com/mod/1274/healing-autocancel) | 91,272 | 5.0.1 / `4.1.6` | **Gezielt prüfen** – Verlinkte Quelle bindet Heilungs-Callbacks einmal am Raidstart; Arena ersetzt danach den lokalen Körper. |
| 98 | [Item Sell Price](https://sp-mod.com/mod/909/item-sell-price) | 89,837 | 1.7.0 / `~4.0.0` | **Älterer Eintrag** – Preis-Anzeigemod mit gelistetem SPT-4.0-Ziel. |
| 99 | [Task List Fixes](https://sp-mod.com/mod/824/task-list-fixes) | 89,594 | 1.8.0 / `~4.1.0` | **Kein konkreter Hinweis** – Aufgabenlisten-Sortierung und Anzeigeleistung; kein konkreter Arena-Konflikt aus der Vorprüfung. |
| 100 | [Borkel's Bloody Bullet Wounds + Particles + Splatters](https://sp-mod.com/mod/1250/borkels-bloody-bullet-wounds-particles-splatters) | 89,467 | 1.2.4 / `~3.10.0` | **Älterer Eintrag** – Ältere Blut- und Partikeleffekte ohne gelistete SPT-4.1-Version. |
| 101 | [MergeConsumables](https://sp-mod.com/mod/1657/mergeconsumables) | 89,002 | 1.6.3 / `>=4.1.3` | **Überschneidung** – Eigene Merge-Operationen für Meds/Food überschneiden Inventartransaktionen; keine nachgewiesene externe Persistenzlücke. |
| 102 | [Amands' Sense - Updated](https://sp-mod.com/mod/2521/amands-sense-updated) | 88,547 | 3.1.0 / `~4.1.0` | **Gezielt prüfen** – Gespeicherte Leichenreferenzen treffen auf Arenas physische Körperfreigabe. |
| 103 | [Climbable Ladders](https://sp-mod.com/mod/2649/climbable-ladders) | 84,688 | 1.0.4 / `~4.1.0` | **Gezielt prüfen** – Leitersteuerung und globale Vault/Climb-Overrides greifen in Arenas Bewegung ein. |
| 104 | [Lots of Loot](https://sp-mod.com/mod/492/lots-of-loot) | 80,427 | 2.2.2 / `~3.8.0` | **Älterer Eintrag** – Loot-Verteilung gehört zum alten SPT-3.8-Eintrag. |
| 105 | [Fast Sell In Flea](https://sp-mod.com/mod/2207/fast-sell-in-flea) | 79,489 | 1.3.1 / `~4.1` | **Kein konkreter Hinweis** – Flea-Angebotserstellung liegt außerhalb der Arena-Raidmechanik. |
| 106 | [Shadow Flicker Fix](https://sp-mod.com/mod/1621/shadow-flicker-fix) | 78,139 | 1.5.3 / `~4.1` | **Kein konkreter Hinweis** – Schatten-/Antialiasingkorrektur ohne belegten gemeinsamen Arena-Hook. |
| 107 | [CWX-MegaMod](https://sp-mod.com/mod/1454/cwx-megamod) | 76,956 | 4.0.1 / `4.0.*` | **Älterer Eintrag** – Sammelmod mit Wetter-/Bewegungsüberschneidungen, aber jüngster Katalogstand nur SPT 4.0. |
| 108 | [Keys In Loot (KIL)](https://sp-mod.com/mod/1248/keys-in-loot-kil) | 76,438 | 1.7.0 / `~3.11.0` | **Älterer Eintrag** – Schlüssel-Lootmod nur für SPT 3.11 belegt. |
| 109 | [AR Shoppe Unlimited](https://sp-mod.com/mod/144/ar-shoppe-unlimited) | 76,122 | 3.7.0 / `~3.7.0` | **Älterer Eintrag** – Historischer Händler-/Waffeninhalt für SPT 3.7. |
| 110 | [AI Limit](https://sp-mod.com/mod/572/ai-limit) | 75,786 | 1.8.0 / `~3.9.0` | **Älterer Eintrag** – Alter AI-Limit-Eintrag ID 572, nicht der aktuelle Adapterkandidat ID 1945. |
| 111 | [More Energy Drinks](https://sp-mod.com/mod/1688/more-energy-drinks) | 75,331 | 1.3.1 / `~4.1.0` | **Kein konkreter Hinweis** – Zusätzliche Getränke-/Buff-Templates ohne konkreten Arena-Eingriff. |
| 112 | [MANIMAL'S ICEBREAKER BACKPORT](https://sp-mod.com/mod/2866/manimals-icebreaker-backport) | 74,577 | 1.1.3 / `~4.1.5` | **Außerhalb Raid/Katalog** – Icebreaker ist eine zusätzliche Karte außerhalb des aktuellen Arena-Kartenkatalogs. |
| 113 | [give-ui](https://sp-mod.com/mod/1617/give-ui) | 72,546 | 5.0.1 / `~4.1.0` | **Außerhalb Raid/Katalog** – Manuelles Give-/Profilwerkzeug, keine automatische Arena-Integration. |
| 114 | [Simple Workout QTE](https://sp-mod.com/mod/1437/simple-workout-qte) | 70,055 | 2.2.2 / `~4.1.3` | **Kein konkreter Hinweis** – Hideout-Workout-QTE außerhalb des Arena-Raids. |
| 115 | [Bot Callsigns - Reloaded](https://sp-mod.com/mod/1873/bot-callsigns-reloaded) | 70,045 | 2.0.5 / `~4.1.1` | **Kein konkreter Hinweis** – Botnamen sind Anzeige-/Generierungsdaten, Arena bindet Körper über Identitäten. |
| 116 | [Open Sesame](https://sp-mod.com/mod/1184/open-sesame) | 69,944 | 3.0.1 / `~4.1.0` | **Überschneidung** – Manuelles Türöffnen kann den von Arena verwendeten Kartenraum verändern. |
| 117 | [Brighter Interiors](https://sp-mod.com/mod/2613/brighter-interiors) | 69,881 | 1.1.1 / `4.1.*` | **Überschneidung** – Globale Innenraum-Schattendeckkraft verändert Arenas visuelle Lesbarkeit. |
| 118 | [HarryHideout](https://sp-mod.com/mod/1303/harryhideout) | 69,090 | 3.1.2 / `~4.1` | **Kein konkreter Hinweis** – Hideout-Baumaterialhändler ohne konkreten Arena-Laufzeitpfad. |
| 119 | [TM151-BossEvents](https://sp-mod.com/mod/202/tm151-bossevents) | 68,503 | 3.0.0 / `nicht angegeben` | **Älterer Eintrag** – Historischer Boss-Event-Eintrag von 2021 ohne SPT-4.1-Versionsangabe. |
| 120 | [Modding Stats Helper - by wara](https://sp-mod.com/mod/1300/modding-stats-helper-by-wara) | 67,461 | 1.2.0 / `~4.1` | **Kein konkreter Hinweis** – Statistiktooltips im Waffenbau, kein belegter Match-/Körperhook. |
| 121 | [PAUSE](https://sp-mod.com/mod/2046/pause) | 67,097 | 1.4.0 / `~4.0.0` | **Älterer Eintrag** – Raidpause wäre zeitkritisch, veröffentlichter Eintrag aber nur SPT 4.0. |
| 122 | [Time & Weather Changer](https://sp-mod.com/mod/318/time-weather-changer) | 67,047 | 2.3.1 / `~3.6.0` | **Älterer Eintrag** – Alter Zeit-/Wetter-GUI-Eintrag für SPT 3.6. |
| 123 | [Ai Limit](https://sp-mod.com/mod/1945/ai-limit) | 66,002 | 1.9.2 / `~4.1` | **Bereits behandelt** – Aktueller AI-Limit-Vertrag wird bereits gezielt in Arena pausiert. |
| 124 | [Lua's Spawn Rework](https://sp-mod.com/mod/171/luas-spawn-rework) | 65,369 | 2.1.8 / `~3.3.0` | **Älterer Eintrag** – Lua Spawn Rework ist ein alter SPT-3.3-Spawner. |
| 125 | [ATLAS Custom Weapons & Equipment](https://sp-mod.com/mod/764/atlas-custom-weapons-equipment) | 64,742 | 1.3.0 / `~3.10.0` | **Älterer Eintrag** – ATLAS-Inhalt ist im Katalog nur bis SPT 3.10 ausgewiesen. |
| 126 | [All Quests Checkmarks](https://sp-mod.com/mod/2025/all-quests-checkmarks) | 64,141 | 1.4.0 / `~4.1` | **Kein konkreter Hinweis** – Quest-Item-Markierungen ohne konkreten Arena-Laufzeitkonflikt. |
| 127 | [Content Backport - Prestiges](https://sp-mod.com/mod/2540/content-backport-prestiges) | 62,784 | 1.1.0 / `~4.1` | **Kein konkreter Hinweis** – Prestige-Inhalt verändert Profiloptionen außerhalb des Arena-Matches. |
| 128 | [Borkel's Big Realistic Thermal Package - Bring real life realism to your thermal scopes](https://sp-mod.com/mod/1091/borkels-big-realistic-thermal-package-bring-real-life-realism-to-your-thermal-scopes) | 62,750 | 1.0.4 / `~3.9.0` | **Älterer Eintrag** – Borkels Thermikpaket ist nur für SPT 3.9 belegt. |
| 129 | [Enable Labyrinth](https://sp-mod.com/mod/2416/enable-labyrinth) | 62,444 | 1.1.0 / `~4.1` | **Bereits behandelt** – Labyrinth-Zugriff/Exit-Überlappung ist im Arena-Code ausdrücklich abgegrenzt. |
| 130 | [Better Keys NG](https://sp-mod.com/mod/1888/better-keys-ng) | 62,391 | 1.8.0 / `~4.1` | **Kein konkreter Hinweis** – Schlüsselbeschreibungen sind Informationsanzeige. |
| 131 | [Deminvincibility](https://sp-mod.com/mod/1117/deminvincibility) | 61,656 | 2.2.0 / `~4.1.3` | **Hohe Priorität** – Aktive Unsterblichkeits-/Second-Chance-Optionen verhindern Arenas Tod-/Respawn-Vertrag. |
| 132 | [Realistic Thermal Scopes](https://sp-mod.com/mod/2056/realistic-thermal-scopes) | 61,147 | 2.0.0 / `~4.0.0` | **Älterer Eintrag** – Realistic Thermal Scopes 2.0.0 ist nur SPT 4.0. |
| 133 | [Better Spawns Plus](https://sp-mod.com/mod/746/better-spawns-plus) | 60,627 | 2.0.5 / `~3.9.0` | **Älterer Eintrag** – Better Spawns Plus ist ein SPT-3.9-Spawner. |
| 134 | [Simple Crosshair](https://sp-mod.com/mod/1387/simple-crosshair) | 59,986 | 1.4.0 / `~4.1.0` | **Gezielt prüfen** – Fadenkreuz wird bei Spieler-Unregister entfernt und nur bei BattleUI.Show neu erstellt. |
| 135 | [Skills Extended](https://sp-mod.com/mod/2383/skills-extended) | 59,863 | 3.1.4 / `~4.1` | **Gezielt prüfen** – Skills, Bewegungsregeln und ein separates Signal-XP-Journal liegen an mehreren Arena-Grenzen. |
| 136 | [Kiki-AllTheBoss](https://sp-mod.com/mod/178/kiki-alltheboss) | 59,820 | 2.10.0 / `~3.7.0` | **Älterer Eintrag** – Kiki-AllTheBoss ist nur für SPT 3.7 belegt. |
| 137 | [FU Inertia](https://sp-mod.com/mod/613/fu-inertia) | 59,576 | 2.2.1 / `~3.7.0` | **Älterer Eintrag** – FU Inertia ist ein alter SPT-3.7-Bewegungsmod. |
| 138 | [FAUPI - HIDEOUT ARCHITECT](https://sp-mod.com/mod/367/faupi-hideout-architect) | 59,412 | 1.7.1 / `nicht angegeben` | **Älterer Eintrag** – Historische Hideout-Markierungen ohne aktuelle Runtime-Angabe. |
| 139 | [Audio Accessibility Indicators](https://sp-mod.com/mod/1760/audio-accessibility-indicators) | 59,361 | 2.1.0 / `~4.1` | **Gezielt prüfen** – Beim Arena-Körperwechsel kann die Richtungsrotation der Audioanzeige dauerhaft stoppen. |
| 140 | [MaidTraiders](https://sp-mod.com/mod/445/maidtraiders) | 58,083 | 1.3.0 / `~3.7.0` | **Älterer Eintrag** – Historisches Händlerpaket für SPT 3.7. |
| 141 | [ORBIT 2.0](https://sp-mod.com/mod/2706/orbit-20) | 57,986 | 2.1.1 / `~4.1.6` | **Bereits gesperrt** – Geladenes ORBIT sperrt Arena bereits ausdrücklich. |
| 142 | [Hideout In Progress](https://sp-mod.com/mod/2076/hideout-in-progress) | 57,828 | 3.0.0 / `~4.1` | **Kein konkreter Hinweis** – Schrittweise Hideout-Abgaben sind außerhalb des Arena-Matches. |
| 143 | [Raid Review](https://sp-mod.com/mod/1479/raid-review) | 57,508 | 1.8.0 / `~4.1.6` | **Gezielt prüfen** – Wachsende Körperhistorie und ProfileId-basierte Aufzeichnung benötigen Arena-Langzeitprüfung. |
| 144 | [BossNotifier ](https://sp-mod.com/mod/2543/bossnotifier) | 57,244 | 1.2.1 / `4.1.3` | **Überschneidung** – Boss-Spawnmeldungen können Arena-Boss-/Wellenanzeigen doppeln. |
| 145 | [SamSWAT's Fire Support - Arys Reloaded](https://sp-mod.com/mod/1180/samswats-fire-support-arys-reloaded) | 56,846 | 2.3.4 / `~4.0.3` | **Älterer Eintrag** – Feuerunterstützung wäre schadensrelevant, vorliegender Stand nur SPT 4.0.3. |
| 146 | [Better Rear Sights](https://sp-mod.com/mod/1591/better-rear-sights) | 56,748 | 1.8.0 / `~4.1.5` | **Kein konkreter Hinweis** – Visierdarstellung ohne konkreten Arena-Lifecycle-/Profilhook. |
| 147 | [BarterItemsStacks](https://sp-mod.com/mod/2480/barteritemsstacks) | 56,539 | 1.4.3 / `~4.1.0` | **Kein konkreter Hinweis** – Barter-Stapelgrößen betreffen nicht automatisch Arenas private Pickups. |
| 148 | [Medical SICC case (MICC)](https://sp-mod.com/mod/158/medical-sicc-case-micc) | 55,769 | 3.9.3 / `~3.8.0` | **Älterer Eintrag** – Medical SICC case ist nur für SPT 3.8 ausgewiesen. |
| 149 | [ALL AMMO TRADER - UPDATED BY TILTUSHKIN](https://sp-mod.com/mod/1554/all-ammo-trader-updated-by-tiltushkin) | 55,565 | 1.5.0 / `~4.1.3` | **Kein konkreter Hinweis** – Munitionshändler ohne automatisch veränderte Arena-Kits. |
| 150 | [Lua's Flea Market Price Updater](https://sp-mod.com/mod/216/luas-flea-market-price-updater) | 55,163 | 3.1.2 / `~3.6.0` | **Älterer Eintrag** – Alter Flea-Preisupdater für SPT 3.6. |
| 151 | [WTT - W.A.A.C. (Women's Advanced Assault Corps)](https://sp-mod.com/mod/1519/wtt-waac-womens-advanced-assault-corps) | 54,851 | 2.1.0 / `~4.1.6` | **Kein konkreter Hinweis** – Kleidungs-, Kopf- und Stimmenpaket ohne konkreten Hinweis auf einen Arena-Match-/Profilschutzkonflikt. |
| 152 | [SandCore](https://sp-mod.com/mod/999/sandcore) | 54,058 | 1.0.5 / `~3.10.0` | **Älterer Eintrag** – SandCore 1.0.5 ist ein SPT-3.10-Retexturpaket. |
| 153 | [Headshot Darkness](https://sp-mod.com/mod/1582/headshot-darkness) | 53,728 | 1.2.5 / `~4.1` | **Gezielt prüfen** – Todeseffekte binden einen HealthController; Arena ersetzt den lokalen Körper innerhalb desselben Raids. |
| 154 | [SamSWAT's Helicopter Crash Sites - Arys Reloaded](https://sp-mod.com/mod/1292/samswats-helicopter-crash-sites-arys-reloaded) | 53,654 | 2.5.2 / `~4.0.3` | **Älterer Eintrag** – Arys Helicopter Crash Sites 2.5.2 ist nur für SPT 4.0 gelistet. |
| 155 | [Freecam](https://sp-mod.com/mod/164/freecam) | 53,424 | 1.4.8 / `~4.1` | **Bereits behandelt** – Arena erkennt den Terkoiz-Freecam-Kamerabesitz bereits gezielt. |
| 156 | [Lock Picking Attorney Redux](https://sp-mod.com/mod/160/lock-picking-attorney-redux) | 53,258 | 5.0.0 / `~3.8.0` | **Älterer Eintrag** – Schlüsselhändler für SPT 3.8, kein 4.1-Paket. |
| 157 | [Andern](https://sp-mod.com/mod/1064/andern) | 53,129 | 3.2.0 / `~4.0.13` | **Älterer Eintrag** – Andern 3.2.0 zielt auf SPT 4.0.13. |
| 158 | [AKGuy Super Shop](https://sp-mod.com/mod/252/akguy-super-shop) | 53,020 | 3.7.0 / `~3.7.0` | **Älterer Eintrag** – AK-Händler für SPT 3.7, kein aktueller 4.1-Release. |
| 159 | [Props Obviously Original Program (POOP)](https://sp-mod.com/mod/632/props-obviously-original-program-poop) | 53,019 | 1.5.3 / `~3.6.0` | **Älterer Eintrag** – POOP ist eine alte 3.x-KI-Konfiguration. |
| 160 | [GTFO](https://sp-mod.com/mod/1246/gtfo) | 52,825 | 1.2.1 / `~3.9.0` | **Älterer Eintrag** – GTFO 1.2.1 ist nur für SPT 3.9 gelistet. |
| 161 | [Softcore (Proper Singleplayer Experience for SPT)](https://sp-mod.com/mod/742/softcore-proper-singleplayer-experience-for-spt) | 52,409 | 3.2.1 / `~3.10.0` | **Älterer Eintrag** – Softcore 3.2.1 ist ein SPT-3.10-Overhaul. |
| 162 | [Two Slot Extended Mags](https://sp-mod.com/mod/917/two-slot-extended-mags) | 52,315 | 1.0.5+b / `~3.11.0` | **Älterer Eintrag** – Two Slot Extended Mags ist nur für SPT 3.11 gelistet. |
| 163 | [Bigger Keytool](https://sp-mod.com/mod/150/bigger-keytool) | 52,268 | 2.6.4 / `~3.8.0` | **Älterer Eintrag** – Bigger Keytool 2.6.4 zielt auf SPT 3.8. |
| 164 | [Take A Break](https://sp-mod.com/mod/471/take-a-break) | 51,743 | 2.0.1 / `~3.6.0` | **Älterer Eintrag** – Take A Break ist ein altes Pause-Plugin ohne 4.1-Freigabe. |
| 165 | [Camo And Stickers](https://sp-mod.com/mod/2658/camo-and-stickers) | 51,710 | 1.19.0 / `~4.1.5` | **Überschneidung** – Material-/Decal-Poolhooks treffen auch Arena-Waffenmodelle; kein belegter destruktiver Konflikt. |
| 166 | [Make Shotguns Great Again!](https://sp-mod.com/mod/1453/make-shotguns-great-again) | 51,541 | 1.18.1 / `~4.1.6` | **Überschneidung** – Globale Waffen-/Munitionswerte und Schusshooks wirken auch bei Arena-Kits. |
| 167 | [ODT's Item Info - SPT 4.0](https://sp-mod.com/mod/2430/odts-item-info-spt-40) | 51,413 | 3.0.0 / `~4.1.5` | **Kein konkreter Hinweis** – Item-Informationsanzeige ohne konkreten Arena-Konflikthinweis. |
| 168 | [Couturier - gear and clothing pack](https://sp-mod.com/mod/2239/couturier-gear-and-clothing-pack) | 50,530 | 2.0.4 / `~4.1.2` | **Kein konkreter Hinweis** – Bekleidungs-/Ausrüstungs- und Händlerpaket ohne identifizierten Arena-Lebenszykluseingriff. |
| 169 | [Door Randomizer](https://sp-mod.com/mod/820/door-randomizer) | 49,918 | 1.8.0 / `~4.1.0` | **Überschneidung** – Einmalige Randomisierung gewöhnlicher Türen verändert die Anfangsgeometrie. |
| 170 | [Amands' Sense](https://sp-mod.com/mod/993/amands-sense) | 49,872 | 1.1.0 / `~3.7.0` | **Älterer Eintrag** – Amands Sense 1.1.0 ist ein SPT-3.7-Plugin. |
| 171 | [Gambler - Trader](https://sp-mod.com/mod/1456/gambler-trader) | 49,335 | 1.0.1 / `4.0.13` | **Älterer Eintrag** – Gambler 1.0.1 ist ausdrücklich für SPT 4.0.x/4.0.13. |
| 172 | [Foldables](https://sp-mod.com/mod/2422/foldables) | 49,013 | 1.1.1 / `~4.1.2` | **Überschneidung** – Faltbare Container verändern Inventarplatzsuche und verzögerte Itemoperationen. |
| 173 | [TechHappy's Web Minimap](https://sp-mod.com/mod/1029/techhappys-web-minimap) | 48,664 | 1.0.9 / `~3.8.0` | **Älterer Eintrag** – Web-Minimap 1.0.9 zielt auf SPT 3.8. |
| 174 | [KMC-CORE](https://sp-mod.com/mod/64/kmc-core) | 48,345 | 3.0.2 / `~3.6.0` | **Älterer Eintrag** – KMC-CORE ist eine alte 3.x-Bibliothek. |
| 175 | [Custom Interactions](https://sp-mod.com/mod/938/custom-interactions) | 48,196 | 1.8.0 / `~4.0.0` | **Älterer Eintrag** – Custom Interactions 1.8.0 ist für SPT 4.0 kompiliert. |
| 176 | [JustNU Core](https://sp-mod.com/mod/235/justnu-core) | 48,118 | 1.1.0+aki-35x / `~3.7.0` | **Älterer Eintrag** – JustNU Core ist nur für alte 3.x-Versionen gelistet. |
| 177 | [FAUPI - MUNITIONS EXPERT](https://sp-mod.com/mod/372/faupi-munitions-expert) | 47,900 | 1.7.1 / `nicht angegeben` | **Älterer Eintrag** – FAUPI MUNITIONS EXPERT ist Legacy; Autorenhinweis nennt SPT 3.5.2. |
| 178 | [SOCOM Armory + New Quests & Story Line](https://sp-mod.com/mod/1835/socom-armory-new-quests-story-line) | 47,874 | 3.0.4 / `4.0.13` | **Älterer Eintrag** – SOCOM 3.0.4 ist ausdrücklich nur SPT 4.0.13. |
| 179 | [Late to the Party](https://sp-mod.com/mod/814/late-to-the-party) | 47,661 | 3.1.1 / `~4.1.0` | **Hohe Priorität** – Automatische Loot-Löschung kann Arena-eigene native Waffen-Pickups konsumieren. |
| 180 | [Health Per Level](https://sp-mod.com/mod/1706/health-per-level) | 47,607 | 2.2.0 / `4.1.*` | **Überschneidung** – Levelabhängige HP ändern Balance und Respawn-Maxima; kein belegter Snapshot-Bypass. |
| 181 | [StatTrack](https://sp-mod.com/mod/1853/stattrack) | 47,010 | 2.1.1 / `~4.1` | **Hohe Priorität** – StatTrack speichert Arena-Kills über eigene Persistenz außerhalb des Profil-Snapshots. |
| 182 | [Wolfik's Heavy Trooper Masks](https://sp-mod.com/mod/1569/wolfiks-heavy-trooper-masks) | 46,588 | 1.2.0 / `~4.1` | **Kein konkreter Hinweis** – Neue Schutzmasken/Farbvarianten ohne konkreten Arena-Konflikthinweis. |
| 183 | [Bosses Have Gp Coins](https://sp-mod.com/mod/2523/bosses-have-gp-coins) | 46,448 | 1.1.0 / `~4.1` | **Kein konkreter Hinweis** – Zusätzliche GP-Coins im Bossloot ohne erkannten Arena-Steuerungskonflikt. |
| 184 | [Project Fika - Server](https://sp-mod.com/mod/2357/project-fika-server) | 46,413 | 2.4.2 / `>=4.1.3` | **Außerhalb Raid/Katalog** – Nur zusammen mit geladenem Fika-Client bereits gesperrt; eine Startsperre allein durch die reine Server-DLL ist nicht nachgewiesen. |
| 185 | [C11 - True North](https://sp-mod.com/mod/2423/c11-true-north) | 46,191 | 4.0.29 / `~4.1.6` | **Überschneidung** – Custom-Waffen, NVG-Masken und Animatoränderungen berühren Kamera-/Waffenwechsel. |
| 186 | [Priscilu Special Weapons](https://sp-mod.com/mod/609/priscilu-special-weapons) | 46,101 | 3.1.0 / `~3.11.0` | **Älterer Eintrag** – Priscilu 3.1.0 ist für SPT 3.11. |
| 187 | [Fershte's Balance Patch](https://sp-mod.com/mod/122/fershtes-balance-patch) | 45,832 | 3.7.0 / `~3.7.0` | **Älterer Eintrag** – Fershtes Balance Patch 3.7.0 ist ein altes SPT-3.7-Overhaul. |
| 188 | [De-Clutterer](https://sp-mod.com/mod/1118/de-clutterer) | 45,777 | 1.1.5 / `~3.7.0` | **Älterer Eintrag** – De-Clutterer 1.1.5 ist für SPT 3.7. |
| 189 | [Legs the Trader](https://sp-mod.com/mod/1404/legs-the-trader) | 45,737 | 2.1.2 / `~4 <4.1.0` | **Älterer Eintrag** – Legs 2.1.2 schließt 4.1 ausdrücklich aus (~4 <4.1.0). |
| 190 | [Welcome Gifts](https://sp-mod.com/mod/740/welcome-gifts) | 45,319 | 4.0.0 / `4.0.12` | **Älterer Eintrag** – Welcome Gifts 4.0.0 zielt auf SPT 4.0.12. |
| 191 | [Kawaii Weapon Mods](https://sp-mod.com/mod/266/kawaii-weapon-mods) | 44,666 | 3.6.1 / `~3.9.0` | **Älterer Eintrag** – Kawaii Weapon Mods ist ein altes 3.x-Inhaltspaket. |
| 192 | [SPT Mods Installer](https://sp-mod.com/mod/1252/spt-mods-installer) | 43,965 | 2.1.0 / `~4.1.3` | **Außerhalb Raid/Katalog** – Eigenständiges Installationswerkzeug, kein Raid-Plugin. |
| 193 | [NerfBotGrenades](https://sp-mod.com/mod/1925/nerfbotgrenades) | 43,791 | 1.3.1 / `>=4.1.3` | **Überschneidung** – Wurfrichtungsstreuung aller Bots verändert auch Arena-Kampfverhalten. |
| 194 | [Definitive Weapon Variants](https://sp-mod.com/mod/2010/definitive-weapon-variants) | 43,762 | 4.3.2 / `~4.1.2` | **Überschneidung** – Neue Waffen-/Munitionstemplates erweitern den Datenbestand für Arena-Varianten. |
| 195 | [QuickSellFlea](https://sp-mod.com/mod/2407/quicksellflea) | 43,606 | 1.4.5 / `>=4.1.3` | **Kein konkreter Hinweis** – Flohmarkt-Verkauf außerhalb des Arena-Kampfs ohne konkreten Konflikthinweis. |
| 196 | [Helicopter Crash Sites](https://sp-mod.com/mod/463/helicopter-crash-sites) | 43,341 | 2.1.0 / `~3.6.0` | **Älterer Eintrag** – Original Helicopter Crash Sites ist ein separater alter 3.x-Eintrag. |
| 197 | [Munitions Expert (Reboot)](https://sp-mod.com/mod/972/munitions-expert-reboot) | 43,160 | 1.7.0 / `~4.0.0` | **Älterer Eintrag** – Munitions Expert Reboot 1.7.0 ist für SPT 4.0. |
| 198 | [UNTAR Go Home!](https://sp-mod.com/mod/2342/untar-go-home) | 43,131 | 3.2.1 / `~4.1.5` | **Überschneidung** – UNTAR nutzt MoreBots-Revenge/Fraktionen und eigene Spawnkonfiguration; Arena hat bereits relevante API-Schutzmaßnahmen. |
| 199 | [ZeroTimes](https://sp-mod.com/mod/100/zerotimes) | 43,035 | 2.0.0 / `~3.3.0` | **Älterer Eintrag** – ZeroTimes ist ein alter 3.x-/Legacy-Zeitmod. |
| 200 | [WTT - Armory - HK417](https://sp-mod.com/mod/1432/wtt-armory-hk417) | 42,998 | 1.1.7 / `~3.10.0` | **Älterer Eintrag** – WTT HK417 1.1.7 ist nur für SPT 3.10 gelistet. |

## Vertiefte Einzelbefunde

Die Empfehlungen sind Vorbereitung möglicher Folgearbeiten, keine in dieser Prüfung umgesetzten Änderungen.

### 3. Server Value Modifier [SVM]

**Gezielt prüfen** · 2.2.3; Source 5d8c5cfab332c98fbfd6d0300be4be5a4a19ecad

SVM 2.2.3 LocalRaidPatch.Prefix hängt an MatchCallbacks.EndLocalRaidAsync. SaveLoot + EnableRaids + EnableRaidStartup oder ein per DefineRaidStatus auf TRANSIT/Ignore umgeschriebenes Ergebnis liefert NullResponse und return false. Arenas geschützter Abschluss sitzt darunter an LocationLifecycleService.EndLocalRaidAsync und wird dann nicht erreicht. Die genaue SVM-Konfiguration ist entscheidend; Arena besitzt eine tokengebundene Crash-Recovery-Route. Daher konkretes bedingtes Abschluss-/Recovery-Risiko, kein belegter dauerhafter Profilverlust. Andere SVM-Stats beeinflussen Balance, sind allein kein Defekt.

**Empfehlung:** Gezielte Prüfung mit SaveLoot/Ignore-Raid-Presets; bei Bedarf Arena-Ausnahme am Callback.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/236/server-value-modifier-svm), [Quelle 2](https://github.com/GhostFenixx/svm-csharp/blob/5d8c5cfab332c98fbfd6d0300be4be5a4a19ecad/ServerValueModifier/HarmonyOverrides/LocalRaidOverrider.cs).

### 5. Amands's Graphics

**Überschneidung** · 1.8.0; Source 5ad92998bc6acdaf9ba90286485e9a96d9c3d962

Amands Graphics 1.8.0 bindet Player bereits bei Player.Init neu; der zunächst vermutete einmalige MainPlayer-Cache trifft hier nicht zu. Der CameraManager.Blur-Prefix greift je UIDepthOfField-Option ein, weitere Patches steuern NVG/Schmerz-Postprocessing. Damit bleibt eine visuelle Testfläche beim Killcam-/Menüwechsel, aber kein aus diesem Code belegter Respawn-Ausfall. Forge nennt Labyrinth ausdrücklich als noch fehlend in der eigenen Kartenliste.

**Empfehlung:** Visueller Test von Killcam, Nacht, NVG und Respawn; vorerst nicht sperren.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/592/amandss-graphics), [Quelle 2](https://github.com/ArchangelWTF/AmandsGraphics/blob/5ad92998bc6acdaf9ba90286485e9a96d9c3d962/AmandsGraphics/Patches/AmandsPlayerPatch.cs), [Quelle 3](https://github.com/ArchangelWTF/AmandsGraphics/blob/5ad92998bc6acdaf9ba90286485e9a96d9c3d962/AmandsGraphics/Patches/AmandsGraphicsCameraManagerPatch.cs).

### 19. Fontaine's FOV Fix

**Überschneidung** · 4.1.0; Source e3ece13848e1dbcb8a8979b97889f454fc19c629

FOV Fix 4.1.0 LerpCameraPatch ersetzt ProceduralWeaponAnimation.LerpCamera per Prefix und return false für den lokalen FirearmController; CameraTransform sowie CameraOffset werden selbst gesetzt. Die geprüften Stellen holen den jeweiligen Controller/Spieler und andere Helfer den aktuellen GameWorld.MainPlayer, nicht ausschließlich einen Weltstart-Cache. Arena-Killcam und Akimbo können visuell überlappen; ein harter Konflikt oder Profilpfad ist dadurch nicht bewiesen. Nicht mit dem veralteten Realism-Overhaul gleichsetzen.

**Empfehlung:** Optik-, Schulter-, Akimbo- und Killcam-Rückkehrtest; kein belegter harter Block.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/701/fontaines-fov-fix), [Quelle 2](https://github.com/space-commits/SPT-FOV-Fix/blob/e3ece13848e1dbcb8a8979b97889f454fc19c629/src/FovPatches.cs), [Quelle 3](https://github.com/space-commits/SPT-FOV-Fix/blob/e3ece13848e1dbcb8a8979b97889f454fc19c629/src/Utils.cs).

### 25. ACCURATE CIRCULAR RADAR

**Gezielt prüfen** · 1.3.4; Source 8b58dde7b681b37c7dad70153f7b212c64d7da0a

ACCURATE CIRCULAR RADAR 1.3.4 setzt HaloRadar._player ausschließlich in Awake auf GameWorld.MainPlayer. Update/Render und Loot-/ExfilTracker verwenden diese Instanz weiter; Update kehrt bei zerstörtem _player zurück. Redeploy.Core ersetzt den lokalen Player innerhalb derselben GameWorld. Daraus folgt ein konkretes Risiko eines am alten Ort gebundenen oder eingefrorenen Radars nach Respawn; kein belegter Arena-Matchabbruch. Das HUD muss neu gebunden werden, nicht nur seine Bildschirmposition geändert.

**Empfehlung:** Bei Arena-Respawn den Radar-/Tracker-Kontext gezielt neu binden oder Arena-seitig pausieren.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1100/accurate-circular-radar), [Quelle 2](https://github.com/Leonana69/Tyrian-Radar-Standalone/blob/8b58dde7b681b37c7dad70153f7b212c64d7da0a/Radar/HaloRadar.cs), [Quelle 3](https://github.com/Leonana69/Tyrian-Radar-Standalone/blob/8b58dde7b681b37c7dad70153f7b212c64d7da0a/Radar/ExfilTracker.cs), [Quelle 4](https://github.com/Leonana69/Tyrian-Radar-Standalone/blob/8b58dde7b681b37c7dad70153f7b212c64d7da0a/Radar/LootTracker.cs).

### 31. Borkel's Realistic Night Vision Goggles (NVGs and T-7)

**Überschneidung** · 3.0.4; Source 0b238cbca085a40df2a3b55ba5dafbcd1a130cd3

Borkel Realistic NVG 3.0.4 schreibt im LaserBeam.LateUpdate-Prefix den Beam-PropertyBlock und verändert sichtbare Intensität temporär bei eingeschaltetem NVG; ein Postfix stellt sie wieder her. Arena hat seinerseits begrenzte Laser-Helligkeits-/Teamfarb-Brackets. Kombination und Patchreihenfolge können Helligkeit beeinflussen, beweisen aber keinen falschen Teamfarbwert. PlayerHelper liest den aktuellen MainPlayer: ein bloßer alter-Spieler-Cache wurde hier nicht bestätigt.

**Empfehlung:** NVG an/aus mit Teamlaser und Helligkeit testen; kein automatischer Block.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/954/borkels-realistic-night-vision-goggles-nvgs-and-t-7), [Quelle 2](https://github.com/Borkel/RealisticNVG-client-2/blob/0b238cbca085a40df2a3b55ba5dafbcd1a130cd3/BorkelRNVG/Patches/LaserBeamLateUpdatePatch.cs), [Quelle 3](https://github.com/Borkel/RealisticNVG-client-2/blob/0b238cbca085a40df2a3b55ba5dafbcd1a130cd3/BorkelRNVG/Helpers/PlayerHelper.cs).

### 35. WTT - Pack 'n' Strap

**Gezielt prüfen** · 2.1.1; Source aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1

Pack n Strap 2.1.1 überschreibt IsAtBindablePlace im Postfix mit Common.IsItemInReachableLocation. IsAtReachablePlace bewahrt dagegen ein bereits wahres Ergebnis durch einen frühen Return. NewBindAvailableSlots enthält ArmBand, Rig/Pockets und Waffenslots, aber nicht Backpack/SecuredContainer. Arenas eigener Postfix erweitert diese Resultate; ohne explizite Reihenfolge kann der spätere Pack-n-Strap-Bindable-Postfix die Hotkey-Bindbarkeit von Rucksack-Items zurücknehmen. Die bereits freigegebene Erreichbarkeit wird nicht auf dieselbe Weise zurückgenommen. PackNStrap installiert diese Patches außerdem nur ohne geladene Standalone-GUID com.cj.useFromAnywhere; Arena allein erfüllt diese Ausnahme nicht. Außerdem ist ArmBand ein Container, während Arena den ArmBand-Baum beim Own-Gear-Restore ausdrücklich als geschützten Bestand überträgt: kein Beleg, dass er blind gelöscht wird. Der Mod-Player-Cache wird nur bei null neu gesetzt, daher zusätzliche Respawn-Probe der Mag-Dump-Funktion.

**Empfehlung:** Rucksack-Hotkeys, G/Nachladen und PFT-Own-Gear mit gefülltem Gürtel nach Respawn prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1278/wtt-pack-n-strap), [Quelle 2](https://github.com/WelcomeToTarkov/PackNStrap/blob/aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1/WTT-PackNStrapClient/Patches/IsAtBindablePlacePatch.cs), [Quelle 3](https://github.com/WelcomeToTarkov/PackNStrap/blob/aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1/WTT-PackNStrapClient/Patches/IsAtReachablePlacePatch.cs), [Quelle 4](https://github.com/WelcomeToTarkov/PackNStrap/blob/aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1/WTT-PackNStrapClient/Helpers/Common.cs), [Quelle 5](https://github.com/WelcomeToTarkov/PackNStrap/blob/aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1/WTT-PackNStrapClient/PackNStrap.cs), [Quelle 6](https://github.com/WelcomeToTarkov/PackNStrap/blob/aa802d1bb2a4aefa96e3c14558b06a2bafe8cce1/WTT-PackNStrapClient/Patches/EquipmentClassPatches.cs).

### 39. HollywoodFX

**Gezielt prüfen** · 3.0.0; Source 1bbdd0211e0a6a5357130c19153be65b4c7390a0

HollywoodFX 3.0.0 setzt ImpactStatic.LocalPlayer in GameWorld.OnGameStarted und leert erst in GameWorld.Dispose. ImpactController.Emit sowie MichelinManPatch vergleichen Treffer mit dessen Transform; ScopeDofController liest dessen ProceduralWeaponAnimation. Ein Arena-Respawn ersetzt den Körper ohne neuen Weltstart. Damit sind falsche lokale Treffer-/Gore-Zuordnung oder an der alten Waffe hängendes Optik-DOF plausibel; eine Exception hängt von der Gültigkeit des alten Körpers ab und ist nicht ingame bewiesen. Reiner Grafik-Mod bedeutet hier nicht automatisch lifecycle-neutral.

**Empfehlung:** Spielerreferenz nach Respawn aktualisieren; Optik-DOF, Treffer/Gore und Killcam gezielt testen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2003/hollywoodfx), [Quelle 2](https://github.com/SleepingPills/HollywoodFX/blob/1bbdd0211e0a6a5357130c19153be65b4c7390a0/HollywoodFX/Patches/GameWorld.cs), [Quelle 3](https://github.com/SleepingPills/HollywoodFX/blob/1bbdd0211e0a6a5357130c19153be65b4c7390a0/HollywoodFX/ImpactController.cs), [Quelle 4](https://github.com/SleepingPills/HollywoodFX/blob/1bbdd0211e0a6a5357130c19153be65b4c7390a0/HollywoodFX/Render/ScopeDofController.cs), [Quelle 5](https://github.com/SleepingPills/HollywoodFX/blob/1bbdd0211e0a6a5357130c19153be65b4c7390a0/HollywoodFX/Patches/Dispose.cs).

### 47. ABPS - Acid's Bot Placement System

**Gezielt prüfen** · 2.1.0; Source 8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a

ABPS 2.1.0 speichert bei BossSpawnScenario.Stop via /botplacementsystem/save in mod/Data/{profileId}.json, außerhalb des SPT-Profil-Snapshots. Produzenten liegen an BossSpawnScenario.Create und BotSpawner.method_10. Gegenbelege: progressive/regressive Chancen sind standardmäßig aus; Arena leert native Bosswellen, erzeugt kontrollierte Bots über SpawnBotsInZoneOnPositions und nutzt im Labyrinth bossTagillaAgro/bossKillaAgro, die nicht in ABPS TrackedBosses stehen. Daher ist der externe Schreibpfad belegt, eine tatsächlich veränderte Boss-Chance durch die aktuelle Arena-Regie jedoch nicht. Kein harter Konflikt und kein unbedingter neuer Adapter aus diesem Audit ableitbar.

**Empfehlung:** Mit progressiven/regressiven Chancen Dateiinhalte vor/nach Arena vergleichen; nur nach belegtem Schreibeffekt gezielt isolieren.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2097/abps-acids-bot-placement-system), [Quelle 2](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Client/Patches/BossSpawnScenarioSpawnProgressPatch.cs), [Quelle 3](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Client/Patches/BossProgressiveRegressivePatch.cs), [Quelle 4](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Client/Patches/BossSpawnScenarioStopPatch.cs), [Quelle 5](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Client/Spawning/BossSpawnTracking.cs), [Quelle 6](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Server/Routers/StaticRouters.cs), [Quelle 7](https://github.com/acidphantasm/botplacementsystem-csharp/blob/8cd7f40c6d7237aa45f7bcff55aa6498357c3c3a/Client/ABPSConfig.cs).

### 54. HandsAreNotBusy

**Gezielt prüfen** · Forge 1.7.1 (>=4.1.3), Abruf 2026-10-19; Upstream 8b5240b1647dfe5f247c454891dc7936355a2571

HANB_Component.Awake bindet den Spieler einmal; Update prüft nur GameWorld und Reset-Taste. FixHandsController entfernt aktive Inventarereignisse, zerstört den HandsController und zieht neu. HANB_Patch hängt die Komponente bei RegisterPlayer an. Gegenprüfung: natives GameWorld.RegisterPlayer setzt MainPlayer vor dem Postfix, der Registrierungsverdacht ist damit widerlegt. Nach Arena-Tod bleiben jedoch Körperobjekte erhalten; CorpseCleanupService.StopTicking deaktiviert nur Player, keine fremden Komponenten. Alte HANB-Komponenten können daher weiterhin denselben Hotkey verarbeiten. Zusätzlich fehlt eine Grenze gegenüber laufendem Kit-/Waffenwechsel. Noch keine native Reproduktion.

**Empfehlung:** Priorität 2: Reset nach mehreren Todesrespawns und während Kitwechsel prüfen; bei bestätigtem Fehler exakten aktuellen Körper/Wechsel schützen, keine pauschale Startsperre.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1298/handsarenotbusy), [Quelle 2](https://github.com/Lacyway/HandsAreNotBusy), [Quelle 3](https://github.com/Lacyway/HandsAreNotBusy/blob/8b5240b1647dfe5f247c454891dc7936355a2571/HANB_Component.cs), [Quelle 4](https://github.com/Lacyway/HandsAreNotBusy/blob/8b5240b1647dfe5f247c454891dc7936355a2571/HANB_Patch.cs), [Quelle 5](https://github.com/Lacyway/HandsAreNotBusy/blob/8b5240b1647dfe5f247c454891dc7936355a2571/HANB_Plugin.cs).

### 60. Continuous Healing

**Hohe Priorität** · Forge 1.6.3 (>=4.1.3), Abruf 2026-10-19; Upstream 1e66a459afbeb85fcbb4f3899871c12726feeb6e

CH_CancelHeal_Patch setzt CancelRequested für den lokalen Spieler; CH_StartHeal_Patch löscht es erst beim nächsten lokalen Heilungsstart. CH_EndHeal_Patch.Prefix auf MedsInHandsOperation.OnEffectRemoved wertet die statische Markierung vor IsYourPlayer aus und ruft ClearQueue/Finish auch auf Bot-Operationen. Arenas wiederholte Körper-/Botwechsel und PFT-Begleiter machen dies relevant. docs/HANDOFF.md dokumentiert eine historische Beobachtung; die heutige Quellprüfung bestätigt denselben Pfad. Nicht alle späten BotFirstAid-NREs sind dadurch erklärt: docs/BLEEDING-NRE-REVIEW.md grenzt deren Ursachen ausdrücklich ein.

**Empfehlung:** Priorität 1: lokaler Heilabbruch mit heilenden Bots/PFT und Tactical-Rundenwechsel; danach enger Operationsadapter oder Upstream-Fix. Keine neue Sperre ohne gesonderte Entscheidung.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1884/continuous-healing), [Quelle 2](https://github.com/Lacyway/ContinuousHealing), [Quelle 3](https://github.com/Lacyway/ContinuousHealing/blob/1e66a459afbeb85fcbb4f3899871c12726feeb6e/Patches/CH_EndHeal_Patch.cs), [Quelle 4](https://github.com/Lacyway/ContinuousHealing/blob/1e66a459afbeb85fcbb4f3899871c12726feeb6e/Patches/CH_CancelHeal_Patch.cs), [Quelle 5](https://github.com/Lacyway/ContinuousHealing/blob/1e66a459afbeb85fcbb4f3899871c12726feeb6e/Patches/CH_StartHeal_Patch.cs).

### 61. Classic Movement

**Hohe Priorität** · Forge 1.1.3 (~4.1.0), Abruf 2026-10-19; Upstream 4875053b1a4a1c7aeea38053c1a894cff85d8805; Server fbbf68edf6a9afcb8e934ea5047c999d2ff40b48

StateReplacer.Prefix ersetzt bei NoInertia Idle/Run/Sidestep durch OldIdleState/OldRunState/OldSidestepState; NostalgiaMode ersetzt zusätzlich Jump/Sprint. ArenaDoubleJump.IsGroundLocomotion und ArenaBunnyhop.GroundState verlangen GetType exakt gleich IdlePlayerState/MovePlayerState/SprintPlayerState. Daher können Arena-Funktionen still ausfallen. SmoothSpeedFix greift außerdem in MovementContext.ManualUpdate ein. Die aktuelle Serverkonfiguration setzt NoInertia=true und BotsUseOldMovement=true; Nostalgia wird nicht mehr unterstützt. Gegenargument: Ohne NoInertia entfällt genau dieser Zustandsersatz. Kein allgemeiner Crashnachweis.

**Empfehlung:** Priorität 1: Doppelsprung/Bunnyhop und normale Bewegung isoliert prüfen; konkurrierende Bewegungsregeln koordinieren oder warnen, bevor eine Startsperre erwogen wird.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1860/classic-movement), [Quelle 2](https://github.com/TheBoogle/ClassicMovement), [Quelle 3](https://github.com/TheBoogle/ClassicMovementServer), [Quelle 4](https://github.com/TheBoogle/ClassicMovement/blob/4875053b1a4a1c7aeea38053c1a894cff85d8805/ClassicMovement/Patches/StateReplacerPatch.cs), [Quelle 5](https://github.com/TheBoogle/ClassicMovement/blob/4875053b1a4a1c7aeea38053c1a894cff85d8805/ClassicMovement/Patches/SmoothSpeedFix.cs), [Quelle 6](https://github.com/TheBoogle/ClassicMovement/blob/4875053b1a4a1c7aeea38053c1a894cff85d8805/ClassicMovement/Plugin.cs), [Quelle 7](https://github.com/TheBoogle/ClassicMovementServer/blob/fbbf68edf6a9afcb8e934ea5047c999d2ff40b48/src/Config/settings.jsonc).

### 65. Weapon Customizer

**Gezielt prüfen** · Forge 4.0.0 (~4.1), Abruf 2026-10-19; Upstream 6f258ba89382ca2a56bb86bed224ee39ec95a7ab

EditPatches erlaubt optional Modding im Raid. HelperExtensions.SetCustomization/ResetCustomization speichert für Owner.ID==MainProfileId; Customizations.Save sendet /weaponcustomizer/save, Server/WeaponCustomizer.Save schreibt mod-eigene customizations.json. Arenas SPT-Profil-Snapshot deckt diese Datei nicht ab; temporäre Arena-Waffen könnten externe Anpassungseinträge hinterlassen. Gegenargument: Customize Weapons In Raid steht standardmäßig auf Never. ApplyPatches entfernt Pool-Anpassungen bei unmodifizierten Waffen bereits gezielt. Kein belegter Verlust vorhandener Customizations.

**Empfehlung:** Priorität 2 bei aktiviertem Raid-Modding: externe Datei vor/nach Kitwechsel/Exit vergleichen; zunächst Raid-Modding auf Never belassen, keine pauschale Mod-Sperre.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1950/weapon-customizer), [Quelle 2](https://github.com/tyfon7/WeaponCustomizer), [Quelle 3](https://github.com/tyfon7/WeaponCustomizer/blob/6f258ba89382ca2a56bb86bed224ee39ec95a7ab/src/HelperExtensions.cs), [Quelle 4](https://github.com/tyfon7/WeaponCustomizer/blob/6f258ba89382ca2a56bb86bed224ee39ec95a7ab/src/Customizations.cs), [Quelle 5](https://github.com/tyfon7/WeaponCustomizer/blob/6f258ba89382ca2a56bb86bed224ee39ec95a7ab/src/Patches/EditPatches.cs), [Quelle 6](https://github.com/tyfon7/WeaponCustomizer/blob/6f258ba89382ca2a56bb86bed224ee39ec95a7ab/src/Settings.cs), [Quelle 7](https://github.com/tyfon7/WeaponCustomizer/blob/6f258ba89382ca2a56bb86bed224ee39ec95a7ab/WeaponCustomizer.Server/WeaponCustomizer.cs).

### 66. MoreBotsAPI

**Bereits behandelt** · Forge 2.1.1 (~4.1.5), Abruf 2026-10-19; bestehender Arena-Vertrag gegen 0986311f75279fc4e6d98228ff2ecf89a8ad4d93

docs/MOREBOTS-COMPATIBILITY-20261019.md dokumentiert exakte Spawnkennungen, getrennte Revenge-Produzenten-/Endsenderwachen und Beziehungsschutz gegen 2.1.1. Neuere Versionen werden mit Vertragsprüfung/Warnung versucht; native Abnahme des Adapters bleibt separat offen. Abhängige Content-Mods erben keine vollständige Freigabe.

**Empfehlung:** Bestehende Schutzverträge erhalten; weder als ungeschützten neuen Konflikt noch als voll nativ getestet darstellen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2426/morebotsapi), [Quelle 2](https://github.com/TacticalToaster/MoreBotsAPI), [Quelle 3](https://github.com/TacticalToaster/MoreBotsAPI/tree/0986311f75279fc4e6d98228ff2ecf89a8ad4d93).

### 67. BrightLasers

**Bereits behandelt** · Forge 3.0.0 (~4.1), Abruf 2026-10-19; Upstream 91361e870017bd3e2cbf0ee293cd542c9fc9586d

Live Source bestätigt CreateLight-Materialklon und Intensitätsmultiplikatoren sowie optionale LateUpdate-Materialfarbe. ArenaLaserBrightness erfasst die Originalintensität und verwendet ein Minimum statt Doppelverstärkung; ArenaTeamLaserColors nutzt eigene MaterialPropertyBlocks. docs/LASER-BRIGHTNESS-REVIEW.md und LASER-BEAM-REVIEW.md begrenzen den Nachweis: keine vollständige native Koexistenzfreigabe.

**Empfehlung:** Vorhandenen Vertrag erhalten; RGB/Override, sichtbare/IR-Laser und Pool-Wiederverwendung später nativ prüfen, keine neue Sperre.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1358/brightlasers), [Quelle 2](https://github.com/acidphantasm/acidphantasm-brightlasers), [Quelle 3](https://github.com/acidphantasm/brightlasers-csharp), [Quelle 4](https://github.com/acidphantasm/brightlasers-csharp/blob/91361e870017bd3e2cbf0ee293cd542c9fc9586d/Client/Patches/LaserBeamCreateLightPatch.cs), [Quelle 5](https://github.com/acidphantasm/brightlasers-csharp/blob/91361e870017bd3e2cbf0ee293cd542c9fc9586d/Client/Patches/LaserBeamLateUpdatePatch.cs).

### 69. WTT - Menu Overhaul

**Kein konkreter Hinweis** · Forge 1.3.0 (~4.1), Abruf 2026-10-19; Upstream 12bbec310c0645c2c37e869b5ba6a074439ec04e

Tag 1.3.0 patcht MenuScreen.Show(Profile, MatchmakerPlayersController, ESessionMode). MenuGuardHelpers weist InGame zurück; MenuLifecycleCoordinator/MenuScreenVisibilityPolicy blenden Änderungen außerhalb des Hauptmenüs aus. ArenaSideSelectionPatch setzt an MatchMakerSideSelectionScreen.Show an, ArenaPostRaidScreens an gebundener Ergebnisoperation. Gemeinsame Umgebungs-/Kameraobjekte bleiben ein visueller Testpunkt; ein direkter Austausch von Arenas Auswahl-/Abschlussablauf ist nicht belegt.

**Empfehlung:** Keine Sperre; Hauptmenü → Arena → Ergebnis → Hauptmenü einschließlich Vorschau einmal gezielt prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1775/wtt-menu-overhaul), [Quelle 2](https://github.com/emilanderss0n/SPT-Menu-Overhaul/), [Quelle 3](https://github.com/emilanderss0n/SPT-Menu-Overhaul/blob/12bbec310c0645c2c37e869b5ba6a074439ec04e/Patches/MainMenuLayoutPatchAdapter.cs), [Quelle 4](https://github.com/emilanderss0n/SPT-Menu-Overhaul/blob/12bbec310c0645c2c37e869b5ba6a074439ec04e/Helpers/Services/MenuGuardHelpers.cs), [Quelle 5](https://github.com/emilanderss0n/SPT-Menu-Overhaul/blob/12bbec310c0645c2c37e869b5ba6a074439ec04e/Infrastructure/Lifecycle/MenuLifecycleCoordinator.cs).

### 71. Increase Climb Height

**Überschneidung** · Forge 2.1.1 (~4.1), Abruf 2026-10-19; Upstream 0a919dc187f12e4c1cf720ae68d7c82bcd3233a5

EditDatabaseValues.OnLoadAsync ersetzt globals.Configuration.VaultingSettings aus config.json. ArenaMovementVaultingReachPatch ruft vor der Spielerkomponente VaultingTuning.Apply auf: Capture nimmt die bereits modifizierte Basis, skaliert obere Grenzen/Distanz und begrenzt an GridSizeY/Z. Restore stellt die erfasste Basis wieder her. Wirkung kann sich stapeln; ein pauschaler Restore-Fehler ist daraus nicht bewiesen.

**Empfehlung:** Konfigurationsüberschneidung dokumentieren; Höhe/Reichweite/Luft-Vault und normalen Folgeraid prüfen, keine Startsperre.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1575/increase-climb-height), [Quelle 2](https://github.com/redlaser42/IncreaseClimbHeight), [Quelle 3](https://github.com/redlaser42/IncreaseClimbHeight/blob/0a919dc187f12e4c1cf720ae68d7c82bcd3233a5/EditDatabase.cs).

### 73. Use Items Anywhere

**Überschneidung** · Forge 2.1.6 (~4.1), Abruf 2026-10-19; Upstream fc190e6b56dd12173e1e43de22c231d3759fc640

V2.1.6 patcht dieselben InventoryController.IsAtReachablePlace/IsAtBindablePlace-Ergebnisse wie ArenaReachPatches, überschreibt aber nach Kategorien statt nur false auf true zu erweitern. Eigene GrenadeThrowingSlots/Reload-Regeln können restriktive Nutzereinstellungen reihenfolgeabhängig überlagern. Gegenargument zum pauschalen Respawnverdacht: CoopRuntime.Tick erkennt Referenzwechsel des aktuellen PlayerOwner und löst Cleanup aus; ItemAccessDelayPatch besitzt Generation-/Abbruchprüfungen. Arenas alter 1.4.0-Nachweis umfasst diese neuen Funktionen nicht vollständig.

**Empfehlung:** 2.1.6 gezielt mit restriktiven Slots, G/R, verschachtelten Behältern und laufendem Delay beim Körperwechsel prüfen; keine Sperre allein wegen Dopplung.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2386/use-items-anywhere), [Quelle 2](https://github.com/CJ-SPT/CJ-SDK), [Quelle 3](https://github.com/CJ-SPT/UseItemsAnywhere), [Quelle 4](https://github.com/CJ-SPT/UseItemsAnywhere/blob/fc190e6b56dd12173e1e43de22c231d3759fc640/UseItemsAnywhere/Patches/IsAtReachablePlacePatch.cs), [Quelle 5](https://github.com/CJ-SPT/UseItemsAnywhere/blob/fc190e6b56dd12173e1e43de22c231d3759fc640/UseItemsAnywhere/Integration/CoopRuntime.cs), [Quelle 6](https://github.com/CJ-SPT/UseItemsAnywhere/blob/fc190e6b56dd12173e1e43de22c231d3759fc640/UseItemsAnywhere/Patches/ItemAccessDelayPatch.cs).

### 75. WTT - Black Division [REDACTED] Home

**Gezielt prüfen** · Forge 1.3.1 (~4.1.5), Abruf 2026-10-19; Tag 1.3.1 d28d27d78024a6f32f6776d7f5d1a3d8018f9214

Tag 1.3.1 d28d27d: Plugin.Awake aktiviert BDNvgPatch. Der Prefix auf BotNightVisionData.MoveToHeadPocket liefert bei Nicht-BlackDiv false und unterdrückt deren nativen Verstaupfad. Die lokale native Methode wird bei ausreichender Sichtweite aufgerufen; normale Arena-PMCs/PFT-Begleiter sind nicht BlackDiv. SpawnController fügt Hunt-/Bosspläne hinzu, doch ArenaLocationMutator bereinigt fremde BossLocationSpawn und MoreBots ist bereits adaptiert. Daher kein Beweis unkontrollierter Spawns. HEAD ist bereits 1.3.2; diese Aussage wurde am veröffentlichten 1.3.1-Tag geprüft. Native Abnahme weiterhin offen.

**Empfehlung:** Priorität 2: normale/PFT-Bots mit NVG am Tag und Populationsgrenzen prüfen; gegebenenfalls enger Hook-Schutz/Upstream-Korrektur, keine eigenmächtige Startsperre.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2511/wtt-black-division-redacted-home), [Quelle 2](https://github.com/TacticalToaster/BlackDiv), [Quelle 3](https://github.com/TacticalToaster/BlackDiv/blob/d28d27d78024a6f32f6776d7f5d1a3d8018f9214/Plugin/Patches/BDNvgPatch.cs), [Quelle 4](https://github.com/TacticalToaster/BlackDiv/blob/d28d27d78024a6f32f6776d7f5d1a3d8018f9214/Plugin/Plugin.cs), [Quelle 5](https://github.com/TacticalToaster/BlackDiv/blob/d28d27d78024a6f32f6776d7f5d1a3d8018f9214/Server/Controllers/SpawnController.cs), [Quelle 6](https://github.com/TacticalToaster/BlackDiv/blob/d28d27d78024a6f32f6776d7f5d1a3d8018f9214/Prepatch/ClientInfo.cs).

### 82. Use Loose Loot

**Kein konkreter Hinweis** · Forge 1.6.0 (~4.1.0), Abruf 2026-10-19; Upstream d04924f195cac15953f0a1d91a5420f307cbf7c3

MakeFoodMedsUsablePatch ergänzt InteractionContextHelper.GetAvailableInteractionState nur für Meds/FoodDrink; FoodMedUser.UseAll ruft den HealthController des Action-Owners auf. Arena-Powerups verbraucht ArenaPickupRuntime selbst, sie sind keine solchen nativen Loot-Items. Kein belegter konkurrierender Powerup-Verbrauch; echte lose Medizin ist ein anderer Interaktionspfad.

**Empfehlung:** Keine Sperre; echte lose Medizin unmittelbar vor Körperwechsel bei Bedarf gesondert prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/933/use-loose-loot), [Quelle 2](https://github.com/DrakiaXYZ/SPT-UseLooseLoot), [Quelle 3](https://github.com/DrakiaXYZ/SPT-UseLooseLoot/blob/d04924f195cac15953f0a1d91a5420f307cbf7c3/Patches/FoodMeds.cs).

### 86. Project Fika

**Bereits gesperrt** · Forge 2.4.6 (>=4.1.3), Abruf 2026-10-19

Forge 2.4.6 ist für >=4.1.3 aktuell. ArenaSession und ArenaLocalRaidStartedPatch prüfen FikaCompatCheck.IsFikaLoaded und verweigern die Aktivierung. Lokaler Körper-/Matchbesitz ist nicht netzwerkautorisiert. Dies ist die bestehende harte Arena-Entscheidung, kein neu entdeckter Verdacht.

**Empfehlung:** Bestehende Startsperre beibehalten; Fika-Badges anderer Mods sind keine Arena-Freigabe.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2326/project-fika), [Quelle 2](https://github.com/project-fika/Fika-Plugin).

### 88. Continuous Load Ammo

**Gezielt prüfen** · Forge 1.2.0 (~4.1.0), Abruf 2026-10-19; Upstream 6337683e0088bad8e05c9c26e798f31e27318cff

RegisterPlayerPatch ersetzt die statische Controllerreferenz bei jedem lokalen RegisterPlayer. OnDispose entfernt den Handler jedoch von dieser aktuellen Referenz und setzt sie null, statt den auslösenden Alt-Controller zu identifizieren. LiveKitRespawn zerstört den lebenden Alt-Körper nach Installation des neuen; natives Player.Dispose löst OnIPlayerDeadOrUnspawn aus. Alt-Cleanup kann somit die Neureferenz verlieren. SetPlayerStateAsync(false) wartet zusätzlich 800 ms und greift ohne Body-/Cancellation-Prüfung auf den gecachten Spieler zu. Gegenargument: OnIPlayerDeadOrUnspawn und Handswechsel bereinigen viele Normalfälle bereits.

**Empfehlung:** Priorität 1–2: Laden/Entladen über initialen Kitwechsel, Tod und Tactical-Rundenwechsel testen; danach engen Controller-/Task-Lebensdauerschutz prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2112/continuous-load-ammo), [Quelle 2](https://github.com/ozen-m/SPT-ContinuousLoadAmmo), [Quelle 3](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/blob/6337683e0088bad8e05c9c26e798f31e27318cff/ContinuousLoadAmmo/Patches/RegisterPlayerPatch.cs), [Quelle 4](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/blob/6337683e0088bad8e05c9c26e798f31e27318cff/ContinuousLoadAmmo/Controllers/LoadAmmoController.cs).

### 91. VRAM Cleaner

**Kein konkreter Hinweis** · Forge 1.1.0 (4.1.6), Abruf 2026-10-19; Upstream 4bea4008e80f82e252b4b872b97e24b591f96b88

VRAMCleaner 1.1.0 patcht PreloaderUI.ShowRaidStartInfo und führt GC.Collect, GC.WaitForPendingFinalizers und Resources.UnloadUnusedAssets einmal aus; MatchmakerFinalCountdown.Show setzt die Markierung zurück. Arena hält aktive Modelle und Kit-Bundle-Leases. Start-Hänger oder Nachladen unbenutzter Assets sind plausibel, doch UnloadUnusedAssets beweist keinen Verlust referenzierter Arena-Pickups oder Körper. Keine wiederholte Cleanup-Schleife pro Respawn gefunden.

**Empfehlung:** Keine Startsperre; bei beobachtetem Stottern Raidstart-Zeit/Asset-Nachladen messen und normalen Folgeraid einbeziehen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2173/vram-cleaner), [Quelle 2](https://github.com/matsixx/SPTVRAMCleaner), [Quelle 3](https://github.com/matsixx/SPTVRAMCleaner/blob/4bea4008e80f82e252b4b872b97e24b591f96b88/Patches/VRAMCleaner.cs).

### 97. Healing Autocancel

**Gezielt prüfen** · Forge 5.0.1 (4.1.6), Abruf 2026-10-19; Upstream d00067dec7a03734962277b18ccdb2e1d1e84812; Source intern 1.1.0, Gleichheit mit Forge-Binärdatei unbestätigt

Forge nennt 5.0.1 für exakt 4.1.6. Verlinktes Live-Tag d00067d verwendet intern csproj-Version 1.1.0; Release-Binärität unbestätigt. Plugin.cs HealingAutoCancelPatch bindet bei GameWorld.OnGameStarted Globals.player/activeHealthController und HealthChangedEvent. Kein RegisterPlayer-/Bodywechsel-Neubinden oder Abmelden vorhanden. Nach Arena LiveKitRespawn bleiben die Referenzen beim Startkörper, neue Heilungen treffen den Ersatzkörper. Quell-Kandidat für Funktionsausfall, noch kein nachgewiesener Defekt exakt der Forge-Binärdatei.

**Empfehlung:** Priorität 2: zuerst Forge-5.0.1-Binärvertrag zur Quelle abgleichen, dann Heilung nach Kit-/Todes-/Rundenwechsel prüfen; erst danach Anpassung/Warnung entscheiden.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1274/healing-autocancel), [Quelle 2](https://github.com/DeltaTwozero/HealingAutoCancel_SPT), [Quelle 3](https://github.com/DeltaTwozero/HealingAutoCancel_SPT/blob/d00067dec7a03734962277b18ccdb2e1d1e84812/Plugin.cs), [Quelle 4](https://github.com/DeltaTwozero/HealingAutoCancel_SPT/blob/d00067dec7a03734962277b18ccdb2e1d1e84812/ImprovedSelfcare.csproj).

### 101. MergeConsumables

**Überschneidung** · Forge 1.6.3 (>=4.1.3), Katalog 2026-10-19; Upstream a923d4dc2f9410c93e1077721550645413085a4d

MergeConsumables erweitert ItemController.ExecutePossibleAction und liefert MergeMedsOperation; CombineItemController.CombineItems verändert PmcData.Inventory. Arena KitBuilder, TeamArenaLoadoutSnapshot und ProfileSnapshotService verwalten dieselben Verbrauchsgüter, ArenaHealthPickup dagegen heilt direkt ohne Item-Merge. Das ist eine Transaktions-/Bedienüberschneidung, kein belegter dauerhafter Profilverlust. Der SptProfile-Snapshot erfasst PmcData; ein Merge während Körper-/Kitwechsel bleibt ungeprüft.

**Empfehlung:** Bei Bedarf gezielt Merge vor/nach Kitwechsel und Abbruch prüfen; keine pauschale Sperre aus dem Quellbefund ableiten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1657/mergeconsumables), [Quelle 2](https://github.com/Lacyway/MergeConsumables), [Quelle 3](https://github.com/Lacyway/MergeConsumables/blob/a923d4dc2f9410c93e1077721550645413085a4d/Patches/ExecutePossibleAction_Patch.cs), [Quelle 4](https://github.com/Lacyway/MergeConsumables/blob/a923d4dc2f9410c93e1077721550645413085a4d/Operations/MergeMedsOperation.cs), [Quelle 5](https://github.com/Lacyway/MergeConsumables/blob/a923d4dc2f9410c93e1077721550645413085a4d/servercsharp/Code/CombineItemController.cs).

### 102. Amands' Sense - Updated

**Gezielt prüfen** · Forge 3.1.0 (~4.1.0), Katalog 2026-10-19; Upstream 09b80a8430a79091528386424942256793ada384

AmandsKillPatch hängt tote Player an DeadPlayers. AmandsSenseClass.SenseDeadBodies liest victim.Position und gameObject ohne vorgeschaltete Unity-Lebensprüfung; gefunden wurde nur Clear(), keine Entfernung einzelner freigegebener Körper. Arena CorpseCleanupService.ReleaseTrimmed/TryRelease führt später Player.Dispose aus. Damit besteht ein konkreter Kandidat für Sense-Fehler oder veraltete Marker nach längerer Arena-Laufzeit. Gegenargument: AmandsPlayerPatch auf Player.Init bindet den neuen lokalen Körper und leert die Liste beim eigenen Respawn; der einfache Erstkörper-Cacheverdacht trifft deshalb nicht zu.

**Empfehlung:** Leichensuche nach vielen Bot-Toden ohne eigenen Tod sowie nach Respawn prüfen; gezielte Entfernung ungültiger Fremdreferenzen beim Autor diskutieren.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2521/amands-sense-updated), [Quelle 2](https://github.com/DrakiaXYZ/SPT-AmandsSense), [Quelle 3](https://github.com/DrakiaXYZ/SPT-AmandsSense/blob/09b80a8430a79091528386424942256793ada384/AmandsSense/Components/AmandsSenseClass.cs), [Quelle 4](https://github.com/DrakiaXYZ/SPT-AmandsSense/blob/09b80a8430a79091528386424942256793ada384/AmandsSense/Patches/AmandsPlayerPatch.cs), [Quelle 5](https://github.com/DrakiaXYZ/SPT-AmandsSense/blob/09b80a8430a79091528386424942256793ada384/AmandsSense/Patches/AmandsKillPatch.cs).

### 103. Climbable Ladders

**Gezielt prüfen** · Forge 1.0.4 (~4.1.0), Katalog 2026-10-19; Upstream 96c0b070c7daacb5b3a78d386a49166bed70f869

PlayerLadderController.Init aktiviert statische CanClimb/CanVault- und ObstacleDistance-Overrides; die Leiter verwendet IsInBufferZone, leert Hände und setzt Jump-/Landing-Zustand. ArenaAirVault.Unity und ArenaBunnyHop besitzen ebenfalls Input-/Animatorübergänge, sodass Leiterende, Luft-Vault und Tod an der Leiter gezielt zusammen geprüft werden müssen. LaddersLoader lädt auch Factory/Customs-Szenen. Gegenargument: Der Controller wird erst per Climb-Interaktion an den aktuellen Körper gehängt; OnPlayerDead und OnDestroy räumen ihn einschließlich Overrides auf. Ein unvermeidbarer Fehler nach jedem Respawn ist damit nicht belegt.

**Empfehlung:** Leiter betreten/verlassen, darauf sterben und danach erneut klettern; Luft-Vault/Bunnyhop und begrenzte Customs-Arenen separat prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2649/climbable-ladders), [Quelle 2](https://github.com/bmpq/spt-ladders), [Quelle 3](https://github.com/bmpq/spt-ladders/blob/96c0b070c7daacb5b3a78d386a49166bed70f869/ladders.bep/PlayerLadderController.cs), [Quelle 4](https://github.com/bmpq/spt-ladders/blob/96c0b070c7daacb5b3a78d386a49166bed70f869/ladders.bep/Patch_VaultingComponent.cs), [Quelle 5](https://github.com/bmpq/spt-ladders/blob/96c0b070c7daacb5b3a78d386a49166bed70f869/ladders.bep/LaddersLoader.cs).

### 106. Shadow Flicker Fix

**Kein konkreter Hinweis** · Forge 1.5.3 (~4.1), Katalog 2026-10-19; Upstream 487b178a31332c8dfbf149817528718756af7cf8

DistantShadowPatches aktualisiert Komponenten auf GameWorld.OnGameStarted und kann CameraManager.SetAntiAliasing nachbearbeiten. Arenas ArenaBotEffectRenderer erzeugt eigene Materialien aus seinem Shaderbundle und verändert keine nativen Kameras oder Meshmaterialien. Gemeinsame Bildausgabe allein beweist keinen Patchkonflikt; Sichtbarkeit bei AA-/Optikwechsel bleibt ungetestet.

**Empfehlung:** Visuellen Vergleich nur bei tatsächlichen Schatten-/Contourproblemen priorisieren; kein prophylaktischer Adapter.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1621/shadow-flicker-fix), [Quelle 2](https://github.com/peinwastaken/shadowflickerfix), [Quelle 3](https://github.com/peinwastaken/shadowflickerfix/blob/487b178a31332c8dfbf149817528718756af7cf8/patches/DistantShadowPatches.cs).

### 117. Brighter Interiors

**Überschneidung** · Forge 1.1.1 (4.1.*), Katalog 2026-10-19; Upstream 3e1390394031de9492023b0662efdc062d3357b0

Brighter Interiors ersetzt AmbientLight.DrawStencilShadow und skaliert _StencilAmbientColor mit ShadowOpacity. ArenaBotEffectRenderer zeichnet eigene, tiefenverdeckte innere Buff-/Spawnkonturen; der Fremdpatch nennt diese Materialien nicht. Ein gemeinsamer Renderer-Einstieg oder Ausfall ist nicht belegt, wohl aber geänderte Helligkeit und Kontrast in Factory/Labyrinth/Labs.

**Empfehlung:** Nur zusammen sichtprüfen, wenn die Kombination gewünscht ist; Schattenwerte nicht für alle Nutzer automatisch ändern.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2613/brighter-interiors), [Quelle 2](https://github.com/7Bpencil/SPT.BrighterInteriors), [Quelle 3](https://github.com/7Bpencil/SPT.BrighterInteriors/blob/3e1390394031de9492023b0662efdc062d3357b0/Plugin.cs).

### 123. Ai Limit

**Bereits behandelt** · Forge 1.9.2 (~4.1), Katalog 2026-10-19; Arena-Vertrag 1.9.2+ im aktuellen Quellstand gegengelesen

AI Limit 1.9.2 deaktiviert entfernte bzw. überzählige Bots; Arena verlangt aktive Teilnehmer und ersetzt unerwartet inaktive Körper. ArenaAiLimitCompat/ArenaAiLimitScope unterdrücken ausschließlich AILimitComponent.Enable im exakt bestätigten Arena-Spiel einschließlich Ladephase. Der API-Vertrag wird geprüft, Fehler sperren nur Arena. Andere globale Fremdpatches bleiben vorhanden; daher keine vollständige Zertifizierung aller Optionen.

**Empfehlung:** Bestehenden Adapter beibehalten; neue Versionen an dessen Vertrag und einem normalen Raid → Arena → normalen Raid prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1945/ai-limit), [Quelle 2](https://github.com/asler/SPT-AILimit).

### 129. Enable Labyrinth

**Bereits behandelt** · Forge 1.1.0 (~4.1), Katalog 2026-10-19; Upstream 25cbb07ed08522fbdd79bd8ffb62aae5123c9f7d

EnableLabyrinthOnLoad ändert global Sichtbarkeit, Zugriff und Exfils; LocationSceneAwake fügt den Scav-Exit hinzu. ArenaLabyrinthAccess projiziert nur die Arena-Auswahl, ArenaLocationMutator.Apply setzt im exakten Raidklon labir_exit auf sofortige Verfügbarkeit mit zehn Sekunden Countdown. Andere Exits einschließlich Scav bleiben erhalten. Das deckt die dokumentierte Schnittmenge ab, nicht jede optionale Schlüssel-/Bossänderung.

**Empfehlung:** Keine zusätzliche Pflichtinstallation; vorhandene Abgrenzung beibehalten und neue Fremd-Exitoptionen gezielt vergleichen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2416/enable-labyrinth), [Quelle 2](https://github.com/acidphantasm/enablelabyrinth-csharp), [Quelle 3](https://github.com/acidphantasm/enablelabyrinth-csharp/blob/25cbb07ed08522fbdd79bd8ffb62aae5123c9f7d/Server/EnableLabyrinthOnLoad.cs), [Quelle 4](https://github.com/acidphantasm/enablelabyrinth-csharp/blob/25cbb07ed08522fbdd79bd8ffb62aae5123c9f7d/Client/Patches/LocationSceneAwake.cs).

### 131. Deminvincibility

**Hohe Priorität** · Forge 2.2.0 (~4.1.3), Katalog 2026-10-19; Upstream 2d347f6629dbd3ccc49be21cadaa92228bbba310

Deminvincibility Patches/Kill.Prefix gibt bei Keep1Health bzw. SecondChance false zurück; ApplyDamage.Prefix reduziert oder begrenzt lokalen Schaden. Native ActiveHealthController.Kill setzt IsAlive=false und feuert DiedEvent; ArenaRespawnDriver.OnPlayerDied sowie ArenaGiveUp.Kill(Undefined) benötigen diesen Tod. Damit können Unsterblichkeit, verhinderter Give-up und veränderte Zombie-/Rundenbedingungen entstehen. Gegenargument: Keep1Health/SecondChance sind standardmäßig aus; der Effekt ist konfigurationsabhängig. Die vorhandene ChangeHealth-Datei wird in Plugin.ApplyPatches nicht aktiviert und ist deshalb kein Konfliktbeleg.

**Empfehlung:** Aktive Unsterblichkeit/SecondChance für Arena nicht freigeben; konkrete optionale Schutzgrenze oder Anwenderhinweis vor einem späteren Integrationstest entwerfen, ohne jetzt Einstellungen zu ändern.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1117/deminvincibility), [Quelle 2](https://github.com/minihazel/Deminvincibility), [Quelle 3](https://github.com/minihazel/Deminvincibility/blob/2d347f6629dbd3ccc49be21cadaa92228bbba310/Patches/Kill.cs), [Quelle 4](https://github.com/minihazel/Deminvincibility/blob/2d347f6629dbd3ccc49be21cadaa92228bbba310/Patches/ApplyDamage.cs), [Quelle 5](https://github.com/minihazel/Deminvincibility/blob/2d347f6629dbd3ccc49be21cadaa92228bbba310/Plugin.cs).

### 134. Simple Crosshair

**Gezielt prüfen** · Forge 1.4.0 (~4.1.0), Katalog 2026-10-19; Upstream 4c5ee788d6b31cd3ac45dc44b589a576b7472382; Redeploy.Core lokal d1d7240a5959cb193efe5da4c31bf44d99f8fe2e

GameWorldUnregisterPlayerPatch ruft DestroyCrosshair; RegisterPlayer bindet nur eine schon existierende Komponente. BattleUIScreenShowPatch/Plugin.TryAttachToBattleUIScreen erzeugen sie. ArenaRespawnDriver nutzt Redeploy.Core RespawnEngine, das den alten Spieler unregistert; EnsureBattleUiReady ruft ShowBattleUIScreen nur, wenn BattleUI nicht bereits aktiv ist. Daher ist ein fehlendes Fadenkreuz nach bestimmten Respawns ein konkreter bedingter Kandidat. Ein tatsächlich erneut ausgelöstes BattleUI.Show stellt es wieder her; Sichtverlust ist noch kein nativer Befund.

**Empfehlung:** Zwei Respawns mit bereits offener BattleUI und anschließenden UI-Wechsel prüfen; falls reproduzierbar, gezielten Rebind statt doppeltem HUD erwägen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1387/simple-crosshair), [Quelle 2](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair), [Quelle 3](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair/blob/4c5ee788d6b31cd3ac45dc44b589a576b7472382/Patches/GameWorldPatches.cs), [Quelle 4](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair/blob/4c5ee788d6b31cd3ac45dc44b589a576b7472382/Patches/BattleUIScreenShowPatch.cs), [Quelle 5](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair/blob/4c5ee788d6b31cd3ac45dc44b589a576b7472382/Plugin.cs), [Quelle 6](https://github.com/maschine34675/Redeploy/blob/d1d7240a5959cb193efe5da4c31bf44d99f8fe2e/Redeploy.Core/Engine/RespawnEngine.cs).

### 135. Skills Extended

**Gezielt prüfen** · Forge 3.1.4 (~4.1), Katalog 2026-10-19; Upstream 1e00eee4a1e24b0bc6ae61f8b8fab249145a850a

Tag V3.1.4: SignalsRuntime.ApplyXp reserviert XP über SignalsXpLedger.Reserve in BepInEx/config/skills-extended-signals-xp.json, anschließend erfolgt ExecuteSkill. ProfileSnapshotService restauriert nur SptProfile; dieses Quittungsjournal ist darin nicht enthalten. Das beweist noch keinen persistenten Skillgewinn: die eigentlichen Profile-Skills bleiben Snapshotbestandteil. SignalsMaps schließt Factory aus; Labs/Labyrinth/Customs können unterstützt sein. Zusätzlich verändern CanWalkPatch und RefreshObstacleRestrictions FirstAid-/Strength-Regeln neben ArenaSurgicalWalk und Luftbewegung. Entwickler-Webeditor ist ein separater manueller Pfad, kein automatisch belegter Raidfehler.

**Empfehlung:** Höchste Priorität innerhalb medium: normalen Skillfortschritt und das separate Journal getrennt vor/nach einem Signalereignis in unterstützter Arena vergleichen; danach FirstAid/Strength und Respawn untersuchen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2383/skills-extended), [Quelle 2](https://github.com/CJ-SPT/Skills-Extended), [Quelle 3](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/Client/SkillsExtended.Client.Skills/Skills/Signals/SignalsXpLedger.cs), [Quelle 4](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/Client/SkillsExtended.Client.Skills/Skills/Signals/SignalsRuntime.cs), [Quelle 5](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/SkillsExtended.Common/Signals/SignalsMaps.cs), [Quelle 6](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/Client/SkillsExtended.Client.Skills/Skills/FirstAid/Patches/CanWalkPatch.cs).

### 139. Audio Accessibility Indicators

**Gezielt prüfen** · Forge 2.1.0 (~4.1), Katalog 2026-10-19; Upstream d0438f818a8df5a93b1828a29852826ee21ee6ec; Redeploy.Core lokal d1d7240a5959cb193efe5da4c31bf44d99f8fe2e

GameWorldUnregisterPlayerPatch ruft für IsYourPlayer Panel.Dispose; diese stoppt KeepNorthRotation mit isActuallyActive=false. Nur Awake setzt true; CreateHUD wird nur auf OnGameStarted aufgerufen und kehrt bei bestehendem HUD zurück. Arena/Core RespawnEngine.UnregisterDeadPlayerFromWorld löst genau diesen Übergang ohne neuen Raidstart aus. Der eng belegte Kandidat ist eingefrorene Kompass-/Richtungsausrichtung, nicht zerstörtes HUD oder verlorener Ton. Ohne Respawn tritt dieser spezifische Weg nicht auf; native Bestätigung fehlt.

**Empfehlung:** Gezielten 360-Grad-Hörindikatorvergleich vor/nach zwei Respawns planen; Lifecycle-Reaktivierung wäre ein enger möglicher Adapter.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1760/audio-accessibility-indicators), [Quelle 2](https://github.com/acidphantasm/acidphantasm-accessibilityindicators/), [Quelle 3](https://github.com/acidphantasm/acidphantasm-accessibilityindicators/blob/d0438f818a8df5a93b1828a29852826ee21ee6ec/Patches/GameWorldPatch.cs), [Quelle 4](https://github.com/acidphantasm/acidphantasm-accessibilityindicators/blob/d0438f818a8df5a93b1828a29852826ee21ee6ec/IndicatorUI/Panel.cs), [Quelle 5](https://github.com/acidphantasm/acidphantasm-accessibilityindicators/blob/d0438f818a8df5a93b1828a29852826ee21ee6ec/Scripts/KeepNorthRotation.cs), [Quelle 6](https://github.com/maschine34675/Redeploy/blob/d1d7240a5959cb193efe5da4c31bf44d99f8fe2e/Redeploy.Core/Engine/RespawnEngine.cs).

### 141. ORBIT 2.0

**Bereits gesperrt** · Forge 2.1.1 (~4.1.6), Katalog 2026-10-19; bestehender Arena-Review pinnt ORBIT 428b861393fabed7053f9e358464f2654b11fa88 (kein neuer Upstream-Vollreview)

ArenaOptionalMods.GetRejectionReason prüft com.chazut.orbit in Chainloader.PluginInfos. Der bestehende 2.1.1-Review nennt Verzögerungsinitialisierung, Extraction-/Verhaltenssteuerung und Ghost-Virtualisierung ohne vollständige Raidpause. Auswahl und finaler Start bleiben gesperrt; normale Raids sind ausgenommen. Eine ORBIT-Fika-Aussage oder Ghost-off beweist keine Arena-Freigabe.

**Empfehlung:** Bestehende Arena-Sperre dokumentieren, nicht entfernen; eine Integration wäre ein eigener Architekturauftrag.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2706/orbit-20), [Quelle 2](https://github.com/Chazut/ORBIT).

### 143. Raid Review

**Gezielt prüfen** · Forge 1.8.0 (~4.1.6), Katalog 2026-10-19; Upstream 4cc98b9724b3f21e246c9ebf171c237d3d92cfda

Raid Review Tag 1.8.0 (derselbe Commit trägt auch 1.7.0; Client-AssemblyVersion bleibt dort 1.7.0) iteriert in Client/mod.cs AllPlayersEverExisted und bündelt Tracking über ProfileId. BotChecker arbeitet zusätzlich auf dieser Historie; Postraid-Dateien/DB liegen außerhalb des SPT-Profils. Arena erzeugt viele Ersatzkörper und CorpseCleanupService erhält historische IDs für späte Schadensquellen. Daraus folgen konkrete Prüfpunkte für Langzeitkosten, Lebenssegmentierung und entsorgte Körper. Gegenargument: myPlayer wird im Polling aus GameWorld.MainPlayer erneuert, null-Player werden im Hauptloop übersprungen; ein fester Erstkörper-Cache ist nicht belegt. Aufzeichnungen sind erwartete Telemetrie, kein Profil-XP-Verlust.

**Empfehlung:** Lange DM-Session mit wiederholten Respawns, periodischen Botchecks und finalem END prüfen; ProfileId-/Lebensdarstellung und Dateiwachstum getrennt bewerten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1479/raid-review), [Quelle 2](https://github.com/ekky-llc/spt-raid-review), [Quelle 3](https://github.com/Chazut/SPT-RaidReview), [Quelle 4](https://github.com/Chazut/SPT-RaidReview/blob/4cc98b9724b3f21e246c9ebf171c237d3d92cfda/Client/mod.cs), [Quelle 5](https://github.com/Chazut/SPT-RaidReview/blob/4cc98b9724b3f21e246c9ebf171c237d3d92cfda/Client/helpers/BotChecker.cs), [Quelle 6](https://github.com/Chazut/SPT-RaidReview/blob/4cc98b9724b3f21e246c9ebf171c237d3d92cfda/Client/patches/player_onGameSessionEndPatch.cs).

### 153. Headshot Darkness

**Gezielt prüfen** · 1.2.5 (Forge; SPT ~4.1); Quellstand ecbec4bb0ae0e7a85a3fd5b996574fcbebad8bf5

Headshot Darkness 1.2.5: DarknessManager.OnGameStarted bindet DiedEvent an den dann aktuellen ActiveHealthController und entfernt den Handler beim ersten Tod. ArenaMode.Client/Respawn/LiveKitRespawn ersetzt den lokalen Körper bereits für Startkits und später erneut; GameWorld.OnGameStarted läuft nicht pro Ersatzkörper. Ausfallendes oder fehlgebundenes Todesfeedback ist deshalb plausibel. Gegenbeleg zur schwereren Dauer-Stummschaltungsbehauptung: Redeploy.Core/View/DeathEffectsHelper.ClearDeathScreenEffects ruft DeathFade.DisableEffect; der fremde Postfix fadet AudioListener.volume wieder auf 1. Kein belegter permanenter Blackout. Replay/Respawn und Handler-Lebenszyklus sind gezielt zu prüfen.

**Empfehlung:** Headshot- und normalen Tod nach Startkitwechsel und mehreren Leben prüfen; mögliche Anpassung an den exakten aktuellen Körper sowie Replay-/Respawn-Lebenszyklus binden.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1582/headshot-darkness), [Quelle 2](https://github.com/peinwastaken/headshotdarkness/blob/ecbec4bb0ae0e7a85a3fd5b996574fcbebad8bf5/Components/DarknessManager.cs), [Quelle 3](https://github.com/peinwastaken/headshotdarkness/blob/ecbec4bb0ae0e7a85a3fd5b996574fcbebad8bf5/Components/VolumeAdjuster.cs), [Quelle 4](https://github.com/peinwastaken/headshotdarkness/blob/ecbec4bb0ae0e7a85a3fd5b996574fcbebad8bf5/Patches/DeathFadeDisablePatch.cs), [Quelle 5](https://github.com/peinwastaken/headshotdarkness/blob/ecbec4bb0ae0e7a85a3fd5b996574fcbebad8bf5/Patches/OnGameStartedPatch.cs).

### 155. Freecam

**Bereits behandelt** · 1.4.8 (Forge; SPT ~4.1); Quellstand fa45a86d48aa6b9311f9a255b03a82a84be291ba

Freecam 1.4.8 behält das öffentliche bool IsFreecamActive auf Terkoiz.Freecam.FreecamController und GUID com.terkoiz.freecam. Genau diesen Vertrag liest ArenaMode.Client/Spectator/TerkoizFreecamProbe; SpectatorPolicy/ArenaSpectatorService vermeiden konkurrierenden Kamerabesitz. Freecam speichert zugleich _gamePlayerOwner und _mainCamera beim Start und kann den aktuellen Spieler zur Kamera versetzen. Der vorhandene Adapter deckt die bekannte Zuschauerkonkurrenz ab, zertifiziert aber weder diese gespeicherten Referenzen über Respawns noch Teleport-/Eingabefunktionen.

**Empfehlung:** Vorhandenen Kameravertrag beibehalten; Teleport und Aktivierung während Körperwechsel separat prüfen, keine vollständige Freecam-Freigabe ableiten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/164/freecam), [Quelle 2](https://github.com/acidphantasm/SPT-Freecam/blob/fa45a86d48aa6b9311f9a255b03a82a84be291ba/project/Terkoiz.Freecam/FreecamController.cs), [Quelle 3](https://github.com/acidphantasm/SPT-Freecam/blob/fa45a86d48aa6b9311f9a255b03a82a84be291ba/project/Terkoiz.Freecam/FreecamPlugin.cs).

### 165. Camo And Stickers

**Überschneidung** · 1.19.0 (Forge; SPT ~4.1.5); Quellstand 89830bf0a1084cb7f879e8cd63293617c8188dec

Camo And Stickers 1.19.0 hakt Itemerzeugung sowie AssetPoolObject.ReturnToPool/OnDestroy; Plugin.OnItemDestroyed entfernt die konkrete Instance-ID und eigene Decals. ArenaWeaponPickup erzeugt native gepoolte Waffenmodelle, KitBundlePreloader/WeaponSwapService neue Hände. Wiederholte Modellwiederverwendung ist daher ein sinnvoller Test. Gegenbelege: APPLY-PAINT-Kontextdialog und MaterialEditor kehren bei InRaid() früh zurück; Poolcleanup ist vorhanden. Externe kosmetische Dateien belegen nicht automatisch persistierten Arena-Spielerfortschritt. Kein konkreter destruktiver Konflikt im geprüften Pfad.

**Empfehlung:** Lackierte eigene Ausrüstung und wiederholte Poolwiederverwendung prüfen; vorhandene Raid-/Cleanup-Grenzen beachten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2658/camo-and-stickers), [Quelle 2](https://github.com/7Bpencil/SPT.WeaponCamoAndStickers/blob/89830bf0a1084cb7f879e8cd63293617c8188dec/Client/WeaponCamoAndStickers/Patches.cs), [Quelle 3](https://github.com/7Bpencil/SPT.WeaponCamoAndStickers/blob/89830bf0a1084cb7f879e8cd63293617c8188dec/Client/WeaponCamoAndStickers/Plugin.cs), [Quelle 4](https://github.com/7Bpencil/SPT.WeaponCamoAndStickers/blob/89830bf0a1084cb7f879e8cd63293617c8188dec/Client/MaterialEditor/Patches.cs).

### 166. Make Shotguns Great Again!

**Überschneidung** · 1.18.1 (Forge; SPT ~4.1.6); Quellstand 0e60ebe1bda30ccd523da48037349dfa364ed87f

Make Shotguns Great Again 1.18.1 verändert serverseitig auch vorhandene Waffenwerte und Bot-Munitionspools. BuckshotDispersionPatch setzt bei mehrteiliger Munition fehlende Streuung im InitiateShot-Prefix. Arenas Kit-/Variantenpfad nutzt geladene native Templates; seine Schussregeln wirken damit auf veränderten Werten. DragonBreathPatch erzeugt im gelesenen Clientpfad Mündungs-Partikel und löscht das Schussflag im Postfix; er ist kein eigener Feuer-Damage-Tick wie ArenaFireRuntime. Bewusste Balance-/Munitionsüberschneidung, kein nachgewiesener Molotov-/Profilschutzdefekt.

**Empfehlung:** Custom-Munition und Streuung gezielt prüfen; geänderte Balance dokumentieren, Partikeleffekte nicht als unbewiesenen Molotov-Schadenskonflikt behandeln.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1453/make-shotguns-great-again), [Quelle 2](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/0e60ebe1bda30ccd523da48037349dfa364ed87f/Client/Patches/DragonBreathPatch.cs), [Quelle 3](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/0e60ebe1bda30ccd523da48037349dfa364ed87f/Client/Patches/BuckshotDispersionPatch.cs), [Quelle 4](https://github.com/viniHNS/Make-Shotguns-Great-Again/blob/0e60ebe1bda30ccd523da48037349dfa364ed87f/Server/makeshotgunsgreatagain.cs).

### 169. Door Randomizer

**Überschneidung** · 1.8.0 (Forge; SPT ~4.1.0); Quellstand d620191e824109afe7f7e7383d0d82ef4b55a168

Door Randomizer 1.8.0 erzeugt im Prefix auf GameWorld.OnGameStarted seine Komponente. Awake randomisiert mit 50% nur aktive/bedienbare EFT.Interactive.Door auf InteractiveLayer und nur aus Open/Shut. Gewöhnliche Factory-/Customs-Wege können sich vor Arena-Readiness ändern. Gegenbeleg: Locked-/nicht bedienbare Türen bleiben ausgeschlossen; ArenaMapPort SpawnGates verwaltet Transform-Teile und NavMesh-Carver, nicht automatisch normale Door-Komponenten. Arena prüft echte Pfade in ArenaAreaTeamRuntime/ArenaTeamBoxRuntime. Ein allgemeiner Spawn-Gate-Bypass ist nicht belegt; einmalige Geometrieüberschneidung.

**Empfehlung:** Factory-/Customs-Startpfade prüfen; MapPort-Spawn-Gates nicht allein aus dem Namen als betroffen behaupten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/820/door-randomizer), [Quelle 2](https://github.com/DrakiaXYZ/SPT-DoorRandomizer/blob/d620191e824109afe7f7e7383d0d82ef4b55a168/DoorRandomizerComponent.cs), [Quelle 3](https://github.com/DrakiaXYZ/SPT-DoorRandomizer/blob/d620191e824109afe7f7e7383d0d82ef4b55a168/DoorRandomizerPlugin.cs).

### 172. Foldables

**Überschneidung** · 1.1.1 (Forge; SPT ~4.1.2); Quellstand dbe7456f82622de0a79c549c39197b695dd5d065

Foldables 1.1.1 macht Rucksäcke, Rigs ohne Slots und Kopfhörer faltbar. FindLocationPatch verweigert Grid.TryFindLocationForItem in gefalteten Containern. FoldingDelay hält Inventar-/Itemreferenzen über mehrere Frames und führt später eine Transaktion aus. Arenas KitBuilder.ApplyToProfile, LiveKitRespawn und ZombieOwnGearLoadout betreffen diese Inventare. Gegenbeleg: StopProcessesPatch ruft StopFolding und bricht per CancellationToken ab; SlotViews entfalten automatisch. Damit ein aktionsabhängiger Integrationstest, kein nachgewiesener Inventarverlust und kein pauschaler Blockgrund.

**Empfehlung:** Gefaltete eigene Ausrüstung im Zombie-Einstieg sowie Tod während Faltaktionen prüfen; vorhandene Abbruchlogik berücksichtigen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2422/foldables), [Quelle 2](https://github.com/ozen-m/SPT-Foldables/blob/dbe7456f82622de0a79c549c39197b695dd5d065/Foldables/Patches/Operations/FindLocationPatch.cs), [Quelle 3](https://github.com/ozen-m/SPT-Foldables/blob/dbe7456f82622de0a79c549c39197b695dd5d065/Foldables/Patches/Operations/InRaid/StopProcessesPatch.cs), [Quelle 4](https://github.com/ozen-m/SPT-Foldables/blob/dbe7456f82622de0a79c549c39197b695dd5d065/Foldables/Utils/ItemUiContextExtensions.cs), [Quelle 5](https://github.com/ozen-m/SPT-Foldables/blob/dbe7456f82622de0a79c549c39197b695dd5d065/Foldables.Server/Foldables.cs).

### 179. Late to the Party

**Hohe Priorität** · 3.1.1 (Forge; SPT ~4.1.0); Quellstand 6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7

Late to the Party 3.1.1, exakt Tag/Commit 6b7f370a: FindAndDestroyLoot scannt GameWorld.LootList.OfType<LootItem>. ProcessFoundLooseLootItem filtert Quests, ausgeschlossene Templates und registrierte Spieler-Drops, aber keine Arena-Pickup-Identität. ArenaWeaponPickup.Prepare erstellt Waffen über CreateLootWithRigidbody mit PickupOwner(weapon.Id): native Loose-Loot-Objekte. DestroyViaLTTP löst bei Owner.ID==item.Id direkt RemoveItemEvent(Status=Succeed) aus. ArenaWeaponPickup.Handle.Removed setzt darauf _consumed=true; AllowTransfer/OutProcess muss dabei nicht beteiligt sein. Möglich sind daher verschwundene/unbrauchbare Waffen-Pickups ohne legitime Aufnahme. Gegenbelege und Bedingungen: Defaults aktivieren Loot-Zerstörung, aber min_loot_age=120, 40m Spieler-/25m Botabstand, NavMesh-Erreichbarkeit, Ausschlusslisten und Ranking begrenzen den Eintritt. Nicht jeder Pickup muss verschwinden; powerup-eigene Marker sind keine LootItems. DoorTogglingComponent verändert zusätzlich gewöhnliche interaktive Türen; MapPort-Gates sind dadurch nicht automatisch nachgewiesen betroffen. Kein LTTP-Adapter im Arena-HEAD identifiziert. Das ist ein belegter Konfliktpfad, kein nativ reproduzierter Fehler.

**Empfehlung:** Als nächsten engen Adapterkandidaten priorisieren: LTTP-Zerstörung im exakten bestätigten Arena-World-Kontext ausnehmen oder eigene Pickup-Identitäten schützen; Tür-/Schalterautomation gesondert begrenzen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/814/late-to-the-party), [Quelle 2](https://github.com/dwesterwick/SPTLateToTheParty/blob/6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7/Client/Controllers/LootManager.cs), [Quelle 3](https://github.com/dwesterwick/SPTLateToTheParty/blob/6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7/Client/Helpers/Loot/LootDiscoveryHelpers.cs), [Quelle 4](https://github.com/dwesterwick/SPTLateToTheParty/blob/6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7/Client/Helpers/Loot/LootDestructionHelpers.cs), [Quelle 5](https://github.com/dwesterwick/SPTLateToTheParty/blob/6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7/Client/Components/DoorTogglingComponent.cs), [Quelle 6](https://github.com/dwesterwick/SPTLateToTheParty/blob/6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7/Shared/Config/config.json).

### 180. Health Per Level

**Überschneidung** · 2.2.0 (Forge; SPT 4.1.*); Quellstand d8183b9fe4c9c29528c29255a651fb205d5e84e9

Health Per Level 2.2.0 bindet /client/game/start, /client/game/profile/select und /client/game/bot/generate. ModifyBotHealth verändert bei aktivierter AI-Option Current/Maximum pro Körperteil anhand Botlevel. Arena nutzt native Botprofile und LiveKitRespawn setzt HealthPercent=100; Redeploy.Core/Engine/RespawnEngine.ResetProfileHealth setzt Current=Maximum*percent, normalisiert die Maxima aber nicht. Höhere HP bleiben damit wirksam und verändern Time-to-kill/Heilbedarf. Gegenbeleg: Spieler-HP liegen im SptProfile und damit in Arenas ProfileSnapshotService; keine zusätzliche externe Statdatei oder selbstständige Arena-Endroute belegt. Primär eine Balanceentscheidung.

**Empfehlung:** Gewünschte HP-Regel festlegen und Spieler-/Bot-Maxima über Körperwechsel vergleichen; keine automatische globale Normalisierung ableiten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1706/health-per-level), [Quelle 2](https://github.com/fryciarz7/HealthPerLevel-CSharp/blob/d8183b9fe4c9c29528c29255a651fb205d5e84e9/Plugin.cs), [Quelle 3](https://github.com/fryciarz7/HealthPerLevel-CSharp/blob/d8183b9fe4c9c29528c29255a651fb205d5e84e9/HealthPerLevel.cs).

### 181. StatTrack

**Hohe Priorität** · 2.1.1 (Forge; SPT ~4.1); Quellstand 3e46c819bd22a7b95bf1e8df22daf8249e615d69

StatTrack 2.1.1 erfasst Bullet-Kills pro Waffeninstanz UND Template. GameWorldPatch auf UnregisterPlayer ruft bei IsYourPlayer EndRaidMergeData; SaveRaidEndInServer sendet /stattrack/save, TrackingData.Save schreibt mod/Data/{profileId}.json. Arenas LiveKitRespawn ruft Redeploy.Core RespawnEngine; UnregisterDeadPlayerFromWorld meldet den alten lokalen Körper auch innerhalb eines Matches ab. Der dekompilierte GameWorld.UnregisterPlayer ist Körperabmeldung, kein exklusives Raidende. ProfileSnapshotService klont ausschließlich SaveServer.SptProfile und erfasst diese Datei nicht; Arenas native EndLocalRaid-Unterdrückung erreicht den separaten Save nicht. Folge ist mögliche bleibende Arena-Telemetrie in StatTrack, keine nachgewiesene Korruption des SPT-Hauptprofils. Gegenbeleg: zufällige Arena-Waffen-IDs können Instanzzeilen isolieren, aber Template-Gesamtstatistiken werden ebenfalls verändert. Asynchrone Mehrfach-Merges könnten zusätzliche Zählprobleme erzeugen; konkrete Mehrfachzählung wurde nicht nativ gemessen.

**Empfehlung:** Als nächsten Profilschutzadapter priorisieren: Erfassung und verzögerte Merge-/Save-Auslöser für die genaue Arena-Sitzung abgrenzen; normale Daten/Queues erhalten und Körperwechsel, Matchende sowie normalen Folgeraid vergleichen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1853/stattrack), [Quelle 2](https://github.com/acidphantasm/stattrack-csharp/blob/3e46c819bd22a7b95bf1e8df22daf8249e615d69/Client/Patches/GameWorldPatch.cs), [Quelle 3](https://github.com/acidphantasm/stattrack-csharp/blob/3e46c819bd22a7b95bf1e8df22daf8249e615d69/Client/Patches/PlayerPatch.cs), [Quelle 4](https://github.com/acidphantasm/stattrack-csharp/blob/3e46c819bd22a7b95bf1e8df22daf8249e615d69/Client/Utils/JsonFileUtils.cs), [Quelle 5](https://github.com/acidphantasm/stattrack-csharp/blob/3e46c819bd22a7b95bf1e8df22daf8249e615d69/Server/TrackingData.cs), [Quelle 6](https://github.com/acidphantasm/stattrack-csharp/blob/3e46c819bd22a7b95bf1e8df22daf8249e615d69/Server/StatTrackStaticRouter.cs).

### 185. C11 - True North

**Überschneidung** · 4.0.29 (Forge; SPT ~4.1.6); Quellstand f720baf8f677af3d5110ba3ef61e6a4279ec985a (separater Client-HEAD; Paketidentität unbestätigt)

Forge-Paket True North 4.0.29 für ~4.1.6; separat verlinkter Client-HEAD f720baf8. MagCheckForearmRotator pollt GameWorld.MainPlayer und erneuert Hand-/Animatorbindungen; NightVisionMaskApply/NvgPodRotator ändern Masken und Geräterotation. Arena ersetzt Hände/Körper und besitzt Kamera-Rebind-/Replay-Wechsel. Deshalb Spezialausrüstung gezielt prüfen. Gegenbeleg: wiederholtes MainPlayer-Polling vermeidet eine ausschließlich einmalige Startkörperbindung; kein unabhängiger Fortschrittsspeicher oder allgemeiner Spawnmanager in den gelesenen Clientpfaden. Binäridentität des Client-HEAD mit dem 1,74-GB-Forge-Paket wurde nicht verifiziert.

**Empfehlung:** Betroffene Spezialausrüstung nach Kitwechsel, Tod und Replay prüfen; Client-HEAD nicht ungeprüft mit dem großen Forge-Paket gleichsetzen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2423/c11-true-north), [Quelle 2](https://github.com/c11gunworks-lab/C11-TN4-Client/blob/f720baf8f677af3d5110ba3ef61e6a4279ec985a/C11-TN4-Client/Core/MagCheckForearmRotator.cs), [Quelle 3](https://github.com/c11gunworks-lab/C11-TN4-Client/blob/f720baf8f677af3d5110ba3ef61e6a4279ec985a/C11-TN4-Client/Core/NvgPodRotator.cs), [Quelle 4](https://github.com/c11gunworks-lab/C11-TN4-Client/blob/f720baf8f677af3d5110ba3ef61e6a4279ec985a/C11-TN4-Client/patches/NightVisionMaskApply.cs).

### 193. NerfBotGrenades

**Überschneidung** · 1.3.1 (Forge; SPT >=4.1.3); Quellstand 853c539aba143c74a89eb378592367eab605e12c

NerfBotGrenades Forge 1.3.1; Upstream 853c539a ersetzt BotGrenadeController.ToThrowDirection durch AIGreanageThrowData.Direction plus precisionOffset mal Konfigurationsmultiplikator. Richtung ändert sich, nicht Save-Routen oder Schadenszuweisung. ArenaFriendlyExplosionEffects/CombatPatches entscheiden separat über Team-/Spawn-Schutz und native Granatenschäden. Daher bewusste Schwierigkeitsüberschneidung für Arena-Bots einschließlich Begleiter; kein aus diesem Hook ableitbarer Profilschutz-Bypass. Keine native Flugbahn-/Teamprüfung.

**Empfehlung:** Als bewussten Schwierigkeitsmodifikator behandeln; Grenaden mit beiden Teamseiten prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/1925/nerfbotgrenades), [Quelle 2](https://github.com/Lacyway/NerfBotGrenades/blob/853c539aba143c74a89eb378592367eab605e12c/Patches/BotGrenadeController_ToThrowDirection_Patch.cs), [Quelle 3](https://github.com/Lacyway/NerfBotGrenades/blob/853c539aba143c74a89eb378592367eab605e12c/NerfBotGrenades_Plugin.cs).

### 194. Definitive Weapon Variants

**Überschneidung** · 4.3.2 (Forge; SPT ~4.1.2); Quellstand 4bf074cb699f3e6d8412921eaf6c8e58193967dc (HEAD 4.3.3; Releaseidentität unbestätigt)

Forge 4.3.2 (~4.1.2) beschreibt mehr als 600 Waffenvarianten. Verlinkter HEAD 4bf074cb trägt schon ModMetadata 4.3.3; nur gesonderter Funktionshinweis, kein Nachweis identischen Releasecodes. CustomItemCreator klont Templates und registriert eigene IDs/Presets; CustomPropertiesChanger verändert Eigenschaften. Arena.Server/Services/ArenaWeaponVariants indiziert geladene Templates und validiert gezogene Aufsätze, während die kuratierte Waffenliste nicht allein durch Installation beliebig erweitert wird. Bewusste Inhalts-/Balanceüberschneidung; kein belegter Respawn- oder eigener Persistenzkonflikt.

**Empfehlung:** Neue Templates nicht automatisch als kuratierte Arena-Startwaffen zusagen; tatsächlich gezogene Aufsätze/Munition und Kitvalidierung prüfen.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2010/definitive-weapon-variants), [Quelle 2](https://github.com/Szonszczyk/DefinitiveWeaponVariants-csharp/blob/4bf074cb699f3e6d8412921eaf6c8e58193967dc/CustomClasses/CustomItemCreator.cs), [Quelle 3](https://github.com/Szonszczyk/DefinitiveWeaponVariants-csharp/blob/4bf074cb699f3e6d8412921eaf6c8e58193967dc/CustomClasses/CustomPropertiesChanger.cs), [Quelle 4](https://github.com/Szonszczyk/DefinitiveWeaponVariants-csharp/blob/4bf074cb699f3e6d8412921eaf6c8e58193967dc/ModMetadata.cs).

### 198. UNTAR Go Home!

**Überschneidung** · 3.2.1 (Forge; SPT ~4.1.5); Quellstand 915d9f7aa2bc41973bcd12bdac6bbc511867acf8

UNTAR Go Home 3.2.1 exakt an Tag 915d9f7a geprüft; main acdd80ad war bereits 3.2.2. Mod.cs registriert untar mit RevengeAfterRaids=true/RevengeRaidAmount=3 über MoreBots FactionService und fügt BossLocationSpawn-Patrouillen/Checkpoints/Hunts hinzu; /client/match/local/end berechnet Spawnlisten neu. Client registriert MoreBots-Huntrollen sowie UNTAR-spezifische Komponenten. Gegenbeleg: keine eigene Revenge-Datei oder unabhängiger Revenge-Sender im gelesenen Release. ArenaMoreBotsCompatibility schützt genau MoreBots-Produzent/Sender/Feindschaft; ArenaSoloPopulation/ArenaPopulationPolicy kontrollieren fremde Wellen und Erzeuger in der bestätigten Arena. Das reduziert den vermuteten Konflikt deutlich, zertifiziert aber nicht jeden Content-Spawn. Globale Spawnlisten-Neuberechnung und unveränderte externe Revenge-Datei bleiben Testpunkte. Forge fordert MoreBots 2.1.1, obwohl Tag-Metadaten noch >=2.1.0 nennen; Arenas strengere Mindestversion gilt. Kein neuer unabhängiger Revenge-Bypass belegt.

**Empfehlung:** UNTAR 3.2.1 mit MoreBots >=2.1.1 auf ungewollte Patrouillen/Hunts und unveränderte Revenge-Datei prüfen; keine pauschale Content-Freigabe oder neue unabhängige Revenge-Lücke behaupten.

**Primärquellen:** [Quelle 1](https://sp-mod.com/mod/2342/untar-go-home), [Quelle 2](https://github.com/TacticalToaster/TacticalToasterUNTARGH/blob/915d9f7aa2bc41973bcd12bdac6bbc511867acf8/TacticalToasterUNTARGH/Mod.cs), [Quelle 3](https://github.com/TacticalToaster/TacticalToasterUNTARGH/blob/915d9f7aa2bc41973bcd12bdac6bbc511867acf8/TacticalToasterUNTARGH/Controllers/UntarSpawnController.cs), [Quelle 4](https://github.com/TacticalToaster/TacticalToasterUNTARGH/blob/915d9f7aa2bc41973bcd12bdac6bbc511867acf8/Plugin/Plugin.cs), [Quelle 5](https://github.com/TacticalToaster/TacticalToasterUNTARGH/blob/915d9f7aa2bc41973bcd12bdac6bbc511867acf8/Plugin/Patches/TarkovInitPatch.cs).

## Verifikation dieses Berichts

- Vollständigkeit, 200 eindeutige Rang-/Mod-IDs, absteigende Downloadreihenfolge und vier Rohantwort-Hashes geprüft.
- Vier getrennte 50er-Sichtungen integriert; sämtliche Einzelbefunde einschließlich Gegenargumenten und Versionsgrenzen bleiben im JSON erhalten.
- Wesentliche Befunde gegen tatsächliche Arena-/Redeploy-Gegenstellen gegengelesen. Zusätzliche unabhängige Gegenprüfung der SVM-, ABPS- und Pack-n-Strap-Aussagen. Letztere wurde auf den bedingten Bindable-Postfix begrenzt; Reachable und Standalone-UseItemsAnywhere-Ausnahme sind ausdrücklich berücksichtigt.
- Keine neue native Reproduktion behauptet. Keine Aussage aus einer Fika-Markierung abgeleitet. Kein fremder Quellcode ins Produkt kopiert.
- Ausschließlich Auditdokumentation geändert. Kein Build, Deployment, Release oder neuer Startblock Bestandteil dieses Auftrags.
