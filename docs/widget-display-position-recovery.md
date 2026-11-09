# Kalender und Aufgaben: Position nach Anzeigeänderungen erhalten

## Befund und Ursache

Beim NVIDIA-Treiberupdate am 9. November 2026 blieben die gespeicherten Positionen
erhalten, während die laufenden Fenster verschoben wurden:

| Widget | Gespeichert | Laufendes Fenster nach dem Update |
| --- | --- | --- |
| Kalender | 2917 / 288 | 1580 / 288, Breite 340 |
| Aufgaben | 2403 / 1 | 1560 / 1, Breite 360 |

Die beiden X-Werte entsprechen exakt `1920 - Fensterbreite`. Der Bildschirm
meldete bei der Untersuchung wieder 3440 × 1440. Das spricht für einen temporär
1920 Pixel breiten Arbeitsbereich während des Treiberwechsels. Die tatsächliche
temporäre Auflösung ist im vorhandenen Anwendungsprotokoll nicht aufgezeichnet.

Beide Renderer begrenzten bei `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE` ihre aktuelle
Fensterposition auf den aktuell gemeldeten Arbeitsbereich. Auch ein gesperrtes
Widget wurde so verschoben. Bei Rückkehr der größeren Auflösung wurde die neue,
bereits gültige Position beibehalten, statt die ursprüngliche Position zu verwenden.
Die gemeinsame Desktop-Z-Order-Behandlung ist nicht die Ursache dieses Clamp-Pfads.

Die laufenden Fenster wurden anhand von Prozess-ID, Fensterhandle und Breite
geprüft und anschließend an die unverändert gespeicherten Positionen zurückgesetzt.
Dabei wurden weder Einstellungen noch Aufgaben oder Kalenderdaten geändert.

## Gezielte Korrektur

`WidgetDisplayPosition` merkt die gewünschte Position getrennt von einer temporär
sichtbaren Ausweichposition. Kalender und Aufgaben wählen den Arbeitsbereich anhand
dieser gewünschten Koordinaten und begrenzen nur die Darstellung. Sobald der
Arbeitsbereich wieder passt, wird beim erneuten Rendern die gewünschte Position
wiederhergestellt. Dafür muss die Positionssperre nicht aufgehoben werden.

Ein abgeschlossener Benutzer-Drag aktualisiert weiterhin die gewünschte Position
und verwendet den bisherigen Speichercallback. Explizites Wiederherstellen aus den
Widgeteinstellungen aktualisiert ebenfalls die gewünschte Position, ohne einen
automatischen Speichervorgang auszulösen. Es gibt keine neuen Registrywerte,
zusätzlichen Timer oder systemweiten Anzeigeänderungen.

Ein dauerhaft entfernter Monitor führt weiterhin zu einem erreichbaren
Ausweichplatz. Der ursprüngliche Wunsch bleibt erhalten, bis der Monitor zurückkehrt
oder der Benutzer das Widget bewusst neu platziert. Windows darf Fenster während
eines Grafiktreiberwechsels kurzzeitig verschieben; diese Änderung kann die
Anwendung nicht vollständig verhindern, sie übernimmt sie aber nicht als neue
gewünschte Position.

## Validierung

- 21 neue Positionsprüfungen bestanden, einschließlich echter Render-/Nachrichtenpfade
  beider Widgets mit simulierter Arbeitsfläche.
- 126 bestehende Kalenderprüfungen bestanden.
- 1061 bestehende Aufgaben-/Serienaufgabenprüfungen bestanden.
- Release-Build: 0 Warnungen, 0 Fehler; Publish erfolgreich.

Die Tests verändern weder die echte Bildschirmauflösung noch die gespeicherten
Benutzereinstellungen. Ein weiteres reales Treiberupdate wurde nicht ausgelöst.

## Manuelle Abnahme

Die bisherige Anwendung vollständig über **Beenden** schließen und
`artifacts/widget-display-position-review/WallpaperControl.exe` aus dem vollständigen
Ausgabeordner starten. Die installierte Version wird dabei nicht überschrieben;
der laufende alte Prozess enthält die Korrektur noch nicht.

1. Kalender und Aufgaben positionieren und sperren.
2. Bildschirmauflösung vorübergehend verkleinern und anschließend wiederherstellen:
   Beide Widgets müssen wieder an der ursprünglichen Position stehen.
3. Auf einem zweiten Monitor einschließlich negativer Koordinaten wiederholen;
   Monitor trennen und wieder verbinden.
4. Sperre aufheben, Widget verschieben: Die neue Position muss nach Anzeigewechsel
   und Programmneustart erhalten bleiben.
5. Höhen-/Stilvorschau sowie Verwerfen in den Widgeteinstellungen prüfen;
   Scrollen und Aufgabenbedienung müssen weiterhin funktionieren.
