# PriceWatch Phase 15A – Analyse und Planung des Preisverlaufs

Stand: 19.11.2026. Ausgangspunkt: `master`, Commit
`9bddbb9a75063b12536912ad0c391e3ade4af736`.
Analysebranch: `codex/pricewatch-phase-15-history`.

**Ergebnis:** Ein grafischer Verlauf ist mit der vorhandenen Datenquelle technisch
machbar. 30/90 Tage können auf dem bestehenden Proxyvertrag aufbauen. Für ein Jahr
und „Gesamt“ reicht der heutige Vertrag nicht aus. Eine vollständige historische
Abdeckung und eine zeitlich unbegrenzte Archivierungsberechtigung sind nicht belegt.
Empfohlen wird ein schlankes selbst gezeichnetes WinForms-Control mit expliziter
Herkunft und Abdeckungsangabe, ohne neue Diagrammbibliothek.

## 1. Umfang und Beleglage

Geprüft wurden der C#-Quellstand und die lokalen PHP-Dateien im separaten Verzeichnis
`C:/Users/yasmi/source/repos/PriceWatch.ItadProxy`. Dieses Verzeichnis ist selbst kein
Git-Repository. Daraus lässt sich der aktuell hochgeladene Serverstand nicht verifizieren.
Keine privaten Konfigurationen oder Zugangsdaten wurden gelesen.

Die Dokumentationswebsite antwortete beim direkten Abruf mit HTTP 403. Die Untersuchung
verwendet deshalb zusätzlich die offiziellen Quelldateien im Repository von ITAD.
Keine authentifizierten API-/Proxy-Anfragen, keine Händlerabfragen, keine Builds oder
Tests; alle technischen Aussagen beruhen auf statischer Prüfung und Dokumentation.
Die frühere Live-Abnahme von `/history` ist Nutzerangabe aus dem bisherigen Projektverlauf,
kein neuer Test und kein Nachweis für Jahres-/Gesamtzeiträume.

## 2. Tatsächlich vorhandene Daten

### ITAD-Vertrag

`GET /games/history/v2` liefert ein Änderungsprotokoll für eine Spiel-UUID.
Parameter: `id`, `country`, `shops`, `since`. Ohne `since` werden standardmäßig die
letzten drei Monate geladen. `since` fordert Preisänderungen nach einem Zeitpunkt an;
ein dokumentierter Parameter für `until`, Seitenzahlen oder einen Gesamtzeitraum fehlt.
Die Spezifikation nennt keine garantierte maximale Rückschau und keine Garantie
vollständiger Händlerabdeckung. [Offizielle Route](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/docs/routes/games/history.v2.yaml)

Jeder Eintrag enthält `timestamp`, `shop.id/name` und `deal`. Ein vorhandenes `deal`
enthält Verkaufspreis `price`, regulären Preis `regular` und Rabatt `cut`; Geldwerte
enthalten `amount`, `amountInt`, `currency`. `deal` darf auch `null` sein.
Reguläre historische Preise sind damit an gelieferten Ereignissen verfügbar,
jedoch keine unabhängig lückenlose Listenpreisserie. Die History enthält keine
Angebots-URL, kein Ablaufdatum und keine historische DRM-/Plattformzuordnung.
[Antwortschema](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/docs/routes/games/schemas/resp.games.history.yaml),
[Beispiel](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/docs/examples/games/history.v2.success.json)

Ein historischer Datenpunkt bedeutet einen von ITAD gemeldeten Änderungszustand
zu einem Zeitpunkt. Er ist kein Kaufbeleg und keine Bestätigung heutiger Kaufbarkeit.
Die genaue Erfassungsverzögerung, Bedeutung sämtlicher Null-Ereignisse und vollständige
Rekonstruktion an der `since`-Grenze werden nicht ausreichend erläutert. Sicher ist:
`null` darf niemals zu 0 EUR werden. Geplanter Text: „Keine Preisinformation“.
Die frühere Korrektur von Shopfilter und `since`-Grenze im ITAD-Changelog spricht
zusätzlich für ausdrückliche Grenztests statt Annahmen über das Verhalten.
[Changelog 2.5.0](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/CHANGELOG.md)

### Bestehender PHP-Proxy

