# GPU-Nachtest: Respawn-Engine Stufe 0, r3, mit Arena-Gestenabschaltung

Arbeitsauftrag auf dem Spiel-PC: zuerst AGENTS.md, den neuesten HANDOFF-Eintrag und
`GPU-RESPAWN-STAGE0-TEST-RESULTS-r2.md` lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Ausschließlich native Bedienung und Beobachtung; keine
Produktionsänderungen, Builds, DLL-Tausche, Harmony-/Speicherinstrumentierung,
Arm-Aufrufe oder erzwungenen Matchereignisse. Launcher aus seinem tatsächlichen
Arbeitsverzeichnis `C:\SPT41\SPT_Runtime` starten. Spiel und Server frisch starten;
der Client-Build ist neu.

## Verbindlicher Teststand — neuer Arena-Build, Redeploy unverändert

- Test-ID: `respawn-stage0-r3`.
- ArenaMode-Code `a2fcced` auf `fix/offline-harness-contracts` (drei Commits nach
  dem r2-Bericht `7cc2391`). Client-ProductVersion
  `0.1.0+a2fcced88e7f9ca9cf7fdd1eacb7c81525008755`; Server-ProductVersion dieselbe —
  reiner Neubau, Serverquelle seit `f257a78` unverändert.
- Redeploy wie in r1/r2: `main` `287c2d95a4645ae72dbdf44843958781c84b9b60`
  (Code-Stand `c938f5c`), Core `0.9.0+287c2d95…`, Redeploy.Client `0.9.0+c938f5c…`.
- Die Dateien kommen über den Spiegel nach `C:\SPT41`. Vor dem Spielstart alle neun
  Hashes prüfen; stimmen sie nicht, warten und melden, nichts bauen oder kopieren.

