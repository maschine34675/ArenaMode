# ArenaMode – nativer GPU-Nachtest vom 08.09.2026

**Zwischenbericht: native Fortsetzung seit 14:08–14:10 MESZ durch einen
Eingabefehler blockiert; keine abgeschlossene Gesamtabnahme.** Ausgewertet sind
die vier neuen USEC-Größen 2v2–5v5 und die einzige gezielte 2v2-Bewegungswiederholung,
insgesamt fünf regulär beendete Arenen mit unveränderten geschützten Profilfeldern.
Beim anschließenden Profilwechsel lässt sich die native Fraktionsauswahl zwar
abbilden, aber nicht mehr bedienen: `GetCursorPos failed: Zugriff verweigert
(0x80070005)`. BEAR-Raids, aktuelle Legacy-/Normalraid-Regression und weitere
Bewegungstests bleiben **nicht ausgeführt**. Spiel und Server laufen noch;
es gibt keinen gestoppten Abschlussstand. Einzelheiten und offene Testeinstellungen
stehen am Berichtsschluss.

Das neue Teamstart-Preset erreicht in allen vier USEC-Größen eine vollständige
aktive Mannschaft. Im 5v5 sind Botbewegung, Kämpfe und beidseitige Nachbesetzungen
belegt, im 3v3 Botkills und gegnerische Nachbesetzungen. Beide 2v2-Läufe bleiben
dagegen ohne nachgewiesenen Botkampf oder Nachbesetzungen;
ihre Kartenstichproben zeigen gleiche Botpositionen. Dieser Befund wurde einmal
gezielt wiederholt, sein Mechanismus ist nicht bewiesen. Die geprüften USEC-Rollen,
Render-/PFT-Menüfälle und geschützten Arena-Profilenden bestehen innerhalb der
jeweils angegebenen Grenzen. Doppelsprünge sind erstmals nativ bestätigt, einschließlich
eines Falls nach Respawn; die vollständige Bewegungsabnahme ist damit nicht erledigt.

## Auftrag, Teststand und Belege

Der Nutzer hat die Nachtests auf dem GPU-Rechner anhand des aktualisierten Handoffs
ausdrücklich beauftragt. Tatsächliche Launcher-/Spielmenüs, Raids und normale
Spielhandlungen werden autonom bedient. Dieser Auftrag implementiert keine Fehlerbehebung:
kein Build, DLL-Tausch, Deployment, manueller Profileingriff, Pull, Reset oder Push.
Das Hauptprofil wird nicht verwendet. Quellcode- und Offline-Testerfolge der
gelieferten Änderung sind Kontext, kein Ersatz für diese GPU-Beobachtungen.

| Merkmal | Verifizierter Anfangsstand |
| --- | --- |
| Installation / Repository | `C:\SPT41` / `C:\SPT41\Development\ArenaMode` |
| Branch / Dokumentations-HEAD | `master`, `e94530f7f08074087dd4efa9a246c6f01e803cf0`; zu Beginn sauber |
| Gelieferter Code | `e2859e89ef6d4ff13e41a90c85cc0fa60f62ddc6`; enthält Doppelsprung-Merge, Rollen-, Render-/PFT- und Teamstart-/Waypoint-Korrekturen |
| Client SHA256 | `DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E` |
| Server SHA256 | `D7879AEFD593B8CA2915B8F8FAF7106C9F007A6E6BE2D02425D3892AF253AAF2` |
| Redeploy.Core SHA256 | `7D950A475A1175FF3C7D727D0BA49000F69B74EAD59602B9058F7E803E1A2BCB`; aktueller Lieferbeleg, nicht der abweichende historische Handoff-Tabellenwert |
| GPU / Treiber | NVIDIA RTX A2000 / `32.0.15.9706` |
| Native Darstellung | 1920 × 945; DLSS **Balanced aktiviert**, FSR2/3 aus; tatsächliche `SPT_Runtime/user/sptSettings/Graphics.ini` vor Testbeginn gesichert |
| UI-Skalierung | **0,875 abgeleitet** aus den tatsächlichen 1920 × 945 und dem dekompilierten Algorithmus: `min(1920/1920, 945/1080)`. `../_decompiled/EFT.UI/UICanvasScalerController.cs:95–100` / `../_decompiled/Utils.cs:617–624`; **keine Live-Canvas-Probe**. |
| Versionen | SPT 4.1.4, BepInEx 5.4.23.5; installiert u. a. PFT 0.10.0, BigBrain 1.5.0, SAIN 4.5.1, Waypoints 1.9.0, DynamicMaps 1.2.1 |
| Neustart | Spiel und Server zu Beginn gestoppt; frischer Serverstart 12:58:12, frischer Spielprozess 13:00:29 MESZ |
| Verwendeter Testcharakter | Separates Standard-USEC-Testprofil, Level 1, XP 0; keine privaten Konto-/Profilkennungen im Bericht |
| Ausgangskonfiguration | `GameMode=endless`, `ExperimentalArea=full-map`, separate Teamgröße 4; `RandomKit=true`, `SelectedKit` leer |
| Bewegung vor Testbeginn | Movement aktiv, `JumpHeightMult=1.3`, `DoubleJumpEnabled=true`, `DoubleJumpHeightMult=1`, `InfiniteStamina=true`, `NoFallDamage=true` |

Alle Rohbelege liegen lokal und Git-ignoriert unter
`artifacts/gpu-acceptance/2026-09-08_125900-retest/`: `test-stand.json`,
`settings-initial/`, `screenshots/`, `logs/` und private Profilkopien/-vergleiche.
Bildnummern unten beziehen sich auf die Dateipräfixe in `screenshots/`.
Der Verzeichnisname ist keine Aussage über den exakten Beginn einzelner Ereignisse.
Clientzeiten sind **MESZ (UTC+2)**; Server-/Launcherzeiten sind **UTC** und werden
entsprechend gekennzeichnet. Der Bildindex enthält Archivierungszeiten, keine
garantierten Aufnahmezeitpunkte. Sichtbare HUD-Zeitdifferenzen werden separat genannt.

