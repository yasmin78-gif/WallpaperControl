# Phase 15A.1 – weiche Preiskurven und lokale Endpunkte

Stand: 19.11.2026. Branch: `codex/pricewatch-phase-15-history`.
Umgesetzt und offline geprüft; manuelle Freigabe ausstehend.
Dieser Bericht ergänzt und ersetzt die früheren Aussagen zur reinen Stufendarstellung
und zum vollständigen Verzicht auf Verbindungen zwischen Historie und lokalen Punkten.

## Darstellung

- Historische gültige Preisereignisse werden durch kräftigere, geglättete Kurven verbunden.
  Kleine gefüllte Kreise markieren ausschließlich die tatsächlichen Ereignisse.
- Händlerfarben bleiben gleich. Historische Linien sind durchgezogen; Glow erhält eine
  dezente zusätzliche Kontur. Clean und Minimal verwenden dieselbe Kurvengeometrie.
- Später erfasste lokale Beobachtungen können vom letzten historischen Punkt desselben
  Händlers aus eine gestrichelte Verbindung bis zur letzten Preisraute bilden.
  Ihre tatsächlichen Zeitpunkte und Preise bleiben unverändert.
- Die erweiterte Legende erläutert ausdrücklich:
  **„Verbindung zwischen bekannten Preisständen – Zwischenverlauf unbekannt“**.
  Auch mehrere lokale Stichproben belegen nicht die dazwischenliegenden Preise;
  diese Verbindungen bleiben deshalb vollständig gestrichelt.
- Lokale Kreise, letzte Preisrauten, Wunschpreislinie, Herkunfts-Tooltips und die getrennte
  ITAD-Tiefstpreisstatistik bleiben bestehen. Der Renderer verändert keine gespeicherten Werte.

## Kurvenberechnung

Neue reine Geometrieklasse `PriceWatchCurves`: PCHIP-artige monotone kubische Hermite-
Interpolation mit gewichteten harmonischen Tangenten für ungleichmäßige Zeitabstände.
Die Darstellung erfolgt über kubische Beziersegmente in GDI+; keine neue Abhängigkeit.

Die X-Koordinaten entstehen aus den wirklichen Zeitdifferenzen, nicht aus gleichmäßig
verteilten Indexpositionen. Richtungswechsel und Plateaus begrenzen die Tangenten.
Bezier-Kontrollpreise bleiben zusätzlich innerhalb des Preisintervalls der beiden
Endpunkte. Dadurch wird die Kurve weder negativ noch ober-/unterhalb ihrer tatsächlichen
Endpunktpreise gezeichnet. Zwei Punkte ergeben eine geradlinige kubische Verbindung;
mehr Punkte erlauben sanfte Übergänge. Ein einzelner Punkt bleibt ein Punkt.

Die Kurve visualisiert Preisstände zwischen realen Ereignissen. Sie ist **keine Messung
des tatsächlichen Zwischenverlaufs**. Interpolierte Werte werden weder gespeichert noch
für Tooltips, Tiefstpreise, Wunschpreisprüfungen oder Alarme verwendet. Der historische
Tiefstpreis bleibt das Minimum der echten sichtbaren ITAD-Datenpunkte.

## Lücken, Widersprüche und Verbindungen

- Historische Null-Ereignisse sowie bereits als ungültig/widersprüchlich markierte
  Ereignisse zerlegen den Verlauf in getrennte Abschnitte. Keine Glättung über diese Grenzen.
- Historische Kurven beginnen und enden an tatsächlichen Punkten; keine Verlängerung
  zur linken oder rechten Zeitgrenze.
- Für gestrichelte Verbindungen wird nur der letzte historische Punkt als Anker verwendet.
  Ist dieser eine Lücke, fehlt ein gültiger Anker und es gibt keine solche Verbindung.
  Frühere gültige Punkte dürfen eine nachfolgende Null-Meldung nicht überbrücken.
- Nur spätere lokale Beobachtungen desselben Händlers kommen in zeitlicher Reihenfolge
  hinzu. Frühere lokale Beobachtungen bleiben unabhängige Punkte. Keine rückwärts
  gerichtete Verbindung und keine Verbindung zwischen verschiedenen Händlern.
- Unterschiedliche Preise am identischen historischen/lokalen Ankerzeitpunkt verhindern
  die Verbindung. Konflikte zwischen lokalen Punkten stoppen die Kette vor dem Konflikt.
  Auch ein bekanntes Angebotsende zwischen lokalen Beobachtungen wird nicht überbrückt.
- Händler ohne ITAD-Historie behalten ausschließlich ihre lokalen Punkte/Rauten,
  auch wenn mehrere lokale Messungen vorhanden sind.
- Die vorherige Validierung der Datenmodi, UUIDs, Währung, Ablaufzeiten und des gewählten
  Zeitfensters bleibt unverändert. Die Geometrieklasse verhindert zusätzlich gemischte
  Spiel-/Modusidentitäten innerhalb einer Verbindung.

