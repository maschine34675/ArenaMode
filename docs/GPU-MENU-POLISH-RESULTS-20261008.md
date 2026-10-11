# GPU-Abnahme Menü-Politur (SPT-GPU-2, 08.10.2026 11:03–11:12)

Sechs Nutzerwünsche aus der Zombie-v2-Abnahme (Branch `feature/menu-polish`, Commits
`cc23559`, `6df0041`, `1a5c2b1`, `7bbfa7d`, `7628a28`, Merge mit `master` `5327906` als
`7308eb1`). Getesteter Client: `0.1.0+7308eb1…`, SHA256
`0A0FE617EC663991AA154E4D602FC0839E11A00D8022F094F18CC98BDDAE2CFD`, lokal nach
`D:\SPT41\BepInEx\plugins` kopiert, per Syncthing in 16 s auf Host 2, `gpu-run restart`
63 s, `uibridge_status` 21/21 identisch (VM-Build = Host-Platte = geladen). Server
unverändert. Nach den elf Review-Korrekturen (Produktionscode nur in den
Caption-Dateien: Zustandsautomat in `ArenaSessionModeCaptionLedger`,
Ladebildschirm-Gate ohne Arm-Flag; sonst Kommentare und Beschreibungstexte) wurde
der Review-Stand `0.1.0+972e4de…`, SHA256
`209625EB0E8D5BA5737C4DF11F7FF7ABDD7D8AA2AB605EB88992759A9578C794`, ebenso deployt
(Sync 15 s, Restart 69 s, 21/21 identisch) und die Kopfzeile auf allen vier
Bildschirmen erneut gelesen (13:3x: Seite nach ARENA-Toggle, Karte, Annahme,
Ladebildschirm je „ARENA ZONE“; keine `caption`-Warnung). Dieser Stand ist der
deployte. Werkzeuge: UiBridge 0.7.4 (`uibridge_snapshot`/`_wait`/`_inspect`/`_hook`/
`_look`/`_teleport`), Windows-MCP-Klicks, `gpu-run raid/leave`.

## Ist-Zustand vor dem Deploy (Client `5327906`, Kartenauswahl, Zombie gewählt)

Kopfzeile „PVE ZONE" (960,76) auf Seiten- und Kartenauswahl; Mutatorenpanel mit
Legende „Green: ON / Grey: OFF / N/A: unavailable" und Fußzeile „Saved with F12 …";
Zombie-Fakten „Survive wave after wave; they only get bigger" und „Instant respawn,
the wave keeps coming" in der 218 px breiten Wertespalte (abgeschnitten); COLORED
TRACERS in Zombie „OFF" (also verfügbar), TEAM LASER COLORS „N/A".

## Ergebnis je Punkt

1. **ANNOUNCER blendet die Kill-Callouts aus.** Deathmatch auf Factory, Announcer
   per Mutator AUS (Log `announcer attached: mode=deathmatch, enabled=False`),
   Godmode, Zielhalten + Feuerhook: ein Kill (rayleefx, AK-74N, score 200).
   `uibridge_inspect` auf `ArenaHud._callout._shownAt` = **-10** (Startwert, das
   Callout wurde nie gezeigt). Einzige Warnung der Runde: `announcer skipped:
   'prepare_to_fight', reason=disabled`. Gegenprobe im zweiten Raid mit Announcer
   AN (`enabled=True`): zwei Kills (Mk 17) → `_shownAt` = 424,7 und Stinger
   `double_kill` um 11:10:34 — das Callout erscheint also weiter, wenn der
   Announcer an ist. Die Killfeed-Zeile selbst wurde nicht erfasst (der Snapshot kam
   erst nach dem Ausblenden; sichtbar war nur die Rivalenzeile „rukira 500").
2. **Mutatorenpanel ohne Legende und Fußzeile.** Snapshot der Kartenauswahl: 16
   Zeilen ab y=560 (Panel-Top 508 + 52), Zeilenabstand 51,5 px, Beschreibungen
   26 px, letzte Beschreibung endet bei 972 < 980 (Panelboden); keine Legenden- und
   keine „Saved with F12"-Zeile mehr. ANNOUNCER-Beschreibung „Stingers, cues and
   kill callouts.".
3. + 4. **Ein Schema für die Modus-Fakten; Zombie passt.** Gelesen je Modus:
   Zombie „Survive the waves" / „Instant" / EQUIPMENT-Wahlzeile (Own gear); Gun Game
   „Kill to advance" / „Instant" / „Weapon ladder"; Tactical 1v5 „Round elimination"
   / „Next round" / „Buy / retain kit" plus Hinweis „5v5 requires PitFireTeam + 4
   teammates."; Team DM zeigt wie bisher seine Regel-Unteransicht („TEAM KILLS: 50",
   „TIME LIMIT: 10 min"), Deathmatch seine („OPPONENTS: Free-for-all"). Alle Werte
   stehen in einer Zeile ohne Auslassungspunkte.
5. **Gefärbte Tracer nur in Teammodi.** COLORED TRACERS in Zombie, Gun Game,
   Tactical 1v5 und Deathmatch „N/A" mit „Requires Team DM or Tactical 5v5 (PFT)."
   (derselbe Grund wie TEAM LASER COLORS); in Team DM „ON" mit „Team colors for
   every bullet." und klickbar. Laser unverändert team-only.
6. **„ARENA ZONE" statt „PVE ZONE".** Seitenauswahl: „PVE ZONE" solange ARENA aus
   ist; nach dem ARENA-Toggle „ARENA ZONE" (960,76, 104 × 20 px); PMC gewählt →
   wieder „PVE ZONE", ARENA unchecked (Rückweg). Kartenauswahl, Bereitschaftsbildschirm
   (MatchMakerAccept) und Ladebildschirm „DEPLOYING TO LOCATION" (TimeHasCome):
   „ARENA ZONE". Nach dem Raid öffnet die Seitenauswahl wieder mit PMC und „PVE
   ZONE" (Arena-Auswahl zurückgesetzt).

## Log und Aufräumen

Keine Error-Zeilen in der gesamten Spielsitzung (Start 11:03:26 bis 11:12), keine
`caption left native`-Warnung, keine Harmony-Patchfehler. `Deathmatch / Announcer`
wurde für Raid 1 auf false gestellt und danach auf den Nutzerwert true
zurückgesetzt; Godmode endet mit dem Raid; Spiel zuletzt im Hauptmenü.

## Nicht geprüft

Hover-Beschreibungen (nicht verdrahtet, siehe `ArenaModeCatalog`-Doku), Tactical
5v5 (PitFireTeam) und die Kopfzeile auf dem PFT-Weg, Zombie-Raid mit „WAVE n"
(HUD-Text, bewusst ungegatet; Code-Pfad unverändert zum v2-Test vom 06.10.), eine
tatsächlich gefärbte Tracer-Salve im Team DM (Renderpfad unverändert).