Bedienung erfolgt über die dokumentierte native Computer-Use-Schnittstelle;
Logs, vorhandene Dateien und gespeicherte Profile werden ergänzend gelesen.
Diese Schnittstelle unterstützt Tastendrücke/-kombinationen, aber keine
definierte Haltedauer, getrenntes Key-down/Key-up oder phasengenaue Bewegungssteuerung.
Ein `Shift+w`-Impuls ist deshalb kein längerer kontrollierter Sprint. Dreifache
Sprungsequenzen sind feste native Teststimuli mit dazwischenliegenden Bildern;
ihre ungefähr einsekündigen Abstände decken nicht zuverlässig Aufstieg, Scheitel
und Fall jedes einzelnen Sprungs ab. Einzelbilder belegen keine lückenlose
Flackerfreiheit, vollständige Trajektorie oder dauernde Botbeobachtung.

Ein Launcherstart mit falschem geerbtem Arbeitsverzeichnis scheiterte vor dem
Spielstart am Zugriff auf `user/logs` im fremden Verzeichnis. Der anschließende
Start mit dem tatsächlichen SPT-Runtime-Arbeitsverzeichnis funktionierte.
Beleg: `logs/01-launcher-start-diagnostic/`. Dies ist ein Startumgebungsbefund,
kein Arena-Raidfehler; Konfiguration oder Installation wurden dafür nicht repariert.

## Neues Teamstart-Preset: USEC 2v2

Nativ gewählt: Arena → Team Deathmatch → Customs → **Customs contour (team starts)**,
`customs-factory-center-teams-v1`, 2v2, Tageslicht. Belege: Bilder 03–23,
`logs/03-after-usec-2v2/`, `private/usec-2v2-independent-audit.md`.

| Prüffall | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Auswahl und neue Gebietsvorbereitung | **bestanden** | Arm 13:04:04.910; Graphversuch 1 erfolgreich 13:04:51.054: 28/28 sampled, 28 reached, 28 fresh, 7 über Waypoints wiedergewonnen. 126 gerichtete Abfragen **insgesamt**, keine Pro-Frame-Zahl. Gebiet READY 13:04:51.056 mit 17 PMC-/11 Botmarkern. |
| Tatsächlich aktives 2v2 | **bestanden** | `Team Deathmatch ready: 2v2` 13:05:29.469; ein eigener USEC-PMC + ein USEC-Bot gegen zwei BEAR-Bots. Bild 08 zeigt alle vier LIVE. Kandidaten-/Graphzahlen werden davon getrennt bewertet. |
| Eigener erster USEC-Start | **bestanden** für diese Stichprobe | Initialposition 13:04:10.388 bei (317.34, 1.13, −78.76), westlicher Start; Bild 05. Gegnerische Körper, alle Startpunkte und mögliche Überlappungen sind dadurch nicht vollständig geprüft. |
| F9-Rolle vor/nach Respawn | **bestanden** | Lokaler menschlicher Spieler als USEC in Bildern 08 und 14; zweites Bild nach erstem Respawn. Keine neue gespeicherte Profilfraktion behauptet. |
| Drei eigene Respawns | **bestanden** | Give-up → fertig: 13:08:32.746 → 13:08:34.435 (1,65 s); 13:09:48.648 → 13:09:50.170 (1,52 s); 13:15:36.514 → 13:15:38.002 (1,47 s). Drei Leben, fortgesetztes Match; Bilder 13–15/19. |
| Zweckgerichteter Botkampf / Botnachbesetzung | **fehlgeschlagen als Bewegungs-/Kampfnachweis** | Matchende mit 0 eigenen Kills/3 eigenen Give-ups; keine Botverluste oder späteren Admissions belegt. Bilder 17/18 zeigen gleiche drei Botpfeilpositionen an festen Gebäuden bei HUD 09:42 → 10:15. Das sind gleiche Endpunkte über 33 s HUD-Differenz, keine lückenlose 33-s-Aufzeichnung; Archivierungsdifferenz nur 18,364 s. Kein Beweis, dass jeder Bot über den gesamten Raid absolut stillstand. |
| Vollständige Grenz-/Spawnfairnessprüfung | **nicht ausgeführt** | Sichtbare Kontur, Einzelbewegungen und Startdaten ersetzen keine Abnahme aller Kanten, Höhen, Laufwege oder Spawnüberlappungen. Keine abgeleitete Freigabe aus fehlenden Rejection-Logs. |
| Reguläres Matchende | **bestanden** | Escape → Disconnect → Confirm Leave; Matchende 13:19:58.656, 0 Kills/3 Tode. Native Anzeige Bild 23: Left the Action, **00:15:09**, EXP 0. Keine Prozessbeendigung als Raidende. |

Die längere Raidzeit enthält Menü-/Konfigurationsbedienung und Belegsicherung;
sie ist keine 15-minütige ununterbrochene direkte Beobachtung aller Bots.
Alle drei Spielerrespawns melden denselben PMC-Marker und gerundet 157,1 m zum
nächsten Gegner. Das sind einzelne Spawnmessungen, keine kontinuierlichen Wege.

## Einziger gezielter USEC-2v2-Bewegungsnachtest

Nach den vier Größen wurde genau ein weiterer USEC-2v2-Lauf im gleichen neuen
Preset durchgeführt, um den offenen Bewegungs-/Kampfbefund gezielt zu prüfen.
Gleicher Spielprozess, keine zwischenzeitliche Code-/DLL-Änderung und kein
künstlich erzwungener Botkill. Belege: Bilder 61–69,
`logs/07-after-usec-2v2-retry/`,
`private/usec-2v2-retry-independent-audit.md` und private Diffs.

