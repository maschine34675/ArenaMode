# Abhängigkeiten, Integrationen und Veröffentlichung

Stand: 2026-10-20. Diese Übersicht beschreibt den geprüften ArenaMode-Quellstand,
nicht die jeweils neueste Veröffentlichung anderer Mods. Neuere Versionen werden
mit Warnung und unveränderten Schnittstellenprüfungen versucht. Das ist keine
pauschale Kompatibilitätszusage. Gemeinsames Laden
beweist nicht die vollständige Kompatibilität jeder Funktion.

## Harte Voraussetzungen

| Bestandteil | Voraussetzung |
|---|---|
| SPT 4.1.x | Einschließlich der mit SPT gelieferten BepInEx-/Client-Komponenten. Aktuelle GPU-Prüfung mit SPT 4.1.6. |
| ArenaMode Client und Server | Beide Hälften sowie die eigenen benötigten Daten/Assets gehören zum Paket. |
| `maschine-Redeploy.Core.dll` | Direkte Laufzeitabhängigkeit. Der aktuelle Quellstand benötigt mindestens 1.0.8 für die optionalen Respawn-Zeitmessungen; ältere Test-/Buildberichte können noch 1.0.6/1.0.7 nennen. Passend zum konkreten Arena-Build ausliefern. Nicht mit der vollständigen Redeploy-Mod verwechseln. |

Der Hauptclient deklariert SPT Core, PFT, SAIN, BigBrain, DynamicMaps, MoreBotsAPI,
AI Limit, Questing Bots, ORBIT, Looting Bots, Remove The Dead, Smoothie,
Continuous Healing, Late To The Party, Classic Movement, StatTrack,
Continuous Load Ammo und Deminvincibility als weiche
Ladereihenfolge-Abhängigkeiten. Die harte Core-Abhängigkeit besteht über den
Assemblyverweis und `RedeployCore.Init`. Die Servermetadaten deklarieren keine
zusätzlichen fremden Pflichtmods. Die mit SPT gelieferten Plattform-DLLs sind
keine zusätzlichen Mod-Downloads.

`Redeploy.Server` wird von Core nicht benötigt. Der eigenständige Redeploy-Client
fragt dessen `/redeploy/ping` ab; Core selbst führt keine solchen Serveranfragen
aus. Arenas Server übernimmt seinen eigenen geschützten Raid-Abschluss und die
Profilwiederherstellung. Deshalb gehören weder Redeploy.Client noch Redeploy.Server
ins Arena-Paket. Siehe [Quellprüfung und Bibliotheksumbau](BIGBRAIN-LIBRARY-20261018.md).

Quellen: `ArenaMode.Client/Plugin.cs`, `ArenaMode.Client/ArenaMode.Client.csproj`,
`ArenaMode.Server/ModMetadata.cs`, `../Redeploy/Redeploy.Core/CoreVersion.props`.

## Optionale Funktionen und gezielte Adapter

| Mod/Komponente | Geprüfter Vertrag | Zweck / Verhalten ohne sie |
|---|---|---|
| SAIN | Ab 4.5.1; damit geprüft, neuere Versionen mit Warnung | Persönlichkeiten, Suchtempo, Bewegung und Lebenszyklusabsicherung. Ohne SAIN native KI. |
| BigBrain + ArenaMode.BigBrainCompat | Ab 1.5.0, damit getestet; neuere Versionen mit Warnung und Laufzeitprüfung | Gezielte Pickup-Suche, Verfolgung nach Spielertod und Feuerflucht. Ohne den Arena-Begleiter keine gezielte Pickup-Steuerung und kein Molotov-Feuer; das private Molotov-Item bleibt dann harmlos, normale Granaten bleiben unverändert. Pickups selbst bleiben nutzbar. BigBrain ist außerdem eigene Voraussetzung von SAIN/PFT. |
| PitFireTeam, Client und Server | 0.10.0 oder ab 0.10.3; neuere Versionen mit Warnung, 0.10.1/0.10.2 weiterhin ausgeschlossen | 1–4 eingeladene Begleiter für Tactical (weiterhin fünf Gegner) und PFT-Zombies. Solo und unabhängiges Team DM benötigen PFT nicht. Equator bleibt für beide PFT-Varianten gesperrt. |
| APBS | Ab Metadatenversion 2.4.1; damit geprüft, neuere Versionen mit Warnung | Bot-Ausrüstung/Progression. Ohne APBS native SPT-Erzeugung; bei fehlender/zu alter Version oder fehlerhaften Schnittstellen bleibt APBS' eigene Levelwahl mit Warnung. |
| ArenaMapPort, Client/Server/Kartenassets | Optionale Karte muss client- und serverseitig vorhanden/aktiv sein; Air Pit Team DM benötigt die Teamstarts aus 0.2.2 oder neuer | Bay5, Equator und Air Pit. Keine Pflicht für die gewöhnlichen SPT-Karten. |
| Killcam | Aktuelles Integrationsziel 1.0.0; Schnittstellenprüfung statt fester Versionsfreigabe | Echte Killcam-Replays; DeathReplay muss aktiviert sein. Das frühere KillcamReplay-Plugin wird nicht akzeptiert. Arenas anschließender Team-Zuschauermodus ist eigener Code. |
| DynamicMaps | Ab 1.2.1; damit geprüft, neuere Versionen mit Warnung | Minimaps und bereinigte Leichenmarker. Arenas statische Kartenvorschauen funktionieren ohne DynamicMaps. |
| Waypoints | Keine direkte Arena-Abhängigkeit; Koexistenz mit 1.9.0 beobachtet | Eigenständige Navigations-/Spawnanzeigefunktionen; nicht allein wegen der üblichen KI-Modkombination als Arena-Pflicht aufführen. |

