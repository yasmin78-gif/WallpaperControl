# Besuchermodus: Implementierung und Abnahme

## Ausgangspunkt und Git

Implementiert auf `codex/visitor-mode`, ausgehend von `d6ddbb5` (Release 2.3.1).
Das Arbeitsverzeichnis war vor Beginn sauber. Die Implementierung ist nach zentraler
Logik, Oberfläche, Widget, Hotkey und abschließender Absicherung gruppiert.
Es wurden keine NuGet-Abhängigkeiten oder UI-Frameworks hinzugefügt.
Der abgeschlossene Quellstand liegt in `f79bba9`; dieser Bericht wird separat
versioniert. Die Anwendung zielt weiterhin auf .NET 10. Der vorhandene lokale
SDK 11 Preview meldet beim Build lediglich den SDK-Vorschauhinweis.

## Bedienung

In der linken Navigation **Besuchermodus** öffnen. Als Quelle Einzelbild, MP4 oder
Medienordner wählen, die Quelle auswählen und **Speichern** drücken. Danach den
Schalter betätigen. Der Schalter verwendet immer die zuletzt gespeicherte Quelle.
Ohne gültige Quelle bleibt der Modus mit einem neutralen Hintergrund aktiv.

Der Ordner verwendet das eingestellte Intervall in Sekunden und wahlweise eine
zufällige Reihenfolge. Er wird vor jedem Wechsel neu eingelesen. Videos laufen
stumm in der vorhandenen Endlosschleife; im Ordner erfolgt der nächste Wechsel
nach dem Intervall, unabhängig von der Videolänge. Unterordner werden wie beim
bisherigen Bildordner nicht durchsucht.

Das Mini-Widget und seine Positionssperre werden auf derselben Seite aktiviert und
gespeichert. Den Hintergrund oder die Beschriftung des Widgets ziehen; der
Schalter selbst dient ausschließlich zum Umschalten. Die Position wird nach dem
Ziehen gespeichert. Das Widget verwendet die bestehende Desktop-Ebene und das
vorhandene Snapping. Es schwebt nicht über gewöhnlichen Anwendungsfenstern.

Für einen globalen Hotkey das Hotkey-Feld anklicken und eine Tastenkombination
mit Ctrl und/oder Alt, optional Shift, drücken. Entf oder Rücktaste entfernt die
Belegung. **Speichern** übernimmt die Kombination. Interne Duplikate werden
abgewiesen. Scheitert die native Registrierung, wird die vorherige Belegung
wiederhergestellt und eine Meldung angezeigt. Auch der allgemeine
Hotkey-Einstellungsdialog berücksichtigt das Besucherkürzel.
Gedrückthalten des Besucherkürzels löst keine automatischen Wiederholungen aus;
die Wiederholungsrichtlinie vorhandener Kürzel bleibt unverändert.

## Architektur und wiederverwendete Komponenten

`VisitorModeService` besitzt den einzigen Aktivierungszustand, den gespeicherten
Wiederherstellungszustand und die Änderungsbenachrichtigungen. Ein Semaphore
serialisiert Schalter-, Konfigurations- und Hotkeyaktionen über asynchrone Grenzen.
Ein Toggle wird erst innerhalb der Serialisierung ausgewertet, sodass schnelle
Hotkeyfolgen nicht zu verlorenen Umschaltungen führen.

`VisitorModeStore` speichert unter `HKCU\Software\WallpaperControl` ausschließlich
die neuen Werte `VisitorMode` (JSON) und `VisitorModeActive` (Sicherungsmarkierung).
Die Markierung wird vor dem Aktivieren gesetzt und erst nach erfolgreicher
Wiederherstellung gelöscht. Ist die JSON-Konfiguration bei gesetzter Markierung
unlesbar, startet die Anwendung sicher im Besuchermodus. Speicherfehler werden
protokolliert und auf der Seite angezeigt; ohne erfolgreichen Schreibzugriff
kann der Zustand für den nächsten Start nicht garantiert werden.

`MainForm.VisitorMode.cs` integriert den Dienst in die bestehende Wiedergabe:

- `WallpaperModeOwnership` erhält eine zusätzliche Besucherzuständigkeit. Neue
  Generationen entwerten bereits gestartete Bild- und Timeroperationen.
- Der bestehende `wallpaperModeChange`-Semaphore und die vorhandene Abbruchlogik
  beenden veraltete Video-Vorbereitungen und Bildübergänge vor dem nächsten Start.
- Der neutrale native Hintergrund wird vor asynchronem Aufräumen gesetzt.
  Bestehende Videofenster werden bei ihrer Freigabe vor langwieriger nativer
  Zerstörung verborgen.
- Bilder verwenden `WallpaperTransitionService` mit direkter Umschaltung.
- Videos verwenden denselben `VideoWallpaperController` und dieselben Backends
  wie normale Videos. Die Position wird beim Wiederherstellen vor der Präsentation
  angesprungen. Eine zweite Wallpaper-Engine wurde nicht implementiert.
- Besucherordner verwenden `WallpaperCandidateRunner`, die bestehende
  Bildformaterkennung und einen eigenen Timer sowie Zufallsgenerator. Dadurch
  verbraucht der Besucherordner keine Zufallswerte der normalen Bildauswahl.
- Der vorhandene Bildmodus-Snapshot wird für die Wiederherstellung verwendet.
  Fehler werden im Besucherpfad weitergegeben, sodass ein fehlgeschlagener
  Wiederherstellungsversuch den Besuchermodus nicht freigibt.
- `WidgetManager` verwaltet das neue WinForms-Widget einschließlich Lebensdauer,
  Desktop-Ebene, Positionssichtbarkeit und Snapping. `WidgetDragHandler` übernimmt
  das Verschieben. `VisitorToggleSwitch` wird in Hauptmaske und Widget verwendet.
- `GlobalHotkeyManager` registriert das optionale Kürzel über die vorhandene
  `WM_HOTKEY`-Verarbeitung. `AppLogger` und `SettingsPersistence` melden Fehler.

Normale Einstellungen, Wallpaperordner, Verlauf und Statistiken werden durch
Besuchermedien nicht ersetzt. Die normale Wallpaperseite ist während des Modus
gesperrt. Remote-Weiter, Scheduler, normale Modusauswahl und manuelle Bildwechsel
erhalten währenddessen keine Wiedergabezuständigkeit. Andere Widgets behalten
ihre Einstellungen; Weiter- und Wallpaperinfo-Widgets folgen der bestehenden
Modusabhängigkeit und werden ausgeblendet.

## Start, Ende und Wiederherstellung

Ein gespeicherter aktiver Modus reserviert bereits während der Konstruktion die
Wallpaperzuständigkeit und setzt den neutralen Hintergrund. Erst danach werden
normale Einstellungen geladen und später Besuchermedien gestartet. Der normale
Bildscheduler, normale Videostart und normale Desktop-Warm-up werden in diesem
Pfad nicht gestartet. Vollbildrichtlinie, Widgets und automatische Updateprüfung
bleiben angebunden.

Bei normalem Ausschalten werden Bild-/Videoquelle, manueller Pausenzustand und
die verbleibende Zeit des anwendungseigenen Bildtimers wiederhergestellt. Die
erste Frist liegt um diese Restzeit nach der Wiederherstellung; folgende Wechsel
verwenden wieder die vorhandene Uhrzeitausrichtung. Die Videoposition wird in der
vorhandenen Zeiteinheit des Controllers gespeichert. Laufende Vollbildpausen
bleiben unabhängig von der manuellen Pause bestehen.
War vor dem Besuchermodus ein normales Video aktiv, bleibt auch der darunterliegende
Bildmodus für ein späteres normales Umschalten von Video zurück zu Bildern erhalten;
dieser Zustand wird nach einem Neustart aus dem Besuchersnapshot rekonstruiert.

Beim expliziten Beenden im Besuchermodus wird ein neutraler statischer Hintergrund
gesetzt. Die normale Windows-Diashow wird nicht wiederhergestellt. Der aktive
Zustand und der ursprüngliche Wiederherstellungszustand bleiben gespeichert.

## Grenzen