| Prüffall | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Auswahl, Gebiet und USEC-Erststart | **bestanden** | Arm 13:53:40.419; initialer PMC 13:53:44.914 bei (314.87, 1.16, −93.12). Matchstart 13:54:16.452; Graph 13:54:17.273: sampled/reached/fresh 28/28, 6 über Waypoints, 124 gerichtete Abfragen insgesamt. Gebiet READY 13:54:17.276. |
| Vollständige aktive Aufstellung / eigene Rolle | **bestanden** | Vollständige Aktivierung 13:54:42.108. Bild 63 zeigt alle vier LIVE: eigener PMC + ein USEC-Bot gegen zwei BEAR-Bots, lokaler ROLE=USEC. |
| Gezielter Botbewegungs-/Kampfnachtest | **fehlgeschlagen; Befund wiederholt** | Kartenbilder 64/65/68 bei HUD **01:14 → 02:07 → 04:26** zeigen dieselben drei Botmarker relativ den festen Gebäuden: 53 s zwischen den ersten beiden und 3:12 zwischen erstem und letztem Bild, letzteres nach eigenem Respawn. Keine Botkills oder Nachbesetzungen im 5:14-Raid. Die Anzeigen sind drei Endpunktstichproben, keine kontinuierliche Weltpositionsaufzeichnung jedes Bots. |
| Eigener Respawn trotz Botbefund | **bestanden** | Give-up 13:57:00.131, Tod 13:57:00.145, fertig 13:57:01.617; protokollierte Laufzeit **1,47 s** (Bundles 0,03 s, Pipeline 1,43 s), Bild 67. Grass-Rebind 13:57:01.610. HUD 0:1 stammt allein aus diesem eigenen Tod. |
| Inventareingabe | **bestanden** als einfache native Blockierungsstichprobe | Bild 66 nach Space im offenen Inventar/Kartenbild: keine Änderung der angezeigten eigenen Position. Keine framegenaue Positionsmessung oder Prüfung sämtlicher Menü-/Eingabezustände. |
| Reguläres Ende / Profilschutz | **bestanden** | Matchende 13:59:30.251 mit eigenem 0/1. Bild 69: Left the Action, **00:05:14**, EXP 0; bestätigter Restore und Persistenz sowie geschützter Diff 0 vor dem Profilwechsel. |

Die eigene Karte zeigt vor Respawn (314.87, −93.07, 1.16), danach
(314.87, −93.12, 1.16), jeweils in der nativen Kartenreihenfolge. Die kleine
Abweichung des Spielerstarts ist kein Botbewegungsbeleg. Beim nativen Blick-Drag
wurde eine Kugel abgefeuert; keine Gegnerkontakte oder dadurch ausgelöste
Nachbesetzungen wurden beobachtet. Beide 2v2-Läufe zeigen funktionierende
Aufstellung und eigene Respawns bei fehlendem beobachtetem Botkampf. Die genaue
Ursache des wiederholten Befunds bleibt offen; die Kartenbilder beweisen keine
lückenlose absolute Bewegungslosigkeit aller Körper. Kein weiterer USEC-2v2-Retry
gehört zu diesem ausgewerteten Zwischenstand.

## Neues Teamstart-Preset: USEC 5v5

Nativ dasselbe neue Preset mit 5v5 und Tageslicht gewählt. Belege: Bilder 24–42,
`logs/04-after-usec-5v5/`, `private/usec-5v5-independent-audit.md`.
Gleicher frischer Spielprozess wie beim 2v2, kein zusätzlicher Kaltstart.

| Prüffall | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Auswahl, Graph und erster USEC-Start | **bestanden** | Arm 13:22:49.824; initialer PMC 13:22:54.682 bei (314.87, 1.16, −93.12). Graph 13:23:22.416: 28/28, fresh 28, 6 über Waypoints, 124 gerichtete Abfragen insgesamt; Gebiet READY 13:23:22.422. Matchstart 13:23:21.481. |
| Tatsächlich aktives 5v5 | **bestanden** | Vollständige Readiness 13:25:15.809. Bild 28 zeigt vorher erst fünf eigene und vier gegnerische aktive Zeilen und ist ausdrücklich kein vollständiger 5v5-Beleg. Bilder 33 und 40 zeigen jeweils alle zehn LIVE: eigener PMC + vier USEC-Bots gegen fünf BEAR-Bots. |
| Botbewegung und Kampf | **bestanden** als Stichprobe | Bilder 29/30 bei HUD 02:19 → 02:34 zeigen mehrere verschobene gelbe/rote Botpfeile relativ festen Gebäuden. Bild 29 enthält einen Botkill im Killfeed. Bild 33 zeigt 3:1 vor jedem eigenen Give-up. Zweckgerichtete Wege sind aus Karte/Kampf erschlossen, nicht durch eine fortlaufende Ego-Beobachtung jedes Bots bewiesen. |
| Botnachbesetzungen | **bestanden** für beobachtete Rückkehr zur Sollbesetzung | Neun Ersatz-Zulassungen 13:25:59.460–13:31:56.225, davon zwei USEC und sieben BEAR. Admission zunächst PreActive; tatsächlich wieder zehn LIVE in Bildern 33/40. Der spätere Stand 5:5 enthält drei eigene Give-ups und darf nicht als fünf gegnerische Kampf-Kills gelesen werden. |
| Drei eigene Respawns | **bestanden** als abgeschlossene Respawns; Verzögerung dokumentiert | 13:28:34.342 → 13:28:55.867: **21,52 s**, davon 20,00 s Bundleladen. Danach 13:29:42.453 → 13:29:44.015 (1,55 s) und 13:30:41.769 → 13:30:43.341 (1,56 s); Laufzeitwerte aus der protokollierten Bundle-/Pipeline-Messung. Keine Aussage durchgehend sofortiger Respawns. |
| F9-Rolle nach mehreren Respawns | **bestanden** | Bild 40 nach drittem Respawn: lokaler PMC ROLE=USEC, alle zehn LIVE. |
| Grenz-/Sprungstichprobe | **bestanden** nur als beobachtetes Zurückbleiben auf derselben Seite | Bilder 26/27: Vorwärtsimpuls an Betonwand mit Cyan-Kontur, dann `w+space`, `space`, `space`. Erst erhöhte Sicht, zweites Bild höher, drittes fallend auf derselben Seite. Native Betonwand und Arena-Grenze überlagern sich; kein isolierter Beweis der Konturkollision und keine Freigabe der gesamten Fläche. |
| Reguläres Ende | **bestanden** | Matchende 13:32:11.158; native Endanzeige Bild 42: Left the Action, **00:08:50**, EXP 0; anschließendes bestätigtes Restore/Persist-Paar und geschützter Vergleich, siehe unten. |

