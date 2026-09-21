namespace WallpaperControl
{
    internal sealed class WidgetChangesDialog : Form
    {
        internal WidgetEditDecision Decision { get; private set; } = WidgetEditDecision.Cancel;
        internal WidgetChangesDialog(bool dark, string language)
        {
            Text = Localization.Get("WidgetChangesTitle", language);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            TableLayoutPanel layout = new() { AutoSize = true, ColumnCount = 1, Padding = new Padding(20) };
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(480, 0), Text = Localization.Get("WidgetChangesPrompt", language), Margin = new Padding(4, 4, 4, 20) });
            FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
            foreach (var (key, decision) in new[] { ("SettingsCancel", WidgetEditDecision.Cancel), ("WidgetDiscard", WidgetEditDecision.Discard), ("SettingsSave", WidgetEditDecision.Save) })
            {
                Button button = CalendarSourceDialogStyle.Button(Localization.Get(key, language));
                button.Click += (_, _) => { Decision = decision; DialogResult = DialogResult.OK; Close(); };
                actions.Controls.Add(button);
                if (decision == WidgetEditDecision.Cancel) CancelButton = button;
            }
            layout.Controls.Add(actions); Controls.Add(layout);
            CalendarSourceDialogStyle.Apply(this, dark);
        }
    }
}
