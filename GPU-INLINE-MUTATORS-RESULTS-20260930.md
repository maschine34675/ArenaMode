# Native Inline-Mutatoren-Prüfung — 30.09.2026

Quellcommit `d0a602e`, Client-SHA256
`3388B737E8129A7F735C8CA4C8E8A5D6847A0695385F18844993E10B5C7004FD`.
GPU-Host 1 (.114), Profil SPT-GPU-1, EFT 0.16.9.5, 1920×1080.
UiBridge `0.7.3+419df3c`, Instanz `0cb76e77`, Start 14:18:36.
Alle 24 maschine-Plugins und Redeploy.Core: VM = Host-Disk = geladen.
Server/Core/BigBrain unverändert; Client-Auslieferung siehe
`INLINE-MUTATORS-DEPLOYMENT-20260930.json`. Host 2 nicht bedient.

## Ergebnis

- Arena > Kartenauswahl zeigt ohne weiteren Klick acht Mutatoren rechts.
  Eigene dunkle Buttons mit getrennten Statusfeldern; aktive Optionen sind
  deutlich grün (ON, Chip und Seitenstreifen), OFF/N/A grau.
- Double jump AN → AUS → AN: Beschriftung, Farbwechsel und von UiBridge
  ausgelesener F12-Wert `DoubleJumpEnabled` stimmen überein. Hover über dem
  ausgeschalteten Button macht dessen Status nicht wieder grün.
- Factory: kompakte Karteninfos oben rechts, Mutatoren direkt darunter.
  Tag-/Nachtwechsel funktioniert in beide Richtungen, native Next-Schaltfläche
  ist laut Hit-Test erreichbar. Kein Panel überdeckt diese Controls.
- Team DM > Customs: Arealkarte oben, Mutatoren darunter. Factory Center im
  5v5-Layout, alle drei Arealbuttons und Teamgrößen links sichtbar. AREA/OVERVIEW
  wechselt weiterhin die Kartenansicht. Kontur, Beschriftung und Attribution
  bleiben sichtbar; die native Zeit-/Wetterleiste hat rechts Abstand zum Panel.
- Zurück zu Deathmatch: Customs-Auswahl wird wie bisher ungültig, kompakte
  Karteninfo erscheint wieder, Mutatoren rücken nach oben. Teamlaser zeigt N/A;
  in Team DM zuvor ON. Keine doppelte oder liegengebliebene Vorschau.
- Escape → PMC → Factory: keine Mutatoren oder kompakte Arena-Infobox mehr.
  Native Factory-Grafik, volle Beschreibung und Raidstatistiken sind wieder
  sichtbar. Die zahlreichen überlappenden zusätzlichen ArenaMapPort-Kartentitel
  oben gehören nicht zu dieser neuen Ansicht und wurden hier nicht verändert.

Teständerungen zurückgesetzt: GameMode deathmatch, DoubleJumpEnabled true;
Factory-Zeit zurück auf Tag. Keine anderen Config-Werte geändert, kein Raid
gestartet, keine Inventar-/Kauf-/Heilungsaktionen. Spiel und der für die Prüfung
gestartete Server anschließend wieder sauber beendet.

Keine Warnung über den neuen Kartenpicker/Preview und kein CanvasGroup-Spam
im zugehörigen Log. Vor der Menüprüfung erschien ein Mikrofon-Initialisierungsfehler
von Unity sowie Startwarnungen anderer Hooks/optionaler Fika-Komponenten;
kein Beleg für eine Verursachung durch dieses UI.

## Belege und Grenzen

Lokale Belege unter `artifacts/inline-mutators-native/`:

| Screenshot | SHA256 |
|---|---|
| `factory/step-1-screenshot.png` | `9F720B920A86A1224F2BBFAE13614C944A48555B2A8B8879B52142463DF9CB93` |
| `customs/step-1-screenshot.png` | `F3832E31AEF1455997379CACD942F5A48F407027DE509A961F6197DE45E702C1` |
| `pmc/step-1-screenshot.png` | `457E73AAE037E1346C19DF0C6C3CAEC94AC692067AF1DF678EE7AA5E08DFAB55` |

Zusätzlich OFF-Screenshot und Logkopie `LogOutput.log` aus
`BepInEx/LogOutput-SPT-GPU-1-2026-09-30_14-18-13.log`.

188 Menü-Assertions und 14 kompilierte Fehlerkontrollen bestanden; Kartenharness
4393 Assertions und 5 kompilierte Fehlerkontrollen bestanden. Beide führen die
echten Produktionsansichten aus. Restaurierung und Lebensdauer der zwei
CanvasGroup-Besitzer werden auch kombiniert geprüft. Isolierter Release-Build
erfolgreich; unabhängiges Review nach Bereinigung des Fehlerpfads ohne offene
konkrete P0–P2-Befunde.

Native Abnahme ausschließlich der Menüs in 1080p. Kleinere 16:9-Geometrien
sind offline geprüft, nicht die dortige Schriftlesbarkeit. Keine neue
Gameplay-/Replay-/PFT-Raid-Abnahme; die Einstellungen und Gameplaypfade selbst
wurden in dieser Layoutänderung nicht geändert.
