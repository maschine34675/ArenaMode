# Zombie-Populationsgrenze — SPT-GPU-2, 13.10.2026

**Empfehlung: MaxAlive 24 als gemeinsamer Ausgangswert für Factory und Labs.**
Das ist die höchste auf beiden Karten bestandene Teststufe. 28 besteht auf
Factory im Gesamtwert, scheitert aber auf Labs; 32 scheitert schon auf Factory.
25–27 wurden nicht gemessen, ein exaktes mathematisches Maximum wird daher nicht
behauptet. Auch mit 24 können einzelne Kampfminuten das Ziel unterschreiten.

Gesucht ist die höchste brauchbare Zahl gleichzeitig lebender Zombies auf der
Hardware des Nutzers, bei möglichst mindestens 60 FPS auch im langsameren Teil
des normalen Kampfgeschehens. Es wurden keine Arena-DLLs geändert oder gebaut.
Die neuen Dateien unter `tools/ZombiePerformanceChecks/` sind externe Mess- und
Bedienwerkzeuge; sie ersetzen keine Spiel- oder Modfunktionen.
Sieben vollständig ausgewertete Läufe umfassen zusammen **35,0 Minuten Kampf,
197.422 Frames, 497 Kills und 6.610 bestätigte Schüsse**. Die beiden abgebrochenen
Messläufe sowie vorbereitende Kit-/Hook-Versuche sind darin nicht enthalten.
Spielpräferenzen wurden anschließend zurückgesetzt; MaxAlive steht deshalb
wieder auf dem ursprünglichen Wert 56, nicht automatisch auf der Empfehlung.

## Zielgröße und Grenzen

Die Anforderung wird als **P95 der Framezeit ≤ 1000/60 = 16,6667 ms** verstanden:
Mindestens 95 % der einzelnen Frames erreichen damit die 60-FPS-Grenze.
Das ist die untere FPS-Grenze des 5. Perzentils, nicht das schnelle 95. FPS-Perzentil.
Ein hoher FPS-Mittelwert allein genügt nicht. Zusätzlich werden P99, Maximum,
Anteil der Frames über dem Budget, echte Besetzung, Schüsse und Kills festgehalten.

Die Ergebnisse gelten für die aufgeführten Karten, Grafikwerte und installierten
Mods. Weder PFT mit vier Begleitern noch sämtliche Arena-Karten sind damit
freigegeben. Godmode soll einen vorzeitigen Testabbruch verhindern, verändert
aber das Überleben und erlaubt länger anhaltenden Nahkampf. Unendliche Munition
und ausgeschaltete Waffenabnutzung vermeiden Nachlade- und Ladehemmungspausen;
der Beschuss läuft weiterhin durch die echte Waffe und die native Schadenslogik.

## Rechner, Grafik und geladene Versionen

Quelle: `artifacts/zombie-cap-20261013/baseline/hardware.json`, gesicherte
`Graphics.ini`/`PostFx.ini` und das EFT-Startlog vom 13.10.2026, 15:03:27 MESZ.
Die Einstellungen liegen auf GPU2 unter `SPT_Runtime/user/sptSettings/`, nicht
im leeren ursprünglichen Battlestate-Roaming-Verzeichnis.

| Merkmal | Festgehaltener Wert |
|---|---|
| CPU | AMD Ryzen 7 9800X3D, 16 logische Prozessoren |
| RAM / GPU | 31.862 MB System-RAM; RTX 4070 Ti, 11.994 MB VRAM |
| System / Renderer | Windows 11, Build 26200; Direct3D 11 |
| Spiel | EFT 0.16.9.5.40743, Unity 2022.3.43f1; SPT-Bibliotheken 4.1.6 |
| Darstellung | 1920 × 1080, randloses Vollbild; Spiel-Limit 120 FPS, VSync aus |
| Texturen / Schatten / Wolken | native Werte 2 / 3 / High; Schattendistanz 80 |
| Sichtweite / LOD | 3.000 / 4 |
| SSAO / SSR / AA | ColoredHighestQuality / Ultra / TAA_High |
| Upscaling | DLSS, FSR2, FSR3 aus; Supersampling 1,0 |
| Weitere Grafik | Reflex, Mip Streaming, High Quality Fog, PostFX aus |
| Weitere Effekte | Grass Shadow, Volumetric Light, Noise, ZBlur und chromatische Aberration an |

