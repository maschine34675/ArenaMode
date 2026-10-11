# Zombie-Modus-Prototyp — GPU-2, 06.10.2026

## Ergebnis

Der fünfte Arena-Modus **ZOMBIES** läuft auf SPT-GPU-2 (192.168.178.109, EFT
0.16.9.5, UiBridge 0.7.4) im Spiel: Er ist im Kartenmenü wählbar, das native
Halloween-Infektionsevent wird nur für den bewaffneten Raid reaktiviert, Arenas
eigener Wellendirektor spawnt die Infizierten, sie verfolgen den Spieler, greifen
an, töten ihn, der Instant-Respawn greift, und der Spieler kann sie töten und
bekommt Punkte.

Der erste GPU-Raid (Raid 1) fand genau einen Blocker: **kein Zombie hat in 19
Minuten einen Treffer gelandet.** Ursache war SAINs globaler
`RunToEnemyUpdate`-Prefix, der den nativen Nahkampf aller Bots ohne
SAIN-Komponente unterdrückt, also auch aller Infizierten. Der Fix (Commits
`445269c` bis `0c6fab3`) treibt den echten nativen Nahkampf-Body für Infizierte
über einen HarmonyX-Reverse-Patch. Der zweite Raid (Raid 2) belegt ihn: 2618
Schwünge, 178 gelandete Treffer, ein Spielertod, drei Spieler-Kills, keine
Error-Zeile. Ein anschließender normaler PMC-Raid auf Factory zeigt keinen
Zombie-Einfluss, und das native Flag war nach beiden Zombie-Raids wieder `false`.

Offen und kein Blocker: Der Stuck-Wave-Wachhund räumt eine laufende Welle nach
100 s ab, auch mitten im Kampf, wenn der Spieler sie nur nicht schnell genug
tötet. Das ist für echte Spieler zu kurz (siehe Nächste Schritte). Das Ende einer
Welle durch Kills wurde nicht gesehen.

## Build und Auslieferung

Beide Raids liefen über `gpu-run` (UiBridge-Repo, Commit `c394e9c`), das jetzt
`raid --mode zombie --map FACTORY` und die aktuelle Kartenfreigabe kennt. Beide
Raids starteten damit nach 36 s bis `raidStarted`, Factory bei Tag,
Standardwerte: MaxAlive 20, Start 8, +4 je Welle, Spawnintervall 1 s, Pause 10 s,
Schützenanteil 0 %, StuckWaveTimeout 100 s.

| Raid | Client-Quelle | Client-SHA256 | Server-SHA256 |
|---|---|---|---|
| 1 | `1e76582` | `1ee4c51ffeeb8cc997bdd38fd8793f091f57e326ef08ab670a9242205d38eb18` | `0a826e8b3016cbce6a9abe910c46281040d4f4d4e77ebbcb464b7b3c1aea850b` |
| 2 | `0c6fab3` | `231f3be155298aa04ed1531b2efeead24a5d229c065472978437cda30770f4bd` | unverändert (dieselbe Server-DLL) |

Die Server-Quellen sind seit `1e76582` unverändert, die Server-DLL gilt damit auch
für Raid 2. Client- und Server-DLL aus `1e76582` wurden in die Spiegel-Installation
`D:\SPT41` kopiert; Syncthing lieferte sie in 15–20 s auf Host 2. Für Raid 2
kam die Client-DLL aus `0c6fab3`, EFT wurde per `gpu-run restart` neu gestartet.
24/24 geladene maschine-Plugins sind identisch (VM-Build = Host-Platte =
geladen). Redeploy.Core und BigBrainCompat sind unverändert.

Die Builds im Worktree liefen ohne Deploy (`-p:SPTModType=Library`,
`-p:SPTUserMods` auf einen nicht existierenden Pfad). Offline: 835 Tests, keine
Fehlschläge (833 bestanden, 2 vorbestehende umgebungsabhängige Skips).

## Raid 1 (Befund)

12:52–13:11, Build `1e76582`, UiBridge-Instanz `fab8a003`.

- Der Modus ist im Kartenmenü wählbar (Zeile ZOMBIES). Der Server-Klon trägt den
  Infektionsblock, die Bridge meldet „armed the native Halloween infection event“.
- 8 Infizierte spawnten in 7 s (`infectedAssault`/`infectedPmc`/`infectedCivil`/
  `infectedLaborant`, 25–70-m-Ring, BotZone).
- Zombie-Körper (`UsedSimplifiedSkeleton=true`), Messer in der Hand, Ebene
  `InfectedTarget` → `ZombieFast`. Sie erreichten den stehenden Spieler in etwa
  40 s.
- HUD „WAVE 1 | 8 LEFT | 00:13“ und Callout erschienen.
- **Kein einziger Treffer in 19 Minuten.**
- Der Wachhund räumte jede Welle nach 100 s ab (Wellen 1–4 beobachtet), Pausen
  10 s, die nächste Welle jeweils +4.
- Keine Error-Zeilen. Das Flag `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` wurde nach dem
  Raid live gelesen: `false`.
- Eine Warnzeile: `Activate()` feuerte 77 ms nach dem ersten Arm ein zweites Mal
  im selben Raid (das Flag blieb korrekt). Seit `c7b4dee` ist das nur noch Info,
  solange derselbe `GameWorld` vorliegt.

## Diagnose

Per `uibridge_inspect` live an Zombies in 0,8 m Entfernung zum Spieler:

- Knotenverzeichnis enthält `OneMeleeAttackNode`; Agent `LastReason` „atk2“
  (`oneMeleeAttack` der `InfectedAttackLayer`), `EndReason` „rndTimer“. Tür-Gate
  `CanRun`, Mover `Default`, keine Tür.
