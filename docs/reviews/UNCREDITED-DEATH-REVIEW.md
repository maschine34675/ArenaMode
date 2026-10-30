# Eigene Tode im Killfeed

Stand: 2026-10-01.

EFTs `Player.OnDead` ruft `OnBeenKilledByAggressor` nur bei vorhandenem
`LastAggressor` auf. Arena verwirft dort zudem einen Angreifer mit derselben
Profil-ID wie das Opfer. Deshalb fehlen eigene Unfälle und Selbsttötungen im
normalen Kill-Ereignis. Die bisherige Sonderanzeige für Stacheldraht wurde zu
`ArenaUncreditedDeathPatch` erweitert: alle lokalen Tode ohne fremden Angreifer,
bei anderen Akteuren weiterhin ausschließlich die bestehende Stacheldrahtanzeige.

Die Anzeige enthält Opfername, Fraktion und die native Todesursache. Unbekannte
Ursachen heißen DEATH; Fall, Explosion, Blutung, Feuer usw. werden benannt. Das
passende vorhandene Symbol oder der Totenkopf ersetzt die Waffe. Kein erfundener
Angreifer, keine Distanz und kein Headshot. Es entsteht kein `ArenaKill`, keine
Punktänderung und kein Assist-/Announcer-Ereignis. Selbstgranaten behalten ihren
bereits vorhandenen spöttischen Announcer-Pfad.

Der Prefix merkt sich den aktiven Match, damit der letzte Tod auch dann erscheint,
wenn ein nativer Death-Subscriber den Match bereits beendet hat. Ein gewechselter
Match oder eine geschlossene Arena-Session verwirft die Anzeige. Eine separate
schwache Körperidentität verhindert Duplikate, lässt jedoch einen späteren Respawn
desselben Profils erneut erscheinen. Administrative Tötungen bleiben gesperrt.

Aufgeben benutzt weiterhin die administrative Sperre gegen einen möglicherweise
alten fremden Angreifer. Erst nach deren Ende und nach bestätigter synchroner
Tötung meldet der Aufrufer explizit GAVE UP. Die separate TDM-Teamwertung bleibt
unverändert. Killfeed-Master aus unterdrückt alles; fremde Kills aus unterdrückt
keine eigenen Todesanzeigen.

## Prüfung

Der bestehende Ordner `tools/BarbedDeathChecks` testet jetzt die verallgemeinerte
reale Capture-Klasse, DTO, administrative Sperre und extrahierte echte HUD-Methode.
Alle nativen Damage-Enums mit null/eigenem/fremdem Angreifer, Respawn, Duplikate,
Match-Ende/-Wechsel, Verwaltungsfälle, HUD-Schalter und enthaltene Fehler werden
ausgeführt. Fehlerkontrollen entfernen Dedupe und Aggressor-Guard. Die separate
HarmonyX-Probe auf Desktop-CLR ist nicht gleichbedeutend mit Unity/Mono-Abnahme.

KillfeedTextChecks und ArenaUiAssetChecks prüfen die echte Formatierung und die
Wiederverwendung von Zeilen nach Headshot, Blutung, Stacheldraht und eigenem Tod.
TeamGiveUpChecks prüft die bestehende Teamwertung und native Fehlerfälle sowie
den Anzeigeaufruf erst nach erfolgreichem Tod und geschlossener Sperre.
Ein unabhängiger Review fand keine konkreten offenen P0–P2-Befunde. Build,
Auslieferung und native GPU-Prüfung werden separat im HANDOFF dokumentiert.