| Komponente | Geladener Stand |
|---|---|
| ArenaMode.Client | `0.1.0+a4ebe18c31386bd6da46f6c60e73326e3aa71cf2` |
| ArenaMode.BigBrainCompat | `0.1.0+4fd79815d6a8dcd15f4d06d4cf5e0d1543d0b998` |
| ArenaMapPort.Client | `0.1.0+7d71f6873d2dca9bb7e143ba21dc66dcba5a14c6` |
| Redeploy.Core | `1.0.7+50fe0b04ae2d4df087069f1ed525fbe79500b6a7` |
| UiBridge | `0.9.1+7b94a3f551e9bdb0fab3545c29e0c84f6f7ff593` |
| SAIN / BigBrain / PFT | 4.5.1 / 1.5.0 / 0.10.3 |

Geladene MVIDs stimmen in der Baseline mit den jeweiligen Dateien überein.
Alle 43 Plugins und fünf Bibliotheken waren auch bei der Schlusskontrolle
identisch mit Hostdateien und VM-Build. Die vorhandene RespawnApiProbe blieb
installiert; ihre absichtlich werfenden Replaced-Subscriber sind im Log erkennbar.
Die Modzusammenstellung wurde nicht für bessere Messwerte ausgedünnt.
Arena-Client-SHA256: `EB3939749E4701264090BEDF67B85163A84D7643E112693B757E1988BA2AB4DB`.
Grafikdatei-SHA256: `AE2CC4E334780AF7B86739D9E3CB8C8062836D1454A9C357CB72735D016379F5`.
ModProfiler bleibt installiert, ist während der Messung aber **Active=false,
Instrumented=false**. Es läuft keine zusätzliche ModProfiler-Instrumentierung.

## Ablauf und Erfassung

- Solo-Zombies, zunächst Factory Tag. Keine Begleiter oder künstlich getöteten Bots.
- Pro Limit ein neuer Raid: MaxAlive wird beim Matchstart eingefroren, die erste
  Wellengröße und der Schützenanteil bereits beim Laden.
- Konstante Last der MaxAlive-Kurzläufe: WaveStartSize 200, WaveGrowth 0,
  SpawnIntervalSeconds 0,2, ShooterPercent 20. VoiceBudget und PFT-Fast-Path
  bleiben aktiv. Die anschließenden Langläufe prüfen zusätzlich Wellenwechsel.
- Konstante Ausrüstung: Arena-Kit `crusader`, HK416 mit M855A1; OwnGear aus,
  RandomKit aus. Das vorher ausgewählte eigene Testprofil-Gear enthielt keine Waffe.
- UiBridge-Godmode und `infiniteAmmo` mit `keepCondition=true`; reale Feuerstöße
  von 500 ms auf sichtbare Ziele, dazwischen Bewegung und kurze Feuerpausen.
- Walker bewegt den Spieler über native Wege. Ungefähr alle 55 Sekunden erfolgt
  ein überprüfter Teleport zu einem weiteren Spawnanker. Ortswechsel sind Teil
  der Last; Teleportspitzen werden nicht aus der Frameverteilung entfernt.
- 30 Sekunden Warmup, danach mehrere Minuten Kampf. Lebende Bots werden alle
  ungefähr 3–5 Sekunden abgefragt; periodische Bot-Snapshots sichern Rollen und Zustand.
  Sämtliche beobachteten Botzustände der vollständigen Läufe waren `Active`,
  Standby `active`; es wurden keine bloßen Config-Sollwerte als Last ausgegeben.
- Ab `factory24-a` wird die gespeicherte Factory-Route einschließlich eines
  anfänglichen Teleports wiederverwendet. Die erste Route entstand abhängig vom
  zufälligen Einstiegspunkt; der erste Lauf ist deshalb kein identisches Paar.

