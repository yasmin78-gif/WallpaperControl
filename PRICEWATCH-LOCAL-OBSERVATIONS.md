# Phase 15A.1 – letzte Preisstände und lokale Beobachtungen

Stand: 19.11.2026. Branch `codex/pricewatch-phase-15-history`.
Ergänzt den bisherigen Diagrammstand; manuelle Freigabe ausstehend.

## 1. Bisherige SQLite-Daten

Die Quellcodeprüfung ergab vor dieser Erweiterung:

- `watched`: Spielidentität je Datenmodus, letzter JSON-Preisstand (`snapshot`),
  dessen `fetched_ticks`. Erfolgreiche neuere Preisantworten überschreiben diesen Stand.
- `PriceWatchReply.FetchedAt`: Zeitpunkt der Datenbeschaffung durch den Proxy.
  Bei einem Proxy-Cachetreffer bleibt dieser Zeitpunkt unverändert.
- `PriceWatchOffer.Timestamp`: Zeitangabe des Angebots von ITAD, nicht der Zeitpunkt
  unserer Beobachtung. Zusätzlich kann ein Angebot `Expiry` enthalten.
- `game_info`: Stammdaten; `target_prices`: Wunschpreise; `price_alerts`: Alarmzustand;
  `price_monitor_state`: letzter automatischer Versuch/Erfolg. Keine dieser Tabellen
  enthielt frühere Preisbeobachtungen.

Damit ist ein alter Snapshot als **letzter erfasster Preisstand mit seinem originalen
Abrufzeitpunkt** nutzbar. Eine rückwirkende Serie aus vergangenen Prüfungen ist daraus
nicht ableitbar. Es werden bei der Migration keinerlei Beobachtungen daraus erzeugt.
Geprüft wurde das Schema im Code und in temporären Migrationstests; keine private
Produktivdatenbank oder Zugangsdaten wurden dafür ausgelesen.

## 2. Implementierte Darstellung

Der Verlauf unterscheidet nun:

| Zeichen | Bedeutung | Zeitposition |
|---|---|---|
| Stufenlinie und kleine gefüllte Punkte | Gelieferte ITAD-Historyereignisse | Originaler Ereigniszeitpunkt |
| Offener Kreis | Lokal gespeicherte Preisbeobachtung | Originaler Proxy-Abrufzeitpunkt |
| Gefüllte Raute | Letzter erfasster gültiger Händlerpreis aus dem gespeicherten Snapshot | Dessen `FetchedAt`, niemals künstlich „jetzt“ |

Alle verwenden die bestehende Händlerfarbe. Es gibt keine Verbindung zwischen lokalen
Punkten/Rauten und ITAD-Linien und auch keine Verbindung lokaler Beobachtungen untereinander.
Die Legende erläutert die Quellen. Tooltips nennen Herkunft, Zeitpunkt inklusive Sekunden
und Zeitzonenoffset, Händler, Preis und gegebenenfalls regulären Preis. Pfeiltasten im
fokussierten Diagramm erreichen auch lokale Punkte. Marker am rechten Achsenrand bleiben
vollständig sichtbar und für Tooltips erreichbar.

Zeitraum und lokale Legendenauswahl filtern sämtliche Quellen. Die Skala umfasst alle
sichtbaren Preise und den Wunschpreis. Der separat beschriftete **ITAD-Tiefstpreis**
berechnet sich weiterhin ausschließlich aus gültigen ITAD-Historypunkten des Zeitraums
und der sichtbaren Händler. Lokale Punkte verändern ihn nicht.

Die Beschriftung „Keine historischen Preispunkte“ bezieht sich ausdrücklich auf die
ITAD-Historie. Ein solcher Händler kann trotzdem eine lokale Raute beziehungsweise
lokale Beobachtungen haben. Aktuelle Kaufbarkeit oder heutige Aktualität werden aus
einem alten Snapshot nicht abgeleitet; die Raute heißt deshalb „Letzter erfasster Preis“.

Nur gültige EUR-Angebote werden als letzte Preisstände dargestellt. Abgelaufene Angebote
erhalten keine Raute. Eine frühere, bei der Erfassung gültige Beobachtung bleibt nach
Ablauf des damaligen Angebots als **historischer lokaler Kreis** erhalten. Sie behauptet
keine heutige Verfügbarkeit. Eine bereits zum Erfassungszeitpunkt abgelaufene Beobachtung
wird nicht dargestellt. Außerhalb des gewählten Zeitfensters und bei zukünftigen
Erfassungszeitpunkten wird kein Punkt erzeugt.

