# Mutatoren im Kartenmenü

Stand: 05.10.2026, Movement-Hauptschalter. Die vierzehn Mutatoren stehen dauerhaft in der
rechten Bildschirmspalte, ohne Öffnen-Button oder Popup. Eigene Controls im
Arena-Stil zeigen aktive Optionen mit grünem ON, Farbchip und Seitenstreifen;
OFF bleibt grau, nicht verfügbare Optionen zeigen N/A mit Begründung.
Es gibt keine neuen Zahlenregler oder eine zweite Konfiguration.

| Schalter | Bestehende Einstellung | Einschränkung |
|---|---|---|
| Fast movement | Movement / Enabled | Hauptschalter für das gesamte lokale Arena-Movement; bleibt auch bei AUS bedienbar |
| Double jump | Movement / DoubleJumpEnabled | Arena movement muss aktiv sein |
| Bunnyhop | Movement / BunnyhopEnabled | Arena movement muss aktiv sein; neuer Standard AN |
| Air control | Movement / AirControlEnabled | Arena movement muss aktiv sein |
| Airborne hip fire | Movement / AirHipFireEnabled | Arena movement muss aktiv sein |
| Air vault | Movement / AirVaultEnabled | Arena movement muss aktiv sein |
| Slide | Movement / SlideEnabled | Arena movement muss aktiv sein |
| Infinite stamina | Movement / InfiniteStamina | Arena movement muss aktiv sein |
| No fall damage | Movement / NoFallDamage | Arena movement muss aktiv sein |
| Backpack items | Kits / UseItemsAnywhere | Getragene Items und Rucksackmagazine; keine Erweiterung der Mechanik |
| Killcam | ReplaySeconds des gewählten Modus | Aktive kompatible KillcamReplay erforderlich |
| Pickups | Arena pickups / Enabled | Deathmatch oder unabhängiges TDM; benötigt zusätzlich ein Karten-/Areal-Layout |
| Colored tracers | Deathmatch / ColoredTracers, Gun Game / ColoredTracers, Tactical / SoloColoredTracers bzw. Effects / TeamTracers | DM, Gun Game und Tactical 1v5 separat, standardmäßig aus; aus stellt native Tracer wieder her |
| Team laser colors | Effects / TeamLasers | TDM oder geschütztes Tactical/PFT; Helligkeit unverändert |

Die sichtbaren Schalter lesen und ändern exakt die vorhandenen ConfigEntries.
Keine fremde Mod-Konfiguration und keine neue Serverroute. Killcam verwendet weiter TrySelectKillcam: AN setzt drei Sekunden,
AUS null, getrennt pro Modus; die bestehende Aufnahme beim Arm bleibt erhalten.
Tracer verwenden in Deathmatch, Gun Game und Tactical 1v5 jeweils eine eigene
Einstellung mit Standard AUS. Die bestehende Effects-Einstellung bleibt für
Tactical 5v5 und TDM erhalten;
gespeicherte Werte werden nicht migriert oder bei Moduswechsel überschrieben.
Der Renderer liest dieselbe Auswahl anhand des eingefrorenen ActiveMode und
ArenaTracerTeams.ProtectedTeamMatch, erst nach dem IsActive-Guard. Der Teamwert
bleibt über Rundenwechsel gültig und wird beim Matchende gelöscht; er stammt
nicht aus der veränderbaren Menüauswahl. Legacy Endless/Rounds verwenden die Deathmatch-Einstellung.
Alle anderen Schalter gelten Arena-weit. Änderungen werden sofort gespeichert,
auch bei deaktiviertem SaveOnConfigSet. F12-Änderungen erscheinen im bestehenden
0,2-s-Menürefresh; abgelehnte Schreibzugriffe zeigen den tatsächlichen Wert.

N/A nennt den Grund und verändert keine gespeicherte Präferenz. Insbesondere
werden Bewegung, Pickups oder Laserfarben nicht stillschweigend eingeschaltet,
wenn eine Abhängigkeit fehlt. Die Pickup-Beschreibung verspricht keine Punkte
auf Karten ohne Layout. Die gesamte Bedienung ist ab Arm bzw. im Raid gesperrt.
Es werden keine Spielmechanik- oder Profilpfade verändert.