Ein BEAR-Spawnrequest läuft um 13:26:59.220 nach 45 s aus; zwischenzeitlich sind
drei von vier USEC-Bots und drei von fünf BEAR-Bots aktiv. Überholte Profile
werden vor Dispatch verworfen, um 13:27:06.291 ist der abgelaufene Request
abgeschlossen. Neue Ankünfte bringen BEAR bis 13:28:13.395 wieder auf fünf;
Bild 33 bestätigt danach zehn LIVE. Dies ist eine beobachtete Verzögerung mit
Erholung, kein verschwiegener Timeout und kein dauerhafter Matchabbruch.

**Bildbenennung:** Bild 32 heißt `respawn-one`, zeigt jedoch noch 0 eigene Tode
und Conscript vor dem ersten Give-up. Bild 34 zeigt den schwarzen Ladezustand
nach Tod 1, keinen fertigen Vanguard. Fertige Darstellung wird erst mit den
nachfolgenden Bildern, insbesondere ADS 36/38, belegt. Die 20 s Bundlezeit
umfasst 22 neu geladene Vanguard-Bundles beim Wechsel des festen Kits; unter
`RandomKit=false` läuft hier kein katalogweiter Hintergrund-Warmup. Kein
dauerhafter Hands-/Kamera-Softlock beobachtet.

Das 5v5 widerlegt eine pauschale Behauptung, die neue Variante verhindere immer
jede Botbewegung. Es erklärt den abweichenden 2v2-Befund nicht. Eine vollständige
Navigation-/Grenzfreigabe beider Größen folgt daraus nicht.

## Neues Teamstart-Preset: USEC 3v3 und 4v4

Beide Größen wurden nativ im gleichen neuen Customs-Teamstart-Preset bei Tageslicht
ausgewählt, weiterhin im selben Spielprozess. 3v3-Belege: Bilder 43–50,
`logs/05-after-usec-3v3/`, `private/usec-3v3-independent-audit.md`.
4v4-Belege: Bilder 51–60, `logs/06-after-usec-4v4/`,
`private/usec-4v4-independent-audit.md`.

| Prüffall 3v3 | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Auswahl, Gebiet und USEC-Erststart | **bestanden** | Arm 13:36:04.879/.880; initialer PMC 13:36:09.301 bei (314.87, 1.16, −93.12), westlicher Pool. Graph 13:36:38.438: sampled/reached/fresh 28/28, 6 über Waypoints, 124 gerichtete Abfragen insgesamt. Gebiet READY 13:36:38.439. |
| Vollständig aktives 3v3 / Rolle | **bestanden** | Native Aktivierung 13:37:10.457: ein PMC + zwei USEC-Bots gegen drei BEAR-Bots. Bild 49 zeigt später sechs LIVE nach Nachbesetzungen; lokaler ROLE=USEC. Kein direktes Bild der allerersten vollständigen Aufstellung behauptet. |
| Botkills / BEAR-Nachbesetzung | **bestanden** | Bild 49 bei HUD 07:04: Team 3:0, Verbündeter Jester_Stacks K=3/HS=1/Score=650, eigener PMC 0/0. BEAR-Ersatzankünfte 13:40:57.988, 13:40:59.708 und 13:42:16.639; die später LIVE sichtbaren Ersatznamen sind defonotmaple, Snofielf und iDkGK. |
| Eigene Respawns / USEC-Nachbesetzungen | **nicht ausgeführt** | Keine eigenen Tode/Give-ups oder USEC-Ersatzankünfte in diesem 3v3. Die drei gegnerischen Ersatzbots ersetzen diese Teilprüfungen nicht. |
| Vollständige Bewegungs-/Grenzprüfung | **nicht ausgeführt** | Botkampf belegt, aber keine kontinuierlichen Pfade aller Bots oder vollständige Konturprüfung. Spätere BEAR-Nutzung eines anfänglichen USEC-Poolmarkers entspricht der nach der Erstaufstellung erlaubten freien Bewertung verbundener Punkte; kein daraus abgeleiteter Fraktionsfehler. |
| Reguläres Ende | **bestanden** | Matchende 13:44:10.001 mit eigenem 0/0. Bild 50: Left the Action, **00:07:32**, EXP 0; Restore/Persist/Profilvergleich erfolgreich. |

