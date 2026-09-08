# ArenaMode: native GPU-Abnahme am 8. September 2026

**Gesamtabnahme fehlgeschlagen:** Das neue Preset **Customs contour (team starts)** erreicht bei keiner geprüften Größe eine aktive Mannschaft. 2v2 scheitert auch bei der einzigen gezielten Wiederholung. Menüauswahl, der bisherige Kontur-2v2-Raid, die beobachteten Optikabläufe und alle sechs Arena-Profilwiederherstellungen bestehen die unten abgegrenzten Prüfungen. Der normale PMC-Test wird gesondert dokumentiert. Frühere Offlineprüfungen zählen hier nicht als GPU-Abnahme.

## Teststand und Durchführung

Alle Zeiten sind **MESZ (UTC+02:00)**, außer ausdrücklich als **UTC** bezeichneten Serverzeiten. Die Prüfung begann um 10:27; die Spieltests endeten um 11:35. Spiel und Server sind anschließend regulär beendet worden. Es gab keine Mod-Codeänderung, Fehlerbehebung, Builds, Pulls, Resets, DLL-Austausche oder Profil-JSON-Edits.

| Vorbereitung | Status und tatsächlicher Nachweis |
| --- | --- |
| Pflichtdokumente | **bestanden**: AGENTS, HANDOFF, AREA-TEAM-SETUP-REVIEW, OPTIC-PRELOAD-REVIEW in vorgegebener Reihenfolge gelesen; AREA-TEAM-SPAWN-SURVEY ergänzend. Installationspfade auf `C:\SPT41` bezogen. |
| Checkout / Implementierung | **bestanden**: sauberer Ausgangsstand auf `feature/customs-contour`, HEAD `62c7b7015119e83fc4ce820645d12a092c2a17c0`; Implementierung `6843e1e9988c4a3002c82b0f76539d3738ff49c7` einschließlich Optikfix `240735197031009241f2b61e68dee2e545fe8f48` vorhanden. |
| Installierte DLLs | **bestanden**: erwartete SHA256 vor dem ersten Test und erneut nach allen Tests identisch, siehe unten. |
| Native Bedienung | **bestanden**: echte Launcher- und Spieloberflächen über die dokumentierte Computer-Use-Schnittstelle bedient und aufgenommen; sichtbare, entsperrte RDP-Sitzung 2 auf `WIN-Q470OL2A14A`. Keine simulierte Oberfläche. Keine fremde laufende Spielsitzung unterbrochen. |
| Separates Profil / Sicherung | **bestanden**: Standard-USEC-Profil über Launcher und Charaktererstellung angelegt; Launchername `ArenaGPU20260908`, Figurenname `ArenaGPU0908`, Level 1. Eigentliches Spielerprofil nicht ausgewählt. Ausgangsbasis bei gestopptem Spiel und Server um **10:35:43** gesichert. Zweite gestoppte Basis nach nativem Optikkauf um **11:13:55.764**. |
| Neustartbelege | **bestanden**: Client-, Server- und Launcherlogs vor Neustarts archiviert; abschließendes Paket nach regulärem Serverstopp vorhanden. Keine Recovery-Dateien manuell repariert oder gelöscht. |

Client: `C:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`, SHA256
`49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`.

Server: `C:\SPT41\SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll`, SHA256
`D7879AEFD593B8CA2915B8F8FAF7106C9F007A6E6BE2D02425D3892AF253AAF2`.

GPU: **NVIDIA RTX A2000**, Treiber **32.0.15.9706**. Native Spielaufnahmen: **1920 × 945**. UI-Skalierung **0,875**, aus der aktuellen Auflösung und `UICanvasScalerController`/`Utils` hergeleitet, **kein direkt ausgelesener Laufzeitwert**. Die abgelegte aktuelle `Graphics.ini` und die tatsächlichen Aufnahmen stützen die Auflösung; ältere abweichende Einstellungen wurden nicht als Laufzeitnachweis verwendet.

