# Bunnyhop – GPU-2, 2026-10-05

## Ausgelieferter Stand

Client-Quellstand `6356bef` (auf `93b8ba6`, `2d3f8f7`, `5a6b90b`).
SHA256 `5674CAF6746E453246B9DB76D773526AD3A1D649E894E8B8404ED118344034FF`.
Lokal und auf SPT-GPU-2 installiert; Server, Redeploy.Core und BigBrainCompat
unverändert. Auslieferungsbeleg: `BUNNYHOP-DEPLOYMENT-20261005.json`.

EFT 0.16.9.5 / Unity 2022.3.43f1, Profil SPT-GPU-2, 1920×1080.
Finaler Prozess `e34bb467c7544d80b606b32c5c0107f6`, gestartet 13:16:22 MESZ.
UiBridge 0.7.4+8218b027; 24/24 Mod-Identitäten zwischen lokalem Build,
Host-Dateien und geladenem Prozess übereinstimmend, auch nach dem Raid.
Keine Laufzeitpatches oder manipulierten Bewegungszustände durch den Test.
UiBridge lieferte native Jump-Kommandos und NavMesh-Laufsteuerung; Teleports
dienten ausschließlich dem Aufstellen vor einzelnen Versuchen. Godmode war
während der Messung aktiv, ein Tod wurde ausdrücklich für den Respawn ausgelöst.

## Beobachtungen

| Prüfung | Ergebnis |
|---|---|
| Mutator-Menü | BUNNYHOP sichtbar, ON/OFF ändern denselben F12-Wert; kein Überlappen mit READY. Visuell am ersten Build geprüft, Layout im finalen Build unverändert. |
| Sprint-Kette, Factory | Zwei Folgesprünge bestätigt; native Debug-Ereignisse #3041/#3044: 6,29 → 6,67 m/s Carry, Kettenlimit 9,01 m/s. |
| Geh-Kette, Factory | Drei Folgesprünge bestätigt; #3097/#3101/#3106: 3,28 → 3,47 → 3,68 m/s, Limit 4,56 m/s. |
| Frühe Eingabe | Kein Bunnyhop; die vorhandene DoubleJump-Ansage im Debug-Log bleibt aktiv. |
| Bunnyhop OFF | Normale Sprünge weiterhin möglich; keine einzige Flight-Instanz in 455 Stichproben. |
| Movement-Master OFF | Ebenfalls keine Flight-Instanz; beide Einstellungen danach zurückgestellt. |
| Respawn | Alter Flight-Zustand leer. Auf der äußeren ebenen Spawnfläche anschließend wieder ein Folgesprung mit Carry bestätigt. |
| Geschützter Ausstieg | MainMenu erreicht; keine Ausrüstung, Erfahrung, Skills, Quests, Händlerstände oder Untersuchungseinträge verändert. Gesundheit einschließlich Effekten unverändert, nur Health.UpdateTime fortgeschrieben. |

Die verwinkelte innere Route lieferte nach dem Respawn zunächst keine Kette;
sie enthält Hindernisse und Höhenwechsel, der Rückweg lieferte gar keine
rechtzeitige Folgeeingabe. Diese Versuche werden nicht als Erfolg gezählt.
Der erfolgreiche Außenversuch ist separat erfasst. Kein lückenloser Beweis
für beliebige Terrainkanten oder sämtliche zeitlichen Grenzfälle.

Die ersten beiden Builds offenbarten echte Fehler: SimpleCC meldet selbst auf
ebenem Boden nicht durchgehend sein letztes Depenetrationsbit. Zunächst wurde
dadurch der erste Flug nicht erfasst, danach der Folgesprung nach bereits
bestätigter Landung blockiert. Beide Fälle korrigiert und mit nativer Messung
wiederholt. Ein zunächst zu schwerer Testinspektor verpasste zusätzlich das
150-ms-Fenster; die abschließenden Läufe priorisieren die Eingabe vor Detailreads.

Die kompakte Messdatei `GPU-BUNNYHOP-RESULTS-20261005.json` enthält Stichproben-
zahlen, erfolglose Versuche, native Hop-Ereignisse und SHA256 der Rohmessungen.
Rohdaten und Screenshots liegen lokal unter `artifacts/bunnyhop-gpu/`.

## Prüfung und Grenzen

76 produktionsgebundene Assertions plus 13 kompilierte Fehlerkontrollen bestanden.
DoubleJump, AirControl samt installierten IL-Verträgen, Handling und Mutator-Menü
bestanden erneut auf dem finalen Code. Isolierter Client-Build erfolgreich:
vier bestehende Nullable-Warnungen, keine Fehler. Unabhängiger adversarial
Review: keine offenen P0–P2. Kontakterfassung, verzögerte Kontaktbeobachtung,
DoubleJump-Kredit und JumpBoots-Kombination sind offline abgedeckt.

Nicht als nativ abgenommen gelten: JumpBoots-Kombination, spezielle Treppen/
Schrägen, Vault während einer Kette und ein gewöhnlicher PMC-Raid. Hier bestehen
Quell-/Regressionsevidenz, keine neue Ingame-Abnahme. Subjektives Timing und
Spielgefühl bleiben sinnvoller Gegenstand des Nutzertests. Native Landeanimation
und ihre kurze Verzögerung bleiben absichtlich erhalten; kein automatisches
Dauerspringen durch gehaltene Leertaste.

Finales Log: #1–3505, keine Error/Fatal-Einträge und kein Bunnyhop-Fehler.
Ein bereits gekürzter fremder Logeintrag begrenzt die vollständige Textabdeckung.
Andere Hinweise bleiben separat: nicht unterstütztes Kill-Hitmarker-PCM,
absichtlich werfender RespawnApiProbe-Abonnent, Harmony-/Szenenwarnungen.
Alle sechs Factory-Pickups wurden von ihrem unterirdischen Startanker wegen
Pfad-/Boden-/Freiraumprüfungen übersprungen; keine Pickup-Abnahme in diesem Lauf
und keine Ursache durch Bunnyhop nachgewiesen. Nicht im Rahmen dieses Features
geändert.

Profilvergleich bindet ausschließlich die richtige aktive Profildatei vor und
nach dem **finalen** Raid. Die ältere Sicherung der Vorversuche erwischte eine
Synchronisations-Konfliktkopie und wird ausdrücklich nicht als Beweis verwendet.
Bestehende Arena-Configwerte unverändert; nur neuer Schlüssel BunnyhopEnabled=false.
Godmode/Laufsteuerung beendet, EFT und der für diese Tests gestartete Server aus.

## Bedienung

BUNNYHOP im Mutatoren-Kasten einschalten (Standard AUS). Beim Landen erneut
springen; bis 150 ms vorher wird die Eingabe gepuffert. Bei einer auf Loslassen
gebundenen Sprungtaste zählt entsprechend das Loslassen. Bei gelungenen Ketten
6 % zusätzliches horizontales Tempo pro Hop, maximal 25 % über Kettenbeginn und
absolut 12 m/s; Kollisionen können das Tempo reduzieren oder die Kette beenden.
