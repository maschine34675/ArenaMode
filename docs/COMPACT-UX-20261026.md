# Kompakte Arena-UX — 26.10.2026

Umsetzung des [Menü-Reviews](UX-REVIEW-20261026.md). Die Oberfläche verwendet kurze Statuszeilen, Gruppen und Schalter; zusätzliche Fließtextblöcke entfallen. Gameplay-Regeln, Auslieferungsstandards und gespeicherte Einstellungen bleiben unverändert.

| Reviewproblem | Implementierte Lösung |
|---|---|
| Arena ist im Hauptmenü unsichtbar | Schmaler Hinweis mit Einstiegspfad; kurze Einführung auf der Charakterauswahl. |
| Private Session und Rückkehr sind außerhalb der Charakterauswahl unklar | Menüleiste mit Sessionstatus, gefilterten Mods, Ausrüstungsschutz und Rückkehrknopf. Bei Raid, Dialogen und My Squad ausgeblendet. |
| Händler-/Inventar-Abstecher verlieren den Arena-Kontext | Derselbe Raid-Setup wird nach erneuter Zulassungsprüfung fortgesetzt. Fremde Setups werden verworfen; Server-Freigabe und eingefrorener Trupp werden beim Verlassen aufgehoben. |
| Doppelter Neustarttext; Aktion wirkt deaktiviert | Gemeinsame Neustartinformation, heller Aktionsknopf und verbleibende Sekunden beim Bestätigungsklick. Lange Sperrdetails erscheinen bei vorhandener Tooltip-Unterstützung auf Hover. |
| Moduswechsel leert die Karte kommentarlos | Kurzer Auswahlhinweis; Equator nennt den Platzmangel. Factory-Zeitwahl zeigt `DAY` / `NIGHT`. |
| Matchende ist nicht erkennbar | Zwei dynamische Regelzeilen im vorhandenen Beschreibungsfeld, mit höchstens 44 Zeichen je Zeile. Inhalte folgen Modus, Limits und Solo-/PitFireTeam-Auswahl. |
| Gun Game / Deathmatch wirken wie endliche Matches | Gun Game nennt Endlosleiter und zugewiesene Waffen. Deathmatch unterscheidet Endloskampf ohne Gewinner, endlose Rundenserie und begrenzte Rundenzahl; All-vs-player erklärt das Gegnerverhalten. Das Objective folgt den aktiven Limits. |
| Team-DM-Limits und Friendly Fire sind unklar | Erstes aktives Limit bzw. Endlosmatch sowie aktueller Friendly-Fire-Status stehen direkt am Modus. |
| Tactical erklärt Kauf, Tod und Begleiter nicht | Solo: Arena-Geld, Kaufen/Behalten und Rundenverlust bei Tod/Zeitablauf. PitFireTeam: benötigte Siege, Zuschauen nach Tod und Rückkehr in der Folgerunde; Begleiterquelle `My Squad`. |
| Zombies: „None“, Zahlen ohne Einheit, unklare Auffüllung | Solo: ein Leben; PitFireTeam: Team-Wipe und Rückkehr Gefallener nach Wellenabschluss. Ausrüstung wird aufgefüllt. Regler heißen `ALIVE AT ONCE`, `ENEMIES IN WAVE 1`, `EXTRA ENEMIES / WAVE`. |
| Ausrüstungsschutz und Prototypgrenzen fehlen | Eine kurze Schutzzeile am Equipment-Feld; Domination ausdrücklich ohne Profilbelohnungen. Domination zeigt die tatsächliche Zahl der Kontrollpunkte der Karte. WIP bleibt sichtbar; Capture the Flag trägt `PLANNED` direkt in der Zeile. |
| Viele ungegliederte Mutatoren | Vier Gruppen: Movement, Equipment, Combat, Feedback. Movement zeigt `ENHANCED` / `STANDARD`; kurze Hilfen erklären die Optionen und nennen PitFireTeam ausgeschrieben. `HORDE PRESSURE` erklärt das Heranrücken entfernter Zombies. |
| Belohnungsstatus ist als Schalter nicht erkennbar | Eigene `PROFILE REWARDS`-Überschrift mit sichtbarem ON/OFF-Schalter; N/A und laufende Anfrage haben getrennte Zustände. |
| Flächenauswahl und Customs-Namen irritieren | `PLAY AREA · WHOLE MAP` für die feste Gesamtkarte; vor Kartenwahl nur `Select a map.`. Customs-Flächen heißen `Industrial Center`, `Industrial Side`, `Customs Yard`; interne IDs bleiben erhalten. |