Die optionalen Mods behalten ihre eigenen Voraussetzungen. Fremde
SAIN/APBS/PFT/BigBrain-DLLs werden gemäß `AGENTS.md` nicht in Arena mitgeliefert.
Die [Versionsrichtlinie und Prüfungen](OPTIONAL-MOD-VERSIONS-20261019.md) erklären
die Mindestversionen, Warnungen und weiterhin verpflichtenden Schutzverträge.
`ArenaMode.BigBrainCompat` ist eigener Arena-Code und wird immer mitgeliefert.
Seit dem Bibliotheksumbau registriert diese DLL kein BepInEx-Plugin und keine
eigene Plugin-GUID mehr. Arena lädt sie gezielt neben seiner Client-DLL und
übernimmt Start, Pflege und Abschaltung. Beide DLLs gemeinsam aktualisieren;
alte Plugin-Kopien entfernen und das Spiel neu starten.
Ohne geladenes BigBrain bleibt die Bibliothek still inaktiv. Ältere Versionen
als 1.5.0 oder fehlerhafte API-Verträge deaktivieren die Integration sicher.
Neuere Versionen werden mit einer einmaligen Warnung versucht; das ist keine
Bestätigung vollständiger Kompatibilität.

## Weitere ausdrücklich berücksichtigte Kombinationen

- **Continuous Load Ammo 1.2.0+:** Ladecontroller werden beim Arena-Körperwechsel
  neu gebunden. Cleanup und verzögerte Endmeldungen des alten Körpers dürfen
  weder den Nachfolger noch einen neueren Ladevorgang entsperren/beenden.
  Normales Laden, Abbruch und gespeicherte Einstellungen bleiben bestehen.
  Siehe [Ladecontroller und Prüfgrenzen](CONTINUOUS-LOAD-AMMO-COMPAT-20261019.md).
- **SVM 2.2.3+:** SaveLoot/Ignore-Abkürzungen können Arenas geschützten
  Raidabschluss nicht überspringen. Normale Raids behalten die SVM-Regeln;
  andere Balance-Einstellungen werden nicht zurückgesetzt. Der Adapter prüft
  auch die tatsächliche Patch-Reihenfolge des äußeren Abschluss-Callbacks.
  Siehe [Abschlussvertrag und Prüfgrenzen](SVM-COMPAT-20261019.md).
- **Deminvincibility 2.2.0+:** Unsterblichkeit, Second Chance und die Begrenzung
  auf mindestens einen HP weichen nur beim Arena-eigenen lokalen Körper den
  nativen Todes-/Respawnregeln. Schadensmultiplikator, Medizin- und
  Fraktureinstellungen bleiben wirksam; keine gespeicherten Werte ändern sich.
  Siehe [Schadens- und Todesvertrag](DEMINVINCIBILITY-COMPAT-20261019.md).
  **Für diese drei neuen Adapter sind Ingame-Prüfungen noch offen.**
- **Classic Movement 1.1.3+:** Arena behält für seine Körper native Bewegungszustände
  und die eigene Bewegungssteuerung, einschließlich der Erstellung beim Laden.
  Normale Raids behalten Classic Movement. Das alte, vom aktuellen Fremdserver
  nicht mehr unterstützte `NostalgiaMode=true` verhindert nur den Arena-Start.
  Siehe [Bewegungszustände und Prüfgrenzen](CLASSIC-MOVEMENT-COMPAT-20261019.md).
