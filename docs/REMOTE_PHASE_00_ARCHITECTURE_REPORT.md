# WallpaperControl Remote – Phase 0: Bestandsanalyse und Architekturplanung

Stand: 25.11.2026. Status: Architekturvorschlag zur manuellen Freigabe, keine Implementierung.
Untersuchter Commit: `230bfb308ba6be716e1cae6a94d4bb537bba8a4e` (Desktop 2.5.0).
Branch: `codex/remote-phase-00-architecture`. Vor Beginn war der Git-Arbeitsstand sauber; Ausgangsbranch war `codex/reminder-extended-recurrence`. Im Repository wurde keine `AGENTS.md` gefunden.

## 1. Ergebnis und Umfang

Empfohlen wird eine eigenständige Android-App mit .NET MAUI, ein bei Bedarf gestarteter Kestrel-HTTPS-Server im bestehenden Desktopprozess, eine versionierte REST-Steuerungs-API und SignalR ausschließlich für Zustandsmeldungen. Ein gemeinsamer Desktop-Steuerungsadapter führt Befehle auf dem WinForms-UI-Thread aus und verwendet die bestehenden Wiedergabepfade. Der PC bleibt alleinige Zustandsquelle. DNS-SD/mDNS dient als nicht vertrauenswürdiger Adressfinder; die Identität wird durch den beim QR-Scan übernommenen TLS-Schlüsselpin geprüft.

Die Empfehlung ist noch keine verbindliche Technologieentscheidung. Insbesondere Hosting/Publish, Zertifikatsprüfung in beiden Android-Transportpfaden und Windows-DNS-SD werden in einem begrenzten technischen Versuch vor Produktintegration geprüft.

Enthalten: Weiter/Zurück, Darstellung, Intervall, Shuffle, Bild-/Videomodus bei vorhandener Desktopkonfiguration, manuelle Pause/Fortsetzen, Besuchermodus, Verbindung und Wiedergabestatus, QR-Kopplung, mehrere Geräte und Widerruf. Ausgeschlossen: Medien-/Ordner-/Playlistverwaltung, Widgetkonfiguration, Medienübertragung, Cloud, Konten, Routerfreigaben und Internetfernsteuerung. Das optionale Android-Widget bleibt ein eigener späterer Entscheidungsgegenstand.

## 2. Bestehende Architektur und Integrationspunkte

Alle folgenden Pfade sind relativ zur Repositorywurzel. Methodennamen beziehen sich auf den untersuchten Commit; geplante Typen werden ausdrücklich als Vorschlag bezeichnet.

| Bereich | Dateien und Methoden | Befund / Konsequenz |
| --- | --- | --- |
| Laufzeit | `WallpaperControl/WallpaperControl.csproj`, `Program.cs` | WinForms, `net10.0-windows10.0.17763.0`, STA-Einstieg, ein Hauptprozess mit `SingleInstanceGuard`. Vorhandene Pakete u. a. MailKit, LibreHardwareMonitor, Ical.Net, Sqlite, WebView2; derzeit kein ASP.NET-Frameworkverweis. |
| Fensterkoordination | `Forms/MainForm.cs`, `Forms/MainForm/MainForm.Initialization.cs`, `.State.cs` | Große partielle MainForm als Koordinator, kein bereits isoliertes Playback-Application-Service. Konstruktorreihenfolge und `loading`-Schutz sind relevant. Widgets, Timer und native Ressourcen gehören der Form. |
| Einstellungen | `Forms/SettingsForm.cs`, `Forms/SettingsForm/SettingsForm.Initialization.cs`, `.State.cs`, `.WidgetPreview.cs`; `Forms/MainForm/MainForm.Settings.cs` | Dialog sammelt akzeptierte Werte; Widgetvorschau arbeitet auf Klonen und kann verworfen werden. Remote-Einstellungen als eigene Sektion mit explizit wirksamen Verwaltungsaktionen vorsehen. Kopplung/Widerruf nicht als rücknehmbare Widgetvorschau behandeln. |
| Weiter/Zurück | `Forms/MainForm/MainForm.Wallpaper.cs`: `AdvanceWallpaper`, `AdvanceWallpaperAsync`, `AdvanceCustomWallpaperAsync` | Synchroner Wrapper verwirft Task; asynchroner Pfad liefert bool, prüft Ownership, Besuchermodus, manuelle und Vollbildpause. Nativer Fallback wartet über `nativeSlideshowAdvance` auf Bildänderungen. Remote muss awaitbaren Pfad verwenden und Ergebnis genauer klassifizieren. |
| Timing | `Forms/MainForm/MainForm.Scheduler.cs`: `RecalculateCustomSlideshowSchedule`, `ArmCustomSlideshowPreciseTimer`, `CustomSlideshowPreciseTimerCallback`, `ProcessPreciseCustomSlideshowTick` | Anwendung besitzt laufende Diashowzeitplanung, ausgerichtete Termine und One-Shot-Timer. Callback dispatcht per `BeginInvoke`; keine zweite Remote-Uhr zur Mediensteuerung anlegen. |
| Pause | `Forms/MainForm/MainForm.Slideshow.cs`: `PauseSlideshow`, `ResumeSlideshowAsync`, `ToggleSlideshowPauseAsync`; `.Fullscreen.cs`: `UpdateFullscreenPauseAsync` | Manuelle Pause und automatische Vollbildpause sind unabhängig. Gemeinsamer Toggle routet zu Besucherordner oder Video. Remote soll gewünschten manuellen Zustand setzen, nicht blind toggeln. |
| Optionen | `Forms/MainForm/MainForm.SlideshowOptions.cs`: `ApplySlideshowOptions`, `PositionComboBox_SelectedIndexChanged`, `IntervalComboBox_SelectedIndexChanged`, `ShuffleCheckBox_CheckedChanged` | COM `IDesktopWallpaper`, Windows-Registry und eigener Renderer werden koordiniert. Normale Intervall-/Shuffle-Handler ignorieren Änderungen während Pause/Vollbild; Besucherhandler speichern separat. Ein bloß gesetzter Controlwert beweist daher keinen Erfolg. |
| Status | `Forms/MainForm/MainForm.Status.cs`: `CheckSlideshowStatus`; `.Wallpaper.cs`: `UpdateCurrentWallpaperDisplay`; `.VideoWallpaper.cs`: `UpdateVideoControls`; `.VisitorUi.cs`: `UpdateVisitorUi` | Mehrere Statusprojektionen; keine einheitliche öffentliche Remote-Zustandsquelle. Eine gemeinsame datensparsame Snapshotprojektion ergänzen und von allen Änderungsquellen aus aktualisieren. |
| Video | `Forms/MainForm/MainForm.VideoWallpaper.cs`: `ApplyWallpaperModeAsync`, `ApplyWallpaperModeCoreAsync`, `EnsureVideoWallpaperAsync`, `CaptureImageMode`, `RestoreImageMode`; `Wallpaper/Video/VideoWallpaperController.cs` | Controller besitzt Sitzung, Vorbereitung, Recovery, Changed-Event, Generationen und Lifecycle-Semaphor. Moduswechsel werden zusätzlich über `wallpaperModeChange` und `wallpaperModeRequest` gesichert. Video derzeit nur mit einem Monitor; Datei/Backend können trotz gespeicherter Konfiguration scheitern. |
| Medienbesitz | `Wallpaper/Video/WallpaperOperatingMode.cs`: `WallpaperModeOwnership` | Modi `ImageSlideshow`, `VideoWallpaper`, `Visitor`; Generation schützt vor veralteten Fortsetzungen auch bei schnellem Hin-/Herschalten. `Close()` entzieht Ownership. Diese Prüfung muss durchgängig erhalten bleiben. |
| Besucherdienst | `Services/VisitorModeService.cs`: `SetActiveAsync`, `ChangeAsync`, `ConfigureAsync`, `DrainAsync`, `Changed` | Serialisiert Änderungen, speichert aktiven Zustand und Resume-Snapshot; hält Active während Wiederherstellung wahr. Fehler können intern abgefangen werden. Taskabschluss allein ist keine Erfolgsbestätigung. |
| Besucherwiedergabe | `Forms/MainForm/MainForm.VisitorMode.cs`: `InitializeVisitorMode`, `ActivateVisitorMediaAsync`, `RestoreVisitorResumeAsync`; `.VisitorPlayback.cs`: `AdvanceVisitorManuallyAsync`, `ToggleVisitorPauseAsync`, `SaveVisitorPlaybackOptionsAsync` | Eigener Besucherordner, Shuffle, Intervall und manueller Pausenzustand; `VisitorSlideshowClock` nutzt Restzeit. Besucher-Einzelbild/-video haben im gemeinsamen Toggle keinen entsprechenden Pausepfad. Remote darf solche Fähigkeiten nicht pauschal behaupten. |
| Besucherabsicherung | `Services/VisitorWallpaperHandoff.cs`; `Forms/MainForm/MainForm.VisitorHandoff.cs`: `CheckpointVisitorVideoAsync`, `PublishVisitorExitWallpaperAsync`; `.VisitorMode.cs`: `StopVisitorAndCloseAsync` | Sichere native BMP-Kopie vor Hostfreigabe, begrenzte Slots, Video-Checkpoints; Shutdown wartet auf Vorbereitung/Änderungen. Kritisch für Absturz und Prozesskill, siehe unten. |
| Widgetkommunikation | `Widgets/WidgetManager.cs`, `.VisitorMode.cs`, `.WallpaperMode.cs`; `Forms/MainForm/MainForm.Initialization.cs` | Im selben Prozess: Weiter-Widget erhält Action auf `AdvanceWallpaper`; Besuchermoduswidget denselben `VisitorModeService`, Status über Changed. Keine Netzwerk- oder IPC-Brücke zwischen Widgets und Form erforderlich. |
| Lokales IPC | `Services/RemoteCommandServer.cs`; `Forms/MainForm/MainForm.Hotkeys.cs`: `ExecuteRemoteCommand`; `Program.cs` | Benutzergebundene Named Pipe mit `CurrentUserOnly`, begrenzter Befehlslänge/Timeout, `show`/`next`, Start bei Shown. Kein LAN-Server und keine Ergebnis-/Status-API; unverändert getrennt halten. |
| Speicherung | `Services/AppSettingsStore.cs`, `Services/SettingsPersistence.cs`, `Services/VisitorModeService.cs`: `VisitorModeStore`; `Services/WindowsSecretProtector.cs` | App-/Widget-/Besucheroptionen überwiegend HKCU; Besucherzustand als JSON plus separat gesetztem `VisitorModeActive`-Sicherheitsriegel. DPAPI-Helfer für aktuellen Windowsbenutzer vorhanden; unlesbare Geheimnisse dürfen später keinen anonymen Ersatz ermöglichen. |
| Beenden | `MainForm.OnFormClosing`, `Dispose`, `StopVideoAndCloseAsync`, `StopVisitorAndCloseAsync`; `Program.cs` | Schließen kann ins Tray führen; echter Exit teilweise asynchron bei laufender Message Loop. Normale Diashow wird Windows zurückgegeben, Besucherpfad erhält sicheren Hintergrund. Netzwerkshutdown darf diese Reihenfolge nicht blockieren oder normale Wiederherstellung auslösen. |

