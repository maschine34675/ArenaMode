# Zombie-Modus: weitere Performance-Stellschrauben (Bewertung, 17.10.2026)

Zweiter Blick nach den Arbeiten vom 09.–15.10. (Hör-/Sozialfilter, Stimmbudget,
PFT-Schnellpfad, Crowd-Variante, Benchmarks `GPU-ZOMBIE-PERFORMANCE-20261013.md`,
`GPU-ZOMBIE-REVIEW-20261014.md`). Ziel des Nutzers: mehr gleichzeitige Zombies bei
P95 ≥ 60 FPS. Quellen: Dekompilat `D:\SPT41\Development\_decompiled` (EFT 0.16.9.5),
die Benchmark-Rohdaten in `artifacts\zombie-cap-20261013`, zwei Kontrollraids auf
SPT-GPU-2 (17.10., 16:02 und 16:12, Client `master` `3b569e6`) und die Referenz-Mod
`D:\SPT41\Development\_reference\Smoothie-main`.

**Lizenzlage Smoothie:** CC BY-NC-SA 4.0 (die mitgelieferte `LICENSE` nennt im Kopf
sogar ein anderes Projekt, BetterSuppressors/TominoCZ, ist also eine kopierte
Datei). Wie bei SAIN/ABPS gilt: nur Technikvorlage, kein Code übernehmen, keine DLL
bündeln. Smoothie als eigenständige Mod zu installieren ist davon unberührt.

## 1. Was die Kontrollraids heute ergaben

Beide Raids: Factory, MaxAlive 32, Welle 50 (+20), Spawn 0,2 s, 20 % Schützen,
Godmode. ModProfiler 1.4.0 kopflos mit Spitzenliste (`spikes` im Sidecar, je
Spitze `gc`-Flag und die drei teuersten Mod-Methoden).

- **Dauerlast bei 32 Zombies am Spieler, Spieler schießt (Raid 2, 16:13:43–16:14:13):**
  111 FPS, 9,0 ms/Frame. Instrumentierte Mods 2,72 ms: EFT-Hooks 1,53, BigBrain 0,41,
  ArenaMode 0,27, SAIN 0,24, PitFireTeam 0,15. Der Rest (≈ 6,3 ms) ist Engine
  (Animation, Skinning, Schatten, Physik, Audio) und nicht gehookter Spielcode.
  Zum Vergleich der Benchmark der Kollegen bei 32 (laufen, schießen, Teleports):
  P95 17,1 ms. Die Kampflast des Harness (Mündungsfeuer, Tracer, Killcam-Recorder,
  Bewegung) macht also den Unterschied zwischen 9 und 17 ms aus, nicht die Zombies
  allein.
- **GC-Schwelle ist unschuldig.** Heap 1,7 GB nach 1 min, 4,0 GB nach 4,8 min
  (≈ 13 MB/s Allokation in diesem Raid), Schalter um 16:08:58 „triggered by the
  bounded wait" bei 4,81 GB. Die Spitzenliste zeigt um diesen Zeitpunkt **keine**
  Spitze (größte im Fenster 40 ms): Unitys inkrementeller Sammler verteilt die Arbeit.
  Die Vermutung, die 0,85–1,88-s-Hänger der Benchmarks seien die GC-Umschaltung,
  ist damit widerlegt.
- **Wachhund.** Ohne Spielerkills feuert er wie entworfen alle ~100 s nach Sättigung
  (Raid 1: 16:03:58 32 Körper, 16:05:48 18, 16:07:45 32; Spitze dabei 74 ms, davon
  `ArenaController.Update` 56 ms). Mit laufenden Kills (Raid 2, 8 Kills in 2 min)
  feuerte er nicht: Kills zählen als Fortschritt, auch wenn der Nachschub die Zahl
  der Lebenden sofort wieder auffüllt. Die 100-s-Hänger der Benchmarks sind also
  ebenfalls nicht der Wachhund (dort wurde durchgehend getötet, und die Population
  blieb am Cap).