- `GoalEnemy` = Spieler, Distanz 0,79 m, `IsVisible` true, `PersonalLastSeenTime`
  jünger als 1 s.
- `MeleeWeaponEquipped` true, `KnifeController` gesetzt, `Physical.CanMeleeHit`
  true, `StateIsSuitableForHandInput` true, `KnifeController.CurrentOperation`
  Idling.
- Aber `BotMeleeWeaponData._nextTryHitTime` blieb 0 und `LastTimeEnemyHit` −100000:
  `RunToEnemyUpdate` lief nie. `AICoreController._errorsPrinted` war 0, es wurde
  also keine Ausnahme verschluckt.

**Ursache:** SAINs `RunToEnemyUpdatePatch` (`FixPatches.cs`) endet für jeden Bot
ohne SAIN-Komponente mit `return false` und unterdrückt damit den nativen
Nahkampf aller Nicht-SAIN-Bots. Die Infizierten stehen auf SAINs
`StrictExclusionList`, haben also nie eine SAIN-Komponente.

**Fix (`445269c` bis `0c6fab3`):** Ein Prefix auf
`OneMeleeAttackNode.UpdateNodeByBrain` treibt für Infizierte im Zombie-Raid eine
HarmonyX-Reverse-Patch-Kopie des originalen `RunToEnemyUpdate`, die SAINs Prefix
nie sieht. Fallback ist eine zeilenzitierte Reimplementierung. Das Gate
`ZombieMeleeGate` ist eine reine Regel mit 5 Tests samt Fehlerkontrollen.
`ZombieMeleeDiagnostics` loggt die ersten 3 Schwünge und Treffer und am Raidende
die Summe. Details: `ZOMBIE-MODE-REVIEW.md`, Abschnitt „Native melee is
suppressed by SAIN's global prefix“.

## Raid 2 (Nachweis)

14:11–14:16, Build `0c6fab3`, Instanz `cbf3d01e`.

| Zeit | Beobachtung / Beleg |
|---|---|
| 14:12:25 | „first native swing by infectedPmc at 0.7 m“; Pfadzeile „driving infected RunToEnemyUpdate through a HarmonyX reverse patch of the native body“. Der Reverse-Patch lief, nicht der Fallback. |
| 14:12:25.8 | Erster gelandeter Treffer. Spielergesundheit fiel: rechter Arm 60 → 12 → 0, Bauch 70 → 63. |
| 14:12:48 | „Arena death #1 — instant respawn“. Respawn-Pipeline 0,20 s, Killfeed „ZOMBIE → USEC · 1 m · Infectious strike“, Spawnschutz 4 s. Die Welle lief weiter (HUD „1 DEATHS … WAVE 1 \| 8 LEFT“). |
| 14:14:03 | Der Tester schoss nicht, der Wachhund räumte Welle 1 ab. Welle 2 (12 Infizierte) kam. |
| danach | Mit Godmode und Zielverfolgung (`uibridge_look track head` plus `uibridge_hook fire`, HK 416A5) drei Kopfschuss-Kills: Score 450, Streak 3. HUD „WAVE 2 \| 10 LEFT“ nach zwei Kills, der Zähler sinkt also pro Kill. Kill dem Spieler zugeordnet („killed by you, HK 416A5, Head“). |
| 14:16:05 | Der Wachhund räumte die restlichen 9 ab. |
| Raidende | „2618 native melee swing(s) attempted, 178 landed hit(s)“; „3 kills / 1 deaths, score 450, best streak 3, 3.9 min“. |

Keine Error-Zeilen. Performance mit 12 Zombies: 120 fps (8,3 ms Mittel), RAM
11,2 GB, managed 2,3 GB. Flag nach dem Raid: `false`.

Die drei Kills entstanden über Testhooks, nicht durch normales Spielen; sie
belegen Zuordnung, Score und HUD-Zähler, nicht den Schwierigkeitsgrad.

## Kontrollraid

14:16:40, normaler PMC auf Factory, Bots „Low“. PMC-Bots mit „SAIN : Combat
Layer“, im Log nur zwei gewöhnliche ArenaMode-Menüzeilen, keine Zombie- und keine
Bridge-Zeilen, keine Fehler, Flag `false`. Normaler Ausstieg mit HealthTreatment
(geheilt), danach Hauptmenü.

## Grenzen und offene Punkte

- **Wellenende durch Kills nicht im Spiel gesehen.** Beide Wellen beendete der
  Wachhund, weil der Tester nicht alle Infizierten tötete.
- **Wachhund-Semantik:** 100 s ohne Wellenende sind für echte Spieler zu kurz. Eine
  laufende Welle wird mitten im Kampf abgeräumt.
- **Zweites `Activate()`** im selben Raid ohne bekannten Aufrufer; es liegt
  außerhalb der nativen Kette (siehe `ZOMBIE-MODE-REVIEW.md`, „Double-Activate“).
  Harmlos, aber nicht lokalisiert.
- Schützenanteil 0 % nicht getestet; der Fernkampfpfad der `hard`-Infizierten ist
  damit ungesehen.
- Nur Factory. Längster Nahkampf-Raid 3,9 min, keine Langzeitmessung, Leichenzahl
  über die Zeit nicht gemessen. Kein PitFireTeam.
- Kein Profil-Diff in dieser Sitzung; belegt sind nur die Logzeilen „skills
  restored“ und der Kontrollraid mit normaler Heilung.
- „swing(s) attempted“ zählt `TryHit`-Aufrufe im 0,1-s-Takt, nicht sichtbare
  Animationen.

