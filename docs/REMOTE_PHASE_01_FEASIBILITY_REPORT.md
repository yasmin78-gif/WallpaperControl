# WallpaperControl Remote – Phase 01: Technischer Machbarkeitsnachweis

Stand: 25.11.2026. **Teilnachweis mit installierbarer Test-APK; Phase 01 noch nicht vollständig bestanden. Phase 02 derzeit nicht zur Freigabe empfohlen.**

Aktueller Wiederverbindungsnachtrag: Der Benutzer bestätigt die Offline-Sperre der Bedienelemente sowie die Wiederverbindung nach Beenden/Neustart des Hosts, meldet dabei aber eine sehr lange Dauer (keine Sekundenmessung). Die Wiederverbindung ist funktional manuell bestätigt, ihre Latenz noch nicht abgenommen. Wiederherstellung nach WLAN-Unterbrechung wurde noch nicht ausdrücklich bestätigt.

Test-APK **0.1.2 / versionCode 3**: `artifacts/remote-phase01/WallpaperControl.Remote.Experiment-0.1.2.apk`, gleicher lokaler Debugschlüssel, Update ohne Deinstallation vorgesehen. Android verwendet jetzt ausschließlich die eigene Retrykoordination; paralleles SignalR-Autoreconnect ist dort deaktiviert. WSS-/Initial-Syncversuche erhalten einen expliziten Fünf-Sekunden-Zeitrahmen, Retrypausen steigen höchstens auf fünf statt 30 Sekunden. Netzwerkrückkehr und neue NSD-Kandidaten wecken die Warteschleife; die zuletzt authentifizierte Adresse wird vor unbestätigten Discoverykandidaten versucht. Ein alternativer Kandidat benötigt gegebenenfalls einen weiteren begrenzten Versuch. TLS-Pins und Authentifizierung bleiben unverändert, neue Kopplung ist nicht erforderlich.

**67 automatisierte Prüfungen bestanden**, einschließlich explizit begrenztem Verbindungsversuch zu einem stummen TCP-Listener und Wiederanlauf/Vollsnapshot im Modus ohne internes SignalR-Reconnect. Heartbeats sind beidseitig auf fünf Sekunden gestellt, ServerTimeout bleibt 15 Sekunden. Die Disconnectprüfung berücksichtigt diese 15 Sekunden plus Schedulingtoleranz statt eines davon unabhängigen Fünf-Sekunden-Testlimits. Der Benutzer bestätigt nach Bereitstellung der Version 0.1.2: „ja, sehr viel schneller jetzt“. Damit ist die spürbar verbesserte Wiederverbindung nach Host-Neustart manuell bestätigt. Eine genaue Sekundenmessung liegt nicht vor; eine feste Latenzgrenze am Pixel wird daraus nicht abgeleitet. Nachweise: `evidence/reconnect-checks.log`, `evidence/android-0.1.2-build.log`, `evidence/apk-0.1.2-verification.log`, `evidence/artifact-0.1.2.json`. Laufender Host, produktive Quellen und Netzwerk-/Firewallkonfiguration wurden nicht verändert.

Nachtrag zur anschließenden Nutzerabnahme: Test-APK **0.1.1 / versionCode 2** liegt unter `artifacts/remote-phase01/WallpaperControl.Remote.Experiment-0.1.1.apk`. Nach einem zunächst nur an Loopback gebundenen Host wurde ein Listener an der LAN-Adresse nachgewiesen. Die Android-App meldete anschließend `JsonException`; die genaue betroffene Antwort und Ursache sind aus dem Screenshot nicht feststellbar. Version 0.1.1 nutzt explizite generierte JSON-Verträge für REST, QR und SecureStorage, liest auch bisherige PascalCase-Speicherdaten und zeigt bei Fehlern die konkrete Kopplungsstufe plus HTTP-Status bzw. JSON-Zeile/Byte an. Es werden keine Antwortinhalte, Tokens oder QR-Geheimnisse angezeigt. Mobile Loopback-QRs werden vor einer Anfrage mit einer verständlichen Meldung abgewiesen. TLS-Prüfung unverändert.

Die erweiterte Windows-Suite besteht mit **61 Prüfungen**, einschließlich derselben TLS-/WSS-Matrix und zusätzlicher Prüfungen der von Android verwendeten JSON-Metadaten gegen echte Kopplungsantworten. Neue Nachweise: `evidence/serialization-checks.log`, `evidence/android-0.1.1-build.log`, `evidence/apk-0.1.1-verification.log`. Die nachfolgenden ursprünglichen Messungen/58er-Protokolle bleiben als eingefrorener Erststand erhalten. Die anschließende erfolgreiche Kopplung ist durch den vom Benutzer bereitgestellten Screenshot belegt (siehe manueller Nachtrag unten). Die installierte APK-Version wurde dabei nicht per ADB ausgelesen. Der laufende Desktop-Testhost und Netzwerk-/Firewallkonfiguration wurden für diesen Nachtrag nicht verändert.

Basis: freigegebener Architekturcommit `3bd3342`, sauberer Branch `codex/remote-phase-00-architecture`, dessen Parent `230bfb3` weiterhin den erweiterten Reminderstand enthält. Neuer Branch: `codex/remote-phase-01-feasibility`. Kein Reset, Merge oder Push. Die verbindlichen Benutzerentscheidungen wurden im Architekturbericht nachgetragen und die entschiedenen Produktfragen dort aufgelöst.

## Manueller Nachtrag: Android-Falsch-Pin-Test bestanden

