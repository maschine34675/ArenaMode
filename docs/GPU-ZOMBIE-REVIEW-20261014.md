# Zombie-Crowd-Review und GPU2-Vergleich — 14.10.2026

Geprüft wurden Groks vier Commits `36dbd12`, `22d887d`, `aa5edd5` und
`66b9a3e`. Die vorhergehende Squad-Änderung `8b5c772` gehört ebenfalls zum
Unterschied gegenüber der [Messung vom 13.10.](GPU-ZOMBIE-PERFORMANCE-20261013.md),
ist aber nicht Gegenstand einer erneuten vollständigen PFT-Abnahme.
Zwei unabhängige lesende Reviews wurden mit der nativen Dekompilation abgeglichen.
Gameplay-Code wurde für diese Prüfung weder geändert noch neu eingespielt.

## Konkrete Befunde

### P1: Sichtbarkeit bleibt beim Verlassen des Crowd-Pfads hängen

`ZombieCrowdRuntime.Tick` beendet sich bei `CrowdEnabled=false`, während eines
Respawns und bei totem MainPlayer (Zeilen 61–65). Zuvor gesetztes
`Renderer.forceRenderingOff` wird nicht wiederhergestellt. Im PFT-Zombie-Modus
läuft die Welle nach dem lokalen Tod mit Beobachterkamera weiter
(`ZombieTeamRuntime.OnLocalDeath`:301, `RequestStart`:316).
Lebende Zombies hinter der letzten Blickrichtung können dabei unsichtbar bleiben.
Das betrifft auch das Abschalten der Option während eines laufenden Raids.

Die eigene Abkürzung bei unverändertem `Shown` (Runtime:227) berücksichtigt zudem
nicht, dass natives `OfflinePlayerCulling.ApplyVisibleState` denselben Rendererwert
schreibt. Dieses aktualisiert ihn bei seinen Zustandswechseln, nicht garantiert
bei jedem Arena-Tick. Ein einmaliger Schreibzugriff ist keine saubere gemeinsame
Zuständigkeit. Originalzustand und Besitzer sollten gesichert und beim Verlassen
des Pfads wiederhergestellt werden; die tatsächliche Zuschauer-Kamera braucht
eine eigene Regel oder eine vollständige Rückkehr zum nativen Culling.

**Abgrenzung:** AI-Tod deaktiviert natives Culling (`EFT/LocalPlayer.OnDead`),
und die Pool-Rückgabe setzt `forceRenderingOff` zurück. Es wird deshalb kein
pauschaler Fehler „unsichtbare Leichen bleiben im Pool“ behauptet.

### P2: Recall benutzt bei begrenzten Arealen nicht den geprüften Bodenpunkt

`ZombieCrowdRuntime.AcceptPoint` initialisiert `placed=candidate` und liefert im
Area-Zweig nur `ArenaAreaRuntime.AllowTeleport` zurück (Runtime:178–180).
Die Prüfung darf einen bis zu 0,75 m entfernten NavMesh-Punkt finden
(`ArenaAreaRuntime.TrySample`:342–353); dieser korrigierte Punkt wird verworfen.
`BotMover.Teleport`:1021–1025 und `EFT/Player.Teleport`:30865–30870 wenden hingegen
die tatsächlich übergebene Position an. Ein Kandidat knapp in Geometrie oder
neben dem NavMesh kann dadurch trotz gültigem benachbartem Sample gewählt werden.

Auch im begrenzten Areal muss der tatsächlich geprüfte Sample als Ziel dienen.
Belegung und physischer Platz müssen für genau diesen Punkt gelten. Die
Solo-Messungen auf vollständigem Factory/Labs zertifizieren diesen Customs-Pfad nicht.

### P2: Avoidance-Skip überspringt auch den Abbau vorhandener Versätze