- Vor dem Start des Prozesses kontrolliert Windows den Desktop. Die Anwendung
  kann Anmeldebildschirm, Windows-Themes, andere Wallpaperprogramme oder manuelle
  externe Desktopänderungen nicht sperren. Das sichere Beenden hinterlässt einen
  neutralen Hintergrund, ersetzt jedoch keine Windows-Anmelderichtlinie.
- Die vorhandene Videoengine unterstützt MP4 auf einem Monitor. Nicht unterstützte
  Besuchervideos auf mehreren Monitoren führen zum neutralen Ersatz; Bilder werden
  mit dem vorhandenen nativen Wallpaperpfad auf allen Monitoren angezeigt.
- Im laufenden Prozess bleibt die native Windows-Diashow-Sammlung als COM-Snapshot
  erhalten. Nach einem Prozessneustart kann sie nur aus dem gespeicherten normalen
  Ordner rekonstruiert werden. Windows bietet hier keine exakte Wiederherstellung
  der internen Zufallswarteschlange, nativen Restlaufzeit oder eines Video-Frames.
  Videoseeking unterliegt zusätzlich den Fähigkeiten des jeweiligen Decoders.
- Bei einer aktiven nativen Windows-Diashow lässt sich die genaue aktuelle
  Bildzuordnung aller Monitore nicht gleichzeitig mit ihrer internen Playlistposition
  erzwingen: `SetWallpaper` würde die Diashow stoppen. Für statische/pausierte Bilder
  werden die gespeicherten Monitorzuordnungen wiederhergestellt.
- Ist die ursprüngliche Datei beim Ausschalten verschwunden oder die Videowiedergabe
  nicht wiederherstellbar, bleibt der Modus mit neutralem Hintergrund aktiv.
- Die UI- und Medienintegrationstests verwenden isolierte Einstellungen und einen
  simulierten Videodecoder. Sie ersetzen keine Sichtprüfung echter Desktopausgabe,
  Decoder, Explorer-Neustarts oder eines Windows-Anmeldevorgangs.

## Automatisierte Prüfungen

```powershell
dotnet build WallpaperControl/WallpaperControl.csproj --no-restore
dotnet run --project WallpaperControl.RegressionTests -- --visitor-mode-checks
dotnet run --project WallpaperControl.RegressionTests -- --visitor-ui-checks
dotnet run --project WallpaperControl.RegressionTests -- --visitor-regression-checks
dotnet run --project WallpaperControl.RegressionTests
```

Die Besucherprüfungen verwenden temporäre Registry-Schlüssel und Mediendateien.
Sie setzen keine echten Desktop-Wallpaper und registrieren keine globalen Hotkeys.
Die UI-Sichtprüfung zeichnet ein vollständig transparentes Testfenster in ein
Bitmap; es werden keine Produktionspräferenzen gespeichert.

Der bereits vor der Implementierung beobachtete Fehler
`PackageTrackingTests.cs:89` / **Packages metadata fixture is persisted before refresh**
wurde nicht durch Anpassung oder Entfernung dieses Tests behandelt. Im ersten
vollständigen Lauf nach der Integration bestand er; insgesamt bestanden dort
5906 Prüfungen. Er bleibt als zuvor beobachteter, in diesem Lauf nicht
reproduzierter Ausgangsbefund dokumentiert.

Der abschließende vollständige Lauf auf dem unveränderten Quellstand `f79bba9`
bestand mit **5953 Prüfungen** und Exitcode 0. Das Protokoll heißt
`%TEMP%\WallpaperControl-Visitor-Validation\acceptance.log`.

Zusätzlich trat in einem Zwischenlauf `NoteReminderTests.cs:69` / **Changing the
schedule resets its delivery state** auf. Der unveränderte Test bestand danach
isoliert sowohl am Ausgangscommit (625 Prüfungen einschließlich vorangestellter
Videosuite) als auch mit Besuchermodus (670 Prüfungen im damaligen Zwischenstand).
Ein anschließender Gesamtlauf bestand mit 5951 Prüfungen. Eine Ursache für den
vorübergehenden Befund wurde nicht festgestellt; die Notizimplementierung und ihre
Tests wurden nicht geändert. Diese Beobachtung wird nicht als behobener Fehler
ausgegeben.

