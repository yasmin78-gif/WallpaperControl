# WallpaperControl Remote – Phase 07: Android Visitor Widget

**Aktueller USB-Teststand: 1.1.7 / versionCode 10.** Der native NSD-Absturz wurde auf dem angeschlossenen Pixel 9 Pro XL reproduziert und korrigiert. Kaltstarts und direkte Homescreen-Bestätigung beider Widgetvarianten wurden anschließend erfolgreich auf dem Gerät geprüft. Details und verbleibende Abnahmepunkte stehen im letzten Abschnitt dieses fortgeschriebenen Berichts. Frühere Angaben ohne Gerätetests beziehen sich auf die jeweiligen Vorversionen.

## Stand und Freigabegrenze

Umsetzung auf dem vorhandenen Branch `codex/remote-production`. Der Arbeitsbaum war zu Beginn dieser Phase sauber; vorhandene Änderungen wurden nicht zurückgesetzt. Keine neue Branch-Erstellung, kein Commit, Merge oder Push. Die Änderungen bleiben bis zur manuellen Widget-Abnahme uncommittet. Die produktive Vorversion wurde vom Benutzer praktisch bestätigt. Diese Bestätigung wird nicht als Gerätetest der neuen Widgets ausgegeben.

Die Android-App wird als **1.1.0**, **versionCode 3**, weiterhin `de.wallpapercontrol.remote`, mit derselben Release-Identität gebaut. Die bestehende SecureStorage-Kennung und das JSON-Format der Kopplung bleiben erhalten. Installation erfolgt als Update ohne Deinstallation.

## Technische Umsetzung

- Zwei native `AppWidgetProvider`: `CompactVisitorWidget` und `SwitchVisitorWidget`, jeweils mit eigener Provider-Metadatei und eigenem Eintrag in der Widget-Auswahl.
- Native `RemoteViews` mit LinearLayout, ImageView, TextView und ProgressBar. Dunkler abgerundeter Hintergrund, blaue/grüne/graue/neutrale Schild-Vektoren, grünes Häkchen und ein gezeichneter Schalter. Keine WebView, kein MAUI-Fenster für die Aktivierung.
- Kompakt: Zielraster **1 × 1**, zentraler Schild, kurzer Titel und Status; gesamte Fläche bedienbar. Schalter: **2 × 1**, Schild links, Text in der Mitte, Schalter rechts; ebenfalls gesamte Fläche und Schalter bedienbar. Tatsächliche Zellmaße, Textumbruch, Schriftvergrößerung und Launcher-Vorschau müssen am Pixel beurteilt werden. Beide Varianten sind vergrößerbar.
- `VisitorWidgetController` in der bestehenden Clientbibliothek enthält das testbare Zustands- und Ausführungsmodell. Alle Instanzen im gemeinsamen Androidprozess teilen denselben Controller und dieselbe nicht wartende Ausführungssperre.
- `RemoteCredentialStore` extrahiert die bisherige SecureStorage-Nutzung aus `RemoteSession`; App und Widgets benutzen denselben bestehenden Eintrag. Keine zweite Kopplungs- oder Authentifizierungslösung.
- `RemoteConnection.ConnectRestAsync` verwendet die vorhandenen TLS-, Pin-, Auth-, JSON-, Zustands- und Befehlspfade für einen begrenzten REST-Aufruf. Für Widgets wird kein Hub gestartet. Die normale App behält ihre bisherige SignalR-Verbindung.
- Desktop-Besuchermodus, seine Absicherung und seine Steuerungspfade werden in dieser Phase nicht verändert.

## Aktionen und Zustandsmodell

Zustände: unbekannt/veraltet, nicht gekoppelt, nicht erreichbar, bestätigt aktiv/inaktiv, wird aktiviert/deaktiviert, fehlgeschlagen/nicht bestätigt. Ausstehende Aktionen haben einen Ladeindikator und erlauben keinen zweiten Befehl. Alle Widget-Instanzen werden vollständig aktualisiert; der auslösende Widgettyp ist für die Steuerungslogik unerheblich.

**Aktivieren:** Ein unveränderlicher expliziter PendingIntent adressiert ausschließlich den privaten BroadcastReceiver. Dieser wertet den aktuellen Zustand beim Empfang aus, statt einen früheren Toggle-Entschluss aus einem Intent zu übernehmen. Bei unbekanntem oder veraltetem Zustand wird nur Aktivierung versucht. Die App muss dafür keine Activity öffnen. Nach Wiederherstellung des Prozesses existiert keine alte Ausschaltfreigabe im Speicher.

Die Aktion lädt die vorhandene Kopplung, versucht sichere lokale Verbindungen, fragt den aktuellen Zustand ab und sendet bei Bedarf ausdrücklich `visitorOn`. Bestätigt der PC bereits einen fehlerfrei aktiven Besuchermodus, genügt dieser aktuelle Snapshot als idempotentes Ergebnis; es wird insbesondere kein `visitorOff` gesendet. Die vorhandene Desktop-API bietet bei aktivem Besuchermodus keinen erneuten `visitorOn` als Fähigkeit an. Sonst erfolgt die Erfolgsmeldung erst nach `completed`, aktivem Ergebniszustand und ohne Wiedergabefehler. Kein optimistisches grünes Erfolgssignal.

**Deaktivieren:** Nur ein höchstens 30 Sekunden alter bestätigter aktiver Zustand mit `CanDisableVisitor` und der Fähigkeit `visitorOff` bietet die Dialog-Activity an. Der Text lautet:

> Besuchermodus deaktivieren?
>
> Deine normalen Hintergrundbilder werden wieder sichtbar.

Schaltflächen: **Abbrechen**, **Deaktivieren**. Abbrechen/Zurück sendet keinen Befehl. Erst nach Bestätigung werden Alter, aktueller PC-Zustand und aktuelle Berechtigung erneut geprüft. Anschließend wird ausdrücklich `visitorOff` gesendet; der Server prüft zusätzlich Epoch, Revision und Geräteberechtigung. Ein inzwischen veralteter oder nach Prozessneustart verlorener Ausschaltzustand kann keine Deaktivierung autorisieren. Die Activity ist privat und aus der Übersicht letzter Apps ausgeschlossen. Die vollständige Remote-App wird hierfür nicht geöffnet.

Die Dialog-Activity wird ausschließlich unmittelbar infolge der Launcher-Widget-Interaktion geöffnet. Das ist eine von Android dokumentierte Ausnahme für Hintergrund-Activitystarts; das konkrete Verhalten des Pixel-Launchers mit Android 17 muss manuell abgenommen werden. Wird der Start vom System verweigert, erscheint ein Hinweis; es erfolgt keine Deaktivierung.

## Hintergrundausführung und Wiederverbindung

Ein Broadcast nutzt `GoAsync` mit gemeinsamem **7-Sekunden-Gesamtbudget** und anschließendem `Finish`. Netzwerk- und SecureStorage-Warten werden abgebrochen. Es gibt keinen permanenten Service, Foreground Service, Job mit gespeichertem Steuerungsauftrag, Wake Lock oder dauerhaft offenen Widget-WebSocket.

Die vorhandene native DNS-SD-Erkennung liefert nur untrusted Adresskandidaten. Gespeicherte Adresse, QR-Adressen und neu erkannte lokale Adressen werden begrenzt geprüft, maximal drei Verbindungsversuche à 1,4 Sekunden innerhalb des Gesamtbudgets. Die bestehende Android-Link-Local-Scope-Normalisierung wird mitbenutzt. Pin, Server-ID und Token werden aus der bestehenden sicheren Kopplung übernommen; Erkennung ersetzt kein Vertrauen. Nach erfolgreicher Verbindung gibt es höchstens einen Mutationsaufruf. Bei unklarer Antwort wird ausschließlich das ursprüngliche Befehlsergebnis abgefragt, soweit das Restbudget reicht. Niemals automatischer Mutations-Replay.

Ein langsamer/aufwachender PC kann das Budget überschreiten. Dann bleibt das Ergebnis unbestätigt, auch wenn eine bereits am PC begonnene Aktion später abgeschlossen wird. Ein erneuter bewusster Aktivierungstipp prüft zuerst den tatsächlichen Zustand. Es gibt kein Wake-on-LAN und keine Zusicherung, dass ein ausgeschalteter oder schlafender PC erreichbar ist.

Gleichzeitige Taps werden verworfen, nicht aufgereiht. Der Klick-PendingIntent bleibt auch beim Ladeindikator erhalten: Ein vom Betriebssystem beendeter Prozess darf keinen dauerhaft unbedienbaren gespeicherten Spinner hinterlassen. Im laufenden Prozess schützt die Ausführungssperre vor Mehrfachausführung; nach Prozessneustart wird unbekannt gerendert und ein Tipp kann erneut ausdrücklich aktivieren.

## Synchronisation und Aktualität

Die offene App reicht neue bestätigte Zustände an alle Widgets weiter. Das umfasst über den bestehenden Serverkanal auch PC-, Desktop-Widget- und andere Geräteänderungen. Ältere Revisionen und wiederholt gemeldete unveränderte App-Snapshots können ein neueres Widgetergebnis nicht überschreiben oder seine Frist unbegrenzt verlängern. Kopplungswechsel, Entfernen der Kopplung, Verbindungsende und Besucherfehler entziehen die aktuelle Ausschaltfreigabe.

Ohne offene App verwendet Android die Provider-Aktualisierung **frühestens alle 30 Minuten**. Diese fragt ausschließlich Zustand ab. Android darf sie in Doze, Energiesparen oder bei Hintergrundbeschränkung weiter verzögern. Ein nicht aufweckender, ungenauer Alarm dient nur der lokalen Veraltungsanzeige, ohne Netzwerkzugriff und ohne zusätzliche Alarmberechtigung. Bei laufenden Zustandsmeldungen wird dessen Planung zusammengefasst.

Die Anzeige formuliert bewusst **„Zuletzt aktiv“ / „Zuletzt inaktiv“**, jeweils mit **„Stand HH:mm:ss · max. 30 s“**. Grüne/blaue Darstellung kennzeichnet diesen letzten bestätigten Snapshot, nicht eine zugesicherte aktuelle Liveverbindung. Nach Ablauf wird bei der nächsten Render-/Alarmgelegenheit neutral „Status unbekannt / veraltet“ gezeigt. Weil Android auch den Veraltungsalarm verzögern darf, kann die datierte historische Anzeige länger sichtbar bleiben. Sie wird nicht als permanent aktueller Zustand bezeichnet. **Die Prüfung, ob Ausschalten erlaubt ist, läuft unabhängig davon bei jedem Tipp und nach Dialogbestätigung mit der monotonen 30-Sekunden-Frist.** Eine verzögerte Oberfläche kann daher keine alte Ausschaltfreigabe verlängern. Erst ein neuer bestätigter Snapshot erlaubt diese erneut.