| Eigenschaft | Aktueller lokaler Code |
|---|---|
| Route | `POST /history` |
| Body | `{"id":"ITAD-UUID","shops":[35,61],"days":90}` |
| Upstream | `GET /games/history/v2`, `country=DE`, UUID, ausgewählte Shops, `since` |
| Erlaubte Tage | Nur 7, 30, 90; Standard 90 |
| Erlaubte Shops | 35 GOG, 61 Steam, 16 Epic, 27 GamesPlanet DE, 62 Ubisoft, 48 Microsoft, 52 EA |
| Filter | Nur ausgewählte Shops und EUR-Preis/Regulärpreis; Null-Ereignisse bleiben erhalten |
| Envelope | `source`, `sourceUrl`, `fetchedAt`, `data` |
| History-Cache | 900 Sekunden; Schlüssel aus Route, Parametern und Body |
| Zeitgrenze | `since` auf volle UTC-Stunde abgerundet, danach Tagesanzahl abgezogen |
| Upstreamgrenzen | 256 KiB Antwort, 1,5 s Verbindungs- und 5 s Gesamttimeout |

Folgen: 365 Tage und „Gesamt“ liefern heute ungültige Parameter. Der angeforderte
Zeitraum kann bis knapp eine Stunde vor der exakten Anzeigegrenze beginnen; clientseitig
korrekt beschneiden. `fetchedAt` ist der Beschaffungszeitpunkt des Proxyresultats,
nicht der Ereigniszeitpunkt und bei Cachetreffern kein neuer Händlerabruf.

`Payload.php` verwirft Nicht-EUR-Ereignisse. Das kann eine unsichtbare Unterbrechung
erzeugen: Der Client sieht anschließend nicht, dass ein Punkt weggefiltert wurde.
Für eine streng lückentreue Darstellung sollte eine spätere Proxyänderung verworfene
ausgewählte Händlerereignisse als Qualitäts-/Lückenmarker melden, ohne fremde Währungen
in EUR umzudeuten. Historische Fehlerantworten sind keine leere erfolgreiche Historie.

Relevante Dateien: `src/Query.php`, `Payload.php`, `Proxy.php`, `Store.php`, `Transport.php`;
bestehende Mock-Prüfungen in `tests/run.php` und `phase9.php`.

### C# und SQLite

- `PriceWatchClient.cs` besitzt bereits `PriceWatchHistory`, `PriceWatchChange` und
  `HistoryAsync(id, days, shops, token)`. Händler, EUR-Geldwerte und Zeitpunkte werden geprüft.
- `IPriceWatchSource` bietet bislang **keine** History-Methode; `PriceWatchTestData`
  und die UI können History deshalb noch nicht über den gemeinsamen Quellenvertrag laden.
- `PriceWatchReply<T>.FetchedAt` und `PriceWatchHistory.Timestamp` sind bereits getrennt.
- `PriceWatchStore.SaveSnapshot` überschreibt `watched.snapshot` mit dem letzten
  akzeptierten Preisstand. Es gibt **keine** Tabelle mit älteren Preisbeobachtungen.
- `game_info`, Wunschpreise und Alarmzustände enthalten ebenfalls keinen Preisverlauf.
- Ein bestehender Clienttest prüft eine History-Antwort mit `deal=null`; das ersetzt
  keine Diagramm-, Abdeckungs- oder Langzeitprüfung.

Somit steht je Spiel zunächst maximal der zuletzt gespeicherte lokale Snapshot zur
Verfügung. Frühere Prüfungen aus Phase 14 sind nicht rückwirkend rekonstruierbar.
Eine künftige Beobachtungstabelle müsste ausdrücklich ergänzt werden.

## 3. Datenqualität, Kombination und Tiefstpreis

### Identität

Schlüssel immer aus Datenmodus, ITAD-UUID, Land DE, Währung EUR und Händler-ID bilden.
Grundspiel, DLC und Sonderedition niemals über ähnlich geschriebene Titel zusammenführen.
Die Historyantwort wiederholt die Spiel-UUID nicht: Requestkontext muss mit dem Ergebnis
verbunden bleiben. Bei Spielwechsel oder Entfernen verspätete Ergebnisse verwerfen.
ITAD-Zuordnungsfehler oder spätere Katalogkorrekturen bleiben möglich; eine alte Edition
ist nicht allein aufgrund eines plausiblen Datums zuverlässig bestätigt.

### Darstellung der Quellen

