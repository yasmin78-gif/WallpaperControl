# PriceWatch Phase 13B – Konfigurierbare Händlerauswahl

Implementierungsstand auf codex/pricewatch-epic-store. Die Änderungen sind zur manuellen Abnahme vorbereitet.

## Funktionsumfang

Die zentrale Händlerliste in PriceWatchShops enthält GOG (35), Steam (61), Epic Game Store (16), GamesPlanet DE (27), Ubisoft Store (62), Microsoft Store (48) und EA Store (52).

Neue Installationen starten mit GOG, Steam und Epic. Vorhandene PriceWatch-Konfigurationen ohne ShopIds erhalten weiterhin GOG und Steam. Die Händlerauswahl wird in der bestehenden Widget-Konfiguration gespeichert. Reihenfolge, Normalisierung, Kopieren der ID-Listen und mindestens ein aktiver Händler sind berücksichtigt.

Die Einstellungen zeigen sieben Checkboxen. Der letzte aktive Händler lässt sich nicht abwählen. Im Editor wird die Vorschau unmittelbar gefiltert. Nach dem Übernehmen und Beenden der Vorschau aktualisiert eine serielle Warteschlange die Preise der beobachteten Spiele. Ein normaler Programm- oder Widgetstart mit gespeicherter Auswahl löst keine Preisabfrage aus. Eine während der Deaktivierung geänderte Auswahl wird bei erneuter Aktivierung innerhalb derselben Programmsitzung aktualisiert.

Die Preis- und History-Anfragen übergeben die ausgewählten IDs. Unbekannte und nicht angeforderte Händler werden im Client abgewiesen. Die bestehende EUR- und Produktprüfung bleibt bestehen. Früher gestartete Anfragen werden bei einer Auswahländerung abgebrochen. Auch Antworten eines Anbieters, der den Abbruch ignoriert, werden anhand der Auswahlversion verworfen. Der bestehende Timeout und die Behandlung von Retry-After bleiben wirksam.

Preis-Snapshots, Watchlist und Wunschpreise behalten ihre SQLite-Struktur. Während laufender oder fehlgeschlagener Aktualisierungen bleiben gespeicherte Daten erhalten. Anzeige und Wunschpreisvergleich berücksichtigen immer die aktuelle Händlerauswahl.

Im Widget erscheinen höchstens drei Angebote, aufsteigend nach gültigem EUR-Verkaufspreis. Bei Gleichstand entscheidet die konfigurierte Reihenfolge. Pro Händler wird das günstigste gültige Angebot verwendet. Abgelaufene, ungültige und nicht vergleichbare Angebote werden ausgeblendet. Ohne gültiges Angebot erscheint Kein Angebot. Zeilenhöhe und Hit-Testing passen sich der Zahl der Preiszeilen an. Die Widgetbreite bleibt 390 logische Pixel.

Im Detailfenster erscheinen alle ausgewählten Händler in der gespeicherten Reihenfolge. Händler ohne gültiges Angebot erhalten Kein Angebot. Gemeinsamer Wunschpreis, Cover, Beschreibung, proportionale Skalierung, gespeicherte Größe und Größenfixierung bleiben verfügbar.

## Lokale Prüfungen

- Release-Build des Regressionstestprojekts einschließlich WallpaperControl erfolgreich, 0 Warnungen und 0 Fehler.
- Alle 761 PriceWatch-Prüfungen bestanden.
- Neue Tests umfassen neue und alte Einstellungen, dauerhafte Speicherung, sieben Checkboxen, Mindestanzahl, Sortierung, Gleichstand, weniger als drei Angebote, ungültige Angebote, abgelaufene Angebote, Fremdwährungen, Wunschpreise, History-Anfragen und abgewiesene Händler.
- Eine Auswahländerung bei laufendem Erstabruf wurde mit einer absichtlich verspäteten Antwort geprüft. Die alte Antwort überschreibt die neue Auswahl nicht.
- Fehlgeschlagene Aktualisierung und Rücknahme einer Vorschau wurden geprüft.
- Variable Zeilenhöhen und Händler-Hit-Testing wurden in Minimal, Clean und Glow bei 96, 144 und 192 DPI geprüft. Die Links der sortierten Angebote wurden ihren ursprünglichen URLs zugeordnet.
- Detailfenster mit sieben Händlern wurden in allen drei Stilen auf Scrollbarkeit geprüft.
- Lokale Proxy-Prüfungen bestanden mit 45 allgemeinen, 21 Phase-9- und 9 Phase-12B.1-Prüfungen. Die PHP-Syntaxprüfung der Deployment-Datei ist erfolgreich.
- Die Tests verwenden lokale Daten und simulierte HTTP-Antworten. Es wurden keine produktiven ITAD-, Proxy- oder CDN-Abfragen ausgeführt.

## Proxy und WebFTP

Das separate lokale Proxy-Projekt liegt unter C:/Users/yasmi/source/repos/PriceWatch.ItadProxy. Dieses Verzeichnis besitzt kein eigenes Git-Repository und gehört nicht zum WallpaperControl-Branch.

