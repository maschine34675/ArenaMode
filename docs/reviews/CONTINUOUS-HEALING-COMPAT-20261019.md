# Continuous Healing: medizinische Isolation in Arena

Stand: 2026-10-19. Optionaler Vertrag ab **Continuous Healing 1.6.3**.
Neuere Versionen werden nur mit passender Plugin-/Assemblyidentität, denselben
geprüften Methoden und ihren tatsächlich installierten nativen Hooks zugelassen;
dazu erscheint eine einmalige Warnung. Dies bestätigt keine zukünftige
Kompatibilität. Fehlende oder unsichere Verträge sperren ausschließlich Arena.

## Konkreter Konflikt

Die separat bezogene veröffentlichte `ContinuousHealing.dll` 1.6.3 hat SHA256
`26635e3fed8efb77d351b6e0825997735e7378c8f124c4b413c13dc909a3eaaf`.
GUID: `com.lacyway.ch`; Typ: `ContinuousHealing.CH_Plugin`;
Assembly: `ContinuousHealing`, Version `1.6.3.0`.
Der begutachtete Upstream-Stand ist
`Lacyway/ContinuousHealing@1e66a459afbeb85fcbb4f3899871c12726feeb6e` im
separaten Auditverzeichnis `artifacts/forge-top200-20261019/leaf-1/upstream/`.
Der Offline-Test prüft die veröffentlichten DLL-Metadaten; er führt die DLL nicht aus.

`CH_EndHeal_Patch.Prefix` auf
`Player.MedsController.MedsInHandsOperation.OnEffectRemoved(IHealthEffect)`
liest `CancelRequested` **vor** der Prüfung `IsYourPlayer`. Bereits dort kann
der fremde Patch `ClearQueue()` und `Finish()` auslösen. Auch das Aussortieren
von Nicht-Medizineffekten erfolgt vor der Spielerprüfung. Der globale
Spielerabbruch kann somit in die medizinische Operation eines Bots hineinwirken.
Zusätzlich können `CH_StartHeal_Patch` und `CH_CancelHeal_Patch` auf einem alten
Körper mit weiterhin wahrem `IsYourPlayer` die globale Anforderung der neuen
lokalen Heilung verändern.

## Eigener Adapter und Besitzgrenze

Arena präfixt ausschließlich diese drei **fremden Prefix-Methoden**. Beim
End-Prefix wird für eigene Bots und alte Körper dessen Ergebnis auf `true`
gesetzt und der fremde Methodenrumpf ausgelassen. Damit darf das ursprüngliche
native `OnEffectRemoved` weiterlaufen. Start-/Abbruch-Prefixe werden für dieselben
Körper ausgelassen; die ursprünglichen nativen Spawn-/Abbruchaktionen bleiben
erhalten. Der Adapter führt weder `Finish`, `ClearQueue`, Medikamentengabe noch
Callbacks selbst aus. Globale CH-Zustände, Konfiguration und andere Mods werden
nicht geschrieben oder entfernt.

Die drei Produktionsdateien sind:

- `ArenaMode.Client/Compat/ArenaContinuousHealingCompat.cs`: Registrierung,
  Patch-Quittungen, Laufzeitentscheidung, Fehlermeldung und Abschaltung.
- `ArenaMode.Client/Compat/ArenaContinuousHealingContract.cs`: Mindestversion,
  Pluginattribut/Assemblymetadaten, drei exakte Signaturen und native Bindungen.
- `ArenaMode.Client/Compat/ArenaContinuousHealingScope.cs`: schwache Bindung des
  konkreten Körperobjekts an dessen konkrete Spielwelt.

Eine neue Bindung entsteht nur, wenn `ArenaSession.IsExactActiveRaid` das aktuelle
`LocalGame`/`GameWorld`-Paar bestätigt, `LocalGame.GameWorld` und `IBotGame` dazu
passen und `body.GameWorld` dieselbe Welt ist. Karte, Profil-ID, `IsYourPlayer`
allein oder der letzte aktive Raid geben keine Berechtigung. Auch echte
PFT-Begleiter sind Bots hinsichtlich dieser rein medizinischen Spieleroption;
ihre native Versorgung bleibt vollständig erhalten.

