# WallpaperControl Remote – produktive Integration

Stand 25.11.2026. Grundlage ist die ausdrückliche Benutzerfreigabe, WallpaperControl anzupassen und die produktive Android-App zu erstellen. Damit wurde die vorherige Beschränkung auf den isolierten Machbarkeitsnachweis aufgehoben. Branch `codex/remote-production` basiert unverändert auf dem bestätigten Remote-Prototyp einschließlich Reminder-Erweiterungen; kein Reset, Merge oder Push.

## Implementierter Umfang

- WallpaperControl **2.6.0** enthält einen abschaltbaren in-process Kestrel-HTTPS-Server. Standardzustand bleibt deaktiviert.
- **WallpaperControl Remote 1.0.0**, Application-ID `de.wallpapercontrol.remote`, Android 12+/ARM64, besitzt eine eigene Release-Signatur und die kompakte dunkle Hauptseite nach der Benutzerreferenz.
- Gemeinsame versionierte Verträge, REST-Befehle und vollständige SignalR-/WSS-Zustände. Kein separater Serverprozess und keine parallele Wiedergabeengine.
- QR-Kopplung mit ursprünglicher monotoner Fünf-Minuten-Frist, einmaliger Reservierung, Vergleichscode, lokaler PC-Bestätigung und individuellen Credentials. Standardmäßig kein Deaktivierungsrecht für den Besuchermodus.
- PC-Geräteverwaltung mit Umbenennen, Rechteänderung und Widerruf. Bestehende Sitzungen werden bei Rechteänderung getrennt und mit aktuellen Rechten neu authentifiziert; Entfernen sperrt bestehende Tokens und aktive WSS-Sitzungen.
- Native Windows-NLM-Profilprüfung/Ereignisse sowie DNS-SD; Android NSD, letzte erfolgreiche Adresse und manuelle lokale Adresse als Fallback. Adress-/Profiländerung entzieht dem alten Listener die Freigabe und prüft ausschließlich den bewusst gewählten Adapter neu.
- Weiter/Zurück, manuelle Pause/Fortsetzen, Layout, Intervall, Shuffle, Wechsel zu hinterlegtem Video sowie Besucheraktivierung/-deaktivierung über vorhandene Desktopmethoden. Desktop-/Widget-/Automatikänderungen erscheinen im selben Livezustand.
- Keine Medienübertragung, Vorschaubilder, Bildzähler, Dateinamen, lokalen Medienpfade, Ordner-/Playlist- oder Widgetverwaltung. Homescreen-Widget nicht implementiert.

## Quellstruktur

| Ort | Verantwortung |
| --- | --- |
| `WallpaperControl.Remote.Contracts` | Datensparsame DTOs, explizite JSON-Metadaten, SPKI-Vertrauensregel und lokale Endpunkte |
| `WallpaperControl.Remote.Server` | Kestrel, REST/WSS, Limits, widerrufbare Geräteauthentifizierung, DPAPI-Vault, Kopplung und native Netzwerk-/DNS-SD-Anbindung |
| `WallpaperControl.Remote.Client` | Gepinnte REST-/WSS-Transporte, Vollabgleich, Epoch-/Revisionseinordnung und Ergebnisabruf ohne blinde Wiederholung |
| `WallpaperControl/Forms/MainForm/MainForm.Remote.cs` | UI-Dispatcher, echte Zustandsprojektion, Fähigkeiten und Aufrufe vorhandener Steuerungsmethoden |
| `WallpaperControl/Remote` | Desktop-Lebenszyklus, privater Adapter, Einrichtung, Geräteverwaltung und optionale explizite Firewallaktion |
| `WallpaperControl.Remote.Android` | Native MAUI-Hauptseite, QR-Kamera, SecureStorage, NSD, Verbindungslifecycle und Einstellungen |
| `WallpaperControl.Remote.Tests` | Isolierte API-/Sicherheits-/Mehrgeräte- und echte MainForm-Integrationstests ohne reale Wallpaperänderung |
| `build/Build-Remote.ps1` | Reproduzierbarer Desktop-/Android-Releasebuild, dauerhafter lokaler Signierschlüssel, Signaturprüfung und Hashmanifest |

