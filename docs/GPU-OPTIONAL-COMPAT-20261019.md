# Native Prüfung der optionalen Mod-Adapter — 2026-10-19

## Stand

**Native Ladeabnahme bestanden; Gameplayprüfung noch nicht durchgeführt.**
Der erste Start erreichte das Hauptmenü. Looting Bots, Remove The Dead und Smoothie
wurden im echten EFT-Prozess erkannt und alle drei Arena-Adapter meldeten sich
bereit, ohne eine Contractwarnung dieser Adapter.

Vor dem ersten Einstellungswechsel wurde der Test durch einen fremden
Neustart desselben GPU-Hosts unterbrochen. Der eigene Test übernahm die Kontrolle
nicht und veränderte keine Einstellungen. Die offene Raidmatrix unten darf daher
nicht als bestanden gewertet werden. Offline-Harnesse sind davon getrennte Belege.

## Geprüfter Stand

- Testhost: SPT-GPU-2; ausschließlich das gleichnamige Testprofil.
- Arena-Client und BigBrain-Companion: `144a7631340bf2ee2605d10a68bdfc76d49b87c1`.
- EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`.
- UiBridge `0.9.4+42bbfb38e6ea3536b579db4312880ebda1f7b0cf`.
- Echte temporäre Testmods: Looting Bots **1.8.0** samt Serverkomponente,
  Remove The Dead **4.1.3**, Smoothie **1.0.8** samt nativer Bibliothek.
- Serverkomponente von Arena und Redeploy.Core wurden für diesen Test nicht ersetzt.

| Datei | SHA-256 |
| --- | --- |
| ArenaMode.Client | `2e60d3c37b0533087435379c9e4cf5c66a027f4170c0228b6fd1cd3abacc8347` |
| ArenaMode.BigBrainCompat | `c6fdd43fab287f618dd47c1df1b1c9d821a1ddb6ccd0e1ad6c57026b2a9b2bd8` |

Die Hashes stammen aus dem Deploymentmanifest. Zusätzlich verglich UiBridge
die DLLs der Entwicklungsinstallation, des GPU-Hosts und des tatsächlich
geladenen Prozesses per MVID: **44/44 Plugin-Dateien und 6 Bibliotheken identisch**.
Das ist mehr als ein Vergleich der Dateien auf der Festplatte.

## Native Startbelege

Der Start über die reservierte Test-Lane erreichte nach 62 Sekunden das
Hauptmenü; kein Raid wurde begonnen. Uhrzeiten sind lokale Hostzeit.

| Adapter | Beleg im ersten Prozess |
| --- | --- |
| Looting Bots | 20:13:30.115: `Arena Looting Bots: exact-body autonomous-looting guards ready.` |
| Remove The Dead | 20:13:30.123: `Arena Remove The Dead: shared automatic/manual cleanup guard ready.` |
| Smoothie | 20:13:30.127: `Arena Smoothie: exact-raid optional GC-pause guard and camera-checked pickup layers ready.` |

Das echte Harmony-Startlog zeigt die fünf Looting-Bots-Prefixes für
`IsActive`, `BeginSearch`, `EnqueuePriorityCorpse`, `OnAirdropLanded` und
`StartLooting` sowie den Smoothie-Prefix für `KeepCleanupPaused`.
Die RTD-Ready-Meldung erfolgt erst nach der eigenen Patchbesitzprüfung.
Für den stabilen Looting-Bots-Stand 1.8.0 gibt es keinen zusätzlichen
1.8.1-RC-Inventar-Cleanup-Einstieg.

Diese Belege bestätigen erfolgreiches Binden im echten Mono/Harmony-Prozess.
Sie bestätigen noch nicht die Ausführung der Arena-Scopezweige oder die
Unversehrtheit von Leichen und Pickups während eines Raids.

## Offene native Raidmatrix

| Probe | Status | Erforderlicher Wirkungsbeleg |
| --- | --- | --- |
| Factory Deathmatch, vier Teilnehmer, Pickups | **Nicht durchgeführt** | Start ohne Admissionfehler, lebende Bots und private Pickupmarker |
| Looting Bots in Arena | **Nicht durchgeführt** | Einmalige Unterdrückungsmeldung und tatsächlich unterdrückter autonomer Einstieg; keine bloße Log-Abwesenheit |
| Remove The Dead F5 und automatische Reinigung in Arena | **Nicht durchgeführt** | Vorhandene konkrete Leiche bzw. Pickup bleibt trotz echter Reinigungsversuche erhalten; fremder „Removed“-Zähler allein reicht nicht |
| Smoothie nach dem statischen Scan | **Nicht durchgeführt** | Private Marker auf kamera-sichtbarer ausgeschlossener Ebene, sichtbare Darstellung nach dem Scan; gepoolte Waffe unverändert |
| Optionaler Smoothie-GC-Schalter in Arena | **Nicht durchgeführt** | Schalter gezielt eingeschaltet, Original-Tick in genau diesem Arena-Raid unterdrückt |
| Anschließender gewöhnlicher PMC-Raid im selben Prozess | **Nicht durchgeführt** | Looting-Bots-Komponenten und echte Suche/Scan-Aktivität; Smoothies Original-GC-Tick wieder ausgeführt |
| Rückkehr ins Hauptmenü und Wiederherstellung | **Noch offen** | Testwerte zurückgesetzt, Spiel und Server nach bestätigter Freigabe sauber beendet; temporäre Mods entfernt |

PFT-Kommandos, der zusätzliche Cleanup-Pfad von Looting Bots 1.8.1 RC,
Langzeitrunden und Optikwechsel sind durch diese Startabnahme ebenfalls
**nicht nativ geprüft**. Dafür vorhandene Offlineprüfungen dürfen nicht als
Ingameabnahme bezeichnet werden.

## Unterbrechung und unveränderte Testwerte

Vor Änderungen wurden alle Arena-Einstellungen und die Einstellungen der drei
Testmods ausschließlich gelesen und als ignorierte Rohbelege gesichert.
Geplant waren reversible Diagnosewerte für RTD, Looting Bots und Smoothie
sowie eine kurze Deathmatch-Konfiguration.

Der erste `config-pin`-Aufruf wurde bereits an der GPU-Host-Sperre abgewiesen:
Ein fremdes `restart --host host2 --force` lief seit 20:17:33.
Die Pin-Datei wurde **nicht angelegt**, und keine dieser Änderungen wurde gesetzt.
Der Test führte weder Maus-/Tastatureingaben noch Gameplay-Testhooks aus.

Die anschließende ausschließlich lesende Abfrage sah wieder das Hauptmenü in
einem neuen Prozess. Daraus wurde ausdrücklich keine Erlaubnis zur Übernahme
abgeleitet. Die weitere native Prüfung wartet auf eine belegte Hostfreigabe.

Temporäre Testmods sind vorerst bewusst installiert geblieben. Ihre Entfernung
und eine eventuelle Bereinigung neu erzeugter Fremdmod-Konfigurationen übernimmt
die koordinierende Sitzung anhand des ursprünglichen Manifests, erst nach
sicherem Spiel- und Serverende. Dieser Bericht behauptet keine bereits erfolgte
Wiederherstellung.

## Rohbelege

Alle folgenden Dateien liegen im ignorierten Verzeichnis
`artifacts/gpu-optional-compat-20261019/` und enthalten gegebenenfalls private
Laufzeitdetails; sie gehören nicht in das öffentliche Repository:

- `deployment.json`: lokale und GPU-Hashes, unveränderte Core-/Serverdateien.
- `temporary-mod-manifest.json`: temporäre Dateien und zuvor fehlende Ziele.
- `01-startup-status.json`: echter geladener Prozess, Pluginvergleich und Harmonyübersicht.
- `02-startup-adapters.json`: gefiltertes vollständiges Startlog mit Ready- und Hookbelegen.
- `config-before-0.json` bis `config-before-3.json`: unveränderte Einstellungen
  von Arena, RTD, Smoothie und Looting Bots.
