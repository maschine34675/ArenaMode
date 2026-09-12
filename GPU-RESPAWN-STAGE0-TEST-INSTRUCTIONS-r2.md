# GPU-Nachtest: Respawn-Engine Stufe 0, r2 (Fortsetzung nach dem r1-Stopp)

Arbeitsauftrag auf dem Spiel-PC: zuerst AGENTS.md, den neuesten HANDOFF-Eintrag und
`GPU-RESPAWN-STAGE0-TEST-RESULTS-r1.md` lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Ausschließlich native Bedienung und Beobachtung; keine
Produktionsänderungen, Builds, DLL-Tausche, Harmony-/Speicherinstrumentierung,
Arm-Aufrufe oder erzwungenen Matchereignisse. Launcher aus seinem tatsächlichen
Arbeitsverzeichnis `C:\SPT41\SPT_Runtime` starten (r1-Vorbereitungsfehler).

## Verbindlicher Teststand — unverändert gegenüber r1

- Test-ID: `respawn-stage0-r2`.
- Dieselben Dateien wie in r1, kein Neubau: ArenaMode-Build `b5ff2585f1dea2642835582d7b06bf8e4ec72f65`
  (Code-Stand `f257a78`), Redeploy `main` `287c2d95a4645ae72dbdf44843958781c84b9b60`
  (Code-Stand `c938f5c`). Die Bindungstabelle aus
  `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r1.md` gilt wörtlich (Client
  `A92164BC…`, Core `FA452E5A…`, Redeploy.Client `7EC406A1…`, Arena-Server
  `5A920117…`, Redeploy.Server `53F283D8…`, SAIN, KillcamReplay, DynamicMaps).
- Spätere Repository-Commits (Harness-Reparaturen, Dokumentation) sind kein
  Anlass für einen Neubau; der Dateibindungsnachweis erfolgt über die DLLs.

Hashes vor Start, vor jedem Raid und am Ende prüfen; PID/Startzeit beider
Prozesse, Git-HEAD, Auflösung und DLSS-Modus festhalten.

Zusätzlich darf `BepInEx\plugins\maschine-BotDropDiag.dll` (0.1.0, SHA256
`5B37C035BD860E9EED5A9520EE62BF8D607591A77996142BB80CA18BF48E0985`) installiert sein:
ein reines Log-Plugin (Quelle `maschine-BotDropDiag`) für die separate Surv12-Frage,
ohne Spielwirkung. Seine Zeilen zählen nicht als Warnungen/Fehler dieses Auftrags; sie
werden nur mitgesichert.

## Was r1 ergab und was daraus folgt

Der gewöhnliche Redeploy-Respawn ist in r1 gelungen: Fenster statt Death-Screen,
Markerfolge `Death intercepted` → `Spawn protection active` → `Respawn complete` →
`Spawn protection ended`, KillcamReplay-Abbruch und AutoIFF-/Freecam-Rebind ohne
Exception. Die weiteren Prüfungen an A blieben offen, weil der nackte Ersatzkörper
0,5 s nach Ende des Schutzes erneut starb und das Respawn-Limit 1 den Raid beendete.

**Der r1-Stopbefund ist geklärt und keine Regression dieses Builds.** Kette, im
dekompilierten Spiel nachgelesen: `LocalGame.Stop` → `BotsController.StopGettingInfo`
→ `BotsList.Stop` → `BotOwner.Dispose` für jeden Bot (setzt `Medecine = null`,
disposed `WeaponManager`). Ein Bot, der in diesem Moment noch eine Operation mit dem
Surgical Kit ausführt, tickt während der Ausblendung weiter; das Ende seines
Med-Effekts entfernt das verbrauchte Item (`MedEffect.Residue` →
`MedsInHandsOperation.Finish`) und ruft den Abschluss-Callback des bereits
disposten Bots auf (`BotSurgicalKit+CG_ApplyToCurrentPart.method_1` greift auf
`_owner.WeaponManager` / `_owner.Medecine` zu) → `NullReferenceException`, von
`ItemController.SafeRemoveItemEvent` gefangen und geloggt. Kein Redeploy-, Core- oder
Arena-Frame; nichts in Stufe 0 berührt Stop oder Bot-Disposal. KillcamReplays
verzögerter Stop (19 s Replay) vergrößert nur das Fenster, in dem ein Bot operiert.

