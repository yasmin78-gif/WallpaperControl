# 🖼️ Wallpaper Control

🌐 **Sprache:** [English](README.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [日本語](README.ja.md)

**Wallpaper Control** ist ein schlankes Windows-Programm zur Verwaltung und Anzeige von Desktop-Wallpaper-Diashows mit präziser Zeitsteuerung, animierten Übergängen, Statistiken, nativen Desktop-Widgets und zusätzlichen Komfortfunktionen.

Es erweitert die standardmäßige Windows-Wallpaper-Funktion um eine eigene, an der Uhrzeit ausgerichtete Diashow-Engine, direkt auf dem Desktop gerenderte Übergangseffekte und optionale Desktop-Widgets. Dabei integriert es sich sauber in den Windows-Desktop und stellt beim Beenden der Anwendung die native Wallpaper-Verwaltung von Windows wieder her.

**Aktuelle Version: v1.8.4**

## ✨ Funktionen

- 🖼️ **Wallpaper-Diashow-Steuerung**
  - Wallpaper-Ordner auswählen
  - Diashow-Intervall ändern
  - Zufällige Reihenfolge aktivieren oder deaktivieren
  - Sofort zum nächsten Wallpaper wechseln
  - Hervorgehobene Aktion **Nächstes Wallpaper** für schnelleren Zugriff
  - Eigener Bereich für das aktuelle Wallpaper mit Schnellaktionen und Tooltip für den vollständigen Pfad
  - Diashow pausieren und fortsetzen
  - Automatische Pause bei aktiven Vollbildanwendungen, auch auf zusätzlichen Monitoren
  - Zwei Sekunden Verzögerung beim Fortsetzen verhindern, dass kurze Alt-Tab-Wechsel die Hintergrundaktivität sofort wieder starten
  - Manuell gesetzte Diashow-Pausen bleiben unabhängig davon erhalten
  - Aktuelles Wallpaper anheften

- 🎬 **Wallpaper-Übergangseffekte**
  - Weiche Übergänge, die direkt auf dem Windows-Desktop gerendert werden
  - Wipe mit wählbarer Richtung: Links, Rechts, Oben, Unten oder Zufällig
  - Slide mit wählbarer Richtung: Links, Rechts, Oben, Unten oder Zufällig
  - Fade
  - Zoom mit In- und Out-Varianten
  - Split
  - Curtain
  - Der Zufallsmodus wählt bei jedem Wallpaper-Wechsel einen anderen Effekt und variiert, sofern zutreffend, auch Richtung oder Zoom-Modus
  - Einstellbare Übergangsdauer
  - Desktop-Symbole und Desktop-Tools bleiben über der Übergangsebene sichtbar

- 🕐 **Native Desktop-Widgets**
  - Uhr-Widget mit 5 wählbaren Designs
  - Systemmonitor-Widget
  - Wetter-Widget mit optionaler 3-Tage-Vorhersage
  - Kalender-Widget mit iCalendar-/ICS-Unterstützung
  - Optionaler Nächstes-Wallpaper-Button
  - Widgets können unabhängig voneinander frei auf dem Desktop positioniert werden
  - Position jedes Widgets kann separat gesperrt werden
  - Widget-Positionen und Einstellungen werden gespeichert
  - Live-Vorschau der Widgets beim Ändern der Einstellungen
  - Widgets bleiben Teil des Desktops und liegen nicht über normalen Anwendungsfenstern

- 🖥️ **Windows-Integration**
  - Nutzt native Windows-Wallpaper-APIs und stellt gleichzeitig eine eigene Zeitsteuerung und Übergangs-Engine bereit
  - Eigene, an der Uhrzeit ausgerichtete Diashow-Zeitsteuerung für präzise Wallpaper-Wechsel
  - Manuelle Wallpaper-Wechsel setzen den automatischen Diashow-Zeitplan nicht zurück
  - Unterstützt verschiedene Wallpaper-Anzeigemodi
  - Erkennt externe Wallpaper-Wechsel
  - Öffnet Ordner mit dem konfigurierten Standard-Dateimanager
  - Optionaler automatischer Start mit Windows
  - Stellt beim Beenden von Wallpaper Control die native Windows-Wallpaper-Verwaltung wieder her
  - Läuft pro Windows-Benutzer nur als eine Instanz
  - Ein erneuter Start von Wallpaper Control bringt das bereits vorhandene Fenster in den Vordergrund
  - Unterstützt externe Wallpaper-Wechsel über das Kommandozeilenargument `--next`
  - `--next`-Befehle werden sicher an die laufende Instanz des aktuellen Windows-Benutzers weitergeleitet
  - Stellt den Aktivierungsstatus des nativen Uhr-Widgets für externe Anwendungen und Skripte bereit

- 📊 **Statistik-Dashboard**
  - Dauerhafte Wallpaper-Statistiken über Anwendungsneustarts hinweg
  - Erfasst Anzeigen und wann jedes Wallpaper zuletzt dargestellt wurde
  - Zeitbasierte Statistiken für Heute, Gestern, die letzten 7 Tage und die letzten 30 Tage
  - Top-10-, Top-25- und vollständige Statistikansichten
  - Dashboard-Kennzahlen für am häufigsten, am seltensten und durchschnittlich angezeigte Wallpaper
  - Gleichmäßigkeit der Verteilung metric
  - Top-10-Wallpaper-Diagramm
  - Durchschnittliche Wiederkehrzeit von Wallpapern
  - Analyse vernachlässigter Wallpaper
  - Erkennt Wallpaper, die noch nie angezeigt wurden
  - Suche und sortierbare Spalten
  - Wallpaper-Miniaturen und Vorschau beim Darüberfahren
  - Wallpaper direkt aus dem Statistikfenster setzen
  - Wallpaper oder deren Ordner über das Kontextmenü öffnen
  - Einzelne Einträge entfernen oder alle Statistiken zurücksetzen
  - Automatische Sicherung und Wiederherstellung, wenn die Haupt-Statistikdatei nicht geladen werden kann
  - Beschädigte Statistikdateien bleiben für eine mögliche Wiederherstellung erhalten

- 🗑️ **Schnelles Aussortieren von Wallpapern**
  - Unerwünschte Wallpaper mit einem Klick in einen Ordner `Aussortiert` verschieben
  - Das Aussortieren ist während eines laufenden Wallpaper-Übergangs vorübergehend deaktiviert
  - Das nächste Wallpaper wird vollständig angezeigt, bevor das aussortierte Wallpaper verschoben wird
  - Optionaler globaler Aussortierordner
  - Optionale Unterordner für einzelne Wallpaper-Sammlungen
  - Letzte Aussortierung rückgängig machen

- 📜 **Wallpaper-Verlauf**
  - Erfasst die zuletzt während der aktuellen Sitzung angezeigten Wallpaper
  - Wallpaper direkt im Standard-Bildbetrachter öffnen
  - Vorschau beim Darüberfahren zur schnellen Identifikation

- ⌨️ **Globale Hotkeys**
  - Nächstes Wallpaper
  - Pause / Fortsetzen
  - Aktuelles Wallpaper im Dateimanager anzeigen
  - Aktuelles Wallpaper aussortieren
  - Hotkeys können angepasst oder deaktiviert werden
  - Erkennt doppelte Hotkey-Belegungen
  - Warnt, wenn Windows einen ausgewählten Hotkey nicht registrieren kann
  - Hotkeys können zwischen Aktionen getauscht werden, ohne Konflikte mit vorherigen Belegungen
  - Unveränderte Hotkeys bleiben registriert, wenn andere Tastenkürzel geändert werden
  - Standard-Hotkey zum Aussortieren: `Ctrl+Alt+Shift+R`

- 🔔 **Infobereich-Unterstützung**
  - Wallpaper Control kann im Windows-Infobereich weiterlaufen
  - Doppelklick auf das Infobereich-Symbol stellt das Fenster wieder her
  - Optionales **In den Infobereich schließen** beim Klick auf den X-Button des Fensters
  - Anwendung direkt über das Infobereich-Menü beenden

- 🎨 **Benutzeroberfläche & Erscheinungsbild**
  - Neu gestaltete Einstellungsoberfläche
  - Hauptanwendung passend zur Einstellungsoberfläche neu gestaltet
  - Einheitliches modernes Erscheinungsbild in der gesamten Anwendung
  - Auswahl zwischen System-, Dunkel- und Hell-Design
  - System-Design folgt automatisch dem Windows-App-Design
  - Einstellbare Fenstertransparenz
  - Speichert die Fensterposition
  - Drag-&-Drop-Unterstützung
  - Neu organisierte Einstellungsoberfläche
  - Einstellungen öffnen immer auf dem Tab **Allgemein**
  - Überarbeitetes Hauptfenster mit klarerer Gruppierung und verbesserter visueller Hierarchie
  - Dunkle Dropdowns und bessere Lesbarkeit deaktivierter Bedienelemente
  - Verbesserte Tab-Reihenfolge per Tastatur und einheitliche Abstände
  - Separates Zurücksetzen des Erscheinungsbilds
  - Lokalisierte Benutzeroberfläche

## 🎮 Vollbildpause

Wallpaper Control kann die Hintergrundaktivität automatisch reduzieren, solange eine Vollbildanwendung aktiv ist.

- Erkennt Vollbildanwendungen auf allen angeschlossenen Monitoren
- Pausiert automatische Wallpaper-Wechsel und Übergangsanimationen
- Setzt reguläre Aktualisierungen der Desktop-Widgets vorübergehend aus
- Verschiebt automatische Update-Prüfungen
- Setzt die Aktivität fort, nachdem zwei Sekunden lang keine Vollbildanwendung aktiv war
- Kurze Alt-Tab-Wechsel starten pausierte Hintergrundaktivitäten dadurch nicht sofort neu
- Eine manuell pausierte Diashow bleibt auch nach Ende des Vollbildmodus pausiert
- Die Vollbilderkennung ist standardmäßig aktiviert und kann in den Einstellungen deaktiviert werden

## 🔄 Update-Prüfung

Wallpaper Control kann GitHub Releases auf neuere Versionen prüfen, ohne dem Benutzer die Kontrolle über Updates abzunehmen.

- Manuelle Update-Prüfung in den Einstellungen verfügbar
- Optionale automatische Prüfung bei jedem Start von Wallpaper Control
- Solange die Anwendung durchgehend läuft, wird die automatische Prüfung alle 24 Stunden wiederholt
- Eigene Update-Dialoge zeigen die installierte und die neueste verfügbare Version
- Bei einer verfügbaren neueren Version kann die Release-Seite direkt geöffnet werden
- Automatische Update-Prüfungen können in den Einstellungen deaktiviert werden
- Wallpaper Control **lädt oder installiert Updates niemals automatisch**

Ist bereits die aktuelle Version installiert, bleiben automatische Prüfungen unauffällig. Manuelle Prüfungen geben immer eine Rückmeldung.

## 🕐 Desktop-Widgets

Wallpaper Control enthält native Desktop-Widgets, die sich direkt in den Windows-Desktop integrieren.

Widgets können unabhängig voneinander positioniert und an ihrer Position gesperrt werden. Positionen und Einstellungen bleiben zwischen Anwendungssitzungen erhalten.

Änderungen an Widgets werden während der Konfiguration in den Einstellungen sofort als Vorschau angezeigt. Dauerhaft übernommen werden sie beim Speichern. Beim Abbrechen werden der vorherige Widget-Zustand und die vorherige Position wiederhergestellt.

### Uhr

Die Desktop-Uhr bietet:

- Anzeige von Stunden und Minuten
- Optionale Sekundenanzeige
- Lokalisierte Datumsformatierung
- Einstellbare Größe
- 5 wählbare visuelle Designs
- Freie Positionierung
- Optionale Positionssperre

Die Uhr verwendet automatisch die in Wallpaper Control ausgewählte Sprache.

### 🖥️ Systemmonitor

Das System-Widget zeigt wichtige Hardware- und Systeminformationen kompakt direkt auf dem Desktop an.

Es kann Folgendes anzeigen:

- CPU-Auslastung
- CPU-Temperatur
- RAM-Auslastung
- GPU-Auslastung
- GPU-Temperatur
- VRAM-Auslastung
- Netzwerk-Downloadaktivität
- Netzwerk-Uploadaktivität
- Laufwerksauslastung

Das Widget enthält kompakte grafische Auslastungsbalken und bietet die Stile **Minimal**, **Clean** und **Glow**.

Die Hardwareüberwachung läuft asynchron, damit Sensoraktualisierungen weder Wallpaper-Übergänge noch die Reaktionsfähigkeit der Hauptanwendung beeinträchtigen.

### 🌦️ Wetter

Das Wetter-Widget zeigt aktuelle Wetterinformationen direkt auf dem Desktop an.

Es kann Folgendes anzeigen:

- Aktuelle Temperatur
- Gefühlte Temperatur
- Aktuelle Wetterbedingungen
- Luftfeuchtigkeit
- Niederschlag
- Windgeschwindigkeit
- Optionale 3-Tage-Vorhersage

Die Wetterdaten werden von **Open-Meteo** bereitgestellt und benötigen keinen API-Schlüssel.

Der Standort kann in den Einstellungen konfiguriert werden. Wetterinformationen lassen sich automatisch in wählbaren Intervallen aktualisieren.

Das Wetter-Widget bietet die Stile **Minimal**, **Clean** und **Glow** und kann unabhängig positioniert und gesperrt werden.

### 📅 Kalender

Das Kalender-Widget bietet direkt auf dem Desktop eine kompakte Übersicht über bevorstehende Termine.

Kalenderdaten werden über schreibgeschützte **iCalendar-/ICS**-Feeds geladen.

Zu den Funktionen gehören:

- Unterstützung mehrerer ICS-Kalenderquellen
- Termine mit Uhrzeit
- Ganztägige Ereignisse
- Wiederkehrende Ereignisse
- Mehrtägige Ereignisse
- Mehrtägige ganztägige Ereignisse werden an jedem betroffenen Tag angezeigt
- Mehrere Termine am selben Tag werden gruppiert
- Zeigt kommende Tage an, die tatsächlich Termine enthalten
- Leere Tage werden übersprungen
- Optionale Anzeige des Veranstaltungsorts
- Automatische Widget-Größe anhand der angezeigten Termine
- Einstellbares Aktualisierungsintervall
- **Minimal**, **Clean** and **Glow** styles
- Unabhängige Positionierung und Sperre

Private ICS-Adressen werden für den aktuellen Windows-Benutzer verschlüsselt mit der **Windows Data Protection API (DPAPI)** gespeichert.

Private Kalender-URLs werden in den Einstellungen standardmäßig verborgen und können zum Anzeigen oder Bearbeiten gezielt eingeblendet werden.

Wallpaper Control liest Kalender-Feeds ausschließlich und verändert keine Kalenderdaten.

#### 🎌 Feiertagskalender

Separate ICS-Quellen können als Feiertagskalender konfiguriert werden.

Feiertagsereignisse werden visuell hervorgehoben und automatisch vor normalen Terminen desselben Tages platziert, damit Feiertage und andere besondere Kalendereinträge leichter erkennbar sind.

Mehrere normale Kalender- und Feiertagsquellen können im selben Kalender-Widget kombiniert werden.

Kalenderaktualisierungen sind robust gegenüber vorübergehenden Feed-Ausfällen. Bereits geladene Ereignisse bleiben sichtbar, wenn eine einzelne Quelle nicht erreichbar ist, während verfügbare Feeds weiterhin aktualisiert werden. Wallpaper Control weist darauf hin, wenn zwischengespeicherte Kalenderdaten möglicherweise veraltet sind, und entfernt die Warnung wieder, sobald alle konfigurierten Feeds erfolgreich aktualisiert wurden. Zwischengespeicherte Ereignisse bleiben für die aktuelle Anwendungssitzung erhalten.

### Nächstes Wallpaper

Das Nächstes-Wallpaper-Widget bietet einen kompakten Desktop-Button, mit dem sofort zum nächsten Wallpaper gewechselt werden kann.

Es bietet die Stile **Minimal**, **Clean** und **Glow**. Änderungen werden sofort in der Live-Vorschau angezeigt, und der gewählte Stil bleibt zwischen Anwendungssitzungen gespeichert. Bestehende Konfigurationen verwenden standardmäßig weiterhin das bisherige **Minimal**-Erscheinungsbild.

Es kann unabhängig von den anderen Widgets positioniert und gesperrt werden und verwendet dieselbe Wallpaper-Wechsel- und Übergangslogik wie die Hauptanwendung.

## 📊 Statistiken

Wallpaper Control führt dauerhafte Statistiken über die durch die Diashow angezeigten Wallpaper.

Das Statistik-Dashboard kann Folgendes anzeigen:

- Gesamtzahl der Anzeigen jedes Wallpapers
- Wann ein Wallpaper zuletzt angezeigt wurde
- Anteil der Anzeigen und Beliebtheitsrang
- Statistiken für Heute, Gestern, die letzten 7 Tage und die letzten 30 Tage
- Am häufigsten und am seltensten angezeigte Wallpaper
- Durchschnittliche Anzahl der Anzeigen
- Gleichmäßigkeit der Verteilung
- Durchschnittliche Wiederkehrzeit
- Ein Top-10-Diagramm
- Wallpaper, die noch nie oder seit längerer Zeit nicht mehr angezeigt wurden

Statistiken werden lokal gespeichert und bleiben nach einem Neustart der Anwendung erhalten.

Wallpaper Control bewahrt die vorherige Statistikdatei als Sicherung auf. Kann die Hauptdatei nicht geladen werden, kann automatisch auf die Sicherung zurückgegriffen werden, während beschädigte Dateien für eine mögliche Wiederherstellung erhalten bleiben. Statistiken werden über eindeutige temporäre Dateien geschrieben, um das Risiko von Speicherkollisionen oder unvollständigen Ersetzungen zu reduzieren.

Zeitbasierte Statistiken und die Erfassung der Wiederkehr beginnen mit der erstmaligen Initialisierung der entsprechenden Tracking-Daten. Historische Tages- oder Wiederkehrdaten aus der Zeit davor werden nicht rekonstruiert.

Der Verlauf der zuletzt angezeigten Wallpaper bleibt sitzungsbasiert und wird gelöscht, wenn Wallpaper Control vollständig beendet wird.

## 🗑️ Wallpaper aussortieren

Gefällt dir das aktuell angezeigte Wallpaper nicht?

Wallpaper Control wechselt zunächst zum nächsten Wallpaper und wartet, bis der Wechsel abgeschlossen ist, bevor das unerwünschte Bild in einen Ordner `Aussortiert` verschoben wird. Während bereits ein Wallpaper-Übergang läuft, ist das Aussortieren vorübergehend deaktiviert. So wird verhindert, dass das aktuell angezeigte Bild verschoben wird, bevor der Übergang beendet ist.

Das Ziel kann entweder innerhalb des aktuellen Wallpaper-Ordners liegen oder als globaler Aussortierordner konfiguriert werden.

Versehentlich das falsche Bild aussortiert? Die letzte Aussortierung kann während der aktuellen Sitzung rückgängig gemacht werden.

## 🕹️ Externe Steuerung

Wallpaper Control kann während des Betriebs Befehle von externen Anwendungen, Skripten, Verknüpfungen oder Desktop-Tools empfangen.

### Nächstes Wallpaper

Folgender Befehl wird unterstützt:

```text
WallpaperControl.exe --next
```

Dadurch wird eine Anfrage an die laufende Wallpaper-Control-Instanz gesendet und sofort zum nächsten Wallpaper gewechselt.

Wallpaper Control läuft für den aktuellen Windows-Benutzer als einzelne Instanz. Ein erneuter normaler Start bringt das vorhandene Hauptfenster in den Vordergrund. Die Befehlskommunikation wie `--next` ist auf den aktuellen Benutzer beschränkt. Fehlerhafte, übergroße oder festhängende Anfragen werden abgewiesen, ohne nachfolgende Befehle zu blockieren.

Externe Anfragen verwenden dieselbe Diashow-, Zeitplan- und Übergangslogik wie direkt in Wallpaper Control ausgelöste Wallpaper-Wechsel.

Dadurch lässt sich Wallpaper Control in eigene Skripte, Launcher, Automatisierungstools oder andere Desktop-Anwendungen integrieren, ohne das Hauptfenster zu öffnen.

### Uhr Widget State

Externe Anwendungen können durch Auslesen des folgenden Schlüssels feststellen, ob die native Wallpaper-Control-Uhr aktiviert ist:

```text
HKEY_CURRENT_USER\Software\WallpaperControl
```

Registry-Wert:

```text
ClockWidgetEnabled
```

Werte:

```text
0 = Natives Uhr-Widget deaktiviert
1 = Natives Uhr-Widget aktiviert
```

Dadurch können externe Anwendungen oder Desktop-Tools ihr Verhalten daran anpassen, ob die native Uhr von Wallpaper Control aktiviert ist.

Der Registry-Wert entspricht der gespeicherten Widget-Einstellung. Vorschauänderungen bei geöffnetem Einstellungsfenster werden erst beim Speichern der Einstellungen dauerhaft übernommen.

## 🩺 Diagnose

Wallpaper Control enthält eine schlanke Diagnoseprotokollierung für unerwartete Fehler und Ausfälle.

Protokolle werden nur bei Bedarf erstellt und hier gespeichert:

```text
%APPDATA%\WallpaperControl\Logs
```

Die Hauptprotokolldatei lautet:

```text
wallpaper-control.log
```

Die Protokollierung dient der Fehlersuche und erfordert im normalen Betrieb keine zusätzliche Konfiguration.

## 🌍 Sprachen

Wallpaper Control enthält derzeit:

- 🇩🇪 Deutsch
- 🇬🇧 Englisch
- 🇫🇷 Französisch
- 🇪🇸 Spanisch
- 🇯🇵 Japanisch

Die Sprache der Benutzeroberfläche kann direkt in den Anwendungseinstellungen geändert werden.

Texte und Datumsformatierung der Desktop-Widgets folgen der ausgewählten Anwendungssprache.

## 💻 Voraussetzungen

- **Windows 11:** unterstützt und getestet
- **Windows 10:** voraussichtlich kompatibel, derzeit nicht getestet
- 64-Bit-Windows
- Keine separate .NET-Installation erforderlich

## 🚀 Installation

Der **offizielle Installationsweg** für Wallpaper Control ist der Windows-x64-Installer, der mit jedem Release bereitgestellt wird.

1. Lade `WallpaperControl-1.8.3-Setup-x64.exe` aus dem neuesten GitHub-Release herunter.
2. Beende eine bereits laufende Wallpaper-Control-Instanz vor der Installation oder Aktualisierung vollständig über das Infobereich-Symbol.
3. Starte den Installer.
4. Wähle während der Installation optional eine Desktop-Verknüpfung aus.
5. Starte Wallpaper Control über das Startmenü oder die Desktop-Verknüpfung.

Wallpaper Control wird für den aktuellen Windows-Benutzer installiert und **benötigt keine Administratorrechte**.

Der Installer enthält die benötigte **.NET-Runtime**, daher ist keine separate .NET-Installation erforderlich. Eine Startmenü-Verknüpfung wird automatisch erstellt, eine Desktop-Verknüpfung ist optional.

Bei der Aktualisierung einer bestehenden Installation bleiben Einstellungen und Statistiken von Wallpaper Control erhalten. War der automatische Start bereits aktiviert, wird der Eintrag auf den neuen Installationspfad aktualisiert.

Bei der Deinstallation von Wallpaper Control werden die Anwendung und ihre Verknüpfungen entfernt, während Benutzereinstellungen und Statistiken erhalten bleiben und bei einer späteren Installation wiederverwendet werden können.

Der Installer ist derzeit **nicht digital signiert**. Windows kann daher beim Start der Setup-Datei eine Sicherheitswarnung anzeigen.

## 🔒 Datenschutz

Wallpaper Control speichert seine Anwendungseinstellungen und Wallpaper-Statistiken lokal auf deinem Computer.

Es ist kein Wallpaper-Control-Konto erforderlich.

Die meisten Funktionen, darunter Wallpaper-Verwaltung, Diashow-Steuerung, Übergänge, Vollbilderkennung und Statistiken, arbeiten vollständig lokal.

Einige optionale Funktionen benötigen eine Internetverbindung:

- Das **Wetter-Widget** verbindet sich mit Open-Meteo, um Wetterinformationen abzurufen.
- Das **Kalender-Widget** verbindet sich mit den konfigurierten iCalendar-/ICS-Adressen, um Kalenderdaten abzurufen.
- Die optionale **Update-Prüfung** verbindet sich mit GitHub Releases, um festzustellen, ob eine neuere Version von Wallpaper Control verfügbar ist. Automatische Prüfungen können deaktiviert werden, und Wallpaper Control lädt oder installiert Updates niemals automatisch.

Private ICS-Adressen, die für das Kalender-Widget konfiguriert sind, werden für den aktuellen Windows-Benutzer verschlüsselt mit der Windows Data Protection API (DPAPI) gespeichert.

Der Kalenderzugriff ist schreibgeschützt. Wallpaper Control verändert keine Termine oder Kalenderdaten.

Diagnoseprotokolle werden lokal gespeichert und nur bei Bedarf zur Fehlersuche erstellt.

## 🛠️ Entwickelt mit

- C#
- .NET 10
- Windows Forms
- Native Windows APIs / COM integration
- Open-Meteo
- iCalendar / ICS

## 📄 Lizenz

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control ist freie Open-Source-Software und steht unter der **GNU General Public License v3.0 (GPL-3.0)**.

Du darfst Wallpaper Control gemäß den Bedingungen der GNU General Public License v3.0 frei verwenden, untersuchen, verändern und weiterverbreiten.

Den vollständigen Lizenztext findest du in der Datei `LICENSE`.

---

**Wallpaper Control**  
Ein bisschen mehr Kontrolle darüber, was Windows auf deinen Desktop legt. 🖼️
