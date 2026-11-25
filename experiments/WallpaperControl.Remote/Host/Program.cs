using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using QRCoder;

namespace RemoteExperiment;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TestWindow(args));
    }
}

public sealed class TestWindow : Form
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(680, 0) };
    private readonly Label stateText = new() { AutoSize = true, MaximumSize = new(680, 0) };
    private readonly Label pairingText = new() { AutoSize = true };
    private readonly ComboBox addresses = new() { Width = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox disablePermission = new() { Text = "Dieses Gerät darf Besuchermodus deaktivieren", AutoSize = true };
    private readonly PictureBox qrImage = new() { Size = new(360, 360), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Button start = new() { Text = "Testserver starten (standardmäßig AUS)", AutoSize = true };
    private readonly Button qrButton = new() { Text = "Neuer Test-QR", AutoSize = true, Enabled = false };
    private readonly Button approve = new() { Text = "Vergleichscode stimmt: bestätigen", AutoSize = true, Enabled = false };
    private RemoteHost? host;
    private X509Certificate2? certificate;
    private string serverId = "";
    private bool closing;
    private WindowsDiscovery? discovery;
    private readonly string[] args;
    private readonly System.Windows.Forms.Timer profileGuard = new() { Interval = 5000 };
    private bool checkingProfile;
    public long UiTicks;
    public Task<object?> Dispatch(Func<object?> action)
    {
        if (closing || IsDisposed) return Task.FromException<object?>(new ObjectDisposedException(nameof(TestWindow)));
        if (!InvokeRequired) return Task.FromResult(action());
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { BeginInvoke(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.TrySetException(ex); } }); }
        catch (Exception ex) { completion.TrySetException(ex); }
        return completion.Task;
    }
    public TestWindow(string[] arguments)
    {
        args = arguments;
        if (args.Contains("--measure") || args.Contains("--smoke")) { Opacity = 0; ShowInTaskbar = false; }
        Text = "WallpaperControl Remote – ISOLIERTER TEST (keine Wallpapers)"; Size = new(760, 900);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Controls.Add(panel);
        addresses.Items.Add(new BindingChoice(IPAddress.Loopback, 0, "Loopback – nur lokale Tests"));
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
        {
            // VPN/tunnel names additionally excluded; actual private category is rechecked on start.
            if (new[] { "vpn", "proton", "tunnel", "wireguard", "tap", "virtual" }.Any(s => (nic.Name + " " + nic.Description).Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            var properties = nic.GetIPProperties();
            foreach (var entry in properties.UnicastAddresses.Where(u => Trust.Local(u.Address) && !IPAddress.IsLoopback(u.Address)))
                addresses.Items.Add(new BindingChoice(entry.Address, (uint)(entry.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? properties.GetIPv4Properties().Index : properties.GetIPv6Properties().Index), nic.Name));
        }
        addresses.SelectedIndex = 0;
        panel.Controls.AddRange([status, addresses, start, qrButton, qrImage, pairingText, disablePermission, approve, stateText]);
        start.Click += async (_, _) => { try { await StartServerAsync(); } catch (Exception ex) { status.Text = "Start verweigert/fehlgeschlagen: " + ex.GetType().Name; } };
        qrButton.Click += (_, _) => NewQr();
        approve.Click += (_, _) => { host?.Pairing.Confirm(disablePermission.Checked); approve.Enabled = false; pairingText.Text = "Bestätigt; Android kann Testzugangsdaten abholen."; };
        foreach (var command in new[] { ("next", ""), ("pause", "true"), ("pause", "false"), ("visitor", "true"), ("visitor", "false"), ("mode", "video"), ("mode", "slideshow") })
        {
            var button = new Button { Text = command.Item1 + " " + command.Item2, AutoSize = true };
            button.Click += (_, _) => { if (host != null) { var s = host.Model.State; host.Model.Execute(new(Guid.NewGuid().ToString("N"), s.Epoch, s.Revision, command.Item1, command.Item2), "desktop", true); } }; panel.Controls.Add(button);
        }
        var tick = new System.Windows.Forms.Timer { Interval = 50 }; tick.Tick += (_, _) => UiTicks++; tick.Start(); FormClosed += (_, _) => tick.Dispose();
        NetworkChange.NetworkAddressChanged += AddressChanged;
        profileGuard.Tick += async (_, _) =>
        {
            if (checkingProfile || closing || host == null || addresses.SelectedItem is not BindingChoice choice || IPAddress.IsLoopback(choice.Address)) return;
            checkingProfile = true;
            try { if (!await PrivateProfileAsync(choice.Index)) await StopForNetworkChangeAsync(); }
            finally { checkingProfile = false; }
        };
        profileGuard.Start();
        Shown += async (_, _) =>
        {
            if (args.Contains("--measure") || args.Contains("--smoke"))
            {
                if (!args.Contains("--baseline")) await StartServerAsync();
                await Task.Delay(1000);
                Console.WriteLine(JsonSerializer.Serialize(new { ready = true, pid = Environment.ProcessId, port = host?.Port, elapsedMs = Environment.TickCount64 }));
                if (args.Contains("--smoke") && host != null && certificate != null)
                {
                    var credential = new Credential("published-smoke", Trust.Secret(), false); host.Pairing.AddTestDevice(credential);
                    using var http = new HttpClient(Trust.Handler(Trust.Pin(certificate)));
                    http.DefaultRequestHeaders.Authorization = new("Bearer", credential.Token);
                    using var response = await http.GetAsync($"https://127.0.0.1:{host.Port}/api/v1/state"); response.EnsureSuccessStatusCode();
                    Console.WriteLine("PUBLISHED_SMOKE_HTTPS_AUTHENTICATED_OK"); Close();
                }
            }
        };
        FormClosing += async (_, e) =>
        {
            if (closing) return; e.Cancel = true; closing = true;
            profileGuard.Stop(); profileGuard.Dispose(); NetworkChange.NetworkAddressChanged -= AddressChanged;
            if (discovery != null) await discovery.DisposeAsync();
            if (host != null) await host.DisposeAsync(); certificate?.Dispose(); Close();
        };
        status.Text = "AUS. Keine produktiven Daten; nur synthetische Zustände.";
    }
    private void AddressChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || closing || !IsHandleCreated) return;
        BeginInvoke(async () => { if (addresses.SelectedItem is BindingChoice choice && !IPAddress.IsLoopback(choice.Address)) await StopForNetworkChangeAsync(); });
    }
    private async Task StopForNetworkChangeAsync()
    {
        var current = host; if (current == null) return; host = null;
        qrButton.Enabled = approve.Enabled = false; qrImage.Image?.Dispose(); qrImage.Image = null;
        status.Text = "Netzwerk geändert/kein privates Profil: Testserver wird geschlossen. Testhost neu starten und Adapter neu wählen.";
        // Stop HTTPS immediately; discovery teardown must not delay removal of the listener.
        await current.DisposeAsync();
        if (discovery != null) { await discovery.DisposeAsync(); discovery = null; }
    }
    private async Task StartServerAsync()
    {
        if (host != null) return;
        var selected = (BindingChoice)addresses.SelectedItem!;
        if (!IPAddress.IsLoopback(selected.Address) && !await PrivateProfileAsync(selected.Index)) throw new InvalidOperationException("Private LAN profile required");
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperControl.Remote.Experiment");
        Directory.CreateDirectory(folder);
        string idPath = Path.Combine(folder, "server-id.txt"), certPath = Path.Combine(folder, "certificate.dpapi");
        serverId = File.Exists(idPath) ? File.ReadAllText(idPath) : Guid.NewGuid().ToString("N"); File.WriteAllText(idPath, serverId);
        if (File.Exists(certPath)) certificate = X509CertificateLoader.LoadPkcs12(ProtectedData.Unprotect(File.ReadAllBytes(certPath), null, DataProtectionScope.CurrentUser), null, X509KeyStorageFlags.Exportable);
        else { certificate = Trust.CreateCertificate(); File.WriteAllBytes(certPath, ProtectedData.Protect(certificate.Export(X509ContentType.Pfx), null, DataProtectionScope.CurrentUser)); }
        if (args.Contains("--tls-expired")) { var old = certificate; certificate = Trust.CreateCertificate(true, old); old.Dispose(); }
        if (args.Contains("--tls-foreign")) { certificate.Dispose(); certificate = Trust.CreateCertificate(); }
        host = new(new StateModel(serverId), Dispatch);
        string devicesPath = Path.Combine(folder, "devices.dpapi");
        if (File.Exists(devicesPath)) host.Pairing.ImportDevices(JsonSerializer.Deserialize<Pairing.StoredDevice[]>(ProtectedData.Unprotect(File.ReadAllBytes(devicesPath), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("Device store"));
        host.Pairing.DevicesChanged += () =>
        {
            string temporary = devicesPath + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(host.Pairing.ExportDevices()), null, DataProtectionScope.CurrentUser));
            File.Move(temporary, devicesPath, true);
        };
        host.Model.Changed += state => stateText.Text = JsonSerializer.Serialize(state);
        host.Pairing.Requested += (name, code) => BeginInvoke(() => { pairingText.Text = $"Unbestätigter Gerätename: {name}; Vergleichscode: {code}"; disablePermission.Checked = false; approve.Enabled = true; });
        try { await host.StartAsync(selected.Address, 47831, certificate); }
        catch { await host.DisposeAsync(); host = null; throw; }
        start.Enabled = false; addresses.Enabled = false; qrButton.Enabled = true;
        status.Text = "HTTPS aktiv: " + Address(selected.Address, host.Port) + "; keine Firewalländerung";
        if (!IPAddress.IsLoopback(selected.Address)) { discovery = new(); status.Text += "; DNS-SD: " + await discovery.StartAsync(selected.Address, selected.Index, host.Port, serverId); }
    }
    private void NewQr()
    {
        if (host == null || certificate == null) return;
        var selected = (BindingChoice)addresses.SelectedItem!;
        string secret = Trust.Secret(), session = Guid.NewGuid().ToString("N"); host.Pairing.Begin(session, secret);
        var payload = new PairQr(1, Address(selected.Address, host.Port), serverId, Trust.Pin(certificate), session, secret);
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(JsonSerializer.Serialize(payload), QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data); using var stream = new MemoryStream(png.GetGraphic(6));
        using var image = Image.FromStream(stream); qrImage.Image?.Dispose(); qrImage.Image = new Bitmap(image);
        disablePermission.Checked = false; approve.Enabled = false; pairingText.Text = "QR maximal 5 Minuten; erst nach passendem Vergleichscode bestätigen.";
    }
    public static string Address(IPAddress ip, int port) => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"https://[{ip}]:{port}/" : $"https://{ip}:{port}/";
    private static async Task<bool> PrivateProfileAsync(uint index)
    {
        var info = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add($"(Get-NetConnectionProfile -InterfaceIndex {index} -ErrorAction Stop).NetworkCategory.ToString()");
        using var process = Process.Start(info)!; string result = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); return process.ExitCode == 0 && result.Trim() == "Private";
    }
    private sealed record BindingChoice(IPAddress Address, uint Index, string Name) { public override string ToString() => $"{Name}: {Address} (Index {Index})"; }
}
