# PriceWatch Phase 15A.1 – grafischer Preisverlauf

Neueste Darstellungsüberarbeitung: [Weiche Kurven und lokale Endpunkte](PRICEWATCH-SMOOTH-CURVES.md).

Aktuelle Ergänzung: [Letzte Preisstände und lokale Beobachtungen](PRICEWATCH-LOCAL-OBSERVATIONS.md).
Dieser ergänzende Bericht beschreibt die inzwischen hinzugekommene SQLite-Beobachtungstabelle
und ersetzt die nachstehenden früheren Aussagen zum Verzicht auf lokale Preisbeobachtungen.

Stand: 19.11.2026. Branch: `codex/pricewatch-phase-15-history`.
Grundlage: [Phase-15A-Analyse](PRICEWATCH-PHASE-15A.md).
Implementierung abgeschlossen; manuelle Abnahme ausstehend. Kein Commit, Merge,
Push oder Deployment durchgeführt.

## Implementierte Funktionen

Im Detailfenster befindet sich unter dem Wunschpreis der aufklappbare Bereich
**Preisverlauf**. Er ist zunächst geschlossen und lädt erst beim Aufklappen.
Standardzeitraum: **30 Tage**, außerdem 7 und 90 Tage. Es gibt keine zusätzlichen
Historyabrufe beim Widgetstart oder innerhalb der regelmäßigen Preisüberwachung.

- Alle global aktivierten Händler werden in einer gemeinsamen Historyanfrage geladen.
  Eine lokale Legende schaltet jede Serie einzeln sichtbar/unsichtbar, ohne Netzabruf
  und ohne Änderung der globalen Händler- oder Alarmkonfiguration.
- Stabile Händlerfarben und ergänzende Strichmuster, tatsächliche Zeitpositionen,
  EUR-Skala, Datumsskala und horizontale Wunschpreisreferenz.
- Ein Punkt-Tooltip zeigt Datum/Uhrzeit einschließlich Sekunden und Offset, Händler,
  Verkaufspreis und gelieferten regulären Preis. Es gibt keine interpolierten Tooltipwerte.
  Im fokussierten Diagramm können Punkte auch mit Links/Rechts ausgewählt werden.
- „Niedrigster erfasster Preis“ bezeichnet ausschließlich die gültigen verfügbaren
  Daten des gewählten Zeitraums und der sichtbaren Händler, einschließlich echter
  0-EUR-Preise. Händler und Datum werden mit angezeigt; kein behauptetes Allzeitminimum.
- Händler ohne gültige Punkte im gewählten Zeitraum sind als „Keine Daten“ bezeichnet.
- Im Testdatenmodus ausschließlich lokale Beispiele mit unterschiedlichen Händlerpreisen,
  Erhöhungen, Senkungen, mehreren Änderungen am selben Tag und expliziten Unterbrechungen.
  EA dient im Demo als Händler ohne Daten. Testdaten sind im Fenster und Datenstand gekennzeichnet.

## Diagrammtechnik und Darstellung

Eigenes WinForms-Control auf Basis von `OnPaint`/GDI+ und Double Buffering, ohne neue
Bibliotheken. Keine Animationen, kein neuer Timer und keine Arbeit an Netzwerk oder
SQLite im Paintpfad. GDI-Ressourcen werden lokal freigegeben, Tooltip beim Dispose.
Skala wird pro Zeichenvorgang einmal bestimmt; Rendering und Hit-Testing arbeiten
linear über die begrenzte Punktmenge, ohne quadratische wiederholte Skalenberechnung.

Integration in die vorhandenen Minimal-/Clean-/Glow-Oberflächen, Fonts und proportionale
Inhaltsskalierung. Die gespeicherte Fenstergröße und Größenfixierung bleiben erhalten.
Bei geöffnetem Verlauf wird das vorhandene äußere Scrollpanel genutzt. Standardgröße
und Widgetliste bleiben unverändert; zusätzliche Inhalte verursachen gegebenenfalls
vertikales Scrollen. Es gibt keinen eigenen horizontalen Diagrammscrollbalken.
Cover, Stammdaten, aktuelle Angebotstabelle, Wunschpreis und vier bestehende Aktionen bleiben erhalten.

## Datenqualität und Grenzen

- Ausschließlich vorhandene `/history`-Route mit ursprünglicher ITAD-UUID, DE/EUR
  und aktivierten Händler-IDs. Keine Vermischung von Editionen.