- **Die Benchmark-Hänger bleiben unerklärt.** In allen fünf Läufen vom 13.10. liegt
  der große Hänger (730–1708 ms) 102–105 s nach Messbeginn, unabhängig von
  MaxAlive, Welle und Kills; dazu je ein 730-ms-Frame beim Messbeginn selbst. In
  Raid 2 heute, mit Spitzenerfassung ab Raidstart, gab es im Kampf keinen Frame
  über 30 ms (16:12–16:14). Der feste Zeitabstand spricht für ein Harness-Ereignis
  (Teleport, Platzsuche, Bot-Abfrage) oder ein zeitgesteuertes Ereignis, das nur mit
  Dauerfeuer auftritt. **Empfehlung:** den Benchmark-Harness mit laufender
  ModProfiler-Spitzenerfassung fahren (Sidecar `spikes`, siehe
  `tools\gpu-run` `profiler-snapshot --out`) — die Liste nennt Frame, Uhrzeit,
  `gc`-Flag und die teuersten Methoden und beantwortet die Frage in einem Lauf.
- **Ladezeit, nicht Kampf:** beim Raidstart ein 4,3-s-Frame, davon 3,7 s
  `PitFireTeam.SessionLoadBotsEnglishVoicePatch.ApplyEnglishVoiceAsync`, 128 ms
  Waypoints `DoorLinkPatch`, 81 ms ABPS `BotsControllerInitPatch`. Fremde Mods, nur
  Ladebildschirm; für die Zielgröße irrelevant, aber der Vollständigkeit halber.

## 2. Kostenstruktur je Zombie

Gemessene Grenzkosten: 06.10. ≈ 0,25 ms je Zombie am Spieler (stehend), Benchmark
13.10. ≈ 0,37 ms je Zombie am P95 (Kampf). Instrumentiert sind davon nur ≈ 0,09 ms
(2,72 ms / 32). Zwei Drittel bis drei Viertel je Zombie liegen in Engine-Arbeit, die
ModProfiler nicht sieht. Dort sitzen die noch unbesetzten Hebel:

- **Animator.** `UseBodyFastAnimator` ist in dieser Installation `false`
  (`uibridge_inspect` auf `AppEnvironment.Config`), Zombie-Körper laufen also über
  den Unity-Animator. `Player.LateUpdate` setzt jeden Frame
  `BodyAnimatorCommon.cullingMode = AlwaysAnimate`, solange der Körper näher als
  `EFTHardSettings.AnimatorCullDistance` an der Kamera ist (Dekompilat
  `EFT\Player.cs:25910-25919`), und der Standard ist **10 m**
  (`EFTHardSettings.cs:470`). Die Nahkampfhorde steht genau in diesem Radius: 32–56
  voll animierte Körper, auch die hinter dem Spieler. Jenseits 10 m gilt bereits
  `CullUpdateTransforms`. IK/Prozedurales ist für Zombies schon aus
  (`UsedSimplifiedSkeleton`, `Player.cs:28069`, `VisualPass`).
- **Rendering.** Jeder Körper ein SkinnedMeshRenderer plus Schattenpass je Kaskade;
  bei 56 Körpern mehrere hundert Skin-Draws je Frame auf dem Hauptthread.
- **Physik.** Je Körper ≈ 30 kinematische Hit-Collider auf der Ebene `HitCollider`
  (`LayersMaskController.cs:98`), die sich jeden Frame bewegen; EFT simuliert
  die Physik einmal je gerendertem Frame, sobald irgendetwas in Bewegung ist
  (`PhysicsExtensions.cs:221-233`, `SmoothSimulate`), in einem Zombie-Raid also
  praktisch immer (Ragdolls). Bot-gegen-Bot-Kollision ist nativ bereits aus
  (`BotOwner.ExcludeCollisions`, `EFT\BotOwner.cs:621-628`).
- **KI/Managed.** BigBrain + AICore + `BotOwner.UpdateManual` ≈ 0,05–0,07 ms je
  Zombie; hier hat das Team schon gearbeitet (Hören, Sozialsuche, PFT-Pfad).

## 3. Kandidaten, geordnet nach erwartetem Nutzen je Risiko

Jeder Punkt ist als A/B-Schalter zu bauen (F12, Sektion Zombie, Standard aus) und
mit dem bestehenden Harness bei festem MaxAlive 32 auf Factory zu messen, mit
Spitzenerfassung. Alles nur im aktiven Arena-Zombie-Raid, fail-open.

