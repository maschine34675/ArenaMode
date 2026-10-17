# Abhängigkeiten, Integrationen und Veröffentlichung

Stand: 2026-10-17. Diese Übersicht beschreibt den geprüften ArenaMode-Quellstand,
nicht die jeweils neueste Veröffentlichung anderer Mods. Versionsgebundene
Adapter sind keine pauschale Freigabe späterer Versionen. Gemeinsames Laden
beweist nicht die vollständige Kompatibilität jeder Funktion.

## Harte Voraussetzungen

| Bestandteil | Voraussetzung |
|---|---|
| SPT 4.1.x | Einschließlich der mit SPT gelieferten BepInEx-/Client-Komponenten. Aktuelle GPU-Prüfung mit SPT 4.1.6. |
| ArenaMode Client und Server | Beide Hälften sowie die eigenen benötigten Daten/Assets gehören zum Paket. |
| `maschine-Redeploy.Core.dll` | Direkte Laufzeitabhängigkeit. Der aktuelle Quellstand referenziert 1.0.7; ältere Test-/Buildberichte können noch 1.0.6 nennen. Passend zum konkreten Arena-Build ausliefern. Nicht mit der vollständigen Redeploy-Mod verwechseln. |

Der Hauptclient deklariert SPT Core, PFT, SAIN und DynamicMaps als weiche
Ladereihenfolge-Abhängigkeiten. Die harte Core-Abhängigkeit besteht über den
Assemblyverweis und `RedeployCore.Init`. Die Servermetadaten deklarieren keine
zusätzlichen fremden Pflichtmods. Die mit SPT gelieferten Plattform-DLLs sind
keine zusätzlichen Mod-Downloads.

Quellen: `ArenaMode.Client/Plugin.cs`, `ArenaMode.Client/ArenaMode.Client.csproj`,
`ArenaMode.Server/ModMetadata.cs`, `../Redeploy/Redeploy.Core/CoreVersion.props`.

## Optionale Funktionen und gezielte Adapter

| Mod/Komponente | Geprüfter Vertrag | Zweck / Verhalten ohne sie |
|---|---|---|
| SAIN | 4.5.1 für die versionsgebundenen Arena-Adapter | Persönlichkeiten, Suchtempo, Bewegung und Lebenszyklusabsicherung. Ohne SAIN native KI. |
| BigBrain + ArenaMode.BigBrainCompat | Ab 1.5.0, damit getestet; neuere Versionen mit Warnung und Laufzeitprüfung | Gezielte Pickup-Suche, Verfolgung nach Spielertod und Feuerflucht. Ohne den Arena-Begleiter keine gezielte Pickup-Steuerung und kein Molotov-Feuer; das private Molotov-Item bleibt dann harmlos, normale Granaten bleiben unverändert. Pickups selbst bleiben nutzbar. BigBrain ist außerdem eigene Voraussetzung von SAIN/PFT. |
| PitFireTeam, Client und Server | Exakt 0.10.0 oder 0.10.3; keine Freigabe von 0.10.1/0.10.2 | 1–4 eingeladene Begleiter für Tactical (weiterhin fünf Gegner) und PFT-Zombies. Solo und unabhängiges Team DM benötigen PFT nicht. Equator bleibt für beide PFT-Varianten gesperrt. |
| APBS | Adapter für 2.4.1 | Bot-Ausrüstung/Progression. Ohne APBS native SPT-Erzeugung; bei unbekanntem APBS bleibt dessen eigene Levelwahl und Arena warnt statt ungeprüft einzugreifen. |
| ArenaMapPort, Client/Server/Kartenassets | Optionale Karte muss client- und serverseitig vorhanden/aktiv sein | Bay5, Equator und Air Pit. Keine Pflicht für die gewöhnlichen SPT-Karten. |
| KillcamReplay | Schnittstellenprüfung statt fester Versionsfreigabe; Arena-Live-Nachlauf ab 0.9.13 halbiert | Echte Killcam-Replays; DeathReplay muss aktiviert sein. Arenas anschließender Team-Zuschauermodus ist eigener Code. |
| DynamicMaps | Leichenmarker-Adapter für 1.2.1 | Minimaps und bereinigte Leichenmarker. Arenas statische Kartenvorschauen funktionieren ohne DynamicMaps. |
| Waypoints | Keine direkte Arena-Abhängigkeit; Koexistenz mit 1.9.0 beobachtet | Eigenständige Navigations-/Spawnanzeigefunktionen; nicht allein wegen der üblichen KI-Modkombination als Arena-Pflicht aufführen. |