| Prüffall 4v4 | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Auswahl, Gebiet und USEC-Erststart | **bestanden** | Arm 13:47:02.889; initialer PMC 13:47:07.242 bei (317.34, 1.13, −78.76), westlicher Pool. Matchstart 13:47:37.523; Graph 13:47:38.396: sampled/reached/fresh 28/28, 7 über Waypoints, 126 gerichtete Abfragen insgesamt. Gebiet READY 13:47:38.398. |
| Vollständig aktives 4v4 / Rolle | **bestanden** | Vollständige Aktivierung 13:48:33.318: ein PMC + drei USEC-Bots gegen vier BEAR-Bots. Bild 59 zeigt alle acht LIVE, lokaler ROLE=USEC und Teamstand 0:0. |
| Botkampf / Nachbesetzungen / eigene Respawns | **nicht ausgeführt** | Keine Kills, eigenen Tode oder Refills im kurzen Startertest. Sieben anfängliche Botzulassungen sind keine Nachbesetzungen. Dieser Durchlauf wertet Aufstellung und Höhen-Einstellungen aus, keinen längeren Bewegungsverlauf. |
| Vollständige Bewegungs-/Grenzprüfung | **nicht ausgeführt** | Kein Kartenvergleich oder kontinuierlicher Botpfadbeleg in diesem 4v4; aus funktionierender Aufstellung folgt kein Bewegungspass. Die Sprungstichproben sind unten getrennt eingegrenzt. |
| Reguläres Ende | **bestanden** | Matchende 13:50:21.834 mit eigenem 0/0. Bild 60: Left the Action, **00:02:44**, EXP 0; Restore/Persist/Profilvergleich erfolgreich. |

Bild 52 zeigt trotz seines Dateinamens noch die Ladekarte; Bild 53 belegt den
tatsächlichen westlichen Start. Wiederholte gegnerische Zulassungen am selben
Marker sind kein Beweis für gleichzeitige körperliche Spawnüberlappung.

## Darstellung, Optiken, PFT-Menü und Doppelsprung

| Prüffall | Status | Tatsächlicher Befund / Grenze |
| --- | --- | --- |
| Hauptkamera mit aktivem Upscaling im 2v2 | **bestanden** im geprüften Lauf | Grass-Rebind tatsächlich 13:05:25.306 sowie nach allen drei Respawns 13:08:34.429, 13:09:50.162, 13:15:37.997. Sichtbare neue Körper-/Waffenansichten. Keine `GrassMotionVectorsRT`-Fehler oder Kamera-NRE im Prozessfenster bis zum 2v2-Ende. |
| Hauptkamera mit aktivem Upscaling im 5v5 | **bestanden** im geprüften Lauf | Grass-Rebind tatsächlich 13:23:37.566 beim Initialtausch sowie 13:28:55.860, 13:29:44.010, 13:30:43.332 nach allen drei Respawns. Nach abgeschlossenem Laden sichtbare Waffenansichten; keine erneuten `GrassMotionVectorsRT`-/Kamera-NRE-Fehler. |
| Initialdarstellung 3v3 | **bestanden** als Startstichprobe | Grass-Rebind 13:36:44.408 beim einzigen Körpertausch. Bild 45 zeigt gerenderte Zieloptik vor naher Mauer mit `Obscured by foliage`; keine freie Fernsicht und keine Identifikation des exakten Optikmodells allein aus dem wiederverwendeten Kitnamen. Keine Kamera-NRE-, `GrassMotionVectorsRT`- oder MenuScreen-Fehler. |
| Initialdarstellung 4v4 | **bestanden** als Startstichprobe | 33 neue Vanguard-Bundles 13:47:45.000, Grass-Rebind 13:47:46.473, Initialtausch fertig 13:47:46.481. Native Waffen-/Umgebungsansicht danach vorhanden; keine Kamera-NRE-/`GrassMotionVectorsRT`-Fehler. Keine spätere Respawn- oder eigenständige Vergrößerungsoptikprüfung in diesem Durchlauf. |
| PFT Escape-/Settings-Menü | **bestanden** | Warmup tatsächlich vertagt 13:06:45.033; nativ SQUAD SETTINGS geöffnet und erneut geöffnet, jeweils gefüllte PFT-0.10-Einstellungsseite, Bilder 10/11. Rückkehr zum Raid funktioniert; keine inaktive-MenuScreen-Coroutineausnahme. Deaktivierte Felder nicht verändert. |
| Echter geschützter PFT-Tactical-Gruppenablauf | **nicht ausgeführt** | PFT-Menüprüfung benötigt keine Vierergruppe und ist kein Beleg für vier Gefolgsleute, Tactical-Rundenwechsel oder Spectating. |
| Optikstichproben 2v2 | **bestanden** nur für sichtbare Reflex-/Visierdarstellung | Anfangswaffe und weitere Respawnansichten sichtbar; das diesmalige `conscript` ist AKS-74 mit Reflexvisier (Bilder 19/21), **keine Trihawk-Vergrößerungsprüfung**. Der zufällige Sitzungskatalog unterscheidet sich vom früheren Bericht. |
| Vergrößerungsoptik 5v5 | **bestanden** | Im bereits bewaffneten, eingefrorenen Katalog einmal gezielt lesend identifiziert; nativ `RandomKit=false`, `SelectedKit=vanguard` gewählt. ACOG TA01NSN 4×32 (Tan). Bilder 36/38 zeigen vergrößerte Wand-/Baumszene und unverdecktes gelb-schwarzes Absehen, auch nach zweitem Respawn. Kein vollständiger Dauer-ADS-/Flackertest. |
| Kalter Optik-Callback-Schutz selbst | **nicht ausgeführt** als gezielt bestätigter Treffer | Kalte Kitpreloads im 2v2 laufen 13:05:19.038–13:06:10.979, aber keine konkrete `withheld unmounted optic`-Diagnose. Auch im 5v5 kein solcher Hinweis; dort kein katalogweiter Hintergrundpreload wegen fester Kitauswahl. Aus fehlenden NREs wird kein tatsächlicher Treffer dieses separaten Schutzpfads abgeleitet. |
| Doppelsprung nach normalem Bodensprung | **bestanden** als begrenzte native Folge | Bildfolge 12 und genau eine Diagnose `renewed local vertical impulse` um 13:08:16.368. Zweiter Impuls sichtbar; dritte Eingabe erzeugt keine weitere Erfolgsdiagnose. Keine verlässliche Einordnung aller Sprungphasen. |
| Doppelsprung mit Vorwärtsimpuls | **bestanden** als begrenzte Folge | Eingaben 13:24:33.541/34.516/35.515; genau eine zusätzliche Impulsdiagnose 13:24:34.646. Bilder 27. Kein länger gehaltener Sprint, keine quantitative Impulserhaltung gemessen. |
| Doppelsprung nach Respawn | **bestanden** für den 5v5-Fall | Eingaben 13:30:01.708/02.812/03.814; zweite Diagnose 13:30:02.924, Bildfolge 37 nach **Respawn 2** zeigt zusätzlichen Impuls und anschließendes Fallen. Die frühere Bildfolge 20 nach dem dritten 2v2-Respawn hatte keine zweite Erfolgsdiagnose; dieser einzelne Versuch blieb unbestätigt. |
| Doppelsprung deaktiviert / wieder aktiviert | **bestanden** als begrenzte Schalterstichprobe | 3v3: Bild 46 zeigt `DoubleJumpEnabled=Disabled`, Höhe 1,0; Folge 47 mit drei Space-Eingaben erzeugt keine Extraimpulsdiagnose. Im gesamten 3v3 kein Doppelsprung-Erfolgslog. Bild 48 bestätigt wieder Enabled/Höhe 1,0. Kein mitten im Flug ausgeführter Schalterwechsel und kein positiver Reaktivierungsimpuls in diesem Raid behauptet. |
| Doppelsprunghöhe 0,25 | **bestanden** als native Einstellungs-/Impulsstichprobe | 4v4: Bild 54 zeigt Höhe 0,25. Eingaben der Folge 55 um 13:48:28.323/29.304/29.994; genau ein zusätzlicher Impuls protokolliert um 13:48:29.389. Keine quantitative Messung der erreichten Höhe oder des Faktors. |
| Doppelsprunghöhe 1,5 und Rückstellung | **bestanden** als native Einstellungs-/Impulsstichprobe | Bild 56 zeigt Höhe 1,5. Eingaben der Folge 57 um 13:49:16.017/17.059/17.606; genau ein zusätzlicher Impuls 13:49:17.176. Zweite Ansicht sichtbar höher, dritte fallend; kein weiterer Extraimpuls protokolliert. Bild 58 bestätigt danach Enabled/Höhe 1,0. Keine behauptete 1,5-fache gemessene Höhe. |
| Inventar-/Pause-Eingabesperre | **bestanden** nur für die begrenzte Bedienungsstichprobe | Space im geöffneten Inventar/Kartenbild 66 lässt die angezeigte eigene Position unverändert. Bild 41 bleibt nach Space im Escape-Menü; verdeckte Spielerposition dort nicht vermessen. Keine umfassende oder framegenaue Input-/Bewegungsfreigabe. |
| Übrige Doppelsprungfälle | **nicht ausgeführt** | Kontrollierter Stand/Sprint in Aufstieg/Scheitel/Fall, gehaltene und neu belegte Taste, systematisches Landen/Wiederholen, Decken/Schrägen/Kanten, Ledge-Fall, Ducken/Vault/Climb, Tactical-/Spectator-/Buy-Menüs, Movement-Masterschalter und Schalterwechsel mitten im Flug, quantitative Höhen-/Impulsmessung, reale Kosten bei ausgeschalteter unendlicher Ausdauer und Fallfolgen bei ausgeschaltetem Fallschutz. |