Die Desktoplösung enthält Contracts, Server, Client und Tests. Android wird bewusst mit eigener stabiler .NET-11-Toolchain gebaut; ein Desktopbuild benötigt keinen Android-Workload.

## Gemeinsame Wiedergabelogik und Schutzgrenzen

`AdvanceWallpaperAsync` bleibt für Navigation maßgeblich. Die vorhandenen Bild-, Besucher-, Video- und Ownershippfade wurden nicht dupliziert. Für den Remoteaufruf wurden kleine Rückmeldegrenzen ergänzt:

- `PauseSlideshowCore(showError)` kann ohne modalen Fehlerdialog arbeiten; der bestehende parameterlose Desktoppfad bleibt erhalten.
- `ApplyWallpaperPosition(position, showError)` ist die gemeinsame Layoutoperation; der Desktophandler ruft sie ebenfalls auf.
- `ApplySlideshowOptions(showError)` liefert einen verifizierten booleschen Erfolg und bleibt die gemeinsame Windows-/Registryoperation. Besucheroptionen verwenden den vorhandenen Besucherdienst und dieselbe Restzeituhr.
- Video verwendet `ApplyWallpaperModeAsync` und den gespeicherten Desktopvideopfad. Remote nimmt keinen Dateipfad entgegen und bestätigt keinen fehlgeschlagenen/noch wiederherzustellenden Videostart als fertige Wiedergabe.
- Besucherwechsel verwenden `VisitorModeService.SetActiveAsync`. Während Visitor sind normale Modi und Quellen gesperrt. Deaktivierung braucht die aktuelle gerätespezifische Berechtigung; pausierte Besucherordner behalten ihre eigenständige Restzeit und den normalen Resume-Snapshot.
- Pausengründe bleiben unabhängig. Remote-Resume löscht ausschließlich manuelle Pause und hebt Vollbildsperren nicht auf.

Befehle werden auf dem UI-Thread serialisiert, unmittelbar dort nochmals gegen Fähigkeiten, Epoch/Revision und aktuelle Geräteberechtigung geprüft und nach Ausführung anhand tatsächlichen Zustands/Fehlers/Persistenzrevision bestätigt. Eine bereits begonnene Aktion kann bei späterem Widerruf nicht rückwirkend ungeschehen gemacht werden; noch nicht gestartete Aktionen werden erneut geprüft. Die HTTP-/WSS-Verbindung selbst steuert keine Bildwechsel.

Beim echten Programmende wird die Remoteannahme gesperrt, bevor die vorhandenen Besucher-/Video-Shutdownpfade ausgeführt werden. Die native Besucherabsicherung, sichere BMP-/Videoframeübergabe, Ownershipgenerationen und bisherige Reihenfolge von Medienfreigabe und Hostabbau bleiben erhalten. Netzwerkshutdown ist kein Vorbedingungs-Warten vor der sicheren Medienübergabe. Tray-Verbergen zählt weiterhin nicht als Programmende.

## Zustände, Synchronisation und API

`GET /api/v1/state` liefert vollständigen Zustand und Fähigkeiten. `POST /api/v1/commands` nimmt typisierte Befehle mit ID, Epoch und erwarteter Revision entgegen; `GET /api/v1/commands/{id}` liefert nur das Ergebnis des authentifizierten aufrufenden Geräts. `/api/v1/events` ist ein authentifizierter WSS-SignalR-Kanal ohne Steuerungsmethoden. Kopplung hat drei eng begrenzte HTTPS-Endpunkte für Antrag, Claim und Abschluss; Geräteverwaltung bleibt lokal am PC.

Der Zustand umfasst tatsächlichen Modus, Medienart, manuelle Pause und Gründe, Busy, Visitor, Intervall, Shuffle, Darstellung, erlaubte Aktionen und optionale vorhandene Restzeit. Medienidentität bleibt intern; ein privater Playbackstamp erhöht die Revision bei Bildwechsel, ohne den Pfad zu übertragen. Metadatenzeit und Countdown allein verändern keine Befehlsrevision. Derselbe Zustand wird nicht wiederholt publiziert; Countdown höchstens einmal pro Sekunde. Ohne verbundene Geräte entfällt die regelmäßige MainForm-Zustandsabfrage.