FAST MOVEMENT verwendet den vorhandenen Movement-Master. AUS deaktiviert auch
DoubleJump, Bunnyhop, Air Control, Slide und die übrigen lokalen Movement-Boni.
Die Unteroptionen zeigen N/A; ihre Werte und sämtliche Multiplikatoren bleiben
gespeichert und gelten beim erneuten Einschalten wieder. Bot-Movement, Pickups,
Backpack-Items und andere unabhängige Systeme bleiben separat. Keine neue
Zusicherung über sofortige Rückstellung bereits gelatchter ADS-/Vault-Werte
bei F12-Änderungen mitten im Raid; dieser Menüschalter ist vor Raid/Arm bedienbar.

Rechts oben stehen Kartenname und natives Thumbnail ohne Beschreibung oder
Vanilla-Raidstatistiken. Das Thumbnail übernimmt die asynchron geladene native
Sprite-Referenz, bewahrt deren Seitenverhältnis und besitzt/zerstört das Asset
nicht. Bei ausstehendem Bild oder fehlender Kartenauswahl bleibt die Bildfläche
leer, statt das vorige Thumbnail zu zeigen. Customs zeigt dort seine Arealkarte
mit AREA/OVERVIEW. Beide Ansichten reservieren dieselben 320 Referenzpixel Höhe;
die Mutatoren bleiben unabhängig von Karte und Modus fest auf Y=508.
Die vierzehn Schalter stehen in zwei Spalten mit sieben Zeilen; der Kasten ist 472
Referenzpixel hoch und endet bei Y=980. Die erste GPU-2-Prüfung zeigte bei
496 Pixeln eine Überschneidung mit der nativen READY-Fläche (ab Y=984), deshalb
sind Buttons jetzt 26 Pixel hoch, weiterhin mit derselben Schriftgröße und
28 Pixel Platz für zweizeilige Beschreibungen. Beschreibungen bleiben sichtbar, kein
Scrollen oder weiteres Popup. Der zentrale Navigationsbereich bleibt frei.
Die GPU-Prüfung zeigte abgeschnittene Langtexte; die kurzen Hilfetexte sind
deshalb auf eine Zeile gekürzt, ohne Schriftgröße oder Kastenposition zu ändern.
Zeit-/Wetterwahl, Next und Ready bleiben native Controls an ihrem bisherigen Platz.
Die gemeinsame rechte Spalte skaliert bei geringerer Bildschirmhöhe mit.
Escape behält die native Navigation zum vorherigen Bildschirm; Close räumt alle
eigenen Ansichten auf. Die originalen CanvasGroup-Werte werden exakt restauriert,
auch beim Initialisierungsfehler. Der Preview besitzt nur die Sichtbarkeit der
neuen Zusammenfassung, nicht zusätzlich dieselbe native Infobox.
Keine globale Eingabesubskription. Jede Ansicht besitzt ihre eigenen Listener;
Generation, Zeilenidentität, aktive Hierarchie und Arm-Zustand werden nochmals
im Callback geprüft. Der bestehende Modus-/Areal-Panelplatz bleibt unverändert.
Farbchip und Streifen sind unabhängig von Button-Hover/Disabled-Tints; keine
geklonten nativen Animationen können ihren aktiven Zustand übermalen.

Die portierten Marker BAY 5 und EQUATOR stehen nur im Arena-Kartenscreen ohne
Präfix und in Gold. Ausgewählt verwenden sie dunkleres Gold auf dem hellen
nativen Auswahlhintergrund. Ein eigenes TMP-Label schützt vor nativen
Textfarb-Animationen; der ursprüngliche Toggle, seine Animation und seine
Ereignisse bleiben erhalten. Die native Breitenberechnung bekommt den kurzen
Text. Seit 2026-10-01 bleibt das originale TMP dafür eingeschaltet: eine lokale
CanvasGroup blendet nur seine Darstellung aus. Das vorherige Abschalten der
Komponente hatte die native Klickfläche auf GPU 1 bei Bay 5 auf 54 × 5 Pixel
zusammenfallen lassen. Eine vorhandene CanvasGroup wird exakt restauriert;
nur eine eigens hinzugefügte wird beim Verlassen entfernt.
Normierte Markerpositionen: Bay 5 (0,47 / 0,82), Equator (0,70 / 0,29),
letzteres südöstlich von Interchange (0,66 / 0,36). Das ist ausschließlich eine
lokale Ansichtsänderung, keine geografische Aussage über die portierten Arenen.
Beim Verlassen werden Originaltexte, Header-Sichtbarkeit und Markerpositionen
restauriert. Location-Datenbank, IDs, Allowlist und normale PMC-Menüs bleiben
unverändert. Derselbe lokale Kurzname steht auch über dem Thumbnail.

