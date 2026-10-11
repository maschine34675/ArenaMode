# Customs Area Map Preview r1 — GPU-Test vom 13.09.2026

**FAIL, bei Menüprüfung 1 gestoppt.** Factory East wird als vierter Eintrag angeboten, lässt sich aber in zwei nativen Klickversuchen nicht auswählen. Auswahlmarkierung, Vorschautitel und Rechteck bleiben auf Factory Center. Die anschließende reine Leseprüfung des gebundenen Quellstands findet die East-ID weder in der Auswahlvalidierung noch in der Konfigurations-Allowlist. Kein Patch, Build oder Austausch während des Laufs; **0 Raids**.

## Ergebnisse pro Prüfung

| Prüfung aus r1 | Ergebnis | Beobachtung / Grenze |
|---|---|---|
| Buildbindung und frischer Client/Server | PASS | Alle neun Dateien vor Start und am Ende passend; alle vier ProductVersion-Vorgaben passend; neue Prozesse dokumentiert |
| 1: vier Flächen in TEAM DM / Customs angeboten | PASS | Alle vier Zeilen sichtbar; keine PFT-Follower gewählt |
| 1: Factory East auswählen und zugehörige Vorschau | **FAIL** | Zwei Klicks ohne Auswahlwechsel; auch nach jeweils mehr als zehn Sekunden weiterhin Center-Rechteck |
| 1: native Navigation, Karte und Zeitwahl unverdeckt | PASS für Sichtbarkeit; Interaktivität teilweise UNGETESTET | Im ursprünglichen Center-Bild sind Kartenwahl, Zeitwahl, Map, Next, Back und ausgegrautes Ready unverdeckt. Back und Moduswechsel bedienbar; Map, Zeitwahl, Next und Ready auf dieser Seite nicht ausgelöst |
| 2: AREA/OVERVIEW beider gemessener Konturen, Helligkeit und Landmarken | UNGETESTET | East nicht erreichbar; nach reproduziertem Fehler keine weiteren Prüffälle |
| 3: ganze Karte, graue Nachbarflächen, gemeinsame Center/Team-Kontur, Rückkehr AREA | UNGETESTET | Kein Kameratoggle ausgeführt |
| 4: wiederholte Kameratoggles, 10-s-Persistenz, Auswahl- und Teamgrößenstabilität | UNGETESTET | Wartebilder belegen nur den Auswahlfehler, keine Kamerapersistenz. Sichtbares 2v2 gehört zur weiterhin ausgewählten Center-Fläche |
| 5: zehn Next/Back-Zyklen, Duplikate und zeitliche Stabilität | UNGETESTET | 0 der geforderten zehn Zyklen. Cleanup-Back ist kein Ersatz. Kein Clip verfügbar |
| 6: Factory/Labs/Labyrinth, Customs-Rückkehr und gewöhnliche PMC-/Scav-Menüs | UNGETESTET | Stopp vor diesen Prüffällen |
| 7: tatsächliche Darstellung 1920×1080 | PASS nur für anfängliches Center-Layout | Rahmen, Buttons, Caption und Attribution sichtbar; kein offensichtlicher Anschnitt oder Überlappen. Keine East-/Kontur-Abnahme daraus |
| 7: kleinere Auflösung und separate UI-Skalierung | UNGETESTET | Kein Auflösungswechsel nach Stopp. Separater Skalierungswert nicht erhoben; kein unbelegtes „100 %“ |
| geforderter Video-/Flackernachweis | UNGETESTET | Dokumentierte Computer-use-Schnittstelle liefert Einzelbilder, keine Video-/Recording-API. Kein Clip erzeugt oder als geprüft ausgegeben |
| spätere Factory-East-Raid-/Profilabnahme | UNGETESTET, separater Benutzerauftrag | In dieser Anweisung ausdrücklich nicht zur autonomen Durchführung freigegeben |