Der Benutzer bestätigt nach gezielter Aufforderung den erfolgreichen Test mit den erwarteten Ergebnissen: `REST abgewiesen=True` und `WSS abgewiesen=True`, während der normale Testhost weiterlief. Damit ist die Ablehnung eines falschen SPKI-Pins auf dem verwendeten Handy für HTTPS REST und separat für WSS manuell bestätigt. Der zuvor bestätigte normale Verbindungsaufbau dient als positiver Kontrollfall. Ein fremdes Zertifikat und ein abgelaufenes Zertifikat mit korrektem SPKI-Pin wurden am Handy noch nicht bestätigt; diese Fälle bleiben trotz bestehender Windows-Nachweise offen. Phase 01 wird dadurch noch nicht vollständig bestanden.

## Manueller Nachtrag: erfolgreiche Kopplung am 25.11.2026

Quelle: vom Benutzer bereitgestellter Screenshot `Screenshot_20261125-110634.png` (11:06), nach Bereitstellung der APK 0.1.1. Sichtbar sind „Verbunden (TLS + Auth + WSS + Vollsnapshot)“, „Gekoppelt und sicher gespeichert“ sowie ein synthetischer Diashowzustand mit Revision 0. Das belegt den erfolgreichen Appstart, Kopplungsabschluss, die von der App bestätigte SecureStorage-Schreiboperation und den positiven authentifizierten REST-/WSS-Verbindungsaufbau auf dem verwendeten Handy. Der Benutzer bestätigt anschließend ausdrücklich die automatische Verbindung nach App-Neustart ohne erneuten QR-Code-Scan. Damit sind das erneute Laden der gespeicherten Zugangsdaten und die Wiederverbindung nach App-Neustart manuell bestätigt; ein Geräteneustart wurde nicht bestätigt. Die genaue ursprüngliche JSON-Fehlerursache ist nicht isoliert reproduziert; nach der JSON-Vertragsänderung ist der beobachtete Kopplungsfehler in diesem Versuch behoben.

„NSD sucht“ zeigt ausschließlich einen gestarteten Suchlauf, keine erfolgreiche Discovery. Der Benutzer bestätigt anschließend: „reagiert erfolgreich auf alle eingaben“. Damit ist die Reaktion auf die von ihm getesteten Bedieneingaben manuell bestätigt; eine einzeln protokollierte Liste der getesteten Befehle und Richtungen liegt nicht vor. Weiterhin offen: Wiederverbindung nach PC-/Android-Geräteneustart und WLAN-Unterbrechung, sämtliche Android-TLS-Negativtests, DHCP-/VPN-/Multicastfälle und Langzeittests. Der Screenshot allein ist keine vollständige Phase-01-Abnahme. Die folgenden Erststandtabellen und Messprotokolle sind historisch zu lesen; dieser Nachtrag ergänzt den manuellen Prüfstatus, ohne ungetestete Fälle aufzuwerten.

## 1. Ergebnis und Statusbegriffe

**Technisch implementiert** bedeutet vorhandenen Prototypcode; **automatisiert geprüft** einen tatsächlich ausgeführten Test mit Protokoll; **manuell geprüft** eine tatsächlich ausgeführte Bedien-/Geräteprüfung. Der Agent hat keine eigene Pixelbedienung durchgeführt; der oben dokumentierte Benutzer-Screenshot ergänzt den manuellen Nachweis. **Noch nicht geprüft** ist kein Erfolg. Behobene Build-/Testfehler werden von weiterhin offenen Freigabegrenzen unterschieden.

| Prüfbereich | Ergebnis | Nachweisgrenze |
| --- | --- | --- |
| Kestrel im WinForms-.NET-10-Prozess | Implementiert, automatisiert geprüft | HTTPS, UI-Dispatch, Start/Stop, Portkonflikt und erneutes Binden; kein separater Serverprozess. |
| Authentifizierte REST + SignalR/WSS | Implementiert, automatisiert geprüft | Loopback, echte Netzwerktransporte; ein und fünf .NET-Clients. Keine MainForm-Anbindung. |
| TLS-SPKI, REST separat | Automatisiert bestanden | Richtiger Pin akzeptiert; falscher Pin, fremdes und abgelaufenes Zertifikat abgewiesen. |
| TLS-SPKI, WSS separat | Automatisiert bestanden | Eigener WSS-Probeweg ohne anschließenden REST-Aufruf; dieselbe Negativmatrix. |
| Idempotenz, Epoch/Revision, Reconnect | Automatisiert bestanden | Synthetische Befehle, konkurrierende Clients, neuer Prozesszustand auf demselben Port. |
| QR-Kopplung | Implementiert, teilweise automatisiert geprüft | Lokales QR-PNG wird mit ZXing dekodiert; Request/Approve/Claim über HTTPS geprüft. Kamera und Vergleich am Pixel noch nicht geprüft. |
| SecureStorage und App-Neustart | Implementiert, noch nicht am Gerät geprüft | Android-Build allein beweist weder Keystoreverhalten noch erfolgreichen Reconnect nach Prozessende. |
| Windows DNS-SD | Implementiert, native Loopback-Registrierung/Abmeldung geprüft | Native API liefert Status 0; keine Aussage über Discovery vom Handy im LAN. |
| Android NsdManager | Implementiert, kompiliert | API-34+-Callbacks und API-31–33-Kompatibilitätspfad; Laufzeit/Netzwerk nicht geprüft. |
| DHCP/IP-Wechsel, Multicastfilter, gleichnamige Dienste, Proton VPN | Noch nicht im realen Netz geprüft | Gespeicherter Pin wird im Code nie aus NSD ersetzt; Host stoppt bei Adresswechsel und braucht bewussten Neustart. |
| Android/APK | Build und APK-Signatur automatisiert geprüft | MAUI 11.0.10, ARM64, minSdk 31; tatsächlich verwendetes SDK/Workload ist RC. Installation/Start am Pixel offen. |
| Ressourcen | Gemessen, Kurzzeitwerte | Getrennte Prozesse je 30 s sowie kombinierter Client-/Server-Testprozess; kein 10-Minuten- oder 8-Stunden-Nachweis. |
| Manuelle Pixel-Abnahme | Noch nicht geprüft | `adb devices` zeigte kein angeschlossenes Gerät. Es wurde keine App installiert/deinstalliert. |