Diese Hintergrundbeschränkung ist absichtlich keine aggressive Liveüberwachung. Ein Homescreen-Widget ist kein garantierter ständig erreichbarer Sicherheitsschalter: WLAN, PC, Host, LAN-Freigabe, gültige Identität und Android-Ausführungsmöglichkeit müssen vorhanden sein.

## Sicherheit und Fehlerbehandlung

- Bestehendes HTTPS mit SPKI-Pinning, Gültigkeits-/EKU-/Schlüsselprüfung und Header-Authentifizierung. Keine Redirect-/Proxy-/HTTP-/Trust-All-Ausnahme.
- Alle drei Receiver und die Bestätigungs-Activity sind `exported=false`. Steuerungsreceiver ohne extern passenden Intentfilter; Widget-Updatefilter nur an den beiden Providern. PendingIntents explizit, unveränderlich und ohne Zugangsdaten oder gespeicherten Toggle-Befehl.
- Keine Zugangsdaten oder ausführbaren Kommandos in Widget-SharedPreferences, keine Zugangsdaten-/QR-/Tokenlogs. Der sicherheitsrelevante Widget-Cache enthält ausschließlich Status/Berechtigungsentscheidung/Zeit im Prozessspeicher und wird nach Prozessverlust verworfen. Seit Diagnoseausgabe 1.1.2 wird ausschließlich ein abgeschlossener Diagnoseeintrag ohne Identität, Adresse, Geheimnis oder wiederholbaren Auftrag privat gespeichert.
- Keine zusätzlichen Berechtigungen. Android 17 benötigt weiterhin die schon vorhandene LAN-Erlaubnis. Ohne sie bleibt der Zugriff geschlossen; Einrichtung erfolgt in der App. Ein nicht gekoppeltes Widget öffnet beim Tipp die App.
- PC aus/Schlaf, Host beendet, Fernsteuerung aus, WLANverlust, TLS-/Pinfehler und nicht auffindbare neue IP ergeben „Nicht erreichbar“ bzw. ein nicht bestätigtes Ergebnis. Keine Erfolgsmeldung ohne PC-Nachweis.
- Fehlende Besucherkonfiguration/Fähigkeiten, Wiedergabefehler, Busy/Revision-Konflikte und entzogene Rechte werden abgewiesen. Kurze verständliche Hinweise im Widget und bei fehlgeschlagener Tippaktion zusätzlich als Toast; keine technischen oder geheimen Exceptiontexte.
- Nach Verbindungswiederkehr können Hintergrundaktualisierung und App nur Zustand abfragen. Ein zuvor fehlgeschlagener Widgetbefehl wird nicht nachgeholt.

## Build- und Testergebnisse

- **6302 bestehende Regressionstests erfolgreich** (`artifacts/remote-phase07-regression.log`).
- **94 Remote-Prüfungen erfolgreich**, davon **33 neue Widget-Prüfungen** (`artifacts/remote-phase07-tests.log`). Tests der echten Clientbibliothek: explizite Aktivierung, bereits aktiv, unbekannt/veraltet, Bestätigung, Rechteentzug vor Ausführung, Fehler/Abbruch, gleichzeitige Instanzen/20 schnelle Taps, keine optimistische Bestätigung, App-Snapshotreihenfolge, Kopplungsentfernung während Verbindungsaufbau und kein späteres Replay. Echte gepinnte authentifizierte HTTPS-Verbindung mit null WSS-Sitzungen für Aktivierung/Deaktivierung und Rechtewiderruf.
- **13 native Packagingprüfungen** mit `build/Test-VisitorWidgets.ps1`: finale zusammengeführte Manifestregistrierung, private Komponenten, fehlender externer Steuerungsfilter, unveränderte Berechtigungsgrenzen, HTTPS/Backup-Regeln, zwei Rastergrößen, Mindestintervall und RemoteViews-kompatible Layouttypen.
- Release-APK, Signatur und finale Manifestversion werden im zugehörigen Phase-07-Buildnachweis festgehalten. Stabiler privater .NET-11-/MAUI-/Android-Build wie bisher; unveränderte Release-Signieridentität.
- **Vor der späteren USB-Verbindung keine Android-Gerätetests durchgeführt:** `adb devices` meldete damals kein verbundenes Gerät. Der Test eines neuen Controllers simulierte Prozessverlust auf Logikebene; er war kein Nachweis für echten Android-Prozess-Kaltstart, Launcher-Taps, Alarmzustellung oder sichtbares Dialogverhalten. Die späteren tatsächlichen Pixeltests sind unten getrennt dokumentiert.

Nachweise werden unter `docs/remote-evidence/phase07/` abgelegt. Für die neuen Zustands-/Netzwerkpfade wurden Tests ergänzt; die vorhandenen Desktop-Sicherheitsregressionen wurden zusätzlich ausgeführt. Es wurden keine echten Wallpaper durch die Tests geändert.

## APK und manuelle Abnahme auf dem Pixel 9 Pro XL

APK im Repository: `artifacts/remote-production/WallpaperControl.Remote.apk`.

Zusätzliche Kopie gemäß bestehender Benutzerpräferenz: `C:\Users\yasmi\Dropbox\WallpaperControl.Remote-1.1.0.apk`.

