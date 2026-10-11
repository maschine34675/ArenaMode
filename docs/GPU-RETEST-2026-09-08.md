# ArenaMode – nativer GPU-Nachtest vom 08.09.2026

**Abgeschlossener Testdurchlauf auf dem gelieferten Client e2859e8; keine vollständige GPU-Freigabe.** Alle neuen Teamgrößen 2v2–5v5 erreichen auf beiden Spielerfraktionen tatsächlich volle Mannschaften. Insgesamt **elf Arena-Enden** sind regulär beendet, serverseitig wiederhergestellt und persistiert; die geschützten Profildaten bleiben nach jedem einzelnen Ende unverändert. Die aktuelle Legacy-Kontur-2v2-Regression und der normale PMC-Fall mit eigener AK-74M/USP-1 bestehen im jeweils dokumentierten Umfang. Die gewöhnlichen Ausrüstungsverluste des normalen LEFT werden separat ausgewiesen.

Botbewegung, Kämpfe und Nachbesetzungen sind in mehreren Läufen belegt. Frühere USEC-2v2-Läufe, der erste BEAR-2v2-Lauf und BEAR 5v5 blieben dagegen ohne den geforderten Kampf-/Refillnachweis. Beim zusätzlich beauftragten Schussreiz-Test bewegen und bekämpfen sich die Bots bereits **vor dem ersten eigenen Schuss**: ein Aufwecken durch Schüsse ist damit nicht bewiesen. BEAR 5v5 erreicht den vollen Roster erst nach **7:03,098**, BEAR 4v4 nach **3:48,347**. Zwei neue Unity-Dateischließfehler vor dem BEAR-3v3-Arming bleiben ohne belegte Modzuordnung.

Rollenanzeige, Render-/Optikstichproben, PFT-Escape-/Settings-Menü und grundlegende Doppelsprünge einschließlich nach Respawn und mit geänderter Taste bestehen innerhalb der angegebenen Grenzen. Vollständige Geometrie-/Bewegungsabnahme, tatsächliche PFT-Tactical-Gruppen-/Runden-/Buy-/Spectatorabläufe, Scav-/Hideout-Isolation und ein gezielt bestätigter kalter Optik-Guard-Treffer bleiben **nicht ausgeführt**. Aus fehlenden Guard-Meldungen allein wird kein Fehler abgeleitet.

Die Testeinstellungen wurden nativ zurückgestellt: **106/106 Clientwerte unverändert**, Controls und Serverkonfiguration bytegleich zur jeweiligen Ausgangsbasis; alle drei DLL-Hashes unverändert. Spiel und Server sind nach regulärem Beenden seit **18:36:23 MESZ** nachweislich nicht mehr aktiv; der Launcher bleibt geöffnet. Die endgültige Anzeige beträgt **1920 × 1080**, DLSS Balanced unverändert. RDP-bedingte Auflösungsänderungen und sämtliche verbliebenen Testgrenzen sind unten dokumentiert. Kein Codefix, Build, Deployment, manueller Profileingriff oder Push.

## Auftrag, Teststand und Belege

Der Nutzer hat die Nachtests auf dem GPU-Rechner anhand des aktualisierten Handoffs
ausdrücklich beauftragt. Tatsächliche Launcher-/Spielmenüs, Raids und normale
Spielhandlungen werden autonom bedient. Dieser Auftrag implementiert keine Fehlerbehebung:
kein Build, DLL-Tausch, Deployment, manueller Profileingriff, Pull, Reset oder Push.
Spielstarts und Raids verwenden ausschließlich getrennte Testprofile. Beim
Launcherstart der Fortsetzung ab 17:43 war das Hauptprofil zunächst automatisch
ausgewählt; es wurde sofort abgemeldet, ohne Spielstart oder Raid mit diesem Profil.
Quellcode- und Offline-Testerfolge der
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
| Kalter Optik-Callback-Schutz selbst | **nicht ausgeführt** als gezielt bestätigter Treffer | USEC-2v2-Preloads laufen 13:05:19.038–13:06:10.979 ohne konkrete `withheld unmounted optic`-Diagnose. Im USEC-5v5 kein katalogweiter Hintergrundpreload wegen fester Kitauswahl. Im frisch gestarteten BEAR 2v2 ist Reflex-ADS während laufender Katalogvorladung belegt (Bild 78); im folgenden BEAR 5v5 Vergrößerungs-ADS während seiner erneut laufenden Vorladephase (Bild 97, Details unten). Weiterhin kein tatsächlicher Guard-Treffer. Sein Ausbleiben ist laut Auftrag allein kein Fehler; aus fehlenden NREs wird kein Eingriff des Schutzpfads abgeleitet. |
| Doppelsprung nach normalem Bodensprung | **bestanden** als begrenzte native Folge | Bildfolge 12 und genau eine Diagnose `renewed local vertical impulse` um 13:08:16.368. Zweiter Impuls sichtbar; dritte Eingabe erzeugt keine weitere Erfolgsdiagnose. Keine verlässliche Einordnung aller Sprungphasen. |
| Doppelsprung mit Vorwärtsimpuls | **bestanden** als begrenzte Folge | Eingaben 13:24:33.541/34.516/35.515; genau eine zusätzliche Impulsdiagnose 13:24:34.646. Bilder 27. Kein länger gehaltener Sprint, keine quantitative Impulserhaltung gemessen. |
| Doppelsprung nach Respawn | **bestanden** für den 5v5-Fall | Eingaben 13:30:01.708/02.812/03.814; zweite Diagnose 13:30:02.924, Bildfolge 37 nach **Respawn 2** zeigt zusätzlichen Impuls und anschließendes Fallen. Die frühere Bildfolge 20 nach dem dritten 2v2-Respawn hatte keine zweite Erfolgsdiagnose; dieser einzelne Versuch blieb unbestätigt. |
| Doppelsprung deaktiviert / wieder aktiviert | **bestanden** als begrenzte Schalterstichprobe | 3v3: Bild 46 zeigt `DoubleJumpEnabled=Disabled`, Höhe 1,0; Folge 47 mit drei Space-Eingaben erzeugt keine Extraimpulsdiagnose. Im gesamten 3v3 kein Doppelsprung-Erfolgslog. Bild 48 bestätigt wieder Enabled/Höhe 1,0. Kein mitten im Flug ausgeführter Schalterwechsel und kein positiver Reaktivierungsimpuls in diesem Raid behauptet. |
| Doppelsprunghöhe 0,25 | **bestanden** als native Einstellungs-/Impulsstichprobe | 4v4: Bild 54 zeigt Höhe 0,25. Eingaben der Folge 55 um 13:48:28.323/29.304/29.994; genau ein zusätzlicher Impuls protokolliert um 13:48:29.389. Keine quantitative Messung der erreichten Höhe oder des Faktors. |
| Doppelsprunghöhe 1,5 und Rückstellung | **bestanden** als native Einstellungs-/Impulsstichprobe | Bild 56 zeigt Höhe 1,5. Eingaben der Folge 57 um 13:49:16.017/17.059/17.606; genau ein zusätzlicher Impuls 13:49:17.176. Zweite Ansicht sichtbar höher, dritte fallend; kein weiterer Extraimpuls protokolliert. Bild 58 bestätigt danach Enabled/Höhe 1,0. Keine behauptete 1,5-fache gemessene Höhe. |
| Movement-Masterschalter | **bestanden** als begrenzte negative Schalterstichprobe | BEAR 5v5: Bilder 92/93 dokumentieren Movement aus und eine Sprungfolge ohne Extraimpuls; Bilder 94/95 die native Rückstellung auf ein. Danach in diesem BEAR-Lauf kein positiv bestätigter Zusatzimpuls. Kein Schalterwechsel mitten im Flug und keine bestätigte positive Reaktivierungsfolge behauptet. |
| Neu belegte Sprungtaste | **bestanden** für die Zusatzimpulsfolge; Rückstellung später **bestanden** | BEAR 4v4: Bild 117 Space / Release, Bild 118 Return / Release. Bildfolge 119 mit drei Return-Eingaben, genau ein zusätzlicher Impuls **16:56:06.370** bestätigt. Die native Bedienung entfernt außerdem Fire / Mouse0; beim Nutzerstopp stehen beide Änderungen noch aus. Nach Freigabe gezielt nativ zurückgestellt, Bilder 122/123 und vollständiger Controls-Vergleich **17:48:55.553**, 0 Unterschiede. Kein allgemeiner Tasten-/Eingabepass aus dieser Folge. |
| Inventar-/Pause-Eingabesperre | **bestanden** nur für die begrenzte Bedienungsstichprobe | Space im geöffneten Inventar/Kartenbild 66 lässt die angezeigte eigene Position unverändert. Bild 41 bleibt nach Space im Escape-Menü; verdeckte Spielerposition dort nicht vermessen. Keine umfassende oder framegenaue Input-/Bewegungsfreigabe. |
| Nativer Sprung ohne unendliche Ausdauer / Ducken | **bestanden** als einfache Bewegungsstichproben, **kein Zusatzsprung-Kostenpass** | Legacy: InfiniteStamina nativ aus (144), zwei zu weit getrennte Space-Eingaben zeigen normale Sprünge und Balkenverbrauch (146), kein Extraimpuls. Rückstellung ein in 147. Bilder 153/154: Ducken, Aufstehen und anschließender normaler Sprung möglich; keine systematische Doppelsprung-/Vault-/Climb-Abnahme. |
| Übrige Doppelsprungfälle | **nicht ausgeführt** | Kontrollierter Stand/Sprint in Aufstieg/Scheitel/Fall, gehaltene Taste, systematisches Landen/Wiederholen, Decken/Schrägen/Kanten, Ledge-Fall, Zusatzsprungübergänge beim Ducken/Vault/Climb, Tactical-Runden-/Spectator-/Buy-Eingabesituationen, Schalterwechsel mitten im Flug, quantitative Höhen-/Impulsmessung, reale Zusatzsprungkosten bei ausgeschalteter unendlicher Ausdauer und Fallfolgen bei ausgeschaltetem Fallschutz. Die einfachen nativen Stamina-/Duck-Stichproben stehen separat. |

