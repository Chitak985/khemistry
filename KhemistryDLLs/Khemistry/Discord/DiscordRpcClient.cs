using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Khemistry
{
    // Discord's documented local IPC protocol. No authentication tokens, SDK binaries,
    // social subscriptions, or remote network connections are needed for SET_ACTIVITY.
    internal sealed class DiscordRpcClient : IDisposable
    {
        internal const string ApplicationId = "1552454077361692794";
        internal const string Details = "Engineering the way to space!";
        internal const string State = "Playing";
        internal const int MaximumPayload = 65536;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Func<CancellationToken, Stream> connect;
        private readonly Action<string> report;
        private readonly int pid, retryMilliseconds, responseTimeout;
        private string lastStatus;
        private int disposed;
        internal Task Completion { get; }

        internal DiscordRpcClient(int processId, Action<string> reportStatus,
            Func<CancellationToken, Stream> connector = null, int retryDelay = 15000,
            int replyTimeout = 10000)
        {
            pid = processId;
            report = reportStatus;
            connect = connector ?? ConnectLocal;
            retryMilliseconds = retryDelay;
            responseTimeout = replyTimeout;
            Completion = Task.Run(Run);
        }

        internal static string Activity(int processId, string nonce, bool clear = false)
            => "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":"
                + processId.ToString(CultureInfo.InvariantCulture) + ",\"activity\":"
                + (clear ? "null" : "{\"details\":\"" + Details + "\",\"state\":\"" + State + "\"}")
                + "},\"nonce\":\"" + nonce + "\"}";

        private void Report(string message)
        {
            if (message == lastStatus) return;
            lastStatus = message;
            report?.Invoke(message);
        }

        private async Task Run()
        {
            CancellationToken token = stop.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    Stream stream = null;
                    bool published = false;
                    try
                    {
                        stream = connect(token);
                        if (stream == null) throw new IOException();
                        await WriteFrame(stream, 0, Encoding.UTF8.GetBytes(
                            "{\"v\":1,\"client_id\":\"" + ApplicationId + "\"}"), token).ConfigureAwait(false);
                        bool ready = false, acknowledged = false;
                        string nonce = Guid.NewGuid().ToString("N");
                        var deadline = Stopwatch.StartNew();
                        while (!token.IsCancellationRequested)
                        {
                            int timeout = acknowledged ? Timeout.Infinite
                                : Math.Max(1, responseTimeout - (int)deadline.ElapsedMilliseconds);
                            Frame frame = await ReadFrame(stream, token, timeout).ConfigureAwait(false);
                            if (!acknowledged && deadline.ElapsedMilliseconds >= responseTimeout)
                                throw new TimeoutException();
                            if (frame.opcode == 3)
                            {
                                await WriteFrame(stream, 4, frame.payload, token).ConfigureAwait(false);
                                continue;
                            }
                            if (frame.opcode == 2) throw new IOException("Discord closed IPC.");
                            if (frame.opcode == 4) continue;
                            if (frame.opcode != 1) throw new InvalidDataException("Unknown IPC opcode.");
                            RpcMessage message = Parse(frame.payload);
                            if (message.evt == "ERROR")
                            {
                                Report("Discord rejected Rich Presence (code "
                                    + (message.data == null ? "unknown" : message.data.code.ToString(CultureInfo.InvariantCulture)) + ").");
                                break;
                            }
                            if (!ready && message.cmd == "DISPATCH" && message.evt == "READY")
                            {
                                ready = true;
                                await WriteFrame(stream, 1, Encoding.UTF8.GetBytes(Activity(pid, nonce)), token)
                                    .ConfigureAwait(false);
                                published = true;
                                deadline.Restart();
                            }
                            else if (ready && !acknowledged && message.cmd == "SET_ACTIVITY"
                                && message.nonce == nonce)
                            {
                                acknowledged = true;
                                Report("Discord Rich Presence connected.");
                            }
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception exception)
                    {
                        if (!token.IsCancellationRequested)
                            Report("Discord Rich Presence unavailable (" + exception.GetType().Name
                                + "); retrying in " + (retryMilliseconds / 1000) + " seconds.");
                    }
                    finally
                    {
                        if (stream != null)
                        {
                            if (token.IsCancellationRequested && published)
                            {
                                try
                                {
                                    await WriteFrame(stream, 1, Encoding.UTF8.GetBytes(
                                        Activity(pid, Guid.NewGuid().ToString("N"), true)),
                                        CancellationToken.None, 500).ConfigureAwait(false);
                                }
                                catch { /* Closing the IPC connection is the fallback on exit. */ }
                            }
                            try { stream.Dispose(); } catch { }
                        }
                    }
                    if (!token.IsCancellationRequested)
                        await Task.Delay(retryMilliseconds, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { stop.Dispose(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { stop.Cancel(); } catch (ObjectDisposedException) { }
            // Never wait for IPC from Unity's main thread.
        }

        [Serializable]
        internal sealed class RpcMessage
        {
            public string cmd = null;
            public string evt = null;
            public string nonce = null;
            public RpcError data = null;
        }
        [Serializable]
        internal sealed class RpcError { public int code = 0; }
        internal static RpcMessage Parse(byte[] payload)
        {
            // Unity's string JSON parser is thread-safe and already ships with KSP.
            // System.Runtime.Serialization does not ship in KSP's stripped Mono runtime.
            return UnityEngine.JsonUtility.FromJson<RpcMessage>(Encoding.UTF8.GetString(payload))
                ?? throw new InvalidDataException("Empty RPC response.");
        }
        internal sealed class Frame { public int opcode; public byte[] payload; }

        internal static byte[] Packet(int opcode, byte[] payload)
        {
            if (payload.Length > MaximumPayload) throw new InvalidDataException("IPC payload too large.");
            byte[] packet = new byte[8 + payload.Length];
            for (int i = 0; i < 4; i++)
            {
                packet[i] = (byte)(opcode >> (8 * i));
                packet[4 + i] = (byte)(payload.Length >> (8 * i));
            }
            Buffer.BlockCopy(payload, 0, packet, 8, payload.Length);
            return packet;
        }
        internal static async Task WriteFrame(Stream stream, int opcode, byte[] payload,
            CancellationToken token, int timeout = 3000)
        {
            byte[] packet = Packet(opcode, payload);
            // One write for the header and payload, as required by the IPC transport.
            await WaitIO(stream.WriteAsync(packet, 0, packet.Length, token), token, timeout).ConfigureAwait(false);
        }
        internal static async Task<Frame> ReadFrame(Stream stream, CancellationToken token, int timeout)
        {
            byte[] header = new byte[8];
            // Idle connections may wait indefinitely; incomplete frames may not.
            await ReadExact(stream, header, 0, 1, token, timeout).ConfigureAwait(false);
            await ReadExact(stream, header, 1, 7, token, timeout == Timeout.Infinite ? 10000 : timeout).ConfigureAwait(false);
            int opcode = LittleEndian(header, 0), length = LittleEndian(header, 4);
            if (length < 0 || length > MaximumPayload) throw new InvalidDataException("Invalid IPC length.");
            byte[] payload = new byte[length];
            await ReadExact(stream, payload, 0, length, token, 10000).ConfigureAwait(false);
            return new Frame { opcode = opcode, payload = payload };
        }
        private static int LittleEndian(byte[] bytes, int offset)
            => bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        private static async Task ReadExact(Stream stream, byte[] bytes, int offset, int count,
            CancellationToken token, int timeout)
        {
            var elapsed = Stopwatch.StartNew();
            while (count > 0)
            {
                Task<int> read = stream.ReadAsync(bytes, offset, count, token);
                await WaitIO(read, token, timeout == Timeout.Infinite ? timeout
                    : Math.Max(1, timeout - (int)elapsed.ElapsedMilliseconds)).ConfigureAwait(false);
                int got = await read.ConfigureAwait(false);
                if (got == 0) throw new EndOfStreamException();
                offset += got; count -= got;
            }
        }
        private static async Task WaitIO(Task operation, CancellationToken token, int timeout)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task timer = Task.Delay(timeout, deadline.Token);
                if (await Task.WhenAny(operation, timer).ConfigureAwait(false) != operation)
                {
                    // Older Mono pipe implementations may ignore cancellation. The session
                    // disposes the stream on exit; observe any eventual read/write exception.
                    ObserveFailure(operation);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Discord IPC timed out.");
                }
                deadline.Cancel();
                await operation.ConfigureAwait(false);
            }
        }
        private static void ObserveFailure(Task operation)
        {
            operation.ContinueWith(task => { var ignored = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static Stream ConnectLocal(CancellationToken token)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                for (int i = 0; i < 10; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut,
                        PipeOptions.Asynchronous);
                    try { pipe.Connect(150); return pipe; }
                    catch { pipe.Dispose(); }
                }
            }
            else
            {
                foreach (string directory in UnixDirectories())
                    for (int i = 0; i < 10; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        try
                        {
                            IAsyncResult pending = socket.BeginConnect(
                                new UnixEndpoint(Path.Combine(directory, "discord-ipc-" + i)), null, null);
                            using (WaitHandle wait = pending.AsyncWaitHandle)
                            {
                                if (!wait.WaitOne(150)) throw new TimeoutException();
                                socket.EndConnect(pending);
                            }
                            return new NetworkStream(socket, true);
                        }
                        catch { socket.Dispose(); }
                    }
            }
            return null;
        }
        internal static IEnumerable<string> UnixDirectories()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string variable in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
            {
                string path = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && paths.Add(path)) yield return path;
            }
            if (paths.Add("/tmp")) yield return "/tmp";
        }
        // KSP's Mono/.NET 4.x profile has Unix sockets but predates UnixDomainSocketEndPoint.
        private sealed class UnixEndpoint : EndPoint
        {
            private readonly byte[] path;
            internal UnixEndpoint(string value)
            {
                path = Encoding.UTF8.GetBytes(value);
                if (path.Length > 103) throw new ArgumentException("IPC socket path is too long.");
            }
            public override AddressFamily AddressFamily => AddressFamily.Unix;
            public override SocketAddress Serialize()
            {
                var address = new SocketAddress(AddressFamily.Unix, path.Length + 3);
                for (int i = 0; i < path.Length; i++) address[i + 2] = path[i];
                address[path.Length + 2] = 0;
                return address;
            }
            public override EndPoint Create(SocketAddress address) => this;
        }
    }
}