Ein während eines laufenden Testprozesses gestarteter Build wurde durch dessen
gesperrte DLLs blockiert (`MSB3021`/`MSB3027`). Nach Ende des Prozesses wurde seriell
erfolgreich gebaut. Dies war ein Ablaufproblem der Prüfung, kein Compilerfehler
im endgültigen Quellstand. Die abschließenden Prüfungen laufen seriell.

Prüfergebnisse:

| Prüfung | Ergebnis |
| --- | --- |
| Anwendung bauen | 0 Fehler, 0 Warnungen |
| Zentraler Besuchermodus, Persistenz und Hotkeyrichtlinie | 20 bestanden |
| Besucheroberfläche, Widget, Medienintegration und früher Startschutz | 27 bestanden |
| Vorhandene Videosuite | 402 bestanden |
| Kombinierte Besucher-/Scheduler-/Hotkey-/Einstellungs-/Vollbild-/Widgetregression, Zwischenstand | 2458 bestanden |
| Vollständige Regression, Zwischenstand | 5951 bestanden |
| Abschließende vollständige Regression auf `f79bba9` | **5953 bestanden**, Exitcode 0 |

Die generierten Prüfprotokolle und die UI-Vorschau liegen unter
`%TEMP%\WallpaperControl-Visitor-Validation`. Der vorübergehende Checkout zum
Vergleich mit `d6ddbb5` wurde nach der Prüfung wieder entfernt.

## Noch offene Abnahme

Die unten beschriebene reale Desktopprüfung wurde nicht durch automatisches
Umschalten des produktiven Benutzerdesktops ersetzt. Offen sind insbesondere die
Sichtprüfung mit echten Videodateien in beiden Backends, der Windows-Neustart mit
Autostart und die Prüfung an mehreren physischen Monitoren. Die Testoberfläche
wurde in ein Bitmap gerendert und visuell geprüft.

Die beiden vorübergehenden Speicher-/Testbefunde bei Paketen und Notizerinnerungen
sind nicht als behoben anzusehen. Falls sie erneut auftreten, ist ihre Ursache
gesondert zu untersuchen; die Besuchermodus-Änderung enthält keine Anpassungen an
Paket- oder Notizspeicherung.

## Manuelle Abnahme

1. Eine normale Bildsammlung aktivieren. Intervall, Zufallsoption, aktuelles Bild
   und Zeitpunkt des nächsten Wechsels notieren. Auch einmal manuell pausieren.
2. Auf **Besuchermodus** ein harmloses Bild auswählen und speichern. Einschalten:
   sofort nur dieses Bild beziehungsweise kurz den neutralen Hintergrund erwarten.
3. Währenddessen Weiter per Hauptmaske, Tray/Remote und bestehendem Hotkey versuchen.
   Über mindestens ein normales Wechselintervall warten. Kein normales Bild darf
   erscheinen. Anschließend ausschalten und Bild, Pause und Restzeit prüfen.
4. Ein kurzes MP4 als Besucherquelle speichern. Mehrere Schleifen abwarten;
   kein normales Medium darf zwischen den Schleifen sichtbar sein.
5. Eine normale Videowiedergabe starten, deren Position notieren und optional
   pausieren. Besuchermodus ein- und ausschalten. Wiederaufnahme an der gesicherten
   Position und Erhalt der manuellen Pause prüfen.
6. Einen Besucherordner mit mindestens zwei Bildern und einem MP4 konfigurieren.
   Kurzes Intervall einstellen, Reihenfolge und anschließend Zufallsmodus prüfen.
   Normale Ordner-/Intervall-/Audioeinstellungen danach vergleichen.
7. Mini-Widget einschalten und speichern. Beide Schaltrichtungen zwischen Hauptmaske
   und Widget prüfen. Widget auf einen zweiten Monitor ziehen, Anwendung neu starten
   und Position prüfen. Positionssperre und Snapping testen. Vorhandene Widgets prüfen.