## Nächste Schritte

1. **Wachhund-Semantik ändern.** Der Timer soll nur zählen, solange seit 100 s
   kein Infizierter gestorben ist (Fortschritt statt Dauer), oder der Standard
   steigt auf 240 s. Danach ein Raid, in dem eine Welle wirklich durch Kills endet,
   plus ein gezielt gestrandeter Zombie für den Wachhund selbst.
2. **Zweites `Activate()`:** Stacktrace beim nächsten Auftreten der Info-Zeile
   erfassen, um den Aufrufer außerhalb der nativen Kette zu finden.
3. **Performance und Leichen** bei MaxAlive 40–56 messen (Frame-Zeit, Botzahl,
   Leichenzahl über die Zeit).
4. **Später:** Schützenanteil, Labs und Labyrinth, Tagilla, PitFireTeam.

## v1 (06.10.2026 abends): Wachhund, große Wellen, Nachfüllen, eigenes Gear

Die vier Wünsche aus dem Playtest nach v0 (Wachhund nach Fortschritt statt Dauer,
deutlich größere und pausenlos nachfüllende Wellen, Nachfüllen zwischen den Wellen,
eigenes Gear) liefen am Abend in drei Raids auf SPT-GPU-2. Ergebnis in Kürze: Der
Wachhund bleibt still, solange getötet wird (Raid A). Das Nachfüllen nach einem
Wellenende greift im Zufallskit-Modus (Raid A) und mit eigenem Gear (Raid C). Eigenes
Gear hält die Ausrüstung über vier Tode und lässt das Profil unverändert (Raid B,
Profilprüfung). Raid B fand einen Fehler: Der Wachhund schaltete sich nur bei
vollständig gespawnter Welle scharf und feuerte deshalb bei einer gesättigten
30er-Welle (20 lebend, 10 offen, kein Kill) nie. Der Fix (`39e193d`/`acd57ff`) ist in
Raid C bestätigt. Nicht gesehen wurde weiterhin eine Welle, die durch Kills endet.

### Build und Auslieferung

- Commits `452d87a`..`cf100ab` (v1, zwölf Commits) plus `39e193d`/`acd57ff`
  (Wachhund-Korrektur für gesättigte Wellen). Offline: 857 Tests grün (855
  bestanden, 2 vorbestehende umgebungsabhängige Skips, laut `39e193d`),
  Client-Release-Build ohne Fehler.
- Die Server-Quellen sind seit `1e76582` unverändert, deshalb wurde nur die
  Client-DLL ausgeliefert, in zwei Ständen:

| Raids | Client-Quelle | Client-SHA256 |
|---|---|---|
| A und B | `cf100ab` | `2e1448d9b8ad0b171ce7bd1c44ec017104d9a45bb7c76974a70b2dbafabdbd0b` |
| C | `acd57ff` | `fdff66da1ce6f4be4573b25580e39bd2006bb30af815254f324cc61f562765c5` |

- Jeweils in `D:\SPT41` kopiert; Syncthing lieferte in 15–20 s auf Host 2, EFT wurde
  per `gpu-run` neu gestartet, 24/24 geladene maschine-Plugins identisch. Hosts:
  SPT-GPU-2, UiBridge 0.7.4, EFT 0.16.9.5.
- Zustand nach Raid C: OwnGear wieder `false`, die drei Wellenwerte bleiben auf den
  neuen Standards (30 / 10 / 0,75 s).

> **Wichtig für Nutzer:** BepInEx behält gespeicherte Werte. Die neuen Standardwerte
> (WaveStartSize 30, WaveGrowth 10, SpawnIntervalSeconds 0,75) greifen auf einer
> bestehenden Installation erst nach Zurücksetzen in F12 oder Löschen der Einträge in
> der cfg. Auf Host 2 standen noch 8/4/1 aus v0; sie wurden für Raid B per
> `uibridge_config_set` auf die neuen Werte gesetzt. Raid A lief deshalb noch mit
> 8/4/1.

### Raid A (Zufallskits)

17:58–18:04, Build `cf100ab`, Zufallskit „Wraith“, alte Wellenwerte 8 / +4 / 1 s,
Godmode an.

| Zeit | Beobachtung / Beleg |
|---|---|
| 17:59:50, 18:00:30, 18:01:15, 18:01:30 | Vier Kopfschuss-Kills, verteilt über mehr als 100 Sekunden. **Keine Wachhund-Warnung.** Unter v0 hätte er um 18:00:20 gefeuert; die Progress-Semantik ist damit belegt. |
| 18:03:06 | Nach dem letzten Kill 100 s ohne Zombie-Tod: Wachhund „administratively retired 4 infected“, Welle 1 beendet. |
| danach | Nachfüllen: „Zombie refill after wave 1: kit 'Wraith' re-applied“. Magazin 17/30 → 30/30 +1, Gesundheit und Hydration voll, Kills 4 erhalten, Tode 0 (es wurde kein Tod gezählt). |
| danach | HUD „WAVE 2 \| 12 LEFT“. |

Nebenwirkung: Der Spieler wurde beim Nachfüllen an einen Spawnpunkt versetzt
(14, -3, -17 → 72, 0, -74, aus den Positionen gerechnet rund 81 m horizontal). Der Pfad
`LiveKitRespawn.AtRoundBreak` ist der Rundenwechsel; die Versetzung gehört zur
dokumentierten Obermenge (`ZOMBIE-MODE-REVIEW.md`, „Accepted superset“).

Keine Error-Zeilen. Das native Flag `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` wurde nach dem
Raid live gelesen: `false`.

