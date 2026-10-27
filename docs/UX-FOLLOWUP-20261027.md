# Arena-UX: Korrekturen vom 27.10.2026

Fortsetzung von [COMPACT-UX-20261026.md](COMPACT-UX-20261026.md). In normalen Menüs außerhalb der Arena-Auswahl soll ArenaMode keine zusätzliche Darstellung erzeugen.

| Punkt | Änderung |
|---|---|
| Hinweis oben links im normalen Hauptmenü | Sessionleiste ausschließlich bei aktiver privater Arena-Session. Der zusätzliche Hauptmenü-Einstiegshinweis entfällt. |
| Untere Menüleiste drei Pixel höher | Die installierte UIFixes 6.0.4 setzt `BottomPanel.localPosition` auf `(0, -3, 0)`. Durch die bisherige Versionsfilterung fehlte dieser Patch in Arena. UIFixes soll mitgeladen werden; Arena erhält keinen eigenen Pixelversatz. |
| Abweichende geprüfte Modversionen | [Versionsbehandlung](MOD-VERSION-FOLLOWUP-20261027.md): bekannte Identitäten trotz Versionsabweichung versuchen, tatsächliche API- und Schutzprüfungen beibehalten. |
| Zusätzliches Domination-Kontrollpunktpanel | [Gemeinsames Score-Panel](DOMINATION-SCORE-20261027.md). |
| Wiederkehrende Belohnungswarnungen | [Ursachen und gespeicherte Nachweise](REWARD-STARTUP-DIAGNOSIS-20261027.md). |

Die 3-Pixel-Ursache wurde am lokalen Referenzcode und zusätzlich durch Dekompilieren der tatsächlich installierten `Tyfon.UIFixes.dll` geprüft; [Binary-Beleg](../artifacts/ux-followup-20261027/installed-uifixes-taskbar.cs). Die normale Menüleiste hat vor der Änderung bei 1920×1080 `y=1053`, Klickmitte `1067`: [Ausgangsmessung](../artifacts/ux-followup-20261027/before-normal-taskbar.json).

## Prüfung

Offline-Prüfung: 3213 xUnit-Tests bestanden, ein bekannter installierter-APBS-Integrationstest übersprungen. CompactSessionUxChecks: 25 Assertions. ArenaSessionClientChecks: 174 erkannte Negativkontrollen. Versionsharness: 88 Assertions und zehn Negativkontrollen. Domination-Harness: 913 Assertions und 13 Negativkontrollen. Beide Fachänderungen wurden unabhängig gegengelesen. [Gesamtprotokoll](../artifacts/ux-followup-20261027/full-regression.log).

Client und BigBrain-Companion gemeinsam in Release gebaut (null Fehler, vier bestehende Nullable-Kontextwarnungen). Nur diese beiden DLLs installiert. Die automatische Dateiübernahme erreichte GPU-2 binnen 120 Sekunden nicht; nach beendetem Spiel und Server wurden die alten beiden GPU-Dateien unter `BepInEx/backups/arena-ux-followup-20261027/` gesichert und die neuen direkt übertragen. Anschließend stimmten lokale und entfernte SHA-256-Werte überein. Neustart mit 17/17 identischen eigenen Plugins und Bibliotheken.

| DLL | SHA-256 |
|---|---|
| Client | `90B6FAEDB9AE005E7D6F4D1A847A6935E06FF22C8EB3F8D879DCB533C91D486E` |
| BigBrain-Companion | `4F8AFA54ADC572DD4C59F318423AB5ACBAED506C65967A69859DF21786D223D7` |

GPU-2, 1920×1080: normale Menüs nach Neustart mit **23 erfolgreichen Schritten / sechs Erwartungen** geprüft. Kein Sessionhinweis im Hauptmenü, Trader, Inventar oder der normalen PMC-Factory-Auswahl; dort auch kein Arena-Reward-Schalter. Untere Hauptmenü-Taste unverändert bei `y=1053`, Mitte `1067`. [Protokoll und Screenshots](../artifacts/ux-followup-20261027/normal-isolation/report.md).

Private Session nach echtem Neustart: **UIFixes 6.0.4 geladen**, mit identischem lokalem, entferntem und geladenem MVID sowie **341 aktiven Harmony-Methoden**. Der unerlaubte Testmod ArenaWhitelistProbe bleibt ausgeschlossen. **Menüleiste ebenfalls `y=1053`, Mitte `1067`**; Sessionleiste sichtbar. [Geladene Dateien](../artifacts/ux-followup-20261027/private-loaded.json), [Pixelmessung](../artifacts/ux-followup-20261027/private-taskbar.json), [Screenshot](../artifacts/ux-followup-20261027/private-main.png).

Factory, Domination 5v5: drei Punkte innerhalb der gemeinsamen Scorefläche; Neutral, USEC/BEAR und laufender Prozentfortschritt sichtbar. Laufzeitmessung: Score **760×120**, Punktzeile **760×36** bei lokal `y=-84`, direkter Parent `ArenaScorePanel`, Schrift **20**, **eine Zeile**. [Livebild](../artifacts/ux-followup-20261027/domination-live.png), [Fortschritt](../artifacts/ux-followup-20261027/domination-progress.png), [UI-Texte](../artifacts/ux-followup-20261027/domination-text.json), [Laufzeitgeometrie](../artifacts/ux-followup-20261027/domination-geometry.json). Varianten mit einem/zwei Punkten und andere Modi wurden über den verknüpften Produktionscode geprüft; dieser native Durchgang verwendet drei Punkte auf Factory.

Der geschützte Austritt erreichte über ExitStatus, KillList und SessionStatistics wieder MainMenu. Im nativen Log steht beim Abbau einmal `SmokeGrenade.RemoveRigidbody` → `CG_TimerBehaviourCoroutine.MoveNext` mit NullReferenceException; kein Score-/Session-UI-Frame im Stack, kein abgebrochener Austritt. Dieser Befund wird nicht als fehlerfreier Gesamtlog oder bewiesene Fremdursache dargestellt. [Vollständiger Stack](../artifacts/ux-followup-20261027/private-errors.txt).

Die Rückkehr aus **Trader** über `RETURN TO SPT` und die Bestätigung startete den normalen Prozess erfolgreich neu: **17/17** eigene Plugins identisch, Sessionhinweis wieder verschwunden. [Return-Status](../artifacts/ux-followup-20261027/after-return-status.json), [Abschlusslauf](../artifacts/ux-followup-20261027/return-cleanup/report.md).

Alle **228 Einstellungen** und **185 Inventargegenstände** stimmen exakt mit den vollständigen Ausgangslisten überein; die testweise Mode-Auswahl wurde auf den erfassten Wert `tactical` zurückgesetzt. [Datenvergleich](../artifacts/ux-followup-20261027/final-state-comparison.json). Spiel und Server sind wie zu Beginn **gestoppt**, kein weiterer EFT-/SPT-/Launcher-Prozess: [Hostzustand](../artifacts/ux-followup-20261027/after-host-state.log).

Server- und Core-DLL sowie die zuvor vorhandenen Änderungen an `docs/README.md` und `docs/UX-REVIEW-20261026.md` sind gegenüber der Ausgangsmessung unverändert. Keine Reparatur oder Löschung von Reward-/Profildaten vorgenommen; die diagnostizierten Warnungen bleiben bis zu einer gesonderten, belegten Datenbereinigung bestehen.