8. Ein freies Besucherkürzel speichern. Mit anderer Anwendung im Vordergrund
   umschalten. Ein bereits verwendetes internes Kürzel in beiden Einstellungswegen
   versuchen; anschließend einen extern belegten Hotkey versuchen.
9. Kurz vor einem automatischen Bildwechsel sowie während Video-Vorbereitung
   umschalten. UI, Widget und Hotkey wiederholt schnell benutzen. Am Ende müssen
   Schalter, Anzeige und sichtbares Medium übereinstimmen.
10. Fehlende Datei, beschädigtes Bild/Video und leeren/gelöschten Ordner testen.
    Der Modus muss aktiv bleiben und neutral anzeigen. Quelle reparieren und
    erneut speichern. Auch einen Ordner mit defekter und gültiger Datei prüfen.
11. Aktiv im Besuchermodus explizit beenden. Der Desktop muss neutral bleiben.
    Neu starten, auch mit `--tray`: kein normales Wallpaper darf von der Anwendung
    eingeblendet werden. Danach ausschalten und den ursprünglichen Zustand prüfen.
12. Vollbildanwendung öffnen, Modus wechseln und Vollbild verlassen. Manuelle
    Pausen dürfen nicht aufgehoben werden. Windows-Neustart mit Autostart prüfen.
13. Mit mehreren Monitoren die Bildquelle und statische Bildwiederherstellung prüfen.
    Besucher-MP4 muss den sicheren Ersatz anzeigen. Einen Monitor entfernen und die
    sichtbare Wiederherstellung der Widgetposition prüfen.

## Dateien

Neu:

- `WallpaperControl/Services/VisitorModeService.cs`
- `WallpaperControl/Forms/MainForm/MainForm.VisitorMode.cs`
- `WallpaperControl/Forms/MainForm/MainForm.VisitorUi.cs`
- `WallpaperControl/Forms/VisitorToggleSwitch.cs`
- `WallpaperControl/Widgets/VisitorModeWidgetForm.cs`
- `WallpaperControl/Widgets/WidgetManager.VisitorMode.cs`
- `WallpaperControl.RegressionTests/VisitorModeTests.cs`
- `docs/visitor-mode.md`

Geändert:

- `Forms/MainForm.cs`: Besucherstart und sicheres Beenden.
- `Forms/MainForm/MainForm.Initialization.cs`: frühe Initialisierung und Startschutz.
- `Forms/MainForm/MainForm.Navigation.cs`: neue Kategorie und Präsentation.
- `Forms/MainForm/MainForm.Status.cs`: Status-/Recoverypfad für Besuchermedien.
- `Forms/MainForm/MainForm.Slideshow.cs`: normale Pause während Besuchermodus sperren.
- `Forms/MainForm/MainForm.VideoWallpaper.cs`: normale Moduswechsel sperren und
  strengere Bildwiederherstellung ausschließlich für den Besucherpfad.
- `Forms/MainForm/MainForm.Hotkeys.cs`, `MainForm.Settings.cs`, `Forms/SettingsForm.cs`
  und `Forms/SettingsForm/SettingsForm.Hotkeys.cs`: Besucherkürzel und Konfliktprüfung.
- `Services/GlobalHotkeyManager.cs`: gemeinsame Konflikterkennung.
- `Wallpaper/Video/WallpaperOperatingMode.cs`: zusätzliche Wiedergabezuständigkeit.
- `Wallpaper/Video/VideoWallpaperController.cs`: Position lesen und vor Präsentation
  wiederherstellen; bestehende Aufrufe behalten ihre Standardwerte.
- `Wallpaper/Video/MediaFoundationVideoDesktop.cs`: Videofenster vor Decoderabbau verbergen.
- `Widgets/WidgetManager.cs`: Start und Freigabe des Besucherwidgets.
- `Strings*.resx`: neue Texte für Deutsch, Englisch, Französisch, Spanisch und Japanisch.
- `WallpaperControl.RegressionTests/Program.cs`: neue gezielte Testeinstiege und
  Aufnahme der Besucherprüfungen in den vollständigen Lauf.
- `WallpaperControl.RegressionTests/README.md`: Aufruf und Isolation der neuen Prüfungen.