`ShouldRunAvoidance` unterdrückt die gesamte native `ManualUpdate`-Methode
(Runtime:276–277), einschließlich `OffsetDecrease`. Andere native Bots, besonders
PFT-Follower, schreiben trotzdem auf ihre Nachbarn
(`BotLocalAvoidance`:86–87, `AddPower`). `BotMover.SetPosition`:716 verwendet den
gespeicherten `NotLinkedOffset` weiterhin. Ein einmaliger Ausweichversatz kann so
ohne seinen normalen Abbau erhalten bleiben. Nur den teuren Nachbarscan auslassen
oder vor dem Skip die nicht mehr verwendeten Offsets kontrolliert zurücksetzen.

### P2: Skin-Einstellungen können gepoolte Ausrüstung weiter beeinflussen

`Capture` erfasst sämtliche untergeordneten Renderer, also auch skinned
Ausrüstung (Runtime:244). `quality` und `updateWhenOffscreen` werden verändert,
ohne deren vorherigen Zustand zu sichern. `PlayerBody.SlotView.DestroyCurrentModel`
gibt solche Modelle in den Pool zurück (native `EFT/PlayerBody`:305–326).
`SkinDress` und `AssetPoolObject` stellen diese beiden Werte beim Unskin/Rebind
nicht wieder her. Bei Wiederverwendung kann deshalb die reduzierte Skinning-
Qualität auf einen späteren Benutzer übergehen, auch außerhalb des Crowd-Pfads.

Originalwerte vor Modellrückgabe mit eindeutiger Besitzprüfung wiederherstellen
oder gepoolte Ausrüstung von diesem Eingriff ausnehmen. Dress-Schatten und
`forceRenderingOff` haben native Rücksetzungen; sie werden hier nicht als derselbe
Pool-Fehler ausgegeben.

## Weitere Review-Ergebnisse und Grenzen

- Recall und Sicht-Skip verändern das Spielverhalten. Nahkämpfer innerhalb
  80 m umgehen unter anderem Wand-/Sektorprüfungen; Shooter behalten die native
  Sichtprüfung, werden aber **ebenfalls zurückgerufen**. Die Winkel verteilen
  sich über 360°, nicht ausschließlich hinter dem Spieler. Einige Kommentare
  beschreiben beides falsch.
- Bis zu drei synchrone Pfadsuchen für den einen Recall-Kandidaten pro Frame
  kommen als neue Arbeit hinzu. Aus eingesparten Linecasts allein lässt sich
  kein Nettogewinn ableiten.
- Native `Player.VisualPass`:25910–25918 setzt Body-/ArmsAnimator-Culling abhängig
  von der Distanz selbst. Die einmalige Änderung in `Capture` beweist daher
  keine dauerhafte Animator-Ersparnis.
- `hold.mjs` wurde nicht für die Vergleichsmessung verwendet. Seine vier
  rollenden 300-Frame-Fenster ohne Laufen und Schießen sind keine vollständige
  Kampfverteilung. Außerdem erzwingt es vor Profil-/Modusprüfung die Kontrolle
  (`force:true`) und kann ohne `--stay` den Factory-Anker auf einer anderen
  Karte verwenden. Das ist ein separater Sicherheitsmangel des Testhelfers.
- Die Offline-Suite besteht: 964 Tests erfolgreich, zwei übersprungen; davon
  91 Zombie-Tests erfolgreich. Der bisherige Zombie-Performance-Harness besteht
  mit 192 Assertions. Die 17 Rekonstruktions-/Integritätstests des Messwerkzeugs
  bestehen. Diese Prüfungen decken den neuen Unity-Renderer-Lebenszyklus nicht ab.

## Messaufbau

SPT-GPU-2, Ryzen 7 9800X3D / RTX 4070 Ti / 32 GB; gleiche 1920×1080-Grafik wie am
Vortag. Graphics.ini SHA256
`AE2CC4E334780AF7B86739D9E3CB8C8062836D1454A9C357CB72735D016379F5`,
PostFx.ini SHA256
`5B5999B0B50F8905AA81BAC0E211F69015334288588BA26A1EA7389EBC1BAA7B`.
Es bleiben 43 Plugins und fünf Bibliotheken installiert; ModProfiler ist
`Active=false`, `Instrumented=false`.