## Reproduktion und Originalbelege

Native Darstellung: **1920×1080, 16:9, Borderless, DLSS Balanced**, im [Grafikmenü](../test-evidence/area-map-preview-r1/screenshots/13-original-graphics-0.jpg) belegt. Die aktiven Einstellungen liegen unter SPT_Runtime/user/sptSettings; die zusätzlich gesicherten älteren Roaming-Dateien sind nicht die Quelle der tatsächlichen Spielauflösung. Anzeigeeinstellungen wurden nicht verändert.

1. Bestehendes GPU-Testprofil im Launcher öffnen, START GAME. Im Spiel Escape from Tarkov → ARENA → NEXT → TEAM DM → Customs.
2. [Ausgangszustand](../test-evidence/area-map-preview-r1/screenshots/19-customs-initial-0.jpg): vier Flächenzeilen; Auswahlstripe bei Factory Center; rechts Titel „CUSTOMS — Factory Center“ und blaues Rechteck. Team Kills 50, Time Limit 10 min, sichtbares 2v2 fixed teams. Der initiale Customs-Fallback von full-map auf die gültige Center-Fläche ist erwartetes Verhalten.
3. **12:23:50.592 MESZ**: einmal links auf Factory East (contour), Fensterkoordinate (242, 807). [Bild unmittelbar danach](../test-evidence/area-map-preview-r1/screenshots/20-east-area-0.jpg); [Wartebild 12:24:08.092](../test-evidence/area-map-preview-r1/screenshots/21-east-area-settled-0.jpg). Die helle East-Zeile ist nur Hover; der Auswahlstripe bleibt links bei Center, Titel und Rechteck bleiben ebenfalls Center.
4. **12:24:16.793 MESZ**: zweiter einzelner Linksklick auf dieselbe Zeile, (211, 806). [Zweiter Versuch](../test-evidence/area-map-preview-r1/screenshots/22-east-selection-repeat-0.jpg); [Wartebild 12:24:30.531](../test-evidence/area-map-preview-r1/screenshots/23-east-repeat-settled-0.jpg). Unveränderter Befund. Erster Wartebeleg etwa 17,5 s, zweiter etwa 13,7 s nach dem jeweiligen Input.
5. Stopp gemäß „Stop on a reproducible new menu failure“. Nur Cleanup: [Deathmatch wiederhergestellt](../test-evidence/area-map-preview-r1/screenshots/cleanup-24-original-deathmatch-0.jpg), Back zur Charakterauswahl, Back ins [Hauptmenü](../test-evidence/area-map-preview-r1/screenshots/cleanup-26-main-menu-0.jpg). Der Deathmatch-Klick reagierte; es liegt kein nachgewiesener allgemeiner Eingabe-Softlock vor.

Die Bildzeiten sind Archivierungsintervalle, keine framegenauen Eingabe- oder Renderzeitpunkte. [Eingabefolge](../test-evidence/area-map-preview-r1/audit/input-actions.ndjson) und [Bildindex](../test-evidence/area-map-preview-r1/audit/screenshot-index.json) trennen diese Zeiten. Elf öffentliche Originalbilder sind bytegleich kopierte JPEGs, keine Ausschnitte oder nachgestellten Zustände. Insgesamt wurden 16 native Klicks und 25 Aufnahmen protokolliert; frühe Launcherbilder mit privaten Kennungen und eine nicht verwertbare Startaufnahme bleiben privat. Ein Fokus-Timeout beim Laden wurde nach frischer Fensterermittlung einmal erfolgreich wiederholt, vor Beginn der Menüprüfung.

## Statisch belegter Auswahlblocker

Reine Leseprüfung; die relevanten Dateien stimmen ohne Diff mit dem gebundenen Commit d7cde9d überein:

- ArenaAreaSelectionState.cs:33 bietet CustomsFactoryEastPolygonId an; ArenaAreaCatalog.cs:488 kann die ID auflösen.
- Der gemeinsame Row-Listener in ArenaAreaSelectionView.cs:216/248 reicht row.Id weiter; ArenaMenuSelection.cs:209 ruft Settings.TrySelectArea auf.
- **ArenaMode.Client/Config/Settings.cs:235–238** lässt die East-ID in seiner festen Allowlist aus und liefert false, bevor die Einstellung gespeichert oder die Erfolgsmeldung an Zeile 250 geschrieben wird.
- Auch die **AcceptableValueList in Settings.cs:808–810** enthält East nicht.
- Bei false erfolgt im Callback keine sofortige Aktualisierung. Der periodische Refresh liest den unveränderten gespeicherten Center-Wert und verwendet ihn für Liste und Vorschau.

Damit ist ein deterministischer Auswahlblocker im Quellstand belegt, der zum nativen Ergebnis passt. Die tatsächliche Ausführung des Klickcallbacks wurde nicht instrumentiert; ein zusätzlicher Eingabefehler wird dadurch nicht ausgeschlossen. Eine erfolgreiche East-Auswahl mit nachträglicher Rücksetzung wurde nicht beobachtet. [Quellausschnitte und Hash](../test-evidence/area-map-preview-r1/audit/static-selection-blocker.json) sichern den gelesenen Stand. Es wurde nichts korrigiert, um den gebundenen Lauf fortzusetzen.

## Build, Prozesse und Loggrenzen

Test-ID area-map-preview-r1; Auftrag GPU-AREA-MAP-PREVIEW-TEST-INSTRUCTIONS-r1.md. SourceCommit **d7cde9d115a0f775f8c1b52cc324ee8b0c319e62**, Arena-Client und -Server jeweils **0.1.0+d7cde9d115a0f775f8c1b52cc324ee8b0c319e62**. Checkout zu Testbeginn: feature/area-map-preview, HEAD **9dd79b4185047d7592b407de8e8a9f6e9f0bbf90**. Nachfolgende Dokumentation wurde nicht als Binary-ID ausgegeben.

| Komponente | SHA256 |
|---|---|
| ArenaMode.Client | AB53689653C666F8B842DA30C0F4FF9C7C86B801950A8418B47B2FAA37323A17 |
| ArenaMode.Server | A694911344BE7AC9058211EAC856B6944AA1CB16EB3AB1282ECB46403391C7B7 |
| Redeploy.Core | FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C |
| Redeploy.Client | 7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46 |

Die fünf Karten-/Quell-/Lizenzdateien und vier DLLs sind vollständig in der unveränderten [Buildbindung](../test-evidence/area-map-preview-r1/audit/build-binding.json), [Vorprüfung](../test-evidence/area-map-preview-r1/audit/preflight-hashes.json) und [Endprüfung](../test-evidence/area-map-preview-r1/audit/final-binding-processes.json) erfasst. Alle neun Hashes und vier Versionsvorgaben stimmen. Die ursprünglichen GpuInstalledHashesVerified/NativeGpuTested-Felder des gelieferten Manifests bleiben historische Auslieferungsangaben; dieser Bericht ergänzt den tatsächlichen GPU-Nachweis.

| Prozess | PID | Start UTC |
|---|---:|---|
| SPT.Server | 584 | 10:19:33.3193690 |
| SPT.Launcher | 1032 | 10:19:33.3302232 |
| EscapeFromTarkov | 12984 | 10:20:26.7846053 |

Server und Launcher wurden aus **C:\SPT41\SPT_Runtime** gestartet; das Spiel anschließend nativ über den Launcher. Zu Beginn lief keiner dieser Prozesse. Beim Abschluss sind alle drei ansprechbar. Artefakt-/Ausführungsintervall 10:18:32.902–10:25:31.798 UTC, **6:58,896**; Dateiaudit und Bericht danach. Kein Match wurde gestartet; armed, sessionActive und matchStarted im frischen Log jeweils 0. Daraus folgt keine Profilgleichheits- oder Raidabnahme.