- Chronologische Sortierung nach tatsächlichem Zeitpunkt, Beschneidung auf den
  angezeigten Zeitraum und Deduplikation identischer Ereignisse.
- Null-Ereignisse, ungültige Geldwerte und widersprüchliche gleichzeitige Ereignisse
  werden als Lücke behandelt, niemals als erfundener 0-EUR-Preis.
- Stufen werden nur zwischen aufeinanderfolgenden gültigen Ereignissen derselben
  Serie gezeichnet. Die horizontale Stufe bedeutet „zuletzt gemeldeter Zustand“ des
  ITAD-Änderungsprotokolls und ist keine zusätzliche Messung oder Kaufbarkeitsgarantie.
- Keine rückwärtige Ergänzung vor dem ersten Punkt und keine Fortschreibung nach
  dem letzten Punkt bis heute. Kein Überbrücken expliziter Null-/Konflikt-Ereignisse.
  Bei einer Lücke endet die Verbindung konservativ schon am vorherigen gültigen Punkt.
- Unbekannte Erfassungsausfälle bei ITAD sind aus der Antwort nicht erkennbar.
  Der unveränderte Proxy filtert Nicht-EUR-Ereignisse; solche nicht gemeldeten
  Unterbrechungen kann der Desktop ebenfalls nicht rekonstruieren. Der Verlauf
  beschreibt daher ausschließlich die gelieferten Daten, keine vollständige Händlerhistorie.
- Lokale aktuelle Preis-Snapshots werden bewusst **nicht** in die Historie eingezeichnet.
  Keine neue dauerhafte Speicherung, keine Migration und keine rückwirkende Rekonstruktion.
- Reguläre Preise werden nur zusätzlich im Tooltip angezeigt; die Linie zeigt den Verkaufspreis.

## Asynchronität, Cache und Sicherheit

History verwendet denselben Client mit bestehender Authentifizierung, Timeouts,
Retry-After-Sperre und serieller Abrufwarteschlange wie die bisherigen Datenabfragen.
Keine Proxy- oder Zugangsdatenänderung. Der API-Schlüssel bleibt auf dem Server.

Ein eigener begrenzter RAM-Cache für History ergänzt die vorhandene Proxy-Cachewirkung:
maximal 16 Einträge, konservatives Punktbudget von 8 MiB, Frische 15 Minuten.
Schlüssel umfasst Datenmodus, UUID, Zeitraum und normalisierte Händlerauswahl.
Es erfolgt keine Speicherung auf der Festplatte. Identische gleichzeitige Anfragen
werden zusammengefasst; beim letzten abgemeldeten Teilnehmer wird der Transport abgebrochen.
Fehler werden nicht als erfolgreiche/leere Historie gecacht. Einzelantworten über
10.000 Punkte werden als Fehler abgewiesen, nicht still gekürzt. Die bestehenden
engeren HTTP-Antwortgrößenlimits bleiben zusätzlich wirksam.

Die History-Schaltfläche „Preise abrufen“ kann innerhalb der 15 Minuten aus RAM liefern;
sie erzwingt keinen Cache-Bypass und keine zusätzliche Last beim Anbieter. Der angezeigte
Datenstand stammt aus der Antwort, nicht aus dem Zeitpunkt des lokalen Cachetreffers.
Ein Zeitraumschalter lädt bei Bedarf eine andere Cachevariante; Legendenschalter bleiben lokal.

Zeitraumwechsel bricht vorherige Teilnehmer ab; Versionsprüfung verwirft verspätete
Antworten auch dann, wenn ein Testtransport Abbruch ignoriert. Globale Händler-/Moduswechsel
schließen wie bisher das Detailfenster. Schließen, Dispose und Programmende brechen
Ladevorgänge ab. Beim Nachladen werden bisherige Punkte und der Tiefstpreis aus der
Ansicht entfernt. „Preisverlauf wird geladen …“ kennzeichnet den Ladezustand. Fehler
zeigen eine Meldung ohne den alten Verlauf. Der separate RAM-Cache bleibt erhalten
und liefert ausschließlich die zum angeforderten Zeitraum gehörende Variante.

## Verifikation

Release-Build der Anwendung und Regressionstests: **0 Fehler, 0 Warnungen**.
**Alle 845 PriceWatch-Prüfungen bestanden** (bisher 790, zusätzlich 55 Historyprüfungen
einschließlich der Korrektur von Testdaten und Zeitraumdarstellung).

