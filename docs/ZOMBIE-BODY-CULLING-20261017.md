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

## 6. Messrezept und Ergebnis

Siehe Abschnitt 7; A/B-Lauf in einem Raid, MaxAlive 32, Factory, Godmode,
stehend, Phasen zu je 90 s: Basis (nur Performance-Schalter) → Animator →
Schatten → beide → Basis → beide, dazu ModProfiler-Exports alle 30 s.

## 7. Ergebnis GPU-2

_(wird nach dem Lauf eingetragen)_
