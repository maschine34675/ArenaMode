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
