# Native Prüfung der optionalen Mod-Adapter — 2026-10-19

## Ergebnis und getrennte Builds

**Looting-Bots-, RTD- und GC-Scope im echten Spiel geprüft. Der erste
Pickup-Durchlauf deckte einen Marker-Konstruktorfehler auf; der korrigierte
Client bestand den anschließenden Sichtbarkeits-, Rotations- und Sammeltest.**

Die folgenden Arena-/PMC-Ergebnisse stammen ausschließlich vom Client
`144a7631340bf2ee2605d10a68bdfc76d49b87c1`. Der spätere Marker-Fix darf diesen
Belegen nicht rückwirkend als getesteter Build zugeordnet werden.

| Prüfung auf 144a763 | Ergebnis |
| --- | --- |
| Drei Adapter laden und besitzen ihre Harmony-Hooks | Bestanden |
| Factory Deathmatch, vier Teilnehmer, vollständige Gegneraktivierung und Ersatzspawn | Bestanden |
| Looting Bots in Arena unterdrückt; verfügbare Scan-Tickets bleiben erhalten | Bestanden |
| RTD F5 und automatische Reinigung gegen eine echte tote Botinstanz | Bestanden: exakte Leiche bleibt erhalten |
| Optionaler Smoothie-GC-Tick in Arena unterdrückt | Bestanden |
| Anschließender normaler PMC-Raid im selben Prozess | Bestanden: echte LB-Suche, Loot und Ausrüstungswechsel; Smoothie-GC-Tick läuft |
| Pickupmarker nach Smoothie-Scan | Fehlgeschlagen auf 144a763; korrigierter Nachtest auf 2624bef unten getrennt dokumentiert |
| RTD zerstört eine Leiche im normalen PMC-Raid | Nicht geprüft: automatische Runde hatte null Kandidaten |
| Vorwerte nach beiden Raids wiederhergestellt | Bestanden: alle 306 erfassten Einstellungen identisch |

## Umgebung und Originalbuild