1. APK **über die vorhandene App installieren**, nicht deinstallieren. Version 1.1.0 prüfen; bisherige QR-Kopplung muss ohne neuen Scan funktionieren. Keine neue Desktopversion erforderlich.
2. Pixel-Homescreen lange drücken → **Widgets → WallpaperControl Remote**. Beide unabhängigen Varianten platzieren: Kompakt 1 × 1 und Schalter 2 × 1. Zusätzlich mehrere Instanzen platzieren. Rastergröße, Texte, Schalter, Kontrast und vergrößerte Systemschrift prüfen.
3. Bei inaktivem Modus einmal tippen. Kein Appfenster, keine zusätzliche Activity und keine Nachfrage dürfen erscheinen. Ladeanzeige, tatsächlichen PC-Besucherschutz und anschließend übereinstimmende Widgets prüfen.
4. App aus der Übersicht entfernen und den Prozess bei Bedarf kontrolliert über ADB beenden, **nicht mit Android „Stopp erzwingen“ gleichsetzen**. Unbekannt/alte Anzeige antippen: nur Aktivierung darf versucht werden. PC bleibt bei bereits aktivem Besuchermodus aktiv. Mehrfach schnell auf beide Widgets tippen.
5. Nach frischer Aktivbestätigung mit am PC erlaubtem Ausschaltrecht antippen. Nur den kleinen Sicherheitsdialog erwarten. **Abbrechen/Zurück** muss den Besucherschutz erhalten; **Deaktivieren** muss erst nach PC-Bestätigung inaktiv anzeigen. Während des Dialogs das Recht am PC entziehen und erneut bestätigen: keine Deaktivierung.
6. Mehr als 30 Sekunden ohne neue Appmeldung warten; Hintergrund-/Alarmverzögerung berücksichtigen. Ein Tipp auf alte Anzeige darf nur aktivieren/aktiven Zustand neu bestätigen, niemals direkt ausschalten. Erst danach kann ein weiterer bewusster Tipp den Sicherheitsdialog öffnen.
7. Besuchermodus am PC, Desktop-Widget, in der offenen App und über ein anderes gekoppeltes Gerät ändern. Alle Homescreen-Instanzen bei neuen Appmeldungen prüfen. Danach App schließen: keine unverzügliche Liveaktualisierung erwarten; Standzeit/veralteten Zustand beurteilen.
8. WLAN aus, PC/Host aus, Fernsteuerung aus und PC im Energiesparmodus getrennt prüfen. Keine falsche Erfolgsmeldung. Verbindung wiederherstellen: ohne neuen Tipp darf kein früher fehlgeschlagener Befehl ausgeführt werden.
9. Kontrolliert IP-Wechsel/mDNS und manuelle lokale Adresshilfe testen. TLS-/Identitätsfehler dürfen nie in einen unsicheren Fallback führen. Geräteentfernung und fehlende Besucherkonfiguration prüfen.
10. Prozess während Ladeanzeige beenden, anschließend Widget erneut bedienen. Android-Doze, Neustart, Launcher-Neustart und optional „Stopp erzwingen“ separat prüfen. Force-Stop kann Android absichtlich zur Sperre von Widgets/PendingIntents veranlassen; dann muss die App einmal bewusst geöffnet werden. Die App umgeht diesen Systemzustand nicht.
11. Ohne Kopplung platzieren/antippen: „Nicht gekoppelt“ und Einrichtung in der App. Bei normalem Update vorhandene Kopplung und App-ID unverändert prüfen.

## Noch offene Prüfungen und Git-Status

Offen sind sämtliche oben genannten echten Pixel-/Launcher-/Doze-/Prozess-/LAN-Szenarien und die visuelle Prüfung beider Rastergrößen auf dem Gerät. Insbesondere die Dialogöffnung aus der unmittelbaren Widgetinteraktion und die 7-Sekunden-Verbindung unter realen WLAN-/VPN-Bedingungen benötigen manuelle Abnahme. Keine bestätigte Feldabnahme und kein erfolgreicher Android-Kaltstart werden vorweggenommen.

Aktueller Branch: `codex/remote-production`. Änderungen an Android-App, gemeinsamem Client und Testprojekt sowie neue Widgetressourcen, Controller, gemeinsame SecureStorage-Fassade, Packagingtest und dieser Bericht liegen **uncommittet** im Arbeitsbaum. Generierte APKs/Logs bleiben unter den ignorierten Artefakten. Keine privaten Signierschlüssel im Repository oder Dropbox. Ein genauer abschließender `git status --short` wird im Phase-07-Nachweis gespeichert.

**An dieser Stelle wird nach Bereitstellung zur manuellen Abnahme angehalten. Kein Commit, Merge oder Push ohne ausdrückliche Freigabe.**

## Korrektur nach erster manueller Rückmeldung – 1.1.1 / versionCode 4

Benutzerrückmeldung: Die Widgets kennen den Besuchermodusstatus nicht. Bei der Codeprüfung wurde ein konkreter Lebenszyklusfehler gefunden: `RemoteSession.Suspend` und die anschließenden Disconnect-Benachrichtigungen löschten den zuvor bestätigten Widgetzustand beim Verlassen der App sofort. Der beabsichtigte Verbindungsabbau beim Wechsel zum Launcher wird jetzt von echtem Verbindungsverlust im Vordergrund unterschieden. Die ursprüngliche monotone 30-Sekunden-Frist wird erhalten, niemals beim Hintergrundwechsel verlängert. Nach Ablauf kann der letzte bekannte Wert zusätzlich als ausdrücklich veraltete Historie angezeigt werden; Schild und Ausschaltfreigabe bleiben dabei neutral/gesperrt.