## Prüfung

Der erweiterte TdmMatchMenuChecks-Harness kompiliert echte Settings,
ArenaPickupSettings, Adapter und Unity-Ansichten gegen externe UI-/Config-
Grenzen. Er prüft Speicherung, F12-Synchronisation, alle vier Modi inklusive
Tactical Solo/PFT, fehlende Killcam, ausgeschalteten Movement-Master, versteckte
und alte Callbacks, Arm-/Raid-Sperren sowie Close/Wiederöffnen. Kompilierte
Fehlerkontrollen testen die tragenden Guards. Kein Spielprozess und keine echte
Konfiguration werden durch diesen Harness verändert.

Das unabhängige Quellreview hat keine offenen konkreten P0-P2-Befunde gefunden.
Build, Auslieferung und eine eventuelle native UI-Prüfung werden mit Commit und
Hash im HANDOFF dokumentiert; Offline-Prüfungen zertifizieren kein Raycasting,
Schriftbild oder Gameplay.

Native Prüffolge: Arena > Kartenauswahl; vierzehn sofort sichtbare Schalter, ON/OFF
und Farbwechsel mit F12 vergleichen, Killcam pro Modus und Laser/Pickup-N/A
prüfen; Factory-Zeitwahl und Customs-Preview ohne Überlappungen, Escape und
gewöhnlichen PMC-Bildschirm auf vollständige Restaurierung prüfen. Keine neue Raid-Abnahme nötig,
um die unveränderten Gameplaypfade zu beschreiben; deren Funktion ist mit diesem
Menütest aber nicht erneut bestätigt.

## Nachtrag 2026-10-06 (nachts): ANNOUNCER dazu, OWN GEAR raus

Nach dem Stand oben (05.10.2026) kam im selben Zombie-Nachtrag ein neuer
Mutator hinzu und einer wieder heraus, beide ohne `ArenaMutatorsView`-Code-
Änderung — die Ansicht baut ihre Zeilen generisch aus `Enum.GetValues
(typeof(ArenaMutator))`, siehe ZOMBIE-MODE-REVIEW.md, Abschnitt "v2
(2026-10-06 night)" für die volle Begründung:

- **ANNOUNCER** (`ArenaMutator.Announcer`) schaltet den Arena-Announcer PRO
  MODUS, genau wie Killcam pro Modus läuft: Standard AUS bei Zombies, AN bei
  den vier anderen Modi, über je eine eigene `ConfigEntry<bool>` pro
  Modus-Sektion (`Settings.AnnouncerEntry`/`BindAnnouncer`). Dasselbe
  ON/OFF-Aussehen wie jeder bestehende Schalter oben, bei Arm eingefroren
  (`ArenaSession.AnnouncerEnabledSelection`). Der bestehende globale
  Audio/Announcer-Hauptschalter bleibt unverändert LIVE und wirkt weiter als
  Mastergate über alle fünf Modi; Lautstärke bleibt ebenfalls global.
  HUD-Textdurchsagen ("WAVE n") sind kein Announcer und bleiben unverändert.
- **OWN GEAR** stand nie als Zeile in der Tabelle oben: Es kam erst nach dem
  05.10.2026-Stand dieses Dokuments als eigener Mutator (`ArenaMutator.
  OwnGear`) hinzu und wurde noch in derselben Nachtrag-Sitzung wieder
  entfernt, bevor es je hier dokumentiert wurde. Die Zombie-Eigenausrüstung
  ist jetzt eine Zeile im LINKEN Modus-Panel (EQUIPMENT-Fakt-Slot, nur für
  Zombie sichtbar), nicht mehr im rechten Mutatoren-Panel; siehe
  ZOMBIE-MODE-REVIEW.md für die Verfügbarkeitstabelle.