### Raid B (eigenes Gear)

18:04–18:10, Build `cf100ab`, `OwnGear` an, Wellen 30 / +10 / 0,75 s, MaxAlive 20.

- Ladebild und Raidstart: „nothing to preload in the loading screen — Zombie own gear
  keeps the entry loadout“ und „skipped the raid-start kit swap … The secure
  container stays as it is, shown and usable“.
- „Zombie match started: wave 1 of 30, +10/wave, max alive 20, spawn every 0.75s“.
  20 Zombies waren nach etwa 15 s gespawnt, HUD „WAVE 1 \| 30 LEFT“.
- **Das Testprofil SPT-GPU-2 trägt als eigenes Gear nur ein ER-Bajonett, einen leeren
  Gamma-Container, Taschen und einen Dogtag.** Getestet ist damit die Mechanik, nicht
  ein echtes Loadout.
- Vier Tode durch Zombies (18:05:44, 18:06:32, 18:07:00, 18:07:34), jeweils „Arena
  Zombie own-gear respawn completed (epoch n)“ in 0,07–0,19 s. Die Ausrüstung danach
  ist identisch: Scabbard und Gamma mit denselben Item-IDs, Taschen und Dogtag neu.
- Performance bei 20 Zombies: 119,6 fps (8,4 ms Mittel), RAM 12,4 GB, managed 2,6 GB.
- **Befund: Der Wachhund feuerte nie.** Welle 1 kam ohne Kills nie über 20/30
  hinaus, weil er sich nur bei vollständig gespawnter Welle scharf schaltete; mit
  MaxAlive 20 unter einer Wellengröße von 30 war die Welle nie vollständig gespawnt.
  Der Raid wurde nach 5,7 min verlassen. Raidende-Summe: 6 Tode, 0 Kills, 2594
  Schwünge, 178 gelandete Treffer.
- Keine Error-Zeilen. Flag nach dem Raid: `false`.

### Raid C (Wachhund-Korrektur)

**Fix (`39e193d`/`acd57ff`):** Der Wachhund schaltet sich scharf, sobald die Welle
gesättigt ist (alive + pending ≥ min(MaxAlive, Rest + alive)) oder komplett
gespawnt wurde. Die Uhr läuft ab Sättigung beziehungsweise ab dem letzten
Zombie-Tod. Beim Feuern bleibt die Welle in Phase Wave, wenn noch Rest zu spawnen
ist, und spawnt den Rest sofort weiter. Dazu 3 neue Tests, jeweils mit Fehlerkontrolle.

18:27–18:34, Build `acd57ff`, `OwnGear` an, 30 / +10 / 0,75 s.

| Zeit | Beobachtung / Beleg |
|---|---|
| bis 18:28:10 | 20 Infizierte gespawnt (gesättigt bei MaxAlive 20). |
| 18:29:50 | „wave 1 made no progress (no infected death) for 100s while saturated — administratively retiring“, „retired 20 infected“. Die restlichen 10 spawnten sofort (18:29:50–18:29:57), HUD „WAVE 1 \| 10 LEFT“. |
| 18:31:38 | Zweiter Wachhund, „retired 10“, Welle 1 beendet. „Arena own-gear refill complete“ und „Zombie refill after wave 1: own-gear entry loadout re-applied (epoch 1)“, Hydration 0 → 96. |
| danach | Welle 2 = 40 (HUD „WAVE 2 \| 20 LEFT“ bei 20 lebenden Infizierten). |
| 18:33:44 | Wachhund in Welle 2. |

Keine Error-Zeilen. Das Flag `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` wurde nach jedem Raid
live gelesen: `false` (für Raid C nach dem Verlassen nachgetragen: `false`).

### Profilprüfung

Das Serverprofil `6ab7db1c64f04b565cf78be9` wurde vor und nach dem Own-Gear-Raid
(Raid B) über die Freigabe kopiert und verglichen: Inventory, Skills, Quests,
Encyclopedia, TradersInfo, Health (ohne `UpdateTime`) und Experience (12267) sind
identisch, 340 Items, 0 geänderte Items. Zwischen den Raids des Tages liegen nur zwei
bekannte Serialisierungsunterschiede (Dogtag-Felder, `StackObjectsCount` am Scabbard).

### Grenzen und offene Punkte

- **Wellenende durch eigene Kills weiterhin nicht im Spiel gesehen.** Der Tester
  tötete nie alle Infizierten einer Welle; die in den drei Raids beendeten Wellen
  endeten über den Wachhund.
- **Nachfüllen nach Welle 2 nicht belegt.** Gesehen wurde nur das Nachfüllen nach
  Welle 1 (Raid A, Raid C). Dass es nach jeder Welle wieder greift (der Zweck von
  `1203438`), ist damit noch offen.
- **Own Gear nur mit Bajonett-Loadout geprüft** (siehe Raid B). Ein echtes Loadout mit
  Waffe, Munition und Verbrauchsgütern ist ungesehen.
- **Nachfüllen versetzt den Spieler.** Der Rundenwechsel-Pfad
  (`LiveKitRespawn.AtRoundBreak`) verlegt ihn an einen Spawnpunkt (Raid A, rund
  81 m). Gewünscht wäre ein Auffüllen an Ort und Stelle.
- **Konsumgüter im Secure Container werden beim Own-Gear-Nachfüllen nicht
  aufgefüllt** (Vertrag des `TeamArenaLoadoutSnapshot`).
- Schützenanteil 0 %; nur Factory; längster Raid 6 min; kein PitFireTeam.
- Die Subagenten signierten mehrere v1-Commits (`452d87a`..`cf100ab`) mit „Claude
  Sonnet 5“.