- **StatTrack 2.1.1+:** Arena-Schüsse, -Treffer und -Kills fließen nicht in die
  dauerhaften Waffenstatistiken; Körperwechsel lösen keinen Statistikabschluss aus.
  Vorhandene Daten und ausstehende normale Speicheraufträge werden nicht gelöscht
  oder pauschal gesperrt. Normale Versicherungs-/ID-Wechsel bleiben unverändert.
  Siehe [Statistikbesitz und Prüfgrenzen](STATTRACK-COMPAT-20261019.md).
- **Continuous Healing 1.6.3+:** spielerbezogene Heilungsfortsetzung und Abbruch
  werden von Arena-Bots und abgelegten Spielerkörpern abgegrenzt. Der aktuelle
  lebende Spieler behält Continuous Healing; normale Raids bleiben unverändert.
  Siehe [Heilungsoperationen und Prüfgrenzen](CONTINUOUS-HEALING-COMPAT-20261019.md).
- **Late To The Party 3.1.1+:** nur originale, noch Arena-eigene Waffen-Pickup-
  Quellen und ihre Teile sind von fremder Loot-Erfassung/Bereinigung ausgenommen.
  Aufgenommene Waffen und gewöhnlicher Loot behalten die fremden Regeln.
  Siehe [Pickup-Besitz und Prüfgrenzen](LATE-TO-THE-PARTY-COMPAT-20261019.md).
- **Looting Bots 1.8.0+:** autonome Such-/Loot-Aktionen werden nur für exakt
  gebundene Arena-Bots unterdrückt, einschließlich beider Team-DM-Seiten und
  neuer Ersatzbots. Echte PFT-Begleiter behalten ihre kommandierten Aktionen.
  Die zusätzliche Inventarbereinigung des geprüften 1.8.1-RC-Pfads ist ebenfalls
  berücksichtigt. Keine Änderungen gespeicherter Looting-Einstellungen.
  Siehe [Verträge, Prüfungen und Grenzen](LOOTING-BOTS-COMPAT-20261019.md).
- **Remove The Dead 4.1.3+:** die gemeinsame automatische/manuelle
  Löschfunktion pausiert ausschließlich im bestätigten Arena-Spiel. Arena
  behält damit seine geschützten Leichen und Waffen-Pickups unter eigener
  Kontrolle. Timer, Warteschlangen und normale Raids bleiben unverändert.
  Siehe [Cleanup-Schutz](REMOVE-THE-DEAD-COMPAT-20261019.md).
- **Smoothie 1.0.8+:** eigene Pickup-Marker nutzen eine von Smoothies statischer
  Verdeckung ausgenommene Ebene, sofern Haupt- und Optikkamera sie darstellen.
  Nur die optionale GC-Pause tritt im aktiven Arena-Raid zurück; Physik,
  Animationen, Ragdolls und übrige Optimierungen werden nicht verändert.
  Ein unpassender optionaler Vertrag warnt, sperrt Arena aber nicht. Dann
  Smoothies Speicherbereinigungs-Pause ausgeschaltet lassen.
  Siehe [Darstellung und GC-Abgrenzung](SMOOTHIE-COMPAT-20261019.md).
- **AI Limit 1.9.2+:** die Erzeugung seiner Limiter-Komponente wird nur im exakt
  serverbestätigten Arena-Spiel unterdrückt, bereits vor dem Arena-Started-Event.
  Kein Wechsel globaler Einstellungen und kein Entfernen fremder Patches.
  Normale PMC-/Scav-Raids laufen original; ein Folge-Raid auf derselben Map erbt
  keine Sperre. Fehlende API-/Hook-Verträge sperren ausschließlich Arena.
  Siehe [AI-Limit-Adapter und native Testgrenzen](AI-LIMIT-COMPAT-20261019.md).