| Datei | SHA256 | Bemerkung |
|---|---|---|
| `BepInEx\plugins\maschine-ArenaMode.Client.dll` | `E4CD2ACB1B2230CFE5C78E71815E4EDAB5034056FC2198B099BDF48527EA1BF5` | **neu** (a2fcced) |
| `BepInEx\plugins\maschine-Redeploy.Core.dll` | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` | unverändert |
| `BepInEx\plugins\maschine-Redeploy.Client.dll` | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` | unverändert |
| `SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll` | `71C0AEC170CDBE3DFF19D718B73F1790639728DA618F388CABF4000D960ED19F` | **neu gebaut** (a2fcced), Quelle unverändert |
| `SPT_Runtime\user\mods\Redeploy\maschine-Redeploy.Server.dll` | `53F283D8B8F32EC7A7FB94AA1CC7EBC12115169C74A97E77B4088B0E5F06070B` | unverändert |
| `BepInEx\plugins\SAIN\SAIN.dll` (4.5.1.0) | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` | unverändert |
| `BepInEx\plugins\maschine-KillcamReplay.dll` (0.9.5) | `B5F984E6605F4DB206E69C3180DFEE0D743278796C1038204E67EA161BE66CD5` | unverändert |
| `BepInEx\plugins\mpstark-dynamicmaps\DynamicMaps.dll` (1.2.1.0) | `C41507EB1EDB6D6C8101314FEDF9EB52C874C980E757CBC8348F084D2231B1B8` | unverändert |
| `BepInEx\plugins\maschine-BotDropDiag.dll` (0.1.0) | `0D885B3FADCDF448DA71B1D459079253EC15A6DBDB5934AB4959863C2DD686D0` | reines Log-Plugin, Pflichtbindung (der r2-Stand `5B37C035…` gilt nicht mehr) |

Hashes vor Start, vor jedem Raid und am Ende prüfen; PID/Startzeit beider Prozesse,
Git-HEAD, Auflösung und DLSS-Modus festhalten. Spätere Repository-Commits
(Dokumentation) sind kein Anlass für einen Neubau. Der Hashwechsel der
BotDropDiag-DLL in r2 kam von einem Neubau der Pfadausgabe um 08:42 MESZ auf dem
Entwicklungsrechner; alle vier r2-Raids liefen noch auf dem alten Stand.

## Was r2 ergab und was daraus folgt

**A** gelang als gewöhnlicher Respawn; offen blieben Sprint/Sprung im Schutzfenster,
Exfil-Liste, Leichenloot mit Waffenrückholung und der zweite Tod. Der einzelne
Unity-Error `Coroutine couldn't be started because the the game object 'MenuScreen'
is inactive!` (08:18:46, ohne Stack) ist nicht zugeordnet: kein Arena-/Redeploy-Frame,
und der einzige Mod, der `EFT.UI.MenuScreen` patcht (PitFireTeam), startet seine
Coroutinen auf dem eigenen Plugin-Objekt. In r3 zählt er, wird mit Bild und zehn
Logzeilen davor/danach belegt und stoppt nicht, solange Escape-Menü, Inventar und
Karte bedienbar bleiben.

**B** ist nativ nicht auslösbar: im toten Zustand gibt es keinen erreichbaren Exit.
B entfällt in r3; der Reset an der Raid-Grenze ist mit R bereits belegt.

**C** endete auf `BotFirstAid+CG_ApplyToSelf.method_1`, 364 ms nach Confirm Leave.
Aus dem Dekompilat eingeordnet als dieselbe Vanilla-Race-Familie wie in r1:
`LocalGame.Stop → BotsController.StopGettingInfo → BotsList.Stop → BotOwner.Dispose`
(`BotWeaponManager.Dispose` setzt `Selector = null`, `Medecine` wird disposed); ein
Bot mitten in einer Heilung beendet seine `MedsInHandsOperation` beim nächsten
Health-Tick, und der Abschluss-Callback greift auf den disposeden Owner zu.
`BaseHealthController.ManualUpdate` fängt die Exception (`Debug.LogException`), der
Raid endete regulär, das Profil blieb gleich. Kein Arena-, Redeploy- oder
Core-Frame. Für r3 ist die ganze Familie vorab benannt: eine
`NullReferenceException`, deren erster Frame `BotSurgicalKit+CG_ApplyToCurrentPart`,
`BotFirstAid+CG_ApplyToSelf` oder `BotStimulators+CG_TryApply` (jeweils `method_0`
oder `method_1`) ist, mit `MedsInHandsOperation.Finish` im Stack, innerhalb von 15 s
nach einem nativen Stop bzw. Confirm Leave — **zählen, vollständigen Stack sichern,
nicht stoppen.** Jede andere Exception stoppt wie bisher.

Die Zeile `Arena burst spawn of pmcBEAR failed: The operation was canceled.` sechs
Sekunden nach dem Stop war Abbauriauschen: der Raid war zu Ende, die Generation
längst weitergezählt, nichts wurde wiederholt. Der Build meldet diesen Fall jetzt
als Info `Arena burst spawn of <Rolle> cancelled with the raid.`; er ist nach dem
Raidende erwartet. Ein `Arena … failed:` zählt weiterhin als verbotener Marker.

Das einzige Profil-Delta `/characters/pmc/Hideout/sptUpdateLastRunTimestamp`
schreibt SPTs `HideoutHelper.UpdatePlayerHideout` bei jeder Profilverarbeitung, nicht
Arena. Es ist in r3 als einzig zulässige Abweichung des Gesamtdateivergleichs vorab
benannt; die 16 geschützten Feldvergleiche müssen weiterhin gleich sein.

## Was der Build a2fcced ändert

1. **Gesten-Abschaltung** (`ArenaNoBotGesturesPatch`, F12 → ArenaMode → Bots →
   `NoBotGestures`, Standard an; nicht verstellen). Prefix auf
   `BotPeacefulActions.SetActiveAction`, nur bei aktiver oder scharf geschalteter Arena.
   Hintergrund: die freundliche Geste zwischen zwei nahen Bots (beliebiger Seite) löst
   in Vanilla den Item-Tausch aus, bei dem ein PMC ein zufälliges Item aus dem Secure
   Container wirft — bei SPT sein Surv12 oder AFAK. Der 60-s-Schutztimer der Routine
   vergleicht gegen ein nie gesetztes Feld und ist tot; in der Arena lagen die Kits
   deshalb Sekunden nach dem Rundenstart am Boden (Log vom 13.09., vier Würfe 1,9–11,7 s
   nach dem Spawn).
2. **Corpse-Looting-Sperre erstmals aktiv** (`ArenaNoCorpseLootingPatch`, F12 → Performance →
   `NoCorpseLooting`, Standard an; nicht verstellen). Seit `c8bb849` existierte der Patch,
   war aber nie registriert. Jetzt Prefix auf `BotDeadBodyWork.UpdateCheck`: Bots wählen
   in der Arena keine Leiche mehr zum Durchsuchen. PFT-Teamkameraden bleiben ausgenommen.
3. Die Info-Zeile für einen beim Raidende abgebrochenen Burst-Spawn (oben).

## Schutz, Budget und Belege

**Jede neue Exception außer der vorab benannten Familie, Softlock, Fehlerflut,
Profilrisiko oder Hashwechsel stoppt den gesamten Lauf.** Regulär aussteigen, keine
weiteren Testfälle/Raids; Belege sichern. Bekannte reine Lade-/Culling-Warnungen
separat zählen. Maximal **5 Raids / 40 Minuten aktive Testzeit**; Lade-/Werkzeugzeiten
separat ausweisen. Budgetende: sicher aussteigen, Rest OFFEN. Andere Mods und Audio
unangetastet. Aktuelle Einstellungen vorher sichern; nur die genannten Testwerte
setzen und am Ende auf die gesicherten persönlichen Werte zurückstellen.

Profile privat nur lesen/kopieren, niemals zurückkopieren oder bearbeiten. A ist ein
gewöhnlicher PMC-Raid ohne Profilvergleich; Stash und Charakter müssen danach normal
öffnen. Nach jedem Arena-Raid (E, C, D) der übliche vollständige Profilvergleich für
PMC und Scav, mit dem oben benannten zulässigen Delta.

BotDropDiag-Zeilen (Quelle `maschine-BotDropDiag`, Level Info) sind in diesem Lauf
Beleg, keine Warnungen: `THROW`, `DROPPER`, `SURGERY-START`, `MEDS-FINISH`. Je Raid
zählen: `THROW`-Zeilen gesamt und davon die mit
`BotRandomPlanItemDropper.OnStartPeacefulMove` im Stack.

## Startmarker (nach dem Spielstart, vor dem ersten Raid)

Müssen stehen: `Redeploy.Core 0.9.0.0 initialized`,
`Death-interception driver registered: ArenaMode (priority 100)`,
`Death-interception driver registered: Redeploy (priority 0)`, und im Harmony-Teil
des Startlogs je einmal `Patching void BotPeacefulActions::SetActiveAction(…) with 1
prefixes` (Prefix `ArenaMode.Bots.ArenaNoBotGesturesPatch::Prefix`) sowie `Patching void
BotDeadBodyWork::UpdateCheck() with 1 prefixes` (Prefix
`ArenaMode.Bots.ArenaNoCorpseLootingPatch::Prefix`). Fehlt eine der beiden Zeilen, ist es
nicht der gebundene Build: blockiert berichten.

Dürfen nicht stehen (vor dem Raid blockiert berichten):
`Death interception is incomplete — Core stands down`, `… could not be installed`,
`Raid-start patch could not be installed`, `Raid-end patch could not be installed`.

Über den ganzen Lauf zählen (Soll: null): `Respawn refused before any teardown`,
`subscriber … failed` (Core wie Arena), `GameEnd faulted before its first await`,
`Freecam compatibility patches could not be enabled`, `Arena … failed:`.

## E — Arena Team Deathmatch: keine Kits am Boden (zuerst)

Customs / Team Deathmatch 5v5 / Endless / beide Limits Off, Fläche
`customs-factory-center-teams-v1` — die Konstellation, in der die Würfe beobachtet
wurden; ist Customs nicht anwählbar, Factory Team Deathmatch 5v5 mit gleicher Angabe im
Bericht. Etwa vier Minuten ab `Arena match started (Team Deathmatch)`.

Erwartung und Belege:
1. In den ersten drei Minuten **0** `THROW`- und **0** `DROPPER`-Zeilen mit
   `BotRandomPlanItemDropper.OnStartPeacefulMove` im Stack (vorher: vier in fünf
   Minuten). Jede solche Zeile ist ein FAIL von E; vollständige Zeile sichern.
2. Originalbilder beider Team-Spawnbereiche bei etwa 60 s und 180 s: kein Surv12/AFAK
   am Boden. Kits, die ein Bot nachweislich durch Tod verliert, zählen nicht.
3. Bots kämpfen weiter: Treffer am Spieler oder eigene Treffer, HUD-Zählung; Bild.
4. Kein Bot, der über einer Leiche hockt und plündert (Corpse-Looting-Sperre);
   Beobachtung, Bild falls gesehen — melden, nicht stoppen.
5. `SURGERY-START`/`MEDS-FINISH` dürfen vorkommen (Bots heilen); nur zählen.

Danach regulär verlassen; Logdelta bis Hauptmenü; Profilvergleich. Die verbotenen
Arena-Marker aus r2 gelten unverändert.

## A — gewöhnlicher Redeploy-Respawn, offene Unterfälle aus r2

Testwerte in F12 → Redeploy: `General / MaxRespawnsPerRaid` = **3**,
`Respawn / SpawnProtectionSeconds` = **30**, `Respawn / RespawnCooldownSeconds` = 5.
PMC-Raid Factory (Tag), Testprofil mit ausgerüsteter Waffe. Regulär durch einen Bot
sterben; Fenster mit Cursor, „Respawn near corpse".

Nach dem Respawn sofort in Deckung und innerhalb der 30 Schutzsekunden, jeweils mit
Originalbild oder kurzem Clip:
1. Sprint und ein Sprung — zwei Bilder mit sichtbar veränderter Position/Haltung
   genügen, ein Clip ist besser.
2. Escape-Menü öffnen und schließen; auf den MenuScreen-Error achten (oben).
3. Exfil-Liste (native Taste `O`): Timer **und** Liste sichtbar.
4. Eigene Leiche looten; die zuvor getragene Waffe zurückholen
   (`Spawn protection ended early: weapon equipped` ist dann korrekt).
5. Ein zweiter regulärer Tod: erneut das Fenster, `Respawns used: 1/3`.

Marker wie in r2. Dieser Raid ist zugleich die Gegenprobe für die Gesten-Abschaltung:
`THROW`-Zeilen mit `OnStartPeacefulMove` im Stack sind hier **zulässig** (Vanilla)
und werden nur gezählt — sie belegen, dass die Sperre auf die Arena beschränkt ist.
Danach regulär verlassen; Hauptmenü, Stash öffnen.

## C — Arena FFA: natürlicher Tod (offen aus r2)

Factory / Deathmatch FFA / Endless / beide Limits Off. Nach dem Startkitwechsel
bewusst in offener Sicht stehen bleiben, nicht schießen, bis ein Bot tötet
(Obergrenze vier Minuten; gelingt es nicht, einmal Give up und C-Tod als OFFEN
melden). Erwartung: `Arena death #1 — instant respawn`, `Arena respawn timing: …`,
Kit auf dem neuen Körper, HUD/Scoreboard zählen den Tod. Danach regulär verlassen;
Logdelta bis Hauptmenü; Profilvergleich. Die vorab benannte Exception-Familie beim
Stop zählt, stoppt nicht; die Info-Zeile `cancelled with the raid` ist erwartet.

