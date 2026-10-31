# Karten und Modi – Stand 31.10.2026

Die gemeinsame Auswahlprüfung gilt im Menü, beim Armieren, bei Wiederholung und
in der Server-Vorbereitung. Historische pauschale Sperren sind entfernt; konkrete
Grenzen der Spawn- und Arealverwaltung bleiben erhalten. Tabelle am 31.10.2026 um
Bowl, Iceberg und Domination ergänzt; der übrige Text ist der Stand vom 17.10.2026,
wo nicht anders vermerkt.

| Karte | Deathmatch / Gun Game | Team DM | Domination | Tactical solo / PFT | Zombies solo / PFT |
|---|---|---|---|---|---|
| Factory Tag/Nacht | Ja | Ja | Ja, drei von acht Punkten je Match | Ja | Ja |
| Labs, Labyrinth | Ja | Ja | Nein | Ja | Ja |
| Bay5, Air Pit | Ja | Ja, Teamboxen 2v2–10v10 | Ja, Bay5 ein Punkt, Air Pit zwei | Ja | Ja |
| Bowl | Ja | Ja, Teamboxen 2v2–10v10 | Ja, zwei Punkte | Ja | Ja |
| Iceberg (unveröffentlichter ArenaMapPort-Build nötig) | Ja | Ja, Teamboxen 2v2–10v10 | Ja, drei Punkte (A/B/C) | Nein: von der Kartenmitte nur vier entfernte Botpunkte | Ja / Ja |
| Equator | Ja | Ja, Teamboxen 2v2–10v10 | Ja, ein Punkt | Nein: fünf entfernte Gegnerstarts nicht überall möglich | Solo ja; PFT nein: zu wenig Platz für den Fünfertrupp mit Zombie-Welle |
| Customs, ganze Karte | Nein | Nein | Nein | Nein | Nein |
| Drei begrenzte Customs-Areale | Ja, mit Teilnehmerzahl | Ja, 2v2–5v5 | Nein | Ja, standardmäßig 1v5 / PMC + 1–4 Begleiter gegen fünf | Ja, solo / PMC + 1–4 Begleiter |

Domination gibt es nie mit PitFireTeam und nur auf ganzen Karten; sie braucht
BigBrain und Arenas Begleitbibliothek. Auf den portierten Karten nutzt sie dieselben
Teamboxen und Spawn-Tore wie das unabhängige Team DM.

PFT setzt weiterhin die unterstützte Version, 1–4 eingeladene Begleiter und die
geschützte Ausrüstung voraus. Portierte Karten brauchen ArenaMapPort auf Client
und Server; individuelle Karten-Allowlisten bleiben verbindlich. Andere EFT-Karten
werden durch diese Änderung nicht freigegeben.

Für ganze Karten verwenden die Modi überwiegend die bestehenden generischen
Spawnpunkte. Ausnahme: Equator-Team-DM nutzt die ursprünglichen Nord-/Süd-Teamboxen
mit eigenen Arena-Markern für Einstieg und Respawns; siehe
[Equator-Teamstarts](reviews/EQUATOR-TEAM-SPAWNS-REVIEW.md). Auf den übrigen ganzen Karten
braucht Team DM keine vermessenen Fraktionsstarts wie die begrenzten Areale.
Mit aktueller ArenaMapPort-Version bleiben Equators originale Türen während
Vorbereitung und Countdown geschlossen. Der Kampf beginnt erst nach Öffnung
und erfolgreicher Wegeprüfung; bei laufenden Respawns bleiben die Türen offen.
Siehe [Spawn-Tore und Vorbereitungsschutz](reviews/EQUATOR-SPAWN-GATES-20261017.md).
Bay5 hat auch abseits der Spielerstarts genügend Punkte für den Fünfertrupp.
Equator hat zwar genügend Punkte am ursprünglichen Spielereinstieg, aber nur vier
mindestens 25 m entfernte Botpunkte vom Atrium (0, 0.2, 2). Ein überlebender
PFT-Spieler bleibt beim Rundenwechsel am Ort; deshalb wäre die nächste
Fünferformation nicht zuverlässig möglich. Die Sperre wird nicht durch einen
kleineren globalen Sicherheitsabstand oder erfundene Startpunkte umgangen.

Zombies mit PFT sind auf Equator ebenfalls ausgeschlossen: Der kompakte Spielraum
ist für den Fünfertrupp zusammen mit den Zombies zu eng. Solo-Zombies bleiben
freigegeben. Die PFT-Sperre gilt auch mit weniger als vier Begleitern. Diese
gemeinsame Auswahlregel gilt auch für gespeicherte Auswahlen,
wiederholte Arm-Anfragen und Server-Vorbereitung; eine Equator-only-Allowliste
bietet folglich keinen PFT-Modus an. Normale PMC-Raids bleiben unverändert.

