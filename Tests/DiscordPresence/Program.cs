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
using Khemistry;

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
        string pipeName = "khemistry-rpc-test-" + Guid.NewGuid().ToString("N");
        var statuses = new ConcurrentQueue<string>();
        int attempts = 0;
        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        using (var client = new DiscordRpcClient(12345, statuses.Enqueue, token =>
        {
            Interlocked.Increment(ref attempts);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { pipe.Connect(500); return pipe; } catch { pipe.Dispose(); throw; }
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
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { pipe.Connect(500); return pipe; } catch { pipe.Dispose(); throw; }
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
    }
    sealed class FragmentStream : MemoryStream
    {
        public FragmentStream(byte[] bytes) : base(bytes) { }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => base.ReadAsync(buffer, offset, Math.Min(count, 1), token);
    }
}