## Performance-Messung mit 56 gleichzeitigen Zombies (06.10.2026 20:07–20:17)

Beantwortet den offenen Punkt "Performance und Leichen bei MaxAlive 40–56"
(Nächste Schritte 3 oben; Checkliste Punkt 8 in `ZOMBIE-MODE-REVIEW.md`). Ein Raid
auf SPT-GPU-2 (AMD Ryzen 7 9800X3D, RTX 4070 Ti, 1920x1080, EFT 0.16.9.5), Build
`acd57ff` (Client-DLL wie Raid C oben, also v1 vor dem v2-Nachtrag), 20:07–20:17.
Die Zahlen stammen aus `uibridge_status` (300-Frame-Fenster) und aus den
30-s-Delta-Fenstern des ModProfiler-Overlays; die Einordnung am Ende ist eine
Auslegung dieser Zahlen, keine weitere Messung.

**Einstellungen auf dem Host.** Es liefen die eigenen Werte des Nutzers (per Sync),
nicht die Standardwerte 20 / 30 / +10 / 0,75 s / 0 %: MaxAlive 56, WaveStartSize 56
(für die Messung auf 80 gesetzt, danach zurück auf 56), +10 je Welle,
SpawnIntervalSeconds 0,2, ShooterPercent 20, OwnGear an (Nutzer-Loadout MP7A1),
Godmode an (der Spieler ist ein stehendes Ziel). ModProfiler-Overlay ab 20:09:19
offen, AutoExport alle 30 s (vom Nutzer bereits so gesetzt).

### Frame-Zeiten (`uibridge_status`, 300-Frame-Fenster)

| Zeitpunkt | lebende Zombies | fps | Mittel ms | p95 ms | RAM / managed |
|---|---|---|---|---|---|
| 20:07:41 (12 s nach Start, Zombies 13–18 m entfernt) | 56 | 72,6 | 13,8 | 18,7 | 10,0 GB / 1,46 GB |
| 20:08:03 (Zombies am Spieler, 0,6 m) | 56 | 55,5 | 18,0 | 21,2 | 10,4 GB / 1,60 GB |
| 20:08:40 | 56 | 62,2 | 16,1 | 17,9 | 11,0 GB / 1,84 GB |
| 20:12:5x (Profiler offen, Welle 2) | 56 | 58,2 | 17,2 | 18,1 | 14,4 GB / 2,39 GB |
| 20:15:5x (Profiler zu, nach Wachhund) | 20 | 119,7 | 8,4 | 8,3 | 14,4 GB / 2,42 GB |

Vergleich v1-Raid (20 Zombies, kein Profiler): 119,6 fps, 8,4 ms. Daraus: rund
0,25 ms je zusätzlichem Zombie am Spieler; 56 Zombies kosten etwa 9–10 ms mehr als
20.

### ModProfiler, 30-s-Delta-Fenster 20:12:19–20:12:49

56 lebende Zombies und rund 80 Leichen aus zwei Wachhund-Abräumungen; alle Werte
in ms pro Frame.