Die feste Kitauswahl ist eine native Testeinstellung und verändert keine
gespeicherten Arena-Profilgegenstände. Beim historischen Eingabeblocker waren
`RandomKit=false` und `SelectedKit=vanguard` noch gesetzt. Nach wiederhergestellter
Bedienung wurden beide Werte nativ zu `RandomKit=true` und leerem `SelectedKit`
zurückgestellt (Bild 75, unten dokumentiert); keine manuelle Änderung der Konfigurationsdatei.
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
| BEAR neues 2v2 | 14:16:51.921 | 14:16:51.968 | **bestanden / 0**, eigene BEAR-Vorraidbasis wie unten |
| BEAR neues 5v5 | 14:34:42.338 | 14:34:42.503 | **bestanden / 0**, gleiche geschützte BEAR-Vorraidbasis |
| BEAR neues 3v3 | 14:46:01.956 | 14:46:01.996 | **bestanden / 0**, gleiche geschützte BEAR-Vorraidbasis |
| BEAR neues 4v4 | 14:57:17.777 | 14:57:17.827 | **bestanden / 0**, gleiche geschützte BEAR-Vorraidbasis |
| BEAR neues 2v2, zusätzlicher Schussreiz | 15:57:45.823 | 15:57:45.884 | **bestanden / 0**, gleiche geschützte BEAR-Vorraidbasis; zwei eindeutig zugeordnete Serializer-Hashes wie unten |
| BEAR Legacy-Kontur 2v2 | 16:16:43.696 | 16:16:43.769 | **bestanden / 0**, gleiche geschützte BEAR-Vorraidbasis, unabhängig bestätigt |

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
Ausrüstungsverluste. Alle fünf beendeten USEC-Arenen haben bestätigte geschützte Endwerte.

## Fehler, offene Befunde und verbleibende Matrix

| Befund | Einordnung |
| --- | --- |
| Frühere 2v2 ohne belegte Botbewegung/Kämpfe/Refills | **fehlgeschlagen in diesen Beobachtungen; USEC einmal gezielt wiederholt**. USEC-Läufe 15:09 und 5:14, BEAR-Lauf 7:46; gleiche Botmarker in den jeweiligen Kartenstichproben und keine Botkills/Refills. Ursache nicht isoliert. Vollständige Mannschaft und eigene Respawns funktionieren. Der später ausdrücklich beauftragte Schussreiz-Lauf zeigt Bewegung, Botkill und USEC-Refill bereits vor dem ersten eigenen Schuss; der frühere Befund ist deshalb nicht auf jedes 2v2 verallgemeinerbar und belegt keine dauerhafte allgemeine Immobilität. |
| USEC 5v5 erster eigener Respawn 21,52 s | Deutliche beobachtete Verzögerung mit etwa 20 s Bundleladen; danach abgeschlossen. Kein unbelegter sofortiger Respawn-Pass. |
| BEAR 5v5 sehr späte volle Aufstellung | Volle Aktivierung erst **7:03,098 nach Matchstart**, zuvor aufgefangene initiale Spawn-Timeouts. Voller Roster schließlich nativ belegt; die deutliche Startverzögerung bleibt ein eigener Befund. Keine dauerhafte Aktivierungsblockade aus dem später vollständigen Lauf abgeleitet. |
| BEAR 4v4 verspätete volle Aufstellung | Volle Team-READY-Meldung erst **3:48,347 nach Matchstart**. Ein USEC-Spawn-Timeout nach 46 s wurde abgewickelt; Bild 116 zeigt zunächst nur sieben LIVE, Bild 120 später alle acht. Verzögerter Startertest mit schließlich voller Besetzung. |
| BEAR 5v5 ohne belegte Botkämpfe/Nachbesetzungen | **fehlgeschlagen** als geforderter Nachweis: im 13:38-Lauf keine Botkills/Refills, weitgehend gleiche Kartenmarker über 2:44 HUD-Zeit. Eine kleine kurz beobachtete gelbe Markerbewegung verhindert die pauschale Behauptung völliger Immobilität; kein kontinuierlicher Pfad-/Ursachenbeleg. Kein zusätzlicher Retry vorgesehen. |
| 2v2 Fehlerfenster | Unabhängiger Audit: ab frischem Spielstart bis archiviertem 2v2-Ende Client Error/Fatal 0, echte Laufzeit-Exceptions 0; Server ebenfalls keine entsprechenden Fehler. |
| 2v2 Warnungen | 53 Clientwarnungen: optionale Fika-/fremde Harmony-Meldungen, Unity Box-/WheelCollider, DontDestroyOnLoad, vier entfernte alte PerfectCulling-Sampler und ein anschließend erfolgreich abgeschlossenes pending Raidende. Keine Gleichsetzung mit ausgelösten Exceptions aus Harmony-Debug-Methodennamen. |
| Einziger USEC-2v2-Nachtest: Fehlerfenster | Unabhängig geprüft ab 13:51 MESZ bis Archivaufnahme: Client/Server Error/Fatal und echte Exceptions 0. 41 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 2 entfernte alte PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende und 1 erst nach Raidende um 13:59:44.766 pausierte CombatLog-Icon-Anfrage. Keine aus der Icon-Warnung abgeleitete Arena-Navigationsursache. |
| 5v5 Fehlerfenster | Unabhängig bestätigt ab 13:21 MESZ bis archiviertem Ende: Client/Server Error/Fatal und echte Exceptions 0. 48 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 5 Animator.GotoState, 4 entfernte alte PerfectCulling-Sampler, 1 aufgelöster Spawn-Timeout und 1 anschließend abgeschlossenes pending Raidende. |
| 3v3 Fehlerfenster | Unabhängig geprüft 13:33–13:45 MESZ: Client/Server Error/Fatal und echte Exceptions 0. 39 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 1 entfernter alter PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende. Kein Spawn-Timeout oder Movement-Watch-Befund. |
| 4v4 Fehlerfenster | Unabhängig geprüft 13:45–13:52 MESZ: Client/Server Error/Fatal und echte Exceptions 0. 39 Clientwarnungen: 33 BoxCollider, 4 WheelCollider, 1 entfernter alter PerfectCulling-Sampler, 1 anschließend abgeschlossenes pending Raidende. Kein Spawn-Timeout oder Movement-Watch-Abbruch. |
| BEAR 2v2 Fehlerfenster | Unabhängiger Audit ab frischem Prozess 16:03:30 bis Archivaufnahme: Client/Server Error/Fatal und echte Laufzeit-Exceptions **0**, keine Kamera-NRE. Im abgegrenzten Raidfenster ab 16:06 **54 Clientwarnungen**: 33 BoxCollider, 4 WheelCollider, 12 fehlende Skriptreferenzen, 4 entfernte alte PerfectCulling-Sampler und 1 danach erfolgreich abgeschlossenes pending Raidende. Vollprozess 64 Warnungen einschließlich 5 Fika-, 1 DontDestroyOnLoad- und 4 Harmony-Startwarnungen. Die 41 Serverwarnungen sind ausschließlich die zuvor belegten Startup-Stapelnormalisierungen; keine zusätzlichen Raid-Serverwarnungen. |
| BEAR 5v5 Fehlerfenster | Unabhängig im abgegrenzten Raidfenster geprüft: Client/Server Error/Fatal und echte Laufzeit-Exceptions **0**. **44 Clientwarnungen**: 33 BoxCollider, 4 WheelCollider, 4 entfernte alte PerfectCulling-Sampler, 2 Spawn-Timeouts und 1 danach erfolgreich abgeschlossenes pending Raidende. Keine Serverwarnung in diesem Fenster. Die erfolgreiche spätere Aktivierung wird von der beobachteten Startverzögerung getrennt. |
| BEAR 3v3 neue Unity-Fehler vor Arm | Zwei echte Error-Einträge **16:36:59.411/.419**: `AsyncUploadManager Failed to close file` an Bundle-`resS`-Dateien beim Entladen, **vor Arming**. Keine Exception oder Stackspur und keine belegte Zuordnung zu ArenaMode. Nach Arming bis Ende keine weiteren Error/Fatal-Einträge. Abgegrenztes Fenster ab 16:35:30: 39 Warnungen (33 BoxCollider, 4 WheelCollider, 1 Redeploy-Samplerbereinigung, 1 danach aufgelöstes pending Raidende), keine Laufzeit-Exception oder Kamera-NRE; Server 0 Errors/Exceptions/Warnungen. |
| BEAR 4v4 Fehlerfenster | Unabhängig abgegrenzt **16:48–17:06 MESZ**: Client/Server Error/Fatal und Laufzeit-Exceptions **0**; Server außerdem 0 Warnungen. Client **52 Warnungen**: 33 BoxCollider, 4 WheelCollider, 12 fehlende Skriptreferenzen, 1 Redeploy-Samplerbereinigung, 1 Spawn-Timeout und 1 danach aufgelöstes pending Raidende. Geordneter Logout/Client-Patchabbau um 17:06 ist vom zuvor abgeschlossenen Raid getrennt. |
| Native Eingabe nach Profilwechsel | **historischer Blocker**, separat von den fünf abgeschlossenen Raids: Aktivierung und zwei beobachtungsbasierte Klickversuche scheiterten um 14:08–14:10 MESZ mit `GetCursorPos` / `0x80070005`. Nach erneuter RDP-Öffnung seit etwa 15:59 nativ wieder bedienbar; keine daraus abgeleitete Arena-Codeursache. |
| Vollständige Render-/Optik-/Navigationssicherheit | **nicht ausgeführt**; begrenzte Bilder, konkrete Diagnosen und abgeschlossene kurze Abläufe ersetzen keinen Langzeit-/Geometrie-/Alle-Optiken-Test. |

| Neue Teamstarts | 2v2 | 3v3 | 4v4 | 5v5 |
| --- | --- | --- | --- | --- |
| USEC | **bestanden** für aktive Besetzung/Rolle/Spielerrespawns/Profil; Bewegungs-/Kampfnachweis **fehlgeschlagen und einmal gezielt wiederholt** | **bestanden** für aktive Besetzung/Rolle, Botkills, BEAR-Refills und Profil; eigene Respawns/USEC-Refills nicht ausgeführt | **bestanden** für aktive Besetzung/Rolle/Profil; Kampf, Bewegung, Refills und eigene Respawns nicht ausgeführt | **bestanden** für aktive Besetzung, beobachtete Bewegung/Refills, Rolle/Spielerrespawns/Profil; Grenzen wie oben |
| BEAR | **bestanden** für aktive Besetzung/Rolle/drei Spielerrespawns/Profil; erster Lauf ohne Bewegungs-/Kampfnachweis. Zusätzlicher Schussreiz-Lauf: Bewegung, Botkampf, USEC-Refill und Profil **bestanden**, bereits vor eigenem Schuss; Ursache des Unterschieds offen | **bestanden** für vollen Anfangsroster, Botkampf, beidseitige Nachbesetzungen und Profil; keine eigenen Respawns | **bestanden** für schließlich vollen Achter-Roster/Rolle, beidseitige Nachbesetzungen und Profil; Startverzögerung, keine eigenen Respawns | **bestanden** für schließlich aktive Besetzung/Rolle/drei Spielerrespawns/Optikstichproben/Profil; erhebliche Startverzögerung, Kampf-/Nachbesetzungsnachweis **fehlgeschlagen** |

