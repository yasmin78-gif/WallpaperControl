using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class RemoteCommandServer : IDisposable
    {
        private static readonly string PipeName =
            "WallpaperControl.RemoteCommands.v1." + SingleInstanceGuard.UserKey;

        private readonly string pipeName;
        private readonly Action<string> commandHandler;
        private readonly CancellationTokenSource cancellation = new();
        private Task? listenerTask;

        internal RemoteCommandServer(Action<string> commandHandler, string? pipeName = null)
        {
            this.commandHandler = commandHandler;
            this.pipeName = pipeName ?? PipeName;
        }

        internal void Start()
        {
            listenerTask = Task.Run(ListenAsync);
        }

        internal static bool TrySend(string command, string? pipeName = null)
        {
            try
            {
                using NamedPipeClientStream client = new(
                    ".",
                    pipeName ?? PipeName,
                    PipeDirection.Out);

                client.Connect(250);

                using StreamWriter writer = new(client)
                {
                    AutoFlush = true
                };

                writer.WriteLine(command);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Could not send remote command.", ex);
                return false;
            }
        }

        private async Task ListenAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeServerStream server = new(
                        pipeName ?? PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(
                        cancellation.Token);

                    using StreamReader reader = new(server);
                    string? command = await reader.ReadLineAsync(
                        cancellation.Token);

                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        commandHandler(command.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        AppLogger.Warning("Remote command listener failed and will retry.", ex);
                        await Task.Delay(100);
                    }
                }
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            // The listener still uses the token until its pending await ends.
            if (listenerTask == null)
                cancellation.Dispose();
            else
                _ = listenerTask.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
    }
}