Die feste Kitauswahl ist eine native Testeinstellung und verändert keine
gespeicherten Arena-Profilgegenstände. **`RandomKit=false` und
`SelectedKit=vanguard` sind beim Eingabeblocker noch gesetzt.** Die vorgesehene
native Rückstellung zu `RandomKit=true` und leerem `SelectedKit` konnte deshalb
noch nicht erfolgen; keine manuelle Umgehung über die Konfigurationsdatei.
Der Doppelsprung-Einzelschalter und seine Höhe sind dagegen nativ wieder auf
**Enabled / 1,0** gestellt und im gesicherten Blocker-Konfigurationsstand bestätigt.

## Profilwiederherstellung nach jedem abgeschlossenen Arena-Raid

Vor jedem folgenden Raid wurden Restore **und** erfolgreiche Persistenz im
Serverlog sowie die tatsächlich gespeicherten geschützten Felder geprüft.
Vergleich: PMC und Scav jeweils Inventory einschließlich Identitäten, Eltern/Slots,
Teile und Mengen/Geld, Erfahrung/Level, Quests, TaskConditionCounters,
RepeatableQuests und Skills; der unabhängige Audit prüft zusätzlich Stats.
Bytegleichheit des gesamten zeitabhängigen Profildokuments ist kein Kriterium.
Private Profile, konkrete Konto-/Profilkennungen und Logvolltexte werden nicht eingecheckt.

| Arena-Ende | Snapshot restauriert (UTC) | Geschütztes Ende persistiert (UTC) | Status / geschützte Unterschiede |
| --- | --- | --- | --- |
| USEC neues 2v2 | 11:19:58.761 | 11:19:58.821 | **bestanden / 0**, PMC 183 Inventareinträge, XP 0; gesamter Scav gleich |
| USEC neues 5v5 | 11:32:11.235 | 11:32:11.326 | **bestanden / 0** |
| USEC neues 3v3 | 11:44:10.348 | 11:44:10.457 | **bestanden / 0** |
| USEC neues 4v4 | 11:50:22.083 | 11:50:22.128 | **bestanden / 0** |
| Einziger USEC-2v2-Bewegungsnachtest | 11:59:30.298 | 11:59:30.338 | **bestanden / 0** |

Geschützter kanonischer Vergleichshash vor/nach allen fünf USEC-Raids:
`9f0eef41f72cc7f88fb22d3de8cb27f91607ec4b13ebd174ee57becd1204e27b`.
Leere Diffdateien: `private/usec-2v2-protected-diff.json`,
`private/usec-5v5-protected-diff.json`, `private/usec-3v3-protected-diff.json`,
`private/usec-4v4-protected-diff.json` und `private/usec-2v2-retry-protected-diff.json`.

