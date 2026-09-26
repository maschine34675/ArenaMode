# Einheitlicher Arena-Look — 2026-09-22

## Score-Vergleich ohne verwaiste Punktzahl (2026-09-26)

Der Screenshot `Screenshot-2026-09-23-170642.png` zeigt, wie beim langen
Rivalennamen die Punktzahl allein in die nächste Zeile umbrechen konnte.
Der Vergleich hat jetzt eine eigene untere Zeile innerhalb der unveränderten
84 HUD-Einheiten Panelhöhe. Name und Punktzahl sind getrennte, gemeinsam
zentrierte TMP-Felder ohne Wortumbruch; die gemessene Breite der Zahl wird zuerst
reserviert, nur der Name erhält bei Platzmangel eine Ellipse. Matchstatus und
Streak/Zeit behalten die volle Breite darüber (13–16 pt, einzeilig).
Tactical-Vergleich und Warten-Text nutzen die untere Zeile zentriert; Team DM
behält seine Teamzahlen in der Statuszeile und zeigt keinen Rivalenvergleich.
Ohne Vergleich kann die Statuszeile weiterhin beide unteren Zeilen nutzen.
Keine Änderung an Scores, Datenbereinigung, Offsets oder angehängten HUD-Panels.
Native Pixelprüfung mit langem Namen, großen Scores und Tactical/TDM steht aus.

Gewünschte Richtung: **dezenter Arena-Stil**, ohne weitere Einstellungsflut.
Die Änderung betrifft ausschließlich die Darstellung. Gespeicherte Positionen,
Offsets, Sichtbarkeit, eigene Statusfarben und alle Spielregeln bleiben bestehen.

## Gestaltung und Umfang

`ArenaUiTheme` bündelt dunkle blaugraue Flächen, helle Schrift, ruhigere
Nebeninformationen, einen warmen Akzent und feine obere Trennlinien. Bedeutungen
von Team-, Treffer- und Powerup-Farben bleiben erhalten. `ArenaWindowSkin` führt
dieselbe Gestaltung in den bislang separat gestalteten IMGUI-Fenstern fort.

- HUD: Score/Matchstatus, Killfeed, Ansagen, Treffer/Schadenszahlen,
  Bereitschaftshinweis, Powerups, Waffen-/Spielerstatus und integrierte Info-Panels.
  Größere Hauptzahlen, ruhigere Beschriftung; Namen, Distanzen, Granaten und
  Magazininformationen bleiben vollständig datengetrieben.
- Scoreboard/Ergebnisfenster, Tactical-Kaufmenü und Zuschauerstatus verwenden
  dieselben Farben, Schriften, Flächen und Schaltflächenzustände.
- Arena-eigene Charakterbeschriftung, Modus-/Regel-/Teilnehmerauswahl und
  Karten-/Arealvorschau passen dazu. Vanilla-PMC/Scav, Mapprojektion und Credits
  werden nicht umgestaltet.
- Ausgenommen: Server-Adminseite, Entwicklungs-/Erfassungsanzeigen, Weltmarker-
  Projektion und dreidimensionale Pickup-Modelle. Keine neuen Konfigurationswerte.

## Lebensdauer und Eingabe

Flächen/Trennlinien gehören dem jeweiligen UI-Objekt und haben
`raycastTarget=false`. Klon-Schaltflächen behalten ihre Callbacks und ihren
gesperrten/aktiven Zustand. Es werden keine geteilten TMP-Materialien oder
`GUI.skin` verändert und keine TMP-Outline-Setter unter inaktiven Menüobjekten
aufgerufen. Native Canvas-/TMP-Erzeugung und bestehende Sichtbarkeitsgrenzen
bleiben erhalten.

Jeder IMGUI-Besitzer erstellt maximal vier kleine 8×8-Texturen einmalig; sie werden
beim Zerstören freigegeben, auch bei teilweise fehlgeschlagener Konstruktion.
Schriften werden nur ausgeliehen. CursorVote, InputBlocker, Escape, Kaufregeln,
Scoreberechnung, Killfeed-Pooling und Inventarabfragen bleiben unverändert.