Regel für r2: **genau diese Signatur** — erster Stack-Frame
`BotSurgicalKit+CG_ApplyToCurrentPart.method_1`, innerhalb weniger Sekunden nach
`Re-invoking deferred LocalGame.Stop` oder einem nativen Raid-Stop — ist eine
**bekannte Vanilla-Exception**: separat zählen, Stack sichern, **nicht stoppen**.
Jede andere neue Exception stoppt den Lauf weiterhin.

**Korrigierte Erwartung:** `Blocked raid-end black screen while respawn is pending.`
ist im normalen Redeploy-Pfad **nicht** zu erwarten. Core fängt den Tod schon vor
`InitiateGameStopping` ab; der native Stop, der die Ausblendung startet, läuft dann
gar nicht. Der Marker entfällt aus der Sollfolge. Sollte er doch erscheinen, im
Bericht nennen (kein FAIL).

## Schutz, Budget und Belege

**Jede neue Exception (außer der oben benannten), Softlock, Fehlerflut, Profilrisiko
oder Hashwechsel stoppt den gesamten Lauf.** Regulär aussteigen, keine weiteren
Testfälle/Raids; Belege sichern. Bekannte reine Lade-/Culling-Warnungen separat
zählen. Maximal **5 Raids / 40 Minuten aktive Testzeit**; Lade-/Werkzeugzeiten
separat ausweisen. Budgetende: sicher aussteigen, Rest OFFEN. Andere Mods und Audio
unangetastet. Aktuelle Einstellungen vorher sichern; nur die genannten Testwerte
setzen und am Ende auf die gesicherten persönlichen Werte zurückstellen (in r1 war
`MaxRespawnsPerRaid` persönlich 2).

Profile privat nur lesen/kopieren, niemals zurückkopieren oder bearbeiten. A und B
sind gewöhnliche PMC-Raids ohne Profilvergleich; Stash und Charakter müssen danach
normal öffnen. Nach jedem Arena-Raid (C, D) der übliche vollständige Profilvergleich
für PMC und Scav.

## Startmarker (nach dem Spielstart, vor dem ersten Raid)

Müssen stehen: `Redeploy.Core 0.9.0.0 initialized`,
`Death-interception driver registered: ArenaMode (priority 100)`,
`Death-interception driver registered: Redeploy (priority 0)`.

Dürfen nicht stehen (vor dem Raid blockiert berichten):
`Death interception is incomplete — Core stands down`, `… could not be installed`,
`Raid-start patch could not be installed`, `Raid-end patch could not be installed`.

Über den ganzen Lauf zählen (Soll: null): `Respawn refused before any teardown`,
`subscriber … failed` (Core wie Arena), `GameEnd faulted before its first await`,
`Freecam compatibility patches could not be enabled`.

## A — gewöhnlicher Redeploy-Respawn, diesmal mit Zeit für die Prüfungen

Testwerte in F12 → Redeploy: `General / MaxRespawnsPerRaid` = **3**,
`Respawn / SpawnProtectionSeconds` = **30**, `Respawn / RespawnCooldownSeconds` = 5.
PMC-Raid Factory (Tag), Testprofil mit ausgerüsteter Waffe (wie r1). Regulär durch
einen Bot sterben; Fenster mit Cursor, „Respawn near corpse".

Nach dem Respawn **sofort aus der Sichtlinie der Bots in Deckung** und die Prüfungen
innerhalb der 30 Schutzsekunden abarbeiten, jede mit Originalbild:
1. Bewegung (WASD), Sprint, ein Sprung — der Körper reagiert auf Eingaben.
2. Escape-Menü öffnen und schließen; danach Kamera und Eingabe unverändert.
3. Extraktions-Timer/Exfil-Liste sichtbar (native Taste).
4. DynamicMaps-Marker folgt dem neuen Körper.
5. Eigene Leiche looten; die zuvor getragene Waffe muss zurückzuholen sein.

Marker in dieser Reihenfolge: `Death intercepted. Respawns used: 0/3`,
`Spawn protection active for 30s`, `Respawn complete.`,
`Spawn protection ended` (oder `… ended early: weapon equipped` beim Aufheben der
Waffe — das ist korrekt). Ein zweiter Tod ist zulässig und muss erneut das Fenster
liefern (`Respawns used: 1/3`). Danach regulär extrahieren oder per Escape → Exit
verlassen; Hauptmenü, Stash öffnen. Kein `KeyNotFoundException`, kein `Respawn
failed`, kein `Player gave up`.