Beim manuellen Abruf und nach automatischer Überwachung wird ein geöffnetes Detailfenster
aus dem neuen gespeicherten Stand aktualisiert. Dafür erfolgt kein zusätzlicher Netzabruf.
Fehlt beziehungsweise scheitert die ITAD-Historie, können gültige lokale Punkte trotzdem
angezeigt werden; die History-Fehlermeldung und der getrennte ITAD-Tiefstpreis bleiben eindeutig.

## 3. Neue SQLite-Struktur

Additive, wiederholbar ausführbare Anlage von `price_observations`:

| Feld | Inhalt |
|---|---|
| `live`, `id` | Datenmodus und ursprüngliche ITAD-UUID |
| `shop` | Händler-ID |
| `captured_ticks` | Originales `FetchedAt` als UTC-Ticks |
| `cents` | Verkaufspreis als ganzzahliger Centbetrag |
| `regular_cents` | Optionaler regulärer Preis in Cent |
| `currency` | Auf EUR begrenzt |
| `source` | Auf `local` begrenzt |
| `expiry_ticks` | Optionales Angebotsende für Validitätsprüfung |

Primärschlüssel `(live,id,shop,captured_ticks)`, Altersindex auf `captured_ticks`.
Fremdschlüssel auf `watched` mit `ON DELETE CASCADE`. Entfernen eines Spiels entfernt
dessen Beobachtungen. Bestehende Tabellen werden nicht umgebaut oder zurückgesetzt.

`SaveSnapshot` übernimmt Snapshot und neue Beobachtungen in derselben Transaktion.
Alle vorhandenen Preisabfragewege verwenden diese zentrale Methode: Erstabruf,
manueller Abruf, Händlerwechsel und automatische Überwachung. Abgewiesene verspätete
Antworten und entfernte Spiele erzeugen keine Beobachtungen. Es wird nichts allein
wegen eines Diagrammaufrufs erfasst oder abgefragt.

Je erfolgreichem Abruf wird pro Händler das günstigste gültige Angebot erfasst. Bei
mehreren Angeboten bleiben deren vollständige Informationen weiterhin im Snapshot.
Ein wiederholt geliefertes gecachtes Ergebnis mit demselben `FetchedAt` erzeugt keine
zweite Beobachtung. Gleichbleibende Preise bei tatsächlich späterer Datenbeschaffung
dürfen als spätere Beobachtung gespeichert werden. Bei sekundengleichen Antworten
ist der erste gespeicherte Beobachtungswert maßgeblich; es wird kein künstlicher
zusätzlicher Zeitpunkt erzeugt.

Die vorhandene API verlangt gültige reguläre Preise; die neue Tabelle erlaubt dennoch
nullable `regular_cents` und das Diagramm zeigt fehlende Werte nicht erfunden an.
Unveränderte Wunschpreis-, Alarm- und Preisvalidierungsregeln gelten weiterhin.

## 4. Aufbewahrung und Nutzungsbedingungen

Lokale Beobachtungen werden auf **90 Tage** begrenzt. Beim nächsten Datenbankzugriff
werden ältere Beobachtungen gelöscht; es gibt dafür keinen Timer oder Hintergrunddienst.
Bei geschlossenem Programm kann keine zeitgenaue Löschung erfolgen. Das bestehende
einzelne letzte Snapshot bleibt unabhängig davon erhalten und wird im Diagramm nur
innerhalb des gewählten Zeitraums verwendet. Rohdaten aus `/history` bleiben RAM-Daten
und werden nicht in diese Tabelle archiviert.