Die `ConditionalWeakTable` hält den Körper nicht künstlich am Leben. Seine
exakte Bindung bleibt für verzögerte medizinische Ereignisse nach Arenas
Session-Reset gültig, einschließlich alter lokaler Körper während des folgenden
normalen Raids. Sie betrifft ausschließlich den alten Körper. Ein anderer
Körper derselben Profil-ID oder ein wiederverwendetes Objekt mit anderer
`GameWorld` erbt sie nicht. Shutdown/Neubindung entfernt die schwachen Bindungen.

Continuous Healing bleibt aktiv, solange der gebundene Körper lebendig ist,
`IsYourPlayer` erfüllt und exakt `MainPlayer` der aktuellen Welt ist. Ein noch als
`MainPlayer` eingetragener toter Körper fällt bereits auf das native Verhalten
zurück. Start und Cancel werden ebenfalls beobachtet, damit ein erst beim
Raid-Ende eintreffendes End-Ereignis keinen früheren End-Aufruf benötigt.

Normale PMC-/Scav-Körper und Hideout erwerben keine Bindung; vor der bestätigten
Arena-Aktivierung wird ebenfalls keine neue Körperbindung angelegt.
Bei einem Laufzeitfehler bleibt nur ein bereits nachgewiesener eigener Körper
geschützt; unbekannte Körper erhalten keine Ausnahme. Der Fehler sperrt spätere
Arena-Starts und bleibt als Warnung sichtbar. Fehlende eigene oder fremde
Patch-Quittungen werden vor Auswahl/Start erneut geprüft. Fremde zusätzliche
Postfixe, Transpiler oder Finalizer an den drei geprüften Methoden werden nicht
stillschweigend als derselbe Vertrag akzeptiert.

## Native Grundlage

Alle Angaben wurden gegen `../_decompiled/` geprüft:

- `EFT/Player.cs`, `MedsInHandsOperation.Start` (ab Zeile 19389) registriert
  `OnEffectRemoved` auf dem Gesundheitscontroller des konkreten Besitzers.
- `OnEffectRemoved` (19533) reagiert nativ nur auf `IMedEffect`, prüft die
  native Körperteilwarteschlange und führt Fortsetzung oder `Finish` aus.
  `Finish` (19557) besitzt den eigenen `_finished`-Schutz und entfernt sein
  Ereignisabo vor dem einmaligen Abschluss-Callback.
- `MedsController.Spawn` (19867) startet diese Operation;
  `MedsInHandsOperation.HideWeapon` entfernt den Effekt vor dem Abmelden.
  Ein synchroner Effekt-Callback in der Abwicklung ist deshalb relevant.
- `Player.GameWorld` (24451) wird bei `Player.Create` (28056) gesetzt.
  `Player.Dispose` (30917) meldet den Körper ab und zerstört seine Hände,
  ersetzt diese Weltidentität aber nicht durch einen neuen Raid.
- `EFT.HealthSystem/PlayerHealthController.cs:114` entfernt bei
  `CancelApplyingItem` nativ den Medikamenteneffekt.
- `ArenaSession.OnRaidEnded` löscht seine aktiven Spiel-/Weltidentitäten.
  Deshalb genügt ein späteres globales `IsActive` für alte Callbacks nicht.

## Offline-Abnahme und Grenzen

`pwsh -NoProfile -File tools/ContinuousHealingCompatChecks/Verify.ps1`
kompiliert die **drei echten Produktionsdateien** gegen kontrollierte
BepInEx-/Harmony-/EFT-Grenzen. Es gibt keinen Verweis auf das Clientprojekt und
keinen Deploy-Schritt. Eigenständig geschriebene dynamische Methodenfixtures
testen die reflektierte Identität und den tatsächlichen Adapteraufruf. Fremde
Implementierungen werden weder kopiert noch mitgeliefert.