Erster Testprozess: Spiel PID 3832 ab 10:37:34, Server PID 10636 ab 10:36. Frischer Optikprozess: Spiel PID 9424 ab **11:19:21**, Server PID 5464 ab **11:14:11**. Native Anzeige `SPT 4.1.4 - 072d53`, PvE/local; `072d53` wird nicht als EFT-Versionsnummer ausgegeben. Fremde Mods blieben installiert.

## Belege lesen

Alle Rohbelege liegen ausschließlich im von Git ausgeschlossenen Ordner:

`C:\SPT41\Development\ArenaMode\artifacts\gpu-acceptance\2026-09-08_102800\`

Die folgenden Pfade sind relativ zu dieser Belegwurzel. **Bildnummern** bezeichnen die nummerierten Dateien in `screenshots/`; `screenshots/index.ndjson` enthält vollständige Namen, Archivierungszeiten und Bildgrößen. Archivierungszeit ist nicht immer der genaue Ereigniszeitpunkt. Genaue Ablaufzeiten unten stammen aus den zugeordneten Logs. Profile, komplette Logs, Konfigurationen und Binärdateien gehören nicht zum Dokumentationscommit.

`test-stand.json`, `settings-initial/`, `settings-final/` und die gestoppten Sicherungsmanifeste dokumentieren den Teststand. Logpakete `00`–`04` sichern Vorbereitung/Neustarts, `05`–`09` die fünf neuen Teamstart-Versuche, `10`/`11` den frischen Optikstart, `12` Legacy/Optik, `13` normalen PMC, `14`–`16` den Abschluss. Clientzeiten sind lokal, Serverdateien `server-utc.log` verwenden UTC.

## Native Menüauswahl

Einstellung: **Arena → Team Deathmatch → Customs → Customs contour (team starts)**. Prüfung 10:39–10:44 bei oben genannter Auflösung.

| Prüffall | Status | Tatsächliche Beobachtung / Beleg |
| --- | --- | --- |
| 2v2, 3v3, 4v4, 5v5 auswählen und lesen | **bestanden** | Alle vier Größen nativ angeklickt, Auswahl und erklärende Größenzeile stimmen überein; Bilder 08–11. |
| Keine Überdeckung der Vanilla-Schaltflächen | **bestanden** | MAP, BACK, NEXT, Zeitwahl und Karteninformationen sichtbar; keine Überschneidung mit Größenfeldern in dieser Auflösung. Bilder 07–11. |
| Gebietswechsel / alte begrenzte Presets | **bestanden** | Factory Center und Factory Center (contour) zeigen festes 2v2 ohne Größenknöpfe, auch nach vorherigem 5v5. Bilder 12/13, 10:40. |
| Kartenwechsel | **bestanden** | Factory entfernt Customs-Felder; Rückkehr zu Customs normalisiert auf Factory Center/festes 2v2. Bilder 14/15, 10:41. |
| Moduswechsel | **bestanden** | Solo-Tactical entfernt Customs-Auswahl und Teamgrößen. Bild 16, 10:41. Das ist kein authentifizierter PFT-Gruppentest. |
| Zurück / Vorwärts | **bestanden** | Nach Rückkehr aus Bestätigung bleibt die zulässige Teamstart-Auswahl 2v2 erhalten. Bild 20, 10:43:46 archiviert. |
| Normaler PMC und erneuter Arena-Einstieg | **bestanden** | PMC zeigt normale Kartenliste ohne Arena-Felder; Arena übernimmt die zuvor normale PMC-Customs-Auswahl nicht unzulässig. Bilder 17/18, 10:42. |
| Protected PFT Tactical | **nicht ausgeführt** | Kein entsprechendes Setup mit vier gespeicherten, authentifizierten PFT-0.10.0-Begleitern und passender nativer Gruppe verfügbar. Solo-Tactical zählt dafür nicht. |

Dies sind konkrete native Navigationsfolgen, keine Freigabe sämtlicher Auflösungen oder denkbarer Wechselkombinationen.

## Neues Teamstart-Preset: fünf fehlgeschlagene Starts

Einstellungen für alle folgenden Versuche: `customs-factory-center-teams-v1`, Team Deathmatch, Customs, Tageszeitwahl, lokaler USEC, RandomKit=true. Reihenfolge: zuerst 2v2, dessen einzige gezielte Wiederholung, anschließend 5v5, 3v3 und 4v4. Die übrigen Größen sind eigenständige geforderte Prüfungen; der Fehler wurde nicht endlos wiederholt.

| Versuch / Status | Größe eingefroren | Match-Service startet | Navigationsfehler | Genaue Fehlerergänzung | Belegpaket / Bilder |
| --- | --- | --- | --- | --- | --- |
| 2v2: **fehlgeschlagen** | 10:44:43.109 | 10:45:29.314 | **10:45:41.417** | `BEAR starts: PMC 0, bots 3/2` | `logs/05-first-2v2-abort/`, 21–23 |
| 2v2, einzige Wiederholung: **fehlgeschlagen** | 10:52:54.668 | 10:53:28.170 | **10:53:40.186** | `BEAR starts: PMC 0, bots 4/2` | `logs/06-repeat-2v2-end/`, 24–26 |
| 5v5: **fehlgeschlagen** | 10:56:30.802 | 10:57:04.482 | **10:57:16.499** | `BEAR starts: PMC 0, bots 3/5` | `logs/07-5v5-end/`, 27/28 |
| 3v3: **fehlgeschlagen** | 11:00:13.518 | 11:00:45.993 | **11:00:58.006** | `BEAR starts: PMC 0, bots 3/3` | `logs/08-3v3-end/`, 29/30 |
| 4v4: **fehlgeschlagen** | 11:05:00.716 | 11:05:32.365 | **11:05:44.759** | `BEAR starts: PMC 0, bots 4/4` | `logs/09-4v4-end/`, 31–33 |

Gemeinsamer Fehler: **`navigation validation timed out`**, gefolgt von `Arena start is invalid`. Alle fünf Versuche enden automatisch über den Spielablauf mit **MIA / 00:00:13**. Die Abschaltmeldungen zeigen auf beiden Seiten **active=0**. Weder die angeforderten N−1/N-Slots noch die obigen Navigationskandidaten sind aktive Bots. Auch „Match-Service startet“ ist keine vollständige Mannschaftsaufstellung.

Nativ waren beim ersten 2v2, der Wiederholung und 4v4 die Szene beziehungsweise Vorbereitung sichtbar; Bild 32 zeigt weiterhin `Preparing navigation`. Für 5v5 liegt kein Bild einer aktiven Mannschaft in der Welt vor. Die native Auswahl und das Ende dokumentieren den tatsächlich gestarteten Versuch.

Einzelne USEC-Startpunkte wurden protokolliert, z. B. (317.34, 1.13, −78.76) sowie beim wiederholten 2v2 (314.87, 1.16, −93.12). Das ersetzt keine vollständige räumliche Aufstellung beider Fraktionen. Der Navigationsschutz nennt fehlende geeignete BEAR-PMC-Starts; die geometrische Ursache oder ein Beitrag anderer Mods ist damit noch nicht bewiesen.

| Geforderte Folgeprüfung im **neuen** Preset | Status für 2v2 / 5v5 bzw. genannte Größen | Grund und Reichweite |
| --- | --- | --- |
| Vollständig aktive NvN-Mannschaft | **fehlgeschlagen** für 2v2, 3v3, 4v4, 5v5 | Start erreicht vor dem Abbruch keine aktiven Bots, siehe Versuchstabelle. |
| USEC westlich / BEAR östlich bei vollständiger Erstaufstellung | **nicht ausgeführt** | Keine vollständigen Teams; einzelne Spielerpunkte reichen nicht. |
| Spieler/Bots starten innerhalb; sichtbare Grenze hält beide | **nicht ausgeführt** | Grenze teilweise sichtbar, aber kein belastbarer Bewegungs-/Kollisionsversuch mit aktiven Teams. |
| Botbewegung, Kampf, dauerhaftes Stehen/Kreisen/Hindernisse | **nicht ausgeführt** | Kein aktives Match erreicht. Stillstand während Vorbereitung ist kein getestetes Kampfverhalten. |
| Mehrere Spielertode/Aufgeben und Respawns in 2v2 und 5v5 | **nicht ausgeführt** | Früher Navigationsabbruch. Legacy-Respawns unten ersetzen diese Prüfungen nicht. |
| Bot-Nachbesetzungen in 2v2 und 5v5 | **nicht ausgeführt** | Keine Ausgangsteams aktiviert. |
| Wiederholte Aktivierung, überlappende Platzierung, Außenspawns | **nicht ausgeführt** | Keine entsprechenden Ersatzvorgänge erreicht. |
| Reguläres manuelles Ende eines spielbaren neuen NvN-Matches | **nicht ausgeführt** | Alle fünf Enden waren automatische Schutzabbrüche; kein Prozessabbruch. Deren Profilfolge wurde jeweils geprüft. |
| Start mit anderer Spielerfraktion | **nicht ausgeführt** | Kein entsprechendes separates BEAR-Testprofil verfügbar; keine vorhandene Fraktion geändert. |

Spätere Seitenwechsel, fehlende Sichtdeckung und ein nicht garantierter fester Sicherheitsradius wurden nicht als Fehler gewertet. Dazu kam es im neuen Preset gar nicht zu einer spielbaren Prüfung.

## Bisheriger Konturbereich und frischer Optiktest

Einstellung: **Factory Center (contour)** / `customs-factory-center-polygon-v1`, festes Team-DM 2v2, USEC, Tageslicht. Erster Raid des frisch gestarteten Optik-Spielprozesses. Für die Vorbereitung wurden nativ PP-19-01 (21514 RUB) und PSO-1 4×24 (19385 RUB) gekauft, montiert und auf ON BACK ausgerüstet; M4 in den Stash verschoben. PP-19-Magazin leer. Die **40899 RUB** sind ausdrücklich Vorbereitungskosten vor der zweiten gestoppten Basis, keine Arena-Kosten. Bilder 34–36 und private Basisanalyse belegen die Änderungen.

| Prüffall | Status | Beobachtung / genaue Zeit / Beleg |
| --- | --- | --- |
| Alter Kontur-2v2-Raid startet spielbar | **bestanden** | Größe eingefroren 11:22:35.283; Match-Service 11:23:17.226; Bereich READY 11:23:18.108; vollständiges `Team Deathmatch ready: 2v2` **11:23:52.791**. Bild 40: Spieler + Andrudis gegen trap + 野猪, alle LIVE. READY prüft zusätzlich `EBotState.Active`. |
| Tatsächlicher Kampf und freundliche Nachbesetzung | **bestanden** für beobachteten Ablauf | BEAR-Kills und natürlicher Spielertod; DrakiaXYZ im Killfeed (42), später Doge LIVE anstelle Andrudis im 2v2-Roster (46). Ankunftslogs allein wurden nicht als Aktivierung gewertet. |
| Montierte Einstiegoptik während Kitvorladen | **bestanden** für beobachtete ADS-Funktion | PSO-1-ADS in dunkler Halle, Bild 38 um **11:23:29.796** archiviert. Sichtbild vorhanden, dunkles Absehen weniger deutlich als spätere Trihawk-Bilder. Hintergrundstart ungefähr 11:23:25 aus Acht-Sekunden-Startbedingung hergeleitet; kein expliziter Startlog. Hintergrundabschlüsse belegt **11:23:47.560–11:25:00.359**. |
| Optik nach Kitwechsel und natürlichem Respawn | **bestanden** | Erstes Kit Vanguard/AK-74N, danach nativ F12 → RandomKit=false, SelectedKit=conscript (41). Tod #1 **11:24:50.466**, Respawn **11:24:54.537** (4,07 s). AKS-74 mit Trihawk 3×30: vergrößertes Bild und rotes Absehen sichtbar (42/43). |
| Optik nach erstem Aufgeben-Respawn | **bestanden** | BackSpace **11:25:38.538**, Tod #2, Respawn **11:25:39.999** (1,45 s); erneutes Trihawk-ADS mit Absehen, Bild 45. Match läuft weiter. |
| Optik nach zweitem Aufgeben-Respawn | **bestanden** | F9 → Give up **11:26:11.052**, Tod #3, Respawn **11:26:12.522** (1,45 s); Absehen/Visierbild vorhanden (47), reale Sichtbehinderung durch Laub erkennbar. |
| Abgesicherter Vorladefall / Kamera-NRE | **bestanden** für diesen Lauf | Schutzdiagnose **11:24:41.045** tatsächlich einmal ausgelöst, Wortlaut unten. Keine `NullReferenceException`-/`ScopePrefabCache`-Fehler in Archiv 12. Separate Unity-Renderfehler bleiben offen, siehe Fehlerliste. |
| Manuelles echtes Matchende | **bestanden** | Escape → Disconnect → Confirm Leave (48); Ende **11:27:02.031/.041**, 0 Kills/3 Tode. Bild 49: Left the Action, **00:03:45**, EXP 0. Give up wurde zuvor als einzelnes Lebensende, nicht Matchende behandelt. |
| Profil nach spielbarem Legacy-/Optikmatch | **bestanden** | Restore und Persistenz, geschützte Diffs 0; siehe nächste Tabelle. |
| Längeres Botbewegungsverhalten, alle Grenzstellen, gegnerische Ersatzbots, systematische Spawnüberlappung | **nicht ausgeführt** | Kurzer Regressionslauf mit drei Spielerrespawns und freundlichen Nachbesetzungen; keine pauschale Navigations-/Geometrie- oder Langzeitfreigabe. |

Tatsächlich protokollierte Diagnose:

`Arena kit preload: withheld unmounted optic role-model callbacks from the active camera.`

Die aktuelle optische Bestückung von `conscript` wurde einmal lesend aus dem bewaffneten Kitkatalog identifiziert und danach **nativ** ausgewählt/geprüft. Der zufällig pro Arm ergänzte Katalog garantiert dieselbe Optik nicht in späteren Raids. Kein Katalog-Polling.

**Belegkorrektur:** `39-legacy-pso1-ads-during-preload-0.png` trägt einen irreführenden Dateinamen: Das tatsächliche Bild zeigt bereits **Vanguard/AK-74N mit Holovisier** nach dem ersten Kitwechsel. Es ist kein zweites PSO-ADS-Bild. Originaldatei und Index bleiben unverändert.

Es wurde an den beobachteten Zeitpunkten kein verschwundenes Visierbild oder Absehen gesehen. Einzelbilder und getrennte ADS-Aktionen belegen **keine ununterbrochene Flackerfreiheit** und keine dauernde ADS-Nutzung exakt im Moment der Schutzmeldung. Der Optiktest im neuen Teamstart-Preset bleibt wegen dessen Startfehlern nicht ausgeführt.

## Wiederherstellung nach jedem Arena-Ende

Alle sechs Vergleiche wurden **vor dem nächsten Raid** aus den tatsächlich gespeicherten Testprofilen durchgeführt. Verglichen wurden PMC- und Scav-Inventar samt Identitäten, Eltern/Slots, Anbauteilen und Mengen/Geld sowie Erfahrung/Level, Quests, TaskConditionCounters, RepeatableQuests und Skills. Es wird keine Bytegleichheit beliebiger Zeitstempel oder des gesamten Profildokuments behauptet.

| Arena-Ende | Snapshot restauriert (**UTC**) | Geschütztes Ende persistiert (**UTC**) | Status / geschützte Unterschiede |
| --- | --- | --- | --- |
| Erstes neues 2v2 | 08:45:43.721 | 08:45:44.401 | **bestanden / 0** |
| Einzige 2v2-Wiederholung | 08:53:41.479 | 08:53:41.577 | **bestanden / 0** |
| Neues 5v5 | 08:57:17.891 | 08:57:18.786 | **bestanden / 0** |
| Neues 3v3 | 09:00:59.365 | 09:00:59.619 | **bestanden / 0** |
| Neues 4v4 | 09:05:46.043 | 09:05:46.108 | **bestanden / 0** |
| Legacy-Kontur/Optik | 09:27:02.173 | 09:27:02.304 | **bestanden / 0**, auch nach Kitwechsel und drei Toden |

Die zunächst gemeldeten Clientantworten `restored:false, deferred:true, state:InRaid` waren Zwischenstände vor den bestätigten Server-Restore-/Persistenzpaaren, keine fehlgeschlagenen Wiederherstellungen. Generische MIA-/Left-Anzeigen warnten vor Ausrüstungsverlust; die MIA-Anzeige nannte auch EXP 200. Tatsächlich blieben gespeicherte Arena-XP **0** und sämtliche geprüften geschützten Felder gleich.

Die ursprüngliche gestoppte Basis enthielt 205 PMC-Inventareinträge und 500000 RUB. Vor dem ersten Raid ergänzte SPT **46 fehlende StackObjectsCount-Felder auf 1**; jeder Eintrag ist durch Startup-Warnungen **08:38:27.940–08:38:28.046 UTC** belegt. Reale Mengen/Geld blieben gleich. Dies geschah vor dem ersten Arena-Arm. Weitere normale Vorraidabweichungen betrafen Zeitstempel, Modhistorie und ein unbeanspruchtes Startgeschenk in der Post. Analyse: `private/baseline-difference-analysis.md`.

Die zweite gestoppte Optikbasis enthält 215 PMC-Einträge, 459101 RUB und XP 0. Der Kauf fügte genau zehn Inventarknoten hinzu und entfernte keinen. Vor dem Legacy-Arm ergänzte SPT nur die acht erwarteten Mengenfelder der neuen Waffenteile auf 1. Analyse: `private/optic-baseline-difference-analysis.md` und `before-legacy-2v2-profile.json`.

Kanonische SHA256 der verglichenen geschützten Felder:

- Neue Teamstarts, vor/nach jedem Versuch: `00fccc338b830f16e2cd1f92543ddae8e0476645712bd4900ac19d93b76ac8fc`.
- Legacy/Optik, vor/nach: `3944ac3d3ca6029b8f2f0010a24a51478cbf56c2d50215f11ca10a9631dc4b38`.

Private Vor-/Nachprofile und leere Diffdateien sind zu jedem Versuch vorhanden. Ein Restorefehler wurde **nicht künstlich erzeugt**; die vorgeschriebene Stopbedingung trat nicht ein.

## Normaler PMC-Raid und Endzustand

Einstellung: Testprofil USEC, normale PMC-Auswahl, Customs bei Tageslicht, PP-19/PSO-1 aus wiederhergestellter Optikbasis; keine Versicherung hinzugefügt. READY **11:29:44.509**; normaler Raid bestätigt **11:31:47.035–11:31:47.225**. Native Belege 50–57, `logs/13-normal-pmc-end/`.

| Prüffall | Status | Tatsächliche Beobachtung / Grenze |
| --- | --- | --- |
| Normaler PMC-Start | **bestanden** | Normale Karten-, Raid- und Versicherungsmenüs. Tatsächlicher Spawn an Güterwaggons; unveränderte eigene PP-19-Ausrüstung, kein Arena-Kit. Bild 52. |
| Keine Arena-Grenzen / Mannschaftsregeln | **bestanden** im beobachteten Start | Kein Cyan-Grenzgitter, kein Team-DM-HUD; F9 öffnet keine Arena-Mannschaftsanzeige (54). Vorhandenes AREA OUTLINE DRAFT ist das schon vorher eingeschaltete Entwicklungswerkzeug mit 0 Punkten und ausdrücklichem Normal-PMC-Hinweis, keine Arena-Grenze. |
| Keine Arena-Aufgeben-/Respawn-Funktion | **bestanden** für Hotkey-Abgrenzung | BackSpace bleibt ohne Tod oder Respawn, Gesundheit weiterhin 440/440 (55). |
| Normaler Tod mit anschließendem Ausschluss jedes Respawns | **nicht ausgeführt** | In diesem kurzen normalen Test kein Kampftod. Stattdessen reguläres Verlassen; daher keine pauschale Todespfad-Freigabe. |
| Montierte Optik im normalen Raid | **bestanden** | PSO-1-ADS mit Vergrößerung und Absehen (53–55); ADS verlassen und erneut erfolgreich eingenommen (56). Keine Aussage über sämtliche Optikmodelle oder Langzeitflackern. |
| Reguläres Ende | **bestanden** | Escape → Disconnect → Confirm Leave. Clientende **11:33:28.572**, Server verarbeitet LEFT **09:33:29.083 UTC**. Native Endanzeige Left the Action / **00:01:42** / EXP 0 (57); optionale Heilung nach dem Raid übersprungen, normale Profilverarbeitung ausgeführt. Kein Prozessabbruch. |
| Keine neue Kameraausnahme | **bestanden** im geprüften Normalfenster | Ab 11:29:44 keine neuen Error-/Fatal-/Kamera-NRE-Meldungen; 55 separate Unity-Warnungen (33 BoxCollider, 4 WheelCollider, 15 fehlende Skripte, 3 Animator). Kein Arena-Arm, Matchstart oder Respawn. Der vorbereitende `/raid-end`-Aufruf 11:30:22 war Cleanup, keine Aktivierung. |

Beim normalen PMC-Raid gelten ausdrücklich die normalen Inventar-/Gesundheitsfolgen; dieser Endzustand ist keine Arena-Vergleichsbasis und kein Arena-Restorefehler. Die gespeicherten PMC-Inventareinträge ändern sich **215 → 183**, die Gegenstandsmenge ohne Geld **1429 → 1251**: 33 Ausrüstungsknoten mit Menge 179 werden entfernt, darunter PP-19/PSO und 150 M855; ein eigener USEC-Dogtag kommt hinzu. Die M4 im Stash bleibt erhalten. **459101 RUB, Level 1 und XP 0** bleiben gleich, gesamter Scav ebenfalls. Gesundheit **440 → 132**, Energie/Hydration **100 → 97**, normale Skills-/Statistikübernahme. Es wurde keine Ausrüstung manuell zurückgesetzt.

Questdaten sind nach normaler Verarbeitung nicht bytegleich: sechs Einträge **AvailableForStart**, keine angenommenen/erfüllten Quests, 133 neue Zähler sämtlich 0. Die sechs Quest-Statuszeitstempel **11:27:48.272** liegen bereits vor PMC-READY; die Zähler besitzen keine Zeitstempelfelder. Diese normalen Initialisierungs-/Postraidwerte werden nicht als Arena-Gewinn gewertet. Vor-/Nachprofile liegen als `private/before-normal-pmc-profile.json` und `after-normal-pmc-profile.json` vor.

Die native feste Optik-Kitauswahl wurde am Ende zurückgesetzt: **RandomKit=true, SelectedKit leer** (58). Die durch die Menütests gespeicherten Gebietswerte bleiben **Factory Center (contour)** und **AreaPlayersPerTeam=4** für das separate neue Preset; Legacy bleibt unabhängig fest 2v2. Sonstige Unterschiede der Clientkonfigurationskopie sind automatisch geänderte Dezimalzeichen in Kommentaren. Serverkonfiguration semantisch unverändert.

Spiel über EXIT beendet; Server nach beendetem Spiel regulär mit Stop-Signal geschlossen. Gestoppter Endstand **11:36:56.197** in `private/final-stopped-manifest.json`; endgültige Logs in `logs/16-final-stopped/`. Keine Recovery-Reparatur, kein künstliches Zurücksetzen des normalen PMC-Verlusts.

## Fehler getrennt von bestandenen Teilprüfungen

| Befund | Einordnung / Beleg |
| --- | --- |
| Neuer Teamstart-Navigationsabbruch | **fehlgeschlagen, reproduziert**. Fünf oben benannte Starts; je zwei Arena-Error-Meldungen. Keine vollständige Mannschaft. Fehlerbedingung Arena-seitig protokolliert; Ursache noch nicht isoliert. |
| Unity-Renderflächen nach Kit-/Kameratausch | **fehlgeschlagen als fehlerfreier Renderablauf**: vier Error-Meldungen in Legacy, `GrassMotionVectorsRT` 1114×548 gegen DepthBuffer 1920×945, **11:23:46.916, 11:24:54.739, 11:25:40.030, 11:26:12.551**. In dieser Testreihe neu beobachtet, ohne bewiesenen Regressionsbeginn. Wiederholt innerhalb eines Raids, keine zusätzlichen Neustartversuche. Absehen später sichtbar, keine Kamera-NRE; Urheber und Zusammenhang zum Optikfix nicht bewiesen. Archiv 12. |
| Bereits bekannter inaktiver MenuScreen beim Exit | **fehlgeschlagen als fehlerfreier Menüablauf**: Unity-Coroutine-Error erneut **11:26:42.390**. Bereits in `CUSTOMS-CONTOUR-REVIEW.md` für **07:36:03.993** dokumentiert und vom Optikfix ausdrücklich ausgenommen. Confirm Leave, Matchende und Persistenz funktionieren anschließend. Keine belegte Verursachung durch ArenaMode. Archiv 12. |
| F9-Rollenanzeige des Spielers | **fehlgeschlagen als korrekte Rollenanzeige**: lokale Figur steht unter USEC, aber ROLE=Scav (40/46). In dieser Testreihe beobachtete Anzeigeauffälligkeit, Alter unbekannt; keine Behauptung geänderter gespeicherter Profilfraktion. |
| Frühere Optik-Kamera-NRE | Die im Konturreview für **07:27:12.675** beschriebene NRE wurde in diesen frischen Optik-/PMC-Läufen nicht erneut beobachtet. Das beweist die dokumentierten Stichproben, keine vollständige Ausnahmesicherheit. |
| Allgemeine Verlust-/XP-Endtexte | Sichtbare Abweichung zwischen generischem Endtext und Arena-Persistenz separat dokumentiert. Gespeicherte Werte maßgeblich; kein Arena-Inventarverlust nachgewiesen. |
| Szene / Fremdmods | Bereits beim Laden auftretende negative BoxCollider-Skalierung, WheelCollider ohne Rigidbody sowie Harmony-/optionale Fika-Warnungen u. a. von DynamicMaps, UIFixes und WTT getrennt erfasst. Kein belegter Zusammenhang zum neuen Navigationsfehler. Archiv 09 und Belegentwurf. |
| Historischer Serverfehler | Fehlender Modgegenstand um **08:19:33.017 UTC**, vor Testbeginn und Testprofilanlage. Nicht dem Testprofil zugeordnet. Im geprüften Abschnitt des Archivs 09 ab 08:28 UTC keine Error-/Fatal-Meldungen. |

Die Abnahme des neuen Teamstart-Presets bleibt offen für eine spätere Ursachenprüfung und erneute GPU-Prüfung nach einer ausdrücklich beauftragten Änderung. Dieser Auftrag hat nur getestet und dokumentiert. Insbesondere bleiben vollständige neue NvN-Aufstellung, Sektoren, Grenzwirkung, Botnavigation, mehrere neue 2v2-/5v5-Leben, andere Spielerfraktion und echtes Protected PFT Tactical ungeprüft beziehungsweise am dokumentierten Startfehler gescheitert.
