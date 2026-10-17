# Zombie-Modus: vier Schalter gegen Frame-Kosten je Bot (17.10.2026)

Umsetzung der Kandidaten 4, 5, 1 und 2 aus
[ZOMBIE-PERFORMANCE-IDEAS-20261017.md](ZOMBIE-PERFORMANCE-IDEAS-20261017.md).
Alle vier sind F12-Schalter, gelten nur im aktiven Arena-Raid und fallen bei
jeder Ausnahme auf das native Verhalten zurück. Normale Raids bleiben
bit-identisch (Gate `ArenaSession.IsActive`, die Zombie-Schalter zusätzlich
auf den exakten gestarteten Zombie-Raid und auf generische Infizierte).

| Schalter | Sektion | Standard | Wirkung |
|---|---|---|---|
| `TrimDeadBrainList` | Performance | an | Löschliste toter KI-Gehirne nach dem Abarbeiten leeren |
| `SkipBotFovCoroutine` | Performance | an | FOV-Coroutine je Bot je Frame durch ihre zwei Anweisungen ersetzen |
| `CullOffscreenAnimators` | Zombie | aus (bis gemessen) | Körper-Animator der Nahhorde Unitys Renderer-Culling überlassen |
| `HideOffscreenShadows` | Zombie | aus (bis gemessen) | Schatten infizierter Körper außerhalb des Bildes abschalten |

Code: `Performance/BotHotPathPolicy.cs` + `Performance/ArenaBotHotPaths.cs`,
`Bots/ZombieBodyCullingPolicy.cs` + `Bots/ZombieBodyCullingRuntime.cs`; die
reinen Policies sind in `ArenaMode.Tests` gelinkt (13 Tests; zwei Mutanten,
„Liste nicht geleert" und „Frustum-Reichweite invertiert", lassen sie rot
werden).

## 1. Löschliste toter Gehirne (`TrimDeadBrainList`)

Nativ: `AICoreController.Update` (Dekompilat `AICoreController.cs:37-61`)
läuft jeden Frame über `_listToDel` und entfernt jeden Eintrag aus
`_listAgents`, leert `_listToDel` aber nie; `Clear()` (:75-79) leert nur
Agenten- und Fehlerliste. Ein Agent landet genau einmal in der Löschliste,
durch sein eigenes `AICoreAgent.Dispose` (`AICoreAgent.cs:135-137`), nachdem
sein Konstruktor ihn angemeldet hat (:45); niemand meldet einen disposten
Agenten erneut an (alle Aufrufer von `AddNodeController`/`RemoveNodeController`
geprüft: nur `AICoreAgent`). Nach der ersten Anwendung ist jeder weitere
Durchlauf ein Leerlauf, der `Tote × HashSet.Remove` je Frame kostet und jedes
tote Gehirn samt `BotOwner` bis zum Raidende erreichbar hält.

Arena: Prefix auf `AICoreController.Update`, der die Entfernung selbst
anwendet und die Liste leert, solange `_enable` gesetzt ist (bei `Stop()`
bleibt die Liste wie nativ liegen, bis `Activate()` kommt). `_listAgents` ist
danach exakt so, wie der native Durchlauf es hinterließe, eine Anweisung
früher im selben Frame. Prefix statt Postfix, weil Agenten sich innerhalb der
nativen Agentenschleife zur Löschung anmelden.

## 2. FOV-Coroutine je Bot (`SkipBotFovCoroutine`)

Nativ: `Player.LateUpdate` startet für jeden lebenden Körper jeden Frame
`ProceduralWeaponAnimation.StartFovCoroutine(this)` (`EFT/Player.cs:25979`);
die Coroutine (`ProceduralWeaponAnimation.cs:2405-2410`) ruft
`_strategy.ApplyFovAdjustments`, wartet auf das Frameende und ruft
`_strategy.ResetFovAdjustments`. Für die Drittpersonstrategie (jeder Bot)
heißt das `RibcageScaleCurrent = 1` und, nur wenn die Brustkorbskalierung von
1 abweicht, Rücksetzen von Brustkorb und Händehierarchie auf `Vector3.one`
(`ThirdPersonStrategy.cs:50-62`). Zwischen beiden Punkten liest für einen
Bot nichts diese Werte.