Zustandsprojektion und Steuerung laufen auf dem UI-Thread; Versand ist durch einen DropOldest-Kanal der Kapazität eins entkoppelt. IDs werden zehn Minuten, maximal 1024 Ergebnisse lang pro Gerät dedupliziert. Unterschiedliche Payload bei derselben ID wird abgelehnt. Nach unklarer Antwort wird höchstens das ursprüngliche Ergebnis abgefragt, niemals automatisch Navigation erneut ausgeführt. Prozessneustart erzeugt eine neue Epoch. Die App verwirft veraltete Zustände und fordert nach jeder neuen Verbindung einen vollständigen Snapshot an.

## Sicherheit

TLS-Vertrauen entsteht ausschließlich durch den QR-Pin (SHA-256 über SPKI). Validiert werden exakter Schlüssel, gültige Zeit, Server-Auth-EKU, RSA-Schlüsselstärke und SHA-2-Signaturalgorithmus. Kein globaler Accept-All-Validator, kein HTTP-Fallback, kein Rootzertifikatimport. REST und WSS verwenden getrennte gepinnte Transportinstanzen; der WSS-Negativtest läuft ohne anschließenden RESTaufruf. Redirects/Proxys sind aus, Bearer-Tokens nur in Headern. Querystrings und Browser-Origin werden zurückgewiesen. Fehlerantworten enthalten keine Exceptions, Dateien oder Requestinhalte. Kestrel-Konfiguration aus Umgebungsvariablen/externen Dateien ist deaktiviert; Entwicklungsfehlerseiten bleiben auch bei geänderter Umgebung aus.

Der PC speichert nur Hashes permanenter 256-Bit-Gerätetokens; Server-PFX, Konfiguration und Geräteablage sind CurrentUser-DPAPI-geschützt und atomar ersetzt. Nicht lesbare Daten führen zu geschlossenem Zugriff statt automatischem Reset. Zertifikatsverlängerung erhält den SPKI-Schlüssel. Die App speichert eigene Credentials mit SecureStorage/Keystore, ohne QR-Einmalgeheimnis und ohne Appbackup. Rechteänderungen erhöhen die Grantversion, und jeder Befehl wird vor der echten Ausführung nochmals geprüft.

Bindung ausschließlich an ausgewählte lokale IPv4-/ULA-/Link-Local-Adressen eines tatsächlich privaten physischen Adapters. Peer muss im zugehörigen lokalen Präfix liegen. Tunnel/VPN/virtuelle Adapter sind nach Typ und bekannten Treiberbezeichnungen ausgeschlossen. Native NLM-/Adressereignisse sperren Annahme und laufende Sitzungen; ein zusätzlicher 15-Sekunden-Audit prüft die private Zuordnung. Fällt die private Überwachung aus, bleibt Remote geschlossen. Globale IPv6-/Internetendpunkte sind ausgeschlossen.

Die optionale Firewallaktion wird nur nach bewusstem Klick und UAC-Bestätigung ausgeführt. Sie erstellt eine app-/port-/adaptergebundene Private/LocalSubnet-Regel; `LocalAddress Any` bezieht sich dabei ausschließlich auf diesen Adapter, damit DHCPwechsel keine statische IP-Regel ungültig machen. Kestrel bindet trotzdem nur dessen aktuell geprüfte lokale Adressen. Der allgemeine `dotnet`-Host wird für diese Freigabe ausdrücklich abgelehnt. Vom Agenten wurde keine Regel ausgeführt und keine bestehende Netz-/VPN-/Routerkonfiguration verändert.

Limits: 8 KiB Request-/Hubnachricht, 48 HTTP- und 32 Upgradeverbindungen, maximal 32 Geräte, Pairinganträge fünf pro Minute/IP, sonstige Requests begrenzt, Client-HTTPantworten und Transportbuffer auf 64 KiB begrenzt. Diese Grenzen ergänzen Auth/TLS und ersetzen keine lokale Betriebssystemsicherheit. Schadcode im selben Windowsbenutzer-/Administrator-/Android-Appkontext liegt außerhalb des DPAPI-/Keystore-Schutzversprechens.

