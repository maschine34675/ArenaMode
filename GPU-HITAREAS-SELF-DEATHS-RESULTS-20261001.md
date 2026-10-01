# SPT-GPU-1: Kartenbuttons und eigene Tode

## Ergebnis

Die beiden angeforderten Korrekturen sind umgesetzt und nativ geprüft.

- `a387581`: volle native Buttonfläche unter den goldenen Kartenlabels.
- `8fd3e03`: eigene Tode ohne fremden Angreifer als separate Killfeed-Anzeige.
- Client-SHA256: `39CA280EF8EC44D41A9281BEAC12982446D6694FADE722E1FD0F372F013173B3`.
- SPT-GPU-1, 1920×1080, EFT 0.16.9.5, UiBridge 0.7.3; neue Instanz
  `fab347a7`, gestartet 17:46:09. Vor und nach dem Raid: 24/24 Maschine-Module
  lokal = Hostdatei = tatsächlich geladen, Redeploy.Core ebenfalls identisch.
- Server/Core/BigBrain unverändert; direkte Auslieferung ausschließlich lokal
  und Host 1. Beleg: `HITAREAS-SELF-DEATHS-DEPLOYMENT-20261001.json`.

## Menü

Auf dem vorher geladenen Client `a8b8dd1` war BAY 5 reproduzierbar nur
54 × 5 Pixel groß (`[633,145 54x5]`). Das ausgeschaltete native TMP lieferte
keine bevorzugte Textgröße mehr an das Layout. Der eigene goldene Text blieb
trotzdem in voller Größe sichtbar.

Mit der Korrektur:

| Karte | Native Toggle-Fläche | Geprüfter Klick |
|---|---|---|
| BAY 5 | `[633,131 122x32]` | `(721,150)`, rechts außerhalb der alten Fläche |
| EQUATOR | `[815,550 159x32]` | `(939,566)`, am rechten Ende der Beschriftung |

Beide Klicks wählten die jeweilige Karte aus (`checked=true`). Die Screenshots
zeigen den vollständigen hellen Hintergrund unter dem ganzen Namen, dunkles Gold
im ausgewählten Zustand und das passende Thumbnail. Anschließend PMC gewählt:
Originalnamen `ARENA: BAY 5`/`ARENA: EQUATOR`, ursprüngliche Positionen und
normale native Abmessungen wiederhergestellt. Die bereits vorhandenen dicht
nebeneinander liegenden ArenaMapPort-Spike-Buttons im PMC-Menü wurden nicht verändert.

Belege unter `artifacts/gpu-hitareas-20261001/`:
`bay5/step-2-screenshot.png`, `equator/step-2-screenshot.png`,
`pmc-restore/step-2-screenshot.png` plus jeweilige `report.json`/`report.md`.

## Killfeed im Raid

Deathmatch auf Equator, vorhandene Konfiguration unverändert, Killcam aus.
Native Todesereignisse wurden über den UiBridge-Testhook ausgelöst; keine
direkten Aufrufe unserer Killfeed-Methode und keine künstlichen HUD-Zeilen.
Godmode schützte vor fremden Treffern, der Kill-Hook umgeht diesen Schutz und
ruft die echte native Tötung auf. Godmode endet mit dem Tod.

1. Erster Tod ohne Angreifer: genau eine sichtbare Zeile
   `SPT-GPU-1 / USEC · DEATH` mit Totenkopf, ohne Angreiferpfeil, Waffe,
   Entfernung oder Headshot. UiBridge meldete Tod durch „nobody“.
2. Nach Respawn erneut derselbe Profilname: neue einzelne Todeszeile erscheint;
   keine Sperre durch die frühere Körperidentität. HUD danach 2 Tode.
3. Godmode explizit aus, echte Backspace-Eingabe: genau eine Zeile
   `SPT-GPU-1 / USEC · GAVE UP`. Bereits nach dem nächsten Respawn noch sichtbar.
   HUD 3 Tode. Kills, Assists und Punkte bei allen drei Prüfungen unverändert 0.

Belege: `death1/report.md` und `death1/step-13-screenshot.png`,
`death2/report.md` und `death2/step-5-screenshot.png`,
`giveup/report.md` und `giveup/step-4-screenshot.png`.

Zusatzversuch Selbstgranate **nicht als bestanden gewertet**: Die vorhandene
Brandgranaten-Prototyp-Einstellung wandelte die RGD-5 in eine Brandfläche um
(Log 17:57:33–17:57:38), während der Spieler schon Bot-Schaden erlitten hatte.
Die Erwartung `EXPLOSION` lief deshalb ohne Treffer ab; keine geeignete isolierte
Prüfung einer nativen Selbstexplosion. `grenade/report.md` bewahrt den Fehllauf.
Selbstaggressor, alle Damage-Enums und ihre Formatierung sind offline geprüft,
aber nicht jede Todesursache wurde nativ ausgelöst. Kein neuer Tactical-/TDM-Raid.

## Prüfungen und Rückkehr

AreaMapPreviewChecks, BarbedDeathChecks (328 Assertions und Fehlerkontrollen),
KillfeedTextChecks, ArenaUiAssetChecks (291 Assertions und Fehlerkontrollen)
und TeamGiveUpChecks (zuletzt 626 Assertions plus Fehlerkontrollen) bestanden.
Der GiveUp-Recorder bestätigt den Anzeigeaufruf erst nach erfolgreichem Tod und
beendeter administrativer Sperre. Separater adversarial Review ohne konkrete
offene P0–P2-Befunde. Isolierter Release-Build erfolgreich, vier vorhandene
Nullable-Warnungen, keine Fehler. Desktop-HarmonyX-Probe bleibt wegen ihrer
bekannten Initialisierungsinkompatibilität unavailable; die echte Unity-Hook-
Verkettung wurde hier mit dem geladenen Client ausgeführt.

Raid um 17:58:29 regulär über DISCONNECT verlassen und native Ergebnisfolge
bis MainMenu durchlaufen. Match-Ende: 0 Kills / 4 Tode / 0 Punkte. Keine neue
`uncredited death display failed`- oder `give-up death display failed`-Meldung.
Das Log ist nicht völlig fehlerfrei: Mikrofoninitialisierung und einmal
`AsyncUploadManager: Failed to close file archive` beim Menüwechsel; außerdem
die absichtlich werfende `RespawnApiProbe.AlwaysThrow`-Testsubskription. Daraus
folgt keine pauschale Fehlerfreiheit anderer Mods.

Profilvergleich vor/nach Raid: Inventory, Health, Skills, Quests und Encyclopedia
inhaltlich gleich; Erfahrung 210 → 210. Arena-Konfiguration zeilenweise unverändert.
Keine Profildatei zurückkopiert, keine fremden Konfliktdateien bereinigt.
Spiel regulär beendet (UiBridge nachweislich nicht mehr erreichbar), Server
anschließend per Ctrl-C beendet. Damit beide wie zu Testbeginn aus.

Logkopie: `artifacts/gpu-hitareas-20261001/LogOutput-SPT-GPU-1-2026-10-01_17-45-46.log`.
