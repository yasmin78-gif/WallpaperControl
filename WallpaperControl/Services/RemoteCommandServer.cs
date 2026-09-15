using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class RemoteCommandServer : IDisposable
    {
        internal const int MaxCommandLength = 64;
        internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

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
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(
                        cancellation.Token);

                    string? command = await ReadCommandAsync(server, cancellation.Token);

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

        // Bound the complete command, including slow or silent clients. EOF
        // without a line terminator is incomplete and must not execute a command.
        internal static async Task<string?> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(CommandTimeout);
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 128, leaveOpen: true);
            var command = new StringBuilder(MaxCommandLength);
            var character = new char[1];
            try
            {
                while (await reader.ReadAsync(character.AsMemory(), deadline.Token).ConfigureAwait(false) != 0)
                {
                    char value = character[0];
                    if (value == '\n' || value == '\r')
                        return command.ToString();
                    if (command.Length == MaxCommandLength)
                        return null;
                    command.Append(value);
                }
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A client timeout closes this connection; the listener continues.
                return null;
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
