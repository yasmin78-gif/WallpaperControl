using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog CalendarPage members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Rebuilds localized calendar-widget styles while retaining the selected style.
        /// </summary>
        private void RefreshCalendarStyleChoices(SystemWidgetStyle selectedStyle)
        {
            RefreshWidgetStyleChoices(calendarWidgetStyleComboBox, selectedStyle);
        }

        /// <summary>
        /// Returns the selected calendar style with a fallback for an empty selector.
        /// </summary>
        private SystemWidgetStyle GetSelectedCalendarStyle()
        {
            int index = calendarWidgetStyleComboBox?.SelectedIndex ?? (int)SystemWidgetStyle.Glow;
            return Enum.IsDefined(typeof(SystemWidgetStyle), index)
                ? (SystemWidgetStyle)index
                : SystemWidgetStyle.Glow;
        }

        /// <summary>
        /// Maps the entry-count selector to the number of calendar events displayed.
        /// </summary>
        private int GetCalendarMaxEntries() =>
            calendarMaxEntriesComboBox.SelectedIndex switch
            {
                0 => 3,
                2 => 9,
                _ => 5
            };

        /// <summary>
        /// Maps the calendar refresh selector to an interval in minutes.
        /// </summary>
        private int GetCalendarRefreshMinutes() =>
            calendarRefreshComboBox.SelectedIndex switch
            {
                0 => 15,
                2 => 60,
                3 => 120,
                _ => 30
            };
    }
}