Sechs neue Regressionen prüfen den Wechsel zum Launcher samt Hub-Disposal, unverlängerte Ablaufzeit, historische Anzeige ohne Ausschaltfreigabe, erneute App-Synchronisation, echten Verbindungsverlust und Entfernen der Kopplung. Insgesamt **100 Remote-Prüfungen**. Die frühere vollständige Desktop-Regressionsausführung mit 6302 Prüfungen bleibt gültig; diese Korrektur ändert ausschließlich Android-/Client-Lebenszyklus und dessen Tests. Native Packagingprüfungen werden für die neue APK erneut ausgeführt. Ein praktischer Nachweis, dass diese Ursache die konkrete Gerätebeobachtung vollständig erklärt, steht noch aus.

Neue APK: `C:\Users\yasmi\Dropbox\WallpaperControl.Remote-1.1.1.apk`. App über die vorhandene Version installieren, verbunden öffnen und anschließend zum Homescreen wechseln: Der bestätigte Stand darf nicht sofort verschwinden. Nach 30 Sekunden ohne neue Bestätigung ist „veraltet“ weiterhin beabsichtigt. Zusätzlich Aktivierung direkt aus beiden Widgets und anschließenden bestätigten Zustand testen. Keine neue Kopplung, kein Commit/Merge/Push.

## Primärquellen