Mouse-Hit-Tests und Tastaturauswahl beziehen sich weiterhin ausschließlich auf reale
historische oder lokale Punkte. Für eine beliebige Stelle auf der Kurve wird kein
erfundener Preis-Tooltip erzeugt. Die Bedeutung der gestrichelten Abschnitte steht in
der Legende; es werden keine Preise entlang dieser Abschnitte vorgetäuscht.

## Dateien dieser Überarbeitung

- Neu: `WallpaperControl/Services/PriceWatchCurves.cs`.
- `PriceWatchHistoryChart.cs`: Bezier-Rendering, kräftigere Linien und Glow,
  gestrichelte lokale Verbindungen; vorhandene Punkt-/Tooltiplogik bleibt erhalten.
- `PriceWatchHistoryView.cs`: Legende und Weitergabe des gewählten Widget-Stils.
- `PriceWatchDetailForm.cs`: Stil an die Historyansicht weitergeben.
- Ressourcen de/en/fr/es/ja: Erklärung der gestrichelten Verbindung.
- Neu: `WallpaperControl.RegressionTests/PriceWatchCurveTests.cs` und Einbindung in
  den vollständigen PriceWatch-Testlauf.
- Dieser Bericht und Verweis im bisherigen Abschlussbericht.

Keine zusätzliche Datenbankmigration oder Änderung an Speicherung, Proxy, API-Aufrufen,
Cache, Preisabfragen oder Alarmberechnung in dieser Überarbeitung. Die zuvor beauftragte
lokale Beobachtungstabelle bleibt unverändert erhalten. Kein Merge und kein Push.

## Qualitätssicherung

Release-Build: **0 Fehler, 0 Warnungen**.
**Alle 887 PriceWatch-Regressionen bestanden**, davon 17 neue Kurvenprüfungen.

```powershell
dotnet build WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release --nologo -v quiet
dotnet WallpaperControl.RegressionTests/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.RegressionTests.dll --pricewatch-checks
```

Neue Prüfungen: exakte Endpunkte, tatsächliche ungleiche Zeitabstände, Plateau,
monotone begrenzte Kurven ohne Überschwinger, einzelne/gleichzeitige Punkte,
Null-Lücken, chronologische lokale Verbindungen, fehlende Händlerhistorie,
Widersprüche, Angebotsende, unveränderter ITAD-Tiefstpreis und Plotgeometrie
bei 96/144/192 DPI. Die Überschwinger-/Monotonieprüfung tastet 80 deterministische
Fälle mit stark unterschiedlichen Zeitabständen und Preisen ab (225.120 Stichproben).

Die bestehenden Prüfungen für echte Punkt-Tooltips, Wunschpreisreferenz, Quellen-
trennung, Aufbewahrung, SQLite-Daten, Alarme und UI laufen weiterhin vollständig mit.
Alle drei Stile und kleine Fenster sind in den Offline-Layoutprüfungen enthalten.
Die erneuerten Renderings liegen unter `%TEMP%/PriceWatch-Phase15A1/`; das Glow-Bild
wurde visuell geprüft. Echtes Windows-Monitor-/DPI-Umschalten bleibt manuell zu prüfen.
Keine Live-API- oder CDN-Abfragen während Entwicklung und Tests.

## Manuelle Abnahme

1. Release-Anwendung im Testdatenmodus öffnen, Cyberpunk-Details aufrufen und
   den Preisverlauf aufklappen. Bei Bedarf vorher die bestehende Live-Überwachung
   deaktivieren, damit keine unabhängig davon fällige Liveprüfung erfolgt.
2. Historische Kurven, echte Kreismarker und letzte Preisrauten vergleichen.
   Die geglätteten Linien dürfen keine Spitzen über beziehungsweise unter den
   tatsächlichen Preisen benachbarter Ereignisse erzeugen.
3. GOG-/Ubisoft-Unterbrechungen in passenden Zeiträumen prüfen: bekannte Lücken
   bleiben offen und beeinflussen die anderen Händler nicht.
4. Rechte Diagrammseite prüfen: zulässige Verbindungen zu späteren lokalen Punkten
   sind gestrichelt; die Erklärung steht in der Legende. EA ohne Historie bleibt
   bei einzelnen lokalen Punkten und besitzt keine verbindende Kurve.
5. Mit Maus und Pfeiltasten reale Punkte prüfen: Quellen, Zeitpunkte und Preise
   unverändert; zwischen Punkten keine erfundenen Tooltipwerte.
6. Händlerfilter, 7/30/90 Tage, Wunschpreis, Minimal/Clean/Glow, Größenfixierung,
   kleine Fenster sowie Windows-Skalierung 100/150/200 % kontrollieren.

Auf manuelle Freigabe warten.
