# WallpaperControl 2.6.1 / Remote 1.1.7

Der Desktop erhält Versions-, Assembly- und Dateiversion 2.6.1 / 2.6.1.0. Die Remote-Integration aus 2.6.0 bleibt kompatibel; eine neue Kopplung ist beim Update nicht erforderlich.

Die Android-App bietet zwei native Homescreen-Widgets für den Besuchermodus (1×1 und 2×1). Aktivierung erfolgt ohne Appfenster. Ausschalten benötigt einen frischen bestätigten Zustand, Geräteberechtigung und eine Sicherheitsabfrage. Ein veralteter/unbekannter Tipp aktiviert ausschließlich; nach bestätigter Aktivierung erklärt ein kurzer Hinweis den erneuten Tipp zum Ausschalten. Im Hintergrund ist die Anzeige zeitversetzt und mit Standzeit gekennzeichnet.

Die Geräteprüfung auf dem Pixel 9 Pro XL hat einen NSD-Listener-Lebensdauerfehler aufgedeckt und die Korrektur bestätigt. Wiederholte Kaltstarts, direkte Zustandsbestätigung beider Widgets, Diagnoseabfragen und der vollständige Aktivierungs-/Deaktivierungsablauf wurden auf dem Gerät getestet. Das Desktop-Icon ist jetzt im Android-Manifest korrekt zugeordnet. Application-ID, Signierschlüssel und bestehende Kopplung bleiben erhalten.

Benutzerabnahme erfolgreich, anschließend ausdrückliche Freigabe für Commit und Push auf dem Entwicklungsbranch. Details und Nachweise: [Phase-07-Bericht](REMOTE_PHASE_07_ANDROID_VISITOR_WIDGET_REPORT.md).

Prüfungen: 102 Remote-Tests und 15 native Packagingprüfungen erfolgreich. Die bestehenden 6302 Desktop-Regressionen wurden zuvor in Phase 07 erfolgreich ausgeführt. Die abschließende Desktopänderung betrifft ausschließlich die Versionierung. Doze-/Langzeit-/VPN-/DHCP-Feldtests sind damit nicht als vollständig abgenommen ausgegeben.