## B — Raid-Ende während des Cooldowns (Cooldown-Race)

Nur nach sicherem A. `Respawn / RespawnCooldownSeconds` auf **20**. PMC-Raid Factory
(Tag). Sterben, „Respawn near corpse" wählen und **innerhalb der 20 Sekunden** über
Escape → Exit den Raid verlassen (Bestätigung inklusive). Erwartung: Raid endet als
Left, **kein** Respawn läuft mehr an, kein Killed-Panel über dem Exit, Hauptmenü
und Stash normal.

Marker: `External raid end while respawn pending — standing down.` und
`Respawn cancelled: the raid ended during the cooldown.` — **kein** `Spawn protection
active`, **kein** `Respawn complete.`, **kein** `Respawn failed`, **kein**
`Player gave up — ending raid as Killed.` Beide Prozesse laufen weiter.

Ist der Exit im Escape-Menü im toten Zustand nicht erreichbar: bis zum Ende des
Cooldowns nichts weiter tun, den regulären Respawn abwarten und **B als OFFEN** mit
Bild des Menüs melden; nichts erzwingen. Danach einen weiteren kurzen PMC-Raid
(Ready bis Spawn genügt) beginnen und regulär verlassen: kein `Respawn refused`, kein
B-Marker im neuen Raid — das belegt den Reset an der Raid-Grenze. Zählt als Raid.

## C — Arena-Regression: Kit-Swap, Tod, Give up

Nur nach sicherem B. Factory / Deathmatch FFA / Endless / beide Limits Off, etwa
3 Minuten. Erwartung nach dem Spawn: `Arena raid-start kit swap complete: <Kit>.`,
Kit in den Händen, Bots reagieren auf Sicht und Schüsse des Spielers (bleiben sie
vollständig passiv, ist das Beleg für das dokumentierte SAIN-Risiko nach einem
Live-Swap — melden, nicht stoppen). Einmal regulär sterben: `Arena respawn timing: …`,
Kit auf dem neuen Körper, HUD/Scoreboard zählen den Tod. Einmal regulär Give up:
erneuter Respawn. Danach regulär verlassen; Logdelta bis Hauptmenü; Profilvergleich.

Über den Arena-Raid dürfen **nicht** vorkommen:
`Arena respawn finished without the exact new local-player body`,
`stood down — another body replacement is still in flight`,
`live respawn was refused before takeover`,
`Arena event … subscriber … failed`, `Arena … failed:`,
`old-body recovery failed`, `replacement-body recovery failed`.

## D — optional: Rundenpause (nur bei Restbudget)

Factory / Deathmatch FFA / **2 Runden** / Kills Off / 2 Minuten. Der Rundenwechsel
muss `Arena round respawn complete: <Kit>.` liefern, mit Landung an einem Spawnpunkt
und Schutz über die Pause; danach reguläres Ende, Profilvergleich.

## Nicht Teil dieses Laufs

Fail-closed nach einem Patch-Ausfall, ein Aufruf aus einem Event-Handler und eine
abgelehnte Live-Ersetzung lassen sich nativ nicht auslösen; nur die Abwesenheit
ihrer Marker wird gezählt. Nichts davon erzwingen.

## Bericht

`GPU-RESPAWN-STAGE0-TEST-RESULTS-r2.md`, Originalbilder/-clips, Logauszüge und
SHA256-Manifest unter `test-evidence/respawn-stage0-r2/`; vollständige Logs/Profile
nur im ignorierten `artifacts/`. Private Kennungen in gezielten Auszügen redigieren.
Pro Unterfall PASS/FAIL/OFFEN/SKIP, tatsächliche Raids, Zeitbudget, Buildidentitäten,
Steuerhandlungen, Warn-/Fehlerzahlen je Quelle (Redeploy.Core, maschine-Redeploy,
ArenaMode), die separat gezählte Vanilla-Exception, volle relevante Stacks,
Markerzählung und die Profilvergleiche nennen. r1 nicht überschreiben; nur eigenen
Bericht/Belege committen.
