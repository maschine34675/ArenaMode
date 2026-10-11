# Arena UI — GPU-Sichtprüfung und Nachtest

## Gelieferter Stand

- Umsetzung `fa8acae`, Nachbesserung nach Sichtprobe `d9d408c`.
- Finaler Client: `0.1.0+d9d408c93d512a506baaac72b6cffae5237b8372`.
- SHA256: `2AB51B569ED9D339DB6FD84F3961B2A2D1976EBD94DDAEC08112AE66B4766551`.
- Geladene MVID: `15c33ccd-33d7-4f16-aedd-8093aaf81608`, identisch mit GPU-Datei
  und Entwicklungsinstallation. Frischer EFT-Prozess, UiBridge 0.6.1.
- Nur Client-DLL ersetzt; Server/Core/UiBridge unverändert. Lieferung:
  `UNIFIED-UI-DEPLOYMENT-20260922.json`.

## Beobachtet

1920×1080, Factory Tag, separates Testprofil `ArenaGPU20260908`.
Echte Windows-Screenshots wurden während der Prüfung angesehen; die nachfolgend
verlinkten JSON-Abzüge belegen UI-Zustand/Build, keine Pixel. Kein synthetisches
Mockup als Spielnachweis verwendet.

| Prüfung | Ergebnis und Reichweite |
|---|---|
| Charakterauswahl | Arena per Modell/Option auswählbar, Beschreibung zweizeilig; PMC/Scav bleiben vorhanden. |
| Karten-/Regelmenü | Dunkle Flächen, Auswahlstreifen, Teilnehmerzahl und Regeln sichtbar; Wechsel Deathmatch → Tactical sowie Weiter/Ready bedienbar. Finale Buttonflächen auch nach Neustart sichtbar. |
| HUD | Score und Matchstatus auf eigener Fläche unter Quickslots; Waffenname, Ammo/Patronenname, Feuermodus, Magazine, Granaten, HP/Gewicht/Rüstung lesbar. Mk17 im ersten DM-Lauf, VPO-209 im finalen Tactical-Lauf. |
| Killfeed | Erster DM-Lauf: Botkill mit beiden Namen, Fraktionen, 4m und MP7A1; Feindfarbe erhalten, dunkle Fläche lesbar. Dieser Code ist im finalen Build unverändert. |
| F9 | Beide Läufe: Tabelle/Teamfarben/Details lesbar, Footer nicht abgeschnitten, Close/F9 bedienbar. Final: Checkbox-Zustand sichtbar, Killfeed aus/an geschaltet und zurück auf true verifiziert. |
| Tactical-Shop | Finale Fassung: Preise, bezahlbare/zu teure Kits und mehrteilige Beschreibungen lesbar; Footer sichtbar. Kein Kauf ausgeführt — keine neue Kaufverhaltens-Abnahme. |
| Bereitschaft | Finale Fassung: „ASSEMBLING OPPONENTS“ mit vollständiger Detailzeile, kein Überlappen mit Score/Quickslots. Danach fünf Gegner im Scoreboard. |
| Sichtbarkeit/Ausstieg | Erster Lauf: Arena-HUD im Inventar nicht sichtbar, bei Rückkehr wieder da. Final: F9 schließen, Escape/Disconnect und native Rückkehr zum Hauptmenü funktionieren. |

Die erste Sichtprobe fand zwei Präsentationsmängel, die `d9d408c` behebt:
fehlende eigene Buttonflächen bei TMP-Tintziel sowie fehlende Ein/Aus-Grafik der
F9-Schalter. Beide Nachbesserungen wurden im finalen Prozess direkt gesehen.

## Logs, Einstellungen und Profil