`ArenaAreaRuntime` aktiviert Wände, Navigation und reservierte Respawns in allen
Arena-Modi. Erst nach der erfolgreichen Navigationsprüfung beginnen Match,
Modus-Timer und Ausrüstungswechsel. PFT wartet ebenfalls und reserviert für seine
1–4 Begleiter getrennte, überprüfte Punkte innerhalb der Kontur. Der native
PFT-Ablauf und die authentifizierten Profile bleiben erhalten.

Unabhängiges Team DM zeigt die Teamgröße: auf Customs 2v2–5v5, in Equators
Teamboxen bis 10v10. Deathmatch und Gun Game behalten ihre Teilnehmerzahl.
Tactical und Zombies verwalten ihre Besetzung selbst.
Auf Customs bleibt eine gültige gespeicherte Kontur erhalten. Eine alte
Ganzkarten-Präferenz wird bei der Menüauswahl auf das erste Areal umgestellt;
ein endgültiger Raid-Start ohne gültige Customs-Kontur wird abgelehnt. Normale
PMC-Raids sind davon nicht betroffen.

Bay5, Equator und Air Pit besitzen gültige KI-Anker, aber keine nativen Deckungspunkte.
Der optionale PFT-Adapter ermittelt deshalb in genau diesen geschützten Arena-Raids
den nächstgelegenen vorhandenen Anker direkt. Er verändert weder PFT-Dateien noch
Kartengeometrie und erfindet keine Deckungspunkte. Die separate Tactical-Sperre
auf Equator bleibt bestehen, auch wenn weniger als vier Begleiter eingeladen sind.

Air Pit verwendet seine vier vorhandenen Spieler- und 14 Bot-Spawns. Eine neue
Kartenstandard-Migration ergänzt nur erkannte frühere Standardlisten; individuelle
Listen und ausdrücklich entfernte oder deaktivierte Karten bleiben erhalten.
Die ursprüngliche nördliche Air-Pit-Teambox liegt außerhalb der aktuellen
Navigationsfläche und wird nicht als fester Teamstart übernommen. Team DM und
Tactical verwenden hier zunächst die allgemeinen Spawnverfahren. Die Freigabe
umfasst kein neues Pickup-Layout; siehe [Air-Pit-Integration](reviews/AIRPIT-INTEGRATION-20261017.md).

Nachtrag 31.10.2026: Air Pit nutzt inzwischen seine ursprünglichen Nord-/Süd-Teamboxen
für Team DM (ArenaMapPort 0.2.2 oder neuer, siehe
[Air-Pit-Teamboxen](reviews/AIRPIT-TEAM-BOXES-REVIEW.md)); der Absatz oben ist der Stand
vom 17.10.

Bowl ist in allen Modi freigegeben; Team DM und Domination nutzen die ursprünglichen
Boxen USEC Ost / BEAR West hinter den Spawn-Toren. Siehe
[Bowl-Freigabe](reviews/BOWL-INTEGRATION-20261023.md) und
[Bowl-Teamboxen](reviews/BOWL-TEAM-BOXES-20261023.md).

Iceberg braucht einen ArenaMapPort-Build, der `arena_iceberg` mitbringt; das ist
derzeit nur der unveröffentlichte `feature/iceberg`-Build 0.4.0+32fda33. Das
veröffentlichte 0.4.0 hat kein Iceberg und dieselbe Versionsnummer; ArenaMode erkennt
die Karte deshalb nur an der vorhandenen, aktivierten Location, nie an der Version.
Tactical ist auf Iceberg solo und mit PFT gesperrt: Von etwa 48 m² in der Kartenmitte
aus liegen nur vier der neun Botpunkte weiter als 25 m entfernt, also derselbe Mangel
wie auf Equator. Zombies (solo und PFT) bleiben freigegeben, die Karte ist nicht eng.
Team DM und Domination nutzen die CheckPoint-Taschen USEC West / BEAR Ost, auf die
geschlossenen Tore zugeschnitten; Domination hat drei Punkte A/B/C. Ein nativer
GPU-Test steht noch aus. Siehe [Iceberg-Integration](reviews/ICEBERG-INTEGRATION-20261031.md).

Quellprüfung, Daten und Offline-Nachweise: `CUSTOMS-ALL-MODES-REVIEW.md`,
`PFT-PORTED-MAPS-REVIEW.md` und die ursprüngliche Analyse `MAP-MODE-ACCESS-REVIEW.md`.
Native Abnahme ist separat im aktuellen GPU-Bericht/Handoff dokumentiert; die
Freigabematrix behauptet keinen vollständigen Dauertest aller Kombinationen.

Hinweis für Testautomatisierung: Die externe UiBridge-`gpu-run raid`-Tabelle vom
17.10.2026 kennt AirPit noch nicht. Neue Kombinationen bis zu deren
Aktualisierung über die echten Menübuttons auswählen; ihre alte Ablehnung ist
keine Arena-Laufzeitsperre. Bay5-Zeiten und Equator-Wetter bleiben ArenaMapPort-Sache.