Clientlog: **13.176 Zeilen, 12:20:38.159–12:25:19.892 MESZ**. Separates Customs-Menüintervall ab **12:23:42.766 MESZ**, sechs protokollierte Records. Aktueller Serverprozess: **spt20260913.8.log**, seit 10:19:33.319 UTC, Snapshot bis **10:26:22.695 UTC**. Keine alten respawn-r3/r4-Logs zugerechnet.

| Quelle | Frischer Start + Test: Warnungen / Errors | Customs-Menüintervall: Warnungen / Errors |
|---|---:|---:|
| Redeploy.Core | 0 / 0 | 0 / 0 |
| maschine-Redeploy | 0 / 0 | 0 / 0 |
| maschine-ArenaMode | 0 / 0 | 0 / 0 |
| Unity Log / SAIN, jeweils | 0 / 0 | 0 / 0 |
| FixPluginTypesSerialization | 5 / 0 | 0 / 0 |
| HarmonyX | 4 / 0 | 0 / 0 |
| Client gesamt | **9 / 0** | **0 / 0** |
| Server gesamt | **3 / 0** | **0 / 0** |

Geforderte Logkategorien jeweils **0**: Arena area map preview unavailable; doppelte CanvasGroup; andere Komponentenfehler; fehlende Textur; Shader-/Materialfehler; MissingReference; NullReference; Runtime-Exception; Menüexception/-Error. Keine Exceptionstacks vorhanden. Diese Nullen geben den sichtbaren Auswahlfehler nicht frei und beweisen keine Interaktivität sämtlicher nativer Controls.

Genau ein Auswahl-Erfolgsmarker: **12:23:42.766, customs-factory-center-v1**. **0 East-Erfolgsmarker.** Bekannte Warnungen: fünf fehlende optionale Fika-Referenzen um 12:20:38.160; vier Harmony-Warnungen um 12:20:48.400, 12:20:58.034, 12:21:00.417 und 12:21:05.041. Server: Trader-Standardtimer 10:19:41.780 UTC sowie zwei native StackObjectsCount-Korrekturen beim Profilabruf 10:21:21.159/.160 UTC. Keine vom Tester ausgeführte Profilreparatur. [Gezielte Auszüge](../test-evidence/area-map-preview-r1/logs/final-excerpts.txt) und [vollständige strukturierte Zählung](../test-evidence/area-map-preview-r1/audit/final-audit.json).

## Abschluss und Grenzen

Deathmatch / Endless / Kills Off / Zeit Off / FFA wiederhergestellt. Die aktive Grafik-, Spiel-, Steuerungs-, PostFX- und Audiodatei sowie Redeploy sind bytegleich zu ihren Sicherungen. Keine Auflösungs- oder Audioänderung. Der normale Customs-Fallback hat ExperimentalArea von full-map auf customs-factory-center-v1 gesetzt und bleibt so gespeichert; Deathmatch ignoriert diesen TDM-Wert. Der neue Build ergänzt automatisch die beiden HUD-Offset-Defaults 0/0 und aktualisiert die Killfeed-Beschreibung. Keine weiteren bestehenden Arena-Werte verändert. Das ist ausdrücklich keine Behauptung vollständiger Konfigurationsdateigleichheit. [Einstellungsvergleich](../test-evidence/area-map-preview-r1/audit/settings-end-audit.json).

Private Rohlogs und Sicherungen: artifacts/area-map-preview-r1/2026-09-13T10-18-32-902Z/. Öffentliche Logkennungen sind redigiert. Frühere Berichte, fremde Dateien und Produktionsdateien wurden nicht editiert; nur dieser Bericht und seine Belege werden committed. [SHA256-Manifest](../test-evidence/area-map-preview-r1/SHA256SUMS.txt).