Im finalen UiBridge-Logfenster (Start 09:51:18 bis Hauptmenü/Ausstieg, #1–3517)
**keine ArenaMode-Warnung und kein ArenaMode-Fehler**. Keine pauschale Aussage
„gesamtes Modpack fehlerfrei“: Der bereits installierte RespawnApiProbe erzeugt
absichtlich eine `AlwaysThrow`-Subscriber-Ausnahme; außerdem existieren Meldungen
zu Mikrofon, Harmony-Overrides, PerfectCulling und fehlenden Szenenskripten.
Keine Probe/andere Mod entfernt und keine davon als durch den UI-Code verursacht
ausgegeben.

Teständerungen an Teilnehmerzahl, Modus und Killfeed zurückgestellt. Alle
gespeicherten Arena-Konfigurationswerte stimmen wieder mit der Vorherdatei
überein. EFT regulär beendet; Server weiterlaufen gelassen.

Der **zweite** Profilvergleich verwendet eine neue Baseline nach dem frischen
Hauptmenü und unmittelbar vor Raid-Eintritt. Vollständiges JSON identisch bis auf
`characters/pmc/Hideout/sptUpdateLastRunTimestamp`; Inventar, Skills und Stats
unverändert. Keine Profildatei manuell zurückgeschrieben.

Der erste Vergleich war kein gültiger Unverändert-Nachweis: Die vor EFT-Start
gesicherte GPU-Datei gehörte zu einem abweichenden vorhandenen Profilzweig.
Der nach dem Raid gespeicherte Stand entspricht den Backpack-Testprofilen vom
21.09. und mehreren bestehenden Syncthing-Konflikten, bis auf native Zeitstempel.
Serverlog belegt Snapshot/Restore/geschützten Abschluss. Das spricht für eine
Disk-/Server-Speicher-Abweichung, beweist deren genaue Ursache aber nicht.
Insbesondere Syncthing-Konflikte nicht blind auf ein laufendes Serverprofil
zurückspielen. Rohbelege bleiben ausschließlich lokal unter `artifacts/`.

## Offline und Review

Finaler kompletter Lauf inklusive nativer Client-Kompilierung:
`artifacts/ui-refresh-checks/2281ac6ad7dd4be5a6327f42fd8f60fa/`,
`ARENA_UI_REFRESH_VERIFIED`. Die zehn HUD-/Menü-/Tactical-Prüfpfade einschließlich
negativer Kontrollen sind grün. Build: 0 Fehler, vier bereits vorhandene CS8632.
Unabhängiger adversarial Source-/Lebensdauerreview nach beiden Implementierungen:
kein offener konkreter P0–P2. Grenzen der Fixtures: `UNIFIED-UI-REVIEW.md`.

## Begrenzte weitere Sichtabnahme

Noch **nicht** visuell durch diesen Lauf belegt: PFT-Zuschauerstatus mit langen
Botnamen/HP, Customs-Arealkarte/Polygonbeschriftung, Team-DM-Regelmenü in nativer
Darstellung, aktive Powerups, Schadenszahlen/Hitmarker, Terminal-Ergebnisfenster,
andere Auflösungen/DLSS und außergewöhnlich lange lokalisierte Waffen-/Botnamen.
Code und bestehende Verträge sind implementiert/geprüft; dies ist keine Behauptung
vollständiger visueller Abnahme aller Spielzustände.

Gezielter Nachtest mit genau obiger Build-ID, ohne neue UI-Regler:

1. Customs mit vorhandener Kontur auswählen, Mapvorschau/Labels und Team-DM-Regeln
   prüfen. Aktive Auswahlstreifen müssen sichtbar bleiben; Vanilla-Weiter/Zurück
   müssen erreichbar sein.
2. Tactical/PFT 5v5 nach Spielertod: zwei Zuschauerzeilen inklusive HP/Name müssen
   in die Fläche passen; Escape und Wechseln dürfen nicht blockieren.
3. In einem normalen Testmatch lange Namen, Serien/Assists, Powerups und
   Matchabschluss prüfen. Offsets live ändern, Inventar/Escape öffnen/schließen;
   keine abgeschnittenen wichtigen Zahlen und kein dauerhafter Cursorlock.
4. Mit der tatsächlich genutzten Auflösung/DLSS wiederholen. Native Screenshots
   plus geladene MVID festhalten; neue Layoutprobleme konkret am Element melden.
