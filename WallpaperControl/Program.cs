using System;
using System.Linq;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            Localization.Initialize();

            MainForm form = new MainForm();

            if (Environment.GetCommandLineArgs()
                .Any(a => string.Equals(
                    a,
                    "--tray",
                    StringComparison.OrdinalIgnoreCase)))
            {
                form.Shown += (_, _) =>
                {
                    form.WindowState =
                        FormWindowState.Minimized;
                };
            }

            Application.Run(form);
        }
    }
}