## Toolchain, Signierung und Auslieferung

Desktop bleibt .NET 10: SDK 10.0.404, veröffentlichte Core-/WindowsDesktop-/ASP.NET-Laufzeiten 10.0.15. Die Runtimeversion ist pro Frameworkreference gesetzt, damit die anders versionierte Windows-SDK-Referenz nicht fälschlich als 10.0.15 aufgelöst wird.

Für Android wurde .NET SDK **11.0.100** mit SHA-512-Prüfung in einer privaten Installation eingerichtet und `maui-android` als stabiler Workload installiert. Android SDK/Ref **37.2.29**, MAUI **11.0.10**, Compile/Target API **37**, Mindest-API **31**, JDK **21.0.8**. Kein RC-SDK mehr. .NET 11 verwendet CoreCLR; die frühere Mono-AOT-Property wurde durch `PublishReadyToRun=false` ersetzt. SignalR-Client **10.0.15**, QRCoder **1.9.0**, ZXing-MAUI **0.10.4**, ZXing.Net **0.16.11**. Paketversionen/Attributionen stehen in `docs/remote-evidence/dependencies.json` und den ausgelieferten Drittanbieterhinweisen.

Die APK wird als Release mit eigenem RSA-3072-Schlüssel signiert, nicht mit dem Experiment-/Android-Debugkey. Der private Schlüssel bleibt außerhalb Git/Artefakten unter `%LOCALAPPDATA%\WallpaperControl.BuildTools\RemoteSigning`, das zufällige Passwort ist DPAPI-geschützt. Es wird nicht als Klartext-CLIargument oder Log ausgegeben. Unvollständige Signierablage führt zum Abbruch statt Schlüsselwechsel. `apksigner verify` und ein SHA-256-Artefaktmanifest prüfen das Ergebnis. App-ID, Signieridentität und aufsteigender versionCode sichern den vorgesehenen Updateweg ohne Deinstallation.

Die APK ist nicht debuggable, verbietet Cleartext und Backup und verlangt nur INTERNET, Netzwerkstatus, versionsabhängigen LAN-Zugriff und Kamera plus die interne AndroidX-Signaturepermission. Keine Standort-, Medien-, Speicher- oder Biometrieberechtigung. Die global installierte SDK-/Workloadkonfiguration blieb unverändert. Desktop wird self-contained als vollständiger Ordner einschließlich des unveränderten externen Videopakets veröffentlicht; keine bestehende Installation wurde ersetzt.

## Prüfungen und Nachweise

Die bestehenden **6302 Regressionstests** sowie **61 produktive Remote-Prüfungen** wurden erfolgreich ausgeführt. Neue produktive Prüfungen decken echte HTTPS-/WSS-Verbindungen, Authentifizierung, individuelle Rechte, Widerruf, JSON-Vollzustände, Deduplizierung, Fehlergrenzen, fünf gleichzeitige Clients, Burstkonvergenz, native private Profilabfrage, Profilevent-Abonnement und DNS-SD-Registrierung/Abmeldung ab. Die echte MainForm wird mit deaktivierten Desktopdiensten und isolierten Testmedien/-Stores geprüft: Besucherblockaden, Besucherpause, Intervall/Shuffle, unveränderter normaler Resume-Snapshot, erhaltene Vollbildpause und pfadfreie Zustände. Es werden keine echten Wallpaper geändert.

Die TLS-Matrix prüft richtigen/falschen Pin sowie fremdes und abgelaufenes Zertifikat mit gleichem SPKI über **REST und separat WSS**. Zertifikatsverlängerung mit gleichem Pin, defekte Ablage und die native Auswahl privater Adapter werden geprüft. Tests der neuen Produktintegration ersetzen nicht die noch ausstehende manuelle Abnahme der veröffentlichten Androidausgabe. Die vom Benutzer bestätigten Pixeltests des früheren Prototyps bleiben historische Nachweise und werden nicht zu Tests der neuen App umetikettiert.

Ein Screenshot des tatsächlichen dunklen Desktop-Remote-Dialogs wurde aus der isolierten WinForms-Instanz gerendert und visuell geprüft; Farben, Eingabefelder und Scrollbereich wurden korrigiert. Ein Bild des produktiven Android-UIs kann ohne verbundenes Gerät nicht als geprüft ausgegeben werden.

