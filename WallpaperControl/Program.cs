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
            string? remoteCommand = GetRemoteCommand();

            // A command-line invocation (for example from Rainmeter) first
            // tries to hand the command to an already running instance.
            if (remoteCommand != null &&
                RemoteCommandServer.TrySend(remoteCommand))
            {
                return;
            }

            ApplicationConfiguration.Initialize();
            Localization.Initialize();

            MainForm form = new MainForm();
            using RemoteCommandServer remoteServer =
                new RemoteCommandServer(form.ExecuteRemoteCommand);

            remoteServer.Start();

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

            // If Wallpaper Control was not running yet, start normally and
            // execute the requested command once the window is ready.
            if (remoteCommand != null)
            {
                form.Shown += (_, _) =>
                    form.BeginInvoke(() =>
                        form.ExecuteRemoteCommand(remoteCommand));
            }

            Application.Run(form);
        }

        private static string? GetRemoteCommand()
        {
            foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
            {
                if (string.Equals(arg, "--next", StringComparison.OrdinalIgnoreCase))
                    return "next";
            }

            return null;
        }
    }
}
