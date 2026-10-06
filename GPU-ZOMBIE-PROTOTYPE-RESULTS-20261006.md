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
