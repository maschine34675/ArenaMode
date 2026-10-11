# Native Mutatoren-Menüprüfung — 30.09.2026

Quellcommit `2ff4722`, Client-SHA256
`A2CF4757A74E5BF679FE075ECFEB2054183B1FF3105BAAA730774CB32480A9E7`.
GPU-Host 1 (.114), Profil SPT-GPU-1, EFT 0.16.9.5, 1920×1080.
UiBridge `0.7.3+419df3c`, Instanz `eac354d7`, Start 13:47:23.
Alle 24 maschine-Plugins und Redeploy.Core: VM = Host-Disk = geladen.

## Ergebnis

- Arena > Kartenauswahl zeigt MUTATORS an der bisherigen Killcam-Position.
- Popup mit acht vollständigen, lesbaren Schaltern und Beschreibungen; kein
  abgeschnittener Text im Screenshot. Der native Hit-Test bestätigt die
  Abschirmung darunterliegender Bedienelemente durch den Popup-Hintergrund.
- Double jump AN → AUS → AN: sichtbare Beschriftung und tatsächlich gelesener
  F12-Wert DoubleJumpEnabled stimmen jeweils überein.
- Deathmatch-Killcam AUS → AN setzt nur dessen ReplaySeconds auf 3; Gun Game
  und TDM bleiben 0, Tactical bleibt 3. Wieder auf AUS/0 zurückgesetzt.
- Deathmatch: Pickups ON, Team laser colors N/A mit Begründung.
- Gun Game: Pickups und Team laser colors N/A, jeweils deaktiviert mit Grund.
- Team DM: Pickups und Team laser colors ON und bedienbar.
- CLOSE sowie Klick auf den abgedunkelten Hintergrund schließen das Popup.
  Der Hintergrundklick wurde über dem verdeckten MUTATORS-Knopf ausgeführt:
  kein Durchklicken oder sofortiges Wiederöffnen.
- Escape bei offenem Popup führt wie bisher zur Charakterauswahl zurück.
  Erneuter Eintritt/Öffnen zeigt genau eine Ansicht mit acht Schaltern;
  keine alten Fenster oder doppelten Elemente.
- Wechsel zur normalen PMC-Kartenauswahl: kein Mutators-Element vorhanden
  (positiver Arena-Vergleich vorher, danach Snapshot 0/0).

Teständerungen zurückgesetzt: GameMode deathmatch, DoubleJumpEnabled true,
ReplaySeconds DM/GG/TDM 0 und Tactical 3. Andere Mutatoren wurden nicht geändert.
Kein Raid gestartet, kein Inventar-/Kauf-/Heilungseingriff. Spiel und zuvor
ebenfalls ausgeschalteter Server nach der Prüfung wieder sauber beendet.

Beim initialen Hauptmenüladen erschien vor Arena-Auswahl ein Dialog über eine
fehlende Herstellungsrezept-ID im Profil. Bestätigt und in diesem UI-Auftrag
nicht weiter untersucht; kein Nachweis einer Ursache durch das Mutatoren-Menü.

Belege lokal unter `artifacts/mutators-native/`: Screenshot `menu.png`
(SHA256 `18EF5AE98304CF3F0B28C01DB04512BBD21FF3629694F7152677D33804229F24`)
und Kopie des aktuellen Host-1-Logs `LogOutput.log` (Original
`BepInEx/LogOutput-SPT-GPU-1-2026-09-30_13-46-57.log`).

## Grenzen

Keine erneute Gameplay-/Killcam-Replay-Abnahme; keine PFT-Teameinrichtung und
keine anderen Auflösungen getestet. Fehlende Killcam, Movement-Master aus,
Arm-/Raid-Sperren und PFT-N/A-Zuordnung sind im echten Quellcode ausführenden
Offline-Harness geprüft, nicht in dieser nativen Menüsession.
179 Assertions und 14 kompilierte Fehlerkontrollen bestanden; isolierter
Client-Build erfolgreich, unabhängiges Review ohne offene konkrete P0-P2.
