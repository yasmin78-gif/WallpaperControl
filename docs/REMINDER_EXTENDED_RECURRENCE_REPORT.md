# Notizen & Reminder: erweiterte Wiederholungen

Stand: 24. November 2026. Entwicklungsbranch: `codex/reminder-extended-recurrence`, ausgehend von einem sauberen `master`.

## Implementierung

- WinForms und .NET 10 bleiben erhalten. Die Auswahl enthält Keine, Täglich, Monatlich, Jährlich und Individuell.
- Individuell bietet eine positive ganzzahlige Anzahl und Tage, Wochen, Monate oder Jahre. Die Zusammenfassung aktualisiert sich unmittelbar; ausgeblendete Zusatzzeilen geben ihren Platz frei. Eingabe- und Datumsüberläufe verhindern das Speichern mit einer verständlichen Fehlermeldung.
- Alle Kalendertermine werden vom ursprünglichen Referenzdatum abgeleitet. Monatsenden und der 29. Februar kehren nach vorübergehender Verkürzung wieder zum ursprünglichen Tag zurück. Wochen entsprechen sieben Kalendertagen.
- Monatliche, jährliche und individuelle Serien behalten ihren offenen Termin bis zur Erledigung. Danach wird der nächste zukünftige Termin im ursprünglichen Rhythmus ermittelt. Ein am selben Tag noch bevorstehender Termin bleibt dabei erhalten. Die bestehende tägliche Wiederholung öffnet sich weiterhin am nächsten lokalen Kalendertag automatisch.
- Ein gesonderter Abschlussbezug identifiziert auch vorzeitig über ein Popup erledigte Termine. Übersprungene Termine erzeugen keine Popup-Serie. Die vorhandenen Vorlaufzeiten, Snooze-Funktion und persistierte Duplikatunterdrückung bleiben erhalten.
- Datum und Uhrzeit bleiben lokale Kalenderwerte ohne UTC-Addition. Dadurch bleibt die eingestellte lokale Uhrzeit über Sommer-/Winterzeitwechsel erhalten. Die bestehende Popup-Sperre gegen doppelte Zustellung gilt auch während der zurückgestellten Stunde.
- Widget und Verwaltung zeigen die neuen Wiederholungen verständlich an. Checkboxen verwenden explizit die Dialogpalette. Neue Texte sind für Deutsch, Englisch, Französisch, Spanisch und Japanisch vorhanden. Die Tab-Reihenfolge folgt den sichtbaren Eingabezeilen.

## Datenkompatibilität

- `RepeatsDaily` bleibt unverändert vorhanden. Ältere Dokumente ohne neue Eigenschaften erhalten kompatible Standardwerte; bestehende Versionsnummern 1–3 werden weiter gelesen.
- Neue Felder: `Repeat`, `RepeatInterval`, `RepeatUnit`, `RepeatAnchor`, `RepeatCompletedThrough`. Die neuen Enums haben explizite, stabile numerische Werte.
- Dokumente mit erweiterten Serien verwenden Version 4. Ältere Programmversionen lehnen diese Version kontrolliert ab, statt die Serien als einmalige Termine zu behandeln.
- Das Laden führt keine Migration und keine Schreiboperation aus. Erst ein ausdrücklicher Speichervorgang schreibt die neuen Felder. Die bestehende atomare Ersetzung erstellt zuvor `notes.json.bak`; bei einem Fehler bleibt die bisherige Datei erhalten.
- Es werden keine produktiven Notizen für automatisierte Tests benötigt. Diese verwenden eigene Dateien unter dem Testausgabeverzeichnis.

## Validierung

