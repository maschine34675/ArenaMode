# Kompakte Arena-UX — GPU-Nachweise vom 26.10.2026

Host: **SPT-GPU-2 / host2**, Profil `SPT-GPU-2`, Menüprüfung bei **1920 × 1080**. Native Funktionsprüfung, abschließende Session-Rückkehr, Datenvergleich und Bereinigung bestanden. Implementierung und Offline-Ergebnisse stehen im [Implementierungsbericht](COMPACT-UX-20261026.md).

| Geprüft | Ergebnis / Beleg |
|---|---|
| Einstieg und Neustart-Bestätigung | `entry-final`: zwölf Schritte, drei Erwartungen bestanden. Einstiegshinweis, kompakte Sperrbox, Countdown und Ablauf der Bestätigungsfrist sichtbar. [Ablauf](../artifacts/compact-ux-20261026/entry-final/report.md), [Sperrbox](../artifacts/compact-ux-20261026/entry-final/compact-entry.png), [Countdown](../artifacts/compact-ux-20261026/entry-final/restart-countdown.png) |
| Gun Game, Deathmatch, Team DM, Domination | Endloshinweise, Friendly Fire und Domination mit drei Punkten geprüft. [Erster Moduslauf](../artifacts/compact-ux-20261026/modes/report.md) |
| Tactical, Zombies und kartenspezifische Punkte | Fortsetzung mit 18 erfolgreichen Schritten: Tactical nennt Arena-Geld; Zombies ein Leben und Wellenauffüllung; Domination zeigt einen Punkt auf Equator und zwei auf Air Pit. [Fortsetzung](../artifacts/compact-ux-20261026/modes-rest/report.md), [Tactical](../artifacts/compact-ux-20261026/modes-rest/tactical.png), [Zombies](../artifacts/compact-ux-20261026/modes-rest/zombies.png) |
| Unzulässige Karte nach Moduswechsel | Equator → Tactical entfernt die Auswahl und zeigt einen kurzen Grund. [Screenshot](../artifacts/compact-ux-20261026/modes-rest/map-invalidated.png) |
| Händler und Inventar → Zurück | Rückkehr führt wieder zur Arena-Kartenauswahl. [Händler](../artifacts/compact-ux-20261026/trader-session.png), [Inventar](../artifacts/compact-ux-20261026/inventory-session.png), [wiederhergestellte Auswahl](../artifacts/compact-ux-20261026/restored-after-inventory.png) |
| Hideout und Hauptmenü | Hideout besucht; anschließend wieder Hauptmenü. Die Statushistorie enthält auch Trader → SelectLocation und Inventory → SelectLocation. [Hideout](../artifacts/compact-ux-20261026/hideout-session.png), [Statushistorie](../artifacts/compact-ux-20261026/after-hideout-status.json) |
| Normale PMC-Auswahl | Neun Schritte bestanden; normale Factory-Karte ohne Arena-Belohnungskarte. Kein normaler Raid gestartet. [Ablauf](../artifacts/compact-ux-20261026/ordinary-menu/report.md), [Screenshot](../artifacts/compact-ux-20261026/ordinary-menu/ordinary-factory.png) |
| Private Session und erste Rückkehr | Einstieg und Rückkehr ausgeführt; Rückkehr nach ungefähr 55 Sekunden. Danach Konfiguration und Inventar mit dem Ausgangsstand verglichen. |
| Einstellungen nach erster Rückkehr | **228 von 228 Einträgen exakt identisch**, einschließlich Werten und Metadaten; beide Abfragen vollständig. [Vorher](../artifacts/compact-ux-20261026/before-config-full.json), [danach](../artifacts/compact-ux-20261026/after-first-return-config.json) |
| Inventar nach erster Rückkehr | **185 von 185 Gegenständen exakt identisch**; beide Abfragen vollständig. [Vorher](../artifacts/compact-ux-20261026/before-inventory.json), [danach](../artifacts/compact-ux-20261026/after-first-return-inventory.json) |

Der erste Moduslauf ist als fehlgeschlagen protokolliert: Schritt 19 suchte den Automationsnamen `ARENA: EQUATOR`, sichtbar war `EQUATOR`. Die Fortsetzung verwendet den sichtbaren Namen und deckt die ausgelassenen Schritte ab; der ursprüngliche Fehlerbericht bleibt erhalten.

## Build und Deployment

Der [Build für den letzten GPU-Prüfstand](../artifacts/compact-ux-20261026/build-native-final.log) hat null Fehler und vier bereits vorhandene Nullable-Warnungen. Die Eingangskontrollen der aufgezeichneten Szenarien meldeten `build 17/17 identical` in der normalen und `build 16/16 identical` in der privaten Session. Diese Snapshots beziehen sich auf den jeweiligen Prüfzeitpunkt.