Arena: Prefix auf `StartFovCoroutine`, der für `player.IsAI` unter
`ThirdPersonStrategy` (Feld `_strategy` per Reflection, fehlt es, bleibt der
Schalter wirkungslos und meldet das einmal) **und nur bei Brustkorbskalierung
1** die Zuweisung `RibcageScaleCurrent = 1` direkt ausführt und die Coroutine
überspringt. Die Einschränkung ist der Beweis der Gleichheit: der native
Rücksetzer läuft erst nach dem Rendern und tut bei Skalierung 1 nichts; eine
abweichende Skalierung behält die native Coroutine, damit der Rücksetzer
hinter dem Rendern bleibt. Ein Iterator und ein Coroutine-Objekt weniger je
Bot je Frame. Der eigene Körper behält seine Coroutine.

## 3. Animator-Culling der Nahhorde (`CullOffscreenAnimators`)

Nativ: `Player.VisualPass` (`EFT/Player.cs:25910-25919`) setzt für jeden
lebenden Körper jeden Frame `BodyAnimatorCommon.cullingMode`: näher als
`EFTHardSettings.AnimatorCullDistance` (10 m, `EFTHardSettings.cs:470`)
`AlwaysAnimate`, sonst `CullUpdateTransforms`. Bei `CullUpdateTransforms`
überspringt Unity Retargeting und das Schreiben der Knochen, solange kein
Renderer des Körpers für irgendeine Kamera sichtbar ist (Schattenpässe
eingeschlossen, `Renderer.isVisible`); Zustandsmaschine und Root-Motion
laufen weiter, der Körper geht also weiter. Der Offline-Bot-Culler hält jeden
Körper innerhalb von `CULLING_PLAYER_DISTANCE` (30 m) unabhängig vom
Blickfeld gerendert (`ObservedCullingManager.cs:31-44`), die Nahhorde steht
also in jedem Schattenpass, auch hinter dem Spieler, und ist innerhalb von
10 m voll animiert.

Der Nahkampftreffer eines Infizierten hängt doppelt am Körper-Animator: für
einen KI-Besitzer nimmt `KnifeCollider.ManualUpdate` den Pfad
`ManualUpdateServerAI` (`EFT/KnifeCollider.cs:76-88, 156-168`), eine
Overlap-Box vom Skelettknochen `Owner.WeaponRoot` entlang der Blickrichtung,
und zwar nur, solange `OnHit` gesetzt ist; dieses Fenster öffnen und schließen
die Animationsereignisse des Körper-Animators (`OnZombieFireBullet`/
`OnZombieFireEnd`, `EFT/Player.cs:17776-17777, 18191-18218`) während des
Kicks, den `MakeKnifeKick` bei vereinfachtem Skelett auf dem Körper-Animator
auslöst (`EFT/Player.cs:18590-18609`). Der Versuch setzt
`GoalEnemy.Distance < DIST_TO_HIT_MELEE` voraus (`BotMeleeWeaponData.cs:151-168`,
Standard 2 m, `BotGlobalShootData.cs:157`); der Nahradius ist das Doppelte,
ein Körper ist also schon vor seinem ersten Schlagframe ausgenommen. Dass
`CullUpdateTransforms` Root-Motion und Ereignisse weiterlaufen lässt, ist
Unitys dokumentiertes Verhalten und Vanillas Präzedenz jenseits 10 m, nicht
aus dem Dekompilat beweisbar; der GPU-Lauf prüft, dass gecullte Körper
weiter heranlaufen. Hinzunehmender Tausch (wie nativ jenseits 10 m): ein
ungesehener Körper behält seine zuletzt geschriebene Pose samt Trefferzonen,
bis er wieder gesehen wird; eine ungesehen erzeugte Leiche fällt aus dieser
Pose.

