# Besuchermodus: Implementierung und Abnahme

## Korrektur: Weißes Aufblitzen vor Besucherübergängen

Im Abnahmevideo `2026-11-09_11h10_18.mp4` ist vor einem Besucher-Bildwechsel eine
weiße Desktopfläche sichtbar. Der neue Besucherübergabepfad setzte das native
Wallpaper mit `ApplyNativeSelection`; dieser Pfad beendet ausdrücklich den
bestehenden Desktop-Host. Die anschließende Animation musste ihn neu initialisieren.
Dabei wurde der leere Host bereits angezeigt und aktualisiert, bevor `currentFrame`
geladen war. Der leere Fensterhintergrund konnte so kurz sichtbar werden.

Die Korrektur erhält den bisherigen sicheren Frame und denselben Host:

1. Vor dem nativen Bildwechsel wird ein Renderer mit dem alten sicheren Bild
   vorbereitet und dessen bestehende Kompositionsbereitschaft abgewartet.
2. Das neue sichere BMP wird dauerhaft gespeichert und als natives Windows-Bild
   gesetzt, während der fertige alte Frame es abdeckt. Der neue eng begrenzte
   `ApplyCoveredNativeSelection`-Pfad prüft den bereiten Host, behält ihn bei und
   bestätigt nach dem Windows-Aufruf seine Desktopplatzierung erneut.
3. Die bestehende Transition-Engine animiert im gleichen Host zum neuen sicheren
   Bild. Wiederholtes Weiter schließt/öffnet den Host nicht mehr.
4. Beim Beenden beziehungsweise beim sicheren direkten Ersatz bleibt der bisherige
   Ablauf erhalten: natives Bild zuerst, dann Host freigeben.

Auch ein erstmals angelegter Host bleibt beim Anheften und Größenanpassen mit
Alpha 0 transparent. Erst nachdem das erste decodierte Bitmap vollständig gemalt
wurde, wird er mit Alpha 255 sichtbar. Dies gilt auch für normale Rendererstarts.
Fehlgeschlagenes Laden hinterlässt damit kein sichtbar leeres Hostfenster.

Die dauerhafte native Absicherung und die begrenzten sicheren BMP-Slots bleiben
erhalten. Bei fehlendem Renderer wird weiterhin der sichere direkte Ersatzpfad
verwendet. Beim Abbruch während der Vorbereitung bleibt die vorherige tatsächlich
angezeigte Kopie unverändert; ein noch unangezeigtes Ziel wird nicht veröffentlicht.

Die fokussierten Prüfungen laufen mit
`dotnet run --project WallpaperControl.RegressionTests -- --visitor-flash-checks`.
Sie verwenden den tatsächlichen MainForm-/Handoff-/TransitionManager-Pfad mit
isolierten Einstellungen, echten Bildkopien und einem simulierten Desktop-Host.
Sie prüfen verzögerte Kompositionsbereitschaft, Reihenfolge von nativer Absicherung
und Animation, Wiederverwendung des Hosts und Beenden während der Vorbereitung.
Sie ändern weder das echte Windows-Wallpaper noch die Monitoranordnung. Die
physische GPU-/DWM-Ausgabe muss zusätzlich manuell abgenommen werden.

Validierung: 11 neue Ablaufprüfungen bestanden; 2767 kombinierte Besucher-/
Wiedergabe-/Widgetprüfungen und 402 Videoprüfungen bestanden. Nach der abschließenden
Absicherung des übernommenen nativen Aufrufs wurden die 11 Ablaufprüfungen sowie
29 Übergabe-/Neustartprüfungen und 29 Wiedergabe-/Kopierprüfungen erneut erfolgreich
ausgeführt. Die Protokolle liegen unter `artifacts/visitor-flash-review/*.log`.

Abnahmeausgabe: `artifacts/visitor-flash-review/WallpaperControl.exe`, weiterhin
Version 2.5.0 als eigenständige Win-x64-Einzeldatei. Vor dem Ersetzen oder Starten
die bisherige Instanz vollständig über **Beenden** schließen. Zuerst den ersten
Weiterklick nach Aktivierung, dann wiederholtes Weiter/Zurück und automatische
Wechsel prüfen. Zusätzlich während der Vorbereitung/Animation beenden und neu
starten: Das sichere statische Bild und der aktive Besuchermodus müssen erhalten
bleiben. Neue Initialisierung darf kurz das bisherige Bild länger stehen lassen,
aber kein weißes oder leeres Fenster zeigen.

## Besucherordner steuern und normale Bilder übernehmen

Abnahmestand dieser Erweiterung: **2.5.0** auf `codex/visitor-slideshow-controls`.
Die Veröffentlichung 2.4.0 und ihre Release Notes bleiben als Historie unverändert.

Ist als Besucherquelle ein **Ordner** gewählt, bedienen die vorhandenen Regler auf
der Wallpaper-Hauptseite jetzt die Besucherdiashow: Intervall, Zufallsreihenfolge,
Pause/Fortsetzen, Anheften sowie Weiter/Zurück über die vorhandenen Aktionspfade.
Das aktivierte Weiter-Widget bleibt für diesen Modus sichtbar; bei Besucher-Einzelbild
und Einzelvideo bleibt es verborgen. Die Hauptseite zeigt den Besucherordner und
das aktuelle Besuchermedium an. Aussortieren, Rückgängigmachen und der normale
Wallpaperverlauf bleiben im Besuchermodus gesperrt.

