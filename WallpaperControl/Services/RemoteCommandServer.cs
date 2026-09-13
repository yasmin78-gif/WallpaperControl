using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class RemoteCommandServer : IDisposable
    {
        private const string PipeName =
            "WallpaperControl.RemoteCommands.v1";

        private readonly Action<string> commandHandler;
        private readonly CancellationTokenSource cancellation = new();
        private Task? listenerTask;

        internal RemoteCommandServer(Action<string> commandHandler)
        {
            this.commandHandler = commandHandler;
        }

        internal void Start()
        {
            listenerTask = Task.Run(ListenAsync);
        }

        internal static bool TrySend(string command)
        {
            try
            {
                using NamedPipeClientStream client = new(
                    ".",
                    PipeName,
                    PipeDirection.Out);

                client.Connect(250);

                using StreamWriter writer = new(client)
                {
                    AutoFlush = true
                };

                writer.WriteLine(command);
                return true;
            }
            catch
            {
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
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

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
            cancellation.Dispose();
        }
    }
}