Geladene Client-DLL SHA256
`1373FA1CB76C49EB6220F07AD65E08CB880CE62F7852320ED4A3D03B7C0F6859`.
Sie trägt `0.1.0+aa5edd5`, enthält laut Dekompilation aber bereits die Änderungen
aus `66b9a3e` (`ApplyPresentation`, Bone1/Bone2, `forceRenderingOff`, Animator-
Culling). Quellcommit und eingebettete Build-ID werden deshalb getrennt genannt.
VM, Hostdatei und geladene MVID stimmen überein. Serverdatei:
`F5C9539EE088C354776AD273BCD4545BA7A363E1245648332C1C23B3AD9393F7`, ebenfalls
`0.1.0+aa5edd5`; keine neue Bereitstellung durch diese Review-Sitzung.

Gegenüber dem Vortag unterscheiden sich unter den Client-Plugins/Bibliotheken
nur ArenaMode.Client und Redeploy.Core. Letzteres trägt nun `1.0.7+bf227f2` statt
`1.0.7+50fe0b0`; dessen Quelldiff enthält ausschließlich Dokumentation und
Thumbnail-Dateien, keine Laufzeitänderungen. UiBridge, SAIN, MapPort und PFT
haben identische Dateihashes. Die vorhandene RespawnApiProbe bleibt installiert.

Die Messung verwendet unverändert `run.mjs` (SHA256
`1ACD0AEA173EC2532D4037FE323275390C9189C91326FB7A04E100B1772AFC9D`), native
HK416A5/M855A1-Schüsse, Kit `crusader`, Solo ohne PFT-Follower, Godmode,
unendliche Munition ohne Verschleiß, 20 % Shooter und 0,2 s Spawnintervall.
WaveStartSize=200 und WaveGrowth=0 halten die Last konstant. Dieselben sechs
Factory-Anker beziehungsweise dieselbe Labs-Hauptebenenroute werden verwendet:
30 s Warmup, 240 s Factory / 300 s Labs, Laufen und Teleport alle etwa 55 s.
Das Collectorverfahren ist unverändert zu den letzten Vortagsläufen;
die frühen Factory-Vortagsläufe benutzten noch einen schwachen Time-Inspector-
Handle und haben deshalb nicht exakt identischen Abfrageaufwand.

Ziel bleibt **P95 der einzelnen Framezeiten ≤ 16,6667 ms**. Weder Mittel-FPS
noch gemittelte Fenster-P95 ersetzen dieses Kriterium. Recall verändert die
Nähe und Sichtbarkeit der Gegner: dieselbe MaxAlive-Zahl ist keine identische
räumliche Last. Die Resultate sind ein realistischer Versionsvergleich, keine
isolierte Messung einer einzelnen Optimierung.

Rohdaten, Profilkopien und Config-Ledger bleiben privat und ignoriert unter
`artifacts/zombie-review-20261014/`.

## Vergleich mit dem Vortag

Alle fünf neuen Läufe haben vollständige Frameabdeckung, gültige Dauer und
denselben Prozess/dieselbe Karte innerhalb des Laufs. P95-Grenz-FPS ist jeweils
`1000 / P95(ms)`, kein Durchschnitt. Sämtliche Hänger bleiben enthalten.

| Karte / MaxAlive | P95 vorher | P95 neu, Crowd an | Grenz-FPS vorher → neu | Neu: Kills / Schüsse | Ziel neu |
|---|---:|---:|---:|---:|---|
| Factory / 24 | 14,000 ms | 12,591 ms | 71,4 → 79,4 | 77 / 858 | bestanden |
| Factory / 28 | 15,316 ms | 17,041 ms | 65,3 → 58,7 | 107 / 843 | verfehlt |
| Factory / 32 | 17,107 ms | 19,076 ms | 58,5 → 52,4 | 90 / 837 | verfehlt |
| Labs / 24 | 15,391 ms | 23,341 ms | 65,0 → 42,8 | 76 / 1.041 | verfehlt |
| Labs / 28 | 20,474 ms | 20,836 ms | 48,8 → 48,0 | 104 / 1.051 | verfehlt |