**Lesehinweis** (nach `LONG-RAID-REVIEW-2026-09-16.md`, "How the numbers were
obtained"):

- Die Zähler laufen nur, solange das Profiler-Fenster offen ist (hier ab 20:09:19);
  die Delta-Spalten zeigen das jeweils letzte 30-s-Intervall, nicht den Raid-Schnitt.
  Als Nenner zählen die gemessenen Frames (`WebOverlayPlugin.Update`), nicht die
  Aufrufsumme einer Mod.
- Die Instrumentierung kostet rund 0,17 µs je gemessenem Aufruf. Bei 116,6
  `Player.ComplexUpdate`-Aufrufen je Frame sind das etwa 0,02 ms der 1,30 ms dieser
  Zeile; Zeilen in der Nähe von 0,17 µs je Aufruf sind Messung, nicht Arbeit.
- Das Overlay selbst kostet Zeit; offen war es nur bei Stichprobe 4 der Frame-Tabelle
  (die Stichproben 1–3 liegen vor 20:09:19).

| Mod / Gruppe | ms/Frame | Aufschlüsselung |
|---|---|---|
| EFT (vanilla, instrumentiert) | 4,60 | `GameWorld.DoWorldTick` 1,70; `Player.ComplexUpdate` 1,30 bei 116,6 Aufrufen/Frame† (56 lebende); `BotOwner.UpdateManual` 0,88 (56 Aufrufe); `AICoreController.Update` 0,71 (56 Agenten) |
| DrakiaXYZ-BigBrain | 0,85 | `BotAgentUpdatePatch` 0,70 (ersetzt die native Agentenschleife, 56×); `BotBaseBrainUpdatePatch` 0,15 |
| SAIN | 0,67 | `WorldTickPatch` 0,35; `DirectionDataJob` 0,15; Rest Kleinkram je Bot (obwohl SAIN keinen Zombie steuert) |
| maschine-ArenaMode | 0,48 | `ArenaController.Update` 0,225 (alle Arena-Dienste); `MeleeAttackNodePatch` ~0,12 (Reverse-Patch-Körper inklusive NavMesh-Pfad); `BurstSpawnService.SpawnPair` 3 ms je Spawn (nur beim Spawnen) |
| PitAlex-PitFireTeam | 0,43 | drei Prefixe je Bot und Frame: `HostileNonCombatActiveLayerFilter` 0,26, `FollowerWeaponSelector` 0,065, `LootPatrol` 0,03 (fremder Mod, läuft für jeden Zombie mit) |
| DrakiaXYZ-Waypoints | 0,13 | `FindPath`-Prefix |
| übrige Zeilen | je < 0,06 | |
| **Summe instrumentiert** | **≈ 7,3** | von 17,2 ms |

† Korrigiert 2026-10-07: Diese Zeile las die 116,6 Aufrufe/Frame gegen 56
lebende Infizierte ursprünglich als "56 lebende + ~60 Leichen laufen noch
durch `ComplexUpdate`". Das ist falsch. Eine Leiche verlässt
`GameWorld.AllAlivePlayersList` einen Frame nach dem Tod
(`Player.OnDead` -> `OnDeadCoroutine` -> `GameWorld.UnregisterPlayer`), lange
bevor `CorpseCleanupService` sie überhaupt verbirgt oder trimmt, und wird
danach nie wieder getickt — unabhängig davon, wie lange die Freigabe dauert.
Die 116,6 Aufrufe sind 56 LEBENDE Körper, die je rund zweimal pro Frame
`ComplexUpdate` bekommen (einmal über `PlayerTick` für die eigene Warteschlange,
einmal über `DoOtherWorldTick` für die andere), nicht ein Aufruf je lebendem
Körper plus einer je Leiche. Siehe `ZOMBIE-MODE-REVIEW.md`, Abschnitt "v3
(2026-10-07): faster corpse release in Zombie mode", "Why (decompile
evidence)", für den vollständigen Dekompilat-Beleg der Korrektur.

Die größten Einzelzeilen, nach Kosten sortiert (µs je Aufruf abgeleitet aus ms ÷
Aufrufe je Frame, wo das Protokoll die Aufrufzahl nennt):

| Methode | Besitzer | ms/Frame | Aufrufe/Frame | µs/Aufruf |
|---|---|---|---|---|
| `GameWorld.DoWorldTick` | EFT | 1,70 | — | — |
| `Player.ComplexUpdate` | EFT | 1,30 | 116,6 | 11,1 |
| `BotOwner.UpdateManual` | EFT | 0,88 | 56 | 15,7 |
| `AICoreController.Update` | EFT | 0,71 | 56 Agenten | 12,7 |
| `BotAgentUpdatePatch` | BigBrain | 0,70 | 56 | 12,5 |
| `WorldTickPatch` | SAIN | 0,35 | — | — |
| `HostileNonCombatActiveLayerFilter` | PitFireTeam | 0,26 | 56 | 4,6 |
| `ArenaController.Update` | ArenaMode | 0,225 | — | — |
| `DirectionDataJob` | SAIN | 0,15 | — | — |
| `BotBaseBrainUpdatePatch` | BigBrain | 0,15 | — | — |
| `FindPath`-Prefix | Waypoints | 0,13 | — | — |
| `MeleeAttackNodePatch` | ArenaMode | ~0,12 | — | — |
| `FollowerWeaponSelector` | PitFireTeam | 0,065 | 56 | 1,2 |
| `LootPatrol` | PitFireTeam | 0,03 | 56 | 0,54 |

Die Summe der instrumentierten Zeilen beträgt ≈ 7,3 ms von 17,2 ms; der Rest (~10 ms)
ist nicht instrumentiert: Rendering und Animation von 56 Körpern plus Leichen,
Physik (CharacterController), Audio (56 Zombie-Stimmen alle 1–3 s) und Unity-Interna.

**Vorbehalt zur Summe (nicht gegen den CSV-Export nachgerechnet).** Die ≈ 7,3 ms
addieren die Zeilen so, wie sie notiert wurden. ModProfiler misst aber inklusiv, und
zwei Verschachtelungen lassen sich aus README und Quelltext ablesen:

1. `Player.ComplexUpdate` ist laut ModProfiler-README eine verschachtelte Zeile (sie
   steckt in `DoWorldTick` beziehungsweise `DoOtherWorldTick`) und darf nicht von Hand
   zu einer Summe addiert werden. Ob die 4,60 ms die Gruppensumme des Overlays sind
   (dann steht das hier nicht notierte `DoOtherWorldTick` für die 1,30) oder die
   Handsumme der vier genannten Zeilen (1,70 + 1,30 + 0,88 + 0,71 = 4,59), ist offen.
2. BigBrains `BotAgentUpdatePatch` ersetzt `AICoreAgent<BotLogicDecision>.Update`,
   das `AICoreController.Update` je Agent aufruft (`AICoreController.cs:53` im
   Dekompilat). Die 0,70 ms stecken damit sehr wahrscheinlich schon in den 0,71 ms der
   vanilla Zeile (beide bei 56 Aufrufen), und auch `MeleeAttackNodePatch` sowie die
   PitFireTeam-Prefixe laufen vermutlich innerhalb des Agenten-Updates.

Folge: Die ≈ 7,3 ms sind eine Obergrenze der instrumentierten Arbeit, die ~10 ms Rest
eine Untergrenze. Die Aussage, dass mehr als die Hälfte der Frame-Zeit außerhalb
dessen liegt, was das Profil sieht, ändert sich dadurch nicht.

### Heap

Heap 4,0 GB bei Raidminute 2,8 (Allokationsrate 6,45 MB/s in der Spawnphase) → die
GC-Schwelle (`GcThresholdMonitor`, Standard 4 GB) war gewappnet, das Umschalten
geschah beim nächsten Tod (Minute 4,2, 4,69 GB), danach 2,97 GB. Aus der
Frame-Tabelle (managed 1,46 GB um 20:07:41, 4,0 GB bei Minute 2,8, also gegen
20:10:17) ergäbe sich im Mittel gut 16 MB/s; die 6,45 MB/s gelten für die
Spawnphase. Beide Werte wurden nicht auf dieselbe Messgröße zurückgeführt.

### Beobachtungen

- Die Wachhund-Abräumung erzeugt 56 Leichen auf einmal; CorpseCleanup gibt nur 8 je
  5 s frei, sodass die Leichen bis zu ~35 s im Hintergrund liegen bleiben. Korrigiert
  2026-10-07: Sie laufen dabei NICHT durch `Player.ComplexUpdate` — `Player.OnDead`
  startet `OnDeadCoroutine` (EFT/Player.cs:30218), die einen Frame nach dem Tod
  `GameWorld.UnregisterPlayer` aufruft (EFT/Player.cs:30247) und die Leiche aus
  `AllAlivePlayersList` entfernt, bevor `CorpseCleanupService` sie überhaupt sieht;
  ihre Animatoren sind schon seit `Player.OnDead` aus. Was in den ~35 s tatsächlich
  kostet, ist das, was `CORPSE-CLEANUP-REVIEW.md` ("Why hiding was not enough") und
  der Klassenkommentar von `CorpseCleanupService.cs` am 17.09.2026 schon gemessen
  haben: eine vollständige, aktive Spieler-GameObject-Hierarchie je Leiche (Ragdoll
  bis zum Einschlafen, deaktivierte aber weiter registrierte Renderer) in der Szene,
  in `GameWorld.LootList` und in jeder Szenensuche.
- Mit 20 lebenden Zombies, geschlossenem Profiler und nach der Abräumung liegt die
  Frame-Zeit wieder bei 8,4 ms, exakt auf dem Wert des v1-Raids ohne Profiler.
- Keine Error-Zeilen im Raid.

### Einordnung

56 gleichzeitige Infizierte am Spieler kosten gegenüber 20 grob 9–10 ms je Frame,
also rund 0,25 ms je zusätzlichem Zombie (0,21–0,27 in den Stichproben 2–4, in denen
die Zombies am Spieler stehen, jeweils gegen die 8,4 ms der 20er-Referenz; 0,15 in
der ersten, als sie noch 13–18 m entfernt anliefen). Die 20er-Referenz liegt mit
119,7 fps bei p95 8,3 ms vermutlich am 120-fps-Limit; die Steigung ist dann eher eine
Untergrenze. Von den 17,2 ms entfallen laut Profil rund 7,3 ms auf instrumentierte
Methoden: ArenaMode 0,48 ms (2,8 %), BigBrain 0,85, SAIN 0,67, PitFireTeam 0,43,
Waypoints 0,13 und EFTs eigene instrumentierte Hooks 4,6 ms. Der Eigenanteil des
Modus ist damit klein; mehr als ein Viertel der Mod-Zeit (2,1 ms) gehört fremden Mods,
die jeden Bot anfassen, auch wenn sie keinen Zombie steuern. Der nicht instrumentierte
Rest (~10 ms, nach dem Vorbehalt eher mehr) ist Rendering, Animation, Physik und Audio
von 56 Körpern plus Leichen; er ist mit dem heutigen Werkzeug nicht aufzulösen, weil
es keine CPU/GPU-Trennung gibt. Die einzige klar benennbare, vom Modus selbst
verursachte Größe sind die toten Körper nach einer Wachhund-Abräumung — bis zu 60
Leichen, die als vollständige, aktive Spieler-Hierarchien ~35 s lang in der Szene
bleiben, NICHT in `Player.ComplexUpdate` (korrigiert 2026-10-07, siehe Fußnote
oben und ZOMBIE-MODE-REVIEW.md, Abschnitt "v3 (2026-10-07)"). Kandidatenliste mit
Nutzen und Risiko je Punkt: `ZOMBIE-MODE-REVIEW.md`, Abschnitt "Performance at 56
concurrent infected (measured 2026-10-06)".

Grenzen der Messung: ein Raid und ein 30-s-Fenster; stehender Spieler mit Godmode,
alle Zombies drängen am Spieler und laufen im Nahkampfpfad; 20 % Schützen
(`ShooterPercent` 20) und Nutzer-Gear sind Teil der Messung; das Profiler-Overlay war
nur bei Stichprobe 4 offen; die Leichenzahl wurde nur als "~80" im Delta-Fenster
geschätzt, nicht über die Zeit gemessen.

## v3-Messraids: schnellere Leichenfreigabe (07.10.2026 20:21–20:36, SPT-GPU-2)

Build `43cc7c3` (Client SHA256 `736A11CE89618A6A9EFE2435D43DBD48A5ED8FFA6ECEF3A02D024F448DC47E72`,
Server unverändert `0A826E8B…` aus `1e76582`), `uibridge_status` 24/24 identisch (VM-Build =
Host-Platte = geladen). Host-Settings (per Syncthing die F12-Werte des Nutzers): MaxAlive 56,
WaveStartSize 56, +10/Welle, Spawn 0,2 s, OwnGear an (Testprofil trägt nur ein Bajonett),
StuckWaveTimeout 100 s, MaxCorpses 10, ReleaseTrimmedCorpses an. Factory, Godmode über
`uibridge_hook`, Spieler schießt nicht. ModProfiler 1.4.0 kopflos (`Capture/Headless` per
`uibridge_config_set`, Export alle 30 s, Sitzung `20261007-201948`).

### Raid 1 (ShooterPercent 20, Start 20:21:20)

- Die Schützen-Zombies (PM-Pistole) töten per Querschläger andere Zombies: 55 → 36 lebende in
  sechs Minuten, Todesursache laut `uibridge_status` „PM (t), Head". Jeder Tod zeigt den
  Normalfall: Trim im nächsten 1-s-Sweep nach dem Ragdoll-Ende, Freigabe genau 2 s später
  (z. B. `trimmed 1 … released 0` um 20:24:00.916, `released 1` um 20:24:02.929), danach
  wieder „10 corpse(s) in the world". Sweep-Zeilen im 1-s-Takt (20:24:00.9, :02.9, :10.9,
  :13.0, :17.0, :19.0, :21.0).
