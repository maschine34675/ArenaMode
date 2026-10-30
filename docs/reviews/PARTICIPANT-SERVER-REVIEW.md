# Feste Teilnehmerzahl: Serververtrag

Stand: 2026-09-21. Dieser Teil ergänzt die Aushandlung und den Raid-Klon;
die tatsächliche Bot-Aufnahme und das native Menü werden separat geprüft.

## Verhalten

`participants` ist eine optionale ganze Zahl einschließlich des reservierten
lokalen Spielers. Die gemeinsame `ArenaParticipantPolicy` erlaubt Deathmatch
und Gun Game mit 2–24 sowie ganze Karten in Team Deathmatch mit geraden 4–24.
Begrenzte Team-Areale, Tactical und PitFireTeam nehmen keinen solchen Override
an. Ein fehlender oder explizit null gesetzter Wert behält den alten Vertrag.

Der Router lehnt ungültige Werte vor Snapshot-Wiederherstellung oder neuer
Vorbereitung ab. Die Session speichert die validierte Zahl beim Armieren bzw.
bei der Vorbereitung. Antwort, Status und Antwortwiederholung liefern dieselbe
Zahl. Eine geänderte oder nachträglich ausgelassene Zahl mit demselben
Request-Token ist keine identische Wiederholung und wird abgewiesen.

`participantBosses` wird ausschließlich serverseitig einmal eingefroren:
explizite Teilnehmerzahl, Deathmatch/Gun Game, Labyrinth und aktivierte Bosse in
der aktivierten Kartenkonfiguration. Eine verlorene Antwort oder ein späterer
Config-Reload berechnet die Zusage nicht neu. Der Client verwaltet damit einen
Bossplatz innerhalb der gewählten Zahl; die nativen Bosswellen bleiben leer.
Session-Zahl und Bosszusage leben durch Fehlerbehandlung und End-Finalisierung
bis zum tatsächlichen Idle-Übergang und werden dort freigegeben.

## Klon und Freigabe

Nur der bereits konsumierten Arena-Session zugeordnete Raid-Klon wird verändert.
Bei expliziter Zahl erhalten `BotMax`, `BotMaxPvE` und `MaxBotPerZone` exakt
`participants - 1`. Wellen und Boss-/Escort-Einträge werden entfernt, beide
Continuous-Spawn-Flags deaktiviert, Spawn-Fenster geschlossen und beide
Non-Wave-Limits auf null gesetzt. Ein fehlender Modifier wird dafür angelegt;
ein vorhandener Modifier wird kopiert. Wellen werden vor der Lab-Zonenfilterung
entfernt, damit auch versehentlich geteilte Schedule-Objekte unverändert bleiben.

`ArenaGenerateLocationAndLootPatch` verlangt bei expliziter Zahl bereits vor der
Mutation passende Session-, Callback- und Klon-Kartenidentitäten. Fehlende,
abgeschaltete oder unpassende Karten und fehlgeschlagene Mutationen werden als
fehlende Freigabe gespeichert; sie dürfen nicht mit Vanilla-Spawns weiterlaufen.

`ValidateAreaResponse` verlangt nun auch bei ausgewählten ganzen Karten dieselbe
Objektidentität des erfolgreich veränderten Klons. Die finale Validierung prüft
Karten-ID, alle drei Caps, leere Wave-/Bosslisten, geschlossene Pumpen und Limits
erneut. `ConfirmClientRaidStarted` wiederholt diese Prüfung vor der
Client-Aktivierung. Damit genügt ein früherer erfolgreicher Mutationsschritt
nicht, wenn ein anderer Hook den zurückgegebenen Klon später verändert.

## Nachweis und Grenzen

`tools/ParticipantServerChecks/Verify.ps1` führt die neuen produktionsgebundenen
Route-/Session-/Klonfälle sowie vorhandene Location-, Deathmatch- und
Team-Deathmatch-Serverprüfungen aus. Der lokale Selbstlauf bestand 126 Tests ohne
Skip. Vier separat kompilierte Fehlerkontrollen wurden an ihren vorgesehenen
Verhaltensprüfungen erkannt: ignorierte Wiederholungszahl (7 Fälle), ignorierte
Antwort-Klonidentität (3), fehlende finale Validierung (20), weiter geöffnete
Deathmatch-/Gun-Game-Spawns (4; der unveränderte TDM-Kontrollfall bestand).

Der Runner erzeugt isolierte .NET-Artefakte und nutzt eine ausdrücklich nicht
existierende `SPTUserMods`-Sentinel-Adresse. Installierte Arena-Client-,
Arena-Server- und Redeploy-Core-Hashes sowie die Produktionsquelltexte werden
vor und nach dem Lauf verglichen. Ein Compilerfehler zählt nicht als erfolgreich
erkannte Fehlerkontrolle. TRX-Dateien, Protokolle, Quellenbindungen und Receipt
liegen privat unter `artifacts/participant-server/<run>/`.

Die Eigenprüfung fand und behob zusätzlich die mögliche Alias-Mutation durch
den Lab-Zonenfilter; dafür besteht ein eigener Regressionstest. Diese Ergebnisse
beweisen keine native Spawn-Aktivierung, Boss-KI, Spielbarkeit oder GPU-Abnahme.
Es wurde nichts installiert, kein Raid gestartet und kein Commit erstellt.