- Host und Testprofil: **SPT-GPU-2**.
- EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`.
- UiBridge `0.9.4+42bbfb38e6ea3536b579db4312880ebda1f7b0cf`.
- Echte temporäre Testmods: Looting Bots **1.8.0** samt Serverkomponente,
  Remove The Dead **4.1.3**, Smoothie **1.0.8** samt nativer Bibliothek.
- Arena-Server und Redeploy.Core für diesen Test unverändert.
- Alle Zeiten im Bericht sind lokale Hostzeit.

| Originaldatei | SHA-256 |
| --- | --- |
| ArenaMode.Client 144a763 | `2e60d3c37b0533087435379c9e4cf5c66a027f4170c0228b6fd1cd3abacc8347` |
| ArenaMode.BigBrainCompat 144a763 | `c6fdd43fab287f618dd47c1df1b1c9d821a1ddb6ccd0e1ad6c57026b2a9b2bd8` |

Der erste Start um 20:13 bestätigte die drei Ready-Meldungen und native Hooks,
wurde jedoch vor Einstellungen oder Raids durch eine andere Hostnutzung
unterbrochen. Nach erneuter ausdrücklicher Freigabe startete der Test um 20:28
einen neuen Prozess. **Beide nachstehenden Raids liefen im selben Prozess**,
ohne Neustart dazwischen. UiBridge bestätigte wieder **44/44 Plugin-Dateien
und sechs Bibliotheken identisch** zwischen Entwicklung, GPU-Disk und geladenem
MVID; alle 44 Plugins hatten ihren Start abgeschlossen.

## Arena: Factory Deathmatch

Der echte Menüweg wählte Deathmatch, FFA, vier Teilnehmer, endlose Runden,
kein Kill-/Zeitlimit, Pickups an und Killcam aus. Godmode schützte nur den
Testspieler. Die drei Gegner gingen von PreActive nach Active über. Ein mit
14 echten Mk-17-Schüssen getöteter Gegner wurde durch einen neuen Gegner ersetzt.

**Looting Bots:** Um 20:30:35.309 erschien die einmalige Arena-Pausenmeldung.
Die drei echten LootFinder-Komponenten waren vorhanden; beim beobachteten
lebenden Bot blieb `_lootTask=null`. Der ScanScheduler behielt **3/3 Tickets**,
auch nach Tod und Ersatzspawn. Die Unterdrückungsmeldung wird nur beim
tatsächlich geschützten Einstieg erzeugt; die Aussage beruht somit nicht allein
auf fehlenden Lootmeldungen.

**Remove The Dead:** Vor F5 wurde der tote Gegner mit
`HealthController.IsAlive=false` und derselben Root einschließlich BotOwner,
LocalPlayer und Corpse festgehalten. Um 20:31:48.388 löste F5 echte manuelle
Reinigung aus; Arena meldete die Pause. Danach bestanden dieselben
Objekthandles und die aktive Root weiter. Um 20:32:48.486 folgte die automatische
Reinigung, erneut mit demselben erhaltenen Leichnam. RTDs eigener Zähler meldet
auch bei übersprungenem Remove-Aufruf „Removed 1“ und ist **kein
Vernichtungsbeleg**. Entscheidend war der separate Identitäts-/Existenzvergleich.

**Smoothie-GC:** Der optionale Schalter wurde für die Probe gezielt eingeschaltet.
`Diagnostics._raidStarted=true`, während `Cleanup._lastHeapCheck` über
mehrere Beobachtungen und beide RTD-Versuche unverändert **-100** blieb.
Das wurde anschließend mit der positiven Originalwirkung im normalen Raid
verglichen.

**Pickupfehler:** Um 20:30:27.372 stoppte Arena die Pickups mit einer
NullReferenceException; es gab keine Marker in der Szene. Der ursprüngliche
Warning-Eintrag enthielt nur die Exception-Message, keinen Stacktrace.
Die anschließende separate Quell-/DLL-Diagnose identifizierte die verzögerte
TextMeshPro-Awake-Initialisierung durch die neuerdings früh deaktivierte
Marker-Root. Dies ist ein gefundener Fehler des neuen Markercodes, kein
bestandener Renderingtest.

Arena wurde normal über Disconnect und die Ergebnisschirme verlassen.

## Normaler PMC-Raid im selben Prozess

Danach startete ein regulärer Factory-PMC-Raid über die normalen
Raid-Einstellungs-/Versicherungsbildschirme. `ArenaSession.IsActive=false`.
Godmode blieb eine temporäre Testhilfe; das Profil trug seine eigene Ausrüstung.

Looting Bots begann ab 20:34:27 echte Scans mit Ergebnismengen von **250, 183
und 239 Objekten**, später 162 und 76. Bots navigierten zu Behältern, plünderten
Munition und andere Gegenstände und tauschten erfolgreich einen SSh-68 gegen
einen gefundenen ZSh-1-2M-Helm. Ein echter Loot-Task war vorhanden; die
Live-Botliste zeigte die **Looting**-Ebene. Das belegt die wieder aktive
Originalfunktion deutlich stärker als die Abwesenheit einer Arena-Meldung.

Smoothies `Cleanup._lastHeapCheck` stieg nun von **353.242828 auf
368.252838**; `_raidStarted` war weiter true. Der optionale Original-Tick wurde
also wieder ausgeführt, ohne Methoden über den Inspector aufzurufen.

RTDs automatische Runde lief um 20:34:45 mit **null toten Kandidaten**.
Das bestätigt seinen Timer, aber keine physische Leichenvernichtung. Dafür
wurde keine zusätzliche Jagd oder künstliche Profil-Ausrüstung erzeugt.

Der Testspieler extrahierte regulär über **Gate m** und durchlief die
Ergebnisschirme zurück ins Hauptmenü.

## Logs und Grenzen

Der bekannte Pickup-NRE ist der konkrete neue Defekt dieses Durchlaufs.
Keine Contractwarnung und kein Runtimefehler der drei Adapter wurde beobachtet.
Im normalen PMC-Log meldete Looting Bots erwartbar, dass Tagilla wegen seiner
Bottyp-Einstellung nicht plündert. Die zusätzlichen EFT-Meldungen zu
JetMaterial, Factory-Spawnmarker-Korrekturen und dem Transit-BoxCollider traten
auch im normalen Raid auf; ihre genaue Ursache wurde hier nicht neu bewiesen.
Native Waffenmodell-/Animationshinweise bleiben ebenfalls getrennt vom
Adapterergebnis. Die Server-/Client-/EFT-Rohlogs wurden gesichert.

Nicht Gegenstand dieser kurzen Probe: PFT-Kommandos, Looting Bots 1.8.1 RC,
Langzeitrunden, Optikwechsel und RTD-Vernichtung im normalen PMC-Raid.
Der fehlgeschlagene Marker-Durchlauf auf 144a763 belegt insbesondere keine
Pickupsichtbarkeit oder statische Culling-Ausnahme; dafür gilt nur der
nachfolgende Nachtest.

## Einstellungen und Übergabe vor dem Marker-Nachtest

Alle sechs gepinnten Werte wurden zurückgestellt: RTD-Debug aus, Intervall
15 Minuten, Entfernung 30; Smoothies optionaler GC-Schalter aus;
Looting-Bots-Loglevel Default; Arena-Modus Gun Game. Der geschützte
Factory-Solo-Teilnehmerwert wurde über das echte Menü von vier auf den
vorherigen Wert **acht** zurückgestellt.

Der abschließende vollständige Vergleich ergab **null Differenzen**:
224 Arena-, sechs RTD-, 39 Smoothie- und 37 Looting-Bots-Einstellungen.
Godmode endete mit dem Raid. EFT wurde anschließend aus dem Hauptmenü sauber
beendet; UiBridge bestätigte fünf Sekunden Nichterreichbarkeit.
Server und temporäre Testmods blieben auf ausdrückliche Anweisung für den
Marker-Nachtest installiert. Dies ist noch keine abschließende Mod-Bereinigung.

## Korrigierter Markerbuild

Der zweite Prozess ab 20:40 verwendete nachgewiesen
`0.1.0+2624befe27bebbf4af12d27e3ec38f0ff0b6ccaa`. Erneut stimmten alle
44 Plugin-Dateien und sechs Bibliotheken zwischen lokalem Stand, GPU-Disk
und geladenen MVIDs überein. Arena-Server und Redeploy.Core blieben unverändert.
Die Adapterpolitik wurde gegenüber dem ersten Test nicht verändert.

| Korrigierte Datei | SHA-256 |
| --- | --- |
| ArenaMode.Client 2624bef | `42de40f1d04670fba861e302360e122b3133ca053ab6d194530bf394675b1e4e` |
| ArenaMode.BigBrainCompat 2624bef | `f732d5856545c9cb1cec8daa69cba347323a9e517567edca42324a81890e10f3` |

Der Nachtest verwendete nochmals Factory-Deathmatch mit vier Teilnehmern,
Pickups an und Killcam aus. Diesmal wurden nur der Arena-Modus und der
Teilnehmerwert verändert; die bereits zurückgestellten Fremdmod-Diagnosen
blieben auf ihren Vorwerten.

**Marker und Culling:** Um 20:42:16.249 wurden im ersten Versuch **6/6 Pickups**
platziert. Smoothies `DepthCulling._scanned=true` wurde vor dem sichtbaren
Nachweis und erneut danach beobachtet. Nach der ersten Positionsrotation
hatten alle **31 erfolgreich abgefragten Marker-Renderer Layer 15**. Sowohl
Smoothies Skip-Maske `818061568` als auch die Kamera-Culling-Maske `-16777249`
enthielten dessen Bit. Das separate Bild `final-shield-visible.png` zeigt bei
bereits abgeschlossenem Scan das cyanfarbene SHIELD-Label und seinen Lichtstrahl. Es belegt
diesen sichtbaren Marker, nicht sechs gleichzeitig sichtbare Pickups.

Das native M60-LootItem blieb separat: seine Root hatte vor und nach der
Rotation **Layer 22 und keinen Parent**. Sie wurde also nicht in den privaten
Markerbaum umgehängt. Damit wird keine vollständige Prüfung sämtlicher
Kindobjekte des gepoolten Waffenmodells behauptet.

**Rotation und Aufsammeln:** Nach etwa 60 Sekunden wechselten die Pickups ihre
Positionen; eine zweite Rotation lief ebenfalls. Veraltete Inspector-Handles
auf die ersetzten Marker wurden nicht als Fehler gewertet. Nach einer
Positionierung in der Nähe und anschließendem echten Gehen sammelte der
Testspieler um **20:43:46.559** das Damage-Pickup; ein Bot sammelte um
**20:44:15.973** das Health-Pickup. Diese Sammlung ist durch Bewegung und
native Sammellogs belegt, nicht durch einen erzwungenen Pickup-Aufruf.

`final-powerup-collected.png` zeigt dagegen nur eine Wand und das HUD nach
einem späteren Respawn; es ist **kein visueller Buffnachweis**. Der lokale Tod
um 20:44:00 beendete den laufenden Buff. Seine volle Dauer oder konkrete
Schadensverdopplung waren nicht Bestandteil dieses Kompatibilitätstests.

Die ursprüngliche Pickup-Konstruktor-NRE trat nicht wieder auf. Die
Looting-Bots-Pausenmeldung erschien auch in diesem Arena-Raid. Bestehende
Szenenwarnungen einschließlich fehlender `extractor_smoke`-Skripte wurden
gesichert, ohne ihnen hier eine neue Ursache zuzuschreiben. Zusätzlich trat
um **20:44:34.262**, nach dem regulären `Stop(Left)` um 20:44:33, eine einzelne
Unity-NRE in `BotFirstAid.CG_ApplyToSelf.method_1` auf. Der Stack führt über
`MedsInHandsOperation.Finish/OnRemoveItem` und den nativen
`MedEffect.Residue/ManualUpdate`-Pfad; er enthält keinen Pickup- oder
Adapterframe. Das ist ein getrennt dokumentierter Teardown-Befund mit noch
ungeklärter Ursache, keine Wiederholung des behobenen Markerfehlers.
Dieselbe medizinische Callback-Familie wurde bereits vor diesen Adaptern in
`GPU-AKIMBO-MUTATOR-RESULTS-20261005.md` und `BLEEDING-NRE-REVIEW.md`
festgehalten. Die Wiederkehr beweist weder eine Verursachung noch eine
Mitverursachung durch die neuen Adapter; eine neue allgemeine Medizinänderung
wurde aus dieser einzelnen Exit-Beobachtung nicht abgeleitet.

## Endgültige Wiederherstellung und Hostübergabe

Nach normalem Verlassen des Nachtests wurde der Factory-Solo-Wert über das
echte Menü wieder auf **acht**, der Arena-Modus auf **Gun Game** gestellt.
Der erneute vollständige Vergleich aller **306 Einstellungen** ergab null
Differenzen zu den vor diesem Durchlauf gesicherten Werten. Die Testhilfe war
beendet, EFT wurde aus dem Hauptmenü sauber geschlossen und UiBridge blieb
bestätigt fünf Sekunden unerreichbar. Anschließend wurde der SPT-Server
sauber per Ctrl-C mit Exitcode 0 beendet.

Der Host wurde ausdrücklich zur manifestbasierten Entfernung der temporären
Testmods zurückgegeben. Diese Entfernung gehört zur getrennten
Integrations-/Wiederherstellungsprüfung; der Native-Tester entfernte keine
fremden Dateien selbst.

Die koordinierende Sitzung bestätigte anschließend erneut, dass Spiel und Server
auf GPU2 sowie lokal beendet waren, und führte die Entfernung aus. **Alle zwölf
temporär angelegten Nutzdateien sind auf beiden Installationen entfernt; alle
sechs zuvor nicht vorhandenen Fremdmod-Konfigurationen ebenfalls.** Eine zusätzliche
Suche in den Plugin- und Servermod-Verzeichnissen fand keine duplizierten Test-DLLs.
Die acht geprüften Arena-Client-/Companion-, Core- und Serverdateien haben unverändert
die erwarteten Hashes des korrigierten Deployments. Eine zeitversetzte erneute
Prüfung bestätigte denselben Zustand; nur leere Testverzeichnisse bleiben zurück.

Der strenge Hashabgleich hielt zunächst bei Looting Bots' neu serialisierter
Server-JSON an. Vor dem Entfernen wurden beide Fassungen gesichert und vollständig
als JSON mit dem unveränderten Paketoriginal verglichen: beide ursprünglichen
Boolean-Werte waren identisch, nur die Dateidarstellung wich ab. Das ursprüngliche
Stagingmanifest wurde nicht nachträglich verändert.

Abschluss der Arbeitsprüfung: **12 erfüllt, 0 offen, 0 verworfen**. Diese Zahl
bezeichnet die Implementierungs-, Prüf- und Bereinigungsaufgaben dieses Auftrags,
nicht eine fehlerfreie Gesamtabnahme sämtlicher Modkombinationen. Die oben genannte
medizinische Exit-NRE und die ausdrücklich nicht geprüften Fälle bleiben getrennt
dokumentiert.

## Ignorierte Rohbelege

Unter `artifacts/gpu-optional-compat-20261019/`, nicht zur Veröffentlichung:

- `deployment.json`, `temporary-mod-manifest.json`, `pickup-fix-deployment.json`.
- `01-startup-status.json`, `02-startup-adapters.json`: erster, unterbrochener Start.
- `resumed-startup-status.json`, `arena-scenario/`, `ordinary-pmc-scenario/`.
- `arena-before-rtd-*.json`, `arena-after-rtd-manual-*.json`,
  `arena-after-rtd-auto-*.json`: exakte Körper- und GC-/Schedulerbelege.
- `pmc-initial-*.json`, `pmc-positive-*.json`: Originalwirkung im normalen Raid.
- `resumed-before-*.json`, `resumed-restored-*.json`,
  `resumed-test-config-pin.json`, `resumed-config-comparison.json`.
- `resumed-merged-warnings.json`, `native-original-client.log`,
  `native-original-server.log`, `native-original-eft.log`.
  `native-original-merged.json` ist dagegen nur die erste paginierte
  zusammengeführte Teilmenge und wird nicht als vollständiger Logexport gewertet.
- `final-startup-status.json`, `final-pickup-scenario/`, `final-initial-*.json`.
- `final-after-rotation-renderer-layers.json`, `final-end-state-*.json`,
  `saved-finalLayer3.json`: erfolgreiche Layer-, Culling-, Waffenroot- und
  Sammel-/Rotationsbelege. Frühere Abfragen mit veralteten Handles werden
  nicht als erfolgreicher Vollnachweis verwendet.
- `final-shield-visible.png`: sichtbares Label und Lichtstrahl nach dem Scan.
- `final-powerup-collected.png`, `final-death-context.json`: getrennte
  Nachrespawn-Aufnahme und Kontext, kein sichtbarer Buffbeleg.
- `final-before-*.json`, `final-test-config-pin.json`, `final-restored-*.json`,
  `final-config-comparison.json`, `final-main-menu-before-quit.json`,
  `final-game-stopped.json`.
- `final-merged-warnings.json`, `native-final-client.log`,
  `native-final-server.log`, `native-final-eft.log`.
- `server-config-normalization-review.json`, `restoration-verification.json`:
  geprüfte JSON-Neuspeicherung und abschließende Entfernung/Hashkontrolle.
