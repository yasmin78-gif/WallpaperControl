# WallpaperControl Remote – Einrichtung und Abnahme

Die produktive Desktop-Integration wurde am 25.11.2026 ausdrücklich freigegeben. Sie verwendet die vorhandene Wallpapersteuerung. Die Experiment-App und der frühere Testhost werden nicht mehr benötigt; ihre Kopplungsdaten werden bewusst nicht übernommen.

## Bereitgestellte Ausgaben

- Desktop: `artifacts/remote-production/desktop/WallpaperControl.exe`, Version **2.6.0**, Windows x64, eigenständige .NET-10-Ausgabe.
- Android: `artifacts/remote-production/WallpaperControl.Remote.apk`, **WallpaperControl Remote 1.0.0**, Android 12 oder neuer, ARM64 (passend zum Pixel 9 Pro XL).
- Prüfsummen: `artifacts/remote-production/manifest.json`.

Die gesamte Desktopausgabe zusammenlassen, insbesondere `video-runtime` und `licenses`. Die bestehende WallpaperControl-Instanz vor dem Austausch regulär über **Beenden** schließen. Die neue Ausgabe ersetzt keine laufende Installation automatisch. Die produktive Android-App hat eine eigene Application-ID `de.wallpapercontrol.remote` und einen eigenen Release-Signierschlüssel. Sie wird separat neben „WC Remote Experiment“ installiert; andere Apps müssen nicht deinstalliert werden. Spätere Updates derselben produktiven App funktionieren mit identischer Application-ID und identischem Schlüssel ohne Deinstallation.

## PC und Handy koppeln

1. WallpaperControl 2.6.0 starten. Unter **Einstellungen → Allgemein → WallpaperControl Remote einrichten** die Fernsteuerung öffnen.
2. **Fernsteuerung im privaten Heimnetz aktivieren** auswählen. Bewusst den privaten physischen Ethernet-/WLANadapter wählen. VPN-, Tunnel-, virtuelle und öffentliche Adapter stehen nicht zur Auswahl. Standardport ist **47831/TCP**.
3. **Übernehmen**. Der Status muss ein aktives HTTPS-Endpunkt anzeigen. Bei fehlendem privaten Profil oder unsicherer Ablage bleibt der Zugriff geschlossen. Die Windows-Netzwerkkategorie wird nicht automatisch verändert.
4. Falls nötig **Enge Windows-Firewallfreigabe einrichten** wählen und die Administratorabfrage bestätigen. Dies legt eine Regel nur für die veröffentlichte EXE, TCP-Port, ausgewählten Adapter, lokale Subnetze und das Profil **Privat** an. Lokale IP-Änderungen werden durch die Adapterbindung der Regel unterstützt. Keine Routerfreigaben und keine VPN-Änderungen.
5. **Neues Gerät koppeln**. In der Android-App auf die Verbindungskarte bzw. **Einstellungen → PC über QR-Code koppeln** tippen und den QR scannen. Kamerazugriff wird nur hierfür benötigt. Auf Android 17+ den lokalen Netzwerkzugriff erlauben.
6. Die sechsstelligen Vergleichscodes auf PC und Handy vergleichen. Nur bei Übereinstimmung am PC bestätigen. Das Recht **Besuchermodus deaktivieren** ist standardmäßig nicht gesetzt und muss bewusst für dieses Gerät vergeben werden.
7. Das Handy speichert seine individuellen Zugangsdaten sicher und verbindet sich automatisch. Der QR ist einmalig und höchstens fünf Minuten gültig; er enthält keine dauerhaften Zugangsdaten.

Die Fernsteuerung bleibt standardmäßig aus. Aktivierung und Adapterwahl bleiben nach Übernehmen gespeichert. Schließen des Remote-Fensters verwirft bereits ausgeführte Verwaltungsaktionen nicht. Ein bloßes Verbergen des Hauptfensters im Tray beendet die Fernsteuerung nicht.

## Bedienung

Die Hauptseite zeigt Verbindung, Besuchermodus, Wiedergabe und die verfügbaren Diashoweinstellungen. Weiter/Zurück, Pause/Fortsetzen, Intervall, Zufallsreihenfolge, Darstellung und Wechsel zu einem **bereits am PC hinterlegten** Video verwenden dieselben internen Steuerungspfade wie die Desktopbedienung.

- Bei Verbindungsverlust sind Eingaben gesperrt. Die App verbindet sich wieder und fragt den vollständigen Zustand neu ab. Alte Offlinebefehle werden nicht später abgespielt.
- Eine laufende Aktion wird als ausstehend angezeigt; Erfolg wird erst nach PC-Bestätigung angenommen. Bei unklarem Ergebnis wird ein Navigationsbefehl nicht blind wiederholt.
- Eine manuelle Fortsetzung hebt eine automatische Vollbildpause nicht auf.
- Im Besucherordner werden nur dessen vorhandene Wiedergabefunktionen gesteuert. Normale Quellen und normale Wiedergabemodi bleiben gesperrt.
- Bei Besucher-Einzelbild und Besucher-Einzelvideo bleiben die im Desktop nicht vorhandenen allgemeinen Wiedergabefunktionen deaktiviert.
- Besuchermodus aktivieren darf jedes gekoppelte Gerät. Deaktivieren benötigt das gerätespezifische PC-Recht und eine verständliche Bestätigung am Handy; eine erneute PC-Bestätigung ist für bereits berechtigte Geräte nicht erforderlich.
- Ein Countdown wird ausschließlich aus der bestehenden Desktopzeitplanung abgeleitet und nur angezeigt, wenn eine verlässliche Restzeit vorhanden ist. Er steuert keinen Bildwechsel. Für native Windows-Diashow, Einzelmedien und Video ohne vorhandene Deadline entfällt die Anzeige.

