# Classic Movement und StatTrack: Integration

Zweites Umsetzungspaket nach dem [Forge-Audit](COMPATIBILITY-SURVEY-20261019.md),
anschließend an Continuous Healing/Late To The Party. Normale PMC-/Scav-Raids,
gespeicherte Fremdmod-Einstellungen und vorhandene Statistiken bleiben unberührt.
Keine fremden DLLs oder kopierter Fremdquellcode werden mit Arena ausgeliefert.

## Verhalten und Grenzen

- [Classic Movement 1.1.3+](CLASSIC-MOVEMENT-COMPAT-20261019.md): Arena behält
  native Bewegungszustände für seine Körper und nimmt sie vom konkurrierenden
  Zustandstausch und Geschwindigkeitsnachlauf aus. Das gilt bereits für die
  bestätigte Körpererstellung beim Laden und für spätere Respawns. Die strikten
  Zustandsprüfungen von Doppelsprung/Bunnyhop werden nicht auf beliebige fremde
  Unterklassen erweitert. Bei ausgeschaltetem FAST MOVEMENT bleiben die nativen
  Zustände bestehen; ein bestehender Cache wird nicht mitten im Raid getauscht.
  Andere geprüfte Classic-Movement-Präferenzen bleiben erhalten. Das vom aktuellen
  Fremdserver nicht mehr angebotene alte NostalgiaMode blockiert Arena, wenn es
  trotzdem aktiviert wurde; normale Raids bleiben möglich.
- [StatTrack 2.1.1+](STATTRACK-COMPAT-20261019.md): Arena-Schüsse, -Treffer und
  -Kills werden vor der fremden Sammlung ausgenommen. Das Abmelden eines eigenen
  Körpers löst keinen fremden Statistikabschluss aus, auch beim Respawn oder nach
  Arena-Reset. Exakte schwache Körper-/Waffenbindungen tragen verspätete Ereignisse;
  gleiche IDs allein begründen keinen Besitz. Weder die fremden Dictionaries noch
  vorhandene Dateien werden zurückgesetzt; normale vorherige Speicheraufträge
  bleiben bestehen. Der serverseitige TimesLost-/Versicherungsweg ist gesondert
  quellengeprüft, aber kein pauschaler während Arena deaktivierter Schreibpfad.

Beide Mods bleiben optional. Neuere Versionen werden mit Warnung nur bei passenden
Metadaten, APIs und überprüften Hook-Verträgen versucht. Ein unsicherer vorhandener
Vertrag verhindert Arena, nicht normale Raids. Das ist keine allgemeine Garantie
für künftige Releases oder jede andere Kombination von Mods.

## Prüfstand

Automatisierte Prüfungen am eingefrorenen Produktionsstand:

- Classic Movement: 189 Assertions und 23 kompilierte Fehlerkontrollen.
- StatTrack: 127 Assertions und 20 kompilierte Fehlerkontrollen; veröffentlichte
  Client-/Server-DLLs und der vierparametrige native Inventar-Konstruktor geprüft.
- Unit-Suite: 1.232 bestanden, zwei bestehende Integrations-Überspringungen.
- Gemeinsame optionale Zulassung: 54 Assertions und zwölf kompilierte Fehlerkontrollen.
- Native Menü-/Startprüfungen: 752 Assertions.
- Bestehende Bewegungsregressionen: Doppelsprung 293, Bunnyhop 76, Air Control 155 Assertions.

Der integrierte Client-/BigBrainCompanion-Build ohne Deployment gelingt mit vier
bereits bekannten CS8632-Warnungen. Die unabhängige adversarielle Prüfung fand im
finalen Stand keinen Blocker. Dabei wurden zwei frühe StatTrack-Ladeabbruchpfade,
die tatsächliche CLR-Konstruktorsignatur und zu großzügige Versions-/Schreibpfad-
Annahmen korrigiert. Die sechs geprüften Produktionsdateien sind per Hash fixiert.

Beide Adapterprüfungen wurden durch den Hauptagenten unabhängig erneut ausgeführt.
Implementierung: Classic Movement `72204e8`, StatTrack und gemeinsamer Anschluss
`ebb1039`. Der daraus neu gebaute Client-/Companion-Stand ist lokal und auf GPU2
eingespielt; vorhandene Server-, Core-, CH- und UiBridge-Dateien sind unverändert.

| DLL | SHA256 |
|---|---|
| Client | `2759FE74B2538C884A960C9F28E7D0C7B9E3449C7A00AEA6AD250876B91ECA70` |
| BigBrainCompat | `6594B34E952D3010F62E9ED6D534A85ABDF4B82F3945B7108B5A03D0637C4E66` |

Beide melden `0.1.1+ebb1039ae545ff33705cbdcf34dd83ed3dddd21b`.
Die [native Prüfung](GPU-MOVEMENT-STATS-COMPAT-20261019.md) bestätigt normale
Classic-Movement-Zustände und StatTrack-Speicherung im vorherigen PMC-Raid sowie
native Arena-Zustände, Doppelsprung, Bunnyhop, zwei Respawns und unveränderte
StatTrack-Daten trotz Arena-Schüssen und Kill. Ein nicht vom Tester veranlasster
SPT-Server-Neustart während Arena verlor jedoch die laufende geschützte
Raid-Zuordnung. Der Endschutz wies den veralteten Abschluss korrekt ab. Ein
sauberer Arena-Abschluss mit anschließendem normalen PMC-Raid bleibt deshalb
offen; die vorhandenen Gameplay-Belege ersetzen diesen Lebenszyklustest nicht.
Host-Nutzung, Wiederherstellung der Testeinstellungen und Entfernung der
temporären Fremdmods sind im [Handoff](HANDOFF.md) festgehalten. Ignorierte
Quellen, Payloads, Prüfläufe und Belege liegen unter
`artifacts/movement-stats-compat-20261019/`, `artifacts/classic-movement-checks/`
und `artifacts/stattrack-compat-checks/`.