## Ressourcenmessung

Gemessen wird ein isolierter WinForms-Testprozess mit der produktiven Serverbibliothek über Loopback und realer nativer Profilüberwachung. Keine reale Wallpaperengine und keine Androidclients. Baseline und aktivierter Server sind getrennte Prozesse; zehn Sekunden Aufwärmzeit liegen vor der Messung. CPU ist Prozent **eines** Kerns, nicht Gesamt-CPU des Rechners. Vollständige Rohdaten liegen bei den Artefakten bzw. nach Abschluss unter `docs/remote-evidence`.

Der erste 600,99-Sekunden-Lauf mit 3-Sekunden-Profilpolling und unnötigen 2-Hz-Zustandsbeobachtungen ergab **1,17 %**, 37,23 MiB private Bytes und 686 Handles. Das vorläufige <1-%-Ziel war damit verfehlt. Daraufhin wurden ereignisgesteuerte Profilüberwachung plus 15-Sekunden-Audit, entfallende Zustandsabfragen ohne Clients und Publikationsdeduplizierung eingesetzt. Der anschließende **600,81-Sekunden-Lauf** ergab **0,91 % eines Kerns**, **28,03 MiB private Bytes**, 33,27 MiB Working Set, 22 Threads (Maximum 24) und 708 Handles. Das CPU-Ziel ist für diesen isolierten Leerlauftest erreicht. Die Abschlusswerte entstehen nach dem letzten periodischen Peak-Sample und können dieses geringfügig übersteigen. Die Rohdaten bleiben unverändert.

Der 30,36-Sekunden-Baseline-Lauf ohne Server ergab 0,82 % eines Kerns, 12,34 MiB private Bytes, 14 Threads und 324 Handles. Diese kurze Baseline ist kein gleich langer Lastvergleich. Die Messergebnisse sind keine Vorhersage für den gesamten Desktop mit allen Widgets/Video und kein Android-Akkuvergleich. Ein 8h-Lauf und reale VPN-/DHCP-/Multicast-/IPv6-Umgebungen bleiben manuell/offen.

## Offene Abnahme und Grenzen

- Neue produktive APK/echte Desktopsteuerung auf dem Pixel gemeinsam abnehmen, einschließlich Appupdate, Bedienung von Desktop/Widgets und Besucher-Absturzschutz mit Testmedien.
- Reale Kombination mit Proton VPN, geänderter IP, blockiertem Multicast, mehreren Adaptern und Link-Local-IPv6 noch nicht durch Benutzer bestätigt. Manuelle lokale Adresshilfe bleibt vorhanden.
- Kein Homescreen-Widget, keine Medien-/Playlist-/Widgetverwaltung und keine Internetfernsteuerung.
- Die App ist zunächst für Android ARM64 und mit deutscher Oberfläche erstellt. Andere ABIs/Sprachen sind nicht als abgenommen ausgegeben.
- Der Agent hat den produktiven Desktop nicht zur Änderung realer Wallpaper gestartet, keine App auf dem Handy installiert und keinen Installer/Release ins Internet veröffentlicht. Die bereitgestellten Ausgaben sind für die bewusste lokale manuelle Abnahme bestimmt.

Einrichtung: [REMOTE_PRODUCTION_GUIDE.md](REMOTE_PRODUCTION_GUIDE.md). Kein automatischer Merge/Push und keine behauptete vollständige Feldabnahme.

## Technische Primärquellen

- [Windows Network List Manager](https://learn.microsoft.com/en-us/windows/win32/api/netlistmgr/): private Kategorie und native Profilereignisse; GUID-Rückgabe über native COM-Schnittstelle statt Automation-VARIANT.
- [DnsServiceRegisterCancel](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsserviceregistercancel) und [DnsServiceDeRegister](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsservicederegister): asynchroner Speicher bleibt bis Callback gültig; DeRegister verlangt null als Cancelparameter.
- [.NET for Android Build Properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties): .NET-11-CoreCLR, R2R und Signierkonfiguration.
