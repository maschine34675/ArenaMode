# Isolation und Profil, 2026-10-19

Stand: `9708949` (2026-10-19 15:06, „Adjust score layout and Labyrinth and Zombie defaults“).
Quelle gelesen, nicht nur Kommentare. Client-Harmony lässt sich außerhalb des Spiels
nicht vollständig zertifizieren. GPU-2 war erreichbar, EFT lief dort nicht; ein
Kaltstart wurde nicht gemacht, Begründung unten.

## Ergebnis

Ein bestätigter gewöhnlicher Raid bleibt für den Spieler vanilla. Die Patches
hängen dauerhaft, ihre Wirkung hängt aber an einer Arena-Auswahl, einem
bestätigten Arm oder der exakten laufenden Arena-Raid. Für Bewegung, Schaden,
Waffen, Loot, Bots, Licht, Ton, HUD und Skills wurde kein Pfad gefunden, der
in einem normalen PMC- oder Scav-Raid von selbst umschaltet.

Das Profil hat dieselbe Grenze. Arena ersetzt für die exakte End-Identität die
komplette native Local-End-Pipeline und schreibt vorher den Klon zurück. Ein
gewöhnliches Ende läuft die SPT-Pipeline. Es wurde kein Pfad gefunden, der
Arena-Beute, Kits oder Raid-Fortschritt über dieses gewöhnliche Ende auf die
Platte bringt.

Das ist ein Quellbefund plus die bestehenden Server-Tests. Es ist keine neue
native Abnahme eines gewöhnlichen Raids auf GPU-2.

## Was der Spieler außerhalb eines Raids sehen darf

Die Kartenauswahl zeigt Arena, sobald Arena dort gewählt ist. Keycard-Verzicht
und das Überspringen der Vor-Raid-Seiten hängen an `ArenaClaim.WouldTakeOver`:
dieselbe Auswahl, ein startbarer Modus, eine serverseitig freigegebene Karte,
kein Fika, Server-Mod erreichbar. Eine normale Kartenbestätigung fällt auf die
Vanilla-Prüfung zurück.

Die Vermessungswerkzeuge für Flächen und Pickups dürfen in einem normalen
PMC-Raid nur mit beiden Schaltern `Enabled` und `AllowInNormalPmc` lesen und
Entwürfe exportieren. Beide stehen standardmäßig aus. Sie spawnen dort kein
Loot und schalten keine Arena-Raid ein.

Der private Molotow existiert ab Serverstart als eigene Vorlage: kein Händler,
kein Handbuch, kein Flohmarkt, kein Eingriff in die RGD-5. Im normalen Loot
taucht er dadurch nicht auf.

## Spielgrenzen, die geprüft wurden

| Bereich | Gewöhnlicher Raid | Arena |
|---|---|---|
| Location, Wellen, Loot | Klon unangetastet, solange die Session nicht `InRaid` ist | Nur der per-Raid-Klon |
| Bot-Feindschaft | Nur wenn der Klon die gestashte Arena-Referenz ist | Copy-on-write, keine gemeinsamen Config-Singletons |
| Pre-Raid-Wipe | Native Methode läuft | Übersprungen nur bei `IsInRaid` |
| Bot-Tier, TDM-Armbänder | Kein Kontext, kein Eingriff | Session und lebende Raid |
| Local-Start / Local-End | Dieselbe SPT-Pipeline, mit Identitätstor | Eigene geschützte Tasks |
| Tod / Respawn | `WantsToHandleDeath` verlangt `IsActive` | Redeploy nur in der Arena-Raid |
| Bewegung, Luft, Bunnyhop, Handling | `ArenaMovementScope` und die Ticks verlangen `IsActive` | Raid-lokal |
| Schaden, Friendly Fire, Pickups, Feuer, Akimbo | `IsActive`, exakte Raid oder bestätigter Wurf | Raid-lokal |
| GC-Schwelle, Bot-Ladeboost, Licht, Laser, Tracer, Alarm, HUD | Tick kehrt ohne `IsActive` zurück | Raid-lokal |
| Fast Deploy | Jeder Countdown stellt zuerst den alten Wert her; Schreiben nur bei Arm und eingeschaltetem Schalter | 2 s nur in der Arena-Vorbereitung |
| Vaulting-Globals | `MovementRuntime` stellt zurück, sobald weder aktiv noch armed | Nur solange die Arena die Reichweite will |
| Post-Raid-Seiten | Gewöhnliches Ergebnis bleibt nativ | Nur Darstellung der exakt beendeten Arena-Raid |

