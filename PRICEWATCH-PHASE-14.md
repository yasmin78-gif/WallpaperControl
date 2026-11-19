# PriceWatch Phase 14 – automatische Überwachung und Wunschpreisalarme

Stand: 18.11.2026. Implementiert auf `codex/pricewatch-phase-14-alerts`, ausgehend von
`master` (`4de5155611f5fdaea72faee0c4ba059b9d0b9f0c`). Manuelle Abnahme ausstehend.
Kein Deployment, Merge oder Push. PHP-Proxy unverändert.

## Umsetzung

- Zwei dauerhaft gespeicherte Optionen im bestehenden PriceWatch-Einstellungsbereich:
  automatische Überwachung und Windows-Benachrichtigungen, beide standardmäßig aktiv.
  Die Benachrichtigungsoption ist bei abgeschalteter Überwachung deaktiviert.
- Prüfung um 08:00 und 20:00 lokaler Windows-Zeit, nur bei laufendem Programm und
  aktiviertem Desktop-Widget. Die Einstellungs-Vorschau führt keine Prüfung aus.
  Ein einzelner WinForms-Timer wartet bis zum nächsten Termin; kein Polling,
  Dienst, zusätzlicher Prozess oder Windows-Aufgabenplaner.
- Nachholprüfung beim Start, wenn noch keine erfolgreiche automatische Prüfung
  gespeichert ist oder die letzte mindestens zwölf Stunden zurückliegt.
  Ein erfolgreicher Lauf innerhalb der letzten Stunde verhindert einen unmittelbar
  folgenden Lauf am nächsten festen Termin. Zeit-/Zeitzonenänderungen und Aufwachen
  aus dem Energiesparmodus führen zu einer erneuten Terminberechnung.
- Nur Spiele mit Wunschpreis werden automatisch abgefragt. Bestehender Client,
  ausgewählte Händler und gemeinsame serielle Abrufwarteschlange werden verwendet.
  Parallele automatische Läufe werden verhindert. Bei Rate-Limit-Fehlern wird der
  Lauf beendet; bestehende Client-/Retry-After-Behandlung bleibt erhalten.
  Kein aggressiver Wiederholungszyklus nach Fehlern: nächster regulärer Termin
  beziehungsweise spätere Start-Nachholprüfung.
- Ungültige/fehlgeschlagene Antworten ersetzen keinen gespeicherten Preis.
  Entfernen, geänderter Wunschpreis, Händler-/Datenmoduswechsel und Programmende
  entwerten beziehungsweise stoppen laufende Ergebnisse.
- Neue SQLite-Tabellen `price_alerts` und `price_monitor_state` werden migrationssicher
  ergänzend angelegt. Watchlist, Wunschpreise und bisherige Preise bleiben erhalten.
  Alarmzustand und letzte automatische Prüfung sind je Datenmodus getrennt.
- Gültiger EUR-Treffer bei mindestens einem ausgewählten Händler: günstigstes
  passendes Angebot verwenden. Alarmzustand vor Benachrichtigung dauerhaft speichern.
  Auch akzeptierte manuelle Preisaktualisierungen und Erstabrufe werten den Alarm aus,
  sofern die Überwachung aktiviert ist; sie ersetzen nicht den automatischen Zeitstempel.
- Erneute Scharfschaltung nur nach verlässlich bestätigtem Zustand oberhalb des
  Wunschpreises. Dafür muss jeder ausgewählte Händler ein gültiges aktuelles Angebot
  liefern. Fehlende/abgelaufene Angebote oder Fehler setzen den Alarm nicht zurück.
- Testdaten bleiben offline. Alarmereignisse werden über einen austauschbaren
  Callback simuliert und lösen keine Windows-Benachrichtigung aus.

## Windows-Benachrichtigungen

Wiederverwendung von `DesktopNotifications` und `NativeNotificationBridge`:
native Shell-Benachrichtigung über das bereits registrierte WinForms-NotifyIcon
mit dessen tatsächlichem Fensterhandle und Icon-ID. Keine neue WinAppSDK-Abhängigkeit,
keine separate COM-/AUMID-Toast-Aktivierung. Der Klick funktioniert innerhalb der
laufenden Anwendung; eine Aktivierung nach Programmende ist nicht vorgesehen.

Die vorhandene begrenzte Warteschlange zeigt jeweils eine Benachrichtigung,
verwirft veraltete Einträge nach zwei Minuten und ist auf 32 wartende Einträge
begrenzt. Windows-Zustand wird über `SHQueryUserNotificationState` geprüft;
die bestehende native Implementierung verwendet `NIIF_RESPECT_QUIET_TIME`.
Windows entscheidet letztlich über die Anzeige. WallpaperControl wird nicht
aktiviert. Ausschließlich die validierte Original-Angebots-URL wird geöffnet;
beim Klick werden URL und Angebotsablauf erneut geprüft.

Bei deaktivierten Benachrichtigungen oder Unterdrückung durch Windows bleibt der
Preis als bereits erreicht gespeichert. Kein späteres Nachsenden und keine
Zustellgarantie. Lange Titel können durch die Windows-Shell gekürzt werden.

Offizielle Referenzen:

- [NOTIFYICONDATAW und Quiet-Time-Flag](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)
- [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicona)
- [Windows-Benachrichtigungszustände](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state)

## Verifikation

Release-Build der Anwendung und des Regressionstestprojekts: **0 Fehler, 0 Warnungen**.

```powershell
dotnet build WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release --nologo -v quiet
dotnet WallpaperControl.RegressionTests/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.RegressionTests.dll --pricewatch-checks
dotnet WallpaperControl.RegressionTests/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.RegressionTests.dll --notification-feature-checks
```

- **790 PriceWatch-Prüfungen bestanden**, einschließlich Termin-/Sommerzeitberechnung,
  Nachholen, Abschalten, Persistenz/Neustart, Alarmübergängen, unvollständigen Antworten,
  Händler-/Zielwechsel, Abbruch, manueller Aktualisierung und Offline-UI-Integration.
- **102 Prüfungen der vorhandenen Sound-/Benachrichtigungsinfrastruktur bestanden**,
  einschließlich nativer Tray-Identität, Queue, Klickzuordnung und Unterdrückung.
- Ausführung auf Windows 11 (Build 26300). Die Tests verwenden simulierte Anzeige-
  und Browseraktionen. Keine tatsächlichen Preis-/CDN-Anfragen, kein echter Banner.
- Sichtbarer Windows-Banner, tatsächliche Browseröffnung und Fokusmodus bleiben
  Gegenstand der manuellen Abnahme; sie sind durch diese Tests nicht nachgewiesen.

## Manuelle Abnahme

1. Release-Anwendung `WallpaperControl/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.exe`
   starten. Für die Offline-Abnahme zuvor den Testdatenmodus wählen. Achtung:
   bei aktiviertem Live-Modus kann die neue standardmäßig aktive Überwachung beim
   Start eine fällige Nachholprüfung ausführen.
2. PriceWatch aktivieren, beide neuen Optionen prüfen und speichern. Einstellungen
   erneut öffnen: Werte bleiben erhalten, Benachrichtigungsoption folgt der Überwachung.
3. Im Testdatenmodus Cyberpunk mit Wunschpreis 29,99 EUR verwenden und manuell
   aktualisieren. Lokale Preise (GOG 20 EUR / Steam 40 EUR) erfüllen das Ziel.
   Oberfläche bleibt bedienbar, kein echter Windows-Banner und keine Netzwerkanfrage.
   Die simulierten Ereignisse sind durch die Regressionstests geprüft.
4. Für den echten Banner ausdrücklich Live-Modus mit vorhandener Konfiguration
   wählen. Nur ein Spiel und die notwendigen Händler verwenden; einen Wunschpreis
   setzen, der ein tatsächlich aktuelles Angebot umfasst, und manuell aktualisieren.
   Windows-Benachrichtigungen für WallpaperControl zulassen, Fokusmodus deaktivieren.
   Erwartet: ein Banner mit Spiel, Händler, Preis und Wunschpreis. Klick öffnet die
   originale Angebotsseite; WallpaperControl wird nicht in den Vordergrund geholt.
5. Erneut aktualisieren und Anwendung neu starten: kein identischer Alarm.
   Zur Prüfung eines neuen Übergangs Wunschpreis unter das aktuelle Angebot setzen,
   mit vollständigen gültigen Angeboten aller ausgewählten Händler aktualisieren,
   anschließend Ziel über den aktuellen Preis setzen und erneut aktualisieren.
   Keine künstlichen Preisänderungen als Händlerdaten verwenden.
6. Überwachung/Benachrichtigung abschalten und Fokusmodus prüfen: keine Banner;
   nach Wiedereinschalten keine nachträgliche Flut. Überwachung ausgeschaltet:
   weiterhin manuell abrufbar, keine automatische Preisprüfung.
7. Anwendung über 08:00 oder 20:00 geöffnet lassen: ein automatischer Lauf nur für
   Zielpreis-Spiele. Neustart nach mindestens zwölf Stunden: einmalige Nachholprüfung.
   Neustart kurz nach erfolgreichem Lauf: keine Wiederholung. Während eines Abrufs
   Händler wechseln, Spiel entfernen oder schließen: keine veraltete Übernahme.

## Geänderte Bereiche und Einschränkungen

Neue Dienste: `PriceWatchSchedule`, `PriceWatchMonitor`, `PriceWatchStore.Alerts`,
`PriceWatchNotifications`; neue Tests: `PriceWatchMonitorTests`.
Ergänzungen in PriceWatch-Store, Widget/Form/Manager, Einstellungsseite und Vorschau,
MainForm-Benachrichtigungsanbindung sowie Ressourcen aller fünf vorhandenen Sprachen.

Die konservative Scharfschaltung kann bei Händlern ohne Angebot einen weiteren Alarm
verhindern, bis ein vollständig bestätigter Zustand vorliegt. Das ist absichtlich
sicherer als ein Zurücksetzen aufgrund fehlender Daten. Ein persistierter Treffer
bedeutet nicht, dass Windows den Banner tatsächlich angezeigt oder der Benutzer ihn
gesehen hat. Bei geschlossenem Programm erfolgen weder Prüfung noch Benachrichtigung.