- Wachhund 20:28:35.16 (100 s ohne Tod bei Sättigung): 36 Infizierte administrativ getötet,
  46 Leichen in der Welt. Abräumkurve: 20:28:36.28 `trimmed 10` · 37.28 `trimmed 25` ·
  38.28 `trimmed 1, released 2` · 39.28 `released 10` · 40.28 `released 24` → 46/46
  freigegeben, 10 Leichen in der Welt, `player bodies 2 created / 0 pooled`.
  **Abräumdauer 5,1 s.** Begrenzend war die 2-s-Ruhefrist je Körper (DamageSourceQuiet),
  nicht die Kappe von zwei Freigaben je Frame (bis zu 24 Freigaben in einer Sekunde).
- Danach Nachfüllen (`Arena own-gear refill complete`), Welle 2 (66) spawnt.

### Raid 2 (ShooterPercent 0, Start 20:31:41)

- 56 lebende am Spieler: 53,7 fps, 18,6 ms mean, p95 21,9 ms (`uibridge_status`); Profiler-
  Fenster 20:33:04–20:33:34: 1592 Frames / 30 s = 53 fps.
- Wachhund 20:33:35.83: 56 Infizierte administrativ getötet, 57 Leichen (56 + eine eigene;
  der Spieler war vor dem Godmode-Einschalten einmal gestorben). Abräumkurve:
  20:33:37.49 `trimmed 47` (alle Ragdolls nach 1,65 s ruhig) · 38.48 Sweep ohne Zeile
  (nichts fällig) · 39.48 `released 2` · 40.47 `released 45` → 47/47 freigegeben, 10 Leichen
  in der Welt, `player bodies 3 created / 0 pooled`. **Abräumdauer 4,6 s.**