Arena: Postfix auf `Player.VisualPass`. Für einen generischen Infizierten
(`IsAI`, `UsedSimplifiedSkeleton`, Rolle infectedAssault/Civil/Laborant/Pmc,
kein Follower, nicht der eigene Körper) innerhalb der 10 m wird
`CullUpdateTransforms` gesetzt, **außer** der Körper ist näher als der
Nahradius (2 × `DIST_TO_HIT_MELEE`, mindestens 4 m) am Hauptspieler oder an
seinem `GoalEnemy`, oder er schlägt gerade (`KnifeController.CurrentOperation
is Kick`). Zustandslos: der native Aufruf schreibt den Modus jeden Frame neu,
der Schalter wirkt also nur, solange seine Bedingung gilt; den einen Pfad, auf
dem VisualPass früh zurückkehrt (`CustomAnimationsAreProcessing`), nimmt der
Code ausdrücklich zurück. Nützt nur zusammen mit Schalter 4: solange der
Körper Schatten wirft, gilt er als sichtbar.

## 4. Schatten außerhalb des Bildes (`HideOffscreenShadows`)

Für einen generischen Infizierten, der seit 0,3 s außerhalb des um 4 × 3 × 4 m
gepolsterten Kamerakegels liegt, setzt der Postfix jeden Renderer aus
`PlayerBody.GetRenderersNonAlloc` (Körperhäute, Kleidung, Ausrüstung,
`EFT/PlayerBody.cs:804-835`) **und** des Gegenstands in den Händen
(`HandsController.ControllerGameObject`; `SlotView` lässt ihn aus, sein
Renderer hängt unter dem Körper-Animator und hielte ihn allein sichtbar) auf
`shadowCastingMode = Off`. „Außerhalb" heißt: weder der Körper noch das Ende
seines Schattens entlang des Himmelslichts (`TOD_Sky`, Sonne oder Mond, bis
15 m über den Boden) noch der Punkt dazwischen schneidet den Kegel; ein
Schatten, der von hinten ins Bild fällt, bleibt also. Schatten lokaler Lampen
werden nicht vorhergesagt und können mit ihrem Werfer verschwinden; bei einem
harten Kameraschnitt (Zuschauerwechsel, Killcam-Start) kann ein Frame ohne die
Schatten der Körper nahe der neuen Sicht erscheinen. Während ein
vergrößerndes Visier rendert, bleibt alles vor der Kamera unverändert (dessen
Kamerakegel wird nicht verfolgt).

Besitz und Rücknahme, die Lehre aus dem zurückgenommenen Crowd-Versuch
([GPU-ZOMBIE-REVIEW-20261014.md](GPU-ZOMBIE-REVIEW-20261014.md), P1/P2):

- Gemerkt wird nur die eigene Änderung, je Renderer mit dem ersetzten Modus;
  ein Renderer, den jemand anders inzwischen verändert hat, bleibt wie er ist.
- Zurückgesetzt wird, sobald der Körper wieder im Bild ist, wenn er stirbt
  oder aus der Zulassung fällt (ein Frame ohne VisualPass genügt, die
  Prüfung läuft einmal je Frame), wenn der Schalter aus ist, am Raidende und
  **vor jeder Rückgabe an den Pool**: Kleidungs-, Ausrüstungs- und
  Handmodelle sind gepoolt. Nativ setzt nur `IDress.Deinit` den Schattenmodus
  zurück, und das nur für Kleidung eines Modells mit `ContainerCollectionView`
  (Rig, Rucksack: `AssetPoolObject.ReturnToPool` → `DisposeContainerCollectionView`
  → `ContainerCollectionView.Dispose` → `Deinit` → `AdjustShadowMode`,
  `ContainerCollectionView.cs:44-50, 195-201`, `Dress.cs:55-58`); Waffen- und
  sonstige Slot-Modelle bekommen keine Rücksetzung
  (`SlotView.DestroyCurrentModel` gibt direkt zurück, `PlayerBody.cs:305-326`).
  Deshalb stellt ein Prefix auf `AssetPoolObject.ReturnToPool()` alle eigenen
  Änderungen unter dem zurückgehenden Objekt (und seinen gepoolten Eltern)
  wieder her, bevor der Pool es sieht, und der Schalter verdunkelt nur, solange
  dieser Prefix installiert ist. Körperhäute (`LoddedSkin`) sind nicht gepoolt,
  sondern werden mit dem Körper zerstört (`PlayerBody.cs:735-760, 890-905`).
- Nachgeladene Ausrüstung: solange ein Körper ohne Schatten ist, wird seine
  Rendererliste jede Sekunde neu gelesen.

## 5. Gates und Fehlerpfade