Die letzte vollständige funktionale Suite zählt **58 bestandene Prüfungen**, Exitcode 0. Kein Emulatorlauf, keine manuelle Pixelprüfung und kein produktiver Wallpaperregressionslauf werden daraus abgeleitet. Der veröffentlichte Self-contained-Host wurde zusätzlich gestartet, über gepinntes authentifiziertes HTTPS angesprochen und normal beendet (`PUBLISHED_SMOKE_HTTPS_AUTHENTICATED_OK`).

## 2. Verwendete Toolchain und Pakete

| Bestandteil | Tatsächlicher Stand |
| --- | --- |
| Desktop SDK | 10.0.404, innerhalb des Experiments gepinnt; Target `net10.0-windows` |
| Desktop-Publish | `win-x64`, self-contained, Single File, ReadyToRun aus, native Libraries zum Extrahieren eingebunden; RuntimeFrameworkVersion 10.0.15 |
| Android SDK für .NET | 11.0.100-rc.3.26522.148; nur im Android-Unterverzeichnis gepinnt |
| Android Workload/Ref/Runtime | 37.2.0-rc.2.84, installiertes Workloadmanifest für 11.0.100-rc.2 |
| MAUI | NuGet-Controls/Core/Essentials/Build Tasks 11.0.10; vorhandene MAUI-SDK-Packs sind 10.x, keine neue globale Workloadinstallation |
| Android-Plattform | Compile/Target API 37, Build Tools 37.0.0, minimum Android 12/API 31, ABI `arm64-v8a` |
| Java | Microsoft OpenJDK 21.0.8+9-LTS |
| SignalR .NET Client | Microsoft.AspNetCore.SignalR.Client 10.0.15 (gemeinsamer `net10.0`-Client für Windows und Android) |
| QR-Erzeugung | QRCoder 1.9.0 |
| Android-QR-Controls | ZXing.Net.Maui.Controls + ZXing.Net.Maui 0.10.4 |
| QR-Decoder | ZXing.Net 0.16.11 (zusätzlich in Checks für lokalen QR-Roundtrip) |

Der Android-Build funktioniert mit dieser konkreten Mischkonfiguration; eine vollständig stabile und reproduzierbar gewartete MAUI-11-Workloadkette ist damit **noch nicht nachgewiesen**. Vor Produktarbeit stabile .NET-11-/Android-/MAUI-Workloads als zusammenpassenden Satz erproben. Keine globale SDK-/Workloadkonfiguration wurde geändert. Das produktive Projekt bleibt unverändert auf .NET 10.

NuGet-Lockdateien liegen pro Versuchsprojekt vor. `evidence/dependencies.json` inventarisiert 143 Paket-/Versionskombinationen einschließlich transitiver AndroidX-/CameraX-Abhängigkeiten, Buildtools und Checks. Metadatenlizenzen: QRCoder, MAUI, ZXing-MAUI und SignalR MIT, ZXing.Net Apache-2.0; AndroidX-Bindings u. a. kombinierte MIT/Apache-/BSD-Ausdrücke. Keine Bibliothek führt QR-Daten an einen externen Dienst ab. Die NuGet-Audits für Checks und Android meldeten über die abgefragten Quellen keine bekannten anfälligen Pakete; das ist keine umfassende Sicherheitszertifizierung und prüft nicht die RC-Toolchain als Ganzes. Lizenztexte/Notices für eine spätere Produktauslieferung gesondert vollständig paketieren.

## 3. Projektstruktur und technische Umsetzung

Alles liegt getrennt unter `experiments/WallpaperControl.Remote/`:

```text
Core/       DTOs, lokale Endpunktprüfung, Zertifikatserzeugung, SPKI-Validierung
Host/       WinForms-Testfenster, Kestrel, synthetischer StateModel, Pairing, native DNS-SD
Client/     Gemeinsamer REST-/SignalR-Client mit getrennten TLS-Transporten
Android/    MAUI-Test-App, Kamera, NSD, SecureStorage, Verbindungsschleife
Checks/     Ausführbare WinForms-/Netzwerk-Regressionssuite (kein VSTest)
Baseline/   Minimale WinForms-Ausgabe ausschließlich zum Publish-Größenvergleich
evidence/   Eingefrorene Logs, JSON-Messwerte, APK-/Abhängigkeitsnachweise
```

`StateModel` kennt ausschließlich synthetische Diashow/Video-, Besucher-, Pause-, Intervall-, Layout- und Shufflewerte sowie Revision und Epoch. Interner Navigations-Testzähler dient nur der Idempotenzprüfung und wird nicht als Bildzähler in der Android-Oberfläche gezeigt. Es gibt keine Referenz auf WallpaperControls MainForm, Mediencontroller, Registrysettings, Bilddateien oder Widgets.

`RemoteHost` startet Kestrel im selben Prozess. Endpoints: `GET /api/v1/state`, `POST /api/v1/commands`, `/api/v1/events` (nur WebSocket/SignalR), `/api/v1/pairing/requests` und `/api/v1/pairing/claim`. Vollständige Zustände werden veröffentlicht, keine Deltas. Befehle verlangen ID, Epoch und erwartete Revision. Der UI-Dispatcher verwendet `BeginInvoke` plus awaitbare CompletionSource; Netzwerkawaits blockieren nicht die WinForms-Message-Loop. Ein 50-ms-UI-Timer im Host bzw. 20-ms-Testtimer dienen der Responsivitätskontrolle, nicht der Wiedergabe.