`ArenaController.Update` tickt jeden Frame. Die Dienste darin prüfen ihr Gate
selbst. Kit-Vorwärmung, Population, Match, Pickups und Ankündiger laufen ohne
aktive Arena nicht in die Welt.

Deathmatch-Gruppen, Zombie-Ladehooks und der Bot-Profil-Preload dürfen vor dem
`IsActive`-Latch laufen. Sie verlangen einen bestätigten Arm und die passende
Karte. Ein normaler Ladebildschirm ohne diesen Arm sieht sie nicht.

## Profil

Zwei Schichten, beide noch in dieser Form verdrahtet:

1. `ArenaPostRaidPmcSkipPatch` ersetzt `LocationLifecycleService.EndLocalRaidAsync`
   nur für die klassifizierte Arena-End-Identität. `Ordinary` lässt das Original
   durch. Ein markiertes Ende ohne passende Session wird abgelehnt, nicht still
   als normales Ende verbucht.
2. `ArenaNativeEndAdmission.TryClaimAndRestore` beansprucht `FinalizingEnd`,
   danach erst `RestoreSnapshot`. Schlägt das Zurückschreiben fehl, nimmt
   `AbortNativeEndFinalization` den Claim zurück. Die Session bleibt `InRaid`,
   der Klon bleibt liegen, und es gibt keinen normalen Post-Raid-Save.
   Dieselbe Reihenfolge gilt für die Crash-Recovery mit Arm-Token und gebundener
   End-Identität.

`HasNativeEndMarker` erkennt `|arenamode:`, nicht die Teilzeichenkette
`arenamode`. Ordinary-Start-IDs (`arenamode-start:`, `arenamode-ordinary-start:`)
sind damit keine End-Transaktionen. `CutOverToOrdinaryRaid` verweigert den
gewöhnlichen Start, solange ein Arm, eine Raid oder ein Snapshot existiert.

Der Server speichert das Profil in diesem Mod nur nach einem erfolgreichen
Restore: geschütztes Ende, fehlgeschlagener Start, oder ein liegengebliebener
Idle-Snapshot vor dem nächsten Arm. Mid-Raid gibt es keinen Arena-`SaveProfile`.

Client-Änderungen am lebenden Profil (Kits, Secure Container, Molotow-Nachschub,
Waffenmastery, PFT-Enzyklopädie) sitzen auf dem Objekt, das der Server-Klon
ersetzen soll. Skill-Buffs sind nicht serialisiert. `Mastering.Current` ist es.
`ArenaSkills.Restore` läuft am Raid-Ende, beim nächsten gewöhnlichen Raid-Start
und beim Plugin-Unload. Der Retry-Snapshot wird nur nach erfolgreichem
Write-back verworfen. Vaulting und Fast Deploy werden an denselben Ausgängen
zurückgesetzt; der Countdown heilt Fast Deploy zusätzlich vor jedem Lesen.

`IsolationProtectionTests`: 10 bestanden, 0 fehlgeschlagen (2026-10-19, Release).
Abgedeckt sind Ordinary-Start gegen den End-Marker, Claim vor Restore,
Snapshot-Erhalt bei falscher End-Identität und bei fehlgeschlagenem Restore,
sowie die Client-Regel, den Retry-Zustand nur nach Erfolg zu löschen.

## Restrisiken

Keines davon ist ein bestätigter Leak im normalen Raid. Jedes braucht einen
Fehler oder einen zweiten gleichzeitigen Kontext.

**Snapshot nur im Prozess.** `ProfileSnapshotService` hält den Klon in einem
Dictionary. Ein Server-Neustart mitten in der Raid verliert ihn. Arena speichert
während der Raid nicht, die Platte sollte also noch der Stand vor dem Arm sein.
Danach gibt es nichts mehr, woraus ein verschmutztes Speicherprofil repariert
werden könnte. Das ist die größte strukturelle Lücke, kein Schreibfehler im
End-Patch.