- Performance-Schalter: `ArenaSession.IsActive` + Schalterwert; jede Ausnahme
  → nativer Pfad (`return true` im Prefix) und eine Warnung je Prozess.
- Zombie-Schalter: einmal je Frame `ZombieNativeEventBridge.IsActiveZombieRaid`
  und `ArenaSession.IsExactActiveRaid(game, world)` mit `GameStatus.Started`;
  fällt das weg oder sind beide Schalter aus, werden alle eigenen Änderungen
  zurückgenommen. Zulassung je Körper wird erst entschieden, wenn sein
  `BotOwner` da ist, und dann gecacht (Rolle, Skelett, Follower sind fix).
- Diagnose: eine Debug-Zeile je Raid beim ersten Eingriff, danach alle 30 s
  (Zombie) bzw. 60 s (Performance) aggregierte Zähler (vermiedene Arbeit, keine
  Frame-Zeit).

## 6. Messrezept

A/B-Lauf innerhalb eines Raids (SPT-GPU-2, Factory, Solo, eigenes Gear mit
Bajonett, Godmode, kein Schuss, kein Kill): die Schalter werden per
`uibridge_config_set` live umgeschaltet, Phasen zu je 90 s, in jeder Phase
alle 25 s ein Teleport auf den nächsten von sechs Spawnpunkten (≥ 24 m
Abstand), damit die Horde aus allen Richtungen heranläuft (Körper 4–10 m
hinter der Kamera sind der Fall des Animator-Schalters). Alle 5 s
`uibridge_status` (300-Frame-Fenster: Mittel, P95, Max) und `uibridge_player`
(Lebende, nächste Abstände); dazu ModProfiler 1.4.0 kopflos mit Exports alle
30 s (Frames/Sekunden je Intervall, ms/Frame je Mod, Spitzenliste). Skript
`zombie-ab.mjs`, Auswertung `ab_analyze.py` (Sitzungs-Scratchpad; Rohdaten
`run1.jsonl`, `run3.jsonl`, Exportordner `20261017-202636`).

Zwei Vorläufe lehrten das Rezept: Lauf 1 (MaxAlive 32) wurde dreimal vom
Wellen-Wachhund abgeräumt (ohne Spielerkills feuert er nach 100 s Sättigung,
die Bot-Zahl fiel von 32 auf 11), Lauf 2 starb der Spieler vor dem Godmode.
Lauf 3 lief mit `StuckWaveTimeoutSeconds = 0` (per Ledger gepinnt, 0 = aus)
und MaxAlive 56 bei Wellengröße 50, also konstant 50 lebende Infizierte ohne
Nachspawn, unterhalb der 120-fps-Kappe des Hosts.

## 7. Ergebnis GPU-2 (Lauf 3, 20:45–20:57, Client `cd2ede5`, 50 Infizierte)

| Phase | Mittel ms | P95 (Median der Fenster) | fps | Lebende | Exports ms/Frame | ArenaMode ms/Frame |
|---|---|---|---|---|---|---|
| Basis (nur Perf-Schalter) | 11,73 | 13,14 | 85,9 | 49,9 | 11,86 · 11,48 · 11,25 | 0,37 · 0,32 · 0,33 |
| Basis | 12,31 | 14,11 | 81,9 | 50,0 | 12,93 · 12,74 · 11,79 | 0,54 · 0,44 · 0,41 |
| Animator | 12,88 | 14,57 | 77,9 | 50,0 | 13,44 · 12,51 · 13,12 | 0,81 · 0,40 · 0,61 |
| Schatten | 12,61 | 14,37 | 79,6 | 50,0 | 13,41 · 12,39 | 0,56 · 0,48 |
| beide | 13,28 | 15,18 | 76,0 | 48,9 | 13,77 · 12,03 | 0,55 · 0,35 |
| Basis | 13,64 | 15,56 | 73,6 | 48,0 | 13,35 · 13,46 | 0,52 · 0,52 |
| beide | 13,21 | 13,95 | 76,3 | 46,5 | 13,89 · 12,81 | 0,57 · 0,43 |