Gemessen werden Unity-Frames (`Time.unscaledDeltaTime`), keine separate
PresentMon-/GPU-Timing-Spur. Frame Generation ist ausgeschaltet. UiBridge
liefert im normalen Status nur die letzten 300 Frames. Statt P95-Werte
solcher Fenster zu mitteln, liest der Collector den vorhandenen Float-Ring in
zwei Seiten, eingerahmt durch Schreibcursor und `Time.frameCount`. Nur eindeutig
zuordenbare Slots werden übernommen; Überlappungen werden dedupliziert, gerade
überschriebene Slots später nachgelesen. Unbewiesene Ringumlaufe trennen Segmente.
Fehlende Frames zählen für die konservative P95-Prüfung als unendlich langsam.
Das Verfahren verändert weder den Ring noch die Spielzeit. Der zusätzliche
Abfrageaufwand ist vorhanden und wird nicht rechnerisch abgezogen.

Die Ringtests sowie ein unabhängiger adversarialer Test mit 200 zufälligen
Lesetrajektorien, 1.213.529 Framezuordnungen und 59.169 Perzentilfällen bestanden.
Ein numerisches `passesP95` reicht dennoch nicht als vollständige Raid-Abnahme:
Messdauer, vollständige Erfassung, Prozessidentität und echte Kampfbelastung
müssen ebenfalls stimmen. Konfiguration und Profil werden vor/nachher verglichen.

## Vergleichbare MaxAlive-Kurzläufe

Factory Tag, jeweils rund vier Minuten Kampf nach dem Warmup. Gleiche gespeicherte
Route und vergleichbarer Startanker; identische Prozessinstanz. Alle drei Läufe
haben **100 % eindeutige Frameabdeckung, ein Segment und keine Capture-Fehler**.
In sämtlichen erfassten Spielerproben lebten genau so viele Bots wie eingestellt.
Je vier `no_target`-Antworten bedeuteten lediglich kurzzeitig keine freie Sichtlinie.

| Lauf / MaxAlive | Messzeit / Frames | Ø FPS | P95 ms / entsprechende FPS | P99 ms | Frames über 16,67 ms | Ergebnis |
|---|---:|---:|---:|---:|---:|---|
| `factory24-a` / 24 | 241,2 s / 23.839 | 98,85 | 14,00 / 71,43 | 17,04 | 1,28 % | bestanden |
| `factory28-a` / 28 | 241,2 s / 21.994 | 91,19 | 15,32 / 65,29 | 18,85 | 2,42 % | bestanden, geringe Reserve |
| `factory32-a` / 32 | 239,9 s / 19.614 | 81,77 | 17,11 / 58,46 | 20,87 | 5,97 % | verfehlt |

| Lauf | Kills / bestätigte Schüsse | Erfasste Fußbewegung mindestens | Schlechtestes Minuten-P95 |
|---|---:|---:|---:|
| `factory24-a` | 58 / 847 | 244 m | 15,79 ms / 63,32 FPS |
| `factory28-a` | 68 / 834 | 231 m | 16,45 ms / 60,81 FPS |
| `factory32-a` | 65 / 825 | 257 m | 17,84 ms / 56,05 FPS |

Fußbewegung summiert die Abstände der Positionsproben ohne Intervalle, in denen
ein Teleport stattfand; Kurven zwischen Proben fehlen. Minutenfenster sind
zusätzliche lokale Hinweise, kein Mittelwert für das Gesamt-P95. Die Maxima von
ungefähr 0,86–0,89 s bleiben enthalten: Ein bestandenes P95 garantiert keine
ruckelfreien Einzelbilder oder ein bestandenes P99.

**Einordnung:** 28 ist die höchste bestandene Teststufe auf
Factory; 29–31 wurden nicht untersucht. Im schwächsten Minutenfenster bleiben
bei 28 nur etwa 0,22 ms Abstand zur Grenze, bei 24 etwa 0,88 ms. Deshalb ist 24
die vorsichtigere Alltagsempfehlung. 28 bleibt eine Option für eine dichter
besetzte Factory-Runde, erfüllt das Ziel aber nicht in jeder belasteten Minute
und ist nach den folgenden Labs-Tests kein gemeinsamer Wert für beide Karten.

## Ausgeschlossene Läufe und Messwerkzeug-Korrekturen

