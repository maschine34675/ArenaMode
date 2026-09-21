# Bewegungspause nach dem Hochklettern

Stand: 2026-09-21. Untersuchung, **kein bestaetigter Fix** und keine Aenderung
an Client, Server, Konfiguration oder installierten DLLs.

Der Nutzer beschreibt etwa 1-2 Sekunden ohne Bewegung nach dem Hochklettern,
auch als Vanilla-EFT-Problem bekannt. Ob W dabei durchgehend gehalten oder oben
erneut gedrueckt wurde, ist noch nicht beobachtet worden. Die allgemeine
Vanilla-Zuordnung ist Nutzerbeobachtung, kein hier durchgefuehrter Vergleichstest.

## Befund und Grenzen

Es gibt im untersuchten `ClimbState.Exit` keinen expliziten Wartezeit-Timer.
Die native Hindernissperre ist ein konkreter Kandidat fuer einen zusaetzlichen
Stillstand nach dem Ende der Kletteranimation. Ihre Aktivierung beim gemeldeten
Vorfall ist jedoch noch nicht gemessen. Nicht vorsorglich den Bewegungszustand
erzwingen, die Kollision abschalten oder die gesamte Kletteranimation kuerzen.

Der aktuelle `D:/SPT41/BepInEx/LogOutput.log` hatte beim Lesen den Stand
2026-09-21 14:50:48 und 1.889.735 Bytes. Er enthaelt die registrierten
Movement-Patches, aber keine `Arena mantle motion`-Versuche, mit denen sich diese
Pause zuordnen liesse. Der fehlgeschlagene Arena-Start und der DynamicMaps-Fehler
beim Beenden sind kein Beleg fuer einen Zusammenhang mit dem Klettern.

### 1. Hindernissperre mit erhaltenem Zeitmesser

Native Quellen relativ zu `../_decompiled/`:

- `EFT.ObstacleCollision.Models/CollisionCalculatorModel.cs:73-86`: Bei geringer
  gemessener Geschwindigkeit, deutlichem Bewegungswunsch (Magnitude > 0,9) und
  einem Zeitmesser > 0,1 Sekunden wird `_canMove` false. Der normale
  `MovePlayerState` verwendet eine Geschwindigkeitsschwelle von 0,2 m/s.
- `CollisionCalculatorModel.cs:68-71`: `Reset()` setzt lediglich `_canMove=true`.
  Der Zeitmesser `_velocityConditionTimer` bleibt erhalten.
- `EFT/VaultingMovementState.cs:57`: Beim Eintritt ins Klettern/Vaulting wird
  genau dieses `Reset()` aufgerufen. Der Zeitmesser wird nicht beim Exit geleert.
- `EFT/MovePlayerState.cs:215-218`: Nach Ende einer Animator-Transition wird die
  Kollisionssperre erneut ausgewertet. Ein niedriger Geschwindigkeitswert kann
  bei bereits abgelaufenem Zeitmesser sofort wieder sperren.
- `EFT/MovementContext.cs:1300-1313`: Die Sperre geht in `CanWalk` ein. Der
  Move-Zustand nimmt daraufhin `Inert` zurueck, also den Bewegungswunsch im Animator.
- `CollisionCalculatorModel.cs:89-96`: Die Freigabe erfolgt durch eine
  Richtungsaenderung von mehr als 5 Grad oder einen ausreichend grossen Winkel
  zwischen Haenden und Koerper. **Hier existiert kein Freigabe-Timeout.**
- `EFT.ObstacleCollision/KinematicModel.cs:65-68`: Die verwendete Geschwindigkeit
  stammt aus der Positionsdifferenz zum vorherigen Frame, nicht aus einem langen
  gleitenden Mittelwert.

Moeglicher Ablauf: Vor der Kante gegen das Hindernis laufen, Zeitmesser sammeln,
hochsteigen, danach beim langsamen Anlaufen sofort erneut gesperrt werden.
Eine anschliessende Richtungs-/Blickaenderung koennte die als kurze Pause
wahrgenommene Sperre aufheben. Das ist eine **Hypothese**; weder ihr Auftreten
noch eine feste Dauer von 1-2 Sekunden ist damit nachgewiesen.

### 2. Animation und Landung auseinanderhalten

`EFT/VaultingMovementState.cs:114-117` verarbeitet WASD waehrend des Kletterns nur
als `Inert`-Bool. Die eigentliche Bewegung stammt in dieser Phase aus der
Animation. `MovementContext.ProcessStateEnter` wechselt den verwalteten Zustand
beim Eintritt des naechsten Animator-Zustands; erst dessen Bewegungslogik gilt.

Der installierte Standard-Controller wurde direkt aus
`assets/content/characters/controllers/player_anim_controller.bundle` gelesen.
Das Bundle enthaelt mehrere Controller: relevant ist **Player_anim_controller**,
Objekt-ID `-5637414435831338160`, Containerpfad
`Assets/Content/Characters/Controllers/Player_anim_controller.controller`.
Die gleichfalls enthaltenen Rework-/Backup-Controller sind keine austauschbaren
Belege. Native Auswahl: `EFT/Player.cs:26625-26652` und `EFT/InGameBundles.cs`.

`Base Layer.Vaulting.Climb_on` hat direkte Ausgaenge:

| Ziel | Bedingung | Normalisierte Exit-Zeit | Normalisierte Blenddauer |
|---|---|---:|---:|
| Idle_Aim | Inert=false, Level < 0,49 bzw. > 0,49 | 0,75 | 0,25 |
| MOVE | Inert=true | 0,78 | 0,20 |