- **MoreBotsAPI 2.1.1+:** eigene Spawn-Kennungen sowie ein enger Schutz für
  PFT-Begleiter ohne Kennung; die externe Revenge-Persistenz und deren
  Feindschafts-Override werden für genau den gebundenen Arena-Raid ausgenommen.
  Kein pauschales Blockieren der Serverroute und kein Löschen fremder Queues.
  Ältere/unpassende API-Verträge sperren nur Arena, neuere warnen bei weiterhin
  gültigem Vertrag. Keine vollständige Freigabe aller abhängigen Content-Mods.
  Siehe [MoreBots-Adapter und Grenzen](MOREBOTS-COMPATIBILITY-20261019.md).
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
- **Questing Bots / ORBIT:** vorläufige Arena-Startsperre bei aktivem Questing
  Bots bzw. geladenem ORBIT. Normale PMC-/Scav-Raids bleiben unverändert.
  Die geprüfte Questing-Bots-Version 1.1.0 darf vollständig in ihrer
  Serverkonfiguration deaktiviert installiert bleiben; Teilabschaltungen in F12
  reichen nicht. Details: [Arena-Zulassung](OPTIONAL-MOD-ADMISSION-20261019.md).

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
| Killcam | Eigenständiges optionales Plugin; Anbindung per Reflection. | Separates optionales Paket für Replays. |
| ArenaMapPort | Eigenständiges optionales Kartenprojekt. | Separates Kartenpaket. |
| UiBridge / Diagnosehilfen | Entwicklung und Tests, keine Arena-Laufzeitvoraussetzung. | Nicht in das normale Spielerpaket aufnehmen. |

UseItemsAnywhere, BrightLasers, ColoredTracers, EnableLabyrinth, SpeedLoader und
EnemyMarkers sind keine zusätzlichen Voraussetzungen der entsprechenden
Arena-Funktionen. Verhaltensreferenzen/eigene Umsetzungen nicht als vollständige
Übernahme dieser fremden Mods bezeichnen. Angepasste MIT-Teile von DragonDen
behalten ihre vorhandene Attribution; auch dessen Mod-DLL wird nicht benötigt.

## Release-Grenzen

Das Release enthält Arena-Client, Server, eigene Daten/Assets und genau eine passende
flache Redeploy.Core-DLL, bytegleich zur Bibliothek in einem gleichzeitigen
Redeploy-Paket. Ein eigenes Pflicht-Downloadpaket für Core ist nicht nötig.
`tools/Package-TestRelease.ps1 -Release` baut aus isolierten, committeten
Quellständen ohne Deployment und prüft Tests, Archivinhalt und Prüfsummen.
Ohne diesen Schalter entsteht ein ausdrücklich als lokaler Test gekennzeichnetes
Paket. Beide Varianten verwenden dieselben Laufzeitassets und Lizenznachweise.

Spielertexte liegen in [README](../README.md) und der
[Forge-Beschreibung mit sechs Tabs](FORGE-DESCRIPTION.md). Die zentrale
[Lizenzübersicht](../THIRD-PARTY-NOTICES.md) grenzt eigenen MIT-Code von den
separaten Bild-/Audiorechten ab. Sie und die [LICENSE](../LICENSE) werden
zusammen mit den Asset-Notices ausgeliefert. Die lesbaren Dokumente liegen unter
`BepInEx/plugins/maschine-ArenaMode/docs/`, UI-Nachweise daneben unter `Ui/`.
Der Source-Build benötigt das benachbarte öffentliche
[Redeploy-Repository](https://github.com/maschine34675/Redeploy); dessen exakter
Quellstand steht im Paketmanifest. Core bleibt bei Version 1.0.8.

Die 18 früher extrahierten EFT-Arena-UI-Grafiken sind im aktuellen Quellstand
durch acht generierte realistische Waffen-Kategoriebilder, sechs eigene Symbole
bzw. Banner (MIT) sowie vier game-icons.net-Symbole (CC BY 3.0) ersetzt.
Letztere: Skull crack, Grenade und Barbed wire von Lorc; Bowie knife von Skoll.
Quellen, Generator und Herkunftsnachweis liegen im Repo; NOTICE, SOURCES und
vollständige MIT-/CC-BY-3.0-Lizenztexte reisen im Client und lesbar unter
`maschine-ArenaMode/Ui` mit. Siehe auch `UI-GAME-ICONS-20261016.md`.
Siehe `UI-ARTWORK-REPLACEMENT-20261016.md`. Alte Grafiken bleiben in der
Git-Historie und in früheren Paketen: Der Austausch bereinigt diese nicht und
ist keine Freigabe zur Veröffentlichung der Historie oder anderer Assets.

Vertiefung: `RAID-INFO-PANELS-REVIEW.md`, `APBS-COMPATIBILITY-REVIEW.md`,
`FRIENDLY-FIRE-REVIEW.md`, `KILLCAM-MODES-REVIEW.md`,
`tools/ArenaNotificationChecks/README.md`, `../Redeploy/docs/RESPAWN-API.md`.