1. **Animator-Culling innerhalb der 10 m.** Postfix auf `Player.LateUpdate` für
   Zombie-Körper (`IsAI && UsedSimplifiedSkeleton`): liegt der Körper außerhalb
   des Kamerakegels (Ebenentest gegen eine 2,5-m-Box, wie Smoothie
   `AnimatorThrottle.BotBox`), `cullingMode = CullUpdateTransforms` setzen; Unity
   überspringt dann Retargeting, IK und das Schreiben der Transforms, die
   Zustandsmaschine läuft weiter (keine Sprünge beim Wiedereintritt). Nativ
   passiert genau das ab 10 m, nur eben nicht davor. **Risiko:** der Messerkollider
   eines gecullten Zombies hinter dem Spieler bewegt sich nicht mit (Treffer von
   hinten könnten ausbleiben); deshalb nie cullen, wenn der Körper näher als
   ≈ 3 m ist oder sein Angriffs-Layer aktiv ist. Schattenwerfer außerhalb des
   Kegels zählen für Unity als sichtbar, daher Wirkung zusammen mit Punkt 2
   messen. Erwartung: die Hälfte der Horde ist meist nicht im Bild, Animatorzeit
   dafür fällt weg. Alternative mit mehr Aufwand: Smoothies Verfahren (Animator
   abschalten und mit aufgelaufener Zeit manuell `Animator.Update` stoppen,
   Kämpfende ≥ 30 Hz) — für die Nahhorde greift Smoothie selbst allerdings nicht,
   weil es Bots in Hörweite auf voller Rate hält.
2. **Schatten und Skinning außerhalb des Kegels / jenseits 15 m.**
   `shadowCastingMode = Off` und `SkinnedMeshRenderer.quality` eine Stufe tiefer
   für Zombie-Körper, die nicht im Bild oder weiter als 15 m sind. Das ist der
   Teil des Crowd-Versuchs vom 14.10., der wegen Besitzfehlern an gepoolter
   Ausrüstung zurückgenommen wurde (`ZOMBIE-CROWD-FIXES-20261014.md`). Der Fehler ist
   lösbar wie bei Smoothie `HandsCulling`: nur eigene Änderungen je Renderer
   merken und in einem Prefix auf `AssetPoolObject.ReturnToPool` sowie beim Tod
   zurücksetzen, nie den Pool-Zustand selbst anfassen. Erst messen, ob die
   Renderseite auf dem 4070 Ti überhaupt zählt (das Spiel ist CPU-gebunden; die
   Draw-Submission liegt dennoch auf dem Hauptthread).
3. **Hit-Collider gegen Trigger-Volumen.** Smoothies Befund (dort Quelltext-geprüft
   für 0.16.9.5): die Hit-Zonen aller Körper kollidieren in EFTs Matrix nur mit der
   Ebene `Triggers`, und kein Spielskript wertet diese Unity-Trigger-Ereignisse aus
   (EFT prüft Trigger per eigener Overlap-Suche je Spieler). 56 Körper × 30 Hit-
   Zonen = 1.680 bewegte Kollider, die PhysX jeden Schritt gegen jede Trigger-Box
   testet, in der sie stecken. Arena-Zombie-Raid: `Physics.IgnoreLayerCollision(
   HitColliderLayer, TriggersLayer, true)` beim Start, Rücknahme am Ende. **Vorher
   prüfen**, dass keine eigene Logik (Pickups, Labyrinth-Fallen, `CorpseTriggerZones`,
   Area-Boxen) `OnTriggerEnter` mit Hit-Collidern erwartet; ein Scan aller geladenen
   Assemblies nach solchen Listenern ist Smoothies Absicherung.
4. **Tote KI-Agenten bleiben in der Löschliste.** `AICoreController._listToDel`
   (HashSet) wird in `Update` jeden Frame durchlaufen und jedes Element aus
   `_listAgents` entfernt, aber nie geleert (`AICoreController.cs:16,28,42-47`;
   `Clear()` leert nur `_listAgents`/`_listOfErrors`). Ein Zombie-Raid mit hunderten
   Toten zahlt pro Frame `Tote × List.Remove(Lebende)` und hält jedes tote Gehirn
   samt BotOwner im Speicher. Fix: Prefix, der nach dem Entfernen die Liste leert
   (Arena-Raids). Klein, sicher, offline prüfbar.
5. **Eine Coroutine je Bot je Frame.** `Player.LateUpdate` startet für jeden
   lebenden Spieler `ProceduralWeaponAnimation.StartFovCoroutine` (`Player.cs:25979`,
   innerhalb `IsAlive`); für Bots tut die Drittperson-Strategie nichts außer
   `RibcageScaleCurrent = 1` und einem Rücksetzer auf einen ohnehin passenden Wert
   (`ThirdPersonStrategy.cs:50-62`). Bei 56 Zombies sind das 56 Coroutinen-
   Allokationen je Frame (ein Teil der 13 MB/s). Fix: Prefix für `IsAI` im
   Arena-Raid, der den Rücksetzer direkt aufruft und die Coroutine überspringt.