- Bestehender vollständiger Regressionstestlauf: **6.155 Prüfungen erfolgreich** (`dotnet run --project WallpaperControl.RegressionTests -c Release`). Protokoll: `artifacts/recurrence-regression.log`.
- Abschließender gezielter Lauf einschließlich bestehender Notiz-, täglicher Wiederholungs- und Popup-Tests: **866 Prüfungen erfolgreich** (`dotnet run --project WallpaperControl.RegressionTests -c Release -- --extended-recurrence`). Protokoll: `artifacts/recurrence-final-focused.log`.
- Die neuen Tests decken Monatsenden mit 28/29/30/31 Tagen, Schaltjahre, Jahreswechsel, alle vier individuellen Einheiten, Sommer-/Winterzeit, verspätete und vorzeitige Erledigung, einen noch bevorstehenden heutigen Termin, Überläufe, Speichern/Laden, fortgesetzte Serien nach Neustart, Bearbeitung, Auswahlwechsel und Vorlauf-/Duplikatverhalten ab. Die bestehenden Tests prüfen einmalige und tägliche Erinnerungen sowie ältere Datenformate.
- Individuelle Eingabezeilen wurden für fünf Sprachen, Light/Dark Mode und 96/144/192 DPI auf Überlappungen und abgeschnittene Elemente geprüft. Zusätzlich wird das tatsächliche Zeichnen heller deaktivierter Checkbox-Texte im Dark Mode über Bitmap-Pixel geprüft.
- Der breite Lauf fand vor der abschließenden Zeichenkorrektur für deaktivierte Checkboxen statt. Danach wurden die betroffenen bestehenden Notiz- und Popup-Regressionstests zusammen mit den neuen Fällen erneut erfolgreich ausgeführt.
- Abschließender Release-Build von `WallpaperControl.sln`: **0 Warnungen, 0 Fehler**. Auch das Testprogramm einschließlich Abnahmevorschau wurde erfolgreich in Release gebaut. `git diff --check` ist sauber.
- Während der Entwicklung wurden ein DPI-Zeilenfehler und eine falsche Freigabereihenfolge einer Testschrift erkannt und korrigiert. Ein überlappender Buildversuch war durch die laufende Regression gesperrt; die abschließenden Läufe erfolgten nacheinander erfolgreich.

## Manuelle Abnahme

- Der neue WallpaperControl-Release wurde gestartet und die normale Notizverwaltung einschließlich des leeren Eintragsdialogs geöffnet. Die hellen Texte der deaktivierten Checkboxen wurden visuell bestätigt.
- Einschränkung der Desktop-Steuerung: Die verschachtelten, nicht in der Taskleiste geführten Dialoge lassen sich anzeigen, aber ihre Eingaben werden durch die Aktivierung des Hauptfensters abgefangen. Deshalb wurde für die vorgegebene Einstellung ein reproduzierbarer Vorschauaufruf im Testprogramm ergänzt: `--recurrence-preview`.
- Diese Vorschau verwendet die unveränderte produktive Klasse `NoteEditorForm` aus dem Release-Build, mit eigenem Taskleisteneintrag und einer separaten temporären `NotesStore`-Datei. WallpaperControl selbst läuft im Tray weiter. Die Vorschau verändert keine produktiven Daten; auch ein manuelles Speichern in der Vorschau beträfe ausschließlich die temporäre Datei.
- Der sichtbare Dialog **Neuer Eintrag** steht auf **Individuell**, Anzahl **2**, Einheit **Wochen**; die Zusammenfassung lautet **Alle 2 Wochen**. Titel und Notiz bleiben leer. Es wurde nichts gespeichert.
- Die produktive `notes.json` hat vor und nach der Abnahmevorbereitung denselben SHA-256-Hash: `0438BAFB191087885FB0DFF009269B25AD5DE115C079BF9F7F6E2A697B671E88`.
- Die fachliche manuelle Abnahme durch die Nutzerin steht aus. Keine bekannten offenen Implementierungsfehler; die beschriebene Einschränkung betrifft die UI-Automation der Abnahmevorbereitung.

## Auslieferung

Kein Merge in `master`, kein Push und keine Löschung des Entwicklungsbranches. Die Änderungen einschließlich dieses Berichts werden gemeinsam committed. Der Commit wird in der Abschlussnachricht genannt.
