# Begrenzte Solo-Population

Stand: 2026-09-21. Gilt ausschließlich für ausgehandelte Teilnehmerzahlen in
Deathmatch und Gun Game. Die Zahl enthält den lokalen Spieler auch während
seines Todes/Respawns. Null im Protokoll erhält den bisherigen Ambient-Pfad;
Tactical/PitFireTeam und Team Deathmatch behalten ihre eigenen Spawn-Pfade.

## Besitz und Kapazität

`ArenaSoloPopulation` reserviert vor der Profilanfrage genau einen Platz.
Gesundheitlich lebende Besitzer zählen bereits in `PreActive`. Der Snapshot
vereint das native Register mit den akzeptierten bzw. nach gescheiterter
administrativer Entfernung weiterhin bekannten Körperidentitäten. Ein fehlender
Registereintrag ist kein Todesbeleg. Unlesbare Gesundheit hält weitere Anfragen
an; ein gedrosselter Diagnoseeintrag erklärt diese Sperre.

Die reine Policy hält höchstens `Teilnehmer - 1` Botplätze. Nur Vorbereitungen,
die noch nicht an EFT übergeben wurden, dürfen nach 180 Sekunden auslaufen.
Jede Fortsetzung prüft ihr Ticket erneut. Nach dem nativen Aufruf bleiben
Ausnahmen, fehlende Callbacks oder eine unklare Ablehnung unbegrenzt reserviert.
Ein durch unseren Dispatch-Prefix bewiesener Abbruch vor nativen Zählern darf
freigeben; fremde Prefixe und Ausnahmen beweisen diesen Zustand nicht.

Native Registrierung geschieht vor dem Beobachter. Bei dessen Aufnahme wird
deshalb nur die genaue ankommende Identität aus dem Snapshot ausgenommen und
anschließend gegen das Ticket getauscht. Andere noch unbekannte Ankünfte können
vorübergehend sowohl als Registerkörper als auch als Ticket zählen. Das bremst
Nachfüllen konservativ, erlaubt aber keinen zusätzlichen Platz. Derselbe doppelte
Callback bleibt folgenlos; ein weiterer Körper wird administrativ entfernt.
Scheitert das, bleibt seine Identität im Snapshot, auch nach Verlust seines
Registereintrags. Ein zusätzlicher Körper kann niemals das Ticket des ersten
Körpers freigeben. Der später nachgewiesene Tod des exakten ersten abgelehnten
Körpers darf sein eigenes Ticket abschließen.

Die Solo-Generation wird ausschließlich an wirklichen Raid-Grenzen zurückgesetzt.
`BurstSpawnService.Reset()` während Spielerersatz verändert sie nicht. Späte
Fortsetzungen können keine Profile in eine neue Welt dispatchen; späte Ankünfte
werden administrativ entfernt und niemals in die neue Körperliste übernommen.
Terminale Deathmatch-Ergebnisse halten weitere Vorbereitungen an.

## Labyrinth und Rollen

Bei eingefrorener `ParticipantBosses`-Auswahl gehört ein Platz ausschließlich dem
Boss. Er startet mit `bossTagillaAgro`, danach folgen bei nachgewiesenem Tod
abwechselnd `bossKillaAgro` und Tagilla. Auch bei zwei Teilnehmern entsteht genau
ein Boss neben dem Spieler. Die übrigen Plätze verwenden den begrenzten
PMC-Mindestbestand und anschließend Scavs; PMCs wechseln USEC/BEAR.

Der direkte native Ein-Profil-Pfad erzeugt keine Escort-Welle. Er übernimmt aber
auch nicht `BotBossSpawn.SpawnBossMain`'s Initialisierung: deshalb ruft die
verwaltete Ankunft explizit `bot.Boss.SetBoss(0)` vor der Aufnahme auf. Eine
Ausnahme entfernt diesen Körper und versucht dieselbe Bossrolle erneut.
Rollen-, Seiten-, Profil-ID- und Gesundheitsprüfung gelten vor Aufnahme; native
Platzwahl und deren Filter werden nach der asynchronen Vorbereitung erneut
ausgeführt. An EFT gehen genau ein Profil und ein Punkt.

## Native Grenzen