Die gestoppte USEC-Basis besitzt 183 PMC-Inventareinträge, 459101 RUB und XP 0.
SPT ergänzt vor dem ersten Arena-Arm genau zwei zuvor fehlende
`StackObjectsCount=1`-Felder an vorhandenen Messer-/Dogtagobjekten, dokumentiert
durch ProfileFixer-Warnungen 11:01:18.716/.717 UTC. Identitäten und tatsächliche
Mengen bleiben gleich. Deshalb unterscheidet sich der rohe geschützte Hash
der gestoppten Basis von der direkten Vorraidbasis; dies ist keine Arena-Belastung.
Die generische native LEFT-Anzeige ist ebenfalls keine Aussage über gespeicherte
Ausrüstungsverluste. Alle fünf beendeten Arenen haben bestätigte geschützte Endwerte.

## Fehler, offene Befunde und verbleibende Matrix

| Befund | Einordnung |
| --- | --- |
| 2v2 ohne belegte Botbewegung/Kämpfe/Refills | **fehlgeschlagen; einmal gezielt wiederholt**. Erster Lauf 15:09, Nachtest 5:14; gleiche Botmarker in den beschriebenen Kartenstichproben und keine Botkills/Refills. Ursache nicht isoliert. Vollständige Mannschaft und eigene Respawns funktionieren. |
| 5v5 erster eigener Respawn 21,52 s | Deutliche beobachtete Verzögerung mit etwa 20 s Bundleladen; danach abgeschlossen. Kein unbelegter sofortiger Respawn-Pass. |
| 2v2 Fehlerfenster | Unabhängiger Audit: ab frischem Spielstart bis archiviertem 2v2-Ende Client Error/Fatal 0, echte Laufzeit-Exceptions 0; Server ebenfalls keine entsprechenden Fehler. |
| 2v2 Warnungen | 53 Clientwarnungen: optionale Fika-/fremde Harmony-Meldungen, Unity Box-/WheelCollider, DontDestroyOnLoad, vier entfernte alte PerfectCulling-Sampler und ein anschließend erfolgreich abgeschlossenes pending Raidende. Keine Gleichsetzung mit ausgelösten Exceptions aus Harmony-Debug-Methodennamen. |
| Einziger 2v2-Nachtest: Fehlerfenster | Unabhängig geprüft ab 13:51 MESZ bis Archivaufnahme: Client/Server Error/Fatal und echte Exceptions 0. 41 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 2 entfernte alte PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende und 1 erst nach Raidende um 13:59:44.766 pausierte CombatLog-Icon-Anfrage. Keine aus der Icon-Warnung abgeleitete Arena-Navigationsursache. |
| 5v5 Fehlerfenster | Unabhängig bestätigt ab 13:21 MESZ bis archiviertem Ende: Client/Server Error/Fatal und echte Exceptions 0. 48 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 5 Animator.GotoState, 4 entfernte alte PerfectCulling-Sampler, 1 aufgelöster Spawn-Timeout und 1 anschließend abgeschlossenes pending Raidende. |
| 3v3 Fehlerfenster | Unabhängig geprüft 13:33–13:45 MESZ: Client/Server Error/Fatal und echte Exceptions 0. 39 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 1 entfernter alter PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende. Kein Spawn-Timeout oder Movement-Watch-Befund. |
| 4v4 Fehlerfenster | Unabhängig geprüft 13:45–13:52 MESZ: Client/Server Error/Fatal und echte Exceptions 0. 39 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 1 entfernter alter PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende. Kein Spawn-Timeout oder Movement-Watch-Abbruch. |
| Native Eingabe nach Profilwechsel | **blockiert**, separat von den fünf abgeschlossenen Raids: Aktivierung und anschließend zwei anhand frischer Beobachtungen versuchte Klicks scheitern um 14:08–14:10 MESZ. Native Fraktionsauswahl bleibt erreichbar als Bild, weitere Bedienung schlägt mit `GetCursorPos` / `0x80070005` fehl. Keine daraus abgeleitete Arena-Codeursache. |
| Vollständige Render-/Optik-/Navigationssicherheit | **nicht ausgeführt**; begrenzte Bilder, konkrete Diagnosen und abgeschlossene kurze Abläufe ersetzen keinen Langzeit-/Geometrie-/Alle-Optiken-Test. |

| Neue Teamstarts | 2v2 | 3v3 | 4v4 | 5v5 |
| --- | --- | --- | --- | --- |
| USEC | **bestanden** für aktive Besetzung/Rolle/Spielerrespawns/Profil; Bewegungs-/Kampfnachweis **fehlgeschlagen und einmal gezielt wiederholt** | **bestanden** für aktive Besetzung/Rolle, Botkills, BEAR-Refills und Profil; eigene Respawns/USEC-Refills nicht ausgeführt | **bestanden** für aktive Besetzung/Rolle/Profil; Kampf, Bewegung, Refills und eigene Respawns nicht ausgeführt | **bestanden** für aktive Besetzung, beobachtete Bewegung/Refills, Rolle/Spielerrespawns/Profil; Grenzen wie oben |
| BEAR | **nicht ausgeführt** | **nicht ausgeführt** | **nicht ausgeführt** | **nicht ausgeführt** |

Weiterhin **nicht ausgeführt** in diesem Berichtsstand: alle vier BEAR-Größen,
erneuter Legacy-Kontur-2v2 auf dem aktuellen Client, normaler PMC-Raid mit Doppelsprung-/Arena-Isolation,
Scav-/Hideout-Bewegung, echter PFT-Tactical-Gruppenablauf sowie die oben einzeln
genannten weiteren Doppelsprungfälle. Frühere Erfolge auf dem alten Client werden
nicht in diese Matrix übertragen. Weitere Arena-Enden müssen jeweils vor dem
nächsten Raid denselben Restore-/Persist-/Profilvergleich bestehen.