## D — optional: Rundenpause (nur bei Restbudget)

Factory / Deathmatch FFA / **2 Runden** / Kills Off / 2 Minuten. Der Rundenwechsel
muss `Arena round respawn complete: <Kit>.` liefern, mit Landung an einem Spawnpunkt
und Schutz über die Pause; danach reguläres Ende, Profilvergleich.

## Nicht Teil dieses Laufs

B (Cooldown-Race) ist nativ nicht auslösbar und entfällt. Fail-closed nach einem
Patch-Ausfall, ein Aufruf aus einem Event-Handler und eine abgelehnte Live-Ersetzung
lassen sich nativ nicht auslösen; nur die Abwesenheit ihrer Marker wird gezählt.
Nichts davon erzwingen. Die F12-Werte `NoBotGestures` und `NoCorpseLooting` bleiben
auf Standard.

## Bericht

`GPU-RESPAWN-STAGE0-TEST-RESULTS-r3.md`, Originalbilder/-clips, Logauszüge und
SHA256-Manifest unter `test-evidence/respawn-stage0-r3/`; vollständige Logs/Profile
nur im ignorierten `artifacts/`. Private Kennungen in gezielten Auszügen redigieren.
Pro Unterfall PASS/FAIL/OFFEN/SKIP, tatsächliche Raids, Zeitbudget, Buildidentitäten,
Steuerhandlungen, Warn-/Fehlerzahlen je Quelle (Redeploy.Core, maschine-Redeploy,
ArenaMode), die separat gezählte Vanilla-Familie mit vollen Stacks, die
BotDropDiag-Zählung je Raid, Markerzählung und die Profilvergleiche nennen. r1 und r2
nicht überschreiben; nur eigenen Bericht/Belege committen.
