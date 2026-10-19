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

Die native Abnahme und das Einspielen des abschließenden Commits folgen separat.
Dieser Stand behauptet keine bereits bestandene Ingame-Kompatibilität. Ignorierte
Quellen, Payloads, Prüfläufe und Belege liegen unter
`artifacts/movement-stats-compat-20261019/`, `artifacts/classic-movement-checks/`
und `artifacts/stattrack-compat-checks/`.