## Referenz und Herkunft

[ImpactCue v1.9.5](https://github.com/Dizzlow1/ImpactCue-UI-MOD/releases/tag/v1.9.5)
diente als Gestaltungsreferenz für Hierarchie, dunkle Flächen und feine Linien.
**Kein Quellcode, keine Icons, Audios oder Schriften übernommen.** Die gemeinsame
Arena-Gestaltung ist eine eigene Implementierung; ImpactCue ist keine Abhängigkeit.

Geprüft wurde das veröffentlichte `ImpactCue-UI-MOD-1.9.5-Source.zip`, SHA256
`FAFEEFF4FB4168F4AC0642F7298FFA85D6B6798A85FD9BFD35D9C0672503219E` (MIT, Dizzlow;
enthaltene Tabler-Icons mit separatem MIT-Hinweis). Der Quellbaum am Release-Tag
`412a5bb4e149efe37bae2d5067db1499013dd3b9` ist älter als dieses Source-Asset und
wurde nicht als gleichwertiger 1.9.5-Stand behandelt. Kein Referenz-Runtime-Test.
Die umfangreiche Konfiguration und Schadens-/Inventarlogik wurden nicht übernommen.

## Verifikation

`pwsh -NoProfile -File tools/UiRefreshChecks/Verify.ps1` prüft die bestehenden
HUD-, Killfeed-, Menü-, Karten-, Bereitschafts-, Zuschauer- und Tactical-Verträge
einschließlich negativer Kontrollen und baut den nativen Client isoliert, ohne
installierte Dateien zu ersetzen. Tatsächliches Theme ist in drei Menü-Harnessen
verlinkt; zusätzliche Assertions schützen Kontrast, klickdurchlässige Dekoration
und unveränderte gesperrte Schaltflächen/Callbacks. Die Assembly-/Tactical-Fixtures
isolieren ausdrücklich nur Rendering, sie beweisen keine Pixel oder Unity-Laufzeit.

Erster vollständiger Lauf: grün, Belege lokal unter
`artifacts/ui-refresh-checks/450d0847bcce4f1dacd0d6c25f64c306/`.
Unabhängiger Source-Review: kein konkreter offener P0–P2; native Prüfpunkte sind
lange Ammo-Namen, zweizeilige Match-/Zuschauertexte und Schaltflächenhöhe.

Build, Deployment und visuelle Spielabnahme sind getrennte Nachweise. Der
abschließende Liefer-/GPU-Beleg wird separat dokumentiert; Offlineprüfungen
allein bestätigen keine visuelle Abnahme.

### Nachbesserung aus erster GPU-Sichtprobe

Der native Dropdownklon kann seine Beschriftung als `targetGraphic` verwenden.
Die erste Fassung beließ dadurch Teile des Vanilla-Tints. Jeder Arena-Menübutton
bekommt nun ein eigenes passives Hintergrund-Image als Tintziel; der alte
Label-Tween wird mit `CrossFadeColor(white, 0, true, true)` zurückgesetzt.
Auswahlstreifen liegen darüber. Ein Fixture mit TMP als ursprünglichem Tintziel
prüft diese Grenze; das ist weiterhin keine Pixelprüfung.

Die vorhandenen fünf F9-Schalter benutzen eine Kopie von `GUI.skin.toggle`, damit
ihr Ein/Aus-Zustand sichtbar ist. Native Checkboxgrafiken/-abstände bleiben
geborgt; nur Schrift und Textfarben kommen aus der Arena-Gestaltung.
Unabhängiger Nachreview ohne P0–P2, auch gegen installierte Unity/TMP-Lebensdauer
für den Tint-Reset unter inaktiven Menüeltern geprüft. Vollständiger Wrapper erneut
grün (`artifacts/ui-refresh-checks/08b0eaefa50b4c4fb8320cc499b6756b/`).