### Bestehende Werte und Semantik

Die normale Intervallliste in `MainForm.State.cs` enthält 1, 2, 3, 5, 10, 15, 30 Minuten, 1 und 6 Stunden sowie 1 Tag (Millisekunden: 60000 bis 86400000). Darstellungen: Fill/Ausfüllen, Fit/Einpassen, Stretch/Strecken, Tile/Kacheln, Center/Zentrieren, Span/monitorübergreifend. Span-Beschriftung ist monitorabhängig. API verwendet stabile Namen, keine ComboBox-Indizes oder lokalisierten Labels.

Besucherintervalle sind separat auf 1000–86400000 ms begrenzt; die Besucherseite bietet Sekundenwerte 1–86400. Remote-Capabilities müssen deshalb eine normale Auswahlliste und gegebenenfalls den Besucherbereich samt aktuellem Sonderwert unterscheiden. Keine künstliche Rundung bestehender Werte auf fünf Minuten.

Videozustände sind `Stopped`, `Initializing`, `Playing`, `Paused`, `Recovering`, `Failed`; Pausengründe sind Flags `Manual`, `Fullscreen`, `Shutdown`. Die Bilddiashow hat unter anderem `slideshowPaused`, `fullscreenPolicy`, Custom-/Native-Aktivität. Ein einzelnes `isPlaying` wäre unzureichend. Interne Pfade, VideoPosition, MonitorImages und Resume-Snapshots werden nicht serialisiert.

### Bestehende Sicherheitsgrenzen des Besuchermodus

`VisitorModeStore` setzt beim Aktivieren den Sicherheitsriegel vor dem JSON und löscht ihn erst nach gespeichertem Inaktivzustand. Beim Start wird Active aus JSON ODER Riegel gebildet. `InitializeVisitorMode` reserviert Ownership und sichere Abdeckung früh vor normaler Medieninitialisierung. Ein Besucherordner lässt gezielt Weiter/Zurück, Pause, Intervall, Shuffle und Darstellung zu; normale Medienverwaltung/Wiederherstellung bleibt gesperrt. UI-Disabled allein ist kein ausreichender API-Schutz.

Native Besucherbilder werden während des Betriebs als sichere Kopien hinterlegt; Video versucht nach erfolgreichem Start und anschließend im 15-Sekunden-Rhythmus einen sicheren Frame zu veröffentlichen. Reguläres Beenden versucht zusätzlich einen aktuellen Frame vor Stop/Hostfreigabe. Bei hartem Prozessende läuft kein Cleanup: erhalten bleibt der zuletzt erfolgreich nativ gesicherte Stand, nicht zwingend der letzte Videoframe. Vor erster erfolgreicher Übergabe sowie bei externen Themeänderungen, Stromausfall oder Aufnahmefehlern gelten bestehende Grenzen. Remote darf keine stärkere Garantie suggerieren. Details und bestehende Abnahmen: `docs/visitor-mode.md`.

## 3. Empfohlene Zielarchitektur

