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

        /// <summary>
        /// Configures the local named-pipe listener and the callback for accepted commands.
        /// </summary>
        /// <param name="commandHandler">The callback invoked for a complete accepted command.</param>
        /// <param name="pipeName">An optional pipe name, allowing tests to isolate their connections.</param>
        internal RemoteCommandServer(Action<string> commandHandler, string? pipeName = null)
        {
            this.commandHandler = commandHandler;
            this.pipeName = pipeName ?? PipeName;
        }

        /// <summary>
        /// Starts listening for commands from secondary application instances.
        /// </summary>
        internal void Start()
        {
            listenerTask = Task.Run(ListenAsync);
        }

        /// <summary>
        /// Attempts to deliver a command to the primary instance within the connection timeout.
        /// </summary>
        /// <param name="command">The supported command to send or dispatch.</param>
        /// <param name="pipeName">An optional pipe name, allowing tests to isolate their connections.</param>
        /// <returns>True when the command was sent to the pipe; otherwise, false.</returns>
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

        /// <summary>
        /// Accepts pipe connections and dispatches bounded commands until shutdown is requested.
        /// </summary>
        /// <returns>A task representing completion of the asynchronous operation.</returns>
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
        /// <summary>
        /// Reads one bounded command line with a deadline and caller-controlled cancellation.
        /// </summary>
        /// <param name="stream">The input stream supplying one remote command.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result is a complete bounded command, or null for incomplete, oversized, or timed-out input.</returns>
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
        /// <summary>
        /// Cancels the listener and releases its shutdown resources.
        /// </summary>
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