Die Mutatorgruppen behalten lesbare Beschreibungszeilen und denselben unteren Rand oberhalb der nativen Navigation. Dafür wird die rechte Kartenvorschau um 28 Pixel verkürzt. Die Modusregeln nutzen weiterhin das bestehende Feld; der Ausrüstungshinweis bleibt eine einzelne Zusatzzeile.

## Prüfstand

Offline-Prüfungen belegen Regeln und Menüverhalten im Testmodell. Den getrennten Nachweis im Spiel beschreibt der [GPU-Bericht](GPU-COMPACT-UX-20261026.md).

| Prüfung | Ergebnis / Beleg |
|---|---|
| Unit-Suite | **3213 bestanden**, ein bekannter, umgebungsabhängiger APBS-Integrationstest übersprungen; keine Fehler. [TRX](../artifacts/run-arena-verification/run-arena-verification.trx) |
| Kompakte Session / Modi / Bedienelemente | **25 / 312 / 13444** Prüfungen; zusätzlich drei Modus-Negativkontrollen. [Session](../artifacts/run-arena-verification/CompactSessionUxChecks.log), [Modi](../artifacts/run-arena-verification/CompactModeUxChecks.log), [Bedienelemente](../artifacts/run-arena-verification/CompactMenuUxChecks.log) |
| TDM-Menü | **2666** Prüfungen und **36** Negativkontrollen; unveränderte Produktion danach erneut bestanden. [Vollständiges Log](../artifacts/compact-ux-20261026/tdm-match-menu-full.log) |
| Session-Regression | **174** Negativkontrollen: 107 im Erstlauf, 67 in der Fortsetzung. Der Erstlauf stoppte wegen einer veralteten erwarteten Fehlerreihenfolge; die Mutation wurde bereits erkannt. [Erstlauf](../artifacts/compact-ux-20261026/session-first.log), [Fortsetzung](../artifacts/compact-ux-20261026/session-continuation.log) |
| Abschließende Hinweis-Überarbeitung | **750** Session-Prüfungen und **11** gezielte Negativkontrollen bestanden. [Log](../artifacts/run-arena-verification/artifacts_compact_ux_20261026_Verify_NoticeFinal_ps1.log) |
| Flächen-/Teammenü | **894** Menü-, **204** Hook- und **66** View-Prüfungen; fünf Negativkontrollen bestanden. [Log](../artifacts/run-arena-verification/AreaTeamMenuChecks.log) |
| Flächenvorschau | **4468** Prüfungen und fünf Negativkontrollen bestanden. [Log](../artifacts/run-arena-verification/AreaMapPreviewChecks.log) |
| Domination-Menüleistung | Vier Verhaltensmutationen zuverlässig erkannt. [Log](../artifacts/run-arena-verification/DominationMenuPerformanceChecks.log) |
| Build für den letzten GPU-Prüfstand | **0 Fehler, 4 bereits vorhandene CS8632-Warnungen.** [Buildlog](../artifacts/compact-ux-20261026/build-native-final.log) |
| Native Zwischenprüfung | Einstieg, sechs Modi, Kartenfeedback, Menü-Rückwege und erste Session-Rückkehr bestätigt; danach **228 Einstellungen und 185 Inventargegenstände exakt unverändert**. [Belege und Grenzen](GPU-COMPACT-UX-20261026.md) |
| Native Funktionsprüfung des finalen Stands | **Bestanden:** adaptive Bannerposition, bedienbarer Rückkehrknopf auf Händler-/Inventarseite, Dialogsperre, PitFireTeam-Regeln und Arena-Raidstart nach beiden Menü-Abstechern. [GPU-Abschlussbelege](GPU-COMPACT-UX-20261026.md#native-abschlussprüfung) |
| Letzter Rückweg und Bereinigung | **Bestanden:** Raid verlassen, Rückkehr vom Händlerbanner in die normale Session nach 55,1 Sekunden. Alle **228 Einstellungen einschließlich Metadaten und 185 Inventargegenstände exakt unverändert**; Testbegleiter entfernt, Spiel und Server wie zu Beginn beendet. [Abschlussvergleich](../artifacts/compact-ux-20261026/final-data-comparison.json), [Hoststatus](../artifacts/compact-ux-20261026/final-host-state.log) |
| Finale Deployment-Identität | Release-Build und installierter Client/BigBrain-Companion stimmen jeweils per vollständigem SHA-256 überein. Server, Redeploy.Core, README und Review unverändert. [Hashes](../artifacts/compact-ux-20261026/final-deployed-hashes.json) |

Während der TDM-Offline-Prüfung blieben alle sechs erfassten Installations-/Dokument-Hashes unverändert. Der anschließende Deploymentvergleich und die native Abnahme sind abgeschlossen; Belege und die begrenzte Logaussage stehen im [GPU-Bericht](GPU-COMPACT-UX-20261026.md).