6. **Allokationsrate senken.** 1,7 → 4,0 GB managed in drei Minuten bei 32 Zombies
   heißt ≈ 13 MB/s; jedes Megabyte verlängert später die inkrementelle Sammlung.
   Punkt 5 ist eine bekannte Quelle; weitere lassen sich mit ModProfilers
   `growing`-Abschnitt und `GC.GetTotalMemory`-Deltas je Subsystem eingrenzen.
   Zusätzlich: die Zombie-Pause zwischen den Wellen als bevorzugten Sammelmoment
   an `GcThresholdMonitor` melden (heute: nur Spielertod/Kit-Wechsel, sonst der
   120-s-Zwang), auch wenn der Schalter selbst keine Spitze erzeugt.
7. **KI-Takt für ferne Zombies halbieren** (BigBrain-Agentenschleife je zweiten
   Frame ab 20 m). Höchstens ≈ 0,5–0,8 ms bei 56, Verträglichkeit mit BigBrains
   Prefix ungeklärt. Erst nach 1–3.
8. **Physikschritt auf 60 Hz kappen** (Smoothie `CapPhysicsRate`): spart nur, wenn
   das Spiel ohnehin über 60 FPS liegt; für das P95-Ziel zweitrangig.
9. **Spawn-/Wellenwechsel-Spitzen.** Heute 162 ms bei Item-Erzeugung (16:08:12) und
   74 ms bei der Wachhund-Abräumung; Smoothies `ProfileBudget` (Profile je Frame in
   einem 3-ms-Budget bauen) ist die passende Technik, falls die Spitzen beim
   Wellenwechsel in den Harness-Daten auffallen.
10. **Smoothie als Mod installieren und messen.** Seine Gewinne liegen auf
   Kartenebene (Occlusion, Lampen, LOD, AmbientLight-Puffer, Mündungsfeuer-Schatten
   — der Benchmark schießt viel) und schaffen damit Luft für Zombies, ohne deren
   Einzelkosten zu senken. Zu prüfende Berührungspunkte: Postfix auf
   `Corpse.CheckCorpseIsStill` (Ragdoll nach 5 s „still" → hilft unserem Trim, ändert
   aber die Fallzeit), Hooks an `PerfectCullingCrossSceneGroup` (Redeploy.Core räumt
   dort Sampler auf), Prefixe an `Player.LateUpdate`/`ArmsUpdate`, Postfix an
   `MovementContext.ManualUpdate`. Ein Harness-Lauf bei 32 mit und ohne Smoothie
   beantwortet den Nutzen; die Lizenz steht dem nicht im Weg.

Nicht weiterverfolgen: SAIN-globale Kosten (kein sicherer Schnitt, siehe
`ZOMBIE-SAIN-GLOBAL-REVIEW.md`), Crowd-Visualeingriffe in ihrer alten Form,
Stimmen (Budget vorhanden), Bot-gegen-Bot-Kollision (nativ schon aus).

## 4. Messrezept

1. Harness `tools\ZombiePerformanceChecks` bei MaxAlive 32, Factory, gespeicherte
   Route, **mit** `com.maschine.ModProfiler|Capture|Headless=true` ab Raidstart
   (`config-pin`, Export 30 s), danach `profiler-snapshot --out` je Export; die
   `spikes` des Sidecars den Hänger-Zeitpunkten des Rings zuordnen.
2. Je Kandidat ein Schalter, OFF/ON im selben Prozess, gleiche Route; Zielgröße
   P95 ≤ 16,67 ms wie bisher, dazu P99 und die Spitzenliste.
3. Reihenfolge: 4 und 5 (klein, sicher) zusammen als Grundlage, dann 1, dann 2,
   dann 3; 10 unabhängig davon als externer Vergleichslauf.

## 5. Aufräumzustand

Beide Raids verlassen, Spiel im Hauptmenü; `Zombie/OwnGear` für Raid 2 auf `false`
gestellt und auf den Nutzerwert `true` zurückgesetzt; `Capture/Headless` wieder
`false`; keine Code- oder Config-Änderung im Repo.
