using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChitakTech;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static async Task Main()
    {
        try { await Run(); Console.WriteLine(checks + " assertions passed."); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
    }
    static Task Send(Stream stream, int opcode, string json)
        => DiscordRpcClient.WriteFrame(stream, opcode, Encoding.UTF8.GetBytes(json), CancellationToken.None);
    static Task<DiscordRpcClient.Frame> Read(Stream stream)
        => DiscordRpcClient.ReadFrame(stream, CancellationToken.None, 3000);
    static async Task Eventually(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.ElapsedMilliseconds > 3000) throw new TimeoutException("Test condition not reached.");
            await Task.Delay(10);
        }
    }
    static async Task Throws<T>(Func<Task> run, string message) where T : Exception
    {
        try { await run(); } catch (T) { Check(true, message); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }
    static async Task Run()
    {
        byte[] packet = DiscordRpcClient.Packet(3, Encoding.UTF8.GetBytes("é"));
        Check(packet[0] == 3 && packet[4] == 2 && packet.Length == 10, "little-endian byte lengths, not character lengths");
        using (var fragment = new FragmentStream(packet))
        {
            var frame = await Read(fragment);
            Check(frame.opcode == 3 && Encoding.UTF8.GetString(frame.payload) == "é", "fragmented reads reassembled");
        }
        await Throws<EndOfStreamException>(() => Read(new MemoryStream(packet.Take(9).ToArray())), "truncated frame");
        await Throws<InvalidDataException>(() => Read(new MemoryStream(new byte[] { 1, 0, 0, 0, 255, 255, 255, 255 })), "negative length");
        await Throws<InvalidDataException>(() => Read(new MemoryStream(new byte[] { 1, 0, 0, 0, 1, 0, 1, 0 })), "oversized length");
        await Throws<InvalidDataException>(() => Task.FromResult(DiscordRpcClient.Packet(1, new byte[65537])), "oversized outgoing frame");
        var error = DiscordRpcClient.Parse(Encoding.UTF8.GetBytes("{\"evt\":\"ERROR\",\"data\":{\"code\":4000,\"message\":\"do not log user data\"}}"));
        Check(error.evt == "ERROR" && error.data.code == 4000, "error code parsed without retaining message");

        // This pipe's random name cannot connect to the actual Discord client.
        string pipeName = "chitaktech-rpc-test-" + Guid.NewGuid().ToString("N");
        var statuses = new ConcurrentQueue<string>();
        int attempts = 0;
        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        using (var client = new DiscordRpcClient(12345, statuses.Enqueue, token =>
        {
            Interlocked.Increment(ref attempts);
            return PollingPipeStream.Connect(pipeName);
        }, 20, 1000))
        {
            await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var hello = await Read(server);
            using (var json = JsonDocument.Parse(hello.payload))
            {
                Check(hello.opcode == 0, "handshake opcode");
                Check(json.RootElement.GetProperty("v").GetInt32() == 1, "RPC version");
                Check(json.RootElement.GetProperty("client_id").GetString() == "1552454077361692794", "exact app ID");
                Check(json.RootElement.EnumerateObject().Count() == 2, "no credentials in handshake");
            }
            await Send(server, 3, "ping");
            var pong = await Read(server);
            Check(pong.opcode == 4 && Encoding.UTF8.GetString(pong.payload) == "ping", "ping echoed before READY");
            await Send(server, 1, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{\"user\":{\"id\":\"ignored\"}}}");
            var activity = await Read(server);
            string nonce;
            using (var json = JsonDocument.Parse(activity.payload))
            {
                var root = json.RootElement;
                nonce = root.GetProperty("nonce").GetString();
                Check(activity.opcode == 1 && root.GetProperty("cmd").GetString() == "SET_ACTIVITY", "publishes activity after ready");
                Check(root.GetProperty("args").GetProperty("pid").GetInt32() == 12345, "uses KSP process ID");
                var content = root.GetProperty("args").GetProperty("activity");
                Check(content.GetProperty("details").GetString() == "Engineering the way to space!", "exact details");
                Check(content.GetProperty("state").GetString() == "Playing", "exact state");
                Check(content.EnumerateObject().Count() == 2, "only two text fields: no assets, parties, secrets, timestamps or buttons");
            }
            await Send(server, 1, "{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"wrong\",\"data\":{}}");
            await Send(server, 3, "check");
            await Read(server); // Ordered roundtrip makes the preceding message processing deterministic.
            Check(!statuses.Any(s => s.Contains("connected.")), "unrelated acknowledgement is ignored");
            await Send(server, 1, "{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"" + nonce + "\",\"data\":{}}");
            await Eventually(() => statuses.Any(s => s.Contains("connected.")));
            Check(true, "publishing acknowledged");
            await Send(server, 1, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{}}");
            await Send(server, 3, "idle");
            Check((await Read(server)).opcode == 4, "duplicate READY does not republish or change text");

            // Reconnect and republish after Discord restarts.
            server.Disconnect();
            await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check((await Read(server)).opcode == 0, "reconnect starts a fresh handshake");
            await Send(server, 1, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\"}");
            Check((await Read(server)).opcode == 1, "same presence republished on reconnect");
            Check(attempts >= 2, "connection retried");
            client.Dispose();
            var clear = await Read(server);
            using (var json = JsonDocument.Parse(clear.payload))
                Check(json.RootElement.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null,
                    "shutdown clears activity");
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            client.Dispose();
            Check(client.Completion.IsCompletedSuccessfully, "idempotent nonblocking shutdown");
        }

        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        using (var client = new DiscordRpcClient(12345, statuses.Enqueue, token =>
        {
            return PollingPipeStream.Connect(pipeName);
        }, 10000, 100))
        {
            await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await Read(server);
            await Eventually(() => statuses.Any(s => s.Contains("TimeoutException")));
            Check(true, "silent Discord handshake times out");
            client.Dispose();
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(client.Completion.IsCompletedSuccessfully, "shutdown interrupts reconnect delay");
        }
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            int connections = 0;
            var client = new DiscordRpcClient(12345, _ => { }, token =>
            {
                token.Register(() => { entered.Set(); release.Wait(); });
                Interlocked.Increment(ref connections);
                return null;
            }, 10000);
            await Eventually(() => Volatile.Read(ref connections) > 0);
            // A watchdog makes this test fail rather than hang against the old implementation.
            var watchdog = Task.Run(async () => { await Task.Delay(1500); release.Set(); });
            var elapsed = Stopwatch.StartNew();
            client.Dispose();
            Check(elapsed.ElapsedMilliseconds < 500, "Dispose never waits for blocking cancellation callbacks");
            await Eventually(() => entered.IsSet);
            release.Set();
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            await watchdog;
            client.Dispose();
            Check(client.Completion.IsCompletedSuccessfully, "blocked callback shutdown eventually completes");
        }

        var offline = new ConcurrentQueue<string>();
        attempts = 0;
        using (var client = new DiscordRpcClient(12345, offline.Enqueue, token =>
        {
            Interlocked.Increment(ref attempts); return null;
        }, 10))
        {
            await Eventually(() => attempts >= 3);
            client.Dispose();
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(offline.Count == 1, "missing Discord logs once rather than spamming");
        }
        await PipeShutdownTests();
    }

    static async Task PipeShutdownTests()
    {
        string name = "chitaktech-shutdown-test-" + Guid.NewGuid().ToString("N");
        // The actual MCP client uses managed NamedPipeClientStream, not our helper.
        using (var server = PollingPipeStream.CreateServer(name))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            client.Connect(500);
            server.WaitForConnection();
            await Send(client, 3, "bridge interoperability");
            var request = await Read(server);
            Check(Encoding.UTF8.GetString(request.payload) == "bridge interoperability", "managed debug client can send requests");
            await Send(server, 4, "reply");
            Check((await Read(client)).opcode == 4, "managed debug client can read responses");
            client.Dispose();
            using (var deadline = new CancellationTokenSource(3000))
                Check(await server.ReadAsync(new byte[1], 0, 1, deadline.Token) == 0, "disconnected debug client returns EOF");
        }
        using (var server = PollingPipeStream.CreateServer(name))
        {
            var waiting = Task.Run(() => server.WaitForConnection());
            await Task.Delay(100);
            Check(!waiting.IsCompleted, "debug bridge waiting without a client");
            var elapsed = Stopwatch.StartNew();
            server.Dispose();
            Check(elapsed.ElapsedMilliseconds < 500, "closing a listening server cannot block Unity");
            await Throws<ObjectDisposedException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(3)),
                "debug accept worker exits when pipe is closed");
        }

        using (var server = PollingPipeStream.CreateServer(name))
        using (var client = PollingPipeStream.Connect(name))
        {
            server.WaitForConnection();
            byte[] buffer = new byte[4];
            using (var stop = new CancellationTokenSource())
            {
                var read = client.ReadAsync(buffer, 0, buffer.Length, stop.Token);
                await Task.Delay(60);
                Check(!read.IsCompleted, "idle client is waiting for data");
                stop.Cancel();
                await Throws<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)),
                    "idle native client read cancels without pending OS I/O");
            }
            client.Write(new byte[] { 42 }, 0, 1);
            Check(server.Read(buffer, 0, buffer.Length) == 1 && buffer[0] == 42, "debug pipe handles partial requests");
            var blocked = Task.Run(() => server.Read(buffer, 0, buffer.Length));
            await Task.Delay(60);
            Check(!blocked.IsCompleted, "debug worker waits for remaining request data");
            server.Dispose();
            await Throws<ObjectDisposedException>(() => blocked.WaitAsync(TimeSpan.FromSeconds(3)),
                "debug read worker exits on shutdown with incomplete request");
        }

        using (var server = PollingPipeStream.CreateServer(name))
        using (var client = PollingPipeStream.Connect(name))
        using (var stop = new CancellationTokenSource())
        {
            server.WaitForConnection();
            byte[] payload = Enumerable.Range(0, 131072).Select(i => (byte)i).ToArray();
            var write = client.WriteAsync(payload, 0, payload.Length, stop.Token);
            await Task.Delay(100);
            Check(!write.IsCompleted, "full pipe waits in managed code rather than native WriteFile");
            byte[] received = new byte[payload.Length];
            int offset = 0;
            using (var deadline = new CancellationTokenSource(3000))
                while (offset < received.Length)
                    offset += await server.ReadAsync(received, offset, received.Length - offset, deadline.Token);
            await write.WaitAsync(TimeSpan.FromSeconds(3));
            Check(payload.SequenceEqual(received), "partial writes resume without duplicating or losing bytes");

            write = client.WriteAsync(payload, 0, payload.Length, stop.Token);
            await Task.Delay(60);
            stop.Cancel();
            await Throws<OperationCanceledException>(() => write.WaitAsync(TimeSpan.FromSeconds(3)),
                "full pipe write cancels without peer cooperation");
        }

        using (var server = PollingPipeStream.CreateServer(name))
        using (var client = PollingPipeStream.Connect(name))
        {
            server.WaitForConnection();
            var write = Task.Run(() => server.Write(new byte[131072], 0, 131072));
            await Task.Delay(100);
            Check(!write.IsCompleted, "debug response writer is waiting for a non-reading peer");
            server.Dispose();
            await Throws<ObjectDisposedException>(() => write.WaitAsync(TimeSpan.FromSeconds(3)),
                "debug response writer exits on shutdown");
        }
    }
    sealed class FragmentStream : MemoryStream
    {
        public FragmentStream(byte[] bytes) : base(bytes) { }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => base.ReadAsync(buffer, offset, Math.Min(count, 1), token);
    }
}