**`factory56-a`: keine vollständige Benchmark-Messung.** Rund 300 Sekunden
Kampf, 108 Kills, 952 bestätigte Schüsse und fünf Teleports; die nachvollziehbare
Frameverteilung endet nach 277,3 s mit 13.150 Frames. Dort: Ø 47,43 FPS,
P95 29,58 ms / 33,80 FPS, P99 35,78 ms; 80,87 % der Frames über dem Budget.
56 Bots waren in allen erfassten Spielerproben lebendig. Das ist ein deutlicher
Negativbefund, aber kein vollständiger Lauf für die Vergleichsmatrix.

Ursache der fehlenden letzten rund 22 Sekunden: Ein schwach referenziertes
statisches Inspector-Type-Handle wurde vom GC eingesammelt; 63 Capture-Anfragen
meldeten `stale_handle`. Die davor rekonstruierten Frames sind intern lückenlos,
was ausdrücklich keine 100-%-Abdeckung der gesamten 300 Sekunden bedeutet.
Für die folgenden Kurzläufe wurde zunächst der statische Typ pro Klammer neu
geöffnet und wieder freigegeben. Der erste 56er-Lauf hatte außerdem noch keinen
gleichen Startanker und wird nicht als identisches Vergleichspaar ausgegeben.

**`factory28-b`: vollständig ausgeschlossen.** Der geplante 480-Sekunden-Lauf
endete nach 60 Kills mit einem Spielertod. Beim Kit-Refill wurde der aktive
HealthController ersetzt; der UiBridge-Godmode schützte noch die vorherige
Instanz. Messdauer, Abschluss und Erfassungsintegrität bestehen die Prüfungen
nicht; Abschnitte ohne volle Besetzung dürfen kein günstiges Langlauf-P95 liefern.
Der Wert dieses abgebrochenen Laufs wird nicht zur Empfehlung herangezogen.

**Ab `factory28-c` geänderte Instrumentierung:** Framezähler kommen aus
`status.bridge.frame`, der denselben nativen `Time.frameCount` bereitstellt,
statt aus einem schwachen statischen Type-Handle. Der Ring selbst bleibt der
unveränderte UiBridge-Ring. Godmode wird über den öffentlichen Hook in jeder
Kampfschleife erneut angewendet, damit ein neuer HealthController wieder erfasst
wird. Das ist keine atomare Garantie gegen jeden Schaden zwischen Kitwechsel
und nächstem Hook. Ein erneuter Tod würde den Lauf wiederum disqualifizieren.
Diese Änderungen betreffen ausschließlich den externen Runner; zusätzliche
Status-/Hook-Aufrufe können Messaufwand verursachen. Keine Arena- oder
UiBridge-DLL wurde dafür geändert. Langläufe sind daher eine separate Validierung
und kein Nachweis eines Leistungsgewinns gegenüber der früheren Instrumentierung.

## Längerer Wellentest und Kartenvergleich

| Lauf | Karte / MaxAlive | Umfang | Ergebnis |
|---|---|---|---|
| `factory28-c` | Factory Tag / 28 | 479,8 s, 47.736 Frames; normale Wellen 56 + 10 | Gesamt-P95 14,54 ms / 68,76 FPS; schwächste Minute 17,79 ms / 56,20 FPS |
| `labs28-a` | Labs Hauptebene / 28 | 299,6 s, 25.456 Frames; konstante Besetzung | P95 20,47 ms / 48,84 FPS; Ziel verfehlt |
| `labs24-a` | Labs, mehrere Etagen / 24 | 300,2 s, 30.000 Frames; konstante Besetzung | P95 15,12 ms / 66,14 FPS; Gesamtziel bestanden |
| `labs24-b` | Labs Hauptebene / 24 | 300,7 s, 28.783 Frames; identische Route wie `labs28-a` | P95 15,39 ms / 64,97 FPS; Gesamtziel bestanden |