Der Besucherordner hat einen eigenen manuellen Pausenzustand und eine monotone
Restzeitmessung. Eine Pause friert die Restzeit ein; Fortsetzen startet mit dieser
Restzeit. Vollbildpause bleibt ein unabhängiger Grund. Ein manueller Weiterwechsel
setzt die laufende Zeitfrist nicht zurück. Ändern des Intervalls startet eine neue
Frist, Ändern der Zufallsreihenfolge lädt kein Bild neu. Diese Einstellungen und
die Besucher-Pause werden gespeichert; die laufende Restzeit ist pro Prozess.
Der ursprüngliche normale Wiederherstellungssnapshot bleibt unverändert.

Darstellung, Übergangseffekt, Richtung und Dauer verwenden die vorhandenen gemeinsamen
Darstellungseinstellungen. Bei Bild-zu-Bild-Wechseln im Besucherordner nutzt die
vorhandene Transition-Engine ausschließlich bereits geprüfte sichere BMP-Kopien.
Das neue sichere native Wallpaper wird vorher hinterlegt, sodass ein Prozessabbruch
auch während eines Übergangs keinen Rückfall auf die normale Sammlung auslöst.
Bei nicht unterstützter Monitor-/Darstellungskonfiguration bleibt der vorhandene
direkte native Ersatzpfad wirksam. Videoübergänge bleiben bei der bisherigen
Videoengine; deren Einmonitorgrenze wird nicht verändert.

Im normalen Bildmodus erscheint **In Besucherordner kopieren** ausschließlich,
wenn tatsächlich eine vorhandene Ordnerquelle für Besucher konfiguriert ist.
Einzelbild, Einzelvideo und fehlende Ordner bieten diese Aktion nicht an.
Die Aktion kopiert das bei Betätigung aktuelle Originalbild, verändert oder
verschiebt das Original nicht und überschreibt keine vorhandene Datei. Bei
Namenskollisionen wird ein nummerierter Dateiname verwendet. Unvollständige Kopien
haben eine temporäre, für die Diashow ausgeschlossene Erweiterung; erst die
vollständige Datei wird atomar unter ihrem Bildnamen veröffentlicht. Eine bereits
im Zielordner liegende Datei wird nicht erneut dupliziert. Im Besuchermodus selbst
ist die Kopieraktion ausgeblendet und zusätzlich im Ausführungspfad gesperrt.

Neue fokussierte Prüfung:
`dotnet run --project WallpaperControl.RegressionTests -- --visitor-playback-checks`.
Sie verwendet isolierte Widgeteinstellungen, lokale Bildfixtures und deaktivierte
native Wallpaper-Schreibzugriffe der bestehenden Testinitialisierung.

Validierung dieser Erweiterung:

- 29 fokussierte Besucher-Wiedergabe-/Kopierprüfungen bestanden.
- 2754 kombinierte Besucher-/Wiedergabe-/Widgetprüfungen bestanden; anschließend
  wurden die abschließenden UI-Ergänzungen mit 1654 Layout-/Besucherprüfungen
  und erneut 29 Übergabe-/Neustartprüfungen erfolgreich kontrolliert.
- 402 bestehende Videoprüfungen und 21 Positionswiederherstellungsprüfungen bestanden.
- Release-Build erfolgreich mit 0 Warnungen und 0 Fehlern; eigenständige
  Win-x64-Einzeldatei mit Dateiversion 2.5.0.0 veröffentlicht (lokale Abnahmeausgabe).
- Der zunächst gefundene Überlappungsfehler neuer, ausgeblendeter Steuerelemente
  wurde durch explizit leere Bounds behoben; bestehende Tests wurden nicht gelockert.
- Kein erneuter vollständiger globaler Paket-/Notiz-/Netzwerkregressionslauf;
  diese Ergebnisse beziehen sich auf die genannten fokussierten Suites.

Die Protokolle liegen unter `artifacts/visitor-playback-*.log`.
Die eigenständige Abnahmeausgabe liegt unter
`artifacts/visitor-slideshow-review/WallpaperControl.exe`. Die bisherige Instanz
vor dem Start vollständig über **Beenden** schließen. Diese Ausgabe enthält die
verwalteten Abhängigkeiten und die .NET-Laufzeit in der EXE; das unveränderte externe
Videopaket bleibt wie beim bestehenden Installer erforderlich. Für einen Austausch
der EXE in einer vollständigen 2.4.0-Installation kann diese Einzeldatei verwendet
werden. Die laufende Installation wurde nicht automatisch ersetzt.

Manuelle Abnahme:

1. Besucherordner mit mehreren Bildern einstellen und aktivieren. Besucherpfad
   und Dateiname auf der Hauptseite prüfen; Weiter-Widget einblenden und benutzen.
2. Intervall und Zufallsreihenfolge ändern: Das aktuelle Bild bleibt erhalten,
   der normale Wiederherstellungssnapshot wird nicht verändert.
3. Pausieren, kurz warten, fortsetzen: Restzeit prüfen. Vollbildpause zusätzlich
   prüfen; manuelles Fortsetzen darf eine Vollbildpause nicht aufheben.