## Aktueller Eingabeblocker und Zustand für die Fortsetzung

Nach dem fünften erfolgreichen geschützten USEC-Ende wurde der Spielprozess
regulär verlassen. Ein neues lokales **Standard-Testkonto** wurde über den
Launcher angelegt. Es ist noch kein vollständig initialisierter BEAR-Charakter:
Beim neuen Spielstart um **14:04:09 MESZ** wurde nur die native Seite
**Choose Your Faction** erreicht; BEAR wurde nicht ausgewählt und kein weiterer
Raid gestartet. Eine gestoppte BEAR-Ausgangsbasis vor dem ersten Raid existiert
deshalb noch nicht. Es werden weder Konto-/Profilnamen noch private Kennungen
in diesem Bericht veröffentlicht.

Um 14:08 scheiterte die native Fensteraktivierung. Ein danach frisch ermitteltes
Spielfenster lieferte weiterhin die tatsächliche Fraktionsauswahl als Screenshot.
Zwei an erneuten Beobachtungen ausgerichtete Klickversuche um 14:09–14:10 scheiterten
mit **`GetCursorPos failed: Zugriff verweigert (0x80070005)`**. Die native
Bedienung wurde anschließend nicht mit weiteren Klickschleifen fortgesetzt.
Dies ist ein Fehler des aktuell verfügbaren Eingabezugangs, kein beobachteter
BEAR-Arena-Startfehler oder Beleg für eine Regression im Arena-Code.

Die RDP-Spielsitzung wurde weiter als aktiv gemeldet. Der vorhandene LogonUI-Prozess
gehört zu einer anderen Sitzung und lief bereits seit 09:46. Damit ist eine
Sperre des Spiel-Desktops **nicht nachgewiesen**; die genaue Ursache des
Zugriffsfehlers bleibt offen. Der Nutzer wurde informiert, dass für die Fortsetzung
wieder eine sichtbare, entsperrte und tatsächlich bedienbare RDP-Spielsession
benötigt wird. Eine Benutzeraktion zur Wiederherstellung dieses Eingabezugangs
ersetzt keine bereits erteilte Testfreigabe.

Rohbelege vor dem Profilwechsel/Neustart und beim Blocker:
`logs/08-before-profile-change-game-exit/`,
`logs/09-before-bear-character-game-start/`, `logs/10-native-input-blocker/`.
`private/input-blocker.json`, `private/current-blocked-state-audit.md`,
Bild 71 und die zusätzlichen privaten Profilkopien sowie `settings-final/` dokumentieren den
Zustand **bei noch laufenden Prozessen**; das Verzeichniswort „final“ bedeutet
keine abgeschlossene oder gestoppte Testabnahme.

| Zustand am Eingabeblocker | Bedeutung für die Fortsetzung |
| --- | --- |
| Spiel und Server laufen noch | Kein regulär gestoppter Endzustand behauptet; neu gestartetes Spiel steht in der Fraktionsauswahl, kein Raid aktiv. |
| Feste Testkitauswahl `false` / `vanguard` noch gesetzt | Nativ auf `RandomKit=true` und leeres `SelectedKit` zurückstellen, sobald Eingabe wieder möglich ist. |
| Doppelsprung Enabled / Höhe 1,0 bereits nativ zurückgestellt | Bilder 48/58 und gesicherte Konfiguration bestätigen die Rückstellung; Movement-Masterschalter weiterhin aktiv. |
| Fünf USEC-Arena-Enden regulär abgeschlossen und geschützt verglichen | Kein offenes Arena-Restore nach diesen Raids; der spätere Eingabefehler macht ihre Belege nicht rückwirkend zu Fehlversuchen. |
| Neues Standardkonto noch ohne abgeschlossene Fraktions-/Charakterwahl | Erst nativ BEAR fertig initialisieren, dann gestoppte Ausgangsbasis und direkte Vorraidbasis sichern. |
| Client/Server/Redeploy.Core um 14:10:33 nochmals gehasht | Alle drei SHA256 unverändert gegenüber der obigen Anfangstabelle; laufender Prozesszustand ausdrücklich davon getrennt. |
| USEC-Profilkopie am Blocker | Geschützte Felder und gesamter Scav weiterhin identisch zum gespeicherten Ende des 2v2-Nachtests. Nur ein ungeschützter Hideout-Aktualisierungszeitstempel ist weitergelaufen. |

Die gesicherte Grafikdatei aus **`SPT_Runtime/user/sptSettings/Graphics.ini`** ist
bytegleich zur Anfangskopie. Die Server-Konfiguration ist ebenfalls bytegleich.
Der Clientvergleich zeigt genau diese inhaltlichen Änderungen durch
native Testauswahl; weitere Textabweichungen betreffen BepInEx-Dezimalformatierung
in Kommentaren:

| Clientwert | Anfang | Beim Eingabeblocker |
| --- | --- | --- |
| `RandomKit` | `true` | `false` |
| `SelectedKit` | leer | `vanguard` |
| `GameMode` | `endless` | `teamdeathmatch` |
| `ExperimentalArea` | `full-map` | `customs-factory-center-teams-v1` |
| `AreaPlayersPerTeam` | `4` | `2` |

Offen bleiben die vier BEAR-Größen, aktuelle Legacy-/Normal-PMC-Regression,
weitere erreichbare Doppelsprung-/PFT-/Scav-/Hideout-Fälle, native Rückstellung
der Kitauswahl und der regulär gestoppte Abschluss mit vollständigen Vergleichen.
Es wurde kein Codefix, Build, Deployment oder manueller Profileingriff zur
Umgehung des Blockers ausgeführt. Dieser Zwischenbericht hält die bis dahin
belegten Ergebnisse fest und erklärt ausdrücklich, weshalb der gesamte Auftrag
noch nicht abgeschlossen ist.