Pro Gerät/ID wird das letzte Ergebnis samt Payload aufbewahrt, begrenzt auf insgesamt 1024 Einträge; wiederholter identischer Befehl hat keinen zweiten Effekt, andere Payload bei gleicher ID wird abgelehnt. Alte Revision/Epoch wird abgewiesen. Dies ist kein dauerhaftes Exactly-once-Protokoll. Ein Produktionskommandojournal, vollständige Capabilities und Fehler-/Persistenzpostconditions folgen erst später.

Verbindungsfreigabe erfolgt nach WSS, Auth und REST-Vollsnapshot. SignalR-Reconnecting/Closed sperrt die Steuerung, Reconnected holt Vollzustand; Android ergänzt eine initiale Retry-/Discovery-Schleife mit maximal 30 Sekunden Backoff und Foreground-Resync. Offlinebefehle werden nicht gespeichert. Android-Stopped beendet Verbindung/Discovery. Seltene Rennen zwischen alter Verbindung, Discoverykandidat und neuen Epochs benötigen vor Produktion weitere systematische Tests.

## 4. TLS-Nachweise, getrennt nach Transport

SHA-256 über DER-SubjectPublicKeyInfo, konstante Pinprüfung, RSA mindestens 2048 Bit, Server-Auth-EKU und Zertifikatsgültigkeit. Die selbstsignierte Identität wird genau für den gespeicherten Pin akzeptiert; Namens-/PKI-Vertrauen wird nicht global deaktiviert. HTTP-Redirects und Proxyverwendung sind für diese lokalen Clients aus. Nur lokale literale HTTPS-Endpunkte werden akzeptiert, keine Hostnamenauflösung mit ungeprüfter externer Adresse.

REST verwendet einen pro Verbindung erzeugten Handler. WSS verwendet eine explizite `WebSocketFactory` mit `ClientWebSocket.ConnectAsync` und eigenem `HttpMessageInvoker` desselben eng begrenzten Validators; `ProbeWssAsync` benötigt **keinen REST-Aufruf**. Android setzt `UseNativeHttpHandler=false`, um den verwalteten Transport zu verwenden. Genau dieser Laufzeitpfad muss auf dem Pixel noch bestätigt werden.

| Fall | REST / Windows-Loopback | WSS / Windows-Loopback | Pixel |
| --- | --- | --- | --- |
| Richtiger SPKI-Pin und gültiges Zertifikat | Erfolgreich | Erfolgreich | Noch nicht geprüft |
| Falscher Pin | Abgewiesen | Abgewiesen | Testschaltfläche implementiert, nicht ausgeführt |
| Fremdes Zertifikat | Abgewiesen | Abgewiesen | Hostmodus `--tls-foreign` vorbereitet, nicht ausgeführt |
| Abgelaufenes Zertifikat trotz passendem Pin | Abgewiesen | Abgewiesen | Hostmodus `--tls-expired` vorbereitet, nicht ausgeführt |
| Unauthentifiziert/falsches Token | REST anonym 401 | Falsches Token beim Upgrade abgewiesen | Noch nicht geprüft |
| HTTP-Fallback | HTTP zum HTTPS-Port scheitert | Nur WSS vorgesehen | Manifest verbietet Cleartext, Laufzeittest offen |

Für alle drei negativen Zertifikatsfälle ist der Zähler authentifizierter serverseitiger HTTP-Anfragen 0; Credentials erreichen daher nicht den HTTP-Endpunkt des untrusted Servers. Bearerheader werden sowohl bei REST als auch WSS verwendet. Kein AccessTokenProvider mit Querytoken, kein Token in einer URL. Querystrings werden serverseitig grundsätzlich abgewiesen. Serverloggingprovider sind entfernt; UI-/Testmeldungen enthalten weder QR-Geheimnisse noch Tokens. Zusätzliche Browser-Origin wird abgewiesen. Body-/Nachrichtenlimit 8 KiB, Verbindungsgrenze 32, Pairing-Requestlimit fünf pro Minute/IP; übrige Requests separat begrenzt.

Ein früher WSS-Versuch scheiterte mit TLS-EOF. Die finale Variante reimportiert Windows-Testzertifikate aus PFX und nutzt den expliziten gepinnten WebSocket-Transport; die abschließende Matrix besteht. Es wurde kein Accept-All-Validator oder unverschlüsselter Übergang eingebaut. SignalR muss nach den lokalen Ergebnissen nicht ersetzt werden; falls der Androidnachweis scheitert, bleibt der rohe gepinnte WebSocket als Alternative zu untersuchen, ohne Sicherheitsanforderungen zu lockern.

## 5. Vereinfachte QR-Kopplung und Speicherung

Desktop erzeugt JSON mit Protokollversion, lokaler Adresse, stabiler Testserver-ID, SPKI-Pin, sessionId und 256-Bit-Einmalgeheimnis. QRCoder erzeugt lokal das Bild. Android prüft Schema/Länge und verwendet ausschließlich die Kamera/den lokalen ZXing-Decoder.

Server hält gehashtes QR-Geheimnis und ursprüngliche monotone Fünf-Minuten-Frist. Erste gültige Anfrage reserviert atomar; konkurrierende Einlösungen ergeben nur einen Gewinner. Beide Seiten zeigen denselben zufälligen sechsstelligen Vergleichscode. Erst lokale PC-Bestätigung erlaubt Credential-Claim; das Deaktivierungsrecht ist bei jedem Antrag zurückgesetzt und nicht vorausgewählt. Temporärer Claim darf bis zur ursprünglichen Frist dieselben Testcredentials erneut abholen, kein weiteres Gerät erzeugen. Neues QR ersetzt alten Antrag, Prozessneustart verwirft offene QR-Sitzungen.

