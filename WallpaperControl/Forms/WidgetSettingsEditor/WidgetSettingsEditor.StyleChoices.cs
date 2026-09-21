namespace WallpaperControl
{
    internal sealed partial class WidgetSettingsEditor
    {
        private void RefreshWidgetStyleChoices(ComboBox? comboBox, SystemWidgetStyle selectedStyle)
        {
            if (comboBox == null) return;

            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.Add(Localization.Get("ClockStyleMinimal", previewLanguageCode));
            comboBox.Items.Add(Localization.Get("ClockStyleClean", previewLanguageCode));
            comboBox.Items.Add(Localization.Get("ClockStyleGlow", previewLanguageCode));
            comboBox.SelectedIndex = Math.Clamp((int)selectedStyle, 0, 2);
            comboBox.EndUpdate();
        }
    }
}