1. ITAD-Ereignisse chronologisch je Händler sortieren, Originalwerte behalten.
2. Gleiche Händler/Zeitpunkte/Werte innerhalb derselben Quelle deduplizieren.
   Widersprüchliche Werte am selben Zeitpunkt markieren und Verbindung unterbrechen.
3. Lokale Beobachtungen als eigene Marker mit Herkunft „Lokal beobachtet (ITAD-Proxy)“
   einblenden. Das ist eine lokale Erfassung derselben Quelle, keine zweite Händlerquelle.
4. Originaler Angebotstimestamp, Proxy-`fetchedAt` und lokaler Empfangszeitpunkt bleiben
   getrennt. Identische gecachte Antworten erzeugen keine künstlich neuen Beobachtungen.
5. Lokale Einzelmessungen nicht durch eine durchgehende Linie verbinden und nicht zur
   Auffüllung unbekannter ITAD-Intervalle verwenden. Widersprüche im Tooltip kenntlich machen.

### Linien und Lücken

Keine schräge oder geglättete Interpolation. Vorgeschlagen ist eine Stufenlinie für
ITAD-Änderungen: „zuletzt von ITAD gemeldeter Preis“, keine behauptete permanente
Kaufbarkeit. Das Halten zwischen Ereignissen ist eine explizite Darstellungsannahme
des Änderungsprotokolls, keine zusätzliche Messung. Vor Freigabe muss dieses Verhalten
an dokumentierten/gezielten Beispielen einschließlich Null- und Grenzereignissen bestätigt
werden. Andernfalls zunächst nur Ereignismarker anzeigen.

Unterbrechungen bei Null-Ereignissen, ungültigen Daten, bekannter fehlender Abdeckung
und Quellenwechsel. Kein Preis vor dem ersten belegten Ereignis, keine Verlängerung
bis heute nach dem letzten Ereignis ohne begründete Zustandsinformation. Ein Zeitraum
ohne Änderung allein beweist keine Lücke, weil die Quelle ein Änderungslog ist.
Umgekehrt kann eine nicht dokumentierte Erfassungslücke aus der Antwort nicht sicher
erkannt werden; das muss die Quellen-/Qualitätsangabe deutlich machen.

Am linken Zeitrand nicht automatisch den ersten späteren Preis rückwärts einsetzen.
Ein ausdrücklich belegter vorangehender Zustand darf als Randzustand dienen; andernfalls
bleibt der Beginn unbekannt. Zeitpunkte intern in UTC, Anzeige in lokaler Windows-Zeit.

### Tiefstpreis

Minimum ausschließlich aus gültigen gelieferten EUR-Verkaufspreisen, einschließlich
echter 0-EUR-Angebote. Beschriftung: **„Niedrigster erfasster Preis im Zeitraum“**, mit
Händler, Datum und Herkunft. Bezieht sich auf aktuell eingeblendete Händler und den
geladenen Zeitraum; bei Teilabdeckung entsprechend kennzeichnen. Kein behauptetes
Allzeitminimum. Bei fehlenden Preisen „Nicht bestimmbar“.
`regular` kann im Tooltip stehen; keine weitere Linie im ersten Ausbau nötig.

## 4. Abruf, Limits, Cache und Rechte

Offiziell gilt ein anpassbares Kontolimit; dokumentierter Standard für verifizierte
Konten: 1000 Anfragen je fünf Minuten, tatsächliche Werte auf der App-Verwaltungsseite.
ITAD verlangt sinnvolles Caching; 429 enthält `Retry-After`.
[Offizielle OpenAPI-Einleitung](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/dist/openapi.json)

Unser Proxy ist restriktiver: 30 Clientanfragen pro 60 Sekunden inklusive Cachetreffern,
8 davon Suche/Lookup; global maximal 60 Upstreamversuche je 300 Sekunden und nur ein
gleichzeitiger Upstreamabruf. Sperrzeiten und `Retry-After` werden weitergegeben.
Cache: höchstens 64 Einträge, 4 MiB Gesamtzustand; Ablaufbereinigung und Verdrängung.
Die Grenze des C#-Clients liegt bei 512 KiB Antwort und standardmäßig 12 Sekunden
Timeout. Insbesondere Jahres-/Gesamtantworten können schon an 256 KiB im Proxy scheitern.