```powershell
dotnet build WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release --nologo -v quiet
dotnet WallpaperControl.RegressionTests/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.RegressionTests.dll --pricewatch-checks
```

Geprüft: Sortierung, Deduplikation, Grenzen, echte Nullpreise, explizite Lücken,
Konflikte, Nicht-EUR, Minimum/Filter, Cacheidentität und Ablauf/Verdrängung,
Zusammenfassung paralleler Aufrufe, Abbruch/letzter Teilnehmer, Retry-After-Weitergabe,
Offlinebeispiele für alle drei Zeiträume, Punkt-Hit-Test und Tooltipwerte,
Referenzlinien-Rendering, Standardauswahl, spätere Antworten und lokale Legende.
Bestehende Preis-/Watchlist-/Cover-/Alarmprüfungen bleiben im vollständigen Lauf enthalten.

UI-Prüfungen: alle drei Stile, Standard- und Mindestgröße, sieben Händler,
vertikale Erreichbarkeit der Aktionen und kein horizontaler Scrollbalken.
Plotgeometrie für 96/144/192 DPI geprüft. Offline-Renderings aller drei Stile erstellt
und Clean/Glow visuell inspiziert; Dateien unter `%TEMP%/PriceWatch-Phase15A1/`.
Alle neuen Lokalisierungsschlüssel in de/en/fr/es/ja vorhanden; `git diff --check` sauber.

**Nicht nachgewiesen:** echter Live-Historyabruf in dieser Phase, reales Umschalten
zwischen Windows-Monitoren mit 150/200 % DPI sowie tatsächliche Langzeitperformance
mit großen Händlerhistorien. Die DPI-Geometrieprüfung ersetzt diese manuelle Abnahme nicht.
Keine Live-API-, CDN- oder Händleranfragen während Entwicklung/Tests.

## Geänderte Dateien

Neu:

- `WallpaperControl/Forms/PriceWatchHistoryChart.cs`
- `WallpaperControl/Forms/PriceWatchHistoryView.cs`
- `WallpaperControl/Services/PriceWatchHistorySeries.cs`
- `WallpaperControl/Services/PriceWatchHistoryCache.cs`
- `WallpaperControl.RegressionTests/PriceWatchHistoryTests.cs`
- dieser Bericht; der vorhandene Analysebericht bleibt erhalten.

Ergänzt:

- `WallpaperControl/Forms/PriceWatchDetailForm.cs`
- `WallpaperControl/Widgets/PriceWatchWidgetForm.cs`
- `WallpaperControl/Services/PriceWatchClient.cs`
- `WallpaperControl/Services/PriceWatchTestData.cs`
- `WallpaperControl.RegressionTests/PriceWatchTests.cs`
- `WallpaperControl/Strings.resx` sowie `Strings.en/fr/es/ja.resx`.

## Manuelle Abnahme

1. Release-Datei `WallpaperControl/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.exe`
   verwenden. Vor der Offline-Abnahme Testdatenmodus wählen; die bereits bestehende
   Live-Preisüberwachung aus Phase 14 kann sonst unabhängig vom Diagramm fällig sein.
2. Cyberpunk 2077 oder Ultimate Edition öffnen. Wunschpreis beispielsweise 25,00 EUR
   für das Grundspiel speichern. Aktuelle Preise/Metadaten/Bedienung bleiben erhalten.
3. Nach unten scrollen und „Preisverlauf“ aufklappen: 30 Tage vorausgewählt, lokale
   Händlerlinien, Lücke, Wunschpreislinie und statistische Zusammenfassung sichtbar.
4. 7 und 90 Tage wählen. Mehrere Änderungen am selben Tag mit Maus über die tatsächlichen
   Punkte prüfen. Tooltip zeigt Zeit, Händler, Verkaufspreis und regulären Preis.
5. Händler einzeln ausblenden: Diagramm und Minimum reagieren sofort, globale
   Händlerkonfiguration bleibt bestehen. Bei aktiviertem EA wird „Keine Daten“ gezeigt.
6. Alle Händler ausblenden, wieder einschalten, Bereich zuklappen/öffnen, Details schließen
   und erneut öffnen. Keine erfundenen Linien, keine leeren Preise als Nullwerte.
