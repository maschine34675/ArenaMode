# GPU-Nachtest: Respawn-Engine Stufe 0, r4 (offene Fälle aus r3, gleicher Build)

Arbeitsauftrag auf dem Spiel-PC: zuerst AGENTS.md, den neuesten HANDOFF-Eintrag und
`GPU-RESPAWN-STAGE0-TEST-RESULTS-r3.md` lesen. Repo `C:\SPT41\Development\ArenaMode`,
Installation `C:\SPT41`. Ausschließlich native Bedienung und Beobachtung; keine
Produktionsänderungen, Builds, DLL-Tausche, Harmony-/Speicherinstrumentierung,
Arm-Aufrufe oder erzwungenen Matchereignisse. Launcher aus `C:\SPT41\SPT_Runtime`
starten; Spiel und Server frisch starten.

## Verbindlicher Teststand — unverändert gegenüber r3

- Test-ID: `respawn-stage0-r4`.
- Kein Neubau. Die Bindungstabelle aus `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r3.md`
  gilt wörtlich: Client `E4CD2ACB…` und Server `71C0AEC1…` (beide
  `0.1.0+a2fcced…`), Core `FA452E5A…`, Redeploy.Client `7EC406A1…`, Redeploy.Server
  `53F283D8…`, SAIN, KillcamReplay, DynamicMaps, BotDropDiag `0D885B3F…`.
- Spätere Repository-Commits (HUD-Arbeit, Dokumentation, dieser Auftrag) sind kein
  Anlass für einen Neubau; der Dateibindungsnachweis erfolgt über die DLLs. Hashes
  vor Start, vor jedem Raid und am Ende prüfen; ein Wechsel stoppt den Lauf.

## Was r3 ergab und was daraus folgt

**E1 ist bestanden:** null `THROW`/`DROPPER` mit `OnStartPeacefulMove` im ganzen
TDM-Raid, natürlicher Tod mit Arena-Respawn in 1,69 s, Profil gleich. Die
Gesten-Abschaltung gilt damit als belegt; sie wird nicht wiederholt. Offen bleiben
nur die beiden Spawnzonenbilder (E2) — sie sind reine Beleglücke und werden in r4 als
Nebenprodukt von C mit erhoben (unten).

**A und C erreichten keinen natürlichen Tod.** In zehn Minuten Factory ist der
Spieler nicht gestorben; in C stand er in einer Ecke. Das ist kein Softwarebefund,
sondern eine Frage des Vorgehens. r4 schreibt deshalb vor, den Tod zu suchen (unten).

**Der Stopp in C** waren zwei SAIN-Errorzeilen ohne Exception. Einordnung aus dem
SPT-Client (`SPT.Custom.Patches.CustomAiPatch`) und der Serverkonfiguration: SPT gibt
zehn Prozent der Scavs einen Spielernamen (`bot.json chanceAssaultScavHasPlayerScavName`),
der Client behandelt sie als simulierte Player-Scavs und würfelt ihr Gehirn aus
`playerScavBrainType` — auf Factory `assault 1, bossKilla 1, pmcBEAR 2, pmcUSEC 2`.
Fällt `bossKilla`, lehnt SAIN 4.5.1 dieses Gehirn für einen PlayerScav ab und legt
seinen Bot-Component ab; der Bot läuft dann auf dem Vanilla-Killa-Gehirn weiter.
Das ist ein Konfigurationskonflikt zwischen SPT-Standard und SAIN, tritt in jedem
Raid mit rund einem von sechzig Scavs auf und hat mit Arena oder Redeploy nichts zu
tun. Für r4 vorab benannt: das Paar `Updated player scav bot to use: bossKilla brain`
und darauf `[SAIN] … IsAssignedBrainAllowed … Init SAIN ERROR, Disposing…` —
**zählen, Zeilen sichern, nicht stoppen.** Jeder andere SAIN-Error stoppt.

**Nebenbefund in A** (gewöhnlicher Raid, keine Arena-Beteiligung): Bot
`openingspark860` hat 22 Sekunden lang in jedem Frame versucht, mit dem Surv12 eine
Operation an der **Brust** zu beginnen (861 `SURGERY-START` mit `using=False`); der
Start wurde jedes Mal verweigert. Ursache offen, vermutlich ein Vanilla-Zustand
„Brust zerstört, Bot lebt". In r4 nur zählen und, falls es wieder vorkommt, die
erste und letzte Zeile plus die letzte `MEDS-FINISH` desselben Bots davor sichern.

Weiterhin vorab benannt (aus r3): die Meds-Callback-Familie beim nativen Stop
(`BotSurgicalKit+CG_ApplyToCurrentPart`, `BotFirstAid+CG_ApplyToSelf`,
`BotStimulators+CG_TryApply`, `method_0`/`method_1`, `MedsInHandsOperation.Finish`
im Stack, innerhalb 15 s nach Stop/Confirm Leave) und das Profil-Delta
`/characters/pmc/Hideout/sptUpdateLastRunTimestamp`; der einzelne
`MenuScreen`-Coroutine-Error zählt, stoppt aber nicht, solange das Menü bedienbar
bleibt. Die Info-Zeile `Arena burst spawn of <Rolle> cancelled with the raid.` ist nach
dem Raidende erwartet.

## Den Tod suchen — Vorgabe für A und C

Der Tod muss nativ durch Bots eintreten, aber er darf gesucht werden:

- **Ohne Rüstung, ohne Helm** in den Raid (nur die Waffe ausrüsten).
- Nicht in Deckung stehen, nicht schießen, nicht ducken. **Zu den Bots gehen**,
  nicht warten: auf Factory Richtung Büro-/Gate-Seite und die Hallenmitte; sprinten
  (Geräusch), in offener Sicht stehen bleiben, sobald ein Bot in der Nähe ist.
- Sind nach drei Minuten keine Bots in Sicht, weiter durch die Hallen laufen. Die
  Obergrenze je Versuch ist **sechs Minuten** ab bestätigtem Raidstart. Danach den
  Fall als OFFEN mit Bild und Bewegungsbeschreibung melden und regulär verlassen.

## Schutz, Budget und Belege

Wie r3: **jede neue Exception außer den vorab benannten Fällen, Softlock,
Fehlerflut, Profilrisiko oder Hashwechsel stoppt den gesamten Lauf.** Maximal
**5 Raids / 40 Minuten aktive Testzeit**; Lade-/Werkzeugzeiten separat. Aktuelle
Einstellungen sichern, nur die genannten Testwerte setzen, am Ende zurückstellen.
Profile nur lesen/kopieren. A ist ein gewöhnlicher PMC-Raid ohne Profilvergleich;
nach jedem Arena-Raid (C, D) der vollständige Profilvergleich mit dem zulässigen
Delta. BotDropDiag-Zeilen sind Beleg, keine Warnungen; je Raid `THROW`/`DROPPER`
gesamt und davon mit `OnStartPeacefulMove` zählen (Soll in Arena-Raids: 0).

## Startmarker

Wie r3, einschließlich der beiden Harmony-Zeilen
`BotPeacefulActions::SetActiveAction … 1 prefixes` und
`BotDeadBodyWork::UpdateCheck … 1 prefixes`. Verbotene Marker und Zählmarker
unverändert.

## A — gewöhnlicher Redeploy-Respawn: die fünf offenen Unterfälle

Testwerte in F12 → Redeploy: `General / MaxRespawnsPerRaid` = **3**,
`Respawn / SpawnProtectionSeconds` = **30**, `Respawn / RespawnCooldownSeconds` = 5.
PMC-Raid Factory (Tag), nur die MP5 ausgerüstet, keine Rüstung. Den Tod suchen
(oben). Fenster mit Cursor, „Respawn near corpse".

Nach dem Respawn innerhalb der 30 Schutzsekunden, jeweils mit Originalbild oder
kurzem Clip:
1. Sprint und ein Sprung — zwei Bilder mit sichtbar veränderter Position/Haltung,
   besser ein Clip.
2. Escape-Menü öffnen und schließen; auf den MenuScreen-Error achten.
3. Exfil-Liste (native Taste `O`): Timer **und** Liste sichtbar.
4. Eigene Leiche looten; die MP5 zurückholen
   (`Spawn protection ended early: weapon equipped` ist dann korrekt).
5. Ein zweiter regulärer Tod: erneut das Fenster, `Respawns used: 1/3`.

Marker wie in r2/r3. Danach regulär verlassen; Hauptmenü, Stash öffnen.

## C — Arena FFA: natürlicher Tod, dazu die E2-Bilder

Factory / Deathmatch FFA / Endless / beide Limits Off / Whole map. Nach dem
Startkitwechsel den Tod suchen (oben), **in der Hallenmitte, nicht in einer Ecke**.
Bei etwa **60 s und 180 s** nach `Arena match started` je ein Originalbild des
eigenen Spawnbereichs mit Blick auf den Boden (ersetzt die fehlenden E2-Bilder in der
FFA-Variante; kein Surv12/AFAK am Boden, das nicht von einer Leiche stammt).
Erwartung beim Tod: `Arena death #1 — instant respawn`, `Arena respawn timing: …`,
Kit auf dem neuen Körper, HUD zählt den Tod. Gelingt der Tod nicht in sechs
Minuten, einmal Give up und den Tod als OFFEN melden. Danach regulär verlassen;
Logdelta bis Hauptmenü; Profilvergleich.

## D — Rundenpause (nach C, nicht mehr optional)

Factory / Deathmatch FFA / **2 Runden** / Kills Off / 2 Minuten. Der Rundenwechsel
muss `Arena round respawn complete: <Kit>.` liefern, mit Landung an einem Spawnpunkt
und Schutz über die Pause; danach reguläres Ende, Profilvergleich.

## Nicht Teil dieses Laufs

E wird nicht wiederholt. B (Cooldown-Race) ist nativ nicht auslösbar. Fail-closed,
Event-Reentrancy und abgelehnte Live-Ersetzung werden nur über die Abwesenheit ihrer
Marker gezählt. Die F12-Werte `NoBotGestures` und `NoCorpseLooting` bleiben auf
Standard; die SPT-Konfiguration (`bot.json`) wird nicht angefasst.

## Bericht

`GPU-RESPAWN-STAGE0-TEST-RESULTS-r4.md`, Belege und SHA256-Manifest unter
`test-evidence/respawn-stage0-r4/`; vollständige Logs/Profile nur im ignorierten
`artifacts/`. Pro Unterfall PASS/FAIL/OFFEN/SKIP, tatsächliche Raids, Zeitbudget,
Buildidentitäten, Steuerhandlungen, Warn-/Fehlerzahlen je Quelle, die vorab benannten
Fälle mit vollen Zeilen, BotDropDiag-Zählung je Raid, Markerzählung und die
Profilvergleiche. r1 bis r3 nicht überschreiben; nur eigenen Bericht/Belege
committen.