`factory28-c` erfasst die gesamten acht Minuten lückenlos, ohne Capture-Fehler
und ohne Prozesswechsel. 116 Kills, kein Tod, 440 HP in allen Spielerproben;
mindestens 1.370 Schüsse aus abgeschlossenen Feueraufträgen, 1.373 im nativen
Schusszähler. Ein Feuerauftrag lief in ein Timeout, nachdem er bereits drei
Schüsse ausgelöst hatte; weitere 65 `no_target`-Antworten entstanden ohne sichtbares
Ziel. Der erste Wellenwechsel mit Kit-Refill wurde überlebt, die zweite Welle
nahezu geleert. Godmode-Refresh und unendliche Munition blieben wirksam.

Die Besetzung schwankte mit dem natürlichen Wellenabbau zwischen 0 und 28.
Rund 222,5 von 474,5 Sekunden beobachteter Population lagen bei mindestens
90 % des Caps. Das Gesamt-P95 profitiert folglich auch von ruhigeren Phasen:
Es ersetzt den konstanten 28er-Lasttest nicht. Die sechste Kampfminute unterschritt
das Ziel trotz bestandenem Gesamtwert. P99: 17,62 ms; Maximum: 1.708,36 ms;
1,47 % aller Frames überschritten das 60-FPS-Budget. Diese seltenen Spitzen
sind enthalten; ohne separate Ursachenmessung werden sie nicht als GC- oder
Zombie-KI-Problem ausgegeben.

`labs28-a` blieb in allen Spielerproben bei 28 lebenden Zombies. 82 Kills,
947 bestätigte Schüsse, sechs Teleports und mindestens 442 m erfasste
Fußbewegung belegen aktive Belastung. Alle Frames sind eindeutig erfasst,
ohne Capture-Fehler; 20 erfolglose Zielabfragen hatten keine freie Sichtlinie.
Die erste Minute bestand knapp mit 16,36 ms, die vierte fiel auf 24,66 ms
(40,55 FPS), nachdem die Route den westlichen Bereich erreichte. Gesamt:
Ø 84,98 FPS, P99 24,05 ms, Maximum 1.884,95 ms, 12,26 % der Frames über
16,67 ms. Deshalb darf der gute Anfang nicht als Labs-Freigabe für 28 gelten.
Die Aufnahme `labs28-end.png` wurde erst nach der Messung erstellt und zeigt
den laufenden Kampf, 82 Kills, volle HP und das geladene HK416.

`labs24-a` wechselt zwischen Keller, Haupt- und Obergeschoss und besucht
ebenfalls den langsamen westlichen Bereich. Durchgehend 24 Bots in den Proben,
47 Kills, 878 bestätigte Schüsse, sechs Teleports und mindestens 220 m erfasste
Fußbewegung. Vollständige Erfassung, keine Capture-Fehler. Gesamt Ø 99,94 FPS,
P99 17,38 ms, Maximum 1.104,36 ms, 1,53 % der Frames über dem Budget.
Die schwächste Minute verfehlte mit 16,72 ms / 59,81 FPS das Ziel knapp, während das
Gesamt-P95 bestand. Wegen der anderen Route und geringeren Killrate ist der
Abstand zu `labs28-a` kein isolierter Nachweis des reinen Cap-Effekts.

`labs24-b` wiederholt daher dieselben sechs Anker und denselben Einstiegspunkt
wie `labs28-a`. 61 Kills, 909 bestätigte Schüsse, mindestens 354 m erfasste
Fußbewegung; 71 Spielerproben mit 24 und eine mit 23 lebenden Zombies während
des Nachfüllens. Alle 360 Botbeobachtungen waren `Active`. Volle Erfassung,
keine Capture-Fehler, 32 Zielabfragen ohne Sichtlinie. Ø 95,73 FPS, P99 18,11 ms,
Maximum 893,85 ms, 2,13 % der Frames über 16,67 ms. Das Gesamtziel besteht;
die letzte Minute lag dennoch bei 17,85 ms / 56,02 FPS. Eine identische Route
bedeutet bei zufälligen Botstarts und Sichtlinien keine identischen Einzelkämpfe.

## Empfehlung und nächste sinnvolle Untersuchung

- **24 gleichzeitig aktive Zombies**: höchster getesteter gemeinsamer
  Ausgangswert, Gesamt-P95 auf Factory 71,43 FPS, auf Labs 64,97–66,14 FPS.