Der [finale Deploymentvergleich](../artifacts/compact-ux-20261026/final-deployed-hashes.json) bestätigt: Client und BigBrain-Companion ersetzt; Server, Redeploy.Core, README und Review unverändert. Die vollständigen SHA-256-Werte stimmen jeweils zwischen Release-Build, installierter DLL und Vergleichsdatei überein:

| Artefakt | SHA-256 |
|---|---|
| Client | `CAC8D6FADC71D1A58A1F40858B3FD94F91795F06C69C09CF0DF9A35104939349` |
| BigBrain-Companion | `FEE0D2A0AFEE6B69DC17C3A89946DF00ECC1B2D12E04158B96CDFAF70330E550` |

## Native Abschlussprüfung

| Abschlussprüfung | Stand |
|---|---|
| Adaptive Bannerposition und Profil-ID-Korrektur | **PASS.** 18 Schritte und eine Erwartung; Geometrie visuell geprüft. Rückkehrknopf auf Händler- und Inventarseite jeweils `interactable: true`, Treffer `self`. [Ablauf](../artifacts/compact-ux-20261026/final-navigation/report.md), [Händler](../artifacts/compact-ux-20261026/final-navigation/results/step-12-snapshot.json), [Inventar](../artifacts/compact-ux-20261026/final-navigation/results/step-18-snapshot.json) |
| Native Dialogsperre | **PASS.** Ein echtes Messer-Inspektionsfenster blendet die Sessionleiste aus; nach Escape erscheint sie wieder. Manueller Tooldurchgang; [Dialog-Screenshot](../artifacts/compact-ux-20261026/final-modal.png). |
| PitFireTeam-/My-Squad-Oberfläche | **PASS.** Elf Schritte, eine Erwartung. Tactical zeigt `FIRST TO 3 WINS` und Zuschauen nach Tod; Zombies Team-Wipe und Rückkehr Gefallener. Beide Darstellungen passen. Ein Testbegleiter `test1`; anschließend Squad geleert und **0/4** bestätigt. [Ablauf](../artifacts/compact-ux-20261026/pft-final/report.md), [Tactical](../artifacts/compact-ux-20261026/pft-final/pft-tactical.png), [Zombies](../artifacts/compact-ux-20261026/pft-final/pft-zombies.png) |
| Raidstart nach Händler- und Inventar-Abstecher | **PASS.** 15 Schritte, zwei Erwartungen bis `MatchMakerAccept`; anschließend READY und erfolgreicher Start nach rund 20,5 Sekunden. [Menüablauf](../artifacts/compact-ux-20261026/raid-via-menu-return/report.md) |
| Arena-Raid mit finalem Client | **PASS.** `factory4_day`, `BattleUI`, privates Backend auf Port 6970 und `build 16/16 identical`. Arena-TDM-HUD sichtbar, Sessionbanner ausgeblendet. [Raidstatus](../artifacts/compact-ux-20261026/final-raid-status.json), [Screenshot](../artifacts/compact-ux-20261026/final-arena-raid.png) |
| Raid verlassen | **PASS.** `leave --no-heal` führte nach 21 Sekunden über Disconnect, ExitStatus, KillList und SessionStatistics ins Hauptmenü. Backspace wurde getrennt als Aufgeben/Respawn beobachtet. |
| Abschließende Session-Rückkehr | **PASS.** Rückkehrknopf im Händlerbanner mit 5-Sekunden-Bestätigung betätigt; nach **55,1 Sekunden** normales Hauptmenü, Backend Port 6969 und `build 17/17 identical`. [Status nach Rückkehr](../artifacts/compact-ux-20261026/after-final-return-status.json) |
| Abschließender Datenvergleich | **PASS.** **228 Konfigurationseinträge einschließlich Metadaten und 185 vollständige Item-Objekte exakt identisch**; keine gekürzte Abfrage. [Vergleich](../artifacts/compact-ux-20261026/final-data-comparison.json), [Konfiguration](../artifacts/compact-ux-20261026/after-final-return-config.json), [Inventar](../artifacts/compact-ux-20261026/after-final-return-inventory.json) |
| Bereinigung | **PASS.** Testbegleiter entfernt; Spiel und Server beendet, keine weitere SPT-/EFT-/Launcher-Instanz. Ausgangszustand wiederhergestellt. [Hoststatus](../artifacts/compact-ux-20261026/final-host-state.log) |

Die [private Logprüfung](../artifacts/compact-ux-20261026/final-private-errors.log) ergab **0 ArenaMode-Fehler**, die [normale Logprüfung](../artifacts/compact-ux-20261026/final-normal-strip-warnings.log) **0 Sessionleisten-Warnungen**. Der Schluss des zuletzt verfügbaren normalen Logs war nicht vollständig erfasst; diese Nulltreffer gelten für die geprüften Daten.