4. Übergänge, Richtung, Dauer und Bilddarstellung ändern. Während eines Übergangs
   deaktivieren beziehungsweise beenden: Nur sichere Besucherbilder dürfen vor
   der expliziten normalen Wiederherstellung erscheinen.
5. Aussortieren und Rückgängigmachen bleiben gesperrt. Besuchermodus deaktivieren:
   ursprüngliche Quelle, Pause und Timerrestzeit müssen wiederhergestellt werden.
6. Im normalen Modus ein Bild in den Besucherordner kopieren, Namenskollision
   wiederholen und Originaldatei prüfen. Bei Einzelbild-/Einzelvideoquelle und
   ungültigem Ordner darf die Kopieraktion nicht erscheinen.

## Optionaler Widget-Titel

Unter **Widgets → Besuchermodus → Titel anzeigen** lässt sich der Titel ausblenden.
Dann wird ausschließlich der Toggle gezeichnet; Titel, Kartenhintergrund und deren
Platzbedarf entfallen. Bei 100 % Größe und 96 DPI beträgt die Fenstergröße
58 × 32 Pixel einschließlich eines kleinen transparenten Randes. Benutzerskalierung
und Monitor-DPI skalieren Größe und Trefferfläche gemeinsam. Die Titelschriftgröße
vergrößert das kompakte Widget nicht. Zugänglicher Name und Status-Tooltip bleiben
erhalten.

Die Einstellung wird über die bestehenden Widgetfunktionen live angezeigt,
gespeichert und bei **Verwerfen** zurückgesetzt. Bestehende Konfigurationen zeigen
den Titel weiterhin an. Ohne Titel kann das entsperrte Widget direkt am Toggle
verschoben werden; Ziehen schaltet den Besuchermodus nicht um. Die Positionssperre
bleibt wirksam.

Manuelle Abnahme: Titel ausschalten, kompakte Darstellung und Klick prüfen,
Widget am Toggle verschieben, sperren, speichern und Anwendung neu starten.
Anschließend Titel einschalten und ursprüngliche Gestaltung prüfen.
Neue Release-Ausgabe: `artifacts/visitor-mode-widget-title-review/WallpaperControl.exe`.

Validierung: **2725 kombinierte Besucher-/Wiedergabe-/Widgetprüfungen bestanden**;
anschließend nach Ergänzung der kompakten Drag-Behandlung **82 Besucherprüfungen
bestanden**, einschließlich der neuen Klick-/Dragprüfungen. Release-Build und
Publish erfolgreich, **0 Warnungen, 0 Fehler**. Protokolle:
`%TEMP%\WallpaperControl-Visitor-Validation\widget-title-regression.log` und
`widget-title-final.log`. Der vollständige 6071er-Lauf im folgenden Abschnitt
bezieht sich auf den vorherigen Wallpaper-Übergabestand.

## Sicheres statisches Wallpaper bei Beenden, Absturz und Prozessabbruch

Diese Ergänzung auf `codex/visitor-mode` ersetzt das bisherige Verhalten, beim
Beenden stets den neutralen Hintergrund zu setzen. Der frühere Shutdown-Pfad
rief vor dem Stoppen des Videos `ShowVisitorNeutral` auf. Jetzt werden zunächst
Besucherwechsel und laufende Vorbereitungen beendet beziehungsweise abgebrochen,
danach wird das sichere Standbild nativ an Windows übergeben und erst anschließend
werden Videoengine und Desktop-Host freigegeben. Späteres Dispose überschreibt
dieses Bild nicht. Ohne Besuchermodus bleibt der bisherige Beenden-Pfad erhalten.

Ausgangspunkt: sauberer Stand `cc3dfef`; Implementierung mit Regressionstests:
`e8ba09b`. Die nachfolgenden älteren Abschnitte dokumentieren vorherige Abnahmen.

### Bildwahl und dauerhafte Ablage

- Bilder werden schon beim Anzeigen als eigene BMP-Kopie gespeichert. Beim Beenden
  bleibt diese tatsächlich angezeigte Kopie erhalten, auch wenn die Quelldatei
  inzwischen verändert oder gelöscht wurde. Im Ordner zählt das zuletzt erfolgreich
  angezeigte Medium; ein noch vorbereiteter Wechsel darf es nicht ersetzen.
- Für Videos wird bevorzugt der aktuelle decodierte Videoframe verwendet. Media
  Foundation nutzt den vorhandenen EVR-Readback, mpv `screenshot-raw video` auf
  seinem bestehenden nativen Worker. Es wird kein Desktop-Screenshot angefertigt.
  Der gepinnte minimale mpv-Build benötigt dadurch keinen zusätzlichen Bildencoder.
- Falls das nicht gelingt, versucht der Windows-Shell-Thumbnailanbieter ein
  Standbild ausschließlich aus dem Besuchervideo zu gewinnen. Dateisymbole sind
  ausgeschlossen. Danach folgt ein vorhandener sicherer Frame desselben Videos
  oder der neutrale Ersatz. Normale/private Wallpaperquellen werden nicht gesucht.