Es gibt keine Bild-/Videovorschau, Bildzähler, Dateinamen, Medienpfade, Ordner-/Playlistverwaltung oder Widgetkonfiguration in der App. Video bleibt an die bisherigen Desktopvoraussetzungen gebunden, insbesondere die bestehende Einmonitorgrenze.

## Geräte verwalten

Im Remote-Fenster am PC ein Gerät auswählen, umbenennen oder das Deaktivierungsrecht ändern. Änderungen werden sicher gespeichert und bestehende Sitzungen neu authentifiziert. **Gerät entfernen** entzieht den Zugriff unmittelbar und trennt dessen Liveverbindung. Das entfernte Handy benötigt eine neue ausdrücklich bestätigte Kopplung.

Am Handy entfernt **Einstellungen → Gespeicherte Verbindung entfernen** die lokal gespeicherten Zugangsdaten. Den zugehörigen Geräteeintrag bei Bedarf zusätzlich am PC löschen.

Produktive Remote-Konfiguration und Serveridentität liegen unter `%LOCALAPPDATA%\WallpaperControl\Remote`, Windows-DPAPI-geschützt. Android verwendet SecureStorage/Keystore; Appbackup ist deaktiviert. Ein verlorener oder nicht mehr lesbarer Schlüssel wird nicht durch eine unsichere Standardidentität ersetzt. Zertifikate werden mit demselben öffentlichen Schlüssel erneuert, sodass vorhandene Pins erhalten bleiben.

## Verbindung und automatische Erkennung

Der PC wird über DNS-SD/mDNS anhand seiner stabilen Server-ID gesucht. Diese Suche liefert nur Adressen; sie ersetzt nie den gespeicherten TLS-Pin. Die zuletzt erfolgreich authentifizierte Adresse wird zuerst versucht. Bei IP-/Profiländerung wird der bisherige Listener geschlossen und ausschließlich der bereits freigegebene private Adapter neu geprüft und gebunden.

Bei blockiertem Multicast unter **Einstellungen** die aktuelle lokale HTTPS-Adresse des PCs eintragen. Der gespeicherte Pin bleibt dabei unverändert. IPv4 sowie lokale IPv6-Adressen (ULA/Link-Local) werden unterstützt; Link-Local-Scope wird auf das physische Handyinterface umgesetzt. Globale IPv6-/Internetadressen sind nicht freigegeben.

Proton VPN wird nicht umkonfiguriert. Eine VPN-eigene LAN-/Kill-Switchsperre kann auch diese Anwendung blockieren; die Freigabe in einer anderen Sicherheitskomponente wird nicht eigenmächtig umgangen. Reale VPN-/DHCP-/Multicastkonfigurationen müssen in der jeweiligen Umgebung abgenommen werden.

## Noch erforderliche manuelle Produktabnahme

Die Vorab-App hat Kopplung, Bedienung, App-/Host-Neustart, WLAN-Wiederverbindung und REST-/WSS-Pinning bereits im Benutzertest bestätigt. Die **neue produktive, signierte App und die echte Desktopintegration** benötigen eine eigene Abnahme:

1. Neu mit der produktiven App koppeln, beide Codeanzeigen vergleichen, Deaktivierungsrecht zunächst aus lassen.
2. Mit nicht vertraulichen Testmedien die verfügbaren Aktionen am Handy ausführen und am PC prüfen; anschließend Desktop-/Widgetänderungen am Handy verfolgen.
3. Besucherordner, Besucher-Einzelbild/-video sowie manuelle und Vollbildpause getrennt prüfen. Normale Modi müssen bei aktivem Besucherschutz gesperrt bleiben.
4. Berechtigung am PC ändern, dann entziehen und Gerät entfernen; bereits verbundene App darf danach keinen Befehl mehr ausführen.
5. App-/PC-Neustart, WLANverlust, Wiederkehr und ein Update derselben APK ohne Deinstallation prüfen.
6. Nach bewusster Vorbereitung IP-Wechsel, mehrere Adapter, paralleles Proton VPN und blockiertes Multicast testen; danach gegebenenfalls manuelle lokale Adresshilfe prüfen.
7. Bestehende Besuchermodus-Abnahme für reguläres Beenden, Absturz/Prozessabbruch und Neustart mit sicheren Testmedien wiederholen. Die native Absicherung wurde nicht durch Remote ersetzt.

Es wurde vom Agenten keine produktive Instanz gestartet, keine bestehende Installation ersetzt, keine Firewallregel angewendet und kein Router/VPN umkonfiguriert.

## Erneut bauen

```powershell
pwsh -NoProfile -File build/Build-Remote.ps1
```

Der Build verwendet .NET SDK 10.0.404 für den Desktop und eine stabile private .NET-11-/MAUI-Android-Installation unter `%LOCALAPPDATA%\WallpaperControl.BuildTools\dotnet11`. Ein abweichender stabiler CLI-Pfad kann über `-Dotnet11` angegeben werden. JDK- und Android-SDK-Pfade sind ebenfalls Parameter. Die globale SDK-/Workloadinstallation wird nicht verändert.

Der Release-Signierschlüssel liegt unter `%LOCALAPPDATA%\WallpaperControl.BuildTools\RemoteSigning`; sein zufälliges Passwort ist DPAPI-geschützt. Dieser Schlüssel bleibt für Updates erforderlich und gehört nicht in Git oder das Auslieferungsverzeichnis. Der Build bricht bei unvollständiger Schlüsselablage ab und erzeugt nicht stillschweigend einen Ersatzschlüssel. Signatur und Prüfsummen werden nach dem Build geprüft. Lizenzen sind in der Android-App unter Einstellungen und in der Desktopausgabe unter `licenses/Remote` enthalten.