**Abrufvorschlag:** Nur beim Öffnen des Historybereichs bzw. expliziter Aktualisierung,
keine Abfrage aller 20 Spiele beim Widgetstart und keine Erweiterung der regelmäßigen
Phase-14-Prüfung um History. Ein Request umfasst alle aktivierten Händler. Ein-/Ausblenden
vorhandener Serien ist rein lokal. RAM-Cache mit 15 Minuten Frische und begrenztem Budget,
identische laufende Requests zusammenfassen. Beim Zeitraumwechsel zuerst bereits abgedeckte
Daten filtern; sonst gezielt nachladen. Gemeinsame Warteschlange/Client verwenden,
Preisabfragen priorisieren, Abbruch und `Retry-After` beachten. Fehler erhalten vorhandene
Daten mit sichtbarem Aktualitäts-/Fehlerstatus.

**Rechte:** ITAD verlangt unveränderte Daten/Links, Herkunftshinweis und verbietet
konkurrierende Anwendungen. Die aktuelle OpenAPI-Einleitung erlaubt persönliche private
Nutzung; die separate Terms-Datei formuliert private Nutzung enger und verweist an den
Betreiber. Eine feste Cache-/Aufbewahrungsfrist oder ausdrückliche unbefristete lokale
Archivierungsfreigabe ist in den geprüften Texten nicht genannt. Daraus folgt weder eine
belegte pauschale Erlaubnis noch ein belegtes generelles Speicherverbot. Bestehende
individuelle Freigaben wurden hier nicht vorgelegt/geprüft. Vor dauerhafter Historienarchivierung
und öffentlicher Verteilung sollten deren Umfang beziehungsweise ITADs Bestätigung die
lokale Speicherung, Diagramme, private Pilotnutzung und Konkurrenzfrage abdecken.
[Terms-Datei](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/TERMS_OF_SERVICE.md),
[aktuelle API-Einleitung](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/dist/openapi.json)

## 5. Diagrammtechnik und Layout

### Technikentscheidung

| Variante | Bewertung |
|---|---|
| Eigenes doppelt gepuffertes WinForms-Control, GDI+ | Empfehlung: keine neue Abhängigkeit, volle Kontrolle über Lücken, Quellenmarker und Stil. Achsen, Hit-Test, Tooltip und Tastaturzugang selbst implementieren. |
| ScottPlot.WinForms | Offizielles WinForms-Control, MIT-lizenziert; sinnvolle Alternative bei späterem Zoom-/Analysebedarf. Neue Paket-/Renderingabhängigkeiten und konkrete .NET-10-/Publishprüfung nötig; in dieser Phase nicht installiert/getestet. |
| HTML/JavaScript in WebView2 | Bereits als Paket vorhanden, für dieses kleine Diagramm jedoch zusätzliche Web-/Renderinglogik und Abstimmung zwischen zwei UI-Systemen. Nicht empfohlen. |