Individuelle 256-Bit-Tokens, serverseitig Hashes. Zertifikat und Gerätehashstore liegen unter `%LOCALAPPDATA%\WallpaperControl.Remote.Experiment`; PFX und Store werden über CurrentUser-DPAPI geschützt. Der Versuch hat dort ausschließlich eigene Testdaten angelegt. Android speichert Pin/Adresse/ID/Token über SecureStorage; QR-Geheimnis/sessionId werden vor Speicherung geleert, Androidbackup ist deaktiviert. Fehler bei Speicherung werden nicht als erfolgreiche Kopplung gemeldet. Gerätehashes können bei Hostneustart wieder eingelesen werden; dies ist synthetisch automatisiert geprüft, der vollständige PC-/Android-Neustartablauf noch nicht manuell.

Die produktive Geräteverwaltung, nachträgliche Rechteänderung, Widerruf, persistenter Claim-Transaktionsschutz und vollständige Restart-/Crashkonsistenz sind bewusst nicht umgesetzt (Phase 03). Bereits gekoppelte Testgeräte können im Versuch nicht komfortabel entfernt werden. Keine Behauptung produktionsreifer Kopplungssicherheit.

## 6. Netzwerk, Discovery und VPN

Normale Hostausführung startet ohne Listener. Auswahl zeigt Loopback sowie lokale Adressen physischer Ethernet-/WLANadapter. VPN/Tunnel/virtuelle Namen und Typen werden herausgefiltert; vor LAN-Binding muss `Get-NetConnectionProfile` für den ausgewählten Interfaceindex exakt Private zurückgeben. IPv4/IPv6 wird explizit adressgebunden, kein Any-/All-Interfaces-Binding. Bei Adresswechsel wird HTTPS beendet; Profilwechsel werden alle fünf Sekunden geprüft. Das ersetzt noch keine vollständige atomare Interface-/Firewallpolicy für Produktion.

Windows native `DnsServiceConstructInstance`/`DnsServiceRegister` und `DnsServiceDeRegister`, Interfaceindex explizit ungleich 0, Service `_wallpaperctrl._tcp.local`, TXT serverId/version. Der Native-Aufruf einschließlich Callback und Abmeldung wurde über Loopback ausgeführt: `DNS_LOOPBACK_STATUS 0`. Requestspeicher/Callback werden für den asynchronen Aufruf gehalten. Fehler-/Hänger-/Namenskollisionen der nativen API und Abmeldung während noch laufender Registrierung brauchen vor Produktion weitere Tests und eine zeitlich begrenzte Cancellationstrategie.

Android verwendet NsdManager: ab API 34 ServiceInfoCallbacks, darunter ResolveService/Host als begrenzten Kompatibilitätspfad. Kandidaten werden anhand der gespeicherten serverId gefiltert und müssen lokale HTTPS-Adressen sein. NSD ersetzt **niemals** den Pin. Danach müssen gespeicherter Pin und Token weiterhin erfolgreich geprüft werden. Letzte/manuelle lokale IP ist Fallback. Mehrere IPv6-/IPv4-Kandidaten und zeitweise nicht erreichbare gleichnamige Dienste können noch ungünstige Retryauswahl verursachen; eine robuste priorisierte Kandidatenliste ist offen.

Tatsächlicher Rechnerzustand bei Prüfung: nur Ethernet aktiv, Interface 6, Profil **Public**. WLAN und vorhandene andere VPNadapter waren getrennt; eine aktive Proton-VPN-Verbindung stand nicht zur Verfügung. Deshalb keine LAN-Werbung auf Ethernet, kein DHCP-/Profil-/VPN-Wechsel und keine Firewall-/Routeränderung. Die erfolgreiche Loopbackregistrierung ist ausdrücklich kein LAN-Discovery- oder Proton-Nachweis.

Offene Matrix: physisches privates LAN + Pixel, PC-/Appneustart, echte IP-Änderung, paralleles Proton VPN einschließlich dessen unveränderter LAN-/Kill-Switchwirkung, mehrere Adapter, IPv6-Link-Local mit Scope, falscher/gleichnamiger Dienst, blockiertes Multicast und manuelle Adresse. Der Testhost wählt bei Adresswechsel nicht selbständig einen neuen Adapter, sondern stoppt sicher; nach manuellem Hostneustart soll der Android-Client den gespeicherten PC wiederfinden. Vollautomatisches sicheres Rebinding ist derzeit nicht nachgewiesen.

## 7. Android-Kompatibilität und APK

Installierbare, mit `apksigner verify` validierte ARM64-Debug-APK: `artifacts/remote-phase01/WallpaperControl.Remote.Experiment.apk`, Application-ID `de.wallpapercontrol.remote.experiment`, Version 0.1.0 / versionCode 1. APK-Manifest: minSdk 31, targetSdk 37, Cleartext aus, Backup aus. Signatur v3, lokales Android-Debugzertifikat; kein endgültiger Releasekey erzeugt und keine andere App verändert.

Manifestrechte: INTERNET, ACCESS_NETWORK_STATE, CAMERA, ACCESS_LOCAL_NETWORK (auf API 37+ zur Laufzeit abgefragt), außerdem AndroidX-interne appgebundene Signaturepermission für nicht exportierte Receiver. Unbenutzte Biometrie-/Fingerprintrechte aus einer Abhängigkeit werden beim Merge entfernt und sind in der finalen APK nicht vorhanden. Keine Speicher-/Medien-/Standort-/Bluetoothrechte. Kamera erst beim Scannerstart. Android 17 verlangt LAN-Zugriff für targetSdk 37; Buildfähigkeit ersetzt nicht den noch ausstehenden Permissiondialogtest.