Geänderte Dateien im Proxy-Projekt:

- src/Query.php
- tests/Fixture.php
- tests/run.php
- deployment/webhostone/private/pricewatch/src/Query.php
- deployment/webhostone/MANIFEST.md
- deployment/webhostone/UPDATE-13B.md

Die sieben IDs werden explizit zugelassen. Ohne shops bleibt die bisherige Standardauswahl 35,61 erhalten. Authentifizierung, Schlüsselverwaltung und Antwortformat bleiben bestehen.

Nach ausdrücklicher Freigabe wird ausschließlich die Datei deployment/webhostone/private/pricewatch/src/Query.php über WebFTP nach /home/private/pricewatch/src/Query.php hochgeladen. Vorher die bisherige Serverdatei als Rückfallkopie sichern. Dateiberechtigungen beibehalten und den Upload bestätigen. Die genaue Anleitung steht in UPDATE-13B.md.

SHA-256 der vorbereiteten Query.php und der lokalen Quelldatei:

35013875e9a8bc172ebcd680520ea7d5b9953a65f1b5e64d4bda715020879bcf

## Manuelle Abnahme

1. Die bisherige WallpaperControl-Instanz vollständig schließen und den Release-Build starten.
2. Im Testdatenmodus Cyberpunk 2077 und gegebenenfalls weitere Spiele hinzufügen.
3. In den PriceWatch-Einstellungen alle sieben Händler aktivieren, speichern und die Einstellungen schließen. Die Preise werden lokal aktualisiert.
4. Im Widget die günstigsten drei Angebote prüfen. Die lokalen Beispielpreise erzeugen auch einen Gleichstand zwischen GOG und GamesPlanet DE. Deren Reihenfolge folgt der gespeicherten Auswahl.
5. Im Detailfenster alle sieben Händler und den gemeinsamen Wunschpreis prüfen. Bei kleinen Fenstern bis zu den Aktionen scrollen.
6. Nur Steam auswählen und speichern. Das Widget zeigt eine Preiszeile. Ein günstigeres deaktiviertes Angebot darf den Wunschpreis nicht treffen.
7. Einen weiteren Händler aktivieren und Steam abwählen. Der letzte aktive Händler muss erhalten bleiben.
8. Minimal, Clean und Glow sowie die eigene Windows-DPI-Skalierung prüfen. Cover, Hover-Laufschrift, Händlerlinks und Größenfixierung mitprüfen.

## Grenzen und offene Abnahme

Die tatsächlichen Live-Angebote der zusätzlichen Händler wurden noch nicht geprüft. Bis zum manuellen Proxy-Deployment für die Live-Nutzung GOG und Steam beibehalten. Der derzeitige produktive Proxy kann Anfragen mit weiteren IDs zurückweisen. Der Client behandelt diese als Fehler und erhält gespeicherte Snapshots.

Bei sieben Händlern kann die Detailansicht abhängig von Metadaten, Beschreibung, Fenstergröße und DPI vertikales Scrollen benötigen. Es wird kein regelmäßiger Hintergrundabruf eingerichtet.

Kein produktives Deployment, Merge oder Push wurde durchgeführt. Die Umsetzung wartet auf manuelle Abnahme und ausdrückliche Deployment-Freigabe.

## Korrektur nach manueller Abnahme – Widget-Schaltflächen

Beim Hinzufügen über den Suchdialog und anschließend verzögert eintreffenden Metadaten beziehungsweise Preisen konnte die Fortsetzung außerhalb des UI-Threads laufen. Dadurch konnten sichtbare Bitmap und nativer Mausbereich unterschiedliche Höhen erhalten. Ein Test reproduzierte eine Schaltfläche unterhalb des tatsächlichen nativen Clientbereichs.

Initialabruf, Auswahlaktualisierung und manuelle Aktualisierungen übergeben Änderungen an Widgetzustand und Fenster nun ausdrücklich an den UI-Thread. Beim Beenden werden auch bereits wartende UI-Aufträge abgebrochen.

Gezielte Regressionen prüfen obere, mittlere und untere Klickpositionen beider Schaltflächen nach Größenwachstum bis 20 Einträge, nach echten lokalen Suchdialogen mit verzögerten Antworten sowie nach manuellen Preisabrufen. Die Tests vergleichen Layout, nativen Clientbereich, Windows-Hit-Testing und den tatsächlichen gedrückten Aktionszustand. Der aktualisierte Release-Build hat 0 Warnungen und 0 Fehler. Alle 761 PriceWatch-Prüfungen sind bestanden. Die Daten bleiben vollständig lokal beziehungsweise simuliert.

Für die erneute manuelle Abnahme WallpaperControl vollständig beenden, den aktualisierten Release-Build starten, ein weiteres Testspiel hinzufügen und beide unteren Schaltflächen über ihre gesamte Höhe prüfen.
