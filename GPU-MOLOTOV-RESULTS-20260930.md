# Brandgranaten-Prototyp: erster nativer Test

2026-09-30, GPU-Host 1 (.114), Testprofil SPT-GPU-1. Host 2 lief unter fremder
Steuerung im Kartentest und wurde von dieser Arbeit nicht gesteuert/beschrieben.
UiBridge 0.7.3, EFT 0.16.9.5-40743, Unity 2022.3.43f1.

## Getesteter Build und Ablauf

Release aus sauberem Commit `bfdf327` (Prototyp `79eb2ab` plus aktueller Hauptbranch
`50b7bc9`, einschließlich Stacheldraht-Killfeed).

| Datei | SHA256 |
|---|---|
| Client | `EC700EAEE288C73E603AEEB8740DC0E9823FF07C17625F831579D5354CCAC250` |
| BigBrain-Companion | `4A2780F73BEE69F6EAF381E3729018E2BF2C04F4BDE1461CBF8DBCCBC90E04E8` |

Client/Companion lokal und auf Host 1 gemeinsam unter exklusiven Dateisperren
eingespielt, Rücksicherungen vorhanden; Core und Server unverändert. Danach
normaler EFT-Neustart und identische geladene Arena-Builds bestätigt.
ArenaMapPort änderte sich parallel und wich später zwischen Disk und geladenem
Stand ab; keine Gleichheit sämtlicher Mods für den kompletten Test behauptet.

Automatisiert über native Menüs in einen Equator-Deathmatch gestartet. F12-
Prototyp aktiviert, kit-eigene RGD-5 mit nativen ECommands gezogen und geworfen.
Drei tatsächliche Würfe, keine erfundenen Granaten-/Schadensereignisse.

## Beobachtet

- 10:02:53.673: erster eigener Wurf, um 10:02:53.800 Feuer mit 36 Bodenpunkten.
  Screenshot zeigt Flammen und bereits auf 340/440 reduzierte HP. Spieler blieb
  darin, starb und respawnte regulär mit neuem Kit und vollen HP.
- 10:03:34.429: zweiter Wurf, 47 Bodenpunkte. Für das weitere Positionieren war
  UiBridge-Godmode aktiv; deshalb kein weiterer Eigenschaden gewertet.
- 10:04:16.407: dritter Wurf neben Bot Basuro; Feuer mit 33 Bodenpunkten,
  echter Botkontakt um 10:04:16.569. Companion protokolliert Fluchtauftrag von
  `(10.67, 0.10, -12.22)` nach `(14.67, 0.06, -12.22)`.
- Unmittelbar danach meldet der native Bot-Inspektor **Arena Combat Ground Fire
  Escape**, **moving**, Position `(14.0, 0.0, -9.6)`. Später wieder **SAIN : Combat
  Layer**, Position `(12.9, 0.0, 0.8)`. Damit sind tatsächliche Bewegung aus der
  Feuerzone und Rückgabe an die normale KI beobachtet, nicht nur ein Logauftrag.
- Feuer-Optik sichtbar. Direkt innerhalb der Fläche sind die prozeduralen
  Partikel sehr dicht und kantig; Grafik bleibt ausdrücklich ein Platzhalter.
  Keine Hörprobe aufgezeichnet, daher keine subjektive Sound-Abnahme.

Kein gegnerischer Kill durch Feuer in diesem Lauf: Basuro entkam. Deshalb keine
native Abnahme von Feuer-Killfeed/Assists oder posthumen Gegnerkills. Ebenso offen:
separate Stockwerks-/Wandversuche, TDM-Freunde, Tactical/PFT, Bossverhalten,
normale PMC-Kontrollrunde und längerer Lasttest. Diese Grenzen ersetzen die
Offlinetests nicht und werden nicht als bestanden gezählt.

## Aufräumen und Profil

Prototyp wieder **false**, Godmode ausdrücklich aus. Raid normal über
DISCONNECT/Ergebnis-Seiten beendet, keine Heilung gekauft. EFT im Hauptmenü,
Steuerung frei. Native Inspektion nach Raidende: Fires=0, Throws=0, Retain=0,
`_world=null`, `_match=null`, Ticked ohne Einträge; alle fünf Inspector-Handles
freigegeben. Keine Warnung aus dem Brand-/Fluchtpfad.

Vorhandene Nebenbefunde: Mikrofon-Gerätefehler beim Start; Hitmarker-Warnung
`load_kill, Only one mono PCM format is supported` vor dem ersten Brandwurf.
Keine neue Exception aus Brandfläche, Granaten-Hooks oder Fluchtlayer.

Vollständiger semantischer Profilvergleich vom Zustand **vor Login** bis nach dem
Raid: 405/405 identische Item-IDs, keine hinzugefügten/entfernten Gegenstände,
keine XP-/Skill-/Quest-/Kampfstatistikänderung. Fünf Unterschiede:

- `Health.UpdateTime` und `Hideout.sptUpdateLastRunTimestamp`;
- laufende Hideout-Produktion `5d93b91286f77467310ca15d`: Progress 230 → 710;
- zwei bereits vorhandene einzelne Gasmaskenfilter im `hideout`-Slot:
  `upd.StackObjectsCount` fehlend → 1.

Die Produktion lief im Hintergrund weiter; die beiden Zähler entsprechen einer
Serialisierungs-Normalisierung vorhandener Einzelitems. Da der Vergleich vor
Login begann, wird **keine bitidentische Profil-Abnahme** behauptet. Kein Profil
manuell korrigiert, keine Arena-Waffen/Granaten oder Kampffortschritte übernommen.

## Belege und letzte Absicherung

Rohprofile bleiben ignoriert unter `artifacts/molotov-gpu`. Dort liegen auch der
rekursive Vergleich, redigiertes Clientlog und die drei Screenshot-Berichte
`throw1`/`throw2`/`throw3` (je `step-1-screenshot.png`).

| Beleg | SHA256 |
|---|---|
| Profil vorher | `A5B68AD8CAEB13A05110E9D37A3C7A6C1F3D60AC6DB33C8F1B76D0C4605A0947` |
| Profil nachher | `4AC7AEEC5CF728FF53AFF5E7CE9088009548174D60AF138DE8E991C12BA74743` |
| Redigiertes Clientlog | `FAD2DCE36E0CE02479306874918193ED405CF70DAD9A21548AD960F6F0838EDC` |

Nach diesem Test wurde Reset zusätzlich um `Ticked.Clear()` ergänzt: Bei einem
Abschalten mitten im Schadentakt darf auch der temporäre Opfer-Satz keine Körper
halten. 57 Produktions-Assertions und acht gezielte kompilierte Fehlerkontrollen
bestanden; fokussierte unabhängige Review ohne offene P0–P2. Diese letzte einzelne
Bereinigungszeile ist durch Desktopchecks geprüft, noch nicht durch einen zweiten
nativen Wurf. Der endgültige Auslieferungsbeleg wird separat aktualisiert.