- Nach der Abräumung (Welle 2 spawnt, 27 Bots registriert): 113,6 fps, 8,8 ms mean, p95
  10,6 ms; Profiler-Fenster 20:33:34–20:34:04: 2863 Frames = 95 fps.
- `uibridge_bots aliveOnly=false` listete nach der Abräumung nur noch die neuen Spawns:
  freigegebene Körper sind auch aus der Bot-Registratur verschwunden.

### Vorher/Nachher aus den Host-Logs

Alt (`acd57ff`, Host-Log `LogOutput-SPT-GPU-2-2026-10-06_20-05-51.log`): Wachhund 20:09:28.4
mit 56 → `trimmed 46` um 20:09:31.9 (5-s-Kadenz), dann `released 8` je 5 s (36.9, 41.9,
46.9, 51.9, 56.9) und `released 6` um 20:10:01.9 → 46/46 nach **33,5 s**.
Neu (`43cc7c3`): 36 Körper in **5,1 s**, 47 Körper in **4,6 s**.

### Was die Messung sonst zeigte

- `Player.ComplexUpdate` lief mit 56 lebenden und 0–2 Leichen 112,5–123,4-mal je Frame
  (`BotOwner.UpdateManual` 56,0): rund zwei Aufrufe je **lebendem** Körper (Update- und
  FixedUpdate-Queue-Pfad), tote Körper sind nicht dabei — `Player.OnDeadCoroutine`
  (EFT/Player.cs:30238–30249, gestartet in `OnDead`, :30218) ruft einen Frame nach dem Tod
  `GameWorld.UnregisterPlayer`. Die Lesart vom 06.10. („116,6 Aufrufe = 56 lebende + ~60
  Leichen", daraus „~0,65 ms je Frame für die Leichen") war falsch und ist in den
  Dokumenten korrigiert. Der Nutzen der schnelleren Freigabe ist der aus
  CORPSE-CLEANUP-REVIEW.md „Why hiding was not enough": weniger aktive Player-Objekte in der
  Szene (Szenensuchen, Culling, LootList), nicht ein Tick-Kostenanteil.
- Fehlerzeilen (Error) der ganzen Spielsitzung: 0. Keine Warnung „corpse cleanup drain
  failed", keine neue Sorte `ArenaDamageExceptionLog`-Meldung.
- `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` nach beiden Raids `false` (`uibridge_inspect`).
- Zwei **vorbestehende** Warnungen, beide schon im 06.10.-Log (also nicht durch v3):
  `Arena damage exception #n: site=Player.ApplyDamageInfo NullReferenceException …
  victim=SPT-GPU-2 human alive=False … release=no` (06.10.: 5×, 07.10. Raid 2: 6×) — NRE in
  `EFT.BotMemory.GoalTargetBeingHitAction`, wenn ein Zombie-Nahkampftreffer den
  sterbenden/toten Spieler trifft; unabhängig von freigegebenen Leichen (#1/#2 jeweils mit
  `release=no`). Und `Arena burst spawn: no profiles for infectedAssault — request dropped`
  (06.10.: 14×, 07.10. Welle 2: 6×) — der Profilvorrat für `infectedAssault` reicht in Welle 2
  (66) nicht. Beide offen.
- Godmode (`uibridge_hook`) gilt nur für den aktuellen Spielerkörper: nach einem Respawn ist
  er aus (Raid 2: 5 Tode trotz Godmode-Anzeige).