Die UI ist ein funktionaler dunkler Versuch, keine fertige Umsetzung der Referenz. Besucherschalter/-befehle oben, Status, QR, synthetische Zustände/Revision/Epoch, Navigation/Pause/Modus und TLS-Proben. Kein Bildzähler, Preview, Dateiname oder Medienpfad. Deaktivierungsrecht wird beachtet, Android fragt verständlich vor Deaktivierung; normale Moduswechsel bleiben bei Visitor gesperrt. Umfangreiche produktive Capabilities folgen erst mit der tatsächlichen Desktopanbindung.

Updates ohne Deinstallation sind über gleiche Application-ID, gleichen Signierschlüssel und passenden versionCode vorgesehen. Der Debugschlüssel bleibt lokal und wird nicht committet. Die spätere Produkt-ID/Releasekey-Strategie ist unabhängig; mit neuem Produktschlüssel darf kein nahtloses Update einer anders signierten Testapp behauptet werden. Installation/Upgrade auf dem Pixel wurde nicht durchgeführt.

## 8. Countdown aus bestehenden Planern

Nur Quellcodeanalyse, keine produktive Anpassung:

- Normale eigene Diashow: `MainForm.Scheduler.cs` besitzt `customSlideshowNextChange`, berechnet aus `GetNextAlignedChange(DateTime.Now, milliseconds)`. Nur bei aktivem Customengine, gültiger Deadline, ohne Pause/Fullscreen/ChangeRunning wäre eine geplante Restzeit ableitbar. Uhrkorrektur, Neukalkulation und laufender Medienwechsel machen eine ständig garantierte sekundengenaue Anzeige unmöglich.
- Besucherordner: `VisitorSlideshowClock.Remaining`, `Paused` und `Due` verwenden monotone TickCount64-Zeit; Restzeit friert bei Pause ein. Gut als serverseitiger Restzeit-Snapshot geeignet, solange Quelle/Busy-/Übergangszustand berücksichtigt werden.
- Native Windows-Diashow, Video und Einzelbild: kein verlässlich auslesbarer nächster Bildwechselzeitpunkt identifiziert. Keine Herleitung nur aus Intervall oder letztem App-Klick.

Empfehlung: später optionales `nextChange` mit Restzeit und Server-Snapshotzeit liefern, bei unzuverlässiger Quelle oder Busy null. Android darf zwischen Snapshots ausschließlich die Anzeige extrapolieren, nie einen Medienwechsel anstoßen. Pause-/Revisions-/Reconnectänderung setzt Anzeige neu; bei Offline ausblenden. Im Prototyp gibt es deshalb keinen erfundenen Countdown und keine zweite Wiedergabezeitplanung.

## 9. Ressourcenmessungen und Auslieferung

Methode A: `Measure.ps1 -Seconds 30`, je ein neuer Prozess desselben frameworkabhängigen Testhosts ohne aktives Kestrel bzw. mit Kestrel, Loopback, keine Clients. Private Bytes, Working Set, Threads, Handles und Prozess-CPUdelta über Stopwatch erfasst. UI-/Profilkontrolltimer gehören in beiden Fällen zur Messung. Readinesszeit enthält absichtlich eine Sekunde Stabilisierung und Prozessstart; Einzelmessung, Warmcache/Reihenfolge nicht normalisiert, daher kein belastbarer Kaltstartvergleich.

| Szenario | Privat MiB | Working Set MiB | Threads | Handles | CPU % eines Kerns | Ready inkl. 1 s Stabilisierung |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| WinForms, Server aus | 15.46 | 58.66 | 15 | 390 | 0.36 | 2476.7 ms |
| WinForms + Kestrel, kein Client | 20.22 | 80.80 | 25 | 671 | 0.88 | 1434.3 ms |

Das vorläufige <1-%-Ziel wurde in diesem kurzen Kestrel-Intervall unterschritten, **nicht über die geforderten zehn Minuten nachgewiesen**. Kein 8h-Stabilitätslauf, keine Android-Speicher-/Akkumessung.

Methode B: `Checks` enthält Server und .NET-Clients in **einem** Prozess, inklusive Prüfcode, Kryptografie, JIT und UI-Testtimer. Szenarien: Baseline, Kestrel, ein Client, fünf Clients, 500 Änderungen, nicht lesender WebSocket mit 3000 Änderungen, Unterbrechung/Reconnect. Rohwerte stehen in `evidence/measurements.json`; deren kurze CPUintervalle sind ausdrücklich **keine separaten Server-Idlewerte**. Beispiele des dokumentierten Laufs: ein Client 76.30 MiB privat / 35 Threads / 912 Handles; fünf Clients 83.63 MiB / 35 / 944; Slow-Peer-Test 100.89 MiB / 52 / 1082. Ein begrenzter DropOldest-Kanal und Transportbuffer verhindern unbegrenztes Snapshotaufstauen; dieser Kurztest beweist noch keine Langzeit-Leakfreiheit.

Netzwerkaktivität dieses Prüflaufs am Haupttesthost: 23 authentifizierte HTTP-/Upgradeanfragen, 886 veröffentlichte Zustände, 182136 Bytes JSON-Nutzdaten vor Transport und Fan-out. Diese Zähler messen **keine** TLS-Wirebytes, keine Pakete und keine Android-/VPN-Aktivität; echte Wire-/LAN-Trafficmessung wurde nicht durchgeführt. Sie sind keine Bandbreitenschätzung. Rohdaten/aktueller Testlauf können bei anderer Schedulingreihenfolge andere Coalescingzahlen ergeben.

Self-contained Single-File-Größen, tatsächlich veröffentlicht:

| Ausgabe | Bytes | MiB |
| --- | ---: | ---: |
| Minimales Baseline-WinForms-EXE | 116107400 | 110.73 |
| WinForms-Kestrel-Testhost-EXE | 144231085 | 137.55 |
| Differenz | 28123685 | 26.82 |
| ARM64-Test-APK (Debug, nicht getrimmt/AOT) | 72660785 | 69.29 |

PDBs liegen zusätzlich neben der EXE; genaue Dateien/Hashes in `evidence/artifact-manifest.json`. Die EXE-Differenz enthält ASP.NET, QR und Prototypcode, ist also keine reine Kestrel-Bibliotheksmessung und kein prognostizierter Produktgrößenzuwachs.

Produktive Publish-/Installerprüfung war read-only: `WallpaperControl/Properties/PublishProfiles/FolderProfile.pubxml` und `installer/Build-Installer.ps1` veröffentlichen ebenfalls win-x64, self-contained, Single File, ReadyToRun aus; externe Videodateien bleiben separat. Ein zusätzlicher ASP.NET-Frameworkverweis passt prinzipiell in dieses Modell und wurde im Versuchsprojekt erfolgreich veröffentlicht. Produktprofil (mit OneDrive-Ziel), Installer und deren Video-/Versionsprüfung wurden nicht ausgeführt oder verändert. Ein echter integrierter Produktinstaller bleibt erst später zu prüfen.

## 10. Reproduzierbare Build-/Testbefehle und Nachweise

PowerShell aus `experiments/WallpaperControl.Remote`:

```powershell
.\Build.ps1
.\Test.ps1
.\Measure.ps1 -Seconds 30
dotnet run --project Checks/Checks.csproj -c Release -- --dns-loopback
```

Nachweise in `experiments/WallpaperControl.Remote/evidence/`: `checks.log` (58 PASS), `measurements.json`, `process-measurements.json`, `dns-loopback.log`, `android-build.log`, `android-properties.json`, `apk-verification.log`, `apk-manifest-summary.txt`, `published-smoke.log`, Publishlogs, NuGet-Audits, Paketinventar und Artefaktmanifest. Desktop-/APK-Build zuletzt ohne Warnungen/Fehler; SDK11 meldet die Verwendung einer Previewversion. Der erste Androidbuild scheiterte an der inzwischen nicht mehr unterstützten HandlerType-Property; final wird das dokumentierte `UseNativeHttpHandler=false` verwendet. API-34-NSD-Deprecations wurden durch aktuellen APIpfad mit älterem Kompatibilitätszweig beseitigt.

Die Tests überprüfen echte lokale Transportaufrufe, keine gemockte TLS-Akzeptanz. Sie erzeugen ausschließlich synthetische Daten/eigene Testidentitäten und verändern keine Windows-Wallpaper. Die produktive Regression wurde mangels Produktänderung nicht gestartet. Abhängigkeiten wurden ausschließlich in Versuchsprojekten referenziert; weder Hauptsolution noch produktive csproj wurden verändert.

## 11. Konkrete manuelle Pixel-Abnahme

### Vorbedingungen, die eine eigene Freigabe/Benutzeraktion benötigen

Abschnitt 12 des Entwicklungsauftrags verlangt vor realem Netzwerkwechsel, Firewallfreigabe oder erhöhten Rechten anzuhalten. Der aktuelle Ethernet-Public-Zustand wurde daher **nicht** umgestellt. Vor LAN-Abnahme muss der Benutzer den tatsächlich vertrauenswürdigen Heimnetzadapter als Private bestätigen bzw. bewusst selbst passend konfigurieren. Keine pauschale Freigabe für Public oder VPN.

Falls die bestehende Firewall den Versuch blockiert, erst eine gesondert genehmigte **Testhostregel** prüfen: genau diese Host.exe, TCP 47831, Private, LocalSubnet und ausgewählter physischer Interface; DNS-SD-Regel nur soweit wirklich erforderlich, ebenso eng. Keine bestehende produktive Regel erweitern. Hier wurde keine Regel angelegt. Proton VPN, FRITZ!Box und Portweiterleitungen bleiben unverändert. VPNtest nur durch den Benutzer aktivieren/unterbrechen, ohne vom Agenten Einstellungen umzuschreiben. USB-Debugging/ADB-Verbindung sowie APK-Installationsbestätigung erfolgen nur bewusst am Pixel; keine andere App deinstallieren.

### Schrittfolge