Die Schalter griffen nachweislich (Debug-Zähler je 30 s): Animator-Culling
bis 31 Körper je Frame in der Spitze, im Mittel ≈ 12 von 50; Schatten bis 50
von 50 Körpern aus; Rücknahmen beim Pool-Return 0 (keine Ausrüstungsrückgabe
ohne Tote) bzw. 4 und 17 in Lauf 1 (Wachhund-Abräumung). Keine Warnung, kein
Fehler aus den neuen Patches in drei Raids.

**Befund: kein messbarer Gewinn.** Die Frame-Zeit steigt über den Lauf
unabhängig vom Schalter (Basis 11,7 → 12,3 → 13,6 ms bei konstant 50
Lebenden), und die Schalterphasen liegen innerhalb dieses Drifts (ABAB-Wechsel
ohne Sprung). ArenaModes eigener Anteil wächst mit den Schaltern um ≈ 0,1–0,3
ms (die Postfix-Arbeit je Körper je Frame). Deutung: auf diesem Host (RTX 4070
Ti, CPU-gebunden) ist weder die Animator-Auswertung der Nahhorde noch der
Schattenpass der begrenzende Posten; Factory hat zudem kaum Sonnenschatten.
Die Engine-Kosten je Zombie liegen demnach in Physik (Hit-Collider,
Ragdolls), KI und dem Spielcode, nicht in Animation und Schatten. Beide
Zombie-Schalter bleiben **aus** als dokumentierte Messschalter; ein Lauf auf
einer Außenkarte mit tiefer Sonne (Customs-Areal) stünde noch aus, bevor man
sie streicht.

Die Performance-Schalter liefen in allen Phasen mit (≈ 200.000 übersprungene
Coroutinen je Minute bei 32 Bots, 1–6 geleerte Löscheinträge je Minute); ihr
Nutzen ist Allokationsvermeidung und Speicherfreigabe, nicht Frame-Zeit, und
war in diesem Aufbau nicht isoliert messbar. Sie bleiben an (verhaltensgleich,
siehe Abschnitte 1–2).

**Nebenbefunde aus der Spitzenliste (Sidecar `spikes`):**

