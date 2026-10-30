# Mutatoren, Zeitwahl und Wetter – GPU-2, 03.10.2026

**Menüabnahme bestanden. Kein Raid in dieser Prüfung.** GPU-1 war nicht erreichbar;
der Nutzer hat GPU-2 ausdrücklich als Ersatz freigegeben. Anfangs und nach Abschluss
waren dort Spiel und Server beendet. Es wurden keine Kartenpakete neu gebaut oder
kopiert; ArenaMode.Client und beide ArenaMapPort-DLLs sind lokal und auf GPU-2
eingespielt. GPU-1 wurde nicht direkt beschrieben.

## Geprüfter Stand

| Komponente | Quellstand | SHA256 |
|---|---|---|
| ArenaMode.Client | `5a63fa9` (inkl. `5fabaf6`) | `6EB0A8BC6A59803ADC88C4B0ECDC1D8CA9A5F1531741B544ACCACC2E876077DE` |
| ArenaMapPort.Client | `1cb4572` (Menüänderung `aa0a8c1`) | `794B5AE286A30F64F0440B6315B1ACEED168322668C54A596E3050B5BD27CABA` |
| ArenaMapPort.Server | `1cb4572` | `3D54A40A79174F894BEDA0475F4F746482C6DDB961BB80E11FAF08D6AFF34E7F` |
| UiBridge | `0.7.3+419df3c2ef81f2582a3d205b4b2af98b80b35712` | `EC325E81FD79ECBC0890AD6AE5B1D42897676791C68935940B6EAF1FADEF51B6` |

Finaler Spielprozess `52a36096`, Start 19:06:15, Profil SPT-GPU-2,
1920×1080 FullScreenWindow. UiBridge bestätigte 24/24 identische maschine-Plugins
(lokale Installation = GPU-Disk = geladen) und identische Redeploy.Core.
ArenaMode.Server, Core und BigBrain blieben gegenüber Testbeginn unverändert.
UiBridge wurde parallel um 19:12:08 auf der Disk durch 0.7.4+ac0d61d ersetzt
(SHA256 `3D4278875638F52627CA1A40B2D569DA373B1DA97D5D44EBC102E4DEDB955663`).
Dieser Testprozess war vorher mit der oben genannten 0.7.3 gestartet; die
24/24-Aussage gilt für die Startprüfung um 19:07, nicht für den späteren
UiBridge-Diskstand. Die Arena-DLLs blieben identisch. Die Ursache des einzelnen
Screenshot-Proxy-Abbruchs wurde nicht separat untersucht.
Auslieferungsbelege: `MUTATORS-MAP-OPTIONS-DEPLOYMENT-20261003.json`
und `D:/ArenaMapPort/work/map-time-weather-deployment-gpu2.json`.

## Ergebnisse

- Alle zwölf Mutatoren sofort sichtbar; Beschreibungen bleiben lesbar. Der erste
  Testprozess `716b9ec7` deckte eine Überschneidung der ursprünglichen 496-Pixel-
  Fläche mit READY auf. `5a63fa9` reduziert die Fläche auf 472 Pixel und belässt
  Schriftgrößen und Thumbnail unverändert. Finale Unterkante Y=980; native
  READY-Fläche beginnt bei Y=984. Keine Überschneidung mit Zeitwahl, Next, Back
  oder der unteren Navigation. Siehe Screenshot 02/03.
- Air vault, Slide, Infinite stamina und No fall damage per Maus ausgeschaltet,
  alle vier passenden Movement-Einträge über UiBridge als false verifiziert,
  danach über die Schalter wieder eingeschaltet. Graues OFF/grünes ON korrekt.
- Deathmatch begann mit Colored tracers OFF, obwohl Effects/TeamTracers bereits
  true war. Explizites Einschalten änderte den neuen Deathmatch-Eintrag; danach
  auf false zurückgesetzt. Team-DM zeigte weiterhin ON, Rückwechsel zu DM OFF.
- Bay5 zeigt zwei native Auswahlfelder 12:00:00 und 00:00:00. Nacht angeklickt,
  Next bis MatchMakerAccept und Back: Nacht bleibt gewählt. Native Auswahl und
  `TimeSelection ... selected=PAST` im Log stimmen überein (Screenshot 03).
- Equator bei Nacht zeigt sofort Regen (Screenshot 04). Nur die Zeit auf 12:00
  umgeschaltet: Sonnensymbol wiederhergestellt, ohne neue Kartenwahl. Erneute
  Nachtwahl und Wechsel zu Factory: natives Sonnensymbol wiederhergestellt
  (Screenshot 06). Die Tagesansicht wurde direkt per Windows-MCP gesehen; deren
  zusätzlicher Screenshot-Dateiexport scheiterte einmalig am Proxy-Verbindungsabbruch.
- Normaler PMC-Kontrollschirm: keine Mutatoren/Arena-Panels; Factory-Thumbnail,
  Beschreibung, Raidstatistiken und native Zeitwahl wieder vorhanden (Screenshot 08).
  Die übrigen experimentellen MapPort-Karten erscheinen dort weiterhin mit ihren
  sich überlappenden ursprünglichen Spike-Beschriftungen; das ist kein neuer
  Arena-Menüstand und wurde in dieser Aufgabe nicht umgestaltet.

Alle vorhandenen ArenaMode-/ArenaMapPort-Konfigurationswerte sind wieder auf dem
Ausgangswert. Die einzigen erwarteten Unterschiede sind der neue Eintrag
`Deathmatch/ColoredTracers=false` und MapPorts Migrationsmarker
`TimeOfDay/FixedTimeSelectionRevision: 1 -> 2`. Kein gespeicherter Licht-/Wetterwert
wurde geändert. Kein Error/Fatal im gesicherten Clientlog; UiBridge meldete
ebenfalls keine ArenaMode-Warnungen seit seinem Start.

Belege liegen unter `artifacts/mutators-map-options-gpu2/`: Screenshots 01–04,
06–08, Snapshot-/Szenarioberichte, `final-client.log`, Konfiguration vor Test und
`config-delta.json`. Die große Logdatei und BSG-Grafiken bleiben ungetrackt.

## Offline-Prüfung und Grenzen

- Menü-/Config-Harness: 262 Assertions und 14 kompilierte Fehlerkontrollen.
- Tracer-Harness: 146 Assertions und 8 Fehlerkontrollen, einschließlich Übergabe
  des eingefrorenen ActiveMode und gewöhnlicher Raids.
- AreaMapPreviewChecks bestanden; isolierter Arena-Release-Build ohne Fehler
  (vier bekannte Nullable-Warnungen).
- ArenaMapPort: 487 Tests, Client-Build ohne Warnungen/Fehler, 10 neue DLL-only-
  Deployfälle; Implementer außerdem 90 bestehende DynamicMaps-Deployfälle.
- Unabhängige Reviews für Menü/Tracer, native Zeit-/Wetterhooks, Layoutkorrektur
  und sicheren DLL-Deploy ohne offene P0–P2-Befunde.

720p/900p wurden nur im Geometrieharness geprüft. Dieser Lauf bestätigt weder
Bot-Ausrüstung im Raid noch das Schussbild der Tracer oder Bay5s sichtbare
Nachtbeleuchtung. Bay5 verwendet den vorhandenen echten Zeit-/Gearpfad; seine
künstlichen Lampen wurden nicht verändert. Keine neue Karten-/Modusfreigabe:
Begründung und nächster Ausbau in `MAP-MODE-COMPATIBILITY.md`.