Die normale PMC-Regression ist im beobachteten Menü-/Start-/ADS-/F9-Umfang
bestanden und regulär beendet; ihr gewöhnlicher Profilvergleich wird separat
geführt. Weiterhin **nicht ausgeführt**: Scav-/Hideout-Bewegung, echter PFT-Tactical-Gruppenablauf sowie die oben einzeln
genannten weiteren Doppelsprungfälle. Frühere Erfolge auf dem alten Client werden
nicht in diese Matrix übertragen. Weitere Arena-Enden müssen jeweils vor dem
nächsten Raid denselben Restore-/Persist-/Profilvergleich bestehen.

## Historischer Eingabeblocker um 14:10

Nach fünf geschützt abgeschlossenen USEC-Arenen wurde das Spiel regulär verlassen und ein neues Standard-Testkonto nativ angelegt. Der Spielstart **14:04:09 MESZ** erreichte die Fraktionswahl; noch kein BEAR-Charakter oder BEAR-Raid. Fensteraktivierung und zwei frische, beobachtungsbasierte Klickversuche um **14:08–14:10** scheiterten mit **GetCursorPos / Zugriff verweigert (0x80070005)**. Darauf wurde die Bedienung angehalten. RDP meldete die Sitzung aktiv; eine Desktopsperre oder Arena-Codeursache ist nicht bewiesen. Nach erneuter RDP-Öffnung durch den Nutzer funktionierte die Eingabe wieder.

