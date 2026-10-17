# Bot-Profile unter Frame-Budget bauen (`ProfileBuildBudgetMs`, 17.10.2026)

Kandidat 9 aus [ZOMBIE-PERFORMANCE-IDEAS-20261017.md](ZOMBIE-PERFORMANCE-IDEAS-20261017.md),
Anlass: der bis dahin unerklärte Hänger der Zombie-Benchmarks.

## 1. Befund

Der Client baut alle Profile einer Serverantwort auf `/client/game/bot/generate`
in dem Frame, in dem die Antwort eintrifft: `ClientBackendSession.LoadBots`
(Dekompilat `EFT/ClientBackendSession.cs:1886-1895`) ist ein einziges
`Select(descriptor => new Profile(descriptor))` über die Deskriptoren, und der
Konstruktor deserialisiert das gesamte Inventar (`ItemDeserializer.CreateItem`
je Item). Im Zombie-Raid ist das der Stapel für die weiteren Spawns, etwa 90 s
nach dem Start. ModProfilers Spitzenliste vom 17.10. auf SPT-GPU-2 zeigt ihn
zweimal: 625 ms um 20:30:07 und 627 ms um 20:47:14, je ohne GC, mit
`ItemDeserializer.CreateItem` als teuerster instrumentierter Methode (über
UIFixes' Postfix sichtbar). Die Benchmarks vom 13.10. hatten denselben Hänger
mit 730 bis 1708 ms bei 102 bis 105 s ([GPU-ZOMBIE-PERFORMANCE-20261013.md](GPU-ZOMBIE-PERFORMANCE-20261013.md),
[ZOMBIE-PERFORMANCE-IDEAS-20261017.md](ZOMBIE-PERFORMANCE-IDEAS-20261017.md) §1). Auf dem langsameren SPT-GPU-1 kostete derselbe Stapel 1763 ms (21:28:34, 50 Infizierte, Referenzlauf vom 17.10.).

## 2. Umsetzung

`Performance/ProfileBuildBudgetPolicy.cs` (rein, in `ArenaMode.Tests` gelinkt)
und `Performance/ProfileBuildBudget.cs` (Harmony). Im aktiven Arena-Raid
ersetzt ein Prefix niedriger Priorität auf `ClientBackendSession.LoadBots` die
Methode durch eine eigene asynchrone: dieselbe Anfrage Feld für Feld (URL,
`BotGenerateRequestParams<List<CountTypeBotWave>>`, `SendRequest.DefaultRetries`)
über das öffentliche `Backend.Send<T>`, danach die Profile einzeln, auf
demselben Hauptthread, unter einem Zeitbudget je Frame:

- Jeder offene Stapel baut je Frame garantiert ein Profil (ein Stapel kann
  nie stecken bleiben, und ein kurzer, dringender Stapel wartet nicht hinter
  einem langen, der früher kam: Unity setzt wartende Fortsetzungen in
  Eingangsreihenfolge fort, ohne den Platz je Stapel gewönne der ältere jeden
  Frame).
- Weitere Profile nur, solange `verbraucht + laufender Durchschnitt ≤ Budget`;
  sonst `await Task.Yield()`. Die wirksame Obergrenze je Frame ist also das
  Budget oder ein Profil je offenem Stapel, je nachdem, was größer ist; bei
  rund 9 ms je Profil baut das 3-ms-Budget genau eines je Frame. Das erste
  `Yield` nach der Antwort kann noch im selben Frame fortsetzen (Unity
  arbeitet die Warteschlange nach `Update` ab, die HTTP-Antwort kommt davor),
  alle weiteren im nächsten; das Budget hängt an `Time.frameCount`, nicht an
  der Zahl der Yields.
- Der laufende Durchschnitt gewichtet neue Messungen mit 1/8.
- Der Aufrufer bekommt weiterhin eine Task, die mit dem ganzen Array
  abschließt: niemand sieht ein Teilergebnis, für das Spiel antwortet der
  Server einige Frames später. Eine Antwort ohne Deskriptoren oder ein
  werfender Konstruktor lassen die Task dort scheitern, wo die native es täte.
- Priorität `Low`, damit der Prefix von `ArenaBotDifficultyClockPatch` auf
  derselben Methode (schreibt `conditions` in `TimedWave`-Einträge um) zuerst
  läuft und seine Liste gesendet wird.
- Technik nach Smoothies `ProfileBudget` (CC BY-NC-SA, nur gelesen); eigene
  Umsetzung aus der dekompilierten Methode. Smoothies Nebenpfade (Profile auf
  einem Arbeitsthread, Spawn-Check-Deckel, Job-Scheduler-Budget) sind
  bewusst nicht übernommen: der erste ist riskant, die anderen beiden
  betreffen den nativen Spawner, den Arenas eigener Wellendirektor ersetzt.

Schalter: Performance › `ProfileBuildBudgetMs`, Standard 3 ms, 0 = nativ (alles
in einem Frame), Bereich 0 bis 20, gilt für den nächsten Stapel. Der
Ladebildschirm-Preload der Zombie-Eröffnungswelle läuft vor dem Start des
exakten Raids und bleibt nativ (dort fällt ein langer Frame nicht auf).
Je Stapel eine Debug-Zeile: Profile, Frames, Bauzeit, längster Frame-Anteil,
Wanduhrzeit von Antwort bis Abschluss.

Erwartung bei ≈ 9 ms je Profil: ein 625-ms-Stapel (≈ 70 Profile) verteilt
sich auf ≈ 70 Frames, bei 80 fps knapp eine Sekunde, auf Host 1 (1763 ms,
≈ 20 fps) auf ≈ 3,5 s; der nächste Spawn kommt um diese Zeit später. Arenas
Zombie-Anforderungsbuch toleriert 60 s bis zum nativen Eintreffen
(`ZombieSpawnService.NativeArrivalTimeoutSeconds`), BurstSpawnService schützt
die Zwischenzeit mit seiner Abkühlung (8 s, unter Volllast 4 s). Fehlerpfade:
die Übernahmeentscheidung fällt bei jeder Ausnahme auf die native Methode
zurück; die laufende Ersatz-Task scheitert dort, wo die native scheiterte
(Anfragefehler, Antwort ohne Deskriptoren → `ArgumentNullException` wie
`Enumerable.Select`, werfender Konstruktor). Hat ein anderer Prefix die
Methode bereits ersetzt (`__runOriginal` false), bleibt sein Ergebnis stehen
und es wird keine zweite Anfrage gesendet. Das Gate ist der exakte gestartete
Arena-Raid (`IsExactActiveRaid`), nicht der bloße `IsActive`-Latch: ein nach
einem verpassten GameEnd hängengebliebener Latch darf den Ladebildschirm des
nächsten normalen Raids (Preload von ≈ 335 Profilen auf Customs) nicht
berühren.

## 3. Tests

Sieben Tests auf der reinen Policy (erstes Profil je Frame immer, Passung
über den Durchschnitt, Frame-Wechsel setzt den Verbrauch zurück und behält
den Durchschnitt, 1/8-Glättung und negative Uhren, Tick-Umrechnung,
Übernahmebedingungen, Budget 0 = eins je Frame). Mutant: ohne die Regel
„erstes Profil je Frame immer" fallen zwei Tests.

## 4. Ergebnis GPU-2

_(wird nach dem Lauf eingetragen)_