Microsoft dokumentiert `OnPaint`, `Graphics` und kontrolliertes Freigeben von GDI-Ressourcen
sowie `OptimizedDoubleBuffer`. Das passt zur vorhandenen WinForms-Anwendung.
[Custom Painting](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/custom-painting-drawing),
[Double Buffering](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/how-to-reduce-graphics-flicker-with-double-buffering-for-forms-and-controls).
ScottPlots offizielle [WinForms-Anleitung](https://scottplot.net/quickstart/winforms/)
und [MIT-Lizenz](https://github.com/ScottPlot/ScottPlot/blob/main/LICENSE) bestätigen die
grundsätzliche Alternative, nicht ihre ungeprüfte Eignung im konkreten Projekt.

### Aufbau im Detailfenster

Vorhandenen Titel-/Coverbereich, Metadaten, Preistabelle und Wunschpreis erhalten.
Darunter ein eigener Bereich:

```text
Preisverlauf             [30 Tage] [90 Tage] [1 Jahr] [Gesamt]
[x] GOG  [x] Steam  [x] Epic ...          [Aktualisieren]

EUR │     Händlerlinien / Ereignispunkte
    │ - - Wunschpreis - - - - - - - - - - - - -
    └────────────────────────────────────────── Datum
● ITAD-Änderung   ◇ Lokal beobachtet
Niedrigster erfasster Preis: …     Abdeckung / Datenstand: …
```

Alle sieben aktivierten Händler anbieten, nicht nur die drei günstigsten Widgetangebote.
Händlerauswahl für das Diagramm ist ein lokaler Sichtbarkeitsfilter und verändert
nicht die globalen überwachten Händler oder Alarmzustände. Stabile Händlerfarben,
zusätzliche Strichmuster/Marker für Lesbarkeit ohne reine Farbcodierung. Wunschpreis
als beschriftete Referenzlinie, bei fehlendem Ziel keine Linie.

Tooltip zeigt tatsächlichen Punkt: Datum/Uhrzeit, Händler, EUR-Preis, optional
Regulärpreis/Rabatt, Quelle. Keine interpolierten Tooltipwerte. Bei dichtliegenden
Punkten Auswahl beziehungsweise mehrere Einträge. Tastaturfokus und Pfeiltasten
ermöglichen dieselben Informationen ohne Maus; ergänzende Textzusammenfassung.

Empfohlene Diagrammhöhe ca. 190–230 logische Pixel, volle Inhaltsbreite. Das bestehende
700×580-Standardfenster bleibt proportional skalierbar und speichert seine Größe;
mehr Inhalt bedeutet bei Standardgröße voraussichtlich vertikales Scrollen. Keine
stille Vergrößerung der freigegebenen Standardgröße. Bestehendes äußeres Scrollpanel
verwenden, keine verschachtelte Diagrammscrollbar. Aktionsschaltflächen bleiben erreichbar.
Die Größenfixierung sperrt nur manuelles Resizing, nicht interne Layoutberechnung oder
Arbeitsflächenbegrenzung.

Minimal/Clean/Glow aus vorhandenen Detailfarben ableiten. Glow nur dezent an Konturen,
keine unscharfen Datenlinien. DPI 100/150/200 %, Monitorwechsel, schmale Arbeitsflächen
und lange lokalisierte Beschriftungen berücksichtigen; Legende/Zeitraumleiste dürfen
umbruchfähig sein. Achsenlabels abhängig von tatsächlicher Pixelbreite ausdünnen.

### Leistung

Netzwerk asynchron, keine Arbeit in `OnPaint`, keine Animation und kein dauernder Timer.
Immutable Datenserien außerhalb des Paintpfads vorbereiten, UI-Übernahme über bestehenden
Dispatcher. Zeichnen nur bei Resize, Daten-/Sichtbarkeitsänderung oder Hover. Bei großen
Serien Geometrie nach sichtbarem Zeitraum begrenzen und Darstellung pro Pixelspalte
verdichten, ohne Minima/Maxima oder Lückenmarker zu verlieren. Originalwerte für Tooltip
und Minimum behalten. Bei einem Schutzlimit Daten als teilweise geladen kennzeichnen,
nicht still abschneiden. Speicherziel für History-RAM-Cache beispielsweise 8 MiB,
LRU-Verdrängung; konkrete Grenzen nach Messung festlegen. Noch keine Leistungsmessung.

## 6. Notwendige Änderungen nach Freigabe

1. **Quellenvertrag/Client:** `IPriceWatchSource.HistoryAsync`, Offline-Testquelle,
   klarer Zeitraumtyp statt magischer Tageszahlen. History-Ergebnis mit Requestidentität,
   Quelle, Beschaffungszeit und Abdeckungsinformationen verbinden.
2. **Proxy:** 365-Tage-Zeitraum ergänzen; für „Gesamt“ einen expliziten begrenzten Vertrag
   vereinbaren. „Kein since“ ist ausdrücklich keine Gesamthistorie. Möglicherweise
   festgelegter frühester Zeitpunkt, jedoch ohne Vollständigkeitsbehauptung. Für große
   Antworten begrenzte Zeitfenster mit `since` und serverseitigem Endfilter prüfen:
   ohne Upstream-`until` reduziert solches Filtern die empfangene Upstreamgröße nicht.
   Deswegen nicht als bewiesene Lösung für das 256-KiB-Limit verkaufen. Wenn nötig
   sorgfältig begrenzte Größen-/Timeoutanpassung separat freigeben. Qualitätsmarker
   für weggefilterte Ereignisse und Abdeckungsvertrag ergänzen. Auth/Keys unverändert.
3. **Normalisierung:** Neue reine `PriceWatchHistorySeries`-Logik für Sortierung,
   Widersprüche, Lücken, Herkunft, Sichtbarkeit und Minimum. Keine Alarmneuberechnung
   anhand historischer Preise.
4. **Laden/Cache:** `PriceWatchHistoryService`, begrenzter RAM-Cache, Requestzusammenfassung,
   Cancellation, Händler-/Spielwechsel und Quellenmodus. Eine einzelne Änderung der
   Legendencheckbox startet keinen Netzabruf.
5. **Darstellung:** Neues `PriceWatchHistoryControl`, Integration in `PriceWatchDetailForm`,
   dessen Skalierung/Scrolllayout und bestehende Widget-UI-Komponenten; Ressourcen
   für alle fünf vorhandenen Sprachen. Widgetliste unverändert.
6. **Optionale dauerhafte Speicherung, erst nach Rechteklärung:** additive SQLite-Tabellen
   für rohe History-Ereignisse, lokale Beobachtungen und separat geladene Zeitintervalle.
   Centbeträge, nullable Preise für Lücken, UTC, Herkunft, UUID/Modus/Land/Shop als Schlüssel.
   Snapshotannahme und Beobachtungseintrag transaktional; alle akzeptierten manuellen,
   Erst- und automatischen Preisaktualisierungen erfassen. Fehlversuche als Qualitätsstatus,
   niemals als 0 EUR. Gleiche Cacheantwort nicht mehrfach zählen. Bestehenden letzten
   Snapshot höchstens einmal als solchen importieren, keine erfundene Vergangenheit.
   Fremdschlüssel/Entfernung, Aufbewahrung und Deduplikation ausdrücklich festlegen.

## 7. Implementierungsplan und Freigabepunkte

| Paket | Lieferumfang | Abnahme |
|---|---|---|
| 15B.1 Datenvertrag | 30/90 Tage, Offlinefixtures, Normalisierung, Lücken/Quellen/Minimum | Unit-/Mockprüfungen, keine Liveabrufe |
| 15B.2 Diagramm/UI | Control, Zeiträume, Legende, Wunschlinie, Tooltips/Tastatur, drei Stile | DPI, Resize/Fixierung, leere/lange Reihen, kein Überdecken |
| 15B.3 Beschaffung/Cache | Gemeinsamer Client/Queue, RAM-Cache, Abbruch, Fehler-/Retry-After-Zustände | Kein Abruf bei bloßem Legendenwechsel, keine Preis-/Alarmlogikregression |
| 15B.4 Lange Zeiträume | Proxyvertrag für 365/Gesamt, Abdeckung und Antwortgrößen | Erst nach gesonderter Freigabe, gezielter Serverabnahme statt Testserie |
| 15B.5 Lokale Historie | Rechtegeklärte additive Speicherung und neue Beobachtungserfassung | Migration/Deduplikation/Quellen-/Modustrennung, keine rückwirkenden Fantasiedaten |

Schätzung: etwa 5–9 Entwicklungstage für UI, Client und Offline-QA; weitere 2–4 Tage
für dauerhafte Speicherung, große Zeiträume und deren QA. Grobe Planung, keine gemessene
Aufwandsgarantie; Rechte-/Betreiberklärung und manuelle Serverbereitstellung zusätzlich.

Vor Umsetzung ausdrücklich entscheiden:

- Stufenlinien als „zuletzt gemeldeter Zustand“ akzeptieren oder zunächst nur Punkte?
- „Gesamt“ als **gesamte geladene/erfasste Historie** beschriften; echte Allzeitabdeckung
  nur mit belastbarem Nachweis. Frühestes Datum und technische Grenzen klären.
- Proxyerweiterungen separat freigeben; 365/Gesamt nicht mit heutiger Route vortäuschen.
- Dauerhafte Archivierung samt Aufbewahrung und öffentliche Nutzung durch vorhandene
  Freigabe nachweisen oder beim Betreiber klären. Bis dahin RAM-Verarbeitung planen.
- Zusätzliche vertikale Scrollstrecke im unverändert großen Detailfenster akzeptieren.

Geplante QA: Null/0-EUR, Nicht-EUR, fehlender Händler, Sommerzeit, gleichzeitige
widersprüchliche Punkte, linke Zeitgrenze, lange unveränderte Preise, abgelaufene
Cacheantwort, unvollständige Antworten, Größenlimit, Abbruch/Entfernen, Händlermoduswechsel,
Tiefstpreis je sichtbarer Serie sowie vollständig netzfreier Testdatenmodus. Nach späterer
Implementierung Release-Build und relevante Regressionen. In Phase 15A nicht ausgeführt.

**Aktueller Änderungsumfang:** ausschließlich dieser Bericht und der neue Branch.
Kein funktionaler Code, keine Datenbankmigration, kein Proxyupdate, Deployment, Merge oder Push.
