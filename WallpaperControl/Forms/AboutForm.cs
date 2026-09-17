using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class AboutForm : Form
    {
        private readonly List<Font> ownedFonts = new();
        private readonly bool darkMode;
        private readonly PictureBox iconBox;

        /// <summary>
        /// Builds the application information dialog using the selected theme and opacity.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="windowOpacityPercent">The window opacity as a percentage.</param>
        public AboutForm(
            bool darkMode,
            int windowOpacityPercent)
        {
            this.darkMode = darkMode;

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
                CreateOwnedFont("Segoe UI", 10);

            iconBox = new PictureBox
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
                Font = CreateOwnedFont(
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
                Font = CreateOwnedFont(
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
                Font = CreateOwnedFont(
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

        }



        /// <summary>
        /// Applies the native title-bar theme after the window handle is created.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int darkValue =
                darkMode ? 1 : 0;

            DwmSetWindowAttribute(
                Handle,
                20,
                ref darkValue,
                sizeof(int));
        }

        /// <summary>
        /// Formats the Windows version using recognized build ranges without guessing future release names.
        /// </summary>
        /// <returns>The recognized Windows version label, including the available version or build information.</returns>
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

        /// <summary>
        /// Creates a font and tracks it for disposal with the form.
        /// </summary>
        /// <param name="familyName">The name of the font family to create.</param>
        /// <param name="emSize">The font size in the units used by the drawing operation.</param>
        /// <param name="style">The weight and decoration applied to the font.</param>
        /// <returns>The font owned by the form; it is released when the form is disposed.</returns>
        private Font CreateOwnedFont(string familyName, float emSize, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font(familyName, emSize, style);
            ownedFonts.Add(font);
            return font;
        }

        /// <summary>
        /// Releases the resources owned by this about form.
        /// </summary>
        /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Image? image = iconBox.Image;
                iconBox.Image = null;
                image?.Dispose();

                foreach (Font font in ownedFonts)
                {
                    font.Dispose();
                }
                ownedFonts.Clear();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Loads the embedded application icon, with an executable-icon fallback when necessary.
        /// </summary>
        /// <returns>A bitmap of the application logo, or null when no image can be loaded.</returns>
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

        /// <summary>
        /// Reads the requested assembly metadata attribute for the application information dialog.
        /// </summary>
        /// <typeparam name="T">The assembly attribute type to retrieve.</typeparam>
        /// <param name="assembly">The assembly whose metadata is inspected.</param>
        /// <returns>The requested assembly attribute, or null when it is absent.</returns>
        private static T? GetAttribute<T>(
            Assembly assembly)
            where T : Attribute
        {
            return assembly.GetCustomAttribute<T>();
        }

        /// <summary>
        /// Applies the selected palette to the information dialog and its close button.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="closeButton">The close action to style with the dialog.</param>
        private void ApplyTheme(
            bool darkMode,
            Button closeButton)
        {
            Color background =
                AppTheme.WindowBackground(darkMode);

            Color foreground =
                AppTheme.TextPrimary(darkMode);

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
                AppTheme.ControlBackground(darkMode);

            closeButton.ForeColor =
                foreground;

            closeButton.FlatStyle =
                FlatStyle.Flat;

            closeButton.FlatAppearance.BorderColor =
                AppTheme.Border(darkMode);

            closeButton.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);

            closeButton.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);
        }

        /// <summary>
        /// Sets a Desktop Window Manager attribute on the specified native window.
        /// </summary>
        /// <param name="hwnd">The native window handle used by the operation.</param>
        /// <param name="attribute">The native Desktop Window Manager attribute identifier.</param>
        /// <param name="attributeValue">The value supplied for the native window attribute.</param>
        /// <param name="attributeSize">The size of the attribute value in bytes.</param>
        /// <returns>The HRESULT status code; zero indicates success.</returns>
        [DllImport("dwmapi.dll")]
        private static extern int
            DwmSetWindowAttribute(
                IntPtr hwnd,
                int attribute,
                ref int attributeValue,
                int attributeSize);
    }
}
