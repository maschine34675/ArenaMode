# Karten und Modi – Stand 09.10.2026

Die gemeinsame Auswahlprüfung gilt im Menü, beim Armieren, bei Wiederholung und
in der Server-Vorbereitung. Historische pauschale Sperren sind entfernt; konkrete
Grenzen der Spawn- und Arealverwaltung bleiben erhalten.

| Karte | Deathmatch / Gun Game | Team DM | Tactical solo / PFT | Zombies solo / PFT |
|---|---|---|---|---|
| Factory Tag/Nacht, Labs, Labyrinth | Ja | Ja | Ja | Ja |
| Bay5 | Ja | Ja | Ja | Ja |
| Equator | Ja | Ja | Nein: fünf entfernte Gegnerstarts nicht überall möglich | Ja |
| Customs, ganze Karte | Ja | Ja | Ja | Ja |
| Drei begrenzte Customs-Areale | Nein: Area-Anbindung fehlt | Ja, 2v2–5v5 | Nein: Area-Anbindung fehlt | Nein: Area-Anbindung fehlt |

PFT setzt weiterhin die unterstützte Version, vier eingeladene Begleiter und die
geschützte Ausrüstung voraus. Portierte Karten brauchen ArenaMapPort auf Client
und Server; individuelle Karten-Allowlisten bleiben verbindlich. Andere EFT-Karten
werden durch diese Änderung nicht freigegeben.

Für ganze Karten verwenden die Modi die bestehenden generischen Spawnpunkte.
Team DM braucht dort keine vermessenen Fraktionsstarts wie die begrenzten Areale.
Bay5 hat auch abseits der Spielerstarts genügend Punkte für den Fünfertrupp.
Equator hat zwar genügend Punkte am ursprünglichen Spielereinstieg, aber nur vier
mindestens 25 m entfernte Botpunkte vom Atrium (0, 0.2, 2). Ein überlebender
PFT-Spieler bleibt beim Rundenwechsel am Ort; deshalb wäre die nächste
Fünferformation nicht zuverlässig möglich. Die Sperre wird nicht durch einen
kleineren globalen Sicherheitsabstand oder erfundene Startpunkte umgangen.

`ArenaAreaRuntime` aktiviert Wände, Navigation und reservierte Area-Respawns
ausdrücklich nur für unabhängiges Team DM. Andere Modi für die Konturen zu öffnen
würde diese Schutzmechanik auslassen. `ArenaParticipantPolicy` trennt weiterhin
ganze Karten mit Teilnehmerzahl von Arealen mit Teamgröße. Auf Customs bleibt die
gespeicherte gültige Kontur erhalten; „Whole map“ ist jetzt eine echte vierte Wahl.

Quellprüfung, Daten und Offline-Nachweise: `MAP-MODE-ACCESS-REVIEW.md`.
Native Abnahme ist separat im aktuellen GPU-Bericht/Handoff dokumentiert; die
Freigabematrix behauptet keinen vollständigen Dauertest aller Kombinationen.

Hinweis für Testautomatisierung: Die externe UiBridge-`gpu-run raid`-Tabelle vom
06.10.2026 kennt die neuen Freigaben noch nicht. Neue Kombinationen bis zu deren
Aktualisierung über die echten Menübuttons auswählen; ihre alte Ablehnung ist
keine Arena-Laufzeitsperre. Bay5-Zeiten und Equator-Wetter bleiben ArenaMapPort-Sache.