- Standbilder liegen unter `%LOCALAPPDATA%\WallpaperControl\VisitorMode`.
  Drei begrenzte BMP-Slots (`safe-0.bmp` bis `safe-2.bmp`) und der atomar ersetzte
  Zeiger `last-safe.txt` verhindern das Überschreiben des gerade nativen Bildes.
  Die Ablage liegt außerhalb des Programm-/Abnahmeordners und bleibt bei Neustart
  und Releasewechsel bestehen. Temporäre Frame-Dateien werden bereinigt.
- Die native Übergabe verwendet den vorhandenen Windows-Wallpaperpfad für alle
  Monitore sowie die vorhandene Behandlung eigener Wallpaperänderungen. Der Pfad
  wird auch beim späteren Wiederherstellen als eigener Vorgänger berücksichtigt.

Die Framevorbereitung hat je Versuch eine Frist von zwei Sekunden; verspätete Ergebnisse
dürfen ausschließlich ihre temporäre Datei schreiben und keinen neuen nativen
Hintergrund mehr veröffentlichen. Ein blockierter nativer Treiberaufruf selbst
lässt sich damit nicht zwangsweise unterbrechen.

### Absturz, Task-Manager und Windows-Neustart

Da beim harten Prozessende kein Shutdown-Code laufen kann, wird die native
Absicherung bereits während des Betriebs geschrieben: Bilder sofort, Videos nach
dem ersten erfolgreichen Start und anschließend im Abstand von 15 Sekunden.
Ein reguläres Beenden versucht zusätzlich einen frischen Frame vor dem Freigeben
der Wiedergabe. Bei Absturz oder erzwungenem Prozessende bleibt der letzte
erfolgreich an Windows übergebene sichere Stand erhalten. Das ist keine Garantie
für den exakten letzten sichtbaren Videoframe oder eine maximale Frame-Altersgrenze
bei Aufnahmefehlern. Vor der allerersten erfolgreichen nativen Übergabe ist diese
Absicherung noch nicht gegeben; ein Stromausfall hat zusätzliche Windows-/Datenträgergrenzen.

Windows verwaltet dieses statische Wallpaper selbst, auch vor dem nächsten
Programm-/Autostart nach einem Windows-Neustart. Der aktive Besuchermodus und
der ursprüngliche normale Wiederherstellungszustand werden beim Beenden nicht
verändert. Beim Programmstart wird die sichere Kopie bereits vor dem Start der
Medienwiedergabe verwendet. Späteres Deaktivieren verwendet weiterhin den
gespeicherten normalen Zustand und seine bisherigen Wiederherstellungsgrenzen.
Windows-Themes, Spotlight, Richtlinien und andere Programme können das native
Wallpaper außerhalb der Laufzeit überschreiben; die Anwendung kann dies vor
ihrem Start nicht verhindern. Anmelde-/Sperrbildschirm sind davon unabhängig.

### Prüfung und manuelle Abnahme

Neue isolierte Übergabetests: **29 bestanden** (`--visitor-handoff-checks`).
Sie prüfen unter anderem Quelländerung/-verlust, letzten Ordnerinhalt, Abbruch
eines laufenden Wechsels, Veröffentlichung vor Videodispose, verspätete Ergebnisse,
ungültige Medien, mehrere simulierte Monitore sowie gespeicherten Neustart und
anschließende Wiederherstellung mit 23 Sekunden Timerrestzeit.
Ein eigener Kindprozess wird nach erfolgreichem Speichern tatsächlich erzwungen
beendet; sein simuliertes natives Profil, sichere Bilddaten und Besuchersnapshot
bleiben erhalten. Dabei wird das echte Desktop-Wallpaper nicht verändert.

Zusätzlich bestehen **2 native Medienprüfungen** mit dem tatsächlich gepinnten
mpv-DLL und dem installierten Windows-Thumbnailanbieter. Beide liefern die blauen
Pixel eines Test-MP4, kein Symbol. Diese Tests verwenden unsichtbare Testfenster;
physische Monitore und ein echter Windows-Neustart bleiben manuell zu prüfen.
Das vorhandene FFmpeg wurde nur zur Erzeugung der Testdatei verwendet und ist
keine neue Laufzeitabhängigkeit.

Reproduzierbare Einstiege (PowerShell im Repository):

```powershell
dotnet build WallpaperControl/WallpaperControl.csproj -c Release --no-restore
dotnet run --project WallpaperControl.RegressionTests -- --visitor-handoff-checks
dotnet run --project WallpaperControl.RegressionTests -- --visitor-handoff-native-checks <Test-MP4> <absoluter-Pfad-zur-gepinnten-libmpv-2.dll>
dotnet run --project WallpaperControl.RegressionTests
dotnet publish WallpaperControl/WallpaperControl.csproj -c Release --no-restore -o artifacts/visitor-mode-handoff-review
```

Release-Build: **0 Warnungen, 0 Fehler**, Publish erfolgreich. Die DLL der
Abnahmeausgabe stimmt per SHA-256 mit der Release-Buildausgabe überein
(`2576CE254185B28F3C8772D4938657C1BA1AB42EB2509B1C84CFA80BBB4D6A4A`).

