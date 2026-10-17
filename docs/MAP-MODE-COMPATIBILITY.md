# Karten und Modi – Stand 17.10.2026

Die gemeinsame Auswahlprüfung gilt im Menü, beim Armieren, bei Wiederholung und
in der Server-Vorbereitung. Historische pauschale Sperren sind entfernt; konkrete
Grenzen der Spawn- und Arealverwaltung bleiben erhalten.

| Karte | Deathmatch / Gun Game | Team DM | Tactical solo / PFT | Zombies solo / PFT |
|---|---|---|---|---|
| Factory Tag/Nacht, Labs, Labyrinth | Ja | Ja | Ja | Ja |
| Bay5, Air Pit | Ja | Ja | Ja | Ja |
| Equator | Ja | Ja | Nein: fünf entfernte Gegnerstarts nicht überall möglich | Solo ja; PFT nein: zu wenig Platz für den Fünfertrupp mit Zombie-Welle |
| Customs, ganze Karte | Nein | Nein | Nein | Nein |
| Drei begrenzte Customs-Areale | Ja, mit Teilnehmerzahl | Ja, 2v2–5v5 | Ja, standardmäßig 1v5 / PMC + 1–4 Begleiter gegen fünf | Ja, solo / PMC + 1–4 Begleiter |

PFT setzt weiterhin die unterstützte Version, 1–4 eingeladene Begleiter und die
geschützte Ausrüstung voraus. Portierte Karten brauchen ArenaMapPort auf Client
und Server; individuelle Karten-Allowlisten bleiben verbindlich. Andere EFT-Karten
werden durch diese Änderung nicht freigegeben.

Für ganze Karten verwenden die Modi überwiegend die bestehenden generischen
Spawnpunkte. Ausnahme: Equator-Team-DM nutzt die ursprünglichen Nord-/Süd-Teamboxen
mit eigenen Arena-Markern für Einstieg und Respawns; siehe
[Equator-Teamstarts](EQUATOR-TEAM-SPAWNS-REVIEW.md). Auf den übrigen ganzen Karten
braucht Team DM keine vermessenen Fraktionsstarts wie die begrenzten Areale.
Mit aktueller ArenaMapPort-Version bleiben Equators originale Türen während
Vorbereitung und Countdown geschlossen. Der Kampf beginnt erst nach Öffnung
und erfolgreicher Wegeprüfung; bei laufenden Respawns bleiben die Türen offen.
Siehe [Spawn-Tore und Vorbereitungsschutz](EQUATOR-SPAWN-GATES-20261017.md).
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
umfasst kein neues Pickup-Layout; siehe [Air-Pit-Integration](AIRPIT-INTEGRATION-20261017.md).

Quellprüfung, Daten und Offline-Nachweise: `CUSTOMS-ALL-MODES-REVIEW.md`,
`PFT-PORTED-MAPS-REVIEW.md` und die ursprüngliche Analyse `MAP-MODE-ACCESS-REVIEW.md`.
Native Abnahme ist separat im aktuellen GPU-Bericht/Handoff dokumentiert; die
Freigabematrix behauptet keinen vollständigen Dauertest aller Kombinationen.

Hinweis für Testautomatisierung: Die externe UiBridge-`gpu-run raid`-Tabelle vom
17.10.2026 kennt AirPit noch nicht. Neue Kombinationen bis zu deren
Aktualisierung über die echten Menübuttons auswählen; ihre alte Ablehnung ist
keine Arena-Laufzeitsperre. Bay5-Zeiten und Equator-Wetter bleiben ArenaMapPort-Sache.