Es gibt hier **keinen Climb-zu-VaultingLanding-Ausgang**. Die Blendbaum-Dauern
liegen je nach Hoehe zwischen rund 0,667 und 0,833 Sekunden, vor der Skalierung
durch `Vaulting_speed`. Die Exit-/Blendwerte sind Anteile, keine Sekunden und
kein Nachweis der Dauer eines tatsaechlichen Spielvorgangs.

Ein separat gefundener auffaelliger Codepfad in `EFT/JumpLandingState.cs:27-63`
setzt nach kurzer Null-Eingabe `denyMovement=true`, ohne dies bei spaeterer
Eingabe innerhalb desselben Zustands wieder zu loeschen. `VaultingLandingState`
erbt diesen Ablauf. Fuer echtes Hochklettern ist dies wegen der direkten
Controller-Ausgaenge derzeit **kein vorrangiger Erklaerungsansatz**. Relevant
waere er bei einem nachgewiesenen Uebersteigen/Vaulting mit Landung.

Physikalisches Aufsetzen bleibt davon getrennt: `Player.LandingAdjustments`
legt ab einer gemessenen Fallhoehe ueber der Schwelle einen zeitlichen
Fall-Geschwindigkeitsverlust an. `CheckFlying` schliesst Klettern nicht aus.
Bei reinem Hochsteigen ist keine entsprechende Fallstrecke zu erwarten;
ein Absinken nach einem Ueberschwingen muss anhand der tatsaechlichen
`OnGrounded`-Werte geprueft werden. Der Quellstandard der Schwelle ist 0,15 m;
das ist keine aus diesem Raid gemessene Einstellung.

### 3. Arena-Eingriffe

`VaultSpeedPatch` erhoeht den nativen Kletter-Animatorparameter; `VaultMotionPatch`
lockert waehrend `ApplyMotion` den zugehoerigen Geschwindigkeitshardcap. Sie
schreiben keinen anschliessenden Bewegungssperr-Timer. Die airborne Erweiterung
uebergibt ebenfalls an die nativen Kletterzustaende. `LandingRecoveryMultiplier`
verkuerzt nur die Dauer des `Player.LandingAdjustments`-Limits, nicht den
Hindernissperr-Zeitmesser oder die Animator-Uebergaenge.

Der bestehende `ArenaAirVault.Observation`-Logger beginnt nur bei von uns
eingeleiteten **airborne**-Versuchen und endet 0,75 Sekunden nach dem Exit.
Er beobachtet weder normales Hochklettern vollstaendig noch die entscheidenden
Hindernissperr-Werte. Deshalb kann ein weiterer gewoehnlicher Logdurchlauf allein
die Ursache noch nicht bestaetigen.

## Naechster gezielter Test

An derselben Kante zunaechst normal aus dem Stand/Anlaufen hochklettern:

1. W bis mindestens drei Sekunden nach dem Hochklettern durchgehend halten und
   die Kamera moeglichst ruhig lassen.
2. Wenn die Pause auftritt, einmal kurz A oder D dazunehmen. Separat pruefen, ob
   bereits leichtes Drehen der Kamera die Bewegung sofort wieder freigibt.
3. Vergleich: vor dem Erreichen der Oberseite W kurz loslassen, danach erneut
   druecken. Sprung-Hochziehen anschliessend als eigenen Versuch behandeln.

Eine sofortige Freigabe durch Richtungswechsel wuerde den Hindernissperr-Verdacht
stuetzen, aber nicht allein beweisen. Fuer die eindeutige Zuordnung ist als
naechster Implementierungsschritt ein **nur lesender, begrenzter Observer**
vorgesehen; er ist in diesem Dokumentations-Commit noch nicht eingebaut:

- an nativer Climb-/Vault-Entry beginnen; Exit plus etwa drei Sekunden erfassen;
- auf den aktuellen lokalen Spieler/World/Controller begrenzen; bei Tod,
  Respawn, Raidende oder Objektwechsel abbrechen;
- Eingabewunsch, State/Animator-Hash, normalized time und Transition erfassen;
- `CanWalk`, Hindernismodell `CanMove`, Zeitmesser, gecachte Richtung,
  Kinematikgeschwindigkeit und Hands-to-body-Winkel erfassen;
- Animationsbewegung und echte Positionsaenderung vergleichen; Capsule,
  SpeedLimit und `IgnoreDeltaMovement` mitfuehren;
- `OnGrounded`-Fallhoehe und das Fall-Speedlimit inklusive Ablaufzeit zuordnen;
- auch gesundes Ende protokollieren, keine Fehlerunterdrueckung, keine
  Positions-/Zustandskorrektur und kein permanenter Frame-Spam.

Erst wenn diese Messung die Sperre bestaetigt, einen eng begrenzten Reset des
zugehoerigen Zeitmessers an der passenden Klettergrenze erwägen. Ein allgemeines
Entsperren von `CanWalk` wuerde echte Hindernisse und andere Einschraenkungen
uebergehen und ist kein akzeptabler Fix.

## Lokale Evidenz

- Assembly-CSharp.dll SHA256:
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`
- Standard-Controller-Bundle SHA256:
  `9A6B93737D806D6A563BD1AFCBDFB77538DFCCE917C9D1DB0E2BE2E1F39CDD9F`
- Lokaler extrahierter Controller (nicht zur Weiterverteilung):
  `artifacts/climb-investigation/controller.json`.

Native Quellen, Arena-Eingriffe und Controllerdaten wurden untersucht; keine
GPU-Reproduktion, kein Build und kein Deployment fuer diese Untersuchung.
