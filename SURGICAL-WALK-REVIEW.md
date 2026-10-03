# Langsames Gehen während chirurgischer Behandlung

2026-10-03: CMS und Surv12 erlauben dem lokalen Arena-Spieler langsames Gehen
mit höchstens 1 m/s. Der Movement-Master gilt weiterhin. Keine neue Einstellung,
kein verändertes Item-Template, keine Botänderung und keine gewöhnlichen Raids.

## Native Grundlage und Umfang

`ActiveHealthController.MedEffect.IsNoMove` sperrt chirurgische Kits über
`DestroyedPart`. `Player.OnHealthEffectAdded` setzt `UsingMeds` und `HealingLegs`.
Letzteres führt in `MovementContext.CanWalk` zur Ablehnung; danach folgt die
native Hindernisprüfung. Diese Flags sperren auch Sprinten, Springen und Prone;
Vaulting prüft zusätzlich die medizinische Handoperation.

Die Flags bleiben unangetastet. Zwei überprüfte CanWalk-Aufrufstellen in
`IdlePlayerState.Move` und `MovePlayerState.ManualAnimatorMoveUpdate` erhalten
einen nativen Vorabcheck mit enger Ausnahme. Beide müssen erfolgreich gebunden
sein. Fehlende/mehrdeutige IL-Stellen behalten ihre ursprünglichen Anweisungen.
Andere CanWalk-Leser werden nicht erweitert; keine Abhängigkeit von einem
gegebenenfalls inlineten kleinen Getter.

Die Ausnahme prüft den bestehenden exakten ArenaMovementScope samt Input-Sperren,
aktuellen lebenden Körper/Spielwelt/Raid, aktuelle Meds-Handoperation und deren
chirurgisches Item. Ein existierender Added-/Started-MedEffect muss dasselbe Item
und denselben HealthController referenzieren. Nur echte Idle-/Move-Zustände ohne
Prone/Mount/Vaulting oder Control-Overrides sind zugelassen. Die native
ObstacleCollisionFacade entscheidet weiterhin über Hindernisse. Normale
Frakturbehandlung wird nicht erweitert.

Die physische XZ-Bewegung wird nach Arena-Multiplikatoren und Slide-Zugabe begrenzt,
auch bei Faktoren 1. Y bleibt für Schwerkraft und Bodenprojektion unangetastet.
Kein zusätzlicher CharacterController-SpeedLimit-Bracket und keine medizinische
Belegung fremder SpeedLimit-Gründe. Bereits langsamere Bewegung bleibt erhalten.
Ungültige Zeitintervalle erzeugen keine horizontale Bewegung.

Abschluss, Abbruch, Tod, Respawn und Master-Off entfernen die Ausnahme durch den
aktuellen Zustand. Es gibt keinen gehaltenen Spieler, keine Restore-Liste und
keine neue medizinische Operation. EFT konsumiert Ressourcen und restauriert
Flags selbst; bestehende Arena-MedicalUseSpeed bleibt unverändert.

## Nachweise und Grenzen

`tools/SurgicalWalkChecks/Verify.ps1` kompiliert die echten SurgicalWalk- und
MovementScope-Quellen gegen externe Spielgrenzen. Geprüft werden Identitäten,
MedEffect-Zustände, normale Medikamente, Kollision, Input/State-Ausschlüsse,
Cap/Schwerkraft, ausführbares IL-Rewrite und seine Integration nach der
Slide-Zugabe. Sechs kompilierte Fehlerkontrollen sichern die wesentlichen
Abgrenzungen. Der bestehende Medical-Harness prüft separat die Zeitbeschleunigung.

Unabhängiges Quellreview durch `surgical_movement_research`: keine offenen P0–P2.
Die Geschwindigkeitsbegrenzung verändert nicht die Schrittanimation; deren
Darstellung sowie tatsächliche Behandlung unter Bewegung müssen im Spiel
geprüft werden. Build, Deployment und native Ergebnisse werden separat erfasst.

Native Factory-Abnahme am 2026-10-03: langsames Surv12-Gehen mit gemessenen
1 m/s, Master-Off-Vergleich, erfolgreiche Surv12-/CMS-Behandlung und nativer
manueller Abbruch bestätigt. Nach Abschluss keine Begrenzung mehr. Siehe
`GPU-SURGICAL-WALK-RESULTS-20261003.md` für Belege und explizite Testgrenzen.
