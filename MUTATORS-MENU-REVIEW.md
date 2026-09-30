# Mutatoren im Kartenmenü

Stand: 30.09.2026, Inline-Revision. Die acht Mutatoren stehen dauerhaft in der
rechten Bildschirmspalte, ohne Öffnen-Button oder Popup. Eigene Controls im
Arena-Stil zeigen aktive Optionen mit grünem ON, Farbchip und Seitenstreifen;
OFF bleibt grau, nicht verfügbare Optionen zeigen N/A mit Begründung.
Es gibt keine neuen Zahlenregler oder eine zweite Konfiguration.

| Schalter | Bestehende Einstellung | Einschränkung |
|---|---|---|
| Double jump | Movement / DoubleJumpEnabled | Arena movement muss aktiv sein |
| Air control | Movement / AirControlEnabled | Arena movement muss aktiv sein |
| Airborne hip fire | Movement / AirHipFireEnabled | Arena movement muss aktiv sein |
| Backpack items | Kits / UseItemsAnywhere | Getragene Items und Rucksackmagazine; keine Erweiterung der Mechanik |
| Killcam | ReplaySeconds des gewählten Modus | Aktive kompatible KillcamReplay erforderlich |
| Pickups | Arena pickups / Enabled | Deathmatch oder unabhängiges TDM; benötigt zusätzlich ein Karten-/Areal-Layout |
| Colored tracers | Effects / TeamTracers | Aus stellt native Tracer wieder her |
| Team laser colors | Effects / TeamLasers | TDM oder geschütztes Tactical/PFT; Helligkeit unverändert |

Die sichtbaren Schalter lesen und ändern exakt die vorhandenen ConfigEntries.
Keine Defaults oder Migration, keine fremde Mod-Konfiguration, keine neue
Serverroute. Killcam verwendet weiter TrySelectKillcam: AN setzt drei Sekunden,
AUS null, getrennt pro Modus; die bestehende Aufnahme beim Arm bleibt erhalten.
Alle anderen Schalter gelten Arena-weit. Änderungen werden sofort gespeichert,
auch bei deaktiviertem SaveOnConfigSet. F12-Änderungen erscheinen im bestehenden
0,2-s-Menürefresh; abgelehnte Schreibzugriffe zeigen den tatsächlichen Wert.

N/A nennt den Grund und verändert keine gespeicherte Präferenz. Insbesondere
werden Bewegung, Pickups oder Laserfarben nicht stillschweigend eingeschaltet,
wenn eine Abhängigkeit fehlt. Die Pickup-Beschreibung verspricht keine Punkte
auf Karten ohne Layout. Die gesamte Bedienung ist ab Arm bzw. im Raid gesperrt.
Es werden keine Spielmechanik- oder Profilpfade verändert.

Rechts oben stehen Kartenname und natives Thumbnail ohne Beschreibung oder
Vanilla-Raidstatistiken. Das Thumbnail übernimmt die asynchron geladene native
Sprite-Referenz, bewahrt deren Seitenverhältnis und besitzt/zerstört das Asset
nicht. Bei ausstehendem Bild oder fehlender Kartenauswahl bleibt die Bildfläche
leer, statt das vorige Thumbnail zu zeigen. Customs zeigt dort seine Arealkarte
mit AREA/OVERVIEW. Beide Ansichten reservieren dieselben 320 Referenzpixel Höhe;
die Mutatoren bleiben unabhängig von Karte und Modus fest auf Y=508.
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
Text. Normierte Markerpositionen: Bay 5 (0,47 / 0,82), Equator (0,70 / 0,29),
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

Native Prüffolge: Arena > Kartenauswahl; acht sofort sichtbare Schalter, ON/OFF
und Farbwechsel mit F12 vergleichen, Killcam pro Modus und Laser/Pickup-N/A
prüfen; Factory-Zeitwahl und Customs-Preview ohne Überlappungen, Escape und
gewöhnlichen PMC-Bildschirm auf vollständige Restaurierung prüfen. Keine neue Raid-Abnahme nötig,
um die unveränderten Gameplaypfade zu beschreiben; deren Funktion ist mit diesem
Menütest aber nicht erneut bestätigt.