Die optionalen Mods behalten ihre eigenen Voraussetzungen. Fremde
SAIN/APBS/PFT/BigBrain-DLLs werden gemäß `AGENTS.md` nicht in Arena mitgeliefert.
`ArenaMode.BigBrainCompat` ist eigener Arena-Code und wird immer mitgeliefert.
Ohne geladenes BigBrain bleibt der Begleiter still inaktiv. Ältere Versionen
als 1.5.0 oder fehlerhafte API-Verträge deaktivieren die Integration sicher.
Neuere Versionen werden mit einer einmaligen Warnung versucht; das ist keine
Bestätigung vollständiger Kompatibilität.

## Weitere ausdrücklich berücksichtigte Kombinationen

- **UIFixes:** alternativer R-Nachladeweg berücksichtigt; installiert 6.0.3.
- **ABPS/BotPlacementSystem:** Pump-/Botlimit-Verhalten berücksichtigt;
  Arena-eigene Besetzung verhindert zusätzliche fremde Ambient-Spawns.
- **AutoCorpseSearch:** die Arena-Suche tritt bei geladener Standalone-Mod zurück.
- **NotificationFilter:** Arena unterdrückt auch ersetzte Quest-Popups, ohne
  dessen globale Konfiguration zu ändern.
- **BrightLasers:** eigener Helligkeits-/Teamfarben-Code berücksichtigt den
  geprüften 3.0.0-Vertrag und vermeidet doppelte Verstärkung. Keine umfassende
  native Freigabe gemeinsamer Installation daraus ableiten.
- **EnableLabyrinth:** Arena-Zugriff und Countdown sind unabhängig; zusätzliche
  Scav-/Schlüssel-/Exit-Änderungen der anderen Mod werden nicht überschrieben.
- **Terkoiz Freecam:** Kamerabesitz wird erkannt, um konkurrierende Steuerung
  zu vermeiden; keine allgemeine Freigabe sämtlicher Kamerafunktionen.
- **Fika:** ausdrücklich nicht unterstützt. Arena verweigert die Aktivierung;
  das ist mehr als eine fehlende Testfreigabe.

## Eigene Mods: übernommen oder weiterhin eigenständig?

| Eigene Mod | Stand | Konsequenz |
|---|---|---|
| RaidInfoPanels | Vollständig übernommen; eigenständiges lokales Projekt/alte DLL archiviert. | Kein separates Paket für Arena. |
| CombatSlide | Übernommen und Arena-begrenzt; Standalone archiviert. | Kein separates Paket für Arena. |
| AutoCorpseSearch | Arena-relevante corpse-open-Suche übernommen; Standalone mit größerem Umfang weiter vorhanden. | Für Arena nicht nötig, für normale Raids/weitere Suchfunktionen eigenständig sinnvoll. |
| NotificationFilter | Nur Verhaltensreferenz; eigene engere Arena-Implementierung, kein fremder Code/DLL übernommen. | Eigenständige Mod behalten; keine Arena-Voraussetzung. |
| DefaultAutoFireMode | Arena besitzt nun die entsprechende lokale Zieh-Regel: echte Vollautomatik, wenn unterstützt. Keine Übernahme der Standalone-Burst-/Double-Action-Fallbacks. | Kein zusätzlicher Arena-Download nötig. Standalone bleibt für normale Raids eigenständig und wirkt bei gemeinsamer Installation weiterhin nach seinen eigenen Regeln. |
| Redeploy.Core | Gemeinsame Bibliothek mit eigenem Versionsstand. | Passende, getestete Core-DLL direkt mit Arena ausliefern. |
| Redeploy.Client/Server | Eigenständige Respawn-Mod für normale Raids; nicht absorbiert. | Separates Produkt, keine Arena-Pflichtinstallation. |
| KillcamReplay | Eigenständiges optionales Plugin; Anbindung per Reflection. | Separates optionales Paket für Replays. |
| ArenaMapPort | Eigenständiges optionales Kartenprojekt. | Separates Kartenpaket. |
| UiBridge / Diagnosehilfen | Entwicklung und Tests, keine Arena-Laufzeitvoraussetzung. | Nicht in das normale Spielerpaket aufnehmen. |

