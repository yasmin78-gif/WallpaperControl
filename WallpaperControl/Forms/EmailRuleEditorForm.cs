namespace WallpaperControl;

internal sealed class EmailRuleEditorForm : Form
{
    internal EmailRule? Result { get; private set; }
    internal EmailRuleEditorForm(EmailRule? rule, string language, bool dark)
    {
        Text = Localization.Get(rule == null ? "EmailRuleAdd" : "EmailRuleEdit", language); ClientSize = new(540, 290); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 490 };
        foreach (string key in new[] { "EmailSubjectOnly", "EmailAddressCondition", "EmailDomainCondition" }) type.Items.Add(Localization.Get(key, language));
        var sender = new TextBox { Width = 490, Text = rule?.Sender ?? "" }; var subject = new TextBox { Width = 490, Text = rule?.SubjectContains ?? "" };
        var enabled = new CheckBox { AutoSize = true, Text = Localization.Get("EmailRuleEnabled", language), Checked = rule?.Enabled ?? true };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18), AutoScroll = true };
        Controls.Add(layout); layout.Controls.Add(type); layout.Controls.Add(new Label { AutoSize = true, Text = Localization.Get("EmailSender", language) }); layout.Controls.Add(sender);
        layout.Controls.Add(new Label { AutoSize = true, Text = Localization.Get("EmailSubjectCondition", language) }); layout.Controls.Add(subject); layout.Controls.Add(enabled);
        var buttons = new FlowLayoutPanel { AutoSize = true }; var save = new Button { AutoSize = true, Text = Localization.Get("SettingsSave", language) };
        var cancel = new Button { AutoSize = true, Text = Localization.Get("SettingsCancel", language), DialogResult = DialogResult.Cancel }; buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons);
        type.SelectedIndex = rule!=null&&Enum.IsDefined(rule.SenderCondition)?(int)rule.SenderCondition:0; sender.Enabled = type.SelectedIndex != 0;
        type.SelectedIndexChanged += (_, _) => sender.Enabled = type.SelectedIndex != 0;
        save.Click += (_, _) => {
            var candidate = new EmailRule { Id = rule?.Id ?? Guid.NewGuid(), Enabled = enabled.Checked, SenderCondition = (EmailSenderCondition)type.SelectedIndex,
                Sender = type.SelectedIndex == 0 ? "" : sender.Text.Trim(), SubjectContains = subject.Text };
            if (!EmailRuleMatcher.IsValid(candidate)) { MessageBox.Show(this, Localization.Get("EmailInvalidRule", language), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Result = candidate; DialogResult = DialogResult.OK;
        };
        AcceptButton = save; CancelButton = cancel; NotesDialogStyle.Apply(this, SystemWidgetStyle.Clean, dark);
    }
}
