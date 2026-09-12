# GPU-Nachtest: Respawn-Engine Stufe 0 (Redeploy.Core + ArenaMode), r1

Arbeitsauftrag auf dem Spiel-PC: zuerst AGENTS.md und den neuesten HANDOFF-Eintrag
(„Redeploy.Core stage 0 integrated") lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Ausschließlich native Bedienung und Beobachtung; keine
Produktionsänderungen, Builds, DLL-Tausche, Harmony-/Speicherinstrumentierung,
Arm-Aufrufe oder erzwungenen Matchereignisse. Hintergrund und Begründung der
Änderungen: `..\Redeploy\docs\RESPAWN-API.md`, Abschnitt 2 (Stufe 0) und
Abschnitt 10 (Szenarien), sofern das Redeploy-Repo gespiegelt ist.

## Verbindlicher Teststand

- Test-ID: `respawn-stage0-r1`.
- ArenaMode Source-/Build-Commit: `b5ff2585f1dea2642835582d7b06bf8e4ec72f65`
  (clean beim Build; Code-Stand `f257a78`, danach nur Dokumentation), Branch
  `feature/arena-air-control`. Client-ProductVersion `0.1.0+b5ff2585f1dea2642835582d7b06bf8e4ec72f65`.
- Redeploy `main`: `287c2d95a4645ae72dbdf44843958781c84b9b60` (Code-Stand `c938f5c`).
  Core-ProductVersion `0.9.0+287c2d95a4645ae72dbdf44843958781c84b9b60`,
  Redeploy.Client-ProductVersion `0.9.0+c938f5c43cb311a8a9ba1f81b2eb10573b99b936`.

| Datei | SHA256 | Bemerkung |
|---|---|---|
| `BepInEx\plugins\maschine-ArenaMode.Client.dll` | `A92164BC306762ABD954CF3F2A5AA012FA49E8471626966C5A459A1FEE02843D` | geändert |
| `BepInEx\plugins\maschine-Redeploy.Core.dll` | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` | geändert |
| `BepInEx\plugins\maschine-Redeploy.Client.dll` | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` | geändert |
| `SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll` | `5A9201177DC3E4404995EA314FE48A231712435D5E5EB9E1A081C67734D6C04E` | nur Neubau, ProductVersion `0.1.0+f257a78…`, Quelle unverändert |
| `SPT_Runtime\user\mods\Redeploy\maschine-Redeploy.Server.dll` | `53F283D8B8F32EC7A7FB94AA1CC7EBC12115169C74A97E77B4088B0E5F06070B` | unverändert seit 09.09. |
| `BepInEx\plugins\SAIN\SAIN.dll` (4.5.1.0) | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | unverändert |
| `BepInEx\plugins\maschine-KillcamReplay.dll` (0.9.5) | `B5F984E6605F4DB206E69C3180DFEE0D743278796C1038204E67EA161BE66CD5` | unverändert, relevant für A |
| `BepInEx\plugins\mpstark-dynamicmaps\DynamicMaps.dll` (1.2.1.0)` | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | unverändert |

GPU-Synchronisierung und tatsächliche Hashes vor Start, vor jedem Raid und am Ende
prüfen. **Spiel neu starten** (Client, Core und Redeploy.Client sind neu). Der Server
muss nicht neu gestartet werden: die Arena-Server-DLL wurde ohne Quelländerung neu
gebaut; ein laufender Server hat dieselbe Logik geladen. PID/Startzeit beider
Prozesse, Git-HEAD, Auflösung und DLSS-Modus festhalten. Kein Neubau wegen eines
späteren Dokumentationscommits; die r6/r4-Berichte (`bc7f541`) bleiben unverändert.

## Was sich geändert hat

Redeploy.Core (von Redeploy und ArenaMode gemeinsam genutzt) lehnt einen Respawn
jetzt **vor** jedem irreversiblen Schritt ab, wenn er nicht gelingen kann; jeder
Event-Abonnent läuft isoliert; ein unvollständiger Patch-Satz schaltet Core komplett
ab (fail-closed); Raid-Start und Raid-Ende sind gepaart und setzen den Engine-Zustand
zurück; der Cooldown von Redeploy bricht ab, wenn der Raid währenddessen endet.
ArenaMode verträgt eine Ablehnung ohne Teardown, publiziert nur den verifizierten
Ersatzkörper und isoliert seine eigenen Events. Offline sind Unit-Tests, die
produktionsgebundenen Harnesse und zwei adversariale Reviews grün; **kein
Szenario wurde im Spiel geprüft.** Genau das ist dieser Auftrag.

## Schutz, Budget und Belege

**Jede neue Exception, Softlock, Fehlerflut, Profilrisiko oder Hashwechsel stoppt den
gesamten Lauf.** Regulär aussteigen, keine weiteren Testfälle/Raids; Belege sichern.
Bekannte reine Lade-/Culling-Warnungen separat zählen. Keine neue Exception wegen
ihrer Herkunft für harmlos erklären. Maximal **4 Raids / 30 Minuten aktive
Testzeit**; Lade-/Werkzeugzeiten separat ausweisen. Budgetende: sicher aussteigen,
Rest OFFEN. Andere Mods und Audio unangetastet. Aktuelle Einstellungen vorher
sichern; nur die unten genannten eigenen Testwerte setzen und regulär zurückstellen.

Profile privat nur lesen/kopieren, niemals zurückkopieren oder bearbeiten. A und B
sind gewöhnliche PMC-Raids: dort ändert sich das Profil regulär, kein Vergleich
gefordert — nur, dass Stash und Charakter danach normal geöffnet werden. Nach jedem
Arena-Raid (C, D) der übliche vollständige Profilvergleich für PMC und Scav.

## Startmarker (vor dem ersten Raid, nach dem Spielstart)

Im Client-Log müssen stehen:
`Redeploy.Core 0.9.0.0 initialized`,
`Death-interception driver registered: ArenaMode (priority 100)` und
`Death-interception driver registered: Redeploy (priority 0)`.

Es darf **keine** dieser Zeilen geben — jede davon bedeutet: Core steht still, vor
dem ersten Raid blockiert berichten:
`Death interception is incomplete — Core stands down`,
`… could not be installed`,
`Raid-start patch could not be installed`, `Raid-end patch could not be installed`.

Über den ganzen Lauf zählen und nennen (Soll: null):
`Respawn refused before any teardown`, `subscriber … failed`
(Core wie Arena), `GameEnd faulted before its first await`,
`Freecam compatibility patches could not be enabled`.

## A — gewöhnlicher Redeploy-Respawn (kein Arena)

PMC-Raid Factory (Tag), Testprofil, Redeploy-Einstellungen auf Standard
(`Respawn / RespawnCooldownSeconds` = 5). Regulär durch einen Bot sterben. Erwartung:
kein Death-Screen, sondern das Redeploy-Fenster mit Cursor; „Respawn near corpse"
wählen. Nach dem Cooldown steht der Ersatzkörper nackt mit Spawnschutz.

Marker in dieser Reihenfolge: `Death intercepted. Respawns used: 0/1`,
`Blocked raid-end black screen while respawn is pending.`,
`Spawn protection active for …s`, `Respawn complete.`,
`Spawn protection ended` (oder `… ended early: weapon equipped`).

Prüfen: Eingabe und Kamera am neuen Körper, Sprint/Sprung, Escape-Menü, Extraktions-
Timer sichtbar, eigene Leiche lootbar (Ausrüstung zurück), DynamicMaps-Marker folgt
dem neuen Körper. KillcamReplay darf das Replay über dem Fenster zeigen; sein
Abbruch durch den Respawn darf keine Exception werfen. Danach regulär extrahieren
oder per Escape → Exit verlassen; Hauptmenü, Stash öffnen. Im Log kein
`KeyNotFoundException`, kein `Respawn failed`, kein `Player gave up`.

## B — Raid-Ende während des Cooldowns (Cooldown-Race)

Nur nach sicherem A. Redeploy F12 → `Respawn / RespawnCooldownSeconds` auf **20**
(Testwert, danach auf 5 zurück). PMC-Raid Factory (Tag). Sterben, „Respawn near
corpse" wählen und **innerhalb der 20 Sekunden** über Escape → Exit den Raid
verlassen (Bestätigung inklusive). Erwartung: der Raid endet als Left, **kein**
Respawn läuft mehr an, kein Killed-Panel über dem Exit, Hauptmenü und Stash normal.

Marker: `External raid end while respawn pending — standing down.` und
`Respawn cancelled: the raid ended during the cooldown.` — **kein** `Spawn protection
active`, **kein** `Respawn complete.`, **kein** `Respawn failed`, **kein**
`Player gave up — ending raid as Killed.` Beide Prozesse laufen weiter.

Ist der Exit im Escape-Menü im toten Zustand nicht erreichbar: Bis zum Ende des
Cooldowns nichts weiter tun, den regulären Respawn abwarten und **B als OFFEN** mit
Bild des Menüs melden. Die Zeit bis zum MIA-Timer nicht abwarten und nichts
erzwingen. Danach kurz einen weiteren PMC-Raid beginnen (Ready bis Spawn genügt)
und regulär verlassen: kein `Respawn refused`, kein Marker aus B im neuen Raid —
das belegt den Reset an der Raid-Grenze. Zählt als Raid im Budget.

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
muss `Arena round respawn complete: <Kit>.` liefern, mit Landung an einem
Spawnpunkt und Schutz über die Pause; danach reguläres Ende, Profilvergleich.

## Nicht Teil dieses Laufs

Fail-closed nach einem Patch-Ausfall, ein Aufruf aus einem Event-Handler und eine
abgelehnte Live-Ersetzung lassen sich nativ nicht auslösen (sie brauchen die im
Entwurf beschriebene Probe-Erweiterung, die nicht gebaut ist). Nur die Abwesenheit
ihrer Marker wird oben gezählt; nichts davon erzwingen.

## Bericht

`GPU-RESPAWN-STAGE0-TEST-RESULTS-r1.md`, Originalbilder/-clips, Logauszüge und
SHA256-Manifest unter `test-evidence/respawn-stage0-r1/`; vollständige Logs/Profile
nur im ignorierten `artifacts/`. Private Kennungen in gezielten Auszügen redigieren.
Pro Unterfall PASS/FAIL/OFFEN/SKIP, tatsächliche Raids, Zeitbudget, Buildidentitäten,
Steuerhandlungen, Warn-/Fehlerzahlen je Quelle (Redeploy.Core, maschine-Redeploy,
ArenaMode), volle relevante Stacks, Markerzählung und die Profilvergleiche nennen.
Frühere Berichte nicht überschreiben; nur eigenen Bericht/Belege committen.