Abschließender vollständiger Regressionstest für `e8ba09b`: **6071 bestanden**,
Exitcode 0. Er umfasst auch die bestehenden Video-, Scheduler-, Hotkey-, Widget-,
Status- und Remote-Prüfungen. Die zusätzlich gestarteten nativen Medienprüfungen
sind darin nicht enthalten. Der früher dokumentierte Fehler in
`PackageTrackingTests.cs:89` trat in diesem Lauf nicht auf. Paket-/Notiztests und
ihre Assertions wurden für diese Änderung nicht angepasst.
Das vollständige Protokoll liegt unter
`%TEMP%\WallpaperControl-Visitor-Validation\handoff-final-complete.log`.

Für die Abnahme die bisherige Instanz vollständig über **Beenden** schließen und
`artifacts/visitor-mode-handoff-review/WallpaperControl.exe` starten:

1. Besucher-Einzelbild einschalten, explizit beenden: dasselbe Bild bleibt sichtbar.
   Mit einem Ordner wiederholen und während eines Bild-/Videowechsels beenden.
2. Besucher-MP4 einige Sekunden abspielen, beenden: ein sicherer Videoframe bleibt.
   Mit fehlender/beschädigter Quelle muss ein sicherer Ersatz bleiben.
3. Mit aktivem Bild und Video den Prozess im Task-Manager beenden. Beim Video
   vorher mindestens einen erfolgreichen Frame-Checkpoint abwarten (15 Sekunden).
   Der zuletzt gespeicherte sichere Stand bleibt ohne Shutdown sichtbar.
4. Windows neu starten und den Desktop bereits vor dem Programm-/Autostart prüfen.
   Danach müssen Besuchermodus und sichere Wiedergabe weiter aktiv sein.
5. Besuchermodus deaktivieren: ursprüngliches Wallpaper, manuellen Pausenzustand,
   Timerrestzeit und gegebenenfalls Videoposition prüfen. Keine private Quelle
   darf vorher eingeblendet werden.
6. Auf mehreren physischen Monitoren prüfen: statisches Bild bleibt überall.
   Die unveränderte Live-Videoengine unterstützt nur einen Monitor; beim Beenden
   kann auch ohne gestartete Videoengine ein sicheres Video-Thumbnail übergeben werden.
7. Ohne Besuchermodus beenden und bestehende Video-, Hotkey-, Widget-, Scheduler-
   und Remote-Funktionen prüfen.