```text
Android MainPage/ViewModel
  -> HTTPS REST (Befehle, Snapshot, Kopplung)
  <- SignalR/WSS (vollständige versionierte Zustände)
Desktopprozess: RemoteHost -> Auth/Policy -> DesktopControlAdapter
                                          -> UI-Dispatcher
                                          -> bestehende MainForm-/Servicepfade
Desktop/Widgets/Automatik -----------------> dieselben Pfade -> StatePublisher
DNS-SD/NSD -> Adresskandidaten -> TLS-Pinprüfung -> authentifizierte Verbindung
```

Vorgeschlagene Typen: `RemoteHost`, `RemoteDeviceStore`, `PairingCoordinator`, `LocalNetworkPolicy`, `IDesktopControl`, `DesktopControlAdapter`, `RemoteStatePublisher`, `RemoteDiscoveryService`. Hosting bleibt im Desktopprozess; keine Windows-Service-/Serverprozessinstallation. Start erst nach Handle-Erstellung und abgeschlossener sicherheitsrelevanter Startinitialisierung, ausschließlich bei gespeicherter expliziter Aktivierung und geeignetem Netzwerk.

Der Adapter ist eine Befehls-/Ergebnisgrenze, keine Medienengine. Bestehende Methoden später minimal in awaitbare Operationen mit strukturiertem Resultat überführen; Desktophandler rufen diese ebenfalls auf. Keine simulierten Buttonklicks, kein direkter Controlzugriff aus Kestrel-Threads und kein direkter Controllerzugriff aus Endpoints. Modale Fehlerdialoge für Remote-Aufrufe durch Resultate ersetzen, Desktopdialoge weiterhin im UI-Adapter darstellen.

Ein UI-Dispatcher muss asynchrone Operationen vollständig awaiten, Ausnahmen weiterreichen und Shutdown/Handleverlust behandeln. Keine `.Wait()`/`.Result` auf dem UI-Thread. Eine begrenzte Befehlswarteschlange serialisiert Remote-Aufträge; bestehende Generationen/Semaphoren und erneute Policyprüfung nach awaits schützen zusätzlich vor Desktop-/Timer-Rennen. Bei Busy lieber klar ablehnen als unbegrenzt aufstauen. Autorisierung unmittelbar vor Seiteneffekt erneut prüfen.

StatePublisher erstellt immutable Snapshots auf dem UI-Thread; Netzwerkversand läuft davon entkoppelt. Langsame Clients bekommen den neuesten Gesamtzustand statt unbegrenzter Events. Bestehende Änderungen aus Desktop, Hotkeys, Tray, Widgets, Besucherdienst, Video-Changed, Timer, Vollbild, Monitorwechsel und Fehlerpfaden erfassen. Ein seltener Snapshotvergleich im bestehenden Statusrefresh kann als Sicherheitsnetz dienen; keine zweite Wiedergabezeitplanung.

### Technologieabwägung

| Option | Bewertung und Entscheidungsvorschlag |
| --- | --- |
| Kestrel | Gepflegtes HTTPS-/HTTP-/WebSocket-Hosting mit Limits und ASP.NET-Middleware. Kompatibel mit .NET-Desktophosting, aber neues `Microsoft.AspNetCore.App`-Framework und größere Self-contained-Ausgabe. Build-, Installer-, Single-File-/Trim-/Startzeitwirkung im Versuch messen. Empfehlung. |
| HttpListener / eigener TCP-Server | HttpListener bringt Windows-HTTP.sys-/Zertifikat-/Berechtigungsverwaltung; eigener TLS-/HTTP-Stack erhöht Prüfaufwand. Kein erkennbarer Vorteil für dieses Projekt. Nicht empfohlen. |
| REST | Kleine, explizite Befehlsfläche mit Statuscodes, korrelierbaren Ergebnissen und Versionierung. Gut unabhängig testbar. Empfehlung. |
| SignalR | .NET-Client, Lifecycle und Heartbeats reduzieren eigene Protokollarbeit. Clientpaket auf Android, zusätzliche Laufzeit-/Publishkosten; Reconnect und Resync müssen dennoch explizit implementiert werden. Empfehlung mit JSON, WSS/WebSockets ohne Long-Polling zunächst. |
| Roher WebSocket | Weniger Frameworkprotokoll, aber eigener Heartbeat, Backpressure, Reconnect und Nachrichtenvertrag. Sinnvolle Reserve bei nachgewiesenem SignalR-/Pinningproblem. |
| REST-Polling | Einfacher Diagnose-/Fallbackpfad für Status; höhere Verzögerung und Wakeups. Kein bevorzugter Livekanal; bei ausgefallenem Livekanal zunächst Steuerung deaktivieren und Verbindung reparieren. |
| mDNS/DNS-SD | Lokale automatische Wiedererkennung ohne Cloud, funktioniert nicht zuverlässig über VLAN/AP-Isolation/Multicastfilter. Android `NsdManager`; unter Windows bevorzugt native DNS-SD-API per kleinem Adapter, bei Problemen gepflegte Bibliothek nach Lizenz-/Wartungsprüfung. Kein Bonjour-Zusatzprozess. |
| UDP-Broadcast / Subnetzscan | Broadcast mit IPv6-/Firewallproblemen; Scan teuer und unnötig breit. Kein Standardfallback. Letzter Endpunkt, lokaler DNS-Name und manuelle Adresse genügen als Ergänzung. |

Ressourcenannahmen sind noch keine Messwerte. Geplanter Versuch: Release-Ausgabe mit/ohne Remote vergleichen (privater Speicher, Handles, Threads, CPU idle, Startzeit, Paketgröße); 0/1/5 Geräte, Statusburst und langsame Clients. Vorläufiges Ziel: deaktiviert kein Listener/Discoverytimer; aktiviert idle unter 1 % eines CPU-Kerns im 10-Minuten-Mittel; maximal 5 verbundene Geräte im ersten Abnahmeszenario, begrenzte Queues und stabiler Speicher über 8 Stunden. Speicher-/Paketbudget nach Baseline gemeinsam festlegen.

## 4. API- und Zustandsvertrag (Vorschlag v1)

Alle Produktendpoints ausschließlich HTTPS, keine HTTP-Redirect-/Fallbackstrecke. Authentifizierung für jeden Zustand und jeden Steuerungsaufruf; nur eng begrenzte Kopplungsendpoints akzeptieren das kurzlebige Kopplungsgeheimnis. Kein allgemeines unauthentifiziertes Info-/Medienendpoint. Geräteverwaltung und PC-Bestätigung nur lokal.

| Endpoint | Inhalt |
| --- | --- |
| `GET /api/v1/state` | Vollständiger, bereinigter Snapshot einschließlich Capabilities; keine Pfade/Medientitel. |
| `POST /api/v1/commands` | Typisierte Union mit `commandId`, `expectedEpoch`, `expectedRevision`, `type`, begrenztem Payload. |
| `GET /api/v1/commands/{commandId}` | Zeitlich begrenzter Ergebnisabruf für das authentifizierte aufrufende Gerät nach unklarer Antwort. |
| `/api/v1/events` | Authentifizierter SignalR-Hub, serverseitige `StateChanged`-Nachrichten; keine Steuerungs-Hubmethoden. |
| `POST /api/v1/pairing/requests` | QR-Geheimnis einlösen, temporäre Kopplungsanfrage erzeugen; noch keine Geräteberechtigung. |
| `POST /api/v1/pairing/claim` | Nach lokaler Bestätigung Zugangsdaten über temporären Claim-Nachweis beziehen; nur für diese Anfrage. |