Die erneut geprüften offiziellen Bedingungen nennen keine ausdrückliche 90-Tage-Frist
und keine spezifische Archivierungsfreigabe. 90 Tage sind daher unsere technische
Begrenzung, **keine von ITAD bestätigte Lizenzfrist**. Herkunft bleibt erkennbar,
Originalwerte und Links bleiben erhalten. Die Zulässigkeit des privaten Piloten bzw.
einer späteren öffentlichen Verteilung und der lokalen Langzeitspeicherung ist weiterhin
am Umfang einer gegebenenfalls vorhandenen individuellen Freigabe zu prüfen; eine neue
Zusage des Betreibers wurde hier weder eingeholt noch behauptet.
[Offizielle ITAD-Bedingungen](https://raw.githubusercontent.com/IsThereAnyDeal/API/master/TERMS_OF_SERVICE.md)

## 5. Testdatenmodus und Verifikation

Der Testdatenmodus bleibt vollständig offline. Neben tatsächlich neu gespeicherten
Testpreisabrufen gibt es ausdrücklich simulierte lokale Präsentationspunkte für mehrere
Händler. Diese Demoereignisse werden nicht als echte Beobachtungen in SQLite geschrieben.
EA demonstriert einen Händler mit lokalen Preisen, aber ohne ITAD-Historie.

Release-Build: **0 Fehler, 0 Warnungen**. Vollständiger PriceWatch-Lauf:
**870 Prüfungen bestanden**, einschließlich aller bisherigen Tests und 25 zusätzlicher
Prüfungen zur Beobachtungsspeicherung/-darstellung.

```powershell
dotnet build WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release --nologo -v quiet
dotnet WallpaperControl.RegressionTests/bin/Release/net10.0-windows10.0.17763.0/WallpaperControl.RegressionTests.dll --pricewatch-checks
```

Neue Prüfungen betreffen Migration ohne Backfill, Erhalt bestehender Wunschpreis-/Alarm-
und Planungszustände, Centpreise, originalen Erfassungszeitpunkt, Deduplikation,
spätere Abrufe, Modi/Editionen, ungültige/abgelaufene Preise, verspätete Antworten,
90-Tage-Löschung, Entfernen, Snapshot-Rauten, alte lokale Punkte, Quellen-Tooltips,
getrenntes Minimum, gemeinsame Skala und Plotgeometrie bei 96/144/192 DPI.

Die bestehenden UI-Prüfungen decken Minimal/Clean/Glow sowie Standard- und kleine
Fenster ab. Offline-Renderings unter `%TEMP%/PriceWatch-Phase15A1/` wurden erneuert;
das Clean-Rendering mit lokalen Kreisen und Rauten wurde visuell geprüft. Reales
Windows-DPI-Umschalten bleibt Teil der manuellen Abnahme. Kein Live-API-/CDN-Abruf,
keine Proxyänderung, kein Deployment, Merge oder Push.

## 6. Geänderte Bereiche

- Neu: `PriceWatchStore.Observations.cs` mit Daten-/Punktmodell, Speicherung,
  Lesen und quellengetrennter Punktaufbereitung.
- `PriceWatchStore.cs`: additive Tabelle, Altersbereinigung, transaktionale zentrale Erfassung.
- `PriceWatchHistoryChart.cs` und `PriceWatchHistoryView.cs`: lokale Marker, Tooltips,
  Legende, Skala, Auswahl; statistische Trennung.
- `PriceWatchDetailForm.cs` und `PriceWatchWidgetForm.cs`: lokale Daten anbinden,
  geöffnetes Detailfenster nach automatischen Prüfungen aktualisieren.
- `PriceWatchTestData.cs`: ausdrücklich lokale Demo-Beobachtungen.
- Ressourcen aller fünf Sprachen: Quellen-/Markertexte und präzisierte Historybeschriftung.
- Neue `PriceWatchObservationTests.cs`; Ergänzungen an PriceWatch-Tests und Offline-Layoutfixtures.

## 7. Manuelle Abnahme

1. Release-Anwendung im Testdatenmodus öffnen. Cyberpunk-Details aufrufen,
   „Preisverlauf“ aufklappen. Kreise und Rauten müssen sich von ITAD-Stufen unterscheiden.
2. EA beziehungsweise einen Händler ohne Historie aktivieren: trotz „Keine historischen
   Preispunkte“ können lokale Beobachtungen und die letzte Preisraute sichtbar sein.
3. Über Raute/Kreis/ITAD-Punkt fahren: Quelle und tatsächliches Datum müssen korrekt
   unterscheidbar sein. Wunschpreis ändern: nur Referenzlinie aktualisiert sich.
4. Händler ein-/ausblenden und 7/30/90 Tage wechseln: Punkte werden entsprechend gefiltert;
   lokale Schnäppchen verändern den ausdrücklich als ITAD bezeichneten Tiefstpreis nicht.
5. Manuell Preise abrufen und Details neu öffnen: neu erfasster Stand bleibt gespeichert.
   Mehrfach identische Cacheantworten erzeugen keine weiteren Beobachtungen. Für eine
   neue Beobachtung ist ein tatsächlich späterer Beschaffungszeitpunkt erforderlich.
6. Neustart prüfen: Beobachtungen bleiben mode-/spielgebunden erhalten. Nur neue
   erfolgreiche Abrufe erzeugen Beobachtungen; alte Snapshots erzeugen keine nachträgliche Serie.
7. Minimal/Clean/Glow, Größenfixierung, kleine Fenster und 100/150/200 % DPI prüfen.
   Alle bisherigen Aktionen bleiben erreichbar. Für die Abnahme sind keine zusätzlichen
   Liveabfragen nötig; bestehende Live-Überwachung aus Phase 14 bei Bedarf vorher deaktivieren.

Auf manuelle Freigabe warten.
