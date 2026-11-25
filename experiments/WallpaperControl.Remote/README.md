# WallpaperControl Remote – isolierter Phase-01-Versuch

**Kein Produktbetrieb und keine echte Wallpapersteuerung.** Der Server ist beim normalen Start aus. Es werden nur synthetische Zustände geändert. Produktprojekt und Hauptsolution referenzieren diese Projekte nicht.

Ergebnis, Messgrenzen und Freigabetore: [Machbarkeitsbericht](../../docs/REMOTE_PHASE_01_FEASIBILITY_REPORT.md). Vor einem LAN-Test insbesondere dessen Freigabeabschnitt lesen. Ein öffentliches Windows-Netz wird absichtlich verweigert.

## Bauen und lokal prüfen

PowerShell in diesem Verzeichnis:

```powershell
.\Build.ps1
.\Test.ps1
.\Measure.ps1 -Seconds 30
dotnet run --project Checks/Checks.csproj -c Release -- --dns-loopback
```

`Build.ps1` verwendet SDK 10.0.404 für Desktop und das im Android-Unterverzeichnis separat gepinnte vorhandene SDK 11.0.100-rc.3.26522.148. Android verwendet MAUI-Pakete 11.0.10, Mindest-API 31, Compile/Target-API 37 und JDK 21.0.8. Eine stabile vollständige Android-/MAUI-11-Toolchain ist noch nicht nachgewiesen. SDK-/JDK-Pfade sind Parameter, `global.json` muss beim bewussten Toolchainwechsel aktualisiert werden. Es werden keine globalen Workloads installiert oder Systemeinstellungen geändert.

Ausgaben relativ zur Repositorywurzel:

- `artifacts/remote-phase01/Host/Host.exe`: Self-contained Win-x64-Testhost (.NET 10.0.15).
- `artifacts/remote-phase01/WallpaperControl.Remote.Experiment.apk`: ARM64-Debug-APK, eigene Application-ID `de.wallpapercontrol.remote.experiment`.
- `artifacts/remote-phase01/manifest.json`: Größen und SHA-256 der lokalen Ausgaben.
- `experiments/WallpaperControl.Remote/out/`: aktuelle Build-/Testprotokolle; `evidence/` enthält den eingefrorenen dokumentierten Nachweisstand.

## Testhost starten

```powershell
& .\artifacts\remote-phase01\Host\Host.exe
```

Dieser Aufruf erfolgt von der Repositorywurzel. Loopback ist vorausgewählt. Für einen freigegebenen LAN-Test bewusst den privaten physischen Adapter wählen, dann „Testserver starten“. Public-, VPN-/Tunnel-/virtuelle Adapter werden nicht zugelassen. Der Versuch bindet nur die ausgewählte IP, Port 47831; keine Firewallregel wird angelegt. Bei Adresswechsel stoppt der Host zur Sicherheit; neu starten und passende Adresse erneut auswählen. Profilprüfung erfolgt zusätzlich alle fünf Sekunden. Automatisches sicheres Desktop-Rebinding bleibt offen.

„Neuer Test-QR“ erzeugt einen fünf Minuten gültigen Einmal-QR. Beim Antrag Vergleichscode auf beiden Geräten prüfen, Deaktivierungsrecht gegebenenfalls ausdrücklich markieren und am PC bestätigen. Der ungeprüfte Gerätename ist keine Identitätsbestätigung.

Testidentität, Zertifikat und Tokenhashes liegen ausschließlich unter `%LOCALAPPDATA%\WallpaperControl.Remote.Experiment`, Geheimnisse DPAPI-geschützt. Der Android-Client speichert seine Testverbindung in SecureStorage; Backup ist aus. Kein Zugriff auf WallpaperControl-Einstellungen. Testdaten nicht durch das produktive Programm verwenden. Es gibt noch keine produktive Geräteverwaltung/Widerrufsoberfläche.

## Pixel-Abnahme

Die konkrete Schrittfolge und TLS-Negativtests stehen im Bericht. APK nur als **WC Remote Experiment** installieren; andere Apps bleiben unangetastet. Das APK ist mit einem lokalen Debugschlüssel signiert, keine endgültige Produktsignierung. Updates dieses Experiments benötigen dieselbe Application-ID und denselben Schlüssel; spätere Produkt-APK erhält eine eigene dauerhafte Signierstrategie.

Für TLS-Gerätetests sind zwei rein lokale Hoststartvarianten vorhanden, die die gespeicherte Testidentität nicht überschreiben:

```powershell
& .\artifacts\remote-phase01\Host\Host.exe --tls-expired
& .\artifacts\remote-phase01\Host\Host.exe --tls-foreign
```

Zuvor jeweils den bisherigen Testhost normal schließen. `--tls-expired` stellt mit demselben SPKI-Schlüssel ein abgelaufenes Testzertifikat aus; `--tls-foreign` verwendet einen anderen Schlüssel. **Nicht neu scannen**, sondern in Android die Verbindung mit gespeichertem Pin prüfen. Danach normal starten und positiven Kontrolltest wiederholen. Ein Netzwerkfehler allein beweist keine Zertifikatsablehnung; normaler Kontrolllauf und erreichbarer Host sind Voraussetzung.

## Grenzen

Windows-/Loopbacktests ersetzen keine Pixel-/LAN-/Proton-VPN-Abnahme. NSD liefert nur Adressen und ändert niemals Pins. Verbindung wird erst nach WSS, Auth und Vollsnapshot freigegeben; Offlinebefehle werden nicht gepuffert. Die Zustands-/Befehlssemantik ist bewusst synthetisch; sie bildet noch keine MainForm-/Besucherschutzintegration ab. Phase 02 wurde nicht begonnen.