UseItemsAnywhere, BrightLasers, ColoredTracers, EnableLabyrinth, SpeedLoader und
EnemyMarkers sind keine zusätzlichen Voraussetzungen der entsprechenden
Arena-Funktionen. Verhaltensreferenzen/eigene Umsetzungen nicht als vollständige
Übernahme dieser fremden Mods bezeichnen. Angepasste MIT-Teile von DragonDen
behalten ihre vorhandene Attribution; auch dessen Mod-DLL wird nicht benötigt.

## Release-Grenzen

Vorgesehen: Arena-Client, Server, eigene Daten/Assets und genau eine passende
flache Redeploy.Core-DLL, bytegleich zur Bibliothek in einem gleichzeitigen
Redeploy-Paket. Ein eigenes Pflicht-Downloadpaket für Core ist nicht nötig.
Das ist der Paketierungsplan; die vorhandenen Staging-/Map-Preview-Prüfungen
sind noch kein vollständiger allgemeiner Release-Packager.

Spielertexte liegen in [README](../README.md) und der
[Forge-Beschreibung mit sechs Tabs](FORGE-DESCRIPTION.md). Die zentrale
[Lizenzübersicht](../THIRD-PARTY-NOTICES.md) grenzt eigenen MIT-Code von den
separaten Bild-/Audiorechten ab. Sie und die neue [LICENSE](../LICENSE) müssen
bei der späteren Paketierung zusätzlich zu den bisherigen Asset-Notices
mitgeführt werden. Diese Dokumentationsänderung baut oder verteilt nichts.
Der Source-Build benötigt außerdem das benachbarte Redeploy-Repository;
dessen passender Quellstand muss für eine Veröffentlichung zugänglich sein.

Die 18 früher extrahierten EFT-Arena-UI-Grafiken sind im aktuellen Quellstand
durch acht generierte realistische Waffen-Kategoriebilder, sechs eigene Symbole
bzw. Banner (MIT) sowie vier game-icons.net-Symbole (CC BY 3.0) ersetzt.
Letztere: Skull crack, Grenade und Barbed wire von Lorc; Bowie knife von Skoll.
Quellen, Generator und Herkunftsnachweis liegen im Repo; NOTICE, SOURCES und
vollständige MIT-/CC-BY-3.0-Lizenztexte reisen im Client und lesbar unter
`ArenaMode/Ui` mit. Siehe auch `UI-GAME-ICONS-20261016.md`.
Siehe `UI-ARTWORK-REPLACEMENT-20261016.md`. Alte Grafiken bleiben in der
Git-Historie und in früheren Paketen: Der Austausch bereinigt diese nicht und
ist keine Freigabe zur Veröffentlichung der Historie oder anderer Assets.

Vertiefung: `RAID-INFO-PANELS-REVIEW.md`, `APBS-COMPATIBILITY-REVIEW.md`,
`FRIENDLY-FIRE-REVIEW.md`, `KILLCAM-MODES-REVIEW.md`,
`tools/ArenaNotificationChecks/README.md`, `../Redeploy/docs/RESPAWN-API.md`.