Befehlstypen: `next`, `previous`, `setManualPause {paused}`, `setLayout {layout}`, `setInterval {milliseconds}`, `setShuffle {enabled}`, `setMode {mode: slideshow|video}`, `setVisitorMode {enabled}`. Keine beliebigen Strings, Pfade, Dateiauswahl oder Konfigurationsobjekte. `setMode` bezieht die hinterlegte Datei ausschließlich am PC; keine Videoengine-/Audio-/Widgetsteuerung im Umfang.

Einheitliches Resultat: `commandId`, `status: completed|rejected|failed|pending`, stabiler `errorCode`, `epoch`, `revision`, aktueller Snapshot bzw. Operation-ID. HTTP 200 für abgeschlossen, 202 ausschließlich für laufende Operation, 400 für ungültig, 401 für fehlende/entzogene Berechtigung, 403 für verbotenen Befehl, 409 für veralteten Zustand/Busy/Visitor-Schutz, 429 für Limit und 503 für Shutdown. Fehlertexte keine Exceptioninhalte. Pending ist ausdrücklich keine Erfolgsmeldung.

Idempotenz: pro Gerät/commandId denselben Payloadhash und das Ergebnis z. B. 10 Minuten begrenzt halten; gleiche ID mit anderem Inhalt ablehnen. Wiederholte Next-/Previous-Aufträge nicht blind ausführen. Bei Timeout ohne Ergebnis erst Zustand/Operation abgleichen. Nach PC-Neustart (`epoch` gewechselt) keine alten Navigationsaufträge wiederholen; Cache ist nicht dauerhaft genau-einmalig. Offline keine Befehle puffern. Set-Befehle ändern nur bei abweichendem gewünschtem Zustand; Revisionkonflikte verlangen neue Benutzerentscheidung oder aktuellen Abgleich.

Beispielhafter Snapshot (Schemaentwurf, kein Produktionszustand):

```json
{
  "apiVersion": 1,
  "serverId": "opaque-installation-id",
  "epoch": "random-per-process-start",
  "revision": 143,
  "operatingMode": "visitor",
  "mediaKind": "image",
  "playback": { "state": "paused", "manualPaused": false, "pauseReasons": ["fullscreen"], "busy": false },
  "visitor": { "active": true, "sourceKind": "folder", "busy": false },
  "slideshow": { "intervalMilliseconds": 300000, "shuffle": true, "layout": "fill" },
  "capabilities": { "allowedCommands": ["setVisitorMode"], "layouts": [], "intervalPresetsMilliseconds": [], "intervalRange": null, "canUseConfiguredVideo": false },
  "errorCode": null
}
```

Capabilities immer zur konkreten Ownership, Monitorzahl, Quelle, Pause, Busy und Bereitschaft berechnen; obiges reduziertes Beispiel erlaubt während Vollbild nur den separaten Besucherwechsel. Normalmodus, Besucherordner und Besuchereinzelquelle liefern unterschiedliche Fähigkeiten. Pausegründe umfassen mindestens manual/fullscreen/shutdown; Initialisierung/Recovery/Fehler bleiben eigene Zustände. Angeforderter und tatsächlich wirksamer Modus dürfen beim Vorbereiten nicht verwechselt werden.

`revision` steigt bei jeder semantischen Snapshotänderung; `epoch` wechselt bei Prozessstart. Client ignoriert alte Revisionen derselben Epoch. Nach Subscribe zuerst atomar aktuellen Snapshot liefern; ein paralleler REST-Snapshot wird nach Revision eingeordnet. Bei Lücke/Neuverbindung vollständigen Snapshot holen, danach Controls freigeben. Commandresultate und Events können in beliebiger Reihenfolge eintreffen; Korrelation über commandId und Revision. Transportannahme, bool-Rückgabe oder fehlerfreier Taskabschluss allein reichen bei intern abgefangenen Fehlern nicht: Adapter prüft Postcondition und Fehler-/Persistenzresultat.

Die Android-Verbindungszustände sind unabhängig: unpaired, discovering, connecting, synchronizing, connected, reconnecting, unauthorized, incompatible. Connected erst nach TLS/Auth/Livekanal/Snapshot. Heartbeat z. B. alle 5 Sekunden, Verlust spätestens nach 15 Sekunden ohne Lebenszeichen erkennen; bei OS-Netzverlust sofort sperren. Ein verlorenes Funkpaket kann technisch nicht instantan erkannt werden. Während ungeklärter Operation betroffene Controls als ausstehend sperren. Vollbild-Fortsetzen darf nur Manual löschen und muss weiter „durch Vollbild pausiert“ zeigen.

## 5. Kopplung und TLS-Vertrauen

1. Desktopbenutzer aktiviert Remote explizit. Server legt zufällige persistente Server-ID und individuellen TLS-Schlüssel mit selbst ausgestelltem Serverzertifikat an. QR-Erstellung nur im lokalen Desktopdialog.
2. „Neues Gerät koppeln“ erzeugt CSPRNG-Secret mit mindestens 256 Bit und eine zufällige sessionId. Server hält nur dessen Hash, Ablauf und Zustandsautomat im RAM. Ablauf maximal 5 Minuten, nach Neustart ungültig; monotone Frist verhindert Verlängerung durch Uhrkorrektur. Neuer QR ersetzt offenen QR.
3. QR enthält Formatversion, serverId, lokale Adresskandidaten/Port, SHA-256-SPKI-Pin, sessionId, Einmalgeheimnis und Ablaufhinweis. Keine dauerhaften Gerätezugangsdaten oder lokalen Dateipfade. Nicht als automatisch geöffneten Weblink behandeln; strikte Größe-/Schema-/Adressprüfung im Scanner.
4. Android bindet für genau diesen PC einen TLS-Validator an den übernommenen Pin. Vor jeglichem Secretversand muss der TLS-Handshake zum richtigen Schlüssel erfolgreich sein. Kein globales Accept-All, keine pinlose Wiederholung und keine Redirects zu anderen Hosts. Selbstsignierte Kette bzw. wechselnde IP werden durch eine eng begrenzte anwendungsspezifische Pin-Vertrauensregel ersetzt; Gültigkeitszeit, Server-Auth-EKU, Algorithmen und Schlüsselbesitz bleiben geprüft. Diese Ausnahme gilt nur für den gepinnten lokalen Server. Für REST sowie SignalR/WebSocket separat nachweisen, dass der Validator tatsächlich greift.
5. Pairingrequest sendet Secret und begrenzten frei gewählten Gerätenamen über TLS. Atomarer Übergang `issued -> reserved`: nur erste gültige Anfrage gewinnt; das Secret kann keinen zweiten Antrag erzeugen. Maximal ein offener Antrag; Ablehnung/Timeout verbraucht QR und erfordert neuen. Rate Limits vor UI-Anzeige, keine vom Netz ausgelösten unbegrenzten Dialoge.
6. PC zeigt Namen als unbestätigte Angabe und einen zufälligen Vergleichscode, den auch Android über die reservierte Verbindung erhält. Benutzer bestätigt am PC nur bei passendem Code. Ablehnung/ausbleibende Bestätigung bis zur ursprünglichen 5-Minuten-Frist vernichtet Antrag. Vor Bestätigung sind keine Steuerungsrechte vorhanden.
7. Beim Reservieren bekommt nur diese TLS-Sitzung einen starken temporären Claim-Nachweis; nach Bestätigung wird atomar ein Geräteeintrag mit deviceId und eigenem zufälligem 256-Bit-Token gespeichert. Claim liefert Token verschlüsselt nur an diesen Antrag. Bei verlorener Antwort darf derselbe Claim bis zur kurzen Frist dasselbe DPAPI-geschützte Ausgabepaket erneut abholen, ohne ein weiteres Gerät anzulegen; danach endgültig löschen. Kein Claim und keine Zugangsdaten im QR oder Log.
8. Android speichert Token und Pin sicher, bestätigt Empfang; bei Speicherfehler kein scheinbar erfolgreiches Pairing. Server kann unbeanspruchte Einträge auslaufen lassen. PC verwaltet mehrere Geräte mit Umbenennen und Entfernen. Android verbindet sich später ohne QR anhand serverId und Pin.