- Die Zählung "vierzehn" oben war schon vor diesem Nachtrag veraltet
  (Pickups/Tracer/Laser/Akimbo kamen seither dazu, ohne dass dieses Dokument
  nachgezogen wurde) und wird hier nicht nachträglich korrigiert — das ist
  vorbestehende, von diesem Nachtrag unabhängige Technik-Schuld. Die
  aktuelle Zahl laut ZOMBIE-MODE-REVIEW.md: 15 Mutatoren nach Entfernung von
  OWN GEAR, 16 nach Hinzufügen von ANNOUNCER. Noch nicht im Spiel bestätigt.

## Nachtrag 2026-10-08: Menü-Politur (Branch feature/menu-polish)

Sechs Änderungen aus dem Nutzerfeedback nach der GPU-Abnahme von v2; dieser
Abschnitt ergänzt den Stand oben, Zeilen und Absätze weiter oben, die dadurch
veraltet sind, wurden an Ort und Stelle korrigiert.

**ANNOUNCER blendet auch die Einblendungen aus.** Der Schalter wirkt jetzt auf
alles, was der Announcer ausgibt: Stinger, Rundenhinweise UND den großen
Mitteltext der Kill-/Award-Durchsagen ("DOUBLE KILL", "KILLING SPREE",
"SHUTDOWN", ...). `ArenaHud` hängt `ArenaMatch.OnCallout` an einen eigenen
Handler (`OnAwardCallout`), der `CalloutElement` nur zeigt, wenn
`AnnouncerService.IsEnabledForLiveMatch()` wahr ist — dasselbe Gate wie die
Stinger (live Hauptschalter UND die bei Arm eingefrorene Pro-Modus-Wahl, daher
kein Mid-Raid-Umschalten per F12 am Per-Modus-Eintrag; der Hauptschalter
`Audio / Announcer` bleibt live und blendet den Text ebenfalls aus).
`ArenaMatch.OnTextCallout` ("WAVE n" aus ZombieMode, die Eigen-Granaten-
Stichelei aus `ArenaSelfGrenadeTaunt`) hat weiterhin einen eigenen, ungegateten
Handler und erscheint unabhängig vom Announcer-Schalter; der Satz im Nachtrag
vom 06.10. ("HUD-Textdurchsagen sind kein Announcer und bleiben unverändert")
gilt damit weiter. Die Zeilenbeschreibung lautet jetzt "Stingers, cues and
kill callouts." (33 Zeichen, passt in die Beschreibungszelle). Offline belegt
durch `tools/AnnouncerChecks` (Wahrheitstabelle des Gates plus Quelltext-Pins
auf das HUD-Routing, weil `ArenaHud` an Unity gebunden ist) und drei Mutanten
in `Verify-Regression.ps1` (ungegateter Award-Handler, gegateter Text-Handler,
Text-Callout am Award-Handler). Nicht im Spiel gesehen.

**Legende und Fußzeile entfernt.** Die Zeile "Green: ON / Grey: OFF / N/A:
unavailable" unter dem Titel und die Fußnote "Saved with F12. Killcam/tracers:
per mode; other switches: all Arena modes." sind weg (Felder, Erzeugung und
Layout-Zeilen in `ArenaMutatorsView`). Die Zustände erklären sich über ON/OFF/
N/A mit Farbchip und Seitenstreifen selbst, und die Fußnote war veraltet: sie
nannte weder den Announcer noch das neue Team-only-Tracer-Verhalten (die
in HANDOFF.md vermerkte Nachtrags-Kleinigkeit "Killcam/tracers: per mode" ist
damit erledigt). Der Kasten bleibt 600 × 472 Referenzpixel bei Y=508 (der
Harness pinnt Rechteck und 16 Buttons unverändert); frei gewordener Platz
geht in die Zeilen: Titel Top 14 / Höhe 30, erste Zeile bei Top 52, Zeilenabstand
`(MutatorsHeight - 52 - 8) / 8` = 51,5 Pixel (vorher 47,5), Beschreibungshöhe
Abstand minus 26 (25,5), letzte Beschreibung endet 8 Pixel über dem Kastenboden
(Y=464). TdmMatchMenuChecks pinnt den Wegfall und die Geometrie und hat drei
neue Mutanten (Legende zurück, Zeilenanfang 68, alter Zeilenabstand).
