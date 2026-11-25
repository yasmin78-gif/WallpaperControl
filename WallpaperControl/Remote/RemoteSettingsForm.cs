using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;
using WallpaperControl.Remote;

namespace WallpaperControl;

internal sealed class RemoteSettingsForm : Form
{
    private readonly RemoteDesktopManager manager;
    private readonly CheckBox enabled = new() { Text = "Fernsteuerung im privaten Heimnetz aktivieren", AutoSize = true };
    private readonly ComboBox adapters = new() { Width = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 47831, Width = 130 };
    private readonly TextBox name = new() { Width = 420, MaxLength = 64 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(670, 0) };
    private readonly PictureBox qr = new() { Width = 320, Height = 320, SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
    private readonly Label pairingStatus = new() { AutoSize = true, MaximumSize = new Size(670, 0) };
    private readonly CheckBox allowDisable = new() { Text = "Dieses Gerät darf den Besuchermodus auch deaktivieren", AutoSize = true };
    private readonly Button newPairing = new() { Text = "Neues Gerät koppeln", AutoSize = true };
    private readonly Button approve = new() { Text = "Vergleichscode stimmt – Gerät bestätigen", AutoSize = true };
    private readonly ListBox devices = new() { Width = 650, Height = 120, DisplayMember = nameof(RemoteDevice.Name) };
    private readonly TextBox deviceName = new() { Width = 420, MaxLength = 64 };
    private readonly CheckBox deviceDisable = new() { Text = "Besuchermodus deaktivieren erlauben", AutoSize = true };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private string shownCode = "";
    private string deviceSignature = "";
    private DateTimeOffset expires;
    private bool refreshing;
    internal RemoteSettingsForm(RemoteDesktopManager manager, bool dark)
    {
        this.manager = manager; Text = "WallpaperControl Remote"; Size = new(780, 880); MinimumSize = new(680, 620); StartPosition = FormStartPosition.CenterParent;
        BackColor = dark ? Color.FromArgb(18, 23, 31) : Color.White; ForeColor = dark ? Color.WhiteSmoke : Color.FromArgb(24, 28, 34); Font = new Font("Segoe UI", 10);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
        Controls.Add(content);
        void Caption(string text) => content.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(670, 0), Margin = new Padding(3, 14, 3, 5) });
        Caption("Android-App über QR-Code koppeln. Nur im lokalen privaten Netzwerk; keine Cloud und keine Medienübertragung.");
        content.Controls.Add(enabled); Caption("Privater physischer Heimnetzadapter (VPN-/öffentliche Adapter sind ausgeschlossen)"); content.Controls.Add(adapters);
        Caption("PC-Anzeigename"); content.Controls.Add(name); Caption("HTTPS-Port"); content.Controls.Add(port);
        var apply = new Button { Text = "Übernehmen", AutoSize = true }; content.Controls.Add(apply); content.Controls.Add(status);
        var firewall = new Button { Text = "Enge Windows-Firewallfreigabe einrichten …", AutoSize = true }; content.Controls.Add(firewall);
        Caption("Diese Verwaltungsaktionen werden sofort wirksam. Das Schließen dieses Fensters verwirft keine bereits übernommenen Änderungen.");
        content.Controls.Add(newPairing); content.Controls.Add(qr); content.Controls.Add(pairingStatus); content.Controls.Add(allowDisable); content.Controls.Add(approve);
        var reject = new Button { Text = "Kopplung abbrechen", AutoSize = true }; content.Controls.Add(reject);
        Caption("Gekoppelte Geräte"); content.Controls.Add(devices); content.Controls.Add(deviceName); content.Controls.Add(deviceDisable);
        var saveDevice = new Button { Text = "Name / Berechtigung speichern", AutoSize = true }; content.Controls.Add(saveDevice);
        var remove = new Button { Text = "Gerät entfernen und Zugriff sofort sperren", AutoSize = true }; content.Controls.Add(remove);
        var settings = manager.Vault.Settings; enabled.Checked = settings.Enabled; port.Value = settings.Port; name.Text = settings.Name;
        apply.Click += async (_, _) =>
        {
            apply.Enabled = false;
            try
            {
                if (enabled.Checked && adapters.SelectedItem is not RemoteAdapter) { MessageBox.Show(this, "Bitte einen verfügbaren privaten Heimnetzadapter auswählen.", Text); return; }
                await manager.ApplyAsync(new(enabled.Checked, (adapters.SelectedItem as RemoteAdapter)?.Id ?? settings.AdapterId, (int)port.Value, name.Text.Trim()));
                ClearQr();
            }
            catch { MessageBox.Show(this, "Änderung konnte nicht sicher gespeichert oder aktiviert werden.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { apply.Enabled = true; RefreshState(); }
        };
        newPairing.Click += (_, _) =>
        {
            try
            {
                var payload = manager.NewPairing();
                using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(JsonSerializer.Serialize(payload, RemoteJson.Type<RemoteQr>()), QRCodeGenerator.ECCLevel.M);
                using var png = new PngByteQRCode(data); using var stream = new MemoryStream(png.GetGraphic(5)); using var bitmap = Image.FromStream(stream);
                qr.Image?.Dispose(); qr.Image = new Bitmap(bitmap); qr.Visible = true; expires = DateTimeOffset.UtcNow.AddMinutes(5); allowDisable.Checked = false; shownCode = "";
                pairingStatus.Text = "QR scannen und Vergleichscode auf beiden Geräten prüfen. Einmalig, maximal fünf Minuten gültig.";
            }
            catch { MessageBox.Show(this, "Fernsteuerung ist noch nicht bereit. Status und privates Netzwerk prüfen.", Text); }
        };
        approve.Click += (_, _) =>
        {
            if (manager.Server?.Pairing.Pending is not { } pending || pending.Code != shownCode) return;
            try { manager.Server.Pairing.Approve(allowDisable.Checked); ClearQr(); pairingStatus.Text = "Gerät bestätigt. Zugangsdaten werden sicher an die Android-App übergeben."; }
            catch { pairingStatus.Text = "Kopplung abgelaufen. Neuen QR erzeugen."; }
        };
        reject.Click += (_, _) => { manager.Server?.Pairing.Cancel(); ClearQr(); pairingStatus.Text = "Kopplung abgebrochen."; };
        devices.SelectedIndexChanged += (_, _) => { if (devices.SelectedItem is RemoteDevice d) { deviceName.Text = d.Name; deviceDisable.Checked = d.CanDisableVisitor; } };
        saveDevice.Click += (_, _) =>
        {
            if (devices.SelectedItem is not RemoteDevice d) return;
            try { manager.Vault.Change(d.Id, deviceName.Text.Trim(), deviceDisable.Checked); }
            catch { MessageBox.Show(this, "Geräteänderung konnte nicht sicher gespeichert werden.", Text); }
            RefreshState();
        };
        remove.Click += (_, _) =>
        {
            if (devices.SelectedItem is not RemoteDevice d || MessageBox.Show(this, $"Zugriff für „{d.Name}“ sofort entziehen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { manager.Vault.Remove(d.Id); } catch { MessageBox.Show(this, "Entfernen konnte nicht sicher gespeichert werden.", Text); }
            RefreshState();
        };
        firewall.Click += async (_, _) => { try { await InstallFirewallRuleAsync(); } catch { MessageBox.Show(this, "Firewallfreigabe nicht erstellt. Abbruch oder fehlende Administratorrechte.", Text); } };
        refresh.Tick += (_, _) => RefreshState(); refresh.Start();
        foreach (Control control in content.Controls)
        {
            if (control is TextBox or ComboBox or NumericUpDown or ListBox)
            { control.BackColor = dark ? Color.FromArgb(36, 45, 59) : Color.White; control.ForeColor = ForeColor; }
            if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Flat; button.BackColor = dark ? Color.FromArgb(42, 55, 75) : Color.FromArgb(230, 238, 250);
                button.ForeColor = ForeColor; button.FlatAppearance.BorderColor = dark ? Color.FromArgb(75, 93, 120) : Color.LightSteelBlue; button.Padding = new Padding(6);
            }
        }
        qr.BackColor = dark ? Color.FromArgb(24, 31, 42) : Color.White;
        adapters.DrawMode = DrawMode.OwnerDrawFixed; adapters.ItemHeight = 26;
        adapters.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush(adapters.BackColor); e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 ? adapters.Items[e.Index]?.ToString() ?? "" : "";
            TextRenderer.DrawText(e.Graphics, text, adapters.Font, e.Bounds, adapters.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        Shown += async (_, _) =>
        {
            try { var available = await RemoteNetwork.PrivateAdaptersAsync(); adapters.Items.AddRange(available); adapters.SelectedItem = available.FirstOrDefault(a => a.Id == settings.AdapterId) ?? available.FirstOrDefault(); }
            catch { status.Text = "Private Adapter konnten nicht ermittelt werden. Windows-Netzwerkprofil prüfen."; }
            RefreshState();
        };
        RefreshState();
    }
    private void ClearQr() { qr.Image?.Dispose(); qr.Image = null; qr.Visible = false; expires = default; allowDisable.Checked = false; shownCode = ""; approve.Enabled = false; }
    private void RefreshState()
    {
        if (IsDisposed || refreshing) return; refreshing = true;
        try
        {
            status.Text = manager.Status; newPairing.Enabled = manager.Server?.Accepting == true;
            if (manager.Server?.Pairing.Pending is { } pending)
            {
                qr.Visible = false;
                if (shownCode != pending.Code) { shownCode = pending.Code; allowDisable.Checked = false; }
                pairingStatus.Text = $"Unbestätigter Gerätename: {pending.DeviceName}\nVergleichscode: {pending.Code}\nNur bestätigen, wenn derselbe Code auf deinem Handy angezeigt wird.";
                approve.Enabled = true;
            }
            else approve.Enabled = false;
            if (expires != default && DateTimeOffset.UtcNow >= expires) { ClearQr(); manager.Server?.Pairing.Cancel(); pairingStatus.Text = "QR abgelaufen. Für einen weiteren Versuch neuen QR erzeugen."; }
            var current = manager.Vault.Devices;
            string signature = string.Join(";", current.Select(d => d.Id + ":" + d.Version));
            if (signature != deviceSignature)
            {
                string? selected = (devices.SelectedItem as RemoteDevice)?.Id;
                devices.DataSource = current; devices.SelectedItem = current.FirstOrDefault(d => d.Id == selected) ?? current.FirstOrDefault(); deviceSignature = signature;
            }
        }
        finally { refreshing = false; }
    }
    private async Task InstallFirewallRuleAsync()
    {
        if (manager.Adapter is not { } adapter || manager.Server?.Accepting != true) throw new InvalidOperationException();
        if (MessageBox.Show(this, "Eine neue eingehende Regel wird nur für diese Anwendung, den gewählten Adapter, TCP-Port und lokale Subnetze im Profil Privat angelegt. Windows fragt nach Administratorrechten. Fortfahren?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        { MessageBox.Show(this, "Die Freigabe bitte aus der veröffentlichten WallpaperControl.exe einrichten, nicht für den allgemeinen dotnet-Host.", Text); return; }
        string rule = "WallpaperControl.Remote." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exe + adapter.Id + manager.Vault.Settings.Port)))[..16];
        string script = "$ErrorActionPreference='Stop'; if(Get-NetFirewallRule -Name " + Quote(rule) + " -ErrorAction SilentlyContinue){exit 0}; New-NetFirewallRule -Name " + Quote(rule)
            + " -DisplayName 'WallpaperControl Remote (privates Heimnetz)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort " + manager.Vault.Settings.Port
            + " -LocalAddress Any -RemoteAddress LocalSubnet -Profile Private -InterfaceAlias " + Quote(adapter.Name) + " -Program " + Quote(exe) + " -EdgeTraversalPolicy Block | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) };
        using var process = Process.Start(start) ?? throw new InvalidOperationException(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException();
        MessageBox.Show(this, "Private TCP-Freigabe eingerichtet. Router und VPN wurden nicht verändert.", Text);
    }
    protected override void Dispose(bool disposing) { if (disposing) { refresh.Dispose(); qr.Image?.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
}