Zertifikats-/Schlüsselverlust oder Pinwechsel erfordert neue lokale Kopplung; keine automatische Vertrauensübernahme aus mDNS. Vor Ablauf Zertifikat mit gleichem Schlüssel erneuern; Schlüsselrotation nur über gesondert geprüften Übergang oder erneute Kopplung. Appdaten-/PC-Profilverlust wird als Vertrauensverlust behandelt. Ein kopierter QR kann bis zum Einlösen Anträge auslösen; Einmaligkeit, kurze Frist und lokale Sichtbestätigung begrenzen dies. QR-Abfotografieren ist keine Authentifizierung des anfragenden Menschen.

## 6. Sicherheitskonzept

### Netzwerk und Betrieb

Standardzustand aus: kein TCP-Listener, kein mDNS-Angebot, kein automatisches Pairing. Aktivierung bindet ausschließlich ausgewählte aktive private LAN-Schnittstellen, nicht pauschal `0.0.0.0`/`[::]`. Adress-/Profilwechsel werden überwacht; vor erneuter Bindung Freigabe prüfen. Public-, Tunnel-/VPN- und unerwünschte virtuelle Adapter standardmäßig ausschließen, auch bei RFC1918-Adressen. IPv4 und IPv6 explizit testen; private Adresse allein ist kein Vertrauensbeweis.

Firewall später ausschließlich app-/portgebunden, inbound TCP, Profil Private, RemoteAddress LocalSubnet und geeignete Schnittstellen; DNS-SD-UDP-Freigabe ebenso eng, soweit Windows dies benötigt. Keine Public-/Domain-Freigabe, kein UPnP/NAT-PMP, kein Routerzugriff. Anwendung prüft zusätzlich Quellnetz und Interfacepolicy, vertraut keinen Forwarded-Headern/Reverseproxys. Bei nicht eindeutig privatem Netz geschlossen bleiben. Spätere Firewalländerung kann erhöhte Rechte erfordern; Desktop bleibt sonst normaler Benutzer. Phase 0 ändert keine Regeln.

Kein LAN-Programm kann absichtliche externe Routertunnel oder einen kompromittierten autorisierten PC absolut verhindern. Hier bedeutet „ausschließlich lokal“: kein Internetendpoint, kein Relay, kein breites Binding, begrenzte Firewall und geprüfte lokale Peers. Netzwerksegmentierung/AP-Isolation kann legitimen Zugriff verhindern und wird klar angezeigt.

### Identität und Schutz gegen Missbrauch

Individuelle Bearer-Tokens über TLS, Server speichert für reguläre Prüfung nur SHA-256-Tokenhash und Geräte-ID/Status; kryptografisch zufällige hochentropische Tokens benötigen keinen Passwort-KDF. Konstante Vergleiche. Certificate-Private-Key/temporäre Credentialausgabe über DPAPI CurrentUser und eng begrenzte Benutzerdateirechte schützen; kein Klartext-PFX-Passwort im normalen Settings-JSON. Gerätestore atomar ersetzen, beschädigte/unlesbare Daten verweigern Zugriff. Android SecureStorage/Keystore, Secrets von Backups ausschließen; Restore ohne gültigen Schlüssel verlangt Neukopplung. DPAPI schützt nicht vor Schadcode im selben Benutzerkontext.

Widerruf am PC: zuerst persistent als entzogen speichern, aktive Sessions abbrechen, dann UI-Erfolg melden. Jede Anfrage und jeder noch wartende Befehl prüft aktuellen Gerätestatus erneut; keine langlebigen stateless JWT-Rechte ohne Widerrufsprüfung. Bereits vor Widerruf abgeschlossene Seiteneffekte lassen sich nicht rückgängig machen. Umbenennen ändert Token nicht. Remote-Deaktivierung schließt Sessions, stoppt Discovery und verwirft Pairing; gespeicherte Geräte bleiben für spätere bewusste Reaktivierung erhalten, bis lokal entfernt.

Nur typisierte Payloads, z. B. 8 KiB Requestlimit, begrenzte Namen/Strings, Timeouts, maximal begrenzte Connections/Pendingrequests und Rate Limits pro Gerät sowie global für anonyme Pairingversuche. Keine Browserclients/CORS-Freigaben, keine Cookieauth, Origin bei WebSocket nach festgelegter Policy prüfen; native Clients können Origin weglassen, Auth bleibt zwingend. Tokens ausschließlich in Authheadern, nicht URLs; falls eine Bibliothek Querytokens erfordert, Transport anpassen statt Zugangsdaten protokollierbar zu machen. Logs enthalten Fehlercodes und Korrelations-IDs, weder QR/Token noch Paths, Dateinamen, Besuchersnapshots oder Requestbodies. Debug-/Swagger-/Diagnoseendpoints nicht im Produkt öffnen.

### Besuchermodus als serverseitige Policy

| Zustand | Erlaubter Remoteumfang |
| --- | --- |
| Normal, Bilder bereit | Bestehende Bildbefehle entsprechend Pause/Bereitschaft; Wechsel zu hinterlegtem kompatiblem Video; expliziter Besucherwechsel. |
| Normal, Video | Manuelle Pause/Fortsetzen, zurück zu Diashow; keine wirkungslosen Bildoptionen. |
| Besucherordner bereit | Ausschließlich besuchereigene Weiter/Zurück, manuelle Pause, Intervall, Shuffle und vorhandene gemeinsame Darstellung entsprechend bestehender Guards. Kein Wechsel zu normalem Video/normaler Diashow. |
| Besucher-Einzelbild/-video | Nur Besuchermoduswechsel und Status; derzeit keine gemeinsame Einzelvideo-Pause implementiert. Keine neuen Wiedergabefunktionen unterstellen. |
| Besucherbusy / Shutdown | Keine normalen Wiedergabebefehle; beim Shutdown überhaupt keine neuen Mutationen. |