Vergleichsdateien: `factory24-a`, `factory28-a`, `factory32-a`, `labs24-b`,
`labs28-a` vom Vortag; neue Dateien entsprechend `factory24-on` bis `labs28-on`.
Die fünf aktuellen Läufe umfassen 1.325,006 s erfassten Kampf und 118.158 Frames.
Factory 24/28 und Labs 28 halten in allen Spielerproben genau ihr Limit;
Factory 32 hat zwei kurze Proben mit 30/31, Labs 24 zwei mit 20/21.
Die übrigen Proben liegen ebenfalls genau am Limit. Ein gerade aktivierender
`PreActive`-Bot in Factory 32 ist eine normale Nachbesetzung, keine stillgelegte KI.

Die Nähe der Horde verändert sich stark. Mittelwert der periodischen
Bot-Snapshots innerhalb 15 m: Factory 24 von 12,33 auf 20,00; Factory 28 von
13,83 auf 24,08; Factory 32 von 20,33 auf 29,08; Labs 24 von 6,27 auf 18,67.
Die tatsächlich erfasste Fußbewegung sinkt bei Factory 28 von 231,1 auf 191,3 m
und bei Factory 32 von 257,4 auf 144,7 m, obwohl die Befehlsroute gleich ist.
Das ist ein Unterschied der realen Last und Bewegungsfreiheit; diese Positions-
daten allein beweisen weder eingefrorene KI noch ein visuell fehlerfreies Gedränge.

Labs 24 ist kein Ausreißer durch einen einzelnen Ladehänger: In der vierten
Kampfminute liegen 80,12 % der Frames über 16,667 ms, in der fünften 43,58 %.
Deren P95 beträgt 26,54 beziehungsweise 24,67 ms. Auch Factory 28 verfehlt
breit: Selbst eine rein hypothetische Entfernung aller Frames über 33 ms würde
das P95-Ziel nicht retten; in der eigentlichen Auswertung wird nichts entfernt.
Die größten Factory-Spitzen um etwa 72 und 126 Sekunden treten teilweise auch
historisch auf. Ohne eigene Zeitmessung des Verursachers wird keine Ursache behauptet.

Der historische Vergleich isoliert den Crowd-Effekt nicht: Neben verschiedenen
Kampfverläufen unterscheiden sich Prozessalter und Speicherstand. Bei Factory 24
war der alte Prozess beim Messstart etwa 1.311 s, der neue etwa 147 s alt;
die frühen alten Factory-Läufe besitzen außerdem den oben genannten anderen
Collector/Godmode-Aufrufrhythmus. Labs verwendet auf beiden Seiten denselben
aktuellen Runner. Deshalb folgen zusätzliche Kontrollen auf derselben aktuellen DLL.

## Frische OFF/ON-Kontrolle auf Labs

Für jeden der beiden zusätzlichen Läufe wurde EFT separat neu gestartet. Beide
verwenden dieselbe aktuelle DLL, dieselbe Hauptgeschossroute und MaxAlive=24.
Alle 43 Plugins und fünf Bibliotheken bleiben identisch, ModProfiler bleibt aus.
`CrowdEnabled` schaltet das gesamte neue Crowd-Paket; es ist kein isolierter
Schalter ausschließlich für die Renderer-Optimierung.

| Frischer Prozess | P95 | Grenz-FPS | Mittel-FPS | Frames über 16,667 ms | Kills / Schüsse | Ziel |
|---|---:|---:|---:|---:|---:|---|
| Crowd aus | 14,089 ms | 71,0 | 103,8 | 0,72 % | 69 / 802 | bestanden |
| Crowd an | 20,385 ms | 49,1 | 99,2 | 7,83 % | 95 / 1.073 | verfehlt |

Beide Läufe besitzen vollständige Frameabdeckung und 23–24 lebende Zombies in
den Spielerproben. Die unabhängige Auswertung zählt 31.174 beziehungsweise
29.805 Frames. Beide Spieler bleiben bei 440 HP ohne Tod. Die Protokolle weisen
die Schüsse über eindeutige native Shot-Events und den nativen Gesamtzähler nach.