- **Der ~100-s-Hänger ist die Profil-Deserialisierung.** Zweimal reproduziert,
  je ≈ 90 s nach Raidstart: 625 ms (20:30:07) und 627 ms (20:47:14), oben
  jeweils `ItemDeserializer.CreateItem` (über UIFixes' Postfix sichtbar), kein
  GC. Das ist der Stapel neuer Bot-Profile, den der Server für die weiteren
  Spawns liefert und den der Client in einem Frame zu Items baut. Damit ist
  der unerklärte Hänger der Benchmarks vom 13.10. (730–1708 ms bei 102–105 s)
  zugeordnet; die passende Technik ist Kandidat 9 der Bewertung (Profile je
  Frame unter einem Zeitbudget bauen, wie Smoothie `ProfileBudget`).
- **Synchrone Nahkampf-Pfadsuche.** Beim Verlassen des Raids (20:57:32–34)
  zehn Frames in Folge mit 130–180 ms, davon 101–111 ms in
  `AICoreController.Update` → BigBrain → `MeleeAttackNodePatch.Prefix`
  (der native `RunToEnemyUpdate`-Körper mit `CanRunToEnemyToHit`): alle 50
  Infizierten berechneten im selben Frame ihren Pfad zum plötzlich
  unerreichbaren Ziel neu. Im Spiel entspricht das einem Spieler, der für die
  Horde unerreichbar wird (Vorsprung, Leiter). Noch nicht angefasst; ein
  Kandidat wäre, die Pfadneuberechnungen je Frame zu deckeln.
- Wachhund-Abräumung von 32 Infizierten: ein Frame mit 207 ms
  (`ArenaController.Update` 358 ms inklusiv, davon `RespawnEngine.RunRespawn`
  172 ms); PitFireTeams `FollowerWeaponSelectorManualUpdatePatch` einmal
  98 ms in einem Frame; der Raidstart kostet weiterhin 3,7–4,1 s in
  PitFireTeams englischer Stimm-Nachladung (nur Ladebildschirm).
- Die Harness-Aufrufe selbst (`uibridge_status`, `config_set`, Teleport)
  erzeugen einzelne 55–90-ms-Frames (`UiBridge Plugin.Update`); sie liegen in
  allen Phasen gleich und fallen aus den 30-s-Mittelwerten heraus.

### 7b. Referenz SPT-GPU-1 (21:27–21:38, gleicher Build, 50 Infizierte)

Gleiches Rezept auf dem langsameren Host (Wachhund per Ledger aus, MaxAlive 56,
Welle 50, Godmode, Teleport alle 25 s). Dort kostet der Frame rund viermal so
viel, und der Befund ist derselbe:

| Phase | Mittel ms | P95 (Median) | fps | Exports ms/Frame | ArenaMode ms/Frame | Mods gesamt ms/Frame |
|---|---|---|---|---|---|---|
| Basis (Aufwärmen) | 58,1 | 55,0 | 18,9 | 47,8 | 1,56 | 18,7 |
| Basis | 45,9 | 54,7 | 21,9 | 45,2 · 44,3 | 1,17 · 1,83 | 16,1 · 18,4 |
| Animator | 47,9 | 57,3 | 21,0 | 46,9 · 50,6 | 2,57 · 2,13 | 22,0 · 24,1 |
| Schatten | 49,4 | 65,1 | 20,3 | 47,9 · 50,9 | 2,18 · 2,97 | 21,0 · 24,8 |
| beide | 45,1 | 53,6 | 22,2 | 43,8 · 44,9 · 47,2 | 1,45 · 1,53 · 2,84 | 16,5 · 17,6 · 23,5 |
| Basis | 45,2 | 53,4 | 22,2 | 45,7 · 45,9 · 43,4 | 2,35 · 1,17 · 1,08 | 21,2 · 17,8 · 15,7 |
| beide | 48,3 | 63,4 | 20,8 | 49,7 · 46,6 | 3,00 · 2,47 | 25,3 · 21,2 |

Kein Gewinn, eher ein leichter Verlust in den Schalterphasen; die
instrumentierten Mods wachsen dort von 16–18 auf 21–25 ms je Frame, weil die
Postfix-Arbeit je Körper auf der langsamen CPU sichtbar wird
(`VisualPassPatch.Postfix` 0,13 ms je Frame bei 50 Körpern). Wo die Zeit auf
Host 1 liegt (letzter Export, ms je Frame): `GameWorld.DoWorldTick` 5,5,
`Player.ComplexUpdate` 4,1, `AICoreController.Update` 3,1 (davon BigBrain 3,0
und `BotOwner.UpdateManual` 2,6), ArenaModes `MeleeAttackNodePatch.Prefix` 0,9
(der native `RunToEnemyUpdate`-Körper, 9 Aufrufe je Frame à ≈ 100 µs). Der
Profil-Stapel bei ≈ 90 s kostete hier **1763 ms** in einem Frame (21:28:34,
wieder `ItemDeserializer.CreateItem` obenauf); siehe
[PROFILE-BUILD-BUDGET-20261017.md](PROFILE-BUILD-BUDGET-20261017.md).
Harness-Aufrufe erzeugen auf Host 1 Frames von 150–210 ms (`UiBridge
Plugin.Update`), in allen Phasen gleich.

## 8. Aufräumzustand

Alle drei Raids auf Host 2 und der Referenzraid auf Host 1 verlassen; `CullOffscreenAnimators`, `HideOffscreenShadows`
auf `false`, `MaxAlive` wieder 32, `StuckWaveTimeoutSeconds` per Ledger auf
100 und `Capture/Headless` per Ledger auf `false` zurückgestellt
(Abschluss-Export geschrieben). Zwischenfall beim Deploy: die erste Kopie des
Builds landete im alten Wurzelpfad `plugins\maschine-ArenaMode.Client.dll`,
während eine andere Sitzung den Client bereits nach
`plugins\maschine-ArenaMode\` verlegt hatte; BepInEx lud die Wurzelkopie (die
Zweitkopie wurde als Duplikat übersprungen), die Messung lief also mit diesem
Build. Die Wurzelkopie wurde entfernt, der Client liegt nur noch im
kanonischen Ordner, und EFT wurde danach neu gestartet. Der No-Deploy-Build
(`SPTModType=Library`, `SPTUserMods=<nicht existent>`) kopiert die
Arena-Assets trotzdem nach `BepInEx\plugins\ArenaMode` (inhaltlich identisch).