- [Android: Advanced widgets](https://developer.android.com/develop/ui/views/appwidgets/advanced): RemoteViews, Broadcast-Lebensdauer, asynchrone Arbeit, Mindestaktualisierung 30 Minuten.
- [Android: Activity security / background starts](https://developer.android.com/guide/components/activities/secure-bal): direkte Launcher-/Widget-Interaktion als Ausnahme und Einschränkungen neuer Androidversionen.
- [Android: Inexact alarms](https://developer.android.com/develop/background-work/services/alarms/schedule): mögliche Verzögerungen ohne exakte Alarmberechtigung.

Zusätzliche Rückmeldung: „Status unbekannt“ erscheint auch unmittelbar nach dem Appstart. Der Lebenszyklusfehler erklärt diese Beobachtung daher nicht vollständig. Die Widgetsuche verwendet nun ausdrücklich die im Manifest registrierten Android-Komponentennamen. Zuvor stumm verworfene Renderfehler sind als reiner Fehlertyp in einer bewusst aufrufbaren Diagnose sichtbar. Unter App-Einstellungen → „Widgetstatus prüfen und aktualisieren“ werden eine begrenzte reine Zustandsabfrage ausgeführt und erkannte Instanzzahlen, Controllerzustand und Aktualisierungsergebnis angezeigt. Kein Steuerungsbefehl, kein Token-/Identitäts-/Exceptiontext in dieser Diagnose. Die Behebung der konkreten Gerätebeobachtung bleibt bis zum erneuten Pixeltest unbestätigt.

Weitere manuelle Rückmeldung: Ein Widget-Tipp verändert den Besuchermodus am PC, aber die Widget-Anzeige folgt nicht. Das ist keine erfolgreiche Widget-Abnahme. Die konkrete Ursache ist weiterhin nicht belegt; eine reine Erklärung durch verzögerte periodische Hintergrundaktualisierung reicht für diesen Fehler nicht aus.

Version 1.1.2 (versionCode 5) ergänzt einen prozessübergreifend lesbaren privaten Diagnoseeintrag des letzten abgeschlossenen Widget-Aufrufs mit Uhrzeit, Laufzeit, Ergebnisstatus, generischem Hinweis und reinem Renderfehlertyp. Dieser Eintrag wird beim Öffnen der App bzw. einer reinen Diagnoseabfrage nicht überschrieben. Es werden keine Credentials, Serveridentitäten, Adressen oder ausführbaren Aufträge gespeichert. Ein Fehler beim ergänzenden Veraltungsalarm kann außerdem nicht mehr die eigentliche Widget-Aktualisierung überspringen. Ob dieser Fehler auf dem Pixel auftrat, wird ausdrücklich nicht behauptet.

Nächster Geräteschritt: APK 1.1.2 als Update installieren, App verlassen, Widget einmal bedienen, anschließend App → Einstellungen → Widgetstatus prüfen und aktualisieren öffnen und insbesondere den Abschnitt „Letzter Widget-Aufruf“ zurückmelden. Erst dieser Nachweis trennt eine fehlende PC-Bestätigung vom fehlgeschlagenen Rendern oder einer Launcher-Verzögerung. Der Fehler bleibt bis zur erfolgreichen erneuten Abnahme offen. Kein Commit/Merge/Push.

### Weitere Korrekturen – 1.1.3 / versionCode 6

Die Benutzerbeobachtung bleibt: PC reagiert, Homescreenanzeige folgt erst beim Öffnen der App. Der Diagnoseeintrag des konkreten fehlgeschlagenen Aufrufs liegt noch nicht vor. Zwei zusätzliche Schwachstellen wurden im Code abgesichert: Ein später Disconnect des App-Hubs darf einen unabhängig davon gerade per Widget-REST bestätigten Zustand nicht löschen; nur noch Livezustände dieses Hubs werden durch dessen Trennung entwertet. Die native Aktualisierung wird auf dem Android-Hauptthread ausgeführt, und der Broadcast wartet auf die Übergabe der finalen Aktualisierung vor `Finish`. Zwei gezielte Regressionen decken die unabhängige REST-Bestätigung gegen nachlaufende Hub-Trennungsmeldungen ab. Insgesamt 102 Remote-Prüfungen; die konkrete Pixelbehebung ist weiterhin offen.

Separater Iconfehler eindeutig festgestellt: Im finalen Android-Manifest fehlten sowohl `android:icon` als auch `android:roundIcon`, obwohl Resizetizer die Bilddateien erzeugt hatte. Die Anwendung referenziert diese Ressourcen jetzt ausdrücklich. Eine zusätzliche Packagingprüfung sichert beide Manifestverweise ab (nun 14 Packagingprüfungen). Vorherige Prüfungen der generierten PNGs allein waren kein Nachweis für ein tatsächlich zugeordnetes Launcher-Icon. Die Desktop-Icondatei wird weiterhin unverändert verwendet. APK 1.1.3 wird im bestehenden Updatepfad mit identischem Schlüssel erstellt und nach Dropbox kopiert. Keine Deinstallation erforderlich, kein Commit/Merge/Push.

### Absturz bei der Diagnose – 1.1.4 / versionCode 7

Benutzer bestätigt, dass beim Prüfen des Widgetstatus die gesamte App beendet wird; USB/ADB ist aktuell nicht verfügbar. Keine funktionierende Widget-Abnahme. Der bisherige Async-Clickhandler führte zuerst eine Netzabfrage aus und zeigte anschließend über die verdeckte MainPage einen modalen Alert; sein äußerer Fehlerpfad war nicht abgesichert. Ein konkreter Geräte-Stacktrace liegt nicht vor, daher wird diese Codeauffälligkeit nicht als bewiesene alleinige Absturzursache ausgegeben.

Die Diagnose wird nun als eigenständige normale Navigationsseite geöffnet, ohne vorgeschaltete Netzabfrage oder modalen Alert. Der vorhandene Bericht erscheint direkt und kann über „Bericht kopieren“ kopiert werden. Eine getrennte, bewusst anzustoßende reine Zustandsabfrage hat ein sieben Sekunden langes Budget. Lesen, Kopieren, Abfragen und Seitennavigation haben explizite Fehlerbehandlung mit reinen Fehlertypen statt Geheimnissen oder Exceptiontext. Der letzte Widget-Aufruf bleibt unverändert erhalten. Die genaue Widgetursache und der tatsächliche Absturz bleiben bis zu erneutem Gerätetest bzw. Diagnose offen. Nur Build/Packaging werden für diese UI-Korrektur erneut geprüft; keine erneute vollständige Desktop-Regressionsausführung und kein behaupteter Android-Laufzeittest. APK: Dropbox/WallpaperControl.Remote-1.1.4.apk. Keine Deinstallation, kein Commit/Merge/Push.

## Tatsächlicher USB-Gerätetest und behobene Ursache – 1.1.5 / 1.1.6

Google Pixel 9 Pro XL, Android 17/API 37, bestehende produktive Kopplung. Die APK wurde jeweils mit `adb install -r` als signiertes Update installiert. Keine Deinstallation, kein neuer QR-Scan. Der aktuelle Desktop wurde nicht ersetzt; der vorhandene Besuchermodus blieb aktiv.

### Reproduktion und Nachweis

Vor der Korrektur ließ sich das Problem durch einen Tipp auf das Kompakt-Widget nach `am kill` des Hintergrundprozesses reproduzieren. Der PC/REST-Zustand wurde zunächst bestätigt, anschließend stürzte der Prozess beim Beenden der lokalen NSD-Suche ab. Logcat zeigte `Stack overflow`, wiederholte `NsdManager.IDiscoveryListenerInvoker.OnDiscoveryStopped`-/JNI-Aufrufe und schließlich `SIGABRT` auf `ConnectivityThr`. `dumpsys activity exit-info` bestätigte native und Java-Absturzereignisse. Es handelte sich nicht lediglich um einen abgelaufenen Anzeigezeitstempel oder ein zu kurzes HTTP-Zeitlimit.

Ursache: Der kurzlebige Widgetpfad verwendete `using var discovery`. `StopServiceDiscovery` und das Abmelden der ServiceInfoCallbacks sind jedoch asynchron. Der anschließende sofortige `Dispose` trennte die Java- und Managed-Listeneridentität, bevor Android den terminalen Rückruf lieferte. Der JNI-Invoker geriet rekursiv in denselben Rückruf. Das erklärt auch den Diagnose-Absturz beim Anstoßen derselben einmaligen Suche; die vorherige Umstellung auf eine normale Diagnoseseite beseitigte diese native Ursache allein nicht.

### Konkrete Korrektur

Kein unmittelbares `Dispose` des NSD-Listeners im Widgetpfad. Laufende Registrierungen und ihre konkreten Callbackobjekte bleiben in einer begrenzten gemeinsamen Registrierungsliste stark referenziert, bis Discovery-Stopp und alle ServiceInfo-Abmeldungen bzw. Legacy-Auflösungen terminal bestätigt sind. Erst dann entfällt die Wurzelreferenz; die normale Java/Managed-Lebensdauerverwaltung übernimmt. Es gibt keine unbeschränkte Liste und keine dauerhafte Suche nach `Stop`. Wird das Framework nicht fertig, bleiben höchstens 32 ausstehende Registrierungen erhalten; weitere Erkennungsversuche schlagen geschlossen fehl, während vorhandene sichere Adressen weiterhin verwendet werden können. Pinning, Authentifizierung und Kopplung bleiben unverändert. Callbacklisten und Start/Stop sind synchronisiert.

Zusätzlich wurde auf dem Gerät festgestellt, dass die Bestätigungs-Activity beim Abbrechen zum bisherigen App-Task zurückkehren konnte. Sie besitzt nun eine eigene Task-Affinität; Abbrechen führt zum Launcher. In 1.1.6 wurden nach Sichtprüfung die kleinen Widgetlayouts angepasst: kompakteres Schild, optionaler Titel nur bei ausreichend Platz, kürzere Zeitangaben und mehr Textbreite im 2×1-Schalter. Die 30-Sekunden-Sicherheitsfrist bleibt unverändert.

### Tatsächlich durchgeführte Prüfungen

- 1.1.5: drei Kaltstartzyklen Kompakt-Widget nach nachgewiesen beendetem App-Prozess, je direkter Launcher-Tipp und bestätigtes „Zuletzt aktiv“ ohne Appfenster, fortbestehender Prozess nach asynchronem NSD-Stopp und kein Stacküberlauf/Fatalsignal im neuen Prozess. 16 Scriptprüfungen.
- Zweite Widgetvariante regulär über den Pixel-Widgetpicker hinzugefügt. Beide Varianten sind unabhängig auswählbar und gleichzeitig platziert.
- 1.1.5: zwei weitere Kaltstartzyklen über das Schalter-Widget mit Prüfung, dass **beide** Instanzen direkt auf dem Homescreen denselben bestätigten Zustand anzeigen. 11 Scriptprüfungen.
- Ausschaltbestätigung auf dem Pixel geöffnet, korrekter Text/Buttons geprüft und **abgebrochen**. Rückkehr zum Launcher statt vollständiger Remote-App über Activityzustand nachgewiesen. Es wurde kein Live-`visitorOff` ausgeführt und keine normale Hintergrundquelle sichtbar geschaltet.
- Fünf gezielte Diagnoseabfragen in derselben App-Prozessinstanz: Seite blieb geöffnet, Zustand bestätigt, Prozess-ID unverändert und kein Stacküberlauf/Fatalsignal. Das vorherige native Absturzverhalten war danach nicht mehr reproduzierbar.
- Desktop-Icon auf dem Pixel-Homescreen tatsächlich sichtbar. Das benachbarte generische Androidsymbol gehört weiterhin zur separat installierten alten Experiment-App; diese wurde nicht entfernt.
- Gemeinsame Remote-Regressionssuite erneut erfolgreich: **102 Prüfungen**. Finale native Packagingprüfungen einschließlich getrennter Dialog-Task-Affinität: **15 Prüfungen**. Keine erneut behauptete vollständige Desktop-Regressionsausführung; deren 6302 erfolgreiche Prüfungen wurden früher in Phase 07 durchgeführt.
- Nach Layoutanpassung finaler Build **1.1.6** und erneute Gerätetests; das genaue Ergebnis steht in `docs/remote-evidence/phase07/device-final-tests.txt` und dem finalen Buildnachweis.

Die echten Kaltstarttests sind als wiederholbares Skript `build/Test-VisitorWidgetDevice.ps1` vorhanden. Es verwendet `am kill` erst nach Verlassen der App, ausdrücklich kein Force-Stop. Es sendet ausschließlich bewusste Aktivierungen und keine Deaktivierung. Eine vorhandene Kopplung, ein laufender PC und mindestens ein platziertes Widget sind Voraussetzungen. Nach einem APK-Update wird die App einmal normal geöffnet, bevor die Kaltstartsequenz beginnt; der erste Paketstart nach Installation wird nicht als Widget-Kaltstart ausgegeben.

### Offene Feldabnahme

Die tatsächlich getestete sichere Aktion ist die wiederholte Bestätigung/Aktivierung des bereits aktiven PC-Besuchermodus. Ein Live-Wechsel vom aktiven Schutz zu normalen Bildern wurde nicht durch den Agenten ausgeführt. Reale Deaktivierung, Berechtigungsentzug während des Dialogs, PC-/WLANverlust, DHCP/VPN, Doze/Neustart und längere Laufzeiten bleiben gesonderte Abnahmepunkte; die entsprechenden Logik-/Rechteprüfungen sind automatisiert getestet. Es wird keine permanente Aktualität im geschlossenen Hintergrund versprochen. Die Datums-/Uhrzeitangaben in Gerätebelegen stammen vom Gerät, nicht aus einer angenommenen Hostuhr.

Aktuelle APK: `C:\Users\yasmi\Dropbox\WallpaperControl.Remote-1.1.6.apk`, zusätzlich `artifacts/remote-production/WallpaperControl.Remote.apk`. Auf dem Pixel bereits als Update installiert. Beide Widgetvarianten bleiben platziert, der Besuchermodus bleibt aktiv. Kein Commit, Merge, Push oder neuer Branch; alle Änderungen bleiben zur Benutzerabnahme uncommittet.

Primärquelle zum asynchronen Stopp und dem terminalen Listener-Rückruf: [Android NsdManager](https://developer.android.com/reference/android/net/nsd/NsdManager#stopServiceDiscovery(android.net.nsd.NsdManager.DiscoveryListener)). Die konkrete JNI-Rekursion ist durch den eigenen Gerätefehlernachweis belegt, keine aus der Dokumentation übernommene Vermutung.

Finaler Nachweis 1.1.6: 11 automatisierte echte Geräteprüfungen in zwei weiteren Kaltstartzyklen mit beiden Widgetinstanzen erfolgreich. Zusätzlich auf dieser APK zwei erneute Diagnoseabfragen mit fortbestehendem Prozess, bestätigtem Zustand und ohne neue Fatal-/Stackoverflow-Meldung. Ein bereits abgelaufener Widgetzustand wurde per Tipp ausschließlich neu als aktiv bestätigt; erst der folgende frische Tipp bot die Ausschaltbestätigung an. Zurück/Abbrechen führte wieder zum Pixel-Launcher. Titel/Status und verkürzte Zeitangaben wurden auf dem Gerät sichtbar geprüft. Die eigentliche Deaktivierung wurde nicht ausgeführt. APK-Signatur und Dropbox-Kopie geprüft. Abgeschlossene USB-Prüfung, danach Halt zur weiteren Benutzerabnahme; keine Git-Veröffentlichung.

## Vollständiger Ausschaltversuch auf dem Gerät – 1.1.6 / 1.1.7

Nach der Rückmeldung „Beim Beenden über das Widget passiert nichts“ wurde der aktuelle Bildschirm direkt geprüft: Beide Widgets zeigten „Veraltet“ und den letzten aktiven Zustand. Der erste Tipp bestätigte den bereits aktiven Besuchermodus neu, entsprechend der ursprünglichen Vorgabe für unbekannte/veraltete Zustände. Der zweite Tipp öffnete die native Ausschaltbestätigung. Nach bewusstem Antippen von „Deaktivieren“ bestätigten beide Widgets „Zuletzt inaktiv“, ohne Appfenster und ohne Prozessabsturz. Damit wurde nun erstmals auch die echte Live-Deaktivierung über das Widget ausgeführt; die frühere Einschränkung „kein visitorOff getestet“ gilt nur für die vorherigen Tests.

Dieser erfolgreiche Geräteversuch belegt den getesteten Ablauf, nicht rückwirkend jeden früheren Benutzertipp. Als Nutzbarkeitsproblem ist der bisher stille idempotente Ersttipp erkennbar: Bei veraltetem Aktivzustand gibt es zunächst keinen sichtbaren PC-Wechsel und noch keinen Ausschaltbefehl. 1.1.7 (versionCode 10) zeigt deshalb nach erfolgreicher aktiver PC-Bestätigung eine kurze Rückmeldung: „Besuchermodus aktiv. Zum Ausschalten erneut tippen.“ Fehlt das Recht, wird stattdessen die fehlende PC-Freigabe genannt. Diese Rückmeldung erscheint nur nach tatsächlich erfolgreicher Bestätigung, nicht bei Zeitlimit, Fehler oder abgewiesenem Befehl. Die Sicherheitsregel wird nicht abgeschwächt: Ein veralteter/unklarer Tipp aktiviert weiterhin ausschließlich, und Deaktivierung benötigt weiterhin Bestätigung plus erneute Rechte-/Zustandsprüfung.

APK: Dropbox/WallpaperControl.Remote-1.1.7.apk. Das endgültige Testergebnis und der auf dem PC belassene Zustand werden im ergänzenden Gerätenachweis festgehalten. Kein Commit/Merge/Push.

Finaler echter Gerätetest 1.1.7: Update auf dem Pixel installiert, normale App einmal geöffnet, anschließend Hintergrundprozess beendet. Aus inaktivem PC-Zustand per Widget aktiviert; beide Instanzen bestätigten „Zuletzt aktiv“. Danach frischen Widgettipp, native Sicherheitsabfrage und „Deaktivieren“ ausgeführt. Beide Instanzen bestätigten „Zuletzt inaktiv“, der Pixel-Launcher blieb sichtbar und derselbe Prozess bestand ohne neue Fatal-/Stackoverflow-Meldung fort. Der Besuchermodus wurde entsprechend dem gewünschten Beenden **ausgeschaltet belassen**. Die frühere Angabe „Besuchermodus bleibt aktiv“ beschreibt nur den Abschluss des vorherigen Tests. 15 finale Packagingprüfungen erneut erfolgreich; APK-Signatur und identische Dropbox-Kopie geprüft. Keine weiteren Änderungen an Desktopquellen oder Benutzerkonfiguration; weiterhin kein Commit/Merge/Push.

## Benutzerabnahme und Freigabe für Commit/Push

Der Benutzer hat nach dem vollständigen Ausschaltversuch bestätigt: „Jetzt funktioniert es.“ Anschließend wurde ausdrücklich Commit und Push freigegeben, mit vorheriger Anpassung der Desktop-Version. Die frühere Commit-/Push-Sperre ist damit für den Abschluss dieser Phase aufgehoben; die entsprechenden Aussagen weiter oben dokumentieren die damaligen Abnahmestände.

Desktop-Version, AssemblyVersion und FileVersion wurden von 2.6.0 / 2.6.0.0 auf **2.6.1 / 2.6.1.0** erhöht. Android bleibt bei der auf dem Pixel abgenommenen Version **1.1.7 / versionCode 10**. Die Einrichtungshinweise wurden entsprechend aktualisiert. Es wird der bestehende Branch `codex/remote-production` committed und nach `origin` gepusht, ohne Merge in einen anderen Branch und ohne Veröffentlichung eines GitHub-Releases. Desktop-Build, 102 Remote-Prüfungen und 15 native Packagingprüfungen werden für diesen Abschluss erneut geprüft. Die lokal erzeugte Desktopausgabe wird ebenfalls mit Dateiversion 2.6.1.0 bereitgestellt.