OFF enthält 56 `look/no_target`-Antworten, ON eine. Das sind Sichtkontaktpausen,
keine ausgefallenen Frameproben. Im OFF-Lauf liegen im Mittel nur 3,27 Zombies
innerhalb 15 m, bei ON 15,73. Auch mit identischen
Laufbefehlen und Ankern fallen Bewegung und Zielkontakt verschieden aus:
431,7 m erfasste Fußbewegung OFF gegenüber 388,7 m ON. Der Vergleich zeigt den
Nettoeffekt des gesamten Verhaltenspakets unter dieser Route, nicht die Kosten
eines einzelnen Hooks. Es gibt keine feste Zufallsfolge und nur ein frisches
OFF/ON-Paar; keine statistisch abgesicherte Prozentangabe zur Regression.

OFF besteht in allen fünf Minutenfenstern (schlechtestes P95 15,175 ms). ON
verfehlt besonders in Minute vier: P95 24,939 ms, 63,79 % der Frames über dem
Budget; die übrigen vier Minuten liegen bei 11,12–12,40 ms. Auch hier ist der
Gesamtwert kein einzelner Hänger. Die Prozesse sind beim Beginn der Kampfphase
ähnlich alt (66,5/65,9 s), mit ähnlichem Managed Heap (1,342/1,318 GB).
Nur das Alter des vorherigen Testprozesses erklärt den Unterschied somit nicht.

Mit allen sieben abgeschlossenen Läufen sind **179.137 Frames, 618 Kills und
6.505 Schüsse** belegt. Die zusammenhängenden Snapshot-Spannen umfassen
1.926,038 s (32 min 6 s), die Summe der erfassten einzelnen Framezeiten
1.925,758 s. Alle sieben Läufe bestehen die Messintegritätsprüfung; nur die
Bildzeitgrenze entscheidet über den jeweiligen Performance-Pass.

## Logprüfung der ersten fünf Läufe

Im vollständig gelesenen Prozesslog stehen null Einträge auf Error/Fatal-Ebene,
aber sieben als Warning **protokollierte NREs** in
`EFT.BotMemory.GoalTargetBeingHitAction` (`0x0005e`). Arena protokolliert sie und
gibt die Exception weiter (`ArenaDamageExceptionLog`:417); sie wird dadurch
nicht behoben. Sechs Meldungen nennen einen bereits zerstörten Aggressor,
eine einen lebenden. Alle betreffen Nahkampfschaden am lebenden lokalen Spieler.

Die acht Meldungen vom Vortag haben denselben Callback, Offset, Victim-Typ und
dieselbe native Assembly; fünf nennen zerstörte und drei lebende Aggressoren.
Das belegt dieselbe bereits bestehende Fehlersignatur; ein zusätzlicher Einfluss
der Crowd-Änderungen ist damit weder nachgewiesen noch ausgeschlossen. Wegen
unterschiedlicher Dauer/Kontaktdichte folgt auch keine verbesserte Fehlerrate.
Für einen begrenzten Folgeschritt sollte die Diagnose zusätzlich den Besitzer
des aufgerufenen `BotMemory`, dessen Zustand, `GoalEnemy.Person` und
`WeaponManager.Melee` erfassen. Bisher beschreiben die Meldungen Victim/Aggressor,
aber nicht den Callback-Besitzer; native `BotMemory`:803 dereferenziert diese
Zustände ohne Absicherung, seine Zielanmeldung erfolgt über `BeingHitAction`:227.
Die konkrete null-Referenz bleibt unbewiesen.

Weitere Meldungen: 16 Anfragen ohne verfügbares Zombieprofil, danach weiterhin
volle beziehungsweise nachgefüllte Besetzung; 37 erwartete Hinweise wegen
deaktiviertem Announcer. Kein `zombie crowd: optimization skipped`-Fallback.
Die absichtlich werfende RespawnApiProbe und Unity-Animator-Warnungen beim
Aufräumen werden als vorhandene Testumgebung dokumentiert, nicht als neue
Crowd-Fehler ausgegeben.

Die beiden frischen Prozesse ergänzen null NREs bei OFF und zwei bei ON, wieder
dieselbe `GoalTargetBeingHitAction`-Signatur (ein zerstörter, ein lebender
Aggressor). Beide Logs sind vollständig gelesen, ohne Error/Fatal oder
Crowd-Fallback. Vier beziehungsweise sieben Profilanfragen werden verworfen,
anschließend ist die Besetzung wieder am Limit. Daraus folgt kein Beweis, dass
CrowdEnabled die NRE erzeugt: Die Signatur existiert vor diesen Commits und die
Nahkampfkontakte unterscheiden sich erheblich.

