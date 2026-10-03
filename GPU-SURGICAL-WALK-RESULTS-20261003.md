# Solo-Tracer und chirurgisches Gehen — GPU-2

2026-10-03, Client `1f538d4b1210d836860886212d34a2fb25398f1a`, einschließlich
Tracer-Änderung `4ae504a`. Nur Arena-Client lokal und auf GPU-2 erneuert.
SHA256 `56F7DAA61B5C99A9630B274937868A3E6AF9937B53A9398060560FE6CEC3652B`.
Server, Redeploy.Core und BigBrainCompat unverändert; vollständige Hashes im
`SURGICAL-WALK-DEPLOYMENT-20261003.json`.

## Build und Offline-Prüfungen

- Isolierter Clientbuild erfolgreich: vier bestehende Nullable-Warnungen,
  null Fehler. Installationsdateien beim Build unverändert.
- TdmMatchMenuChecks: 273 Assertions, 14 kompilierte negative Kontrollen.
- ArenaTracerChecks: 147 Assertions, neun negative Kontrollen, einschließlich
  verlorener eingefrorener Team-Auswahl.
- SurgicalWalkChecks: 91 Assertions, sechs negative Kontrollen.
- Bestehende ArenaMedicalChecks: 74 Assertions und beide Fehlerkontrollen grün.
- Zwei unabhängige Reviews ohne offene P0–P2; siehe
  `SURGICAL-WALK-REVIEW.md` und `MUTATORS-MENU-REVIEW.md`.

## Native Prüfung

GPU-2 `192.168.178.109`, Profil SPT-GPU-2, EFT 0.16.9.5, Prozess `b68a888e`
ab 20:55:13. Zu Beginn und Ende alle 24 maschine-Pluginidentitäten und Core
zwischen VM-Datei, Host-Datei und geladenem Modul identisch. UiBridge
`0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`, SHA256
`306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E`.
Beim Testbeginn kein anderer aktiver Bediener, keine fremde Kontroll-Lease.
Der zuvor laufende Client wurde im freien Hauptmenü ordentlich beendet.

Menü: echte native Modusbuttons angeklickt. Deathmatch, Gun Game und Tactical
1v5 zeigen COLORED TRACERS / OFF; Team-DM zeigt ON. Die vier gespeicherten Werte
entsprechen genau diesem Stand. Tactical 5v5 nicht extra mit einem PFT-Roster
gestartet; seine Trennung ist offline und im Lifecycle-Review geprüft.

Factory-Deathmatch mit aktivem ArenaSession-Latch, neun Botgegnern, keine
Respawns des Testspielers. Beide Geh-Aufrufstellen und die gemeinsame Aktivierung
wurden im laufenden Prozess als gebunden gelesen. Vorübergehend MedicalUseSpeed
von 2 auf 1, Surv12 und CMS in das geschützte Raid-Inventar gegeben; native
Quickslots 9 und 0. Zerstörte Körperteile durch UiBridge-Health-Hook, gegnerischen
Schaden zwischen diesen Auslösern über den temporären Test-Godmode abgehalten.

| Prüfschritt | Beobachtung |
|---|---|
| Surv12-Abschluss | Arm bzw. Bauch von 0 auf 1 HP restauriert; Ressource verbraucht, MedsController anschließend zur Waffe gewechselt |
| CMS-Abschluss | Rechter Arm von 0 auf 1 HP restauriert; anschließend normale Waffe |
| Gehziel während Surv12 | Spieler erreicht den anderen Spawnpunkt, während UsingMeds/HealingLegs noch aktiv sind und der Bauch noch 0 HP hat |
| Movement-Master AUS | Unter laufender Surgery unveränderte Position und native ActualLinearVelocity 0,0 m/s, trotz angefordertem Gehpfad |
| Movement-Master AN | 21:04:26,831: Surv12, UsingMeds/HealingLegs, Arm 0 HP und native ActualLinearVelocity 0,9999956 m/s |
| Abschluss entfernt Begrenzung | 21:04:27,447: Arm 1 HP, medizinische Flags entfernt, ActualLinearVelocity 3,70808816 m/s; danach Waffe |
| Manueller Abbruch | Nach 2,5 s native ToggleShooting/EndShooting; Arm bleibt 0 HP, beide medizinischen Flags weg, FirearmController verfügbar und das 22,5-m-Gehziel erreicht |
| Hindernisse | Erster automatischer Weg endet an einem tatsächlichen Gitter; kein Durchlaufen. Für den Vergleich anschließend freien 22-m-Pfad verwendet |

Die numerischen Proben samt Zeitpunkten stehen in der ignorierten Datei
`artifacts/surgical-walk-gpu2/observations.json`. Die erste CMS-Probe war bereits
nach dem Heileffekt: dessen Abschluss ist belegt, eine eigenständige quantitative
Geschwindigkeitsreihe während CMS wird daraus nicht behauptet. Die Cap-Messung
unter aktivem Heileffekt stammt von Surv12. Keine Third-Person-Beurteilung der
Fußanimation und kein gewöhnlicher PMC-Kontrollraid; diese Grenzen bleiben bestehen.

## Rückstellung und Log

MedicalUseSpeed zurück auf 2, Movement/Enabled auf true. Gespeicherte
Config-Zuweisungen nach dem Test identisch zum Stand unmittelbar nach Laden des
neuen Builds; lediglich Kommentare zu Zahlenbereichen wechselten Dezimaltrennzeichen.
Godmode beim Raidende automatisch aus, kein Gehauftrag mehr aktiv. Rückkehr
ins Hauptmenü mit 440/440 HP, Energie/Hydration jeweils 100. Serverseitiger
Vorher-/Nachhervergleich: Inventory (340 Items), Health, Skills und Quests exakt
gleich, Erfahrung unverändert 12267. Testitems und Verletzungen nicht persistiert.
EFT danach ordentlich beendet; der bereits vor dem Test laufende SPT-Server bleibt an.

Eingefrorenes vollständiges Clientlog:
`artifacts/surgical-walk-gpu2/client.log`, SHA256
`A051114DA18E29FEB360602C1F62381C66640B71735FAAC2EA4978BB6230C44B`.
Keine Error-/Fatal-Zeilen und keine SurgicalWalk-Warnung. Ein separater
Arena-Hinweis um 20:58:32: `Arena hitmarker audio skipped: load_kill, Only one mono
PCM format is supported.` Dieser Audio-Ladefehler wird hier nicht als Folge der
Bewegungsänderung eingeordnet oder behoben. Keine Aussage über seine Ursache.