1. APK aus `artifacts/remote-phase01/WallpaperControl.Remote.Experiment.apk` auf das Pixel übertragen und **WC Remote Experiment** installieren. Alternativ nach bewusst aktivierter USB-Debuggingverbindung `adb install -r <APK>`; noch nicht ausgeführt. Androidversion, Berechtigungsdialoge und Signaturstand dokumentieren.
2. Von der Repositorywurzel `artifacts\remote-phase01\Host\Host.exe` starten. Privaten Heimnetzadapter auswählen, Server bewusst starten. Public muss verweigert werden. Normale WallpaperControl-Anwendung bleibt unbeeinflusst.
3. „Neuer Test-QR“, Android „QR mit Kamera scannen“. Kamerarecht nur hierfür gewähren; bei API 37+ lokalen Netzwerkzugriff bewusst erlauben. Beide Vergleichscodes vergleichen. Am PC zunächst Deaktivierungsrecht **nicht** markieren, dann bestätigen.
4. Android muss nach sicherer Speicherung „Verbunden“ und Epoch/Revision zeigen. Besucher EIN funktioniert, Besucher AUS bleibt ohne Recht gesperrt. Zweites Testgerät bzw. erneute neue Testkopplung mit ausdrücklich gesetztem Recht: AUS zeigt Androidbestätigung, benötigt danach keine erneute PC-Bestätigung.
5. Am Handy Weiter/Pause/Shuffle bzw. synthetischen Modus betätigen; PC-Zustand/Revision ändern sich erst nach erfolgreichem Befehl. Am PC andere Testbuttons verwenden: Handy aktualisiert vollständig. Bei Visitor sind normale Moduswechsel gesperrt; keine tatsächlichen Hintergrundbilder ändern sich.
6. App vollständig schließen, erneut öffnen: kein Scan nötig. Host normal schließen/neustarten: Testidentität/Devicehashes bleiben, neue Epoch, vollständiger Resync. Ein zweites verbundenes Gerät parallel prüfen.
7. WLAN am Pixel durch Benutzer unterbrechen/wiederherstellen. Controls müssen nach OS-/Heartbeat-Erkennung gesperrt und bis zum Vollsnapshot gesperrt bleiben; keine alten Offlinebefehle nachholen. Kein sofortiges Erkennen eines stillen Funkverlusts versprechen (Heartbeattimeout 15 s).
8. TLS getrennt prüfen: normaler erreichbarer Host und gespeicherter Pin müssen in „TLS-Test: gespeicherter Pin“ REST und WSS akzeptieren. Schaltfläche „falscher Pin“ muss beide ablehnen. Dann Host schließen, mit `--tls-foreign` starten, denselben Adapter wählen und starten: **nicht neu koppeln**, gespeicherter Pin muss beide ablehnen. Mit `--tls-expired` wiederholen (gleicher SPKI, abgelaufenes Zertifikat). Danach normal starten und positiven Kontrolllauf wiederholen. Verbindungs-/Authfehler allein sind keine ausreichenden TLS-Negativnachweise; Ergebnisse/Hosterreichbarkeit getrennt notieren.
9. Erst nach gesonderter Netzwechsel-Freigabe IP-Wechsel/mehrere Adapter/Proton und Multicastblockade testen. Host stoppt bei Adresseänderung; bewusst mit neuer privater IP starten, Android soll über NSD denselben gespeicherten PC finden. Manuelle neue lokale Adresse darf denselben gespeicherten Pin weiterverwenden, kein Vertrauensreset. Falschen gleichnamigen Testdienst nur in isolierter Testumgebung anbieten.
10. Update ohne Deinstallation des Experiments mit demselben Debugkey und erhöhtem versionCode separat prüfen. Kein Produkt-/Releasekeywechsel und keine Deinstallation anderer Apps.

Ergebnisprotokoll je Fall: Gerät/Androidversion, Netzwerk/VPNzustand, erwartetes und tatsächliches Resultat, REST/WSS getrennt, Zeitstempel, Status PASS/FAIL/OFFEN. Keine Tokens/QR-Geheimnisse in Screenshots oder Logs aufnehmen.

## 12. Offene Sicherheitsfragen und Empfehlung

Vor Phase-02-Freigabe fehlen mindestens:

1. Tatsächliche Pixel-TLS-Matrix für **beide** Transportschichten und Keystore-/Kamera-/Neustartablauf; derzeit sicherheitskritisch offen.
2. Vollständig stabile Android-/MAUI-11-Toolchain oder eine ausdrücklich neu bewertete Frameworkentscheidung, anschließend wiederholter APK-/Gerätenachweis.
3. Reale abgesicherte Discovery/Neuerkennung mit DHCP, IPv6, falschen Diensten und parallelem Proton VPN, ohne Übernahme fremder Pins; robuste Kandidatenauswahl und zeitlich begrenzte native DNS-Cancellation.
4. Abschließende Netzwerkpolicy bei Profil-/Adapterwechsel und eine explizit genehmigte enge Test-Firewallkonfiguration; aktuell keine reale Public-/Private-/VPN-Abnahmematrix.
5. Längere Ressourcenmessung und späterer Stabilitätsnachweis; 30 Sekunden/kurze kombinierte Lasttests belegen keine Zehn-Minuten-/8h-Ziele.

Der Prototyp belegt die lokale technische Tragfähigkeit von in-process Kestrel, REST, SignalR und SPKI-Pinning unter Windows sowie die Buildfähigkeit der MAUI-11-Test-APK. Er erfüllt wegen dieser offenen Nachweise die vollständigen Phase-01-Abnahmekriterien **noch nicht**. Kein unsicherer Fallback, keine produktive Integration und kein eigenständiger Start von Phase 02. Nächster Schritt ist die vorbereitete manuelle Abnahme und gesonderte Freigabe der dafür nötigen Benutzer-/Netzwerkaktionen.

## Primärquellen

- [SignalR-Client und Transportkonfiguration](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-10.0): WebSocketFactory/Transportoptionen, Timeouts und Buffer; konkrete Sicherheit zusätzlich durch die lokale Matrix geprüft.
- [Windows DNS_SERVICE_REGISTER_REQUEST](https://learn.microsoft.com/en-us/windows/win32/api/windns/ns-windns-dns_service_register_request), [DnsServiceRegister](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsserviceregister): Interfaceindex, asynchroner Callback und Prozesslebensdauer.
- [Android NsdManager](https://developer.android.com/reference/android/net/nsd/NsdManager), [LAN-Berechtigungen](https://developer.android.com/privacy-and-security/local-network-permission): Discovery sowie API-/Target-abhängige Rechte.
- [MAUI SecureStorage](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0), [MAUI-Supportpolicy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui): Speicher-/Backupaspekte und Wartungszyklus.
- Paketidentitäten und Lizenzen stammen aus den tatsächlich wiederhergestellten NuGet-Nuspecs, vollständig im Paketinventar; ergänzend [QRCoder-Lizenz](https://github.com/Shane32/QRCoder/blob/master/LICENSE.txt) und [ZXing-MAUI-Projekt](https://github.com/Redth/ZXing.Net.Maui).