Belege: Bild 71, Logs 08–10, private/input-blocker.json und private/current-blocked-state-audit.md. Das damals erstellte Verzeichnis **settings-final/** bezeichnet lediglich den Zwischenstand mit noch laufenden Prozessen. Maßgeblicher tatsächlicher Abschluss ist **settings-completed/** nach dem späteren regulären Stopp. Die vorübergehende feste Kitauswahl und übrigen Teständerungen wurden im fortgesetzten Verlauf nativ zurückgestellt; die früheren fünf Profilschutzbelege bleiben gültig.

## Fortsetzung nach erneuter RDP-Öffnung, ab etwa 15:59 MESZ

Der Nutzer hat die RDP-Sitzung wieder geöffnet. Danach ist die Eingabe durch eine
tatsächlich ausgeführte native BEAR-Auswahl bestätigt (Bild 72). Die
Charakterinitialisierung wurde nativ abgeschlossen; Bild 73 zeigt das erreichte
Hauptmenü des getrennten BEAR-Testprofils. Das beseitigt den praktischen
Eingabeblocker für die Fortsetzung, beweist aber nicht nachträglich dessen genaue
Windows-/Sitzungsursache. Kein Codefix, Build, Deployment oder manueller
Profileingriff wurde dafür ausgeführt.

| Vorbereitung der BEAR-Raids | Verifizierter Stand / Grenze |
| --- | --- |
| Native Bedienung / Charaktererstellung | **bestanden** als Vorbereitung: BEAR ausgewählt, Initialisierung bis Hauptmenü; Bilder 72/73. Kein Raid in diesem Initialisierungsschritt. |
| Auflösung und Upscaling der Fortsetzung | **2560 × 1440**, DLSS Balanced aktiviert, FSR2/3 aus; tatsächliche Grafikdatei unter `SPT_Runtime/user/sptSettings/Graphics.ini` sowie Kopie `settings-resumed/Graphics.ini`. Gegenüber dem früheren Blockstand ändern sich zwölf Auflösungs-/Seitenverhältnisfelder. |
| UI-Skalierung der Fortsetzung | **4/3 ≈ 1,3333 abgeleitet**, `min(2560/1920, 1440/1080)` nach demselben oben belegten Algorithmus; keine Live-Canvas-Probe. 0,875 gilt für die früheren 1920 × 945. |
| Reguläres Stoppen vor neuer Basis | Spiel über EXIT um **16:00:49**, Server anschließend um **16:01** regulär mit Stop-Signal beendet; beide Prozesse vor Basissicherung gestoppt. |
| Gestoppte BEAR-Ausgangsbasis | Sicherung **16:01:21**, `private/bear-stopped-baseline.json`: Standard, Fraktion Bear, Level 1, XP 0, **183 PMC-Inventareinträge, 500000 RUB**, keine USD/EUR. Eigenständige neue Basis, kein übernommener USEC-Vergleichswert. |
| Weitere unabhängig geprüfte Basiswerte | Nichtgeldmenge 1481, Quests 0, TaskConditionCounters 0; Scav 32 Inventareinträge, Level 1 und XP 0; keine laufende Raidzuordnung. Native Initialisierung abgeschlossen. |
| DLL-Kontrolle | Client, Server und Redeploy.Core um **16:01:52** erneut exakt auf den dokumentierten Lieferhashes; keine neue Auslieferung. |
| Frischer Wiederanlauf | Server **16:01:40**, Spiel **16:03:30**; Bild 74 bestätigt zuvor das richtige initialisierte Testprofil im Launcher. Dieser frische Prozess ist die Basis der folgenden Optik-/Arena-Tests, kein selbständiger Erfolg dieser Tests. |

Die unabhängig bestätigte geschützte Projektion der gestoppten BEAR-Basis hat
SHA256 **`24ef24a20c6e31c1a3c757cbb435b15a03c652aff7936160ca88c6aee2a3b21e`**.
Davon verschieden ist der SHA256 der gesamten gesicherten Profildatei:
`E77E0132501ECDC7A2BB9314AA3AA349A2C25772F1E7B18AF81D46EDF7FC689A`.
Der private Nachweis `private/bear-baseline-audit.md` trennt beide Werte.
Neue Logs liegen in `logs/11-before-bear-baseline-game-exit/`,
`logs/12-before-bear-baseline-server-stop/` und
`logs/13-before-fresh-bear-game-start/`.

Die direkte BEAR-Vorraidbasis wurde vor dem ersten Arm um **16:06:09.920**
gesichert. Der unabhängige Nachtrag in `private/bear-baseline-audit.md` bestätigt
**41 ausschließlich ergänzte `StackObjectsCount=1`-Felder** innerhalb der geschützten
Projektion. Jedes gehört zu genau einer ProfileFixer-Warnung um 14:04:24.340–.369 UTC;
alle Inventaridentitäten und Templates bleiben gleich. Mengen, XP, Level, Quests,
Counter und Skills sind sonst unverändert, ebenso zusätzlich geprüfte PMC-/Scav-Stats
und das gesamte Scav-Objekt. Die maßgebliche direkte Vorraid-Projektion hat SHA256
**`5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`**.
Das vollständige Profil enthält daneben normale Startmetadaten, darunter Health-/Hideout-Zeit,
14 Mod-Metadatensätze und eine noch nicht eingesammelte Mechanic-Geschenknachricht.
Die Aussage über genau 41 Änderungen gilt ausdrücklich für die geschützte Projektion.

`RandomKit=true` und leeres `SelectedKit` wurden anschließend nativ zurückgestellt
(Bild 75), durch lesenden Konfigurationsabgleich um **16:14:50** bestätigt.
Die frühere Kopie `settings-resumed/com.maschine.ArenaMode.cfg` bleibt als
zeitlich davor liegender Ausgangsbeleg mit `false` / `vanguard` erhalten.
Die Charakter-/Neustartvorbereitung allein ist weiterhin kein BEAR-Raidpass.

## BEAR – abgeschlossener 2v2-Raid nach der Fortsetzung

Der Raid wurde regulär über ESC → Disconnect → Confirm Leave beendet.
Bild 88 zeigt **LEFT 00:07:46 und 0 XP**. Der unabhängige Endaudit
`private/bear-2v2-independent-audit.md` prüft native Bilder, gesicherte Logs und
gespeicherte Profile; Rohlogs liegen in `logs/14-after-bear-2v2/`.

| Fall | Beobachtung / Status |
| --- | --- |
| Neues Teamstart-Preset, BEAR 2v2 | **bestanden für aktive Besetzung und Rolle**: Bild 79 bei HUD 1:02 zeigt alle vier Teilnehmer LIVE und die menschliche Rolle BEAR. |
| Arm / Start / volle Aktivierung | Arm **16:08:20.657**, Matchstart **16:09:05.440**, volle Team-READY-Meldung **16:09:54.700**. Die volle native Besetzung ist durch Bild 79 zusätzlich belegt. |
| Initialer Spielerstart | **16:08:25.892**, östliche Position **(478,86; 2,70; −79,23)**; die Seitenzuordnung ist für diesen Start belegt. Noch kein daraus abgeleiteter Nachweis aller weiteren Spawns. |
| Wegpunktprüfung | **16:09:06.533**: 28/28 gesampelt, erreicht und frisch, 2 über Wegpunkte wiederhergestellt, **116 gerichtete Abfragen insgesamt**; Area READY **16:09:06.536**, 17 verbundene Spieler-/11 Botmarker. Kein daraus abgeleiteter Bewegungs- oder Pro-Frame-Leistungsnachweis. |
| Kartenvergleich | Bild 80 bei HUD **1:34** und Bild 83 bei HUD **6:09** zeigen relativ zu den Gebäuden dieselben drei Botpositionen, Abstand **4:35 HUD-Zeit**. Ein roter BEAR-Verbündeter und zwei gelbe USEC-Gegner; Rot/Gelb bezeichnen Fraktionen, nicht pauschal Gegner/Verbündete. Die grüne eigene PMC-Position hat sich durch Respawn leicht geändert; danach Kartenkoordinaten **(481,06; −79,22; 2,60)** in der dortigen Achsenreihenfolge. |
| Drei eigene Aufgeben-Respawns | **bestanden**: Bild 81 Outlaw/Saiga-9, fertig **16:14:35.123 in 1,62 s**; Bild 84 Sovereign/AK-12, fertig **16:15:40.456 in 1,61 s**; Bild 86 Phantom/SR-25, fertig **16:16:03.924 in 1,58 s**. Bundles jeweils 0,03–0,05 s; Pipeline 1,55–1,58 s. |
| ADS nach Respawn | Bild 82 mit Saiga-9 bei HUD **5:37** zeigt ein rotes Absehen; Bild 85 mit AK-12 bei HUD **6:41** einen roten Ring. Absehen sichtbar, **kein Vergrößerungsbeleg** und kein daraus abgeleiteter Test aller Optiken. |
| Abschließendes Roster | Bild 87 bei HUD **6:58**: alle vier Teilnehmer LIVE; alle Bots 0 Kills/0 Tode, Mensch 0 Kills/3 Tode. Die eigenen Aufgeben-Tode sind keine nachgewiesenen gegnerischen Kampf-Kills. |
| Bewegung, Botkämpfe und Nachbesetzungen | **fehlgeschlagen** als geforderter Nachweis: gleiche drei Marker in den zwei Kartenstichproben, keine Botkills/Refills beobachtet. Keine kontinuierliche Weltbeobachtung aller Bots und keine isolierte Engineursache; derselbe begrenzte 2v2-Befund ist bereits aus USEC und dessen gezieltem Nachtest bekannt. |
| Arena-Ende und geschützter Profilvergleich | **bestanden**: Server-Restore **14:16:51.921 UTC**, erfolgreiche Persistenz **14:16:51.968 UTC**. `private/bear-2v2-protected-diff.json` meldet **0 geschützte Unterschiede**; Vorher-/Nachher-Hash jeweils `5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`. |

Initial wurden genau ein BEAR-Verbündeter und zwei USEC-Gegner zugelassen;
anschließend gibt es keine weiteren Botzulassungen oder Refills. Zwei initiale
USEC-Zulassungen verwenden nacheinander denselben Marker. Dies belegt allein keine
gleichzeitige physische Überlappung. Nach vollständiger Aufstellung werden alle
geeigneten verbundenen Punkte für spätere Respawns freigegeben; eine spätere
Sektoränderung wäre deshalb nicht automatisch ein Fehler. Keine Movement-Watch-,
konkrete Pfadablehnungs-, Gebiets-Recovery- oder Spawn-Timeout-Diagnose in diesem Lauf.

Anfangskit Spartan ist um **16:09:38.611** fertig. Bild 78 zeigt M4A1-Reflex-ADS
bei HUD 0:51, ungefähr **16:09:56**, während die serielle Katalogvorladung noch bis
**16:11:35.575** läuft. Das bestätigt eine Überlappung mit der Vorladephase,
keinen genauen Optik-Callback-Aufruf. Die Guard-Patches werden um
16:09:07.494–.559 registriert, die positive Laufzeitmeldung über tatsächlich
zurückgehaltene unmontierte Optik-Callbacks erscheint jedoch **nicht**.
Ihr Fehlen allein ist kein Fehler; der konkrete abgesicherte Callback-Fall bleibt
unbeobachtet. Aus den Reflex-/Holo-Bildern folgt weder Vergrößerungs-ADS noch
kontinuierliche Flackerfreiheit; nach dem dritten Respawn liegt nur die gerenderte
SR-25 ohne eigenen ADS-Beleg vor.

Die Ersatzkamera wird vor dem Anfangswechsel um **16:09:38.593** und vor allen drei
Respawnabschlüssen um **16:14:35.116**, **16:15:40.449**, **16:16:03.918** neu gebunden.
Die PFT-Menü-Aufschubdiagnose **16:16:29.854** ist ein Logereignis, kein zusätzlicher
nativer Menüpass. Keine Doppelsprung-Erfolgsdiagnose; kein weiterer Sprungtest
wird aus diesem Raid abgeleitet.

Die Session endet **16:16:51.826**, das Match **16:16:51.832**. Der unabhängige
Profilvergleich bestätigt die beiden geschützten Hashes erneut, zusätzlich
gleiche PMC-/Scav-Stats und das gesamte gleiche Scav-Objekt. PMC bleibt bei
183 Inventareinträgen, XP 0 und unveränderten Mengen/Geld. Im vollständigen Profil
ändert sich nur ein ungeschützter Hideout-Aktualisierungszeitstempel.

## BEAR – abgeschlossener 5v5-Raid

Unabhängiger Audit: `private/bear-5v5-independent-audit.md`, Rohlogs in
`logs/15-after-bear-5v5/`. Abgegrenztes Fehlerfenster ab **16:18 MESZ** bis zur
Archivaufnahme; Serverfenster entsprechend ab **14:18 UTC**.

Matchstart **16:21:03.879**, vollständige Team-READY-Meldung erst
**16:28:06.977**, also **7:03,098 nach Matchstart**. Zuvor traten aufgefangene
initiale Spawn-Timeouts auf. Bild 96 bei HUD **8:26** zeigt alle zehn Teilnehmer
LIVE, ohne Kills oder Tode. Der erreichte volle Roster beseitigt die beobachtete
lange Startverzögerung nicht. Der unabhängige Audit bestätigt BEAR-Timeouts um
**16:22:42.888** nach 45 s und **16:25:53.505** nach 46 s sowie fünf verworfene
überholte Spawnvorgänge. Alle neun tatsächlichen Botzulassungen liegen vor der
vollen READY-Meldung; danach keine Nachbesetzungen. Abgelehnte oder überholte
Anfragen werden nicht als zusätzliche aktive Bots oder Kampf-Refills gezählt.

| Fall | Tatsächlicher Befund / Status |
| --- | --- |
| Initialer Spielerstart und Verbindung | **16:20:36.579**, BEAR im Osten bei **(480,24; 2,70; −84,47)**. Graph **16:21:04.763**: 28/28 gesampelt, erreicht und frisch, 4 über Wegpunkte wiederhergestellt, **120 gerichtete Abfragen insgesamt**; Area READY **16:21:04.766**. Gebiets-READY ist vom erst später vollständigen Mannschafts-READY getrennt. |
| Vergrößerungs-ADS vor Respawn | **bestanden** als Einzelbild: Bild 97 bei HUD **9:17** zeigt die M4 mit vergrößertem Visierbild und sichtbarem Fadenkreuz mit gelbem Zentrum. Kein exaktes Optikmodell oder Zoomfaktor aus dem Bild allein behauptet, keine kontinuierliche Flackerfreigabe. |
| Drei eigene Aufgeben-Respawns | **bestanden**: Bild 99 Wraith/MCX, fertig **16:31:21.855 in 1,73 s**; Bild 101 Specter/RSASS, fertig **16:31:49.420 in 1,69 s**; Bild 103 Outlaw/Saiga-9, fertig **16:32:16.139 in 1,68 s**. Jeweils vorher Ersatzkamera-Rebind **16:31:21.848**, **16:31:49.414**, **16:32:16.134**. |
| ADS nach Respawn | **bestanden** als begrenzte Bilder: Bild 100 nach Respawn 1 bei HUD **10:24** mit MCX-Reflexvisier; der Cursor verdeckt teilweise das Absehen. Bild 102 nach Respawn 2 bei HUD **10:52** zeigt RSASS-ADS mit sichtbarem roten Punkt. Aus diesen Bildern kein Vergrößerungs-ADS oder ADS nach Respawn 3 abgeleitet. |
| Platzierung und Beweglichkeit nach Respawn | Alle drei eigenen Respawns am offenen Van. Zweimalige native Rückwärtsimpulse führen frei heraus; Bild 104 belegt die danach freie Position. Kein Feststecken behauptet und keine vollständige Prüfung aller Spawngeometrien. |
| Kartenvergleich | Bild 98 bei HUD **9:39**, Bild 106 bei HUD **12:23**, Abstand **2:44 HUD-Zeit**: weitgehend gleiche vier BEAR-Marker beim Depot und fünf USEC-Marker im Bereich Warehouse 4. Beim Schließen der Karte um HUD 9:49 wurde eine kleine gelbe Markerbewegung beobachtet, aber nicht separat archiviert. Deshalb keine Behauptung, sämtliche Bots seien durchgehend vollkommen unbeweglich. |
| Kampf, Nachbesetzungen und Schlussroster | **fehlgeschlagen** als Kampf-/Nachbesetzungsnachweis: keine Botkills oder Refills beobachtet. Bild 107 bei HUD **12:59** zeigt alle zehn LIVE; alle Bots K0/D0, eigener Mensch K0/D3. Die drei eigenen Give-ups sind keine nachgewiesenen gegnerischen Kampf-Kills. Kein zusätzlicher Wiederholungslauf vorgesehen. |
| Sprungfolge nach Respawn 3 | Bildfolge 105 auf freiem Boden bei HUD **11:54/11:55/11:56**: normales Abheben, zwischen zweiter und dritter Eingabe wieder unten, keine Extraimpuls-Erfolgsdiagnose. Flugphase nicht zuverlässig getroffen; **unbestätigte Folge, kein daraus abgeleiteter Doppelsprungfehler**. Die früheren positiven USEC-Folgen bleiben gültig. |
| Reguläres Ende | **bestanden**: Bild 108 unabhängig bestätigt mit **LEFT 00:13:38 und 0 XP**. Ende ist vom dreimaligen Aufgeben/Respawn getrennt. |
| Geschütztes Profil | **bestanden**: Server-Restore **14:34:42.338 UTC**, Persistenz **14:34:42.503 UTC**; Vergleichssicherung **16:35:03.181 MESZ**, `private/bear-5v5-protected-diff.json` mit **0 Unterschieden**, vor/nach jeweils unabhängig neu bestätigter Hash `5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`. Zusätzlich PMC-/Scav-Stats und ganzes Scav-Objekt gleich; im vollständigen Profil nur ein geänderter ungeschützter Hideout-Zeitstempel. |

Die Bilder 92/93 bestätigen den begrenzten negativen Test bei ausgeschaltetem
Movement-Masterschalter, 94/95 seine native Rückstellung. In diesem BEAR-Lauf
ist nach Rückstellung kein positiver Extraimpuls bestätigt; daraus folgt ohne
getroffene Flugphase kein Defekt des Schalters. Doppelsprung-Einzelschalter und
Höhe bleiben auf Enabled / 1,0, soweit zuletzt bestätigt. Im gesamten BEAR-5v5-Log
gibt es keine Extraimpuls-Erfolgsdiagnose.

Der initiale Kamera-Rebind erfolgt **16:21:18.657**, der Spartan-Kitwechsel ist
**16:21:18.672** fertig. Die Katalogvorladung läuft bis **16:31:43.217**.
Bild 97 bei HUD 9:17 entspricht ungefähr **16:30:21** und liegt damit noch in
dieser Vorladephase. Das ist zusätzlich ein sichtbarer Vergrößerungs-ADS-Fall
während Hintergrundladen, weiterhin ohne tatsächliche `withheld unmounted optic`
Diagnose. Sessionende **16:34:42.208**, Matchende **16:34:42.209**.

## BEAR – abgeschlossener 3v3-Raid

Bild 111 bei HUD **1:28** zeigt die volle Anfangsbesetzung mit sechs Teilnehmern.
Im späteren nativen Zustand bei HUD **5:39** steht BEAR **2:1** gegen USEC;
der BEAR-Verbündete **alias** hat einen Kill, und **RootsNine** ersetzt den zuvor sichtbaren
Verbündeten **ANIME**. Auf USEC-Seite sind Badger und pitAlex sichtbar, der dritte
Platz ist in diesem Zwischenstand gerade nicht wieder vollständig besetzt.
Die eigene Statistik bleibt 0 Kills/0 Tode. Damit sind Botkampf und eine tatsächliche
freundliche Nachbesetzung beobachtet; eine ununterbrochen volle Mannschaft oder
vollständige gegnerische Rückkehr wird aus diesem Zwischenstand nicht abgeleitet.
Bild 113 bestätigt das reguläre Ende mit **LEFT 00:06:41 und 0 XP**, ohne eigene
Kills oder Tode. Server-Restore **14:46:01.956 UTC**, erfolgreiche Persistenz
**14:46:01.996 UTC**. Der gespeicherte Vergleich **16:46:32.845 MESZ** in
`private/bear-3v3-protected-diff.json` enthält **0 Unterschiede**; Vorher-/Nachher-Hash
jeweils `5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`.
Rohlogs sind in `logs/16-after-bear-3v3/` gesichert. Exakte Start-/Nachbesetzungszeiten
und das vollständige Fehlerfenster sind unabhängig in
`private/bear-3v3-independent-audit.md` ausgewertet.

Arm **16:38:45.859**, initialer BEAR-Start **16:38:50.764** im Osten bei
**(478,86; 2,70; −79,23)**, Matchstart **16:39:20.898**. Graph **16:39:21.804**:
28/28 gesampelt, erreicht und frisch, 2 über Wegpunkte wiederhergestellt,
**116 gerichtete Abfragen insgesamt**, Area READY **16:39:21.807** mit
17 Spieler-/11 Botmarkern. Die volle Team-READY-Meldung folgt **16:40:18.271**,
also **57,373 s nach Matchstart**. Keine Spawn-Timeouts, kein Navigationsabbruch
und keine eigenen Respawns. Startkit Reaper/RD-704, Kamera-Rebind **16:39:35.950**,
Kitwechsel fertig **16:39:35.982**; kein neuer ADS-/Optik- oder Sprungtest.
Matchende **16:46:01.699** mit eigenem 0/0.

Der unabhängige Vergleich bestätigt beide geschützten Hashes erneut; zusätzlich
PMC-/Scav-Stats und gesamtes Scav-Objekt gleich. Die BEAR-Basis behält 183
Inventareinträge, Nichtgeldmenge 1481, 500000 RUB, XP 0, Level 1 und keine Quests.
Im vollständigen Profil ändert sich nur ein ungeschützter Hideout-Zeitstempel.
Vier echte Ersatzzulassungen nach READY sind protokolliert: BEAR RootsNine
**16:41:47.103**, USEC pitAlex **16:45:02.163**, USEC itsMickle **16:45:17.805**
und BEAR Carl **16:45:18.492**. Zulassung und tatsächlich im Bild sichtbares LIVE
bleiben getrennt: Bild 111 belegt den vollen Anfangsroster, Bild 112 bei HUD 5:39
zeigt nur fünf LIVE. Zwei Unity-Dateischließfehler vor dem Arm sind oben separat erfasst.

Jede der vier Ersatzzulassungen hat einen anschließenden nativen Brain-Eintrag;
sie sind von den fünf Anfangszulassungen getrennt. Der später bei HUD etwa 6:01
beobachtete Teamstand **2:2** ist ein zusätzlicher Bedienerbefund und kein Inhalt
des gespeicherten Bilds 112. Exakte Schütze-/Opfer-Paare aller Kills sind nicht belegt.

## BEAR – abgeschlossener 4v4-Raid

Unabhängiger Endaudit: `private/bear-4v4-independent-audit.md`, einschließlich
des getrennten Controls-Vergleichs und des späteren Client-Logouts.

Bild 114 zeigt die native 4v4-Auswahl bei **2560 × 1440** und die angezeigte
Spielzeit **10:37**. Diese Menüzeit ist keine lokale Ereignisuhrzeit.
Bild 116 zeigt zunächst nur sieben LIVE und einen verzögerten Spawn. Bild 120
bei HUD **6:18** zeigt schließlich alle acht Teilnehmer LIVE, korrekte Rollen,
Teamstand **1:1** und notGreg mit einem Kill. Eigener PMC bleibt 0 Kills/0 Tode;
keine eigenen Respawns in diesem Startertest. Bild 121 bestätigt
**LEFT 00:07:18, XP 0**.
Server-Restore **14:57:17.777 UTC**, Persistenz **14:57:17.827 UTC**;
Vergleichssicherung **17:06:18.921 MESZ**, `private/bear-4v4-protected-diff.json`
mit **0 Unterschieden** und auf beiden Seiten
`5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`.
Rohlogs: `logs/17-after-bear-4v4/`. Der unabhängige Vergleich bestätigt den
geschützten Hash erneut; zusätzlich PMC-/Scav-Stats und das gesamte Scav-Objekt
gleich, im vollständigen Profil nur ein geänderter ungeschützter Hideout-Zeitstempel.

Arena-Arm **16:49:23.613**, initialer BEAR-Start **16:49:28.333** im Osten bei
**(481,06; 2,70; −79,22)**, Matchstart **16:49:59.889**,
Area READY **16:50:00.889**. Graph **16:50:00.887**: 28/28 gesampelt, erreicht
und frisch, 4 über Wegpunkte wiederhergestellt, **120 gerichtete Abfragen insgesamt**;
17 Spieler-/11 Botmarker verbunden.
Voller Team-READY-Start **16:53:48.236**, **3:48,347 nach Matchstart**.
USEC-Anfrage 7 läuft **16:52:17.490** nach 46 s aus; verspätetes Profil verworfen
**16:53:26.622**, abgewickelt **16:53:27.483**, Ersatz-Anfangszulassung 8
**16:53:47.205**. Diese Anfangsauffüllung ist kein Kampf-Refill.
Nach vollem READY folgen zwei echte Bot-Nachbesetzungen: BEAR **Lacyway**
**16:55:51.323** und USEC **Nomad_420** **16:55:59.864**; danach zeigt Bild 120
den vollen Roster. Keine vollständige Bewegungs-/Geometrieprüfung daraus abgeleitet.

Es sind sieben Anfangszulassungen und zwei spätere Ersatz-Zulassungen, keine
neun gleichzeitig aktiven Bots. Startkit Comrade/SKS, Kamera-Rebind
**16:50:20.077**, Startkit fertig **16:50:20.086**. Die Katalogvorladung endet
**16:54:46.639**; kein gesonderter ADS- oder Optik-Guard-Treffer in diesem Lauf.
Matchende **16:57:17.496**. Die im Schlussbild sichtbaren 396/440 Gesundheit
haben keine gesichert zugeordnete Ursache und werden nicht als bestimmter Fall-
oder Kampfschaden bewertet.

Die neue Sprungbelegung wird in Bild 117 mit **Space / Release**, danach in Bild
118 mit **Return / Release** gezeigt. Drei Return-Eingaben der Bildfolge 119
erzeugen genau eine zusätzliche Impulsdiagnose **16:56:06.370**. Dieser konkrete
Test besteht. Die Konfigurationsgegenüberstellung zeigt zugleich als zweite
Änderung **Fire: Mouse0 → leer** infolge der nativen Belegungsbedienung.
Beide Eingabewerte stehen beim späteren Nutzerstopp noch so; sie sind kein
zurückgestellter Ausgangszustand. Jump **Release** und Fire **Continuous** sowie
alle übrigen Control.ini-Felder bleiben im Vergleich unverändert. Die versehentliche
Fire-Entfernung wird als Bedienungsnebenwirkung dokumentiert, nicht als Arena-Spielmechanikfehler.

## Historischer Konfigurationsvergleich um 16:50 MESZ

Nur lesende Momentaufnahme **16:50:17.992–16:50:18.419**, während die Tests
weiterlaufen; **kein finaler oder regulär gestoppter Endzustand**. Gegen
`settings-initial/` weichen **2 von 106** Client-Einstellungen inhaltlich ab:

| Datei / Wert | Anfang | Momentaufnahme |
| --- | --- | --- |
| `BepInEx/config/com.maschine.ArenaMode.cfg`: Match / GameMode | `endless` | `teamdeathmatch` |
| Dieselbe Datei: Team Deathmatch / ExperimentalArea | `full-map` | `customs-factory-center-teams-v1` |
| Übrige Clientwerte | Ausgangswerte | Inhaltlich gleich, einschließlich AreaPlayersPerTeam 4, RandomKit true, SelectedKit leer, Movement/Doppelsprung aktiv und Doppelsprunghöhe 1 |
| `SPT_Runtime/user/sptSettings/Graphics.ini` | Frühere 1920 × 945 / gespeicherte Fenstergröße 1904 × 858 | Ausschließlich zwölf Auflösungs-/Seitenverhältniswerte auf 2560 × 1440 / 16:9 geändert; Upscaling einschließlich DLSS Balanced gleich |
| `SPT_Runtime/user/mods/ArenaMode/config/config.json` | Gesicherte Anfangsdatei | Bytegleich; SHA256 `5A6965111C80861D3F888205E62C1E07137E9365F48733D969B4C0B058F1B90F` |

Generierte Kommentare sind keine geänderten Einstellungswerte. Modus/Gebiet sind
die in diesem Zeitpunkt verbliebenen nativen Arena-Rückstellziele; spätere
Testauswahlen benötigen vor dem tatsächlichen Abschluss erneut einen Vergleich.
Die bekannte Grafikänderung der RDP-Fortsetzung wird davon getrennt dokumentiert.

## Historischer Nutzerstopp um 17:06 MESZ

Der Nutzer stoppte Computer Use durch physisches Escape; bis zur erneuten ausdrücklichen Freigabe erfolgte keine weitere native Eingabe. Das bereits raidfreie Spiel wurde um **17:06 MESZ** mit geordnetem Logout geschlossen, ohne entsprechende Bedienhandlung des Testagenten. Server-Logout **15:06:00.565 UTC**, Client-Patchabbau bis **17:06:02.207 MESZ**; kein daraus abgeleiteter Crash oder verursachender Bediener. Alle damals neun Arena-Enden waren geschützt verglichen.

Offen waren zu diesem Zeitpunkt die bei der nativen Neubelegung entstandenen Änderungen Jump Return und Fire unbelegt. Sie wurden nach Freigabe gezielt über die native Oberfläche wiederhergestellt; **17:48:55.553 MESZ** bestätigt der vollständige Controls-Vergleich 0 Unterschiede. Private Vorher-/Zwischen-/Rückstellbelege bleiben erhalten. Der vorübergehende Stopp ist kein offener Abschlusszustand dieses Berichts.

## Erneute Fortsetzung ab 17:43 MESZ und gezielter Schussreiz

Der Nutzer erlaubt die Fortsetzung ausdrücklich und bittet, bei den bisher
stehenden Bots Schüsse abzugeben, da diese möglicherweise darauf reagieren.
Dieser zusätzliche, gezielte Stimulus ist autorisiert; die früher dokumentierte
Begrenzung weiterer unveränderter Wiederholungen wird nicht als Verbot dieses
neuen Untertests ausgelegt. Die bisherigen negativen Karten-/Kampfstichproben
bleiben erhalten. Die Vorher-/Nachher-Beobachtung unterschied den tatsächlichen Schuss
und anschließende Bewegung, Kampf oder Nachbesetzung; eine
Reaktion danach wäre zunächst ein zeitlicher Zusammenhang, kein alleiniger
Beweis für die Ursache des früheren Stillstands. **Ausgeführt:** schon vor dem
ersten Schuss sind Bewegung, Botkampf und eine Nachbesetzung vorhanden. Damit
ist ein ursächliches Aufwecken durch den eigenen Schuss in diesem Lauf
**nicht nachgewiesen**; die genaue Beobachtungsfolge steht unten.

| Vorbereitung | Tatsächlicher Stand / Beleg |
| --- | --- |
| Logs vor erneutem Neustart | `logs/18-before-resumed-restart/`, Archivzeit **15:43:28.389 UTC / 17:43:28.389 MESZ** |
| Installierte Dateien | Alle drei SHA256 erneut nur lesend geprüft und unverändert zur oben angegebenen Lieferung von `e2859e8`; kein Austausch, Build oder Deployment |
| Frischer Server | **17:43:35 MESZ**, PID 1152 |
| Launcher-Kontoauswahl | Zunächst automatisch das Hauptprofil ausgewählt, sofort normal abgemeldet. Kein Spielstart oder Raid mit dem Hauptprofil. Anschließend getrenntes BEAR-Testprofil gewählt. |
| Frischer Spielstart | **17:45:03 MESZ**, PID 5040, tatsächlich das getrennte BEAR-Testprofil; native Anzeige 500000 RUB und null gewöhnliche Raids |
| Native Darstellung | Erster Spielcapture **1920 × 1080** nach neuer RDP-Sitzung. Daraus UI-Skalierung **1 abgeleitet**, keine Live-Canvas-Probe. Voriger BEAR-Abschnitt 2560 × 1440; die endgültige Datei-/Bildprüfung bestätigt 1920 × 1080 für die späten Läufe, siehe Abschluss. |
| Tastenbelegung | Gezielte native Wiederherstellung gespeichert und **17:48:55.553 MESZ** geprüft: Jump Space/Release (Bild 122), Fire Mouse0/Continuous (Bild 123); vollständiger Vergleich **0 Unterschiede**, `private/control-restored.ini` und `private/control-restored-diff.json` |

Der Neustart allein begründet keinen weiteren Raidpass. Der zusätzliche
Schussreiz-Raid ist regulär beendet und geschützt verglichen: **zehntes**
Arena-Ende. Kein aktueller Legacy-/Normal-PMC-Erfolg daraus abgeleitet.

Die vollständige Controls-Projektion hat vor und nach Rückstellung denselben
Hash `a4dda6e627e242e4d175c0e9275261e949b924a30fd10a0e9ca33ec095084746`.
Keine globale Rückstellung aller Standardbelegungen und keine manuelle
Einstellungsdateiänderung wurde dafür vorgenommen.

Vor dem gezielten Schussreiz-Raid wurde
`private/before-bear-2v2-shots-profile.json` gesichert. Unabhängiger Vergleich
gegen `private/after-bear-4v4-profile.json`: **geschützte Inhalte identisch**,
mit dem bisherigen rekursiven String-Serializer beide weiterhin
`5054b12e66db33e3cfe2c9cf7b1b6bc85d94af5c692f9d550f256c39ce2cea5a`.
Nach Neuinitialisierung der Prüfsitzung liefert ein Serializer aus sortiertem
Objekt und `JSON.stringify` für beide Dateien stattdessen
`37e00e8d0e6b3cb1c4d1011512e07d981befb1eae04a6464815941e338944338`.
Der Unterschied ist vollständig erklärt: das im Scav-Profil fehlende
`RepeatableQuests` wird in der geschützten Projektion `undefined`; der bisherige
String-Serializer schreibt dafür einen wörtlichen `undefined`-Eintrag, während
`JSON.stringify` dieses Feld auslässt. Die beiden Hashverfahren werden getrennt
gehalten; dieser Darstellungsunterschied ist **keine Profiländerung** und kein
fehlgeschlagenes Restore.

### Zusätzlicher BEAR-2v2-Lauf: Kampf bereits vor dem Schussreiz

Dieser begrenzte Untertest verwendet erneut das neue Teamstart-Preset und die
vorher gesicherte geschützte BEAR-Basis. Native Bildschirmbelege 126–135,
**1920 × 1080**; clientseitiges reguläres Matchende **17:57:45.548 MESZ**.
Bild 136: **LEFT 05:49, XP 0**. Keine weiteren unveränderten Wiederholungen
dieses Befunds geplant.

| Zeitpunkt / Beleg | Beobachtung |
| --- | --- |
| HUD 00:43, Bild 126 | Vier LIVE: eigener BEAR-PMC und Spock gegen USEC Wrecker und rozema. Vollständige Besetzung vor dem Stimulus. |
| Bild 127 | Titan-Kit mit ungedämpftem Mk 17, M80, 20/20 im Magazin plus eine Patrone im Patronenlager. |
| HUD 01:11, Bild 128 | Eigene Kartenposition (481,26; −80,17; 2,60); roter BEAR-Verbündeter im Bereich Warehouse 4 nahe den gelben USEC-Markern. |
| HUD 02:10, Bild 129, **vor jedem eigenen Schuss** | BEAR-Verbündeter gegenüber Bild 128 bis Warehouse 3 verschoben; Teamstand bereits **1:0**, gegnerische Leiche und Ersatz sichtbar. Ein positiver Bewegungs-/Kampfbefund ohne eigenen Schussreiz. |
| **17:54:01.104 MESZ**, Clientlog | Eine echte USEC-Nachbesetzung nach voller Aufstellung: Anfrage 4, 2/2, Ankunft 6,5 s; lazheng mit USEC-Brain um 17:54:01.818. Dieser Ersatz liegt **39,357 s vor dem ersten eigenen Schuss**. |
| HUD 02:38 / 02:51 / 03:02, Bilder 130–132 | Drei einzelne eigene Schüsse um **17:54:40.461 / 17:54:53.194 / 17:55:04.522 MESZ**; angezeigte Munition nacheinander 19, 18 und 17. |
| HUD 03:18, Bild 133 | Roter Verbündeter zurück im Bereich Warehouse 4; zwei gelbe Gegner vorhanden. |
| HUD 04:52, Bild 134 | Ein östlicher USEC-Marker ungefähr gleich; der andere, südlich gelegene USEC-Marker hat gegenüber Bild 133 seine Position verändert. Keine fortlaufende Verfolgung sämtlicher Pfade oder präzise Laufrichtung daraus abgeleitet. |
| HUD 05:09, Bild 135 | Wieder vier LIVE: Spock mit einem Kill, eigener PMC 0 Kills / 0 Tode, USEC Wrecker und lazheng jeweils 0/0. |

**Bestanden** für vollständige Besetzung, beobachtete Botbewegung/-kampf,
gegnerische Nachbesetzung und den tatsächlich ausgeführten Schussreiz.
**Nicht nachgewiesen** ist die vermutete Ursache: die Bots kämpfen und bewegen
sich bereits vor dem ersten eigenen Schuss. Die früheren ruhigen Läufe bleiben
korrekt dokumentiert, sind aber kein Nachweis, dass jeder 2v2-Lauf stehen bleibt.
Keine eigene Respawnserie oder vollständige Grenzprüfung aus diesem Untertest.

Erster BEAR-Start **17:51:17.603**, östlich bei (481,06; 2,70; −79,22).
Matchstart **17:51:56.157**, Graph **17:51:58.143** mit 28/28 gesampelten,
erreichten und frisch geprüften Markern, vier via Wegpunkte und 120 gerichteten
Abfragen; Area READY **17:51:58.146**. Vollständiges 2v2 READY
**17:52:40.251**, also **44,094 s nach Matchstart**. Anfangs drei Botzulassungen,
später genau der oben getrennt belegte USEC-Ersatz.

Archiv `logs/19-after-bear-2v2-shots/`; Server-Restore
**15:57:45.823 UTC**, Persist **15:57:45.884 UTC**. Root-Profilvergleich
**17:58:20.771 MESZ**, `private/bear-2v2-shots-protected-diff.json`: **0 Diff**,
mit dem neuen JSON-Serializer beide `37e00…4338`. Unabhängig mit dem bisherigen
String-Serializer beide `5054…cea5a`. PMC-Stats und gesamtes Scav-Objekt gleich;
die vollständigen Profile unterscheiden sich ausschließlich im PMC-Hideout-
Aktualisierungszeitstempel. Keine Bytegleichheit der vollständigen Dateien behauptet.

Unabhängiges Clientfenster **17:49 MESZ bis Archivende**: **0 Error/Fatal und
0 echte Laufzeit-Exceptions**, 39 Warnungen (33 BoxCollider, vier WheelCollider,
eine Redeploy-Samplerbereinigung, ein danach abgeschlossenes pending Raidende).
Die Harmony-Registrierung einer Methode mit `Exception`-Parameter ist kein
ausgelöster Fehler. Im zugehörigen Serverfenster ab **15:49 UTC** keine
Error-/Fatal-/Warning-Einträge oder Exceptions.

Die Einstellungen des getrennten Area-Outline-Entwicklungswerkzeugs
`Enabled` und `AllowInNormalPmc` waren nach dem unabhängigen Einstellungsabgleich
bereits in der Ausgangsbasis aktiv. Sichtbar sind null Entwurfspunkte; der
Testagent hat weder einen Entwurf aufgenommen noch exportiert. Dieser gesonderte
Werkzeugzustand ist bei der späteren normalen PMC-Isolationsprüfung von einer
Aktivierung des Arena-Spielmodus zu unterscheiden.

## Zusätzliche native Menüwechsel ab 18:00 MESZ

Die folgenden gespeicherten Bilder wurden unabhängig gelesen. Native Auflösung
**1920 × 1080**, abgeleitete UI-Skalierung 1. Genannte Zeiten sind die
Bildarchivierung in MESZ, keine framegenauen Eingabezeitpunkte.

| Wechsel / Beleg | Status und tatsächlicher Zustand |
| --- | --- |
| Customs → Factory bei Team DM, Bild 137, **18:00:25.684** | **Bestanden** für diesen Kartenwechsel: Factory ist ausgewählt, das Gebiet lautet Whole map mit `5v5 – configured teams`; das Customs-Teamstart-Preset und dessen Größenauswahl bleiben nicht unzulässig stehen. Native Next/Back und Zeitauswahl sichtbar. |
| Team DM → Tactical auf Factory, Bild 138, **18:00:33.905** | **Bestanden** für die Menütrennung: Regeln wechseln auf Elimination rounds, Respawn Next round und Buy / retain kit; Gebiet Whole map. Kein daraus abgeleiteter echter PFT-Gruppen-, Buy-, Spectator- oder Rundenablauf. |
| Customs → Endless, Bild 139, **18:01:27.403** | **Bestanden** für das Entfernen der unzulässigen Kartenwahl: keine gewählte Karte, Customs nicht als verfügbare Karte angezeigt, Gebiet fordert zuerst eine Kartenauswahl. Kein stale Customs-Preset; Back bleibt sichtbar. |
| Zurück zur Charakterwahl → Arena → Vorwärts, Bild 140, **18:07:05.942** | **Bestanden** für diesen Rückweg: die gültige Kombination Team DM / Customs / neues Teamstart-Preset / 4v4 bleibt ausgewählt. Alle vier Größenschaltflächen lesbar, Native Next/Back nicht verdeckt. Das Behalten einer weiterhin gültigen Auswahl ist kein Fehler. |
| Neues Teamstart-Preset → Factory Center (contour), Bild 141, **18:07:13.630** | **Bestanden** für das alte Konturpreset: `2v2 – fixed teams`, keine 2v2–5v5-Auswahl mehr sichtbar; Next/Back und Zeitauswahl frei. Keine separate feste Größenbestätigung des zusätzlich gelisteten rechteckigen Factory-Center-Presets allein aus diesem Bild. |

Diese Übergänge sind keine vollständige Abnahme aller Kombinationen.

## Aktuelle Legacy-Kontur-2v2-Regression mit BEAR

Nativ gewählt ist **Factory Center (contour)**, internes Gebiet
`customs-factory-center-polygon-v1`, ausdrücklich das alte feste 2v2-Preset.
Arm **18:08:07.457**, Matchstart **18:08:37.651**, Area READY
**18:08:38.391**, vollständiges Team READY **18:09:08.152**. Bilder 142–156
bei **1920 × 1080**; Logs `logs/20-after-bear-legacy/`.

| Prüffall | Status und tatsächlicher Befund |
| --- | --- |
| Feste Größe und volle Mannschaft | **Bestanden**: Menü 141 fest 2v2; Bild 142 bei HUD 00:33 vier LIVE. |
| Sichtbare Holo-Optik | **Bestanden** als ADS-Stichprobe: SVDS in Bild 143 bei HUD 01:06 mit rotem Absehen. Katalogvorladung bis 18:09:47.740. Eine zeitliche Überlappung der Ansicht mit dem Vorladen ergibt sich aus HUD/Matchstart; das Bild wurde erst 18:09:57.100 archiviert, weshalb daraus keine exakte Aufnahmezeit oder bestätigter Guard-Eingriff folgt. |
| Natürlicher Tod und Respawn 1 | **Bestanden**: während der Einstellungsprüfung Tod durch Zmyh beobachtet, anschließend SR-25 (145). Tod 18:11:26.241; Respawn fertig 18:11:27.683, **1,44 s**. Kein Give-up für diesen ersten Tod. |
| Respawns 2 und 3 durch Aufgeben | **Bestanden**: Give-ups 18:13:44.491 und 18:14:15.845; MCX fertig 18:13:45.995 (**1,49 s**), VPO-209 fertig 18:14:17.336 (**1,48 s**). Bilder 149/150, sichtbarer neuer Körper/Waffe und Kontur. Aufgeben ist hierbei Respawn, nicht Matchende. |
| ADS nach Respawns | **Bestanden** für die MCX-Stichprobe in 149 bei HUD 05:13: vergrößerte Ansicht und hellrotes Absehen; Botkillfeed sichtbar. SR-25 in 148 bei HUD 04:16 zeigt intakte Glasdarstellung, das Absehen wird vom Cursor verdeckt und erhält keinen eigenen Sichtbarkeitspass. |
| Botbewegung und Kampf | **Bestanden** als Stichprobe: gelbe USEC-Kartenmarker in 151/152, HUD 05:51 → 06:24, haben ihre Positionen geändert. Killfeed 149 und Schlussroster 155 zeigen Botkills. Keine eigenen Schüsse in diesem Lauf. |
| Nachbesetzungen und später voller Roster | **Bestanden**: zwei USEC-Ersatz-Zulassungen 18:14:19.754 / 18:14:41.100 und drei BEAR-Ersatz-Zulassungen 18:15:21.495 / 18:15:57.869 / 18:16:40.049. Bild 155 bei HUD 07:14 vier LIVE mit eigenem PMC, FiveF, Qwertyalex und PenOkOh; USEC-Bots jeweils ein Kill, Teamstand 2:5. Die letzte BEAR-Zulassung liegt nach Bild 155; kein späterer vollständig sichtbarer Roster daraus abgeleitet. |
| Reguläres Ende und Schutz | **Bestanden**: Matchende 18:16:43.627; Bild 156 **LEFT 08:06, XP 0**. Eigener PMC 0 Kills / 3 Tode. Server-Restore **16:16:43.696 UTC**, Persist **16:16:43.769 UTC**; geschützter Vergleich **0 Unterschiede**. |

InfiniteStamina wurde in Bild 144 ausgeschaltet. Die zwei Space-Eingaben bei
HUD 03:12 und 03:22 liegen rund zehn Sekunden auseinander und zeigen normale
Sprünge mit sichtbarem Ausdauerverbrauch, **keinen bestätigten Zusatzimpuls**.
Sie prüfen somit nicht die Kosten oder Berechtigung des Doppelsprungs.
InfiniteStamina ist in 147 wieder eingeschaltet. Ducken, Aufstehen und normaler
Sprung in 153/154 bestätigen eine einfache Rückkehr zur Bewegung, keine
systematischen Doppelsprung-/Vault-/Climb-Übergänge. Die sichtbare Grenze in 150
ist kein vollständiger Nachweis aller Kollisionskanten oder Spawngeometrien.

Root-Vergleich **18:17:01.766 MESZ**, `private/bear-legacy-protected-diff.json`,
mit neuem Serializer beide `37e00…4338`. Unabhängiger Vergleich
`private/bear-legacy-post-independent.json` um **18:17:43.277** bestätigt
mit bisherigem Serializer beide `5054…cea5a`, **0 geschützte Unterschiede**,
gleiche PMC-/Scav-Stats und gesamtes Scav-Objekt. Vollprofil nur mit geändertem
PMC-Hideout-Aktualisierungszeitstempel. Damit sind **elf** Arena-Enden vor dem
nächsten Raid geschützt abgeschlossen.

Der unabhängige Clientaudit zählt **42 Warnungen**: 33 BoxCollider, vier
WheelCollider, vier alte Redeploy-Kamerasampler und ein anschließend
abgeschlossenes pending Raidende; **0 echte Errors/Fatal und 0 Laufzeit-Exceptions**.
Keine flächendeckende Langzeit-, Optik- oder Geometriefreigabe aus diesem Lauf.

## Aktuelle normale PMC-Regression mit eigener USP-1

Nach dem elften geschützten Arena-Ende erfolgt dieser gewöhnliche Raid mit dem
gleichen getrennten BEAR-Testprofil und eigener Ausrüstung. Die nativ gekaufte
**USP-1** kostete **15805 RUB**, Restbestand **484195 RUB**. Bild 157 zeigt den
normalen Kauf, Bild 158 die Montage an der ausgerüsteten **AK-74M**.

Die tatsächliche Montage ist zusätzlich in der eindeutig benannten gespeicherten
Basis `private/before-normal-pmc-mounted-persisted-profile.json` nachgewiesen:
184 Inventareinträge, USP-1 direkt am `mod_mount_000` der ausgerüsteten
FirstPrimaryWeapon, Waffe ungeklappt, Haltbarkeit 100/100, XP 0 und Level 1.
Unabhängige Bestätigung **18:28:26.316 MESZ** in
`private/normal-pmc-mounted-baseline-independent.md` und `.json`.
PMC-Stats und gesamtes Scav-Objekt gegenüber nach Legacy unverändert.
Der neue historische Projektionshash
`5d56468112374adcc8a9e4331b6b80fb444912463359fbedf5c56b143932717e`
ist die erwartete gewöhnliche Kauf-/Montageänderung und kein Arena-Schutzfehler.

Frühere Kopien wurden vor vollständiger Speicherung aufgenommen:
`after-normal-pmc-optic-purchase-profile.json` noch vor dem persistierten Kauf,
`before-normal-pmc-profile.json` nach Kauf, aber noch vor persistierter Montage.
Diese werden für den normalen Nachraidvergleich **nicht** als maßgebliche
Montagebasis verwendet. Daraus wird kein fehlgeschlagener Kauf oder Montagefehler
abgeleitet; verwendet wird ausschließlich die später verifizierte montierte Basis.

Nativ ausdrücklich **PMC**, **Customs bei Tageslicht** gewählt. Bild 159,
archiviert **18:28:12.437 MESZ**, zeigt die normale Kartenauswahl ohne Arena-Panel.
Anschließend gewöhnliche Vorbereitung und Versicherung mit **0 RUB**, dann Ready;
Vorbereitung und Laden ab etwa **18:29 MESZ**, tatsächlicher normaler Spielbeginn
um **18:30:37 MESZ** mit bestätigtem ordinary-confirm. Die Vorbereitung und der folgende gewöhnliche
Raid werden von den elf geschützten Arena-Enden getrennt bewertet.

| Prüffall / Beleg | Status und tatsächliche Beobachtung |
| --- | --- |
| Gewöhnlicher PMC-Start, Bild 160 | **Bestanden**: eigene AK-74M mit USP-1 vorhanden, 440 HP, 17,3 kg; keine Arena-Kitausgabe oder Team-/Konturanzeige in dieser Ansicht. |
| Montierte Vergrößerungsoptik, Bild 161 | **Bestanden** als ADS-Stichprobe: vergrößerte Laubumgebung und deutlich sichtbares grünes USP-1-Dreieck, Cursor außerhalb des Absehens. `Obscured by foliage` ist sichtbar, kein dadurch abgeleiteter Optikausfall. |
| F9 im normalen Raid, Bild 162 | **Bestanden** für den tatsächlich geprüften Hotkey: keine Arena-Anzeigen oder Arena-Scoreboard; Optikdarstellung weiterhin korrekt. |
| Zwei getrennte Space-Eingaben, Bild 163 nach der zweiten | Normale Sprungbewegung und Ausdauerverhalten beobachtet. **Keine phasengenaue Doppelsprung-Ausschlussfreigabe**: die einzelnen Eingaben prüfen nicht zuverlässig eine zweite Taste während desselben Fluges. |
| Regulärer gewöhnlicher Raidabschluss, Bild 164 | **Bestanden**: Escape → Disconnect → Confirm Leave, anschließend **LEFT 02:13, XP 0**. Danach Main Menu und die normale Nachraid-Heilungsseite mit Skip/Yes verlassen. Kein erzwungenes Prozessende und kein Arena-Respawn als vermeintliches Raidende. |

Beim ersten beabsichtigten ADS-Versuch wurde versehentlich ein eigener Schuss
ausgelöst; Munition von 29/30 + 1 auf 28/30 + 1, kein sichtbarer Zieltreffer.
Die korrigierte native Bedienung öffnete anschließend das oben belegte ADS.
Dieser einzelne Schuss ist eine Bedienungsnebenwirkung; daraus wird kein
Botkampf- oder Trefferbeleg abgeleitet. Details in
`private/normal-pmc-native-observations.md`.

Das getrennte Area-Outline-Entwicklungswerkzeug zeigt **Draft 0/64**. Sein
`AllowInNormalPmc=true` war bereits Ausgangseinstellung; keine Aufnahme oder
Export durch den Testagenten. Die Werkzeuganzeige ist kein Arena-Spielmodus,
keine Arena-Kontur und kein Team-/Respawnmechanismus.

Logs `logs/21-after-normal-pmc/`, Archiv **18:33:29.687 MESZ**;
`private/after-normal-pmc-profile.json` wurde unabhängig gegen die bestätigte
montierte Vorraidbasis verglichen. Ein gewöhnlicher LEFT darf das Testprofil
verändern; keine Arena-Restore-Gleichheit für diesen Raid vorausgesetzt und kein
zwölftes geschütztes Arena-Ende gezählt.

### Gewöhnlicher Serverpfad, Profilfolgen und Fehlerfenster

Unabhängige Auswertung: `private/normal-pmc-independent-audit.md` und
`private/normal-pmc-post-independent.json`, Profilprüfung **18:34:24.256 MESZ**.
Server **16:30:37.156 UTC** bestätigt `/arenamode/ordinary-confirm`,
**16:32:51.077 UTC** den gewöhnlichen `/client/match/local/end` und
**16:32:51.099 UTC** das Ergebnis `bigmap.Pmc … LEFT`.
Ein vorhandener Arena-Abgrenzungs-/Aufräumaufruf vor dem Raid ist kein aktiver
Arena-Spielmodus; es wird ausdrücklich nicht behauptet, es gebe keinerlei
Arena-Routenaufruf im gewöhnlichen Ablauf.

Im abgegrenzten Normalfenster **18:28 MESZ / 16:28 UTC bis Archivende**:
kein Arena-Arming, keine Arena-Sessionaktivierung, Kitausgabe, Respawn oder
Extraimpuls-Erfolg und kein neues Snapshot-Restore/Protected-Arena-End.
Das bestätigt den beobachteten Isolationsfall, keinen modfreien Vergleich
auf vollständige Bitgleichheit.

| Gewöhnlicher Vorher-/Nachhervergleich | Tatsächlicher Befund |
| --- | --- |
| Inventar | **184 → 151 Einträge**: 34 mitgebrachte Einträge entfernt, darunter AK-74M, gekaufte USP-1, Anbauteile und weitere getragene Ausrüstung; ein Dogtag neu, netto 33 weniger. Ein erhaltener Eintrag verliert lediglich das zuvor vorhandene StackObjectsCount=1-Feld. |
| Geld / XP / Level | **484195 RUB unverändert**, XP 0 und Level 1 unverändert. Der Optikkauf von 15805 RUB ist getrennt und bereits in der montierten Vorraidbasis enthalten. |
| Scav | Gesamtes Scav-Objekt unverändert. |
| Gesundheit | Nach gewöhnlichem LEFT **132/440**, alle Körperteile auf 30 Prozent; Energie 100 → 94, Hydration 100 → 95, normale Zeit-/Temperaturdaten aktualisiert. Kein daraus abgeleiteter Fallschaden- oder Doppelsprungtest. |
| Raidstatistik | Eine PMC-Session, ein LEFT, LifeTime 132, **AmmoUsed=1**, keine Kills. Pedometer-/Trigger- und weitere Statistikfelder gespeichert. |
| Skills | Gewöhnliche Änderungen bei StressResistance, Perception, Charisma und MagDrills. Common-Liste 59 → 45; 14 vorherige Einträge mit Progress 0 fehlen. Identitätsbezogener Vergleich statt fehlerhafter Deutung verschobener Arrayindizes; Ursache jeder Schemabereinigung nicht einzeln isoliert. |
| Quests / Zähler | Sechs verfügbare Questeinträge, alle Status 1 / StartTime 0, und 133 TaskConditionCounters mit sämtlich Wert 0 gespeichert. Statuszeiten bereits vor Raidstart; kein angenommener/erledigter Questabschluss oder im Raid erzielter Questfortschritt daraus abgeleitet. |

Diese gewöhnlichen Testprofilfolgen werden nicht als Arena-Schutzfehler
bewertet. Der vollständige Einzelvergleich bleibt im privaten Audit.
Client im genannten Fenster: **50 Warnungen** (33 BoxCollider, vier WheelCollider,
neun Missing-script-Hinweise, vier Animator.GotoState), **0 echte Error/Fatal,
0 Laufzeit-Exceptions**, keine Kamera-NRE. Server: keine Warning-/Error-/Fatal-/
Critical-Einträge.

## Regulärer Abschluss und endgültiger Dateivergleich

Die Testauswahl wurde nativ zurückgestellt; Bild **165** zeigt **Endless / Whole map**.
Danach normales Spiel-Exit mit Bestätigung, Logout **18:35:48 MESZ**, Spielprozess
um **18:35:50** nicht mehr vorhanden. Erst anschließend wurde der eigene Server
regulär mit Ctrl+C beendet. Um **18:36:23 MESZ** sind Spiel und Server unabhängig
als nicht mehr aktiv bestätigt. Der Launcher **PID 2724 bleibt geöffnet**;
es wird kein Abschluss sämtlicher SPT-Prozesse behauptet.

Maßgebliche Abschlussbelege nach dem Stopp: `settings-completed/`, Aufnahme
**18:36:37.472 MESZ**; `logs/23-stopped-final/`, gestoppte BEAR-/USEC-Profilkopien
und `private/stopped-final-independent.md` / `.json`, unabhängig geprüft ab
**18:37:56.882 MESZ**. Frühere Ordner mit „final“ im Namen sind historische
Zwischenstände und ersetzen diese Abschlusskopien nicht.

| Abschlussprüfung | Ergebnis |
| --- | --- |
| Arena-Client | **106/106 semantische Werte gleich**, 0 Unterschiede: unter anderem GameMode endless, ExperimentalArea full-map, AreaPlayersPerTeam 4, RandomKit true, SelectedKit leer, Movement/Doppelsprung aktiv, DoubleJumpHeightMult 1 und InfiniteStamina true. |
| Native Steuerung | Control.ini vollständig **bytegleich** zur Basis vor der alternativen Sprungbelegung; Jump Space/Release und Fire Mouse0/Continuous wiederhergestellt. |
| Serverkonfiguration | Vollständig **bytegleich** zur Anfangskopie. |
| Installierte DLLs | Client, Server und Redeploy.Core weiterhin exakt auf den SHA256-Werten der Teststandtabelle; kein Austausch oder Deployment. |
| Gestoppter BEAR gegen nach Normal-PMC | **0 geschützte Unterschiede**; historischer Projektionshash beidseits `654cdcc5b695eb6e630e0e72c516e7ce7cf123ef21429a231bf052bbfe040477`. Vollprofil nur mit weitergelaufenem PMC-Hideout-Zeitstempel. Der gewöhnliche Normalraidverlust bleibt erhalten. |
| Gestoppter USEC gegen letztes USEC-Arena-Ende | **0 geschützte Unterschiede**, beidseits `9f0eef41f72cc7f88fb22d3de8cb27f91607ec4b13ebd174ee57becd1204e27b`. Inventar, Geld, Stats und gesamter Scav gleich. Außerhalb der Schutzprojektion unterscheiden sich der Hideout-Zeitstempel und zwei entfernte ältere Prapor-traderPurchases-Einträge; deren Einzelursache nicht isoliert. |

Alle abgeschlossenen Einstellungskopien und beide gestoppten Profilkopien sind
bytegleich zu ihren jeweiligen tatsächlichen Laufzeitdateien. Keine vollständige
Bytegleichheit des Profils über den gesamten Testverlauf behauptet.

**Verbliebene Grafikänderung:** genau zehn Auflösungs-/Seitenverhältnisfelder
gegen die ursprüngliche RDP-Sitzung. Aktive DisplaySettings am Ende
**1920 × 1080 / 16:9** statt 1920 × 945; gespeicherte WindowResolution separat
**2560 × 1440** statt 1904 × 858. Alle übrigen Grafikwerte gleich, insbesondere
**DLSS Balanced aktiviert, FSR2/FSR3 aus**. Die Bilddaten 126, 155 und 164 wurden
unabhängig als **1920 × 1080** bestätigt; der gespeicherte Fensterwert macht diese
späten Raids nicht zu 2560 × 1440. Ihre abgeleitete UI-Skalierung ist 1, die
früheren BEAR-Abschnitte bei 2560 × 1440 bleiben gesondert dokumentiert.

Die Artefaktdateien dieser letzten Bildstichprobe enthalten JPEG-Daten trotz
`.png`-Dateinamenendung; Auflösung wurde aus den tatsächlichen Bilddaten gelesen.
Das bereits in der Ausgangsbasis aktivierte Area-Outline-Werkzeug bleibt
unverändert, keine Aufnahme/kein Export. Keine weitere Raidwiederholung oder
Geometrieprüfung wurde nach diesem Abschluss ausgeführt. Die ausdrücklich als
**nicht ausgeführt** ausgewiesenen Fälle bleiben Testlücken und werden durch
Dateivergleich oder Offlineprüfungen nicht zu bestandenen GPU-Tests.
