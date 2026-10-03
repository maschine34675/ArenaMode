# Karten und Modi – Stand 03.10.2026

Eine spätere Freigabe weiterer Kombinationen ist grundsätzlich möglich. Die
aktuellen Sperren sind aber Teil gemeinsamer Client-/Server-Verträge und dürfen
nicht nur im Menü entfernt werden. Diese Prüfung ändert keine Freigabe.

| Karten | Bereits erlaubt | Voraussetzung für weitere Modi |
|---|---|---|
| Factory Tag/Nacht, Labs, Labyrinth | Deathmatch, Gun Game, Tactical, Team DM | Keine zusätzliche Modussperre |
| Bay5, Equator | Deathmatch, Gun Game | Geprüfte Teamstarts/Respawns; Tactical zusätzlich vollständige Gegnerformation |
| Drei Customs-Areale | Unabhängiges Team DM, 2v2–5v5 | Area-Lifecycle und Besetzung vom Teammodus entkoppeln |
| Ganz Customs und übrige EFT-Karten | Nicht im Arena-Katalog | Eigene Kartenintegration, Spawns/Zonen, Population und Navigation prüfen |

`ArenaMode.Shared/ArenaAreaCatalog.cs` prüft dieselbe Auswahl im Menü, beim
Armieren, bei Wiederholung und in der Server-Vorbereitung. `ArenaAreaRuntime`
aktiviert Wände, Navigation und Area-Respawns derzeit ausdrücklich nur in TDM;
eine bloße Allowlist-Änderung würde diese Schutzmechanik in Einzelmodi auslassen.
`ArenaParticipantPolicy` schließt begrenzte Areale vom Solo-Teilnehmermodell aus,
weil diese bisher ihre Teamgröße verwenden.

`BurstSpawnPointSelector`/`BurstSpawnService` benötigen für Tactical fünf
verschiedene gültige Gegnerpositionen, mindestens 25 m vom Spieler entfernt.
Ein älterer Equator-Test dokumentiert nur vier entfernte Punkte vom Atrium aus;
das ist ein Prüfhinweis, kein Nachweis für die aktuelle Szene. Insbesondere dort
sollte eine neue Startformation vermessen werden, statt den globalen Abstand
ungeprüft zu senken. Noch fehlende Teamabnahmen stehen in
`BAY5-MAP-REVIEW.md` und `EQUATOR-MAP-REVIEW.md`.

Sinnvolle Reihenfolge: Team DM auf Bay5/Equator (Equator zunächst 2v2 als
Voreinstellung), danach Tactical erst solo und anschließend mit PFT samt
Rundenwechsel. Für Customs zunächst die Area-Laufzeit für DM/Gun Game
verallgemeinern, anschließend Tactical. Große zusätzliche Karten vorzugsweise
über vermessene kleine Areale erschließen; ganze Karten brauchen zusätzlich
passende Such-/Bewegungsdistanzen, damit die Begegnungen nicht ausbleiben.

Bay5-Zeiten und Equator-Wetter gehören zur tatsächlichen Kartenimplementierung
in ArenaMapPort. Die zugehörigen Änderungen erweitern keine Arena-Modusfreigabe.