## Bewertung und nächste technische Schritte

Ein pauschaler Performancegewinn oder ein höheres sicheres Zombie-Limit ist mit
diesen Messungen **nicht belegt**. Factory besteht mit 24, verfehlt mit 28 und 32.
Labs verfehlt mit aktivierter Crowd-Steuerung bereits bei 24, sowohl im längeren
Prozess als auch nach frischem Start; ohne Crowd besteht die frische 24er-Probe.
Das Ergebnis von 24 ON ist nicht als allgemeines Optimum zu verstehen: niedrigere
Caps, andere Karten, PFT und lange normale Wellen sind hier nicht neu vermessen.
Die vom Nutzer gewählten Defaults werden nicht anhand dieser Einzelmessung geändert.

Vor einer weiteren Erhöhung zuerst die vier Zustands-/Lifecycle-Befunde beheben.
Danach Recall/erhöhte Hordendichte getrennt von Sichtprüfung, Avoidance und
Darstellung schaltbar messen, jeweils mit frischem Prozess und derselben Route.
Für Labs ist das aktuelle Gesamtpaket bei 24 kein Nachweis für das 60-FPS-P95-Ziel;
ein bloßes Anheben der Alive-Obergrenze wäre durch diese Daten nicht begründet.
Die zusätzliche BotMemory-Diagnose ist ein separater kleiner Folgeschritt.

## Abschlusszustand und Ablage

Alle temporären Config-Ledger sind ohne Konflikt zurückgesetzt; der semantische
Vergleich sämtlicher Arena-Einstellungen gegen den Beginn der Sitzung ist leer.
Erhalten bleiben die heutigen Nutzerwerte **MaxAlive 32, WaveStartSize 50,
WaveGrowth 20, OwnGear=true, CrowdEnabled=true**, einschließlich ursprünglicher
Kit-Auswahl. Graphics.ini/PostFx.ini sind bytegleich zur Baseline.

Nach dem letzten Test und der Rücksetzung wurde EFT noch einmal sauber neu
gestartet, um alle prozesslokal veränderten Renderer-/Poolzustände zu verwerfen.
GPU2 steht im MainMenu, ohne Raid oder Kontrollinhaber. Godmode, KillGuard,
unendliche Munition, Tracking, Gehen und Trigger sind aus. Alle 43 Plugins und
fünf Bibliotheken stimmen weiterhin zwischen VM, Hostdateien und geladenem
Prozess überein; Client-/Serverdateien wurden nicht ersetzt.

Gegenüber dem gesicherten **Vor-Raid-Profil nach dem ersten Spielstart** ändern
sich nur `Health.UpdateTime` und `Hideout.sptUpdateLastRunTimestamp`. Inventar,
Gesundheitswerte, Quests, XP, Skills und Statistiken bleiben gleich. Beim ersten
Spielstart wurden bereits vor allen Raids Tagesquests erneuert (256 geänderte
Quest-/Repeatable-Pfade plus die beiden Zeitstempel gegenüber der Vor-Start-Kopie);
diese Änderungen sind in der Vor-Raid-Baseline enthalten und danach unverändert.
Das Profil wurde nicht durch Zurückkopieren einer Sicherung manipuliert.

Einzige Tooling-Änderung: `7fd5e2a` macht den bereits benutzten Benchmark-Lane über
`ZOMBIE_BENCH_LANE` wählbar, sodass wiederholte Sitzungen ihren eigenen
Kontrollvertrag nutzen. Messung, Kampfsteuerung und Spielcode bleiben unverändert.
Ein unabhängiger Reviewer hat alle sieben Rohframe-Verteilungen, Schusszähler,
Populationsproben und die beiden frischen Build-Snapshots nachgerechnet; ein
weiterer hat Befunde und Formulierungen gegen Quellcode und Ergebnisse geprüft.