Technische Referenzen: [mpv screenshot-raw](https://mpv.io/manual/stable/#screenshot-raw)
und [Windows IShellItemImageFactory::GetImage](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage).

## Bugfix: Diashow bleibt nach dem Besuchermodus pausiert

Produktiver Quellstand: `24f2b79`, Branch `codex/visitor-mode`.

### Tatsächliche Ursache

Der ursprüngliche manuelle Pausenzustand wurde korrekt im `ImageModeSnapshot`
gespeichert und wiederhergestellt. Er wurde jedoch anschließend überschrieben:
`GetCurrentWallpaperPath()` verglich den nativen Windows-Pfad mit dem beim Start
des wiederhergestellten Renderers erwarteten Originalpfad. Windows kann nach
`SetWallpaper` noch das Besucherbild oder den neutralen Zwischenhintergrund melden.
Diese eigene, noch ausstehende Änderung wurde als externe Wallpaperänderung
interpretiert. Der vorhandene externe Änderungszweig beendete den Renderer und
setzte `slideshowPaused = customSlideshowEngineActive || slideshowPaused` auf true.
Die Hauptseitenanzeige zeigte diesen nachträglich gesetzten Zustand korrekt an;
die Ursache lag weder im Schalter noch in einer vergessenen Besuchermodus-Pause.

### Gezielte Korrektur

- Beim Wiederherstellen erhält die bestehende Desktop-Session die bekannten
  vorherigen Besucher-/Neutralpfade, die native Zuordnung aus dem Snapshot sowie
  den unmittelbar vor dem eigenen `SetWallpaper` gelesenen Windows-Pfad.
- Solange Windows den neuen Originalpfad noch nicht bestätigt hat, werden nur
  diese bekannten Vorgänger als eigene ausstehende Übergänge behandelt. Der
  Originalrenderer bleibt sichtbar; Pause und verbleibende Timerfrist bleiben
  unverändert. Es wird weder `ResumeSlideshow` pauschal aufgerufen noch ein
  zusätzlicher Wallpaperwechsel ausgelöst.
- Ein beliebiger anderer externer Pfad wird weiterhin sofort erkannt. Sobald
  Windows den Originalpfad meldet, endet die Übergangsbehandlung. Sie ist außerdem
  auf höchstens fünf Sekunden begrenzt, gemessen mit einer monotonen Uhr. Dies ist
  keine Wartezeit und kein neuer Slideshow-Timer, sondern eine eng begrenzte
  Zuordnung der noch ausstehenden eigenen nativen Schreiboperation.
- Die Zuordnung wird beim nächsten Sessionstart beziehungsweise Shutdown ersetzt
  oder verworfen. Normale Sessions ohne Besucherwiederherstellung verwenden die
  bisherige externe Änderungserkennung.
- Diagnoseabfragen beobachten diese Zuordnung ohne sie zu bestätigen oder zu
  verbrauchen. Damit verändert das Protokollieren keinen Wiedergabezustand.

Die native Abfrage und ihre Wiedergabefolgen teilen jetzt
`ReconcileNativeWallpaper`. Regressionstests durchlaufen damit auch den tatsächlichen
Zweig, der vorher den manuellen Pausenzustand überschrieben hat. Die bisherigen
Tests hatten nur den Zustand unmittelbar nach der Wiederherstellung geprüft.

### Regressionen und erneute manuelle Prüfung

Die erweiterte Besucherprüfung besteht mit **74 Prüfungen**. Sie umfasst aktive
und manuell pausierte Diashow, unveränderte Restzeit, sofort sichtbares Original,
Hauptseitenstatus, wiederholte schnelle Umschaltungen ohne zusätzlichen Bildwechsel,
unabhängige Vollbildpause und weiterhin wirksame echte externe Änderungen.
Eine kontrollierte monotone Testuhr prüft Ablauf und Bestätigung des nativen
Übergangs ohne reale Wartezeiten. Der kombinierte Regressionseinstieg führt nun
auch die bestehenden Renderer-Stabilisierungs-/Auditprüfungen aus.

Der erste vollständige Lauf brach am bereits dokumentierten, unveränderten
Pakettest `PackageTrackingTests.cs:89` / **Packages metadata fixture is persisted
before refresh** ab. Dieser Befund wird separat behandelt; die Paketspeicherung
und ihre Tests wurden nicht verändert.
Ein zweiter vollständiger Lauf brach in der ebenfalls unveränderten Prüfung
`NoteReminderTests.cs:67` / **Stale editor cannot erase delivery or snooze state**
ab. Der anschließende separate Erinnerungslauf besteht mit **746 Prüfungen**.
Der vollständige Gesamtlauf wird für diesen Stand deshalb nicht als durchgängig
grün ausgewiesen. An Paket-/Notizspeicherung und ihren Assertions wurde nichts geändert.

| Prüfung dieses Bugfixes | Ergebnis |
| --- | --- |
| Erweiterte Besucher-/Pauseprüfung | 74 bestanden |
| Kombinierte Besucher-/Scheduler-/Hotkey-/Vollbild-/Widget-/Rendererregression | 2690 bestanden |
| Vorhandene Videosuite | 402 bestanden |
| Separater Erinnerungslauf | 746 bestanden |
| Release-Build und Publish | erfolgreich |

Protokolle unter `%TEMP%\WallpaperControl-Visitor-Validation`:
`pause-checks.log`, `pause-focused.log`, `pause-video.log`, `pause-reminders.log`,
`pause-full.log` und `pause-final.log`.

Die neue Anwendung liegt unter
`artifacts/visitor-mode-pause-review/WallpaperControl.exe`.
Die bisherige Instanz vollständig über **Beenden** schließen, dann diese EXE aus
ihrem vollständigen Veröffentlichungsordner starten. Die ältere Review-Ausgabe
wird dabei nicht überschrieben.

1. Aktive Diashow: Bild und Restfrist notieren, Besuchermodus ein-/ausschalten.
   Originalbild sofort sichtbar; die Diashow darf nicht als manuell pausiert
   erscheinen und muss nach der verbleibenden Frist weiterschalten. Den Status
   zusätzlich mindestens zehn Sekunden nach dem Ausschalten beobachten.
2. Manuell pausierte Diashow: dieselbe Folge prüfen. Die Pause muss erhalten bleiben.
3. Beide Fälle mehrfach schnell wiederholen; kein zusätzlicher Bildwechsel beim
   Ausschalten und keine nachträglich erscheinende manuelle Pause.
4. Vollbildpause vor dem Umschalten aktivieren. Diese Pause darf beim Ausschalten
   des Besuchermodus nicht aufgehoben werden.
5. Video einschließlich Position/Pause sowie Hotkey und Widget kurz gegenprüfen.

Geändert: `MainForm.VisitorMode.cs`, `MainForm.Scheduler.cs`,
`MainForm.Wallpaper.cs`, `MainForm.WallpaperDiagnostics.cs`,
`PersistentDesktopTransitionManager.cs`, `VisitorRevisionTests.cs` und der
kombinierte Testeinstieg in `Program.cs`. `VisitorModeService`, das gespeicherte
Pausenformat und die Video-/Hotkey-/Widgetimplementierungen bleiben unverändert.

## Nachbesserung nach manueller Abnahme

Quellstand: `a48bc52` auf dem bestehenden Branch `codex/visitor-mode`.

### Ursache der verzögerten Rückkehr

Die Wiederherstellung setzte über `SetWallpaper` das ursprüngliche native Bild
und startete danach den bestehenden Bildscheduler. Dieser las den Windows-Pfad
erneut über `GetCurrentWallpaperPath()`. Windows kann zu diesem Zeitpunkt noch
das Besucherbild melden. Dadurch initialisierte der persistente Desktop-Host das
Besucherbild und überdeckte die native Wiederherstellung bis zum nächsten Wechsel.

`RestoreImageModeCore` übergibt jetzt den Snapshot-Pfad direkt an
`StartCustomSlideshowEngineFromPath`. Die vorhandene Rendererinitialisierung zeigt
dadurch unmittelbar das Originalbild. Es wird kein zusätzlicher Playlistwechsel
ausgelöst. Die Restzeit wird anschließend wieder eingesetzt. Normale Timer und
Weiter-Kommandos bleiben bis zur vollständigen Deaktivierung gesperrt.

Die neue Regression simuliert einen weiterhin auf das Besucherbild zeigenden
Windows-Pfad und prüft den tatsächlich initialisierten Host-Pfad, jeweils mit
laufender und pausierter Diashow. Sie prüft außerdem die Restzeit und das Ausbleiben
eines zusätzlichen Übergangs. Die bisherigen isolierten Tests hatten lediglich
den Zustand geprüft und die Desktop-Initialisierung übersprungen.

### Neue Bedienung und Widgetintegration

- Der Besucherschalter liegt auf **Wallpaper**, oberhalb der Diashowkarten.
  Er erscheint nach dem Speichern einer verfügbaren Quelle. Eine ungespeicherte
  Auswahl reicht nicht. Im aktiven Modus bleibt der Ausschalter auch bei einem
  zwischenzeitlichen Quellenfehler erreichbar.
- Die Seite **Besuchermodus** enthält Quelle, Intervall, Reihenfolge, Hotkey und
  Status; keinen zweiten Schalter und keine Widgetoptionen.
- **Widgets → Besuchermodus** enthält Sichtbarkeit, Positionssperre, Stil,
  Skalierung, Schriftgröße, Hintergrunddeckung und Farbauswahl. Vorschau,
  Speichern, Verwerfen und Standardwerte verwenden die bestehende Widgettransaktion.
  Widgetänderungen starten die Besuchermedien nicht neu.
- `WidgetSettings.Visitor` besitzt die Widgetkonfiguration, gespeichert als
  `VisitorWidgetConfiguration` im bisherigen Registry-Schlüssel. Alte Sichtbarkeits-,
  Sperr- und Positionswerte aus `VisitorMode` werden übernommen, solange die neue
  Konfiguration noch fehlt. Medien und Aktivierungszustand bleiben beim Dienst.
- Das kompakte Widget verwendet `WidgetHeader`, `WidgetDrawing.GetPalette`, die
  gemeinsamen abgerundeten Formen, `LayeredWidgetBitmap`, `WidgetDragHandler` und
  Snapping. Kopf und Schalter stehen in einer Zeile. Eine zusätzliche Statuszeile
  entfällt; Details stehen im Tooltip. Die Standardfläche ist rund ein Drittel
  kleiner als zuvor (248×72). Größere Schrift und Skalierung vergrößern das gemessene
  Layout. Die Hintergrundtransparenz verändert weder Schrift noch Schalter.

### Prüfung der Nachbesserung

Die **36** gezielten Prüfungen unter `--visitor-revision-checks` und die bisherigen
**27** Besucher-UI-/Medienprüfungen bestehen. Der Release-Publish ist erfolgreich.
Hauptseite, Widgeteditor und alle drei Widgetstile wurden gerendert und visuell geprüft.
Die kombinierte Besucher-/Scheduler-/Hotkey-/Einstellungs-/Vollbild-/Widgetregression
unter `--visitor-regression-checks` besteht mit **2513 Prüfungen**.
Der abschließende vollständige Lauf auf dem unveränderten Quellstand `a48bc52`
bestand mit **6004 Prüfungen**, Exitcode 0. Protokoll:
`%TEMP%\WallpaperControl-Visitor-Validation\revision-final.log`.

Ein Gesamtlauf traf auf die veraltete Erwartung von 15 Widgetseiten. Ausschließlich
diese Navigationsassertion in `NotesTests.Settings` wurde auf 16 Seiten einschließlich
`VisitorTitle` erweitert; ebenso die erwartete Liste in `WidgetNavigationTests`.
Es wurden keine Speicherassertions abgeschwächt oder entfernt.

Ein weiterer Gesamtlauf brach in der unveränderten Speicherprüfung
`NotesTests.Storage:69` / **Notes reopen clears completion timestamp** ab. Der separate
Notizlauf bestand anschließend mit **986 Prüfungen**. Dieser vorübergehende Befund
ist von der behobenen Darstellung und der erforderlichen Navigationsanpassung zu
trennen. Die Notizspeicherung wurde nicht verändert.

### Anwendung für die erneute Abnahme

Der Release-Publish liegt unter `artifacts/visitor-mode-review/WallpaperControl.exe`.
Vor dem Start die bisherige Instanz **vollständig über Beenden schließen**;
Schließen zur Taskleiste reicht wegen der Einzelinstanzlogik nicht aus. Die EXE
direkt aus dem veröffentlichten Ordner mit seinen Bibliotheken starten.
Der Publish wurde mit `dotnet publish WallpaperControl/WallpaperControl.csproj
-c Release --no-restore -o artifacts/visitor-mode-review` erstellt. Es wurden keine
neuen Pakete oder Frameworks hinzugefügt. `ABNAHME.txt` im Ausgabeordner enthält
eine kurze Startanleitung.

1. Normale Diashow auf einem bekannten Bild laufen lassen. Besuchermodus ein- und
   nach wenigen Sekunden ausschalten. Das Originalbild muss sofort erscheinen;
   Playlistposition und verbleibende Frist müssen erhalten bleiben.
2. Dasselbe mit pausierter Diashow sowie laufendem/pausiertem Video wiederholen.
   Videoposition und manuellen Pausenzustand prüfen.
3. Ohne gespeicherte Quelle darf kein Hauptseitenschalter erscheinen. Nur eine
   Datei auswählen: weiterhin kein Schalter. Speichern: Schalter auf Wallpaper.
4. Zwischen Hauptseite, Widget und Hotkey umschalten. Der Hauptseitenschalter muss
   während des aktiven Modus bedienbar bleiben.
5. Unter Widgets → Besuchermodus Stile, Schriftgröße, Skalierung, Farben und Deckung
   ändern. Vorschau, Verwerfen, Speichern und Neustart prüfen.
6. Widget verschieben und neu starten; Position prüfen. Danach sperren und prüfen,
   dass der Schalter weiterhin bedienbar ist.
7. Bild, MP4 und Ordner, fehlende Medien und neutralen Ersatz sowie einen Wechsel
   kurz vor der normalen Schedulerfrist erneut prüfen.

Neue Dateien: `Widgets/VisitorWidgetSettings.cs`,
`Forms/WidgetSettingsEditor/WidgetSettingsEditor.VisitorPage.cs` und
`WallpaperControl.RegressionTests/VisitorRevisionTests.cs`.
Weitere Änderungen betreffen Scheduler-Wiederanlauf, Hauptseitenanordnung,
Widgetsettings/-Manager/-Editor, den gemeinsamen Widgetkopf mit optionaler
Schriftgröße, den gemeinsamen Schalterrenderer, Texte in fünf Sprachen und Tests.

## Ausgangspunkt und Git

Implementiert auf `codex/visitor-mode`, ausgehend von `d6ddbb5` (Release 2.3.1).
Das Arbeitsverzeichnis war vor Beginn sauber. Die Implementierung ist nach zentraler
Logik, Oberfläche, Widget, Hotkey und abschließender Absicherung gruppiert.
Es wurden keine NuGet-Abhängigkeiten oder UI-Frameworks hinzugefügt.
Die Erstimplementierung liegt in `f79bba9`; die Nachbesserung nach manueller
Abnahme liegt in `a48bc52`. Dieser Bericht wird separat versioniert. Die Anwendung zielt weiterhin auf .NET 10. Der vorhandene lokale
SDK 11 Preview meldet beim Build lediglich den SDK-Vorschauhinweis.

## Bedienung

In der linken Navigation **Besuchermodus** öffnen. Als Quelle Einzelbild, MP4 oder
Medienordner wählen, die Quelle auswählen und **Speichern** drücken. Danach den
Schalter auf der **Wallpaper-Hauptseite** betätigen. Er verwendet immer die zuletzt
gespeicherte Quelle. Fällt diese im aktiven Modus aus, bleibt der Modus mit einem
neutralen Hintergrund aktiv.

Der Ordner verwendet das eingestellte Intervall in Sekunden und wahlweise eine
zufällige Reihenfolge. Er wird vor jedem Wechsel neu eingelesen. Videos laufen
stumm in der vorhandenen Endlosschleife; im Ordner erfolgt der nächste Wechsel
nach dem Intervall, unabhängig von der Videolänge. Unterordner werden wie beim
bisherigen Bildordner nicht durchsucht.

Das Mini-Widget und seine Positionssperre werden unter **Widgets → Besuchermodus**
aktiviert und mit den übrigen Widgetoptionen gespeichert. Dort sind auch Minimal,
Clean und Glow, Hintergrund-/Schriftfarbe, Hintergrunddeckung, Schriftgröße und
Widgetskalierung einstellbar. Vorschau, Speichern und Verwerfen verwenden dieselbe
Transaktion wie die übrigen Widgets. Den Hintergrund oder die Beschriftung des Widgets ziehen; der
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
Besuchermedien nicht ersetzt. Die normalen Wiedergabesteuerelemente sind während des Modus
gesperrt; der Besucherschalter auf der Wallpaper-Hauptseite bleibt bedienbar. Remote-Weiter, Scheduler, normale Modusauswahl und manuelle Bildwechsel
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

Beim expliziten Beenden im Besuchermodus bleibt das sichere Bild beziehungsweise
ein sicherer Videoframe als statisches Windows-Wallpaper erhalten; nur bei Fehlern
wird neutraler Ersatz gesetzt. Die normale Windows-Diashow wird nicht wiederhergestellt. Der aktive
Zustand und der ursprüngliche Wiederherstellungszustand bleiben gespeichert.

## Grenzen

- Vor dem Start des Prozesses kontrolliert Windows den Desktop. Die Anwendung
  kann Anmeldebildschirm, Windows-Themes, andere Wallpaperprogramme oder manuelle
  externe Desktopänderungen nicht sperren. Das sichere Beenden hinterlässt einen
  sicheren statischen Hintergrund, ersetzt jedoch keine Windows-Anmelderichtlinie.
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
Speicherprüfungen wurden nicht geändert. Diese Beobachtung wird nicht als behobener Fehler
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
2. Auf **Besuchermodus** ein harmloses Bild auswählen und speichern. Auf der
   **Wallpaper-Hauptseite** einschalten:
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
7. Unter **Widgets → Besuchermodus** das Mini-Widget einschalten und speichern. Beide Schaltrichtungen zwischen Hauptmaske
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
11. Aktiv im Besuchermodus explizit beenden. Das sichere Bild beziehungsweise ein
    sicherer Videoframe muss bleiben; nur bei Medienfehlern neutraler Ersatz.
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