`setVisitorMode(false)` ist ein eigener expliziter Schutz aufhebender Befehl, niemals impliziter Teil von Next/Resume/Moduswechsel. Empfehlung: bei Kopplung gesondert am PC die Berechtigung zum Deaktivieren erklären und freigeben; alternativ PC-Bestätigung pro Deaktivierung. Diese Produktentscheidung bleibt offen. UI zeigt vor Deaktivierung deutlich, dass normale Hintergründe wieder sichtbar werden. Berechtigung und aktueller Besuchermodus werden direkt vor Ausführung und nach relevanten awaits geprüft. Normale API kann weder Quellen umstellen noch Resume-Snapshot verändern.

Beim Exit zuerst Remote-Mutationen sperren/Queues entziehen, dann vorhandenen Besucherdrain, sichere native Übergabe und Video-/Hostabbau unverändert ausführen. Netzwerkstop nur zeitlich begrenzt asynchron; kein Warten auf Android als Bedingung der sicheren Übergabe. Tray-Verbergen ist kein Prozessende und beendet Remote nicht. Absturz-/Killabsicherung darf niemals von einem Disconnecthandler abhängen.

## 7. Erkennung und automatische Wiederverbindung

DNS-SD-Servicevorschlag `_wallpaperctrl._tcp.local.` mit API-Hauptversion, zufälliger serverId und TLS-Kennzeichnung in TXT. Anzeigename optional neutral; keine Benutzer-/Medieninformationen, Tokens, QR-Geheimnisse oder Pins als Vertrauensanker verbreiten. Werbung nur bei aktivierter Remote-Funktion auf erlaubten Interfaces.

Android speichert serverId, Pin und letzten erfolgreichen Endpunkt. Beim Start/App-Foreground/WLANwechsel: letzten Endpunkt kurz prüfen, parallel bzw. anschließend NSD suchen, Treffer über serverId filtern, auf aktuellem Netz auflösen und jeden Kandidaten mit TLS-Pin authentifizieren. Kein Senden von Credentials an bloß gleichnamigen Dienst. Wiederholen mit begrenztem exponentiellem Backoff und Jitter (z. B. 1, 2, 4, 8, 15, maximal 30 Sekunden), sofortiger neuer Versuch bei Netzänderung. SignalR-Reconnect allein findet keine geänderte IP und deckt initiale Verbindungsfehler nicht vollständig ab.

Fallback: gespeicherter lokaler DNS-Name oder manuelle lokale IP/Port mit bestehendem Pin; bei Bedarf Desktop zeigt aktuellen Verbindungs-QR ohne langlebige Secrets als Adresshilfe. Keine neue Kopplung allein wegen DHCP-Wechsel. Bei mDNS-Blockade kann vollautomatische Wiedererkennung ohne lokale DNS-/Netzunterstützung nicht garantiert werden. IPv6-Link-Local-Scope korrekt behandeln; keine ungebundenen oder externen QR-Adressen akzeptieren.

Im Android-Hintergrund keine dauerhafte Netzwerk-/Foreground-Servicepflicht: Liveverbindung darf ruhen, bei Rückkehr grundsätzlich Resync. PC-Neustart/Remote-Aus meldet offline; App startet den Desktop nicht und weckt den PC nicht. Widerruf führt zu unauthorized und entfernt keine Rechte am PC durch automatische Neukopplung.

## 8. Android-Technologie, Projektstruktur und UI

Empfehlung .NET MAUI mit XAML/MVVM und Android als einzigem anfänglichem Target: passende kompakte Kartenoberfläche, C#-Verträge, SecureStorage und native Androidanbindung. Nach aktueller Supporttabelle ist MAUI 11 unterstützt, MAUI 10 endet am 11.05.2027; für ein neues Projekt MAUI 11 bevorzugen, Toolchain/Scannerkompatibilität zuvor prüfen. Desktop muss dafür nicht auf .NET 11 migriert werden. Gemeinsame DTO-Bibliothek z. B. `net10.0` und versioniertes JSON-Protokoll halten beide Seiten unabhängig. Exakte SDK-/Paketversionen erst im technischen Versuch festlegen. [MAUI-Supportpolicy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui).

Alternative .NET for Android mit nativen Android-Views: direkter Zugang zu NSD/Widget/Camera, weniger MAUI-Abstraktion, dafür mehr Android-spezifischer UI-Code. Sinnvoll, falls ausschließlich Android langfristig gewünscht und MAUI-Handler/Transportadapter unverhältnismäßig werden. Avalonia als zusätzliche UI-/Android-Abhängigkeit für diese kleine Fernbedienung derzeit ohne belegten Vorteil. Kotlin wäre technisch naheliegend, erfüllt aber keine C#-Präferenz. Auswahl ist eine Wartungsentscheidung, keine Funktionsvoraussetzung.

Vorgeschlagene spätere Struktur:

```text
WallpaperControl.Remote.Contracts/      DTOs, Fehlercodes, Versionen; keine WinForms-Typen
WallpaperControl/Remote/                Host, Auth, Pairing, Discovery, StatePublisher, Adapter
WallpaperControl.Remote.Android/       MAUI Android-App
  Views/                               MainPage, PairingPage, ConnectionSettingsPage
  ViewModels/                          RemoteViewModel, PairingViewModel
  Services/                            RemoteClient, ConnectionCoordinator, CredentialStore
  Platforms/Android/                   NSD, TLS-Handler, QR-Camera, Lifecycle
WallpaperControl.Remote.Tests/         Protokoll, Auth, Pairing, Reconnect, Clientzustand
```