7. Minimal, Clean, Glow; Standardgröße und kleinere Fenster; Größenfixierung und
   gespeicherte Größe prüfen. Unter Windows bei 100/150/200 % sowie Monitorwechsel:
   keine Überlagerungen, lesbare Achsen/Legende und alle vier Aktionen erreichbar.
8. Erst für die gewünschte Live-Abnahme ausdrücklich Live-Modus verwenden und den
   Verlauf eines vorhandenen Spiels öffnen. Es wird die vorhandene Route verwendet,
   kein Proxyupload benötigt. Händler ohne gelieferte Historie bleiben ohne Linie.
   Rasch Zeitraum wechseln beziehungsweise Fenster schließen: keine verspätete Übernahme.

Keine weiteren Arbeiten bis zur manuellen Freigabe.

## Ergänzung: Korrektur der Testdaten und Zeitraumdarstellung

Die ursprünglichen Demoereignisse waren aus der angefragten Tagesanzahl abgeleitet.
Jeder Händler erhielt dabei denselben Null-Zeitpunkt. Diese Konstruktion wurde ersetzt:

- Eine Offline-Datenquelle verankert ihre Ereignisbasis einmalig am UTC-Kalendertag
  ihrer Erstellung. Ereignisse werden nicht bei jedem Abruf neu verschoben.
  Tages-/Stundenpositionen und Preisänderungen sind pro Händler individuell festgelegt
  und unabhängig davon, ob 7, 30 oder 90 Tage angefordert werden.
- Die angefragte Zeitspanne filtert lediglich diese gemeinsame Basis. Innerhalb derselben
  Offline-Datenquelle bleiben identische Kalendertage und Ereignisse beim Wechsel
  unverändert. Bei einer neuen Quelle an einem späteren UTC-Tag wird die Demo neu
  verankert, damit sie für die interaktive Abnahme zeitlich nutzbar bleibt. Es handelt
  sich um Demonstrationsdaten, nicht um ein dauerhaftes Kalenderarchiv.
- Nur GOG und Ubisoft besitzen Null-Ereignisse zu unterschiedlichen Zeitpunkten.
  Steam, Epic, GamesPlanet und Microsoft haben davon unabhängige Preisentwicklungen;
  EA bleibt der Testfall ohne Daten.
- Beim Laden wird die alte Antwort aus der Ansicht entfernt. Der separate Cache wird
  nicht gelöscht; seine Schlüssel enthalten weiterhin UUID, Datenmodus, Shops und Zeitraum.
  Rückkehr zu einem bereits geladenen Zeitraum kann deshalb aus RAM erfolgen.
- Fehler lassen die neue Ansicht ohne alte Kurve/alten Tiefstpreis und mit einer
  verständlichen Meldung stehen. Versions-/Abbruchprüfung verwirft verspätete
  Erfolge und Fehler. Der Ladehinweis ist in allen fünf vorhandenen Sprachen vorhanden.
- Keine Änderung an Renderer, Stufen-/Lückenregeln, Händlerfarben, Wunschpreislinie,
  Tooltip oder Interpretation echter ITAD-Daten. Null bedeutet weiterhin eine
  Unterbrechung, keine behauptete bestätigte Nichtverfügbarkeit.

Zusätzliche zwölf Regressionen prüfen gemeinsame Zeitbasis für alle drei Fenster,
händlerindividuelle Lücken, unterschiedliche Verläufe, korrekte Cache-Rückkehr,
Ladehinweis ohne alte Statistik, Nachladefehler und Minimum nach erfolgreichem Retry.
Der vollständige Release-Lauf mit 845 Prüfungen ist erfolgreich; Build ohne Warnungen
oder Fehler, `git diff --check` sauber. Keine Live-Abfragen oder Änderungen am Proxy,
keine Datenbankmigration, kein Merge und kein Push.

Kurze manuelle Nachprüfung: Testdatenmodus, Cyberpunk-Details, Preisverlauf öffnen und
7/30/90 Tage vergleichen. Ereignisse im gemeinsamen Datumsbereich müssen denselben
Preis haben; bei GOGs Unterbrechung bleiben andere Händlerlinien bestehen. Bei einem
verzögerten Abruf zeigt der Wechsel zunächst den Ladehinweis statt der alten Kurve.
Der schnelle Offline-/Cachepfad kann den Hinweis nur sehr kurz zeigen; verzögerte
Antworten und Fehler sind gezielt über Mock-Antworten regressionsgeprüft.
