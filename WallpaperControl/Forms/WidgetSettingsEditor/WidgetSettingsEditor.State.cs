namespace WallpaperControl
{
    internal sealed partial class WidgetSettingsEditor
    {
        // Clock controls and previews
        private readonly CheckBox clockEnabledCheckBox;
        private readonly CheckBox clockLockedCheckBox;
        private readonly NumericUpDown clockSizeNumeric;
        private readonly CheckBox clockSecondsCheckBox;
        private readonly ComboBox clockStyleComboBox;
        private readonly ClockSettingsPreview clockSettingsPreview;
        private readonly List<ClockStyleCard> clockStyleCards = new();

        // Widget page controls
        private readonly CheckBox nextWidgetEnabledCheckBox;
        private readonly CheckBox nextWidgetLockedCheckBox;
        private readonly ComboBox nextWidgetStyleComboBox;
        private readonly CheckBox systemWidgetEnabledCheckBox;
        private readonly CheckBox systemWidgetLockedCheckBox;
        private readonly ComboBox systemWidgetRefreshComboBox;
        private readonly ComboBox systemWidgetStyleComboBox;
        private readonly CheckBox systemShowCpuCheckBox;
        private readonly CheckBox systemShowRamCheckBox;
        private readonly CheckBox systemShowGpuCheckBox;
        private readonly CheckBox systemShowVramCheckBox;
        private readonly CheckBox systemShowNetworkCheckBox;
        private readonly CheckBox systemShowDrivesCheckBox;
        private readonly CheckBox weatherWidgetEnabledCheckBox;
        private readonly CheckBox weatherWidgetLockedCheckBox;
        private readonly TextBox weatherLocationTextBox;
        private readonly ComboBox weatherWidgetRefreshComboBox;
        private readonly ComboBox weatherWidgetStyleComboBox;
        private readonly CheckBox weatherShowForecastCheckBox;
        private readonly CheckBox calendarWidgetEnabledCheckBox;
        private readonly CheckBox calendarWidgetLockedCheckBox;
        private readonly ComboBox calendarWidgetStyleComboBox;
        private readonly ComboBox calendarMaxEntriesComboBox;
        private readonly CheckBox calendarShowLocationCheckBox;
        private readonly NumericUpDown calendarMaximumHeightNumeric;
        private List<CalendarSource> calendarSources = new();
        private readonly ComboBox calendarRefreshComboBox;

    }
}
