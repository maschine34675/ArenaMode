# Killcam pro Arena-Modus

Stand: 30.09.2026. Native Referenz ist die lokale KillcamReplay-Implementierung
0.9.12 in `../KillcamReplay`, insbesondere `DeathReplayPatch`,
`ReplayController`, `HoldRespawnPatch` und `Plugin.DeathReplayActive`.
KillcamReplay bleibt eine optionale, separat installierte Mod; keine neue DLL
oder fremder Quellcode wird gebündelt.

## Auswahl und Lebensdauer

Im Kartenauswahlbildschirm steht rechts neben MATCH RULES ein Killcam-Schalter.
Die Wahl wird getrennt für Deathmatch, Gun Game, Team Deathmatch und Tactical
gespeichert. Einschalten wählt drei Sekunden Replay, Ausschalten null; die
jeweilige F12-Einstellung `ReplaySeconds` erlaubt weiterhin 0–10 Sekunden.
Erneutes Einschalten am Menüschalter setzt bewusst wieder drei Sekunden.
Deathmatch/Gun Game/Team Deathmatch bleiben standardmäßig aus; Tactical behält
seinen bisherigen Standard von drei Sekunden und vorhandene Konfigurationen.

Ohne verfügbare und aktivierte KillcamReplay-Mod zeigt der Schalter N/A und
erklärt den Grund im Tooltip. `KillAndDamageInfo` deaktiviert dort DeathReplay
ebenfalls. Kein Schreiben in die fremde Konfiguration und keine Serverroute.
Die wirksame Dauer wird beim erfolgreichen Arena-Arm eingefroren und bei
Abbruch, Ablehnung oder Raidende gelöscht. F12-Änderungen im laufenden Match
ändern weder dessen Replaylänge noch seine Respawnfreigabe. Ein bereits
ausrangierter Menü-Callback darf keinen neu geöffneten Bildschirm verändern.

## Kamera- und Respawnbesitz

KillcamReplay setzt den ausstehenden Killer synchron im nativen Todes-Postfix,
bevor Arena die spätere native Todes-/Respawnbehandlung übernimmt. Sichtbares
Playback beginnt erst nach 0,8 Sekunden. Deshalb reicht `IsPlaying` allein
nicht: ausstehendes Replay, laufendes Playback und noch nicht abgeschlossene
Kamerarückgabe (`!ReadyForStop`) zählen alle als belegt. Arena fragt diese
vorhandenen öffentlichen Zustände nur lesend ab.

Der neue Hold bindet das genaue Match, den Raid-Epoch und den toten lokalen
Körper. Modus-Hold und Kamera-Wartezeit laufen gleichzeitig; erst wenn beide
freigeben, darf der vorhandene Respawnweg fortfahren. Nach asynchronem
Kit-Preload wird nochmals geprüft. Tactical wartet weiterhin auf den
Rundenwechsel, PFT behält seine einmalige Rebind-Autorisierung und seinen
eigenen Beobachter. Terminale Ergebnisse und geschützte Raid-Ausgänge bleiben
unverändert. Bei abgeschalteter Killcam durchläuft der Driver weiterhin seinen
bisherigen Weg ohne zusätzliche verschachtelte Coroutine.

Es gibt keine pauschale Drei-Sekunden-Pause: Ohne Aufnahme geht es direkt
weiter; Überspringen gibt frei, sobald die Mod tatsächlich aufgeräumt hat.
Replay-Länge, native Zeitlupe, Nachlauf und Fade können die tatsächliche
Wartezeit verlängern. Eine einmalige Frist aus `GetExtraDelay()+5` (5–75
Simulationssekunden) verhindert unbegrenztes Warten auf defekte Zustände.
Pause verbraucht diese Frist nicht. Ein unbekannter Besitzer oder Fristablauf
autorisiert niemals einen Ersatzkörper, sondern den bestehenden geschützten
Abbruch. `StopPlayback` kann die ausstehende Vorlauf-Coroutine nicht sicher
abbrechen und wird daher nicht zur vermeintlichen Freigabe aufgerufen.
Auch `HoldReplacementOf` wird nicht benutzt: dessen schwarzer Übergang wartet
selbst auf einen Ersatzkörper und würde mit einem Warten auf Idle kollidieren.

Nach abgeschlossener Bot-Retirement wartet PFT vor der Loadout-Vorbereitung und
Respawn-Autorisierung ebenfalls auf bestätigte Kamerafreigabe. Erst danach
beginnen seine 45 Sekunden für den Ersatzkörper; eine erlaubte Kombination aus
langem Replay, Zeitlupe und Nachlauf verbraucht diese Frist nicht vorzeitig.
Ein lebender Teamführer durchläuft diesen zusätzlichen Guard nicht.

Die bestehende Bereinigung der visuellen Gesundheitseffekte wird auch während
Nicht-PFT-Replays verwendet. Ihr neuer Eintritt verlangt einen aktiven Arena-
Raid, aktivierten eingefrorenen Replaywert, den exakt toten lokalen Körper in
Spiel und Welt sowie eine tatsächlich belegte Replaykamera. Der vorhandene
exakte EffectsController-Körpervergleich bleibt bestehen. Nur die Darstellung
wird zurückgesetzt; Gesundheit, Effekte im HealthController und Kamerabesitz
bleiben unberührt. Der bisherige PFT-Eintritt behält seine ursprünglichen Gates.

## Prüfungen und verbleibende Spielabnahme

`tools/ArenaKillcamChecks/Verify.ps1` kompiliert echte Hold-, Frist-, Snapshot-
und Reflection-Klassen gegen schmale externe Grenzen. Die Fehlerkontrollen
müssen ihre konkrete Laufzeitassertion verfehlen; Compilerfehler zählen nicht.
`tools/TdmMatchMenuChecks` kompiliert die echten Konfigurations-/Menüklassen und
prüft modusspezifische Speicherung, Raid-Sperren, fehlende Mod und alte Callbacks.
`Verify-Integration.ps1` ergänzt bestehende Deathmatch-, TDM-, Aufgeben- und
PFT-Regressionsprüfungen sowie einen Build gegen die installierten Spieltypen,
ohne automatisch zu deployen.

Gezielte Läufe am 30.09.2026: Replay **170 Assertions / acht Fehlerkontrollen**,
Menü **121 / zwölf**, visuelle Gesundheits-Scope **80 / neun**, jeweils mit
wiederhergestelltem Produktionslauf. Build gegen die installierten Spieltypen
erfolgreich; vier bestehende Nullable-Warnungen im Shared-Modell. Das unabhängige
Review fand die beiden oben beschriebenen P2-Randfälle (Gesundheitsdarstellung
und lange PFT-Replays); beide sind behoben und nachgeprüft. Keine offenen P0–P2.
Die vollständige abschließende Regression und die saubere Build-/Disk-Auslieferung
werden mit Quellcommit und Hash im Handoff gebunden.

Das ersetzt keine echte Kamera-/Respawn-Abnahme. Im Spiel je Modus ON/OFF,
frühes Überspringen, Tod ohne Aufnahme (Umgebung/Suizid), Tod kurz vor dem
Matchende, Escape/Aufgeben während Replay und PFT-Rundenwechsel prüfen. Die
Ansicht muss nach Replay zum neuen Körper bzw. zum bestehenden PFT-Beobachter
wechseln; gewöhnliche PMC-/Scav-Raids müssen unverändert bleiben. Ergebnisse,
Build-ID und Deployment werden separat im Handoff dokumentiert.
