# Native Prüfung: Continuous Healing und Late To The Party

Stand: 2026-10-19. Host **SPT-GPU-2**, ausschließlich dessen Testprofil.
Alle drei Raids liefen im selben EFT-Prozess: **normaler Factory-PMC →
Factory Deathmatch → normaler Factory-PMC**. Die gezielten Hauptpfade sind
positiv belegt; seltene verzögerte Callbacks und direkte LTTP-Löschaufrufe
bleiben ausdrücklich außerhalb dieser nativen Abnahme.

## Build und Installation

Getesteter Commit: `f8f4a48949870005c3beb3084f6d4fc259dd29af`.
Client und BigBrain-Begleiter tragen
`0.1.1+f8f4a48949870005c3beb3084f6d4fc259dd29af`.

| Datei | SHA-256 |
| --- | --- |
| ArenaMode.Client | `DF2C5583CE3BC908E30D698ED7AB6E4EE0FC1E2E0242F7D1B7CB1424B837E9FD` |
| ArenaMode.BigBrainCompat | `F6C971318FA4488A651A559C9F9EE6C07D258C0177E4CB7893475F7DE6569A7A` |
| ContinuousHealing 1.6.3 | `26635E3FED8EFB77D351B6E0825997735E7378C8F124C4B413C13DC909A3EAAF` |
| LateToTheParty-Client 3.1.1 | `0C1D13AE9E2924096D90BE0C3B81000A1910BA8C4A2E35646922EB00F53EE0F4` |
| UiBridge 0.9.4 | `5271E08B36BD4DD49C8D53EEE4D913E6BE7750040CE97027313F3272DB4FAAB0` |

EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`.
Prozessinstanz `b52827b7`, Bridge-Start **22:41:56**. UiBridge bestätigte
**42/42 Plugins und sechs Bibliotheken identisch** zwischen Entwicklung,
Host-Disk und geladenen MVIDs; alle 42 Plugins hatten ihre Initialisierung
abgeschlossen. Die Ready-Meldungen für CH und LTTP erschienen um 22:41:52.
Alle Uhrzeiten unten sind lokale Hostzeit; das Serverrohlog führt UTC.

CH war bereits installiert und blieb unverändert. Die Koordination installierte
LTTP 3.1.1 vorübergehend als unveränderte veröffentlichte Client-/Server-DLLs.
Nur dessen Serverkonfiguration `debug.enabled` wurde für diese Probe auf true
gestellt. LTTPs Zeit-, Entfernungs-, Tür- und Spieler-Drop-Regeln blieben original.
Arena-Server, Redeploy.Core und die permanente UiBridge wurden nicht verändert.

## Ergebnismatrix

| Prüfung | Native Beobachtung |
| --- | --- |
| CH im normalen PMC vor Arena | Ein IFAK-Einsatz heilt vier Gliedmaßen; echter Abbruch beendet die Fortsetzung |
| CH am aktuellen Arena-Spieler | Ein Grizzly-Einsatz heilt vier Gliedmaßen; Start und Effektende erlauben ausdrücklich das fremde Verhalten |
| CH an einem echten Arena-Bot | Nativer Bot-Start und tatsächliches Effektende erreichen beide den Arena-Fallback |
| Zwei Todes-/Respawnfolgen | Zwei Killcam-Replays und zwei abgeschlossene native Körperersetzungen; beide neuen Körper heilen anschließend wieder |
| Verspäteter CH-Callback eines alten lokalen Körpers | Nicht beobachtet; kein nativer Beleg für diesen Sonderfall |
| LTTP scannt eine eigene Waffenquelle | Echte Ausschlussmeldung; originale Quelle bleibt vorhanden; Arena rotiert anschließend selbst |
| Waffenaufnahme und anschließender Drop | Native Aufnahme in freien zweiten Slot; Quelle wird als verbraucht beendet; derselbe Waffen-ID-Drop hat gewöhnlichen ItemController |
| LTTP in normalen Raids vor und nach Arena | Echte Zerstörung von Behälter- und losem Loot in beiden normalen Raids |
| Einstellungen und Prozessabschluss | Alle erfassten Werte wiederhergestellt; beide PMC-Raids extrahiert; EFT sauber beendet |

## Normaler PMC vor Arena

Factory mit niedriger Botzahl, ohne Arena-Aktivierung:
`ArenaSession.IsActive=false`. Das Testprofil trug zunächst nur Messer,
Taschen, Container und Dogtag. Ein vorhandenes IFAK wurde aus dem Stash in
die Taschen bewegt und nativ auf Taste 4 gebunden; es wurde kein Item erzeugt.

Die UiBridge-Gesundheitsprobe beschädigte beide Arme und Beine auf jeweils
20 HP über den nativen Schadenspfad. Godmode verhindert diesen Schaden und
wurde dafür kurz ausgeschaltet und direkt wieder aktiviert. Ein einziger
nativer `SelectFastSlot4`-Befehl um **22:44:43** startete das IFAK. Ein
Zwischenstand zeigte bereits mehrere unterschiedlich weit geheilte Gliedmaßen,
anschließend erreichten beide Arme 60/60 und beide Beine 65/65 HP.

Der erste Abbruchversuch war zu spät und zählt nicht als Abbruchbeleg.
Die Wiederholung um **22:45:54 / 22:45:57** benutzte das IFAK und anschließend
die normale `ToggleShooting`-Eingabe. Danach lag wieder das Messer in der Hand,
der linke Arm blieb bei **27/60**, der rechte bei **20/60**;
CHs unverändert gelesener `CancelRequested`-Wert war true.

LTTP entfernte natürlich regulären Loot, beispielsweise Roubles um
**22:44:45**, ein defektes GPhone X um **22:44:55**, ein PM-Magazin um
**22:45:05** sowie später lose Munition. Es blieb nicht beim Scan oder bei
einer Null-Kandidaten-Meldung. Das Profil extrahierte regulär über **Gate m**;
die normalen Ergebnisschirme und die Behandlung wurden vollständig durchlaufen.

## Arena-Heilung und Körperwechsel

Factory Deathmatch: **vier Teilnehmer**, Free-for-all, endlose Runden ohne
Kill-/Zeitlimit, Pickups an, Killcam fünf Sekunden. Alle drei Gegner wurden
aktiv; laufende Botkämpfe und Ersatzspawns waren vorhanden.

Um **22:47:48** wurden wieder beide Arme und Beine auf 20 HP beschädigt.
Ein einziger Grizzly-Einsatz heilte alle vier vollständig. Die echten Diagnosen
lauteten um **22:47:49** am Start und **22:47:52** am Effektende jeweils
`current local body keeps foreign behavior`. Beim anschließenden nativen
Abbruchversuch um **22:48:27** endete die Behandlung mit wieder gezogenem
Mk 17 und weiterhin **20/60 HP am linken Arm**. Hier wird kein gesonderter
CH-Cancel-Prefix-Diagnoseeintrag behauptet.

Der echte Botkampf lieferte ohne erzwungenen Medizinaufruf die relevanten
Botereignisse: **22:48:41.171** `CH_StartHeal_Patch: native fallback for Arena
bot; alive=True` und **22:48:46.185** `effect removed: native fallback for
Arena bot; alive=True`. Es wurden keine fremden Prefixe oder ursprünglichen
Callbacks über den Inspector aufgerufen. Ein exakt gleichzeitig laufender
lokaler Abbruch und Bot-Effektabschluss wurde nicht gezielt nachgewiesen.

Die zwei Todesfolgen sind unterschiedlich ausgelöst und werden nicht vermischt:

1. **22:52:24.822:** tatsächlicher Tod durch `skitles55`, M4A1, rechtes Bein
   laut `RightLeg`-Trefferangabe. Killcam verwendete 100
   Snapshots und hielt den Respawn bis zum Replay-Ende zurück. Um
   **22:52:35** ersetzte Pipeline #2 den Körper. Ein neuer Grizzly-Einsatz
   heilte anschließend einen auf 20 HP gesetzten Arm und ein Bein vollständig.
2. **22:54:56.883:** Nach weiteren echten Botbeschüssen und erneuter nativer
   Med-Benutzungsanforderung löste der dokumentierte UiBridge-`kill`-Testhook
   den zweiten Tod aus. Dies ist **kein zweiter ausschließlich durch
   Gegnerschaden verursachter Tod**. Killcam nutzte den tatsächlichen letzten
   Angreifer `TakiiNotFound` und erneut 100 Snapshots. Um **22:55:06** endete
   Pipeline #3 mit einem neuen Körper. Auch dieser heilte Arm und Bein von
   20 auf 60/65 HP über einen einzelnen Grizzly-Einsatz.

Kein `retired local body`-Diagnoseereignis trat dabei auf. Der Versuch belegt
zwei reale Körperersetzungen und deren weitere Bedienbarkeit, aber keinen
verspäteten medizinischen Alt-Körper-Callback. Arena endete um **22:55:42**
über das native `Stop(Left)`. Der Server bestätigte die Wiederherstellung des
Arena-Profil-Snapshots um **20:55:43.691 UTC**.

## LTTP und native Waffenquelle

Die erste Platzierung erzeugte **6/6 Pickups**. Um **22:47:30.920** erschien
aus dem natürlichen LTTP-Scan:
`Arena Late To The Party: owned weapon source excluded from foreign cleanup.`

Die ursprüngliche M60-Quelle hatte die Waffen-ID
`6ad681d9598ad04fce005bba`, ein echtes `ObservedLootItem`, den privaten
`ArenaWeaponPickup.PickupOwner` und 14 einzeln aufgelistete erzeugte Itemobjekte
einschließlich Waffe, Magazin, Munition und Anbauteilen. Wiederholtes Lesen
zeigte dieselbe Quelle und Waffen-ID nach dem Scan. Das ist keine umfassende
visuelle Prüfung jedes gepoolten Kindobjekts.

Der erste eigene Rotationsversuch behielt seine Position wegen der vorhandenen
Geometrie. Um **22:48:45.221** rotierte Arena die Waffenposition selbst von
`(-5.890, 0.031, -27.460)` nach `(-6.770, 0.031, 10.010)`. Die alte Quelle
war anschließend freigegeben; ein neues Pickup-Handle gehörte zur dortigen Waffe.
Weitere normale Rotationen liefen später ebenfalls.

Die Annäherung benutzte Teleport nur zur Positionierung und anschließend
echtes Gehen. Godmode verhinderte zunächst die Sammlung, weil Arena Sammler
mit `DamageCoeff=0` ausschließt; nach Abschalten dieser Testhilfe lief der
normale Transfer. Um **22:49:35.897** meldete Arena die lokale Sammlung.
Das Inventar bestätigte M60E6 **`6ad681df598ad04fce005bc9`** im vorher freien
`SecondPrimaryWeapon`, während das Mk 17 im ersten Slot blieb. Das passende
Pickup-Handle hatte danach **`_consumed=true` und `_loot=null`**.

Die M60 wurde anschließend über ihr echtes sichtbares Inventar-Kontextmenü
`DISCARD` abgelegt. Sie verschwand aus dem zweiten Slot und erschien mit
derselben Waffen-ID als normales LootItem mit **`EFT.InventoryLogic.ItemController`**.
Das verbrauchte Arena-Handle blieb beendet und ohne Quelle. Damit sind die
native Übernahme und die anschließende Aufhebung des ursprünglichen
Quellenbesitzes belegt. Eine spätere LTTP-Zerstörung genau dieses Drops wird
nicht behauptet: die unveränderte LTTP-Konfiguration ignoriert Spieler-Drops.

Der natürliche primäre Scanpfad ist geprüft. Ein bereits vorgemerkter direkter
Löschauftrag, das exakte Zeitfenster einer noch ausstehenden Übertragung,
Rollback und Pool-Wiederverwendung wurden nicht künstlich ausgelöst und bleiben
separate Offline-/Sonderfallnachweise.

## Normaler PMC danach und Wiederherstellung

Ohne Prozessneustart startete erneut ein normaler Factory-PMC-Raid.
`ArenaSession.IsActive=false`. Für diesen wurde ein zweites vorhandenes IFAK
ausgerüstet. Um **22:57:42** heilte wiederum ein einzelner nativer Einsatz alle
vier auf 20 HP beschädigten Gliedmaßen vollständig. Die Wiederholung mit
Abbruch um **22:58:15** hinterließ erneut 27/60 und 20/60 HP an den Armen,
das Messer in der Hand und CHs `CancelRequested=true`.

LTTP zerstörte im Folgeraid tatsächlich lose 7.62x39-FMJ-Munition um
**22:57:50.554**, danach Behälterloot und um **22:58:30** ein loses AI-2.
Das bestätigt seine ursprüngliche Wirkung nach Arena. Der Spieler extrahierte
wieder über Gate m und durchlief die Behandlung zurück ins Hauptmenü.

Vor den Änderungen waren die vollständigen CFG-Dateien sowie 224 aktive
Arena- und drei CH-Einstellungen gesichert. `config-pin` protokollierte
Entwicklermodus, Spielmodus und Replaydauer. Der geschützte Factory-Solo-Wert
wurde nach dem Test über das echte Arena-Menü von vier auf **acht** zurückgesetzt.
Anschließend stellte das Ledger alle drei Werte ohne Konflikt wieder her.

Die abschließende Prüfung ergab:

- Alle **224 aktiven Arena- und drei CH-Werte** exakt wie zuvor.
- Alle **232 serialisierten Arena-, drei CH- und 19 Killcam-Einträge** ohne
  Differenz, einschließlich älterer Dateieinträge.
- Beide gebrauchten IFAKs wieder im Stash; die vier getragenen Ausgangsobjekte
  sind dieselben. Der echte Verbrauch und die normale Behandlung in den
  ungeschützten PMC-Testdurchläufen sind normale Änderungen des Testprofils.
- EFT aus dem Hauptmenü über Exit/Yes sauber beendet, danach fünf Sekunden
  bestätigte Unerreichbarkeit der Bridge. Kein Spielneustart zwischen den Raids.

Die Koordination bestätigte danach den gestoppten SPT-Server und entfernte
manifestbasiert **alle acht temporären LTTP-Dateien auf beiden Installationen**
nach Archiv-/Hashprüfung. Vier leere LTTP-Verzeichnisse bleiben zurück;
deren vollständige Entfernung wird nicht behauptet. CH, UiBridge, Core und
Arena-Server blieben bei der gesonderten Abschlussprüfung unverändert.

## Fehlerbefunde und Beleggrenzen

Die Sitzung war nicht vollständig fehlerfrei. Beim Ende des **ersten normalen
PMC-Raids, vor dem Arena-Raid**, erschienen um **22:46:14.245** und
**22:46:14.571** zwei Unity-NullReferenceExceptions in
`BotWeaponSelector.OnWeaponTaken`, über `CG_TryChangeToSlot` und den nativen
FirearmController-Spawn-/Animatorpfad. Die Ursache wurde hier nicht bewiesen.
Diese beiden Ereignisse sind weder als neuer Adapterfehler noch als behoben
eingestuft. Im gesicherten BepInEx-Prozesslog waren dies die zwei Error-Einträge.
Die frühere medizinische `BotFirstAid.CG_ApplyToSelf.method_1`-NRE wird durch
diesen Durchlauf ebenfalls nicht als behoben erklärt.

LTTPs eingeschaltete Diagnose protokollierte mehrfach auf mehrere Frames
verteilte Zugänglichkeits-/Scanarbeit als Warning. Allgemeine Factory-
BoxCollider-/Szenenmeldungen wurden erhalten. Die nativen Quellen liefern
keine pauschale Freigabe künftiger Modversionen, PFT-Squads, aller Waffenbäume
oder aller verzögerten Callback-Reihenfolgen.

## Ignorierte Rohbelege

`artifacts/health-loot-compat-20261019/native/` enthält:

- `startup-baselines.json`, `before-*.cfg`, `before-file-hashes.json`.
- `pmc1-evidence.json`, `arena-med-pickups.json`, `arena-respawns.json`,
  `pmc2-evidence.json`: konkrete Toolantworten, Identitäten und Zustände.
- `test-config-pin.json`, `config-comparison.json`, `after-*.cfg`,
  `restored-and-errors.json`: Wiederherstellung, Fehlerstacks und bestätigtes Ende.
- `native-client.log`, `native-eft.log`, `native-server.log`: vollständige
  gesicherte Dateien; das Serverlog enthält zusätzlich frühere Sitzungen des Tages.

Build-/Deploy- und Bereinigungsmanifeste liegen eine Ebene darüber. Die
Produktionsprüfung und ihre Offline-Grenzen sind im
[Integrationsbericht](HEALTH-LOOT-COMPAT-INTEGRATION-20261019.md) getrennt dokumentiert.