**Mastery, wenn Restore scheitert.** `ArenaSkills.Tick` hebt neue
Mastery-Einträge an, solange der Snapshot noch da ist, auch ohne `IsActive`.
Im Normalfall ist der Snapshot leer. Scheitert das Zurückschreiben, bleiben
Elite-Mastery und die Sticky-Buffs im Client-Objekt, bis der nächste
Raid-Start oder der Unload es erneut versucht. Buffs wandern nicht in die
Speicherdatei. Mastery schon, falls danach ein gewöhnliches Local-End den
Client-Stand verbucht, bevor ein Restore gelungen ist. Der glückliche Pfad
stellt am Raid-Ende und noch einmal vor dem nächsten Raid zurück.

**Arm, den der Server danach ablehnt.** Bot-Cap und Bot-Profil-Preload greifen
im Ladebildschirm über `ArmedForNextRaid` und die passende Karte, weil
`IsActive` dort noch nicht stehen kann. Lehnt der Server den Arm ab und der
Raid läuft trotzdem als normal weiter, ist dieser eine Raid nicht bit-identisch:
geleerter Profil-Pool, möglicher Arena-Bot-Cap. Das ist die dokumentierte
Ladebildschirm-Ausnahme, kein gewöhnlicher Start ohne Arm.

**Molotow-Explosion ohne Raid-Gate.** Für die verifizierte private Vorlage
unterdrückt der Prefix den nativen Blast auch außerhalb der Arena. Die Vorlage
ist harmlos registriert und wird nur in bestätigten Arena-Raids ins Inventar
gelegt. Ein normaler Raid bemerkt das nur, wenn dieses Template anderswo in
ein Inventar gerät.

**Bot-Cap serverseitig pro Karte.** `AnyInRaidOnMap` hebt `GetBotCap` für jede
Session auf dieser Karte, solange irgendeine Arena dort lebt. Für den üblichen
einen lokalen SPT-Spieler gibt es keine zweite Raid. Zwei Profile im selben
Serverprozess könnten sich die Cap teilen. Dieselbe Klasse gilt für das
gemeinsame Aufräumen von Bot-Cache und Händler-/Hideout-Intervallen am
geschützten Ende.

**PFT-Dateien liegen außerhalb des Profilklons.** Geschützte Team-Raids
blockieren die reflektierten Schreibpfade inklusive Proficiency ab 0.10.3.
Nach dem begrenzten Nachfenster oder bei einem unerkannten Vertrag laufen die
originalen Schreiber wieder. Team-Arena wird abgelehnt, wenn der Schutz nicht
bereit ist. Eine normale PFT-Raid ohne Arena lässt Arena in Ruhe.

**Harter Client-Abbruch.** Prozessende nimmt Client-Mastery, Vaulting und den
verkürzten Countdown mit. Der nächste Prozess lädt das Serverprofil. Ein
Serverprozess, der die Raid überlebt, behält den Klon bis zum geschützten Ende
oder bis zum eigenen Neustart.

## GPU-2

Host `192.168.178.109` antwortet, EFT und die Bridge auf Port 38765 nicht.
Ein Kaltstart plus gewöhnliche Factory-Raid hätte den glücklichen Pfad noch
einmal gezeigt: Countdown, keine Arena-HUD, Profil nach dem Ausgang. Die
Restrisiken oben entstehen beim abgelehnten Arm, beim fehlgeschlagenen Restore
und beim Server-Kill. Dafür ist eine heile Kontroll-Raid kein Nachweis. Deshalb
wurde EFT nicht gestartet.

Eine native Kontrolle bleibt sinnvoll, sobald ein gewöhnlicher Raid ohnehin
läuft: Countdown nicht auf 2 s, keine Elite-Skills, keine Arena-Kampfzeile im
BepInEx-Log, Inventar nach geschütztem Ausgang unverändert.

## Grenze dieses Reviews

Geprüft wurden die Server-Patches, die Session- und Snapshot-Dienste, die
End-Admission, die Molotow-Registrierung, die Client-Ticks und die Patches, die
Originalmethoden überspringen oder gemeinsame Zustände schreiben. Etwa zweihundert
Client-`ModulePatch`-Klassen wurden auf ihr erstes Gate gesehen; nicht jede
kosmetische Postfix-Zeile ist hier einzeln zitiert. Ein Patch, der nur liest
und ohne Arena sofort zurückkehrt, zählt als mitlaufend und unmerklich.