Ein `[ThreadStatic]`-Scope autorisiert genau das aktuelle Spawner/Data-Paar,
anschließend genau dessen Creator und ein Profil. Der Scope überspannt kein
`await`. Neben `SpawnBotsInZoneOnPositions` werden beide `ActivateBot`-Overloads
und `CreateBot` geschützt, da etwa ABPS unmittelbar den Creator aufruft.
Der separate Profile-Overload bleibt für diese Solo-Auswahl gesperrt; außerhalb
der Auswahl laufen alle neuen Prefixe unverändert durch.

`SoloPopulationReady` wird erst nach erfolgreichem Aktivieren aller erforderlichen
Patches gesetzt. Die Session-Integration muss ausgehandelte Solo-Raids ablehnen,
wenn das nicht gelingt. Während des Ladens gilt die dokumentierte Ausnahme nur
für die bestätigte Auswahl und deren exakt passende Map.

Die installierte `Assembly-CSharp.dll` bestätigt folgende Reihenfolge:

- `BotSpawner.method_7` ruft `method_10` vor seinem ersten `Task.Yield` auf.
- `ActivateBot(data)` ruft `method_0`, dieses wiederum `CreateBot`, jeweils vor
  der ersten asynchronen Unterbrechung auf. Das eine Profil erreicht somit die
  synchronen Besitzprüfungen, bevor die native Spielererzeugung weiterläuft.
- `BotCreatorClient.method_3` setzt `PreActive` vor dem Callback.
- `BotSpawner.ActivateBotCallback` registriert den Körper und installiert seine
  Todesbehandlung über `AfterCreation` vor dem gelieferten Beobachter.
- Native `Kill` setzt die Gesundheit synchron auf tot; der vorhandene
  `ArenaAdministrativeDeathGate` umschließt die Score-/Assist-Unterdrückung.
  `BotOwner.Dispose` kann bei `PreActive` trotzdem früh zurückkehren: ein
  gesundheitlich toter Körper ist kein Beweis vollständiger nativer Entsorgung.

## Belege und verbleibende Abnahme

`tools/SoloPopulationChecks/Verify.ps1` kompiliert die Produktionspolicy,
Solo-Verwaltung, Scope-Klasse, echten Harmony-Prefixkörper und unverändert
extrahierte verwaltete Burst-Methoden gegen kontrollierte native Grenzen.
Die Checks decken Größen 2–24, Vorbereitungen/Timeouts, native Unsicherheit,
PreActive/Registerreihenfolge, Duplikate, gescheiterte Entfernung, Weltwechsel,
Respawn-Reset, Ende, Profile/Platzwahl, Bossrotation/-initialisierung und
Isolation der anderen Modi ab. Kopierte Quelltexte werden in einem separaten
Temp-Verzeichnis mutiert; gezielte Fehlerkontrollen müssen jeweils scheitern.
`NativeContracts.ps1` liest Methodensignaturen und relevante Reihenfolgen direkt
aus der installierten DLL mit Mono.Cecil, ohne sie auszuführen.

Aktueller Lauf: 131 Assertions im Produktionscode-Harness, 11 gezielt scheiternde
Fehlerkontrollen und 17 installierte IL-Verträge bestanden. `leaf-1.1:G1` wurde
mit Exit 0 und `SOLO POPULATION CHECKS PASSED` automatisch gebunden erfüllt.
Die unabhängige Prüfung der nativen Reihenfolge fand die fehlende
Bossinitialisierung und die fremde Duplikat-Abrechnung; beide Befunde wurden
behoben und mit eigenen Negativkontrollen abgesichert.

Kein Test dieses Leafs startet einen nativen Clientbuild, kopiert DLLs oder
zertifiziert Mono/Harmony-Ausführung im Spiel. GPU-Abnahme bleibt erforderlich:
gewählte Gesamtzahlen pro Map, FFA und All-vs-player, Gun Game, Bosswechsel über
mehrere Tode, lokaler Respawn, normale Kontrollraids und Zusammenspiel mit den
installierten Bot-Mods. Native Arbeit ohne jemals auflösbares Ergebnis kann bis
Raid-Ende einen Platz belegen; das wird in den 30-Sekunden-Diagnosen als
`native uncertain` sichtbar. Fremdmods, die vollständig eigene Körper außerhalb
der geprüften nativen Erzeugungskette bauen, sind durch diese Hooks nicht bewiesen.
