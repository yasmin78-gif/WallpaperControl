using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class AboutForm : Form
    {
        public AboutForm(
            bool darkMode,
            int windowOpacityPercent)
        {
            Assembly assembly =
                Assembly.GetExecutingAssembly();

            AssemblyName assemblyName =
                assembly.GetName();

            string product =
                GetAttribute<AssemblyProductAttribute>(
                    assembly)?.Product
                ?? "Wallpaper Control";

            string copyright =
                GetAttribute<AssemblyCopyrightAttribute>(
                    assembly)?.Copyright
                ?? "";

            string version =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? assemblyName.Version?.ToString(3)
                ?? "-";

            int plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
            {
                version =
                    version[..plusIndex];
            }

            string runtime =
                RuntimeInformation.FrameworkDescription;

            string os =
                GetFriendlyWindowsVersion();

            string architecture =
                RuntimeInformation.ProcessArchitecture.ToString();

            string processBits =
                Environment.Is64BitProcess
                ? Localization.Get("AboutProcess64Bit")
                : Localization.Get("AboutProcess32Bit");

#if DEBUG
            string build = "Debug";
#else
            string build = "Release";
#endif

            Text =
                Localization.Get("AboutTitle");

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            StartPosition =
                FormStartPosition.CenterParent;

            ClientSize =
                new Size(470, 402);

            Font =
                new Font("Segoe UI", 10);

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(25, 22),
                Size = new Size(80, 80),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadApplicationLogo()
            };

            Label productLabel = new Label
            {
                Text = product,
                Location = new Point(125, 30),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    16,
                    FontStyle.Bold)
            };

            Label versionLabel = new Label
            {
                Text = string.Format(
                    Localization.CurrentCulture,
                    Localization.Get("AboutVersion"),
                    version),
                Location = new Point(127, 65),
                AutoSize = true
            };

            Label descriptionLabel = new Label
            {
                Text =
                    Localization.Get(
                        "AboutDescription"),
                Location = new Point(25, 105),
                Size = new Size(420, 42)
            };

            Label copyrightLabel = new Label
            {
                Text = copyright,
                Location = new Point(25, 168),
                Size = new Size(420, 22)
            };

            Label licenseLabel = new Label
            {
                Text = "GNU General Public License v3.0",
                Location = new Point(25, 190),
                Size = new Size(420, 22)
            };

            Label separator = new Label
            {
                Location = new Point(25, 219),
                Size = new Size(420, 1),
                BorderStyle =
                    BorderStyle.Fixed3D
            };

            Label technicalTitle = new Label
            {
                Text =
                    Localization.Get(
                        "AboutTechnicalInformation"),
                Location = new Point(25, 227),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    11,
                    FontStyle.Bold)
            };

            Label technicalLabel = new Label
            {
                Text =
                    string.Format(
                        Localization.CurrentCulture,
                        Localization.Get(
                            "AboutTechnicalDetails"),
                        runtime,
                        os,
                        architecture,
                        processBits,
                        build),
                Location = new Point(25, 259),
                Size = new Size(420, 100),
                Font = new Font(
                    "Consolas",
                    9)
            };

            Button closeButton = new Button
            {
                Text =
                    Localization.Get(
                        "AboutClose"),
                Location = new Point(340, 352),
                Size = new Size(105, 34),
                DialogResult = DialogResult.OK
            };

            Controls.Add(iconBox);
            Controls.Add(productLabel);
            Controls.Add(versionLabel);
            Controls.Add(descriptionLabel);
            Controls.Add(copyrightLabel);
            Controls.Add(licenseLabel);
            Controls.Add(separator);
            Controls.Add(technicalTitle);
            Controls.Add(technicalLabel);
            Controls.Add(closeButton);

            AcceptButton = closeButton;
            CancelButton = closeButton;

            ApplyTheme(
                darkMode,
                closeButton);

            int darkValue =
                darkMode ? 1 : 0;

            if (IsHandleCreated)
            {
                DwmSetWindowAttribute(
                    Handle,
                    20,
                    ref darkValue,
                    sizeof(int));
            }
        }


        private static string GetFriendlyWindowsVersion()
        {
            if (!OperatingSystem.IsWindows())
            {
                return RuntimeInformation.OSDescription;
            }

            Version version =
                Environment.OSVersion.Version;

            int build =
                version.Build;

            // Windows 11 starts at build 22000.
            if (version.Major == 10)
            {
                if (build >= 22000)
                {
                    return $"Windows 11 (Build {build})";
                }

                return $"Windows 10 (Build {build})";
            }

            // Do not guess the marketing name of future Windows versions.
            return $"Microsoft Windows {version.Major}.{version.Minor} (Build {build})";
        }

        private static Bitmap? LoadApplicationLogo()
        {
            Assembly assembly =
                Assembly.GetExecutingAssembly();

            using Stream? stream =
                assembly.GetManifestResourceStream(
                    "WallpaperControl.WallpaperControl.ico");

            if (stream == null)
            {
                return Icon
                    .ExtractAssociatedIcon(
                        Application.ExecutablePath)
                    ?.ToBitmap();
            }

            using Icon icon =
                new Icon(
                    stream,
                    256,
                    256);

            return icon.ToBitmap();
        }

        private static T? GetAttribute<T>(
            Assembly assembly)
            where T : Attribute
        {
            return assembly.GetCustomAttribute<T>();
        }

        private void ApplyTheme(
            bool darkMode,
            Button closeButton)
        {
            Color background =
                darkMode
                ? Color.FromArgb(32, 32, 32)
                : SystemColors.Control;

            Color foreground =
                darkMode
                ? Color.FromArgb(235, 235, 235)
                : SystemColors.ControlText;

            BackColor = background;
            ForeColor = foreground;

            foreach (Control control in Controls)
            {
                if (control is Label label)
                {
                    label.ForeColor = foreground;
                }
            }

            closeButton.UseVisualStyleBackColor = false;

            closeButton.BackColor =
                darkMode
                ? Color.FromArgb(50, 50, 50)
                : SystemColors.Control;

            closeButton.ForeColor =
                foreground;

            closeButton.FlatStyle =
                FlatStyle.Flat;

            closeButton.FlatAppearance.BorderColor =
                darkMode
                ? Color.FromArgb(85, 85, 85)
                : Color.FromArgb(180, 180, 180);
        }

        [DllImport("dwmapi.dll")]
        private static extern int
            DwmSetWindowAttribute(
                IntPtr hwnd,
                int attribute,
                ref int attributeValue,
                int attributeSize);
    }
}