Die positiven/negativen Kontrollen decken aktuelle lokale Heilung, Bot- und
Nicht-Med-Ereignisse, genau einen Abschluss-Callback, fremde unveränderte
Abbruchzustände, Respawn, toten alten `MainPlayer`, verzögerte Abwicklung,
gewöhnlichen Folgeraid, objektbezogene Wiederverwendung, Patchverlust,
Teilinstallation, API-/Versionsdrift und Fehler beim Gesundheitszugriff ab.
Kompilierte Fehlervarianten müssen jeweils am vorgesehenen Verhaltensassert
scheitern; ein Compilerfehler zählt nicht als erfolgreicher Negativtest.

Die Leaf-Abnahme besteht mit **212 Assertions und 14 kompilierten
Fehlerkontrollen**, einschließlich unveränderter positiver Kontrolle vor und
nach den Fehlerläufen. Evidence:
`artifacts/continuous-healing-checks/759d1c110c374543aee2e722264d099e/`.
Die unabhängige lesende Prüfung fand an dieser Abgrenzung keinen blockierenden
Fehler. Parent-Reverification und kombinierte Abnahme stehen im
übergeordneten Integrationsbericht. Der Offline-Test simuliert
die Harmony-Aufrufgrenze, jedoch nicht das native Mono-Detouring, den echten
Animator oder eine tatsächliche medizinische Behandlung. Er belegt insbesondere
keine Behebung der separat beobachteten `BotFirstAid.CG_ApplyToSelf.method_1`-NRE.
Produktionsbuild, Deployment und native Spielabnahme sind eigene Nachweise.

## Nativer Prüfablauf

Auf einem freigegebenen Testhost die vorhandene CH-Installation beibehalten und
den tatsächlichen geladenen Arena-Build sowie die CH-DLL identifizieren.
`Diagnostics/DeveloperMode` vor dem Start aktivieren. Die begrenzten
`[dev] Continuous Healing ...`-Zeilen unterscheiden native Bot-/Altkörper-Fallbacks
von zugelassener aktueller lokaler Heilung; sie verändern keine Entscheidung.
Pro Bindung erscheint höchstens eine Zeile je Start-/Abbruch-/End-Seam und
Kategorie (Bot, alter lokaler Körper, aktueller lokaler Körper), insgesamt neun.
Frühe Start-/Abbruchereignisse verbrauchen daher nicht die End-Ereignisdiagnose.

1. Im gewöhnlichen PMC-/Scav-Raid mehrere Körperteile beschädigen und ein
   geeignetes Mehrfach-Heilmittel nativ benutzen/abbrechen. CH muss sein
   gewöhnliches Verhalten behalten; Arena darf keinen fremden Körper binden.
2. Im Arena-Raid dieselbe lokale Heilung mit tatsächlicher Item-Benutzung und
   Abbruch prüfen. Ein passender lokaler Diagnoseeintrag muss erscheinen.
3. Einen Arena-Bot durch echte Treffer nicht tödlich verletzen und seine native
   medizinische Aktion abwarten. Währenddessen lokal eine Heilung abbrechen.
   Der native Bot-Fallback muss am tatsächlichen Effekt-Ereignis sichtbar sein;
   bloßer gemeinsamer Start beider Mods ist hierfür kein Nachweis.
4. Während/kurz nach lokaler Behandlung sterben, normal respawnen und erneut
   heilen. Falls ein alter Callback tatsächlich eintrifft, muss der Eintrag ihn
   als alten lokalen Körper unterscheiden. Ohne solchen Callback bleibt genau
   dieser native Fall offen; der Offline-Test ersetzt diesen Nachweis nicht.
5. Geschützt beenden und einen gewöhnlichen Folgeraid starten. Heilung und
   Abbruch müssen wieder ohne Arena-Bindung des neuen Körpers funktionieren.
   Vorhandene CH-Dateien und fremde Einstellungen erhalten; Diagnoseoptionen
   anschließend auf ihren ursprünglichen Stand zurückstellen.

Es sind keine frei erfundenen Testmethoden oder direkten Aufrufe fremder
Heilungs-Prefixe für die native Abnahme vorgesehen.