Keine solche Projektanlage in Phase 0. Scanner bevorzugt on-device mit geprüftem gepflegtem MAUI-/Android-Adapter (z. B. ZXing-basierte Lösung nach Kompatibilitäts-/Lizenzprüfung), kein QR-Bild an externe Dienste. `INTERNET`, Netzwerkstatus bei Bedarf und CAMERA nur bei geöffnetem Scanner; keine Medien-, Speicher-, Standort-, Mikrofon-, Bluetooth- oder Kontaktrechte. Lokale Netzwerkberechtigungen sind Android-/Target-SDK-abhängig und vor Release erneut zu prüfen; keine feste Zusage, dass INTERNET auf allen künftigen Versionen genügt. Native NSD ist gegenüber eigener Multicastimplementierung bevorzugt. [Android-LAN-Berechtigungen](https://developer.android.com/privacy-and-security/local-network-permission), [NsdManager](https://developer.android.com/reference/android/net/nsd/NsdManager).

### Gestaltungsreferenz des Benutzers

Das am 25.11.2026 nachgereichte Appbild ist die visuelle Referenz: sehr dunkler Hintergrund, abgerundete anthrazitfarbene Karten, blauer Akzent, Kopfzeile mit Monitoricon/Titel und Einstellungen, grüne Verbindungskarte, direkt darunter Besuchermoduskarte mit Schild/Schalter, Wiedergabemodus als Segmentauswahl, große zentrale Pause-/Playtaste mit Zurück/Weiter, anschließend Intervall/Shuffle/Darstellung. Kompakte einseitige Bedienung mit Dark Mode, kein Medienpreview nötig. Die Bilddatei liegt nur als lokale Chatreferenz vor und wird nicht über ihren temporären absoluten Pfad ins Repository eingebunden.

„Bild 42 von 915“, WLAN-Name und Versionsnummer im Bild sind Beispieldaten. Empfehlung: Bildzählung zunächst weglassen oder ausschließlich als optionale anonymisierte Zählwerte aus dem PC abnehmen; kein Dateiname, Thumbnail oder Ordnername. Keine zusätzliche Standort-/SSID-Berechtigung nur für „Heimnetzwerk · WLAN“ verlangen: „Lokales Netzwerk“ bzw. ein lokal vergebener PC-Alias genügt. Verbindung wird nie allein anhand WLAN-Zugehörigkeit grün.

Besuchermodus oben immer sichtbar; On/Off, Busy und fehlende Quelle klar unterscheiden. Deaktivieren mit verständlicher Warnung/Bestätigung, eingeschränkte Berechtigung kenntlich machen. Bei aktivem Besucherordner Wiedergabekarte ausdrücklich als Besucherwiedergabe kennzeichnen; normale Modusauswahl sperren. Offline sämtliche Mutationen einschließlich Besucherschalter deaktivieren, letzten Zustand als veraltet markieren. Erfolgreiche Änderungen erst nach abgeschlossenem PC-Ergebnis; ausstehende Werte nicht als bestätigt darstellen.

Zielabnahme Pixel 9 Pro XL mit tatsächlich installiertem Androidstand: Safe Areas/Gestenleiste, mindestens 48-dp-Touchflächen, TalkBack-Beschriftungen, Kontrast und Schriftvergrößerung, Scrollen bei großer Schrift statt abgeschnittener Karten; keine fest verdrahteten Screenshotpixel. Dark Mode als primäre Gestaltung, Systemmodus/helles Theme als spätere Produktentscheidung.

### Optionales Homescreen-Widget

Machbar über nativen `AppWidgetProvider`, XML/RemoteViews und PendingIntent; MAUI-Seite selbst ist kein Homescreen-Widget. Nur Besuchermodus, kein allgemeiner Player. Hintergrundarbeit und Prozessneustart erfordern kurzlebige Netzwerkaufträge, sicheren Credentialzugriff und dieselbe Pin/Auth/Policy. Kein dauerhaftes SignalR im Widget, keine garantierte ständig aktuelle Anzeige bei Doze. Standardanzeige „Status prüfen“/Zeitstempel; explizites Set statt blindem Toggle. Sicherster erster Entwurf öffnet die App zur aktuellen Statusprüfung und Deaktivierungsbestätigung. Echte Hintergrundschaltung braucht eigene Freigabe und Abnahmematrix, insbesondere gegen versehentliche Offenlegung privater Hintergründe. [Android App Widgets](https://developer.android.com/develop/ui/views/appwidgets/overview).

## 9. Risiken, Seiteneffekte und offene Entscheidungen

| Risiko | Gegenmaßnahme / Freigabebedingung |
| --- | --- |
| MainForm-zentrierte Logik, async void und intern abgefangene Fehler | Kleine gemeinsame awaitbare Operationen, Postconditions, kein „Button gedrückt = erfolgreich“. Desktopregression vor Netzwerkfreigabe. |
| Asynchrone Rennen zwischen Besucherwechsel, Next, Video, Revocation und Exit | Bestehende Ownershipgenerationen erhalten, Queue begrenzen, erneute Autorisierung/Policy und klare Busyantworten. |
| Private Bilder bei Besucherübergang/Shutdown | Vorhandene sichere native Übergabe unverändert, Prozesskill-/Neustartprüfung mit synthetischen Medien. |
| Pinning funktioniert nur auf REST, nicht WSS | MITM-/Fremdzertifikattests auf tatsächlichem Androidgerät für beide Transportpfade als Stopkriterium. |
| Größere Auslieferung/neue Sicherheitsupdates durch ASP.NET | Publish-/Installerprüfung und Laufzeitwartung; bei Self-contained Builds neue Runtimepatches durch erneute Auslieferung einbeziehen. |
| DHCP, Multicastfilter, IPv6, mehrere NICs | NSD plus letzter Endpunkt/manuelle lokale Adresse; kein automatisches Vertrauensreset. |
| Gerät kompromittiert oder QR abgefangen | Per-device Widerruf, explizite PC-Bestätigung, minimale API; LAN/TLS allein schützt nicht vor autorisiertem bösartigem Client. |
| Androidhintergrund/Backup/Berechtigungen | Foreground-Resync, Backupausnahme, Permissionmatrix und Gerätetest. |

Notwendige Benutzerentscheidungen vor den jeweils betroffenen Implementierungsphasen:

1. Architekturvorschlag freigeben: Kestrel + REST + SignalR, MAUI Android und native DNS-SD zuerst technisch erproben?
2. Darf jedes gekoppelte Gerät den Besuchermodus deaktivieren, oder braucht es ein separat am PC vergebenes Recht bzw. jedes Mal PC-Bestätigung? Empfehlung: ausdrücklich vergebenes Recht, Androidbestätigung, standardmäßig keine implizite Freigabe.
3. Welche privaten Adapter sollen standardmäßig angeboten werden? Empfehlung: bewusste Auswahl des privaten Heimnetzadapters, keine VPNs und keine Public-Netze. Portvorschlag 47831/TCP, bei Konflikt klarer Fehler und lokale Änderung statt breitem Portscan.
4. Minimale Androidversion, gewünschte APK-Verteilung/Signierung und Updateweg festlegen; Pixel 9 Pro XL allein definiert keine allgemeine Mindestversion. Empfehlung: zunächst signierte lokale Abnahme-APK, kein Store-/Cloudzwang.
5. Reicht reiner Status ohne Bildzähler? Empfehlung: ja; Zähler nur nach Prüfung korrekter Semantik bei Shuffle/History/Visitor. Themes und Mehr-PC-Auswahl sind keine notwendigen Erweiterungen der ersten Abnahme.

Keine dieser Fragen rechtfertigt in Phase 0 produktive Änderungen. Technische Detailwerte und Bibliotheksauswahl sind Vorschläge und können im freigegebenen Versuch ohne neue Produktfeatures präzisiert werden.

## 10. Test- und Abnahmekonzept

Phase 0 prüft ausschließlich Dokumentation und Quellcodebezüge. Es wurden keine Produktbuilds, nativen Wiedergabetests oder laufenden Einstellungen gestartet/verändert; historische Testergebnisse aus vorhandenen Dokumenten sind kein neuer Nachweis für Remote. Für spätere Phasen gelten:

- Unit-/Protokolltests mit isolierter Clock/Stores: QR bei 299/300 Sekunden, Uhrkorrektur, Neustart, Replay, doppelte parallele Einlösung, Reject/Cancel, verlorener Claim, Größen-/Rate-Limits, falsche Tokens, defekte Stores und Permissionentzug in Warteschlange.
- Adaptertests mit bestehenden MainForm-Testeinstiegen und Fake-Desktop: identische Wirkung von UI/Widget/Remote, tatsächliches Resultat nach abgefangenem Fehler, manuelle/vollbildbedingte Pause getrennt, unveränderte Besucher-Resume-Snapshots und Pausenrestzeit, Video-Einmonitorgrenze, fehlendes/defektes Video, rasche Modiwechsel.
- Transporttests: HTTP geschlossen, fremder/abgelaufener TLS-Schlüssel und MITM für REST/WSS abgewiesen, gleichnamiger mDNS-Dienst mit falschem Pin, keine Tokenweitergabe bei Redirect, Revocation trennt bestehende Verbindung und verhindert queued Befehle. Logs/Payloads auf Pfade und Secrets prüfen.
- Synchronisation mit Desktop und zwei Androidgeräten: alle Bedienwege und Automatikticks sichtbar; Event/Result vertauscht, verlorene Events, Epochwechsel, Reconnect während Pending, keine doppelte Navigation. Nach Verbindungsausfall Controls spätestens nach Heartbeattimeout gesperrt und bis Vollsnapshot gesperrt.
- LAN-Matrix: private/public Profilwechsel während Verbindung, falscher Adapter, DHCP-IP-Wechsel, IPv4/IPv6, PC/Androidneustart, Sleep/Wake, WLANverlust, AP-Isolation, blockiertes mDNS, Portkonflikt und fehlende Firewallberechtigung. Keine Freigabe für öffentliches Netz.
- Besuchersicherheit: Einzelbild, Einzelvideo und gemischter Ordner; während Vorbereitung/Transition Prozess beenden bzw. Testkindprozess hart killen; persistierter aktiver Zustand/Sicherheitsriegel/sicherer BMP und Wiederanlauf; keine normale Sammlung vor expliziter Deaktivierung. Reale Windows-Wallpaper-/Neustarttests erst als bewusst vorbereitete manuelle Abnahme mit nicht vertraulichen Testmedien.
- Bestehende Regressionseinstiege aus `WallpaperControl.RegressionTests/README.md`: insbesondere `--visitor-mode-checks`, `--visitor-ui-checks`, `--visitor-regression-checks`, `--visitor-playback-checks`, `--visitor-flash-checks`, Handoff- und Videoprüfungen laut `docs/visitor-mode.md`/Testdokumentation; kompletter Regressionslauf vor Release. Keine historischen Zählwerte als aktuelle Ergebnisse übernehmen.
- Pixel-Abnahme: QR-Kamerarecht nur beim Scannen, abgelehntes Recht behandelbar, Pairingbestätigung am PC, zwei Geräte, Rename/Remove, Dark Mode nach Referenz, TalkBack/große Schrift, Hintergrund/Rückkehr, sichere Speicherung/Backup-Restore, alle Controls kontextabhängig. APK-Signatur und Releaseversion dokumentieren.

Vorläufige Performanceabnahme im normalen LAN: sichtbarer bestätigter Status bei kurzen Befehlen üblicherweise innerhalb einer Sekunde; längere Medienvorbereitung zeigt Pending und wird separat gemessen. Keine künstliche Erfolgsgarantie für Decoderzeiten. Belastung mit fünf Clients, Rate-Limit-Flood und achtstündiger Laufzeit; Idle-/Paketbudget aus Abschnitt 3 prüfen.

## 11. Konkrete Entwicklungsphasen und Freigabetore

| Phase | Arbeit | Abnahmekriterium |
| --- | --- | --- |
| 00 – dieser Bericht | Bestandsanalyse, Protokoll-/Sicherheitsvorschlag, UI-Referenz und offene Entscheidungen | Nur Bericht geändert; manuelle Architekturfreigabe. Danach anhalten. |
| 01 – begrenzter Technikversuch | Integriertes HTTPS-Hosting/Publish, Android REST+WSS-Pinning, Windows DNS-SD/Android NSD, QR-Scannerkompatibilität, Ressourcenbaseline | Kein separater Server nötig; falsche Pins werden in beiden Pfaden abgewiesen; DHCP-Wechsel erfolgreich; Abhängigkeiten/Lizenzen und Budgets dokumentiert. |
| 02 – gemeinsame Desktopsteuerung | Awaitbare Adapteroperationen, typisierte Resultate, einheitlicher Snapshot/Capabilities, Desktop-/Widget-/Automatikänderungen erfassen | Parität aller bestehenden Steuerwege; kein neuer Scheduler; Besucher-/Video-/Shutdownregression bestanden. |
| 03 – Sicherheit und Kopplung | Remote-Einstellungen, privates Interface/Firewallkonzept umsetzen, QR-Zustandsautomat, DPAPI/DeviceStore, lokale Bestätigung, Rename/Revocation | 5-Minuten-/Einmaligkeits-/Replay-/Widerrufstests bestanden; standardmäßig geschlossen; Public-Netz nicht erreichbar. |
| 04 – API und Synchronisation | REST, SignalR, Revision/Epoch, Limits, Idempotenz, Discover/Reconnect und Shutdownintegration | Zwei Geräte + Desktop/Widget/Automatik konsistent; keine doppelte Navigation; kein Zugriff nach Widerruf; keine Pfade/Secrets. |
| 05 – Androidprodukt | MAUI-Hauptseite nach Referenz, Pairing, SecureStorage, Status/Controls, Lifecycle/Berechtigungen | Pixel 9 Pro XL manuell abgenommen, Offline/Pending/Visitor korrekt, automatische Wiederverbindung und IP-Neuerkennung. |
| 06 – Stabilisierung/Auslieferung | Gesamtsuite, Sicherheits-/Last-/8h-Tests, Installer-/Runtimepatchprozess, Bedien-/Fehlerhilfe | Dokumentierte Testnachweise, signierte Abnahme-APK und Desktopausgabe, keine offenen sicherheitskritischen Befunde; gesonderte Releasefreigabe. |
| Optional später | Homescreen-Widget nur Besuchermodus | Eigene Produktfreigabe, Background-/Doze-/Stale-State-/Deaktivierungsschutzabnahme. |

## 12. Externe Primärquellen und Prüfgrenzen

Technologierecherche am 25.11.2026; folgende Quellen stützen Plattformfähigkeiten, nicht die noch unimplementierte Integration:

- [Kestrel-Übersicht](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0) und [HTTPS-Endpunkte](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0): integrierbares Hosting und explizite HTTPS-Konfiguration. Entwicklungszertifikat nicht als Produktionsvertrauen verwenden.
- [SignalR-.NET-Client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0): Clientpaket und explizite Reconnectkonfiguration; ein eigener initialer Retry-/Discovery-/Resyncablauf bleibt erforderlich.
- [MAUI SecureStorage](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0): sichere Speicherung und Android-Backupbesonderheiten.
- [MAUI-Supportpolicy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui): getrennten MAUI-Wartungszyklus beachten, auch bei Desktop-LTS.
- [Android NSD](https://developer.android.com/reference/android/net/nsd/NsdManager), [lokale Netzwerkberechtigungen](https://developer.android.com/privacy-and-security/local-network-permission), [App Widgets](https://developer.android.com/develop/ui/views/appwidgets/overview): lokale Discovery, versionsabhängige Berechtigungen und native Widgetarchitektur.

Die Integration, Pinninghandler, konkrete DNS-SD-Implementierung und Bibliothekskompatibilität wurden nur konzipiert, nicht praktisch bewiesen. Es wurden keine NuGet-Pakete installiert, Androidprojekte angelegt, Firewallregeln, produktiven Einstellungen oder Nutzerdaten verändert. Kein Merge, kein Push. Nächster Schritt ist ausschließlich die manuelle Freigabe dieses Berichts; Implementierung beginnt nicht automatisch.