- **28**: mehr Dichte auf Factory bei geringer Reserve, keine Labs-Empfehlung.
- **32 beziehungsweise bisherige 56**: verfehlen schon auf Factory das Ziel.
  Der 56er-Negativbefund ist nur ein dokumentierter Teil-Lauf.
- Das ist keine Freigabe für andere Karten, vier PFT-Begleiter, andere Grafik-
  oder Modstände oder beliebig späte Wellen. Wer auch in jeder kurzen Kampfphase
  möglichst 60 FPS erreichen möchte, braucht noch mehr Reserve; ein niedrigerer
  Wert wäre dafür gesondert zu testen. Nicht gemessene 20/25–27 erhalten hier
  keine erfundene Freigabe.

Einzelne Hänger von etwa 0,85–1,88 s bleiben unabhängig vom bestandenen P95
sichtbar. Die nächste Untersuchung sollte diese Spitzen gezielt mit einem
Profiler zeitlich zu Spawn, Bereinigung und Wellenwechsel zuordnen. Die aktuelle
Messung weist weder einen bestimmten CPU-/GPU-Engpass noch deren Ursache nach.

## Logs, Wiederherstellung und Nachprüfbarkeit

Der vollständige Prozesslog enthält keine Einträge der Stufe Error/Fatal.
Das heißt nicht fehlerfrei: Acht Warnungen protokollieren abgefangene
`Player.ApplyDamageInfo`-NREs; verfügbare Stacks führen zu
`BotMemory.GoalTargetBeingHitAction`. Fünf nennen einen bereits freigegebenen
Angreifer, drei einen noch lebenden. Außerdem wurden 15 einzelne Spawnaufträge
wegen fehlender Profile verworfen. Die gemessenen Besetzungen wurden trotzdem
erreicht; es ist kein Beleg dafür, dass diese Meldungen das P95 verursachten.
Diese beiden Punkte bleiben separate Diagnoseaufgaben, ohne Änderungen im Test.

Die RespawnApiProbe erzeugt absichtlich Subscriber-Ausnahmen beim Bodywechsel;
Redeploy fängt sie ab. Die zugehörigen PerfectCulling-Aufräumwarnungen und
`Arena announcer skipped ... reason=disabled` gehören zum unveränderten
Testaufbau. Mehrere `gpu-run leave`-Aufrufe trafen beim Rückweg ins Menü das
5-Sekunden-Mainthread-Timeout; die offizielle `menu`-Fortsetzung bestätigte jeweils
den abgeschlossenen Austritt. Lade-/Menüzeiten sind keine Kampf-FPS-Messungen.

Schlusszustand auf GPU2: Hauptmenü, kein Raid, keine fremde Sitzung oder
Kontroll-Lease; Godmode und InfiniteAmmo aus. Alle sieben noch offenen
Konfigurationsledger konfliktfrei in umgekehrter Reihenfolge zurückgesetzt
(das erste Wellenledger war schon vor `factory28-c` wiederhergestellt).
Der semantische Vergleich sämtlicher Arena-CFG-Einträge zeigt **null Unterschiede**.
Graphics.ini und PostFx.ini sind hashidentisch zur Baseline. ModProfiler bleibt
`Active=false`, `Instrumented=false`.

Der abschließende rekursive Profilvergleich meldet ausschließlich
`characters.pmc.Hideout.sptUpdateLastRunTimestamp`. Inventar, Gesundheit, Skills,
Quests und Fortschritt stimmen mit der Ausgangssicherung überein. Kein Profil
wurde aus einem Backup überschrieben, um Änderungen zu verbergen.

Messwerkzeuge: Commit `65169f3`, 17/17 automatische Tests einschließlich
negativer Dauer-/Abdeckungsfälle, Syntaxprüfung und unabhängiger Ring-/Datenreview.
Kein Arena-Build oder Deployment. Die Rohwerte aller vollständigen Läufe und
die empfohlene Einordnung wurden durch einen zweiten Agenten nachgerechnet.

Alle Rohdaten und Profil-/Konfigurationssicherungen liegen ausschließlich im
ignorierten `artifacts/zombie-cap-20261013/`; sie sind kein öffentlicher Anhang.
